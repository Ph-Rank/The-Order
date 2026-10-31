using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using TheOrder.Application;
using TheOrder.Platform.Windows;
using Xunit;

namespace TheOrder.Tests;

[Collection("Launch processes")]
public sealed class GuideLaunchTests
{
    [Fact] public async Task PendingGuideQueryCannotReopenLaunchAdmissionAfterExit()
    {
        await using var rig = new SessionTestRig();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var host = new AppSessionHost(() => Task.FromResult<SessionEngine?>(rig.Engine), () => rig.Library.Snapshot,
            async (_, _) => { entered.SetResult(); await release.Task; });
        var preparation = await host.PrepareAsync("g"); await host.ConfirmPositionAsync(preparation.Snapshot!);
        var launching = host.LaunchAsync("g"); await entered.Task;
        Assert.True(await host.PrepareExitAsync()); release.SetResult();
        Assert.Equal(LaunchOutcome.Closed, (await launching).Outcome); Assert.Equal(0, rig.Platform.Starts);
        Assert.Empty(rig.Library.Snapshot.Entries[0].Sessions);
    }
    [Fact] public async Task FailedOptionalGuideDoesNotTurnSuccessfulGameCreationIntoFailure()
    {
        await using var rig = new SessionTestRig();
        await using var host = new AppSessionHost(() => Task.FromResult<SessionEngine?>(rig.Engine), () => rig.Library.Snapshot,
            (_, _) => throw new OnlineException(OnlineFailure.Offline));
        var preparation = await host.PrepareAsync("g"); await host.ConfirmPositionAsync(preparation.Snapshot!);
        Assert.True((await host.LaunchAsync("g")).ProcessCreated); Assert.Equal(1, rig.Platform.Starts);
    }
    [Fact] public async Task CancelledGuidePreparationDoesNotCreateAGame()
    {
        await using var rig = new SessionTestRig(); using var cancel = new CancellationTokenSource();
        await using var host = new AppSessionHost(() => Task.FromResult<SessionEngine?>(rig.Engine), () => rig.Library.Snapshot,
            (_, token) => { cancel.Cancel(); token.ThrowIfCancellationRequested(); return Task.CompletedTask; });
        var preparation = await host.PrepareAsync("g"); await host.ConfirmPositionAsync(preparation.Snapshot!);
        Assert.Equal(LaunchOutcome.Cancelled, (await host.LaunchAsync("g", cancellation: cancel.Token)).Outcome);
        Assert.Equal(0, rig.Platform.Starts);
    }
    private sealed class FailingPlan : IGameGuideLaunch, IGameGuideLaunchPlan
    {
        public int Pid; public long Created;
        public IReadOnlyDictionary<string, string> Environment => new Dictionary<string, string> { ["THE_ORDER_GUIDE_SESSION"] = "synthetic-no-component" };
        public IGameGuideLaunchPlan Prepare(LaunchSnapshot snapshot) => this;
        public void Attach(int pid, long created) { Pid = pid; Created = created; throw new IOException("Synthetic adapter failure"); }
        public void FailedBeforeCreation() => throw new InvalidOperationException("A created game must not be called a precreation failure");
    }
    [Fact] public async Task FailingSuspendedCompanionAlwaysResumesExactOwnedGameAndPreservesDpiAndArguments()
    {
        await using var fixture = new LaunchFixture(); fixture.Set("game", ["two words", "\"quoted\""]);
        fixture.Library.Entry = fixture.Library.Entry with { Installations = [fixture.Library.Entry.Installations[0] with { Compatibility = new(HighDpi: true) }] };
        await fixture.Approve(); var prepared = await fixture.Service.PrepareAsync("g", null);
        var plan = new FailingPlan(); var platform = new WindowsGameProcesses(fixture.Identity, guides: plan);
        using var observed = platform.Start(prepared.Snapshot!, default);
        var report = await fixture.Report("run");
        Assert.Equal(observed.Pid, plan.Pid); Assert.Equal(observed.Pid, report.GetProperty("pid").GetInt32());
        using var process = Process.GetProcessById(observed.Pid); Assert.Equal(plan.Created, process.StartTime.ToFileTimeUtc());
        Assert.Equal("HIGHDPIAWARE", report.GetProperty("compatibilityLayer").GetString(), ignoreCase: true);
        Assert.Equal(new[] { "two words", "\"quoted\"" }, report.GetProperty("arguments").EnumerateArray().Select(a => a.GetString()));
        await fixture.Stop();
    }
    [Fact] public async Task ExistingGraphicsWrapperRemainsUntouchedAndDisablesNativeLoad()
    {
        await using var fixture = new LaunchFixture(); fixture.Set("game"); await fixture.Approve();
        var prepared = await fixture.Service.PrepareAsync("g", null); Assert.True(WindowsGuidePayload.AllowsNative(prepared.Snapshot!));
        string path = Path.Combine(fixture.Game, "dxgi.dll"); byte[] content = [1, 2, 3, 4]; File.WriteAllBytes(path, content);
        Assert.False(WindowsGuidePayload.AllowsNative(prepared.Snapshot!)); Assert.Equal(content, File.ReadAllBytes(path));
    }
    [Fact] public void InstalledCompanionDoesNotNeedASyntheticDataMarkerAndTamperingIsRejected()
    {
        string root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "the-order-guide-integrity-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(root);
        try
        {
            string[] names = ["x86/TheOrder.GuideHost.exe", "x64/TheOrder.GuideHost.exe", "x86/TheOrder.GuideRuntime.dll",
                "x64/TheOrder.GuideRuntime.dll", "TheOrder.Guide.addon32", "TheOrder.Guide.addon64"];
            foreach (string name in names) { string path = Path.Combine(root, name); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllBytes(path, [1, 2, 3]); }
            var files = names.Select(name => new { path = name, bytes = 3, sha256 = Convert.ToHexString(SHA256.HashData(new byte[] { 1, 2, 3 })) });
            File.WriteAllBytes(Path.Combine(root, "payload.json"), JsonSerializer.SerializeToUtf8Bytes(new { files }));
            Assert.False(File.Exists(Path.Combine(root, IsolatedLibrarySource.MarkerName)));
            using (var payload = new WindowsGuidePayload(root))
                Assert.ThrowsAny<IOException>(() => Directory.Move(root, root + "-moved"));
            File.WriteAllBytes(Path.Combine(root, names[0]), [1, 9, 3]);
            Assert.Throws<InvalidDataException>(() => new WindowsGuidePayload(root));
        }
        finally
        {
            if (!root.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException();
            Directory.Delete(root, recursive: true);
        }
    }
}
