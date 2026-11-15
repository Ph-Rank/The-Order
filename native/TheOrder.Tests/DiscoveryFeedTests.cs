using System.Net.Http;
using System.Text.Json;
using TheOrder.Application;
using TheOrder.Infrastructure.Online;
using TheOrder.Presentation;
using Xunit;
using static TheOrder.Tests.OnlineTransportTests;

namespace TheOrder.Tests;

public sealed class DiscoveryFeedTests
{
    private static readonly DateOnly Month = new(2026, 11, 1);

    [Theory]
    [InlineData(ReleaseCalendarWindow.Month, "month=2026-11")]
    [InlineData(ReleaseCalendarWindow.Year, "precision=year")]
    [InlineData(ReleaseCalendarWindow.Undated, "status=unknown")]
    public async Task CalendarUsesPublicWindowsAndEditorialDisplayGate(ReleaseCalendarWindow window, string parameter)
    {
        using var http = new HttpClient(new Handler((request, _) =>
        {
            var uri = request.RequestUri!;
            Assert.Equal("/v2/catalog/calendar", uri.AbsolutePath);
            Assert.Contains(parameter, uri.Query);
            Assert.Equal(window == ReleaseCalendarWindow.Year, uri.Query.Contains("year=2026"));
            Assert.Contains(window == ReleaseCalendarWindow.Month ? "limit=100" : "limit=24", uri.Query);
            Assert.Contains("nsfw=true", uri.Query);
            Assert.Contains("content_limit=sfw", uri.Query);
            Assert.Contains("cursor=cur_after", uri.Query);
            return Task.FromResult(Calendar(Work("1"), Work("2", "nsfw")));
        }));
        var page = await Catalog(http).CalendarAsync(Month, window, "cur_after", false, default);
        Assert.Equal("1", Assert.Single(page.Works.Items).Id);
        Assert.Equal("r18", page.Works.Items[0].ContentRating);
        Assert.Equal("month", page.Works.Items[0].ReleaseDatePrecision);
        Assert.Equal(new DateOnly(2026, 11, 7), page.Today);
        Assert.True(page.HasPreviousMonth);
        Assert.False(page.HasNextMonth);
        Assert.Equal("cur_more", page.Works.NextCursor);
    }

    [Theory]
    [InlineData(DiscoveryRankingSource.Consensus, "consensus", "mean", "deviation", 85.82, 124L)]
    [InlineData(DiscoveryRankingSource.NextMoe, "nextmoe", "native", "native", 8.75, 24L)]
    [InlineData(DiscoveryRankingSource.Vndb, "vndb", "native", "native", 9.02, 7L)]
    [InlineData(DiscoveryRankingSource.Bangumi, "bangumi", "native", "native", 10, 7L)]
    [InlineData(DiscoveryRankingSource.ErogameScape, "erogamescape", "native", "native", 94, 13L)]
    [InlineData(DiscoveryRankingSource.DLsite, "dlsite", "native", "native", 4.8, 7L)]
    [InlineData(DiscoveryRankingSource.HowLongToBeat, "howlongtobeat", "native", "native", 100, 7L)]
    public async Task RankingsPreserveServerPositionsAndScoreScale(DiscoveryRankingSource source, string by, string metric,
        string unit, double score, long votes)
    {
        using var http = new HttpClient(new Handler((request, _) =>
        {
            var uri = request.RequestUri!;
            Assert.Equal("/v2/catalog/rankings", uri.AbsolutePath);
            Assert.Contains("by=" + by, uri.Query);
            Assert.Contains("metric=" + metric, uri.Query);
            Assert.Contains("scope=vn", uri.Query);
            return Task.FromResult(Json(JsonSerializer.Serialize(new
            {
                @object = "list", by, metric, unit,
                items = new[] { new { @object = "ranking_entry", rank = 12, value = score, work = Work("1"),
                    consensus = new { vote_count = 124 }, per_source = new[]
                    {
                        new { source = "nextmoe", score = 8.75m, vote_count = 24 },
                        new { source = "erogamescape", score = 94m, vote_count = 13 },
                        new { source = by, score = (decimal)score, vote_count = 7 }
                    } } }
            })));
        }));
        var entry = Assert.Single((await Catalog(http).RankingsAsync(source, null, false, default)).Items);
        Assert.Equal(12, entry.Rank);
        Assert.Equal((decimal)score, entry.Score);
        Assert.Equal(votes, entry.Votes);
        Assert.Equal(new RankingSiteScore("nextmoe", 8.75m), entry.SiteScores[0]);
        Assert.Equal(new RankingSiteScore("erogamescape", 94m), entry.SiteScores[1]);
    }

    [Fact]
    public async Task RankingFiltersAreSentToTheServiceOnEveryPage()
    {
        var calls = new List<string>();
        using var http = new HttpClient(new Handler((request, _) =>
        {
            calls.Add(Uri.UnescapeDataString(request.RequestUri!.Query));
            return Task.FromResult(Ranking(Work("1")));
        }));
        var catalog = Catalog(http);
        var filters = new DiscoveryRankingFilters { MinVotes = 100, MaxVotes = 2000, FromYear = 2020, ThroughYear = 2024,
            Platform = RankingPlatform.Switch, Circulation = RankingCirculation.Commercial, Duration = RankingDuration.Medium,
            Language = RankingLanguage.Chinese, MinSources = 3, IncludeOtherGames = true };
        await catalog.RankingsAsync(DiscoveryRankingSource.Consensus, null, false, default, filters);
        await catalog.RankingsAsync(DiscoveryRankingSource.Consensus, "cur_rank", false, default, filters);
        foreach (var query in calls)
            foreach (var parameter in new[] { "min_votes=100", "max_votes=2000", "year=2020-2024", "platform=swi,sw2",
                "circulation=commercial", "playtime_min=601", "playtime_max=1800", "olang=zh,zh-Hans,zh-Hant", "min_sources=3",
                "scope=all", "content_limit=sfw" })
                Assert.Contains(parameter, query.Split('?', '&'));
        Assert.DoesNotContain("cursor=", calls[0]);
        Assert.Contains("cursor=cur_rank", calls[1]);
    }

    [Fact]
    public async Task ChangingRankingFiltersRejectsThePreviousQueryAndCursor()
    {
        var late = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var http = new HttpClient(new Handler((request, _) =>
        {
            var query = request.RequestUri!.Query;
            if (query.Contains("min_votes=50"))
            {
                Assert.DoesNotContain("cursor=", query);
                Assert.Contains("released_after=2020-01-01", query);
                return Task.FromResult(Ranking(Work("2")));
            }
            return query.Contains("cursor=") ? late.Task : Task.FromResult(Ranking(Work("1"), "cur_old"));
        }));
        var model = Model(http, new());
        await model.SelectAsync(DiscoverySection.Rankings);
        var previousPage = model.LoadAsync(true);
        await model.SelectRankingFiltersAsync(new() { MinVotes = 50, FromYear = 2020 });
        late.SetResult(Ranking(Work("3")));
        await previousPage;
        Assert.Equal("2", Assert.Single(model.Items).Id);
        Assert.Null(model.PaginationCursor);
    }

    [Fact]
    public async Task NewsDoesNotReadOrTransmitCredentialsAndOnlyOpensSafeOriginals()
    {
        using var http = new HttpClient(new Handler((request, _) =>
        {
            Assert.Equal("/v2/news", request.RequestUri!.AbsolutePath);
            Assert.Contains("lane=column", request.RequestUri.Query);
            Assert.Contains("nsfw=false", request.RequestUri.Query);
            Assert.Null(request.Headers.Authorization);
            return Task.FromResult(News(Article("1", "ymgal", "https://www.ymgal.games/co/article/1"),
                Article("2", "community", "", true), Article("3", "ymgal", "file:///C:/source.txt")));
        }));
        var catalog = new NextMoeCatalog(new(http, new UnreadableCredential(), new("https://api.nextmoe.dev/v2/")));
        var items = (await catalog.NewsAsync(DiscoveryNewsLane.Column, null, false, default)).Items;
        Assert.Equal("合成资讯来源", items[0].Source);
        Assert.Equal("合成转载署名", items[0].Attribution);
        Assert.Equal("https://www.ymgal.games/co/article/1", items[0].ArticleUri!.AbsoluteUri);
        Assert.Equal("https://www.kungal.com/news/2", items[1].ArticleUri!.AbsoluteUri);
        Assert.Null(items[2].ArticleUri);
        Assert.All(items, item => Assert.Null(item.BannerUrl));
    }

    [Fact]
    public async Task InvalidCursorIsRejectedBeforeNetworkAccess()
    {
        using var http = new HttpClient(new Handler((_, _) => throw new InvalidOperationException("Unexpected request.")));
        var error = await Assert.ThrowsAsync<OnlineException>(() => Catalog(http).NewsAsync(DiscoveryNewsLane.All, "unexpected", false, default));
        Assert.Equal(OnlineFailure.InvalidRequest, error.Failure);
    }

    [Theory]
    [InlineData(DiscoveryNewsSource.Community, "community")]
    [InlineData(DiscoveryNewsSource.GalgameHihyou, "galgame_hihyou")]
    [InlineData(DiscoveryNewsSource.Ymgal, "ymgal")]
    public async Task NewsSourceFilterUsesCatalogQueryAndKeepsLane(DiscoveryNewsSource source, string key)
    {
        using var http = new HttpClient(new Handler((request, _) =>
        {
            Assert.Contains("source=" + key, request.RequestUri!.Query);
            Assert.Contains("lane=column", request.RequestUri.Query);
            return Task.FromResult(News(Article("1", key, "https://example.com/article/1")));
        }));
        var response = await Catalog(http).NewsAsync(DiscoveryNewsLane.Column, null, false, default, source);
        Assert.Single(response.Items);
    }

    [Fact]
    public async Task SwitchingTabsKeepsTheRequestInItsOwnFeed()
    {
        var held = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken calendarToken = default;
        var calendarCalls = 0;
        using var http = new HttpClient(new Handler((request, token) =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/calendar", StringComparison.Ordinal))
            {
                calendarToken = token;
                return ++calendarCalls == 1 ? held.Task : Task.FromResult(Calendar(Work("2")));
            }
            return Task.FromResult(News(Article("20", "ymgal", "https://www.ymgal.games/co/article/1")));
        }));
        var model = Model(http, new());
        var first = model.SelectAsync(DiscoverySection.Calendar);
        Assert.True(model.Busy);
        await model.SelectAsync(DiscoverySection.News);
        Assert.False(calendarToken.IsCancellationRequested);
        var news = Assert.Single(model.Items);
        await model.SelectAsync(DiscoverySection.Calendar);
        Assert.True(model.Busy);
        Assert.Equal(1, calendarCalls);
        await model.SelectAsync(DiscoverySection.News);

        held.SetResult(Calendar(Work("1")));
        await first;
        Assert.Equal(DiscoverySection.News, model.Section);
        Assert.Same(news, Assert.Single(model.Items));
        Assert.False(model.Busy);
        await model.SelectAsync(DiscoverySection.Calendar);
        Assert.Equal("1", Assert.Single(model.Items).Id);
        Assert.Equal(1, calendarCalls);
    }

    [Fact]
    public async Task ReturningKeepsLoadedPagesAndEmptyResultsUntilExplicitRefresh()
    {
        var calls = new List<string>();
        var rankingPages = 0;
        using var http = new HttpClient(new Handler((request, _) =>
        {
            var uri = request.RequestUri!;
            calls.Add(uri.PathAndQuery);
            if (uri.AbsolutePath.EndsWith("/rankings", StringComparison.Ordinal))
                return Task.FromResult(++rankingPages switch
                {
                    1 => Ranking(Work("1"), "cur_rank"),
                    2 => Ranking(Work("2"), "cur_next"),
                    _ => Ranking(Work("3"))
                });
            return Task.FromResult(uri.AbsolutePath.EndsWith("/calendar", StringComparison.Ordinal)
                ? Calendar(Work("4")) : News());
        }));
        var model = Model(http, new());
        await model.SelectAsync(DiscoverySection.Calendar);
        var calendar = model.Items;
        Assert.False(model.CanStep(1));
        await model.SelectAsync(DiscoverySection.Rankings);
        await model.LoadAsync(true);
        var rankings = model.Items;
        var firstRank = rankings[0];
        await model.SelectAsync(DiscoverySection.News);
        var emptyMessage = model.Message;
        Assert.Empty(model.Items);
        Assert.NotEmpty(emptyMessage);
        await model.SelectAsync(DiscoverySection.Search);
        await model.SelectAsync(DiscoverySection.Calendar);
        Assert.Same(calendar, model.Items);
        Assert.False(model.CanStep(1));
        await model.SelectAsync(DiscoverySection.News);
        Assert.Equal(emptyMessage, model.Message);
        await model.SelectAsync(DiscoverySection.Rankings);
        Assert.Same(rankings, model.Items);
        Assert.Same(firstRank, model.Items[0]);
        Assert.Equal(new[] { "1", "2" }, model.Items.Select(row => row.Id));
        Assert.Equal("cur_next", model.PaginationCursor);
        Assert.Equal(4, calls.Count);
        Assert.Contains("cursor=cur_rank", calls[2]);

        await model.ReloadAsync();
        Assert.Equal("3", Assert.Single(model.Items).Id);
        Assert.Equal(5, calls.Count);
        Assert.DoesNotContain("cursor=", calls[^1]);
        await model.SelectAsync(DiscoverySection.Calendar);
        Assert.Equal("4", Assert.Single(model.Items).Id);
        Assert.Equal(5, calls.Count);
    }

    [Fact]
    public async Task ReturningToAFailedFeedWaitsForExplicitRetry()
    {
        var calls = 0;
        using var http = new HttpClient(new Handler((_, _) => Task.FromResult(++calls == 1
            ? new HttpResponseMessage(System.Net.HttpStatusCode.Forbidden)
            : News(Article("20", "ymgal", "https://example.com/article/1")))));
        var model = Model(http, new());
        await model.SelectAsync(DiscoverySection.News);
        Assert.True(model.Failed);
        var message = model.Message;
        await model.SelectAsync(DiscoverySection.Search);
        await model.SelectAsync(DiscoverySection.News);
        Assert.True(model.Failed);
        Assert.Equal(message, model.Message);
        Assert.Equal(1, calls);
        await model.ReloadAsync();
        Assert.False(model.Failed);
        Assert.Equal("20", Assert.Single(model.Items).Id);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task MonthPrecisionDoesNotInventADayAndRepeatedCursorStopsPagination()
    {
        using var http = new HttpClient(new Handler((_, _) => Task.FromResult(Calendar(Work("1")))));
        var model = Model(http, new());
        await model.SelectAsync(DiscoverySection.Calendar);
        Assert.Equal("11月 · 待定", Assert.Single(model.Items).Leading);
        Assert.Null(model.Items[0].ReleaseDay);
        Assert.True(model.CanLoadMore);
        await model.LoadAsync(true);
        Assert.Single(model.Items);
        Assert.False(model.CanLoadMore);
        Assert.Contains("重复", model.Message);
    }

    [Fact]
    public async Task ExactReleaseDayIsAvailableForTheCalendarGrid()
    {
        using var http = new HttpClient(new Handler((_, _) => Task.FromResult(Calendar(Work("1", precision: "day")))));
        var model = Model(http, new());
        await model.SelectAsync(DiscoverySection.Calendar);
        var row = Assert.Single(model.Items);
        Assert.Equal(Month, row.ReleaseDay);
        Assert.Equal("11月1日", row.Leading);
    }

    [Fact]
    public async Task RefreshKeepsRowsUntilTheNewPageIsAcceptedAndRetainsIdenticalRows()
    {
        var response = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        using var http = new HttpClient(new Handler((_, _) => ++calls switch
        {
            1 => Task.FromResult(Calendar(Work("1"))),
            2 => response.Task,
            _ => Task.FromResult(Calendar(Work("2")))
        }));
        var model = Model(http, new());
        await model.SelectAsync(DiscoverySection.Calendar);
        var previous = Assert.Single(model.Items);
        Assert.Equal("https://images.example/cover.png|discovery", previous.ImageKey);
        var revision = model.ResultRevision;
        var refresh = model.ReloadAsync();
        Assert.True(model.Busy);
        Assert.False(model.LoadingMore);
        Assert.Same(previous, Assert.Single(model.Items));
        response.SetResult(Calendar(Work("1")));
        await refresh;
        Assert.Same(previous, Assert.Single(model.Items));
        Assert.Equal(revision, model.ResultRevision);

        await model.ReloadAsync();
        Assert.Equal("2", Assert.Single(model.Items).Id);
        Assert.Equal(revision + 1, model.ResultRevision);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ChangingBrowseOrAuthScopeInvalidatesHiddenAndPendingResults(bool changeAuth)
    {
        var policy = new CatalogBrowsePolicy();
        policy.Set(true);
        var auth = new OnlineAuthContext();
        var changed = false;
        var late = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var http = new HttpClient(new Handler((request, _) =>
        {
            if (request.RequestUri!.Query.Contains("cursor=")) return late.Task;
            return Task.FromResult(request.RequestUri.AbsolutePath.EndsWith("/rankings", StringComparison.Ordinal)
                ? Ranking(Work(changed ? "5" : "4", changed ? "sfw" : "nsfw"))
                : Calendar(Work(changed ? "1" : "2", changed ? "sfw" : "nsfw")));
        }));
        var model = new DiscoveryFeedViewModel(Catalog(http), policy, auth, new Registry());
        await model.SelectAsync(DiscoverySection.Calendar);
        Assert.Equal("2", Assert.Single(model.Items).Id);
        var calendar = model.Items;
        var more = model.LoadAsync(true);
        await model.SelectAsync(DiscoverySection.Rankings);
        Assert.Equal("4", Assert.Single(model.Items).Id);
        var rankings = model.Items;
        await model.SelectAsync(DiscoverySection.Search);

        changed = true;
        if (changeAuth) { auth.BeginMutation(); auth.EndMutation(); }
        else policy.Set(false);
        model.InvalidateIfStale();
        late.SetResult(Calendar(Work("3", "nsfw")));
        await more;
        Assert.Empty(calendar);
        Assert.Empty(rankings);
        Assert.False(model.CanLoadMore);
        Assert.False(model.Busy);
        await model.SelectAsync(DiscoverySection.Rankings);
        Assert.Equal("5", Assert.Single(model.Items).Id);
        await model.SelectAsync(DiscoverySection.Calendar);
        Assert.Equal("1", Assert.Single(model.Items).Id);
    }

    private static NextMoeCatalog Catalog(HttpClient http) => new(new(http, new Credential(), new("https://api.nextmoe.dev/v2/")));
    private static DiscoveryFeedViewModel Model(HttpClient http, CatalogBrowsePolicy policy) => new(Catalog(http), policy, new(), new Registry());
    private static HttpResponseMessage Calendar(params object[] items) => Json(JsonSerializer.Serialize(new
    {
        @object = "list", items, next_cursor = "cur_more",
        meta = new { today = "2026-11-07", min_month = "1982-00", has_prev = true, has_next = false }
    }));
    private static HttpResponseMessage Ranking(object work, string? cursor = null) => Json(JsonSerializer.Serialize(new
    {
        @object = "list", by = "consensus", metric = "mean", unit = "deviation", next_cursor = cursor,
        items = new[] { new { @object = "ranking_entry", rank = 1, value = 90, work } }
    }));
    private static HttpResponseMessage News(params object[] items) => Json(JsonSerializer.Serialize(new { @object = "list", items }));
    private static object Work(string id, string limit = "sfw", string precision = "month") => new
    {
        @object = "work", id, display_name = "合成作品 " + id, medium = "galgame", content_limit = limit,
        content_rating = "r18", release_date = "2026-11-01", release_date_precision = precision,
        cover = new { url = "https://images.example/cover.png", sexual = "safe" }
    };
    private static object Article(string id, string name, string sourceUrl, bool hasBody = false) => new
    {
        @object = "news_item", id, title = "合成资讯 " + id, summary = "合成摘要", lane = "column",
        source = new { name, display_name = "合成资讯来源", attribution = "合成转载署名" },
        source_url = sourceUrl, has_body = hasBody, published_at = "2026-11-06T19:45:23Z",
        banner = new { sexual = "explicit", url = "https://images.example/banner.webp" }
    };
    private sealed class Registry : IRemoteCoverRegistry { public string? Register(string? url, string revision) => url is null ? null : url + "|" + revision; }
    private sealed class UnreadableCredential : ICredentialProvider
    {
        public Task<string?> ReadAsync(CancellationToken cancellation) => throw new InvalidOperationException("News must not read stored credentials.");
    }
}
