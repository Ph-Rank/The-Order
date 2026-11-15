using System.Collections.Immutable;
using System.Text.Json;
using TheOrder.Application;
using TheOrder.Domain;
using TheOrder.Infrastructure.Online;
using Xunit;
using static TheOrder.Tests.RecognitionBoundaryTests;

namespace TheOrder.Tests;
public sealed class ArtworkQualityTests
{
    [Fact] public void ClearPortraitBeatsTinyLandscapeRegardlessOfVotes()
    {
        WorkCover small = new("small", "https://example.org/tiny", 256, 178, 999999, null);
        WorkCover clear = new("vndb:cv1", "https://t.vndb.org/cv/01/1.jpg", 600, 842, 1, null);
        Assert.Equal(clear.Url, CoverQuality.Select([small, clear], small.Url));
        Assert.Equal(small.Url, CoverSources.Selected(Entry() with { Metadata = Work() with { CoverUrl = small.Url, Covers = [small, clear] }, CoverChoice = new("200", small) }));
        var local = new LocalCover("data:image/png;base64,fixture", 1, 1);
        Assert.Equal(local.DataUrl, CoverSources.Selected(Entry() with { Metadata = Work() with { CoverUrl = clear.Url, Covers = [clear] }, LocalCover = local }));
    }
    [Fact] public void UnknownDefaultAndCloseQualityArePreserved()
    {
        WorkCover cover = new("vndb:cv1", "https://t.vndb.org/cv/01/1.jpg", 600, 900, null, null);
        Assert.Equal("https://example.org/unknown", CoverQuality.Select([cover with { Id = "unclassified" }], "https://example.org/unknown"));
        Assert.Equal(cover.Url, CoverQuality.Select([cover, cover with { Id = "vndb:cv2", Url = "https://t.vndb.org/cv/02/2.jpg", Width = 610, Height = 915 }], cover.Url));
    }
    [Theory]
    [InlineData("disc", 4000, 4000)]
    [InlineData("back-cover", 3000, 4000)]
    [InlineData("visual-fanbook", 3000, 4200)]
    [InlineData("soundtrack", 4000, 4000)]
    [InlineData("ps4-box", 3000, 4000)]
    public void UnclassifiedGalleryCannotDisplaceGameFrontEvenWithHugeResolutionAndVotes(string label, int width, int height)
    {
        WorkCover scan = new("123", "https://images.example/" + label, width, height, 9999999, "safe");
        WorkCover front = new("vndb:cv96184", "https://t.vndb.org/cv/84/96184.jpg", 411, 560, null, "safe");
        Assert.Equal(front.Url, CoverQuality.Select([scan, front], scan.Url)); // v20 cache recovery
        Assert.Equal(front.Url, CoverQuality.Select([front, scan], front.Url));
        Assert.Equal("https://images.example/catalog-primary", CoverQuality.Select([scan], "https://images.example/catalog-primary"));
        Assert.Null(CoverQuality.Select([scan], null));
    }
    [Theory]
    [InlineData("vndb:cv1", "https://t.vndb.org/cv/02/1.jpg")]
    [InlineData("123", "https://t.vndb.org/cv/01/1.jpg")]
    [InlineData("vndb:cv1", "https://images.example/cv/01/1.jpg")]
    [InlineData("steam:42", "https://shared.fastly.steamstatic.com/store_item_assets/steam/apps/43/library_600x900.jpg")]
    [InlineData("steam:42", "https://shared.fastly.steamstatic.com/store_item_assets/steam/apps/42/library_hero.jpg")]
    public void ProvenanceRequiresBothReservedProviderIdAndExactCanonicalAsset(string id, string url)
        => Assert.False(CoverQuality.IsVerifiedGameCover(new(id, url, 600, 900, null, "safe")));
    [Fact] public void QualityComparisonRemainsWithinVerifiedGameArtwork()
    {
        WorkCover front = new("vndb:cv1", "https://t.vndb.org/cv/01/1.jpg", 256, 348, null, "safe");
        WorkCover poster = new("steam:42", "https://shared.fastly.steamstatic.com/store_item_assets/steam/apps/42/library_600x900.jpg", 600, 900, null, null);
        Assert.Equal(poster.Url, CoverQuality.Select([front, poster], front.Url));
        using var doc = JsonDocument.Parse("""{"object":"work","id":"200","display_name":"test","covers":[{"id":"1","url":"https://images.example/disc","width":4000,"height":4000}]}""");
        Assert.Null(new RemoteWorkDto(doc.RootElement).Map(true, DateTimeOffset.UtcNow).CoverUrl);
    }
    [Fact] public void ExplicitFilterAndPersonalChoiceArePreserved()
    {
        WorkCover safe = new("1", "https://example.org/safe", 600, 900, null, null);
        WorkCover explicitCover = new("2", "https://example.org/explicit", 1200, 1800, null, "explicit");
        Assert.Equal(safe.Url, CoverQuality.Select([safe, explicitCover], safe.Url, false));
        var game = Entry() with { Metadata = Work() with { CoverUrl = safe.Url, Covers = [safe, explicitCover] }, CoverChoice = new("200", explicitCover) };
        Assert.Equal(explicitCover.Url, CoverSources.Selected(game));
    }
    [Fact] public void PublicArtworkRejectsForeignWorkUnsafeFlagsAndMismatchedImagePaths()
    {
        using var doc = JsonDocument.Parse("""{"results":[{"id":"v26523","image":{"id":"cv1","url":"https://t.vndb.org/cv/01/1.jpg","dims":[1202,1750],"sexual":0},"screenshots":[{"id":"sf12","url":"https://t.vndb.org/sf/12/12.jpg","dims":[1280,720],"sexual":0,"violence":0},{"id":"sf13","url":"https://t.vndb.org/sf/13/13.jpg","dims":[1920,1080],"sexual":1,"violence":0},{"id":"sf14","url":"https://t.vndb.org/sf/14/14.jpg","dims":[1280,720],"sexual":"0","violence":0},{"id":"sf15","url":"https://t.vndb.org/sf/99/99.jpg","dims":[1280,720],"sexual":0,"violence":0},{"id":"sf16","url":"https://evil.example/sf/16/16.jpg","dims":[1280,720],"sexual":0,"violence":0}]}]}""");
        var extra = VndbArtworkSource.Parse(doc.RootElement, "v26523", ["sf12", "sf13"]);
        Assert.True(extra.Complete); Assert.Equal("vndb-curated", Assert.Single(extra.Art).Kind); Assert.Equal(1202, Assert.Single(extra.Covers).Width);
        var foreign = VndbArtworkSource.Parse(doc.RootElement, "v1", []); Assert.False(foreign.Complete); Assert.Empty(foreign.Art);
    }
    [Fact] public async Task PublicRequestUsesOneExactIdAndNoCatalogCredential()
    {
        int calls = 0;
        using var http = new HttpClient(new OnlineTransportTests.Handler(async (request, token) => {
            calls++; Assert.Equal("https://api.vndb.org/kana/vn", request.RequestUri!.AbsoluteUri); Assert.Null(request.Headers.Authorization);
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            Assert.Equal("v26523", body.RootElement.GetProperty("filters")[2].GetString());
            return OnlineTransportTests.Json("""{"results":[{"id":"v26523","screenshots":[]}]}""");
        }));
        var source = new VndbArtworkSource(http); var work = Work() with { References = [new("vndb", "v26523", null)] };
        Assert.True((await source.ReadAsync(work, default)).Complete); Assert.Equal(1, calls);
        Assert.Empty((await source.ReadAsync(work with { References = work.References.Add(new("vndb", "v1", null)) }, default)).Art); Assert.Equal(1, calls);
    }
    [Fact]
    public async Task ReleasePromosRequireOfficialDigitalArtForTheExactVn()
    {
        int calls = 0;
        using var http = new HttpClient(new OnlineTransportTests.Handler(async (request, token) =>
        {
            calls++;
            Assert.Equal("https://api.vndb.org/kana/release", request.RequestUri!.AbsoluteUri);
            Assert.Null(request.Headers.Authorization);
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            var filters = body.RootElement.GetProperty("filters");
            Assert.Equal("v26523", filters[1][2][2].GetString());
            Assert.Equal("dig", filters[5][2].GetString());
            return OnlineTransportTests.Json("""
                {"results":[
                  {"id":"r101","official":true,"patch":false,"vns":[{"id":"v26523","rtype":"complete"}],"images":[
                    {"id":"cv1","url":"https://t.vndb.org/cv/01/1.jpg","dims":[1200,675],"sexual":0,"violence":0,"type":"dig","vn":null,"photo":false},
                    {"id":"cv2","url":"https://t.vndb.org/cv/02/2.jpg","dims":[1200,675],"sexual":0,"violence":0,"type":"pkgback","vn":null,"photo":false},
                    {"id":"cv3","url":"https://t.vndb.org/cv/03/3.jpg","dims":[1200,675],"sexual":0,"violence":0,"type":"dig","vn":null,"photo":true},
                    {"id":"cv4","url":"https://t.vndb.org/cv/04/4.jpg","dims":[1200,675],"sexual":1,"violence":0,"type":"dig","vn":null,"photo":false},
                    {"id":"cv5","url":"https://t.vndb.org/cv/05/5.jpg","dims":[675,1200],"sexual":0,"violence":0,"type":"dig","vn":null,"photo":false}
                  ]},
                  {"id":"r102","official":true,"patch":false,"vns":[{"id":"v26523","rtype":"complete"},{"id":"v2","rtype":"complete"}],"images":[
                    {"id":"cv6","url":"https://t.vndb.org/cv/06/6.jpg","dims":[1200,675],"sexual":0,"violence":0,"type":"dig","vn":null,"photo":false},
                    {"id":"cv7","url":"https://t.vndb.org/cv/07/7.jpg","dims":[1200,675],"sexual":0,"violence":0,"type":"dig","vn":"v2","photo":false}
                  ]},
                  {"id":"r103","official":false,"patch":false,"vns":[{"id":"v26523","rtype":"complete"}],"images":[
                    {"id":"cv8","url":"https://t.vndb.org/cv/08/8.jpg","dims":[1200,675],"sexual":0,"violence":0,"type":"dig","vn":"v26523","photo":false}
                  ]}
                ]}
                """);
        }));
        var source = new VndbArtworkSource(http);
        var work = Work() with { References = [new("vndb", "v26523", null), new("vndb", "r101", null)] };
        var result = await source.ReadPromoAsync(work, default);
        Assert.True(result.Complete);
        Assert.Equal(new CatalogArt("https://t.vndb.org/cv/01/1.jpg", 1200, 675, ArtworkSources.VndbPromoKind, "VNDB r101"), Assert.Single(result.Art));
        Assert.True(result.Covers.IsDefaultOrEmpty);
        Assert.Empty((await source.ReadPromoAsync(work with { References = work.References.Add(new("vndb", "v2", null)) }, default)).Art);
        Assert.Equal(1, calls);
    }
    [Fact] public async Task PublicFailureCannotBecomeSuccessfulEmptyArtwork()
    {
        using var http = new HttpClient(new OnlineTransportTests.Handler((_, _) => throw new HttpRequestException("synthetic offline")));
        var result = await new VndbArtworkSource(http).ReadAsync(Work() with { References = [new("vndb", "v1", null)] }, default);
        Assert.False(result.Complete); Assert.Empty(result.Art);
    }
    [Theory]
    [InlineData("https://t.vndb.org/sf/12/12.jpg", true)]
    [InlineData("https://t.vndb.org/cv/23/123.jpg", true)]
    [InlineData("https://t.vndb.org/sf/99/12.jpg", false)]
    [InlineData("https://t.vndb.org/sf/12/12.jpg?url=http://localhost", false)]
    [InlineData("https://images.example/sf/12/12.jpg", false)]
    [InlineData("https://t.vndb.org:8443/sf/12/12.jpg", false)]
    [InlineData("https://t.vndb.org/other.jpg", false)]
    public async Task OnlyCanonicalPublicOriginalsUseSystemArtworkRoute(string url, bool publicRoute)
    {
        bool routed = false;
        using var checkedHttp = new HttpClient(new OnlineTransportTests.Handler((_, _) => Task.FromResult(OnlineTransportTests.Json("{}"))));
        using var publicHttp = new HttpClient(new OnlineTransportTests.Handler((request, _) => {
            routed = true; Assert.Equal(url, request.RequestUri!.AbsoluteUri); Assert.Null(request.Headers.Authorization); Assert.False(request.Headers.Contains("Cookie"));
            var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3]) };
            response.Content.Headers.ContentType = new("image/jpeg"); return Task.FromResult(response);
        }));
        var source = new RemoteCoverSource(checkedHttp, originalArtwork: publicHttp);
        var key = source.Register(url, "revision");
        if (key is null) { Assert.False(publicRoute); Assert.False(routed); return; }
        var bytes = await source.ReadAsync(key!, default);
        Assert.Equal(publicRoute, routed); if (publicRoute) Assert.Equal(new byte[] { 1, 2, 3 }, bytes); else Assert.Null(bytes);
    }
}
