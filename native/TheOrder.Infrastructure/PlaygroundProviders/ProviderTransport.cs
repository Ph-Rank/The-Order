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
    public static async IAsyncEnumerable<PlaygroundSsePacket> ReadAsync(Stream stream,
        [EnumeratorCancellation] CancellationToken cancellation)
    {
        using var reader = new StreamReader(stream, new UTF8Encoding(false, true), false, 4096, leaveOpen: true);
        var line = new StringBuilder(); var data = new StringBuilder(); string eventName = "";
        var buffer = new char[4096]; bool previousCr = false, hasData = false, first = true; long total = 0;
        while (true)
        {
            int count = await reader.ReadAsync(buffer.AsMemory(), cancellation).ConfigureAwait(false);
            if (count == 0) break;
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
        }
        // SSE dispatch requires a blank line. A truncated final frame is not a
        // completed response, even if its partial JSON happens to parse.
        if (line.Length != 0 || hasData) throw new InvalidDataException("Truncated SSE frame");
    }
}

internal sealed partial class NativePlaygroundProvider
{
    private async Task<HttpResponseMessage> RequestAsync(string endpoint, string relative, HttpMethod method,
        JsonNode? body, CancellationToken cancellation)
    {
        endpoint = PlaygroundProviderEndpoints.Normalize(Kind, endpoint);
        string? secret;
        try { secret = await credentials.ReadAsync(Kind, endpoint, cancellation).ConfigureAwait(false); }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { throw new PlaygroundProviderException(new(PlaygroundFailureKind.Authentication, "无法读取所选连接的凭据，请重新保存 API Key。")); }
        if (string.IsNullOrWhiteSpace(secret)) throw new PlaygroundProviderException(new(PlaygroundFailureKind.Authentication, "请先为此服务商保存 API Key。"));
        if (secret.Length > 16384 || secret.Any(c => c is '\r' or '\n' || char.IsControl(c)))
            throw new PlaygroundProviderException(new(PlaygroundFailureKind.Authentication, "保存的凭据格式无效，请重新配置。"));
        var uri = new Uri(endpoint.TrimEnd('/') + "/" + relative);
        using var request = new HttpRequestMessage(method, uri);
        if (Kind == PlaygroundProviderKind.Gemini) request.Headers.Add("x-goog-api-key", secret);
        else request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", secret);
        if (Kind == PlaygroundProviderKind.Claude) request.Headers.Add("anthropic-version", ClaudeVersion);
        if (body?["stream"] is JsonValue streaming && streaming.TryGetValue<bool>(out bool isStream) && isStream) request.Headers.Accept.Add(new("text/event-stream"));
        request.Headers.Accept.Add(new("application/json"));
        if (body is not null) request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        HttpResponseMessage response;
        try { response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation).ConfigureAwait(false); }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { throw; }
        catch (OperationCanceledException) { throw new PlaygroundProviderException(new(PlaygroundFailureKind.Timeout, "服务商请求超时；可手动重试。", true)); }
        catch (HttpRequestException) { throw new PlaygroundProviderException(new(PlaygroundFailureKind.Network, "无法连接所选服务商，请检查网络与代理。", true)); }
        finally { secret = null; request.Headers.Authorization = null; request.Headers.Remove("x-goog-api-key"); }
        if (response.RequestMessage?.RequestUri is { } actual && (actual.Scheme != uri.Scheme || actual.Host != uri.Host || actual.Port != uri.Port))
        { response.Dispose(); throw new PlaygroundProviderException(new(PlaygroundFailureKind.AccessDenied, "已拒绝服务商请求的跨站重定向。")); }
        if (!response.IsSuccessStatusCode)
        {
            int status = (int)response.StatusCode;
            int? retry = response.Headers.RetryAfter?.Delta is { } wait ? (int)Math.Clamp(wait.TotalSeconds, 0, 3600) : null;
            PlaygroundFailureKind? detail;
            try { detail = await ErrorKind(response, cancellation).ConfigureAwait(false); }
            finally { response.Dispose(); }
            throw HttpFailure(status, retry, detail);
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
    private static async Task<PlaygroundFailureKind?> ErrorKind(HttpResponseMessage response, CancellationToken cancellation)
    {
        // Inspect only allowlisted structured error codes. Server prose, URLs,
        // response bodies and credentials never enter the resulting exception.
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellation).ConfigureAwait(false);
            byte[] buffer = new byte[16385]; int length = 0;
            while (length < buffer.Length)
            { int count = await stream.ReadAsync(buffer.AsMemory(length), cancellation).ConfigureAwait(false); if (count == 0) break; length += count; }
            if (length > 16384) return null;
            var error = JsonNode.Parse(buffer.AsSpan(0, length), documentOptions: new() { MaxDepth = 16 })?["error"];
            return StructuredErrorKind(error);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception error) when (error is JsonException or InvalidOperationException or IOException) { return null; }
    }
    private static PlaygroundFailureKind? StructuredErrorKind(JsonNode? error)
    {
        string code = String(error?["code"], String(error?["type"]));
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
