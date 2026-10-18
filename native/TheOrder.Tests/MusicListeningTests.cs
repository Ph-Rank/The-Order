using System.Net;
using System.Text;
using System.Text.Json;
using TheOrder.Application;
using TheOrder.Infrastructure.Online;
using TheOrder.Infrastructure.Legacy;
using TheOrder.Platform.Windows;
using TheOrder.Presentation;
using Xunit;

namespace TheOrder.Tests;

internal static class OutletData
{
    public static readonly DateTimeOffset Now = MusicTestData.Stamp;
    public static MusicBrainzReference Ref(string id = MusicTestData.Release) => new(MusicBrainzEntityKind.Release, id);
    public static MusicOutletSnapshot Snapshot(string id = MusicTestData.Release) => new(Ref(id), "合成专辑", "合成音乐人", MusicTestData.Group, ["CD"], 12, "2020-01-01", null,
        [MusicOutlets.FromRelationship(Ref(id), "free streaming", "forward", false, "https://open.spotify.com/album/09vpDdZGprjKr4tleWomBV", Now)!], Now) { ReleaseStatus = "Official" };
    public static AppleAlbumCandidate Apple(string id = "123") => new(id, "JP", "合成专辑", "合成音乐人", "https://music.apple.com/jp/album/synthetic/" + id, "2020-01-01", 12, "合成目录检索");
    public sealed class Provider : IMusicBrainzProvider, IMusicOutletProvider
    {
        public int Calls, EditionCalls; public bool Fail;
        public MusicBrainzSearchResult Editions = new([], 0, false, null, null);
        public Func<MusicBrainzReference,MusicOutletSnapshot> Data = reference => Snapshot(reference.Id);
        public Func<MusicBrainzReference,Task<MusicOutletSnapshot>>? AsyncData;
        public Task<MusicOutletSnapshot> FetchOutletsAsync(MusicBrainzReference reference, CancellationToken cancellation)
        { Calls++; cancellation.ThrowIfCancellationRequested(); if (Fail) throw new MusicBrainzException(MusicBrainzFailure.RateLimited, "合成429"); return AsyncData is not null ? AsyncData(reference) : Task.FromResult(Data(reference)); }
        public Task<MusicBrainzSearchResult> EditionsAsync(string id, CancellationToken token) { EditionCalls++; return Task.FromResult(Editions); }
        public Task<MusicBrainzSearchResult> SearchAsync(MusicAlbumSource source, CancellationToken token) => throw new NotSupportedException();
        public Task<MusicBrainzAlbum> FetchAlbumAsync(string id, CancellationToken token) => throw new NotSupportedException();
    }
    public sealed class AppleProvider : IAppleCatalogProvider
    {
        public int Calls;
        public Task<AppleCatalogResult> SearchAsync(AppleCatalogQuery query, CancellationToken token)
        { Calls++; return Task.FromResult(new AppleCatalogResult(query, [Apple()], 0, false, Now)); }
    }
    public sealed class Cache : IMusicBrainzCache
    {
        public Dictionary<string,MusicBrainzCacheValue> Values = [];
        public Task<MusicBrainzCacheValue?> ReadAsync(string key, CancellationToken token) => Task.FromResult(Values.GetValueOrDefault(key));
        public Task WriteAsync(MusicBrainzCacheValue value, CancellationToken token) { Values[value.Key] = value; return Task.CompletedTask; }
    }
}
public sealed class MusicOutletPolicyTests
{
    [Theory]
    [InlineData("free streaming", MusicBrainzEntityKind.Release, "https://open.spotify.com/album/09vpDdZGprjKr4tleWomBV", MusicOutletKind.Listening, "Album")]
    [InlineData("streaming", MusicBrainzEntityKind.Release, "https://music.youtube.com/playlist?list=OLAK5uy_lDYNwTELHncx4saX3DfVgVuvTVI-pjweU", MusicOutletKind.Listening, "Album")]
    [InlineData("streaming", MusicBrainzEntityKind.Recording, "https://music.youtube.com/watch?v=0123456789A", MusicOutletKind.Listening, "Recording")]
    [InlineData("streaming", MusicBrainzEntityKind.Release, "https://music.youtube.com/playlist?list=PL-arbitrary-playlist", MusicOutletKind.Information, "Page")]
    [InlineData("streaming", MusicBrainzEntityKind.Release, "https://y.qq.com/n/yqq/album/001abc.html", MusicOutletKind.Listening, "Album")]
    [InlineData("streaming", MusicBrainzEntityKind.Release, "https://y.qq.com/n/yqq/album.html", MusicOutletKind.Information, "Page")]
    [InlineData("discography entry", MusicBrainzEntityKind.Release, "https://open.spotify.com/album/09vpDdZGprjKr4tleWomBV", MusicOutletKind.Information, "Album")]
    [InlineData("free streaming", MusicBrainzEntityKind.Recording, "https://open.spotify.com/album/09vpDdZGprjKr4tleWomBV", MusicOutletKind.Information, "Album")]
    [InlineData("free streaming", MusicBrainzEntityKind.Recording, "https://music.163.com/#/song?id=123", MusicOutletKind.Listening, "Recording")]
    [InlineData("free streaming", MusicBrainzEntityKind.Release, "https://music.163.com/#/album?id=123", MusicOutletKind.Listening, "Album")]
    [InlineData("streaming", MusicBrainzEntityKind.Release, "https://y.qq.com/n/ryqq/albumDetail/003abcDEF", MusicOutletKind.Listening, "Album")]
    [InlineData("purchase for download", MusicBrainzEntityKind.Release, "https://artist.bandcamp.com/album/synthetic", MusicOutletKind.Purchase, "Album")]
    [InlineData("free streaming", MusicBrainzEntityKind.Release, "https://example.com/album/123", MusicOutletKind.Information, "Page")]
    [InlineData("free streaming", MusicBrainzEntityKind.Release, "https://music.apple.com/jp/album/album/123?i=456", MusicOutletKind.Information, "Recording")]
    public void RelationshipDomainAndLayerAllConstrainListening(string type, MusicBrainzEntityKind kind, string url, MusicOutletKind expected, string layer)
    {
        var value = MusicOutlets.FromRelationship(new(kind, MusicTestData.Release), type, "forward", false, url, OutletData.Now);
        Assert.NotNull(value); Assert.True(MusicOutlets.Valid(value)); Assert.Equal(expected, value.Kind); Assert.Equal(layer, value.TargetLayer);
    }
    [Theory]
    [InlineData("file:///C:/private.mp3")][InlineData("https://127.0.0.1/album/123")][InlineData("https://user:pass@music.163.com/album?id=123")]
    [InlineData("javascript:alert(1)")][InlineData("https://music.163.com:444/album?id=123")]
    public void UnsafeLinksAreNotLaunchedOrClassified(string url) => Assert.Null(MusicOutlets.FromRelationship(OutletData.Ref(), "free streaming", "forward", false, url, OutletData.Now));
    [Fact]
    public void EndedBackwardAndWorkLinksCannotBecomePlayback()
    {
        var url = "https://open.spotify.com/album/09vpDdZGprjKr4tleWomBV";
        Assert.Null(MusicOutlets.FromRelationship(OutletData.Ref(), "streaming", "backward", false, url, OutletData.Now));
        Assert.Null(MusicOutlets.FromRelationship(OutletData.Ref(), "streaming", "forward", true, url, OutletData.Now));
        Assert.Null(MusicOutlets.FromRelationship(new(MusicBrainzEntityKind.Work, MusicTestData.Release), "streaming", "forward", false, url, OutletData.Now));
    }
    [Fact]
    public void MergeKeepsOtherEditionsAndDomesticAndInternationalTargets()
    {
        var first = OutletData.Snapshot().Links[0];
        var duplicate = first with { Url = first.Url + "?si=tracking", Sources = [new("第二来源", first.Entity.Url, "同一目标")] };
        var other = first with { Entity = OutletData.Ref(MusicTestData.Other) };
        var domestic = MusicOutlets.FromRelationship(first.Entity, "streaming", "forward", false, "https://music.163.com/#/album?id=123", OutletData.Now)!;
        var merged = MusicOutlets.Merge([first, duplicate, other, domestic], MusicOutletSort.DomesticFirst);
        Assert.Equal(3, merged.Length); Assert.Equal("网易云音乐", merged[0].Platform); Assert.Equal("https://music.163.com/album?id=123", merged[0].Url);
        Assert.Equal(2, merged.Single(x => x.Entity == first.Entity && x.Platform == "Spotify").Sources.Length);
        Assert.Equal(2, merged.Count(x => x.Platform == "Spotify")); Assert.Empty(first.Regions);
    }
    [Fact]
    public void ParsingMismatchedSourceIdCannotContaminateTheSnapshot()
    {
        using var json = JsonDocument.Parse(MusicTestData.AlbumJson);
        Assert.Throws<MusicBrainzException>(() => MusicBrainzProvider.MapOutlets(OutletData.Ref(MusicTestData.Other), json.RootElement, OutletData.Now));
        Assert.False(MusicBrainzValidation.Cache(new("outlets:1:" + MusicTestData.Other, OutletData.Now) { Outlets = OutletData.Snapshot() }));
        Assert.False(MusicOutlets.Valid(OutletData.Snapshot().Links[0] with { Url = "https://evil.example/album/123" }));
        Assert.False(MusicOutlets.Valid(OutletData.Snapshot().Links[0] with { RelationshipType = "discography entry" }));
        Assert.False(MusicBrainzValidation.Cache(new("apple:" + new string('A',64), OutletData.Now) { Apple = new(null!, [], 0, false, OutletData.Now) }));
    }
    [Theory]
    [InlineData(MusicBrainzEntityKind.Recording)][InlineData(MusicBrainzEntityKind.Work)][InlineData(MusicBrainzEntityKind.ReleaseGroup)]
    public void RecordingWorkAndGroupCannotBeRecastAsAReleaseForUserChoices(MusicBrainzEntityKind kind)
        => Assert.Throws<InvalidDataException>(() => MediaIdentityContext.ForMusicOutlet(new(kind, MusicTestData.Release)));
}
public sealed class AppleMusicCatalogTests
{
    private static AppleCatalogQuery Query() => new(OutletData.Ref(), "合成专辑", "合成音乐人", "123456789012", "JP");
    private static string Row(string title = "合成专辑", string url = "https://music.apple.com/jp/album/synthetic/123", string country = "JPN") => JsonSerializer.Serialize(new
    { wrapperType = "collection", collectionType = "Album", collectionId = 123, collectionName = title, artistName = "合成音乐人", collectionViewUrl = url, trackCount = 12, releaseDate = "2020-01-01T00:00:00Z", country, previewUrl = "https://example.com/never-download.mp3" });
    private static AppleCatalogResult Map(string row, AppleCatalogQuery? query = null)
    { using var json = JsonDocument.Parse("{\"resultCount\":1,\"results\":[" + row + "]}"); return AppleCatalogProvider.Map(query ?? Query(), json.RootElement, "合成查询", OutletData.Now); }
    [Fact]
    public void OfficialBarcodeResultWithUnrelatedTitleIsNotAnIdentityProof()
    {
        var result = Map(Row("Songs of the Seafarer")); Assert.Empty(result.Candidates); Assert.Equal(1, result.Discarded);
    }
    [Fact]
    public void TitlesContainingOnlySymbolsCannotCreateTitleEvidence()
        => Assert.Empty(Map(Row("♡"), Query() with { Title = "★" }).Candidates);
    [Theory]
    [InlineData("https://music.apple.com/us/album/synthetic/123", "USA")]
    [InlineData("https://music.apple.com/jp/album/synthetic/123?i=456", "JPN")]
    [InlineData("https://music.apple.com/jp/album/synthetic/123?i=456&i=789", "JPN")]
    [InlineData("https://music.apple.com/jp/album/synthetic/123?%69=456", "JPN")]
    [InlineData("https://evil.example/jp/album/synthetic/123", "JPN")]
    public void WrongStoreTrackOrHostIsNotAnAlbumCandidate(string url, string country) => Assert.Empty(Map(Row(url: url, country: country)).Candidates);
    [Fact]
    public void CatalogMetadataNeverCachesArtworkPreviewOrClaimsSubscriptionAvailability()
    {
        var result = Map(Row()); Assert.True(result.IsValid); var saved = JsonSerializer.Serialize(result);
        Assert.DoesNotContain("previewUrl", saved); Assert.DoesNotContain("never-download", saved); Assert.DoesNotContain("artwork", saved);
        Assert.Equal("JP", Assert.Single(result.Candidates).Country);
    }
    [Fact]
    public void ExplicitIdCanSurviveTitleChangesButNotBecomeADifferentId()
    {
        var result = Map(Row("更名后的专辑"), Query() with { ExplicitAlbumId = "123" }); Assert.Single(result.Candidates);
        Assert.Empty(Map(Row(), Query() with { ExplicitAlbumId = "456" }).Candidates);
    }
    [Fact]
    public async Task OfficialLookupFallsBackWhenBarcodeReturnsAnUnrelatedAlbum()
    {
        using var handler = new Handler(); using var http = new HttpClient(handler);
        var result = await new AppleCatalogProvider(http).SearchAsync(Query(), default);
        Assert.Single(result.Candidates); Assert.Equal(1, result.Discarded); Assert.Equal(2, handler.Paths.Count);
        Assert.StartsWith("/lookup?upc=", handler.Paths[0]); Assert.StartsWith("/search?", handler.Paths[1]);
        Assert.All(handler.Paths, path => { Assert.DoesNotContain("preview", path); Assert.DoesNotContain("artwork", path); });
    }
    [Theory]
    [InlineData(500, MusicBrainzFailure.Server)]
    [InlineData(429, MusicBrainzFailure.RateLimited)]
    [InlineData(302, MusicBrainzFailure.InvalidResponse)]
    public async Task CatalogHttpFailuresAreNotEmptyResultsOrIdentityChoices(int status, MusicBrainzFailure expected)
    {
        using var handler = new FailureHandler(status); using var http = new HttpClient(handler);
        var error = await Assert.ThrowsAsync<MusicBrainzException>(() => new AppleCatalogProvider(http).SearchAsync(Query() with { ExplicitAlbumId = "123" }, default));
        Assert.Equal(expected, error.Failure); Assert.Equal(1, handler.Calls);
    }
    [Fact]
    public async Task HttpDeadlineAndCallerCancellationRemainDifferentOutcomes()
    {
        using var handler = new FailureHandler(null); using var http = new HttpClient(handler) { Timeout = TimeSpan.FromMilliseconds(120) };
        var error = await Assert.ThrowsAsync<MusicBrainzException>(() => new AppleCatalogProvider(http).SearchAsync(Query() with { ExplicitAlbumId = "123" }, default));
        Assert.Equal(MusicBrainzFailure.Timeout, error.Failure);
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new AppleCatalogProvider(http).SearchAsync(Query(), cancelled.Token));
        Assert.Equal(1, handler.Calls);
    }
    private sealed class FailureHandler(int? status) : HttpMessageHandler
    {
        public int Calls;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Calls++; if (status is null) { await Task.Delay(Timeout.Infinite, token); throw new InvalidOperationException(); }
            var response = new HttpResponseMessage((HttpStatusCode)status.Value) { Content = new StringContent("{}") };
            if (status == 429) response.Headers.RetryAfter = new(TimeSpan.Zero);
            if (status == 302) response.Headers.Location = new("https://untrusted.example/redirect");
            return response;
        }
    }
    private sealed class Handler : HttpMessageHandler
    {
        public List<string> Paths = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Assert.Equal("itunes.apple.com", request.RequestUri!.Host); Assert.Null(request.Headers.Authorization); Assert.False(request.Headers.Contains("Cookie"));
            Paths.Add(request.RequestUri.PathAndQuery); var row = Row(Paths.Count == 1 ? "Songs of the Seafarer" : "合成专辑");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"resultCount\":1,\"results\":[" + row + "]}", Encoding.UTF8, "application/json") });
        }
    }
}
public sealed class MusicListeningStateTests
{
    [Fact]
    public async Task EditionBrowseIncludesMediaInsteadOfSilentlyLosingDigitalCandidates()
    {
        using var handler = new MusicTestData.Handler((request, _) =>
        {
            var row=System.Text.Json.Nodes.JsonNode.Parse(MusicTestData.AlbumJson)!.AsObject();
            if(!Uri.UnescapeDataString(request.RequestUri!.Query).Contains("+media",StringComparison.Ordinal)) row.Remove("media");
            return Task.FromResult(MusicTestData.Response("{\"release-count\":1,\"releases\":["+row.ToJsonString()+"]}"));
        });
        using var http=new HttpClient(handler); var time=new MusicTestData.Clock();
        var result=await new MusicBrainzProvider(http,time.Rate()).EditionsAsync(MusicTestData.Group,default);
        Assert.Equal("CD",Assert.Single(Assert.Single(result.Candidates).MediaFormats));
    }
    [Fact]
    public async Task LegacyGroupCacheWithoutMediaCannotHideARealDigitalEdition()
    {
        var old=MusicTestData.Candidate(MusicTestData.Other) with { MediaFormats=[] };
        var fresh=old with { MediaFormats=["Digital Media"] };
        var provider=new OutletData.Provider { Editions=new([fresh],1,false,null,null) };
        provider.Data=reference=>OutletData.Snapshot(reference.Id) with{Formats=["Digital Media"]};
        var disk=new OutletData.Cache();disk.Values["group:"+MusicTestData.Group]=new("group:"+MusicTestData.Group,OutletData.Now,Search:new([old],1,false,null,null));
        var result=await new MusicBrainzService(provider,disk,()=>OutletData.Now).LoadDigitalOutletsAsync(OutletData.Snapshot(),default);
        Assert.Single(result.Editions);Assert.Equal(1,provider.EditionCalls);
        Assert.Empty(disk.Values["group:"+MusicTestData.Group].Search!.Candidates[0].MediaFormats);
    }
    [Fact]
    public async Task DigitalReleaseWhoseStatusChangedIsNotPresentedAsAnOfficialEdition()
    {
        var provider = new OutletData.Provider { Editions = new([MusicTestData.Candidate(MusicTestData.Other) with { MediaFormats = ["Digital Media"] }], 1, false, null, null) };
        provider.Data = reference => OutletData.Snapshot(reference.Id) with { Formats = ["Digital Media"], ReleaseStatus = "Bootleg" };
        var result = await new MusicBrainzService(provider, clock: () => OutletData.Now).LoadDigitalOutletsAsync(OutletData.Snapshot(), default);
        Assert.Empty(result.Editions); Assert.True(result.Incomplete); Assert.Contains("发行状态", result.Warning);
    }
    [Fact]
    public async Task OlderResponseCannotReplaceNewerCacheAndRootDoesNotLoadEagerly()
    {
        var provider = new OutletData.Provider(); var pending = new TaskCompletionSource<MusicOutletSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        provider.AsyncData = r => provider.Calls == 1 ? pending.Task : Task.FromResult(OutletData.Snapshot(r.Id) with { Title = "较新的源资料" });
        var service = new MusicBrainzService(provider, clock: () => OutletData.Now);
        using var vm = new MusicListeningViewModel(service); vm.SetContext(OutletData.Ref()); Assert.Equal(0, provider.Calls);
        var older = service.LoadOutletsAsync(OutletData.Ref(), false, default);
        await service.LoadOutletsAsync(OutletData.Ref(), true, default); pending.SetResult(OutletData.Snapshot()); await older;
        Assert.Equal("较新的源资料", (await service.LoadOutletsAsync(OutletData.Ref(), false, default)).Snapshot.Title);
    }
    [Fact]
    public async Task DigitalBudgetIsTwoAndChangedGroupIsExcluded()
    {
        const string third = "5ae1dd91-d42c-425b-861b-63a6fe601228";
        const string fourth = "5ae1dd91-d42c-425b-861b-63a6fe601229";
        var provider = new OutletData.Provider { Editions = new(new[] { MusicTestData.Other, third, fourth }.Select(id => MusicTestData.Candidate(id) with { MediaFormats = ["Digital Media"] }).ToArray(), 3, false, null, null) };
        provider.Data = r => OutletData.Snapshot(r.Id) with { Formats = ["Digital Media"], GroupId = r.Id == MusicTestData.Other ? MusicTestData.Release : MusicTestData.Group };
        var service = new MusicBrainzService(provider, clock: () => OutletData.Now);
        var result = await service.LoadDigitalOutletsAsync(OutletData.Snapshot(), default);
        Assert.Equal(2, provider.Calls); Assert.Single(result.Editions); Assert.True(result.Incomplete); Assert.Contains("已变化", result.Warning);
    }
    [Fact]
    public async Task BoundedDigitalEnrichmentDoesNotChangeCurrentReleaseOrTrustPartialUniqueness()
    {
        var provider = new OutletData.Provider { Editions = new([MusicTestData.Candidate(MusicTestData.Other) with { MediaFormats = ["Digital Media"] }], 40, true, null, null) };
        provider.Data = reference => OutletData.Snapshot(reference.Id) with { Formats = ["Digital Media"] };
        var service = new MusicBrainzService(provider, clock: () => OutletData.Now);
        var current = OutletData.Snapshot(); var result = await service.LoadDigitalOutletsAsync(current, default);
        Assert.True(result.Incomplete); Assert.Single(result.Editions); Assert.Equal(MusicTestData.Other, result.Editions[0].Snapshot.Entity.Id); Assert.Equal(MusicTestData.Release, current.Entity.Id);
        Assert.Equal(1, provider.Calls);
    }
    [Fact]
    public async Task CacheSurvivesRateLimitAndCallersCannotMutateOwnedLinks()
    {
        var provider = new OutletData.Provider(); var disk = new OutletData.Cache(); var service = new MusicBrainzService(provider, disk, () => OutletData.Now);
        var first = await service.LoadOutletsAsync(OutletData.Ref(), false, default);
        // Replace the caller-owned array element; it must not mutate the cache.
        first.Snapshot.Links[0] = first.Snapshot.Links[0] with { Platform = "污染" };
        provider.Fail = true; var second = await service.LoadOutletsAsync(OutletData.Ref(), true, default);
        Assert.True(second.FromCache); Assert.NotNull(second.Warning); Assert.Equal("Spotify", second.Snapshot.Links[0].Platform);
        var restarted = new MusicBrainzService(provider, disk, () => OutletData.Now); Assert.Equal("Spotify", (await restarted.LoadOutletsAsync(OutletData.Ref(), false, default)).Snapshot.Links[0].Platform);
    }
    [Fact]
    public async Task LateResponseAndCancellationCannotFillAnotherAlbumsPanel()
    {
        var reader = new DelayedReader(); using var vm = new MusicListeningViewModel(reader); vm.SetContext(OutletData.Ref()); var old = vm.LoadAsync();
        vm.SetContext(OutletData.Ref(MusicTestData.Other)); reader.Pending.SetResult(new(OutletData.Snapshot(), false, false, null)); await old;
        Assert.Null(vm.Current); Assert.Equal(MusicTestData.Other, vm.Reference!.Id);
    }
    private sealed class DelayedReader : IMusicListeningReader
    {
        public TaskCompletionSource<MusicOutletResult> Pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<MusicOutletResult> LoadOutletsAsync(MusicBrainzReference r, bool refresh, CancellationToken t) => Pending.Task;
        public Task<MusicDigitalOutletResult> LoadDigitalOutletsAsync(MusicOutletSnapshot r, CancellationToken t) => throw new NotSupportedException();
        public Task<AppleCatalogResult> SearchAppleAsync(MusicOutletSnapshot s, string c, bool f, CancellationToken t) => throw new NotSupportedException();
        public Task<MediaUserLinks> ReadAppleChoicesAsync(MusicBrainzReference r, CancellationToken t) => Task.FromResult(MediaUserLinks.Empty(MediaIdentityContext.ForMusicOutlet(r).Anchor));
        public Task<MediaUserLinks> ChangeAppleChoiceAsync(MusicBrainzReference r, AppleAlbumCandidate? c, MediaIdentityReference target, long revision, CancellationToken t) => throw new NotSupportedException();
    }
}
[Collection("Native profile resource")]
public sealed class MusicListeningPersistenceTests
{
    [Fact]
    public async Task AppleChoiceIsIndependentUserDataAndNeverAnEquivalentRelease()
    {
        using var fixture = new ProfileFixture(); var profile = fixture.Open();
        try
        {
            var store = new IsolatedMediaIdentityDecisions(profile.Configuration()); var provider = new OutletData.Provider();
            var cache = new IsolatedMusicBrainzCache(profile.Configuration());
            var apple = new OutletData.AppleProvider();
            var service = new MusicBrainzService(provider, cache, () => OutletData.Now, store, apple); var candidate = OutletData.Apple();
            var library = File.ReadAllBytes(Path.Combine(fixture.Root, "library.json"));
            var saved = await service.ChangeAppleChoiceAsync(OutletData.Ref(), candidate, candidate.Target, 0, default);
            Assert.Single(saved.Items); Assert.Equal(MediaUserLinkState.Related, saved.Items[0].State);
            await service.LoadOutletsAsync(OutletData.Ref(), false, default);
            var catalog = await service.SearchAppleAsync(OutletData.Snapshot(), "JP", false, default);
            Assert.Single(catalog.Candidates); Assert.Equal(1, apple.Calls);
            var cacheReader = new MusicBrainzService(provider, cache, () => OutletData.Now, store, apple);
            Assert.True((await cacheReader.SearchAppleAsync(OutletData.Snapshot(), "JP", false, default)).FromCache);
            Assert.Equal(1, apple.Calls);
            await Assert.ThrowsAsync<InvalidDataException>(() => store.ApplyAsync(new(candidate.Target, MediaIdentityDecisionKind.Confirmed,
                MediaIdentityContext.ForMusicOutlet(OutletData.Ref()), "Album", null, OutletData.Now, MediaIdentityDecisionOrigin.User, "不能冒充确定身份"), 0, default));
            profile.Dispose();
            var remote = Path.Combine(fixture.Root, IsolatedMusicBrainzCache.DirectoryName);
            Assert.StartsWith(Path.GetFullPath(fixture.Root) + Path.DirectorySeparatorChar, Path.GetFullPath(remote), StringComparison.OrdinalIgnoreCase);
            Directory.Delete(remote, true); profile = fixture.Open();
            var restarted = new MusicBrainzService(provider, identityDecisions: new IsolatedMediaIdentityDecisions(profile.Configuration()));
            Assert.Single((await restarted.ReadAppleChoicesAsync(OutletData.Ref(), default)).Items);
            Assert.Empty((await restarted.ReadAppleChoicesAsync(OutletData.Ref(MusicTestData.Other), default)).Items);
            await Assert.ThrowsAsync<IOException>(() => restarted.ChangeAppleChoiceAsync(OutletData.Ref(), null, candidate.Target, 0, default));
            await restarted.ChangeAppleChoiceAsync(OutletData.Ref(), null, candidate.Target, 1, default);
            Assert.Empty((await restarted.ReadAppleChoicesAsync(OutletData.Ref(), default)).Items); Assert.Equal(library, File.ReadAllBytes(Path.Combine(fixture.Root, "library.json")));
        }
        finally { profile.Dispose(); }
    }
}
