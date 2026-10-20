using TheOrder.Application;
using System.Collections.Immutable;
using System.Text.RegularExpressions;

namespace TheOrder.Application;

public sealed record ScanLimits(int MaxDepth = 6, int MaxDirectories = 5000, int MaxEntries = 50000, int MaxCandidates = 2000);
public enum ScanStatus { Completed, NoCandidates, Cancelled, Failed }
public enum ScanTruncation { None, DepthLimit }
public sealed record DirectoryItem(string Path, string Name, bool IsDirectory, bool IsReparsePoint);
public interface IScanFilesystem
{
    IEnumerable<DirectoryItem> Enumerate(string directory);
    // OS adapters can hold a short-lived read lease while validating and enumerating this observation.
    IEnumerable<DirectoryItem> Enumerate(PathObservation directory, PathObservation authorizedRoot) => Enumerate(directory.StoredPath);
    bool IsForbiddenRoot(PathObservation root);
}
public sealed record ScanProgress(int Directories, int Entries, int Candidates, string CurrentDirectory);
public sealed record ScanCandidate(string Id, string Name, PathObservation Folder, ImmutableArray<string> Executables,
    string Evidence, InstallationPreflight Preflight)
{
    public string? SteamAppId { get; init; }
    public bool SteamCatalogMatch { get; init; }
    public override string ToString() => $"{Name} · {Preflight.Path.Message}";
}
public sealed record ScanResult(ScanStatus Status, ImmutableArray<ScanCandidate> Candidates, ScanProgress Progress, ImmutableArray<string> Warnings, string Message)
{
    public ScanTruncation Truncation { get; init; }
    public bool IsPartial => Truncation != ScanTruncation.None || !Warnings.IsEmpty;
}

public sealed partial class BoundedScanner(IFilesystemIdentity identity, IScanFilesystem filesystem)
{
    public static bool IsGameExecutable(string name) => name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && !ExcludedExecutable().IsMatch(name);
    public static bool IsLauncherScript(string name) => name.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) && !ExcludedExecutable().IsMatch(Path.ChangeExtension(name, ".exe"));
    public static readonly ImmutableHashSet<string> SkippedDirectories = ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase,
        "node_modules", ".git", ".svn", ".potatovn", "$recycle.bin", "system volume information", "windows", "redist", "_commonredist", "汉化补丁", "补丁", "patches", "_patch", "backup", "beifen");
    public Task<ScanResult> ScanAsync(string root, ImmutableArray<ExistingInstallation> existing, ScanLimits? limits = null,
        IProgress<ScanProgress>? progress = null, CancellationToken cancellation = default)
        => Task.Run(() => Scan(root, existing, limits ?? new(), progress, cancellation));
    private ScanResult Scan(string root, ImmutableArray<ExistingInstallation> existing, ScanLimits limits, IProgress<ScanProgress>? progress, CancellationToken cancellation)
    {
        var candidates = ImmutableArray.CreateBuilder<ScanCandidate>(); var warnings = ImmutableArray.CreateBuilder<string>();
        var current = new ScanProgress(0, 0, 0, root);
        var lastReport = System.Diagnostics.Stopwatch.StartNew();
        var truncation = ScanTruncation.None;
        try
        {
            if (limits.MaxDepth is < 0 or > 6 || limits.MaxDirectories is < 1 or > 5000 || limits.MaxEntries is < 1 or > 50000 || limits.MaxCandidates is < 1 or > 2000)
                throw new InvalidDataException(UiText.Get("扫描上限不在允许范围内。"));
            var probe = identity.Inspect(root); if (!probe.IsValid) throw new InvalidDataException(probe.Message);
            if (filesystem.IsForbiddenRoot(probe.Observation!) || PatchDirectory().IsMatch(probe.Observation!.FinalPath))
                throw new InvalidDataException(UiText.Get("请选择游戏目录或数据盘；不能扫描系统盘根目录、系统目录、网络共享根目录或补丁备份目录。"));
            // This authorization never moves to a newly resolved root during the scan.
            var authorizedRoot = probe.Observation!;
            var queue = new Queue<(PathObservation Observation, int Depth)>(); queue.Enqueue((authorizedRoot, 0));
            var visited = new HashSet<FilesystemIdentity>(); var preflight = new InstallationPreflightService(identity);
            var observed = preflight.Observe(existing, cancellation);
            while (queue.TryDequeue(out var item))
            {
                cancellation.ThrowIfCancellationRequested();
                var itemPath = item.Observation.StoredPath;
                current = current with { Directories = current.Directories + 1, CurrentDirectory = itemPath };
                if (current.Directories > limits.MaxDirectories || candidates.Count >= limits.MaxCandidates) throw new InvalidDataException(UiText.Get("扫描范围达到上限，请选择更具体的目录。"));
                RequireCurrentRoot();
                var directory = identity.Inspect(itemPath);
                if (!directory.IsValid) { Warn(directory.Message); continue; }
                if (!InstallationPreflightService.SameObservation(item.Observation, directory.Observation!) || !InScope(directory.Observation!))
                { Warn(UiText.Get("排队目录的身份、链接或授权范围已变化，已跳过。请重新扫描。")); continue; }
                if (!visited.Add(directory.Observation!.Identity)) continue;
                var executables = new List<string>(); var scripts = new List<string>(); var subdirectories = new List<PathObservation>();
                bool deeperDirectory = false;
                try
                {
                    foreach (var entry in filesystem.Enumerate(item.Observation, authorizedRoot))
                    {
                        cancellation.ThrowIfCancellationRequested();
                        current = current with { Entries = current.Entries + 1 };
                        if (current.Entries > limits.MaxEntries) throw new ScanLimitException();
                        if (entry.IsReparsePoint) continue; // Old scanner excludes links. Never follow loops or read external trees.
                        if (entry.IsDirectory)
                        {
                            if (ScanDirectoryPolicy.SkipChild(item.Observation, entry.Name)) continue;
                            deeperDirectory = true;
                            if (item.Depth < limits.MaxDepth)
                            {
                                var child = identity.Inspect(entry.Path);
                                if (child.IsValid && InScope(child.Observation!))
                                { if (!filesystem.IsForbiddenRoot(child.Observation!)) subdirectories.Add(child.Observation!); }
                                else Warn(UiText.Get("子目录不可读取或包含不支持的链接，已跳过。"));
                            }
                        }
                        else if (IsGameExecutable(entry.Name)) executables.Add(entry.Path);
                        else if (IsLauncherScript(entry.Name)) scripts.Add(entry.Path);
                    }
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Warn(UiText.Get("有目录无法读取或已消失，已跳过。")); continue; }
                RequireCurrentRoot();
                // Discard discoveries if the enumerated path changed while its entries were read.
                var after = identity.Inspect(itemPath);
                if (!after.IsValid || !InstallationPreflightService.SameObservation(item.Observation, after.Observation!))
                { Warn(UiText.Get("枚举期间目录发生变化，本目录结果已丢弃。请重新扫描。")); continue; }
                // A launcher-only folder is also importable; recognition still reads titles
                // and available exe evidence only. No script is interpreted or executed.
                if (executables.Count == 0) executables.AddRange(scripts);
                if (executables.Count > 0)
                {
                    var name = ScanDirectoryPolicy.CandidateName(directory.Observation);
                    var sorted = executables.OrderByDescending(p => Compact(Path.GetFileNameWithoutExtension(p)) == Compact(name))
                        .ThenBy(p => p, StringComparer.CurrentCulture).ToImmutableArray();
                    candidates.Add(new(Guid.NewGuid().ToString(), name, directory.Observation, sorted,
                        UiText.Format($"根目录发现 {sorted.Length} 个候选启动文件；未执行、未识别作品资料。"), preflight.Check(itemPath, observed, cancellation)));
                }
                else if (item.Depth < limits.MaxDepth)
                    foreach (var child in subdirectories)
                    {
                        if (queue.Count + current.Directories >= limits.MaxDirectories) throw new ScanLimitException();
                        queue.Enqueue((child, item.Depth + 1));
                    }
                else if (deeperDirectory) truncation = ScanTruncation.DepthLimit;
                current = current with { Candidates = candidates.Count };
                if (current.Directories == 1 || lastReport.ElapsedMilliseconds >= 50) { progress?.Report(current); lastReport.Restart(); }
            }
            RequireCurrentRoot();
            return Result(candidates.Count == 0 ? ScanStatus.NoCandidates : ScanStatus.Completed,
                truncation == ScanTruncation.DepthLimit ? UiText.Get("扫描已完成，但因深度限制可能未检查全部目录。请缩小扫描范围后重试。") :
                candidates.Count == 0 ? UiText.Get("未发现候选游戏。") : UiText.Get("扫描完成，请核对并选择要导入的候选。"));

            void RequireCurrentRoot()
            {
                var actual = identity.Inspect(authorizedRoot.StoredPath);
                if (!actual.IsValid || !InstallationPreflightService.SameObservation(authorizedRoot, actual.Observation!))
                    throw new InvalidDataException(UiText.Get("扫描根目录的身份或目标已变化，扫描已停止。请重新选择目录。"));
            }
            bool InScope(PathObservation directory) => directory.IsDirectory
                && directory.Identity.Volume == authorizedRoot.Identity.Volume
                && (directory.Identity == authorizedRoot.Identity || InstallationPreflightService.Within(directory.FinalPath, authorizedRoot.FinalPath))
                // A root alias is allowed; no additional reparse component beneath that root is followed.
                && directory.ViaReparsePoint == authorizedRoot.ViaReparsePoint
                && directory.ReparseTag == authorizedRoot.ReparseTag
                && directory.ReparsePaths.SequenceEqual(authorizedRoot.ReparsePaths, StringComparer.OrdinalIgnoreCase);
        }
        catch (OperationCanceledException) { return Result(ScanStatus.Cancelled, UiText.Get("扫描已取消，未导入任何候选。")); }
        catch (Exception e) when (e is InvalidDataException or ScanLimitException or IOException or UnauthorizedAccessException)
        { return Result(ScanStatus.Failed, e is ScanLimitException ? UiText.Get("扫描范围达到上限，请选择更具体的目录。") : e.Message); }
        void Warn(string message) { if (warnings.Count < 100) warnings.Add(message); }
        ScanResult Result(ScanStatus status, string message) => new(status, candidates.ToImmutable(), current with { Candidates = candidates.Count }, warnings.ToImmutable(), message) { Truncation = truncation };
    }
    private sealed class ScanLimitException : Exception;
    private static string Compact(string name) => NonAlphanumeric().Replace(name.ToLowerInvariant(), "");
    [GeneratedRegex(@"^(?:unins\d*|uninstall(?:er)?|setup|install(?:er)?|vcredist.*|vc_redist.*|dxsetup|directx.*|redist.*|unitycrashhandler.*|crashreport.*|updater?|configure|configuration)(?:[._ -].*)?\.exe$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)] private static partial Regex ExcludedExecutable();
    [GeneratedRegex(@"[^\p{L}\p{N}]")] private static partial Regex NonAlphanumeric();
    [GeneratedRegex(@"(?:^|[\\/])(?:汉化补丁|补丁|patches|_patch|backup|beifen)(?:[\\/]|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)] private static partial Regex PatchDirectory();
}
