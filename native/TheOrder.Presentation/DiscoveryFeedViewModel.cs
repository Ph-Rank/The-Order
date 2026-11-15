using System.Collections.ObjectModel;
using System.Globalization;
using TheOrder.Application;
using TheOrder.Domain;

namespace TheOrder.Presentation;

public enum DiscoverySection { Search, Calendar, Rankings, News }

public sealed record DiscoveryRow(string Id, string Title, string? ImageKey, string Leading, string Metadata,
    string Summary = "", string Attribution = "", Uri? ArticleUri = null, string Score = "", string ScoreLabel = "")
{
    public DateOnly? ReleaseDay { get; init; }
    public string[] SiteRatings { get; init; } = [];
    public bool HasImage => ImageKey is not null;
    public bool HasMetadata => Metadata.Length != 0;
    public bool HasSummary => Summary.Length != 0;
    public bool HasScoreLabel => ScoreLabel.Length != 0;
    public bool IsArticle => Attribution.Length != 0;
    public bool HasArticleLink => ArticleUri is not null;
    public bool HasSiteRatings => SiteRatings.Length != 0;
    public string AccessibleName => string.Join("，", new[] { Leading, Title, Metadata, (ScoreLabel + " " + Score).Trim() }.Where(value => value.Length != 0));
}

public sealed class DiscoveryFeedViewModel(ICatalogDiscovery catalog, CatalogBrowsePolicy policy, OnlineAuthContext auth,
    IRemoteCoverRegistry covers) : ObservableState
{
    private const int ResultLimit = 500;
    private readonly Dictionary<DiscoverySection, FeedState> feeds = new()
    {
        [DiscoverySection.Search] = new(),
        [DiscoverySection.Calendar] = new(),
        [DiscoverySection.Rankings] = new(),
        [DiscoverySection.News] = new()
    };
    private CatalogBrowseScope currentPolicy = policy.Capture();
    private long currentAuth = auth.Generation;

    private FeedState ActiveFeed => feeds[Section];
    private FeedState CalendarFeed => feeds[DiscoverySection.Calendar];
    public ObservableCollection<DiscoveryRow> Items => ActiveFeed.Items;
    public DiscoverySection Section { get; private set; }
    public ReleaseCalendarWindow CalendarWindow { get; private set; }
    public DiscoveryRankingSource RankingSource { get; private set; }
    public DiscoveryRankingFilters RankingFilters { get; private set; } = new();
    public DiscoveryNewsLane NewsLane { get; private set; }
    public DiscoveryNewsSource NewsSource { get; private set; }
    public DateOnly Today { get; private set; } = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(9));
    public DateOnly Month { get; private set; } = MonthStart(DateOnly.FromDateTime(DateTime.UtcNow.AddHours(9)));
    public string Message => ActiveFeed.Message;
    public bool Failed => ActiveFeed.Failed;
    public bool Busy => ActiveFeed.Pending is not null;
    public string? PaginationCursor => ActiveFeed.Cursor;
    public bool CanLoadMore => !Busy && PaginationCursor is not null;
    public bool LoadingMore => Busy && ActiveFeed.LoadingMore;
    public int ResultRevision => ActiveFeed.ResultRevision;

    public Task SelectAsync(DiscoverySection section)
    {
        InvalidateIfStale();
        if (Section != section)
        {
            Section = section;
            Notify();
        }
        return EnsureLoadedAsync();
    }

    public Task EnsureLoadedAsync()
    {
        InvalidateIfStale();
        return Section != DiscoverySection.Search && !ActiveFeed.Loaded && !Failed ? LoadAsync() : Task.CompletedTask;
    }

    public Task SelectCalendarWindowAsync(ReleaseCalendarWindow window)
    {
        if (CalendarWindow == window) return Task.CompletedTask;
        CalendarWindow = window;
        return ChangeFilterAsync(DiscoverySection.Calendar);
    }

    public Task SelectRankingSourceAsync(DiscoveryRankingSource source)
    {
        if (RankingSource == source) return Task.CompletedTask;
        RankingSource = source;
        return ChangeFilterAsync(DiscoverySection.Rankings);
    }

    public Task SelectRankingFiltersAsync(DiscoveryRankingFilters filters)
    {
        if (RankingFilters == filters || !filters.IsValid) return Task.CompletedTask;
        RankingFilters = filters;
        return ChangeFilterAsync(DiscoverySection.Rankings);
    }

    public Task SelectNewsLaneAsync(DiscoveryNewsLane lane)
    {
        if (NewsLane == lane) return Task.CompletedTask;
        NewsLane = lane;
        return ChangeFilterAsync(DiscoverySection.News);
    }

    public Task SelectNewsSourceAsync(DiscoveryNewsSource source)
    {
        if (NewsSource == source) return Task.CompletedTask;
        NewsSource = source;
        return ChangeFilterAsync(DiscoverySection.News);
    }

    public bool CanStep(int direction) => direction is -1 or 1 && (CalendarWindow switch
    {
        ReleaseCalendarWindow.Month => direction < 0
            ? CalendarFeed.HasPreviousMonth != false && Month > DateOnly.MinValue
            : CalendarFeed.HasNextMonth != false && Month < new DateOnly(9999, 12, 1),
        ReleaseCalendarWindow.Year => direction < 0 ? Month.Year > 1 : Month.Year < 9999,
        _ => false
    });

    public Task StepAsync(int direction)
    {
        if (!CanStep(direction)) return Task.CompletedTask;
        Month = Month.AddMonths(CalendarWindow == ReleaseCalendarWindow.Year ? direction * 12 : direction);
        return ChangeFilterAsync(DiscoverySection.Calendar);
    }

    public Task ReturnToCurrentMonthAsync()
    {
        var currentMonth = MonthStart(Today);
        if (Month == currentMonth) return Task.CompletedTask;
        Month = currentMonth;
        return ChangeFilterAsync(DiscoverySection.Calendar);
    }

    public Task ReloadAsync() => LoadAsync(more: false, refresh: true);

    private Task ChangeFilterAsync(DiscoverySection section)
    {
        feeds[section].Reset();
        if (Section != section) return Task.CompletedTask;
        Notify();
        return EnsureLoadedAsync();
    }

    public void Cancel()
    {
        var wasBusy = Busy;
        foreach (var feed in feeds.Values) feed.StopRequest();
        if (wasBusy) Notify();
    }

    public void InvalidateIfStale()
    {
        if (Current()) return;
        currentPolicy = policy.Capture();
        currentAuth = auth.Generation;
        foreach (var feed in feeds.Values) feed.Reset();
        Notify();
    }

    public void ReportLinkFailure()
    {
        feeds[DiscoverySection.News].Message = UiText.Get("链接未能打开。");
        if (Section == DiscoverySection.News) Notify();
    }

    public Task LoadAsync(bool more = false) => LoadAsync(more, refresh: false);

    private async Task LoadAsync(bool more, bool refresh)
    {
        InvalidateIfStale();
        var section = Section;
        var feed = ActiveFeed;
        if (section == DiscoverySection.Search || Busy || more && !CanLoadMore || !more && feed.Loaded && !refresh) return;
        var next = more ? feed.Cursor : null;
        using var request = new CancellationTokenSource();
        feed.Pending = request;
        feed.LoadingMore = more;
        feed.Failed = false;
        feed.Message = "";
        Notify();
        try
        {
            var page = await FetchAsync(section, next, request.Token);
            if (!OwnsResult()) return;

            var items = feed.Items;
            var seen = more ? items.Select(item => item.Id).ToHashSet(StringComparer.Ordinal) : new HashSet<string>(StringComparer.Ordinal);
            var rows = page.Rows.Where(item => seen.Add(item.Id)).Take(ResultLimit - (more ? items.Count : 0)).ToArray();
            if (page.Calendar is { } calendar)
            {
                Today = calendar.Today;
                feed.HasPreviousMonth = calendar.HasPreviousMonth;
                feed.HasNextMonth = calendar.HasNextMonth;
            }
            if (more)
            {
                foreach (var item in rows) items.Add(item);
            }
            else
            {
                feed.UsedCursors.Clear();
                if (!items.SequenceEqual(rows, DiscoveryRowComparer.Instance))
                {
                    items.Clear();
                    foreach (var item in rows) items.Add(item);
                    feed.ResultRevision++;
                }
            }

            if (next is not null) feed.UsedCursors.Add(next);
            feed.Cursor = page.Next is { } candidate && !feed.UsedCursors.Contains(candidate) && items.Count < ResultLimit ? candidate : null;
            feed.Loaded = true;
            if (page.Next is not null && feed.Cursor is null)
                feed.Message = UiText.Get(items.Count >= ResultLimit ? "已达到浏览上限。" : "分页标记重复，已停止加载。");
            else if (items.Count == 0)
                feed.Message = UiText.Get(feed.Cursor is null ? "这个分类暂无内容。" : "本页没有符合当前条件的条目。");
        }
        catch (OperationCanceledException) { }
        catch (OnlineException error)
        {
            if (OwnsResult()) { feed.Failed = true; feed.Message = error.Message; }
        }
        catch (Exception)
        {
            if (OwnsResult())
            { feed.Failed = true; feed.Message = UiText.Get("读取未完成，请重试。"); }
        }
        finally
        {
            if (ReferenceEquals(feed.Pending, request))
            {
                feed.Pending = null;
                if (!Current()) InvalidateIfStale();
                else if (Section == section) Notify();
            }
        }

        bool OwnsResult() => ReferenceEquals(feed.Pending, request) && Current();
    }

    private sealed class FeedState
    {
        public ObservableCollection<DiscoveryRow> Items { get; } = [];
        public HashSet<string> UsedCursors { get; } = new(StringComparer.Ordinal);
        public CancellationTokenSource? Pending;
        public string? Cursor;
        public string Message = "";
        public bool Loaded, LoadingMore, Failed;
        public bool? HasPreviousMonth, HasNextMonth;
        public int ResultRevision;

        public void StopRequest()
        {
            var old = Pending;
            Pending = null;
            old?.Cancel();
        }

        public void Reset()
        {
            StopRequest();
            Items.Clear();
            Cursor = null;
            UsedCursors.Clear();
            Loaded = Failed = false;
            HasPreviousMonth = HasNextMonth = null;
            Message = "";
        }
    }

    private sealed class DiscoveryRowComparer : IEqualityComparer<DiscoveryRow>
    {
        public static readonly DiscoveryRowComparer Instance = new();
        public bool Equals(DiscoveryRow? left, DiscoveryRow? right) =>
            ReferenceEquals(left, right) || left is not null && right is not null &&
            (left with { SiteRatings = right.SiteRatings }) == right && left.SiteRatings.SequenceEqual(right.SiteRatings);
        public int GetHashCode(DiscoveryRow row) => StringComparer.Ordinal.GetHashCode(row.Id);
    }

    private bool Current() => currentPolicy == policy.Capture() && currentAuth == auth.Generation;
    private void Notify() => Changed(string.Empty);
    private static DateOnly MonthStart(DateOnly date) => new(date.Year, date.Month, 1);

    // Defer projection until the response is still valid for its own feed.
    private async Task<(IEnumerable<DiscoveryRow> Rows, string? Next, ReleaseCalendarPage? Calendar)> FetchAsync(
        DiscoverySection section, string? next, CancellationToken cancellation)
    {
        var showNsfw = currentPolicy.ShowNsfw;
        switch (section)
        {
            case DiscoverySection.Calendar:
                var calendar = await catalog.CalendarAsync(Month, CalendarWindow, next, showNsfw, cancellation);
                return (calendar.Works.Items.Select(CalendarRow),
                    calendar.Works.NextCursor, calendar);
            case DiscoverySection.Rankings:
                var source = RankingSource;
                var ranking = await catalog.RankingsAsync(source, next, showNsfw, cancellation, RankingFilters);
                return (ranking.Items.Select(work => RankRow(work, source)), ranking.NextCursor, null);
            case DiscoverySection.News:
                var news = await catalog.NewsAsync(NewsLane, next, showNsfw, cancellation, NewsSource);
                return (news.Items.Select(NewsRow), news.NextCursor, null);
            default: throw new InvalidOperationException();
        }
    }

    private DiscoveryRow WorkRow(WorkMetadata work, string leading, string metadata) =>
        // The response time changes on every refresh. Covers in these feeds
        // use the URL-addressed artwork cache and only change with that URL.
        new(work.Id, CatalogLanguage.Title(work), covers.Register(work.CoverUrl, "discovery"), leading, metadata);

    private DiscoveryRow CalendarRow(WorkMetadata work)
    {
        var validDate = TryReleaseDate(work, out var date);
        var label = (validDate ? work.ReleaseDatePrecision : null) switch
        {
            "day" => UiText.Format($"{date.Month}月{date.Day}日"),
            "month" => UiText.Format($"{date.Month}月 · 待定"),
            "year" => UiText.Format($"{date.Year}年 · 待定"),
            _ => UiText.Get("日期未定")
        };
        return WorkRow(work, label, string.Join(" · ", work.Developers)) with
        {
            ReleaseDay = validDate && work.ReleaseDatePrecision == "day" ? date : null
        };
    }

    private static bool TryReleaseDate(WorkMetadata work, out DateOnly date) =>
        DateOnly.TryParseExact(work.ReleaseDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

    private DiscoveryRow RankRow(RankedWork work, DiscoveryRankingSource source)
    {
        var maximum = DiscoveryRankingScale.NativeMaximum(source);
        var score = work.Score.ToString("0.00", CultureInfo.CurrentCulture);
        var votes = work.Votes is { } count ? UiText.Format($"{count:N0} 条评分") : "";
        var year = TryReleaseDate(work.Work, out var date) ? date.Year.ToString(CultureInfo.CurrentCulture) : "";
        var metadata = string.Join(" · ", work.Work.Developers.Append(year).Append(votes).Where(text => text.Length > 0));
        return WorkRow(work.Work, $"{work.Rank}", metadata) with
        {
            Score = score,
            ScoreLabel = maximum is { } limit ? $"/ {limit}" : UiText.Get("综合分"),
            SiteRatings = work.SiteScores.Select(site => $"{RankingSiteLabel(site.Source)} {site.Score:0.##}").ToArray()
        };
    }

    private static string RankingSiteLabel(string source) => source switch
    {
        "vndb" => "VNDB",
        "bangumi" => "Bangumi",
        "erogamescape" => UiText.Get("批评空间"),
        "dlsite" => "DLsite",
        "howlongtobeat" => "HLTB",
        "nextmoe" => "NextMoe",
        _ => source
    };

    private DiscoveryRow NewsRow(DiscoveryNewsItem item)
    {
        var lane = UiText.Get(item.IsColumn ? "专栏" : "资讯");
        return new(item.Id, item.Title, covers.Register(item.BannerUrl, item.PublishedAt.ToString("O")), "",
            $"{lane} · {item.Source} · {item.PublishedAt.ToLocalTime():yyyy-MM-dd}", item.Summary, item.Attribution, item.ArticleUri);
    }
}
