using TheOrder.Application;
using System.Threading.Channels;
using TheOrder.Domain;

namespace TheOrder.Application;

// Explicit composition for isolated core/lifecycle hosts. No window, tray, automatic formal-profile access,
// or second writer. Owns its launch service and sampler handles; the caller owns library/power adapters.
public sealed class SessionEngine : IAsyncDisposable
{
    private readonly TransactionalLibrary library;
    private readonly IGameProcessPlatform platform;
    private readonly IFilesystemIdentity filesystem;
    private readonly ISessionClock clock;
    private readonly ISessionRecoveryTokens tokens;
    private readonly ISessionPowerEvents? power;
    private readonly bool requireRecordingMinimum;
    private readonly GameLaunchService launch;
    private readonly IDisposable engineLease;
    private readonly Channel<Func<Task>> commands = Channel.CreateBounded<Func<Task>>(new BoundedChannelOptions(256) { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });
    private readonly CancellationTokenSource lifetime = new();
    private readonly Task worker, polling;
    private readonly object gate = new();
    private Task? initialization, disposal;
    private Task<LaunchStatus>? launching;
    private bool closing, disposed;
    private int pendingPower = -1, announcedPower;
    private Runtime? active;
    // Admission outlives tracking/history. Immutable recovery binding; owns no OS handle.
    // Initialized by recovery, then only checked/released inside the existing launching gate.
    private SessionRecoveryIdentity? recoveredOccupancy;
    private SessionEngineSnapshot snapshot = new(null, SessionTracking.Unknown, SessionTiming.Stopped, false, 0, null, null);
    public SessionEngineSnapshot Snapshot => Volatile.Read(ref snapshot);
    public LaunchStatus LaunchStatus => launch.Status;
    public event Action<SessionEngineSnapshot>? Changed;

    private sealed class Runtime(LaunchSnapshot launch, PlaySession record, ProcessIdentity? current, bool full)
    {
        public LaunchSnapshot Launch = launch;
        public PlaySession Record = record;
        public PlaySession? Durable;
        public ProcessIdentity? Current = current;
        public IObservedProcess? Observed;
        public bool FullMonitor = full, Suspended, InstallationUnverified;
        public SessionTracking Tracking = full ? SessionTracking.LaunchScope : SessionTracking.CurrentProcessOnly;
        public SessionTiming Timing = SessionTiming.Unverified;
        public SessionAccumulator Time = new(record.Native!.Accumulated100ns);
        public SessionClockStamp? Bridge;
        public string? Error;
        public NativeSession Native => Record.Native!;
    }

    public SessionEngine(TransactionalLibrary library, IFilesystemIdentity filesystem, IGameProcessPlatform platform,
        ILaunchPositionStore positions, ISessionClock clock, ISessionRecoveryTokens tokens,
        ISessionPowerEvents? power = null, bool autoPoll = true, LaunchTrackingOptions? launchOptions = null, LaunchPolicy? launchPolicy = null,
        bool requireRecordingMinimum = false)
    {
        this.library = library; this.filesystem = filesystem; this.platform = platform; this.clock = clock; this.tokens = tokens; this.power = power;
        this.requireRecordingMinimum = requireRecordingMinimum;
        engineLease = library.AcquireSessionEngine();
        launch = new(new SessionLaunchLibrary(library), filesystem, platform, positions, launchOptions, launchPolicy);
        worker = Task.Run(ConsumeAsync);
        launch.Changed += StatusChanged; launch.ProcessChanged += ProcessChanged;
        if (power is not null) power.SuspendedChanged += PowerChanged;
        polling = autoPoll ? Task.Run(PollAsync) : Task.CompletedTask;
    }

    public Task InitializeAsync()
    {
        lock (gate)
        {
            if (initialization is null || initialization.IsFaulted || initialization.IsCanceled) initialization = Submit(RecoverCoreAsync);
            return initialization;
        }
    }
    public Task<LaunchPreparation> PrepareAsync(string gameId, string? installation = null, CancellationToken cancellation = default)
        => launch.PrepareAsync(gameId, installation, cancellation);
    public Task ConfirmPositionAsync(LaunchSnapshot reviewed, CancellationToken cancellation = default) => launch.ConfirmPositionAsync(reviewed, cancellation);
    public async Task<LaunchStatus> LaunchAsync(string gameId, string? installation = null, CancellationToken cancellation = default)
    {
        await InitializeAsync().ConfigureAwait(false);
        Task<LaunchStatus> task;
        lock (gate)
        {
            if (closing || disposed) return launch.Status with { Outcome = LaunchOutcome.Closed };
            if (launching is { IsCompleted: false }) return launch.Status with { Outcome = LaunchOutcome.AlreadyLaunching };
            if (Snapshot.Session?.Native is { IsTerminal: false }) return launch.Status with { Outcome = LaunchOutcome.AlreadyRunning, Message = UiText.Get("已有自有会话待处理。") };
            task = launching = Task.Run(() => LaunchWithRecoveredOccupancyAsync(gameId, installation, cancellation));
        }
        var result = await task.ConfigureAwait(false);
        // Also reconcile the successful result: event subscribers cannot make OS creation transactional.
        await Submit(() => HandleStatusAsync(result)).ConfigureAwait(false);
        return result;
    }
    private async Task<LaunchStatus> LaunchWithRecoveredOccupancyAsync(string gameId, string? installation, CancellationToken cancellation)
    {
        if (cancellation.IsCancellationRequested) return launch.Status with { Outcome = LaunchOutcome.Cancelled };
        if (recoveredOccupancy is { } reservation)
        {
            var presence = RevalidateRecoveredOccupancy(reservation);
            if (presence != ProcessPresence.Inactive)
                return launch.Status with { Outcome = LaunchOutcome.AlreadyRunning, Message = presence == ProcessPresence.Active
                    ? UiText.Get("原恢复游戏仍在运行；停止观察不会释放启动占用。")
                    : UiText.Get("无法确认原恢复游戏已结束，未启动另一个游戏；可稍后重试。") };
            // No other LaunchAsync can pass the launching gate between this release and 5A admission.
            recoveredOccupancy = null;
        }
        return await launch.LaunchAsync(gameId, installation, cancellation).ConfigureAwait(false);
    }
    private ProcessPresence RevalidateRecoveredOccupancy(SessionRecoveryIdentity reservation)
    {
        var identity = reservation.Process;
        try
        {
            using (var observed = platform.Attach(identity))
            {
                if (observed is not null)
                {
                    var value = observed.Query();
                    if (value.Identity != identity) return ProcessPresence.Unknown;
                    if (value.State == ProcessTrackingState.Exited) return ProcessPresence.Inactive;
                    if (!IsRunning(value)) return ProcessPresence.Unknown;
                    var installation = library.Snapshot.Entries.FirstOrDefault(g => g.Id == reservation.GameId)?
                        .Installations.FirstOrDefault(i => i.Id == reservation.InstallationId);
                    if (installation is null || installation.Folder != reservation.Folder.StoredPath
                        || installation.Executable != reservation.Executable.StoredPath
                        || LaunchSelection.WorkingDirectory(installation) != reservation.WorkingDirectory.StoredPath)
                        return ProcessPresence.Unknown;
                    var preflight = new InstallationPreflightService(filesystem);
                    preflight.Revalidate(reservation.Folder); preflight.Revalidate(reservation.Executable); preflight.Revalidate(reservation.WorkingDirectory);
                    return ProcessPresence.Active;
                }
            }
            // Attach may fail for a reused PID or an inaccessible/changed image. Only a positive
            // generation/exit proof releases ownership; PID+creation alone never proves Active here.
            return platform.RevalidateExternal(identity) == ProcessPresence.Inactive ? ProcessPresence.Inactive : ProcessPresence.Unknown;
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or CandidateStaleException or InvalidOperationException or System.ComponentModel.Win32Exception)
        { return ProcessPresence.Unknown; }
    }
    public Task DrainAsync() => Submit(() => Task.CompletedTask);
    // Windows may cancel shutdown after QUERYENDSESSION. Reopen the same invocation only
    // from exit preparation, preserving pause/count/duration and establishing a fresh baseline.
    public async Task CancelExitPreparationAsync()
    {
        await Submit(async () =>
        {
            if (active is { Native.Lifecycle: SessionLifecycle.RecoveryPending } r)
            {
                Freeze(r); Set(r, r.Native with { Lifecycle = SessionLifecycle.Open, Reason = null });
                r.Tracking = r.FullMonitor ? SessionTracking.LaunchScope : SessionTracking.CurrentProcessOnly;
                await SaveAsync(r).ConfigureAwait(false);
            }
        }).ConfigureAwait(false);
        lock (gate) { if (!disposed && disposal is null) closing = false; }
    }
    internal Task ObserveFactAsync(ProcessEvent fact) => Submit(() => HandleFactAsync(fact));
    internal Task ObserveLaunchStatusAsync(LaunchStatus value) => Submit(() => HandleStatusAsync(value));
    public Task SampleAsync() => Submit(SampleCoreAsync);
    public Task CheckpointAsync() => Submit(async () => { if (active is { } r) await SaveAsync(r).ConfigureAwait(false); });
    public Task PauseAsync() => Submit(async () =>
    {
        if (active is not { Native.IsTerminal: false } r || r.Native.UserPaused) return;
        Freeze(r); Set(r, r.Native with { UserPaused = true }); await SaveAsync(r).ConfigureAwait(false);
    });
    public Task ResumeAsync() => Submit(async () =>
    {
        if (active is not { Native.IsTerminal: false } r || !r.Native.UserPaused) return;
        Freeze(r);
        r.InstallationUnverified = !ValidateInstallation(r.Launch);
        if (!r.InstallationUnverified && Query(r).State is ProcessTrackingState.RunningConfirmed or ProcessTrackingState.RunningLimited)
            Set(r, r.Native with { UserPaused = false });
        await SampleCoreAsync().ConfigureAwait(false); await SaveAsync(r).ConfigureAwait(false);
    });
    public Task SuspendAsync() => Submit(() => HandlePowerAsync(true));
    public Task ResumeFromSuspendAsync() => Submit(() => HandlePowerAsync(false));
    public Task StopObservingAsync() => Submit(async () =>
    {
        if (active is not { Native.IsTerminal: false } r) return;
        Finish(r, SessionReason.UserStoppedObservation); await SaveAsync(r).ConfigureAwait(false);
    });

    public Task<bool> CheckpointForExitAsync(SessionReason reason = SessionReason.ManagerExit)
        => CheckpointForExitAsync(reason, stopTiming: false);

    internal async Task<bool> CheckpointForExitAsync(SessionReason reason, bool stopTiming)
    {
        if (reason is not (SessionReason.ManagerExit or SessionReason.SystemShutdown)) throw new ArgumentOutOfRangeException(nameof(reason));
        Task<LaunchStatus>? pending;
        lock (gate) { closing = true; pending = launching; }
        bool saved = false;
        try
        {
            if (pending is not null) await pending.ConfigureAwait(false);
            await DrainAsync().ConfigureAwait(false);
            await Submit(async () =>
            {
                if (stopTiming) await SampleCoreAsync().ConfigureAwait(false);
                if (active is not { } r) return;
                if (!r.Native.IsTerminal)
                {
                    r.Tracking = SessionTracking.Detached;
                    if (stopTiming) Finish(r, reason);
                    else
                    {
                        Freeze(r); SyncDuration(r);
                        Set(r, r.Native with { Lifecycle = r.Native.RecoveryToken is null ? SessionLifecycle.Incomplete : SessionLifecycle.RecoveryPending,
                            Reason = r.Native.RecoveryToken is null ? SessionReason.RecoveryUnavailable : reason });
                    }
                }
                // Retry a failed save with the same terminal cutoff.
                await SaveAsync(r).ConfigureAwait(false);
            }).ConfigureAwait(false);
            return saved = Snapshot.Error is null;
        }
        finally
        {
            if (!saved)
                lock (gate) { if (disposal is null) closing = false; }
        }
    }

    private Task Submit(Func<Task> work)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        return EnqueueAsync();
        async Task EnqueueAsync()
        {
            await commands.Writer.WriteAsync(async () =>
            {
                try { await work().ConfigureAwait(false); Publish(); completion.TrySetResult(); }
                catch (Exception e) { ReportFailure(e); Publish(); completion.TrySetException(e); }
            }).ConfigureAwait(false);
            await completion.Task.ConfigureAwait(false);
        }
    }
    private void StatusChanged(LaunchStatus status) => PostFact(() => HandleStatusAsync(status));
    private void ProcessChanged(ProcessEvent fact) => PostFact(() => HandleFactAsync(fact));
    private void PostFact(Func<Task> work)
    {
        // Launch/monitor publishing is on background workers. Bounded backpressure never drops a critical fact.
        try { commands.Writer.WriteAsync(work).AsTask().GetAwaiter().GetResult(); }
        catch (ChannelClosedException) { }
    }
    private void PowerChanged(bool suspended)
    {
        if (Interlocked.Exchange(ref announcedPower, suspended ? 1 : 0) == (suspended ? 1 : 0)) return;
        clock.Discontinuity(); Interlocked.Exchange(ref pendingPower, suspended ? 1 : 0);
        commands.Writer.TryWrite(() => Task.CompletedTask); // Coalesced flag survives a full queue; native callback never waits for disk.
    }
    private async Task ConsumeAsync()
    {
        await foreach (var command in commands.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            try
            {
                int changed = Interlocked.Exchange(ref pendingPower, -1);
                if (changed >= 0) await HandlePowerAsync(changed == 1).ConfigureAwait(false);
                await command().ConfigureAwait(false);
            }
            catch (Exception e) { ReportFailure(e); }
            Publish();
        }
    }
    private async Task PollAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        try
        {
            while (await timer.WaitForNextTickAsync(lifetime.Token).ConfigureAwait(false))
                try { await SampleAsync().ConfigureAwait(false); }
                catch (Exception e) when (e is IOException or InvalidOperationException or System.Runtime.InteropServices.ExternalException
                    or EntryPointNotFoundException or DllNotFoundException) { /* The command already exposed/froze the failure. */ }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (ChannelClosedException) { }
    }
    private void Publish()
    {
        var r = active;
        var value = r is null ? snapshot : new(r.Record, r.Tracking, r.Timing, r.Durable is not null, r.Time.Durable, r.Current, r.Error);
        Volatile.Write(ref snapshot, value);
        foreach (Action<SessionEngineSnapshot> listener in Changed?.GetInvocationList() ?? []) try { listener(value); } catch { }
    }
    private void ReportFailure(Exception e)
    {
        if (active is { } r) { Freeze(r); r.Timing = SessionTiming.PersistenceBlocked; r.Error = UiText.Format($"会话未能保存或验证：{e.GetType().Name}。可重试；游戏不会被关闭。"); }
        else Volatile.Write(ref snapshot, snapshot with { Error = UiText.Format($"会话核心不可用：{e.GetType().Name}。") });
    }

    private async Task HandleStatusAsync(LaunchStatus status)
    {
        if (!status.ProcessCreated || status.Snapshot is not { } origin) return;
        if (active?.Record.Id == origin.OperationId.ToString()) return;
        if (active is { Native.IsTerminal: false }) throw new SessionConflictException(UiText.Get("不允许第二个 owned Session。"));
        active?.Observed?.Dispose();
        var n = new NativeSession(1, origin.Game.GameId, origin.Installation.Id, CreationUtc(status.Initial), Utc(), null, null,
            0, SessionLifecycle.Open, null, false, 0, library.SessionWriterOwner, new(0, null), null)
        { AwaitingRecordingMinimum = requireRecordingMinimum };
        var r = active = new(origin, n.Project(origin.OperationId.ToString()), status.Current, true);
        SetToken(r); await SaveAsync(r).ConfigureAwait(false);
    }
    private async Task HandleFactAsync(ProcessEvent fact)
    {
        if (active is not { } r || r.Record.Id != fact.OperationId.ToString() || r.Native.IsTerminal) return;
        switch (fact.Kind)
        {
            case ProcessEventKind.ProcessStarted: return; // Changed(success) precedes this event in the authoritative publisher.
            case ProcessEventKind.ProcessTrackingAttached:
                if (r.Current is null && fact.Current is { } initial)
                { r.Current = initial; Set(r, r.Native with { StartedAtUtc = CreationUtc(initial) }); SetToken(r); }
                else if (fact.Current != r.Current) return;
                r.Tracking = SessionTracking.LaunchScope; break;
            case ProcessEventKind.ProcessHandoff:
                if (fact.Current is null || fact.Current == r.Current || fact.Previous != r.Current) return;
                var bridge = r.Bridge ?? r.Time.Baseline;
                r.Observed?.Dispose(); r.Observed = null; r.Current = fact.Current; r.Time.Freeze(); r.Bridge = null;
                var before = clock.Read(); var observed = Query(r); var after = clock.Read();
                if (bridge is { } from && !r.Native.UserPaused && !r.Suspended && r.Error is null && IsRunning(observed) && r.Durable is not null)
                    r.Time.Bridge(from, before, after);
                SetToken(r); break;
            case ProcessEventKind.TrackingLimited:
                r.FullMonitor = false; r.Tracking = r.Current is null ? SessionTracking.Unknown : SessionTracking.CurrentProcessOnly;
                Freeze(r);
                await SampleCoreAsync().ConfigureAwait(false);
                // Sampling may have finalized the session. Keep its cutoff fixed even if the save failed.
                if (r.Native.IsTerminal) return;
                break;
            case ProcessEventKind.Detached:
                r.FullMonitor = false; r.Tracking = SessionTracking.Detached; Freeze(r);
                if (r.Native.Lifecycle != SessionLifecycle.RecoveryPending)
                    Set(r, r.Native with { Lifecycle = r.Native.RecoveryToken is null ? SessionLifecycle.Incomplete : SessionLifecycle.RecoveryPending,
                        Reason = r.Native.RecoveryToken is null ? SessionReason.RecoveryUnavailable : SessionReason.ObserverDetached });
                r.Observed?.Dispose(); r.Observed = null; break;
            case ProcessEventKind.ProcessExited:
                if (fact.Current != r.Current || fact.Current is null) return;
                if (fact.ExitOccurredAtUtc is not { } end || fact.OccurredAtUtc != end)
                { Freeze(r); r.Tracking = SessionTracking.Unknown; break; }
                Freeze(r); Set(r, r.Native with { Lifecycle = SessionLifecycle.Completed, Reason = SessionReason.ExitConfirmed, EndedAtUtc = end, RecoveryToken = null });
                r.Observed?.Dispose(); r.Observed = null; r.Timing = SessionTiming.Stopped; break;
        }
        SyncDuration(r); await SaveAsync(r).ConfigureAwait(false);
    }

    private static bool IsRunning(ProcessObservation observation) => observation.State is ProcessTrackingState.RunningConfirmed or ProcessTrackingState.RunningLimited;
    private ProcessObservation Query(Runtime r)
    {
        if (r.Current is null) return new(null, ProcessTrackingState.Unknown);
        try
        {
            r.Observed ??= platform.Attach(r.Current);
            if (r.Observed is null) return new(null, platform.RevalidateExternal(r.Current) == ProcessPresence.Inactive ? ProcessTrackingState.Exited : ProcessTrackingState.Unknown);
            var observed = r.Observed.Query();
            return observed.Identity == r.Current ? observed : new(observed.Identity, ProcessTrackingState.Exited);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
        { return new(null, ProcessTrackingState.Unknown); }
    }
    private async Task SampleCoreAsync()
    {
        if (active is not { Native.IsTerminal: false } r || r.Native.Lifecycle != SessionLifecycle.Open || r.Durable is null || r.Error is not null || r.Suspended) return;
        if (r.InstallationUnverified)
        {
            Freeze(r);
            if (!ValidateInstallation(r.Launch))
            {
                if (r.FullMonitor) r.Timing = SessionTiming.AwaitHandoff;
                else { Finish(r, SessionReason.IdentityLost); await SaveAsync(r).ConfigureAwait(false); }
                return;
            }
            r.InstallationUnverified = false;
        }
        var before = clock.Read(); var observed = Query(r); var after = clock.Read();
        if (r.Time.Baseline is { } prior && prior.SuspendEpoch != before.SuspendEpoch)
        {
            Freeze(r);
            r.InstallationUnverified = !ValidateInstallation(r.Launch);
            if (r.InstallationUnverified) observed = new(null, ProcessTrackingState.Exited);
            await SaveAsync(r).ConfigureAwait(false);
            if (observed.State != ProcessTrackingState.Exited) return;
        }
        if (observed.State == ProcessTrackingState.Exited)
        {
            if (r.FullMonitor)
            {
                var changed = r.Timing != SessionTiming.AwaitHandoff;
                r.Bridge ??= r.Time.Baseline; r.Time.Freeze(); r.Timing = SessionTiming.AwaitHandoff;
                // Wait for 5A, including Changed(Exited) -> ProcessChanged(Exited) publication ordering.
                if (changed) { await SaveAsync(r).ConfigureAwait(false); if (r.Error is null) r.Timing = SessionTiming.AwaitHandoff; }
            }
            else { Finish(r, r.Native.Recovery.Count > 0 ? SessionReason.RecoveredProcessEnded : SessionReason.ProcessGone); await SaveAsync(r).ConfigureAwait(false); }
            return;
        }
        if (!IsRunning(observed))
        {
            var changed = r.Timing != SessionTiming.Unverified; Freeze(r); r.Tracking = SessionTracking.Unknown;
            if (changed) await SaveAsync(r).ConfigureAwait(false);
            return;
        }
        r.Tracking = r.FullMonitor ? SessionTracking.LaunchScope : SessionTracking.CurrentProcessOnly;
        r.Time.Observe(before, after, !r.Native.UserPaused);
        r.Timing = r.Native.UserPaused ? SessionTiming.Stopped : r.Time.AtLimit ? SessionTiming.PersistenceBlocked : SessionTiming.Running;
        SyncDuration(r);
        if (r.Time.AtCapacity)
        {
            await SaveAsync(r).ConfigureAwait(false); r.Time.Freeze(); r.Timing = SessionTiming.PersistenceBlocked;
            r.Error ??= UiText.Get("此会话已达到历史格式的时长上限，计时已冻结；原记录保留。");
        }
        else if (r.Time.Accumulated - r.Time.Durable >= SessionAccumulator.CheckpointTicks
            || r.Durable.Native!.AwaitingRecordingMinimum && !r.Native.AwaitingRecordingMinimum)
            await SaveAsync(r).ConfigureAwait(false);
    }
    private async Task HandlePowerAsync(bool suspended)
    {
        if (active is not { Native.IsTerminal: false } r) return;
        if (r.Suspended == suspended) return;
        Interlocked.Exchange(ref announcedPower, suspended ? 1 : 0); clock.Discontinuity();
        r.Suspended = suspended; Freeze(r);
        if (suspended) r.Timing = SessionTiming.Suspended;
        else if (!ValidateInstallation(r.Launch))
        { r.InstallationUnverified = true; if (r.FullMonitor) r.Timing = SessionTiming.AwaitHandoff; else Finish(r, SessionReason.IdentityLost); }
        else await SampleCoreAsync().ConfigureAwait(false);
        await SaveAsync(r).ConfigureAwait(false);
    }
    private static void Set(Runtime r, NativeSession value) => r.Record = value.Project(r.Record.Id);
    private DateTimeOffset Utc() { try { return clock.Read().Utc; } catch { return DateTimeOffset.UtcNow; } }
    private void Freeze(Runtime r)
    {
        r.Time.Freeze(); r.Bridge = null; r.Timing = SessionTiming.Unverified;
        if (!r.Native.IsTerminal) Set(r, r.Native with { AccountingCutoffAtUtc = Utc() });
    }
    private void SyncDuration(Runtime r) => Set(r, r.Native with
    {
        Accumulated100ns = r.Time.Accumulated,
        AccountingCutoffAtUtc = Utc(),
        AwaitingRecordingMinimum = r.Native.AwaitingRecordingMinimum && r.Time.Accumulated < NativeSession.MinimumRecordedTicks
    });
    private void Finish(Runtime r, SessionReason reason)
    {
        Freeze(r); SyncDuration(r); Set(r, r.Native with { Lifecycle = SessionLifecycle.Incomplete, Reason = reason, EndedAtUtc = null, RecoveryToken = null });
        r.Timing = SessionTiming.Stopped; r.Observed?.Dispose(); r.Observed = null;
    }
    private async Task<bool> SaveAsync(Runtime r)
    {
        if (r.Native.IsTerminal && r.Record == r.Durable) { r.Error = null; return true; }
        // A terminal candidate's cutoff, final delta and real exit time are fixed.
        // Its revision is derived from Durable until the transaction acknowledges it.
        if (!r.Native.IsTerminal) SyncDuration(r);
        // No elapsed interval is extrapolated across disk/queue latency, including a hung writer.
        r.Time.Freeze();
        var target = r.Native with { Revision = r.Durable is null ? 0 : checked(r.Durable.Native!.Revision + 1),
            RecoveryToken = r.Native.IsTerminal ? null : r.Native.RecoveryToken };
        try
        {
            var saved = target.Project(r.Record.Id);
            if (r.Durable is null && target.IsTerminal && target.AwaitingRecordingMinimum)
                await library.ReleaseUnrecordedLaunchAsync(r.Launch.Game, target.InstallationId).ConfigureAwait(false);
            else saved = (await library.CommitSessionAsync(r.Launch.Game, saved, create: r.Durable is null).ConfigureAwait(false)).Value;
            r.Record = r.Durable = saved; r.Time.Acknowledge(saved.Native!.Accumulated100ns); r.Error = null;
            if (r.Native.IsTerminal) r.Timing = SessionTiming.Stopped;
            else if (!r.Suspended) r.Timing = r.Native.UserPaused ? SessionTiming.Stopped : SessionTiming.Unverified;
            return true;
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException or OverflowException)
        { Freeze(r); r.Timing = SessionTiming.PersistenceBlocked; r.Error = UiText.Format($"游戏状态保留，记录未保存（{e.GetType().Name}）；请重试。"); return false; }
    }
    private static DateTimeOffset? CreationUtc(ProcessIdentity? identity)
    { try { return identity is null ? null : DateTimeOffset.FromFileTime(identity.CreationFileTime); } catch (ArgumentOutOfRangeException) { return null; } }
    private bool ValidateInstallation(LaunchSnapshot origin)
    {
        try
        {
            var install = library.Snapshot.Entries.FirstOrDefault(e => e.Id == origin.Game.GameId)?.Installations.FirstOrDefault(i => i.Id == origin.Installation.Id);
            if (install is null || install.Folder != origin.Installation.Folder || install.Executable != origin.Installation.Executable
                || LaunchSelection.WorkingDirectory(install) != origin.WorkingDirectory.StoredPath) return false;
            var preflight = new InstallationPreflightService(filesystem);
            preflight.Revalidate(origin.Folder); preflight.Revalidate(origin.Executable); preflight.Revalidate(origin.WorkingDirectory);
            return true;
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or CandidateStaleException or InvalidOperationException) { return false; }
    }
    private void SetToken(Runtime r)
    {
        string? value = null;
        try
        {
            if (r.Current is { ImageIdentity: not null } current && tokens.BootId is { } boot && ValidateInstallation(r.Launch))
                value = tokens.Seal(new(1, r.Record.Id, r.Native.GameId, r.Native.InstallationId, boot, tokens.ProfileIdentity,
                    current, r.Launch.Folder, r.Launch.Executable, r.Launch.WorkingDirectory));
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException or System.Security.Cryptography.CryptographicException) { }
        Set(r, r.Native with { RecoveryToken = value });
    }

    private async Task RecoverCoreAsync()
    {
        if (library.Snapshot.Access != LibraryAccess.ValidWritable) throw new LibraryReadOnlyException(library.Snapshot.ReadOnlyReason!);
        if (active is null) Volatile.Write(ref snapshot, snapshot with { Error = null });
        var candidates = library.Snapshot.Entries.SelectMany(g => g.Sessions.Where(s => s.Native is { IsTerminal: false }).Select(s => (Game: g, Session: s))).ToArray();
        if (candidates.Length > 1 || candidates.Length > 0 && library.Snapshot.Recovery == LibraryRecovery.Backup)
        {
            await library.InvalidateSessionRecoveryAsync(library.Snapshot.Recovery == LibraryRecovery.Backup ? SessionReason.BackupRecovery : SessionReason.RecoveryInvalid).ConfigureAwait(false);
            return;
        }
        foreach (var (game, session) in candidates)
        {
            var n = session.Native!;
            var entry = await library.CaptureLaunchAsync(game.Id).ConfigureAwait(false);
            var install = entry.Installations.FirstOrDefault(i => i.Id == n.InstallationId);
            var token = ReadToken(n.RecoveryToken);
            var reason = library.Snapshot.Recovery == LibraryRecovery.Backup ? SessionReason.BackupRecovery
                : candidates.Length != 1 ? SessionReason.RecoveryInvalid : SessionReason.RecoveryUnavailable;
            IObservedProcess? process = null;
            LaunchSnapshot? origin = null;
            bool valid = false;
            try
            {
                try
                {
                var boot = tokens.BootId;
                if (reason != SessionReason.BackupRecovery && candidates.Length == 1 && install is not null && token is not null
                    && token.SessionId == session.Id && token.GameId == game.Id && token.InstallationId == install.Id
                    && token.Version == 1 && token.ProfileIdentity == tokens.ProfileIdentity
                    && token is { Process: { Pid: > 0, CreationFileTime: > 0, ImageIdentity: not null }, Folder: not null, Executable: not null, WorkingDirectory: not null })
                {
                    reason = boot is null || token.BootId != boot ? SessionReason.BootChanged : SessionReason.RecoveryInvalid;
                    origin = new(Guid.Parse(session.Id), 0, entry.Identity, install, entry.Preferred, token.Folder, token.Executable, token.WorkingDirectory, install.WorkingDirectory is not null);
                    if (boot == token.BootId && ValidateInstallation(origin))
                    {
                        process = platform.Attach(token.Process);
                        var observed = process?.Query();
                        valid = observed?.Identity == token.Process && IsRunning(observed) && tokens.BootId == boot;
                        if (!valid) reason = SessionReason.ProcessGone;
                    }
                }
                }
                catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException or FormatException or System.ComponentModel.Win32Exception)
                { valid = false; reason = SessionReason.RecoveryInvalid; }
                var next = n with { WriterOwner = library.SessionWriterOwner, Revision = n.Revision + 1,
                    Lifecycle = valid ? SessionLifecycle.Open : SessionLifecycle.Incomplete, Reason = valid ? null : reason,
                    EndedAtUtc = null, RecoveryToken = valid ? n.RecoveryToken : null,
                    Recovery = valid && n.Recovery.LastAttemptId != library.SessionWriterOwner ? new(n.Recovery.Count + 1, library.SessionWriterOwner) : n.Recovery };
                var receipt = await library.CommitSessionAsync(entry.Identity, next.Project(session.Id), recover: true, expectedRevision: n.Revision).ConfigureAwait(false);
                if (valid)
                {
                    active = new(origin!, receipt.Value, token!.Process, false) { Durable = receipt.Value, Observed = process };
                    recoveredOccupancy = token;
                    process = null;
                }
            }
            finally { process?.Dispose(); }
        }
    }
    private SessionRecoveryIdentity? ReadToken(string? value)
    {
        try { return value is null ? null : tokens.Open(value); }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException or FormatException or System.Security.Cryptography.CryptographicException) { return null; }
    }

    public ValueTask DisposeAsync()
    {
        lock (gate) return new(disposal ??= DisposeCoreAsync());
    }
    private async Task DisposeCoreAsync()
    {
        Task<LaunchStatus>? pending;
        lock (gate) { closing = true; pending = launching; }
        if (pending is not null) await pending.ConfigureAwait(false);
        lifetime.Cancel(); await polling.ConfigureAwait(false);
        if (power is not null) power.SuspendedChanged -= PowerChanged;
        // Resource disposal itself is not a successful graceful-exit checkpoint.
        launch.Changed -= StatusChanged; launch.ProcessChanged -= ProcessChanged;
        await launch.DisposeAsync().ConfigureAwait(false);
        commands.Writer.TryComplete(); await worker.ConfigureAwait(false);
        active?.Observed?.Dispose(); recoveredOccupancy = null;
        lifetime.Dispose(); engineLease.Dispose(); lock (gate) disposed = true;
    }
}
