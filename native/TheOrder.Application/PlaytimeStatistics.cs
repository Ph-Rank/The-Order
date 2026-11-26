using System.Collections.Immutable;
using System.Globalization;
using TheOrder.Domain;

namespace TheOrder.Application;

public sealed record DailyPlaytime(string GameId, string Name, DateOnly Date, long RecordedSeconds, long ManualSeconds, bool Estimated)
{
    public long Seconds => checked(RecordedSeconds + ManualSeconds);
}
public sealed record PlaytimeReport(ImmutableArray<DailyPlaytime> Days, long UnknownSeconds, long TotalSeconds);
public static class PlaytimeStatistics
{
    // Historical sessions do not store pause intervals. Cross-day allocation is labelled an estimate.
    // Only dated, terminal records enter charts, using credited seconds rather than wall time.
    public static PlaytimeReport Build(IEnumerable<LibraryEntry> entries, TimeZoneInfo zone, CancellationToken cancellation = default)
        => Build(entries, zone, cancellation, null);

    internal static PlaytimeReport Build(IEnumerable<LibraryEntry> entries, TimeZoneInfo zone, CancellationToken cancellation,
        Action<LibraryEntry, DateTimeOffset, DateTimeOffset>? addSession)
    {
        var rows = new Dictionary<(string, DateOnly), DailyPlaytime>(); long unknown = 0, total = 0;
        foreach (var e in entries)
        {
            cancellation.ThrowIfCancellationRequested();
            long attributed = 0; total = checked(total + e.EffectivePlaySeconds);
            foreach (var s in e.RecordedSessions)
            {
                cancellation.ThrowIfCancellationRequested();
                if (s.Seconds <= 0 || !TryGetAccountingWindow(s, out var start, out var end)
                    || end <= start || (end - start).TotalDays > 367 || s.Seconds > (end - start).TotalSeconds + 1) continue;
                var first = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(start, zone).DateTime);
                var last = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(end.AddTicks(-1), zone).DateTime);
                if (first.Year is < 2 or > 9998 || last.Year > 9998) continue;
                var duration = Math.Min(s.Seconds, Math.Max(0, e.TotalPlaySeconds - attributed));
                if (duration > 0 && (s.Native is null or { Lifecycle: SessionLifecycle.Completed }))
                    addSession?.Invoke(e, start, end);
                long left = duration; var cursor = start;
                for (var day = first; day <= last; day = day.AddDays(1))
                {
                    var next = Midnight(day.AddDays(1), zone); var stop = next < end ? next : end;
                    if (stop <= cursor) continue;
                    var seconds = day == last ? left : Math.Min(left, (long)((decimal)duration * (stop - cursor).Ticks / (end - start).Ticks));
                    left -= seconds; cursor = stop; attributed += seconds;
                    Add(day, seconds, 0, first != last || duration != s.Seconds);
                }
            }
            unknown = checked(unknown + Math.Max(0, e.TotalPlaySeconds - attributed));
            foreach (var m in e.ManualPlaytime.Where(m => !m.Undone)) Add(DateOnly.ParseExact(m.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture), 0, m.Seconds, false);
            void Add(DateOnly day, long automatic, long manual, bool estimated)
            {
                var key = (e.Id, day); var old = rows.GetValueOrDefault(key) ?? new(e.Id, e.Name, day, 0, 0, false);
                rows[key] = old with { RecordedSeconds = checked(old.RecordedSeconds + automatic), ManualSeconds = checked(old.ManualSeconds + manual), Estimated = old.Estimated || estimated };
            }
        }
        return new([.. rows.Values.OrderBy(x => x.Date).ThenBy(x => x.GameId, StringComparer.Ordinal)], unknown, total);
    }
    private static bool TryGetAccountingWindow(PlaySession session, out DateTimeOffset start, out DateTimeOffset end)
    {
        // ProcessGone seals confirmed samples. The accounting cutoff bounds those samples without asserting an exit time.
        if (session.Native is { Lifecycle: SessionLifecycle.Incomplete, Reason: SessionReason.ProcessGone,
            StartedAtUtc: { } observedStart, AccountingCutoffAtUtc: { } cutoff })
        {
            start = observedStart;
            end = cutoff;
            return true;
        }
        start = end = default;
        return (session.Native is null or { Lifecycle: SessionLifecycle.Completed })
            && Parse(session.StartedAt, out start) && Parse(session.EndedAt, out end);
    }

    internal static bool TryGetCompletedWindow(PlaySession session, out DateTimeOffset start, out DateTimeOffset end)
    {
        start = end = default;
        return session.Seconds > 0
            && (session.Native is null or { Lifecycle: SessionLifecycle.Completed })
            && Parse(session.StartedAt, out start) && Parse(session.EndedAt, out end)
            && end > start && (end - start).TotalDays <= 367 && session.Seconds <= (end - start).TotalSeconds + 1;
    }

    private static bool Parse(string text, out DateTimeOffset value) => DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out value);
    internal static DateTimeOffset Midnight(DateOnly day, TimeZoneInfo zone)
    {
        var local = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        // Some zones move clocks at midnight; pick the first real instant of that calendar date.
        for (var n = 0; zone.IsInvalidTime(local) && n < 180; n++) local = local.AddMinutes(1);
        var offset = zone.IsAmbiguousTime(local) ? zone.GetAmbiguousTimeOffsets(local).Max() : zone.GetUtcOffset(local);
        return new DateTimeOffset(local, offset).ToUniversalTime();
    }
}
