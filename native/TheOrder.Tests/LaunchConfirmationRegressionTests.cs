using TheOrder.Application;
using TheOrder.Domain;
using TheOrder.Platform.Windows;
using TheOrder.Presentation;
using Xunit;

namespace TheOrder.Tests;

[Collection("Launch processes")]
public sealed class LaunchConfirmationRegressionTests
{
    [Fact]
    public async Task SavingInstallationSettingsDoesNotLeaveConfirmationStuckOnRetiredSnapshot()
    {
        await using var game = new LaunchFixture(); game.Set("game");
        using var profile = new ProfileFixture();
        using var store = profile.Open();
        await using var library = TransactionalLibrary.Open(NativeProfileTests.Codec, store.CreateLibraryPersistence());
        await library.ImportAsync(NativeProfileTests.Codec.Encode([SessionTestRig.Entry(total: 0, count: 0) with
            { Installations = game.Library.Entry.Installations }], SessionTestRig.Date));
        await using var engine = new SessionEngine(library, game.Identity, game.Platform,
            new IsolatedLaunchPositionStore(store.Configuration()), new WindowsSessionClock(),
            new WindowsSessionRecoveryTokens(store.Configuration()), autoPoll: false);
        await using var host = new AppSessionHost(() => Task.FromResult<SessionEngine?>(engine), () => library.Snapshot);
        await using var model = new LaunchViewModel(library, host);
        await model.OpenAsync("g"); Assert.True(model.CanConfirm);

        // The installation editor writes auxiliary overrides even when every tool is off.
        // This retires the preparation captured by the still-selected detail page.
        var before = (await library.CaptureLaunchAsync("g")).Identity;
        var row = (await library.InstallationsAsync("g")).Items[0];
        var saved = row with { Compatibility = new LaunchCompatibility { Auxiliaries = new() } };
        await library.SaveInstallationAsync((await library.InstallationsAsync("g")).Token,
            new(saved, game.Identity.Inspect(game.Game).Observation!, game.Identity.Inspect(game.Exe, false).Observation, null),
            true, new(game.Identity));
        Assert.NotEqual(before, (await library.CaptureLaunchAsync("g")).Identity);

        await model.ConfirmAsync();
        Assert.Empty(await new IsolatedLaunchPositionStore(store.Configuration()).ReadAsync(default));
        Assert.True(model.CanConfirm);
        Assert.DoesNotContain("位置未能保存或已经变化", model.Message);
        await model.ConfirmAsync();
        Assert.True(model.CanLaunch, model.Message);
        Assert.Single(await new IsolatedLaunchPositionStore(store.Configuration()).ReadAsync(default));
        Assert.Empty(Directory.GetFiles(game.Reports, "*.json"));
    }

    [Fact]
    public async Task ReplacedExecutableIsShownForNewReviewAndNeverAutomaticallyApproved()
    {
        await using var game = new LaunchFixture(); game.Set("game");
        await using var model = new LaunchViewModel(game.Library, game.Service);
        await model.OpenAsync("g");
        var before = game.Identity.Inspect(game.Exe, false).Observation!.Identity;
        File.Move(game.Exe, game.Exe + ".old"); File.Copy(game.Exe + ".old", game.Exe);
        await model.ConfirmAsync();
        Assert.Empty(await game.Store.ReadAsync(default));
        Assert.True(model.CanConfirm); Assert.False(model.CanLaunch);
        await model.ConfirmAsync();
        Assert.True(model.CanLaunch, model.Message);
        Assert.NotEqual(before, Assert.Single(await game.Store.ReadAsync(default)).ExecutableObservation.Identity);
        Assert.Empty(Directory.GetFiles(game.Reports, "*.json"));
    }

    [Fact]
    public async Task RefreshRetainsExplicitInstallationAndFallsBackWhenItWasRemoved()
    {
        await using var game = new LaunchFixture(); game.Set("game");
        game.Library.Entry = game.Library.Entry with { Installations = [game.Library.Entry.Installations[0],
            game.Library.Entry.Installations[0] with { Id = "other" }] };
        await using var model = new LaunchViewModel(game.Library, game.Service);
        await model.OpenAsync("g"); await model.SelectAsync("other");
        await model.RefreshAsync(); Assert.Equal("other", model.SelectedInstallationId);
        game.Library.Entry = game.Library.Entry with { Installations = [game.Library.Entry.Installations[0]] };
        await model.RefreshAsync(); Assert.Equal("i", model.SelectedInstallationId);
        await model.ConfirmAsync(); Assert.True(model.CanLaunch, model.Message);
    }
}
