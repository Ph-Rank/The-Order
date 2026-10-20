using System.Collections.Immutable;
using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Text.Json;
using LiteDB;
using Microsoft.Data.Sqlite;
using TheOrder.Application.Migrations;
using TheOrder.Domain;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace TheOrder.Infrastructure.Migration;

public sealed partial class ManagerArchiveWriter
{
    private static void WritePlaynite(ZipArchive zip, ImmutableArray<ImportGame> games, List<ManagerExportNotice> notices, CancellationToken token)
    {
        using var temp = new InterchangeTemp(); var rows = new List<BsonDocument>();
        var categories = games.SelectMany(g => g.Categories).Distinct().ToArray();
        var tags = games.SelectMany(g => g.Tags).Distinct().ToArray();
        BsonValue Date(DateTimeOffset? value) => value is { } d ? new BsonValue(d.UtcDateTime) : BsonValue.Null;
        foreach (var g in games)
        {
            token.ThrowIfCancellationRequested(); var install = g.Installations.FirstOrDefault(); var links = new BsonArray();
            foreach (var id in g.Identities)
            {
                var url = id.Provider switch { "vndb" => "https://vndb.org/" + id.Id, "bangumi" => "https://bgm.tv/subject/" + id.Id, "steam" => "https://store.steampowered.com/app/" + id.Id, _ => "" };
                if (url.Length > 0) links.Add(new BsonDocument { ["Name"] = id.Provider, ["Url"] = url });
            }
            var actions = new BsonArray();
            if (install?.Executable is { } exe) actions.Add(new BsonDocument { ["Type"] = 0, ["IsPlayAction"] = true,
                ["Name"] = "启动", ["Path"] = exe, ["Arguments"] = "", ["WorkingDir"] = install.Folder, ["TrackingMode"] = 0 });
            rows.Add(new BsonDocument { ["_id"] = Guid.Parse(g.SourceId), ["Name"] = g.Name, ["SortingName"] = g.Name,
                ["PluginId"] = Guid.Empty, ["GameId"] = g.SourceId, ["Favorite"] = g.Favorite == true, ["Hidden"] = false,
                ["IsInstalled"] = install is not null, ["InstallDirectory"] = install?.Folder ?? "", ["GameActions"] = actions,
                ["Playtime"] = g.TotalSeconds, ["PlayCount"] = g.PlayCount ?? 0, ["Added"] = Date(g.AddedAt), ["LastActivity"] = Date(g.LastPlayed),
                ["UserScore"] = g.Rating is { } r ? new BsonValue((int)Math.Round(r * 10, MidpointRounding.AwayFromZero)) : BsonValue.Null,
                ["Notes"] = "<pre>" + WebUtility.HtmlEncode(g.Notes) + "</pre>", ["Version"] = g.Edition ?? "", ["Links"] = links,
                ["CompletionStatusId"] = Guid.Parse(Key("status:" + g.Status)),
                ["CategoryIds"] = new BsonArray(g.Categories.Select(c => new BsonValue(Guid.Parse(Key("category:" + c))))),
                ["TagIds"] = new BsonArray(g.Tags.Select(c => new BsonValue(Guid.Parse(Key("tag:" + c))))),
                ["GenreIds"] = KnownGalgame(g) ? new BsonArray { Guid.Parse(Key("visual-novel-genre")) } : new BsonArray() });
            if (!g.Sessions.IsEmpty || !g.Days.IsEmpty) notices.Add(new("history", "Playnite 原生库仅保存累计时长、次数及最近游玩；详细会话与按日记录不导出到插件。", 1));
            if (g.OriginalName.Length > 0 || !g.Aliases.IsEmpty) notices.Add(new("aliases", "Playnite 不支持独立的原名及别名字段。", 1));
        }
        void Save(string file, string collection, IEnumerable<BsonDocument> docs)
        { PlayniteArchive.WriteRaw(Path.Combine(temp.Root, file + ".db"), collection, docs); }
        Save("games", "Game", rows);
        Save("categories", "Category", categories.Select(n => new BsonDocument { ["_id"] = Guid.Parse(Key("category:" + n)), ["Name"] = n }));
        Save("tags", "Tag", tags.Select(n => new BsonDocument { ["_id"] = Guid.Parse(Key("tag:" + n)), ["Name"] = n }));
        Save("genres", "Genre", [new BsonDocument { ["_id"] = Guid.Parse(Key("visual-novel-genre")), ["Name"] = "Visual Novel" }]);
        Save("completionstatuses", "CompletionStatus", GameProgress.Ordered.Select(s => new BsonDocument { ["_id"] = Guid.Parse(Key("status:" + s)), ["Name"] = GameProgress.Label(s), ["IsCompleted"] = s == "completed" }));
        // Playnite 10's restore code tests Windows '\\' separators literally.
        Json(zip, "library\\database.json", new { Version = 4 }); DirectoryMembers(zip, temp.Root, "library", token, backslash: true);
    }
    private static int SqlId(string value) => (int)(BitConverter.ToUInt32(Convert.FromHexString(ImportHash.Text(value)), 0) & 0x7fffffff) is var id && id != 0 ? id : 1;
    private static void WriteReina(ZipArchive zip, ImmutableArray<ImportGame> games, List<ManagerExportNotice> notices, CancellationToken token)
    {
        using var temp = new InterchangeTemp(); var path = Path.Combine(temp.Root, "reina_manager.db");
        using (var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString()))
        {
            db.Open(); using (var schema = db.CreateCommand()) { schema.CommandText = Resource("reinamanager-0.32.0.sql"); schema.ExecuteNonQuery(); }
            using var transaction = db.BeginTransaction();
            void Execute(string sql, params object?[] values)
            {
                token.ThrowIfCancellationRequested(); using var command = db.CreateCommand(); command.Transaction = transaction; command.CommandText = sql;
                for (var i = 0; i < values.Length; i++) command.Parameters.AddWithValue("$p" + i, values[i] ?? DBNull.Value);
                command.ExecuteNonQuery();
            }
            var ids = new HashSet<int>(); var categories = games.SelectMany(g => g.Categories.Concat(g.Favorite == true ? ["收藏"] : [])).Distinct().ToArray(); var categoryIds = new HashSet<int>();
            foreach (var c in categories)
            {
                if (!categoryIds.Add(SqlId(c))) throw new InvalidDataException("EXPORT_CATEGORY_ID_COLLISION");
                Execute("INSERT INTO collections (id,name,sort_order) VALUES ($p0,$p1,0)", SqlId(c), c);
            }
            foreach (var g in games)
            {
                var id = SqlId(g.SourceId); if (!ids.Add(id)) throw new InvalidDataException("EXPORT_GAME_ID_COLLISION");
                var install = g.Installations.FirstOrDefault();
                if (g.TotalSeconds / 60 > int.MaxValue) throw new InvalidDataException("ReinaManager 的累计分钟数超出其格式上限。");
                var custom = JsonSerializer.Serialize(new { name = g.Name, aliases = g.Aliases.Concat(new[] { g.OriginalName }).Where(n => n.Length > 0 && n != g.Name).Distinct(),
                    tags = TypeTags(g), user_rating = g.Rating, user_review = g.Notes });
                Execute("INSERT INTO games (id,id_type,localpath,executable,launch_type,custom_data,clear,created_at,updated_at) VALUES ($p0,'custom',$p1,$p2,'local',$p3,$p4,$p5,$p5)",
                    id, install?.Folder, install?.Executable, custom, g.Status switch { "wish" => 1, "playing" => 2, "completed" => 3, "paused" => 4, "dropped" => 5, _ => 0 }, g.AddedAt?.ToUnixTimeSeconds());
                foreach (var identity in g.Identities.Where(i => i.Provider is "vndb" or "bangumi" or "nextmoe"))
                    Execute("INSERT INTO game_sources (game_id,source,external_id,data) VALUES ($p0,$p1,$p2,$p3)", id,
                        identity.Provider == "bangumi" ? "bgm" : identity.Provider, identity.Id, JsonSerializer.Serialize(new { name = g.OriginalName.Length > 0 ? g.OriginalName : g.Name, name_cn = g.Name, aliases = g.Aliases, tags = KnownGalgame(g) ? new[] { "Galgame" } : [] }));
                foreach (var c in g.Categories.Concat(g.Favorite == true ? ["收藏"] : []).Distinct())
                    Execute("INSERT INTO game_collection_link (game_id,collection_id,sort_order) VALUES ($p0,$p1,0)", id, SqlId(c));
                foreach (var s in g.Sessions.Where(s => s.Seconds >= 60))
                    Execute("INSERT INTO game_sessions (game_id,start_time,end_time,duration,date) VALUES ($p0,$p1,$p2,$p3,$p4)", id, s.Start.ToUnixTimeSeconds(), s.End.ToUnixTimeSeconds(), s.Seconds / 60, s.Start.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                var daily = JsonSerializer.Serialize(g.Days.Where(d => d.Seconds >= 60).Select(d => new { date = d.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), playtime = d.Seconds / 60 }));
                Execute("INSERT INTO game_statistics (game_id,total_time,session_count,last_played,daily_stats) VALUES ($p0,$p1,$p2,$p3,$p4)", id, g.TotalSeconds / 60, g.PlayCount ?? 0, g.LastPlayed?.ToUnixTimeSeconds(), daily);
                if (g.TotalSeconds % 60 != 0 || g.Sessions.Any(s => s.Seconds % 60 != 0) || g.Days.Any(d => d.Seconds % 60 != 0)) notices.Add(new("time", "ReinaManager 按整分钟保存时长，秒数向下取整。", 1));
                if (!g.Days.IsEmpty) notices.Add(new("daily", "按日调整保存在统计字段；ReinaManager 部分活动图仅按真实会话生成，不显示这些调整。", 1));
                if (Id(g, "steam").Length > 0) notices.Add(new("identity", "Steam 编号不会被当作 ReinaManager 的元数据身份或替代本地启动。", 1));
            }
            transaction.Commit();
        }
        Bytes(zip, "reina_manager.db", File.ReadAllBytes(path));
        if (games.Any(g => g.Favorite == true)) notices.Add(new("favorite", "收藏以“收藏”分类保留。", games.Count(g => g.Favorite == true)));
    }
}
