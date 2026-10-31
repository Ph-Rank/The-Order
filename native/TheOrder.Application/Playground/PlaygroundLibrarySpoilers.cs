using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using TheOrder.Domain;

namespace TheOrder.Application.Playground;

public sealed partial class PlaygroundService
{
    private static readonly Regex NarrativeDetail = new(
        @"结局|結局|真相|死亡|死去|死了|杀死|殺害|凶手|犯人|幕后|黑幕|黒幕|身份反转|真实身份|真實身份|実は|实为|實為|背叛|牺牲|犧牲|复活|復活|最终章|最終章|终章|終章|后日谈|後日談|通关后|结尾|結尾|第[一二三四五六七八九十百0-9]+[章幕]|\b(?:ending|culprit|dies|died|murderer|betrayal|resurrection|final\s+chapter|true\s+identity|dead\s+end|bad\s+end)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private sealed record NarrativeScope(bool Identified, bool Allowed);
    private static IEnumerable<string> ReliableTitles(LibraryEntry entry) => entry.Metadata is not { } metadata ? []
        : new[] { metadata.Title, metadata.OriginalTitle }.Concat(metadata.Aliases).Where(t => !string.IsNullOrWhiteSpace(t)).Distinct(StringComparer.Ordinal);
    private static string SpoilerLibraryIdentity(LibrarySnapshot? snapshot) => Hash(JsonSerializer.Serialize(
        snapshot?.Entries.OrderBy(entry => entry.Id, StringComparer.Ordinal).Select(entry => new
        { key = WorkKeyFor(entry), entry.Status, titles = ReliableTitles(entry).OrderBy(title => title, StringComparer.Ordinal).ToArray() }).ToArray(), ToolJson));

    private NarrativeScope ResolveNarrativeScope(Run run, string text, IEnumerable<string>? boundKeys = null)
    {
        if (run.Conversation.Options.Spoiler == PlaygroundSpoilerMode.Full) return new(true, true);
        if (run.Conversation.Options.Spoiler != PlaygroundSpoilerMode.LibraryStatus || !Can(run, "library")) return new(false, false);
        var entries = ReadLibrary(run)?.Entries.Where(entry => entry.Metadata is not null).ToArray() ?? [];
        var matched = new Dictionary<string, LibraryEntry>(StringComparer.Ordinal); bool unknown = false, identified = false;
        foreach (Match title in PublicTitles.Matches(text))
        {
            identified = true;
            string name = Normalize(title.Groups[1].Value.Trim());
            var exact = entries.Where(entry => ReliableTitles(entry).Any(alias => Normalize(alias) == name)).Take(2).ToArray();
            if (exact.Length != 1) unknown = true;
            else matched[WorkKeyFor(exact[0])] = exact[0];
        }
        var aliases = entries.SelectMany(entry => ReliableTitles(entry).Select(alias => (Entry: entry, Alias: alias)))
            .Where(row => AliasIn(text, row.Alias)).GroupBy(row => Normalize(row.Alias), StringComparer.Ordinal);
        foreach (var alias in aliases)
        {
            identified = true;
            var candidates = alias.Select(row => row.Entry).DistinctBy(WorkKeyFor).Take(2).ToArray();
            if (candidates.Length != 1) unknown = true;
            else matched[WorkKeyFor(candidates[0])] = candidates[0];
        }
        foreach (string key in boundKeys ?? [])
        {
            identified = true;
            var exact = entries.Where(entry => WorkKeyFor(entry) == key).Take(2).ToArray();
            if (exact.Length != 1) unknown = true;
            else matched[key] = exact[0];
        }
        return new(identified, identified && !unknown && matched.Count != 0 && matched.Values.All(entry => entry.Status == "completed"));
    }
    private bool NarrativeAllowed(Run run, string text, IEnumerable<string>? boundKeys = null, bool inheritQuestion = false)
    {
        if (run.Conversation.Options.Spoiler == PlaygroundSpoilerMode.Full) return true;
        var scope = ResolveNarrativeScope(run, text, boundKeys);
        return scope.Identified ? scope.Allowed : inheritQuestion && QuestionNarrativeScope(run).Allowed;
    }
    private NarrativeScope QuestionNarrativeScope(Run run)
    {
        var direct = ResolveNarrativeScope(run, run.Question);
        if (direct.Identified || run.Conversation.Options.Spoiler != PlaygroundSpoilerMode.LibraryStatus || !Can(run, "library")) return direct;
        // A pronoun may inherit the most recent reliably associated user turn,
        // never an older played work behind a more recent unknown/unplayed one.
        var snapshot = Snapshot; var admission = new MessageAdmission(this, run, snapshot);
        foreach (var message in snapshot.Messages.Where(message => message.ConversationId == run.Conversation.Id
            && message.Id != run.UserId && message.Role == "user").Reverse().Take(20))
        {
            var scope = ResolveNarrativeScope(run, message.Text, message.Sources.Where(source => source.WorkKey is not null).Select(source => source.WorkKey!));
            if (!admission.Allows(message)) { if (scope.Identified) return new(true, false); continue; }
            if (scope.Identified) return scope;
        }
        return direct;
    }
    private string NarrativeScopeInstructions(Run run)
    {
        if (run.Conversation.Options.Spoiler != PlaygroundSpoilerMode.LibraryStatus) return "以当前模式的约束为准。";
        if (!Can(run, "library")) return "未授权读取书架或未使用既有资料，全部作品按未知处理；不得暗读库状态。";
        var entries = ReadLibrary(run)?.Entries.Where(entry => ReliableTitles(entry).Any(title => AliasIn(run.Question, title))).Take(16).ToArray() ?? [];
        bool allowed = QuestionNarrativeScope(run).Allowed;
        string detail = JsonSerializer.Serialize(new { current_question_detail_allowed = allowed,
            works = entries.Select(entry => new { title = Bounded(entry.Metadata!.Title, 100), work_key = WorkKeyFor(entry),
                status = entry.Status, detail_allowed = NarrativeRecordAllowed(run, entry) }),
            notice = "这是本轮新鲜书架策略依据，不是全通关证明。未列出或无法关联的作品按未知；一个作品的放行不能扩大到续作或其他作品。" }, ToolJson);
        return detail.Length <= 4000 ? detail : JsonSerializer.Serialize(new { current_question_detail_allowed = allowed,
            notice = "当前作品清单超出提示容量，可用书架工具逐项核对；无法可靠关联的情节仍不放行。" }, ToolJson);
    }
    private bool NarrativeRecordAllowed(Run run, LibraryEntry entry)
        => NarrativeAllowed(run, string.Join(" ", ReliableTitles(entry).Select(title => "《" + title + "》")), [WorkKeyFor(entry)]);
    private bool NarrativeHistoryAllowed(Run run, PlaygroundMessage message)
        => NarrativeAllowed(run, message.Text, message.Sources.Where(source => source.WorkKey is not null).Select(source => source.WorkKey!));
    private bool NarrativeMemoryAllowed(Run run, PlaygroundMemory memory)
        => NarrativeAllowed(run, memory.Text + "\n" + (memory.OriginalQuote ?? ""), memory.WorkKey is null ? [] : [memory.WorkKey]);
    private string SpoilerSafeText(Run run, string text, bool inheritQuestion, out bool changed, IEnumerable<string>? boundKeys = null)
    {
        changed = false;
        if (run.Conversation.Options.Spoiler == PlaygroundSpoilerMode.Full || string.IsNullOrEmpty(text)) return text;
        var result = new System.Text.StringBuilder(); bool omitted = false;
        foreach (string segment in Regex.Split(text, @"(?<=[。！？!?\n])|(?<=\.)\s+", RegexOptions.CultureInvariant))
        {
            if (segment.Length == 0) continue;
            if (NarrativeDetail.IsMatch(segment) && !NarrativeAllowed(run, segment, boundKeys, inheritQuestion))
            {
                changed = true;
                if (!omitted) result.Append("〔未玩或无法确认作品归属的剧情片段已省略〕\n");
                omitted = true;
            }
            else { result.Append(segment); omitted = false; }
        }
        return result.ToString();
    }
    private ImmutableArray<PlaygroundWireMessage> ContextMessageWires(Run run, PlaygroundMessage message, bool includeImages = true)
    {
        if (run.Conversation.Options.Spoiler == PlaygroundSpoilerMode.Full) return MessageWires(message, includeImages);
        bool images = includeImages && QuestionNarrativeScope(run).Allowed;
        var attachments = message.Attachments.Select(attachment => IsImageAttachment(attachment) ? attachment : attachment with
            { Text = SpoilerSafeText(run, attachment.Text, true, out _) }).ToImmutableArray();
        if (message.Attachments.Any(IsImageAttachment) && !images) Activity(run, "未能确认附件作品的剧情范围，本轮未发送图片。");
        return MessageWires(message with { Attachments = attachments }, images);
    }
    private static bool NativeWebState(string data)
    {
        try { return HasWeb(JsonNode.Parse(data, documentOptions: new() { MaxDepth = 48 })); }
        // The continuation contract is opaque for test/custom providers. Known
        // native adapters validate their own JSON envelope before any request.
        catch (JsonException) { return false; }
        static bool HasWeb(JsonNode? value)
        {
            if (value is JsonArray array) return array.Any(HasWeb);
            if (value is not JsonObject item) return false;
            string type = item["type"]?.GetValueKind() == JsonValueKind.String ? item["type"]!.GetValue<string>() : "";
            string name = item["name"]?.GetValueKind() == JsonValueKind.String ? item["name"]!.GetValue<string>() : "";
            if (type is "web_search_call" or "google_search_call" or "google_search_result" or "web_search_tool_result"
                || (type is "function_call" or "tool_use" or "server_tool_use") && (name is "public_search" or "public_fetch" or "web_search")) return true;
            return item.Any(field => HasWeb(field.Value));
        }
    }
}
