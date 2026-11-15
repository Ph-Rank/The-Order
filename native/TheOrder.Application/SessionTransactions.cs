using TheOrder.Application;
using System.Collections.Immutable;
using TheOrder.Domain;

namespace TheOrder.Application;

public sealed class SessionConflictException(string message = "Session 已变化，未保存旧请求。") : InvalidOperationException(message);

public sealed partial class TransactionalLibrary
{
    private readonly Dictionary<string, string> pendingOwnedSessions = [];
    private bool sessionEngineOwned;
    internal bool HasSessionEngine { get { lock (admission) return sessionEngineOwned; } }
    public Guid SessionWriterOwner => epoch;
    internal IDisposable AcquireSessionEngine()
    {
        lock (admission)
        {
            ObjectDisposedException.ThrowIf(closing || disposed, this);
            if (sessionEngineOwned) throw new SessionConflictException(UiText.Get("此 writer 已有会话引擎。"));
            sessionEngineOwned = true; return new EngineLease(this);
        }
    }
    private sealed class EngineLease(TransactionalLibrary owner) : IDisposable
    {
        private TransactionalLibrary? current = owner;
        public void Dispose() { if (Interlocked.Exchange(ref current, null) is { } value) lock (value.admission) value.sessionEngineOwned = false; }
    }

    // Used only by the 5B composition. The existing 5A service/contracts are unchanged.
    internal Task<T> WithSessionLaunchAdmissionAsync<T>(GameLaunchIdentity identity, string installationId, Func<T> create, CancellationToken cancellation)
        => Enqueue(() =>
        {
            AssertSessionIdentity(identity, installationId);
            if (current.Access != LibraryAccess.ValidWritable) throw new LibraryReadOnlyException(current.ReadOnlyReason!);
            if (pendingOwnedSessions.Count != 0 || current.Entries.Any(e => e.Sessions.Any(s => s.Native is { IsTerminal: false })))
                throw new LaunchValidationException(LaunchOutcome.AlreadyRunning, UiText.Get("已有自有会话待处理，没有启动第二个程序。"));
            cancellation.ThrowIfCancellationRequested();
            var result = create();
            if (result is IObservedProcess) pendingOwnedSessions[identity.GameId] = installationId;
            return Task.FromResult(result);
        }, cancellation);

    private void AssertSessionIdentity(GameLaunchIdentity identity, string installationId, bool requireInstallation = true)
    {
        if (identity.Epoch != epoch || !launchIncarnations.TryGetValue(identity.GameId, out var incarnation) || incarnation != identity.Incarnation
            || !current.Entries.Any(e => e.Id == identity.GameId && (!requireInstallation || e.Installations.Any(i => i.Id == installationId))))
            throw new StaleLibraryRequestException();
    }

    // The initial checkpoint may fail. If play ends before it was ever saved, release
    // that admission without rewriting the library or disturbing the launch scope.
    internal Task ReleaseUnrecordedLaunchAsync(GameLaunchIdentity identity, string installationId) => Enqueue(() =>
    {
        AssertSessionIdentity(identity, installationId);
        if (pendingOwnedSessions.GetValueOrDefault(identity.GameId) != installationId) throw new SessionConflictException();
        pendingOwnedSessions.Remove(identity.GameId);
        return Task.FromResult(true);
    }, default);

    public Task<CommitResult<PlaySession>> CommitSessionAsync(GameLaunchIdentity identity, PlaySession target,
        bool create = false, bool recover = false, long? expectedRevision = null, CancellationToken cancellation = default)
        => Mutate(games =>
        {
            var n = target.Native ?? throw new SessionConflictException(); n.Validate();
            if (n.WriterOwner != epoch || n.GameId != identity.GameId) throw new SessionConflictException();
            AssertSessionIdentity(identity, n.InstallationId, !(recover && n.IsTerminal));
            var game = Find(games, identity.GameId);
            var previous = game.Sessions.FirstOrDefault(s => s.Id == target.Id);
            var old = previous?.Native;
            if (previous is null)
            {
                if (!create || recover || !pendingOwnedSessions.TryGetValue(game.Id, out var install) || install != n.InstallationId
                    || games.Any(e => e.Sessions.Any(s => s.Id == target.Id)) || n.Accumulated100ns != 0 || n.Revision != 0
                    || n.Recovery != new SessionRecovery(0, null) || n.IsTerminal && n.AwaitingRecordingMinimum) throw new SessionConflictException();
            }
            else
            {
                if (old is null || old.GameId != n.GameId || old.InstallationId != n.InstallationId
                    || old.CreatedObservedAtUtc != n.CreatedObservedAtUtc || old.StartedAtUtc is not null && old.StartedAtUtc != n.StartedAtUtc)
                    throw new SessionConflictException();
                if (old.WriterOwner == n.WriterOwner && n.Revision < old.Revision) return (games, previous);
                if (old == n) return (games, previous);
                if (old.IsTerminal || old.WriterOwner == n.WriterOwner && n.Revision == old.Revision) throw new SessionConflictException();
                if (recover)
                {
                    if (old.Revision != expectedRevision || n.Accumulated100ns != old.Accumulated100ns
                        || n.Revision != old.Revision + 1 || n.UserPaused != old.UserPaused
                        || n.Recovery.Count < old.Recovery.Count || n.Recovery.Count > old.Recovery.Count + 1
                        || old.Recovery.LastAttemptId == epoch && n.Recovery != old.Recovery
                        || n.Recovery != old.Recovery && n.Recovery.LastAttemptId != epoch)
                        throw new SessionConflictException();
                }
                else if (old.WriterOwner != epoch || n.Revision <= old.Revision || n.Recovery != old.Recovery) throw new SessionConflictException();
                if (n.Accumulated100ns < old.Accumulated100ns) throw new SessionConflictException();
                if (n.AwaitingRecordingMinimum != (old.AwaitingRecordingMinimum && n.Accumulated100ns < NativeSession.MinimumRecordedTicks))
                    throw new SessionConflictException();
            }
            var projected = n.Project(target.Id);
            if (n.IsTerminal && n.AwaitingRecordingMinimum)
                return (games.Replace(game, game with { Sessions = game.Sessions.Remove(previous!) }), projected);
            bool countedBefore = old is { AwaitingRecordingMinimum: false };
            bool countedNow = !n.AwaitingRecordingMinimum;
            var delta = checked((countedNow ? projected.Seconds : 0) - (countedBefore ? previous!.Seconds : 0));
            var start = NativeSession.Format(n.StartedAtUtc ?? n.CreatedObservedAtUtc);
            var changed = game with
            {
                Sessions = previous is null ? game.Sessions.Add(projected) : game.Sessions.Replace(previous, projected),
                TotalPlaySeconds = checked(game.TotalPlaySeconds + delta),
                PlayCount = checked(game.PlayCount + (countedNow && !countedBefore ? 1 : 0)),
                LastPlayedAt = countedNow && (game.LastPlayedAt is null || string.CompareOrdinal(start, game.LastPlayedAt) > 0) ? start : game.LastPlayedAt
            };
            return (games.Replace(game, changed), projected);
        }, cancellation, sessionMutation: true);

    private void ProtectSessionBindings(ImmutableArray<LibraryEntry> before, ImmutableArray<LibraryEntry> after, string? forceNewGeneration)
    {
        foreach (var e in before)
        {
            var bindings = e.Sessions.Where(s => s.Native is { IsTerminal: false }).Select(s => s.Native!.InstallationId).ToHashSet();
            if (pendingOwnedSessions.TryGetValue(e.Id, out var pending)) bindings.Add(pending);
            if (bindings.Count == 0) continue;
            var next = after.FirstOrDefault(g => g.Id == e.Id);
            if (next is null || forceNewGeneration == e.Id || bindings.Any(id => e.Installations.FirstOrDefault(i => i.Id == id) != next.Installations.FirstOrDefault(i => i.Id == id))
                || !e.Installations.Select(i => i.Id).Order().SequenceEqual(next.Installations.Select(i => i.Id).Order()))
                throw new SessionConflictException(UiText.Get("请先处理此游戏的活动会话，再删除、合并或更换安装。"));
        }
    }

    internal Task<CommitResult<bool>> InvalidateSessionRecoveryAsync(SessionReason reason) => Mutate(games =>
    {
        var changed = games.Select(e => e with { Sessions = [.. e.RecordedSessions.Select(s => s.Native is { IsTerminal: false } n
            ? (n with { WriterOwner = epoch, Revision = n.Revision + 1, Lifecycle = SessionLifecycle.Incomplete,
                Reason = reason, EndedAtUtc = null, RecoveryToken = null }).Project(s.Id) : s)] }).ToImmutableArray();
        return (changed, true);
    }, default, sessionMutation: true);

    public Task<byte[]> ExportPortableAsync(CancellationToken cancellation = default) => Enqueue(() =>
    {
        if (current.Access != LibraryAccess.ValidWritable) throw new LibraryReadOnlyException(current.ReadOnlyReason!);
        return Task.FromResult(codec.Encode([.. current.Entries.Select(PortableSessionHistory)], now()));
    }, cancellation);

    private static LibraryEntry PortableSessionHistory(LibraryEntry e) => e with
    {
        Sessions = [.. e.RecordedSessions.Select(s => s.Native is { IsTerminal: false } n
            ? (n with { Lifecycle = SessionLifecycle.Incomplete, Reason = SessionReason.ImportedSnapshot, EndedAtUtc = null, RecoveryToken = null }).Project(s.Id) : s)]
    };
}

internal sealed class SessionLaunchLibrary(TransactionalLibrary library) : ILaunchLibrary
{
    public Task<LaunchEntry> CaptureLaunchAsync(string id, CancellationToken cancellation) => library.CaptureLaunchAsync(id, cancellation);
    public Task<T> WithLaunchAdmissionAsync<T>(GameLaunchIdentity identity, string installationId, Func<T> create, CancellationToken cancellation)
        => library.WithSessionLaunchAdmissionAsync(identity, installationId, create, cancellation);
}
