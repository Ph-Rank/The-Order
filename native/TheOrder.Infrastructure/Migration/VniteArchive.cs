using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using TheOrder.Application.Migrations;

namespace TheOrder.Infrastructure.Migration;

internal static class VniteArchive
{
    private const string Doc = "ÿdocument-storeÿ", Seq = "ÿby-sequenceÿ", Meta = "ÿmeta-storeÿ";
    internal static ImportDataset Read(InterchangeArchive archive, string digest, CancellationToken cancellation)
    {
        using var temp = new InterchangeTemp();
        var games = ReadDatabase(archive, "game", temp.Root, cancellation);
        var locals = archive.Has("game-local/CURRENT") ? ReadDatabase(archive, "game-local", temp.Root, cancellation) : [];
        var collections = archive.Has("game-collection/CURRENT") ? ReadDatabase(archive, "game-collection", temp.Root, cancellation) : [];
        return ImportRecord.Dataset("vnite", digest, games.Values, o =>
        {
            var m = o.At("metadata"); var r = o.At("record"); var id = o.At("_id").Str(200);
            if (id.Length == 0 || m.ValueKind != JsonValueKind.Object || r.ValueKind != JsonValueKind.Object) throw new InvalidDataException("VNITE_GAME_LAYOUT_UNSUPPORTED");
            var g = new ImportRecord("vnite", id, m.At("name").Str()) { Original = m.At("originalName").Str(),
                Total = Milliseconds(r.At("playTime")), Status = r.At("playStatus").Str() switch
                { "finished" or "multiple" => "completed", "playing" or "partial" => "playing", "shelved" => "paused", "unplayed" or "" => "planned", _ => throw new InvalidDataException("UNKNOWN_PROGRESS") } };
            g.Identity("vndb", m.At("vndbId").Str(200)); g.Identity("steam", m.At("steamId").Str(200));
            foreach (var link in m.At("relatedSites").Items()) ManagerLinks.CollectIdentity(g, link.At("url").Str(2048));
            g.Tags.AddRange(m.At("tags").Items().Concat(m.At("genres").Items()).Select(t => t.Str(200)));
            g.Rating = r.At("score").ValueKind == JsonValueKind.Number && r.At("score").GetDouble() >= 0 ? r.At("score").GetDouble() : null;
            var added = r.At("addDate").Str(100); var last = r.At("lastRunDate").Str(100);
            g.Added = ImportRecord.Timestamp(added); g.Last = ImportRecord.Timestamp(last); g.AddedLocal = g.Added is null ? added : null; g.LastLocal = g.Last is null ? last : null;
            foreach (var s in r.At("timers").Items()) g.Session("timer-" + g.Sessions.Count, s.At("start").Str(100), s.At("end").Str(100));
            foreach (var d in r.At("dailyPlayTimes").Items())
            {
                var seconds = Milliseconds(d.At("playTime"));
                if (!DateOnly.TryParseExact(d.At("date").Str(100), "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var date) || seconds > 86400)
                    throw new InvalidDataException("INVALID_DAILY_TIME");
                if (seconds > 0) g.Days.Add(new(date, seconds));
            }
            if (locals.TryGetValue(id, out var l))
            {
                var launcher = l.At("launcher"); var mode = launcher.At("mode").Str();
                g.Installation(l.At("path").At("gamePath").Str(2048), launcher.At("fileConfig").At("path").Str(2048), mode);
                if (mode is "script" or "url" || launcher.At("fileConfig").At("args").Items().Any()) g.Issue("launch", "CUSTOM_LAUNCH_CONFIGURATION_NOT_IMPORTED");
            }
            foreach (var c in collections.Values)
                if (c.At("games").Items().Any(i => i.Str(200) == id)) g.Categories.Add(c.At("name").Str(200));
            var notes = o.At("memory").At("memoryList");
            if (notes.ValueKind == JsonValueKind.Object)
                g.Notes = string.Join("\n\n", notes.EnumerateObject().Select(n => n.Value.At("note").Str(20000)).Where(n => n.Length > 0));
            return g.Finish();
        }, cancellation);
    }
    private static long Milliseconds(JsonElement value) => value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null ? 0
        : value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var n) && double.IsFinite(n) && n >= 0 && n <= 1_000_000_000_000_000 ? checked((long)Math.Floor(n / 1000)) : throw new InvalidDataException("INVALID_MILLISECONDS");

    internal static Dictionary<string, JsonElement> ReadDatabase(InterchangeArchive archive, string name, string temp, CancellationToken token)
    {
        var current = archive.Text(name + "/CURRENT");
        if (!Regex.IsMatch(current, "^MANIFEST-[0-9]{1,12}\\n$", RegexOptions.CultureInvariant) || !archive.Has(name + "/" + current.Trim()))
            throw new InvalidDataException("UNSAFE_OR_INCOMPLETE_LEVELDB_MANIFEST");
        var folder = Path.Combine(temp, name); Directory.CreateDirectory(folder); long bytes = 0;
        foreach (var member in archive.Entries.Keys.Where(n => n.StartsWith(name + "/", StringComparison.OrdinalIgnoreCase)))
        {
            token.ThrowIfCancellationRequested(); var file = member[(name.Length + 1)..];
            if (!Regex.IsMatch(file, @"^(CURRENT|MANIFEST-[0-9]{1,12}|[0-9]{1,12}\.(ldb|sst|log))$", RegexOptions.CultureInvariant)) continue;
            var value = archive.Read(member, 64 * 1024 * 1024); bytes += value.Length;
            if (bytes > 128L * 1024 * 1024) throw new InvalidDataException("LEVELDB_SNAPSHOT_LIMIT");
            File.WriteAllBytes(Path.Combine(folder, file), value);
        }
        using var options = new LevelDB.Options { CreateIfMissing = false, ParanoidChecks = true, MaxOpenFiles = 32, WriteBufferSize = 1024 * 1024 };
        using var db = new LevelDB.DB(options, folder);
        using var read = new LevelDB.ReadOptions { VerifyCheckSums = true, FillCache = false };
        using var iterator = db.CreateIterator(read); iterator.Seek(Doc);
        var result = new Dictionary<string, JsonElement>(StringComparer.Ordinal); var active = 0;
        while (iterator.IsValid())
        {
            token.ThrowIfCancellationRequested(); var key = iterator.KeyAsString(); if (!key.StartsWith(Doc, StringComparison.Ordinal)) break;
            var data = iterator.Value(); if (data.Length > 1024 * 1024) throw new InvalidDataException("LEVELDB_DOCUMENT_LIMIT");
            var metadata = ImportJson.Parse(data); var id = metadata.At("id").Str(200); var rev = metadata.At("winningRev").Str(200);
            if (rev.Length == 0) rev = metadata.At("rev").Str(200);
            if (metadata.At("deleted").ValueKind != JsonValueKind.True)
            {
                active++;
                var seq = metadata.At("rev_map").At(rev).Int();
                if (seq == 0) throw new InvalidDataException("LEVELDB_WINNING_REVISION_MISSING");
                var body = db.Get(Seq + seq.ToString("D16", System.Globalization.CultureInfo.InvariantCulture));
                if (body is null || Encoding.UTF8.GetByteCount(body) > 1024 * 1024) throw new InvalidDataException("LEVELDB_DOCUMENT_MISSING");
                var json = ImportJson.Parse(body);
                if (json.At("_id").Str(200) != id || json.At("_rev").Str(200) != rev || !id.StartsWith('_') && !result.TryAdd(id, json)) throw new InvalidDataException("LEVELDB_REVISION_BINDING_INVALID");
                if (result.Count > 10000) throw new InvalidDataException("SOURCE_GAME_LIMIT");
            }
            iterator.Next();
        }
        if (!int.TryParse(db.Get(Meta + "_local_doc_count"), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var expected)
            || expected != active) throw new InvalidDataException("LEVELDB_DOCUMENT_COUNT_MISMATCH");
        return result;
    }
    internal static void WriteDatabase(string folder, IEnumerable<object> documents)
    {
        using var options = new LevelDB.Options { CreateIfMissing = true, ErrorIfExists = true, ParanoidChecks = true };
        using var db = new LevelDB.DB(options, folder); var seq = 0;
        foreach (var value in documents)
        {
            var doc = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(value))!.AsObject(); var id = doc["_id"]!.GetValue<string>();
            var hash = TheOrder.Application.Migrations.ImportHash.Of(value)[..32]; var rev = "1-" + hash; doc["_rev"] = rev; seq++;
            db.Put(Seq + seq.ToString("D16", System.Globalization.CultureInfo.InvariantCulture), doc.ToJsonString());
            db.Put(Doc + id, JsonSerializer.Serialize(new { id, rev, winningRev = rev, deleted = false, seq,
                rev_map = new Dictionary<string, int> { [rev] = seq }, rev_tree = new[] { new { pos = 1, ids = new object[] { hash, new { status = "available" }, Array.Empty<object>() } } } }));
        }
        db.Put(Meta + "_local_doc_count", seq.ToString(System.Globalization.CultureInfo.InvariantCulture));
        db.Put(Meta + "_local_last_update_seq", seq.ToString(System.Globalization.CultureInfo.InvariantCulture));
        db.Put(Meta + "_local_uuid", JsonSerializer.Serialize(Guid.NewGuid().ToString("D")));
    }
}

internal static class ManagerLinks
{
    internal static void CollectIdentity(ImportRecord game, string raw)
    {
        if (!Uri.TryCreate(raw, UriKind.Absolute, out var url) || url.Scheme != "https" || url.UserInfo.Length > 0 || !url.IsDefaultPort) return;
        var path = url.AbsolutePath.Trim('/');
        if (url.Host is "vndb.org" or "www.vndb.org" && Regex.IsMatch(path, "^v[1-9][0-9]{0,9}$", RegexOptions.CultureInvariant)) game.Identity("vndb", path);
        if (url.Host is "bgm.tv" or "bangumi.tv" or "chii.in" && Regex.IsMatch(path, "^subject/[1-9][0-9]{0,9}$", RegexOptions.CultureInvariant)) game.Identity("bangumi", path[8..]);
        if (url.Host == "store.steampowered.com" && Regex.Match(path, "^app/([1-9][0-9]{0,9})(/|$)", RegexOptions.CultureInvariant) is { Success: true } match) game.Identity("steam", match.Groups[1].Value);
    }
}
