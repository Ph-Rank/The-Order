using System.Collections.Immutable;
using System.Globalization;
using System.Text.RegularExpressions;

namespace TheOrder.Domain;

// Data lineage lives in the existing library document, in the same atomic commit as the data.
// Imported IDs are source assertions, not a NextMoe metadata binding or a remote verification.
public sealed record ImportedIdentity(string Provider, string Id);
public sealed record ImportedField(string Target, string SourceField, string ValueDigest);
public sealed record ImportedProgressUpdate(string Status, string DocumentDigest, string RecordDigest, string UpdatedAt);
// A source high-water mark and its dated contributions. These receipts are separate
// from the remote cache and survive an undone/deleted record without restoring it.
public sealed record ImportedDayCoverage(string Date, long Seconds, string TargetId);
public sealed record ImportedSessionCoverage(string SourceKey, string StartedAt, string EndedAt, long Seconds,
    string? TargetId, string Disposition);
public sealed record ImportedHistoryDates(long AccountedSeconds, ImmutableArray<ImportedDayCoverage> Days,
    ImmutableArray<ImportedSessionCoverage> Sessions);
public sealed record ImportedMigrationReview(string SourceRecordDigest, string DocumentDigest, string TargetKey,
    bool IdentityAndVersionConfirmed, string HistoryChoice, long BeforeSeconds, long AfterSeconds, string ReviewedAt)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public long? AccountedSourceSeconds { get; init; }
}
public sealed record MigrationOrigin(string Manager, string RecordId, string RecordDigest, string DocumentDigest,
    string Format, string ImportedAt, string OriginalTitle, ImmutableArray<string> Aliases,
    ImmutableArray<ImportedIdentity> Identities, ImmutableArray<ImportedField> Fields,
    string TimePrecision, long ReportedSeconds, long? ReportedPlayCount, string? LocalLastPlayed,
    string? LocalAddedAt, string? Edition, string? SourceTimeZone = null)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public ImportedProgressUpdate? ProgressUpdate { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public ImportedMigrationReview? Review { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public ImportedHistoryDates? HistoryDates { get; init; }
}

public static class MigrationLineage
{
    public static ImmutableArray<MigrationOrigin> Merge(ImmutableArray<MigrationOrigin> first, ImmutableArray<MigrationOrigin> second)
    {
        var result = first.ToBuilder();
        foreach (var item in second)
        {
            var old = result.FirstOrDefault(x => x.Manager == item.Manager && x.RecordId == item.RecordId);
            if (old is null) result.Add(item);
            else if (old.RecordDigest != item.RecordDigest) throw new InvalidDataException("Conflicting migration origin; manual review required.");
            if (old?.HistoryDates is { } dates && item.HistoryDates is { } otherDates
                && (dates.AccountedSeconds != otherDates.AccountedSeconds || !dates.Days.SequenceEqual(otherDates.Days)
                    || !dates.Sessions.SequenceEqual(otherDates.Sessions)))
                throw new InvalidDataException("Conflicting imported history dates; manual review required.");
            if (old is not null && old.HistoryDates is null && item.HistoryDates is not null)
            {
                result[result.IndexOf(old)] = old with { HistoryDates = item.HistoryDates };
                old = result.First(x => x.Manager == item.Manager && x.RecordId == item.RecordId);
            }
            if (old is not null && item.ProgressUpdate is { } progress && (old.ProgressUpdate is null
                || GameProgress.Higher(old.ProgressUpdate.Status, progress.Status) == progress.Status
                && (old.ProgressUpdate.Status != progress.Status || string.CompareOrdinal(progress.UpdatedAt, old.ProgressUpdate.UpdatedAt) > 0)))
                result[result.IndexOf(old)] = old with { ProgressUpdate = progress };
            if (old is not null && item.Review is { } review && (old.Review is null || string.CompareOrdinal(review.ReviewedAt, old.Review.ReviewedAt) > 0))
            {
                var index = result.IndexOf(result.First(o => o.Manager == item.Manager && o.RecordId == item.RecordId));
                result[index] = result[index] with { Review = review };
            }
        }
        Validate(result.ToImmutable()); return result.ToImmutable();
    }
    public static bool Digest(string? value) => value is not null && Regex.IsMatch(value, "^[a-f0-9]{64}$", RegexOptions.CultureInvariant);
    public static bool Identity(ImportedIdentity value) => value is not null && value.Provider switch
    {
        "vndb" => Regex.IsMatch(value.Id, "^v[1-9][0-9]{0,9}$"),
        "bangumi" or "nextmoe" or "steam" => Regex.IsMatch(value.Id, "^[1-9][0-9]{0,9}$"),
        _ => false
    };
    public static void Validate(ImmutableArray<MigrationOrigin> origins)
    {
        if (origins.IsDefault || origins.Length > 32) throw new InvalidDataException("Invalid migration lineage.");
        var ids = new HashSet<(string, string)>();
        foreach (var o in origins)
        {
            if (o is null || !MigrationFormats.Valid(o.Manager, o.Format) || string.IsNullOrWhiteSpace(o.RecordId) || o.RecordId.Length > 200
                || !ids.Add((o.Manager, o.RecordId)) || !Digest(o.RecordDigest) || !Digest(o.DocumentDigest)
                || !DateTimeOffset.TryParse(o.ImportedAt, out _)
                || o.ReportedSeconds is < 0 or > 1_000_000_000_000 || o.ReportedPlayCount is < 0 or > 1_000_000_000
                || o.TimePrecision is not ("undated-total" or "daily-aggregate" or "sessions" or "sessions-and-daily-aggregate")
                || o.Aliases.IsDefault || o.Aliases.Length > 100 || o.Identities.IsDefault || o.Identities.Length > 16
                || o.Identities.Any(x => !Identity(x)) || o.Identities.Select(x => x.Provider).Distinct().Count() != o.Identities.Length
                || o.Fields.IsDefault || o.Fields.Length > 32 || o.Fields.Any(f => f is null || !Digest(f.ValueDigest)))
                throw new InvalidDataException("Invalid migration lineage.");
            Text(o.OriginalTitle, 2000); Text(o.LocalLastPlayed, 100); Text(o.LocalAddedAt, 100); Text(o.Edition, 500);
            Text(o.SourceTimeZone, 100);
            if (o.ProgressUpdate is { } progress && (!GameProgress.Valid(progress.Status) || !Digest(progress.DocumentDigest)
                || !Digest(progress.RecordDigest) || !DateTimeOffset.TryParse(progress.UpdatedAt, out _)))
                throw new InvalidDataException("Invalid imported progress update.");
            if (o.Review is { } review && (!Digest(review.SourceRecordDigest) || !Digest(review.DocumentDigest)
                || review.TargetKey is null || !Regex.IsMatch(review.TargetKey, "^[a-f0-9]{20}$", RegexOptions.CultureInvariant)
                || !review.IdentityAndVersionConfirmed || review.HistoryChoice is not ("keep-existing" or "use-source-total" or "add-source-total")
                || review.AccountedSourceSeconds is < 0 or > 1_000_000_000_000
                || review.BeforeSeconds is < 0 or > 1_000_000_000_000 || review.AfterSeconds is < 0 or > 1_000_000_000_000
                || !DateTimeOffset.TryParse(review.ReviewedAt, out _)))
                throw new InvalidDataException("Invalid migration review.");
            if (o.HistoryDates is { } history) ValidateHistoryDates(history);
            foreach (var a in o.Aliases) Text(a, 2000);
            foreach (var f in o.Fields) { Text(f.Target, 100); Text(f.SourceField, 200); }
        }
    }
    private static void ValidateHistoryDates(ImportedHistoryDates history)
    {
        if (history.AccountedSeconds is < 0 or > 1_000_000_000_000 || history.Days.IsDefault || history.Sessions.IsDefault
            || history.Days.Length > 10000 || history.Sessions.Length > 10000
            || history.Days.Any(d => d is null || !DateOnly.TryParseExact(d.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)
                || d.Seconds is <= 0 or > 86400 || !Guid.TryParseExact(d.TargetId, "D", out _))
            || history.Days.Select(d => d.Date).Distinct().Count() != history.Days.Length
            || history.Days.Select(d => d.TargetId).Distinct().Count() != history.Days.Length
            || history.Sessions.Any(s => s is null || !Digest(s.SourceKey)
                || !DateTimeOffset.TryParseExact(s.StartedAt, "yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var start)
                || !DateTimeOffset.TryParseExact(s.EndedAt, "yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var end)
                || end <= start || (end - start).TotalDays > 366 || s.Seconds <= 0 || s.Seconds > (end - start).TotalSeconds
                || s.Disposition is not ("imported" or "shared" or "overlap")
                || (s.Disposition == "overlap" ? s.TargetId is not null : !Guid.TryParseExact(s.TargetId, "D", out _)))
            || history.Sessions.Select(s => s.SourceKey).Distinct().Count() != history.Sessions.Length
            || history.Days.Sum(d => d.Seconds) + history.Sessions.Sum(s => s.Seconds) > history.AccountedSeconds)
            throw new InvalidDataException("Invalid imported history dates.");
    }
    private static void Text(string? value, int max)
    { if (value is not null && (value.Length > max || value.Any(char.IsControl))) throw new InvalidDataException("Invalid migration text."); }
}
