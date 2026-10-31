using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace TheOrder.Application.Playground;

public sealed partial class PlaygroundService
{
    private static readonly Regex Space = new(@"\s+", RegexOptions.CultureInvariant);
    private static readonly Regex Terms = new(@"[a-z0-9]+|[\p{IsCJKUnifiedIdeographs}\p{IsHiragana}\p{IsKatakana}]{1,}", RegexOptions.CultureInvariant);
    private static readonly Regex PrivatePath = new(@"(?i)(?:[a-z]:[\\/]|\\\\)[^\r\n，。；;\s]{1,500}", RegexOptions.CultureInvariant);
    private static readonly Regex CitationMarker = new(@"\[\[([A-Za-z0-9_-]{1,160})\]\]", RegexOptions.CultureInvariant);
    private static string Endpoint(PlaygroundProviderKind provider, string endpoint) => PlaygroundEndpointPolicy.Normalize(provider, endpoint);
    internal static bool SafeWebUrl(string? url) => PlaygroundEndpointPolicy.SafePublicUrl(url);
    private static string Normalize(string text)
    {
        var n = Space.Replace(text.Normalize(NormalizationForm.FormKC).ToLowerInvariant(), " ").Trim();
        foreach (var (a, b) in new[] { ("シナリオ", "剧情"), ("情节", "剧情"), ("故事", "剧情"), ("story", "剧情"), ("plot", "剧情"),
            ("キャラクター", "角色"), ("人物", "角色"), ("character", "角色"), ("好感", "喜欢"), ("喜爱", "喜欢"), ("好き", "喜欢"), ("like", "喜欢"),
            ("サウンド", "音乐"), ("音樂", "音乐"), ("music", "音乐"), ("推荐", "推荐"), ("recommend", "推荐") }) n = n.Replace(a, b, StringComparison.Ordinal);
        return n;
    }
    private static IEnumerable<string> Tokens(string text)
    {
        foreach (Match match in Terms.Matches(Normalize(text)))
        {
            var value = match.Value; yield return value;
            if (value.Any(c => c > 127) && value.Length > 2) for (int i = 0; i < value.Length - 1; i++) yield return value.Substring(i, 2);
        }
    }
    private static double Match(string text, string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return 1;
        var haystack = Normalize(text); var needle = Normalize(query);
        double score = haystack.Contains(needle, StringComparison.Ordinal) ? 12 : 0;
        foreach (var term in Tokens(query).Distinct(StringComparer.Ordinal).Take(40)) if (haystack.Contains(term, StringComparison.Ordinal)) score += term.Length > 1 ? 2 : .2;
        return score;
    }
    private static string Bounded(string value, int max = 1200) => value.Length <= max ? value : value[..max] + "…";
    private static string Redacted(string value, int max = 1600) => Bounded(PrivatePath.Replace(value, "[本地路径已省略]"), max);
    private static string Hash(string value) => "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static bool PlotText(string text) => new[] { "结局", "結局", "真相", "死亡", "死了", "凶手", "幕后", "黑幕", "剧透", "劇透", "背叛", "end1", "ending", "spoiler", "culprit", "dies", "死亡", "ネタバレ", "犯人", "黒幕" }
        .Any(t => text.Contains(t, StringComparison.OrdinalIgnoreCase));
    private static bool PrivateOpinion(string text) => new[] { "密码", "密钥", "api key", "身份证", "银行卡", "住址", "手机号", "宗教", "种族", "病历", "诊断", "password", "sk-", "c:\\", "d:\\" }
        .Any(t => text.Contains(t, StringComparison.OrdinalIgnoreCase));
    private static void Validate(PlaygroundSettings s)
    {
        if (s.Permissions is null || s.Connections.IsDefault || s.Connections.Length > 30 || s.MaximumToolRounds < 0
            || s.MaximumSearches < 0 || s.DeepMaximumSearches < 0 || s.MaximumOutputTokens is < 128 or > 65536
            || s.MaximumContextCharacters is < 2000 or > 1000000 || s.TimeoutSeconds is < 10 or > 1800) throw new ArgumentException("Playground 设置超出允许范围。");
        foreach (var c in s.Connections) { if (!Enum.IsDefined(c.Provider) || c.Model.Length > 180) throw new ArgumentException("服务商设置无效。"); Endpoint(c.Provider, c.Endpoint); }
        if (s.Connections.Select(c => (c.Provider, Endpoint(c.Provider, c.Endpoint))).Distinct().Count() != s.Connections.Length) throw new ArgumentException("服务商连接重复。");
    }
    private static void Validate(PlaygroundConversationOptions o)
    {
        if (!Enum.IsDefined(o.Provider) || !Enum.IsDefined(o.Search) || !Enum.IsDefined(o.Spoiler) || o.Model.Length > 180
            || o.Model.Any(char.IsControl) || o.ReasoningEffort?.Length > 64 || o.SystemInstructions is null || o.SystemInstructions.Length > 8000
            || o.SystemInstructions.Any(c => char.IsControl(c) && c is not ('\r' or '\n' or '\t'))
            || o.WorkProgress is null || o.WorkProgress.Count > 128
            || o.WorkProgress.Any(p => p.Key.Length is < 1 or > 1024 || p.Value.Length is < 1 or > 1000)
            || o.WorkProgress.Sum(p => p.Key.Length + p.Value.Length) > 12000) throw new ArgumentException("会话设置无效。");
        Endpoint(o.Provider, o.Endpoint);
    }
    private void Validate(PlaygroundState s)
    {
        if (s.Identity != store.Identity || s.Identity.ProfileId == Guid.Empty || s.Identity.StoreId == Guid.Empty || s.Version != 1 || s.Revision < 0
            || s.Conversations.IsDefault || s.Messages.IsDefault || s.Memories.IsDefault || s.Suppressions.IsDefault
            || s.Conversations.Length > 500 || s.Messages.Length > 20000 || s.Memories.Length > 4000 || s.Suppressions.Length > 8000)
            throw new InvalidDataException("Playground 资料身份、版本或容量无法验证，原资料已保留。");
        Validate(s.Settings);
        if (s.Conversations.Select(c => c.Id).Distinct().Count() != s.Conversations.Length || s.Messages.Select(m => m.Id).Distinct().Count() != s.Messages.Length
            || s.Memories.Select(m => m.Id).Distinct().Count() != s.Memories.Length) throw new InvalidDataException("Playground 资料包含重复身份。");
        var conversations = s.Conversations.Select(c => c.Id).ToHashSet(); var messages = s.Messages.ToDictionary(m => m.Id);
        foreach (var c in s.Conversations) { if (c.Id == Guid.Empty || c.Temporary || c.Title.Length is < 1 or > 120) throw new InvalidDataException("会话资料无效。"); Validate(c.Options); }
        foreach (var m in s.Messages)
        {
            if (m.Id == Guid.Empty || !conversations.Contains(m.ConversationId) || m.Role is not ("user" or "assistant") || m.Text is null
                || m.Text.Length > 1048576 || !Enum.IsDefined(m.Status) || !Enum.IsDefined(m.Retention) || m.Sources.IsDefault || m.Sources.Length > 64 || m.Activities.Length > 32
                || m.ContextMessageIds.IsDefault || m.ContextMessageIds.Length > 1024 || m.ContextMessageIds.Contains(m.Id)
                || m.ContextMessageIds.Contains(Guid.Empty) || m.ContextMessageIds.Distinct().Count() != m.ContextMessageIds.Length
                || !PlaygroundPricing.IsValidStoredUsage(m) || !PlaygroundPricing.IsValidCost(m.Cost)
                || m.Role != "user" && !m.Attachments.IsEmpty || m.Role == "user" && m.Text.Length == 0 && m.Attachments.IsEmpty)
                throw new InvalidDataException("消息资料无效。");
            try { ValidateAttachments(m.Attachments); } catch (ArgumentException error) { throw new InvalidDataException("文件资料快照无效，原资料已保留。", error); }
        }
        foreach (var m in s.Memories) if (!messages.TryGetValue(m.MessageId, out var original) || original.Role != "user" || original.ConversationId != m.ConversationId
            || m.Text.Length is < 1 or > 2000 || m.OriginalQuote?.Length > 2000 || m.Kind is not ("explicit" or "inferred" or "user-edited")) throw new InvalidDataException("偏好来源无法验证。");
        foreach (var x in s.Suppressions) if (x.Text.Length > 128 || !x.Text.StartsWith("sha256:", StringComparison.Ordinal) || x.MessageIds.IsDefault || x.MessageIds.Length > 20000)
            throw new InvalidDataException("遗忘记录无效。");
    }
}

