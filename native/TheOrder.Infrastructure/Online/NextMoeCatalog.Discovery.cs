using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using TheOrder.Application;
using TheOrder.Domain;
using static TheOrder.Infrastructure.Online.RemoteWorkDto;

namespace TheOrder.Infrastructure.Online;

public sealed partial class NextMoeCatalog : ICatalogDiscovery
{
    private const int DiscoveryPageSize = 24;

    public Task<ReleaseCalendarPage> CalendarAsync(DateOnly month, ReleaseCalendarWindow window, string? cursor,
        bool showNsfw, CancellationToken cancellation)
    {
        var pageSize = window == ReleaseCalendarWindow.Month ? 100 : DiscoveryPageSize;
        var query = WorkFeedQuery(cursor, showNsfw, pageSize);
        switch (window)
        {
            case ReleaseCalendarWindow.Month:
                query["month"] = month.ToString("yyyy-MM", CultureInfo.InvariantCulture);
                break;
            case ReleaseCalendarWindow.Year:
                query["year"] = month.ToString("yyyy", CultureInfo.InvariantCulture);
                query["precision"] = "year";
                break;
            case ReleaseCalendarWindow.Undated:
                query["status"] = "unknown";
                break;
            default: throw new OnlineException(OnlineFailure.InvalidRequest);
        }
        return transport.Operation(async scope =>
        {
            using var body = await transport.GetAsync("catalog/calendar", query, scope).ConfigureAwait(false);
            var root = body.RootElement;
            var next = FeedCursor(root, pageSize);
            var meta = Field(root, "meta");
            if (!DateOnly.TryParseExact(Text(meta, "today"), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var today))
                throw new OnlineException(OnlineFailure.InvalidResponse);
            var works = Rows(root, "items").Select(row => MapDiscoveryWork(row, showNsfw)).OfType<WorkMetadata>()
                .DistinctBy(work => work.Id).ToImmutableArray();
            scope.Checkpoint(OnlineAcceptanceStage.Mapped);
            return new ReleaseCalendarPage(new(works, next), today, CalendarFlag(meta, "has_prev"), CalendarFlag(meta, "has_next"));
        }, cancellation);
    }

    public Task<DiscoveryPage<RankedWork>> RankingsAsync(DiscoveryRankingSource source, string? cursor,
        bool showNsfw, CancellationToken cancellation, DiscoveryRankingFilters? filters = null)
    {
        filters ??= new();
        if (!filters.IsValid) throw new OnlineException(OnlineFailure.InvalidRequest);
        var query = WorkFeedQuery(cursor, showNsfw);
        var (by, metric, unit) = source switch
        {
            DiscoveryRankingSource.Consensus => ("consensus", "mean", "deviation"),
            DiscoveryRankingSource.NextMoe => ("nextmoe", "native", "native"),
            DiscoveryRankingSource.Vndb => ("vndb", "native", "native"),
            DiscoveryRankingSource.Bangumi => ("bangumi", "native", "native"),
            DiscoveryRankingSource.ErogameScape => ("erogamescape", "native", "native"),
            DiscoveryRankingSource.DLsite => ("dlsite", "native", "native"),
            DiscoveryRankingSource.HowLongToBeat => ("howlongtobeat", "native", "native"),
            _ => throw new OnlineException(OnlineFailure.InvalidRequest)
        };
        query["by"] = by;
        query["metric"] = metric;
        query["scope"] = filters.IncludeOtherGames ? "all" : "vn";
        void SetNumber(string key, int? value)
        {
            if (value is int number) query[key] = number.ToString(CultureInfo.InvariantCulture);
        }
        SetNumber("min_votes", filters.MinVotes);
        SetNumber("max_votes", filters.MaxVotes);
        if (source == DiscoveryRankingSource.Consensus) SetNumber("min_sources", filters.MinSources);
        if (filters.FromYear is { } from && filters.ThroughYear is { } through)
            query["year"] = from == through ? FormattableString.Invariant($"{from}") : FormattableString.Invariant($"{from}-{through}");
        else if (filters.FromYear is { } start) query["released_after"] = FormattableString.Invariant($"{start}-01-01");
        else if (filters.ThroughYear is { } end) query["released_before"] = FormattableString.Invariant($"{end}-12-31");
        if (filters.Platform != RankingPlatform.Any)
            query["platform"] = filters.Platform switch
            {
                RankingPlatform.Windows => "win",
                RankingPlatform.Mac => "mac",
                RankingPlatform.Switch => "swi,sw2",
                RankingPlatform.PlayStation => "psp,psv,ps1,ps2,ps3,ps4,ps5",
                RankingPlatform.Mobile => "and,ios,mob",
                RankingPlatform.Linux => "lin",
                _ => throw new OnlineException(OnlineFailure.InvalidRequest)
            };
        if (filters.Circulation != RankingCirculation.Any)
            query["circulation"] = filters.Circulation == RankingCirculation.Commercial ? "commercial" : "doujin";
        if (filters.Language != RankingLanguage.Any)
            query["olang"] = filters.Language switch
            {
                RankingLanguage.Japanese => "ja",
                RankingLanguage.Chinese => "zh,zh-Hans,zh-Hant",
                RankingLanguage.English => "en",
                _ => throw new OnlineException(OnlineFailure.InvalidRequest)
            };
        SetNumber("playtime_min", filters.Duration switch { RankingDuration.Medium => 601, RankingDuration.Long => 1801, _ => null });
        SetNumber("playtime_max", filters.Duration switch { RankingDuration.Short => 600, RankingDuration.Medium => 1800, _ => null });
        var maximum = DiscoveryRankingScale.NativeMaximum(source);
        return transport.Operation<DiscoveryPage<RankedWork>>(async scope =>
        {
            using var body = await transport.GetAsync("catalog/rankings", query, scope).ConfigureAwait(false);
            var root = body.RootElement;
            var next = FeedCursor(root);
            if (Text(root, "by") != by || Text(root, "metric") != metric || Text(root, "unit") != unit)
                throw new OnlineException(OnlineFailure.InvalidResponse);
            var items = ImmutableArray.CreateBuilder<RankedWork>();
            foreach (var row in Rows(root, "items"))
            {
                var rankValue = Field(row, "rank");
                var scoreValue = Field(row, "value");
                if (Text(row, "object") != "ranking_entry" || rankValue.ValueKind != JsonValueKind.Number
                    || !rankValue.TryGetInt32(out var rank) || rank < 1 || scoreValue.ValueKind != JsonValueKind.Number
                    || !scoreValue.TryGetDecimal(out var score) || maximum is { } limit && (score < 0 || score > limit))
                    throw new OnlineException(OnlineFailure.InvalidResponse);
                if (MapDiscoveryWork(Field(row, "work"), showNsfw) is not { } work) continue;
                var siteScores = Rows(row, "per_source");
                var totals = source == DiscoveryRankingSource.Consensus ? Field(row, "consensus")
                    : siteScores.FirstOrDefault(value => Text(value, "source") == by);
                var votes = Field(totals, "vote_count");
                items.Add(new RankedWork(rank, work, score, votes.ValueKind == JsonValueKind.Number
                    && votes.TryGetInt64(out var count) && count >= 0 ? count : null)
                { SiteScores = ReadSiteScores(siteScores) });
            }
            scope.Checkpoint(OnlineAcceptanceStage.Mapped);
            return new(items.ToImmutable(), next);
        }, cancellation);
    }

    private static ImmutableArray<RankingSiteScore> ReadSiteScores(IEnumerable<JsonElement> sites)
    {
        var ratings = ImmutableArray.CreateBuilder<RankingSiteScore>();
        foreach (var site in sites.Take(12))
        {
            var name = Text(site, "source");
            var value = Field(site, "score");
            if (name.Length != 0 && value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var score) && score is > 0 and <= 100)
                ratings.Add(new(name, score));
        }
        return ratings.DistinctBy(site => site.Source).ToImmutableArray();
    }

    public Task<DiscoveryPage<DiscoveryNewsItem>> NewsAsync(DiscoveryNewsLane lane, string? cursor,
        bool showNsfw, CancellationToken cancellation, DiscoveryNewsSource source = DiscoveryNewsSource.All)
    {
        var query = FeedQuery(cursor);
        query["nsfw"] = showNsfw ? "true" : "false";
        if (source != DiscoveryNewsSource.All)
            query["source"] = source switch
            {
                DiscoveryNewsSource.Community => "community",
                DiscoveryNewsSource.GalgameHihyou => "galgame_hihyou",
                DiscoveryNewsSource.Ymgal => "ymgal",
                _ => throw new OnlineException(OnlineFailure.InvalidRequest)
            };
        if (lane != DiscoveryNewsLane.All)
            query["lane"] = lane switch
            {
                DiscoveryNewsLane.News => "news",
                DiscoveryNewsLane.Column => "column",
                _ => throw new OnlineException(OnlineFailure.InvalidRequest)
            };
        return transport.Operation<DiscoveryPage<DiscoveryNewsItem>>(async scope =>
        {
            using var body = await transport.GetAsync("news", query, scope).ConfigureAwait(false);
            var root = body.RootElement;
            var next = FeedCursor(root);
            var items = Rows(root, "items").Select(row => MapNews(row, showNsfw)).ToImmutableArray();
            scope.Checkpoint(OnlineAcceptanceStage.Mapped);
            return new(items, next);
        }, cancellation);
    }

    private static DiscoveryNewsItem MapNews(JsonElement row, bool showNsfw)
    {
        var id = Text(row, "id");
        var title = Text(row, "title");
        var source = Field(row, "source");
        var sourceName = Text(source, "display_name");
        var attribution = Text(source, "attribution");
        var lane = Text(row, "lane");
        if (Text(row, "object") != "news_item" || !ValidId(id) || title.Length == 0 || sourceName.Length == 0
            || attribution.Length == 0 || lane is not ("news" or "column")
            || !DateTimeOffset.TryParse(Text(row, "published_at"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var published))
            throw new OnlineException(OnlineFailure.InvalidResponse);

        var original = Text(row, "source_url");
        Uri? article = null;
        if (Uri.TryCreate(original, UriKind.Absolute, out var uri) && RemoteCoverSource.Allowed(uri))
            article = uri;
        else if (original.Length == 0 && Text(source, "name") == "community" && Field(row, "has_body").ValueKind == JsonValueKind.True)
            article = new Uri("https://www.kungal.com/news/" + id);

        var banner = Field(row, "banner");
        var image = showNsfw || Text(banner, "sexual") is "safe" or "suggestive" ? Image(banner) : null;
        return new(id, title, Text(row, "summary"), sourceName, attribution, published, lane == "column", image, article);
    }

    private Dictionary<string, string> WorkFeedQuery(string? cursor, bool showNsfw, int pageSize = DiscoveryPageSize)
    {
        var query = FeedQuery(cursor, pageSize);
        query["nsfw"] = includeExplicit ? "true" : "false";
        query["include"] = "titles,covers,companies";
        if (!showNsfw) query["content_limit"] = "sfw";
        return query;
    }

    private static Dictionary<string, string> FeedQuery(string? cursor, int pageSize = DiscoveryPageSize)
    {
        var query = new Dictionary<string, string> { ["limit"] = pageSize.ToString(CultureInfo.InvariantCulture) };
        if (cursor is not null)
        {
            if (!ValidFeedCursor(cursor)) throw new OnlineException(OnlineFailure.InvalidRequest);
            query["cursor"] = cursor;
        }
        return query;
    }

    private static bool ValidFeedCursor(string value) => value.Length is > 4 and <= 512 && value.StartsWith("cur_", StringComparison.Ordinal);

    private static string? FeedCursor(JsonElement root, int pageSize = DiscoveryPageSize)
    {
        RequireList(root, pageSize);
        var cursor = Field(root, "next_cursor");
        if (cursor.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null) return null;
        if (cursor.ValueKind == JsonValueKind.String && cursor.GetString() is { } value && ValidFeedCursor(value)) return value;
        throw new OnlineException(OnlineFailure.InvalidResponse);
    }

    private static bool? CalendarFlag(JsonElement meta, string field) => Field(meta, field).ValueKind switch
    {
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => null
    };
}
