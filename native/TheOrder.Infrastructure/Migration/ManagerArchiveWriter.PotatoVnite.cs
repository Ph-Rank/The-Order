using System.Collections.Immutable;
using System.Globalization;
using System.IO.Compression;
using TheOrder.Application.Migrations;

namespace TheOrder.Infrastructure.Migration;

public sealed partial class ManagerArchiveWriter
{
    private static void WritePotato(ZipArchive zip, ImmutableArray<ImportGame> games, List<ManagerExportNotice> notices, CancellationToken token)
    {
        object Locked(object? value) => new { Value = value, IsLocked = true };
        var rows = new List<object>(); var sources = new List<object>();
        foreach (var g in games)
        {
            token.ThrowIfCancellationRequested();
            var ids = new string?[10]; ids[0] = Id(g, "vndb").TrimStart('v'); ids[1] = Id(g, "bangumi"); ids[7] = Id(g, "steam");
            var played = g.Days.GroupBy(d => d.Date).ToDictionary(d => d.Key.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), d => d.Sum(x => x.Seconds) / 60);
            if (!g.Sessions.IsEmpty) notices.Add(new("sessions", "PotatoVN 兼容格式保留累计时长；真实会话起止记录不能直接迁移。", 1));
            if (g.TotalSeconds % 60 != 0 || g.Days.Any(d => d.Seconds % 60 != 0)) notices.Add(new("time", "PotatoVN 以整分钟保存时长，秒数向下取整。", 1));
            if (g.Rating is { } rating && (rating % 1 != 0 || rating == 0)) notices.Add(new("rating", "PotatoVN 使用整数评分，四舍五入；0 分代表未评分。", 1));
            rows.Add(new
            {
                Uuid = g.SourceId, Name = Locked(g.Name), OriginalName = Locked(g.OriginalName), ChineseName = Locked(""), CnName = "", Ids = ids,
                Tags = Locked(g.Tags), Comment = g.Notes, MyRate = g.Rating is { } r ? (int)Math.Round(r, MidpointRounding.AwayFromZero) : 0,
                PlayType = g.Status switch { "playing" => 1, "completed" => 2, "paused" => 3, "dropped" => 4, "wish" => 5, _ => 0 },
                TotalPlayTime = checked((int)(g.TotalSeconds / 60)), PlayedTime = played, PlayCount = g.PlayCount ?? 0,
                AddTime = Stamp(g.AddedAt), LastPlayTime = Stamp(g.LastPlayed), PreferredInstallationId = g.Installations.IsEmpty ? (string?)null : Key(g.SourceId + ":" + g.Installations[0].SourceId),
                ImagePath = Locked("ms-appx:///Assets/WindowIcon.ico"), HeaderImagePath = Locked(null), Characters = Array.Empty<object>(), Staffs = Array.Empty<object>()
            });
            foreach (var i in g.Installations)
                sources.Add(new
                {
                    SourceType = 1, Path = i.Folder, Url = "local_folder://" + i.Folder, Name = g.Name, ScanOnStart = false, SaveMetaBackup = false,
                    Galgames = new[] { new { Galgame = g.SourceId, EntryId = Key(g.SourceId + ":" + i.SourceId), Path = i.Folder,
                        LocalConfig = new { ExePath = i.Executable, LaunchArguments = "", EnableMagpie = false, EnableLe = false } } }
                });
        }
        var categories = games.SelectMany(g => g.Categories.Concat(g.Favorite == true ? ["收藏"] : []).Distinct().Select(c => (Game: g, Category: c))).GroupBy(x => x.Category);
        Json(zip, "data.galgames.json", rows);
        Json(zip, "data.galgameSources.json", sources);
        Json(zip, "data.categoryGroups.json", new[] { new { Id = Key("potato-group"), Type = 2, Name = "从 The Order 导入", Categories = categories.Select(c => new
            { Id = Key("potato-category:" + c.Key), Name = c.Key, GalgamesX = c.Select(x => x.Game.SourceId).ToArray(), ImagePath = "" }).ToArray() } });
        Json(zip, "data.dataStatus.json", new { LargerFileSeparateUpgraded = true, GalgameSourceFormatUpgrade = true, GalgameSourceAddVirtualSource = true,
            MetaBackupPerSourceUpgrade = true, GalgameSourceRemoveableUpgrade = true, GalgameMultiInstallUpgrade = true,
            CategoryGameIndexUpgrade = true, CategoryAddLastPlayed = true, CategoryAddWantToPlay = true, GalgameDetectedSavePath = true,
            GameLiteDbUpgrade = false, CategoryLiteDbUpgrade = false, SourceLiteDbUpgrade = false, MultiStreamPageLiteDbUpgrade = false,
            ImportGalgame = false, ImportGalgameSource = false, ImportCategory = false, ImportStaff = true, ImportPageSettings = true });
        if (games.Any(g => g.Favorite == true)) notices.Add(new("favorite", "收藏以“收藏”分类保留。", games.Count(g => g.Favorite == true)));
        if (games.Any(g => !g.Categories.IsEmpty)) notices.Add(new("categories", "自定义分类放在“从 The Order 导入”分类组下。", games.Count(g => !g.Categories.IsEmpty)));
    }
    private static void WriteVnite(ZipArchive zip, ImmutableArray<ImportGame> games, List<ManagerExportNotice> notices, CancellationToken token)
    {
        using var temp = new InterchangeTemp();
        var documents = new List<object>(); var locals = new List<object>();
        foreach (var g in games)
        {
            token.ThrowIfCancellationRequested();
            var safeTimers = g.Sessions.All(s => (long)(s.End - s.Start).TotalSeconds == s.Seconds);
            if (!safeTimers) notices.Add(new("sessions", "Vnite 仅按会话起止差统计；含暂停等差异的会话不导出起止，累计时长仍保留。", 1));
            if (g.Status is "wish" or "dropped") notices.Add(new("status", "Vnite 无对应进度状态；以未游玩状态及同名进度分类保留。", 1));
            if (g.PlayCount > 0) notices.Add(new("count", "Vnite 没有独立的历史启动次数字段。", 1));
            var links = new List<object>();
            if (Id(g, "bangumi") is { Length: > 0 } bgm) links.Add(new { label = "Bangumi", url = "https://bgm.tv/subject/" + bgm });
            documents.Add(new
            {
                _id = g.SourceId,
                metadata = new { name = g.Name, originalName = g.OriginalName, sortName = g.Name, releaseDate = "", description = "", developers = Array.Empty<string>(), publishers = Array.Empty<string>(),
                    platforms = new[] { "PC (Windows)" }, genres = KnownGalgame(g) ? new[] { "Visual Novel" } : [], tags = g.Tags, relatedSites = links, steamId = Id(g, "steam"), vndbId = Id(g, "vndb"), igdbId = "", ymgalId = "", extra = Array.Empty<object>() },
                record = new { addDate = Stamp(g.AddedAt), lastRunDate = Stamp(g.LastPlayed), score = g.Rating ?? -1, playTime = checked(g.TotalSeconds * 1000),
                    playStatus = g.Status switch { "playing" => "playing", "completed" => "finished", "paused" => "shelved", _ => "unplayed" }, hideFromRecentGames = false,
                    timers = (safeTimers ? g.Sessions : []).Select(s => new { start = Stamp(s.Start), end = Stamp(s.End) }).ToArray(),
                    dailyPlayTimes = g.Days.Select(d => new { date = d.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), playTime = checked(d.Seconds * 1000) }).ToArray(), storageSize = -1 },
                save = new { saveList = new Dictionary<string, object>(), maxBackups = 7, autoBackupSave = false, autoRestoreSave = false },
                memory = new { preferences = new { viewMode = (string?)null }, memoryList = g.Notes.Length == 0 ? new Dictionary<string, object>() : new Dictionary<string, object>
                    { [Key(g.SourceId + ":note")] = new { _id = Key(g.SourceId + ":note"), date = Stamp(g.AddedAt), note = g.Notes, pinned = false } } },
                apperance = new { logo = new { position = new { x = 1.5, y = 35 }, size = 100, visible = true }, nsfw = false }
            });
            var install = g.Installations.FirstOrDefault();
            locals.Add(new { _id = g.SourceId, path = new { gamePath = install?.Folder ?? "", savePaths = Array.Empty<string>(), screenshotPath = "" },
                launcher = new { mode = "file", fileConfig = new { path = install?.Executable ?? "", args = Array.Empty<string>(), monitorMode = "folder", monitorPath = "" },
                    urlConfig = new { url = "", browserPath = "", monitorMode = "folder", monitorPath = "" }, scriptConfig = new { workingDirectory = "", command = Array.Empty<string>(), monitorMode = "folder", monitorPath = "" }, useMagpie = false },
                utils = new { markPath = "", rootPath = "" } });
        }
        VniteArchive.WriteDatabase(Path.Combine(temp.Root, "game"), documents);
        VniteArchive.WriteDatabase(Path.Combine(temp.Root, "game-local"), locals);
        var categories = games.SelectMany(g => g.Categories.Concat(g.Favorite == true ? ["收藏"] : []).Concat(g.Status is "wish" or "dropped" ? [TheOrder.Domain.GameProgress.Label(g.Status)] : []).Distinct().Select(c => (Game: g, Category: c))).GroupBy(x => x.Category);
        VniteArchive.WriteDatabase(Path.Combine(temp.Root, "game-collection"), categories.Select(c => new { _id = Key(c.Key), name = c.Key, sort = 0, sortBy = "custom", sortOrder = "asc", secondarySort = (object?)null, games = c.Select(x => x.Game.SourceId).ToArray() }));
        foreach (var name in new[] { "game", "game-local", "game-collection" }) DirectoryMembers(zip, Path.Combine(temp.Root, name), name, token, levelDbSnapshot: true);
        if (games.Any(g => g.Favorite == true)) notices.Add(new("favorite", "收藏以“收藏”分类保留。", games.Count(g => g.Favorite == true)));
    }
}
