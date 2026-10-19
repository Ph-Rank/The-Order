using System.Collections.Immutable;
using TheOrder.Application;
using TheOrder.Domain;
using TheOrder.Presentation;
using Xunit;
using static TheOrder.Tests.RecognitionBoundaryTests;

namespace TheOrder.Tests;

public sealed class StatisticsDashboardTests
{
    private static readonly DateOnly Today = new(2026, 10, 19);
    private static LibraryEntry Game(string id = "a", string date = "2026-10-19", long seconds = 3600) => Entry() with
    {
        Id = id, Name = "同名作品", TotalPlaySeconds = seconds, PlayCount = 1,
        Sessions = [new("s", date + "T10:00:00Z", date + "T12:00:00Z", seconds)]
    };
    private static StatisticsDashboard Build(ImmutableArray<LibraryEntry> games, StatisticsPeriod period = StatisticsPeriod.Last30Days, TimeZoneInfo? zone = null, string? id = null)
        => StatisticsDashboardBuilder.Build(new(games, period, Today, zone ?? TimeZoneInfo.Utc, id));
    private static LibrarySnapshot Snapshot(params LibraryEntry[] games) => new([.. games], LibraryRecovery.Main);
    [Fact] public void EmptyLibraryHasZeroValuesAndNoFabricatedActivity()
    {
        var data = Build([]); Assert.Equal(0, data.LibraryCount); Assert.Equal(0, data.LifetimeSeconds); Assert.Equal(0, data.PeriodSeconds);
        Assert.Empty(data.Games); Assert.Empty(data.Days); Assert.Equal(30, data.Trend.Length); Assert.All(data.Trend, x => Assert.Equal(0, x.Seconds));
        Assert.All(data.Statuses, x => Assert.Equal(0, x.Count)); Assert.Equal(7, data.Weekdays.Length);
    }
    [Fact] public void ImportedTotalsDoNotBecomeDatedFromLastPlayedAt()
    {
        var game = Game() with { Sessions = [], LastPlayedAt = "2026-10-19T12:00:00Z" };
        var data = Build([game]); Assert.Equal(3600, data.LifetimeSeconds); Assert.Equal(3600, data.UnknownSeconds); Assert.Equal(0, data.PeriodSeconds);
        Assert.Empty(data.Games); Assert.Equal(0, data.ActiveDays);
    }
    [Fact] public void SevenDaysIncludesBothBoundariesAndFillsQuietDays()
    {
        var data = Build([Game("a", "2026-10-12"), Game("b", "2026-10-13"), Game("c")], StatisticsPeriod.Last7Days);
        Assert.Equal(Today.AddDays(-6), data.Start); Assert.Equal(7, data.Trend.Length); Assert.Equal(7200, data.PeriodSeconds);
        Assert.Equal(2, data.ActiveDays); Assert.Equal(2, data.PlayedGames); Assert.Equal(data.PeriodSeconds, data.Trend.Sum(x => x.Seconds));
    }
    [Fact] public void WindowAndWeekdaysUseSelectedLocalTimezone()
    {
        var game = Game() with { Sessions = [new("s", "2026-10-18T22:30:00Z", "2026-10-18T23:30:00Z", 3600)] };
        var zone = TimeZoneInfo.CreateCustomTimeZone("Synthetic+9", TimeSpan.FromHours(9), "test", "test");
        var data = Build([game], zone: zone); Assert.Equal(Today, Assert.Single(data.Days).Date); Assert.Equal(3600, data.Weekdays[0].Seconds);
        Assert.All(data.Weekdays.Skip(1), x => Assert.Equal(0, x.Seconds));
    }
    [Fact] public void ManualCorrectionsAndUndoRemainSeparateFromRecordedFacts()
    {
        var game = Game() with { ManualPlaytime = [new("m", "2026-10-19", -600, "", "2026-10-19T12:00:00Z"), new("old", "2026-10-19", 1800, "", "2026-10-19T12:00:00Z", true)] };
        var data = Build([game]); Assert.Equal(3000, data.PeriodSeconds); Assert.Equal(3000, data.LifetimeSeconds); Assert.Equal(-600, data.ManualSeconds);
        Assert.Equal(3600, Assert.Single(data.Days).RecordedSeconds); Assert.Equal(3600, game.TotalPlaySeconds); Assert.True(game.ManualPlaytime[1].Undone);
    }
    [Fact] public void ManualOnlyTimeIsDatedAndUnknownTotalsStaySeparate()
    {
        var game = Game() with { Sessions = [], ManualPlaytime = [new("m", "2026-10-19", 600, "", "2026-10-19T12:00:00Z")] };
        var data = Build([game]); Assert.Equal(4200, data.LifetimeSeconds); Assert.Equal(600, data.PeriodSeconds); Assert.Equal(3600, data.UnknownSeconds);
    }
    [Fact] public void CrossMidnightAllocationsPreserveSecondsAndShowEstimate()
    {
        var game = Game(seconds: 3601) with { Sessions = [new("s", "2026-10-18T23:00:00Z", "2026-10-19T01:00:00Z", 3601)] };
        var data = Build([game]); Assert.True(data.Estimated); Assert.Equal(2, data.Days.Length); Assert.Equal(3601, data.Days.Sum(x => x.Seconds)); Assert.Equal(0, data.UnknownSeconds);
    }
    [Theory] [InlineData(SessionLifecycle.Open)] [InlineData(SessionLifecycle.Incomplete)] [InlineData(SessionLifecycle.RecoveryPending)]
    public void NonCompletedSessionsDoNotInventDailyActivity(SessionLifecycle lifecycle)
    {
        var game = Game(); var now = DateTimeOffset.Parse("2026-10-19T10:00:00Z");
        game = game with { Sessions = [game.Sessions[0] with { Native = new(1, "a", "i", now, now, null, now.AddHours(1), 3600 * NativeSession.TicksPerSecond,
            lifecycle, lifecycle == SessionLifecycle.Open ? null : SessionReason.UserStoppedObservation, false, 1, Guid.NewGuid(), new(0, null), null) }] };
        var data = Build([game]); Assert.Equal(3600, data.UnknownSeconds); Assert.Empty(data.Days);
    }
    [Fact] public void SameTitleDifferentIdsAndMultipleInstallationsAreNotMergedOrCountedTwice()
    {
        var first = Game() with { Installations = [new("i1", @"C:\synthetic\one", null, []), new("i2", @"C:\synthetic\two", null, [])] };
        var data = Build([first, Game("b")]); Assert.Equal(2, data.LibraryCount); Assert.Equal(2, data.Games.Length); Assert.Equal(2, data.PlayedGames); Assert.Equal(7200, data.PeriodSeconds);
    }
    [Fact] public void CompletionUsesUserStateRegardlessOfTimeRange()
    {
        var done = Game("a", "2025-10-19") with { Status = "completed" }; var longGame = Game("b") with { Status = "playing" };
        var data = Build([done, longGame], StatisticsPeriod.Last7Days); Assert.Equal(1, data.CompletedGames); Assert.Equal(1, data.PlayedGames);
        Assert.Equal(2, data.Statuses.Sum(x => x.Count));
    }
    [Fact] public void FilteringNeverUsesTitlesAsIdentity()
    {
        var data = Build([Game(), Game("b", seconds: 7200)], id: "b"); Assert.Equal(7200, data.LifetimeSeconds); Assert.Equal(1, data.LibraryCount); Assert.Equal("b", Assert.Single(data.Games).Id);
        Assert.Equal(0, Build([Game()], id: "missing").LibraryCount);
    }
    [Fact] public void FutureRecordsDoNotInflateCurrentActivity()
    {
        var data = Build([Game("a", "2026-10-20")], StatisticsPeriod.AllTime); Assert.Equal(0, data.PeriodSeconds); Assert.Equal(3600, data.FutureSeconds); Assert.Equal(3600, data.LifetimeSeconds); Assert.Empty(data.Days);
    }
    [Fact] public void CorrectionsAfterTimezoneChangeAreNotClampedOrHidden()
    {
        var game = Game() with { Sessions = [new("s", "2026-10-19T00:00:00Z", "2026-10-19T01:00:00Z", 3600)], ManualPlaytime = [new("m", "2026-10-19", -600, "", "2026-10-19T12:00:00Z")] };
        var zone = TimeZoneInfo.CreateCustomTimeZone("Synthetic-4", TimeSpan.FromHours(-4), "test", "test");
        var data = Build([game], zone: zone); Assert.Equal(3000, data.PeriodSeconds); Assert.Contains(data.Trend, x => x.Seconds == -600); Assert.Equal(1, data.ActiveDays);
    }
    [Fact] public void YearBucketsUseCalendarMonthsAndDoNotShowFutureMonths()
    {
        var data = Build([Game("a", "2026-01-01"), Game()], StatisticsPeriod.ThisYear);
        Assert.Equal(new DateOnly(2026, 1, 1), data.Start); Assert.Equal(10, data.Trend.Length); Assert.Equal(Today, data.Trend[^1].End); Assert.Equal(7200, data.Trend.Sum(x => x.Seconds));
    }
    [Fact] public void LongHistoryHasBoundedBucketsAndPreservesAllTime()
    {
        var data = Build([Game("a", "1900-01-01"), Game()], StatisticsPeriod.AllTime);
        Assert.InRange(data.Trend.Length, 1, 21); Assert.Equal(7200, data.Trend.Sum(x => x.Seconds)); Assert.Equal(new DateOnly(1900, 1, 1), data.Start);
    }
    [Fact] public void OrderingIsIndependentOfLibraryOrder()
    {
        var left = Build([Game("z"), Game("a")]); var right = Build([Game("a"), Game("z")]);
        Assert.Equal(left.Games.ToArray(), right.Games.ToArray()); Assert.Equal(left.Days.ToArray(), right.Days.ToArray()); Assert.Equal(left.Trend.ToArray(), right.Trend.ToArray());
    }
    [Fact] public void CancelledProjectionDoesNoWork()
    { Assert.Throws<OperationCanceledException>(() => StatisticsDashboardBuilder.Build(new([Game()], StatisticsPeriod.Last7Days, Today, TimeZoneInfo.Utc), new(true))); }
    [Theory] [InlineData(20, 1)] [InlineData(300, 60)] [InlineData(37200, 3600)]
    public void ChartScaleShowsShortSessionsAndIncludesAllValues(long seconds, double divisor)
    {
        var scale = StatisticsChartScale.For([0, seconds, -seconds / 2]);
        Assert.Equal(divisor, scale.Divisor); Assert.True(scale.Maximum >= seconds); Assert.True(scale.Minimum <= -seconds / 2d);
        Assert.True(double.IsFinite(scale.Step) && scale.Step > 0); Assert.InRange((scale.Maximum - scale.Minimum) / scale.Step, 1, 6);
    }
    [Fact] public void ZeroChartHasFiniteScaleWithoutInventingActivity()
    { var scale = StatisticsChartScale.For([0, 0]); Assert.Equal(0, scale.Minimum); Assert.True(scale.Maximum > 0); Assert.True(scale.Step > 0); }
    [Fact] public async Task ViewModelIgnoresLatePriorPeriodEvenWhenBuilderIgnoresCancellation()
    {
        var calls = new List<(StatisticsRequest Request, TaskCompletionSource<StatisticsDashboard> Completion)>();
        using var vm = new StatisticsViewModel((request, _) => { var completion = new TaskCompletionSource<StatisticsDashboard>(TaskCreationOptions.RunContinuationsAsynchronously); calls.Add((request, completion)); return completion.Task; }, () => Today, TimeZoneInfo.Utc);
        var first = vm.RefreshAsync(Snapshot(Game())); var second = vm.SelectPeriodAsync(StatisticsPeriod.Last7Days);
        calls[1].Completion.SetResult(StatisticsDashboardBuilder.Build(calls[1].Request)); await second;
        calls[0].Completion.SetResult(StatisticsDashboardBuilder.Build(calls[0].Request)); await first;
        Assert.Equal(Today.AddDays(-6), vm.Data!.Start); Assert.False(vm.Busy); Assert.Null(vm.Error);
    }
    [Fact] public async Task LeavingCancelsWorkAndLateCompletionCannotRestoreData()
    {
        var completion = new TaskCompletionSource<StatisticsDashboard>(TaskCreationOptions.RunContinuationsAsynchronously); StatisticsRequest? captured = null;
        using var vm = new StatisticsViewModel((request, _) => { captured = request; return completion.Task; }, () => Today, TimeZoneInfo.Utc);
        var pending = vm.RefreshAsync(Snapshot(Game())); vm.Leave(); completion.SetResult(StatisticsDashboardBuilder.Build(captured!)); await pending;
        Assert.Null(vm.Data); Assert.False(vm.Busy);
    }
    [Fact] public async Task FailureRetainsLastGoodProjectionAndCanRetry()
    {
        bool fail = false;
        using var vm = new StatisticsViewModel((request, _) => fail ? Task.FromException<StatisticsDashboard>(new IOException()) : Task.FromResult(StatisticsDashboardBuilder.Build(request)), () => Today, TimeZoneInfo.Utc);
        var snapshot = Snapshot(Game()); await vm.RefreshAsync(snapshot); var first = vm.Data; fail = true; await vm.RefreshAsync(snapshot, true);
        Assert.Same(first, vm.Data); Assert.NotNull(vm.Error); fail = false; await vm.RefreshAsync(snapshot); Assert.Null(vm.Error); Assert.NotNull(vm.Data);
    }
    [Fact] public async Task SameSnapshotIsReusedButDateRolloverAndMutationRefresh()
    {
        var date = Today; int calls = 0;
        using var vm = new StatisticsViewModel((request, _) => { calls++; return Task.FromResult(StatisticsDashboardBuilder.Build(request)); }, () => date, TimeZoneInfo.Utc);
        var snapshot = Snapshot(Game()); await vm.RefreshAsync(snapshot); await vm.RefreshAsync(snapshot); Assert.Equal(1, calls);
        date = date.AddDays(1); await vm.RefreshAsync(snapshot); Assert.Equal(2, calls);
        await vm.RefreshAsync(Snapshot(Game("b"))); Assert.Equal(3, calls);
    }
    [Fact] public async Task RemovingFilteredGameRestoresAllScopeWithoutStaleNumbers()
    {
        using var vm = new StatisticsViewModel(today: () => Today, zone: TimeZoneInfo.Utc);
        await vm.RefreshAsync(Snapshot(Game(), Game("b", seconds: 7200))); await vm.SelectGameAsync("a"); Assert.Equal(3600, vm.Data!.LifetimeSeconds);
        await vm.RefreshAsync(Snapshot(Game("b", seconds: 7200))); Assert.Null(vm.GameId); Assert.Equal(7200, vm.Data!.LifetimeSeconds); Assert.DoesNotContain(vm.GameOptions, x => x.Id == "a");
    }
    [Fact] public async Task WindowShutdownMayDisposeBeforePageUnloaded()
    {
        var vm = new StatisticsViewModel(today: () => Today, zone: TimeZoneInfo.Utc);
        await vm.RefreshAsync(Snapshot(Game())); vm.Dispose(); vm.Leave(); vm.Dispose();
        Assert.False(vm.Busy);
    }
}
