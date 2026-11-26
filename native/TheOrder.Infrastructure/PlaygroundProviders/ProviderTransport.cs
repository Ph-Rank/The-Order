using System.Collections.Immutable;
using System.Net;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using TheOrder.Application.Playground;

namespace TheOrder.Infrastructure.PlaygroundProviders;

internal sealed record PlaygroundSsePacket(string Event, string Data);

internal static class PlaygroundSse
{
    // Read decoded characters rather than lines: ReadLineAsync can allocate an
    // unbounded malicious line before its caller can enforce a length limit.
    public static async IAsyncEnumerable<PlaygroundSsePacket> ReadAsync(TextReader reader, ReadOnlyMemory<char> prefix,
        [EnumeratorCancellation] CancellationToken cancellation)
    {
        var line = new StringBuilder(); var data = new StringBuilder(); string eventName = "";
        var buffer = new char[4096]; bool previousCr = false, hasData = false, first = true; long total = 0;
        prefix.CopyTo(buffer);
        int count = prefix.Length;
        if (count == 0) count = await reader.ReadAsync(buffer.AsMemory(), cancellation).ConfigureAwait(false);
        while (count != 0)
        {
            if ((total += count) > 8 * 1024 * 1024) throw new InvalidDataException("SSE size limit");
            for (int i = 0; i < count; i++)
            {
                char c = buffer[i];
                if (first) { first = false; if (c == '\ufeff') continue; }
                if (c == '\n' && previousCr) { previousCr = false; continue; }
                if (c is '\r' or '\n')
                {
                    previousCr = c == '\r'; string value = line.ToString(); line.Clear();
                    if (value.Length == 0)
                    {
                        if (hasData) { yield return new(eventName, data.ToString()); data.Clear(); hasData = false; }
                        eventName = "";
                    }
                    else if (value[0] != ':')
                    {
                        int colon = value.IndexOf(':'); string field = colon < 0 ? value : value[..colon];
                        string payload = colon < 0 ? "" : value[(colon + 1)..]; if (payload.StartsWith(' ')) payload = payload[1..];
                        if (field == "event") eventName = payload;
                        else if (field == "data")
                        {
                            if (hasData) data.Append('\n'); data.Append(payload); hasData = true;
                            if (data.Length > 2 * 1024 * 1024) throw new InvalidDataException("SSE event limit");
                        }
                    }
                }
                else { previousCr = false; line.Append(c); if (line.Length > 2 * 1024 * 1024) throw new InvalidDataException("SSE line limit"); }
            }
            count = await reader.ReadAsync(buffer.AsMemory(), cancellation).ConfigureAwait(false);
        }
        // SSE dispatch requires a blank line. A truncated final frame is not a
        // completed response, even if its partial JSON happens to parse.
        if (line.Length != 0 || hasData) throw new InvalidDataException("Truncated SSE frame");
    }
}

internal sealed partial class NativePlaygroundProvider
{
    private const string ClientRequestHeader = "X-Client-Request-Id";
    private const int HttpErrorLimit = 16 * 1024;
    private static readonly UTF8Encoding ResponseEncoding = new(false, true);
    private static readonly string[] SseFieldPrefixes = ["data:", "event:", "id:", "retry:"];

    private async Task<HttpResponseMessage> RequestAsync(string endpoint, string relative, HttpMethod method,
        JsonNode? body, CancellationToken cancellation, string? accountId = null)
    {
        endpoint = PlaygroundProviderEndpoints.Normalize(Kind, endpoint);
        string? secret;
        try { secret = Kind == PlaygroundProviderKind.ChatGPT
            ? await credentials.ReadForAccountAsync(Kind, endpoint, accountId, cancellation).ConfigureAwait(false)
            : await credentials.ReadAsync(Kind, endpoint, cancellation).ConfigureAwait(false); }
        catch (OperationCanceledException) { throw; }
        catch (ChatGptAuthException e) { throw new PlaygroundProviderException(new(e.Kind == ChatGptAuthFailure.Network
            ? PlaygroundFailureKind.Network : PlaygroundFailureKind.Authentication, e.Message, e.Kind == ChatGptAuthFailure.Network)); }
        catch (Exception) { throw new PlaygroundProviderException(new(PlaygroundFailureKind.Authentication, Kind == PlaygroundProviderKind.ChatGPT
            ? "无法安全读取 ChatGPT 连接，原资料已保留；请在连接设置中检查。" : "无法读取所选连接的凭据，请重新保存 API Key。")); }
        if (string.IsNullOrWhiteSpace(secret)) throw new PlaygroundProviderException(new(PlaygroundFailureKind.Authentication,
            Kind == PlaygroundProviderKind.ChatGPT ? "请先登录此 ChatGPT 账号并授权使用额度。" : "请先为此服务商保存 API Key。"));
        if (secret.Length > (Kind == PlaygroundProviderKind.ChatGPT ? 65536 : 16384) || secret.Any(c => c is '\r' or '\n' || char.IsControl(c)))
            throw new PlaygroundProviderException(new(PlaygroundFailureKind.Authentication, "保存的凭据格式无效，请重新配置。"));
        var uri = new Uri(endpoint.TrimEnd('/') + "/" + relative);
        using var request = new HttpRequestMessage(method, uri);
        if (Kind == PlaygroundProviderKind.Gemini) request.Headers.Add("x-goog-api-key", secret);
        else request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", secret);
        if (Kind == PlaygroundProviderKind.Claude) request.Headers.Add("anthropic-version", ClaudeVersion);
        bool wantsEvents = Boolean(body?["stream"]);
        bool requiresEvents = Kind == PlaygroundProviderKind.ChatGPT && wantsEvents;
        if (wantsEvents) request.Headers.Accept.Add(new("text/event-stream"));
        if (requiresEvents) request.Headers.Add(ClientRequestHeader, Guid.NewGuid().ToString("N"));
        else request.Headers.Accept.Add(new("application/json"));
        if (body is not null) request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        HttpResponseMessage response;
        try { response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation).ConfigureAwait(false); }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { throw; }
        catch (OperationCanceledException) { throw new PlaygroundProviderException(new(PlaygroundFailureKind.Timeout, "服务商请求超时；可手动重试。", true)); }
        catch (HttpRequestException) { throw new PlaygroundProviderException(new(PlaygroundFailureKind.Network, "无法连接所选服务商，请检查网络与代理。", true)); }
        finally { secret = null; request.Headers.Authorization = null; request.Headers.Remove("x-goog-api-key"); }
        if (response.RequestMessage?.RequestUri is { } actual && (actual.Scheme != uri.Scheme || actual.Host != uri.Host || actual.Port != uri.Port))
        { response.Dispose(); throw new PlaygroundProviderException(new(PlaygroundFailureKind.AccessDenied, "已拒绝服务商请求的跨站重定向。")); }
        string? mediaType = response.Content.Headers.ContentType?.MediaType;
        if (!response.IsSuccessStatusCode || (requiresEvents && mediaType is not null && !IsEventStream(response)))
        {
            using (response)
            {
                int status = (int)response.StatusCode;
                int? retry = response.Headers.RetryAfter?.Delta is { } wait ? (int)Math.Clamp(wait.TotalSeconds, 0, 3600) : null;
                var error = !response.IsSuccessStatusCode || IsJsonMediaType(mediaType)
                    ? await ReadHttpError(response, cancellation).ConfigureAwait(false) : default;
                if (Kind == PlaygroundProviderKind.ChatGPT)
                    throw new PlaygroundProviderException(ChatGptHttpFailure(response, mediaType, error, status, retry));
                throw HttpFailure(status, retry, StructuredErrorKind(error.Code ?? ""));
            }
        }
        // No automatic POST retry: the server may already have accepted/billed it.
        return response;
    }
    private static PlaygroundProviderException HttpFailure(int status, int? retry = null, PlaygroundFailureKind? detail = null)
    {
        var kind = detail ?? (status switch
        {
            401 => PlaygroundFailureKind.Authentication, 402 => PlaygroundFailureKind.Quota,
            403 => PlaygroundFailureKind.AccessDenied, 404 => PlaygroundFailureKind.ModelUnavailable,
            408 or 504 => PlaygroundFailureKind.Timeout, 429 => PlaygroundFailureKind.RateLimited,
            400 or 422 => PlaygroundFailureKind.Unsupported, _ => PlaygroundFailureKind.Network
        });
        string text = kind switch
        {
            PlaygroundFailureKind.Authentication => "服务商拒绝了凭据，请检查所选服务商的 API Key。",
            PlaygroundFailureKind.Quota => "服务商提示额度不足，请检查该账号的用量与账单。",
            PlaygroundFailureKind.AccessDenied => "服务商拒绝访问；可能涉及权限、地区或服务政策。",
            PlaygroundFailureKind.ModelUnavailable => "此模型或接口当前不可用，可重新枚举模型或填写有效模型 ID。",
            PlaygroundFailureKind.RateLimited => "服务商请求过于频繁，请稍后手动重试。",
            PlaygroundFailureKind.Timeout => "服务商请求超时，可稍后手动重试。",
            PlaygroundFailureKind.Unsupported => "所选模型或接口不接受本次参数，请调整模型或高级设置。",
            _ => "服务商暂时不可用，请稍后手动重试。"
        };
        return new(new(kind, text, kind != PlaygroundFailureKind.Quota && (status is 408 or 429 or >= 500), status, retry));
    }
    private static bool IsEventStream(HttpResponseMessage response) =>
        string.Equals(response.Content.Headers.ContentType?.MediaType, "text/event-stream", StringComparison.OrdinalIgnoreCase);

    private static bool IsJsonMediaType(string? mediaType) =>
        string.Equals(mediaType, "application/json", StringComparison.OrdinalIgnoreCase)
        || mediaType?.EndsWith("+json", StringComparison.OrdinalIgnoreCase) == true;

    private enum HeaderlessBody { Events, Json, Html, Unknown }

    private async IAsyncEnumerable<PlaygroundSsePacket> ReadResponseSseAsync(HttpResponseMessage response,
        [EnumeratorCancellation] CancellationToken cancellation)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellation).ConfigureAwait(false);
        using var reader = new StreamReader(stream, ResponseEncoding, false, 4096, leaveOpen: true);
        ReadOnlyMemory<char> prefix = default;

        if (Kind == PlaygroundProviderKind.ChatGPT && response.Content.Headers.ContentType?.MediaType is null)
        {
            // Inspect only enough of the decoded body to identify its framing.
            // Replay those characters before reading further from the same reader.
            var probe = new char[512];
            int length = 0;
            HeaderlessBody? shape = null;
            while (length < probe.Length && shape is null)
            {
                int read = await reader.ReadAsync(probe.AsMemory(length), cancellation).ConfigureAwait(false);
                if (read == 0) break;
                length += read;
                shape = ClassifyHeaderlessBody(probe.AsSpan(0, length));
            }
            prefix = probe.AsMemory(0, length);

            if (shape is not HeaderlessBody.Events)
            {
                var detail = shape == HeaderlessBody.Json
                    ? await ReadHttpError(reader, prefix, cancellation).ConfigureAwait(false) : default;
                string format = detail.Format ?? (shape switch
                {
                    HeaderlessBody.Json => "JSON",
                    HeaderlessBody.Html => "网页",
                    _ => WithoutBom(prefix.Span).Trim().IsEmpty && length < probe.Length ? "空响应" : "未知正文格式"
                });
                throw ChatGptStreamFailure(response, format, detail);
            }
        }

        await foreach (var packet in PlaygroundSse.ReadAsync(reader, prefix, cancellation).ConfigureAwait(false))
            yield return packet;
    }

    private static HeaderlessBody? ClassifyHeaderlessBody(ReadOnlySpan<char> prefix)
    {
        prefix = WithoutBom(prefix).TrimStart();
        if (prefix.IsEmpty) return null;
        if (prefix[0] == ':') return HeaderlessBody.Events;
        if (prefix[0] is '{' or '[') return HeaderlessBody.Json;
        if (prefix[0] == '<') return HeaderlessBody.Html;
        foreach (string field in SseFieldPrefixes)
        {
            if (prefix.StartsWith(field, StringComparison.Ordinal)) return HeaderlessBody.Events;
            if (field.AsSpan().StartsWith(prefix, StringComparison.Ordinal)) return null;
        }
        return HeaderlessBody.Unknown;
    }

    private static ReadOnlySpan<char> WithoutBom(ReadOnlySpan<char> value) =>
        !value.IsEmpty && value[0] == '\ufeff' ? value[1..] : value;

    private static PlaygroundProviderException ChatGptStreamFailure(HttpResponseMessage response, string format,
        ProviderHttpError detail = default) => new(ChatGptHttpFailure(response,
            response.Content.Headers.ContentType?.MediaType, detail with { Format = format }, (int)response.StatusCode, null));

    private readonly record struct ProviderHttpError(string? Code, string? Parameter, string? Format);

    private static async Task<ProviderHttpError> ReadHttpError(HttpResponseMessage response, CancellationToken cancellation)
    {
        // Retain only the envelope shape and explicitly mapped fields in diagnostics.
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellation).ConfigureAwait(false);
            using var reader = new StreamReader(stream, ResponseEncoding, false, 4096, leaveOpen: true);
            return await ReadHttpError(reader, default, cancellation).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception error) when (error is InvalidOperationException or IOException or ArgumentException) { return default; }
    }

    private static async Task<ProviderHttpError> ReadHttpError(TextReader reader, ReadOnlyMemory<char> prefix,
        CancellationToken cancellation)
    {
        try
        {
            var buffer = new char[HttpErrorLimit + 1];
            prefix.CopyTo(buffer);
            int length = prefix.Length;
            while (length < buffer.Length)
            {
                int count = await reader.ReadAsync(buffer.AsMemory(length), cancellation).ConfigureAwait(false);
                if (count == 0) break;
                length += count;
            }
            if (length > HttpErrorLimit || ResponseEncoding.GetByteCount(buffer.AsSpan(0, length)) > HttpErrorLimit) return default;
            var envelope = JsonNode.Parse(WithoutBom(buffer.AsSpan(0, length)).ToString(),
                documentOptions: new() { MaxDepth = 16 }) as JsonObject;
            if (envelope?["error"] is JsonObject error)
                return new(StructuredErrorCode(error), String(error["param"]), "JSON 错误");
            return envelope?.ContainsKey("detail") == true ? new(null, null, "JSON 接入错误") : default;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception error) when (error is JsonException or InvalidOperationException or IOException or ArgumentException) { return default; }
    }
    private static string StructuredErrorCode(JsonNode? error) => error is JsonObject ? String(error["code"], String(error["type"])) : "";
    private static PlaygroundFailureKind? StructuredErrorKind(JsonNode? error) => StructuredErrorKind(StructuredErrorCode(error));
    private static PlaygroundFailureKind? StructuredErrorKind(string code)
    {
        return code switch
        {
            "insufficient_quota" or "quota_exceeded" or "credit_balance_too_low" => PlaygroundFailureKind.Quota,
            "authentication_error" or "invalid_api_key" => PlaygroundFailureKind.Authentication,
            "rate_limit_error" or "rate_limit_exceeded" => PlaygroundFailureKind.RateLimited,
            "model_not_found" => PlaygroundFailureKind.ModelUnavailable,
            "permission_error" => PlaygroundFailureKind.AccessDenied,
            _ => null
        };
    }
    private static PlaygroundFailure ChatGptFailure(string code) =>
        ChatGptPlanFailure(code, null, null) ?? ChatGptHttpFallback(503, null, StructuredErrorKind(code));

    private static PlaygroundFailure? ChatGptPlanFailure(string code, int? status, int? retry) => code switch
    {
        "subscription_sharing_usage_limit_exceeded" => new(PlaygroundFailureKind.Quota,
            ChatGptSignInPolicy.UsageLimitMessage, false, status, retry),
        "subscription_sharing_usage_unavailable" or "subscription_sharing_user_unavailable" => new(PlaygroundFailureKind.Network,
            "ChatGPT 额度服务暂时不可用，请稍后重试；登录信息已保留。", true, status, retry),
        "subscription_sharing_user_not_eligible" => new(PlaygroundFailureKind.AccessDenied,
            "此 ChatGPT 账号当前不符合套餐额度使用条件，请在 ChatGPT 中检查账号资格。", false, status),
        "chatpass_v2_scope_not_authorized" or "chatpass_v2_invalid_authorization_context" => new(PlaygroundFailureKind.Authentication,
            "尚未授权此连接使用 ChatGPT 额度，请在「连接与授权」中重新授权。", false, status),
        "subscription_sharing_invalid_user" => new(PlaygroundFailureKind.Authentication, "ChatGPT 账号授权已失效，请重新登录此连接。", false, status),
        "subscription_sharing_unsupported_capability" or "subscription_sharing_route_not_supported" => new(PlaygroundFailureKind.Unsupported,
            "ChatGPT 额度连接不支持本次模型或能力，请更换模型或调整设置。", false, status),
        _ => null
    };

    private static PlaygroundFailure ChatGptHttpFallback(int status, int? retry, PlaygroundFailureKind? detail)
    {
        var failure = HttpFailure(status, retry, detail).Failure;
        return failure.Kind == PlaygroundFailureKind.Authentication
            ? failure with { Message = "ChatGPT 拒绝了此连接的授权，请在「连接与授权」中检查所选账号。" }
            : failure;
    }

    private static PlaygroundFailure ChatGptHttpFailure(HttpResponseMessage response, string? mediaType,
        ProviderHttpError error, int status, int? retry)
    {
        string code = error.Code ?? "";
        var planFailure = ChatGptPlanFailure(code, status, retry);
        var detail = StructuredErrorKind(code);
        var failure = planFailure ?? (response.IsSuccessStatusCode && detail is null
            ? new PlaygroundFailure(PlaygroundFailureKind.InvalidResponse, "ChatGPT 额度请求没有返回可读取的回复流。", false, status)
            : ChatGptHttpFallback(status, retry, detail));

        string format = error.Format ?? (IsJsonMediaType(mediaType) ? "JSON" : mediaType?.ToLowerInvariant() switch
            {
                "text/html" or "application/xhtml+xml" => "网页",
                "text/plain" => "纯文本",
                "text/event-stream" => "事件流",
                null => "无内容类型",
                _ => "其他格式"
            });
        List<string> diagnostics = [$"HTTP {status}", format];
        if (planFailure is not null || detail is not null) diagnostics.Add(code);
        string? field = error.Parameter?.Split(['.', '['], 2)[0];
        if (field is "model" or "input" or "tools" or "reasoning" or "service_tier" or "include" or "instructions" or "stream" or "store")
            diagnostics.Add($"参数 {field}");
        if (response.Headers.TryGetValues("x-request-id", out var ids)
            && ids.FirstOrDefault() is { Length: > 4 and <= 80 } requestId
            && requestId.StartsWith("req_", StringComparison.Ordinal)
            && requestId.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-'))
            diagnostics.Add($"请求编号 {requestId}");
        else if (response.RequestMessage?.Headers.TryGetValues(ClientRequestHeader, out var clientIds) == true
            && clientIds.FirstOrDefault() is { } clientId && Guid.TryParseExact(clientId, "N", out _))
            diagnostics.Add($"客户端请求编号 {clientId}");
        return failure with { Message = $"{failure.Message}（{string.Join(" · ", diagnostics)}）" };
    }

    private static async Task<JsonNode> ReadJsonAsync(HttpResponseMessage response, CancellationToken cancellation)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellation).ConfigureAwait(false);
        using var data = new MemoryStream(); byte[] buffer = new byte[8192];
        while (true)
        {
            int read = await stream.ReadAsync(buffer, cancellation).ConfigureAwait(false); if (read == 0) break;
            if (data.Length + read > 2 * 1024 * 1024) throw Invalid("服务商返回内容超过本次安全上限。");
            data.Write(buffer, 0, read);
        }
        return JsonNode.Parse(data.ToArray(), documentOptions: new() { MaxDepth = 48 }) ?? throw Invalid();
    }
    private static string String(JsonNode? value, string fallback = "") => value is JsonValue v && v.TryGetValue<string>(out var text) ? text : fallback;
    private static bool Boolean(JsonNode? value) => value is JsonValue v && v.TryGetValue<bool>(out bool answer) && answer;
    private static long? Integer(JsonNode? value) => value is JsonValue v && v.TryGetValue<long>(out long answer) && answer >= 0 ? answer : null;
    private static int? Count(JsonNode? value) => Integer(value) is { } count && count <= int.MaxValue ? (int)count : null;
}
