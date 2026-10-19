using TheOrder.Application;
using System.Collections.Immutable;
using System.Globalization;
using TheOrder.Domain;

namespace TheOrder.Application;

public interface ILibraryCodec
{
    ImmutableArray<LibraryEntry> Decode(ReadOnlyMemory<byte> document);
    byte[] Encode(ImmutableArray<LibraryEntry> entries, string exportedAt);
    LibraryDocument Inspect(ReadOnlyMemory<byte> document, string exportedAt);
    string EvidenceIdentity(LibraryEntry entry);
    bool ProtectsName(LibraryEntry entry);
    string InstallationKey(string path);
    WorkMetadata NormalizeWork(WorkMetadata work);
}

// A persistence implementation owns an exclusive profile lease for its entire lifetime.
public interface ILibraryPersistence : IDisposable
{
    byte[]? ReadMain();
    byte[]? ReadBackup();
    Task<PersistenceReceipt> SaveAsync(ReadOnlyMemory<byte> previous, ReadOnlyMemory<byte> candidate);
}
public sealed record PersistenceReceipt(string? Warning = null);
public enum LibraryRecovery { Main, Backup, New }
public enum LibraryAccess { InvalidInput, ValidReadOnly, ValidWritable }
public sealed record LibraryDocument(LibraryAccess Access, ImmutableArray<LibraryEntry> Entries, byte[]? WritableBytes, string? Reason = null);
public sealed class LibraryWriteLimitException() : InvalidOperationException(UiText.Get("当前资料可读取，但当前 writer 无法在 32 MiB 契约内安全保存。"));
public sealed class LibraryReadOnlyException(string reason) : InvalidOperationException(reason);
public sealed record LibrarySnapshot(ImmutableArray<LibraryEntry> Entries, LibraryRecovery Recovery,
    LibraryAccess Access = LibraryAccess.ValidWritable, string? ReadOnlyReason = null);
public sealed record CommitResult<T>(T Value, bool CancellationAfterCommit, string? Warning);
public sealed record ImportCount(int Added, int Skipped);
public readonly record struct Change<T>(bool IsSpecified, T Value)
{
    public static implicit operator Change<T>(T value) => new(true, value);
}
public sealed record PersonalPatch
{
    public Change<string> Name { get; init; }
    public Change<string> Status { get; init; }
    public Change<double?> Rating { get; init; }
    public Change<bool> Favorite { get; init; }
    public Change<ImmutableArray<string>> Categories { get; init; }
    public Change<ImmutableArray<string>> Tags { get; init; }
    public Change<string> Notes { get; init; }
    public Change<string?> PreferredInstallationId { get; init; }
}
public sealed record RequestToken(Guid Epoch, string GameId, Guid Generation, long Request, long Revision);
public sealed class StaleLibraryRequestException() : InvalidOperationException(UiText.Get("条目或请求已变化，请重新开始操作。"));

// UI-independent transaction service. All state is immutable; only this queue publishes snapshots.
// Metadata fetching, directory authorization and game launch deliberately do not live here.
public sealed partial class TransactionalLibrary : IDisposable, IAsyncDisposable
{
    private readonly ILibraryCodec codec;
    private readonly ILibraryPersistence persistence;
    private readonly Func<string> now;
    private readonly Func<string> newId;
    private readonly SemaphoreSlim queue = new(1, 1);
    private readonly object admission = new();
    private int admitted;
    private bool closing;
    private TaskCompletionSource? drained;
    private Task? shutdown;
    private readonly Dictionary<long, CancellationTokenRegistration> requestCancellations = [];
    private readonly Guid epoch = Guid.NewGuid();
    private Dictionary<string, Lifecycle> lifecycles = [];
    private Dictionary<string, Guid> launchIncarnations = [];
    private long revision, request;
    private LibrarySnapshot current;
    private byte[] lastGood;
    private bool disposed;
    private sealed record Lifecycle(Guid Generation, long Revision, long Request = 0, bool Pending = false);

    public LibrarySnapshot Snapshot => Volatile.Read(ref current);
    private TransactionalLibrary(ILibraryCodec codec, ILibraryPersistence persistence, Func<string> now, Func<string> newId,
        LibraryDocument document, LibraryRecovery recovery)
    {
        this.codec = codec; this.persistence = persistence; this.now = now; this.newId = newId;
        current = new(document.Entries, recovery, document.Access, document.Reason); lastGood = document.WritableBytes ?? [];
        foreach (var e in document.Entries) { lifecycles[e.Id] = new(Guid.NewGuid(), ++revision); launchIncarnations[e.Id] = Guid.NewGuid(); }
    }
    public static TransactionalLibrary Open(ILibraryCodec codec, ILibraryPersistence persistence, Func<string>? clock = null, Func<string>? idFactory = null, TimeProvider? timeProvider = null)
    {
        // Opening never writes, including recovery and the genuinely-new profile case.
        try
        {
            byte[]? main = null, backup = null;
            LibraryDocument? document = null;
            Exception? mainError = null;
            try { main = persistence.ReadMain(); if (main is not null) document = Read(main); }
            catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException) { mainError = e; }
            // Construction/output capability must never be interpreted as input corruption.
            if (document is not null) return Create(document, (persistence as ILibraryRecoveryProvenance)?.MainRecovery ?? LibraryRecovery.Main);
            try { backup = persistence.ReadBackup(); if (backup is not null) document = Read(backup); }
            catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException) { throw new InvalidDataException(UiText.Get("主文件及备份不可用；原文件已保留。"), e); }
            if (document is not null) return Create(document, LibraryRecovery.Backup);
            if (main is not null || mainError is not null) throw new InvalidDataException(UiText.Get("主文件不可用且没有有效备份；原文件已保留。"), mainError);
            return Create(new(LibraryAccess.ValidWritable, [], codec.Encode([], Now())), LibraryRecovery.New);
        }
        catch { persistence.Dispose(); throw; }
        string Now() => clock is not null ? clock() : (timeProvider ?? TimeProvider.System).GetUtcNow().ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
        LibraryDocument Read(byte[] bytes)
        {
            var read = codec.Inspect(bytes, Now());
            return read.Access == LibraryAccess.InvalidInput ? throw new InvalidDataException(read.Reason) : read;
        }
        TransactionalLibrary Create(LibraryDocument d, LibraryRecovery r) => new(codec, persistence, Now, idFactory ?? (() => Guid.NewGuid().ToString()), d, r);
    }

    private Task<T> Enqueue<T>(Func<Task<T>> action, CancellationToken cancellation)
    {
        lock (admission)
        {
            ObjectDisposedException.ThrowIf(closing || disposed, this);
            // Register the wait in caller admission order, BEFORE dispatching CPU work.
            var turn = queue.WaitAsync(cancellation);
            admitted++;
            return Task.Run(async () =>
            {
                bool ownsTurn = false;
                try { await turn.ConfigureAwait(false); ownsTurn = true; cancellation.ThrowIfCancellationRequested(); return await action().ConfigureAwait(false); }
                finally
                {
                    if (ownsTurn) queue.Release();
                    lock (admission) { if (--admitted == 0) drained?.TrySetResult(); }
                }
            });
        }
    }
    private Task<CommitResult<T>> Mutate<T>(Func<ImmutableArray<LibraryEntry>, (ImmutableArray<LibraryEntry> Entries, T Result)> operation, CancellationToken cancellation, string? forceNewGeneration = null, Action? precommit = null, Func<Task>? verifyBeforeCommit = null, bool sessionMutation = false, bool renewGenerations = false, bool skipUnchanged = false)
        => Enqueue(async () =>
        {
            if (current.Access != LibraryAccess.ValidWritable) throw new LibraryReadOnlyException(current.ReadOnlyReason!);
            var previous = current.Entries;
            var (candidate, result) = operation(previous);
            if (skipUnchanged && candidate == previous) { cancellation.ThrowIfCancellationRequested(); return new CommitResult<T>(result, false, null); }
            if (!sessionMutation) ProtectSessionBindings(previous, candidate, forceNewGeneration);
            // Round-trip validates and canonicalizes the complete candidate before any I/O.
            var serialized = codec.Encode(candidate, now());
            var validated = codec.Decode(serialized);
            serialized = codec.Encode(validated, now());
            // Prepare every allocating/validating lifecycle calculation before durable I/O too.
            // Once the primary switches, publishing the prepared state requires only assignments.
            var old = previous.ToDictionary(e => e.Id);
            var nextLifecycles = new Dictionary<string, Lifecycle>(lifecycles);
            var nextLaunchIncarnations = new Dictionary<string, Guid>(launchIncarnations);
            var nextRevision = revision;
            foreach (var id in nextLifecycles.Keys.Except(validated.Select(e => e.Id)).ToArray()) nextLifecycles.Remove(id);
            foreach (var id in nextLaunchIncarnations.Keys.Except(validated.Select(e => e.Id)).ToArray()) nextLaunchIncarnations.Remove(id);
            foreach (var e in validated)
                if (renewGenerations || e.Id == forceNewGeneration || !nextLaunchIncarnations.ContainsKey(e.Id)
                    || old.TryGetValue(e.Id, out var priorLaunch) && (!priorLaunch.Installations.Select(i => i.Id).Order().SequenceEqual(e.Installations.Select(i => i.Id).Order())
                        || e.Installations.Any(i => priorLaunch.Installations.FirstOrDefault(p => p.Id == i.Id) is { } prior && (prior.Compatibility != i.Compatibility || prior.SteamAppId != i.SteamAppId))))
                    nextLaunchIncarnations[e.Id] = Guid.NewGuid();
            foreach (var e in validated)
                if (renewGenerations || e.Id == forceNewGeneration || !old.TryGetValue(e.Id, out var before) || codec.EvidenceIdentity(before) != codec.EvidenceIdentity(e))
                    nextLifecycles[e.Id] = new(Guid.NewGuid(), ++nextRevision);
            var retiredRequests = lifecycles.Where(pair => !nextLifecycles.TryGetValue(pair.Key, out var next) || next.Generation != pair.Value.Generation)
                .Select(pair => pair.Value.Request).ToArray();
            var nextSnapshot = new LibrarySnapshot(validated, LibraryRecovery.Main);
            var releasedPending = sessionMutation ? pendingOwnedSessions.Keys.Where(id =>
            {
                var oldIds = previous.FirstOrDefault(e => e.Id == id)?.Sessions.Select(s => s.Id).ToHashSet() ?? [];
                return validated.FirstOrDefault(e => e.Id == id)?.Sessions.Any(s => s.Native is not null && !oldIds.Contains(s.Id)) == true;
            }).ToArray() : [];
            precommit?.Invoke();
            if (verifyBeforeCommit is not null) await verifyBeforeCommit().ConfigureAwait(false);
            cancellation.ThrowIfCancellationRequested();
            // No cancellation inside the durable phase. A post-replace warning is a committed result.
            var receipt = await persistence.SaveAsync(lastGood, serialized).ConfigureAwait(false);
            lifecycles = nextLifecycles; launchIncarnations = nextLaunchIncarnations; revision = nextRevision;
            lastGood = serialized;
            Volatile.Write(ref current, nextSnapshot);
            foreach (var id in releasedPending) pendingOwnedSessions.Remove(id);
            foreach (var retired in retiredRequests) UnregisterRequest(retired);
            return new CommitResult<T>(result, cancellation.IsCancellationRequested, receipt.Warning);
        }, cancellation);

    public Task<CommitResult<ImportCount>> ImportAsync(ReadOnlyMemory<byte> backup, CancellationToken cancellation = default)
    {
        cancellation.ThrowIfCancellationRequested();
        // As with Stream.WriteAsync, caller retains buffer ownership until completion.
        return Mutate(games =>
        {
            var incoming = codec.Decode(backup.ToArray()).Select(PortableSessionHistory).ToImmutableArray();
            var plan = AddMissing(games, incoming);
            return (plan.Entries, plan.Count);
        }, cancellation);
    }
    public Task<CommitResult<LibraryEntry>> AddAsync(WorkMetadata work, CancellationToken cancellation = default)
    {
        cancellation.ThrowIfCancellationRequested();
        return Mutate(games =>
        {
            work = codec.NormalizeWork(work);
            var existing = games.FirstOrDefault(e => e.Metadata?.Id == work.Id);
            if (existing is not null) return (games, existing);
            var e = new LibraryEntry(newId(), work.Title, false, work, [], null, "planned", null, false, [], [], "", 0, 0, [], now(), null, null, null, new(false, null));
            return (games.Add(e), e);
        }, cancellation);
    }
    public Task<CommitResult<LibraryEntry>> UpdateAsync(string id, PersonalPatch p, CancellationToken cancellation = default)
        => Mutate(games =>
        {
            var e = Find(games, id);
            var changed = e with
            {
                Name = p.Name.IsSpecified ? p.Name.Value : e.Name, NameIsCustom = p.Name.IsSpecified ? true : e.NameIsCustom,
                Status = p.Status.IsSpecified ? p.Status.Value : e.Status, PersonalRating = p.Rating.IsSpecified ? p.Rating.Value : e.PersonalRating,
                Favorite = p.Favorite.IsSpecified ? p.Favorite.Value : e.Favorite, Categories = p.Categories.IsSpecified ? p.Categories.Value : e.Categories,
                CustomTags = p.Tags.IsSpecified ? p.Tags.Value : e.CustomTags, Notes = p.Notes.IsSpecified ? p.Notes.Value : e.Notes,
                PreferredInstallationId = p.PreferredInstallationId.IsSpecified ? p.PreferredInstallationId.Value : e.PreferredInstallationId
            };
            // Legacy patch normalizes these fields before returning the operation result.
            changed = codec.Decode(codec.Encode([changed], now()))[0];
            return (games.Replace(e, changed), changed);
        }, cancellation);
    public Task<CommitResult<bool>> DeleteAsync(string id, CancellationToken cancellation = default)
        => Mutate(games => (games.Remove(Find(games, id)), true), cancellation);

    public Task<CommitResult<int>> DeleteBatchAsync(ImmutableArray<string> ids, CancellationToken cancellation = default)
        => Mutate(games =>
        {
            if (ids.IsDefaultOrEmpty) throw new InvalidDataException("请选择需要移除的作品。");
            var selected = ids.ToHashSet(StringComparer.Ordinal);
            foreach (var id in selected) _ = Find(games, id);
            return (games.Where(e => !selected.Contains(e.Id)).ToImmutableArray(), selected.Count);
        }, cancellation);
    // Installation is an already-authorized value, never an instruction to scan/open/execute a path.
    public Task<CommitResult<LibraryEntry>> AddInstallationAsync(string id, Installation installation, RequestToken token, CancellationToken cancellation = default)
        => Mutate(games =>
        {
            AssertToken(id, token); var e = Find(games, id);
            var key = codec.InstallationKey(installation.Folder);
            foreach (var known in games.SelectMany(g => g.Installations).Select(i => codec.InstallationKey(i.Folder)))
                if (key == known || key.StartsWith(known.TrimEnd('\\') + "\\", StringComparison.Ordinal) || known.StartsWith(key.TrimEnd('\\') + "\\", StringComparison.Ordinal))
                    throw new InvalidDataException(UiText.Get("安装目录重复或范围重叠。"));
            var preferred = e.Installations.Any(i => i.Id == e.PreferredInstallationId && i.Executable is not null) ? e.PreferredInstallationId : installation.Id;
            var changed = e with { Installations = e.Installations.Add(installation), PreferredInstallationId = preferred };
            return (games.Replace(e, changed), changed);
        }, cancellation);
    // Explicit Phase 2 addition: removing a record never deletes game files.
    public Task<CommitResult<LibraryEntry>> RemoveInstallationAsync(string id, string installationId, CancellationToken cancellation = default)
        => Mutate(games =>
        {
            var e = Find(games, id); var location = e.Installations.FirstOrDefault(i => i.Id == installationId) ?? throw new InvalidDataException(UiText.Get("安装不存在。"));
            var changed = e with { Installations = e.Installations.Remove(location), PreferredInstallationId = e.PreferredInstallationId == installationId ? null : e.PreferredInstallationId };
            return (games.Replace(e, changed), changed);
        }, cancellation);

    public Task<RequestToken> BeginRequestAsync(string id, CancellationToken cancellation = default)
        => Enqueue(() =>
        {
            Find(current.Entries, id); var state = lifecycles[id];
            UnregisterRequest(state.Request);
            state = state with { Request = ++request, Revision = ++revision, Pending = true }; lifecycles[id] = state;
            var token = new RequestToken(epoch, id, state.Generation, state.Request, state.Revision);
            if (cancellation.CanBeCanceled)
                requestCancellations[token.Request] = cancellation.Register(() =>
                {
                    lock (admission)
                    {
                        // Shutdown clears remaining request state after draining accepted work.
                        if (!closing) _ = FinishRequestAsync(token);
                    }
                });
            return Task.FromResult(token);
        }, cancellation);
    public Task<bool> FinishRequestAsync(RequestToken token, CancellationToken cancellation = default)
        => Enqueue(() =>
        {
            ClearRequest(token);
            return Task.FromResult(true);
        }, CancellationToken.None); // Cleanup must survive cancellation of the original request.
    private void ClearRequest(RequestToken token)
    {
        if (token.Epoch != epoch) return;
        UnregisterRequest(token.Request);
        if (lifecycles.TryGetValue(token.GameId, out var s) && s.Generation == token.Generation && s.Request == token.Request)
            lifecycles[token.GameId] = s with { Pending = false };
    }
    private void UnregisterRequest(long requestId)
    {
        if (requestCancellations.Remove(requestId, out var registration)) registration.Unregister();
    }
    private void AssertToken(string id, RequestToken token)
    {
        if (token.Epoch != epoch || token.GameId != id || !lifecycles.TryGetValue(id, out var s) || s.Generation != token.Generation || s.Request != token.Request || !s.Pending)
            throw new StaleLibraryRequestException();
    }
    // Immediate explicit user intent only. Delayed API/identification results MUST use CommitLinkAsync.
    public Task<CommitResult<LibraryEntry>> LinkAsync(string id, WorkMetadata work, bool preserveName = false, bool preserveMergedName = false, CancellationToken cancellation = default)
        => LinkCoreAsync(id, work, preserveName, preserveMergedName, null, cancellation);
    public async Task<CommitResult<LibraryEntry>> CommitLinkAsync(string id, WorkMetadata work, RequestToken token, bool preserveName = false, bool preserveMergedName = false, CancellationToken cancellation = default, Func<Task>? verifyBeforeCommit = null)
    {
        ArgumentNullException.ThrowIfNull(token);
        try { return await LinkCoreAsync(id, work, preserveName, preserveMergedName, token, cancellation, verifyBeforeCommit).ConfigureAwait(false); }
        finally
        {
            Task cleanup;
            lock (admission) cleanup = closing ? Task.CompletedTask : FinishRequestAsync(token);
            await cleanup.ConfigureAwait(false);
        }
    }
    private Task<CommitResult<LibraryEntry>> LinkCoreAsync(string id, WorkMetadata work, bool preserveName, bool preserveMergedName, RequestToken? token, CancellationToken cancellation, Func<Task>? verifyBeforeCommit = null)
    {
        return Mutate(games =>
        {
            if (token is not null) AssertToken(id, token);
            work = codec.NormalizeWork(work);
            var source = Find(games, id); var target = games.FirstOrDefault(e => e.Id != id && e.Metadata?.Id == work.Id);
            if (target is not null && lifecycles.TryGetValue(target.Id, out var life) && (life.Pending || token is not null && life.Revision > token.Revision)) throw new StaleLibraryRequestException();
            var keep = preserveName || codec.ProtectsName(source);
            var mergedName = preserveMergedName && !keep && target?.Metadata is not null && codec.ProtectsName(target) ? target.Name : null;
            var e = source with { CoverChoice = source.CoverChoice?.WorkId == work.Id ? source.CoverChoice : null };
            if (target is not null)
            {
                var sessions = e.Sessions.ToBuilder(); long duplicateSeconds = 0, duplicateCount = 0;
                var sessionIndices = new Dictionary<string, int>(sessions.Count, StringComparer.Ordinal);
                for (int i = 0; i < sessions.Count; i++) sessionIndices.Add(sessions[i].Id, i);
                foreach (var play in target.Sessions)
                {
                    if (!sessionIndices.TryGetValue(play.Id, out var index)) { sessionIndices.Add(play.Id, sessions.Count); sessions.Add(play); continue; }
                    var previous = sessions[index];
                    if (previous.Native is not null || play.Native is not null)
                    {
                        if (previous.Native is not { IsTerminal: true } a || play.Native is not { IsTerminal: true } b
                            || a.GameId != b.GameId || a.InstallationId != b.InstallationId || a.CreatedObservedAtUtc != b.CreatedObservedAtUtc
                            || a.StartedAtUtc != b.StartedAtUtc || a.Lifecycle != b.Lifecycle || a.Reason != b.Reason || a.EndedAtUtc != b.EndedAtUtc)
                            throw new SessionConflictException(UiText.Get("原生游玩记录冲突，未合并。"));
                        duplicateSeconds += Math.Min(previous.Seconds, play.Seconds); duplicateCount++;
                        if (b.Accumulated100ns > a.Accumulated100ns) sessions[index] = play;
                        continue;
                    }
                    if (previous.StartedAt != play.StartedAt) throw new InvalidDataException(UiText.Get("游玩记录编号冲突，未合并。"));
                    duplicateSeconds += Math.Min(previous.Seconds, play.Seconds);
                    if (previous.Seconds > 0 && play.Seconds > 0) duplicateCount++;
                    if (play.Seconds > previous.Seconds) sessions[index] = play;
                }
                var inheritCover = e.LocalCover is null && e.CoverChoice is null;
                e = e with
                {
                    Installations = e.Installations.AddRange(target.Installations), PreferredInstallationId = e.PreferredInstallationId ?? target.PreferredInstallationId,
                    Categories = [.. e.Categories.Concat(target.Categories).Distinct(StringComparer.Ordinal)], CustomTags = [.. e.CustomTags.Concat(target.CustomTags).Distinct(StringComparer.Ordinal)],
                    Favorite = e.Favorite || target.Favorite, PersonalRating = e.PersonalRating ?? target.PersonalRating, Status = e.Status == "planned" ? target.Status : e.Status,
                    Notes = target.Notes.Length > 0 && target.Notes != e.Notes ? string.Join("\n\n", new[] { e.Notes, target.Notes }.Where(s => s.Length > 0)) : e.Notes,
                    Sessions = sessions.ToImmutable(), TotalPlaySeconds = e.TotalPlaySeconds + target.TotalPlaySeconds - duplicateSeconds, PlayCount = e.PlayCount + target.PlayCount - duplicateCount,
                    ManualPlaytime = MetadataMixing.MergeLedger(e.ManualPlaytime, target.ManualPlaytime),
                    MigrationOrigins = MigrationLineage.Merge(e.MigrationOrigins, target.MigrationOrigins),
                    GuidePaths = [.. e.GuidePaths.Concat(target.GuidePaths).Distinct(StringComparer.OrdinalIgnoreCase)],
                    MetadataMix = MetadataMixing.Merge(work, e.MetadataMix, target.MetadataMix),
                    LastPlayedAt = target.LastPlayedAt is not null && (e.LastPlayedAt is null || string.CompareOrdinal(target.LastPlayedAt, e.LastPlayedAt) > 0) ? target.LastPlayedAt : e.LastPlayedAt,
                    LocalCover = inheritCover ? target.LocalCover : e.LocalCover,
                    CoverChoice = inheritCover && target.LocalCover is null && target.CoverChoice?.WorkId == work.Id ? target.CoverChoice : e.CoverChoice,
                    CoverDisplay = e.CoverDisplay.IsSpecified ? e.CoverDisplay : target.CoverDisplay
                };
                games = games.Remove(target);
            }
            var mix = e.MetadataMix;
            // A deliberate new association must not transplant overrides from a different work.
            if (target is null && source.Metadata?.Id != work.Id) mix = null;
            if (mix is not null) mix = mix with { Base = work };
            e = e with { MetadataMix = mix, Metadata = mix is null ? work : MetadataMixing.Compose(mix), Name = mergedName ?? (keep ? e.Name : work.Title), NameIsCustom = mergedName is not null || keep };
            return (games.Replace(source, e), e);
        }, cancellation, id, verifyBeforeCommit: verifyBeforeCommit);
    }
    private static LibraryEntry Find(ImmutableArray<LibraryEntry> entries, string id) => entries.FirstOrDefault(e => e.Id == id) ?? throw new InvalidDataException(UiText.Get("游戏不存在。"));
    public void Dispose()
    {
        lock (admission)
        {
            if (disposed) return;
            // Compatibility for idle owners only; NEVER block a UI thread waiting for I/O.
            if (admitted != 0 || shutdown is not null) throw new InvalidOperationException(UiText.Get("事务仍在运行，请 await DisposeAsync / ShutdownAsync。"));
            closing = true;
            DisposeResources();
        }
    }
    public Task ShutdownAsync()
    {
        lock (admission)
        {
            if (disposed) return Task.CompletedTask;
            if (shutdown is not null) return shutdown;
            closing = true;
            var wait = admitted == 0 ? Task.CompletedTask : (drained = new(TaskCreationOptions.RunContinuationsAsynchronously)).Task;
            // Accepted queued work drains in order; cancellation still skips pre-durable work.
            // Persistence already entered is allowed to finish and publish even after close.
            return shutdown = Task.Run(async () => { await wait.ConfigureAwait(false); lock (admission) DisposeResources(); });
        }
    }
    public ValueTask DisposeAsync() => new(ShutdownAsync());
    private void DisposeResources()
    {
        foreach (var registration in requestCancellations.Values) registration.Unregister();
        requestCancellations.Clear(); lifecycles.Clear(); persistence.Dispose(); queue.Dispose(); disposed = true;
    }
}
