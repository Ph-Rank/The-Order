using System.Collections.Immutable;
using System.Text.Json;
using TheOrder.Application;

namespace TheOrder.Infrastructure.Online;

// No durable response cache: the service currently sends Cache-Control: no-store, private.
public sealed class NextMoeWalkthroughs(CatalogTransport transport) : IWalkthroughReader
{
    public Task<WalkthroughPage> ListAsync(string workId, string? cursor, bool showNsfw, CancellationToken cancellation)
    {
        RequireId(workId); if (!CatalogReleaseFacts.Cursor(cursor)) throw new OnlineException(OnlineFailure.InvalidRequest);
        return transport.Operation(async budget => {
            var query = Query(showNsfw); query["work_id"] = workId; query["limit"] = "50";
            if (cursor is not null) query["cursor"] = cursor;
            using var response = await transport.GetAsync("catalog/walkthroughs", query, budget).ConfigureAwait(false);
            var root = response.RootElement;
            var items = Array(root, "items", 50).Select(item => Summary(item, workId)).ToImmutableArray();
            var next = Text(root, "next_cursor", 512, optional: true);
            if (!CatalogReleaseFacts.Cursor(next)) Invalid();
            return new WalkthroughPage(items, next);
        }, cancellation);
    }
    public Task<WalkthroughDocument> ReadAsync(string id, string workId, bool showNsfw, CancellationToken cancellation)
    {
        RequireId(id); RequireId(workId);
        return transport.Operation(async budget => {
            using var response = await transport.GetAsync("catalog/walkthroughs/" + id, Query(showNsfw), budget).ConfigureAwait(false);
            var value = Parse(response.RootElement, workId);
            if (value.Summary.Id != id) Invalid();
            return value;
        }, cancellation);
    }
    private static Dictionary<string, string> Query(bool showNsfw) => new() { ["nsfw"] = showNsfw ? "true" : "false" };
    public static WalkthroughDocument Parse(JsonElement root, string workId)
    {
        var summary = Summary(root, workId);
        if (!root.TryGetProperty("chart", out var chart) || chart.ValueKind != JsonValueKind.Object) Invalid();
        var sections = Array(chart, "sections", 512).Select(s => new WalkthroughSection(Key(s), Label(s, "title"))).ToImmutableArray();
        var nodes = ImmutableArray.CreateBuilder<WalkthroughNode>();
        foreach (var entry in Array(chart, "entries", 512)) nodes.Add(new("entry", Key(entry), Label(entry, "label")));
        foreach (var save in Array(chart, "saves", 2048)) nodes.Add(new("save", Key(save), Label(save, "label")));
        foreach (var ending in Array(chart, "endings", 2048)) nodes.Add(new("ending", Key(ending), Label(ending, "name")) { EndingType=Text(ending,"ending_type",64,optional:true) });
        if (nodes.Count == 0 || nodes.Select(n => (n.Kind, n.Key)).Distinct().Count() != nodes.Count
            || sections.Select(s => s.Key).Distinct().Count() != sections.Length) Invalid();
        var lookup = nodes.ToDictionary(n => (n.Kind, n.Key));
        foreach (var entry in Array(chart, "entries", 512))
        {
            var unlock = Text(entry, "unlocked_by_ending_key", 128, optional: true);
            if (unlock is null) continue;
            if (!lookup.TryGetValue(("ending", unlock), out var prerequisite)) Invalid();
            var key = ("entry", Key(entry)); lookup[key] = lookup[key] with { Requirement = prerequisite!.Label.Display, RequirementKey = unlock };
        }
        var sectionKeys = sections.Select(s => s.Key).ToHashSet(StringComparer.Ordinal);
        var segments = ImmutableArray.CreateBuilder<WalkthroughSegment>(); int totalSteps = 0;
        foreach (var segment in Array(chart, "segments", 2048))
        {
            WalkthroughNode Node(string property)
            {
                if (!segment.TryGetProperty(property, out var node) || node.ValueKind != JsonValueKind.Object) { Invalid(); return null!; }
                var kind = Text(node, "node_type", 32)!;
                if (kind == "collect")
                {
                    if (Text(node, "key", 128, optional: true) is { Length: > 0 }) Invalid();
                    return new("collect", "", new("回收后结束", "回收后结束", false));
                }
                var key = (kind, Key(node));
                if (!lookup.TryGetValue(key, out var found)) { Invalid(); return null!; }
                return found;
            }
            var section = Text(segment, "section_key", 128, optional: true) ?? "";
            if (section.Length > 0 && !sectionKeys.Contains(section)) Invalid();
            var steps = ImmutableArray.CreateBuilder<WalkthroughStep>();
            foreach (var step in Array(segment, "steps", 4096))
            {
                if (++totalSteps > 16384) Invalid();
                var kind = Text(step, "step_type", 32)!;
                if (kind is not ("choice" or "action" or "save" or "load" or "note" or "heading")) Invalid();
                var save = Text(step, "save_key", 128, optional: true);
                if (save is not null && !lookup.ContainsKey(("save", save))) Invalid();
                var text = Label(step, "text", allowEmpty: true);
                if (save is not null && text.Original.Length == 0) text = lookup[("save", save)].Label;
                if (text.Original.Length == 0 && text.Display.Length == 0) Invalid();
                var alternatives = Array(step, "alternatives", 128, optional: true).Select(a => Label(a, "text")).ToImmutableArray();
                bool achievement = step.TryGetProperty("achievement", out var flag) && flag.ValueKind == JsonValueKind.True;
                steps.Add(new(kind, text, save, alternatives, achievement));
            }
            segments.Add(new(Key(segment), section, Node("from_node"), Node("to_node"),
                Label(segment, "condition", allowEmpty: true, localizedProperty: "condition_localized"), steps.ToImmutable()));
        }
        if (segments.Count == 0 || segments.Select(s => s.Key).Distinct().Count() != segments.Count) Invalid();
        return new(summary, root.TryGetProperty("work_route_hints", out var hints) && hints.ValueKind == JsonValueKind.True,
            sections, lookup.Values.ToImmutableArray(), segments.ToImmutable());
    }
    private static WalkthroughSummary Summary(JsonElement item, string workId)
    {
        string id = Text(item, "id", 20)!, actualWork = Text(item, "work_id", 20)!;
        RequireId(id); if (actualWork != workId) Invalid();
        var url = Text(item, "source_url", 2048, optional: true);
        if (url is not null && (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || uri.UserInfo.Length > 0)) Invalid();
        var release = "未注明适用版本";
        if (item.TryGetProperty("release", out var version) && version.ValueKind == JsonValueKind.Object)
            release = Text(version, "title", 512, optional: true) ?? Text(version, "name", 512, optional: true) ?? release;
        return new(id, workId, Text(item, "source", 128)!, url, Text(item, "lang", 32)!, release,
            Count(item, "ending_count"), Count(item, "save_count"), Count(item, "segment_count"), Text(item, "updated_at", 64, optional: true) ?? "");
    }
    private static int Count(JsonElement value, string name)
    {
        if (!value.TryGetProperty(name, out var field)) return 0;
        if (field.ValueKind != JsonValueKind.Number || !field.TryGetInt32(out var count) || count is < 0 or > 100000) { Invalid(); return 0; }
        return count;
    }
    private static string Key(JsonElement item) => Text(item, "key", 128)!;
    private static WalkthroughText Label(JsonElement item, string field, bool allowEmpty = false, string localizedProperty = "localized")
    {
        var original = Text(item, field, 8192, optional: allowEmpty) ?? "";
        var display = original; bool machine = false;
        if (item.TryGetProperty(localizedProperty, out var translations) && translations.ValueKind == JsonValueKind.Object
            && translations.TryGetProperty("zh-Hans", out var chinese) && chinese.ValueKind == JsonValueKind.Object)
        {
            display = Text(chinese, "value", 8192, optional: true) ?? original;
            machine = chinese.TryGetProperty("is_machine", out var flag) && flag.ValueKind == JsonValueKind.True;
        }
        return new(original, display, machine);
    }
    private static string? Text(JsonElement value, string name, int maximum, bool optional = false)
    {
        if (value.ValueKind != JsonValueKind.Object) { Invalid(); return null; }
        if (!value.TryGetProperty(name, out var field) || field.ValueKind == JsonValueKind.Null)
        { if (!optional) Invalid(); return null; }
        if (field.ValueKind != JsonValueKind.String) { Invalid(); return null; }
        string text = field.GetString()!;
        if (text.Length > maximum || text.Contains('\0') || !optional && text.Length == 0) Invalid();
        return text;
    }
    private static IEnumerable<JsonElement> Array(JsonElement value, string name, int maximum, bool optional = false)
    {
        if (value.ValueKind != JsonValueKind.Object) { Invalid(); return []; }
        if (!value.TryGetProperty(name, out var field)) { if (!optional) Invalid(); return []; }
        if (field.ValueKind != JsonValueKind.Array || field.GetArrayLength() > maximum) { Invalid(); return []; }
        return field.EnumerateArray().ToArray();
    }
    private static void RequireId(string value) { if (!CatalogReleaseFacts.Id(value)) throw new OnlineException(OnlineFailure.InvalidRequest); }
    private static void Invalid() => throw new OnlineException(OnlineFailure.InvalidResponse);
}
