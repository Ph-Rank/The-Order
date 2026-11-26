using System.Collections.Immutable;
using System.Globalization;
using TheOrder.Domain;

namespace TheOrder.Application;

public sealed record StatisticsSessionSpan(
    string SessionKey,
    string GameId,
    string GameName,
    int ColorIndex,
    DateOnly CycleDate,
    int AxisStartSecond,
    int AxisEndSecond,
    DateTime ActualStartLocal,
    DateTime ActualEndLocal,
    bool CrossesMidnight,
    DateTimeOffset VisibleStart,
    DateTimeOffset VisibleEnd);

public sealed record StatisticsSessionBucket(
    DateOnly Start,
    DateOnly End,
    string Label,
    ImmutableArray<StatisticsSessionSpan> Sessions);

public sealed record StatisticsSessionTimelineData(
    StatisticsSessionAxis? Axis,
    ImmutableArray<StatisticsSessionBucket> Buckets)
{
    public static StatisticsSessionTimelineData Empty { get; } = new(null, []);
}

public readonly record struct StatisticsSessionAxis(int StartSecond, int EndSecond, int TickSeconds)
{
    public const int SecondsPerDay = 24 * Hour;
    private const int Hour = 3600;

    public bool CrossesMidnight => EndSecond > SecondsPerDay;
    public int SpanSeconds => EndSecond - StartSecond;

    public ImmutableArray<int> GetTicks()
    {
        var ticks = ImmutableArray.CreateBuilder<int>();
        int first = (int)Math.Ceiling((double)StartSecond / TickSeconds) * TickSeconds;
        for (int second = first; second <= EndSecond; second += TickSeconds)
            ticks.Add(second);
        if (ticks.Count == 0)
        {
            ticks.Add(StartSecond);
            if (EndSecond > StartSecond) ticks.Add(EndSecond);
        }
        return ticks.ToImmutable();
    }

    public static StatisticsSessionAxis? FromCoverage(IEnumerable<(int StartSecond, int EndSecond)> coverage, bool hasCrossMidnight)
    {
        var ranges = coverage
            .Select(item => (StartSecond: Math.Clamp(item.StartSecond, 0, SecondsPerDay), EndSecond: Math.Clamp(item.EndSecond, 0, SecondsPerDay)))
            .Where(item => item.EndSecond > item.StartSecond)
            .OrderBy(item => item.StartSecond)
            .ThenBy(item => item.EndSecond)
            .ToArray();
        if (ranges.Length == 0) return null;

        // Cut the 24h clock at its longest quiet stretch so evening and after-midnight play share one continuous axis.
        // When both daytime and overnight play exist, prefer a dawn/sleep cut so morning hours stay on the same day.
        var gaps = new List<(int CoverStart, int Gap, int AfterMidnightEnd, bool AcrossMidnight)>();
        int first = ranges[0].StartSecond;
        int coveredThrough = ranges[0].EndSecond;
        foreach (var range in ranges.AsSpan(1))
        {
            int gap = range.StartSecond - coveredThrough;
            if (gap > 0) gaps.Add((range.StartSecond, gap, coveredThrough, false));
            coveredThrough = Math.Max(coveredThrough, range.EndSecond);
        }
        int gapAcrossMidnight = first + SecondsPerDay - coveredThrough;
        if (gapAcrossMidnight > 0) gaps.Add((first, gapAcrossMidnight, 0, true));

        int coverStart = first;
        int largestGap = 0;
        bool midnightGapWins = false;
        if (gaps.Count > 0)
        {
            var chosen = gaps.OrderByDescending(g => g.Gap)
                .ThenByDescending(g => g.AcrossMidnight && !hasCrossMidnight)
                .First();
            if (hasCrossMidnight && chosen.AcrossMidnight)
                chosen = gaps.Where(g => !g.AcrossMidnight).OrderByDescending(g => g.Gap).FirstOrDefault(chosen);
            if (!chosen.AcrossMidnight && chosen.AfterMidnightEnd > 9 * Hour)
            {
                var naturalDawn = gaps
                    .Where(g => !g.AcrossMidnight && g.AfterMidnightEnd <= 9 * Hour && g.Gap >= 2 * Hour)
                    .OrderByDescending(g => g.Gap)
                    .FirstOrDefault();
                if (naturalDawn.Gap > 0) chosen = naturalDawn;
            }
            coverStart = chosen.CoverStart;
            largestGap = chosen.Gap;
            midnightGapWins = chosen.AcrossMidnight && !hasCrossMidnight;
        }

        int span = SecondsPerDay - largestGap;
        int padding = Math.Max(Hour, 4 * Hour - span) / 2;
        int viewStart = FloorHour(coverStart - padding);
        int viewEnd = CeilHour(coverStart + span + padding);

        if (midnightGapWins)
        {
            // Keep strictly daytime/same-day sessions inside [00:00, 24:00] instead of needlessly wrapping past midnight.
            if (viewStart < 0)
            {
                viewEnd = Math.Min(SecondsPerDay, viewEnd - viewStart);
                viewStart = 0;
            }
            if (viewEnd > SecondsPerDay)
            {
                viewStart = Math.Max(0, viewStart - (viewEnd - SecondsPerDay));
                viewEnd = SecondsPerDay;
            }
        }

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
        int start = ((viewStart % SecondsPerDay) + SecondsPerDay) % SecondsPerDay;
        int step = width switch { <= 6 * Hour => Hour, <= 12 * Hour => 2 * Hour, <= 18 * Hour => 3 * Hour, _ => 4 * Hour };
        return new(start, start + width, step);
    }

    private static int FloorHour(int second) => (int)Math.Floor((double)second / Hour) * Hour;
    private static int CeilHour(int second) => (int)Math.Ceiling((double)second / Hour) * Hour;
}

internal sealed class StatisticsSessionTimeline
{
    public const int PaletteSize = 12;

    private readonly TimeZoneInfo zone;
    private readonly DateOnly? requestedStart;
    private readonly DateOnly today;
    private readonly DateTimeOffset todayEndUtc;
    private readonly Dictionary<string, int> colorIndices;
    private readonly List<RecordedSession> sessions = [];

    private readonly record struct RecordedSession(string SessionKey, string GameId, string GameName, DateTimeOffset StartUtc, DateTimeOffset EndUtc);

    internal StatisticsSessionTimeline(ImmutableArray<LibraryEntry> allEntries, TimeZoneInfo zone, DateOnly? requestedStart, DateOnly today)
    {
        this.zone = zone;
        this.requestedStart = requestedStart;
        this.today = today;
        todayEndUtc = PlaytimeStatistics.Midnight(today.AddDays(1), zone);
        colorIndices = BuildColorMap(allEntries);
    }

    internal static Dictionary<string, int> BuildColorMap(ImmutableArray<LibraryEntry> allEntries)
    {
        var ordered = allEntries
            .Select(entry =>
            {
                DateTimeOffset earliest = DateTimeOffset.MaxValue;
                foreach (var session in entry.RecordedSessions)
                {
                    if (PlaytimeStatistics.TryGetCompletedWindow(session, out var start, out _) && start < earliest)
                        earliest = start;
                }
                return (entry.Id, Earliest: earliest);
            })
            .OrderBy(item => item.Earliest)
            .ThenBy(item => item.Id, StringComparer.Ordinal)
            .ToArray();

        var map = new Dictionary<string, int>(ordered.Length, StringComparer.Ordinal);
        for (int i = 0; i < ordered.Length; i++)
            map[ordered[i].Id] = i % PaletteSize;
        return map;
    }

    internal void AddSession(LibraryEntry entry, DateTimeOffset startUtc, DateTimeOffset endUtc)
    {
        if (startUtc >= todayEndUtc) return;
        var effectiveEnd = endUtc > todayEndUtc ? todayEndUtc : endUtc;
        if (effectiveEnd <= startUtc) return;

        var firstDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(startUtc, zone).DateTime);
        var lastDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(effectiveEnd.AddTicks(-1), zone).DateTime);
        if (firstDate > today) return;
        if (requestedStart is { } minDate && lastDate < minDate) return;

        string key = $"{entry.Id}:{startUtc.UtcTicks}:{effectiveEnd.UtcTicks}:{sessions.Count}";
        sessions.Add(new(key, entry.Id, entry.Name, startUtc, effectiveEnd));
    }

    internal StatisticsSessionTimelineData Build(ImmutableArray<StatisticsBucket> trendBuckets, CancellationToken cancellation)
    {
        if (trendBuckets.IsEmpty) return StatisticsSessionTimelineData.Empty;
        if (sessions.Count == 0)
        {
            return new(null, [.. trendBuckets.Select(bucket => new StatisticsSessionBucket(bucket.Start, bucket.End, bucket.Label, []))]);
        }

        // Extract local wall-clock segments (splitting only at DST offset changes) and 24h coverage.
        var extracted = new List<(RecordedSession Session, List<WallSegment> WallSegments, DateTime ActualStart, DateTime ActualEnd, bool CrossesMidnight)>(sessions.Count);
        var coverage = new List<(int StartSecond, int EndSecond)>();
        bool anyCrossMidnight = false;

        foreach (var session in sessions)
        {
            cancellation.ThrowIfCancellationRequested();
            var wallSegments = SplitByOffsetTransitions(session.StartUtc, session.EndUtc);
            if (wallSegments.Count == 0) continue;

            var actualStart = wallSegments[0].Start;
            var actualEnd = wallSegments[^1].End;
            bool crossesMidnight = DateOnly.FromDateTime(actualStart) != DateOnly.FromDateTime(actualEnd.AddTicks(-1));
            if (crossesMidnight) anyCrossMidnight = true;

            foreach (var segment in wallSegments)
                CollectDailyCoverage(segment.Start, segment.End, coverage);

            extracted.Add((session, wallSegments, actualStart, actualEnd, crossesMidnight));
        }

        var axis = StatisticsSessionAxis.FromCoverage(coverage, anyCrossMidnight);
        if (axis is not { } resolvedAxis)
        {
            return new(null, [.. trendBuckets.Select(bucket => new StatisticsSessionBucket(bucket.Start, bucket.End, bucket.Label, []))]);
        }

        int cut = resolvedAxis.CrossesMidnight ? resolvedAxis.StartSecond : 0;
        bool isDaily = trendBuckets.All(bucket => bucket.Start == bucket.End);
        var windowStart = trendBuckets[0].Start;
        var windowEnd = trendBuckets[^1].End;
        var previousCycleDate = windowStart.AddDays(-1);
        bool includeLeadingCycle = false;

        var allSpans = new List<StatisticsSessionSpan>(extracted.Count);
        foreach (var (session, wallSegments, actualStart, actualEnd, crossesMidnight) in extracted)
        {
            cancellation.ThrowIfCancellationRequested();
            int colorIndex = colorIndices.GetValueOrDefault(session.GameId, 0);

            foreach (var (segStart, segEnd, offset) in wallSegments)
            {
                var segStartDate = DateOnly.FromDateTime(segStart);
                var cycle = (int)Math.Floor(segStart.TimeOfDay.TotalSeconds) < cut ? segStartDate.AddDays(-1) : segStartDate;
                while (CycleOrigin(cycle, cut) < segEnd)
                {
                    var dayStart = cycle.ToDateTime(TimeOnly.MinValue);
                    var axisStart = dayStart.AddSeconds(resolvedAxis.StartSecond);
                    var axisEnd = dayStart.AddSeconds(resolvedAxis.EndSecond);
                    var pieceStart = segStart > axisStart ? segStart : axisStart;
                    var pieceEnd = segEnd < axisEnd ? segEnd : axisEnd;

                    if (pieceEnd > pieceStart && cycle >= previousCycleDate && cycle <= windowEnd)
                    {
                        if (isDaily && cycle == previousCycleDate) includeLeadingCycle = true;
                        allSpans.Add(new(
                            session.SessionKey,
                            session.GameId,
                            session.GameName,
                            colorIndex,
                            cycle,
                            (int)Math.Floor((pieceStart - dayStart).TotalSeconds),
                            (int)Math.Ceiling((pieceEnd - dayStart).TotalSeconds),
                            actualStart,
                            actualEnd,
                            crossesMidnight,
                            new DateTimeOffset(pieceStart, offset),
                            new DateTimeOffset(pieceEnd, offset)));
                    }
                    cycle = cycle.AddDays(1);
                }
            }
        }

        var bucketDefs = new List<(DateOnly Start, DateOnly End, string Label)>(trendBuckets.Length + (includeLeadingCycle ? 1 : 0));
        if (includeLeadingCycle)
            bucketDefs.Add((previousCycleDate, previousCycleDate, previousCycleDate.ToString("M/d", CultureInfo.InvariantCulture)));
        foreach (var bucket in trendBuckets)
            bucketDefs.Add((bucket.Start, bucket.End, bucket.Label));

        var grouped = new List<StatisticsSessionSpan>[bucketDefs.Count];
        for (int i = 0; i < grouped.Length; i++) grouped[i] = [];

        foreach (var span in allSpans)
        {
            cancellation.ThrowIfCancellationRequested();
            int target = -1;
            if (!isDaily && span.CycleDate < bucketDefs[0].Start)
            {
                target = 0;
            }
            else
            {
                for (int i = 0; i < bucketDefs.Count; i++)
                {
                    if (span.CycleDate >= bucketDefs[i].Start && span.CycleDate <= bucketDefs[i].End)
                    {
                        target = i;
                        break;
                    }
                }
            }
            if (target >= 0) grouped[target].Add(span);
        }

        var result = ImmutableArray.CreateBuilder<StatisticsSessionBucket>(bucketDefs.Count);
        for (int i = 0; i < bucketDefs.Count; i++)
        {
            var orderedSpans = grouped[i]
                .OrderBy(span => span.CycleDate)
                .ThenBy(span => span.AxisStartSecond)
                .ThenBy(span => span.AxisEndSecond)
                .ThenBy(span => span.GameId, StringComparer.Ordinal)
                .ThenBy(span => span.SessionKey, StringComparer.Ordinal)
                .ToImmutableArray();
            result.Add(new(bucketDefs[i].Start, bucketDefs[i].End, bucketDefs[i].Label, orderedSpans));
        }

        return new(resolvedAxis, result.MoveToImmutable());
    }

    private static DateTime CycleOrigin(DateOnly cycleDate, int cutSeconds)
        => cycleDate.ToDateTime(TimeOnly.MinValue).AddSeconds(cutSeconds);

    private readonly record struct WallSegment(DateTime Start, DateTime End, TimeSpan Offset);

    private List<WallSegment> SplitByOffsetTransitions(DateTimeOffset startUtc, DateTimeOffset endUtc)
    {
        var pieces = new List<WallSegment>();
        SplitRange(startUtc.UtcTicks, endUtc.UtcTicks);
        return pieces;

        void SplitRange(long fromTicks, long toTicks)
        {
            if (toTicks <= fromTicks) return;
            var startOffset = zone.GetUtcOffset(new DateTimeOffset(fromTicks, TimeSpan.Zero));
            var lastOffset = zone.GetUtcOffset(new DateTimeOffset(toTicks - 1, TimeSpan.Zero));
            if (startOffset == lastOffset)
            {
                var localStart = new DateTime(fromTicks + startOffset.Ticks, DateTimeKind.Unspecified);
                var localEnd = new DateTime(toTicks + startOffset.Ticks, DateTimeKind.Unspecified);
                if (localEnd > localStart) pieces.Add(new(localStart, localEnd, startOffset));
                return;
            }

            long low = fromTicks, high = toTicks - 1;
            while (high - low > 1)
            {
                long mid = low + (high - low) / 2;
                if (zone.GetUtcOffset(new DateTimeOffset(mid, TimeSpan.Zero)) == startOffset) low = mid;
                else high = mid;
            }
            SplitRange(fromTicks, high);
            SplitRange(high, toTicks);
        }
    }

    private static void CollectDailyCoverage(DateTime localStart, DateTime localEnd, List<(int StartSecond, int EndSecond)> coverage)
    {
        var day = DateOnly.FromDateTime(localStart);
        var lastDay = DateOnly.FromDateTime(localEnd.AddTicks(-1));
        for (; day <= lastDay; day = day.AddDays(1))
        {
            var dayStart = day.ToDateTime(TimeOnly.MinValue);
            var dayEnd = dayStart.AddDays(1);
            var segStart = localStart > dayStart ? localStart : dayStart;
            var segEnd = localEnd < dayEnd ? localEnd : dayEnd;
            int s = Math.Clamp((int)Math.Floor((segStart - dayStart).TotalSeconds), 0, StatisticsSessionAxis.SecondsPerDay);
            int e = Math.Clamp((int)Math.Ceiling((segEnd - dayStart).TotalSeconds), 0, StatisticsSessionAxis.SecondsPerDay);
            if (e > s) coverage.Add((s, e));
        }
    }
}
