using System.Collections.Immutable;
using System.Globalization;
using System.Text.RegularExpressions;

namespace TheOrder.Domain;

public sealed record ManualPlaytime(string Id, string Date, long Seconds, string Note, string CreatedAt, bool Undone = false);
public enum LaunchOverride { Inherit, Enabled, Disabled }
public sealed record LaunchAuxiliaries(LaunchOverride LocaleEmulator = LaunchOverride.Inherit,
    LaunchOverride Magpie = LaunchOverride.Inherit, LaunchOverride BackgroundMute = LaunchOverride.Inherit);
public sealed record LaunchCompatibility(bool Administrator = false, bool HighDpi = false, string? LocaleEmulator = null, string? ScalingTool = null, bool UseSteam = false, bool MuteInBackground = false)
{
    // Null is a legacy configuration, whose exact behavior is preserved. Only new
    // installations and an explicit reset adopt the current global defaults.
    public LaunchAuxiliaries? Auxiliaries { get; init; }
}
public sealed record MetadataSourceLayer(string Source, string SourceId, WorkMetadata Work, ImmutableArray<string> Fields, bool FillOnly);
public sealed record MetadataMix(WorkMetadata? Base, ImmutableArray<MetadataSourceLayer> Layers);

public static class LibraryExtras
{
    public static readonly ImmutableArray<string> MetadataFields = ["description", "releaseDate", "developers", "tags", "ratings", "characters", "cover"];
    public static void Validate(LibraryEntry entry)
    {
        MigrationLineage.Validate(entry.MigrationOrigins);
        if (entry.GuidePaths.IsDefault || entry.GuidePaths.Length > 20 || entry.GuidePaths.Distinct(StringComparer.OrdinalIgnoreCase).Count() != entry.GuidePaths.Length) throw new InvalidDataException("攻略记录无效。");
        foreach (var guide in entry.GuidePaths) Path(guide);
        if (entry.ManualPlaytime.IsDefault || entry.ManualPlaytime.Length > 10000 || entry.ManualPlaytime.Any(x => x is null) || entry.ManualPlaytime.Select(x => x.Id).Distinct(StringComparer.Ordinal).Count() != entry.ManualPlaytime.Length)
            throw new InvalidDataException("人工记录数量或编号无效。");
        foreach (var r in entry.ManualPlaytime)
            if (!Guid.TryParseExact(r.Id, "D", out _) || !DateOnly.TryParseExact(r.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)
                || Math.Abs((decimal)r.Seconds) > 366L * 86400 || r.Note is null || r.Note.Length > 2000 || r.Note.Contains('\0')
                || !DateTimeOffset.TryParse(r.CreatedAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out _))
                throw new InvalidDataException("人工记录日期、时长或说明无效。");
        if (entry.TotalPlaySeconds is < 0 or > 1_000_000_000_000 || entry.EffectivePlaySeconds is < 0 or > 1_000_000_000_000) throw new InvalidDataException("修正后的累计时长不能为负数或超过上限。");
        foreach (var i in entry.Installations)
        {
            Path(i.SaveFolder);
            if (i.SteamAppId is not null && !Regex.IsMatch(i.SteamAppId, "^[1-9][0-9]{0,9}$")) throw new InvalidDataException("Steam 编号无效。");
            if (i.Compatibility is { } c)
            {
                if (c.Auxiliaries is { } a && (!Enum.IsDefined(a.LocaleEmulator) || !Enum.IsDefined(a.Magpie) || !Enum.IsDefined(a.BackgroundMute)))
                    throw new InvalidDataException("游戏启动覆盖选项无效。");
                Path(c.LocaleEmulator); Path(c.ScalingTool);
                if (c.LocaleEmulator is not null && !System.IO.Path.GetFileName(c.LocaleEmulator).Equals("LEProc.exe", StringComparison.OrdinalIgnoreCase)
                    || c.ScalingTool is not null && !System.IO.Path.GetFileName(c.ScalingTool).Equals("Magpie.exe", StringComparison.OrdinalIgnoreCase)
                    || c.UseSteam && (i.SteamAppId is null || c.Administrator || c.HighDpi || c.LocaleEmulator is not null)
                    || c.Administrator && c.HighDpi)
                    throw new InvalidDataException("启动辅助设置不兼容，或外部工具名称无效。");
            }
        }
        if (entry.MetadataMix is { } mix)
        {
            if (mix.Layers.IsDefault || mix.Layers.Length > 2 || mix.Layers.Any(x => x is null) || mix.Layers.Select(x => x.Source).Distinct().Count() != mix.Layers.Length) throw new InvalidDataException("资料源配置无效。");
            foreach (var layer in mix.Layers)
                if (layer.Source is not ("bangumi" or "vndb") || layer.SourceId is null || !Regex.IsMatch(layer.SourceId, layer.Source == "vndb" ? "^v[1-9][0-9]{0,9}$" : "^[1-9][0-9]{0,9}$")
                    || layer.Work is null || layer.Work.Id != layer.Source + ":" + layer.SourceId || layer.Work.References.Count(r => r.Source == layer.Source && r.Id == layer.SourceId) != 1 || layer.Fields.IsDefault
                    || layer.Fields.Length is 0 || layer.Fields.Length > MetadataFields.Length || layer.Fields.Any(f => !MetadataFields.Contains(f)) || layer.Fields.Distinct().Count() != layer.Fields.Length)
                    throw new InvalidDataException("资料源字段配置无效。");
        }
    }
    public static void Path(string? value)
    {
        if (value is null) return;
        if (value.Length is < 3 or > 32768 || value.Contains('\0') || !System.IO.Path.IsPathFullyQualified(value)) throw new InvalidDataException("请选择完整的本地路径。");
    }
}
