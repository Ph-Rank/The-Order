using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using TheOrder.Application.Migrations;

namespace TheOrder.Infrastructure.Migration;

internal static class ReinaArchive
{
    internal static ImportDataset Read(InterchangeArchive archive, string member, string digest, CancellationToken token)
    {
        if (archive.Has(member + "-wal") || archive.Has(member + "-shm")) throw new InvalidDataException("SQLITE_LIVE_COPY_IS_NOT_A_CONSISTENT_BACKUP");
        using var temp = new InterchangeTemp(); var path = temp.Write(archive.Read(member, 64 * 1024 * 1024));
        using var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        try
        {
            db.Open(); using var cancel = token.Register(() => SQLitePCL.raw.sqlite3_interrupt(db.Handle));
            SQLitePCL.raw.sqlite3_limit(db.Handle, SQLitePCL.raw.SQLITE_LIMIT_LENGTH, 1024 * 1024);
            SQLitePCL.raw.sqlite3_limit(db.Handle, SQLitePCL.raw.SQLITE_LIMIT_COLUMN, 200);
            SQLitePCL.raw.sqlite3_limit(db.Handle, SQLitePCL.raw.SQLITE_LIMIT_ATTACHED, 0);
            using (var settings = db.CreateCommand())
            { settings.CommandText = "PRAGMA trusted_schema=OFF; PRAGMA query_only=ON; PRAGMA busy_timeout=1000;"; settings.ExecuteNonQuery(); }
            var tables = Tables(db);
            if (!tables.Contains("games") || !tables.Contains("game_statistics")) throw new InvalidDataException("REINA_BACKUP_LAYOUT_UNSUPPORTED");
            var games = ReadTable(db, "games", token);
            var sources = tables.Contains("game_sources") ? ReadTable(db, "game_sources", token).ToLookup(s => s.At("game_id").Str()) : null;
            var stats = ReadTable(db, "game_statistics", token).ToDictionary(s => s.At("game_id").Str());
            var sessions = tables.Contains("game_sessions") ? ReadTable(db, "game_sessions", token).ToLookup(s => s.At("game_id").Str()) : null;
            var collections = tables.Contains("collections") ? ReadTable(db, "collections", token).ToDictionary(s => s.At("id").Str(), s => s.At("name").Str(200)) : [];
            var links = tables.Contains("game_collection_link") ? ReadTable(db, "game_collection_link", token).ToLookup(s => s.At("game_id").Str()) : null;
            return ImportRecord.Dataset("reinamanager", digest, games, o =>
            {
                var id = o.At("id").Str(200); var custom = Object(o.At("custom_data"));
                var sourceRows = sources?[id].ToList() ?? [];
                if (sources is null)
                    foreach (var (provider, field) in new[] { ("vndb", "vndb_data"), ("bgm", "bgm_data"), ("ymgal", "ymgal_data") })
                        if (o.At(field).ValueKind == JsonValueKind.String)
                            sourceRows.Add(ImportJson.Parse(JsonSerializer.Serialize(new { source = provider, external_id = o.At(provider + "_id").Str(200), data = Object(o.At(field)) })));
                var facts = sourceRows.Select(s => Object(s.At("data"))).ToArray();
                var name = custom.At("name").Str();
                if (name.Length == 0) name = o.At("custom_name").Str();
                if (name.Length == 0) name = facts.Select(f => First(f, "name_cn", "name", "title")).FirstOrDefault(n => n.Length > 0) ?? "";
                var g = new ImportRecord("reinamanager", id, name) { Notes = custom.At("user_review").Str(20000),
                    Status = o.At("clear").Int(5) switch { 1 => "wish", 2 => "playing", 3 => "completed", 4 => "paused", 5 => "dropped", _ => "planned" } };
                g.Aliases.AddRange(custom.At("aliases").Items().Select(a => a.Str()));
                g.Tags.AddRange(custom.At("tags").Items().Select(a => a.Str(200)));
                foreach (var s in sourceRows)
                {
                    var provider = s.At("source").Str(100); var sourceId = s.At("external_id").Str(200); var data = Object(s.At("data"));
                    if (provider is "vndb" or "bgm" or "nextmoe") g.Identity(provider == "bgm" ? "bangumi" : provider, sourceId);
                    var original = First(data, "name", "title", "alttitle"); if (g.Original.Length == 0 && original.Length > 0) g.Original = original;
                    foreach (var n in new[] { "name", "name_cn", "title", "alttitle" }) if (data.At(n).ValueKind == JsonValueKind.String) g.Aliases.Add(data.At(n).Str());
                }
                if (sourceRows.Count == 0)
                { g.Identity("vndb", o.At("vndb_id").Str(200)); g.Identity("bangumi", o.At("bgm_id").Str(200)); }
                var rating = custom.At("user_rating").ValueKind == JsonValueKind.Number ? custom.At("user_rating") : o.At("user_rating");
                if (rating.ValueKind == JsonValueKind.Number && rating.GetDouble() > 0) g.Rating = rating.GetDouble();
                g.Installation(o.At("localpath").Str(2048), o.At("executable").Str(2048), o.At("launch_type").Str(100) is "" or "file" or "normal" or "local" ? "file" : "other");
                if (stats.TryGetValue(id, out var st))
                {
                    g.Total = checked(st.At("total_time").Int(1_000_000_000) * 60); g.Count = st.At("session_count").ValueKind == JsonValueKind.Number ? st.At("session_count").Int(1_000_000_000) : null;
                    g.Last = Epoch(st.At("last_played"));
                    if (sessions is not null)
                        foreach (var s in sessions[id])
                        {
                            var start = Epoch(s.At("start_time")); var end = Epoch(s.At("end_time"));
                            if (start is not null && end is not null) g.Session(s.At("session_id").Str(200), start.Value.ToString("O"), end.Value.ToString("O"), checked(s.At("duration").Int(1_000_000_000) * 60));
                            else g.Issue("sessions", "INVALID_SESSION_KEPT_UNDATED");
                        }
                    // daily_stats is derived from the same sessions, not extra time.
                    if (g.Sessions.Count == 0 && st.At("daily_stats").ValueKind == JsonValueKind.String)
                    {
                        var daily = Object(st.At("daily_stats"));
                        foreach (var d in daily.Items())
                            if (DateOnly.TryParseExact(d.At("date").Str(100), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) && d.At("playtime").Int(1440) is > 0 and var minutes)
                                g.Days.Add(new(date, minutes * 60));
                    }
                }
                g.Added = Epoch(o.At("created_at"));
                if (links is not null) foreach (var link in links[id]) if (collections.TryGetValue(link.At("collection_id").Str(), out var category)) g.Categories.Add(category);
                return g.Finish();
            }, token);
        }
        catch (SqliteException e) { token.ThrowIfCancellationRequested(); throw new InvalidDataException("REINA_DATABASE_INVALID", e); }
    }
    private static string First(JsonElement element, params string[] keys) => keys.Select(k => element.At(k).Str()).FirstOrDefault(s => s.Length > 0) ?? "";
    private static JsonElement Object(JsonElement value) => value.ValueKind == JsonValueKind.String && value.Str(1024 * 1024).Length > 0 ? ImportJson.Parse(value.Str(1024 * 1024)) : value.ValueKind == JsonValueKind.String ? default : value;
    private static DateTimeOffset? Epoch(JsonElement value) => value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var n) && n > 0 ? DateTimeOffset.FromUnixTimeSeconds(n) : null;
    private static HashSet<string> Tables(SqliteConnection db)
    {
        using var command = db.CreateCommand(); command.CommandText = "SELECT name FROM sqlite_schema WHERE type='table' LIMIT 201";
        using var reader = command.ExecuteReader(); var tables = new HashSet<string>(StringComparer.Ordinal);
        while (reader.Read()) tables.Add(reader.GetString(0)); if (tables.Count > 200) throw new InvalidDataException("SQLITE_TABLE_LIMIT"); return tables;
    }
    private static List<JsonElement> ReadTable(SqliteConnection db, string table, CancellationToken token)
    {
        if (table is not ("games" or "game_sources" or "game_statistics" or "game_sessions" or "collections" or "game_collection_link")) throw new InvalidDataException();
        using var command = db.CreateCommand(); command.CommandText = "SELECT * FROM \"" + table + "\" LIMIT 100001"; command.CommandTimeout = 3;
        using var reader = command.ExecuteReader(); var result = new List<JsonElement>();
        while (reader.Read())
        {
            token.ThrowIfCancellationRequested(); if (result.Count >= 100000) throw new InvalidDataException("SQLITE_ROW_LIMIT");
            var row = new Dictionary<string, object?>();
            for (var i = 0; i < reader.FieldCount; i++)
            {
                var value = reader.IsDBNull(i) ? null : reader.GetValue(i);
                if (value is byte[] || value is string text && text.Length > 1024 * 1024) throw new InvalidDataException("SQLITE_FIELD_LIMIT");
                row.Add(reader.GetName(i), value);
            }
            result.Add(ImportJson.Parse(JsonSerializer.SerializeToUtf8Bytes(row)));
        }
        return result;
    }
}
