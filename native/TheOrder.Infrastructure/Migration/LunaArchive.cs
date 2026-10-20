using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using TheOrder.Application.Migrations;

namespace TheOrder.Infrastructure.Migration;

// DuckDB's EXPORT DATABASE defaults to CSV. Parse only whitelisted CSV files;
// never execute schema.sql/load.sql from an untrusted backup or load extensions.
internal static class LunaArchive
{
    internal static ImportDataset Read(InterchangeArchive archive, string digest, CancellationToken token)
    {
        var prefix = archive.Has("database/games.csv") ? "database/" : "";
        List<Dictionary<string, string>> Table(string name) => archive.Has(prefix + name + ".csv") ? InterchangeCsv.Read(archive.Text(prefix + name + ".csv"), token) : [];
        var games = Table("games");
        if (games.Any(g => !g.ContainsKey("id") || !g.ContainsKey("name") || !g.ContainsKey("source_type"))) throw new InvalidDataException("LUNABOX_LAYOUT_UNSUPPORTED");
        var sessions = Table("play_sessions").ToLookup(s => Get(s, "game_id"));
        var metadata = Table("game_metadata_sources").ToLookup(s => Get(s, "game_id"));
        var categories = Table("categories").ToDictionary(s => Get(s, "id"), s => Get(s, "name"));
        var links = Table("game_categories").ToLookup(s => Get(s, "game_id"));
        var tags = Table("game_tags").ToLookup(s => Get(s, "game_id"));
        var reviews = Table("game_reviews").ToDictionary(s => Get(s, "game_id"));
        var progress = Table("game_progress").ToLookup(s => Get(s, "game_id"));
        var rows = games.Select(g => ImportJson.Parse(JsonSerializer.Serialize(g)));
        return ImportRecord.Dataset("lunabox", digest, rows, o =>
        {
            string Field(string key) => o.At(key).Str(key is "aliases" ? 20000 : 2048);
            var id = Field("id");
            var g = new ImportRecord("lunabox", id, Field("name")) { Status = Field("status") switch
            { "" or "not_started" => "planned", "want_to_play" => "wish", "playing" => "playing", "completed" => "completed", "on_hold" => "paused", "dropped" => "dropped", _ => throw new InvalidDataException("UNKNOWN_PROGRESS") } };
            void Identity(string provider, string value)
            {
                provider = provider.ToLowerInvariant();
                if (provider is "vndb" or "bangumi" or "nextmoe") g.Identity(provider, value);
            }
            Identity(Field("source_type"), Field("source_id"));
            foreach (var m in metadata[id]) Identity(Get(m, "source_type"), Get(m, "source_id"));
            if (Field("aliases").Length > 0) g.Aliases.AddRange(ImportJson.Parse(Field("aliases")).Items().Select(a => a.Str()));
            foreach (var c in links[id]) if (categories.TryGetValue(Get(c, "category_id"), out var name)) g.Categories.Add(name);
            g.Tags.AddRange(tags[id].Select(t => Get(t, "name")));
            g.Installation(Field("game_directory"), Field("path"), Field("launch_mode") is "" or "normal" ? "file" : "other");
            if (Field("wine_args").Length > 0 || Field("steam_launch_options").Length > 0) g.Issue("launch", "CUSTOM_LAUNCH_CONFIGURATION_NOT_IMPORTED");
            foreach (var s in sessions[id])
            {
                var seconds = Number(Get(s, "duration")); g.Total = checked(g.Total + seconds);
                g.Session(Get(s, "id"), Zoned(Get(s, "start_time")), Zoned(Get(s, "end_time")), seconds);
            }
            if (g.Sessions.Count > 0) g.Count = g.Sessions.Count;
            if (reviews.TryGetValue(id, out var review))
            {
                if (Get(review, "rating").Length > 0) g.Rating = Number(Get(review, "rating"));
                g.Notes = Get(review, "content");
            }
            var progressNotes = progress[id].Select(p => string.Join(" · ", new[] { Get(p, "chapter"), Get(p, "route"), Get(p, "progress_note") }.Where(n => n.Length > 0))).Where(n => n.Length > 0);
            g.Notes = string.Join("\n\n", new[] { g.Notes }.Concat(progressNotes).Where(n => n.Length > 0));
            g.Added = ImportRecord.Timestamp(Zoned(Field("created_at"))); g.Last = ImportRecord.Timestamp(Zoned(Field("last_played_at")));
            if (g.Last is null && g.Sessions.Count > 0) g.Last = g.Sessions.Max(s => s.Start);
            return g.Finish();
        }, token);
    }
    private static string Get(Dictionary<string, string> row, string key) => row.GetValueOrDefault(key) ?? "";
    private static long Number(string value) => value.Length == 0 ? 0 : long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n is >= 0 and <= 1_000_000_000_000 ? n : throw new InvalidDataException("INVALID_CSV_NUMBER");
    private static string Zoned(string value)
    {
        // DuckDB TIMESTAMPTZ emits +00 or +0900 as well as full ISO offsets.
        if (Regex.IsMatch(value, @"[+-]\d\d$", RegexOptions.CultureInvariant)) return value + ":00";
        if (Regex.IsMatch(value, @"[+-]\d{4}$", RegexOptions.CultureInvariant)) return value.Insert(value.Length - 2, ":");
        return value;
    }
}

internal static class InterchangeCsv
{
    internal static List<Dictionary<string, string>> Read(string text, CancellationToken token)
    {
        var rows = new List<string[]>(); var row = new List<string>(); var field = new StringBuilder(); bool quoted = false, closed = false;
        void Field() { if (row.Count >= 200) throw new InvalidDataException("CSV_COLUMN_LIMIT"); row.Add(field.ToString()); field.Clear(); closed = false; }
        void Row() { Field(); if (rows.Count >= 100001) throw new InvalidDataException("CSV_ROW_LIMIT"); rows.Add(row.ToArray()); row.Clear(); }
        for (var i = 0; i < text.Length; i++)
        {
            if (i % 4096 == 0) token.ThrowIfCancellationRequested(); var c = text[i];
            if (c == '\0' || field.Length > 100000) throw new InvalidDataException("CSV_FIELD_LIMIT");
            if (quoted)
            {
                if (c == '"') { if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; } else { quoted = false; closed = true; } }
                else field.Append(c);
            }
            else if (c == ',') Field();
            else if (c is '\r' or '\n') { if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++; Row(); }
            else if (c == '"' && field.Length == 0 && !closed) quoted = true;
            else { if (closed || c == '"') throw new InvalidDataException("MALFORMED_CSV_QUOTES"); field.Append(c); }
        }
        if (quoted) throw new InvalidDataException("TRUNCATED_CSV_QUOTES");
        if (row.Count > 0 || field.Length > 0 || closed) Row();
        if (rows.Count == 0) throw new InvalidDataException("CSV_HEADER_REQUIRED"); var header = rows[0];
        if (header.Distinct(StringComparer.Ordinal).Count() != header.Length || header.Any(h => !Regex.IsMatch(h, "^[a-zA-Z_][a-zA-Z_0-9]{0,99}$", RegexOptions.CultureInvariant))) throw new InvalidDataException("CSV_HEADER_INVALID");
        return rows.Skip(1).Select(values => values.Length == header.Length ? header.Zip(values).ToDictionary(x => x.First, x => x.Second, StringComparer.Ordinal) : throw new InvalidDataException("CSV_COLUMN_MISMATCH")).ToList();
    }
    internal static string Write(IEnumerable<string> columns, IEnumerable<IReadOnlyDictionary<string, string?>> rows)
    {
        var headers = columns.ToArray(); var output = new StringBuilder(); output.AppendJoin(',', headers).Append('\n');
        foreach (var row in rows)
        {
            output.AppendJoin(',', headers.Select(h => row.TryGetValue(h, out var v) && v is not null ? "\"" + v.Replace("\"", "\"\"") + "\"" : "")); output.Append('\n');
        }
        return output.ToString();
    }
}
