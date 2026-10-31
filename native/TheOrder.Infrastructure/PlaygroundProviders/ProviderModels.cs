using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using TheOrder.Application.Playground;

namespace TheOrder.Infrastructure.PlaygroundProviders;

internal sealed partial class NativePlaygroundProvider
{
    public async Task<ImmutableArray<PlaygroundModel>> ListModelsAsync(string endpoint, CancellationToken cancellation)
    {
        endpoint = PlaygroundProviderEndpoints.Normalize(Kind, endpoint);
        var result = ImmutableArray.CreateBuilder<PlaygroundModel>();
        var modelIds = new HashSet<string>(StringComparer.Ordinal);
        var pages = new HashSet<string>(StringComparer.Ordinal);
        string path = Kind == PlaygroundProviderKind.Gemini ? "models?pageSize=100"
            : Kind == PlaygroundProviderKind.Claude ? "models?limit=100" : "models";
        try
        {
            while (true)
            {
                cancellation.ThrowIfCancellationRequested();
                // This is a directory byte/page boundary, not a paid tool-call
                // allowance. Never return a silently truncated partial catalog.
                if (pages.Count >= 100 || !pages.Add(path)) throw Invalid("模型目录分页重复或超过安全容量；未返回不完整列表。");
                using var response = await RequestAsync(endpoint, path, HttpMethod.Get, null, cancellation).ConfigureAwait(false);
                var json = await ReadJsonAsync(response, cancellation).ConfigureAwait(false);
                if (json is not JsonObject || json[Kind == PlaygroundProviderKind.Gemini ? "models" : "data"] is not JsonArray models)
                    throw Invalid("模型目录缺少有效列表；未将接口异常当成空目录。");
                foreach (var item in models)
                {
                    if (item is not JsonObject) throw Invalid("模型目录条目格式异常。");
                    string id;
                    try { id = CleanModel(String(item[Kind == PlaygroundProviderKind.Gemini ? "name" : "id"])); }
                    catch (PlaygroundProviderException) { throw Invalid("模型目录包含无效编号；未将格式异常当作空目录。"); }
                    if (!modelIds.Add(id)) continue;
                    if (result.Count >= 10000) throw Invalid("模型目录超过安全容量；未返回不完整列表。");
                    var methods = ModelMethods(item, out bool methodsReported);
                    string name = String(item[Kind == PlaygroundProviderKind.Gemini ? "displayName" : "display_name"], String(item["display_name"], id));
                    result.Add(new(id, name.Length > 240 ? name[..240] : name, Capabilities(id, endpoint))
                    {
                        Version = ModelString(item["version"], 128), CreatedAt = ModelCreatedAt(item),
                        GenerationMethods = methods, GenerationMethodsReported = methodsReported,
                        Lifecycle = ModelString(item["lifecycle"], 64)
                    });
                }
                string next;
                if (Kind == PlaygroundProviderKind.Gemini)
                {
                    next = ModelString(json["nextPageToken"], 1000) ?? "";
                    if (next.Length == 0) return result.ToImmutable();
                    path = "models?pageSize=100&pageToken=" + Uri.EscapeDataString(next);
                }
                else if (Kind == PlaygroundProviderKind.Claude)
                {
                    if (json["has_more"] is not JsonValue flag || !flag.TryGetValue<bool>(out bool more))
                        throw Invalid("Claude 模型目录缺少有效分页终态；未返回不完整列表。");
                    if (!more) return result.ToImmutable();
                    next = ModelString(json["last_id"], 1000) ?? "";
                    if (next.Length == 0 || models.Count == 0) throw Invalid("Claude 模型目录缺少下一页游标；未返回不完整列表。");
                    path = "models?limit=100&after_id=" + Uri.EscapeDataString(next);
                }
                else
                {
                    // These official /models endpoints currently return one
                    // complete list; don't guess a foreign pagination protocol.
                    if (Boolean(json["has_more"]) || !string.IsNullOrEmpty(String(json["nextPageToken"])) || !string.IsNullOrEmpty(String(json["next_page"])))
                        throw Invalid("模型目录返回了未支持的分页方式；未将部分目录当成完整结果。");
                    return result.ToImmutable();
                }
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception error) when (error is JsonException or InvalidOperationException or FormatException or IOException)
        { throw Invalid("模型目录响应异常或读取中断；未使用旧列表或把失败当作空结果。"); }
    }

    private static string? ModelString(JsonNode? value, int maximum)
    {
        if (value is null) return null;
        if (value is not JsonValue scalar || !scalar.TryGetValue<string>(out string? text)
            || text is null || text.Length > maximum || text.Any(char.IsControl)) throw Invalid("模型目录元数据格式异常。");
        return text;
    }
    private static ImmutableArray<string> ModelMethods(JsonNode item, out bool reported)
    {
        reported = false; var result = ImmutableArray.CreateBuilder<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (string field in new[] { "supportedGenerationMethods", "supported_generation_methods", "supported_actions", "supportedActions" })
        {
            if (item[field] is null) continue;
            reported = true;
            if (item[field] is not JsonArray values || values.Count > 64) throw Invalid("模型生成方法元数据格式异常。");
            foreach (var value in values)
            {
                string method = ModelString(value, 128) ?? throw Invalid("模型生成方法为空。");
                if (seen.Add(method)) result.Add(method);
            }
        }
        return result.ToImmutable();
    }
    private static DateTimeOffset? ModelCreatedAt(JsonNode item)
    {
        var value = item["created_at"] ?? item["created"];
        if (value is null) return null;
        if (value is JsonValue scalar && scalar.TryGetValue<string>(out string? text)
            && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var date)) return date;
        if (Integer(value) is { } unix && unix <= 253402300799) return DateTimeOffset.FromUnixTimeSeconds(unix);
        throw Invalid("模型创建时间元数据格式异常。");
    }
}
