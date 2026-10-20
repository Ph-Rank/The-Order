using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using TheOrder.Application;
using TheOrder.Application.Migrations;
using TheOrder.Domain;

namespace TheOrder.Infrastructure.Migration;

public sealed class ManagerImportAdapter(ITitleIndex? index = null, TimeZoneInfo? sourceTimeZone = null) : ILibraryMigrationAdapter
{
    public string Manager => "automatic";
    public async Task<ImportDataset> ReadAsync(Stream source, string documentDigest, CancellationToken cancellation = default)
    {
        if (!MigrationLineage.Digest(documentDigest)) throw new InvalidDataException("INVALID_SOURCE_DIGEST");
        using var archive = new InterchangeArchive(source);
        var formats = new List<string>();
        if (archive.Has("data.galgames.json")) formats.Add("potatovn");
        if (archive.Has("game/CURRENT")) formats.Add("vnite");
        if (archive.Has("library/games.db") && archive.Has("library/database.json")) formats.Add("playnite");
        if (archive.Has("database/games.csv") && archive.Has("database/schema.sql") || archive.Has("games.csv") && archive.Has("schema.sql")) formats.Add("lunabox");
        var reina = archive.Entries.Keys.Where(n => Regex.IsMatch(n, @"(^|/)reina_manager(?:_[a-zA-Z0-9_-]+)?\.db$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)).ToArray();
        if (reina.Length == 1) formats.Add("reinamanager");
        if (formats.Count != 1) throw new InvalidDataException("UNKNOWN_OR_AMBIGUOUS_MANAGER_BACKUP");
        cancellation.ThrowIfCancellationRequested();
        ImportDataset dataset;
        if (formats[0] == "potatovn")
        { source.Position = 0; dataset = await new PotatoVnImportAdapter(sourceTimeZone).ReadAsync(source, documentDigest, cancellation).ConfigureAwait(false); }
        else dataset = formats[0] switch
        {
            "vnite" => VniteArchive.Read(archive, documentDigest, cancellation),
            "playnite" => PlayniteArchive.Read(archive, documentDigest, cancellation),
            "reinamanager" => ReinaArchive.Read(archive, reina[0], documentDigest, cancellation),
            "lunabox" => LunaArchive.Read(archive, documentDigest, cancellation),
            _ => throw new InvalidDataException("UNSUPPORTED_MANAGER")
        };
        // A mixed general-purpose library is not a Galgame library merely because
        // a backup can be parsed. Never infer eligibility from a title substring.
        var games = ImmutableArray.CreateBuilder<ImportGame>(); var issues = dataset.Issues.ToBuilder();
        var exclusions = ImmutableArray.CreateBuilder<ImportExclusion>(); var indexUnavailable = false;
        foreach (var game in dataset.Games)
        {
            cancellation.ThrowIfCancellationRequested();
            var eligible = game.Identities.Any(i => i.Provider is "vndb" or "nextmoe")
                || game.Tags.Concat(game.Categories).Any(t => t.Trim().ToLowerInvariant() is "galgame" or "visual novel" or "visual novels" or "美少女游戏" or "视觉小说" or "ビジュアルノベル");
            if (!eligible && index is not null && !indexUnavailable)
            try
            {
                foreach (var id in game.Identities)
                    if (await index.ContainsReferenceAsync(id.Provider + ":" + id.Id, cancellation).ConfigureAwait(false)) { eligible = true; break; }
            }
            catch (Exception e) when (e is IOException or InvalidDataException or System.Text.Json.JsonException)
            { indexUnavailable = true; issues.Add(new("", "eligibility", "TITLE_INDEX_UNAVAILABLE_STRONG_EVIDENCE_ONLY")); }
            if (eligible) games.Add(game);
            else
            {
                var key = ImportHash.Key(dataset.Manager, game.SourceId);
                issues.Add(new(key, "eligibility", "NON_GALGAME_OR_UNVERIFIED_IDENTITY_EXCLUDED"));
                exclusions.Add(new(key, game.Name, "NON_GALGAME_OR_UNVERIFIED_IDENTITY_EXCLUDED"));
            }
        }
        dataset = dataset with { Games = games.ToImmutable(), Issues = issues.ToImmutable(), Exclusions = exclusions.ToImmutable() };
        ImportModel.Validate(dataset); return dataset;
    }
}

internal sealed class ImportRecord(string manager, string id, string name)
{
    internal string Original = "", Notes = "", Status = GameProgress.Default;
    internal List<string> Aliases = [], Tags = [], Categories = [];
    internal List<ImportedIdentity> Ids = [];
    internal List<ImportInstallation> Installations = [];
    internal List<ImportSession> Sessions = [];
    internal List<ImportDay> Days = [];
    internal List<ImportIssue> Issues = [];
    internal long Total;
    internal long? Count;
    internal bool? Favorite;
    internal double? Rating;
    internal DateTimeOffset? Added, Last;
    internal string? AddedLocal, LastLocal;
    internal bool Review;
    internal void Issue(string field, string code) => Issues.Add(new(ImportHash.Key(manager, id), field, code));
    internal void Identity(string provider, string raw)
    {
        if (raw.Length == 0) return;
        var value = provider == "vndb" && Regex.IsMatch(raw, "^[1-9][0-9]{0,9}$", RegexOptions.CultureInvariant) ? "v" + raw : raw;
        var identity = new ImportedIdentity(provider, value);
        if (!MigrationLineage.Identity(identity)) { Issue("identity", "INVALID_EXTERNAL_ID_IGNORED"); return; }
        if (Ids.Any(i => i.Provider == provider && i != identity)) { Review = true; Issue("identity", "SOURCE_IDENTITY_CONFLICT"); }
        else if (!Ids.Contains(identity)) Ids.Add(identity);
    }
    internal void Installation(string folder, string executable, string kind = "file")
    {
        if (folder.Length == 0 && executable.Length == 0) return;
        var safe = ImportPaths.Folder(folder);
        if (safe is null && ImportPaths.Folder(executable) is { } full && full.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            safe = full[..full.LastIndexOf('\\')];
        if (safe is null) { Issue("installation", "INVALID_OR_UNRESOLVED_INSTALLATION_PATH"); return; }
        var exe = kind == "file" ? ImportPaths.Executable(safe, executable) : null;
        if (executable.Length > 0 && exe is null) Issue("executable", "UNSAFE_EXECUTABLE_NOT_IMPORTED");
        if (!Installations.Any(i => ImportPaths.Key(i.Folder) == ImportPaths.Key(safe))) Installations.Add(new(id + ":" + Installations.Count, safe, exe));
    }
    internal static DateTimeOffset? Timestamp(string value) => Regex.IsMatch(value, @"(Z|[+-]\d\d:\d\d)$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)
        && DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var result) && result.Year is >= 2 and <= 9998 ? result : null;
    internal void Session(string key, string start, string end, long? seconds = null)
    {
        var a = Timestamp(start); var b = Timestamp(end);
        if (a is null || b is null || b <= a || (b - a).Value.TotalDays > 366) { Issue("sessions", "INVALID_OR_UNZONED_SESSION_KEPT_UNDATED"); return; }
        var duration = seconds ?? (long)(b.Value - a.Value).TotalSeconds;
        if (duration <= 0 || duration > (b.Value - a.Value).TotalSeconds) { Issue("sessions", "INVALID_SESSION_DURATION_KEPT_UNDATED"); return; }
        Sessions.Add(new(key, a.Value, b.Value, duration));
    }
    internal ImportGame Finish()
    {
        var ordered = Sessions.OrderBy(s => s.Start).ToArray();
        if (ordered.Zip(ordered.Skip(1)).Any(p => p.Second.Start < p.First.End)
            || Sessions.Select(s => s.SourceId).Distinct().Count() != Sessions.Count
            || Days.Select(d => d.Date).Distinct().Count() != Days.Count
            || Sessions.Sum(s => s.Seconds) + Days.Sum(d => d.Seconds) > Total)
        { Review = true; Issue("history", "SOURCE_TIME_ACCOUNTING_CONFLICT"); Sessions.Clear(); Days.Clear(); }
        var result = new ImportGame(id, name, Original, [.. Aliases.Where(a => a.Length > 0 && a != name).Distinct().Take(100)], [.. Ids], [.. Installations],
            [.. Categories.Distinct().Take(100)], [.. Tags.Distinct().Take(100)], Status, Rating, Favorite, Notes, Total, Count, [.. Days], [.. Sessions], Last, Added,
            LastLocal, AddedLocal, null, [.. Issues]) { NeedsReview = Review };
        // Validate each record independently so corruption does not hide the rest of a backup.
        ImportModel.Validate(new(manager, ManagerFormat(manager), new string('a', 64), 1, [result], [])); return result;
    }
    internal static string ManagerFormat(string manager) => manager switch
    { "vnite" => "vnite-pouchdb-v4", "reinamanager" => "reinamanager-sqlite-v32", "playnite" => "playnite10-litedb4", "lunabox" => "lunabox-duckdb-csv-v1", _ => "potatovn-export-json-v1" };
    internal static ImportDataset Dataset(string manager, string digest, IEnumerable<JsonElement> rows, Func<JsonElement, ImportGame> parse, CancellationToken token)
    {
        var games = ImmutableArray.CreateBuilder<ImportGame>(); var issues = ImmutableArray.CreateBuilder<ImportIssue>(); var count = 0;
        foreach (var row in rows)
        {
            token.ThrowIfCancellationRequested(); if (++count > ImportModel.MaximumGames) throw new InvalidDataException("SOURCE_GAME_LIMIT");
            try { games.Add(parse(row)); }
            catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException or FormatException or OverflowException or ArgumentException)
            { issues.Add(new(ImportHash.Key(manager, "row-" + count), "record", "INVALID_SOURCE_RECORD")); }
        }
        return new(manager, ManagerFormat(manager), digest, count, games.ToImmutable(), issues.ToImmutable());
    }
}
