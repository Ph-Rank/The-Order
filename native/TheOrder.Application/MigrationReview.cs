using System.Collections.Immutable;
using System.Text.RegularExpressions;
using TheOrder.Domain;

namespace TheOrder.Application.Migrations;

public static class MigrationReview
{
    public static bool CanUseSourceTotal(LibraryEntry target, long sourceSeconds)
    {
        var total = sourceSeconds - target.ManualPlaytime.Where(x => !x.Undone).Sum(x => x.Seconds);
        return total >= target.Sessions.Sum(x => x.Seconds) && total is >= 0 and <= 1_000_000_000_000;
    }
    internal static ImportReviewCandidate Candidate(LibraryEntry target, ImportGame source) => new(
        ImportHash.Text(target.Id)[..20], target.Name, target.Metadata?.Title ?? "", target.Status, target.EffectivePlaySeconds,
        MigrationResolver.Identities(target), [.. target.Installations.Take(6).Select(i => i.Folder)],
        !source.Installations.IsEmpty && source.Installations.All(i => target.Installations.Any(x => ImportPaths.Key(x.Folder) == ImportPaths.Key(i.Folder))),
        CanUseSourceTotal(target, source.TotalSeconds),
        target.Notes.Length > 0 && source.Notes.Length > 0 && target.Notes != source.Notes,
        target.PersonalRating is { } a && source.Rating is { } b && a != b, target.Installations.Length);

    // A matching Steam app is positive evidence. Other URLs for the same work
    // (including its trial) do not contradict that exact match. Disjoint app IDs
    // and conflicting single-work providers still require human verification.
    internal static bool ConflictingIds(ImportGame source, ImmutableArray<ImportedIdentity> target)
        => source.Identities.Any(i => i.Provider == "steam"
            ? target.Any(x => x.Provider == i.Provider) && !target.Contains(i)
            : target.Any(x => x.Provider == i.Provider && x.Id != i.Id));

    internal static bool EditionDifference(ImportGame source, LibraryEntry target)
    {
        if (target.MigrationOrigins.Any(o => o.Edition is not null && source.Edition is not null && o.Edition != source.Edition)) return true;
        var left = Editions(string.Join(" ", new[] { source.Name, source.OriginalName }.Concat(source.Aliases)));
        var right = Editions(string.Join(" ", new[] { target.Name, target.Metadata?.Title ?? "", target.Metadata?.OriginalTitle ?? "" }.Concat(target.Metadata?.Aliases ?? [])));
        return !left.SetEquals(right);
    }
    private static HashSet<string> Editions(string title) => Regex.Matches(title,
        @"\b(remake|remaster(?:ed)?|qhd|anniversary|reflection\s*blue|re\.?\s*boot|hd\s+edition|complete\s+edition|demo|trial(?:\s+version)?|fan\s*disc)\b|重制版|重製版|高清版|リメイク|体験版|试玩版|試玩版",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant).Select(x => Edition(x.Value)).ToHashSet(StringComparer.Ordinal);
    private static string Edition(string value)
    {
        var key = Regex.Replace(value.ToLowerInvariant(), @"[\s.]", "");
        return key switch { "remastered" => "remaster", "重制版" or "重製版" or "リメイク" => "remake",
            "demo" or "trialversion" or "体験版" or "试玩版" or "試玩版" => "trial", "高清版" => "hdedition", _ => key };
    }

    public static string Explain(IEnumerable<string> reasons)
    {
        var codes = reasons.ToHashSet(StringComparer.Ordinal);
        if (codes.Contains("USER_CONFIRMED_IDENTITY_AND_VERSION")) return "已人工核验作品与版本，保留现有作品资料。";
        if (codes.Contains("SOURCE_RECORD_OR_IDENTITY_CONFLICT")) return "备份内的记录、身份或时长数据有矛盾，需先修正来源。";
        if (codes.Contains("PARENT_CHILD_INSTALLATION_CONFLICT")) return "安装目录存在父子重叠，不能自动合并。";
        if (codes.Contains("LIVE_HISTORY_CONFLICT")) return "存在尚未结束的游玩会话，请先结束计时。";
        if (codes.Contains("EXTERNAL_ID_OR_EDITION_CONFLICT") || codes.Contains("TITLE_EDITION_DIFFERENCE_REQUIRES_REVIEW")) return "作品编号或版本描述存在差异，请核对后确认对应。";
        if (codes.Contains("MULTIPLE_OR_CONTRADICTORY_TARGETS")) return "存在多个可能条目，请选择并核对对应作品。";
        if (codes.Contains("PATH_WITHOUT_CONFIRMED_IDENTITY")) return "安装目录相同，但缺少双方共有的作品编号；可人工核验。";
        if (codes.Contains("WORK_ID_ONLY_VERSION_OR_INSTALLATION_REVIEW_REQUIRED")) return "作品编号相符，安装位置或发行版本仍需核对。";
        if (codes.Contains("TITLE_ONLY_NEVER_MERGED")) return "标题相似，尚不足以自动认定为同一作品；可人工核验。";
        if (codes.Contains("SOURCE_RECORD_CHANGED_OR_DUPLICATED")) return "此前导入的来源记录已变化，请核对差异。";
        if (codes.Contains("PLAYTIME_OVERLAP_CONFLICT")) return "两边累计时长不同，请选择保留本地或采用来源累计值，不会相加。";
        if (codes.Contains("NOTES_CONFLICT") || codes.Contains("RATING_CONFLICT")) return "个人笔记或评分不同；核验后仍保留本地值。";
        return "请核对作品、版本及个人记录。";
    }
}
