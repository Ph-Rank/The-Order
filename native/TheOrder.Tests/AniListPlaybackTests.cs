using System.Text.Json;
using TheOrder.Application;
using TheOrder.Infrastructure.Online;
using Xunit;

namespace TheOrder.Tests;

public sealed class AniListPlaybackTests
{
    private static AniListDetails Map(string fields)
    {
        using var json = JsonDocument.Parse(AniListTestData.DetailJson[..^1] + "," + fields[1..^1] + "}");
        return AniListProvider.MapDetails(json.RootElement, 1, AniListTestData.Stamp);
    }
    [Fact] public void OnlyEnabledStreamingLinksBecomeWatchEntrypoints()
    {
        var item = Map("""{"externalLinks":[{"id":1,"site":"HIDIVE","type":"STREAMING","url":"https://www.hidive.com/tv/clannad/","isDisabled":false},{"id":2,"site":"Official Site","type":"INFO","url":"https://www.tbs.co.jp/clannad/"},{"id":3,"site":"Disabled","type":"STREAMING","url":"https://www.example.com/","isDisabled":true},{"id":4,"site":"Unsafe","type":"STREAMING","url":"file:///C:/Windows/explorer.exe"}]}""");
        Assert.Equal("HIDIVE", Assert.Single(item.WatchLinks).Site); Assert.True(item.HasPlaybackMetadata);
    }
    [Fact] public void ValidLinksKeepPlatformLanguageNotesAndExactAddressWithoutNameGuessing()
    {
        var item = Map("""{"externalLinks":[{"id":1,"site":"Amazon Prime Video","type":"STREAMING","url":"https://www.amazon.com/Clannad/dp/B005IGW5Y0?tag=source","language":"English","notes":"US"},{"id":2,"site":"Amazon Prime Video","type":"STREAMING","url":"https://www.amazon.com/Clannad/dp/B005IGW5Y0?tag=source"}]}""");
        var link = Assert.Single(item.WatchLinks); Assert.Equal("English", link.Language); Assert.Equal("US", link.Notes); Assert.EndsWith("?tag=source", link.Url);
    }
    [Theory]
    [InlineData("javascript:alert(1)")][InlineData("file:///c:/data")][InlineData("http://www.example.com/a")]
    [InlineData("https://user:pass@example.com/a")][InlineData("https://localhost/a")][InlineData("https://127.0.0.1/a")]
    [InlineData("https://[::1]/a")][InlineData("https://host.local/a")][InlineData("https://a.example:8443/a")]
    [InlineData("https://example.com\\@evil.com/")][InlineData("https://example.com/a\n")]
    public void NonPublicOrExecutableLinkCannotBeOpened(string url) => Assert.False(AniListPlayback.SafeExternalUrl(url));
    [Fact] public void MissingOrUnknownTrailerDoesNotBreakMetadata()
    {
        Assert.Null(Map("""{"trailer":null}""").Trailer);
        Assert.Null(Map("""{"trailer":{"id":"https://evil.com/injected","site":"youtube"}}""").Trailer);
        Assert.Null(Map("""{"trailer":{"id":"abcde123456","site":"unsupported"}}""").Trailer);
        Assert.NotEmpty(Map("""{"trailer":{"id":"abc","site":"youtube"}}""").Description);
    }
    [Fact] public void YoutubeIdIsNotAnArbitraryEmbeddedUrlOrScript()
    {
        var trailer = Map("""{"trailer":{"id":"9ZseZTshl2k","site":"youtube"}}""").Trailer!;
        Assert.True(trailer.CanEmbed); Assert.Equal("https://www.youtube.com/watch?v=9ZseZTshl2k", trailer.Url);
        var html = AniListPlayback.PlayerDocument(trailer); Assert.Contains("youtube-nocookie.com", html); Assert.Contains("autoplay:0", html);
        Assert.Contains("controls:1", html); Assert.Contains("hl:'zh-CN'", html);
        Assert.DoesNotContain("setPlaybackQuality", html);
        Assert.Contains("strict-origin-when-cross-origin", html); Assert.Contains(AniListPlayback.AppOrigin, html); Assert.Contains("script-src 'sha256-", html);
        Assert.Throws<InvalidDataException>(() => AniListPlayback.PlayerDocument(trailer with { Id = "';alert(1)//" }));
        Assert.Throws<InvalidDataException>(() => AniListPlayback.PlayerDocument(trailer with { Site = "evil" }));
    }
    [Fact] public void DailymotionRetainsOriginalLinkWithoutInventingRequiredPlayerRegistration()
    {
        var trailer = Map("""{"trailer":{"id":"x84sh87","site":"dailymotion"}}""").Trailer!;
        Assert.False(trailer.CanEmbed); Assert.Equal("https://www.dailymotion.com/video/x84sh87", trailer.Url);
        Assert.Throws<InvalidDataException>(() => AniListPlayback.PlayerDocument(trailer));
    }
    [Fact] public async Task OldCacheWithoutPlaybackFieldsIsUpdatedOnceThenCached()
    {
        var old = AniListTestData.Details() with { HasPlaybackMetadata = false };
        var disk = new AniListMemoryCache(); disk.Values["media:0:2167:1"] = new("media:0:2167:1", AniListTestData.Stamp, Details: old);
        var provider = new AniListFakeProvider(); var service = new AniListService(provider, disk, () => AniListTestData.Stamp);
        Assert.True((await service.LoadAsync(2167, AniListMediaKind.Anime, false, 1, default)).Details.HasPlaybackMetadata);
        Assert.True((await service.LoadAsync(2167, AniListMediaKind.Anime, false, 1, default)).FromCache); Assert.Equal(1, provider.Fetches);
    }
    [Fact] public async Task OldCacheRemainsReadableWhenRefreshIsOffline()
    {
        var old = AniListTestData.Details() with { HasPlaybackMetadata = false };
        var disk = new AniListMemoryCache(); disk.Values["media:0:2167:1"] = new("media:0:2167:1", AniListTestData.Stamp, Details: old);
        var provider = new AniListFakeProvider { Fetch = (_, _, _) => throw new AniListException(AniListFailure.Offline, "离线") };
        var result = await new AniListService(provider, disk, () => AniListTestData.Stamp).LoadAsync(2167, AniListMediaKind.Anime, false, 1, default);
        Assert.True(result.FromCache); Assert.True(result.IsStale); Assert.Equal(old.Description, result.Details.Description);
    }
    [Fact] public void ExistingSerializedCachesDefaultOptionalFieldsAndInvalidNewFieldsReject()
    {
        var json = JsonSerializer.SerializeToNode(AniListTestData.Details())!.AsObject(); json.Remove("WatchLinks"); json.Remove("Trailer"); json.Remove("HasPlaybackMetadata");
        var old = json.Deserialize<AniListDetails>()!; Assert.True(AniListValidation.Details(old)); Assert.Empty(old.WatchLinks); Assert.Null(old.Trailer); Assert.False(old.HasPlaybackMetadata);
        Assert.False(AniListValidation.Details(old with { WatchLinks = [new(1, "X", "javascript:evil()")] }));
        Assert.False(AniListValidation.Details(old with { Trailer = new("youtube", "unsafe") }));
    }
    [Theory][InlineData(100)][InlineData(101)][InlineData(150)][InlineData(153)][InlineData(5)]
    public void PlayerFailuresHaveUsableFallback(int code) => Assert.NotEmpty(AniListPlayback.PlayerError(code));
}
