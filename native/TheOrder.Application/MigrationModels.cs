using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TheOrder.Domain;

namespace TheOrder.Application.Migrations;

public enum ImportMatch { Confirmed, Candidate, Unmatched, Conflict }
public enum ImportAction { Create, FillMissing, Skip, AlreadyImported }
public enum ImportTimePrecision { UndatedTotal, DailyAggregate, Sessions, SessionsAndDailyAggregate }
public enum ImportHistoryChoice { KeepExisting, UseSourceTotal, AddSourceTotal }
public sealed record ImportReviewDecision(string RecordKey, string? TargetKey, bool IdentityAndVersionConfirmed = false,
    ImportHistoryChoice History = ImportHistoryChoice.KeepExisting);
public sealed record ImportReviewCandidate(string TargetKey, string Name, string CatalogName, string Status, long TotalSeconds,
    ImmutableArray<ImportedIdentity> Identities, ImmutableArray<string> Folders, bool ExactInstallation, bool CanUseSourceTotal,
    bool NotesConflict, bool RatingConflict, int InstallationCount)
{
    public bool CanAddSourceTotal { get; init; } = true;
    public long AdditionalSourceSeconds { get; init; }
    public long NewlyDatedSeconds { get; init; }
    public long DuplicateSessionSeconds { get; init; }
    public ImmutableArray<string> HistoryWarnings { get; init; } = [];
}
public sealed record ImportIssue(string RecordKey, string Field, string Code);
public sealed record ImportExclusion(string RecordKey, string Name, string Reason);
public sealed record ImportDay(DateOnly Date, long Seconds);
public sealed record ImportSession(string SourceId, DateTimeOffset Start, DateTimeOffset End, long Seconds);
public sealed record ImportInstallation(string SourceId, string Folder, string? Executable, string? Edition = null);
public sealed record ImportGame(string SourceId, string Name, string OriginalName, ImmutableArray<string> Aliases,
    ImmutableArray<ImportedIdentity> Identities, ImmutableArray<ImportInstallation> Installations,
    ImmutableArray<string> Categories, ImmutableArray<string> Tags, string Status, double? Rating, bool? Favorite,
    string Notes, long TotalSeconds, long? PlayCount, ImmutableArray<ImportDay> Days,
    ImmutableArray<ImportSession> Sessions, DateTimeOffset? LastPlayed, DateTimeOffset? AddedAt,
    string? LocalLastPlayed, string? LocalAddedAt, string? Edition, ImmutableArray<ImportIssue> Issues)
{
    public bool NeedsReview { get; init; }
    public ImportTimePrecision Precision => !Sessions.IsEmpty && !Days.IsEmpty ? ImportTimePrecision.SessionsAndDailyAggregate : !Sessions.IsEmpty ? ImportTimePrecision.Sessions : !Days.IsEmpty ? ImportTimePrecision.DailyAggregate : ImportTimePrecision.UndatedTotal;
}
public sealed record ImportDataset(string Manager, string Format, string DocumentDigest, int RawCount,
    ImmutableArray<ImportGame> Games, ImmutableArray<ImportIssue> Issues)
{
    public string? SourceTimeZone { get; init; }
    public ImmutableArray<ImportExclusion> Exclusions { get; init; } = [];
}
public sealed record ImportRow(string RecordKey, ImportMatch Match, ImportAction Action, string? TargetKey,
    ImmutableArray<string> Reasons, ImmutableArray<string> Fields)
{
    public long HistoryAdditionalSeconds { get; init; }
    public long HistoryNewlyDatedSeconds { get; init; }
    public long HistoryDeduplicatedSeconds { get; init; }
    public ImmutableArray<string> HistoryWarnings { get; init; } = [];
    public ImmutableArray<string> CandidateKeys { get; init; } = [];
    public string? StatusBefore { get; init; }
    public string? StatusAfter { get; init; }
    public ImmutableArray<ImportReviewCandidate> ReviewCandidates { get; init; } = [];
    public bool HumanReviewed { get; init; }
}
public sealed class MigrationPlan
{
    internal MigrationPlan(ImportDataset dataset, string targetDigest, string targetScope, ImmutableArray<ImportRow> rows,
        ImmutableArray<MigrationOperation> operations, ImmutableArray<ImportReviewDecision> reviews = default)
    {
        Dataset = dataset; TargetDigest = targetDigest; TargetScope = targetScope; Rows = rows; Operations = operations;
        Reviews = reviews.IsDefault ? [] : reviews;
        Id = ImportHash.Of(new { policy = 5, dataset.Manager, dataset.Format, dataset.DocumentDigest, dataset.SourceTimeZone, Reviews,
            normalized = ImportHash.Of(dataset.Games), targetDigest, targetScope,
            rows = rows.Select(r => new { r.RecordKey, r.Match, r.Action, r.TargetKey, r.Reasons, r.Fields, r.CandidateKeys,
                r.StatusBefore, r.StatusAfter, r.HumanReviewed, reviewTargets = r.ReviewCandidates.Select(c => c.TargetKey) }) });
    }
    public string Id { get; }
    public string TargetDigest { get; }
    public string TargetScope { get; }
    public ImmutableArray<ImportRow> Rows { get; }
    public ImportDataset Dataset { get; }
    public ImmutableArray<ImportReviewDecision> Reviews { get; }
    internal ImmutableArray<MigrationOperation> Operations { get; }
}
internal sealed record MigrationOperation(ImportGame Game, string? TargetId, string GameId, ImmutableArray<string> Fields,
    ImportReviewDecision? Review = null);
public sealed record MigrationCommit(int Created, int Filled, int Skipped, string PlanId);
public sealed record MigrationVerification(string Status, int BeforeCount, int AfterCount, long BeforeSeconds, long AfterSeconds, bool Reopened, int ExpectedCount);
public interface ILibraryMigrationAdapter
{
    string Manager { get; }
    Task<ImportDataset> ReadAsync(Stream source, string documentDigest, CancellationToken cancellation = default);
}
public static class ImportHash
{
    public static string Bytes(ReadOnlySpan<byte> bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    public static string Text(string text) => Bytes(Encoding.UTF8.GetBytes(text));
    public static string Of<T>(T value) => Bytes(JsonSerializer.SerializeToUtf8Bytes(value));
    public static string Key(string manager, string sourceId) => Text(manager + "\0" + sourceId)[..20];
    public static string GuidFor(string input) => new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(input)).AsSpan(0, 16)).ToString("D");
    public static string Snapshot(ImmutableArray<LibraryEntry> entries, ILibraryCodec codec) => Bytes(codec.Encode(entries, "2000-01-01T00:00:00.000Z"));
}
public static class ImportPaths
{
    // Pure syntax only. Do not enumerate or probe another manager's game directories.
    public static string? Folder(string? value)
    {
        if (value is null || value.Length is < 3 or > 2048 || value.Any(char.IsControl) || value.Contains('%')
            || !System.Text.RegularExpressions.Regex.IsMatch(value, "^[A-Za-z]:[\\\\/]")
            || value[2..].Contains(':') || value.IndexOfAny(['*', '?', '"', '<', '>', '|']) >= 0) return null;
        var parts = value[3..].Replace('/', '\\').Split('\\', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 || parts.Any(p => p is "." or ".." || p.EndsWith('.') || p.EndsWith(' ')
            || System.Text.RegularExpressions.Regex.IsMatch(p, "^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])($|[.])", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant))) return null;
        return char.ToUpperInvariant(value[0]) + ":\\" + string.Join('\\', parts);
    }
    public static string Key(string path) => path.Replace('/', '\\').TrimEnd('\\').ToUpperInvariant();
    public static string? Executable(string folder, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var full = Folder(value) ?? (value.Contains(':') || value.StartsWith('\\') || value.StartsWith('/') ? null : Folder(folder + "\\" + value));
        if (full is null || !full.StartsWith(folder.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase)
            || !full.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return null;
        var name = full.Split('\\')[^1].ToLowerInvariant();
        return new[] { "cmd.exe", "powershell.exe", "pwsh.exe", "wscript.exe", "cscript.exe", "mshta.exe", "rundll32.exe", "regsvr32.exe", "msiexec.exe" }.Contains(name) ? null : full;
    }
}
public static class ImportModel
{
    public const int MaximumGames = 10000;
    public static void Validate(ImportDataset d)
    {
        if (!MigrationFormats.Valid(d.Manager, d.Format) || !MigrationLineage.Digest(d.DocumentDigest)
            || d.Games.IsDefault || d.Games.Length > MaximumGames || d.RawCount < d.Games.Length || d.RawCount > MaximumGames
            || d.Issues.IsDefault || d.Issues.Length > 100000 || d.Exclusions.IsDefault || d.Exclusions.Length > d.RawCount
            || d.Exclusions.Any(e => e is null || e.Name.Length is 0 or > 2000 || e.Name.Contains('\0')
                || e.RecordKey.Length != 20 || !e.RecordKey.All(Uri.IsHexDigit) || e.Reason != "NON_GALGAME_OR_UNVERIFIED_IDENTITY_EXCLUDED"))
            throw new InvalidDataException("Invalid import dataset.");
        foreach (var g in d.Games)
        {
            if (g is null || string.IsNullOrWhiteSpace(g.SourceId) || g.SourceId.Length > 200 || string.IsNullOrWhiteSpace(g.Name) || g.Name.Length > 2000
                || g.Notes.Length > 20000 || g.Notes.Contains('\0') || !GameProgress.Valid(g.Status)
                || g.Rating is { } rating && (!double.IsFinite(rating) || rating is < 0 or > 10)
                || g.TotalSeconds is < 0 or > 1_000_000_000_000 || g.PlayCount is < 0 or > 1_000_000_000
                || g.Installations.IsDefault || g.Installations.Length > 100 || g.Days.IsDefault || g.Days.Length > 10000
                || g.Sessions.IsDefault || g.Sessions.Length > 10000 || g.Identities.IsDefault || g.Identities.Length > 16
                || g.Identities.Any(i => !MigrationLineage.Identity(i)) || g.Identities.Select(x => x.Provider).Distinct().Count() != g.Identities.Length
                || g.Categories.IsDefault || g.Tags.IsDefault || g.Categories.Length > 100 || g.Tags.Length > 100
                || g.Aliases.IsDefault || g.Aliases.Length > 100 || g.Issues.IsDefault) throw new InvalidDataException("Invalid import game.");
            foreach (var i in g.Installations)
                if (ImportPaths.Folder(i.Folder) is null || i.Executable is not null && ImportPaths.Executable(i.Folder, i.Executable) != i.Executable)
                    throw new InvalidDataException("Unsafe import installation.");
            if (g.Days.Any(x => x is null) || g.Days.Select(x => x.Date).Distinct().Count() != g.Days.Length || g.Days.Any(x => x.Seconds is <= 0 or > 86400)
                || g.Sessions.Any(x => x is null || string.IsNullOrWhiteSpace(x.SourceId) || x.SourceId.Length > 200 || x.SourceId.Any(char.IsControl)
                    || x.End <= x.Start || (x.End - x.Start).TotalDays > 366 || x.Seconds <= 0 || x.Seconds > (x.End - x.Start).TotalSeconds)
                || g.Sessions.Select(x => x.SourceId).Distinct().Count() != g.Sessions.Length
                || g.Days.Sum(x => x.Seconds) + g.Sessions.Sum(x => x.Seconds) > g.TotalSeconds)
                throw new InvalidDataException("Inconsistent source time accounting.");
            var sessions = g.Sessions.OrderBy(x => x.Start).ToArray();
            for (var i = 1; i < sessions.Length; i++) if (sessions[i].Start < sessions[i - 1].End) throw new InvalidDataException("Overlapping source sessions.");
        }
    }
}
