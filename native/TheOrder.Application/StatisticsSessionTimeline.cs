using System.Collections.Immutable;
using TheOrder.Domain;

namespace TheOrder.Application;

public readonly record struct StatisticsSessionRange(string GameId, string GameName, int StartSecond, int EndSecond);
public sealed record StatisticsSessionBucket(DateOnly Start, DateOnly End, string Label, ImmutableArray<StatisticsSessionRange> Ranges);

public readonly record struct StatisticsSessionAxis(int StartSecond, int EndSecond, int TickSeconds)
{
    public const int SecondsPerDay = 24 * Hour;
    private const int Hour = 3600;

    public static StatisticsSessionAxis? For(ImmutableArray<StatisticsSessionBucket> buckets)
    {
        var ranges = buckets.SelectMany(bucket => bucket.Ranges).OrderBy(range => range.StartSecond).ToArray();
        if (ranges.Length == 0) return null;

        // Cut the clock at its longest quiet stretch so evening and morning can share one axis.
        int first = ranges[0].StartSecond;
        int coveredThrough = ranges[0].EndSecond;
        int coverStart = first;
        int largestGap = 0;
        foreach (var range in ranges.AsSpan(1))
        {
            int gap = range.StartSecond - coveredThrough;
            if (gap > largestGap) (coverStart, largestGap) = (range.StartSecond, gap);
            coveredThrough = Math.Max(coveredThrough, range.EndSecond);
        }
        int gapAcrossMidnight = first + SecondsPerDay - coveredThrough;
        if (gapAcrossMidnight >= largestGap) (coverStart, largestGap) = (first, gapAcrossMidnight);

        int span = SecondsPerDay - largestGap;
        int padding = Math.Max(Hour, 4 * Hour - span) / 2;
        int viewStart = FloorHour(coverStart - padding);
        int viewEnd = CeilHour(coverStart + span + padding);
        if (viewEnd - viewStart >= SecondsPerDay)
        {
            viewStart = FloorHour(coverStart);
            viewEnd = CeilHour(coverStart + span);
            if (viewEnd - viewStart >= SecondsPerDay)
            {
                viewStart = largestGap == 0 ? 6 * Hour : FloorHour(coverStart - largestGap / 2 + Hour / 2);
                viewEnd = viewStart + SecondsPerDay;
            }
        }

        int width = viewEnd - viewStart;
        int start = (viewStart + SecondsPerDay) % SecondsPerDay;
        int step = width switch { <= 6 * Hour => Hour, <= 12 * Hour => 2 * Hour, <= 18 * Hour => 3 * Hour, _ => 4 * Hour };
        return new(start, start + width, step);
    }

    public IEnumerable<StatisticsSessionRange> Project(StatisticsSessionRange range)
    {
        if (range.EndSecond > StartSecond)
            yield return range with { StartSecond = Math.Max(range.StartSecond, StartSecond) };
        if (range.StartSecond < StartSecond)
            yield return range with
            {
                StartSecond = range.StartSecond + SecondsPerDay,
                EndSecond = Math.Min(range.EndSecond, StartSecond) + SecondsPerDay
            };
    }

    private static int FloorHour(int second) => (int)Math.Floor((double)second / Hour) * Hour;
    private static int CeilHour(int second) => (int)Math.Ceiling((double)second / Hour) * Hour;
}

internal sealed class StatisticsSessionTimeline(TimeZoneInfo zone)
{
    private readonly Dictionary<DateOnly, List<StatisticsSessionRange>> days = [];

    internal void Add(LibraryEntry entry, DateOnly day, DateTimeOffset start, DateTimeOffset end)
    {
        long first = start.UtcTicks;
        long after = end.UtcTicks;
        var startOffset = zone.GetUtcOffset(start);
        var lastOffset = zone.GetUtcOffset(end.AddTicks(-1));

        if (startOffset == lastOffset)
        {
            AddSlice(first, after, startOffset);
            return;
        }

        // Split at a clock change so gaps and repeated hours stay at their recorded wall times.
        long low = first, high = after - 1;
        while (high - low > 1)
        {
            long mid = low + (high - low) / 2;
            if (zone.GetUtcOffset(new DateTimeOffset(mid, TimeSpan.Zero)) == startOffset) low = mid;
            else high = mid;
        }
        AddSlice(first, high, startOffset);
        AddSlice(high, after, lastOffset);

        void AddSlice(long from, long to, TimeSpan offset)
        {
            const int daySeconds = 24 * 60 * 60;
            long midnight = day.ToDateTime(TimeOnly.MinValue).Ticks;
            long localStart = from + offset.Ticks - midnight;
            long localEnd = to + offset.Ticks - midnight;
            int startSecond = (int)Math.Clamp(localStart / TimeSpan.TicksPerSecond, 0, daySeconds);
            int endSecond = (int)Math.Clamp((localEnd + TimeSpan.TicksPerSecond - 1) / TimeSpan.TicksPerSecond, 0, daySeconds);
            if (endSecond <= startSecond) return;
            if (!days.TryGetValue(day, out var ranges)) days.Add(day, ranges = []);
            ranges.Add(new(entry.Id, entry.Name, startSecond, endSecond));
        }
    }

    internal ImmutableArray<StatisticsSessionBucket> Build(ImmutableArray<StatisticsBucket> buckets, CancellationToken cancellation)
    {
        var dates = days.OrderBy(pair => pair.Key).ToArray();
        var result = ImmutableArray.CreateBuilder<StatisticsSessionBucket>(buckets.Length);
        int nextDate = 0;
        foreach (var bucket in buckets)
        {
            cancellation.ThrowIfCancellationRequested();
            while (nextDate < dates.Length && dates[nextDate].Key < bucket.Start) nextDate++;
            var ranges = new List<StatisticsSessionRange>();
            while (nextDate < dates.Length && dates[nextDate].Key <= bucket.End)
                ranges.AddRange(dates[nextDate++].Value);

            // Longer buckets show the times of day at which each game was played.
            var combined = ImmutableArray.CreateBuilder<StatisticsSessionRange>();
            foreach (var group in ranges.GroupBy(range => range.GameId, StringComparer.Ordinal))
            {
                cancellation.ThrowIfCancellationRequested();
                using var ordered = group.OrderBy(range => range.StartSecond).GetEnumerator();
                if (!ordered.MoveNext()) continue;
                var current = ordered.Current;
                while (ordered.MoveNext())
                {
                    var range = ordered.Current;
                    if (range.StartSecond <= current.EndSecond)
                        current = current with { EndSecond = Math.Max(current.EndSecond, range.EndSecond) };
                    else
                    {
                        combined.Add(current);
                        current = range;
                    }
                }
                combined.Add(current);
            }

            result.Add(new(bucket.Start, bucket.End, bucket.Label, [.. combined.OrderBy(range => range.StartSecond)
                .ThenBy(range => range.GameId, StringComparer.Ordinal)]));
        }
        return result.MoveToImmutable();
    }
}
