using System.Collections.Immutable;
using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using TheOrder.Application.Migrations;
using TheOrder.Domain;

namespace TheOrder.Infrastructure.Migration;

// Reads the JSON produced by PotatoVN's ExportTask, not its live LiteDB database.
// The pinned upstream writer/model/converters and supported layouts are recorded in docs/migration-v1.md.
public sealed class PotatoVnImportAdapter(TimeZoneInfo? sourceTimeZone = null) : ILibraryMigrationAdapter
{
    public string Manager => "potatovn";
    public const int MaximumJsonBytes = 32 * 1024 * 1024;
    public const long MaximumArchiveBytes = 512L * 1024 * 1024;
    public async Task<ImportDataset> ReadAsync(Stream source, string documentDigest, CancellationToken cancellation = default)
    {
        if (!source.CanSeek || source.Length > MaximumArchiveBytes || !MigrationLineage.Digest(documentDigest)) throw new InvalidDataException("SOURCE_SIZE_OR_STREAM_UNSUPPORTED");
        var data = new Dictionary<string, JsonDocument>(StringComparer.Ordinal);
        try
        {
            var signature = new byte[4]; var length = await source.ReadAtLeastAsync(signature, 4, false, cancellation); source.Position = 0;
            var archive = length == 4 && signature[0] == 'P' && signature[1] == 'K';
            if (length == 4 && signature[0] == 'P' && signature[1] == 'K')
            {
                CheckDirectory(source); source.Position = 0;
                using var zip = new ZipArchive(source, ZipArchiveMode.Read, true);
                var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase); long expanded = 0;
                foreach (var entry in zip.Entries)
                {
                    cancellation.ThrowIfCancellationRequested();
                    var name = entry.FullName.Replace('\\', '/');
                    if (name.Length > 1000 || name.StartsWith('/') || name.Contains(':') || name.Split('/').Any(p => p is "." or "..")
                        || name.Any(char.IsControl) || !names.Add(name) || ((entry.ExternalAttributes >> 16) & 0xf000) == 0xa000)
                        throw new InvalidDataException("UNSAFE_OR_DUPLICATE_ARCHIVE_PATH");
                    expanded = checked(expanded + entry.Length);
                    if (expanded > 2L * 1024 * 1024 * 1024 || entry.Length > 1024L * 1024 * 1024) throw new InvalidDataException("ARCHIVE_EXPANSION_LIMIT");
                    if (name is not ("data.galgames.json" or "data.categoryGroups.json" or "data.galgameSources.json" or "data.dataStatus.json")) continue;
                    if (entry.Length > MaximumJsonBytes || entry.Length > 1024 * Math.Max(1, entry.CompressedLength)) throw new InvalidDataException("METADATA_EXPANSION_LIMIT");
                    await using var input = entry.Open(); data.Add(name, await Json(input, cancellation));
                }
            }
            else data.Add("data.galgames.json", await Json(source, cancellation));
            if (!data.TryGetValue("data.galgames.json", out var games) || games.RootElement.ValueKind != JsonValueKind.Array
                || games.RootElement.GetArrayLength() > ImportModel.MaximumGames) throw new InvalidDataException("POTATOVN_EXPORT_LAYOUT_UNSUPPORTED");
            if (!archive && games.RootElement.GetArrayLength() == 0) throw new InvalidDataException("EMPTY_JSON_REQUIRES_EXPORT_CONTAINER");
            if (games.RootElement.GetArrayLength() > 0 && !games.RootElement.EnumerateArray().Any(g => g.ValueKind == JsonValueKind.Object && g.TryGetProperty("Name", out _) && g.TryGetProperty("Ids", out _) && g.TryGetProperty("TotalPlayTime", out _)))
                throw new InvalidDataException("POTATOVN_EXPORT_LAYOUT_UNSUPPORTED");
            if (data.TryGetValue("data.dataStatus.json", out var status))
            {
                if (status.RootElement.ValueKind != JsonValueKind.Object) throw new InvalidDataException("POTATOVN_STATUS_INVALID");
                if (status.RootElement.TryGetProperty("FormatVersion", out _) || status.RootElement.TryGetProperty("Version", out _)) throw new InvalidDataException("POTATOVN_EXPLICIT_VERSION_UNSUPPORTED");
                foreach (var field in status.RootElement.EnumerateObject())
                    if (field.Name.Contains("LiteDb", StringComparison.OrdinalIgnoreCase) && field.Value.ValueKind != JsonValueKind.False)
                        throw new InvalidDataException("LIVE_DATABASE_IS_NOT_AN_EXPORT");
            }
            var allIssues = ImmutableArray.CreateBuilder<ImportIssue>(); var parsed = new List<ImportGame>(); var rawCount = games.RootElement.GetArrayLength();
            var index = 0;
            foreach (var game in games.RootElement.EnumerateArray())
            {
                cancellation.ThrowIfCancellationRequested();
                try { parsed.Add(ParseGame(game)); }
                catch (Exception ex) when (ex is InvalidDataException or FormatException or OverflowException or InvalidOperationException or ArgumentException)
                { allIssues.Add(new(ImportHash.Key(Manager, "row-" + index), "record", "INVALID_SOURCE_RECORD")); }
                index++;
            }
            if (data.TryGetValue("data.galgameSources.json", out var sources)) parsed = LinkSources(parsed, sources.RootElement, allIssues);
            if (data.TryGetValue("data.categoryGroups.json", out var categories)) parsed = LinkCategories(parsed, categories.RootElement, allIssues);
            var result = new ImportDataset(Manager, "potatovn-export-json-v1", documentDigest, rawCount, [.. parsed], allIssues.ToImmutable()) { SourceTimeZone = sourceTimeZone?.Id };
            ImportModel.Validate(result); return result;
        }
        catch (JsonException ex) { throw new InvalidDataException("SOURCE_JSON_INVALID", ex); }
        finally { foreach (var doc in data.Values) doc.Dispose(); }
    }
    private ImportGame ParseGame(JsonElement o)
    {
        if (o.ValueKind != JsonValueKind.Object || !o.TryGetProperty("Name", out _) || !o.TryGetProperty("TotalPlayTime", out _)
            || !o.TryGetProperty("Ids", out var idArray) || idArray.ValueKind != JsonValueKind.Array || idArray.GetArrayLength() is < 2 or > 32)
            throw new InvalidDataException("Unknown game layout.");
        var name = Text(Prop(Prop(o, "Name"), "Value"), 2000); if (string.IsNullOrWhiteSpace(name)) throw new InvalidDataException();
        var original = Text(Prop(Prop(o, "OriginalName"), "Value"), 2000);
        var aliases = new[] { Text(Prop(o, "CnName"), 2000), Text(Prop(Prop(o, "ChineseName"), "Value"), 2000), original }.Where(s => s.Length > 0 && s != name).Distinct(StringComparer.Ordinal).ToImmutableArray();
        var ids = ImmutableArray.CreateBuilder<ImportedIdentity>(); var issues = ImmutableArray.CreateBuilder<ImportIssue>(); var review = false;
        foreach (var (provider, position) in new[] { ("vndb", 0), ("bangumi", 1), ("steam", 7) })
        {
            if (position >= idArray.GetArrayLength()) continue;
            var value = Text(idArray[position], 200); if (value.Length == 0) continue;
            var clean = NormalizeId(provider, value);
            if (clean is null) { issues.Add(new("", "Ids", "INVALID_EXTERNAL_ID_IGNORED")); continue; }
            ids.Add(new(provider, clean));
        }
        var uuid = Text(Prop(o, "Uuid"), 200);
        if (uuid.Length > 0 && !Guid.TryParseExact(uuid, "D", out _)) throw new InvalidDataException("Invalid source UUID.");
        var sourceId = uuid.Length > 0 ? uuid.ToLowerInvariant() : "legacy-" + ImportHash.Of(new { name, ids = ids.ToArray(), path = Text(Prop(o, "Path"), 2048) });
        var key = ImportHash.Key(Manager, sourceId);
        var total = checked(Integer(Prop(o, "TotalPlayTime"), 0, 1_000_000_000) * 60);
        var days = ImmutableArray.CreateBuilder<ImportDay>();
        if (Prop(o, "PlayedTime") is { ValueKind: JsonValueKind.Object } played)
        {
            foreach (var day in played.EnumerateObject())
            {
                var minutes = Integer(day.Value, 0, 1440);
                if (!DateOnly.TryParseExact(day.Name, ["yyyy/M/d", "yyyy-MM-dd"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                    || date.Year is < 2 or > 9998) { issues.Add(new(key, "PlayedTime", "AMBIGUOUS_DATE_KEPT_UNDATED")); continue; }
                if (minutes > 0) days.Add(new(date, checked(minutes * 60)));
            }
        }
        if (days.Select(d => d.Date).Distinct().Count() != days.Count || days.Sum(x => x.Seconds) > total)
        { review = true; days.Clear(); issues.Add(new(key, "PlayedTime", "SOURCE_TOTAL_DAILY_CONFLICT")); }
        var playType = Integer(Prop(o, "PlayType"), 0, 100);
        var status = playType switch { 0 or 5 => "planned", 1 => "playing", 2 => "completed", 3 => "paused", 4 => "dropped", _ => "planned" };
        if (playType > 5) { review = true; issues.Add(new(key, "PlayType", "UNKNOWN_STATE_REQUIRES_REVIEW")); }
        var rate = Integer(Prop(o, "MyRate"), 0, 10);
        var tags = Array(Prop(Prop(o, "Tags"), "Value")).Select(x => Text(x, 200)).Where(x => x.Length > 0).Distinct(StringComparer.Ordinal).Take(101).ToImmutableArray();
        if (tags.Length > 100) throw new InvalidDataException("Too many tags.");
        var rawLast = LocalDateText(Prop(o, "LastPlayTime")) ?? LocalDateText(Prop(Prop(o, "LastPlay"), "Value")); var rawAdded = LocalDateText(Prop(o, "AddTime"));
        var last = Instant(rawLast); var added = Instant(rawAdded);
        if (last is null && rawLast is not null) issues.Add(new(key, "LastPlayTime", "LOCAL_TIME_NOT_CONVERTED_PRESERVED_IN_LINEAGE"));
        if (added is null && rawAdded is not null) issues.Add(new(key, "AddTime", "LOCAL_TIME_NOT_CONVERTED_PRESERVED_IN_LINEAGE"));
        var installations = ImmutableArray<ImportInstallation>.Empty;
        var path = Text(Prop(o, "Path"), 2048);
        if (path.Length > 0)
        {
            if (ImportPaths.Folder(path) is { } folder)
            {
                var rawExe = Text(Prop(o, "ExePath"), 2048); var exe = ImportPaths.Executable(folder, rawExe);
                if (rawExe.Length > 0 && exe is null) issues.Add(new(key, "ExePath", "UNSAFE_EXECUTABLE_NOT_IMPORTED"));
                installations = [new("legacy", folder, exe)];
                issues.Add(new(key, "Path", "PATH_EXISTENCE_NOT_PROBED"));
            }
            else { review = true; issues.Add(new(key, "Path", "INVALID_OR_UNRESOLVED_INSTALLATION_PATH")); }
        }
        foreach (var field in new[] { "ExeArguments", "RunAsAdmin", "RunInLocaleEmulator", "EnableMagpie", "MuteInBackground", "SavePath", "ImagePath", "HeaderImagePath", "KeyMappings", "IdForPlugins" })
            if (o.TryGetProperty(field, out var v) && v.ValueKind is not (JsonValueKind.Null or JsonValueKind.False) && v.ToString() is not ("" or "[]" or "{}"))
                issues.Add(new(key, field, "UNSUPPORTED_OR_AUXILIARY_FIELD_NOT_IMPORTED"));
        return new(sourceId, name, original, aliases, ids.ToImmutable(), installations, [], tags, status, rate == 0 ? null : rate,
            null, Text(Prop(o, "Comment"), 20000), total, o.TryGetProperty("PlayCount", out var count) ? Integer(count, 0, 1_000_000_000) : null,
            [.. days.OrderBy(d => d.Date)], [], last, added, last is null ? rawLast : null, added is null ? rawAdded : null, null,
            [.. issues.Select(i => i with { RecordKey = key })]) { NeedsReview = review };
    }
    private List<ImportGame> LinkSources(List<ImportGame> games, JsonElement sources, ImmutableArray<ImportIssue>.Builder issues)
    {
        if (sources.ValueKind != JsonValueKind.Array) throw new InvalidDataException("POTATOVN_SOURCES_LAYOUT_UNSUPPORTED");
        var lookup = new LinkLookup(games);
        var links = new Dictionary<string, List<ImportInstallation>>(StringComparer.Ordinal);
        foreach (var source in sources.EnumerateArray())
        {
            var kind = Integer(Prop(source, "SourceType"), 0, 100);
            if (kind != 1 && kind != 4) { if (kind is not (0 or 2 or 3)) issues.Add(new("", "SourceType", "UNSUPPORTED_LIBRARY_KIND")); continue; }
            foreach (var row in Array(Prop(source, "Galgames")))
            {
                var matches = lookup.Find(Prop(row, "Galgame"));
                if (matches.Length != 1) { issues.Add(new("", "Galgames.Galgame", "UNRESOLVED_OR_AMBIGUOUS_SOURCE_LINK")); continue; }
                var g = matches[0]; var key = ImportHash.Key(Manager, g.SourceId); var raw = Text(Prop(row, "Path"), 2048);
                var folder = ImportPaths.Folder(raw);
                if (folder is null) { issues.Add(new(key, "Galgames.Path", "INVALID_OR_UNRESOLVED_INSTALLATION_PATH")); continue; }
                var config = Prop(row, "LocalConfig"); var rawExe = Text(Prop(config, "ExePath"), 2048);
                if (rawExe.Length == 0 && g.Installations.FirstOrDefault(i => ImportPaths.Key(i.Folder) == ImportPaths.Key(folder)) is { } legacy) rawExe = legacy.Executable ?? "";
                var exe = ImportPaths.Executable(folder, rawExe);
                if (rawExe.Length > 0 && exe is null) issues.Add(new(key, "LocalConfig.ExePath", "UNSAFE_EXECUTABLE_NOT_IMPORTED"));
                if (Text(Prop(config, "ExeArguments"), 2000).Length > 0) issues.Add(new(key, "LocalConfig.ExeArguments", "LAUNCH_ARGUMENTS_REQUIRE_MANUAL_COMPATIBILITY_REVIEW"));
                var entryId = Text(Prop(row, "EntryId"), 200); if (entryId.Length == 0) entryId = ImportHash.Text(ImportPaths.Key(folder));
                if (!links.TryGetValue(g.SourceId, out var list)) links[g.SourceId] = list = [];
                list.Add(new(entryId, folder, exe));
                issues.Add(new(key, "LocalConfig", "AUXILIARY_SETTINGS_NOT_IMPORTED"));
                issues.Add(new(key, "Galgames.Path", "PATH_EXISTENCE_NOT_PROBED"));
            }
        }
        return games.Select(g => links.TryGetValue(g.SourceId, out var list) ? g with
        {
            // Modern source entries own installations. An obsolete game-level Path is not an extra install.
            Installations = [.. list.GroupBy(i => ImportPaths.Key(i.Folder)).Select(group => group.First())],
            NeedsReview = g.NeedsReview || list.GroupBy(i => ImportPaths.Key(i.Folder)).Any(group => group.Count() > 1),
            Issues = list.GroupBy(i => ImportPaths.Key(i.Folder)).Any(group => group.Count() > 1)
                ? g.Issues.Add(new(ImportHash.Key(Manager, g.SourceId), "Galgames.Path", "DUPLICATE_INSTALLATION_CONFIG_REQUIRES_REVIEW")) : g.Issues
        } : g).ToList();
    }
    private List<ImportGame> LinkCategories(List<ImportGame> games, JsonElement groups, ImmutableArray<ImportIssue>.Builder issues)
    {
        if (groups.ValueKind != JsonValueKind.Array) throw new InvalidDataException("POTATOVN_CATEGORIES_LAYOUT_UNSUPPORTED");
        var lookup = new LinkLookup(games);
        var categories = new Dictionary<string, List<string>>();
        foreach (var group in groups.EnumerateArray())
        {
            // 0 developer / 1 status / 2 user custom / 3 engine. Do not pretend scraped categories are personal labels.
            if (Integer(Prop(group, "Type"), 0, 100) != 2) continue;
            var groupName = Text(Prop(group, "Name"), 200);
            foreach (var category in Array(Prop(group, "Categories")))
            {
                var label = Text(Prop(category, "Name"), 200); if (label.Length == 0) continue;
                var full = groupName.Length == 0 ? label : groupName + " / " + label;
                foreach (var link in Array(Prop(category, "GalgamesX")))
                {
                    var matches = lookup.Find(link);
                    if (matches.Length != 1) { issues.Add(new("", "Categories.GalgamesX", "UNRESOLVED_OR_AMBIGUOUS_CATEGORY_LINK")); continue; }
                    if (!categories.TryGetValue(matches[0].SourceId, out var list)) categories[matches[0].SourceId] = list = [];
                    if (!list.Contains(full)) list.Add(full);
                }
            }
        }
        return games.Select(g => categories.TryGetValue(g.SourceId, out var list) ? g with { Categories = [.. list] } : g).ToList();
    }
    private sealed class LinkLookup
    {
        private readonly Dictionary<string, ImportGame[]> bySource;
        private readonly Dictionary<(string, string), ImportGame[]> byId;
        public LinkLookup(List<ImportGame> games)
        {
            bySource = games.GroupBy(g => g.SourceId, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.OrdinalIgnoreCase);
            byId = games.SelectMany(g => g.Identities.Select(i => (i.Provider, i.Id, Game: g))).GroupBy(x => (x.Provider, x.Id)).ToDictionary(g => g.Key, g => g.Select(x => x.Game).ToArray());
        }
        public ImportGame[] Find(JsonElement link)
        {
            if (link.ValueKind == JsonValueKind.String) return bySource.GetValueOrDefault(link.GetString()!) is { Length: 1 } source ? source : [];
            if (link.ValueKind != JsonValueKind.Object) return [];
            var ids = new[] { ("vndb", "VndbId"), ("bangumi", "BangumiId") }.Select(x => (Provider: x.Item1, Id: NormalizeId(x.Item1, Text(Prop(link, x.Item2), 200))))
                .Where(x => x.Id is not null).ToArray();
            var candidates = ids.Select(i => byId.GetValueOrDefault((i.Provider, i.Id!))).Where(x => x is not null).OrderBy(x => x!.Length).FirstOrDefault();
            if (candidates is null || candidates.Length > 2) return [];
            return candidates.Where(g => !ids.Any(i => g.Identities.Any(x => x.Provider == i.Provider && x.Id != i.Id))).ToArray();
        }
    }
    private static string? NormalizeId(string provider, string value)
    {
        if (provider == "vndb" && value.StartsWith('v')) value = value[1..];
        var id = provider == "vndb" ? "v" + value : value;
        return MigrationLineage.Identity(new(provider, id)) ? id : null;
    }
    private static JsonElement Prop(JsonElement element, string name) => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) ? value : default;
    private static IEnumerable<JsonElement> Array(JsonElement element)
        => element.ValueKind == JsonValueKind.Array ? element.EnumerateArray() : element.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined ? [] : throw new InvalidDataException("Expected array.");
    private static long Integer(JsonElement element, long min, long max)
    {
        if (element.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null) return min;
        if (!element.TryGetInt64(out var n) || n < min || n > max) throw new InvalidDataException("Number outside supported range.");
        return n;
    }
    private static string Text(JsonElement element, int max)
    {
        if (element.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return "";
        if (element.ValueKind != JsonValueKind.String) throw new InvalidDataException("Expected text.");
        var s = element.GetString()!; if (s.Length > max || s.Contains('\0')) throw new InvalidDataException("Text outside supported range.");
        return s;
    }
    private static string? LocalDateText(JsonElement element)
    { var s = Text(element, 100); return s.Length == 0 || s.StartsWith("0001-01-01", StringComparison.Ordinal) ? null : s; }
    private DateTimeOffset? Instant(string? text)
    {
        if (text is null) return null;
        if (System.Text.RegularExpressions.Regex.IsMatch(text, "(Z|[+-][0-9]{2}:[0-9]{2})$")
            && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var time)) return time;
        if (sourceTimeZone is null || !DateTime.TryParseExact(text, ["yyyy-MM-dd'T'HH:mm:ss.FFFFFFF", "yyyy-MM-dd'T'HH:mm:ss"],
            CultureInfo.InvariantCulture, DateTimeStyles.None, out var local)) return null;
        local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        if (sourceTimeZone.IsAmbiguousTime(local) || sourceTimeZone.IsInvalidTime(local)) return null;
        return new DateTimeOffset(local, sourceTimeZone.GetUtcOffset(local));
    }
    private static async Task<JsonDocument> Json(Stream input, CancellationToken cancellation)
    {
        using var buffer = new MemoryStream(); var bytes = new byte[65536]; int read;
        while ((read = await input.ReadAsync(bytes, cancellation)) > 0)
        { if (buffer.Length + read > MaximumJsonBytes) throw new InvalidDataException("SOURCE_METADATA_TOO_LARGE"); await buffer.WriteAsync(bytes.AsMemory(0, read), cancellation); }
        var data = buffer.ToArray(); var start = data.AsSpan().StartsWith(new byte[] { 239, 187, 191 }) ? 3 : 0;
        var document = JsonDocument.Parse(data.AsMemory(start), new() { MaxDepth = 32 });
        try { var nodes = 0; Check(document.RootElement, ref nodes, cancellation); return document; } catch { document.Dispose(); throw; }
    }
    private static void Check(JsonElement e, ref int nodes, CancellationToken cancellation)
    {
        if (++nodes % 512 == 0) cancellation.ThrowIfCancellationRequested();
        if (nodes > 3_000_000) throw new InvalidDataException("SOURCE_NODE_LIMIT");
        if (e.ValueKind == JsonValueKind.Object)
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var p in e.EnumerateObject())
            {
                if (p.Name.Length > 200 || !keys.Add(p.Name) || keys.Count > 10000 || p.Name is "$type" or "$values") throw new InvalidDataException("DUPLICATE_OR_TYPED_JSON_PROPERTY");
                Check(p.Value, ref nodes, cancellation);
            }
        }
        else if (e.ValueKind == JsonValueKind.Array)
        { if (e.GetArrayLength() > 10000) throw new InvalidDataException("SOURCE_ARRAY_LIMIT"); foreach (var item in e.EnumerateArray()) Check(item, ref nodes, cancellation); }
        else if (e.ValueKind == JsonValueKind.String && e.GetString()!.Length > 100000) throw new InvalidDataException("SOURCE_FIELD_TOO_LARGE");
    }
    private static void CheckDirectory(Stream source)
    {
        var size = (int)Math.Min(source.Length, 65557); source.Position = source.Length - size; var tail = new byte[size]; source.ReadExactly(tail);
        for (var i = size - 22; i >= 0; i--)
        {
            if (BitConverter.ToUInt32(tail, i) != 0x06054b50) continue;
            if (i + 22 + BitConverter.ToUInt16(tail, i + 20) != size) continue;
            var count = BitConverter.ToUInt16(tail, i + 10);
            if (BitConverter.ToUInt32(tail, i + 4) != 0 || count > 30000 || count == 65535 || count != BitConverter.ToUInt16(tail, i + 8)
                || BitConverter.ToUInt32(tail, i + 12) > 12 * 1024 * 1024
                || (long)BitConverter.ToUInt32(tail, i + 16) + BitConverter.ToUInt32(tail, i + 12) > source.Length - size + i)
                throw new InvalidDataException("ARCHIVE_DIRECTORY_LIMIT_OR_ZIP64_UNSUPPORTED");
            return;
        }
        throw new InvalidDataException("ARCHIVE_DIRECTORY_INVALID");
    }
}
