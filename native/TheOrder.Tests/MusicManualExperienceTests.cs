using TheOrder.Application;
using TheOrder.Presentation;
using Xunit;

namespace TheOrder.Tests;

public sealed class MusicManualExperienceTests
{
    private sealed class Subjects : IBangumiSubjectDetailsReader
    {
        public Task<BangumiSubjectDetailsResult> LoadAsync(string id, BangumiRelationKind kind, bool refresh, CancellationToken cancellation)
            => Task.FromResult(new BangumiSubjectDetailsResult(MusicTestData.Details(id) with { MusicFacts = MusicTestData.Source().Facts with
                { ExternalIds = [new(MusicBrainzEntityKind.Release, MusicTestData.Release)] } }, false, false, null));
    }
    private static MusicTestProvider Provider() => new() { Search = (_, _) => Task.FromResult(new MusicBrainzSearchResult(
        [MusicTestData.Candidate(), MusicTestData.Candidate(MusicTestData.Other)], 2, false, null, null)) };
    [Fact]
    public async Task ConflictingReleaseIsSavedAsOtherEditionWithoutChangingConfirmedRelease()
    {
        var choices = new SessionMediaIdentityDecisions(); var service = new MusicBrainzService(Provider(), identityDecisions: choices);
        var subjects = new Subjects(); var source = MusicAlbumSource.From((await subjects.LoadAsync(MusicTestData.Item().Id, BangumiRelationKind.Music, false, default)).Details);
        await service.ConfirmAsync(source, MusicTestData.Album(), default);
        using var vm = new MusicAlbumViewModel(subjects, service); await vm.OpenAsync(MusicTestData.Item());
        await vm.SearchAsync(new MusicBrainzReference(MusicBrainzEntityKind.Release, MusicTestData.Other).Url);
        await vm.PreviewAsync(MusicTestData.Other); Assert.False(vm.CanConfirm); Assert.True(vm.CanAddRelated);
        await vm.AddRelatedAsync(); Assert.True(vm.RelatedSelection); Assert.Equal(MusicAlbumState.Ready, vm.State);
        var context = MediaIdentityContext.From(source); var link = Assert.Single((await choices.ReadLinksAsync(context, default)).Items);
        Assert.Equal("OTHER_EDITION", link.Relation); Assert.Equal(MusicTestData.Release, (await choices.ReadAsync(context, default)).Confirmed!.Target.Id);
        using var restarted = new MusicAlbumViewModel(subjects, service); await restarted.OpenAsync(MusicTestData.Item());
        Assert.Equal(MusicTestData.Release, restarted.Displayed!.Album.Release.Id);
        await restarted.OpenUserLinkAsync(Assert.Single(restarted.UserLinks!.Items)); Assert.True(restarted.RelatedSelection);
        await restarted.RemoveUserLinkAsync(link.Target); Assert.Empty(restarted.UserLinks!.Items);
        Assert.Equal(MusicTestData.Release, (await choices.ReadAsync(context, default)).Confirmed!.Target.Id);
    }
    [Fact]
    public async Task FailedManualReleaseLookupCanBeRetriedWithoutConfirmedIdentity()
    {
        var provider = Provider(); var choices = new SessionMediaIdentityDecisions(); var service = new MusicBrainzService(provider, identityDecisions: choices);
        using var vm = new MusicAlbumViewModel(new Subjects(), service); await vm.OpenAsync(MusicTestData.Item());
        provider.Search = (_, _) => throw new MusicBrainzException(MusicBrainzFailure.Timeout, "合成超时");
        await vm.SearchAsync(new MusicBrainzReference(MusicBrainzEntityKind.Release, MusicTestData.Other).Url);
        Assert.True(vm.CanSavePending); await vm.SavePendingAsync(); var pending = Assert.Single(vm.UserLinks!.Items);
        Assert.Equal(MediaUserLinkState.Pending, pending.State); Assert.Null((await choices.ReadAsync(MediaIdentityContext.From(vm.Source!), default)).Confirmed);
        await vm.OpenUserLinkAsync(pending); Assert.Equal(MusicAlbumState.Preview, vm.State); Assert.False(vm.CanConfirm);
        Assert.Null((await choices.ReadAsync(MediaIdentityContext.From(vm.Source!), default)).Confirmed);
    }
    [Theory]
    [InlineData(MusicBrainzEntityKind.Recording)] [InlineData(MusicBrainzEntityKind.Work)] [InlineData(MusicBrainzEntityKind.Artist)]
    public async Task NonAlbumUrlsCannotMasqueradeAsReleaseOrPendingReleaseGroup(MusicBrainzEntityKind kind)
    {
        using var vm = new MusicAlbumViewModel(new Subjects(), new MusicBrainzService(Provider())); await vm.OpenAsync(MusicTestData.Item());
        await vm.SearchAsync(new MusicBrainzReference(kind, MusicTestData.Other).Url);
        Assert.False(vm.CanSavePending); Assert.Contains("独立资料", vm.Message);
    }
    [Fact]
    public async Task FailedSearchDoesNotFusePreviouslyViewedRelatedReleaseWithBangumiSource()
    {
        var provider = Provider(); var service = new MusicBrainzService(provider);
        using var vm = new MusicAlbumViewModel(new Subjects(), service); await vm.OpenAsync(MusicTestData.Item());
        await vm.SearchAsync("another release"); await vm.PreviewAsync(MusicTestData.Other); await vm.AddRelatedAsync(); Assert.True(vm.RelatedSelection);
        provider.Search = (_, _) => throw new MusicBrainzException(MusicBrainzFailure.Timeout, "合成超时");
        await vm.SearchAsync("new search after related album"); Assert.True(vm.RelatedSelection);
        Assert.Equal(MusicTestData.Other, vm.Displayed!.Album.Release.Id);
    }
}
