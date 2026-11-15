using System.Collections.Immutable;
using TheOrder.Domain;

namespace TheOrder.Application;

public enum ReleaseCalendarWindow { Month, Year, Undated }
public enum DiscoveryRankingSource { Consensus, NextMoe, Vndb, Bangumi, ErogameScape, DLsite, HowLongToBeat }

public static class DiscoveryRankingScale
{
    public static int? NativeMaximum(DiscoveryRankingSource source) => source switch
    {
        DiscoveryRankingSource.Vndb or DiscoveryRankingSource.Bangumi or DiscoveryRankingSource.NextMoe => 10,
        DiscoveryRankingSource.DLsite => 5,
        DiscoveryRankingSource.ErogameScape or DiscoveryRankingSource.HowLongToBeat => 100,
        _ => null
    };
}
public enum RankingPlatform { Any, Windows, Mac, Switch, PlayStation, Mobile, Linux }
public enum RankingCirculation { Any, Commercial, Doujin }
public enum RankingDuration { Any, Short, Medium, Long }
public enum RankingLanguage { Any, Japanese, Chinese, English }

public sealed record DiscoveryRankingFilters
{
    public int? MinVotes { get; init; }
    public int? MaxVotes { get; init; }
    public int? FromYear { get; init; }
    public int? ThroughYear { get; init; }
    public RankingPlatform Platform { get; init; }
    public RankingCirculation Circulation { get; init; }
    public RankingDuration Duration { get; init; }
    public RankingLanguage Language { get; init; }
    public int? MinSources { get; init; }
    public bool IncludeOtherGames { get; init; }

    public bool IsValid => (MinVotes is null or >= 1 and <= 1_000_000) && (MaxVotes is null or >= 1 and <= 1_000_000)
        && !(MinVotes > MaxVotes) && (FromYear is null or >= 1900 and <= 2100) && (ThroughYear is null or >= 1900 and <= 2100)
        && !(FromYear > ThroughYear) && (MinSources is null or >= 2 and <= 6)
        && Enum.IsDefined(Platform) && Enum.IsDefined(Circulation) && Enum.IsDefined(Duration) && Enum.IsDefined(Language);

    public int ActiveCount(bool consensus) => (MinVotes.HasValue || MaxVotes.HasValue ? 1 : 0)
        + (FromYear.HasValue || ThroughYear.HasValue ? 1 : 0)
        + (Platform == RankingPlatform.Any ? 0 : 1) + (Circulation == RankingCirculation.Any ? 0 : 1)
        + (Duration == RankingDuration.Any ? 0 : 1) + (Language == RankingLanguage.Any ? 0 : 1)
        + (consensus && MinSources.HasValue ? 1 : 0) + (IncludeOtherGames ? 1 : 0);
}

public enum DiscoveryNewsLane { All, News, Column }
public enum DiscoveryNewsSource { All, Community, GalgameHihyou, Ymgal }

public sealed record DiscoveryPage<T>(ImmutableArray<T> Items, string? NextCursor);
public sealed record ReleaseCalendarPage(DiscoveryPage<WorkMetadata> Works, DateOnly Today, bool? HasPreviousMonth, bool? HasNextMonth);
public sealed record RankedWork(int Rank, WorkMetadata Work, decimal Score, long? Votes)
{
    public ImmutableArray<RankingSiteScore> SiteScores { get; init; } = [];
}
public sealed record RankingSiteScore(string Source, decimal Score);
public sealed record DiscoveryNewsItem(string Id, string Title, string Summary, string Source, string Attribution,
    DateTimeOffset PublishedAt, bool IsColumn, string? BannerUrl, Uri? ArticleUri);

public interface ICatalogDiscovery
{
    Task<ReleaseCalendarPage> CalendarAsync(DateOnly month, ReleaseCalendarWindow window, string? cursor, bool showNsfw, CancellationToken cancellation);
    Task<DiscoveryPage<RankedWork>> RankingsAsync(DiscoveryRankingSource source, string? cursor, bool showNsfw, CancellationToken cancellation,
        DiscoveryRankingFilters? filters = null);
    Task<DiscoveryPage<DiscoveryNewsItem>> NewsAsync(DiscoveryNewsLane lane, string? cursor, bool showNsfw, CancellationToken cancellation,
        DiscoveryNewsSource source = DiscoveryNewsSource.All);
}
