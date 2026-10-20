using System.Globalization;
using System.Text.RegularExpressions;

namespace TheOrder.Application;

// Provider statements, not independent copyright or availability verification.
// Optional fields are additive to existing, disposable metadata caches.
public sealed record BookExternalLink(int Id, string Site, string? Url, string Type,
    string? Language = null, string? Notes = null, bool IsDisabled = false);
public enum BookOutletKind { Reading, Sample, Ebook, Print, Official, Information }
public enum BookOutletLevel { Unspecified, Series, Volume, Publication, Chapter, Collection }
public sealed record BookOutletEvidence(BookIdentity Identity, string Format, string SourceUrl,
    BookExternalLink Original, DateTimeOffset FetchedAtUtc)
{
    public string? CrossReference { get; init; }
}
public sealed record BookOutletLink(string Platform, string Url, BookOutletKind Kind, BookOutletLevel Level,
    string? Language, string? Storefront, string? Volume, string? Isbn, string Purpose, BookOutletEvidence[] Sources);
public sealed record BookLinkReview(BookExternalLink Original, string Reason);
public sealed record BookOutletSet(BookOutletLink[] Links, BookLinkReview[] Excluded);

public static class BookOutlets
{
    public static bool ValidRaw(BookExternalLink? value) => value is not null && value.Id >= 0
        && AniListValidation.Text(value.Site, 100, false) && AniListValidation.OptionalText(value.Url, 2048)
        && AniListValidation.Text(value.Type, 100) && AniListValidation.OptionalText(value.Language, 100)
        && AniListValidation.OptionalText(value.Notes, 500);
    private static bool Match(string value, string pattern) => Regex.IsMatch(value, pattern,
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50));
    public static string Medium(string format) => format is "NOVEL" or "小说" or "轻小说" ? "NOVEL"
        : format is "MANGA" or "ONE_SHOT" or "漫画" ? "MANGA" : "";
    public static BookOutletSet FromAniList(AniListDetails details)
        => details.Media.Kind != AniListMediaKind.Manga || !AniListValidation.Details(details) ? new([], [])
        : From(new("AniList", details.Media.Id.ToString(CultureInfo.InvariantCulture), BookEntityLevel.Unknown),
            details.Media.Format, details.Media.Url, details.BookLinks, details.FetchedAtUtc);
    public static BookOutletSet FromBangumi(BangumiSubjectDetails details)
        => details.Kind != BangumiRelationKind.Book || !BangumiSubjectDetailsService.Valid(details.Id, details.Kind, details) ? new([], [])
        : From(BookSource.From(details).Identity, details.Platform ?? "", details.Url, details.BookFacts?.ExternalLinks ?? [], details.FetchedAtUtc);
    public static BookOutletSet From(BookIdentity identity, string format, string sourceUrl,
        IEnumerable<BookExternalLink> input, DateTimeOffset fetched)
    {
        if (!identity.IsValid || !WatchLinks.SafeUrl(sourceUrl) || fetched == default) return new([], []);
        var links = new List<BookOutletLink>(); var excluded = new List<BookLinkReview>();
        foreach (var raw in input.Take(96))
        {
            if (!ValidRaw(raw)) continue;
            if (raw.IsDisabled) { excluded.Add(new(raw, "来源已停用此链接")); continue; }
            if (!WatchLinks.SafeUrl(raw.Url)) { excluded.Add(new(raw, "地址不符合安全外链规则")); continue; }
            var uri = new Uri(raw.Url!); var path = uri.AbsolutePath; var host = uri.IdnHost;
            var query = uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries);
            if (raw.Type == "SOCIAL" || path is "" or "/" || Match(path, @"/(search|searching|s|login|signin|account|redirect|out)(/|$)")
                || query.Any(q => Match(Uri.UnescapeDataString(q.Split('=')[0]), @"^(q|query|search|keyword|keywords|redirect|redirect_url|url|target|returnUrl|next)$")))
            { excluded.Add(new(raw, "首页、搜索、社交或跳转地址不作为条目入口")); continue; }
            bool novel = Medium(format) == "NOVEL";
            bool mangaOnly = host is "comic-walker.com" or "manga.bilibili.com" or "ac.qq.com" or "www.kuaikanmanhua.com"
                or "shonenjumpplus.com" or "mangaplus.shueisha.co.jp" or "younganimal.com" or "manga-park.com"
                or "www.shonenjump.com" or "www.sunday-webry.com" or "sunday-webry.com" or "zebrack-comic.shueisha.co.jp";
            if (mangaOnly && BookOutlets.Medium(format) != "MANGA"
                || novel && Match(path, @"(?:^|[-_/])(manga|comics|comic|comicslist)(?:[-_/]|$)")
                || !novel && Medium(format) == "MANGA" && Match(path, @"(?:^|[-_/])(light-novel|novel)(?:[-_/]|$)"))
            { excluded.Add(new(raw, "目标页面明确的漫画／小说类型与当前条目冲突")); continue; }

            string platform = raw.Site; string? market = null; BookOutletKind kind = BookOutletKind.Information;
            BookOutletLevel level = BookOutletLevel.Unspecified; string purpose = "资料来源";
            bool route = false;
            switch (host)
            {
                case "bookwalker.jp": case "global.bookwalker.jp": case "www.bookwalker.com.tw": case "bookwalker.com.tw":
                    platform = "BOOK☆WALKER"; market = host.EndsWith(".tw", StringComparison.Ordinal) ? "TW" : host == "bookwalker.jp" ? "JP" : null;
                    route = Match(path, @"^/(de[0-9a-f-]{32,36}|series/[0-9]+(?:/list)?|book/[0-9]+)/?$");
                    kind = BookOutletKind.Ebook; level = path.StartsWith("/series/", StringComparison.Ordinal) ? BookOutletLevel.Series : BookOutletLevel.Publication; break;
                case "ebookjapan.yahoo.co.jp":
                    platform = "ebookjapan"; market = "JP"; route = Match(path, @"^/books/[0-9]+(?:/[0-9a-z-]+)?/?$");
                    kind = BookOutletKind.Ebook; level = path.Trim('/').Split('/').Length == 2 ? BookOutletLevel.Series : BookOutletLevel.Publication; break;
                case "play.google.com":
                    platform = "Google Play Books"; route = path.StartsWith("/store/books/details", StringComparison.Ordinal)
                        && query.Any(x => x.StartsWith("id=", StringComparison.Ordinal) && x.Length > 3);
                    kind = BookOutletKind.Ebook; level = BookOutletLevel.Publication; break;
                case "mangaplus.shueisha.co.jp": case "mangamillion.shueisha.co.jp":
                    platform = host.StartsWith("mangaplus", StringComparison.Ordinal) ? "MANGA Plus" : "MANGA MILLION";
                    route = Match(path, @"^/(titles|[a-z-]+/title|viewer)/[0-9]+/?$"); kind = BookOutletKind.Reading;
                    level = path.Contains("/viewer/", StringComparison.Ordinal) ? BookOutletLevel.Chapter : BookOutletLevel.Series; break;
                case "comic-walker.com":
                    platform = "Kadocomi"; route = Match(path, @"^/(detail/KC_[A-Z0-9_]+|contents/detail/[A-Z0-9_]+)(/|$)");
                    kind = BookOutletKind.Reading; level = path.Contains("/episodes/", StringComparison.Ordinal) ? BookOutletLevel.Chapter : BookOutletLevel.Series; break;
                case "manga.bilibili.com":
                    platform = "哔哩哔哩漫画"; route = Match(path, @"^/detail/mc[0-9]+/?$"); kind = BookOutletKind.Reading; level = BookOutletLevel.Series; break;
                case "ac.qq.com":
                    platform = "腾讯动漫"; route = Match(path, @"^/Comic/comicInfo/id/[0-9]+/?$"); kind = BookOutletKind.Reading; level = BookOutletLevel.Series; break;
                case "www.kuaikanmanhua.com":
                    platform = "快看漫画"; route = Match(path, @"^/web/topic/[0-9]+/?$"); kind = BookOutletKind.Reading; level = BookOutletLevel.Series; break;
                case "www.viz.com":
                    platform = "VIZ"; route = Match(path, @"^/(shonenjump|vizmanga)/chapters/[a-z0-9-]+/?$");
                    kind = route ? BookOutletKind.Reading : BookOutletKind.Official; level = BookOutletLevel.Series;
                    route |= Match(path, @"^/[a-z0-9]+(?:-[a-z0-9]+)+/?$") && (raw.Notes == "Volumes" || raw.Type == "engtl");
                    purpose = "选卷与购买"; break;
                case "j-novel.club":
                    platform = "J-Novel Club"; route = Match(path, @"^/(s|series)/[a-z0-9-]+/?$"); kind = BookOutletKind.Reading; level = BookOutletLevel.Series; break;
                case "younganimal.com": case "manga-park.com": case "zebrack-comic.shueisha.co.jp":
                    route = Match(path, @"^/(series|title)/[a-z0-9]+/?$"); kind = BookOutletKind.Reading; level = BookOutletLevel.Series; break;
                case "shonenjumpplus.com": case "pocket.shonenmagazine.com": case "comic-days.com":
                    route = Match(path, @"^/episode/[0-9]+/?$"); kind = BookOutletKind.Reading; level = BookOutletLevel.Chapter; break;
                case "www.sunday-webry.com": case "sunday-webry.com":
                    platform = "Sunday Webry"; route = path == "/detail.php" && query.Any(x => Match(x, @"^title_id=[0-9]+$")) || Match(path, @"^/episode/[0-9]+/?$");
                    kind = BookOutletKind.Reading; level = path == "/detail.php" ? BookOutletLevel.Series : BookOutletLevel.Chapter; break;
                case "www.shonenjump.com":
                    platform = "Shonen Jump"; route = Match(path, @"^/j/rensai/[a-z0-9_-]+\.html$"); kind = BookOutletKind.Reading; level = BookOutletLevel.Series; break;
                case "piccoma.com" when !novel:
                    platform = "Piccoma"; route = Match(path, @"^/web/product/[0-9]+/?$"); kind = BookOutletKind.Reading; level = BookOutletLevel.Series; break;
                case "www.lezhin.com" when !novel:
                    platform = "Lezhin"; route = Match(path, @"^/[a-z-]+/comic/[a-z0-9_-]+/?$"); kind = BookOutletKind.Reading; level = BookOutletLevel.Series; break;
                case "series.naver.com":
                    platform = "Naver Series"; route = (novel ? path == "/novel/detail.series" : path == "/comic/detail.series") && query.Any(x => Match(x, @"^productNo=[0-9]+$"));
                    kind = BookOutletKind.Reading; level = BookOutletLevel.Series; break;
                case "page.kakao.com" when !novel:
                    platform = "KakaoPage"; route = Match(path, @"^/content/[0-9]+/?$"); kind = BookOutletKind.Reading; level = BookOutletLevel.Series; break;
                case "www.ono.live" when !novel: case "www.mangas.io" when !novel:
                    route = Match(path, @"^/(manga|lire)/[a-z0-9-]+/?$"); kind = BookOutletKind.Reading; level = BookOutletLevel.Series; break;
                case "yenpress.com": case "kodansha.us": case "sevenseasentertainment.com":
                    platform = host switch { "yenpress.com" => "Yen Press", "kodansha.us" => "Kodansha USA", _ => "Seven Seas" };
                    route = Match(path, @"^/series/[a-z0-9-]+/?$") || Match(path, @"^/(titles|books)/[a-z0-9-]+/?$");
                    kind = BookOutletKind.Official; level = path.StartsWith("/series/", StringComparison.Ordinal) ? BookOutletLevel.Series : BookOutletLevel.Publication;
                    purpose = level == BookOutletLevel.Series ? "选卷与购买" : "出版物与购买"; break;
                case "www.kadokawa.co.jp": case "www.kadokawa.com.tw":
                    platform = host.EndsWith(".tw", StringComparison.Ordinal) ? "台湾角川" : "KADOKAWA";
                    route = Match(path, @"^/products?/[a-z0-9-]+/?$"); kind = BookOutletKind.Official;
                    level = BookOutletLevel.Publication; purpose = "出版物与购买"; break;
                case "dengekibunko.jp": case "sneakerbunko.jp": case "mfbunkoj.jp":
                    platform = host switch { "dengekibunko.jp" => "电击文库", "sneakerbunko.jp" => "角川Sneaker文库", _ => "MF文库J" };
                    route = Match(path, @"^/(product|series)/[a-z0-9-]+/?$"); kind = BookOutletKind.Official; level = BookOutletLevel.Series; purpose = "选卷与购买"; break;
                case "herobunko.com":
                    platform = "Hero文库"; route = Match(path, @"^/category/books/[a-z0-9-]+/?$"); kind = BookOutletKind.Official; level = BookOutletLevel.Series; purpose = "选卷与购买"; break;
                case "www.hakusensha.co.jp":
                    platform = "白泉社"; route = Match(path, @"^/comicslist/[0-9]+/?$"); kind = BookOutletKind.Official; level = BookOutletLevel.Publication; purpose = "出版物与购买"; break;
                case "www.darkhorse.com" when !novel:
                    platform = "Dark Horse"; route = Match(path, @"^/books/[0-9-]+/[a-z0-9-]+/?$"); kind = BookOutletKind.Official; level = BookOutletLevel.Publication; purpose = "出版物与购买"; break;
                case "www.amazon.co.jp": case "www.amazon.com": case "www.amazon.co.uk": case "www.amazon.de": case "www.amazon.fr":
                    platform = "Amazon"; market = host switch { "www.amazon.co.jp" => "JP", "www.amazon.co.uk" => "GB", "www.amazon.de" => "DE", "www.amazon.fr" => "FR", _ => "US" };
                    route = Match(path, @"/(dp|gp/product)/[A-Z0-9]{10}/?$"); kind = BookOutletKind.Official; level = BookOutletLevel.Publication; purpose = "商品页 · 格式未标注"; break;
            }
            if (route)
            {
                if (raw.Type is "试读" or "試し読み" || raw.Notes is "Sample" or "Preview" or "試し読み" or "试读") kind = BookOutletKind.Sample;
                else if (raw.Type is "纸质书" or "纸质购买" && kind == BookOutletKind.Official) kind = BookOutletKind.Print;
                else if (raw.Type is "电子书" or "电子版" && kind == BookOutletKind.Official) kind = BookOutletKind.Ebook;
                else if (Match(path, @"(?:^|[-_/])(paperback|hardcover|tpb)(?:[-_/]|$)")) kind = BookOutletKind.Print;
            }
            else { kind = BookOutletKind.Information; purpose = "资料来源"; }
            string? volume = null, isbn = null;
            var volumeMatch = Regex.Match(path, @"(?:^|[-_/])(?:vol(?:ume)?|book)[-_/](\d{1,4})(?:[-_/]|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50));
            if (volumeMatch.Success) { volume = volumeMatch.Groups[1].Value; level = BookOutletLevel.Volume; }
            if (Match(path, @"(?:^|[-_/])(omnibus|box-set|complete|collected)(?:[-_/]|$)")) level = BookOutletLevel.Collection;
            foreach (var segment in path.Split('/')) if (BookIsbn.Normalize(segment) is { } number) isbn = number;
            var proof = new BookOutletEvidence(identity, format, sourceUrl, raw, fetched);
            links.Add(new(platform, raw.Url!, kind, level, raw.Language, market, volume, isbn, purpose, [proof]));
        }
        return new(Merge(links), excluded.ToArray());
    }
    private static string Resource(BookOutletLink value) => WatchLinks.ExternalResourceKey(value.Url, preserveTag: true) + "|" + value.Kind
        + "|" + value.Level + "|" + value.Volume + "|" + value.Isbn;
    public static string Key(BookOutletLink value) => Resource(value) + "|" + LanguageName(value.Language);
    public static BookOutletLink[] Merge(IEnumerable<BookOutletLink> input)
    {
        var values = input.Where(x => WatchLinks.SafeUrl(x.Url)).Take(192).ToArray();
        var known = values.Where(x => !string.IsNullOrEmpty(x.Language)).GroupBy(Resource).ToDictionary(g => g.Key,
            g => g.Select(x => x.Language).DistinctBy(LanguageName).ToArray(), StringComparer.Ordinal);
        // An unlabelled copy of the same resource adds provenance, not another
        // edition. Conflicting explicit languages and version parameters stay distinct.
        var resolved = values.Select(x => string.IsNullOrEmpty(x.Language) && known.TryGetValue(Resource(x), out var languages) && languages.Length == 1
            ? x with { Language = languages[0] } : x);
        return WatchLinks.OrderPlatforms(resolved.GroupBy(Key).Select(g => g.First() with
        { Sources = g.SelectMany(x => x.Sources).Distinct().Take(8).ToArray() }), x => x.Platform, [], true).ToArray();
    }
    public static string KindName(BookOutletLink value) => value.Kind switch
    { BookOutletKind.Reading => "阅读／连载", BookOutletKind.Sample => "试读", BookOutletKind.Ebook => "电子书", BookOutletKind.Print => "纸质书", BookOutletKind.Official => value.Purpose, _ => "资料来源" };
    public static string ScopeName(BookOutletLink link, BookSource current)
    {
        if (link.Level == BookOutletLevel.Series) return current.Identity.Level is BookEntityLevel.Volume or BookEntityLevel.Publication ? "前往本系列" : "系列入口";
        if (link.Level == BookOutletLevel.Collection) return "合订本／套装 · 请核对版本";
        if (link.Volume is { } volume) return "平台第 " + volume + " 卷" + (current.Volume is { } selected && selected != volume ? " · 与当前卷不同" : " · 版本以平台为准");
        if (link.Isbn is { } isbn) return current.Isbns.Contains(isbn, StringComparer.Ordinal) ? "对应 ISBN " + isbn
            : (current.Isbns.Length > 0 ? "其他出版物 · ISBN " : "出版物 · ISBN ") + isbn;
        return link.Level switch { BookOutletLevel.Chapter => "平台章节页", BookOutletLevel.Publication => "出版物入口 · 请核对卷册与版本", _ => "作品入口 · 层级未标注" };
    }
    public static string LanguageName(string? language) => language switch
    { "Japanese" or "ja" or "jpn" => "日语", "English" or "en" or "eng" => "英语", "Chinese" or "zh" or "chi" or "zho" => "中文", "Korean" or "ko" => "韩语", "French" or "fr" => "法语", "German" or "de" => "德语", "Spanish" or "es" => "西班牙语", "Thai" or "th" => "泰语", "Indonesian" or "id" => "印尼语", null or "" => "语言未标注", _ => language };
}
