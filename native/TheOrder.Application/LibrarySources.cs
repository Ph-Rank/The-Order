using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;

namespace TheOrder.Application;

public enum LibrarySourceKind { Folder, Steam }
public sealed record LibrarySource(string Id, LibrarySourceKind Kind, PathObservation Root, bool Watch = true)
{
    public bool ShowUnconfirmedSteam { get; init; }
    public ImmutableArray<string> IgnoredCandidates { get; init; } = [];
}
public sealed record SourceScan(string SourceId, ScanStatus Status, string Message, ImmutableArray<ScanCandidate> Candidates);
public interface ILibrarySourcesStore
{
    Task<ImmutableArray<LibrarySource>> LoadAsync(CancellationToken cancellation);
    Task SaveAsync(ImmutableArray<LibrarySource> sources, CancellationToken cancellation);
}
public interface ISteamLibraryScanner
{
    Task<ScanResult> ScanAsync(PathObservation root, ImmutableArray<ExistingInstallation> existing, CancellationToken cancellation);
}
public sealed class LibrarySourcesService(ILibrarySourcesStore store, IFilesystemIdentity identity, IScanFilesystem filesystem,
    BoundedScanner scanner, ISteamLibraryScanner steam, OfflineLibraryClient library) : IAsyncDisposable
{
    private readonly SemaphoreSlim queue = new(1);
    private readonly CancellationTokenSource lifetime = new();
    private readonly object gate = new();
    private ImmutableArray<LibrarySource> sources = [];
    private ImmutableArray<SourceScan> scans = [];
    private readonly Dictionary<string, DateTimeOffset> dirty = new(StringComparer.Ordinal);
    private bool loaded, closing;
    public ImmutableArray<LibrarySource> Sources { get { lock (gate) return sources; } }
    public ImmutableArray<SourceScan> Scans { get { lock (gate) return scans; } }
    public ImmutableArray<ScanCandidate> Candidates
    {
        get
        {
            // A successful import can happen between filesystem scans. Hide those candidates
            // immediately without opening paths on the UI thread; import still revalidates identity.
            var known = (library.CurrentSnapshot?.Entries ?? []).SelectMany(e => e.Installations)
                .Select(i => CanonicalPath(i.Folder)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            lock (gate)
            {
                var roots = sources.ToDictionary(s => s.Id, StringComparer.Ordinal);
                var ignored = sources.Where(s => !s.IgnoredCandidates.IsEmpty).ToDictionary(s => s.Id, s => s.IgnoredCandidates.ToHashSet(StringComparer.Ordinal), StringComparer.Ordinal);
                return [.. scans.Where(s => roots.ContainsKey(s.SourceId)).SelectMany(s => s.Candidates.Where(c =>
                    (roots[s.SourceId].Kind != LibrarySourceKind.Steam || roots[s.SourceId].ShowUnconfirmedSteam || c.SteamCatalogMatch)
                    && (!ignored.TryGetValue(s.SourceId, out var keys) || !keys.Contains(CandidateKey(roots[s.SourceId], c)))))
                    .Where(c => c.Preflight.CanAttach && !known.Contains(CanonicalPath(c.Folder.StoredPath)) && !known.Contains(CanonicalPath(c.Folder.FinalPath)))
                    .DistinctBy(c => c.Folder.Identity).Take(2000)];
            }
        }
    }
    private static string CanonicalPath(string path)
    {
        var value = path.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase) ? @"\\" + path[8..]
            : path.StartsWith(@"\\?\", StringComparison.Ordinal) ? path[4..] : path;
        try { return Path.GetFullPath(value).Replace('/', '\\').TrimEnd('\\'); }
        catch (ArgumentException) { return value.Replace('/', '\\').TrimEnd('\\'); }
    }
    public async Task InitializeAsync(CancellationToken cancellation = default)
    {
        await queue.WaitAsync(cancellation).ConfigureAwait(false);
        try { if (closing || loaded) return; var result = await store.LoadAsync(cancellation).ConfigureAwait(false); Validate(result); lock (gate) { sources = result; loaded = true; foreach (var root in sources) dirty[root.Id] = DateTimeOffset.MinValue; } }
        finally { queue.Release(); }
    }
    public void Changed(string id) { lock (gate) if (!closing && sources.Any(x => x.Id == id && x.Watch)) dirty[id] = DateTimeOffset.UtcNow; }
    public async Task AddAsync(string path, LibrarySourceKind kind, CancellationToken cancellation = default)
    {
        await InitializeAsync(cancellation).ConfigureAwait(false);
        await queue.WaitAsync(cancellation).ConfigureAwait(false);
        try
        {
            if (closing) throw new OperationCanceledException();
            var root = await Task.Run(() => identity.Inspect(path), cancellation).ConfigureAwait(false);
            if (!root.IsValid || root.Observation!.ViaReparsePoint || filesystem.IsForbiddenRoot(root.Observation)) throw new InvalidDataException(UiText.Get("请选择可读取的库目录或数据盘，不能使用系统盘根目录、系统目录、网络共享根目录或目录链接。"));
            var prior = Sources;
            if (prior.Any(x => x.Root.Identity == root.Observation.Identity)) throw new InvalidDataException("这个库目录已经添加。");
            // A whole data volume can be checked explicitly, but never watched recursively in the background.
            var next = prior.Add(new(Guid.NewGuid().ToString("D"), kind, root.Observation, !ScanDirectoryPolicy.IsDriveRoot(root.Observation))); Validate(next);
            await store.SaveAsync(next, cancellation).ConfigureAwait(false);
            lock (gate) { sources = next; dirty[next[^1].Id] = DateTimeOffset.MinValue; }
        }
        finally { queue.Release(); }
    }
    public async Task ConfigureAsync(string id, bool? watch, CancellationToken cancellation = default)
    {
        await queue.WaitAsync(cancellation).ConfigureAwait(false);
        try
        {
            if (closing || !loaded) throw new OperationCanceledException();
            var prior = Sources; var root = prior.FirstOrDefault(x => x.Id == id) ?? throw new InvalidDataException("库目录不存在。");
            if (watch == true && ScanDirectoryPolicy.IsDriveRoot(root.Root)) throw new InvalidDataException(UiText.Get("数据盘根目录仅支持手动检查；自动检查请选择具体游戏库文件夹。"));
            var next = watch is null ? prior.Remove(root) : prior.Replace(root, root with { Watch = watch.Value });
            await store.SaveAsync(next, cancellation).ConfigureAwait(false);
            lock (gate) { sources = next; dirty.Remove(id); scans = scans.RemoveAll(x => x.SourceId == id); if (watch == true) dirty[id] = DateTimeOffset.MinValue; }
        }
        finally { queue.Release(); }
    }
    private static string CandidateKey(LibrarySource source, ScanCandidate candidate)
        => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new[] { source.Id,
            CanonicalPath(candidate.Folder.FinalPath).ToUpperInvariant(), candidate.Folder.Identity.Volume,
            candidate.Folder.Identity.FileId, candidate.SteamAppId ?? "" })));
    public async Task IgnoreAsync(ImmutableArray<ScanCandidate> selected, CancellationToken cancellation = default)
    {
        if (selected.IsDefaultOrEmpty || selected.Length > 2000) throw new InvalidDataException("请选择要忽略的新安装。");
        await queue.WaitAsync(cancellation).ConfigureAwait(false);
        try
        {
            if (closing || !loaded) throw new OperationCanceledException();
            var available = Candidates;
            if (selected.Select(c => c.Id).Distinct().Count() != selected.Length
                || selected.Any(c => !available.Contains(c))) throw new InvalidDataException("新安装列表已变化，请重新检查后选择。");
            var selectedFolders = selected.Select(c => c.Folder.Identity).ToHashSet();
            var prior = Sources; var next = prior;
            foreach (var source in prior)
            {
                var inSource = Scans.FirstOrDefault(s => s.SourceId == source.Id)?.Candidates ?? [];
                // One visible installation may have been discovered by overlapping roots.
                // Persist each source's own key so its duplicate cannot reappear after dismissal.
                var ignored = inSource.Where(c => selectedFolders.Contains(c.Folder.Identity)).ToImmutableArray();
                if (ignored.IsEmpty) continue;
                await Task.Run(() =>
                {
                    var guard = new InstallationPreflightService(identity); guard.Revalidate(source.Root);
                    foreach (var candidate in ignored) { cancellation.ThrowIfCancellationRequested(); guard.Revalidate(candidate.Folder); }
                }, cancellation).ConfigureAwait(false);
                next = next.Replace(source, source with { IgnoredCandidates = [.. source.IgnoredCandidates.Concat(ignored.Select(c => CandidateKey(source, c))).Distinct(StringComparer.Ordinal)] });
            }
            Validate(next); await store.SaveAsync(next, cancellation).ConfigureAwait(false);
            lock (gate) sources = next;
        }
        finally { queue.Release(); }
    }
    public async Task ConfigureDiscoveryAsync(string id, bool? showUnconfirmedSteam = null, bool restoreIgnored = false, CancellationToken cancellation = default)
    {
        await queue.WaitAsync(cancellation).ConfigureAwait(false);
        try
        {
            if (closing || !loaded) throw new OperationCanceledException();
            var prior = Sources; var source = prior.FirstOrDefault(s => s.Id == id) ?? throw new InvalidDataException("库目录不存在。");
            if (showUnconfirmedSteam is not null && source.Kind != LibrarySourceKind.Steam) throw new InvalidDataException("仅 Steam 库支持作品类型筛选。");
            var next = prior.Replace(source, source with { ShowUnconfirmedSteam = showUnconfirmedSteam ?? source.ShowUnconfirmedSteam,
                IgnoredCandidates = restoreIgnored ? [] : source.IgnoredCandidates });
            Validate(next); await store.SaveAsync(next, cancellation).ConfigureAwait(false);
            lock (gate) sources = next;
        }
        finally { queue.Release(); }
    }
    public async Task ScanAsync(bool force = false, CancellationToken cancellation = default, bool watchedOnly = false, bool waitForTurn = false)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token, cancellation);
        // Periodic checks can skip a busy service. A requested scan must actually run
        // before its caller reports completion or presents the discovered games.
        if (waitForTurn) await queue.WaitAsync(linked.Token).ConfigureAwait(false);
        else if (!await queue.WaitAsync(0, linked.Token).ConfigureAwait(false)) return;
        try
        {
            if (closing || !loaded) return;
            foreach (var source in Sources.Where(x => !watchedOnly || x.Watch))
            {
                DateTimeOffset stamp;
                lock (gate) { if (!force && (!dirty.TryGetValue(source.Id, out stamp) || DateTimeOffset.UtcNow - stamp < TimeSpan.FromSeconds(3))) continue; stamp = dirty.GetValueOrDefault(source.Id, DateTimeOffset.MinValue); }
                var existing = TransactionalLibrary.Existing(library.CurrentSnapshot?.Entries ?? []);
                var probe = await Task.Run(() => identity.Inspect(source.Root.StoredPath), linked.Token).ConfigureAwait(false);
                SourceScan result;
                if (!probe.IsValid) result = new(source.Id, ScanStatus.Failed, "库目录离线或无权访问；已有书架保留。", []);
                else if (!InstallationPreflightService.SameObservation(source.Root, probe.Observation!)) result = new(source.Id, ScanStatus.Failed, "库目录身份已改变；请移除后重新选择此目录。", []);
                else
                {
                    var scan = source.Kind == LibrarySourceKind.Steam ? await steam.ScanAsync(source.Root, existing, linked.Token).ConfigureAwait(false)
                        : await scanner.ScanAsync(source.Root.StoredPath, existing, cancellation: linked.Token).ConfigureAwait(false);
                    var message = source.Kind == LibrarySourceKind.Steam && scan.Status is ScanStatus.Completed or ScanStatus.NoCandidates
                        ? $"目录已收录 {scan.Candidates.Count(c => c.SteamCatalogMatch)} 个，作品类型未确认 {scan.Candidates.Count(c => !c.SteamCatalogMatch)} 个。"
                        : scan.Message;
                    result = new(source.Id, scan.Status, message + (scan.IsPartial ? " 部分项目未能检查。" : ""), scan.Candidates);
                }
                lock (gate) { scans = scans.RemoveAll(x => x.SourceId == source.Id).Add(result); if (dirty.GetValueOrDefault(source.Id) == stamp) dirty.Remove(source.Id); }
            }
        }
        finally { queue.Release(); }
    }
    public static void Validate(ImmutableArray<LibrarySource> value)
    {
        if (value.IsDefault || value.Length > 16 || value.Any(x => x is null || x.Root is null) || value.Select(x => x.Id).Distinct().Count() != value.Length || value.Select(x => x.Root.Identity).Distinct().Count() != value.Length)
            throw new InvalidDataException("最多管理 16 个不同的库目录。");
        if (value.Any(x => x.IgnoredCandidates.IsDefault) || value.Sum(x => x.IgnoredCandidates.Length) > 2000)
            throw new InvalidDataException("忽略记录最多保存 2000 项，请先恢复部分记录。");
        foreach (var x in value)
            if (!Guid.TryParseExact(x.Id, "D", out _) || !Enum.IsDefined(x.Kind) || x.Root is null || !x.Root.IsDirectory || x.Root.ViaReparsePoint || x.Root.StoredPath.Length > 32768 || x.Root.Identity is null
                || x.Kind != LibrarySourceKind.Steam && x.ShowUnconfirmedSteam || x.Watch && ScanDirectoryPolicy.IsDriveRoot(x.Root)
                || x.IgnoredCandidates.Distinct(StringComparer.Ordinal).Count() != x.IgnoredCandidates.Length
                || x.IgnoredCandidates.Any(k => k is null || k.Length != 64 || k.Any(c => c is not (>= '0' and <= '9') and not (>= 'A' and <= 'F'))))
                throw new InvalidDataException("库目录记录无效。");
    }
    private Task? disposal;
    public ValueTask DisposeAsync()
    {
        lock (gate) { if (disposal is not null) return new(disposal); closing = true; lifetime.Cancel(); return new(disposal = Drain()); }
        async Task Drain() { await queue.WaitAsync().ConfigureAwait(false); queue.Release(); lifetime.Dispose(); }
    }
}
