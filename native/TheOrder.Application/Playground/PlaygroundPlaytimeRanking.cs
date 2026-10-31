using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using TheOrder.Domain;

namespace TheOrder.Application.Playground;

public sealed partial class PlaygroundService
{
    // Rank the complete admitted snapshot before paging. A recent-play sample,
    // session sum, or title search is never a substitute for lifetime totals.
    private string PlaytimeRanking(Run run, JsonElement args)
    {
        Activity(run, "核对全书架时长排名");
        var snapshot = ReadLibrary(run);
        if (snapshot is null) return Result("unknown", notice: "书架尚未就绪，无法核对全库排名。");
        int limit = Count(args, "limit", 3, 10);
        int offset = args.TryGetProperty("offset", out var value) ? value.GetInt32() : 0;
        if (offset is < 0 or > 100000) return Result("invalid_arguments");
        run.PlaytimeRankingStamp = PlaytimeRankingIdentity(snapshot);
        var ordered = snapshot.Entries.Where(entry => entry.EffectivePlaySeconds > 0)
            .OrderByDescending(entry => entry.EffectivePlaySeconds).ThenBy(entry => entry.Id, StringComparer.Ordinal).ToArray();
        var observedAt = clock();
        var rows = ordered.Skip(offset).Take(limit).Select((entry, index) =>
        {
            int rank = offset + index + 1;
            long seconds = entry.EffectivePlaySeconds;
            string duration = string.Create(CultureInfo.InvariantCulture, $"{seconds / 3600} 小时 {seconds % 3600 / 60} 分 {seconds % 60} 秒");
            string sourceId = RecordSourceId(entry);
            LocalSource(run, new(sourceId, PlaygroundSourceKind.LocalRecord, "时长排名 · " + Redacted(entry.Name, 160))
            { TargetId = entry.Id, WorkKey = WorkKeyFor(entry), At = observedAt,
                Quote = $"在当前完整书架中按累计记录时长降序第 {rank} 项：{duration}。包含未撤销人工修正；不代表剧情进度或喜好。" });
            return new { rank, sourceId, workKey = WorkKeyFor(entry), title = Redacted(entry.Name, 160),
                recordedPlaySeconds = seconds, displayDuration = duration,
                tiedWithPrevious = rank > 1 && ordered[rank - 2].EffectivePlaySeconds == seconds };
        }).ToArray();
        return Result(rows.Length == 0 ? "empty" : "ok", new
        {
            scope = "entire_current_library", metric = "effective_recorded_play_seconds", observedAt,
            libraryEntryCount = snapshot.Entries.Length, entriesWithRecordedTime = ordered.Length,
            offset, limit, returnedCount = rows.Length, nextOffset = offset + rows.Length < ordered.Length ? (int?)(offset + rows.Length) : null,
            tieOrder = "相同秒数按稳定条目 ID 排列，无时长高低之分。", works = rows
        }, "已在完整书架上排序后分页，不受最近游玩日期、安装或分类影响；每个书架条目只计一次。累计含有效人工修正，不另加 session 或本次会话以免重复计时；零时长条目不入榜。只按此处 rank 和 display_duration 回答，不能用最近样本或聊天旧答案改排。");
    }

    private static string PlaytimeRankingIdentity(LibrarySnapshot? snapshot) => Hash(JsonSerializer.Serialize(
        snapshot?.Entries.OrderBy(entry => entry.Id, StringComparer.Ordinal).Select(entry => new
        { key = WorkKeyFor(entry), entry.Name, seconds = entry.EffectivePlaySeconds }).ToArray(), ToolJson));

    private static bool PlaytimeRankingQuestion(string question) => Regex.IsMatch(question,
        @"(?:游玩|游戏|作品|玩过|玩得|玩了|玩的).{0,20}(?:时长|时间).{0,16}(?:最[长多]|排行|排名|前\s*[一二两三四五六七八九十0-9]+)|(?:时长|时间).{0,12}(?:排行|排名|最[长多])|\bmost[-\s]+played\b|\b(?:longest|top\s*\d+).{0,20}(?:play|game)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static bool TryPlaytimeRankingLimit(string question, out int limit)
    {
        limit = 3;
        // Time periods, status/category filters and named subsets need their
        // own interpretation; do not silently substitute a lifetime ranking.
        if (!PlaytimeRankingQuestion(question) || Regex.IsMatch(question,
            @"最近|本[周月年]|这[周月年]|上[周月年]|[今昨前]天|\d+\s*[天周月年]|20\d{2}|已玩|未玩|玩过分类|正在玩|收藏|分类|标签|《|「|『|\b(?:last|past|this|since|between|category|tag)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) return false;
        var count = Regex.Match(question, @"(?:前\s*|\btop\s*)([一二两三四五六七八九十0-9]+)|([一二两三四五六七八九十0-9]+)\s*(?:个|款|部)\s*(?:游戏|作品)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (count.Success)
        {
            string number = count.Groups[1].Success ? count.Groups[1].Value : count.Groups[2].Value;
            limit = number switch { "一" => 1, "二" or "两" => 2, "三" => 3, "四" => 4, "五" => 5, "六" => 6, "七" => 7, "八" => 8, "九" => 9, "十" => 10,
                _ => int.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out int parsed) ? parsed : 0 };
        }
        else if (Regex.IsMatch(question, @"哪[个款部]|最[长多]的(?:游戏|作品)(?:是|[？?]|$)", RegexOptions.CultureInvariant)) limit = 1;
        return limit is >= 1 and <= 10;
    }
}
