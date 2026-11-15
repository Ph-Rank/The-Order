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
    private static readonly string[] ProviderPrefixes = ["gpt-", "gemini-", "claude-", "deepseek-", "grok-"];
    private static readonly HashSet<string> Excluded = new(StringComparer.Ordinal)
    {
        "image", "images", "audio", "video", "music", "voice", "live", "tts", "speech", "realtime", "transcribe",
        "transcription", "whisper", "embed", "embedding", "embeddings", "rerank", "moderation", "ocr", "fim",
        "instruct", "completion", "completions", "codex", "code", "coder", "robotics", "search", "research",
        "base", "cyber", "permissive", "aqa", "sora", "babbage", "davinci"
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
        foreach (var model in catalog)
        {
            if (model is null || string.IsNullOrWhiteSpace(model.Id) || model.Id.Length > 160 || model.Id.Any(char.IsControl)) continue;
            string id = (model.Id.StartsWith("models/", StringComparison.Ordinal) ? model.Id[7..] : model.Id).ToLowerInvariant();
            if (new[] { "retired", "shutdown", "disabled" }.Contains(model.Lifecycle, StringComparer.OrdinalIgnoreCase)
                || model.CatalogTextOutput == false
                || model.GenerationMethodsReported && (model.GenerationMethods.IsDefaultOrEmpty || !model.GenerationMethods.Any(TextGenerationMethod))) continue;

            var candidate = Describe(provider, prefix, id, model);
            if (candidate is null) continue;
            if (!selected.TryGetValue(candidate.Family, out var previous) || Compare(candidate, previous) > 0)
                selected[candidate.Family] = candidate;
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
            or "interactions" or "createinteraction" or "createinteractions" or "interactionscreate";
    }
    private sealed record Candidate(PlaygroundModel Model, string Family, string Id, int[] Version, bool Active,
        bool Preview, bool Snapshot, bool LatestAlias, long Date, int[] Revision);

    private static Candidate? Describe(PlaygroundProviderKind provider, string prefix, string id, PlaygroundModel model)
    {
        bool knownPrefix = id.StartsWith(prefix, StringComparison.Ordinal);
        if (!knownPrefix && ProviderPrefixes.Any(other => id.StartsWith(other, StringComparison.Ordinal))) return null;
        string body = (knownPrefix ? id[prefix.Length..] : id).Replace('_', '-');
        if (!ModelBody.IsMatch(body) || SpecializedVariant.IsMatch(body) || body.Split(['-', '.']).Any(Excluded.Contains))
            return null;

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

        var status = ReleaseSuffix.Match(body);
        string release = status.Groups["status"].Value;
        if (status.Success) body = body[..status.Index];
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
        // A documented family is useful even when a provider introduces a new ID layout.
        if (model.CatalogFamily?.ToLowerInvariant() is { } declared && FamilyName.IsMatch(declared)) family = declared;
        if (!FamilyName.IsMatch(family)) family = body;

        if (provider == PlaygroundProviderKind.Gemini)
        {
            // Google's named successors keep their old category for ranking.
            family = family switch { "flash-lite" => "lite", "hydrogen" => "flash", "carbon" => "pro", _ => family };
        }
        return new(model, family, id, version, !string.Equals(model.Lifecycle, "deprecated", StringComparison.OrdinalIgnoreCase),
            release is "preview" or "experimental" or "exp" or "beta" or "alpha", snapshot, release == "latest", date, ParseVersion(revision));
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
        int compared = a.Active.CompareTo(b.Active); if (compared != 0) return compared;
        compared = CompareNumbers(a.Version, b.Version); if (compared != 0) return compared;
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
