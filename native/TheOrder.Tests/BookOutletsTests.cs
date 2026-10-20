using System.Text.Json;
using System.Text.Json.Nodes;
using TheOrder.Application;
using TheOrder.Infrastructure.Online;
using Xunit;

namespace TheOrder.Tests;

public sealed class BookOutletsTests
{
    internal static readonly DateTimeOffset Stamp = AniListTestData.Stamp;
    internal static BookExternalLink Raw(string url, string type = "INFO", string? language = "Japanese", string? notes = null)
        => new(1, "Official Site", url, type, language, notes);
    internal static BookOutletSet Parse(string url, string type = "INFO", string format = "MANGA", string? notes = null)
        => BookOutlets.From(new("AniList", "30715", BookEntityLevel.Unknown), format, "https://anilist.co/manga/30715", [Raw(url, type, notes: notes)], Stamp);
    internal static AniListDetails Details(int id = 30715, string format = "MANGA", bool complete = true, params BookExternalLink[] links)
    {
        var original = AniListTestData.Details();
        return original with { Media = original.Media with { Id = id, Kind = AniListMediaKind.Manga, Format = format, Episodes = null, Chapters = 14, Volumes = 2 },
            Studios = [], WatchLinks = [], HasBookLinkMetadata = complete, BookLinks = links };
    }
    [Theory]
    [InlineData("https://mangaplus.shueisha.co.jp/titles/100020", BookOutletKind.Reading, BookOutletLevel.Series)]
    [InlineData("https://comic-walker.com/detail/KC_000518_S?episodeType=comics", BookOutletKind.Reading, BookOutletLevel.Series)]
    [InlineData("https://comic-walker.com/contents/detail/KDCW_AM05000042010000_68/", BookOutletKind.Reading, BookOutletLevel.Series)]
    [InlineData("https://manga.bilibili.com/detail/mc30460", BookOutletKind.Reading, BookOutletLevel.Series)]
    [InlineData("https://ac.qq.com/Comic/comicInfo/id/643504", BookOutletKind.Reading, BookOutletLevel.Series)]
    [InlineData("https://bookwalker.jp/series/135012/", BookOutletKind.Ebook, BookOutletLevel.Series)]
    [InlineData("https://ebookjapan.yahoo.co.jp/books/601441/", BookOutletKind.Ebook, BookOutletLevel.Series)]
    [InlineData("https://play.google.com/store/books/details/A_Book?id=12345&hl=zh&gl=US", BookOutletKind.Ebook, BookOutletLevel.Publication)]
    [InlineData("https://yenpress.com/series/yotsuba", BookOutletKind.Official, BookOutletLevel.Series)]
    [InlineData("https://www.kadokawa.co.jp/product/200505000091/", BookOutletKind.Official, BookOutletLevel.Publication)]
    [InlineData("https://www.darkhorse.com/books/12-705/berserk-volume-1-tpb/", BookOutletKind.Print, BookOutletLevel.Volume)]
    public void RecognizedWorkPagesKeepTheirActualPurposeAndLevel(string url, BookOutletKind kind, BookOutletLevel level)
    {
        var value = Assert.Single(Parse(url).Links); Assert.Equal(kind, value.Kind); Assert.Equal(level, value.Level);
        Assert.Equal(url, value.Url); Assert.Equal("30715", Assert.Single(value.Sources).Identity.Id);
    }
    [Theory]
    [InlineData("https://bookwalker.jp/")][InlineData("https://ebookjapan.yahoo.co.jp/search/?keyword=x")]
    [InlineData("https://www.amazon.com/s?k=book")][InlineData("https://www.viz.com/search?search=book")]
    [InlineData("https://bookwalker.jp/series/135012/?url=https%3A%2F%2Fevil.example%2Fx")]
    [InlineData("file:///C:/Windows/explorer.exe")][InlineData("http://bookwalker.jp/series/1/")]
    [InlineData("https://user:pass@bookwalker.jp/series/1/")][InlineData("https://127.0.0.1/a")]
    public void HomepageSearchRedirectAndUnsafeAddressesAreNeverEntrypoints(string url)
    { var set = Parse(url); Assert.Empty(set.Links); Assert.Single(set.Excluded); }
    [Fact]
    public void DisabledAndSocialAreNotMainOrSourceButtons()
    {
        var details = Details(links: [Raw("https://bookwalker.jp/series/1/") with { IsDisabled = true }, Raw("https://x.com/publisher", "SOCIAL")]);
        var result = BookOutlets.FromAniList(details); Assert.Empty(result.Links); Assert.Equal(2, result.Excluded.Length);
    }
    [Fact]
    public void InfoCanBeCatalogButNotEveryOfficialIntroductionIsReadingOrBuying()
    {
        Assert.Equal(BookOutletKind.Official, Assert.Single(Parse("https://yenpress.com/series/sword-art-online", format: "NOVEL").Links).Kind);
        foreach (var url in new[] { "https://mfbunkoj.jp/special/gimaiseikatsu/", "https://bgm.tv/subject/51", "https://ndlsearch.ndl.go.jp/books/R100000002-I000000001", "https://en.wikipedia.org/wiki/Clannad" })
            Assert.Equal(BookOutletKind.Information, Assert.Single(Parse(url).Links).Kind);
    }
    [Theory]
    [InlineData("https://comic-walker.com/detail/KC_003678_S?episodeType=comics", "NOVEL")]
    [InlineData("https://yenpress.com/series/fate-zero-manga", "NOVEL")]
    [InlineData("https://sevenseasentertainment.com/books/a-title-novel/", "MANGA")]
    public void ExactSourceIdDoesNotExcuseAnExplicitMangaNovelContradiction(string url, string format)
    { var set = Parse(url, "STREAMING", format); Assert.Empty(set.Links); Assert.Contains("冲突", Assert.Single(set.Excluded).Reason); }
    [Fact]
    public void AmbiguousNovelOutletIsInformationRatherThanComicReading()
    { Assert.Equal(BookOutletKind.Information, Assert.Single(Parse("https://piccoma.com/web/product/6749?etype=episode", "STREAMING", "NOVEL").Links).Kind); }
    [Fact]
    public void PreviewIsNeverInferredFromFreeAccessOrAStoreDomain()
    {
        var url = "https://bookwalker.jp/series/135012/";
        Assert.Equal(BookOutletKind.Ebook, Assert.Single(Parse(url, notes: "Free preview unavailable").Links).Kind);
        Assert.Equal(BookOutletKind.Sample, Assert.Single(Parse(url, notes: "Preview").Links).Kind);
    }
    [Fact]
    public void UnknownAmazonFormatDoesNotBecomeKindleOrPaperBasedOnAsin()
    {
        var url = "https://www.amazon.co.jp/dp/B08KNX1RSQ?tag=publisher-22";
        var link = Assert.Single(Parse(url).Links); Assert.Equal(BookOutletKind.Official, link.Kind); Assert.Equal(url, link.Url);
        Assert.Null(link.Isbn); Assert.Equal("JP", link.Storefront); Assert.Contains("未标注", link.Purpose);
    }
    [Fact]
    public void SeriesOnVolumePageIsNotPurchaseCurrentVolumeAndDifferentVolumesRemainDifferent()
    {
        var source = BookSource.From(Details()) with { Identity = new("Bangumi", "71", BookEntityLevel.Volume), Volume = "2", Isbns = ["9784048677608"] };
        var series = Assert.Single(Parse("https://bookwalker.jp/series/135012/").Links);
        Assert.Equal("前往本系列", BookOutlets.ScopeName(series, source)); Assert.Null(series.Isbn);
        var one = Assert.Single(Parse("https://sevenseasentertainment.com/books/example-vol-1/").Links);
        var two = Assert.Single(Parse("https://sevenseasentertainment.com/books/example-vol-2/").Links);
        var omnibus = Assert.Single(Parse("https://sevenseasentertainment.com/books/example-omnibus-vol-1/").Links);
        Assert.Contains("不同", BookOutlets.ScopeName(one, source)); Assert.Equal(BookOutletLevel.Collection, omnibus.Level);
        Assert.Equal(3, BookOutlets.Merge([one, two, omnibus]).Length);
    }
    [Fact]
    public void ExactIsbnIsKeptButDoesNotProveAnElectronicEdition()
    {
        var link = Assert.Single(Parse("https://www.kadokawa.com.tw/products/9789575647391").Links);
        Assert.Equal("9789575647391", link.Isbn); Assert.Equal(BookOutletKind.Official, link.Kind);
        var source = BookSource.From(Details()) with { Isbns = ["9784048677608"] };
        Assert.Contains("其他出版物", BookOutlets.ScopeName(link, source));
    }
    [Fact]
    public void DedupKeepsLanguageEditionVolumeAndMeaningfulParametersWithoutRewritingTheAddress()
    {
        var a = Raw("https://play.google.com/store/books/details/A?id=one&hl=en&edition=standard&utm_source=provider");
        var b = a with { Id = 2, Url = a.Url!.Replace("utm_source=provider", "utm_source=other") };
        var c = a with { Id = 3, Url = a.Url!.Replace("id=one", "id=two") };
        var d = a with { Id = 4, Language = "Chinese" };
        var e = a with { Id = 5, Url = a.Url!.Replace("standard", "special") };
        var values = BookOutlets.From(new("AniList", "1", BookEntityLevel.Unknown), "NOVEL", "https://anilist.co/manga/1", [a,b,c,d,e], Stamp);
        Assert.Equal(4, values.Links.Length); Assert.Equal(2, values.Links.First().Sources.Length); Assert.Equal(a.Url, values.Links.First().Url);
        Assert.NotEqual(WatchLinks.ExternalResourceKey("https://books.example/a#one"), WatchLinks.ExternalResourceKey("https://books.example/a#two"));
        var tagged = values.Links[0];
        Assert.Equal(2, BookOutlets.Merge([tagged with { Url = "https://books.example/item?tag=volume-1" }, tagged with { Url = "https://books.example/item?tag=volume-2" }]).Length);
    }
    [Fact]
    public void DisplayLanguageDoesNotFilterPublicationLanguageOrInventWorldwideAvailability()
    {
        var details = Details(links: [Raw("https://bookwalker.jp/series/135012/", language: "Japanese"), Raw("https://yenpress.com/series/yotsuba", language: "English")]);
        var rows = BookOutlets.FromAniList(details).Links; Assert.Equal(2, rows.Length); Assert.Contains(rows, x => x.Language == "Japanese");
        Assert.Null(rows.Single(x => x.Platform == "Yen Press").Storefront);
    }
    [Fact]
    public void AnUnlabelledCopyAddsEvidenceWithoutDuplicatingThePlatformOrHidingExplicitLanguages()
    {
        var url = "https://bookwalker.jp/series/135012/";
        var rows = BookOutlets.From(new("AniList", "1", BookEntityLevel.Unknown), "MANGA", "https://anilist.co/manga/1",
            [Raw(url, language: "Japanese"), Raw(url, language: null) with { Id = 2 }], Stamp).Links;
        var link = Assert.Single(rows); Assert.Equal("Japanese", link.Language); Assert.Equal(2, link.Sources.Length);
        Assert.Contains(link.Sources, x => x.Original.Language is null);
        var english = link with { Language = "English" }; Assert.Equal(2, BookOutlets.Merge([link, english]).Length);
    }
    [Fact]
    public void NewProviderMappingKeepsAllBookEvidenceAndDoesNotChangeAnimePlayback()
    {
        var json = JsonNode.Parse(AniListTestData.DetailJson)!.AsObject();
        json["type"] = "MANGA"; json["format"] = "MANGA"; json["episodes"] = null;
        json["externalLinks"] = JsonSerializer.SerializeToNode(new[] { new { id=1,site="BOOK☆WALKER",url="https://bookwalker.jp/series/1/",type="INFO",language="Japanese",notes="Volumes",isDisabled=false }, new { id=2,site="Old",url="https://bookwalker.jp/series/2/",type="INFO",language="English",notes="Disabled",isDisabled=true } });
        using var doc = JsonDocument.Parse(json.ToJsonString()); var mapped = AniListProvider.MapDetails(doc.RootElement, 1, Stamp);
        Assert.True(mapped.HasBookLinkMetadata); Assert.Equal(2, mapped.BookLinks.Length); Assert.Empty(mapped.WatchLinks);
        Assert.True(mapped.BookLinks[1].IsDisabled); Assert.Equal("Volumes", mapped.BookLinks[0].Notes);
        var old = JsonSerializer.SerializeToNode(mapped)!.AsObject(); old.Remove("BookLinks"); old.Remove("HasBookLinkMetadata");
        var decoded = JsonSerializer.Deserialize<AniListDetails>(old.ToJsonString())!; Assert.False(decoded.HasBookLinkMetadata); Assert.Empty(decoded.BookLinks); Assert.True(AniListValidation.Details(decoded));
    }
    [Fact]
    public void BangumiKeepsProvidedLinksWithoutInventingAddressesFromPublisherOrIsbn()
    {
        using var json = JsonDocument.Parse("""{"series":false,"infobox":[{"key":"ISBN","value":"9784048677608"},{"key":"出版社","value":"KADOKAWA"},{"key":"电子版","value":"https://bookwalker.jp/series/135012/"},{"key":"官方网站","value":"https://www.kadokawa.co.jp/"}]}""");
        var facts = BangumiBookFactsMapping.Map(json.RootElement); Assert.True(facts.HasExternalLinkMetadata); Assert.Equal(2, facts.ExternalLinks.Length);
        Assert.Contains("KADOKAWA", facts.Publishers); Assert.DoesNotContain(facts.ExternalLinks, x => x.Url!.Contains("9784048677608", StringComparison.Ordinal));
        var set = BookOutlets.From(new("Bangumi", "71", facts.Level), "漫画", "https://bgm.tv/subject/71", facts.ExternalLinks, Stamp);
        Assert.Equal(BookOutletKind.Ebook, Assert.Single(set.Links).Kind); Assert.Single(set.Excluded);
    }
}
