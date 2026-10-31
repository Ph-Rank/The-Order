using System.Collections.Immutable;
using System.Buffers;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using TheOrder.Application.Playground;

namespace TheOrder.Infrastructure.PlaygroundProviders;

internal sealed partial class NativePlaygroundProvider
{
    public async IAsyncEnumerable<PlaygroundProviderEvent> SendAsync(PlaygroundProviderRequest request,
        [EnumeratorCancellation] CancellationToken cancellation)
    {
        await using var enumerator = SendCore(request, cancellation).GetAsyncEnumerator(cancellation);
        while (true)
        {
            bool next = false; PlaygroundFailure? failure = null;
            try { next = await enumerator.MoveNextAsync().ConfigureAwait(false); }
            catch (PlaygroundProviderException error) { failure = error.Failure; }
            catch (OperationCanceledException) { failure = new(PlaygroundFailureKind.Cancelled, "本次回复已停止。"); }
            catch (Exception error) when (error is JsonException or InvalidDataException or DecoderFallbackException or InvalidOperationException or FormatException)
            { failure = new(PlaygroundFailureKind.InvalidResponse, "回复数据中断或格式异常，已保留收到的正文；未自动重发请求。"); }
            catch (Exception error) when (error is HttpRequestException or IOException)
            { failure = new(PlaygroundFailureKind.Network, "连接已中断，已保留收到的正文；可手动重试。", true); }
            if (failure is not null) { yield return new(PlaygroundProviderEventKind.Error) { Failure = failure }; yield break; }
            if (!next) yield break;
            yield return enumerator.Current;
        }
    }
    private async IAsyncEnumerable<PlaygroundProviderEvent> SendCore(PlaygroundProviderRequest request,
        [EnumeratorCancellation] CancellationToken cancellation)
    {
        if (request.Phase != PlaygroundPhase.Discussion) throw new PlaygroundProviderException(new(PlaygroundFailureKind.Unsupported, "公开检索必须使用独立的检索入口。"));
        JsonArray items = InitialItems(request); JsonObject body = BuildRequest(request, items, true);
        var round = new ProviderRound(this, request);
        using var response = await RequestAsync(request.Endpoint, GenerationPath, HttpMethod.Post, body, cancellation).ConfigureAwait(false);
        if (response.Content.Headers.ContentType?.MediaType?.Equals("text/event-stream", StringComparison.OrdinalIgnoreCase) != true)
        {
            var json = await ReadJsonAsync(response, cancellation).ConfigureAwait(false);
            foreach (var ev in round.Final(json)) yield return ev;
        }
        else
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellation).ConfigureAwait(false);
            await foreach (var packet in PlaygroundSse.ReadAsync(stream, cancellation).ConfigureAwait(false))
            {
                if (packet.Data == "[DONE]") continue; // Responses/Claude/Gemini still need their typed terminal.
                JsonNode json = JsonNode.Parse(packet.Data, documentOptions: new() { MaxDepth = 48 }) ?? throw Invalid();
                foreach (var ev in round.Apply(json)) yield return ev;
                if (round.Terminal) break;
            }
        }
        cancellation.ThrowIfCancellationRequested();
        foreach (var ev in round.CompleteLegacyGeminiStream()) yield return ev;
        if (!round.Terminal) throw Invalid("回复流在终态事件前中断，半截回复不会标为完成。");
        if (round.Failure is { } failure)
        {
            // A rejected/truncated server turn can still carry billed usage.
            // Deliver that metadata once before reporting the original error.
            if (round.Usage is { } failedUsage) yield return new(PlaygroundProviderEventKind.Usage) { Usage = failedUsage };
            throw new PlaygroundProviderException(failure);
        }
        // Even syntactically complete arguments are not actionable when the
        // provider declares the response truncated. The coordinator keeps the
        // partial text and marks the turn Interrupted.
        var calls = round.StopReason == "max_tokens" ? ImmutableArray<PlaygroundToolCall>.Empty : round.ToolCalls();
        if (!calls.IsEmpty)
        {
            yield return new(PlaygroundProviderEventKind.Continuation) { Continuation = MakeContinuation(request, items, round.Output) };
            foreach (var call in calls) yield return new(PlaygroundProviderEventKind.ToolCall) { ToolCall = call };
        }
        else if (round.StopReason == "end_turn" && !round.Refused && MakeConversationTape(request, items, round.Output) is { } tape)
            yield return new(PlaygroundProviderEventKind.ConversationTape) { ConversationTape = tape };
        foreach (var source in round.Sources()) yield return new(PlaygroundProviderEventKind.Source) { Source = source };
        if (round.Usage is { } usage) yield return new(PlaygroundProviderEventKind.Usage) { Usage = usage };
        if (round.Refused) yield return new(PlaygroundProviderEventKind.Refusal) { Text = "所选模型拒绝回答本次请求。" };
        yield return new(PlaygroundProviderEventKind.Completed) { StopReason = !calls.IsEmpty ? "tool_calls" : round.Refused ? "refusal" : round.StopReason };
    }

    private sealed class ProviderRound(NativePlaygroundProvider owner, PlaygroundProviderRequest request)
    {
        private readonly StringBuilder text = new();
        private readonly SortedDictionary<int, JsonObject> blocks = [];
        private readonly HashSet<int> geminiClosedSteps = [];
        private bool geminiStreaming;
        private JsonNode? geminiLegacyTerminal;
        private long? geminiVisibleOutputTokens;
        private long? claudeUncachedInputTokens;
        private string? billingModel, serviceTier;
        private readonly Dictionary<int, StringBuilder> arguments = [];
        private sealed record Citation(JsonObject Value, int? Offset, bool GeminiRangeMapped = false);
        private readonly List<Citation> citations = [];
        private readonly HashSet<string> ambiguousGeminiAnnotations = new(StringComparer.Ordinal);
        private readonly Dictionary<long, string> sequence = [];
        private readonly HashSet<string> suggestions = new(StringComparer.Ordinal);
        public bool Terminal { get; private set; }
        public bool HostedSearch { get; private set; }
        public bool Refused { get; private set; }
        public string StopReason { get; private set; } = "end_turn";
        public PlaygroundFailure? Failure { get; private set; }
        public PlaygroundUsage? Usage { get; private set; }
        public JsonArray Output { get; private set; } = [];
        public string Text => text.ToString();
        public string? SearchSuggestionsHtml { get; private set; }
        private PlaygroundProviderEvent TextEvent(string delta)
        {
            if (text.Length + delta.Length > Math.Clamp(request.MaximumOutputTokens, 64, 65536) * 16L)
                throw Invalid("本轮输出超过安全上限。");
            text.Append(delta); return new(PlaygroundProviderEventKind.Text) { Text = delta };
        }
        public IEnumerable<PlaygroundProviderEvent> Apply(JsonNode json)
        {
            string type = String(json[owner.Kind == PlaygroundProviderKind.Gemini ? "event_type" : "type"]);
            if (json["sequence_number"] is JsonValue s && s.TryGetValue<long>(out long number))
            {
                string value = json.ToJsonString();
                if (sequence.TryGetValue(number, out string? prior))
                { if (prior != value) throw Invalid("回复事件序号冲突。"); yield break; }
                if (sequence.Count >= 100000) throw Invalid(); sequence[number] = value;
            }
            if (Terminal) yield break;
            if (type == "error" || type == "response.failed")
            { ReadUsage(json["response"] ?? json); Failure = new(StructuredErrorKind(json["error"] ?? json["response"]?["error"]) ?? PlaygroundFailureKind.Network, "服务商在生成过程中返回错误；未自动重发请求。"); Terminal = true; yield break; }
            if (owner.Kind == PlaygroundProviderKind.Claude)
            {
                if (type == "message_start") ReadUsage(json["message"]);
                else if (type == "content_block_start")
                {
                    int index = Index(json); var block = json["content_block"]?.DeepClone() as JsonObject ?? throw Invalid(); blocks[index] = block;
                    string blockType = String(block["type"]);
                    if (blockType is "tool_use" or "server_tool_use")
                    { arguments[index] = new(); if (blockType == "server_tool_use") { HostedSearch = true; yield return Activity(); } }
                    if (blockType == "text" && String(block["text"]) is { Length: > 0 } initial) yield return TextEvent(initial);
                    SearchData(block);
                }
                else if (type == "content_block_delta")
                {
                    int index = Index(json); if (!blocks.TryGetValue(index, out var block)) throw Invalid();
                    var delta = json["delta"] ?? throw Invalid();
                    switch (String(delta["type"]))
                    {
                        case "text_delta": string value = String(delta["text"]); block["text"] = String(block["text"]) + value; yield return TextEvent(value); break;
                        case "input_json_delta": AppendArguments(index, String(delta["partial_json"])); break;
                        case "citations_delta": if (delta["citation"] is JsonObject citation) citations.Add(new(citation.DeepClone().AsObject(), null)); break;
                        case "signature_delta": block["signature"] = String(block["signature"]) + String(delta["signature"]); break;
                        case "thinking_delta": block["thinking"] = String(block["thinking"]) + String(delta["thinking"]); break;
                    }
                }
                else if (type == "content_block_stop") FinalizeArguments(Index(json), "input");
                else if (type == "message_delta")
                {
                    StopReason = String(json["delta"]?["stop_reason"], StopReason); MergeUsage(json["usage"]);
                    if (String(json["delta"]?["stop_details"]?["type"]) == "refusal" || StopReason == "refusal") Refused = true;
                }
                else if (type == "message_stop")
                {
                    Terminal = true; Output = new(new JsonObject { ["role"] = "assistant", ["content"] = new JsonArray(blocks.Values.Select(v => v.DeepClone()).ToArray()) });
                    if (StopReason == "pause_turn") Failure = new(PlaygroundFailureKind.InvalidResponse, "服务端检索暂停，未自动继续发送计费请求。");
                    CollectOutputCitations();
                }
                yield break;
            }
            if (owner.Kind == PlaygroundProviderKind.Gemini)
            {
                geminiStreaming = true;
                if (geminiLegacyTerminal is not null && type.StartsWith("step.", StringComparison.Ordinal))
                    throw Invalid("服务商在完成状态之后继续改变回复步骤。");
                if (type == "step.start")
                {
                    int index = Index(json); var step = json["step"]?.DeepClone() as JsonObject ?? throw Invalid(); blocks[index] = step;
                    if (String(step["type"]) == "function_call") arguments[index] = new();
                    if (String(step["type"]) == "model_output")
                        foreach (string initial in TextParts(step)) if (initial.Length > 0) yield return TextEvent(initial);
                    SearchData(step);
                }
                else if (type == "step.delta")
                {
                    int index = Index(json); if (!blocks.TryGetValue(index, out var step)) throw Invalid();
                    var delta = json["delta"] ?? throw Invalid(); string deltaType = String(delta["type"]);
                    if (deltaType == "arguments_delta") AppendArguments(index, String(delta["arguments"]));
                    else if (deltaType == "text" && String(step["type"]) == "model_output")
                    {
                        string value = String(delta["text"]); var content = step["content"] as JsonArray;
                        if (content is null) { content = []; step["content"] = content; }
                        var part = content.LastOrDefault() as JsonObject;
                        if (part is null || String(part["type"]) != "text") { part = new() { ["type"] = "text", ["text"] = "" }; content.Add(part); }
                        part["text"] = String(part["text"]) + value;
                        if (delta["annotations"] is JsonArray ann)
                        {
                            if (part["annotations"] is not JsonArray existing) { existing = []; part["annotations"] = existing; }
                            foreach (var annotation in ann) existing.Add(annotation?.DeepClone());
                        }
                        yield return TextEvent(value);
                    }
                    else if (deltaType == "text_annotation_delta" && String(step["type"]) == "model_output")
                    {
                        // GA has a separate annotation delta. Its annotations
                        // belong to text content, not to the step root.
                        if (delta["annotations"] is JsonArray annotations)
                        {
                            if (step["content"] is not JsonArray content) { content = []; step["content"] = content; }
                            var part = content.LastOrDefault() as JsonObject;
                            if (part is null || String(part["type"]) != "text")
                            { part = new() { ["type"] = "text", ["text"] = "" }; content.Add(part); }
                            if (part["annotations"] is not JsonArray existing) { existing = []; part["annotations"] = existing; }
                            bool ambiguous = content.Count(p => String(p?["type"]) == "text") != 1;
                            foreach (var annotation in annotations)
                            {
                                var copy = annotation?.DeepClone(); existing.Add(copy);
                                if (ambiguous && copy is JsonObject copied) ambiguousGeminiAnnotations.Add(copied.ToJsonString());
                            }
                        }
                    }
                    else if (deltaType == "thought_summary" && String(step["type"]) == "thought")
                    {
                        // A summary delta adds a content item. Preserve the
                        // native summary hierarchy for stateless replay.
                        if (delta["content"] is { } summary)
                        {
                            if (step["summary"] is not JsonArray summaries) { summaries = []; step["summary"] = summaries; }
                            summaries.Add(summary.DeepClone());
                        }
                    }
                    else
                    {
                        foreach (var field in delta.AsObject()) if (field.Key != "type") step[field.Key] = field.Value?.DeepClone();
                        SearchData(step);
                    }
                }
                else if (type == "step.stop")
                {
                    int index = Index(json); if (!blocks.ContainsKey(index) || !geminiClosedSteps.Add(index)) throw Invalid("回复包含无效或重复的步骤终态。");
                    FinalizeArguments(index, "arguments"); MergeUsage(json["usage"]);
                }
                else if (type == "interaction.created") ReadUsage(json["interaction"]);
                else if (type is "interaction.status_update" or "interaction.completed")
                {
                    // GA emits a typed completion carrying a partial Interaction,
                    // without steps. Older servers used the status-update event.
                    // Neither EOF nor the trailing [DONE] is a success signal.
                    var interaction = type == "interaction.completed" ? json["interaction"] ?? throw Invalid("服务商终态缺少 interaction 数据。") : json;
                    string status = String(interaction["status"]); ReadUsage(interaction);
                    if (status is "completed" or "requires_action" or "incomplete" or "failed" or "cancelled" or "requires_continuation")
                    {
                        if (type == "interaction.completed")
                            foreach (var ev in Final(interaction)) yield return ev;
                        else
                        {
                            // GA can emit a status update before its typed final
                            // event. Keep reading for the final usage and steps.
                            // A normal EOF with an explicit older terminal is
                            // the sole fallback for legacy-server compatibility.
                            if (geminiLegacyTerminal is not null && String(geminiLegacyTerminal["status"]) != status)
                                throw Invalid("服务商完成状态互相冲突。");
                            geminiLegacyTerminal = interaction.DeepClone();
                        }
                    }
                    else if (type == "interaction.completed") throw Invalid("服务商终态缺少有效完成状态。");
                }
                yield break;
            }
            // OpenAI, xAI and DeepSeek use Responses events, but request fields,
            // feature admission and model capabilities remain provider-specific.
            if (type == "response.output_item.added")
            {
                int index = Index(json, "output_index"); var item = json["item"]?.DeepClone() as JsonObject ?? throw Invalid(); blocks[index] = item;
                if (String(item["type"]) == "function_call") arguments[index] = new();
                SearchData(item);
            }
            else if (type == "response.output_text.delta") yield return TextEvent(String(json["delta"]));
            else if (type == "response.function_call_arguments.delta") AppendArguments(Index(json, "output_index"), String(json["delta"]));
            else if (type == "response.function_call_arguments.done")
            { int index = Index(json, "output_index"); if (!blocks.TryGetValue(index, out var item)) throw Invalid(); item["arguments"] = String(json["arguments"]); }
            else if (type == "response.output_item.done")
            { int index = Index(json, "output_index"); if (json["item"] is JsonObject item) { blocks[index] = item.DeepClone().AsObject(); SearchData(item); } }
            else if (type == "response.output_text.annotation.added" && json["annotation"] is JsonObject annotation) citations.Add(new(annotation.DeepClone().AsObject(), null));
            else if (type == "response.refusal.delta") Refused = true;
            else if (type.StartsWith("response.web_search_call.", StringComparison.Ordinal)) { HostedSearch = true; yield return Activity(); }
            else if (type is "response.completed" or "response.incomplete")
            { foreach (var ev in Final(json["response"] ?? throw Invalid())) yield return ev; }
        }
        public IEnumerable<PlaygroundProviderEvent> CompleteLegacyGeminiStream()
        {
            if (!Terminal && owner.Kind == PlaygroundProviderKind.Gemini && geminiLegacyTerminal is { } legacy)
                foreach (var ev in Final(legacy)) yield return ev;
        }
        public IEnumerable<PlaygroundProviderEvent> Final(JsonNode root)
        {
            if (root["error"] is not null) { ReadUsage(root); Failure = new(PlaygroundFailureKind.InvalidResponse, "服务商未完成本次回复。"); Terminal = true; yield break; }
            if (owner.Kind == PlaygroundProviderKind.Claude)
            {
                var content = root["content"] as JsonArray ?? throw Invalid(); Output = new(new JsonObject { ["role"] = "assistant", ["content"] = content.DeepClone() });
                StopReason = String(root["stop_reason"], "end_turn"); Terminal = true;
                if (String(root["stop_details"]?["type"]) == "refusal" || StopReason == "refusal") Refused = true;
                if (StopReason == "pause_turn") Failure = new(PlaygroundFailureKind.InvalidResponse, "服务端检索暂停，未自动继续发送计费请求。");
                foreach (var part in content) SearchData(part);
            }
            else if (owner.Kind == PlaygroundProviderKind.Gemini)
            {
                string status = String(root["status"]); Terminal = status is "completed" or "requires_action" or "incomplete" or "failed" or "cancelled" or "requires_continuation";
                StopReason = status == "incomplete" ? "max_tokens" : status == "requires_action" ? "tool_calls" : "end_turn";
                if (status is "failed" or "cancelled" or "requires_continuation") Failure = new(status == "cancelled" ? PlaygroundFailureKind.Cancelled : PlaygroundFailureKind.InvalidResponse, "服务商未完成本次回复；未自动续发请求。");
                Output = root["steps"]?.DeepClone() as JsonArray ?? (geminiStreaming ? new JsonArray(blocks.Values.Select(v => v.DeepClone()).ToArray()) : throw Invalid());
                foreach (var step in Output) SearchData(step);
            }
            else
            {
                string status = String(root["status"]); Terminal = status is "completed" or "incomplete";
                StopReason = status == "incomplete" ? "max_tokens" : "end_turn";
                Output = root["output"]?.DeepClone() as JsonArray ?? new JsonArray(blocks.Values.Select(v => v.DeepClone()).ToArray());
                foreach (var item in Output) SearchData(item);
            }
            ReadUsage(root); FinalSearchUsage(); CollectOutputCitations();
            string full = string.Concat(Output.SelectMany(TextParts)); string current = text.ToString();
            if (full.Length > 0 && full != current)
            {
                if (!full.StartsWith(current, StringComparison.Ordinal)) throw Invalid("流式正文与服务商最终正文不一致。");
                yield return TextEvent(full[current.Length..]);
            }
        }
        public ImmutableArray<PlaygroundToolCall> ToolCalls()
        {
            if (owner.Kind == PlaygroundProviderKind.Gemini && geminiStreaming
                && blocks.Any(pair => String(pair.Value["type"]) == "function_call" && !geminiClosedSteps.Contains(pair.Key)))
                throw Invalid("服务商工具步骤未完整结束，未执行半截工具调用。");
            var result = ImmutableArray.CreateBuilder<PlaygroundToolCall>(); var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in Output)
            {
                // Do not let the conditional infer JsonArray for [item]: adding
                // an already-owned node to another JsonArray throws at runtime.
                IEnumerable<JsonNode?> values = owner.Kind == PlaygroundProviderKind.Claude ? item?["content"] as JsonArray ?? [] : new JsonNode?[] { item };
                foreach (var call in values)
                {
                    string type = String(call?["type"]); if (type is not ("function_call" or "tool_use")) continue;
                    string name = String(call?["name"]), id = String(call?[owner.Kind == PlaygroundProviderKind.Claude || owner.Kind == PlaygroundProviderKind.Gemini ? "id" : "call_id"]);
                    if (id.Length is < 1 or > 200 || !ids.Add(id) || !owner.Capabilities(request.Model, request.Endpoint).Tools
                        || !request.Tools.Any(t => t.Name == name)) throw Invalid("服务商请求了未授权或重复的工具调用。");
                    string args = owner.Kind == PlaygroundProviderKind.Claude ? call?["input"]?.ToJsonString() ?? "{}"
                        : owner.Kind == PlaygroundProviderKind.Gemini ? call?["arguments"]?.ToJsonString() ?? "{}" : String(call?["arguments"]);
                    if (args.Length > 32000 || JsonNode.Parse(args, documentOptions: new() { MaxDepth = 24 }) is not JsonObject) throw Invalid("工具参数未完整返回或超过限制。");
                    result.Add(new(id, name, args));
                }
            }
            if (StopReason == "tool_calls" && result.Count == 0) throw Invalid("服务商要求续接，但没有返回完整工具调用。");
            return result.ToImmutable();
        }
        private void AppendArguments(int index, string value)
        { if (!arguments.TryGetValue(index, out var builder)) throw Invalid(); if (builder.Length + value.Length > 32000) throw Invalid("工具参数超过限制。"); builder.Append(value); }
        private void FinalizeArguments(int index, string field)
        {
            if (!arguments.TryGetValue(index, out var builder) || builder.Length == 0) return;
            if (!blocks.TryGetValue(index, out var block)) throw Invalid();
            block[field] = JsonNode.Parse(builder.ToString(), documentOptions: new() { MaxDepth = 24 }) as JsonObject ?? throw Invalid("工具参数不是完整 JSON 对象。");
        }
        private void SearchData(JsonNode? value)
        {
            string type = String(value?["type"]);
            if (type is "web_search_call" or "google_search_call" || type == "server_tool_use" && String(value?["name"]) == "web_search") HostedSearch = true;
            if (type == "web_search_call" && String(value?["status"]) is "failed" or "cancelled" or "incomplete")
                Failure = new(PlaygroundFailureKind.Network, "服务商检索未完成；未将本次答复标记为已核实。", true);
            if (type == "web_search_tool_result")
            {
                if (value?["content"] is JsonObject error && String(error["type"]) == "web_search_tool_result_error")
                    Failure = new(PlaygroundFailureKind.Network, "服务商检索失败；未将本次答复标记为已核实。", true);
            }
            if (type == "google_search_result")
            {
                HostedSearch = true;
                if (Boolean(value?["is_error"])) Failure = new(PlaygroundFailureKind.Network, "Google 检索未完成；未将本次答复标记为已核实。", true);
                // Current Interactions schema carries search_suggestions on the
                // result step itself. Keep the optional result-level form too.
                AddSearchSuggestions(String(value?["search_suggestions"]));
                if (value?["result"] is JsonArray results) foreach (var result in results)
                {
                    AddSearchSuggestions(String(result?["search_suggestions"]));
                    if (result is JsonObject source) citations.Add(new(source.DeepClone().AsObject(), null));
                }
            }
            if (type == "web_search_call" && value?["action"]?["sources"] is JsonArray searched)
                foreach (var source in searched.OfType<JsonObject>()) citations.Add(new(source.DeepClone().AsObject(), null));
        }
        private void AddSearchSuggestions(string html)
        {
            if (html.Length > 128000) throw Invalid();
            if (html.Length > 0 && suggestions.Add(html))
            { SearchSuggestionsHtml = SearchSuggestionsHtml is null ? html : SearchSuggestionsHtml + "\n" + html; if (SearchSuggestionsHtml.Length > 128000) throw Invalid(); }
        }
        private void CollectOutputCitations()
        {
            int offset = 0;
            foreach (var item in Output)
            {
                IEnumerable<JsonNode?> content = item?["content"] as JsonArray ?? [];
                foreach (var part in content)
                {
                    string type = String(part?["type"]); if (type == "refusal") Refused = true;
                    if (type is not ("text" or "output_text")) continue;
                    if (part?["annotations"] is JsonArray annotations) foreach (var citation in annotations.OfType<JsonObject>())
                    {
                        var normalized = citation.DeepClone().AsObject(); bool mapped = false;
                        if (owner.Kind == PlaygroundProviderKind.Gemini)
                        {
                            if (String(citation["type"]) != "url_citation") continue;
                            mapped = TryUtf8Range(String(part["text"]), Count(citation["start_index"]), Count(citation["end_index"]), out int start, out int end)
                                && !ambiguousGeminiAnnotations.Contains(citation.ToJsonString());
                            if (mapped) { normalized["start_index"] = (long)start; normalized["end_index"] = (long)end; }
                        }
                        // Normalize only the public citation copy; native
                        // steps retain their original byte offsets/signatures.
                        citations.Add(new(normalized, offset, mapped));
                    }
                    if (part?["citations"] is JsonArray clauses) foreach (var citation in clauses.OfType<JsonObject>()) citations.Add(new(citation.DeepClone().AsObject(), null));
                    offset += String(part?["text"]).Length;
                }
            }
        }
        public ImmutableArray<PlaygroundSource> Sources()
        {
            var sources = ImmutableArray.CreateBuilder<PlaygroundSource>(); var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in citations)
            {
                var citation = entry.Value; var offset = entry.Offset;
                string url = String(citation["url"]); if (!SafePublicUrl(url, out string normalized)) continue;
                if (offset is null && citations.Any(c => c.Offset is not null && String(c.Value["url"]) == url)) continue;
                int? start = Count(citation["start_index"]), end = Count(citation["end_index"]);
                bool exact = offset is not null && start is not null && end is not null && end > start
                    && (long)end.Value + offset.Value <= text.Length
                    && (owner.Kind == PlaygroundProviderKind.Gemini ? entry.GeminiRangeMapped : !Text.Any(char.IsSurrogate));
                if (exact) { start += offset; end += offset; } else { start = null; end = null; }
                string fingerprint = normalized + "|" + start + "|" + end;
                if (!seen.Add(fingerprint)) continue;
                string id = "web-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fingerprint)))[..20].ToLowerInvariant();
                sources.Add(new(id, PlaygroundSourceKind.Web, String(citation["title"], new Uri(normalized).Host))
                { Url = normalized, Quote = String(citation["cited_text"]) is { Length: > 0 } quote ? quote[..Math.Min(quote.Length, 2000)] : null,
                    Start = start, End = end, HasExactRange = exact,
                    Retention = owner.Kind == PlaygroundProviderKind.Gemini ? PlaygroundRetention.SessionOnly : PlaygroundRetention.Portable });
                if (sources.Count >= 64) break;
            }
            return sources.ToImmutable();
        }
        private static bool TryUtf8Range(string value, int? byteStart, int? byteEnd, out int start, out int end)
        {
            start = end = -1;
            if (byteStart is null || byteEnd is null || byteEnd <= byteStart) return false;
            int bytes = 0, characters = 0;
            while (true)
            {
                if (bytes == byteStart) start = characters;
                if (bytes == byteEnd) { end = characters; return start >= 0; }
                if (bytes > byteEnd || characters == value.Length) return false;
                if (Rune.DecodeFromUtf16(value.AsSpan(characters), out Rune rune, out int consumed) != OperationStatus.Done) return false;
                bytes += rune.Utf8SequenceLength; characters += consumed;
            }
        }
        private void ReadUsage(JsonNode? root)
        {
            if (root is null) return;
            billingModel = UsageLabel(root["model"], 180) ?? billingModel;
            serviceTier = UsageLabel(root["service_tier"], 64) ?? serviceTier;
            MergeUsage(root["usage"]);
            if (Usage is { } usage) Usage = usage with { BillingModel = billingModel, ServiceTier = serviceTier };
        }
        private void FinalSearchUsage()
        {
            // OpenAI reports hosted search invocations as typed output items,
            // not as citations. Missing completion status stays unknown.
            if (owner.Kind != PlaygroundProviderKind.OpenAI) return;
            var calls = Output.Where(item => String(item?["type"]) == "web_search_call").ToArray();
            if (calls.Any(item => String(item?["status"]) != "completed" || String(item?["id"]).Length == 0)
                || calls.Select(item => String(item?["id"])).Distinct(StringComparer.Ordinal).Count() != calls.Length) return;
            if (Usage is { } usage) Usage = usage with { Searches = calls.Length };
            else if (calls.Length > 0) Usage = new(Searches: calls.Length) { BillingModel = billingModel, ServiceTier = serviceTier };
        }
        private void MergeUsage(JsonNode? value)
        {
            if (value is null) return;
            var prior = Usage ?? new();
            int? searches = Count(value["server_tool_use"]?["web_search_requests"]) ?? Count(value["server_tool_use"]?["tool_search"]);
            // This adapter offers only web_search as an xAI server-side tool;
            // its successful-call count is distinct from num_sources_used.
            if (owner.Kind == PlaygroundProviderKind.Grok) searches ??= Count(value["num_server_side_tools_used"]);
            if (value["grounding_tool_count"] is JsonArray counts)
            {
                long total = 0; bool searchCountReported = false;
                foreach (var count in counts)
                    if (String(count?["type"]) == "google_search" && Count(count?["count"]) is { } number)
                    { total += number; searchCountReported = true; if (total > int.MaxValue) throw Invalid("服务商检索用量超过安全范围。"); }
                if (searchCountReported) searches = (int)total;
            }
            long? reasoning = Integer(value["output_tokens_details"]?["reasoning_tokens"]) ?? Integer(value["output_tokens_details"]?["thinking_tokens"])
                ?? Integer(value["total_thought_tokens"]) ?? prior.ReasoningTokens;
            long? cached = Integer(value["input_tokens_details"]?["cached_tokens"]) ?? Integer(value["cache_read_input_tokens"])
                ?? Integer(value["total_cached_tokens"]) ?? prior.CachedInputTokens;
            long? written = Integer(value["input_tokens_details"]?["cache_write_tokens"])
                ?? Integer(value["cache_creation_input_tokens"]) ?? prior.CacheWriteInputTokens;
            long? write5m = Integer(value["cache_creation"]?["ephemeral_5m_input_tokens"]) ?? prior.CacheWrite5mInputTokens;
            long? write1h = Integer(value["cache_creation"]?["ephemeral_1h_input_tokens"]) ?? prior.CacheWrite1hInputTokens;
            if (written is null && write5m is not null && write1h is not null) written = TokenTotal(write5m, write1h);
            long? input = Integer(value["input_tokens"]) ?? Integer(value["total_input_tokens"]) ?? prior.InputTokens;
            if (owner.Kind == PlaygroundProviderKind.Claude)
            {
                // Claude's input_tokens excludes both cache reads and writes.
                // Keep the raw value between deltas, then normalize once.
                claudeUncachedInputTokens = Integer(value["input_tokens"]) ?? claudeUncachedInputTokens;
                input = TokenTotal(claudeUncachedInputTokens, cached, written);
            }
            long? output = Integer(value["output_tokens"]) ?? Integer(value["total_output_tokens"]) ?? prior.OutputTokens;
            // Gemini reports visible output and thought tokens separately. The
            // shared OutputTokens means generated total (reasoning is a subset),
            // matching Responses/Claude and avoiding undercounted comparisons.
            if (owner.Kind == PlaygroundProviderKind.Gemini)
            {
                geminiVisibleOutputTokens = Integer(value["total_output_tokens"]) ?? geminiVisibleOutputTokens;
                output = TokenTotal(geminiVisibleOutputTokens, reasoning);
            }
            decimal? reported = prior.ReportedCostUsd;
            if (owner.Kind == PlaygroundProviderKind.Grok && value.AsObject().ContainsKey("cost_in_usd_ticks"))
                reported = Integer(value["cost_in_usd_ticks"]) is { } ticks ? ticks / 10000000000m : null;
            serviceTier = UsageLabel(value["service_tier"], 64) ?? serviceTier;
            Usage = new(input, output, reasoning, cached, searches ?? prior.Searches,
                Count(value["server_tool_use"]?["tool_fetch"]) ?? Count(value["server_tool_use"]?["web_fetch_requests"]) ?? prior.Fetches)
            {
                BillingModel = billingModel, ServiceTier = serviceTier, ReportedCostUsd = reported,
                CacheWriteInputTokens = written, CacheWrite5mInputTokens = write5m, CacheWrite1hInputTokens = write1h,
                CacheWrite30mInputTokens = Integer(value["input_tokens_details"]?["cache_write_tokens_details"]?["ttl_30m_tokens"]) ?? prior.CacheWrite30mInputTokens,
                CacheWrite12hInputTokens = Integer(value["input_tokens_details"]?["cache_write_tokens_details"]?["ttl_12h_tokens"]) ?? prior.CacheWrite12hInputTokens
            };
        }
        private static long? TokenTotal(long? required, long? second, long? third = null)
        {
            if (required is not { } total) return null;
            foreach (long value in new[] { second ?? 0, third ?? 0 })
            { if (total > long.MaxValue - value) return null; total += value; }
            return total;
        }
        private static string? UsageLabel(JsonNode? value, int maximum)
        { string text = String(value); return text.Length > 0 && text.Length <= maximum && !text.Any(char.IsWhiteSpace) && !text.Any(char.IsControl) ? text : null; }
        private static int Index(JsonNode json, string field = "index") => Count(json[field]) is { } index && index >= 0 ? index : throw Invalid();
        private static IEnumerable<string> TextParts(JsonNode? item)
        { if (item?["content"] is JsonArray parts) foreach (var part in parts) if (String(part?["type"]) is "text" or "output_text") yield return String(part?["text"]); }
        private static PlaygroundProviderEvent Activity() => new(PlaygroundProviderEventKind.Activity) { Text = "搜索公开资料" };
    }
    private static bool SafePublicUrl(string url, out string normalized)
    {
        normalized = "";
        if (!PlaygroundEndpointPolicy.SafePublicUrl(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        normalized = uri.AbsoluteUri; return true;
    }
}
