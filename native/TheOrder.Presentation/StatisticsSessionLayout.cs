using TheOrder.Application;

namespace TheOrder.Presentation;

public readonly record struct StatisticsSessionMark(
    StatisticsSessionSpan Span, double Top, double Height, int Lane, int LaneCount);

public static class StatisticsSessionLayout
{
    private const double PreferredMarkHeight = 4;
    private const double CompactRowSpacing = 1.5;

    private sealed record Placement(StatisticsSessionSpan Span, int Group, int Lane, double RawTop, double RawBottom)
    {
        public double Top { get; set; }
    }

    public static StatisticsSessionMark[] ArrangeDay(
        IEnumerable<StatisticsSessionSpan> sessions, StatisticsSessionAxis axis, double plotHeight)
    {
        var marks = new List<Placement>();
        var groupLaneCounts = new List<int>();
        var laneEnds = new List<DateTimeOffset>();
        DateTimeOffset groupEnd = DateTimeOffset.MinValue;

        // The visible intervals retain their offsets. Only concurrent time uses horizontal lanes.
        foreach (var span in sessions.OrderBy(span => span.VisibleStart).ThenBy(span => span.VisibleEnd))
        {
            var start = span.VisibleStart;
            var end = span.VisibleEnd;
            if (laneEnds.Count > 0 && start >= groupEnd)
            {
                groupLaneCounts.Add(laneEnds.Count);
                laneEnds.Clear();
            }
            int lane = laneEnds.FindIndex(occupiedUntil => occupiedUntil <= start);
            if (lane < 0)
            {
                lane = laneEnds.Count;
                laneEnds.Add(end);
            }
            else laneEnds[lane] = end;
            if (end > groupEnd) groupEnd = end;

            var dayStart = span.CycleDate.ToDateTime(TimeOnly.MinValue);
            marks.Add(new(span, groupLaneCounts.Count, lane,
                Project((start.DateTime - dayStart).TotalSeconds),
                Project((end.DateTime - dayStart).TotalSeconds)));
        }
        if (marks.Count == 0) return [];
        groupLaneCounts.Add(laneEnds.Count);

        // Give tightly spaced, distinct starts a small vertical separation within the plot.
        var ordered = marks.OrderBy(mark => mark.RawTop).ThenBy(mark => mark.Span.VisibleStart).ToArray();
        var rows = ordered.GroupBy(mark => (mark.RawTop, mark.Span.VisibleStart.UtcTicks)).ToArray();
        double spacing = Math.Min(CompactRowSpacing, plotHeight / rows.Length);
        double nextTop = 0;
        for (int i = 0; i < rows.Length; i++)
        {
            double latestTop = plotHeight - (rows.Length - i) * spacing;
            double top = Math.Min(Math.Max(rows[i].Key.RawTop, nextTop), latestTop);
            foreach (var mark in rows[i]) mark.Top = top;
            nextTop = top + spacing;
        }

        var result = new StatisticsSessionMark[ordered.Length];
        var nextLaneTop = new Dictionary<(int Group, int Lane), double>();
        double nextGroupTop = plotHeight;
        for (int i = ordered.Length - 1; i >= 0; i--)
        {
            var mark = ordered[i];
            if (i + 1 < ordered.Length && ordered[i + 1].Group != mark.Group)
                nextGroupTop = ordered[i + 1].Top;

            var lane = (mark.Group, mark.Lane);
            double limit = Math.Min(nextGroupTop, nextLaneTop.GetValueOrDefault(lane, plotHeight));
            if (limit < plotHeight) limit -= Math.Min(0.5, (limit - mark.Top) * 0.25);

            double bottom = Math.Min(limit, Math.Max(mark.RawBottom, mark.Top + PreferredMarkHeight));
            result[i] = new(mark.Span, mark.Top, bottom - mark.Top, mark.Lane, groupLaneCounts[mark.Group]);
            nextLaneTop[lane] = mark.Top;
        }
        return result;

        double Project(double seconds) => plotHeight * (seconds - axis.StartSecond) / axis.SpanSeconds;
    }
}
