using System.Net;
using System.Text;
using System.Text.Json;
using TheOrder.Application;
using TheOrder.Infrastructure.Online;
using Xunit;

namespace TheOrder.Tests;

internal static class MusicTestData
{
    internal const string Release = "5ae1dd91-d42c-425b-861b-63a6fe601226", Group = "71f4e5a2-6b44-3be8-9810-c827ee4563b7";
    internal const string Other = "5ae1dd91-d42c-425b-861b-63a6fe601227", Recording = "077d765e-62d5-42f6-8560-0246210b911f";
    internal static readonly DateTimeOffset Stamp = new(2026, 10, 13, 0, 0, 0, TimeSpan.Zero);
    internal static MusicAlbumSource Source() => new("616", "CLANNAD Original SoundTrack", "2004-08-13",
        new(["KSLA-0012～0014"], ["Lia", "riya"], [], null, 3, []));
    internal static MusicBrainzReleaseCandidate Candidate(string id = Release) => new(id, Group, "CLANNAD ORIGINAL SOUNDTRACK", "Various Artists",
        "2004-08-13", "JP", "Official", null, [new("Key Sounds Label", "KSLA-0012"), new("Key Sounds Label", "KSLA-0013"), new("Key Sounds Label", "KSLA-0014")], 3, 3);
    internal static MusicBrainzAlbum Album(string id = Release) => new(Candidate(id), "CLANNAD ORIGINAL SOUNDTRACK", "Album", ["Soundtrack"],
        Enumerable.Range(1, 3).Select(x => new MusicBrainzDisc(x, "", "CD", [new(1, "1", "合成曲目 " + x, "合成音乐家", 68000, Recording,
            [new("composer", "合成作曲者", null)])])).ToArray(), [], null, Stamp);
    internal static BangumiSubjectDetails Details(string id = "616") => new(id, BangumiRelationKind.Music, "CLANNAD Original SoundTrack", "CLANNAD Original SoundTrack",
        null, "合成简介", "2004-08-13", null, null, null, null, Stamp) { MusicFacts = Source().Facts };
    internal static BangumiRelationItem Item(string id = "616") => new(id, BangumiRelationKind.Music, "CLANNAD Original SoundTrack", "CLANNAD Original SoundTrack", "原声音乐", null);
    internal static MusicBrainzSearchResult Search() => new([Candidate()], 1, false, null, null);
    internal const string AlbumJson = """
    {"id":"5ae1dd91-d42c-425b-861b-63a6fe601226","title":"CLANNAD ORIGINAL SOUNDTRACK","status":"Official","date":"2004-08-13","country":"JP",
    "artist-credit":[{"name":"Various Artists","joinphrase":""}],"label-info":[{"catalog-number":"KSLA-0012","label":{"name":"Key Sounds Label"}},{"catalog-number":"KSLA-0013","label":{"name":"Key Sounds Label"}},{"catalog-number":"KSLA-0014","label":{"name":"Key Sounds Label"}}],
    "release-group":{"id":"71f4e5a2-6b44-3be8-9810-c827ee4563b7","title":"CLANNAD ORIGINAL SOUNDTRACK","primary-type":"Album","secondary-types":["Soundtrack"]},
    "media":[{"position":1,"format":"CD","track-count":1,"tracks":[{"position":1,"number":"1","title":"合成曲目一","length":68000,"artist-credit":[{"name":"合成音乐家","joinphrase":""}],"recording":{"id":"077d765e-62d5-42f6-8560-0246210b911f","relations":[{"type":"performance","work":{"relations":[{"type":"composer","artist":{"name":"合成作曲者","id":"9782d6e1-9a4e-4013-b60b-3e050b03a067"}}]}}]}}]},
    {"position":2,"format":"CD","track-count":1,"tracks":[{"position":1,"number":"1","title":"合成曲目二"}]},
    {"position":3,"format":"CD","track-count":1,"tracks":[{"position":1,"number":"1","title":"合成曲目三"}]}]}
    """;
    internal static HttpResponseMessage Response(string body, HttpStatusCode code = HttpStatusCode.OK) => new(code) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    internal sealed class Clock : TimeProvider
    {
        private long milliseconds;
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => Interlocked.Read(ref milliseconds);
        public override DateTimeOffset GetUtcNow() => Stamp.AddMilliseconds(GetTimestamp());
        public Task Delay(TimeSpan delay, CancellationToken token) { token.ThrowIfCancellationRequested(); Interlocked.Add(ref milliseconds, (long)Math.Ceiling(delay.TotalMilliseconds)); return Task.CompletedTask; }
        public void Advance(TimeSpan value) => Interlocked.Add(ref milliseconds, (long)value.TotalMilliseconds);
        public MusicBrainzRateCoordinator Rate() => new(this, Delay);
    }
    internal sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> action) : HttpMessageHandler
    {
        internal List<string> Urls { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Assert.Equal(HttpMethod.Get, request.Method); Assert.Null(request.Headers.Authorization); Assert.False(request.Headers.Contains("Cookie"));
            Assert.Contains("TheOrder/" + AppReleaseIdentity.Version, request.Headers.UserAgent.ToString()); Assert.Contains("https://github.com/Ph-Rank/The-Order", request.Headers.UserAgent.ToString());
            lock (Urls) Urls.Add(request.RequestUri!.AbsoluteUri); return action(request, token);
        }
    }
}
public sealed class MusicBrainzTests
{
    [Theory]
    [InlineData("release", MusicBrainzEntityKind.Release)] [InlineData("release-group", MusicBrainzEntityKind.ReleaseGroup)] [InlineData("recording", MusicBrainzEntityKind.Recording)]
    public void ExternalIdentityRetainsEntityKind(string path, MusicBrainzEntityKind kind)
    { Assert.True(MusicBrainzReference.TryParse("https://musicbrainz.org/" + path + "/" + MusicTestData.Release, out var reference)); Assert.Equal(kind, reference!.Kind); }
    [Theory]
    [InlineData("http://musicbrainz.org/release/")] [InlineData("https://musicbrainz.org.evil/release/")]
    [InlineData("https://musicbrainz.org/recording/../release/")] [InlineData("https://user@musicbrainz.org/release/")]
    public void ExternalIdentityRejectsNoncanonicalOriginOrPath(string prefix)
        => Assert.False(MusicBrainzReference.TryParse(prefix + MusicTestData.Release, out _));
    [Fact] public void CatalogueRangeHasBoundedExplicitExpansion()
    {
        Assert.Equal(new[] { "KSLA-0012", "KSLA-0013", "KSLA-0014" }, MusicBrainzMatching.Catalogues(["KSLA-0012～0014"]));
        Assert.Single(MusicBrainzMatching.Catalogues(["KSLA-0001～9999"]));
    }
    [Fact] public void ExactCatalogueDateAndDiscEvidenceCanSelectUniqueRelease()
        => Assert.Equal(MusicTestData.Release, MusicBrainzMatching.Automatic(MusicTestData.Source(), MusicTestData.Search()));
    [Fact] public void TitleAloneNeverBindsEvenOneHundredPercentSearchScore()
    { var source = MusicTestData.Source() with { Facts = BangumiMusicFacts.Empty, ReleaseDate = null }; Assert.Null(MusicBrainzMatching.Automatic(source, MusicTestData.Search())); }
    [Theory]
    [InlineData("title")][InlineData("date")][InlineData("catalogue")][InlineData("discs")][InlineData("barcode")]
    public void ConflictingEditionFactsCannotBeAutoSelected(string field)
    {
        var candidate = MusicTestData.Candidate(); var source = MusicTestData.Source();
        candidate = field switch { "title" => candidate with { Title = "Different Album" }, "date" => candidate with { Date = "2018-01-01" },
            "catalogue" => candidate with { Labels = [new("Other", "OTHER-12")] }, "discs" => candidate with { DiscCount = 1 }, _ => candidate with { Barcode = "12345678" } };
        if (field == "barcode") source = source with { Facts = source.Facts with { Barcode = "98765432" } };
        Assert.Null(MusicBrainzMatching.Automatic(source, new([candidate], 1, false, null, null)));
    }
    [Fact] public void AmbiguousAndTruncatedSearchesRequireConfirmation()
    {
        Assert.Null(MusicBrainzMatching.Automatic(MusicTestData.Source(), new([MusicTestData.Candidate(), MusicTestData.Candidate(MusicTestData.Other)], 2, false, null, null)));
        Assert.Null(MusicBrainzMatching.Automatic(MusicTestData.Source(), new([MusicTestData.Candidate()], 26, true, null, null)));
    }
    [Fact] public void ExplicitReleaseIsStrongButExplicitRecordingIsNotAnAlbumIdentity()
    {
        var basic = MusicTestData.Source() with { Facts = BangumiMusicFacts.Empty, ReleaseDate = null };
        Assert.Equal(MusicTestData.Release, MusicBrainzMatching.Automatic(basic with { Facts = basic.Facts with { ExternalIds = [new(MusicBrainzEntityKind.Release, MusicTestData.Release)] } }, MusicTestData.Search()));
        Assert.Null(MusicBrainzMatching.Automatic(basic with { Facts = basic.Facts with { ExternalIds = [new(MusicBrainzEntityKind.Recording, MusicTestData.Release)] } }, MusicTestData.Search()));
    }
    [Fact] public void FingerprintIncludesBridgeIdentityAndEditionFacts()
    {
        var source = MusicTestData.Source(); Assert.Equal(64, source.Fingerprint.Length); Assert.Equal(source.Fingerprint, MusicTestData.Source().Fingerprint);
        Assert.NotEqual(source.Fingerprint, (source with { BangumiId = "617" }).Fingerprint);
        Assert.NotEqual(source.Fingerprint, (source with { Facts = source.Facts with { DiscCount = 1 } }).Fingerprint);
    }
    [Fact] public void BangumiInfoboxProvidesMusicFactsWithoutGuessingIdsFromProse()
    {
        using var document = JsonDocument.Parse("""{"id":616,"type":3,"name":"OST","name_cn":"","summary":"","nsfw":false,"date":"2004-08-13","infobox":[{"key":"商品编号","value":"KSLA-0012～0014"},{"key":"艺术家","value":[{"v":"Lia"},{"v":"riya"}]},{"key":"碟片数量","value":"3"},{"key":"website","value":"https://musicbrainz.org/release-group/71f4e5a2-6b44-3be8-9810-c827ee4563b7"}]}""");
        var mapped = BangumiSubjectDetailsProvider.Map("616", BangumiRelationKind.Music, document.RootElement, MusicTestData.Stamp);
        Assert.Equal(3, mapped.MusicFacts!.DiscCount); Assert.Equal(new[] { "Lia", "riya" }, mapped.MusicFacts.Artists);
        Assert.Equal(MusicBrainzEntityKind.ReleaseGroup, Assert.Single(mapped.MusicFacts.ExternalIds).Kind);
    }
    [Fact] public void MultiDiscMappingRetainsRecordingIdsAndWorkComposerCredits()
    {
        using var document = JsonDocument.Parse(MusicTestData.AlbumJson);
        var album = MusicBrainzProvider.MapAlbum(MusicTestData.Release, document.RootElement, MusicTestData.Stamp);
        Assert.Equal(3, album.Discs.Length); Assert.Equal(MusicTestData.Group, album.Release.ReleaseGroupId);
        var track = Assert.Single(album.Discs[0].Tracks); Assert.Equal(MusicTestData.Recording, track.RecordingId);
        Assert.Equal("composer", Assert.Single(track.Credits).Role); Assert.Equal(68000, track.LengthMs);
    }
    [Theory]
    [InlineData("\"track-count\":1", "\"track-count\":2")]
    [InlineData("\"position\":2", "\"position\":1")]
    [InlineData("\"title\":\"合成曲目一\"", "\"title\":\"\"")]
    [InlineData("\"length\":68000", "\"length\":-1")]
    public void MalformedOrIncompleteTrackFactsDoNotMasqueradeAsAlbum(string before, string after)
    {
        using var document = JsonDocument.Parse(MusicTestData.AlbumJson.Replace(before, after));
        Assert.Throws<MusicBrainzException>(() => MusicBrainzProvider.MapAlbum(MusicTestData.Release, document.RootElement, MusicTestData.Stamp));
    }
    [Fact] public void WrongReleaseIdentityIsRejectedAtLookup()
    { using var data = JsonDocument.Parse(MusicTestData.AlbumJson); Assert.Throws<MusicBrainzException>(() => MusicBrainzProvider.MapAlbum(MusicTestData.Other, data.RootElement, MusicTestData.Stamp)); }
    [Theory]
    [InlineData("approved", "false")][InlineData("front", "false")]
    public void CoverArchiveOnlySelectsApprovedFront(string field, string value)
    {
        var json = $$$"""{"release":"http://musicbrainz.org/release/{{{MusicTestData.Release}}}","images":[{"approved":true,"front":true,"thumbnails":{"500":"http://coverartarchive.org/release/{{{MusicTestData.Release}}}/123-500.jpg"}}]}""";
        using var data = JsonDocument.Parse(json.Replace("\"" + field + "\":true", "\"" + field + "\":" + value)); Assert.Null(MusicBrainzProvider.MapCover(MusicTestData.Release, data.RootElement));
    }
    [Fact] public void CanonicalLegacyHttpCoverIsUpgradedAndIdentityChecked()
    {
        using var data = JsonDocument.Parse($$$"""{"release":"http://musicbrainz.org/release/{{{MusicTestData.Release}}}","images":[{"approved":true,"front":true,"thumbnails":{"500":"http://coverartarchive.org/release/{{{MusicTestData.Release}}}/123-500.jpg"}}]}""");
        Assert.Equal("https://coverartarchive.org/release/" + MusicTestData.Release + "/123-500.jpg", MusicBrainzProvider.MapCover(MusicTestData.Release, data.RootElement));
        Assert.Throws<MusicBrainzException>(() => MusicBrainzProvider.MapCover(MusicTestData.Other, data.RootElement));
    }
    [Theory]
    [InlineData("https://archive.org/download/mbid-", "/cover.jpg", true)]
    [InlineData("https://ia800100.us.archive.org/20/items/mbid-", "/cover_thumb500.jpg", true)]
    [InlineData("https://dn721908.ca.archive.org/0/items/mbid-", "/cover_thumb500.jpg", true)]
    [InlineData("https://archive.org/download/other-", "/cover.jpg", false)]
    [InlineData("https://evil.archive.org/download/mbid-", "/cover.jpg", false)]
    [InlineData("https://archive.org/download/mbid-", "/cover.jpg?redirect=http://localhost", false)]
    public void ArtworkRedirectsStayOnExactEditionArchive(string prefix, string suffix, bool allowed)
        => Assert.Equal(allowed, MusicBrainzValidation.CoverUri(prefix + MusicTestData.Release + suffix, MusicTestData.Release));
    [Fact] public void CoverIndexMayFollowVerifiedArchiveCdnButNotAnotherEdition()
    {
        Assert.True(MusicBrainzProvider.ValidCoverIndex(new("https://dn721908.ca.archive.org/0/items/mbid-" + MusicTestData.Release + "/index.json"), MusicTestData.Release));
        Assert.False(MusicBrainzProvider.ValidCoverIndex(new("https://dn721908.ca.archive.org/0/items/mbid-" + MusicTestData.Other + "/index.json"), MusicTestData.Release));
    }
    [Fact] public async Task TitleSearchIsEscapedAndRecordingRefCannotTriggerReleaseLookup()
    {
        var time = new MusicTestData.Clock();
        using var handler = new MusicTestData.Handler((_, _) => Task.FromResult(MusicTestData.Response("{\"count\":0,\"releases\":[]}")));
        using var http = new HttpClient(handler); var provider = new MusicBrainzProvider(http, time.Rate());
        var source = MusicTestData.Source() with { Title = "Album\" OR artist:*", Facts = BangumiMusicFacts.Empty with { ExternalIds = [new(MusicBrainzEntityKind.Recording, MusicTestData.Recording)] } };
        var result = await provider.SearchAsync(source, default); Assert.Empty(result.Candidates);
        Assert.Contains("单曲录音", result.Notice); Assert.InRange(handler.Urls.Count, 1, MusicBrainzSearchQueries.MaximumTitleQueries); Assert.Contains("query=release%3A", handler.Urls[0]);
        Assert.All(handler.Urls, url => Assert.DoesNotContain("/recording/", url));
    }
    [Fact] public async Task EveryViewAndRetryShareMonotonicOneSecondPacing()
    {
        var time = new MusicTestData.Clock(); var rate = time.Rate(); var starts = new List<long>(); int calls = 0;
        using var handler = new MusicTestData.Handler((_, _) =>
        {
            starts.Add(time.GetTimestamp()); var response = MusicTestData.Response("{\"count\":1,\"releases\":[" + MusicTestData.AlbumJson + "]}", ++calls == 1 ? HttpStatusCode.TooManyRequests : HttpStatusCode.OK);
            if (calls == 1) response.Headers.RetryAfter = new(TimeSpan.FromSeconds(3)); return Task.FromResult(response);
        });
        using var http = new HttpClient(handler); var a = new MusicBrainzProvider(http, rate, clock: time.GetUtcNow); var b = new MusicBrainzProvider(http, rate, clock: time.GetUtcNow);
        var source = MusicTestData.Source() with { Facts = BangumiMusicFacts.Empty };
        await Task.WhenAll(a.SearchAsync(source, default), b.SearchAsync(source, default));
        Assert.Equal(3, starts.Count); Assert.True(starts[1] - starts[0] >= 3000);
        Assert.All(starts.Zip(starts.Skip(1)), pair => Assert.True(pair.Second - pair.First >= 1100));
    }
    [Fact] public async Task PrefixAndMisspelledVersionFallbackFindsCandidateWithoutAutoBinding()
    {
        var source = MusicTestData.Source() with { Title = "CLANNAD 願いが叶う場所 ～Vocal & Hamony version～", ReleaseDate = "2009-02-25", Facts = BangumiMusicFacts.Empty };
        var calls = new List<string>();
        using var handler = new MusicTestData.Handler((request, _) =>
        {
            var query = Uri.UnescapeDataString(request.RequestUri!.Query); calls.Add(query);
            var json = query.Contains("release:\"願いが叶う場所\" AND date:", StringComparison.Ordinal)
                ? "{\"count\":1,\"releases\":[" + MusicTestData.AlbumJson.Replace("CLANNAD ORIGINAL SOUNDTRACK", "願いが叶う場所 ～Vocal&Harmony version～").Replace("2004-08-13", "2009-02-25") + "]}"
                : "{\"count\":0,\"releases\":[]}";
            return Task.FromResult(MusicTestData.Response(json));
        });
        using var http = new HttpClient(handler); var result = await new MusicBrainzProvider(http, new MusicTestData.Clock().Rate()).SearchAsync(source, default);
        Assert.Single(result.Candidates); Assert.Equal(3, calls.Count); Assert.Contains("标题片段", result.Notice);
        Assert.Null(MusicBrainzMatching.Automatic(source, result));
    }
    [Fact] public async Task ExplicitReleaseGroupListsOnlyItsEditionsAndNeverTreatsGroupAsRelease()
    {
        using var handler = new MusicTestData.Handler((_, _) => Task.FromResult(MusicTestData.Response("{\"release-count\":1,\"releases\":[" + MusicTestData.AlbumJson + "]}")));
        using var http = new HttpClient(handler); var provider = new MusicBrainzProvider(http, new MusicTestData.Clock().Rate());
        var source = MusicTestData.Source() with { Facts = BangumiMusicFacts.Empty with { ExternalIds = [new(MusicBrainzEntityKind.ReleaseGroup, MusicTestData.Group)] } };
        var value = await provider.SearchAsync(source, default); Assert.Single(value.Candidates); Assert.Contains("release-group=" + MusicTestData.Group, handler.Urls[0]);
        Assert.DoesNotContain("/release/" + MusicTestData.Group, handler.Urls[0]); Assert.Null(MusicBrainzMatching.Automatic(source, value));
    }
    [Fact] public void ExternalGroupConflictAndUnofficialCandidatesCannotAutoBind()
    {
        var source = MusicTestData.Source() with { Facts = MusicTestData.Source().Facts with { ExternalIds = [new(MusicBrainzEntityKind.ReleaseGroup, MusicTestData.Other)] } };
        Assert.Null(MusicBrainzMatching.Automatic(source, MusicTestData.Search()));
        Assert.Null(MusicBrainzMatching.Automatic(MusicTestData.Source(), MusicTestData.Search() with { Candidates = [MusicTestData.Candidate() with { Status = "Bootleg" }] }));
    }
    [Fact] public void DifferentLanguagesCanFallBackToArtistAndReleaseDateWithoutChangingIdentity()
    {
        var source = MusicTestData.Source() with { Title = "全く異なる名前", Facts = BangumiMusicFacts.Empty with { Artists = ["Lia"] } };
        var queries = MusicBrainzSearchQueries.Fallback(source);
        Assert.Contains("artist:\"Lia\" AND date:\"2004-08-13\"", queries); Assert.InRange(queries.Length, 1, 4);
        Assert.Equal("全く異なる名前", source.Title); Assert.Null(MusicBrainzMatching.Automatic(source, MusicTestData.Search()));
    }
    [Fact] public async Task ExhaustedRetriesAndOfflineAreFailuresNotEmptyMatches()
    {
        using var handler = new MusicTestData.Handler((_, _) => Task.FromResult(MusicTestData.Response("{}", HttpStatusCode.ServiceUnavailable)));
        using var http = new HttpClient(handler); var provider = new MusicBrainzProvider(http, new MusicTestData.Clock().Rate());
        Assert.Equal(MusicBrainzFailure.RateLimited, (await Assert.ThrowsAsync<MusicBrainzException>(() => provider.SearchAsync(MusicTestData.Source(), default))).Failure);
        Assert.Equal(3, handler.Urls.Count);
        using var broken = new MusicTestData.Handler((_, _) => throw new HttpRequestException()); using var noConnection = new HttpClient(broken);
        Assert.Equal(MusicBrainzFailure.Offline, (await Assert.ThrowsAsync<MusicBrainzException>(() => new MusicBrainzProvider(noConnection, new MusicTestData.Clock().Rate()).SearchAsync(MusicTestData.Source(), default))).Failure);
    }
    [Fact] public async Task CallerCancelAndDeadlineAreDifferentOutcomes()
    {
        using var handler = new MusicTestData.Handler(async (_, token) => { await Task.Delay(Timeout.Infinite, token); return MusicTestData.Response("{}"); });
        using var http = new HttpClient(handler); var provider = new MusicBrainzProvider(http, new MusicTestData.Clock().Rate(), TimeSpan.FromMilliseconds(40));
        Assert.Equal(MusicBrainzFailure.Timeout, (await Assert.ThrowsAsync<MusicBrainzException>(() => provider.SearchAsync(MusicTestData.Source(), default))).Failure);
        using var stop = new CancellationTokenSource(); stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.SearchAsync(MusicTestData.Source(), stop.Token));
    }
    [Fact] public async Task CredentialsAndOversizeResponsesAreRejected()
    {
        using var handler = new MusicTestData.Handler((_, _) => { var response = MusicTestData.Response("{}"); response.Content.Headers.ContentLength = MusicBrainzProvider.MaximumResponseBytes + 1; return Task.FromResult(response); });
        using var http = new HttpClient(handler); var provider = new MusicBrainzProvider(http, new MusicTestData.Clock().Rate());
        Assert.Equal(MusicBrainzFailure.ResponseTooLarge, (await Assert.ThrowsAsync<MusicBrainzException>(() => provider.SearchAsync(MusicTestData.Source(), default))).Failure);
        http.DefaultRequestHeaders.Authorization = new("Bearer", "synthetic-secret");
        await Assert.ThrowsAsync<MusicBrainzException>(() => provider.SearchAsync(MusicTestData.Source(), default)); Assert.Single(handler.Urls);
    }
    [Fact] public async Task MissingCoverNeverLosesAlbumAndCaaCannotRedirectToArbitraryHost()
    {
        var time = new MusicTestData.Clock();
        using var handler = new MusicTestData.Handler((request, _) =>
        {
            if (request.RequestUri!.Host == "musicbrainz.org") return Task.FromResult(MusicTestData.Response(MusicTestData.AlbumJson));
            var response = new HttpResponseMessage(HttpStatusCode.TemporaryRedirect); response.Headers.Location = new("https://localhost/private"); return Task.FromResult(response);
        });
        using var http = new HttpClient(handler); var value = await new MusicBrainzProvider(http, time.Rate()).FetchAlbumAsync(MusicTestData.Release, default);
        Assert.Equal(3, value.Discs.Length); Assert.Null(value.CoverUrl); Assert.NotNull(value.CoverWarning); Assert.Equal(2, handler.Urls.Count);
    }
}
