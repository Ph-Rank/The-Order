using TheOrder.Application;
using TheOrder.Domain;
using Xunit;

namespace TheOrder.Tests;

public sealed class SessionRecordingMinimumTests
{
    [Theory]
    [InlineData(299.999, false)]
    [InlineData(300, true)]
    [InlineData(301, true)]
    public async Task NewSessionsEnterHistoryOnlyAtMinimumConfirmedDuration(double seconds, bool recorded)
    {
        var start = DateTimeOffset.Parse(SessionTestRig.Date).AddDays(-1);
        var historic = new PlaySession("old", NativeSession.Format(start), NativeSession.Format(start.AddSeconds(45)), 45);
        var entry = SessionTestRig.Entry() with { Sessions = [historic], LastPlayedAt = historic.StartedAt };
        await using var rig = new SessionTestRig(SessionTestRig.Codec.Encode([entry], SessionTestRig.Date), requireRecordingMinimum: true);
        await rig.Start();
        await Accrue(rig, 180);
        var pending = rig.Library.Snapshot.Entries[0];
        Assert.Equal(5, pending.PlayCount);
        Assert.Equal(1000, pending.TotalPlaySeconds);
        Assert.Equal(historic.StartedAt, pending.LastPlayedAt);
        Assert.Equal(historic, Assert.Single(pending.RecordedSessions));
        var portable = Assert.Single(SessionTestRig.Codec.Decode(await rig.Library.ExportPortableAsync()));
        Assert.Equal(historic, Assert.Single(portable.Sessions));

        await Accrue(rig, seconds - 180);
        var end = rig.Fact(ProcessEventKind.ProcessExited, exit: rig.Clock.Utc);
        await rig.Engine.ObserveFactAsync(end);
        await rig.Engine.ObserveFactAsync(end);
        var final = Assert.Single(SessionTestRig.Codec.Decode(rig.Disk.Main!));
        Assert.Equal(recorded ? 6 : 5, final.PlayCount);
        Assert.Equal(1000 + (recorded ? (long)seconds : 0), final.TotalPlaySeconds);
        Assert.Equal(recorded ? 2 : 1, final.Sessions.Length);
        Assert.Equal(historic, final.Sessions[0]);
        Assert.DoesNotContain(final.Sessions, s => s.Native?.AwaitingRecordingMinimum == true);
        if (recorded) Assert.Equal(SessionReason.ExitConfirmed, final.Sessions[1].Native!.Reason);
        else Assert.Equal(historic.StartedAt, final.LastPlayedAt);
    }

    [Theory]
    [InlineData(299, false)]
    [InlineData(300, true)]
    public async Task ManualExitSettlesOnlySessionsThatReachedTheMinimum(int seconds, bool recorded)
    {
        await using var rig = new SessionTestRig(requireRecordingMinimum: true);
        await rig.Start();
        await Accrue(rig, seconds - 1);
        await rig.Engine.SampleAsync();
        rig.Clock.Advance(1);
        await using var host = new AppSessionHost(() => Task.FromResult<SessionEngine?>(rig.Engine), () => rig.Library.Snapshot);

        Assert.True(await host.PrepareManualExitAsync());
        var entry = Assert.Single(rig.Library.Snapshot.Entries);
        Assert.Equal(recorded ? 6 : 5, entry.PlayCount);
        Assert.Equal(1000 + (recorded ? seconds : 0), entry.TotalPlaySeconds);
        if (recorded)
            Assert.Equal(SessionReason.ManagerExit, Assert.Single(entry.RecordedSessions).Native!.Reason);
        else
            Assert.Empty(entry.Sessions);
        Assert.Equal(ProcessTrackingState.RunningConfirmed, rig.Platform.Processes[71].State);
    }

    [Fact]
    public async Task PendingCheckpointRecoversAndPauseTimeDoesNotQualify()
    {
        await using var rig = new SessionTestRig(requireRecordingMinimum: true);
        await rig.Start();
        await Accrue(rig, 250);
        await rig.Engine.PauseAsync();
        Assert.True(await rig.Engine.CheckpointForExitAsync());
        await rig.Reopen();
        Assert.Equal(250, rig.Stored.Seconds);
        Assert.True(rig.Native.AwaitingRecordingMinimum);
        await rig.Advance(300);
        await rig.Engine.ResumeAsync();
        await Accrue(rig, 50);
        await rig.Engine.StopObservingAsync();
        Assert.Equal(300, rig.Stored.Seconds);
        Assert.Equal(1300, rig.Library.Snapshot.Entries[0].TotalPlaySeconds);
        Assert.Equal(6, rig.Library.Snapshot.Entries[0].PlayCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DiscardCanRecoverFromFailedWriteWithoutRecordingOrLeakingAdmission(bool firstWriteFailed)
    {
        await using var rig = new SessionTestRig(requireRecordingMinimum: true);
        rig.Disk.Fail = firstWriteFailed;
        await rig.Start();
        if (!firstWriteFailed) await Accrue(rig, 100);
        rig.Disk.Fail = true;
        await rig.Engine.ObserveFactAsync(rig.Fact(ProcessEventKind.ProcessExited, exit: rig.Clock.Utc));
        Assert.Empty(rig.Library.Snapshot.Entries[0].RecordedSessions);
        if (!firstWriteFailed) Assert.NotNull(rig.Engine.Snapshot.Error);
        rig.Disk.Fail = false;
        await rig.Engine.CheckpointAsync();
        Assert.Null(rig.Engine.Snapshot.Error);
        Assert.Empty(rig.Library.Snapshot.Entries[0].Sessions);
        Assert.Equal(5, rig.Library.Snapshot.Entries[0].PlayCount);
        Assert.Equal(1000, rig.Library.Snapshot.Entries[0].TotalPlaySeconds);
        // Library admission is released, while the independently retained launch scope
        // still prevents a second live process.
        var identity = (await rig.Library.CaptureLaunchAsync("g")).Identity;
        Assert.True(await rig.Library.WithSessionLaunchAdmissionAsync(identity, "i", () => true, default));
        Assert.Equal(LaunchOutcome.AlreadyRunning, (await rig.Engine.LaunchAsync("g")).Outcome);
    }

    [Fact]
    public async Task RecoveryWithoutTheProcessRemovesOnlyTheNewProvisionalRecord()
    {
        await using var rig = new SessionTestRig(requireRecordingMinimum: true);
        await rig.Start();
        await Accrue(rig, 115);
        Assert.True(await rig.Engine.CheckpointForExitAsync());
        rig.Platform.Processes[71].State = ProcessTrackingState.Exited;
        await rig.Reopen();
        Assert.Empty(rig.Library.Snapshot.Entries[0].Sessions);
        Assert.Equal(5, rig.Library.Snapshot.Entries[0].PlayCount);
        Assert.Equal(1000, rig.Library.Snapshot.Entries[0].TotalPlaySeconds);
    }

    private static async Task Accrue(SessionTestRig rig, double seconds)
    {
        while (seconds > 0)
        {
            await rig.Engine.SampleAsync(); // Reestablish the baseline after a durable write.
            double step = Math.Min(5, seconds);
            await rig.Advance(step);
            seconds -= step;
        }
    }
}
