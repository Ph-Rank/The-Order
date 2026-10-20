using System.Collections.Immutable;
using System.Globalization;
using TheOrder.Domain;

namespace TheOrder.Application.Migrations;

internal sealed record MigrationHistoryAddition(long AdditionalSeconds, long DuplicateSeconds, long NewlyDatedSeconds,
    ImmutableArray<PlaySession> Sessions, ImmutableArray<ManualPlaytime> ManualPlaytime,
    ImportedHistoryDates Coverage, ImmutableArray<string> Warnings);

// Works only with the validated normalized model. No source files, game folders or
// clocks are consulted. Daily totals stay daily totals; only real intervals become sessions.
internal static class MigrationHistoryDates
{
    internal const string DailyNote = "导入的来源按日累计；非真实会话。";
    private const string PreviewTime = "2000-01-01T00:00:00.000Z";
    internal static DateTimeOffset? LastPlayed(ImportGame source)
        => source.Sessions.Select(s => (DateTimeOffset?)s.End).Append(source.LastPlayed).Max();
    internal static string DayId(string manager, string record, DateOnly date)
        => ImportHash.GuidFor(manager + ":" + record + ":day:" + Day(date));
    internal static string SessionId(string manager, string record, ImportSession session)
        => ImportHash.GuidFor(manager + ":" + record + ":session:" + session.SourceId);
    internal static ImportedHistoryDates Initial(string manager, ImportGame source)
        => new(source.TotalSeconds, [.. source.Days.Select(d => new ImportedDayCoverage(Day(d.Date), d.Seconds, DayId(manager, source.SourceId, d.Date)))],
            [.. source.Sessions.Select(s => Receipt(s, SessionId(manager, source.SourceId, s), "imported"))]);

    internal static MigrationHistoryAddition Build(LibraryEntry target, string manager, ImportGame source, string now = PreviewTime)
    {
        var previous = target.MigrationOrigins.SingleOrDefault(o => o.Manager == manager && o.RecordId == source.SourceId);
        var accounted = MigrationReview.SourceSecondsAlreadyAccounted(target, manager, source);
        var oldCoverage = previous?.HistoryDates ?? Bootstrap(target, manager, source, previous, accounted);
        var days = oldCoverage.Days.ToDictionary(d => d.Date, StringComparer.Ordinal);
        var receipts = oldCoverage.Sessions.ToDictionary(s => s.SourceKey, StringComparer.Ordinal);
        var sessions = target.Sessions.ToDictionary(s => s.Id, StringComparer.Ordinal);
        var manual = target.ManualPlaytime.ToDictionary(d => d.Id, StringComparer.Ordinal);
        var knownIntervals = new SessionIntervals(target.Sessions);
        var targetStamps = target.Sessions.Where(s => s.Native is null || s.Native.Lifecycle == SessionLifecycle.Completed)
            .Select(s => (Session: s, Stamp: Stamp(s))).Where(s => s.Stamp is not null)
            .GroupBy(s => s.Stamp!.Value).ToDictionary(g => g.Key, g => g.First().Session);
        var receiptStamps = receipts.Values.Select(Stamp).ToHashSet();
        var warnings = new HashSet<string>(StringComparer.Ordinal);
        var ceiling = Math.Max(accounted, source.TotalSeconds);
        long claimBudget = Math.Max(0, ceiling - days.Values.Sum(d => d.Seconds) - receipts.Values.Sum(s => s.Seconds));
        long delta = Math.Max(0, source.TotalSeconds - accounted), duplicate = 0, newlyDated = 0;

        // Only a first contribution can prove that these exact seconds have not
        // already been credited. An old undated contribution is never subtracted
        // on the strength of a later, incomplete snapshot.
        if (accounted == 0)
            foreach (var s in source.Sessions)
                if (targetStamps.ContainsKey(Stamp(s))) duplicate += s.Seconds;
        duplicate = Math.Min(delta, duplicate); delta -= duplicate;
        if (duplicate > 0) warnings.Add("IDENTICAL_SESSIONS_DEDUPLICATED");
        long undatedBudget = Math.Max(0, target.TotalPlaySeconds + delta - target.Sessions.Sum(s => s.Seconds));

        foreach (var sourceSession in source.Sessions.OrderBy(s => s.Start))
        {
            var key = ImportHash.Text(sourceSession.SourceId);
            // Some exporters renumber their sessions. The exact interval and
            // measured duration identify a duplicate even when its source ID changed.
            if (receiptStamps.Contains(Stamp(sourceSession))) continue;
            receipts.TryGetValue(key, out var receipt);
            if (receipt is not null)
            {
                var increase = sourceSession.Seconds - receipt.Seconds;
                if (receipt.Disposition != "imported" || receipt.TargetId is null || !sessions.TryGetValue(receipt.TargetId, out var owned)
                    || !Unchanged(owned, receipt) || Start(receipt) != sourceSession.Start || End(receipt) > sourceSession.End || increase <= 0)
                { warnings.Add("SOURCE_SESSION_REVISED_OR_LOCAL_RECORD_EDITED"); continue; }
                if (increase > claimBudget || increase > undatedBudget
                    || knownIntervals.Overlaps(sourceSession, owned.Id))
                { warnings.Add("SOURCE_SESSION_CHANGE_NOT_ATTRIBUTABLE"); continue; }
                sessions[owned.Id] = owned with { EndedAt = NativeSession.Format(sourceSession.End), Seconds = sourceSession.Seconds };
                receiptStamps.Remove(Stamp(receipt)); receiptStamps.Add(Stamp(sourceSession));
                receipts[key] = Receipt(sourceSession, owned.Id, "imported");
                claimBudget -= increase; undatedBudget -= increase; newlyDated += increase;
                continue;
            }
            if (sourceSession.Seconds > claimBudget || receipts.Count >= 10000)
            { warnings.Add("SOURCE_DATES_EXCEED_UNALLOCATED_CONTRIBUTION"); continue; }
            if (targetStamps.TryGetValue(Stamp(sourceSession), out var shared))
            {
                receipts.Add(key, Receipt(sourceSession, shared.Id, "shared")); claimBudget -= sourceSession.Seconds;
                receiptStamps.Add(Stamp(sourceSession));
                warnings.Add("IDENTICAL_SESSION_DATE_ALREADY_PRESENT"); continue;
            }
            if (knownIntervals.Overlaps(sourceSession))
            {
                receipts.Add(key, Receipt(sourceSession, null, "overlap")); claimBudget -= sourceSession.Seconds;
                receiptStamps.Add(Stamp(sourceSession));
                warnings.Add("OVERLAPPING_SESSION_KEPT_UNDATED"); continue;
            }
            if (sourceSession.Seconds > undatedBudget || sessions.Count >= 10000)
            { warnings.Add("SOURCE_DATES_EXCEED_UNALLOCATED_CONTRIBUTION"); continue; }
            var id = SessionId(manager, source.SourceId, sourceSession);
            if (sessions.ContainsKey(id)) { warnings.Add("SOURCE_SESSION_ID_COLLISION"); continue; }
            sessions.Add(id, new(id, NativeSession.Format(sourceSession.Start), NativeSession.Format(sourceSession.End), sourceSession.Seconds));
            receipts.Add(key, Receipt(sourceSession, id, "imported")); claimBudget -= sourceSession.Seconds;
            receiptStamps.Add(Stamp(sourceSession));
            undatedBudget -= sourceSession.Seconds; newlyDated += sourceSession.Seconds;
        }
        foreach (var d in source.Days.OrderBy(d => d.Date))
        {
            var date = Day(d.Date); days.TryGetValue(date, out var receipt);
            var increase = d.Seconds - (receipt?.Seconds ?? 0);
            if (increase <= 0)
            { if (increase < 0) warnings.Add("SOURCE_DAILY_TOTAL_REVISED_EXISTING_DATE_PRESERVED"); continue; }
            if (increase > claimBudget || increase > undatedBudget || receipt is null && (days.Count >= 10000 || manual.Count >= 10000))
            { warnings.Add("SOURCE_DATES_EXCEED_UNALLOCATED_CONTRIBUTION"); continue; }
            var id = receipt?.TargetId ?? DayId(manager, source.SourceId, d.Date);
            if (receipt is not null)
            {
                if (!manual.TryGetValue(id, out var owned) || owned.Undone || owned.Date != date || owned.Seconds != receipt.Seconds || owned.Note != DailyNote)
                { warnings.Add("LOCAL_DAILY_RECORD_EDIT_PRESERVED"); continue; }
                manual[id] = owned with { Seconds = d.Seconds };
            }
            else
            {
                if (manual.ContainsKey(id)) { warnings.Add("SOURCE_DAILY_ID_COLLISION"); continue; }
                manual.Add(id, new(id, date, d.Seconds, DailyNote, now));
            }
            days[date] = new(date, d.Seconds, id); claimBudget -= increase; undatedBudget -= increase;
            newlyDated += increase;
        }
        return new(delta, duplicate, newlyDated, [.. sessions.Values], [.. manual.Values],
            new(ceiling, [.. days.Values.OrderBy(d => d.Date, StringComparer.Ordinal)], [.. receipts.Values.OrderBy(s => s.SourceKey, StringComparer.Ordinal)]),
            [.. warnings.Order(StringComparer.Ordinal)]);
    }

    internal static long AddedDailySeconds(LibraryEntry target, MigrationHistoryAddition addition)
        => addition.ManualPlaytime.Where(m => !m.Undone).Sum(m => m.Seconds) - target.ManualPlaytime.Where(m => !m.Undone).Sum(m => m.Seconds);
    internal static bool CanRepair(LibraryEntry target, string manager, ImportGame source)
    {
        var origin = target.MigrationOrigins.SingleOrDefault(o => o.Manager == manager && o.RecordId == source.SourceId);
        return origin is { HistoryDates: null, Review: { HistoryChoice: "add-source-total" } review }
            && GameProgress.Ordered.Any(status => review.SourceRecordDigest == ImportHash.Of(source with { Status = status }))
            && (source.Days.Length > 0 || source.Sessions.Length > 0)
            && Build(target, manager, source).NewlyDatedSeconds > 0;
    }
    private static ImportedHistoryDates Bootstrap(LibraryEntry target, string manager, ImportGame source, MigrationOrigin? previous, long accounted)
    {
        if (previous is null) return new(0, [], []);
        var days = target.ManualPlaytime.Where(d => d.Seconds > 0 && d.Note == DailyNote
            && DateOnly.TryParseExact(d.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            && IsLegacyDayId(d.Id, manager, source.SourceId, date))
            .Select(d => new ImportedDayCoverage(d.Date, d.Seconds, d.Id)).ToImmutableArray();
        var known = target.Sessions.ToDictionary(s => s.Id, StringComparer.Ordinal);
        var sessions = source.Sessions.Where(s => known.TryGetValue(SessionId(manager, source.SourceId, s), out var t) && Same(t, s))
            .Select(s => Receipt(s, SessionId(manager, source.SourceId, s), "imported")).ToImmutableArray();
        // A damaged/edited legacy receipt must not turn local data into source credit.
        return days.Sum(d => d.Seconds) + sessions.Sum(s => s.Seconds) <= accounted ? new(accounted, days, sessions) : new(accounted, [], []);
    }
    private static bool IsLegacyDayId(string id, string manager, string record, DateOnly day)
        => id == DayId(manager, record, day) || new[] { CultureInfo.CurrentCulture, CultureInfo.InvariantCulture,
            CultureInfo.GetCultureInfo("en-US"), CultureInfo.GetCultureInfo("zh-CN"), CultureInfo.GetCultureInfo("ja-JP") }.Any(c => id == ImportHash.GuidFor(manager + ":" + record + ":day:" + day.ToString(c)));
    private static string Day(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    private static ImportedSessionCoverage Receipt(ImportSession s, string? id, string disposition)
        => new(ImportHash.Text(s.SourceId), NativeSession.Format(s.Start), NativeSession.Format(s.End), s.Seconds, id, disposition);
    private static DateTimeOffset Start(ImportedSessionCoverage s) => DateTimeOffset.Parse(s.StartedAt, CultureInfo.InvariantCulture);
    private static DateTimeOffset End(ImportedSessionCoverage s) => DateTimeOffset.Parse(s.EndedAt, CultureInfo.InvariantCulture);
    private static bool Same(PlaySession a, ImportSession b) => DateTimeOffset.TryParse(a.StartedAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var start)
        && DateTimeOffset.TryParse(a.EndedAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var end)
        && start == b.Start && end == b.End && a.Seconds == b.Seconds && (a.Native is null || a.Native.Lifecycle == SessionLifecycle.Completed);
    private static bool Unchanged(PlaySession a, ImportedSessionCoverage b) => a.Native is null && a.Seconds == b.Seconds
        && DateTimeOffset.TryParse(a.StartedAt, out var start) && start == Start(b) && DateTimeOffset.TryParse(a.EndedAt, out var end) && end == End(b);
    private readonly record struct SessionStamp(long Start, long End, long Seconds);
    private static SessionStamp Stamp(ImportSession s) => new(s.Start.UtcTicks, s.End.UtcTicks, s.Seconds);
    private static SessionStamp Stamp(ImportedSessionCoverage s) => new(Start(s).UtcTicks, End(s).UtcTicks, s.Seconds);
    private static SessionStamp? Stamp(PlaySession s) => DateTimeOffset.TryParse(s.StartedAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var start)
        && DateTimeOffset.TryParse(s.EndedAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var end) ? new(start.UtcTicks, end.UtcTicks, s.Seconds) : null;
    private sealed class SessionIntervals
    {
        private readonly (string Id, SessionStamp Stamp)[] sorted;
        private readonly long[] maximumEnd;
        internal SessionIntervals(ImmutableArray<PlaySession> sessions)
        {
            sorted = sessions.Select(s => (s.Id, Stamp: MigrationHistoryDates.Stamp(s))).Where(s => s.Stamp is not null)
                .Select(s => (s.Id, Stamp: s.Stamp!.Value)).OrderBy(s => s.Stamp.Start).ToArray();
            maximumEnd = new long[sorted.Length]; long end = long.MinValue;
            for (var n = 0; n < sorted.Length; n++) maximumEnd[n] = end = Math.Max(end, sorted[n].Stamp.End);
        }
        internal bool Overlaps(ImportSession source, string? except = null)
        {
            var stamp = MigrationHistoryDates.Stamp(source); int low = 0, high = sorted.Length;
            while (low < high) { var mid = low + (high - low) / 2; if (sorted[mid].Stamp.Start < stamp.End) low = mid + 1; else high = mid; }
            if (low == 0 || maximumEnd[low - 1] <= stamp.Start) return false;
            if (except is null) return true;
            // Extensions may ignore their own original interval; timestamps are
            // parsed once, and the prefix bound avoids unrelated earlier history.
            for (var n = low - 1; n >= 0 && maximumEnd[n] > stamp.Start; n--)
                if (sorted[n].Id != except && sorted[n].Stamp.End > stamp.Start) return true;
            return false;
        }
    }
}
