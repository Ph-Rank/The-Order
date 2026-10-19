using System.Collections.Immutable;
using System.Text.RegularExpressions;

namespace TheOrder.Domain;

// Data lineage lives in the existing library document, in the same atomic commit as the data.
// Imported IDs are source assertions, not a NextMoe metadata binding or a remote verification.
public sealed record ImportedIdentity(string Provider, string Id);
public sealed record ImportedField(string Target, string SourceField, string ValueDigest);
public sealed record MigrationOrigin(string Manager, string RecordId, string RecordDigest, string DocumentDigest,
    string Format, string ImportedAt, string OriginalTitle, ImmutableArray<string> Aliases,
    ImmutableArray<ImportedIdentity> Identities, ImmutableArray<ImportedField> Fields,
    string TimePrecision, long ReportedSeconds, long? ReportedPlayCount, string? LocalLastPlayed,
    string? LocalAddedAt, string? Edition, string? SourceTimeZone = null);

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
            if (o is null || o.Manager != "potatovn" || string.IsNullOrWhiteSpace(o.RecordId) || o.RecordId.Length > 200
                || !ids.Add((o.Manager, o.RecordId)) || !Digest(o.RecordDigest) || !Digest(o.DocumentDigest)
                || o.Format != "potatovn-export-json-v1" || !DateTimeOffset.TryParse(o.ImportedAt, out _)
                || o.ReportedSeconds is < 0 or > 1_000_000_000_000 || o.ReportedPlayCount is < 0 or > 1_000_000_000
                || o.TimePrecision is not ("undated-total" or "daily-aggregate" or "sessions")
                || o.Aliases.IsDefault || o.Aliases.Length > 100 || o.Identities.IsDefault || o.Identities.Length > 16
                || o.Identities.Any(x => !Identity(x)) || o.Identities.Select(x => x.Provider).Distinct().Count() != o.Identities.Length
                || o.Fields.IsDefault || o.Fields.Length > 32 || o.Fields.Any(f => f is null || !Digest(f.ValueDigest)))
                throw new InvalidDataException("Invalid migration lineage.");
            Text(o.OriginalTitle, 2000); Text(o.LocalLastPlayed, 100); Text(o.LocalAddedAt, 100); Text(o.Edition, 500);
            Text(o.SourceTimeZone, 100);
            foreach (var a in o.Aliases) Text(a, 2000);
            foreach (var f in o.Fields) { Text(f.Target, 100); Text(f.SourceField, 200); }
        }
    }
    private static void Text(string? value, int max)
    { if (value is not null && (value.Length > max || value.Any(char.IsControl))) throw new InvalidDataException("Invalid migration text."); }
}
