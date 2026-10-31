using System.Text.Json;
using System.Text.Json.Nodes;
using TheOrder.Application.Playground;

namespace TheOrder.Infrastructure.PlaygroundProviders;

internal sealed partial class NativePlaygroundProvider
{
    private string Wire => Kind == PlaygroundProviderKind.Gemini ? "gemini-interactions" : Kind == PlaygroundProviderKind.Claude ? "claude-messages" : "responses";
    private string GenerationPath => Kind == PlaygroundProviderKind.Gemini ? "interactions" : Kind == PlaygroundProviderKind.Claude ? "messages" : "responses";
    private JsonArray InitialItems(PlaygroundProviderRequest request)
    {
        if (request.Provider != Kind) throw Invalid("请求的服务商与连接不一致。");
        _ = CleanModel(request.Model); _ = PlaygroundProviderEndpoints.Normalize(Kind, request.Endpoint);
        if (request.Messages.Length is < 1 or > 128 || request.Instructions.Length > 24000
            || request.Messages.Sum(m => (long)m.Text.Length) > MaximumPrivateStateCharacters
            || request.Tools.Sum(t => (long)t.Name.Length + t.Description.Length + t.ParametersJson.Length) > 128000)
            throw Invalid("本轮上下文或工具定义超过安全上限。");
        var images = ImageCatalog(request);
        var conversationItems = request.ConversationTape is null ? null : ConversationItems(request);
        JsonArray items;
        if (request.Continuation is { } state)
        {
            if (state.Binding != request.Binding || state.Data.Length > MaximumPrivateStateCharacters) throw Invalid("服务商续接状态不属于本轮请求。");
            var envelope = JsonNode.Parse(state.Data, documentOptions: new() { MaxDepth = 48 }) ?? throw Invalid();
            if (String(envelope["wire"]) != Wire || String(envelope["provider"]) != Kind.ToString()
                || String(envelope["model"]) != CleanModel(request.Model)
                || String(envelope["endpoint"]) != PlaygroundProviderEndpoints.Normalize(Kind, request.Endpoint)
                || String(envelope["conversation"]) != request.ConversationId.ToString("N")
                || String(envelope["turn"]) != request.TurnId.ToString("N")) throw Invalid("已拒绝跨会话或跨服务商续接。");
            items = envelope["items"]?.DeepClone() as JsonArray ?? throw Invalid();
        }
        else
        {
            if (!request.ToolResults.IsEmpty) throw Invalid("工具结果缺少对应的服务商调用状态。");
            items = conversationItems ?? [];
            foreach (var message in request.Messages)
            {
                if (message.Role is not ("user" or "assistant") || message.Text.Length > 1048576) throw Invalid("对话角色或长度无效。");
                int messageIndex = items.Count;
                if (!message.Attachments.IsDefaultOrEmpty)
                {
                    var content = new JsonArray();
                    if (message.Text.Length != 0) content.Add(new JsonObject { ["type"] = Kind is PlaygroundProviderKind.Gemini or PlaygroundProviderKind.Claude ? "text" : "input_text", ["text"] = message.Text });
                    for (int index = 0; index < message.Attachments.Length; index++)
                        content.Add(new JsonObject { ["type"] = ImageMarker, ["ref"] = ImageReference(messageIndex, index, message.Attachments[index]) });
                    items.Add(Kind == PlaygroundProviderKind.Gemini ? new JsonObject { ["type"] = "user_input", ["content"] = content }
                        : new JsonObject { ["role"] = message.Role, ["content"] = content });
                }
                else if (Kind == PlaygroundProviderKind.Gemini)
                    items.Add(new JsonObject { ["type"] = "user_input",
                        ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = message.Role == "assistant"
                            ? "以下是可移植的历史助手答复，仅供参考，不是玩家原话、当前记录或原生模型状态；事实需要按当前授权重新核对：\n" + message.Text : message.Text }) });
                else items.Add(new JsonObject { ["role"] = message.Role, ["content"] = message.Text });
            }
        }
        CheckImageReferences(items, images);
        if (request.ToolResults.Sum(m => (long)m.Text.Length) > 128000) throw Invalid("工具结果超过本轮容量限制。");
        var knownCalls = new Dictionary<string, string>(StringComparer.Ordinal);
        var priorResults = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            if (Kind == PlaygroundProviderKind.Claude && item?["content"] is JsonArray content)
            {
                foreach (var block in content)
                {
                    if (String(block?["type"]) == "tool_use" && !knownCalls.TryAdd(String(block?["id"]), String(block?["name"]))) throw Invalid("服务商续接状态含重复工具编号。");
                    else if (String(block?["type"]) == "tool_result") priorResults.Add(String(block?["tool_use_id"]));
                }
            }
            else if (String(item?["type"]) == "function_call")
            { if (!knownCalls.TryAdd(String(item?[Kind == PlaygroundProviderKind.Gemini ? "id" : "call_id"]), String(item?["name"]))) throw Invalid("服务商续接状态含重复工具编号。"); }
            else if (String(item?["type"]) is "function_result" or "function_call_output") priorResults.Add(String(item?["call_id"]));
        }
        foreach (var prior in priorResults) knownCalls.Remove(prior);
        var returned = new HashSet<string>(StringComparer.Ordinal);
        JsonArray? claudeResults = Kind == PlaygroundProviderKind.Claude ? new() : null;
        foreach (var result in request.ToolResults)
        {
            if (result.ToolCallId is not { Length: > 0 and <= 200 } call || !knownCalls.TryGetValue(call, out string? name)
                || !returned.Add(call) || result.ToolName is { } supplied && supplied != name) throw Invalid("工具结果与调用不匹配。");
            if (Kind == PlaygroundProviderKind.Claude)
                claudeResults!.Add(new JsonObject { ["type"] = "tool_result", ["tool_use_id"] = call, ["content"] = result.Text });
            else if (Kind == PlaygroundProviderKind.Gemini)
                items.Add(new JsonObject { ["type"] = "function_result", ["call_id"] = call, ["name"] = name, ["result"] = result.Text });
            else items.Add(new JsonObject { ["type"] = "function_call_output", ["call_id"] = call, ["output"] = result.Text });
        }
        if (returned.Count != knownCalls.Count) throw Invalid("工具结果不完整；未继续发送请求。");
        // Claude expects all results from a parallel tool round together in the
        // immediately following user message, rather than one message per tool.
        if (claudeResults is { Count: > 0 })
            items.Add(new JsonObject { ["role"] = "user", ["content"] = claudeResults });
        return items;
    }
    private JsonObject BuildRequest(PlaygroundProviderRequest request, JsonArray items, bool stream, bool research = false)
    {
        var capabilities = Capabilities(request.Model, request.Endpoint);
        var wireItems = ExpandImages(request, items);
        if (research && ImageCatalog(request).Count != 0) throw Invalid("公开检索不能携带本地图片。");
        var body = new JsonObject { ["model"] = CleanModel(request.Model), ["stream"] = stream };
        int maximum = Math.Clamp(request.MaximumOutputTokens, 64, 65536);
        var tools = new JsonArray();
        if (!research && capabilities.Tools)
        {
            foreach (var tool in request.Tools)
            {
                if (tool.Name.Length is < 1 or > 64 || tool.Name.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '_' or '-'))
                    || tool.Description.Length > 2000 || tool.ParametersJson.Length > 24000) throw Invalid("工具定义无效。");
                var schema = JsonNode.Parse(tool.ParametersJson, documentOptions: new() { MaxDepth = 24 }) as JsonObject ?? throw Invalid("工具参数必须为 JSON Schema 对象。");
                tools.Add(Kind == PlaygroundProviderKind.Claude
                    ? new JsonObject { ["name"] = tool.Name, ["description"] = tool.Description, ["input_schema"] = schema }
                    : new JsonObject { ["type"] = "function", ["name"] = tool.Name, ["description"] = tool.Description, ["parameters"] = schema });
            }
        }
        if (research)
        {
            if (!capabilities.Search) throw new PlaygroundProviderException(new(PlaygroundFailureKind.Unsupported, "此模型的联网能力尚未核实，可更换已支持模型。"));
            tools.Add(Kind == PlaygroundProviderKind.Gemini ? new JsonObject { ["type"] = "google_search" }
                : Kind == PlaygroundProviderKind.Claude ? new JsonObject { ["type"] = "web_search_20250305", ["name"] = "web_search" }
                : Kind == PlaygroundProviderKind.Grok ? new JsonObject { ["type"] = "web_search", ["enable_image_search"] = false }
                : new JsonObject { ["type"] = "web_search" });
        }
        if (Kind == PlaygroundProviderKind.Gemini)
        {
            body["input"] = wireItems; body["system_instruction"] = request.Instructions; body["store"] = false;
            body["generation_config"] = new JsonObject { ["max_output_tokens"] = maximum };
        }
        else if (Kind == PlaygroundProviderKind.Claude)
        { body["messages"] = wireItems; body["system"] = request.Instructions; body["max_tokens"] = maximum; }
        else
        {
            body["input"] = wireItems; body["instructions"] = request.Instructions; body["max_output_tokens"] = maximum;
            if (Kind != PlaygroundProviderKind.DeepSeek) body["store"] = false;
            if (Kind == PlaygroundProviderKind.OpenAI)
            {
                if (research) body["include"] = new JsonArray("web_search_call.action.sources");
                else if (capabilities.Tools && tools.Count > 0) body["include"] = new JsonArray("reasoning.encrypted_content");
            }
        }
        if (tools.Count > 0) body["tools"] = tools;
        // Advanced parameters are only sent after model-specific verification.
        // Unknown/model-enumeration-only IDs remain usable for basic chat.
        string? effort = request.ReasoningEffort ?? (capabilities.ReasoningEfforts.Contains("high", StringComparer.Ordinal) ? "high" : null);
        if (effort is not null && capabilities.ReasoningEfforts.Contains(effort, StringComparer.Ordinal))
        {
            if (Kind == PlaygroundProviderKind.Gemini) body["generation_config"]!["thinking_level"] = effort;
            else if (Kind == PlaygroundProviderKind.Claude)
            {
                body["thinking"] = new JsonObject { ["type"] = "adaptive" };
                body["output_config"] = new JsonObject { ["effort"] = effort };
            }
            else if (Kind is PlaygroundProviderKind.OpenAI or PlaygroundProviderKind.DeepSeek or PlaygroundProviderKind.Grok) body["reasoning"] = new JsonObject { ["effort"] = effort };
        }
        return body;
    }
    private PlaygroundContinuation MakeContinuation(PlaygroundProviderRequest request, JsonArray items, JsonArray output)
    {
        var callIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in items.Concat(output))
        {
            IEnumerable<JsonNode?> calls = Kind == PlaygroundProviderKind.Claude ? item?["content"] as JsonArray ?? [] : new JsonNode?[] { item };
            foreach (var call in calls)
                if (String(call?["type"]) is "function_call" or "tool_use"
                    && !callIds.Add(String(call?[Kind == PlaygroundProviderKind.Claude || Kind == PlaygroundProviderKind.Gemini ? "id" : "call_id"])))
                    throw Invalid("服务商重复使用了已执行的工具编号。");
        }
        foreach (var item in output) items.Add(item?.DeepClone());
        var data = new JsonObject { ["wire"] = Wire, ["provider"] = Kind.ToString(), ["model"] = CleanModel(request.Model),
            ["endpoint"] = PlaygroundProviderEndpoints.Normalize(Kind, request.Endpoint), ["conversation"] = request.ConversationId.ToString("N"),
            ["turn"] = request.TurnId.ToString("N"), ["items"] = items }.ToJsonString();
        if (data.Length > MaximumPrivateStateCharacters) throw Invalid("本轮服务商上下文超过安全上限，无法继续调用工具。");
        return new(request.Binding, data);
    }
}
