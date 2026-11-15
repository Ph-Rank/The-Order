using System.Collections.Immutable;
using System.Diagnostics;
using System.Text.Json;
using TheOrder.Application;
using TheOrder.Domain;
using TheOrder.Platform.Windows;
using Xunit;
using Xunit.Abstractions;

namespace TheOrder.Tests;

[CollectionDefinition("Launch processes", DisableParallelization = true)]
public sealed class LaunchProcessCollection;

[Collection("Launch processes")]
public sealed class LaunchTests(ITestOutputHelper output)
{
    [Fact]
    public async Task RealDirectProcessPreservesArgumentArrayCwdIdentityAndNoPlayData()
    {
        await using var f = new LaunchFixture();
        string[] args = ["", "two spaces here", "日本語", "中文", "\"quoted\"", @"C:\a\b\", @"trailing\", "a\"b", "$(literal)", @"D:\with space\ゲーム.exe"];
        f.Set("game", args);
        await f.Approve();
        var observedPlatform = new WindowObservationPlatform(f.Platform);
        await using var service = new GameLaunchService(f.Library, f.Identity, observedPlatform, f.Store,
            new() { HandoffWindow = TimeSpan.FromSeconds(2) });
        var launched = await service.LaunchAsync("g");
        Assert.Equal(LaunchOutcome.Started, launched.Outcome);
        await f.Wait(() => service.Status.Tracking == ProcessTrackingState.RunningConfirmed);
        var report = await f.Report("run");
        Assert.Equal(args, report.GetProperty("arguments").EnumerateArray().Select(v => v.GetString()));
        Assert.Equal(Path.GetFullPath(f.Game), Path.GetFullPath(report.GetProperty("cwd").GetString()!));
        Assert.True(launched.Initial!.CreationFileTime > 0);
        Assert.Equal(report.GetProperty("pid").GetInt32(), launched.Initial.Pid);
        Assert.Equal(f.Identity.Inspect(f.Exe, false).Observation!.Identity, launched.Initial.ImageIdentity);
        Assert.Equal(LaunchOutcome.AlreadyRunning, (await service.LaunchAsync("g")).Outcome);
        // A live process/report can precede its window. End this stable-window scenario
        // only after the production monitor has actually consumed HasWindow=true.
        await observedPlatform.WindowObserved.Task.WaitAsync(TimeSpan.FromSeconds(15));
        await f.Stop();
        await f.Wait(() => service.Status.Tracking == ProcessTrackingState.Exited);
    }

    [Theory]
    [InlineData("launcher", ProcessTrackingState.RunningConfirmed)]
    [InlineData("chain", ProcessTrackingState.RunningConfirmed)]
    [InlineData("multiple", ProcessTrackingState.RunningLimited)]
    [InlineData("empty", ProcessTrackingState.RunningLimited)]
    [InlineData("quick-launcher", ProcessTrackingState.RunningLimited)]
    public async Task ActualWindowsLauncherPatternsHaveBoundedSafeOutcomes(string mode, ProcessTrackingState expected)
    {
        await using var f = new LaunchFixture(observedHandoff: mode == "launcher");
        f.Set(mode);
        var events = new List<ProcessEvent>(); f.Service.ProcessChanged += e => { lock (events) events.Add(e); };
        await f.Approve(); f.Observation?.Requested(); var started = await f.Service.LaunchAsync("g");
        Assert.True(started.ProcessCreated);
        await f.Wait(() => expected == ProcessTrackingState.RunningLimited
            ? f.Service.Status.Tracking == expected
            : f.Service.Status.Tracking == expected && f.Service.Status.Current?.Pid != started.Initial?.Pid);
        if (expected == ProcessTrackingState.RunningConfirmed)
        {
            Assert.Contains(events, e => e.Kind == ProcessEventKind.ProcessHandoff);
            Assert.EndsWith("child_game.exe", f.Service.Status.Current!.ImagePath, StringComparison.OrdinalIgnoreCase);
            if (f.Observation is not null)
            {
                Assert.Equal(LaunchOutcome.AlreadyRunning, (await f.Service.LaunchAsync("g")).Outcome);
                f.Observation.Completed(f.Service.Status);
                output.WriteLine(JsonSerializer.Serialize(new { mode, timeline = f.Observation.Timeline, status = f.Service.Status }));
            }
        }
        else
        {
            Assert.Equal(LaunchOutcome.StartedButTrackingLimited, f.Service.Status.Outcome);
            Assert.DoesNotContain(events, e => e.Kind == ProcessEventKind.ProcessHandoff);
        }
    }

    [Fact]
    public async Task ShutdownReleasesHandlesAndNeverKillsCreatedProcess()
    {
        await using var f = new LaunchFixture(); f.Set("game"); await f.Approve();
        var started = await f.Service.LaunchAsync("g"); await f.Report("run");
        await f.Service.DisposeAsync();
        using var game = Process.GetProcessById(started.Initial!.Pid);
        Assert.False(game.HasExited);
        Assert.Equal(LaunchOutcome.Closed, (await f.Service.LaunchAsync("g")).Outcome);
    }

    [Fact]
    public async Task CancellationAfterCreationDoesNotKillOrReportLaunchFailed()
    {
        await using var f = new LaunchFixture(); f.Set("game"); await f.Approve();
        using var cancel = new CancellationTokenSource();
        var started = await f.Service.LaunchAsync("g", cancellation: cancel.Token);
        await f.Report("run"); cancel.Cancel();
        await f.Wait(() => f.Service.Status.Tracking == ProcessTrackingState.Detached);
        using var game = Process.GetProcessById(started.Initial!.Pid); Assert.False(game.HasExited);
        Assert.Equal(LaunchOutcome.StartedButTrackingLimited, f.Service.Status.Outcome);
        Assert.Equal(LaunchOutcome.AlreadyRunning, (await f.Service.LaunchAsync("g")).Outcome);
    }

    [Fact]
    public async Task CancelBeforeCreationAndUnreviewedPositionNeverExecute()
    {
        await using var f = new LaunchFixture(); f.Set("game");
        Assert.Equal(LaunchOutcome.PositionReviewRequired, (await f.Service.LaunchAsync("g")).Outcome);
        await f.Approve(); using var cancel = new CancellationTokenSource(); cancel.Cancel();
        Assert.Equal(LaunchOutcome.Cancelled, (await f.Service.LaunchAsync("g", cancellation: cancel.Token)).Outcome);
        Assert.False(File.Exists(Path.Combine(f.Reports, "run.json")));
    }

    [Theory]
    [InlineData("missing-exe", LaunchOutcome.ExecutableMissing)]
    [InlineData("exe-directory", LaunchOutcome.StaleInstallation)]
    [InlineData("missing-folder", LaunchOutcome.InstallationMissing)]
    [InlineData("missing-cwd", LaunchOutcome.WorkingDirectoryInvalid)]
    [InlineData("outside-cwd", LaunchOutcome.WorkingDirectoryInvalid)]
    [InlineData("relative-cwd", LaunchOutcome.WorkingDirectoryInvalid)]
    public async Task InvalidInstallationFailsBeforeCreation(string kind, LaunchOutcome expected)
    {
        await using var f = new LaunchFixture(); f.Set("game");
        var installation = f.Library.Entry.Installations[0];
        installation = kind switch
        {
            "missing-exe" => installation with { Executable = Path.Combine(f.Game, "absent.exe") },
            "exe-directory" => installation with { Executable = f.Game },
            "missing-folder" => installation with { Folder = Path.Combine(f.Game, "absent") },
            "missing-cwd" => installation with { WorkingDirectory = Path.Combine(f.Game, "absent") },
            "outside-cwd" => installation with { WorkingDirectory = Path.GetTempPath() },
            _ => installation with { WorkingDirectory = "." }
        };
        f.Library.Entry = f.Library.Entry with { Installations = [installation] };
        var result = await f.Service.LaunchAsync("g");
        Assert.Equal(expected, result.Outcome);
        if (kind == "missing-exe") Assert.Contains("启动文件不存在", result.Message);
        if (kind == "missing-cwd") Assert.Contains("工作目录不存在", result.Message);
        if (kind == "missing-folder") Assert.Contains("安装目录不存在", result.Message);
        Assert.False(File.Exists(Path.Combine(f.Reports, "run.json")));
    }

    [Fact]
    public async Task ReplacedExecutableRequiresExplicitNewReviewAndDpapiSurvivesRestart()
    {
        await using var f = new LaunchFixture(); f.Set("game"); await f.Approve();
        var before = f.Identity.Inspect(f.Exe, false).Observation!;
        File.Move(f.Exe, f.Exe + ".old"); File.Copy(f.Exe + ".old", f.Exe);
        Assert.NotEqual(before.Identity, f.Identity.Inspect(f.Exe, false).Observation!.Identity);
        Assert.Equal(LaunchOutcome.StaleInstallation, (await f.Service.LaunchAsync("g")).Outcome);
        var reread = new IsolatedLaunchPositionStore(new(f.Profile.Root));
        Assert.Single(await reread.ReadAsync(default));
        Assert.Equal(before.Identity, (await reread.ReadAsync(default))[0].ExecutableObservation.Identity);
    }

    [Theory]
    [InlineData(1, LaunchOutcome.Started)]
    [InlineData(5, LaunchOutcome.WorkingDirectoryInvalid)]
    public async Task ExplicitWorkingDirectorySupportsJapaneseSpacesAndRejectsUnsupportedLength(int segments, LaunchOutcome expected)
    {
        await using var f = new LaunchFixture(); f.Set("game");
        var cwd = Path.Combine(f.Game, string.Join(Path.DirectorySeparatorChar, Enumerable.Repeat("日本語 working directory " + new string('a', 28), segments)));
        Directory.CreateDirectory(cwd);
        f.Library.Entry = f.Library.Entry with { Installations = [f.Library.Entry.Installations[0] with { WorkingDirectory = cwd }] };
        await f.Approve(); var result = await f.Service.LaunchAsync("g");
        Assert.Equal(expected, result.Outcome);
        Assert.True(result.Snapshot!.ExplicitWorkingDirectory);
        if (expected != LaunchOutcome.Started) { Assert.False(File.Exists(Path.Combine(f.Reports, "run.json"))); return; }
        var echo = await f.Report("run"); Assert.Equal(Path.GetFullPath(cwd), Path.GetFullPath(echo.GetProperty("cwd").GetString()!));
    }

    [Fact]
    public void PreferredAndExplicitSelectionDoNotSilentlyChooseFirstOrChangePreference()
    {
        var a = new Installation("a", @"C:\a", @"C:\a\a.exe", []);
        var b = new Installation("b", @"C:\b", @"C:\b\b.exe", []);
        var entry = new LaunchEntry(new(Guid.NewGuid(), Guid.NewGuid(), "g"), [a, b], null);
        Assert.Null(LaunchSelection.Select(entry));
        Assert.Equal(a, LaunchSelection.Select(entry with { Installations = [a] }));
        Assert.Equal(b, LaunchSelection.Select(entry with { Preferred = "b" }));
        Assert.Equal(a, LaunchSelection.Select(entry with { Preferred = "b" }, "a"));
        Assert.Null(LaunchSelection.Select(entry, "missing"));
        Assert.Null(entry.Preferred);
    }

    [Fact]
    public async Task PositionStoreCorruptionIsNotMissingAndCannotBeSilentlyTrusted()
    {
        await using var f = new LaunchFixture(); f.Set("game"); await f.Approve();
        var path = Path.Combine(f.Profile.Root, "native-launch-positions.dpapi");
        File.WriteAllBytes(path, [1, 2, 3]);
        Assert.Equal(LaunchOutcome.StaleInstallation, (await f.Service.LaunchAsync("g")).Outcome);
        await Assert.ThrowsAsync<InvalidDataException>(() => f.Store.ReadAsync(default));
        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(path));
    }

    [Fact]
    public async Task PlatformRetainsOriginalCreationIdentityAndDistinguishesExit()
    {
        await using var f = new LaunchFixture(); f.Set("game");
        var prepared = await f.Service.PrepareAsync("g", null);
        using var process = f.Platform.Start(prepared.Snapshot!, default);
        await f.Report("run");
        var identity = process.Query().Identity!;
        Assert.Null(f.Platform.Attach(identity with { CreationFileTime = identity.CreationFileTime + 1 }));
        await f.Stop();
        Assert.Equal(ProcessTrackingState.Exited, process.Query().State);
        Assert.Equal(identity, process.Query().Identity);
    }

    [Fact]
    public async Task HeadlessExeWaitingForItsGameTransfersBeforeEitherExits()
    {
        await using var f = new LaunchFixture();
        f.Set("waiting-launcher");
        await f.Approve();
        var exit = new TaskCompletionSource<ProcessEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Service.ProcessChanged += fact => { if (fact.Kind == ProcessEventKind.ProcessExited) exit.TrySetResult(fact); };
        var started = await f.Service.LaunchAsync("g");
        Assert.True(started.ProcessCreated);
        var childPid = (await f.Report("run-game")).GetProperty("pid").GetInt32();
        using var child = Process.GetProcessById(childPid);
        _ = child.Handle;
        await f.Wait(() => f.Service.Status.Current?.Pid == childPid);
        Assert.Equal(ProcessPresence.Active, f.Platform.RevalidateExternal(started.Initial!));
        File.WriteAllText(Path.Combine(f.Reports, "run-game.stop"), "normal fixture close");
        var confirmed = await exit.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(childPid, confirmed.Current!.Pid);
        Assert.Equal(child.ExitTime.ToUniversalTime(), confirmed.ExitOccurredAtUtc!.Value.UtcDateTime);
        Assert.Equal(ProcessTrackingState.Exited, f.Service.Status.Tracking);
    }

    [Fact]
    public async Task ActualChildTreeRequiresParentCreationAndExitInterval()
    {
        await using var f = new LaunchFixture(); f.Set("launcher");
        var prepared = await f.Service.PrepareAsync("g", null);
        using var parent = f.Platform.Start(prepared.Snapshot!, default);
        await f.Report("run-game");
        var observed = parent.Query(); Assert.NotNull(observed.Identity);
        var valid = f.Platform.Descendants([observed], 8192, 32);
        Assert.Contains(valid.Candidates, c => c.ParentPid == observed.Identity.Pid);
        var recycled = observed with { Identity = observed.Identity with { CreationFileTime = DateTimeOffset.UtcNow.AddDays(1).ToFileTime() } };
        Assert.Empty(f.Platform.Descendants([recycled], 8192, 32).Candidates);
        var ended = observed with { State = ProcessTrackingState.Exited, ExitFileTime = observed.Identity.CreationFileTime };
        Assert.Empty(f.Platform.Descendants([ended], 8192, 32).Candidates);
    }
    [Theory]
    [InlineData("launcher", "run-game")]
    [InlineData("chain", "run-helper-game")]
    public async Task ShutdownDuringRealSyntheticHandoffDoesNotKillLauncherOrFutureChild(string mode, string report)
    {
        await using var f = new LaunchFixture(); f.Set(mode); await f.Approve();
        var started = await f.Service.LaunchAsync("g"); Assert.True(started.ProcessCreated);
        await f.Service.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        var child = await f.Report(report);
        using var process = Process.GetProcessById(child.GetProperty("pid").GetInt32());
        Assert.False(process.HasExited);
        Assert.Equal(LaunchOutcome.Closed, (await f.Service.LaunchAsync("g")).Outcome);
    }
}

internal sealed class WindowObservationPlatform(IGameProcessPlatform inner) : IGameProcessPlatform
{
    public TaskCompletionSource WindowObserved { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public IObservedProcess Start(LaunchSnapshot snapshot, CancellationToken cancellation) => inner.Start(snapshot, cancellation);
    public IObservedProcess? Attach(ProcessIdentity identity) => inner.Attach(identity);
    public ImmutableArray<ProcessIdentity> FindExisting(PathObservation executable, int maximum) => inner.FindExisting(executable, maximum);
    public ProcessTreeObservation Descendants(ImmutableArray<ProcessObservation> ancestors, int maximumProcesses, int maximumCandidates)
    {
        if (ancestors.Any(p => p.HasWindow)) WindowObserved.TrySetResult();
        return inner.Descendants(ancestors, maximumProcesses, maximumCandidates);
    }
}

internal sealed class LaunchFixture : IAsyncDisposable
{
    public TransactionProfile Profile { get; } = new();
    public string Game { get; }
    public string Reports { get; }
    public string Exe => Path.Combine(Game, "simple_game.exe");
    public LaunchLibraryStub Library { get; }
    public WindowsFilesystemIdentity Identity { get; } = new();
    public WindowsGameProcesses Platform { get; }
    public IsolatedLaunchPositionStore Store { get; }
    public GameLaunchService Service { get; }
    public LaunchFixtureObservation? Observation { get; }
    public LaunchFixture(bool observedHandoff = false, bool syntheticSteam = false)
    {
        Game = Path.Combine(Profile.Root, "日本語 游戏 [synthetic]"); Directory.CreateDirectory(Game);
        Reports = Path.Combine(Profile.Root, "observations"); Directory.CreateDirectory(Reports);
        File.WriteAllText(Path.Combine(Reports, ".the-order-launch-fixture"), "owned fixture");
        var source = ReferenceSources.FixtureOutput("TheOrder.LaunchFixture");
        foreach (var path in Directory.GetFiles(source)) File.Copy(path, Path.Combine(Game, Path.GetFileName(path)));
        foreach (var name in new[] { "simple_game", "child_game", "second_game", "helper", "LEProc", "Steam" })
            File.Copy(Path.Combine(source, "TheOrder.LaunchFixture.exe"), Path.Combine(Game, name + ".exe"));
        Library = new(new(new(Guid.NewGuid(), Guid.NewGuid(), "g"), [], "i"));
        Platform = new(Identity, steamClient: syntheticSteam ? () => Identity.Inspect(Path.Combine(Game, "Steam.exe"), false).Observation! : null); Store = new(new(Profile.Root));
        Observation = observedHandoff ? new(Platform, Reports) : null;
        Service = new(Library, Identity, (IGameProcessPlatform?)Observation ?? Platform, Store, new() { HandoffWindow = TimeSpan.FromSeconds(2), InitialTreeWindow = TimeSpan.FromSeconds(4), CandidateSettle = TimeSpan.FromMilliseconds(300) });
        if (Observation is not null) Service.ProcessChanged += Observation.ProcessChanged;
    }
    public void Set(string mode, string[]? arguments = null) => Library.Entry = Library.Entry with
    { Installations = [new("i", Game, Exe, [Reports, mode, "run", .. Observation is not null && mode == "launcher" ? new[] { "--observed-handoff" } : [], .. arguments ?? []])] };
    public async Task Approve()
    {
        var prepared = await Service.PrepareAsync("g", null); Assert.NotNull(prepared.Snapshot);
        await Service.ConfirmPositionAsync(prepared.Snapshot!);
    }
    public async Task<JsonElement> Report(string name)
    {
        var path = Path.Combine(Reports, name + ".json"); var deadline = Stopwatch.StartNew();
        while (true)
        {
            try { using var report = JsonDocument.Parse(await File.ReadAllBytesAsync(path)); return report.RootElement.Clone(); }
            catch (IOException) when (deadline.Elapsed < TimeSpan.FromSeconds(15)) { }
            catch (JsonException) when (deadline.Elapsed < TimeSpan.FromSeconds(15)) { }
            // Creation can be observed before the fixture finishes writing its report.
            await Task.Delay(100);
        }
    }
    public async Task Wait(Func<bool> condition)
    {
        var deadline = Stopwatch.StartNew();
        while (!condition()) { if (deadline.Elapsed > TimeSpan.FromSeconds(15)) throw new TimeoutException(Service.Status.ToString()); await Task.Delay(100); }
    }
    public async Task Stop()
    {
        // Synthetic control asks the fixture's own UI message loop to exit normally; no process kill.
        for (int attempt = 0; attempt < 100; attempt++)
        {
            bool any = false;
            foreach (var report in Directory.GetFiles(Reports, "*.json"))
            {
                File.WriteAllText(Path.ChangeExtension(report, ".stop"), "normal fixture close");
                try
                {
                    using var document = JsonDocument.Parse(File.ReadAllBytes(report));
                    using var process = Process.GetProcessById(document.RootElement.GetProperty("pid").GetInt32());
                    any |= !process.HasExited;
                }
                catch (ArgumentException) { }
            }
            if (!any) { await Task.Delay(300); return; }
            await Task.Delay(100);
        }
        throw new InvalidOperationException("Fixture did not close normally; retained files for diagnosis.");
    }
    public async ValueTask DisposeAsync()
    {
        await Service.DisposeAsync(); await Stop();
        // An exited Windows image can remain mapped briefly while OS teardown completes.
        for (int retry = 0; ; retry++)
        {
            try { Profile.Dispose(); return; }
            catch (Exception e) when (retry < 20 && e is IOException or UnauthorizedAccessException) { await Task.Delay(100); }
        }
    }
}

internal sealed class LaunchLibraryStub(LaunchEntry entry) : ILaunchLibrary
{
    public LaunchEntry Entry = entry;
    public Dictionary<string, LaunchEntry> OtherEntries = [];
    public Task<LaunchEntry> CaptureLaunchAsync(string id, CancellationToken cancellation)
    { cancellation.ThrowIfCancellationRequested(); return Task.FromResult(id == Entry.Identity.GameId ? Entry : OtherEntries[id]); }
    public Task<T> WithLaunchAdmissionAsync<T>(GameLaunchIdentity identity, string installationId, Func<T> create, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        if (identity != Entry.Identity || !Entry.Installations.Any(i => i.Id == installationId)) throw new LaunchValidationException(LaunchOutcome.StaleInstallation, "stale");
        return Task.FromResult(create());
    }
}
