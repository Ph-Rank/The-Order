using System.Collections.Immutable;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using LiteDB;
using TheOrder.Application.Migrations;

namespace TheOrder.Infrastructure.Migration;

// Playnite 10.62 uses LiteDB 4, unlike the unreleased file layout on master.
// Only raw BSON is read. Never deserialize a source document into a CLR type:
// that would expose LiteDB 4's vulnerable _type / BsonMapper activation path.
internal static class PlayniteArchive
{
    internal static ImportDataset Read(InterchangeArchive archive, string digest, CancellationToken token)
    {
        if (archive.Json("library/database.json").At("Version").Int(100) != 4) throw new InvalidDataException("PLAYNITE_DATABASE_VERSION_UNSUPPORTED");
        using var temp = new InterchangeTemp();
        var games = Raw(archive, "games", "Game", temp, token);
        var categories = Names(archive, "categories", "Category", temp, token);
        var tags = Names(archive, "tags", "Tag", temp, token);
        var genres = Names(archive, "genres", "Genre", temp, token);
        var statuses = Raw(archive, "completionstatuses", "CompletionStatus", temp, token)
            .ToDictionary(s => s.At("_id").Str(200), StringComparer.OrdinalIgnoreCase);
        return ImportRecord.Dataset("playnite", digest, games, o =>
        {
            var id = o.At("_id").Str(200); if (!Guid.TryParse(id, out _)) throw new InvalidDataException("PLAYNITE_GAME_ID_INVALID");
            var g = new ImportRecord("playnite", id, o.At("Name").Str()) { Total = o.At("Playtime").Int(),
                Notes = PlainNotes(o.At("Notes").Str(20000)), Favorite = o.At("Favorite").ValueKind == JsonValueKind.True,
                Added = ImportRecord.Timestamp(o.At("Added").Str(100)), Last = ImportRecord.Timestamp(o.At("LastActivity").Str(100)) };
            if (o.At("PlayCount").ValueKind == JsonValueKind.Number) g.Count = o.At("PlayCount").Int(1_000_000_000);
            if (o.At("UserScore").ValueKind == JsonValueKind.Number) g.Rating = o.At("UserScore").Int(100) / 10d;
            foreach (var link in o.At("Links").Items()) ManagerLinks.CollectIdentity(g, link.At("Url").Str(2048));
            g.Categories.AddRange(Lookup(o.At("CategoryIds"), categories)); g.Tags.AddRange(Lookup(o.At("TagIds"), tags)); g.Tags.AddRange(Lookup(o.At("GenreIds"), genres));
            if (statuses.TryGetValue(o.At("CompletionStatusId").Str(200), out var s))
            {
                var n = s.At("Name").Str().Trim().ToLowerInvariant();
                g.Status = s.At("IsCompleted").ValueKind == JsonValueKind.True ? "completed" : n switch
                {
                    "completed" or "played" or "beaten" or "100%" or "已完成" or "已通关" or "玩过" => "completed",
                    "playing" or "in progress" or "在玩" or "游玩中" => "playing",
                    "plan to play" or "wishlist" or "想玩" => "wish",
                    "on hold" or "paused" or "搁置" => "paused",
                    "abandoned" or "dropped" or "放弃" or "抛弃" => "dropped",
                    "unplayed" or "not played" or "未游玩" or "未设置" or "未开始" => "planned",
                    _ => "planned"
                };
                if (g.Status == "planned" && n is not ("unplayed" or "not played" or "未游玩" or "未设置" or "未开始")) g.Issue("status", "CUSTOM_COMPLETION_STATUS_NOT_MAPPED");
            }
            var folder = o.At("InstallDirectory").Str(2048);
            var actions = o.At("GameActions").Items().Where(a => a.At("IsPlayAction").ValueKind == JsonValueKind.True && a.At("Type").Int(10) == 0).ToArray();
            var exe = actions.Length == 1 ? actions[0].At("Path").Str(2048).Replace("{InstallDir}", folder, StringComparison.OrdinalIgnoreCase) : "";
            g.Installation(folder, exe);
            if (actions.Any(a => a.At("Arguments").Str(10000).Length > 0) || o.At("PreScript").Str(20000).Length > 0 || o.At("PostScript").Str(20000).Length > 0)
                g.Issue("launch", "SCRIPTS_AND_ARGUMENTS_NOT_IMPORTED");
            return g.Finish();
        }, token);
    }
    private static IEnumerable<string> Lookup(JsonElement ids, Dictionary<string, string> names) => ids.Items().Select(i => names.GetValueOrDefault(i.Str(200))).OfType<string>();
    private static Dictionary<string, string> Names(InterchangeArchive a, string file, string collection, InterchangeTemp temp, CancellationToken token)
        => Raw(a, file, collection, temp, token).ToDictionary(d => d.At("_id").Str(200), d => d.At("Name").Str(200), StringComparer.OrdinalIgnoreCase);
    private static JsonElement[] Raw(InterchangeArchive a, string file, string collection, InterchangeTemp temp, CancellationToken token)
    {
        var name = "library/" + file + ".db"; if (!a.Has(name)) return [];
        var path = temp.Write(a.Read(name, 64 * 1024 * 1024), file + ".db");
        try
        {
            // LiteDB v4 has no read-only mode. It is opened only on our disposable
            // copy, with upgrades/repair disabled; the original ZIP stays pinned read-only.
            using var db = new LiteDatabase(new ConnectionString { Filename = path, Mode = LiteDB.FileMode.Exclusive, Journal = false, CacheSize = 16, UtcDate = true, Upgrade = false });
            var actual = db.GetCollectionNames().SingleOrDefault(c => c.Equals(collection, StringComparison.OrdinalIgnoreCase));
            if (actual is null) throw new InvalidDataException("PLAYNITE_COLLECTION_UNSUPPORTED");
            var result = new List<JsonElement>();
            foreach (var doc in db.GetCollection(actual).FindAll())
            {
                token.ThrowIfCancellationRequested();
                // Raw dictionary conversion only; no BsonMapper.ToObject/Deserialize.
                var bytes = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(RawValue(doc, 0));
                if (bytes.Length > 1024 * 1024 || result.Count >= 10000) throw new InvalidDataException("PLAYNITE_RECORD_LIMIT");
                result.Add(ImportJson.Parse(bytes));
            }
            return result.ToArray();
        }
        catch (LiteException e) { throw new InvalidDataException("PLAYNITE_DATABASE_INVALID", e); }
    }
    private static object? RawValue(BsonValue value, int depth)
    {
        if (depth > 32) throw new InvalidDataException("BSON_DEPTH_LIMIT");
        if (value.IsDocument)
        {
            var doc = value.AsDocument;
            if (doc.Keys.Any(k => k is "_type" or "$type" or "$values")) throw new InvalidDataException("TYPED_BSON_FORBIDDEN");
            return doc.ToDictionary(p => p.Key, p => RawValue(p.Value, depth + 1), StringComparer.Ordinal);
        }
        if (value.IsArray) return value.AsArray.Select(v => RawValue(v, depth + 1)).ToArray();
        if (value.IsGuid) return value.AsGuid.ToString("D");
        if (value.IsDateTime) return value.AsDateTime.ToUniversalTime().ToString("O", System.Globalization.CultureInfo.InvariantCulture);
        if (value.IsString) { if (value.AsString.Length > 100000) throw new InvalidDataException("BSON_FIELD_LIMIT"); return value.AsString; }
        if (value.IsInt32) return value.AsInt32;
        if (value.IsInt64) return value.AsInt64;
        if (value.IsDouble) return value.AsDouble;
        if (value.IsDecimal) return value.AsDecimal;
        if (value.IsBoolean) return value.AsBoolean;
        if (value.IsNull) return null;
        throw new InvalidDataException("BSON_VALUE_UNSUPPORTED");
    }
    internal static string PlainNotes(string value) => WebUtility.HtmlDecode(Regex.Replace(Regex.Replace(value,
        @"<(br\s*/?|/p|/div|/li)>" , "\n", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking),
        "<[^>]*>", "", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)).Trim();
    internal static void WriteRaw(string path, string collection, IEnumerable<BsonDocument> documents)
    {
        using var db = new LiteDatabase(path); var rows = db.GetCollection(collection);
        rows.EnsureIndex("Name"); // Materialize empty collections for an empty-library backup.
        foreach (var document in documents) rows.Insert(document);
    }
}
