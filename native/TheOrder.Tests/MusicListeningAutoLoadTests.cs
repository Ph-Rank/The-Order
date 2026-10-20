using TheOrder.Application;
using TheOrder.Presentation;
using Xunit;

namespace TheOrder.Tests;

public sealed class MusicListeningAutoLoadTests
{
    [Fact]
    public async Task OneExpansionReadsCurrentAndDigitalWithoutChangingTheSelectedRelease()
    {
        var reader = new Reader(); using var model = Model(reader);
        await model.ExpandAsync();
        Assert.Equal(new[] { "current", "digital" }, reader.Calls);
        Assert.Equal(MusicTestData.Release, model.Current!.Snapshot.Entity.Id);
        Assert.Equal(MusicTestData.Other, Assert.Single(model.Digital!.Editions).Snapshot.Entity.Id);
        Assert.False(model.Busy);
    }
    [Fact]
    public async Task RepeatedExpansionNeitherDuplicatesAnActiveChainNorReloadsCompletedResults()
    {
        var reader = new Reader { DelayedDigital = NewDigital() }; using var model = Model(reader);
        var first = model.ExpandAsync(); await model.ExpandAsync();
        Assert.True(model.Busy); Assert.Equal(1, reader.DigitalCalls);
        reader.DelayedDigital.SetResult(Digital()); await first; await model.ExpandAsync();
        Assert.Equal(1, reader.RootCalls); Assert.Equal(1, reader.DigitalCalls);
    }
    [Fact]
    public async Task PreviouslyLoadedCurrentLinksDoNotRequireAnotherReadToDiscoverDigitalEditions()
    {
        var reader = new Reader(); using var model = Model(reader);
        await model.LoadAsync(); Assert.Null(model.Digital);
        await model.ExpandAsync(); Assert.NotNull(model.Digital);
        Assert.Equal(1, reader.RootCalls); Assert.Equal(1, reader.DigitalCalls);
    }
    [Theory]
    [InlineData(MusicBrainzEntityKind.Release)]
    [InlineData(MusicBrainzEntityKind.Recording)]
    public async Task NoGroupOrRecordingCannotTriggerAnInventedAlbumSearch(MusicBrainzEntityKind kind)
    {
        var reader = new Reader { MissingGroup = true }; using var model = new MusicListeningViewModel(reader);
        model.SetContext(new(kind, MusicTestData.Release)); await model.ExpandAsync(); await model.ExpandAsync();
        Assert.NotNull(model.Current); Assert.Equal(1, reader.RootCalls); Assert.Equal(0, reader.DigitalCalls);
    }
    [Fact]
    public async Task CancellationDuringTheFirstReadPreventsTheDigitalStageEvenIfTheProviderReturnsLate()
    {
        var reader = new Reader { DelayedRoot = new(TaskCreationOptions.RunContinuationsAsynchronously) }; using var model = Model(reader);
        var pending = model.ExpandAsync(); model.Cancel();
        reader.DelayedRoot.SetResult(new(OutletData.Snapshot(), false, false, null)); await pending;
        Assert.Null(model.Current); Assert.Null(model.Digital); Assert.Equal(0, reader.DigitalCalls);
    }
    [Fact]
    public async Task CancelledDigitalReadCanResumeOnReopenAndItsLateFailureCannotOverrideTheNewResult()
    {
        var reader = new Reader { DelayedDigital = NewDigital() }; using var model = Model(reader);
        var pending = model.ExpandAsync(); Assert.NotNull(model.Current);
        model.Cancel(); await model.ExpandAsync(); var accepted = model.Digital;
        reader.DelayedDigital.SetException(new IOException("late synthetic failure")); await pending;
        Assert.Same(accepted, model.Digital); Assert.NotNull(accepted); Assert.False(model.NeedsAttention); Assert.False(model.Busy);
        Assert.Equal(1, reader.RootCalls); Assert.Equal(2, reader.DigitalCalls);
    }
    [Fact]
    public async Task NavigationDuringTheFirstStageCannotStartDigitalDiscoveryForAnotherAlbum()
    {
        var reader = new Reader { DelayedRoot = new(TaskCreationOptions.RunContinuationsAsynchronously) }; using var model = Model(reader);
        var pending = model.ExpandAsync(); model.SetContext(OutletData.Ref(MusicTestData.Other));
        reader.DelayedRoot.SetResult(new(OutletData.Snapshot(), false, false, null)); await pending;
        Assert.Equal(MusicTestData.Other, model.Reference!.Id); Assert.Null(model.Current); Assert.Null(model.Digital); Assert.Equal(0, reader.DigitalCalls);
    }
    [Fact]
    public async Task DigitalFailurePreservesCurrentLinksAndAReopenRetriesOnlyTheFailedStage()
    {
        var reader = new Reader { FailDigital = true }; using var model = Model(reader);
        await model.ExpandAsync(); Assert.NotNull(model.Current); Assert.Null(model.Digital); Assert.True(model.NeedsAttention);
        reader.FailDigital = false; await model.ExpandAsync();
        Assert.NotNull(model.Digital); Assert.False(model.NeedsAttention); Assert.Equal(1, reader.RootCalls); Assert.Equal(2, reader.DigitalCalls);
    }
    [Fact]
    public async Task ExplicitRefreshRetriesBothStagesAfterACompletedExpansion()
    {
        var reader = new Reader(); using var model = Model(reader);
        await model.ExpandAsync(); await model.ExpandAsync(true);
        Assert.Equal(2, reader.RootCalls); Assert.Equal(2, reader.DigitalCalls); Assert.True(reader.LastRefresh);
    }
    [Fact]
    public async Task CurrentLinksAreObservableWhileDigitalDiscoveryIsStillLoading()
    {
        var reader = new Reader { DelayedDigital = NewDigital() }; using var model = Model(reader);
        bool visible = false; model.PropertyChanged += (_, _) => visible |= model.Busy && model.Current is not null && model.Digital is null;
        var pending = model.ExpandAsync(); Assert.True(visible); Assert.True(model.Busy);
        reader.DelayedDigital.SetResult(Digital()); await pending; Assert.False(model.Busy);
    }
    private static MusicListeningViewModel Model(Reader reader)
    { var model = new MusicListeningViewModel(reader); model.SetContext(OutletData.Ref()); return model; }
    private static TaskCompletionSource<MusicDigitalOutletResult> NewDigital() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static MusicDigitalOutletResult Digital() => new([new(OutletData.Snapshot(MusicTestData.Other) with { Formats = ["Digital Media"] }, false, false, null)], false, null);
    private sealed class Reader : IMusicListeningReader
    {
        public int RootCalls, DigitalCalls;
        public bool MissingGroup, FailDigital, LastRefresh;
        public List<string> Calls = [];
        public TaskCompletionSource<MusicOutletResult>? DelayedRoot;
        public TaskCompletionSource<MusicDigitalOutletResult>? DelayedDigital;
        public Task<MusicOutletResult> LoadOutletsAsync(MusicBrainzReference reference, bool refresh, CancellationToken token)
        {
            Calls.Add("current"); RootCalls++; LastRefresh = refresh;
            if (RootCalls == 1 && DelayedRoot is not null) return DelayedRoot.Task;
            var value = OutletData.Snapshot(reference.Id) with { Entity = reference, GroupId = MissingGroup || reference.Kind == MusicBrainzEntityKind.Recording ? null : MusicTestData.Group,
                Links = reference.Kind == MusicBrainzEntityKind.Recording ? [] : OutletData.Snapshot(reference.Id).Links,
                ReleaseStatus = reference.Kind == MusicBrainzEntityKind.Recording ? null : "Official",
                Formats = reference.Kind == MusicBrainzEntityKind.Recording ? [] : ["CD"],
                TrackCount = reference.Kind == MusicBrainzEntityKind.Recording ? null : 12 };
            return Task.FromResult(new MusicOutletResult(value, false, false, null));
        }
        public Task<MusicDigitalOutletResult> LoadDigitalOutletsAsync(MusicOutletSnapshot current, CancellationToken token)
        {
            Calls.Add("digital"); DigitalCalls++;
            if (FailDigital) throw new MusicBrainzException(MusicBrainzFailure.RateLimited, "合成429");
            return DigitalCalls == 1 && DelayedDigital is not null ? DelayedDigital.Task : Task.FromResult(Digital());
        }
        public Task<MediaUserLinks> ReadAppleChoicesAsync(MusicBrainzReference reference, CancellationToken token) => Task.FromResult(MediaUserLinks.Empty(MediaIdentityContext.ForMusicOutlet(reference).Anchor));
        public Task<AppleCatalogResult> SearchAppleAsync(MusicOutletSnapshot snapshot, string country, bool refresh, CancellationToken token) => throw new NotSupportedException();
        public Task<MediaUserLinks> ChangeAppleChoiceAsync(MusicBrainzReference reference, AppleAlbumCandidate? candidate, MediaIdentityReference target, long revision, CancellationToken token) => throw new NotSupportedException();
    }
}
