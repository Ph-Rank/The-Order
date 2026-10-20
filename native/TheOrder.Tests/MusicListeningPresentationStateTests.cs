using TheOrder.Application;
using TheOrder.Presentation;
using Xunit;

namespace TheOrder.Tests;

public sealed class MusicListeningPresentationStateTests
{
    [Fact]
    public async Task SuccessfulReadDoesNotShowRoutineTechnicalStatus()
    {
        var provider = new OutletData.Provider();
        using var model = new MusicListeningViewModel(new MusicBrainzService(provider, clock: () => OutletData.Now));
        model.SetContext(OutletData.Ref()); await model.LoadAsync();
        Assert.NotNull(model.Current); Assert.False(model.NeedsAttention); Assert.False(model.Busy);
    }

    [Fact]
    public async Task BoundedSearchNoteIsRetainedWithoutDuplicatingTheVisibleResults()
    {
        var provider = DigitalProvider(truncated: true);
        using var model = new MusicListeningViewModel(new MusicBrainzService(provider, clock: () => OutletData.Now));
        model.SetContext(OutletData.Ref()); await model.LoadAsync(); await model.FindDigitalAsync();
        Assert.Single(model.Digital!.Editions); Assert.NotNull(model.Digital.Warning);
        Assert.False(model.NeedsAttention); Assert.Equal(MusicTestData.Release, model.Current!.Snapshot.Entity.Id);
    }

    [Fact]
    public async Task OfflineCacheFallbackRemainsVisibleAndRecoveryClearsTheNotice()
    {
        var provider = new OutletData.Provider();
        using var model = new MusicListeningViewModel(new MusicBrainzService(provider, clock: () => OutletData.Now));
        model.SetContext(OutletData.Ref()); await model.LoadAsync(); provider.Fail = true; await model.LoadAsync(true);
        Assert.NotNull(model.Current); Assert.NotNull(model.Current.Warning); Assert.True(model.NeedsAttention);
        provider.Fail = false; await model.LoadAsync(true);
        Assert.Null(model.Current.Warning); Assert.False(model.NeedsAttention);
    }

    [Fact]
    public async Task FailureWithoutCacheIsVisibleAndDoesNotBecomeAnEmptySuccess()
    {
        var provider = new OutletData.Provider { Fail = true };
        using var model = new MusicListeningViewModel(new MusicBrainzService(provider, clock: () => OutletData.Now));
        model.SetContext(OutletData.Ref()); await model.LoadAsync();
        Assert.Null(model.Current); Assert.True(model.NeedsAttention); Assert.False(model.Busy);
    }

    [Fact]
    public async Task EmptyDigitalResultsAndCancellationRemainVisibleButDoNotLeakToAnotherAlbum()
    {
        using var model = new MusicListeningViewModel(new MusicBrainzService(new OutletData.Provider(), clock: () => OutletData.Now));
        model.SetContext(OutletData.Ref()); await model.LoadAsync(); await model.FindDigitalAsync();
        Assert.Empty(model.Digital!.Editions); Assert.True(model.NeedsAttention);
        model.Cancel(); Assert.True(model.NeedsAttention); Assert.NotNull(model.Current);
        model.SetContext(OutletData.Ref(MusicTestData.Other)); Assert.False(model.NeedsAttention); Assert.Null(model.Current);
    }

    private static OutletData.Provider DigitalProvider(bool truncated)
    {
        var provider = new OutletData.Provider { Editions = new([MusicTestData.Candidate(MusicTestData.Other) with { MediaFormats = ["Digital Media"] }], truncated ? 40 : 1, truncated, null, null) };
        provider.Data = reference => OutletData.Snapshot(reference.Id) with { Formats = ["Digital Media"] };
        return provider;
    }
}
