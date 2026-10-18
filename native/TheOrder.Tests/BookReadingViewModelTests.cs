using TheOrder.Application;
using TheOrder.Infrastructure.Legacy;
using TheOrder.Platform.Windows;
using TheOrder.Presentation;
using Xunit;

namespace TheOrder.Tests;

public sealed class BookReadingViewModelTests
{
    private static AniListDetails Fresh(int id = 30715) => BookOutletsTests.Details(id, links: [BookOutletsTests.Raw("https://bookwalker.jp/series/135012/")]);
    [Fact]
    public async Task CurrentMetadataShowsImmediatelyAndDoesNotFetchAnyPlatformOrRepeatProviderRequests()
    {
        var reader = new Reader(); using var vm = new BookReadingViewModel(reader, new Subjects()); var details = Fresh();
        vm.SetSource(BookSource.From(details), details, null); Assert.Single(vm.Links); Assert.False(vm.Expanded); Assert.Equal(0, reader.Calls);
        await vm.ExpandAsync(); await vm.ExpandAsync(); Assert.True(vm.Expanded); Assert.Single(vm.Links); Assert.Equal(0, reader.Calls);
    }
    [Fact]
    public async Task OldCacheIsOnlyEnrichedWhenExpandedAndRefreshUsesTheSameProviderIdentity()
    {
        var reader = new Reader(); using var vm = new BookReadingViewModel(reader, new Subjects()); var old = Fresh() with { BookLinks = [], HasBookLinkMetadata = false };
        vm.SetSource(BookSource.From(old), old, null); Assert.Empty(vm.Links); Assert.Equal(0, reader.Calls);
        await vm.ExpandAsync(); Assert.Single(vm.Links); Assert.Equal(1, reader.Calls); Assert.Equal(30715, reader.LastId);
        await vm.ExpandAsync(); Assert.Equal(1, reader.Calls); await vm.ExpandAsync(true); Assert.Equal(2, reader.Calls);
    }
    [Fact]
    public async Task FailureDuringRefreshRetainsCurrentLinksAndDoesNotBecomeAnEmptySuccess()
    {
        var reader = new Reader { Read = (_,_) => throw new AniListException(AniListFailure.Offline, "synthetic") };
        using var vm = new BookReadingViewModel(reader, new Subjects()); var details = Fresh(); vm.SetSource(BookSource.From(details), details, null);
        await vm.ExpandAsync(true); Assert.Single(vm.Links); Assert.Contains("保留", vm.Message); Assert.False(vm.Busy);
    }
    [Fact]
    public async Task RapidNavigationCannotApplyLateLinksToAnotherWork()
    {
        var pending = new TaskCompletionSource<AniListResult>(); var reader = new Reader { Read = (_,_) => pending.Task };
        using var vm = new BookReadingViewModel(reader, new Subjects()); var old = Fresh() with { HasBookLinkMetadata = false, BookLinks = [] };
        vm.SetSource(BookSource.From(old), old, null); var wait = vm.ExpandAsync(); Assert.True(vm.Busy);
        var next = Fresh(45821) with { BookLinks = [] }; vm.SetSource(BookSource.From(next), next, null);
        pending.SetResult(new(Fresh(), false, false, null)); await wait;
        Assert.Equal("45821", vm.Source!.Identity.Id); Assert.Empty(vm.Links); Assert.False(vm.Busy);
    }
    [Fact]
    public async Task CancelAndReopenCannotLetAnOldCompletionClearNewLoadingState()
    {
        var one = new TaskCompletionSource<AniListResult>(); var two = new TaskCompletionSource<AniListResult>(); int requests = 0;
        var reader = new Reader { Read = (_,_) => ++requests == 1 ? one.Task : two.Task };
        using var vm = new BookReadingViewModel(reader, new Subjects()); var old = Fresh() with { HasBookLinkMetadata = false, BookLinks = [] };
        vm.SetSource(BookSource.From(old), old, null); var first = vm.ExpandAsync(); vm.Collapse(); Assert.False(vm.Busy);
        var second = vm.ExpandAsync(); Assert.True(vm.Busy); one.SetResult(new(Fresh(), false, false, null)); await first;
        Assert.True(vm.Busy); Assert.Empty(vm.Links); two.SetResult(new(Fresh(), false, false, null)); await second;
        Assert.False(vm.Busy); Assert.Single(vm.Links);
    }
    [Theory][InlineData(true)][InlineData(false)]
    public async Task ChangedProviderIdOrMediumCannotSilentlyReplaceTheChosenBook(bool id)
    {
        var changed = Fresh() with { Media = Fresh().Media with { Id = id ? 99 : 30715, Format = id ? "MANGA" : "NOVEL" } };
        var reader = new Reader { Read = (_,_) => Task.FromResult(new AniListResult(changed, false, false, null)) };
        using var vm = new BookReadingViewModel(reader, new Subjects()); var details = Fresh(); vm.SetSource(BookSource.From(details), details, null);
        await vm.ExpandAsync(true); Assert.Equal("30715", vm.Source!.Identity.Id); Assert.Single(vm.Links); Assert.Contains("发生变化", vm.Message);
    }
    [Fact]
    public void VolumeCannotBorrowAnilistSeriesIdentityAndWrongSourceIdIsRefused()
    {
        using var vm = new BookReadingViewModel(new Reader(), new Subjects()); var details = Fresh();
        Assert.Throws<InvalidDataException>(() => vm.SetSource(BookSource.From(details) with { Identity = new("Bangumi", "71", BookEntityLevel.Volume) }, details, null));
        Assert.Throws<InvalidDataException>(() => vm.SetSource(BookSource.From(details), Fresh(99), null));
    }
    [Fact]
    public async Task AFailedBangumiRefreshDoesNotRemoveAnilistDataOrForceAnIdentityChange()
    {
        var subject = Subject(); var source = BookSource.From(subject); var reader = new Reader();
        var subjects = new Subjects { Read = (_,_) => throw new BangumiRelationsException(BangumiRelationsFailure.Offline, "synthetic") };
        using var vm = new BookReadingViewModel(reader, subjects); vm.SetSource(source, Fresh(), subject);
        await vm.ExpandAsync(true); Assert.Single(vm.Links); Assert.Equal("71", vm.Source!.Identity.Id); Assert.Contains("Bangumi", vm.Message);
    }
    [Fact]
    public async Task UserConfirmationRejectionAndRevocationRemainIndependentOfBookLinkCacheAcrossRestart()
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "the-order-book-outlets-" + Guid.NewGuid().ToString("N")));
        NativeProfileStore? profile = null;
        try
        {
            profile = NativeProfileStore.Open(new(NativeProfileKind.Test, root), new NativeLibraryCodec());
            var library = File.ReadAllBytes(Path.Combine(root, "library.json")); var source = AniListSource.From(Subject()); var provider = new Provider();
            AniListService Service() => new(provider, new IsolatedAniListCache(profile!.Configuration()));
            var service = Service(); await service.ConfirmAsync(source, Fresh(), default); await service.LoadAsync(30715, AniListMediaKind.Manga, false, 1, default);
            Clear(); profile.Dispose(); profile = NativeProfileStore.Open(new(NativeProfileKind.Test, root), new NativeLibraryCodec());
            service = Service(); var match = await service.MatchAsync(source, null, false, default); Assert.True(match.Binding!.IsUserDecision); Assert.Equal(30715, match.Binding.MediaId);
            var details = await service.LoadAsync(30715, AniListMediaKind.Manga, false, 1, default); Assert.Single(BookOutlets.FromAniList(details.Details).Links);
            await service.RevokeAsync(source, 30715, default); Assert.Null((await service.MatchAsync(source, null, false, default)).Binding);
            await service.RejectAsync(source, Fresh().Media, default); Clear(); profile.Dispose(); profile = NativeProfileStore.Open(new(NativeProfileKind.Test, root), new NativeLibraryCodec());
            service = Service(); match = await service.MatchAsync(source, null, false, default); Assert.Null(match.Binding); Assert.Contains(30715, match.RejectedIds);
            Assert.Equal(library, File.ReadAllBytes(Path.Combine(root, "library.json")));
            void Clear()
            {
                var cache = Path.GetFullPath(Path.Combine(root, IsolatedAniListCache.DirectoryName));
                if (!cache.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException();
                if (Directory.Exists(cache)) Directory.Delete(cache, true);
            }
        }
        finally
        {
            profile?.Dispose();
            var temp = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!root.StartsWith(temp, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(root).StartsWith("the-order-book-outlets-", StringComparison.Ordinal)) throw new InvalidOperationException();
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
    internal static BangumiSubjectDetails Subject() => new("71", BangumiRelationKind.Book, "Fate/stay night", "Fate/stay night", null, "", null, "漫画", null, null, null, BookOutletsTests.Stamp)
        { BookFacts = new(true, 2, null, [], [], [], [], [], [], null) { HasExternalLinkMetadata = true } };
    private sealed class Reader : IAniListReader
    {
        public int Calls; public int LastId; public Func<int,CancellationToken,Task<AniListResult>> Read = (id,_) => Task.FromResult(new AniListResult(Fresh(id), false, false, null));
        public Task<AniListResult> LoadAsync(int id, AniListMediaKind kind, bool refresh, int page, CancellationToken token) { Calls++; LastId = id; Assert.True(refresh); return Read(id, token); }
        public Task<AniListMatch> MatchAsync(AniListSource source, string? query, bool refresh, CancellationToken token) => throw new NotSupportedException();
        public Task<AniListMatch> FindCandidatesAsync(AniListSource source, bool refresh, CancellationToken token) => throw new NotSupportedException();
        public Task ConfirmAsync(AniListSource source, AniListDetails details, CancellationToken token) => throw new NotSupportedException();
    }
    private sealed class Subjects : IBangumiSubjectDetailsReader
    {
        public Func<string,CancellationToken,Task<BangumiSubjectDetailsResult>> Read = (_,_) => Task.FromResult(new BangumiSubjectDetailsResult(Subject(), false, false, null));
        public Task<BangumiSubjectDetailsResult> LoadAsync(string id, BangumiRelationKind kind, bool refresh, CancellationToken token) => Read(id,token);
    }
    private sealed class Provider : IAniListProvider
    {
        public Task<AniListMedia[]> SearchAsync(AniListSource source, string? query, CancellationToken token) => Task.FromResult(new[] { Fresh().Media });
        public Task<AniListMedia> ResolveAsync(AniListReference reference, CancellationToken token) => Task.FromResult(Fresh(reference.Id).Media);
        public Task<AniListDetails> FetchAsync(int id, AniListMediaKind kind, int page, CancellationToken token) => Task.FromResult(Fresh(id));
    }
}
