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
    [Fact] public void SessionTimelineKeepsContinuousCrossMidnightSessionOnStartNightCycle()
    {
        var first = Game("a", seconds: 7200) with
        {
            Sessions = [new("s", "2026-10-18T23:00:00Z", "2026-10-19T01:00:00Z", 7200)]
        };
        var second = Game("b", seconds: 3600) with
        {
            Sessions = [new("t", "2026-10-19T18:00:00Z", "2026-10-19T19:00:00Z", 3600)]
        };

        var data = Build([first, second], StatisticsPeriod.Last7Days);
        Assert.Equal(7, data.SessionTimeline.Buckets.Length);
        var overnight = Assert.Single(data.SessionTimeline.Buckets[^2].Sessions);
        Assert.Equal(("a", 82800, 90000), Coordinates(overnight));
        Assert.True(overnight.CrossesMidnight);
        Assert.Equal(new DateOnly(2026, 10, 18), overnight.CycleDate);
        Assert.Equal(new DateTime(2026, 10, 18, 23, 0, 0), overnight.ActualStartLocal);
        Assert.Equal(new DateTime(2026, 10, 19, 1, 0, 0), overnight.ActualEndLocal);

        var evening = Assert.Single(data.SessionTimeline.Buckets[^1].Sessions);
        Assert.Equal(("b", 64800, 68400), Coordinates(evening));
        Assert.False(evening.CrossesMidnight);

        var filtered = Build([first, second], StatisticsPeriod.Last7Days, id: "b");
        var filteredSpan = Assert.Single(filtered.SessionTimeline.Buckets[^1].Sessions);
        Assert.Equal(("b", 64800, 68400), Coordinates(filteredSpan));
        Assert.Equal(evening.ColorIndex, filteredSpan.ColorIndex);
        Assert.Empty(filtered.SessionTimeline.Buckets[^2].Sessions);
    }

    [Fact] public void SessionAxisAdaptsToActiveSessionsAndAlignsMidnightTick()
    {
        var evening = Game("a", seconds: 7200) with
        {
            Sessions = [new("overnight", "2026-10-18T23:00:00Z", "2026-10-19T01:00:00Z", 7200)]
        };
        var afternoon = Game("b") with
        {
            Sessions = [new("earlier", "2026-10-19T18:00:00Z", "2026-10-19T19:00:00Z", 3600)]
        };

        var data = Build([evening, afternoon], StatisticsPeriod.Last7Days);
        var combinedAxis = data.SessionTimeline.Axis!.Value;
        Assert.Equal(new StatisticsSessionAxis(61200, 93600, 7200), combinedAxis);
        Assert.True(combinedAxis.CrossesMidnight);
        Assert.Contains(StatisticsSessionAxis.SecondsPerDay, combinedAxis.GetTicks());

        var overnightOnly = Build([evening, afternoon], StatisticsPeriod.Last7Days, id: "a");
        var nightAxis = overnightOnly.SessionTimeline.Axis!.Value;
        Assert.Equal(new StatisticsSessionAxis(79200, 93600, 3600), nightAxis);
        Assert.Equal([79200, 82800, 86400, 90000, 93600], nightAxis.GetTicks().ToArray());
        var span = Assert.Single(overnightOnly.SessionTimeline.Buckets[^2].Sessions);
        Assert.Equal(("a", 82800, 90000), Coordinates(span));
        Assert.Empty(overnightOnly.SessionTimeline.Buckets[^1].Sessions);

        var daytime = Build([evening, afternoon], StatisticsPeriod.Last7Days, id: "b");
        var dayAxis = daytime.SessionTimeline.Axis!.Value;
        Assert.Equal(new StatisticsSessionAxis(57600, 75600, 3600), dayAxis);
        Assert.False(dayAxis.CrossesMidnight);
    }

    [Fact] public void SessionAxisPrefersDawnCutWhenBothMorningAndOvernightExist()
    {
        var morning = Game("m", seconds: 3600) with
        {
            Sessions = [new("am", "2026-10-19T10:00:00Z", "2026-10-19T11:00:00Z", 3600)]
        };
        var overnight = Game("n", seconds: 7200) with
        {
            Sessions = [new("pm", "2026-10-18T23:00:00Z", "2026-10-19T01:00:00Z", 7200)]
        };

        var data = Build([morning, overnight], StatisticsPeriod.Last7Days);
        var axis = data.SessionTimeline.Axis!.Value;
        Assert.True(axis.CrossesMidnight);
        Assert.True(axis.StartSecond <= 36000);
        Assert.True(axis.EndSecond <= StatisticsSessionAxis.SecondsPerDay + 9 * 3600);
        var morningSpan = Assert.Single(data.SessionTimeline.Buckets[^1].Sessions);
        Assert.Equal(new DateOnly(2026, 10, 19), morningSpan.CycleDate);
        Assert.Equal(("m", 36000, 39600), Coordinates(morningSpan));
    }

    [Fact] public void EarlyMorningOnlySessionsStayWithinSingleDayWithoutUnnecessaryWrap()
    {
        var early = Game("e", seconds: 4500) with
        {
            Sessions = [new("early", "2026-10-19T00:15:00Z", "2026-10-19T01:30:00Z", 4500)]
        };
        var data = Build([early], StatisticsPeriod.Last7Days);
        var axis = data.SessionTimeline.Axis!.Value;
        Assert.False(axis.CrossesMidnight);
        Assert.Equal(0, axis.StartSecond);
        Assert.Equal(18000, axis.EndSecond);
        var span = Assert.Single(data.SessionTimeline.Buckets[^1].Sessions);
        Assert.Equal(("e", 900, 5400), Coordinates(span));
        Assert.Equal(new DateOnly(2026, 10, 19), span.CycleDate);
    }

    [Fact] public void LeadingWindowBoundaryOvernightSessionPrependsCycleColumnWithoutSplitting()
    {
        var boundary = Game("a", seconds: 7200) with
        {
            Sessions = [new("b", "2026-10-12T23:00:00Z", "2026-10-13T01:00:00Z", 7200)]
        };
        var data = Build([boundary], StatisticsPeriod.Last7Days);
        Assert.Equal(8, data.SessionTimeline.Buckets.Length);
        Assert.Equal(new DateOnly(2026, 10, 12), data.SessionTimeline.Buckets[0].Start);
        var span = Assert.Single(data.SessionTimeline.Buckets[0].Sessions);
        Assert.Equal(("a", 82800, 90000), Coordinates(span));
        Assert.True(span.CrossesMidnight);
    }

    [Theory]
    [InlineData(0, 21600, 108000)]
    [InlineData(3600, 3600, 86400)]
    public void SessionAxisKeepsAllExtensiveDataWithoutPaddingItPastOneClockCycle(int from, int axisStart, int axisEnd)
    {
        var axis = StatisticsSessionAxis.FromCoverage([(from, 86400)], hasCrossMidnight: from == 0)!.Value;
        Assert.Equal(new StatisticsSessionAxis(axisStart, axisEnd, 14400), axis);
        if (axis.CrossesMidnight) Assert.Contains(StatisticsSessionAxis.SecondsPerDay, axis.GetTicks());
        Assert.All(axis.GetTicks(), tick =>
        {
            Assert.InRange(tick, axis.StartSecond, axis.EndSecond);
            Assert.Equal(0, tick % axis.TickSeconds);
        });
    }

    [Fact] public void SessionColorsRemainDeterministicAcrossPeriodsFiltersAndLibraryOrder()
    {
        var library = Enumerable.Range(1, 12)
            .Select(i => Game($"game-{i:00}", date: $"2026-10-{Math.Min(19, 7 + i):00}", seconds: 3600))
            .ToImmutableArray();

        var month = Build(library, StatisticsPeriod.Last30Days);
        var reversedMonth = Build([.. library.Reverse()], StatisticsPeriod.Last30Days);
        var colorMap = month.SessionTimeline.Buckets.SelectMany(b => b.Sessions)
            .ToDictionary(s => s.GameId, s => s.ColorIndex, StringComparer.Ordinal);
        Assert.Equal(12, colorMap.Count);
        Assert.Equal(12, colorMap.Values.Distinct().Count());
        foreach (var span in reversedMonth.SessionTimeline.Buckets.SelectMany(b => b.Sessions))
            Assert.Equal(colorMap[span.GameId], span.ColorIndex);

        var week = Build(library, StatisticsPeriod.Last7Days);
        foreach (var span in week.SessionTimeline.Buckets.SelectMany(b => b.Sessions))
            Assert.Equal(colorMap[span.GameId], span.ColorIndex);

        var filtered = Build(library, StatisticsPeriod.Last7Days, id: "game-12");
        var filteredSpan = Assert.Single(filtered.SessionTimeline.Buckets.SelectMany(b => b.Sessions));
        Assert.Equal(colorMap["game-12"], filteredSpan.ColorIndex);
    }

    [Fact] public void MonthlySessionBucketsPreserveIndividualRealSessionsWithoutMerging()
    {
        var first = Game("a", seconds: 10800) with
        {
            Sessions =
            [
                new("one", "2026-10-01T08:00:00Z", "2026-10-01T09:00:00Z", 3600),
                new("two", "2026-10-02T08:30:00Z", "2026-10-02T09:30:00Z", 3600),
                new("three", "2026-10-03T20:00:00Z", "2026-10-03T21:00:00Z", 3600)
            ]
        };
        var second = Game("b", date: "2026-10-03");
        var data = Build([first, second], StatisticsPeriod.ThisYear);
        var sessions = data.SessionTimeline.Buckets.Single(bucket => bucket.Start.Month == 10).Sessions;
        Assert.Collection(sessions,
            span => { Assert.Equal(new DateOnly(2026, 10, 1), span.CycleDate); Assert.Equal(("a", 28800, 32400), Coordinates(span)); },
            span => { Assert.Equal(new DateOnly(2026, 10, 2), span.CycleDate); Assert.Equal(("a", 30600, 34200), Coordinates(span)); },
            span => { Assert.Equal(new DateOnly(2026, 10, 3), span.CycleDate); Assert.Equal(("b", 36000, 43200), Coordinates(span)); },
            span => { Assert.Equal(new DateOnly(2026, 10, 3), span.CycleDate); Assert.Equal(("a", 72000, 75600), Coordinates(span)); });
    }

    [Theory]
    [InlineData(StatisticsPeriod.Last7Days)]
    [InlineData(StatisticsPeriod.Last30Days)]
    [InlineData(StatisticsPeriod.ThisYear)]
    public void NearOrTouchingShortSessionsStayInOneLane(StatisticsPeriod period)
    {
        var report = Build(
        [
            ChartSession("a", "2026-10-19T06:00:00.100Z", "2026-10-19T06:00:01.500Z"),
            ChartSession("b", "2026-10-19T06:00:01.500Z", "2026-10-19T06:00:02.800Z"),
            ChartSession("c", "2026-10-19T06:00:03.200Z", "2026-10-19T06:00:04.700Z")
        ], period);
        var marks = StatisticsSessionLayout.ArrangeDay(report.SessionTimeline.Buckets[^1].Sessions,
            report.SessionTimeline.Axis!.Value, 214);

        Assert.Equal(new[] { "a", "b", "c" }, marks.Select(mark => mark.Span.GameId));
        Assert.Equal(marks[0].Span.VisibleEnd, marks[1].Span.VisibleStart);
        Assert.All(marks, mark =>
        {
            Assert.Equal(1, mark.LaneCount);
            Assert.Equal(0, mark.Lane);
            Assert.True(mark.Height >= 1);
        });
        Assert.True(marks[0].Top + marks[0].Height < marks[1].Top);
        Assert.True(marks[1].Top + marks[1].Height < marks[2].Top);
    }

    [Fact]
    public void ActualSubsecondOverlapCreatesLanesAndTouchingSessionStartsANewGroup()
    {
        var report = Build(
        [
            ChartSession("a", "2026-10-19T06:00:00.100Z", "2026-10-19T06:00:01.400Z"),
            ChartSession("b", "2026-10-19T06:00:01.399Z", "2026-10-19T06:00:02.700Z"),
            ChartSession("c", "2026-10-19T06:00:02.700Z", "2026-10-19T06:00:04.000Z")
        ]);
        var marks = StatisticsSessionLayout.ArrangeDay(report.SessionTimeline.Buckets[^1].Sessions,
            report.SessionTimeline.Axis!.Value, 214);

        Assert.True(marks[0].Span.VisibleEnd > marks[1].Span.VisibleStart);
        Assert.Equal(marks[1].Span.VisibleEnd, marks[2].Span.VisibleStart);
        Assert.Equal(new[] { 2, 2, 1 }, marks.Select(mark => mark.LaneCount));
        Assert.Equal(new[] { 0, 1, 0 }, marks.Select(mark => mark.Lane));
    }

    [Fact]
    public void RepeatedFallBackHourDoesNotCreateFalseOverlap()
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        var report = StatisticsDashboardBuilder.Build(new(
        [
            ChartSession("before", "2026-11-01T05:15:00Z", "2026-11-01T05:16:00Z"),
            ChartSession("after", "2026-11-01T06:15:00Z", "2026-11-01T06:16:00Z")
        ], StatisticsPeriod.Last7Days, new(2026, 11, 1), zone));
        var marks = StatisticsSessionLayout.ArrangeDay(report.SessionTimeline.Buckets[^1].Sessions,
            report.SessionTimeline.Axis!.Value, 214);

        Assert.Equal(2, marks.Length);
        Assert.Equal(marks[0].Span.VisibleStart.DateTime, marks[1].Span.VisibleStart.DateTime);
        Assert.NotEqual(marks[0].Span.VisibleStart.Offset, marks[1].Span.VisibleStart.Offset);
        Assert.All(marks, mark => Assert.Equal(1, mark.LaneCount));
        Assert.True(marks[0].Top + marks[0].Height < marks[1].Top);
    }

    [Fact] public void SessionTimelineOmitsUndatedCorrectionsAndUnconfirmedExitTimes()
    {
        var game = Game() with
        {
            Sessions = [ProcessGone("gone", "2026-10-19T14:00:00Z", "2026-10-19T15:00:00Z", 1800)],
            ManualPlaytime = [new("added", "2026-10-19", 600, "", "2026-10-19T16:00:00Z")]
        };
        var data = Build([game]);
        Assert.All(data.SessionTimeline.Buckets, bucket => Assert.Empty(bucket.Sessions));
        Assert.Null(data.SessionTimeline.Axis);
        Assert.Equal(2400, data.PeriodSeconds);
        Assert.Equal(1800, data.UnknownSeconds);
    }

    [Theory]
    [InlineData(3, 8, "2026-03-08T06:30:00Z", "2026-03-08T07:30:00Z", 5400, 7200, 10800, 12600)]
    [InlineData(11, 1, "2026-11-01T05:55:00Z", "2026-11-01T06:05:00Z", 3600, 3900, 6900, 7200)]
    public void SessionTimelinePreservesWallClockGapsAtDaylightSavingTransitions(int month, int day, string start, string end,
        int first, int firstEnd, int second, int secondEnd)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        long seconds = (long)(DateTimeOffset.Parse(end) - DateTimeOffset.Parse(start)).TotalSeconds;
        var game = Game("a", seconds: seconds) with { Sessions = [new("s", start, end, seconds)] };
        var report = StatisticsDashboardBuilder.Build(new([game], StatisticsPeriod.Last7Days, new(2026, month, day), zone));
        Assert.Collection(report.SessionTimeline.Buckets[^1].Sessions,
            span => Assert.Equal(("a", first, firstEnd), Coordinates(span)),
            span => Assert.Equal(("a", second, secondEnd), Coordinates(span)));
        Assert.Equal(
            report.SessionTimeline.Buckets[^1].Sessions[0].SessionKey,
            report.SessionTimeline.Buckets[^1].Sessions[1].SessionKey);
    }

    private static LibraryEntry ChartSession(string id, string start, string end) =>
        Game(id, seconds: 1) with { Sessions = [new("s", start, end, 1)] };

    private static (string Id, int Start, int End) Coordinates(StatisticsSessionSpan span)
        => (span.GameId, span.AxisStartSecond, span.AxisEndSecond);

    [Theory] [InlineData(SessionLifecycle.Open)] [InlineData(SessionLifecycle.Incomplete)] [InlineData(SessionLifecycle.RecoveryPending)]
    public void NonCompletedSessionsDoNotInventDailyActivity(SessionLifecycle lifecycle)
    {
        var game = Game(); var now = DateTimeOffset.Parse("2026-10-19T10:00:00Z");
        game = game with { Sessions = [game.Sessions[0] with { Native = new(1, "a", "i", now, now, null, now.AddHours(1), 3600 * NativeSession.TicksPerSecond,
            lifecycle, lifecycle == SessionLifecycle.Open ? null : SessionReason.UserStoppedObservation, false, 1, Guid.NewGuid(), new(0, null), null) }] };
        var data = Build([game]); Assert.Equal(3600, data.UnknownSeconds); Assert.Empty(data.Days);
    }
    [Fact] public void ProcessGoneContributesConfirmedTimeToDaysAndRanking()
    {
        var game = Game();
        var sameDay = ProcessGone("same-day", "2026-10-17T14:00:00Z", "2026-10-17T15:30:00Z", 4500);
        var overnight = ProcessGone("overnight", "2026-10-18T15:30:00Z", "2026-10-18T16:30:00Z", 3000);
        game = game with { Sessions = [.. game.Sessions, sameDay, overnight], TotalPlaySeconds = 11100 };
        var zone = TimeZoneInfo.CreateCustomTimeZone("Synthetic+8", TimeSpan.FromHours(8), "test", "test");

        var data = Build([game], zone: zone);
        Assert.Equal(11100, data.LifetimeSeconds);
        Assert.Equal(11100, data.PeriodSeconds);
        Assert.Equal(11100, Assert.Single(data.Games).Seconds);
        Assert.Equal(0, data.UnknownSeconds);
        Assert.Equal(11100, data.Trend.Sum(x => x.Seconds));
        Assert.Collection(data.Days,
            day => { Assert.Equal(Today, day.Date); Assert.Equal(5100, day.Seconds); Assert.True(day.Estimated); },
            day => { Assert.Equal(Today.AddDays(-1), day.Date); Assert.Equal(1500, day.Seconds); Assert.True(day.Estimated); },
            day => { Assert.Equal(Today.AddDays(-2), day.Date); Assert.Equal(4500, day.Seconds); Assert.False(day.Estimated); });
    }

    [Theory]
    [InlineData(null, "2026-10-19T12:00:00Z")]
    [InlineData("2026-10-19T10:00:00Z", null)]
    [InlineData("2026-10-19T10:00:00Z", "2026-10-19T09:00:00Z")]
    [InlineData("2026-10-19T10:00:00Z", "2026-10-19T10:30:00Z")]
    public void ProcessGoneWithMissingOrImplausibleAccountingBoundsRemainsUndated(string? start, string? cutoff)
    {
        var game = Game() with { Sessions = [ProcessGone("partial", start, cutoff, 3600)] };
        var data = Build([game]);
        Assert.Equal(3600, data.LifetimeSeconds);
        Assert.Equal(3600, data.UnknownSeconds);
        Assert.Empty(data.Days);
        Assert.Empty(data.Games);
    }

    private static PlaySession ProcessGone(string id, string? start, string? cutoff, long seconds)
    {
        DateTimeOffset? startedAt = start is null ? null : DateTimeOffset.Parse(start);
        return new NativeSession(1, "a", "i", startedAt, startedAt ?? DateTimeOffset.Parse("2026-10-19T10:00:00Z"),
            null, cutoff is null ? null : DateTimeOffset.Parse(cutoff), seconds * NativeSession.TicksPerSecond,
            SessionLifecycle.Incomplete, SessionReason.ProcessGone, false, 1, Guid.NewGuid(), new(0, null), null).Project(id);
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
        Assert.Equal(0, scale.Step % scale.Divisor);
    }

    [Theory]
    [InlineData(1, 1, 1)]
    [InlineData(60, 60, 60)]
    [InlineData(28800, 10800, 32400)]
    [InlineData(36000, 18000, 36000)]
    public void ChartScaleUsesFewWholeUnitTicks(long seconds, double step, double maximum)
    {
        var scale = StatisticsChartScale.For([0, seconds]);
        Assert.Equal(0, scale.Minimum);
        Assert.Equal(step, scale.Step);
        Assert.Equal(maximum, scale.Maximum);
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
