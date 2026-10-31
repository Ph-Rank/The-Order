using System.Collections.Immutable;
using System.Globalization;
using System.Text.RegularExpressions;

namespace TheOrder.Application.Playground;

// A pure view of one successfully fetched Key-visible catalog. It never adds
// hardcoded models, probes generation, grants capabilities or caches a prior Key.
public static class PlaygroundModelSelection
{
    private static readonly Regex Tokens = new("[a-z0-9]+", RegexOptions.CultureInvariant);
    private static readonly Regex LeadingVersion = new("^(?:gpt|gemini|grok)-(?<version>[0-9]+(?:[.-][0-9]{1,3})*)(?:-|$)", RegexOptions.CultureInvariant);
    private static readonly Regex SnapshotDate = new("(?:^|-)(?<year>20[0-9]{2})-?(?<month>[0-9]{2})-?(?<day>[0-9]{2})(?:-|$)", RegexOptions.CultureInvariant);
    private static readonly Regex PreviewMonth = new("-(?<month>[0-9]{2})-(?<year>20[0-9]{2})(?:-|$)", RegexOptions.CultureInvariant);
    private static readonly Regex SnapshotSuffix = new("-[0-9]{3,8}$", RegexOptions.CultureInvariant);
    private static readonly Regex SpecializedVariant = new("(?:^|-)(?:custom-?tools?|computer-use)(?:-|$)", RegexOptions.CultureInvariant);
    private static readonly HashSet<string> Excluded = new(StringComparer.Ordinal)
    { "image", "images", "audio", "embed", "embedding", "embeddings", "tts", "realtime", "transcribe", "transcription", "whisper",
        "speech", "codex", "code", "coder", "rerank", "moderation", "robotics", "search", "research", "base" };

    public static ImmutableArray<PlaygroundModel> SelectLatest(PlaygroundProviderKind provider, IEnumerable<PlaygroundModel> catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        var order = Families(provider); var selected = new Dictionary<string, Candidate>(StringComparer.Ordinal);
        foreach (var model in catalog)
        {
            if (model is null || string.IsNullOrWhiteSpace(model.Id) || model.Id.Length > 160 || model.Id.Any(char.IsControl)) continue;
            string id = model.Id.StartsWith("models/", StringComparison.Ordinal) ? model.Id[7..].ToLowerInvariant() : model.Id.ToLowerInvariant();
            var tokens = Tokens.Matches(id).Select(match => match.Value).ToArray();
            if (tokens.Any(Excluded.Contains) || SpecializedVariant.IsMatch(id) || id.Contains("multi-agent", StringComparison.Ordinal)
                || id.StartsWith("ft:", StringComparison.Ordinal) || new[] { "retired", "shutdown", "disabled" }.Contains(model.Lifecycle, StringComparer.OrdinalIgnoreCase)) continue;
            if (provider == PlaygroundProviderKind.Gemini && model.GenerationMethodsReported
                && (model.GenerationMethods.IsDefaultOrEmpty || !model.GenerationMethods.Any(TextGenerationMethod))) continue;
            string? family = Family(provider, id, tokens);
            if (family is null) continue;
            var candidate = Describe(provider, family, id, model);
            if (!selected.TryGetValue(family, out var previous) || Compare(candidate, previous) > 0) selected[family] = candidate;
        }
        return order.Where(selected.ContainsKey).Select(family => selected[family].Model).ToImmutableArray();
    }

    private static string[] Families(PlaygroundProviderKind provider) => provider switch
    {
        PlaygroundProviderKind.OpenAI => ["luna", "terra", "sol", "astra"],
        PlaygroundProviderKind.Gemini => ["lite", "flash", "pro", "argon"],
        PlaygroundProviderKind.Claude => ["haiku", "sonnet", "opus", "fable"],
        PlaygroundProviderKind.DeepSeek => ["flash", "pro", "chat", "reasoner"],
        PlaygroundProviderKind.Grok => ["grok", "fast", "mini"],
        _ => []
    };
    private static string? Family(PlaygroundProviderKind provider, string id, string[] tokens)
    {
        string prefix = provider switch { PlaygroundProviderKind.OpenAI => "gpt-", PlaygroundProviderKind.Gemini => "gemini-",
            PlaygroundProviderKind.Claude => "claude-", PlaygroundProviderKind.DeepSeek => "deepseek-", PlaygroundProviderKind.Grok => "grok-", _ => "" };
        if (prefix.Length == 0 || !id.StartsWith(prefix, StringComparison.Ordinal)) return null;
        if (provider == PlaygroundProviderKind.Grok) return tokens.Contains("fast") ? "fast" : tokens.Contains("mini") ? "mini" : "grok";
        // flash-lite is the Lite family, not a second Flash duplicate.
        return Families(provider).FirstOrDefault(tokens.Contains);
    }
    private static bool TextGenerationMethod(string method)
    {
        string normalized = string.Concat(method.Where(char.IsAsciiLetterOrDigit)).ToLowerInvariant();
        return normalized is "generatecontent" or "streamgeneratecontent" or "generatemessage" or "generatetext"
            or "interactions" or "createinteraction" or "createinteractions" or "interactionscreate";
    }
    private sealed record Candidate(PlaygroundModel Model, string Id, int[] Version, bool Preview, bool Snapshot,
        bool LatestAlias, long Date, int[] Revision);
    private static Candidate Describe(PlaygroundProviderKind provider, string family, string id, PlaygroundModel model)
    {
        int[] version = ParseVersion(LeadingVersion.Match(id).Groups["version"].Value);
        if (provider is PlaygroundProviderKind.Claude or PlaygroundProviderKind.DeepSeek)
        {
            var pieces = id.Split('-'); int index = Array.IndexOf(pieces, family);
            IEnumerable<string> before = index > 1 ? pieces.Skip(1).Take(index - 1) : [];
            IEnumerable<string> after = index >= 0 ? pieces.Skip(index + 1) : [];
            version = ParseVersion(string.Join(".", before.Concat(after).TakeWhile(part => Regex.IsMatch(part, "^v?[0-9]{1,3}(?:\\.[0-9]{1,3})*$", RegexOptions.CultureInvariant))));
        }
        // A provider's patch counter (e.g. 001) is not a semantic major version.
        if (version.Length == 0 && model.Version?.Contains('.') == true) version = ParseVersion(model.Version);
        bool preview = id.Contains("preview", StringComparison.Ordinal) || id.Contains("experimental", StringComparison.Ordinal)
            || id.Split('-').Contains("exp") || id.Split('-').Contains("beta");
        long date = 0; var match = SnapshotDate.Match(id);
        if (match.Success && DateTime.TryParseExact(match.Groups["year"].Value + match.Groups["month"].Value + match.Groups["day"].Value,
            "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var calendar)) date = calendar.Ticks;
        else if ((match = PreviewMonth.Match(id)).Success && DateTime.TryParseExact(match.Groups["year"].Value + match.Groups["month"].Value,
            "yyyyMM", CultureInfo.InvariantCulture, DateTimeStyles.None, out calendar)) date = calendar.Ticks;
        bool snapshot = date != 0 || SnapshotSuffix.IsMatch(id);
        string revision = model.Version ?? (snapshot ? id.Split('-').Last() : "");
        return new(model, id, version, preview, snapshot, id.EndsWith("-latest", StringComparison.Ordinal), date, ParseVersion(revision));
    }
    private static int[] ParseVersion(string value)
    {
        if (value.StartsWith('v')) value = value[1..];
        var parts = value.Split(['.', '-'], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 || parts.Length > 8) return [];
        var result = new int[parts.Length];
        for (int index = 0; index < parts.Length; index++)
            if (!int.TryParse(parts[index], NumberStyles.None, CultureInfo.InvariantCulture, out result[index])) return [];
        return result;
    }
    private static int Compare(Candidate a, Candidate b)
    {
        int compared = CompareNumbers(a.Version, b.Version); if (compared != 0) return compared;
        // Within a semantic generation prefer its ordinary stable alias over
        // dated duplicates. A newer preview generation still beats an older one.
        compared = (!a.Preview).CompareTo(!b.Preview); if (compared != 0) return compared;
        compared = (!a.Snapshot).CompareTo(!b.Snapshot); if (compared != 0) return compared;
        compared = (!a.LatestAlias).CompareTo(!b.LatestAlias); if (compared != 0) return compared;
        compared = a.Date.CompareTo(b.Date); if (compared != 0) return compared;
        compared = CompareNumbers(a.Revision, b.Revision); if (compared != 0) return compared;
        compared = Nullable.Compare(a.Model.CreatedAt, b.Model.CreatedAt); if (compared != 0) return compared;
        return StringComparer.Ordinal.Compare(a.Id, b.Id);
    }
    private static int CompareNumbers(int[] a, int[] b)
    {
        for (int index = 0; index < Math.Max(a.Length, b.Length); index++)
        { int compared = (index < a.Length ? a[index] : 0).CompareTo(index < b.Length ? b[index] : 0); if (compared != 0) return compared; }
        return 0;
    }
}
