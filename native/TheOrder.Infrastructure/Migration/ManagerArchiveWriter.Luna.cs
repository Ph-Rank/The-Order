using System.Collections.Immutable;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using TheOrder.Application.Migrations;

namespace TheOrder.Infrastructure.Migration;

public sealed partial class ManagerArchiveWriter
{
    private static void WriteLuna(ZipArchive zip, ImmutableArray<ImportGame> games, List<ManagerExportNotice> notices, CancellationToken token)
    {
        var schema = ImportJson.Parse(Resource("lunabox-1.13.3.json"));
        var tables = schema.EnumerateObject().ToDictionary(p => p.Name, _ => new List<IReadOnlyDictionary<string, string?>>(), StringComparer.Ordinal);
        var now = Stamp(DateTimeOffset.UtcNow);
        Dictionary<string, string?> Row(params (string Key, string? Value)[] cells) => cells.ToDictionary(c => c.Key, c => c.Value, StringComparer.Ordinal);
        string Number(long n) => n.ToString(CultureInfo.InvariantCulture);
        foreach (var g in games)
        {
            token.ThrowIfCancellationRequested(); var install = g.Installations.FirstOrDefault();
            var identity = g.Identities.FirstOrDefault(i => i.Provider == "vndb") ?? g.Identities.FirstOrDefault(i => i.Provider == "bangumi");
            var aliases = g.Aliases.Concat(new[] { g.OriginalName }).Where(n => n.Length > 0 && n != g.Name).Distinct().ToArray();
            tables["games"].Add(Row(("id", g.SourceId), ("name", g.Name), ("aliases", JsonSerializer.Serialize(aliases)), ("company", ""), ("summary", ""), ("rating", "0"), ("release_date", ""),
                ("cover_url", ""), ("cover_source_url", ""), ("path", install?.Executable ?? ""), ("game_directory", install?.Folder ?? ""), ("save_path", ""), ("process_name", ""),
                ("wine_runner", ""), ("wine_args", ""), ("wine_prefix", ""), ("launch_mode", "normal"), ("steam_launch_id", ""), ("steam_launch_kind", ""), ("steam_user_id", ""), ("steam_launch_options", ""),
                ("status", g.Status switch { "completed" => "completed", "playing" => "playing", "wish" => "want_to_play", "paused" => "on_hold", "dropped" => "dropped", _ => "not_started" }),
                ("source_type", identity?.Provider ?? "local"), ("source_id", identity?.Id ?? ""), ("cached_at", now), ("created_at", Stamp(g.AddedAt)), ("updated_at", now),
                ("use_locale_emulator", "false"), ("use_magpie", "false"), ("is_nsfw", "false"), ("metadata_locked", "true")));
            foreach (var i in g.Identities.Where(i => i.Provider is "vndb" or "bangumi"))
                tables["game_metadata_sources"].Add(Row(("game_id", g.SourceId), ("source_type", i.Provider), ("source_id", i.Id), ("cached_at", now), ("created_at", now), ("updated_at", now)));
            foreach (var s in g.Sessions)
                tables["play_sessions"].Add(Row(("id", Key(g.SourceId + ":session:" + s.SourceId)), ("game_id", g.SourceId), ("start_time", Stamp(s.Start)), ("end_time", Stamp(s.End)), ("duration", Number(s.Seconds)), ("updated_at", now)));
            if (g.Rating is not null || g.Notes.Length > 0)
                tables["game_reviews"].Add(Row(("game_id", g.SourceId), ("rating", g.Rating is { } r && r >= 1 ? Number((long)Math.Round(r, MidpointRounding.AwayFromZero)) : null),
                    ("content", g.Notes), ("is_spoiler", "false"), ("created_at", now), ("updated_at", now)));
            foreach (var category in g.Categories.Concat(g.Favorite == true ? ["收藏"] : []).Distinct())
                tables["game_categories"].Add(Row(("game_id", g.SourceId), ("category_id", Key(category)), ("updated_at", now)));
            foreach (var tag in TypeTags(g))
                tables["game_tags"].Add(Row(("id", Key(g.SourceId + ":tag:" + tag)), ("game_id", g.SourceId), ("name", tag), ("source", "custom"), ("weight", "1"), ("is_spoiler", "false"), ("created_at", now), ("updated_at", now)));
            if (g.TotalSeconds != g.Sessions.Sum(s => s.Seconds)) notices.Add(new("time", "LunaBox 仅用真实会话计算时长：无日期累计及按日调整保留在补充文件，不伪造会话填补。", 1));
            if (g.PlayCount is { } count && count != g.Sessions.Length) notices.Add(new("count", "LunaBox 从会话统计次数，不能表达独立的历史启动次数。", 1));
            if (g.Rating is { } rating && (rating % 1 != 0 || rating == 0)) notices.Add(new("rating", "LunaBox 使用 1–10 整数评分，四舍五入；0 分留空。", 1));
            if (g.Identities.Any(i => i.Provider is "steam" or "nextmoe")) notices.Add(new("identity", "LunaBox 不支持的外部编号保留在补充文件中。", 1));
        }
        foreach (var c in games.SelectMany(g => g.Categories.Concat(g.Favorite == true ? ["收藏"] : [])).Distinct())
            tables["categories"].Add(Row(("id", Key(c)), ("name", c), ("emoji", ""), ("created_at", now), ("updated_at", now), ("is_system", "false")));
        var ddl = new StringBuilder(); var load = new StringBuilder();
        foreach (var table in schema.EnumerateObject())
        {
            token.ThrowIfCancellationRequested(); var name = table.Name;
            ddl.Append(table.Value.At("sql").Str(100000)).AppendLine(";");
            var columns = table.Value.At("columns").Items().Select(c => c.Str(100)).ToArray();
            Text(zip, "database/" + name + ".csv", InterchangeCsv.Write(columns, tables[name]));
            load.Append("COPY ").Append(name).Append(" FROM '").Append(name).Append(".csv' (FORMAT 'csv', HEADER true, DELIMITER ',', QUOTE '\"', ESCAPE '\"');\n");
        }
        // These migration numbers are from the pinned 1.13.3 upstream runner.
        ddl.AppendLine("CREATE TABLE schema_migrations (version INTEGER PRIMARY KEY, description TEXT, applied_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP);");
        var versions = new[] { 131, 134, 140, 150, 151, 154, 155, 156, 157, 158, 159, 160, 161, 162, 163, 164, 165, 166, 167, 168, 169, 170, 171, 172, 173, 174, 175, 176 };
        Text(zip, "database/schema_migrations.csv", InterchangeCsv.Write(["version", "description", "applied_at"], versions.Select(v => Row(("version", Number(v)), ("description", "official 1.13.3 baseline"), ("applied_at", now)))));
        load.AppendLine("COPY schema_migrations FROM 'schema_migrations.csv' (FORMAT 'csv', HEADER true, DELIMITER ',', QUOTE '\"', ESCAPE '\"');");
        Text(zip, "database/schema.sql", ddl.ToString()); Text(zip, "database/load.sql", load.ToString());
        if (games.Any(g => g.Favorite == true)) notices.Add(new("favorite", "收藏以“收藏”分类保留。", games.Count(g => g.Favorite == true)));
    }
}
