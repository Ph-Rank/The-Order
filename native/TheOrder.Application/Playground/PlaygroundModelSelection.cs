using System.Collections.Immutable;
using System.Globalization;
using System.Text.RegularExpressions;

namespace TheOrder.Application.Playground;

// A pure view of one successfully fetched Key-visible catalog. It never adds
// hardcoded models, probes generation, grants capabilities or caches a prior Key.
public static class PlaygroundModelSelection
{
    private const string VersionPattern = "[0-9]{1,3}(?:[.-][0-9]{1,3})*";
    private const string FamilyPattern = "[a-z][a-z0-9]*(?:-[a-z][a-z0-9]*)*";
    private static readonly Regex ModelBody = new("^[a-z0-9]+(?:[.-][a-z0-9]+)*$", RegexOptions.CultureInvariant);
    private static readonly Regex FamilyName = new("^" + FamilyPattern + "$", RegexOptions.CultureInvariant);
    private static readonly Regex VersionFirst = new("^v?(?<version>" + VersionPattern + ")(?:-(?<family>" + FamilyPattern + "))?$", RegexOptions.CultureInvariant);
    private static readonly Regex VersionLast = new("^(?<family>" + FamilyPattern + ")-v?(?<version>" + VersionPattern + ")$", RegexOptions.CultureInvariant);
    private static readonly Regex SnapshotDate = new("(?:^|-)(?<year>20[0-9]{2})-?(?<month>[0-9]{2})-?(?<day>[0-9]{2})$", RegexOptions.CultureInvariant);
    private static readonly Regex PreviewMonth = new("-(?<month>[0-9]{2})-(?<year>20[0-9]{2})$", RegexOptions.CultureInvariant);
    private static readonly Regex SnapshotSuffix = new("-[0-9]{3,8}$", RegexOptions.CultureInvariant);
    private static readonly Regex ReleaseSuffix = new("-(?<status>preview|experimental|exp|beta|alpha|latest|stable|ga)$", RegexOptions.CultureInvariant);
    private static readonly Regex SpecializedVariant = new("(?:^|-)(?:custom-?tools?|computer-use|multi-agent|deep-research|dall-e)(?:-|$)", RegexOptions.CultureInvariant);
    private static readonly Regex StandaloneGptFamily = new(@"\A[a-z][a-z0-9]{1,24}\z", RegexOptions.CultureInvariant);
    private static readonly Regex OpenAiReasoning = new(@"^o[1-9][0-9]?(?:-(?:mini|pro))?(?:-20[0-9]{2}-[0-9]{2}-[0-9]{2})?$", RegexOptions.CultureInvariant);
    private static readonly HashSet<string> Unavailable = new(StringComparer.OrdinalIgnoreCase)
    { "retired", "shutdown", "disabled", "deprecated", "legacy" };
    private static readonly string[] ProviderPrefixes = ["gpt-", "gemini-", "claude-", "deepseek-", "grok-"];
    private static readonly HashSet<string> Excluded = new(StringComparer.Ordinal)
    {
        "image", "images", "audio", "video", "music", "voice", "live", "tts", "speech", "realtime", "transcribe",
        "transcription", "whisper", "embed", "embedding", "embeddings", "rerank", "moderation", "ocr", "fim",
        "instruct", "completion", "completions", "codex", "code", "coder", "robotics", "search", "research",
        "base", "cyber", "permissive", "aqa", "sora", "babbage", "davinci", "agent", "agents"
    };

    public static ImmutableArray<PlaygroundModel> SelectLatest(PlaygroundProviderKind provider, IEnumerable<PlaygroundModel> catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        var order = Families(provider);
        var selected = new Dictionary<string, Candidate>(StringComparer.Ordinal);
        string prefix = provider switch
        {
            PlaygroundProviderKind.OpenAI => "gpt-",
            PlaygroundProviderKind.Gemini => "gemini-",
            PlaygroundProviderKind.Claude => "claude-",
            PlaygroundProviderKind.DeepSeek => "deepseek-",
            PlaygroundProviderKind.Grok => "grok-",
            _ => ""
        };
        if (prefix.Length == 0) return [];
        int latestStableGeneration = 0, latestGeneration = 0;
        foreach (var model in catalog)
        {
            if (model is null || string.IsNullOrWhiteSpace(model.Id) || model.Id.Length > 160 || model.Id.Any(char.IsControl)) continue;
            string id = (model.Id.StartsWith("models/", StringComparison.Ordinal) ? model.Id[7..] : model.Id).ToLowerInvariant();
            if (model.Lifecycle is { } lifecycle && Unavailable.Contains(lifecycle)
                || model.CatalogTextOutput == false
                || model.GenerationMethodsReported && (model.GenerationMethods.IsDefaultOrEmpty || !model.GenerationMethods.Any(TextGenerationMethod))) continue;

            var candidate = Describe(provider, prefix, id, model);
            if (candidate is null) continue;
            latestGeneration = Math.Max(latestGeneration, candidate.Generation);
            if (!candidate.Preview) latestStableGeneration = Math.Max(latestStableGeneration, candidate.Generation);
            if (!selected.TryGetValue(candidate.Family, out var previous) || Compare(candidate, previous) > 0)
                selected[candidate.Family] = candidate;
        }

        if (provider is PlaygroundProviderKind.OpenAI or PlaygroundProviderKind.Gemini)
        {
            // Numbered GPT/Gemini generations are comparable within that product.
            // A future preview or independently named product can appear alongside the current generation.
            int floor = latestStableGeneration > 0 ? latestStableGeneration : latestGeneration;
            bool ordinary = selected.Values.Any(candidate => !candidate.Legacy && !candidate.LatestAlias);
            foreach (var candidate in selected.Values.ToArray())
                if (candidate.Generation > 0 && candidate.Generation < floor
                    || ordinary && (candidate.Legacy || candidate.LatestAlias && candidate.Generation == 0))
                    selected.Remove(candidate.Family);
        }

        return order.Where(selected.ContainsKey)
            .Concat(selected.Keys.Except(order, StringComparer.Ordinal).Order(StringComparer.Ordinal))
            .Select(family => selected[family].Model).ToImmutableArray();
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
    private static bool TextGenerationMethod(string method)
    {
        string normalized = string.Concat(method.Where(char.IsAsciiLetterOrDigit)).ToLowerInvariant();
        return normalized is "generatecontent" or "streamgeneratecontent" or "generatemessage" or "generatetext"
            or "interactions" or "createinteraction" or "createinteractions" or "interactionscreate"
            or "responses" or "chatcompletions";
    }
    private sealed record Candidate(PlaygroundModel Model, string Family, string Id, int[] Version, int Generation, bool Legacy,
        bool Preview, bool Snapshot, bool LatestAlias, long Date, int[] Revision);

    private static Candidate? Describe(PlaygroundProviderKind provider, string prefix, string id, PlaygroundModel model)
    {
        bool knownPrefix = id.StartsWith(prefix, StringComparison.Ordinal);
        bool openAi = provider == PlaygroundProviderKind.OpenAI;
        bool gemini = provider == PlaygroundProviderKind.Gemini;
        bool explicitText = model.CatalogTextOutput == true;
        bool reasoningFallback = openAi && OpenAiReasoning.IsMatch(id);
        bool rollingChat = openAi && id == "chat-latest";
        if (!knownPrefix && (ProviderPrefixes.Any(other => id.StartsWith(other, StringComparison.Ordinal))
            || !reasoningFallback && !rollingChat && !explicitText || gemini)) return null;
        string body = (knownPrefix ? id[prefix.Length..] : reasoningFallback ? id[1..] : id).Replace('_', '-');
        // OpenAI's old 4o spelling belongs to the GPT-4 generation.
        if (openAi && (body == "4o" || body.StartsWith("4o-", StringComparison.Ordinal))) body = "4" + body[2..];
        var parts = body.Split(['-', '.']);
        if (!ModelBody.IsMatch(body) || SpecializedVariant.IsMatch(body) || parts.Any(Excluded.Contains)
            || gemini && parts.Contains("omni")) return null;

        long date = 0;
        var suffix = SnapshotDate.Match(body);
        if (suffix.Success && DateTime.TryParseExact(suffix.Groups["year"].Value + suffix.Groups["month"].Value + suffix.Groups["day"].Value,
            "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var calendar))
            date = calendar.Ticks;
        else if ((suffix = PreviewMonth.Match(body)).Success && DateTime.TryParseExact(suffix.Groups["year"].Value + suffix.Groups["month"].Value,
            "yyyyMM", CultureInfo.InvariantCulture, DateTimeStyles.None, out calendar))
            date = calendar.Ticks;
        if (date != 0) body = body[..suffix.Index];
        else suffix = SnapshotSuffix.Match(body);
        bool snapshot = date != 0 || suffix.Success;
        string revision = model.Version ?? (snapshot && date == 0 ? body[(suffix.Index + 1)..] : "");
        if (snapshot && date == 0) body = body[..suffix.Index];

        bool preview = false, alias = false;
        for (var status = ReleaseSuffix.Match(body); status.Success; status = ReleaseSuffix.Match(body))
        {
            var release = status.Groups["status"].Value;
            preview |= release is "preview" or "experimental" or "exp" or "beta" or "alpha";
            alias |= release == "latest";
            body = body[..status.Index];
        }
        // Grok has used both of these mode suffixes for the same chat family.
        if (provider == PlaygroundProviderKind.Grok)
            foreach (string mode in new[] { "-non-reasoning", "-reasoning" })
                if (body.EndsWith(mode, StringComparison.Ordinal)) { body = body[..^mode.Length]; break; }

        var naming = VersionFirst.Match(body);
        if (!naming.Success) naming = VersionLast.Match(body);
        int[] version = ParseVersion(naming.Groups["version"].Value);
        if (version.Length == 0 && model.Version?.Contains('.') == true) version = ParseVersion(model.Version);
        string family = naming.Success ? naming.Groups["family"].Value : body;
        if (family.Length == 0) family = knownPrefix ? prefix[..^1] : body;
        if (reasoningFallback) family = "o" + (naming.Groups["family"].Value is { Length: > 0 } tier ? "-" + tier : "");
        bool knownAlias = alias && (Families(provider).Contains(family) || family == "flash-lite");
        bool newGptFamily = openAi && knownPrefix && StandaloneGptFamily.IsMatch(body) && !alias && !preview;
        if ((openAi || gemini) && version.Length == 0 && !explicitText && !knownAlias && !rollingChat && !newGptFamily) return null;
        if (gemini && !Families(provider).Contains(family) && family is not ("flash-lite" or "hydrogen" or "carbon")
            && !explicitText && !model.GenerationMethodsReported) return null;
        // A documented family is useful even when a provider introduces a new ID layout.
        if (model.CatalogFamily?.ToLowerInvariant() is { } declared && FamilyName.IsMatch(declared)) family = declared;
        if (!FamilyName.IsMatch(family)) family = body;

        if (provider == PlaygroundProviderKind.Gemini)
        {
            // Google's named successors keep their old category for ranking.
            family = family switch { "flash-lite" => "lite", "hydrogen" => "flash", "carbon" => "pro", _ => family };
        }
        int generation = (openAi || gemini) && knownPrefix ? version.FirstOrDefault() : 0;
        return new(model, family, id, version, generation, reasoningFallback || rollingChat, preview, snapshot, alias, date, ParseVersion(revision));
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
