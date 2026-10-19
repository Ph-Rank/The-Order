using System.Collections.Immutable;
using System.Globalization;
using TheOrder.Domain;

namespace TheOrder.Application;

public enum StatisticsPeriod { Last7Days, Last30Days, ThisYear, AllTime }
public sealed record StatisticsRequest(ImmutableArray<LibraryEntry> Entries, StatisticsPeriod Period, DateOnly Today, TimeZoneInfo Zone, string? GameId = null);
public sealed record StatisticsBucket(DateOnly Start, DateOnly End, string Label, long Seconds);
public sealed record StatisticsGame(string Id, string Name, long Seconds);
public sealed record StatisticsStatus(string Key, string Name, int Count);
public sealed record StatisticsWeekday(string Name, long Seconds);
public sealed record StatisticsDashboard(DateOnly Start, DateOnly End, int LibraryCount, long LifetimeSeconds, long PeriodSeconds,
    int ActiveDays, int PlayedGames, long UnknownSeconds, long FutureSeconds, long ManualSeconds, bool Estimated,
    string IntervalLabel, ImmutableArray<StatisticsBucket> Trend, ImmutableArray<StatisticsGame> Games,
    ImmutableArray<StatisticsStatus> Statuses, ImmutableArray<StatisticsWeekday> Weekdays, ImmutableArray<DailyPlaytime> Days)
{
    public int CompletedGames => Statuses.First(x => x.Key == "completed").Count;
}

// Read-only projection of the same ledger used by the per-game editor. Never invent dates
// for imported totals, count installations as games, or infer completion from playtime.
public static class StatisticsDashboardBuilder
{
    public static StatisticsDashboard Build(StatisticsRequest request, CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(request.Zone);
        if (!Enum.IsDefined(request.Period) || request.Today.Year is < 2 or > 9998) throw new ArgumentOutOfRangeException(nameof(request));
        cancellation.ThrowIfCancellationRequested();
        var entries = request.GameId is null ? request.Entries : [.. request.Entries.Where(x => x.Id == request.GameId)];
        var report = PlaytimeStatistics.Build(entries, request.Zone, cancellation);
        var today = request.Today;
        var available = report.Days.Where(x => x.Date <= today).ToImmutableArray();
        var start = request.Period switch
        {
            StatisticsPeriod.Last7Days => today.AddDays(-6),
            StatisticsPeriod.Last30Days => today.AddDays(-29),
            StatisticsPeriod.ThisYear => new DateOnly(today.Year, 1, 1),
            _ => available.IsEmpty ? today.AddDays(-29) : available.Min(x => x.Date)
        };
        var days = available.Where(x => x.Date >= start).ToImmutableArray();
        var byDay = days.GroupBy(x => x.Date).ToDictionary(x => x.Key, x => x.Sum(y => y.Seconds));
        var games = days.GroupBy(x => x.GameId, StringComparer.Ordinal)
            .Select(x => new StatisticsGame(x.Key, x.First().Name, x.Sum(y => y.Seconds)))
            .Where(x => x.Seconds != 0).OrderByDescending(x => x.Seconds).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Id, StringComparer.Ordinal).ToImmutableArray();
        var statuses = GameProgress.Ordered.Select(key => new StatisticsStatus(key, GameProgress.Label(key), entries.Count(e => e.Status == key))).ToImmutableArray();
        var weekdays = new[] { "周一", "周二", "周三", "周四", "周五", "周六", "周日" }
            .Select((name, index) => new StatisticsWeekday(name, byDay.Where(x => ((int)x.Key.DayOfWeek + 6) % 7 == index).Sum(x => x.Value))).ToImmutableArray();
        var windows = Windows(start, today, request.Period, out var interval);
        // O(rows + buckets), including old libraries with many dated records.
        var values = new long[windows.Length];
        foreach (var day in byDay)
        {
            cancellation.ThrowIfCancellationRequested();
            for (int i = 0; i < windows.Length; i++)
                if (day.Key >= windows[i].Start && day.Key <= windows[i].End) { values[i] = checked(values[i] + day.Value); break; }
        }
        var trend = windows.Select((x, i) => new StatisticsBucket(x.Start, x.End, x.Label, values[i])).ToImmutableArray();
        return new(start, today, entries.Length, report.TotalSeconds, days.Sum(x => x.Seconds), byDay.Count(x => x.Value > 0), games.Count(x => x.Seconds > 0),
            report.UnknownSeconds, report.Days.Where(x => x.Date > today).Sum(x => x.Seconds), days.Sum(x => x.ManualSeconds), days.Any(x => x.Estimated), interval,
            trend, games, statuses, weekdays, [.. days.OrderByDescending(x => x.Date).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.GameId, StringComparer.Ordinal)]);
    }

    private static ImmutableArray<(DateOnly Start, DateOnly End, string Label)> Windows(DateOnly start, DateOnly end, StatisticsPeriod period, out string interval)
    {
        var result = ImmutableArray.CreateBuilder<(DateOnly, DateOnly, string)>();
        var span = end.DayNumber - start.DayNumber + 1;
        bool months = period == StatisticsPeriod.ThisYear || span > 120 && span <= 730;
        bool years = span > 730 && !months;
        interval = years ? "按年" : months ? "按月" : span > 31 ? "按周" : "按日";
        var cursor = start;
        int yearStep = Math.Max(1, (int)Math.Ceiling((end.Year - start.Year + 1) / 20d));
        while (cursor <= end)
        {
            DateOnly next;
            string label;
            if (years)
            {
                next = new DateOnly(Math.Min(9999, cursor.Year + yearStep), 1, 1);
                var lastYear = Math.Min(end.Year, next.Year - 1);
                label = lastYear > cursor.Year ? $"{cursor.Year}–{lastYear}" : cursor.Year.ToString(CultureInfo.InvariantCulture);
            }
            else if (months)
            {
                next = new DateOnly(cursor.Year, cursor.Month, 1).AddMonths(1);
                label = start.Year == end.Year ? $"{cursor.Month}月" : cursor.ToString("yy/MM", CultureInfo.InvariantCulture);
            }
            else { next = cursor.AddDays(span > 31 ? 7 : 1); label = cursor.ToString("M/d", CultureInfo.InvariantCulture); }
            var stop = next.AddDays(-1) < end ? next.AddDays(-1) : end;
            result.Add((cursor, stop, label));
            if (stop == end) break;
            cursor = next;
        }
        return result.ToImmutable();
    }
}
