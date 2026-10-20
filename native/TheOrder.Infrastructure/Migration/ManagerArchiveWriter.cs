using System.Collections.Immutable;
using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using TheOrder.Application.Migrations;
using TheOrder.Domain;

namespace TheOrder.Infrastructure.Migration;

public sealed partial class ManagerArchiveWriter : IManagerArchiveWriter
{
    public Task<ManagerExportPackage> CreateAsync(string manager, ImmutableArray<LibraryEntry> entries, CancellationToken cancellation)
        => Task.Run(() => Build(manager, entries, cancellation), cancellation);
    private static ManagerExportPackage Build(string manager, ImmutableArray<LibraryEntry> entries, CancellationToken token)
    {
        if (!MigrationFormats.Valid(manager, ImportRecord.ManagerFormat(manager)) || entries.IsDefault || entries.Length > 10000)
            throw new InvalidDataException("UNSUPPORTED_EXPORT_TARGET");
        if (entries.Any(e => e.Sessions.Any(s => s.Native is { IsTerminal: false }))) throw new InvalidDataException("请先结束计时，再导出兼容备份。");
        var notices = new List<ManagerExportNotice>();
        var rows = entries.Select(e => Project(e, manager, notices)).ToImmutableArray();
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true))
        {
            switch (manager)
            {
                case "potatovn": WritePotato(zip, rows, notices, token); break;
                case "vnite": WriteVnite(zip, rows, notices, token); break;
                case "playnite": WritePlaynite(zip, rows, notices, token); break;
                case "reinamanager": WriteReina(zip, rows, notices, token); break;
                case "lunabox": WriteLuna(zip, rows, notices, token); break;
                default: throw new InvalidDataException("UNSUPPORTED_EXPORT_TARGET");
            }
            var summary = notices.GroupBy(n => (n.Field, n.Message)).Select(g => new ManagerExportNotice(g.Key.Field, g.Key.Message, g.Sum(n => n.Count))).ToImmutableArray();
            Json(zip, "the-order-export-report.json", new { schema = 1, manager, entries = rows.Length, notices = summary,
                omitted = new[] { "credentials", "cookies", "plugins", "scripts", "game files", "save files", "image bytes" } });
            // Foreign programs ignore this file. It retains facts their own schema
            // cannot express, without fabricating dates, sessions or unsupported IDs.
            Json(zip, "the-order-supplement.json", new { schema = 1, manager, games = rows });
            var instruction = manager == "reinamanager" ? "解压后，在 ReinaManager 的数据库导入中选择 reina_manager.db。"
                : manager == "potatovn" ? "在 PotatoVN 设置的数据导入中选择本 ZIP（可改名为 .pvnExport.zip）。" : manager == "vnite" ? "在 Vnite 设置的数据库恢复中选择本 ZIP。"
                : manager == "playnite" ? "在 Playnite 的备份恢复中选择本 ZIP，仅勾选游戏库。" : "在 LunaBox 的数据库备份恢复中选择本 ZIP。";
            Text(zip, "The-Order-导入说明.txt", $"The Order → {MigrationFormats.Display(manager)}\n\n{instruction}\n\n请先备份目标管理器。恢复功能通常替换目标库，并非合并；本包不包含账号或配置。\n"
                + "原始 The Order 备份导出仍是完整保真的方式，兼容 ZIP 不能代替它。不同管理器不能表达的字段与精度差异如下：\n"
                + string.Join("\n", summary.Select(n => $"- {n.Message}（{n.Count} 项）")));
        }
        token.ThrowIfCancellationRequested();
        if (output.Length > 64 * 1024 * 1024) throw new InvalidDataException("EXPORT_ARCHIVE_LIMIT");
        return new(manager, entries.Length, output.ToArray(), [.. notices.GroupBy(n => (n.Field, n.Message)).Select(g => new ManagerExportNotice(g.Key.Field, g.Key.Message, g.Sum(n => n.Count)))]);
    }
    private static ImportGame Project(LibraryEntry e, string manager, List<ManagerExportNotice> notices)
    {
        LibraryExtras.Validate(e);
        var ids = MigrationResolver.Identities(e).GroupBy(i => i.Provider).Where(g => g.Distinct().Count() == 1).Select(g => g.First()).ToImmutableArray();
        var original = e.Metadata?.OriginalTitle is { Length: > 0 } title ? title : e.MigrationOrigins.Select(o => o.OriginalTitle).FirstOrDefault(t => t.Length > 0) ?? "";
        var aliases = (e.Metadata?.Aliases ?? []).Concat(e.MigrationOrigins.SelectMany(o => o.Aliases)).Distinct().Take(100).ToImmutableArray();
        var g = new ImportGame(ImportHash.GuidFor("the-order:" + e.Id), e.Name, original, aliases, ids,
            [.. e.Installations.Select(i => new ImportInstallation(i.Id, i.Folder, ImportPaths.Executable(i.Folder, i.Executable)))],
            e.Categories, e.CustomTags, e.Status, e.PersonalRating, e.Favorite, e.Notes, e.EffectivePlaySeconds, e.PlayCount,
            [.. e.ManualPlaytime.Where(m => !m.Undone && m.Seconds > 0).GroupBy(m => m.Date).Select(d => new ImportDay(DateOnly.ParseExact(d.Key, "yyyy-MM-dd", CultureInfo.InvariantCulture), d.Sum(m => m.Seconds)))],
            [.. e.Sessions.Where(s => s.Seconds > 0).Select(s => new ImportSession(s.Id, DateTimeOffset.Parse(s.StartedAt, CultureInfo.InvariantCulture), DateTimeOffset.Parse(s.EndedAt, CultureInfo.InvariantCulture), s.Seconds))],
            e.LastPlayedAt is null ? null : DateTimeOffset.Parse(e.LastPlayedAt, CultureInfo.InvariantCulture), DateTimeOffset.Parse(e.AddedAt, CultureInfo.InvariantCulture), null, null,
            e.MigrationOrigins.Select(o => o.Edition).FirstOrDefault(x => x is not null), []);
        if (g.Installations.Length > 1 && manager != "potatovn") notices.Add(new("installations", "目标格式仅采用首个安装；其余安装保留在 The Order 原生备份中。", 1));
        if (e.ManualPlaytime.Any(m => !m.Undone && m.Seconds < 0) || g.Days.Sum(d => d.Seconds) + g.Sessions.Sum(s => s.Seconds) > g.TotalSeconds
            || g.Days.Any(d => d.Seconds > 86400) || g.Sessions.OrderBy(s => s.Start).Zip(g.Sessions.OrderBy(s => s.Start).Skip(1)).Any(p => p.Second.Start < p.First.End))
        { g = g with { Days = [], Sessions = [] }; notices.Add(new("history", "时长含修正或重叠记录：仅导出有效累计，不伪造逐日记录或会话。", 1)); }
        if (e.Metadata is not null) notices.Add(new("metadata", "仅导出名称、原名、别名和可靠外部编号；封面、角色、制作人员及在线确认关系不迁移。", 1));
        if (e.Installations.Any(i => !i.Arguments.IsEmpty || i.Compatibility is not null)) notices.Add(new("launch", "启动参数、转区、脚本及插件设置不迁移；仅保留安全的程序路径。", 1));
        return g;
    }
    private static string Id(ImportGame g, string provider) => g.Identities.FirstOrDefault(i => i.Provider == provider)?.Id ?? "";
    private static bool KnownGalgame(ImportGame g) => g.Identities.Any(i => i.Provider is "vndb" or "nextmoe")
        || g.Tags.Concat(g.Categories).Any(t => t.Trim().ToLowerInvariant() is "galgame" or "visual novel" or "visual novels" or "美少女游戏" or "视觉小说" or "ビジュアルノベル");
    private static IEnumerable<string> TypeTags(ImportGame g) => g.Tags.Concat(KnownGalgame(g) ? ["Galgame"] : []).Distinct();
    private static string Stamp(DateTimeOffset? time) => time?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture) ?? "";
    private static string Key(string value) => ImportHash.GuidFor("the-order-export:" + value);
    private static void Json(ZipArchive zip, string name, object value) => Bytes(zip, name, JsonSerializer.SerializeToUtf8Bytes(value));
    private static void Text(ZipArchive zip, string name, string value) => Bytes(zip, name, Encoding.UTF8.GetBytes(value));
    private static void Bytes(ZipArchive zip, string name, ReadOnlySpan<byte> value)
    { if (value.Length > 64 * 1024 * 1024) throw new InvalidDataException("EXPORT_MEMBER_LIMIT"); using var file = zip.CreateEntry(name, CompressionLevel.Optimal).Open(); file.Write(value); }
    private static void DirectoryMembers(ZipArchive zip, string folder, string prefix, CancellationToken token, bool backslash = false, bool levelDbSnapshot = false)
    {
        foreach (var path in Directory.EnumerateFiles(folder))
        {
            token.ThrowIfCancellationRequested(); var file = Path.GetFileName(path);
            // LOG/LOCK are process diagnostics and locks, not snapshot data. The
            // native logger may retain a handle briefly after closing the DB.
            if (levelDbSnapshot && !System.Text.RegularExpressions.Regex.IsMatch(file, @"^(CURRENT|MANIFEST-[0-9]{1,12}|[0-9]{1,12}\.(ldb|sst|log))$",
                System.Text.RegularExpressions.RegexOptions.CultureInvariant)) continue;
            var name = prefix + "/" + file;
            Bytes(zip, backslash ? name.Replace('/', '\\') : name, File.ReadAllBytes(path));
        }
    }
    private static string Resource(string filename)
    {
        var assembly = typeof(ManagerArchiveWriter).Assembly;
        using var input = assembly.GetManifestResourceStream(assembly.GetManifestResourceNames().Single(n => n.EndsWith(".Migration.Schemas." + filename, StringComparison.Ordinal)))!;
        using var reader = new StreamReader(input, Encoding.UTF8, true); return reader.ReadToEnd();
    }
}
