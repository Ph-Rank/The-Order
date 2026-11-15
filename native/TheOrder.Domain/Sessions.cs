using System.Globalization;

namespace TheOrder.Domain;

public enum SessionLifecycle { Open, RecoveryPending, Completed, Incomplete, LegacyUnknown }
public enum SessionTracking { LaunchScope, CurrentProcessOnly, Unknown, Detached }
public enum SessionTiming { Running, AwaitHandoff, Suspended, Unverified, PersistenceBlocked, Stopped }
public enum SessionReason
{
    ExitConfirmed, ManagerExit, SystemShutdown, ObserverDetached, UserStoppedObservation,
    IdentityLost, ProcessGone, RecoveredProcessEnded, BootChanged, RecoveryUnavailable,
    RecoveryInvalid, BackupRecovery, ImportedSnapshot, LegacyUnspecified
}
public sealed record SessionRecovery(long Count, Guid? LastAttemptId);
public sealed record NativeSession(int Version, string GameId, string InstallationId,
    DateTimeOffset? StartedAtUtc, DateTimeOffset CreatedObservedAtUtc, DateTimeOffset? EndedAtUtc,
    DateTimeOffset? AccountingCutoffAtUtc, long Accumulated100ns, SessionLifecycle Lifecycle,
    SessionReason? Reason, bool UserPaused, long Revision, Guid WriterOwner,
    SessionRecovery Recovery, string? RecoveryToken)
{
    public const long TicksPerSecond = 10_000_000;
    public const long MinimumRecordedTicks = 300 * TicksPerSecond;
    public const long MaximumTicks = 366L * 86400 * TicksPerSecond;
    // Recovery checkpoints for new launches stay private to the timer until eligible.
    public bool AwaitingRecordingMinimum { get; init; }
    public bool IsTerminal => Lifecycle is SessionLifecycle.Completed or SessionLifecycle.Incomplete or SessionLifecycle.LegacyUnknown;
    public PlaySession Project(string id)
    {
        Validate();
        var start = StartedAtUtc ?? CreatedObservedAtUtc;
        var end = EndedAtUtc ?? AccountingCutoffAtUtc ?? start;
        if (end < start) end = start;
        return new(id, Format(start), Format(end), Accumulated100ns / TicksPerSecond, this);
    }
    public static string Format(DateTimeOffset value) => value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
    public void Validate()
    {
        if (Version != 1 || string.IsNullOrWhiteSpace(GameId) || GameId.Length > 200 || string.IsNullOrWhiteSpace(InstallationId)
            || InstallationId.Length > 200 || Accumulated100ns < 0 || Accumulated100ns > MaximumTicks
            || AwaitingRecordingMinimum && Accumulated100ns >= MinimumRecordedTicks
            || Revision < 0 || Revision > 9_007_199_254_740_991 || WriterOwner == Guid.Empty || Recovery is null
            || Recovery.Count < 0 || Recovery.Count > 9_007_199_254_740_991
            || Recovery.Count == 0 && Recovery.LastAttemptId is not null || Recovery.Count > 0 && Recovery.LastAttemptId is null
            || Recovery.LastAttemptId == Guid.Empty || !Enum.IsDefined(Lifecycle) || Reason is { } r && !Enum.IsDefined(r)
            || RecoveryToken is { Length: > 32768 } || IsTerminal && RecoveryToken is not null
            || Lifecycle == SessionLifecycle.Completed && (Reason != SessionReason.ExitConfirmed || EndedAtUtc is null)
            || Lifecycle != SessionLifecycle.Completed && (EndedAtUtc is not null || Reason == SessionReason.ExitConfirmed)
            || Lifecycle == SessionLifecycle.Open && Reason is not null
            || Lifecycle != SessionLifecycle.Open && Reason is null)
            throw new InvalidDataException("Session 记录不符合 v1 契约。");
    }
}
