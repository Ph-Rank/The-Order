using System.Text.Json;
using System.Text.Json.Nodes;
using TheOrder.Application;
using TheOrder.Infrastructure.Online;
using Xunit;

namespace TheOrder.Tests;

public sealed class MusicOutletSourcePolicyTests
{
    [Theory]
    [InlineData("https://www.kugou.com/song-36/70q485.html", MusicBrainzEntityKind.Recording, "Recording")]
    [InlineData("https://www.kugou.com/album/info/b77v5a/", MusicBrainzEntityKind.Release, "Album")]
    [InlineData("https://www.kugou.com/yy/album/single/123.html", MusicBrainzEntityKind.Release, "Album")]
    [InlineData("https://pc.service.kugou.com/yueku/v9/album/single/201199122-91.html", MusicBrainzEntityKind.Release, "Album")]
    [InlineData("https://www.kugou.com/song/#hash=0123456789ABCDEF0123456789ABCDEF&album_id=123", MusicBrainzEntityKind.Recording, "Recording")]
    public void KugouDirectPagesKeepTheSameIdentityRulesAsOtherPlatforms(string url, MusicBrainzEntityKind kind, string layer)
    {
        var link = MusicOutlets.FromRelationship(new(kind, MusicTestData.Release), "streaming", "forward", false, url, OutletData.Now);
        Assert.NotNull(link); Assert.True(MusicOutlets.Valid(link));
        Assert.Equal("酷狗音乐", link.Platform); Assert.Equal(layer, link.TargetLayer); Assert.Equal(MusicOutletKind.Listening, link.Kind);
    }

    [Theory]
    [InlineData("https://music.163.com/#/search/m/?s=CLANNAD")]
    [InlineData("https://music.163.com/")]
    [InlineData("https://y.qq.com/n/ryqq/search?w=CLANNAD")]
    [InlineData("https://www.kugou.com/yy/html/search.html#searchType=song&searchKeyWord=CLANNAD")]
    [InlineData("https://open.spotify.com/search/CLANNAD")]
    public void SearchAndPlatformHomePagesAreNeverDisplayedAsEntityDirectLinks(string url)
        => Assert.Null(MusicOutlets.FromRelationship(OutletData.Ref(), "streaming", "forward", false, url, OutletData.Now));

    [Fact]
    public void ADirectPageWithTwoRelationshipTypesIsOneEntryWithBothOriginalClaims()
    {
        var streaming = Link("https://music.163.com/#/album?id=123", "streaming");
        var purchase = Link("https://music.163.com/album?id=123&utm_source=test", "purchase for download");
        var result = Assert.Single(MusicOutlets.Merge([streaming, purchase], MusicOutletSort.SourceOrder));
        Assert.Equal(MusicOutletKind.Listening, result.Kind);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(result));
        var relationships = json.RootElement.GetProperty("Relationships").EnumerateArray().ToArray();
        Assert.Equal(2, relationships.Length);
        Assert.Contains(relationships, x => x.GetProperty("Type").GetString() == "streaming");
        Assert.Contains(relationships, x => x.GetProperty("Type").GetString() == "purchase for download");
        Assert.Contains(relationships, x => x.GetProperty("OriginalUrl").GetString() == streaming.Url.Replace("/album?", "/#/album?"));
        var reversed = Assert.Single(MusicOutlets.Merge([purchase, streaming], MusicOutletSort.SourceOrder));
        Assert.Equal(result.Kind, reversed.Kind); Assert.Equal(result.RelationshipType, reversed.RelationshipType);
    }

    [Theory]
    [InlineData(MusicOutletSort.DomesticFirst)]
    [InlineData(MusicOutletSort.SourceOrder)]
    [InlineData(MusicOutletSort.PlatformName)]
    public void SortNeverFiltersDomesticInternationalOrInformationEntries(MusicOutletSort sort)
    {
        var links = new[] { Link("https://open.spotify.com/album/09vpDdZGprjKr4tleWomBV"), Link("https://music.163.com/album?id=123"),
            Link("https://y.qq.com/n/ryqq/albumDetail/003abcDEF"), Link("https://www.kugou.com/yy/album/single/123.html"),
            Link("https://vgmdb.net/album/123", "other databases"), Link("https://music.apple.com/jp/album/test/123") };
        var actual = MusicOutlets.Merge(links, sort);
        Assert.Equal(6, actual.Length);
        Assert.Equal(links.Select(x => x.Url).Order(), actual.Select(x => x.Url).Order());
        Assert.Contains(actual, x => x.Platform == "酷狗音乐");
        if (sort == MusicOutletSort.DomesticFirst) Assert.Equal(new[] { "网易云音乐", "QQ 音乐", "酷狗音乐" }, actual.Take(3).Select(x => x.Platform));
    }

    [Fact]
    public void DuplicateUrlsOnDifferentReleasesOrRecordingsNeverMergeIdentities()
    {
        var first = Link("https://music.163.com/album?id=123");
        var second = MusicOutlets.FromRelationship(OutletData.Ref(MusicTestData.Other), "streaming", "forward", false, first.Url, OutletData.Now)!;
        var recording = MusicOutlets.FromRelationship(new(MusicBrainzEntityKind.Recording, MusicTestData.Release), "streaming", "forward", false, first.Url, OutletData.Now)!;
        Assert.Equal(3, MusicOutlets.Merge([first, second, recording], MusicOutletSort.SourceOrder).Length);
        Assert.Equal(MusicOutletKind.Information, recording.Kind);
    }

    [Fact]
    public void RawMusicBrainzRelationshipTypeUrlAndSourceEntitySurviveNormalizationAndCacheRoundTrip()
    {
        var root = JsonNode.Parse(MusicTestData.AlbumJson)!.AsObject();
        const string typeId = "b5f3058a-666c-406f-aafb-f9249fc7b122";
        const string urlId = "d3ef3524-7460-4386-8686-bfb5b8a0dd7f";
        const string original = "https://music.163.com/#/album?id=123&utm_source=mb";
        root["relations"] = new JsonArray(new JsonObject { ["type"] = "streaming", ["type-id"] = typeId, ["direction"] = "forward", ["ended"] = false,
            ["url"] = new JsonObject { ["id"] = urlId, ["resource"] = original } });
        using var doc = JsonDocument.Parse(root.ToJsonString());
        var snapshot = MusicBrainzProvider.MapOutlets(OutletData.Ref(), doc.RootElement, OutletData.Now);
        var copy = JsonSerializer.Deserialize<MusicOutletSnapshot>(JsonSerializer.Serialize(snapshot))!;
        Assert.True(MusicOutlets.Valid(copy));
        var link = Assert.Single(copy.Links);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(link));
        var relation = Assert.Single(json.RootElement.GetProperty("Relationships").EnumerateArray());
        Assert.Equal(original, relation.GetProperty("OriginalUrl").GetString());
        Assert.Equal(typeId, relation.GetProperty("TypeId").GetString());
        Assert.Equal(urlId, relation.GetProperty("UrlId").GetString());
        Assert.Equal("forward", relation.GetProperty("Direction").GetString());
        Assert.Equal(link.Entity.Id, relation.GetProperty("Entity").GetProperty("Id").GetString());
        Assert.Equal("MusicBrainz", Assert.Single(link.Sources).Source);
    }

    [Fact]
    public async Task LoadingDirectLinksNeverCallsTheOptionalAppleCatalog()
    {
        var provider = new OutletData.Provider(); var apple = new OutletData.AppleProvider();
        using var model = new TheOrder.Presentation.MusicListeningViewModel(new MusicBrainzService(provider, clock: () => OutletData.Now, apple: apple));
        model.SetContext(OutletData.Ref()); await model.LoadAsync();
        Assert.Equal(0, apple.Calls); Assert.Equal(1, provider.Calls);
    }

    [Fact]
    public async Task OldKugouCacheCanBeReclassifiedWithoutARequestOrInventingMissingProvenance()
    {
        var oldLink = new MusicOutletLink(OutletData.Ref(), "www.kugou.com", "https://www.kugou.com/album/info/b77v5a/", MusicOutletKind.Information,
            "Page", [], [new("MusicBrainz", OutletData.Ref().Url, "streaming · 旧缓存", OutletData.Now)]) { RelationshipType = "streaming" };
        var oldSnapshot = OutletData.Snapshot() with { Links = [oldLink] };
        var disk = new OutletData.Cache(); disk.Values[oldSnapshot.CacheKey] = new(oldSnapshot.CacheKey, OutletData.Now) { Outlets = oldSnapshot };
        var provider = new OutletData.Provider { Fail = true };
        var result = await new MusicBrainzService(provider, disk, () => OutletData.Now).LoadOutletsAsync(OutletData.Ref(), false, default);
        Assert.True(result.FromCache); Assert.Equal(0, provider.Calls);
        var link = Assert.Single(result.Snapshot.Links);
        Assert.Equal("酷狗音乐", link.Platform); Assert.Equal(MusicOutletKind.Listening, link.Kind);
        Assert.Empty(link.Relationships); Assert.Equal(oldLink.Sources, link.Sources);
        Assert.Equal(MusicOutletKind.Information, oldSnapshot.Links[0].Kind);
    }

    [Theory]
    [InlineData("free streaming", "正版收听 · MusicBrainz 收录")]
    [InlineData("streaming", "正版收听 · MusicBrainz 收录")]
    [InlineData("other databases", "资料链接")]
    [InlineData("purchase for download", "购买页面")]
    public void WordingFollowsTheMusicBrainzRelationshipRatherThanThePlatformName(string type, string expected)
    {
        var link = Link("https://music.163.com/album?id=123", type);
        Assert.Equal(expected, MusicOutlets.PurposeLabel(link));
        Assert.DoesNotContain("已核验", MusicOutlets.PurposeLabel(link));
    }

    [Fact]
    public void OriginalRelationshipCannotBeReusedForAnotherReleaseAndCopiesDoNotShareArrays()
    {
        var original = OutletData.Snapshot(); var first = original.Links[0];
        Assert.False(MusicOutlets.Valid(first with { Entity = OutletData.Ref(MusicTestData.Other) }));
        var copy = MusicOutlets.Copy(original);
        copy.Links[0].Relationships[0] = copy.Links[0].Relationships[0] with { OriginalUrl = "https://music.163.com/album?id=999" };
        Assert.False(MusicOutlets.Valid(copy)); Assert.True(MusicOutlets.Valid(original));
    }

    private static MusicOutletLink Link(string url, string type = "streaming")
        => MusicOutlets.FromRelationship(OutletData.Ref(), type, "forward", false, url, OutletData.Now)!;
}
