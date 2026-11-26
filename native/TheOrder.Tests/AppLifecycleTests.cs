using TheOrder.Application;
using TheOrder.Domain;
using TheOrder.Presentation;
using TheOrder.Infrastructure.Legacy;
using Xunit;

namespace TheOrder.Tests;

public sealed class AppLifecycleTests
{
    private static AppSessionHost Host(SessionTestRig rig) => new(() => Task.FromResult<SessionEngine?>(rig.Engine), () => rig.Library.Snapshot);

    [Fact]
    public async Task StartupCommandsWaitForExactlyOneRecoveryInitialization()
    {
        await using var rig = new SessionTestRig(); int creates = 0;
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var host = new AppSessionHost(async () => { Interlocked.Increment(ref creates); await release.Task; return rig.Engine; }, () => rig.Library.Snapshot);
        var preparation = host.PrepareAsync("g"); var initializes = Enumerable.Range(0, 20).Select(_ => host.InitializeAsync()).ToArray();
        Assert.False(host.Ready); Assert.False(preparation.IsCompleted); Assert.Equal(0, rig.Platform.Starts);
        release.SetResult(); await Task.WhenAll(initializes); Assert.NotNull((await preparation).Snapshot);
        Assert.Equal(1, creates); Assert.True(host.Ready);
    }
    [Theory]
    [InlineData(false, false)] [InlineData(false, true)] [InlineData(true, false)] [InlineData(true, true)]
    public async Task ExplicitExitPreservesSameRecoverySessionAndPauseWithoutOfflineGap(bool recovered, bool paused)
    {
        await using var rig = new SessionTestRig(); await rig.Start(); await rig.Engine.SampleAsync(); await rig.Advance(8);
        if (recovered) await rig.Reopen();
        if (paused) await rig.Engine.PauseAsync();
        await using var host = Host(rig); await host.InitializeAsync();
        var id = rig.Stored.Id; var count = rig.Library.Snapshot.Entries[0].PlayCount;
        Assert.True(await host.PrepareExitAsync());
        Assert.Equal(SessionLifecycle.RecoveryPending, rig.Native.Lifecycle); Assert.Null(rig.Native.EndedAtUtc);
        var seconds = rig.Stored.Seconds; var writes = rig.Disk.Writes;
        Assert.True(await host.PrepareExitAsync()); Assert.Equal(writes, rig.Disk.Writes);
        Assert.Equal(LaunchOutcome.Closed, (await host.LaunchAsync("g")).Outcome);
        await host.DisposeAsync(); Assert.Equal(ProcessTrackingState.RunningConfirmed, rig.Platform.Processes[71].State);
        rig.Clock.Advance(3600); await rig.Reopen();
        Assert.Equal(id, rig.Stored.Id); Assert.Equal(count, rig.Library.Snapshot.Entries[0].PlayCount);
        Assert.Equal(seconds, rig.Stored.Seconds); Assert.Equal(paused, rig.Native.UserPaused);
        Assert.Equal(SessionTracking.CurrentProcessOnly, rig.Engine.Snapshot.Tracking);
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task ManualExitSettlesConfirmedTimeAndDoesNotResumeOnReopen(bool paused)
    {
        await using var rig = new SessionTestRig();
        await rig.Start();
        await rig.Engine.SampleAsync();
        await rig.Advance(8);
        if (paused) await rig.Engine.PauseAsync();
        rig.Clock.Advance(2);

        await using var host = Host(rig);
        Assert.True(await host.PrepareManualExitAsync());
        var expected = paused ? 8 : 10;
        var final = rig.Stored;
        Assert.Equal(expected, final.Seconds);
        Assert.Equal(SessionLifecycle.Incomplete, final.Native!.Lifecycle);
        Assert.Equal(SessionReason.ManagerExit, final.Native.Reason);
        Assert.Equal(rig.Clock.Utc, final.Native.AccountingCutoffAtUtc);
        Assert.Null(final.Native.RecoveryToken);
        Assert.Null(final.Native.EndedAtUtc);
        Assert.Equal(SessionTiming.Stopped, host.Session!.Timing);
        var writes = rig.Disk.Writes;
        Assert.True(await host.PrepareManualExitAsync());
        Assert.Equal(writes, rig.Disk.Writes);
        Assert.Equal(LaunchOutcome.Closed, (await host.LaunchAsync("g")).Outcome);
        await host.DisposeAsync();
        Assert.Equal(ProcessTrackingState.RunningConfirmed, rig.Platform.Processes[71].State);

        rig.Clock.Advance(3600);
        await rig.Reopen();
        await rig.Engine.SampleAsync();
        Assert.Null(rig.Engine.Snapshot.Session);
        Assert.Equal(final, rig.Stored);
        Assert.Equal(1000 + expected, rig.Library.Snapshot.Entries[0].TotalPlaySeconds);
        Assert.Equal(6, rig.Library.Snapshot.Entries[0].PlayCount);
    }

    [Fact]
    public async Task ManualExitRetriesTheSameCutoffAfterSaveFailure()
    {
        await using var rig = new SessionTestRig();
        await rig.Start();
        await rig.Engine.SampleAsync();
        await rig.Advance(8);
        await using var host = Host(rig);

        var previous = rig.Stored;
        rig.Disk.Fail = true;
        Assert.False(await host.PrepareManualExitAsync());
        Assert.False(host.Closing);
        Assert.Equal(previous, rig.Stored);
        var pending = host.Session!.Session!;
        Assert.Equal(SessionReason.ManagerExit, pending.Native!.Reason);
        rig.Clock.Advance(15);
        rig.Disk.Fail = false;

        Assert.True(await host.PrepareManualExitAsync());
        Assert.Equal(pending.Seconds, rig.Stored.Seconds);
        Assert.Equal(pending.Native.AccountingCutoffAtUtc, rig.Native.AccountingCutoffAtUtc);
        Assert.Equal(6, rig.Library.Snapshot.Entries[0].PlayCount);
        Assert.Equal(ProcessTrackingState.RunningConfirmed, rig.Platform.Processes[71].State);
    }

    [Fact]
    public async Task FailedSaveCancelsExitAndRetryDrainsWithoutKill()
    {
        await using var rig = new SessionTestRig(); await rig.Start(); await using var host = Host(rig); await host.InitializeAsync();
        rig.Disk.Fail = true; Assert.False(await host.PrepareExitAsync());
        Assert.False(host.Closing); Assert.NotNull(host.Error); Assert.Equal(1, rig.Platform.Starts);
        rig.Disk.Fail = false; Assert.True(await host.PrepareExitAsync());
        Assert.Equal(SessionLifecycle.RecoveryPending, rig.Native.Lifecycle); Assert.Equal(6, rig.Library.Snapshot.Entries[0].PlayCount);
        Assert.Equal(ProcessTrackingState.RunningConfirmed, rig.Platform.Processes[71].State);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExitAfterTrackingLossPreservesFinalizedSession(bool failFirstSave)
    {
        await using var rig = new SessionTestRig();
        await rig.Start();
        await rig.Engine.SampleAsync();
        await rig.Advance(4);
        await using var host = Host(rig);
        await host.InitializeAsync();

        var cutoff = rig.Clock.Utc;
        var previous = rig.Stored;
        var writes = rig.Disk.Writes;
        rig.Disk.Fail = failFirstSave;
        // Advance wall time during persistence so a second sync cannot reuse the original cutoff.
        rig.Disk.BeforeSave = () => { rig.Clock.Advance(.05); return Task.CompletedTask; };
        rig.Platform.Processes[71].State = ProcessTrackingState.Exited;
        await rig.Engine.ObserveFactAsync(rig.Fact(ProcessEventKind.TrackingLimited));

        var finalized = host.Session!.Session!;
        Assert.Equal(SessionLifecycle.Incomplete, finalized.Native!.Lifecycle);
        Assert.Equal(SessionReason.ProcessGone, finalized.Native.Reason);
        Assert.Equal(cutoff, finalized.Native.AccountingCutoffAtUtc);
        if (failFirstSave)
        {
            Assert.Equal(previous, rig.Stored);
            Assert.False(await host.PrepareExitAsync());
            Assert.Equal(finalized, host.Session!.Session);
            Assert.Equal(writes, rig.Disk.Writes);
            rig.Disk.Fail = false;
        }
        else
        {
            Assert.Equal(rig.Stored, finalized);
            Assert.Null(host.Error);
        }

        Assert.True(await host.PrepareExitAsync());
        await host.CheckpointAsync();
        Assert.Null(host.Error);
        Assert.Equal(writes + 1, rig.Disk.Writes);
        Assert.Equal(cutoff, rig.Native.AccountingCutoffAtUtc);
        Assert.Equal(previous.Id, rig.Stored.Id);
        Assert.Equal(4, rig.Stored.Seconds);
        Assert.Equal(1004, rig.Library.Snapshot.Entries[0].TotalPlaySeconds);
        Assert.Equal(6, rig.Library.Snapshot.Entries[0].PlayCount);
    }

    [Fact]
    public async Task IdleReadOnlyOrFailedStartupCanExitWithoutCreatingSession()
    {
        await using var empty = new AppSessionHost(() => Task.FromResult<SessionEngine?>(null), () => null);
        Assert.True(await empty.PrepareExitAsync()); Assert.Null(empty.Session);
        await using var failed = new AppSessionHost(() => throw new IOException("unreadable"), () => null);
        Assert.True(await failed.PrepareExitAsync()); Assert.Null(failed.Session);
        await using var rig = new SessionTestRig(); await using var host = Host(rig);
        Assert.True(await host.PrepareExitAsync()); Assert.Empty(rig.Library.Snapshot.Entries[0].Sessions);
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task CancelledShutdownRestoresSameInvocationAndPause(bool pause)
    {
        await using var rig = new SessionTestRig(); await rig.Start(); if (pause) await rig.Engine.PauseAsync();
        await using var host = Host(rig); await host.InitializeAsync(); var id = rig.Stored.Id;
        Assert.True(await host.PrepareSystemEndAsync(TimeSpan.FromSeconds(1)));
        Assert.Equal(SessionReason.SystemShutdown, rig.Native.Reason);
        rig.Clock.Advance(100); await host.CancelSystemEndAsync(); await rig.Engine.SampleAsync();
        Assert.False(host.Closing); Assert.Equal(SessionLifecycle.Open, rig.Native.Lifecycle); Assert.Equal(pause, rig.Native.UserPaused);
        Assert.Equal(id, rig.Stored.Id); Assert.Equal(0, rig.Stored.Seconds); Assert.Equal(6, rig.Library.Snapshot.Entries[0].PlayCount);
    }
    [Fact]
    public async Task SystemDeadlineAndDuplicateRequestDoNotCancelAtomicSaveOrClaimCompleted()
    {
        await using var rig = new SessionTestRig(); await rig.Start(); await using var host = Host(rig); await host.InitializeAsync();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        rig.Disk.BeforeSave = async () => { entered.TrySetResult(); await release.Task; };
        var first = host.PrepareSystemEndAsync(TimeSpan.FromMilliseconds(25)); await entered.Task;
        var second = host.PrepareSystemEndAsync(TimeSpan.FromMilliseconds(25));
        try { Assert.False(await first.WaitAsync(TimeSpan.FromSeconds(3))); Assert.False(await second); Assert.True(host.Closing); }
        finally { release.TrySetResult(); }
        Assert.True(await host.PrepareExitAsync()); Assert.Equal(SessionLifecycle.RecoveryPending, rig.Native.Lifecycle);
        Assert.Null(rig.Native.EndedAtUtc); Assert.Equal(ProcessTrackingState.RunningConfirmed, rig.Platform.Processes[71].State);
    }
    [Theory]
    [InlineData(false, false)] [InlineData(true, false)] [InlineData(false, true)]
    public async Task CloseRestoreFiftyRoundsNeverOwnsEngineOrChangesPause(bool pause, bool recovered)
    {
        await using var rig = new SessionTestRig(); await rig.Start(); if (recovered) await rig.Reopen(); if (pause) await rig.Engine.PauseAsync();
        await using var host = Host(rig); await host.InitializeAsync(); var id = rig.Stored.Id;
        var window = new WindowLifetime { TrayAvailable = true };
        for (int i = 0; i < 50; i++) { Assert.True(window.CloseToTray()); Assert.True(window.Hidden); await host.CheckpointAsync(); window.Show(); Assert.False(window.Hidden); }
        Assert.Equal(id, rig.Stored.Id); Assert.Equal(pause, rig.Native.UserPaused); Assert.True(host.CanMutate);
        Assert.Equal(6, rig.Library.Snapshot.Entries[0].PlayCount); Assert.Equal(1, rig.Platform.Starts);
        window.TrayAvailable = false; Assert.False(window.CloseToTray()); Assert.False(window.Hidden); Assert.NotNull(window.Error);
    }
    [Fact]
    public async Task RefreshCannotReplaceApplicationOwnedWriterOrLoseSession()
    {
        await using var rig = new SessionTestRig(); var factory = new CountingFactory(rig);
        await using var client = new OfflineLibraryClient(factory, new LegacyClientTextRules());
        await client.LoadAsync(default);
        await using var engine = client.CreateSessionEngine(rig.Files, rig.Platform, rig.Positions, rig.Clock, rig.Tokens);
        await using var host = new AppSessionHost(() => Task.FromResult<SessionEngine?>(engine), () => client.CurrentSnapshot);
        await host.InitializeAsync(); var preview = await host.PrepareAsync("g"); await host.ConfirmPositionAsync(preview.Snapshot!);
        Assert.True((await host.LaunchAsync("g")).ProcessCreated);
        var id = engine.Snapshot.Session!.Id;
        for (int i = 0; i < 30; i++) await client.LoadAsync(default);
        Assert.Equal(1, factory.Opens); Assert.Equal(id, Assert.Single(client.CurrentSnapshot!.Entries[0].Sessions).Id);
        Assert.Equal(6, client.CurrentSnapshot.Entries[0].PlayCount);
        Assert.Equal(LaunchOutcome.AlreadyRunning, (await host.LaunchAsync("g")).Outcome);
    }
    private sealed class CountingFactory(SessionTestRig rig) : ITransactionalLibraryFactory
    {
        public int Opens;
        public Task<TransactionalLibrary> OpenAsync(CancellationToken cancellation) { Opens++; return Task.FromResult(rig.Open()); }
    }
    [Fact]
    public async Task FailedSystemCheckpointKeepsLiveSessionAndMayBeRetried()
    {
        await using var rig = new SessionTestRig(); await rig.Start(); await using var host = Host(rig); await host.InitializeAsync();
        rig.Disk.Fail = true; Assert.False(await host.PrepareSystemEndAsync(TimeSpan.FromSeconds(1)));
        Assert.False(host.Closing); Assert.NotNull(host.Error); Assert.Null(rig.Native.EndedAtUtc);
        rig.Disk.Fail = false; await host.CancelSystemEndAsync();
        Assert.Equal(SessionLifecycle.Open, rig.Native.Lifecycle);
        Assert.True(await host.PrepareSystemEndAsync(TimeSpan.FromSeconds(1)));
        Assert.Equal(SessionReason.SystemShutdown, rig.Native.Reason); Assert.Equal(6, rig.Library.Snapshot.Entries[0].PlayCount);
    }
    [Fact]
    public async Task PendingDurableWriteMustDrainBeforeExitAndRejectNewLaunch()
    {
        await using var rig = new SessionTestRig(); await rig.Start(); await using var host = Host(rig); await host.InitializeAsync();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        rig.Disk.BeforeSave = async () => { entered.TrySetResult(); await release.Task; };
        var pause = host.PauseAsync(); await entered.Task;
        var exit = host.PrepareExitAsync();
        try { Assert.False(exit.IsCompleted); Assert.Equal(LaunchOutcome.Closed, (await host.LaunchAsync("g")).Outcome); }
        finally { release.TrySetResult(); }
        await pause; Assert.True(await exit); Assert.True(rig.Native.UserPaused);
        Assert.Equal(SessionLifecycle.RecoveryPending, rig.Native.Lifecycle); Assert.Equal(1, rig.Platform.Starts);
    }
    [Theory]
    [InlineData(SessionLifecycle.Open, SessionTracking.LaunchScope, false, "正在游玩")]
    [InlineData(SessionLifecycle.Open, SessionTracking.LaunchScope, true, "计时已暂停")]
    [InlineData(SessionLifecycle.Open, SessionTracking.CurrentProcessOnly, false, "追踪受限")]
    [InlineData(SessionLifecycle.Open, SessionTracking.Unknown, false, "状态未确认")]
    [InlineData(SessionLifecycle.Incomplete, SessionTracking.Detached, false, "未能确认完整结束")]
    [InlineData(SessionLifecycle.RecoveryPending, SessionTracking.Detached, false, "等待下次恢复")]
    [InlineData(SessionLifecycle.Completed, SessionTracking.LaunchScope, false, "已确认游戏退出")]
    public async Task PresentationUsesAccountingSnapshotWithoutConflatingExit(SessionLifecycle life, SessionTracking tracking, bool paused, string expected)
    {
        await using var rig = new SessionTestRig(); await rig.Start();
        var session = (rig.Native with
        {
            Lifecycle = life, UserPaused = paused,
            Reason = life switch { SessionLifecycle.Completed => SessionReason.ExitConfirmed, SessionLifecycle.Incomplete => SessionReason.UserStoppedObservation, SessionLifecycle.RecoveryPending => SessionReason.ManagerExit, _ => null },
            EndedAtUtc = life == SessionLifecycle.Completed ? rig.Clock.Utc : null,
            RecoveryToken = life is SessionLifecycle.Completed or SessionLifecycle.Incomplete ? null : rig.Native.RecoveryToken
        }).Project(rig.Stored.Id);
        var value = SessionPresentation.From(new(session, tracking, SessionTiming.Running, true, 0, rig.Platform.Root, null), rig.Library.Snapshot, true);
        Assert.Contains(expected, value.State); Assert.Contains("0:00:00", value.Duration); Assert.Contains("6 次", value.History);
        Assert.Equal(life == SessionLifecycle.Open && !paused, value.CanPause);
        Assert.Equal(life == SessionLifecycle.Open && paused, value.CanResume);
    }
    [Fact]
    public async Task PresentationCommandsPauseGapAndResumeDoNotCreateAnotherInvocation()
    {
        await using var rig = new SessionTestRig(); await rig.Start(); await using var host = Host(rig); await host.InitializeAsync();
        await rig.Engine.SampleAsync(); await rig.Advance(5); await host.PauseAsync();
        await rig.Advance(80); await host.ResumeAsync(); await rig.Engine.SampleAsync(); await rig.Advance(4); await host.CheckpointAsync();
        Assert.Equal(9, rig.Stored.Seconds); Assert.Equal(6, rig.Library.Snapshot.Entries[0].PlayCount); Assert.Equal(1, rig.Platform.Starts);
    }
}
