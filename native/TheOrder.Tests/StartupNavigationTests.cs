using TheOrder.Presentation;
using Xunit;

namespace TheOrder.Tests;

public sealed class StartupNavigationTests
{
    [Theory]
    [InlineData(WorkspacePage.Library)]
    [InlineData(WorkspacePage.Settings)]
    [InlineData(WorkspacePage.Online)]
    [InlineData(WorkspacePage.Statistics)]
    public void PeerPagesDoNotInventBackHistory(WorkspacePage page)
    { Assert.False(ShellNavigationPolicy.CanGoBack(page)); Assert.Null(ShellNavigationPolicy.Parent(page)); }
    [Theory]
    [InlineData(WorkspacePage.Details, WorkspacePage.Library)]
    [InlineData(WorkspacePage.Import, WorkspacePage.Library)]
    [InlineData(WorkspacePage.Recognition, WorkspacePage.Library)]
    [InlineData(WorkspacePage.Installations, WorkspacePage.Details)]
    public void NestedPagesRetainSingleBackRoute(WorkspacePage page, WorkspacePage parent)
    { Assert.True(ShellNavigationPolicy.CanGoBack(page)); Assert.Equal(parent, ShellNavigationPolicy.Parent(page)); }
    [Fact]
    public void DiscoveryBrowsingHasBackUntilItReturnsToDiscoveryHome()
    { Assert.True(ShellNavigationPolicy.CanGoBack(WorkspacePage.Online, true)); Assert.False(ShellNavigationPolicy.CanGoBack(WorkspacePage.Online, false)); }
    [Fact]
    public void StatisticsDrilldownReturnsToStatisticsWithoutInventingOtherParents()
    {
        Assert.Equal(WorkspacePage.Statistics, ShellNavigationPolicy.Parent(WorkspacePage.Details, WorkspacePage.Statistics));
        Assert.Equal(WorkspacePage.Details, ShellNavigationPolicy.Parent(WorkspacePage.Installations, WorkspacePage.Statistics));
        Assert.Equal(WorkspacePage.Library, ShellNavigationPolicy.Parent(WorkspacePage.Details, WorkspacePage.Settings));
    }
    [Fact]
    public async Task HomeCannotRevealBeforeLocalStateVisibleArtworkAndFinalFrame()
    {
        var gate = new StartupReadiness(); var local = new TaskCompletionSource(); var images = new TaskCompletionSource(); var frame = new TaskCompletionSource();
        var imagesEntered = new TaskCompletionSource(); var finalFrameEntered = new TaskCompletionSource();
        int frames = 0; bool began = false;
        var load = gate.PrepareAsync(() => { began = true; return local.Task; }, () => { imagesEntered.SetResult(); return images.Task; },
            () => { if (++frames != 3) return Task.CompletedTask; finalFrameEntered.SetResult(); return frame.Task; });
        Assert.True(began); Assert.Equal(StartupPhase.Loading, gate.Phase); Assert.False(gate.CanReveal);
        await Assert.ThrowsAsync<InvalidOperationException>(() => gate.PrepareAsync(() => Task.CompletedTask, () => Task.CompletedTask, () => Task.CompletedTask));
        local.SetResult(); await imagesEntered.Task; Assert.Equal(StartupPhase.PreparingFirstPage, gate.Phase); Assert.False(gate.CanReveal);
        images.SetResult(); await finalFrameEntered.Task; Assert.Equal(3, frames); Assert.False(gate.CanReveal);
        frame.SetResult(); await load; Assert.Equal(StartupPhase.Ready, gate.Phase); Assert.True(gate.CanReveal);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedLocalLoadOrArtworkNeverShowsPartialHomepage(bool artwork)
    {
        var gate = new StartupReadiness();
        await Assert.ThrowsAsync<IOException>(() => gate.PrepareAsync(
            () => artwork ? Task.CompletedTask : Task.FromException(new IOException("library failure")),
            () => Task.FromException(new IOException("image failure")), () => Task.CompletedTask));
        Assert.Equal(StartupPhase.Failed, gate.Phase); Assert.False(gate.CanReveal);
    }
}
