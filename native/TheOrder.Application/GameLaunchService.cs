using TheOrder.Application;
using System.Collections.Immutable;
using System.Diagnostics;

namespace TheOrder.Application;

public sealed record LaunchTrackingOptions
{
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromMilliseconds(250);
    public TimeSpan RunningPollInterval { get; init; } = TimeSpan.FromSeconds(1);
    public TimeSpan InitialTreeWindow { get; init; } = TimeSpan.FromSeconds(12);
    public TimeSpan HandoffWindow { get; init; } = TimeSpan.FromSeconds(6);
    public TimeSpan CandidateSettle { get; init; } = TimeSpan.FromMilliseconds(750);
    public int MaximumProcesses { get; init; } = 8192;
    public int MaximumCandidates { get; init; } = 32;
    public int MaximumTreePolls { get; init; } = 240;
}

// Single active launch admission, with no writable library/playtime dependency.
public sealed class GameLaunchService(ILaunchLibrary library, IFilesystemIdentity filesystem, IGameProcessPlatform platform,
    ILaunchPositionStore positions, LaunchTrackingOptions? trackingOptions = null, LaunchPolicy? policy = null) : IAsyncDisposable
{
    private readonly object gate = new();
    private readonly SemaphoreSlim positionGate = new(1, 1);
    private readonly CancellationTokenSource lifetime = new();
    private readonly LaunchTrackingOptions options = trackingOptions ?? new();
    private readonly LaunchPolicy effectivePolicy = policy ?? new();
    private Task? startTask, monitorTask;
    private bool starting, reserved, closing;
    private IProcessLifetimeScope? lifetimeScope;
    private ImmutableArray<ProcessIdentity> externalReservation = [];
    private long generation;
    private LaunchStatus status = new(LaunchOutcome.Ready, ProcessTrackingState.None, null, null, null, UiText.Get("尚未启动。"));
    public LaunchStatus Status => Volatile.Read(ref status);
    public event Action<LaunchStatus>? Changed;
    public event Action<ProcessEvent>? ProcessChanged;

    private void Publish(LaunchStatus value, ProcessEventKind? kind = null, ProcessIdentity? previous = null)
    {
        Volatile.Write(ref status, value);
        // Observers cannot turn a successful OS creation into a launch failure.
        foreach (Action<LaunchStatus> listener in Changed?.GetInvocationList() ?? []) { try { listener(value); } catch { } }
        if (kind is null || value.Snapshot is null) return;
        var occurred = kind == ProcessEventKind.ProcessStarted && value.Current is { } started ? FileTime(started.CreationFileTime)
            : kind == ProcessEventKind.ProcessExited ? value.ExitOccurredAtUtc : null;
        var notification = new ProcessEvent(kind.Value, value.Snapshot.OperationId, previous, value.Current, DateTimeOffset.UtcNow, occurred,
            kind == ProcessEventKind.ProcessExited ? value.ExitOccurredAtUtc : null);
        foreach (Action<ProcessEvent> listener in ProcessChanged?.GetInvocationList() ?? []) { try { listener(notification); } catch { } }
    }

    public async Task<LaunchPreparation> PrepareAsync(string gameId, string? explicitInstallation, CancellationToken cancellation = default)
    {
        try
        {
            var entry = await library.CaptureLaunchAsync(gameId, cancellation).ConfigureAwait(false);
            var installation = LaunchSelection.Select(entry, explicitInstallation);
            if (installation is null) return new(LaunchOutcome.InstallationMissing, null, UiText.Get("请选择首选或明确的本地安装。"));
            long? policyRevision = null;
            { var resolved = effectivePolicy.Resolve(installation); installation = resolved.Installation; policyRevision = resolved.Revision; }
            if (installation.Executable is null) return new(LaunchOutcome.ExecutableMissing, null, UiText.Get("点击启动，从本安装目录选择 exe 或 cmd；选择后会记住启动位置。"));
            var snapshot = await Task.Run(() =>
            {
                cancellation.ThrowIfCancellationRequested();
                var folder = Require(installation.Folder, true, LaunchOutcome.InstallationMissing);
                var executable = Require(installation.Executable, false, LaunchOutcome.ExecutableMissing);
                if (executable.IsDirectory || !LaunchFileDiscovery.IsLaunchFile(Path.GetFileName(installation.Executable))
                    || !InstallationPreflightService.Within(executable.FinalPath, folder.FinalPath))
                    throw new LaunchValidationException(LaunchOutcome.StaleInstallation, UiText.Get("启动文件不属于已选择的安装目录。"));
                var cwd = LaunchSelection.WorkingDirectory(installation);
                var working = Require(cwd, true, LaunchOutcome.WorkingDirectoryInvalid);
                if (working.Identity != folder.Identity && !InstallationPreflightService.Within(working.FinalPath, folder.FinalPath))
                    throw new LaunchValidationException(LaunchOutcome.WorkingDirectoryInvalid, UiText.Get("工作目录必须位于安装目录内，不会自动更换目录。"));
                if (installation.Arguments.IsDefault || installation.Arguments.Length > 100 || installation.Arguments.Any(a => a is null || a.Length > 8192 || a.Contains('\0')))
                    throw new LaunchValidationException(LaunchOutcome.LaunchFailed, UiText.Get("启动参数无效，请检查参数数组。"));
                var options = installation.Compatibility;
                if (options?.UseSteam == true && (installation.SteamAppId is null || options.Administrator || options.HighDpi || options.LocaleEmulator is not null))
                    throw new LaunchValidationException(LaunchOutcome.LaunchFailed, "Steam 专用启动不能与转区、管理员或 DPI 辅助同时启用。");
                if (options?.Administrator == true && options.HighDpi) throw new LaunchValidationException(LaunchOutcome.LaunchFailed, "管理员启动与进程内 DPI 辅助不能同时启用，请选择其中一项。");
                if (options?.LocaleEmulator is not null && (!Path.GetExtension(installation.Executable).Equals(".exe", StringComparison.OrdinalIgnoreCase) || working.Identity != Require(Path.GetDirectoryName(installation.Executable)!, true, LaunchOutcome.WorkingDirectoryInvalid).Identity))
                    throw new LaunchValidationException(LaunchOutcome.LaunchFailed, "Locale Emulator 需要 exe，且工作目录须为该 exe 所在目录。");
                PathObservation? Tool(string? path, string name)
                {
                    if (path is null) return null; var tool = Require(path, false, LaunchOutcome.ExecutableMissing);
                    if (tool.ViaReparsePoint || !Path.GetFileName(path).Equals(name, StringComparison.OrdinalIgnoreCase)) throw new LaunchValidationException(LaunchOutcome.LaunchFailed, "启动辅助工具路径无效，请重新选择。");
                    return tool;
                }
                return new LaunchSnapshot(Guid.NewGuid(), Interlocked.Increment(ref generation), entry.Identity, installation, entry.Preferred,
                    folder, executable, working, installation.WorkingDirectory is not null) { LocaleTool = Tool(options?.LocaleEmulator, "LEProc.exe"), ScalingTool = Tool(options?.ScalingTool, "Magpie.exe"), PolicyRevision = policyRevision };
            }, cancellation).ConfigureAwait(false);
            await positionGate.WaitAsync(cancellation).ConfigureAwait(false);
            try
            {
                var saved = (await positions.ReadAsync(cancellation).ConfigureAwait(false)).FirstOrDefault(p => p.GameId == gameId && p.InstallationId == installation.Id);
                return saved is null ? new(LaunchOutcome.PositionReviewRequired, snapshot, UiText.Get("首次使用此安装，请核对 exe 和工作目录后确认启动位置。"))
                    : LaunchSelection.Matches(saved, snapshot) ? new(LaunchOutcome.Ready, snapshot, UiText.Get("启动位置已核对。"))
                    : new(LaunchOutcome.StaleInstallation, snapshot, UiText.Get("安装目标与本机核对记录不同，未允许启动。请重新核对位置。"));
            }
            finally { positionGate.Release(); }
        }
        catch (LaunchValidationException error) { return new(error.Outcome, null, error.Message); }
        catch (OperationCanceledException) { return new(LaunchOutcome.Cancelled, null, UiText.Get("启动准备已取消。")); }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException)
        { return new(LaunchOutcome.StaleInstallation, null, UiText.Get("启动位置未能安全核对，未执行程序。请检查安装和本机核对记录。")); }
    }

    private PathObservation Require(string path, bool directory, LaunchOutcome missing)
    {
        var probe = filesystem.Inspect(path, directory);
        if (!probe.IsValid)
        {
            var message = probe.Status == PreflightStatus.NotFound ? missing switch
            {
                LaunchOutcome.ExecutableMissing => UiText.Get("启动文件不存在，请在安装设置中重新选择 exe。"),
                LaunchOutcome.WorkingDirectoryInvalid => UiText.Get("工作目录不存在，未启动程序，也不会自动更换目录。"),
                _ => UiText.Get("安装目录不存在，请检查本地安装位置。")
            } : probe.Message;
            throw new LaunchValidationException(probe.Status == PreflightStatus.AccessDenied ? LaunchOutcome.AccessDenied : missing, message);
        }
        return probe.Observation!;
    }

    public async Task ConfirmPositionAsync(LaunchSnapshot reviewed, CancellationToken cancellation = default)
    {
        await positionGate.WaitAsync(cancellation).ConfigureAwait(false);
        try
        {
            var preflight = new InstallationPreflightService(filesystem);
            await library.WithLaunchAdmissionAsync(reviewed.Game, reviewed.Installation.Id, () =>
            {
                if (reviewed.PolicyRevision is { } revision) effectivePolicy.Admit(revision, () => true);
                preflight.Revalidate(reviewed.Folder); preflight.Revalidate(reviewed.Executable); preflight.Revalidate(reviewed.WorkingDirectory);
                if (reviewed.LocaleTool is not null) preflight.Revalidate(reviewed.LocaleTool);
                if (reviewed.ScalingTool is not null) preflight.Revalidate(reviewed.ScalingTool); return true;
            }, cancellation).ConfigureAwait(false);
            var existing = await positions.ReadAsync(cancellation).ConfigureAwait(false);
            var next = existing.Where(p => p.GameId != reviewed.Game.GameId || p.InstallationId != reviewed.Installation.Id).Append(LaunchSelection.Position(reviewed)).ToImmutableArray();
            await positions.WriteAsync(next, cancellation).ConfigureAwait(false);
        }
        finally { positionGate.Release(); }
    }

    public Task<LaunchStatus> LaunchAsync(string gameId, string? explicitInstallation = null, CancellationToken cancellation = default)
    {
        lock (gate)
        {
            if (closing) return Task.FromResult(Status with { Outcome = LaunchOutcome.Closed, Message = UiText.Get("管理器正在关闭。") });
            if (starting) return Task.FromResult(Status with { Outcome = LaunchOutcome.AlreadyLaunching, Message = UiText.Get("已有启动操作正在进行，没有重复启动。") });
            if (reserved && monitorTask is { IsCompleted: false }) return Task.FromResult(Occupied());
            starting = true;
            var task = StartCoreAsync(gameId, explicitInstallation, cancellation); startTask = task; return task;
        }
    }

    private async Task<LaunchStatus> StartCoreAsync(string gameId, string? explicitInstallation, CancellationToken cancellation)
    {
        IObservedProcess? process = null;
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellation, lifetime.Token);
        LaunchSnapshot? snapshot = null;
        var clock = Stopwatch.StartNew();
        try
        {
            if (reserved)
            {
                ProcessPresence presence;
                try { presence = await Task.Run(RevalidateReservation, stop.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) { return Status with { Outcome = LaunchOutcome.Cancelled, Message = UiText.Get("已取消本次启动请求，原进程状态未改变。") }; }
                if (presence != ProcessPresence.Inactive) return Occupied(presence == ProcessPresence.Unknown);
                lock (gate) { lifetimeScope?.Dispose(); lifetimeScope = null; externalReservation = []; reserved = false; }
                // Releasing admission does not manufacture an exit event/session boundary.
            }
            var preparation = await PrepareAsync(gameId, explicitInstallation, stop.Token).ConfigureAwait(false);
            snapshot = preparation.Snapshot;
            if (preparation.Outcome != LaunchOutcome.Ready || snapshot is null)
            { var rejected = new LaunchStatus(preparation.Outcome, ProcessTrackingState.None, snapshot, null, null, preparation.Message); Publish(rejected); return rejected; }
            Publish(new(LaunchOutcome.Starting, ProcessTrackingState.Attaching, snapshot, null, null, UiText.Get("正在启动…")));
            var existing = await Task.Run(() => platform.FindExisting(snapshot.Executable, options.MaximumProcesses), stop.Token).ConfigureAwait(false);
            if (!existing.IsEmpty)
            {
                var already = new LaunchStatus(LaunchOutcome.AlreadyRunning, ProcessTrackingState.RunningLimited, snapshot, existing[0], existing[0], UiText.Get("同一启动文件已有进程运行，没有再次启动；跨重启会话恢复尚未启用。"));
                lock (gate) { externalReservation = existing; reserved = true; }
                Publish(already); return already;
            }
            process = await library.WithLaunchAdmissionAsync(snapshot.Game, snapshot.Installation.Id, () => {
                IObservedProcess Create() { clock.Restart(); return platform.Start(snapshot, stop.Token); }
                return snapshot.PolicyRevision is { } revision ? effectivePolicy.Admit(revision, Create) : Create();
            }, stop.Token).ConfigureAwait(false);
            // From here onward every failure describes successful creation with limited observation.
            lock (gate) reserved = true;
            lifetimeScope = process.RetainLifetimeScope();
            ProcessObservation first;
            try { first = process.Query(); } catch { first = new(null, ProcessTrackingState.Unknown); }
            var identity = first.Identity;
            var created = new LaunchStatus(identity is null ? LaunchOutcome.StartedButTrackingLimited : LaunchOutcome.Started,
                identity is null ? ProcessTrackingState.RunningLimited : ProcessTrackingState.Attaching, snapshot,
                identity, identity, (identity is null ? UiText.Get("程序已经启动，但无法取得完整进程身份；请勿重复启动。") : UiText.Get("程序已经启动，正在确认进程连续性。")) + (process.AuxiliaryNotice is { } warning ? " " + warning : ""), process.StartTimestamp is { } timestamp ? Stopwatch.GetElapsedTime(timestamp).TotalMilliseconds : clock.Elapsed.TotalMilliseconds);
            Publish(created, ProcessEventKind.ProcessStarted);
            var owned = process; process = null;
            monitorTask = Task.Run(() => MonitorAsync(owned, created, cancellation));
            return created;
        }
        catch (Exception error)
        {
            if (process is not null)
            {
                process.Dispose(); lock (gate) reserved = true;
                var limited = new LaunchStatus(LaunchOutcome.StartedButTrackingLimited, ProcessTrackingState.Unknown, snapshot, null, null, UiText.Get("程序已创建，追踪未完成；请勿重复启动。"));
                Publish(limited, ProcessEventKind.TrackingLimited); return limited;
            }
            var outcome = error is OperationCanceledException ? LaunchOutcome.Cancelled : error is LaunchValidationException validation ? validation.Outcome : error is UnauthorizedAccessException ? LaunchOutcome.AccessDenied : LaunchOutcome.LaunchFailed;
            var failure = new LaunchStatus(outcome, ProcessTrackingState.None, snapshot, null, null, error is LaunchValidationException known ? known.Message : outcome == LaunchOutcome.Cancelled ? UiText.Get("创建进程前已取消。") : UiText.Get("未创建游戏进程，请检查启动位置、权限和运行环境。"));
            Publish(failure); return failure;
        }
        finally { lock (gate) starting = false; }
    }

    private async Task MonitorAsync(IObservedProcess initial, LaunchStatus created, CancellationToken requestedCancellation)
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token, requestedCancellation);
        var owned = new List<IObservedProcess> { initial };
        var clock = Stopwatch.StartNew(); var current = initial;
        bool hadWindow = false, adoptedWhileLauncherRunning = false, ambiguous = false, incompleteTree = false;
        var unresolvedAttachments = new HashSet<ProcessIdentity>();
        var scopedIdentities = new HashSet<ProcessIdentity>();
        bool commandLaunch = string.Equals(Path.GetExtension(created.Snapshot!.Executable.FinalPath), ".cmd", StringComparison.OrdinalIgnoreCase);
        double? exitedAt = null, candidateSince = null;
        ProcessIdentity? candidateIdentity = null;
        uint? authoritativeScopeGeneration = null;
        int treePolls = 0;
        try
        {
            while (true)
            {
                stop.Token.ThrowIfCancellationRequested();
                var observed = owned.Select(p => p.Query()).ToImmutableArray();
                var root = observed[owned.IndexOf(current)];
                hadWindow |= root.HasWindow;
                var membership = lifetimeScope?.Query();
                RememberScope(membership);
                // A visible tracked process is authoritative only after the kernel scope
                // proves it is the sole survivor. Later births invalidate this checkpoint.
                if (ScopeCheckpoint(membership, root) is { } checkpoint)
                    authoritativeScopeGeneration = checkpoint;
                if (root.State is ProcessTrackingState.Unknown or ProcessTrackingState.RunningLimited || root.Identity is null)
                { Limited(UiText.Get("程序已经启动，但无法可靠查询进程；没有把查询失败当作退出。")); return; }
                bool exited = root.State == ProcessTrackingState.Exited;
                if (exited) exitedAt ??= clock.Elapsed.TotalMilliseconds;
                // Some EXE launchers wait headlessly for the game, just like a synchronous
                // command script. Start looking while both lifetimes are still observable.
                bool waitingLauncher = current == initial && !exited && (commandLaunch || !hadWindow);
                bool uncoveredMember = waitingLauncher && membership is { Complete: true }
                    && membership.ActivePids.Any(pid => owned.All(p => p.Pid != pid));
                bool observeTree = clock.Elapsed < options.InitialTreeWindow || exited || uncoveredMember
                    || waitingLauncher && commandLaunch;
                if (observeTree && treePolls++ < options.MaximumTreePolls)
                {
                    var tree = platform.Descendants(observed, options.MaximumProcesses, options.MaximumCandidates);
                    incompleteTree |= !tree.Complete;
                    foreach (var item in tree.Candidates)
                    {
                        var id = item.Observation.Identity;
                        if (id is null || owned.Any(p => p.Pid == id.Pid)) continue;
                        if (owned.Count >= options.MaximumCandidates) { ambiguous = true; break; }
                        var attached = platform.Attach(id);
                        if (attached is not null) { owned.Add(attached); unresolvedAttachments.Remove(id); }
                        else if (item.Observation.HasWindow && LaunchSelection.IsContinuationExecutable(Path.GetFileName(id.ImagePath))
                            && InstallationPreflightService.Within(id.ImagePath, created.Snapshot!.Folder.FinalPath)
                            || unresolvedAttachments.Count >= options.MaximumCandidates) ambiguous = true;
                        else unresolvedAttachments.Add(id);
                    }
                    // A raced enumeration may recover when a later verified snapshot
                    // accounts for every currently live member of the kernel-owned scope.
                    var coverage = lifetimeScope?.Query();
                    RememberScope(coverage);
                    if (tree.Complete && coverage is { Complete: true } && coverage.ActivePids.All(pid => owned.Any(p => p.Pid == pid)))
                    {
                        incompleteTree = false;
                        // A non-game helper may disappear between enumeration and Attach. Recover
                        // only after its exact generation is proved inactive and all live kernel
                        // members are covered. Unknown/access-denied helpers and competing game
                        // windows remain unresolved; neither absence nor a timeout is exit proof.
                        unresolvedAttachments.RemoveWhere(id => platform.RevalidateExternal(id) == ProcessPresence.Inactive);
                    }
                }
                else if (observeTree) ambiguous = true;
                if (exited || waitingLauncher)
                {
                    if (exited && Status.Tracking != ProcessTrackingState.ObservingHandoff)
                        Publish(Status with { Tracking = ProcessTrackingState.ObservingHandoff, Message = UiText.Get("入口进程已退出，正在核对可能的游戏子进程。") });
                    var candidates = owned.Where(p => p != current).Select(p => (Process: p, Observation: p.Query()))
                        .Where(p => p.Observation.State == ProcessTrackingState.RunningConfirmed && p.Observation.Identity is { } id
                            && p.Observation.HasWindow && LaunchSelection.IsContinuationExecutable(Path.GetFileName(id.ImagePath))
                            && InstallationPreflightService.Within(id.ImagePath, created.Snapshot!.Folder.FinalPath)).ToArray();
                    if (candidates.Length > 1) ambiguous = true;
                    var handoffScope = lifetimeScope?.Query();
                    RememberScope(handoffScope);
                    bool launcherScopeCovered = !waitingLauncher || handoffScope is { Complete: true }
                        && handoffScope.ActivePids.Contains(candidates.Length == 1 ? candidates[0].Process.Pid : -1)
                        && handoffScope.ActivePids.All(pid => owned.Any(p => p.Pid == pid && p.Query().State == ProcessTrackingState.RunningConfirmed));
                    if (!ambiguous && !incompleteTree && unresolvedAttachments.Count == 0 && candidates.Length == 1 && launcherScopeCovered)
                    {
                        var next = candidates[0];
                        if (candidateIdentity != next.Observation.Identity) { candidateIdentity = next.Observation.Identity; candidateSince = clock.Elapsed.TotalMilliseconds; }
                        if (clock.Elapsed.TotalMilliseconds - candidateSince >= options.CandidateSettle.TotalMilliseconds)
                        {
                            var handoffMs = clock.Elapsed.TotalMilliseconds - (exitedAt ?? 0);
                            var previous = Status.Current; current = next.Process; hadWindow = true; exitedAt = null; candidateSince = null;
                            adoptedWhileLauncherRunning |= waitingLauncher;
                            authoritativeScopeGeneration = ScopeCheckpoint(handoffScope, next.Observation);
                            Publish(Status with { Current = next.Observation.Identity, Tracking = ProcessTrackingState.RunningConfirmed, AttachMilliseconds = created.StartMilliseconds + clock.Elapsed.TotalMilliseconds, HandoffMilliseconds = handoffMs,
                                Message = UiText.Get("已根据父子关系、创建时间和唯一游戏窗口接续追踪。") }, ProcessEventKind.ProcessHandoff, previous);
                        }
                    }
                    else { candidateIdentity = null; candidateSince = null; }
                    if (exitedAt is { } ended && clock.Elapsed.TotalMilliseconds - ended >= options.HandoffWindow.TotalMilliseconds)
                    {
                        bool anyUnresolved = ambiguous || incompleteTree || unresolvedAttachments.Count != 0 || owned.Where(p => p != current).Any(p => p.Query().State != ProcessTrackingState.Exited);
                        var lifetimeEvidence = lifetimeScope?.Query();
                        // Snapshot completeness is only a successful enumeration. It is NOT
                        // proof that all short-lived intermediaries were ever observed.
                        bool endedCompletely = lifetimeEvidence is { Complete: true, ActiveProcesses: 0 }
                            && authoritativeScopeGeneration is { } completeGeneration && lifetimeEvidence.TotalProcesses == completeGeneration;
                        var actualExit = root.ExitFileTime is { } time ? FileTime(time) : null;
                        if (hadWindow && !anyUnresolved && endedCompletely && actualExit is not null)
                        {
                            Publish(Status with { Tracking = ProcessTrackingState.Exited, ExitOccurredAtUtc = actualExit, Message = UiText.Get("已确认所追踪的游戏进程退出，所属进程范围已结束。") }, ProcessEventKind.ProcessExited);
                            lock (gate) { lifetimeScope?.Dispose(); lifetimeScope = null; reserved = false; }
                        }
                        else Limited(UiText.Get("入口已退出，但未能确认唯一游戏接续进程；程序曾成功创建，请勿重复启动。"));
                        return;
                    }
                }
                if (!exited && Status.Tracking != ProcessTrackingState.RunningConfirmed)
                    Publish(Status with { Current = root.Identity, Tracking = ProcessTrackingState.RunningConfirmed, AttachMilliseconds = created.StartMilliseconds + clock.Elapsed.TotalMilliseconds, Message = UiText.Get("游戏进程正在运行。") }, ProcessEventKind.ProcessTrackingAttached);
                await Task.Delay(observeTree ? options.PollInterval : options.RunningPollInterval, stop.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        { Publish(Status with { Outcome = LaunchOutcome.StartedButTrackingLimited, Tracking = ProcessTrackingState.Detached, Message = UiText.Get("观察已停止，已创建的游戏继续运行。") }, ProcessEventKind.Detached); }
        catch (Exception) { Limited(UiText.Get("程序已经启动，后续追踪受限；游戏未被结束，请勿重复启动。")); }
        finally { foreach (var process in owned) process.Dispose(); }
        void Limited(string message) => Publish(Status with { Outcome = LaunchOutcome.StartedButTrackingLimited, Tracking = ProcessTrackingState.RunningLimited, Message = message }, ProcessEventKind.TrackingLimited);
        void RememberScope(ProcessLifetimeObservation? scope)
        {
            if (scope is not { Complete: true }) return;
            foreach (var process in owned)
                if (scope.ActivePids.Contains(process.Pid) && process.Query() is { State: ProcessTrackingState.RunningConfirmed, Identity: { } id }
                    && scopedIdentities.Count < options.MaximumCandidates) scopedIdentities.Add(id);
        }
        uint? ScopeCheckpoint(ProcessLifetimeObservation? scope, ProcessObservation tracked)
        {
            if (tracked is not { State: ProcessTrackingState.RunningConfirmed, HasWindow: true, Identity: { } id }
                || scope is not { Complete: true } || !scope.ActivePids.Contains(id.Pid)) return null;
            if (scope.ActiveProcesses == 1 && scope.ActivePids.Length == 1) return scope.TotalProcesses;
            // A synchronous launcher can stay alive until its GUI child ends. In that
            // case every kernel-scope birth must have been observed through an exact lease,
            // and every live PID must still be covered. A missed short-lived birth prevents
            // this checkpoint; unchanged total births and zero live members are required later.
            return adoptedWhileLauncherRunning && scope.TotalProcesses == scopedIdentities.Count
                && scope.ActivePids.All(pid => owned.Any(p => p.Pid == pid && p.Query().State == ProcessTrackingState.RunningConfirmed))
                ? scope.TotalProcesses : null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        Task? start;
        lock (gate) { if (closing) return; closing = true; lifetime.Cancel(); start = startTask; }
        if (start is not null) await start.ConfigureAwait(false);
        if (monitorTask is not null) await monitorTask.ConfigureAwait(false);
        lifetimeScope?.Dispose(); lifetimeScope = null;
        // No Kill/TerminateProcess/job-object close action. Only observer handles are released.
    }
    private LaunchStatus Occupied(bool unknown = false) => Status with { Outcome = LaunchOutcome.AlreadyRunning,
        Message = unknown ? UiText.Get("不能可靠确认原进程范围已结束，未重复启动；可稍后重试核对。") : UiText.Get("已有游戏运行或追踪尚未确认结束，没有再次启动。") };
    private ProcessPresence RevalidateReservation()
    {
        try
        {
            if (!externalReservation.IsEmpty)
            {
                var states = externalReservation.Select(platform.RevalidateExternal).ToArray();
                return states.Contains(ProcessPresence.Active) ? ProcessPresence.Active : states.All(s => s == ProcessPresence.Inactive) ? ProcessPresence.Inactive : ProcessPresence.Unknown;
            }
            var scope = lifetimeScope?.Query();
            return scope is not { Complete: true } ? ProcessPresence.Unknown : scope.ActiveProcesses == 0 ? ProcessPresence.Inactive : ProcessPresence.Active;
        }
        catch { return ProcessPresence.Unknown; }
    }
    private static DateTimeOffset? FileTime(long value)
    { try { return value > 0 ? DateTimeOffset.FromFileTime(value).ToUniversalTime() : null; } catch (ArgumentOutOfRangeException) { return null; } }
}
