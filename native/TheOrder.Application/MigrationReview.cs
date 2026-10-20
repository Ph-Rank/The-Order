using System.Collections.Immutable;
using System.Text.RegularExpressions;
using TheOrder.Domain;

namespace TheOrder.Application.Migrations;

public static class MigrationReview
{
    public static long SourceSecondsAlreadyAccounted(LibraryEntry target, string manager, ImportGame source)
    {
        var previous = target.MigrationOrigins.FirstOrDefault(o => o.Manager == manager && o.RecordId == source.SourceId);
        var reviewed = previous?.Review?.AccountedSourceSeconds
            ?? (previous?.Review is { HistoryChoice: "use-source-total" } adopted ? adopted.AfterSeconds
                : previous?.Fields.Any(f => f.Target == "history") == true ? previous.ReportedSeconds : 0);
        return Math.Max(reviewed, previous?.HistoryDates?.AccountedSeconds ?? 0);
    }
    public static long AdditionalSourceSeconds(LibraryEntry target, string manager, ImportGame source)
        => MigrationHistoryDates.Build(target, manager, source).AdditionalSeconds;
    public static bool CanAddSourceTotal(LibraryEntry target, string manager, ImportGame source)
    {
        var addition = MigrationHistoryDates.Build(target, manager, source);
        return target.TotalPlaySeconds <= 1_000_000_000_000 - addition.AdditionalSeconds
            && target.EffectivePlaySeconds <= 1_000_000_000_000 - addition.AdditionalSeconds;
    }
    public static bool CanUseSourceTotal(LibraryEntry target, long sourceSeconds)
    {
        var total = sourceSeconds - target.ManualPlaytime.Where(x => !x.Undone).Sum(x => x.Seconds);
        return total >= target.Sessions.Sum(x => x.Seconds) && total is >= 0 and <= 1_000_000_000_000;
    }
    internal static ImportReviewCandidate Candidate(LibraryEntry target, ImportGame source, string manager)
    {
        var addition = MigrationHistoryDates.Build(target, manager, source);
        return new(
        ImportHash.Text(target.Id)[..20], target.Name, target.Metadata?.Title ?? "", target.Status, target.EffectivePlaySeconds,
        MigrationResolver.Identities(target), [.. target.Installations.Take(6).Select(i => i.Folder)],
        !source.Installations.IsEmpty && source.Installations.All(i => target.Installations.Any(x => ImportPaths.Key(x.Folder) == ImportPaths.Key(i.Folder))),
        CanUseSourceTotal(target, source.TotalSeconds),
        target.Notes.Length > 0 && source.Notes.Length > 0 && target.Notes != source.Notes,
        target.PersonalRating is { } a && source.Rating is { } b && a != b, target.Installations.Length)
        { CanAddSourceTotal = CanAddSourceTotal(target, manager, source), AdditionalSourceSeconds = addition.AdditionalSeconds,
            NewlyDatedSeconds = addition.NewlyDatedSeconds, DuplicateSessionSeconds = addition.DuplicateSeconds, HistoryWarnings = addition.Warnings };
    }

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
        if (codes.Contains("PLAYTIME_OVERLAP_CONFLICT")) return "两边累计时长不同，可选择相加、保留本地或采用来源累计值。";
        if (codes.Contains("NOTES_CONFLICT") || codes.Contains("RATING_CONFLICT")) return "个人笔记或评分不同；核验后仍保留本地值。";
        return "请核对作品、版本及个人记录。";
    }
    public static string ExplainHistoryWarnings(IEnumerable<string> warnings)
    {
        var codes = warnings.ToHashSet(StringComparer.Ordinal); var notes = new List<string>();
        if (codes.Contains("OVERLAPPING_SESSION_KEPT_UNDATED")) notes.Add("部分会话与本地记录重叠，未重复分配到日期。");
        if (codes.Contains("LOCAL_DAILY_RECORD_EDIT_PRESERVED") || codes.Contains("SOURCE_SESSION_REVISED_OR_LOCAL_RECORD_EDITED"))
            notes.Add("保留本地已修改或撤销的记录；来源会话改写未直接覆盖。");
        if (codes.Contains("SOURCE_DAILY_TOTAL_REVISED_EXISTING_DATE_PRESERVED")) notes.Add("来源逐日时长有减少，保留此前记录。");
        if (codes.Any(c => c is "SOURCE_DATES_EXCEED_UNALLOCATED_CONTRIBUTION" or "SOURCE_SESSION_CHANGE_NOT_ATTRIBUTABLE" or "SOURCE_SESSION_ID_COLLISION" or "SOURCE_DAILY_ID_COLLISION"))
            notes.Add("部分日期无法安全合并，剩余时长仍单独列为未分配日期。");
        return string.Join("", notes);
    }
}
