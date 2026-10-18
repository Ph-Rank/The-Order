using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using TheOrder.Application;
using TheOrder.Infrastructure.Online;
using TheOrder.Infrastructure.Legacy;
using TheOrder.Platform.Windows;
using TheOrder.Presentation;
using Xunit;

namespace TheOrder.Tests;

public sealed class MangaDexBookLinksTests
{
    private static string Fixture(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "book-outlets", name + "-native.json"));
    internal static AniListMedia Manga(int id = 118586) => BookOutletsTests.Details(id).Media with
        { Title = "Sousou no Frieren", NativeTitle = "葬送のフリーレン", EnglishTitle = "Frieren: Beyond Journey's End", Synonyms = [], MalId = 126287 };
    internal static BookLinkSupplement Snapshot() { using var json = JsonDocument.Parse(Fixture("frieren")); return MangaDexBookLinksProvider.Map(json.RootElement, Manga(), Manga().NativeTitle, AniListTestData.Stamp); }
    [Theory]
    [InlineData("frieren", 118586, "葬送のフリーレン", 126287)]
    [InlineData("berserk", 30002, "ベルセルク", 2)]
    [InlineData("witch", 128896, "ウィッチウォッチ", 133545)]
    public void RealBoundedMetadataAddsSuppliedEbookLinksWithCrossReferenceEvidence(string name, int id, string title, int mal)
    {
        using var json = JsonDocument.Parse(Fixture(name)); var source = Manga(id) with { Title = title, NativeTitle = title, EnglishTitle = "", MalId = mal };
        var result = MangaDexBookLinksProvider.Map(json.RootElement, source, title, AniListTestData.Stamp);
        Assert.True(result.Matches(source)); Assert.NotNull(result.MangaDexId);
        var links = result.Outlets().Links; var ebook = Assert.Single(links, x => x.Platform == "ebookjapan");
        Assert.Equal(BookOutletKind.Ebook, ebook.Kind); Assert.Equal(BookOutletLevel.Series, ebook.Level);
        var evidence = Assert.Single(ebook.Sources); Assert.Equal("MangaDex", evidence.Identity.Provider); Assert.Equal("ebj", evidence.Original.Type);
        Assert.Contains("AniList:" + id, evidence.CrossReference); Assert.Null(ebook.Language); Assert.Equal("JP", ebook.Storefront);
        Assert.DoesNotContain(links, x => x.Url.Contains("bookwalker", StringComparison.Ordinal)); // bw is a slug, never guessed into a URL.
    }
    [Theory]
    [InlineData("missing-id")][InlineData("other-id")][InlineData("duplicate-id")][InlineData("partial")][InlineData("other-mal")][InlineData("other-title")]
    public void TitlesPartialSearchesAndConflictingIdsCannotCreateSupplementaryLinks(string change)
    {
        var json = JsonNode.Parse(Fixture("frieren"))!.AsObject();
        var rows = json["data"]!.AsArray(); var selected = rows.Single(x => x!["attributes"]!["links"]?["al"]?.GetValue<string>() == "118586")!;
        var attrs = selected["attributes"]!;
        switch (change)
        {
            case "missing-id": attrs["links"]!.AsObject().Remove("al"); break;
            case "other-id": attrs["links"]!["al"] = "99999"; break;
            case "duplicate-id": rows.Add(selected.DeepClone()); json["total"] = rows.Count; break;
            case "partial": json["total"] = rows.Count + 1; break;
            case "other-mal": attrs["links"]!["mal"] = "2"; break;
            case "other-title": attrs["title"] = new JsonObject { ["ja"] = "Other comic" }; attrs["altTitles"] = new JsonArray(); break;
        }
        using var doc = JsonDocument.Parse(json.ToJsonString()); var value = MangaDexBookLinksProvider.Map(doc.RootElement, Manga(), Manga().NativeTitle, AniListTestData.Stamp);
        Assert.Null(value.MangaDexId); Assert.Empty(value.Outlets().Links);
    }
    [Fact]
    public void SameNameNovelNeverUsesMangaLinksAndIdentifierOnlyBookwalkerIsNotFabricated()
    {
        using var json = JsonDocument.Parse(Fixture("kara"));
        Assert.Throws<InvalidDataException>(() => MangaDexBookLinksProvider.Map(json.RootElement, Manga(53947) with { Format = "NOVEL" }, "空の境界", AniListTestData.Stamp));
        var manga = Manga(53947) with { Title = "Kara no Kyoukai: The Garden of Sinners", NativeTitle = "空の境界 the Garden of sinners", MalId = 23947 };
        var result = MangaDexBookLinksProvider.Map(json.RootElement, manga, manga.NativeTitle, AniListTestData.Stamp);
        Assert.NotNull(result.MangaDexId); Assert.Empty(result.Outlets().Links);
    }
    [Fact]
    public async Task TransportOnlyRequestsTheFixedMetadataApiWithAnEncodedTitle()
    {
        var clock = new AniListTestData.Clock();
        using var handler = new AniListTestData.Handler((request, _) =>
        {
            Assert.Equal("api.mangadex.org", request.RequestUri!.Host); Assert.Equal("/manga", request.RequestUri.AbsolutePath);
            Assert.Contains("limit=10", request.RequestUri.Query); Assert.Contains(Uri.EscapeDataString(Manga().NativeTitle), request.RequestUri.OriginalString);
            Assert.Equal(HttpMethod.Get, request.Method); Assert.Null(request.Headers.Authorization);
            return Task.FromResult(AniListTestData.Response(Fixture("frieren")));
        });
        using var client = new HttpClient(handler); var provider = new MangaDexBookLinksProvider(client, clock.Rate(), () => clock.Now);
        Assert.NotNull((await provider.FetchAsync(Manga(), default)).MangaDexId); Assert.Equal(1, handler.Calls);
    }
    [Theory][InlineData(302)][InlineData(403)][InlineData(429)][InlineData(500)]
    public async Task FailureAndRedirectNeverFetchAnyPlatformOrRetryUnboundedly(int status)
    {
        var clock = new AniListTestData.Clock();
        using var handler = new AniListTestData.Handler((_, _) => Task.FromResult(AniListTestData.Response("{}", (HttpStatusCode)status)));
        using var client = new HttpClient(handler); var provider = new MangaDexBookLinksProvider(client, clock.Rate(), () => clock.Now);
        await Assert.ThrowsAsync<AniListException>(() => provider.FetchAsync(Manga(), default)); Assert.Equal(1, handler.Calls);
    }
    [Fact]
    public async Task TimeoutIsSeparateFromUserCancellation()
    {
        var clock = new AniListTestData.Clock();
        using var handler = new AniListTestData.Handler(async (_, token) => { await Task.Delay(Timeout.InfiniteTimeSpan, token); throw new InvalidOperationException(); });
        using var client = new HttpClient(handler); var provider = new MangaDexBookLinksProvider(client, clock.Rate(), () => clock.Now, TimeSpan.FromMilliseconds(80));
        var error = await Assert.ThrowsAsync<AniListException>(() => provider.FetchAsync(Manga(), default)); Assert.Equal(AniListFailure.Timeout, error.Failure);
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.FetchAsync(Manga(), cancel.Token));
    }
    [Fact]
    public async Task LateSupplementCannotPolluteAnotherBookAndFailureDoesNotRemoveCoreLinks()
    {
        var pending = new TaskCompletionSource<BookLinkSupplementResult>(); var supplement = new Supplement { Load = (_,_) => pending.Task };
        var details = BookOutletsTests.Details(118586) with { Media = Manga() };
        using var model = new BookReadingViewModel(new Reader(), new Subjects(), supplement);
        model.SetSource(BookSource.From(details), details, null); var work = model.ExpandAsync();
        var next = details with { Media = details.Media with { Id = 30104 } }; model.SetSource(BookSource.From(next), next, null);
        pending.SetResult(new(Snapshot(), null)); await work; Assert.Empty(model.Links); Assert.Equal("30104", model.Source!.Identity.Id);
        supplement.Load = (_,_) => throw new AniListException(AniListFailure.Offline, "synthetic offline");
        var core = details with { BookLinks = [BookOutletsTests.Raw("https://yenpress.com/series/frieren")] };
        model.SetSource(BookSource.From(core), core, null); await model.ExpandAsync(); Assert.Single(model.Links); Assert.Contains("已有入口", model.Message);
    }
    [Fact]
    public async Task SameAniListIdWithChangedCorroboratingIdCannotKeepAnOldSupplementVisible()
    {
        var details = BookOutletsTests.Details(118586) with { Media = Manga() };
        using var model = new BookReadingViewModel(new Reader(), new Subjects(), new Supplement());
        model.SetSource(BookSource.From(details), details, null); await model.ExpandAsync(); Assert.Contains(model.Links, x => x.Platform == "ebookjapan");
        var changed = details with { Media = details.Media with { MalId = 2 }, FetchedAtUtc = details.FetchedAtUtc.AddMinutes(1) };
        Assert.Equal(BookSource.From(details).Fingerprint, BookSource.From(changed).Fingerprint);
        model.SetSource(BookSource.From(changed), changed, null); Assert.Empty(model.Links);
    }
    [Fact]
    public async Task SupplementCacheUsesExistingProfileStorageAndSurvivesRestartWithoutUserMappingWrites()
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "the-order-book-supplement-" + Guid.NewGuid().ToString("N"))); NativeProfileStore? profile = null;
        try
        {
            profile = NativeProfileStore.Open(new(NativeProfileKind.Test, root), new NativeLibraryCodec());
            var original = File.ReadAllBytes(Path.Combine(root, "library.json")); var supplement = new Supplement();
            AniListService Service() => new(new Provider(), new IsolatedAniListCache(profile!.Configuration()), () => AniListTestData.Stamp, bookSupplementProvider: supplement);
            var service = Service(); var first = await service.LoadBookLinksAsync(Manga(), false, default); Assert.NotNull(first.Value.MangaDexId); Assert.Equal(1, supplement.Calls);
            profile.Dispose(); profile = NativeProfileStore.Open(new(NativeProfileKind.Test, root), new NativeLibraryCodec());
            service = Service(); await service.LoadBookLinksAsync(Manga(), false, default); Assert.Equal(1, supplement.Calls);
            supplement.Fetch = (_,_) => throw new AniListException(AniListFailure.Offline, "synthetic offline");
            var offline = await service.LoadBookLinksAsync(Manga(), true, default); Assert.Equal(first.Value.MangaDexId, offline.Value.MangaDexId); Assert.NotNull(offline.Warning);
            supplement.Fetch = (_,_) => Task.FromResult(Snapshot() with { AniListId = 99999 });
            var conflict = await service.LoadBookLinksAsync(Manga(), true, default); Assert.Equal(first.Value.MangaDexId, conflict.Value.MangaDexId); Assert.NotNull(conflict.Warning);
            profile.Dispose(); profile = NativeProfileStore.Open(new(NativeProfileKind.Test, root), new NativeLibraryCodec());
            service = Service(); Assert.Equal(first.Value.MangaDexId, (await service.LoadBookLinksAsync(Manga(), false, default)).Value.MangaDexId);
            await Assert.ThrowsAsync<InvalidDataException>(() => service.LoadBookLinksAsync(Manga() with { MalId = 2 }, false, default));
            Assert.Equal(original, File.ReadAllBytes(Path.Combine(root, "library.json")));
            var cache = Path.GetFullPath(Path.Combine(root, IsolatedAniListCache.DirectoryName)); Assert.StartsWith(root + Path.DirectorySeparatorChar, cache, StringComparison.OrdinalIgnoreCase); Directory.Delete(cache, true);
            var decisions = new IsolatedMediaIdentityDecisions(profile.Configuration());
            var context = MediaIdentityContext.From(AniListSource.From(BookReadingViewModelTests.Subject()));
            Assert.Empty((await decisions.ReadAsync(context, default)).Decisions); // A metadata assertion never confirms a user mapping.
        }
        finally
        {
            profile?.Dispose(); var temp = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!root.StartsWith(temp, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(root).StartsWith("the-order-book-supplement-", StringComparison.Ordinal)) throw new InvalidOperationException();
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
    private sealed class Supplement : IBookOutletSupplementProvider, IBookOutletSupplementReader
    {
        public int Calls;
        public Func<AniListMedia,CancellationToken,Task<BookLinkSupplement>> Fetch = (_,_) => Task.FromResult(Snapshot());
        public Func<AniListMedia,CancellationToken,Task<BookLinkSupplementResult>> Load = (_,_) => Task.FromResult(new BookLinkSupplementResult(Snapshot(), null));
        public Task<BookLinkSupplement> FetchAsync(AniListMedia source, CancellationToken token) { Calls++; return Fetch(source, token); }
        public Task<BookLinkSupplementResult> LoadBookLinksAsync(AniListMedia source, bool refresh, CancellationToken token) => Load(source, token);
    }
    private sealed class Reader : IAniListReader
    {
        public Task<AniListResult> LoadAsync(int id, AniListMediaKind kind, bool refresh, int page, CancellationToken token) => throw new NotSupportedException();
        public Task<AniListMatch> MatchAsync(AniListSource source, string? query, bool refresh, CancellationToken token) => throw new NotSupportedException();
        public Task<AniListMatch> FindCandidatesAsync(AniListSource source, bool refresh, CancellationToken token) => throw new NotSupportedException();
        public Task ConfirmAsync(AniListSource source, AniListDetails details, CancellationToken token) => throw new NotSupportedException();
    }
    private sealed class Subjects : IBangumiSubjectDetailsReader
    { public Task<BangumiSubjectDetailsResult> LoadAsync(string id, BangumiRelationKind kind, bool refresh, CancellationToken token) => throw new NotSupportedException(); }
    private sealed class Provider : IAniListProvider
    {
        public Task<AniListMedia[]> SearchAsync(AniListSource source, string? query, CancellationToken token) => throw new NotSupportedException();
        public Task<AniListMedia> ResolveAsync(AniListReference reference, CancellationToken token) => throw new NotSupportedException();
        public Task<AniListDetails> FetchAsync(int id, AniListMediaKind kind, int page, CancellationToken token) => throw new NotSupportedException();
    }
}
