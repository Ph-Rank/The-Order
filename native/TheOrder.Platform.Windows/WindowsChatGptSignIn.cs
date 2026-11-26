using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using TheOrder.Application.Playground;

namespace TheOrder.Platform.Windows;

public interface IChatGptSystemBrowser
{ Task OpenAsync(Uri authorization, CancellationToken cancellation); }
public sealed class WindowsChatGptSystemBrowser : IChatGptSystemBrowser
{
    public Task OpenAsync(Uri authorization, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        if (authorization.GetLeftPart(UriPartial.Path) != ChatGptSignInPolicy.Authorize || authorization.UserInfo.Length != 0
            || authorization.Fragment.Length != 0) throw new ChatGptAuthException(ChatGptAuthFailure.InvalidResponse, "已拒绝非官方 ChatGPT 登录地址。");
        try { using var process = Process.Start(new ProcessStartInfo(authorization.AbsoluteUri) { UseShellExecute = true }); return Task.CompletedTask; }
        catch { throw new ChatGptAuthException(ChatGptAuthFailure.Network, "无法打开系统浏览器，请检查默认浏览器后重试。"); }
    }
}
public sealed class WindowsChatGptSignIn(IChatGptSystemBrowser browser, TimeSpan? duration = null) : IChatGptBrowserSignIn
{
    public async Task<ChatGptAuthorizationCode> AuthorizeAsync(ChatGptSignInRequest request, CancellationToken cancellation)
    {
        if (!ChatGptSignInPolicy.ValidHost(request.HostId) || request.ClientId is not null && !ChatGptSignInPolicy.ValidClient(request.ClientId)
            || request.IdTokenHint is not null && !ChatGptSignInPolicy.SafeToken(request.IdTokenHint)) throw Invalid();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(duration ?? TimeSpan.FromMinutes(5));
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Server.ExclusiveAddressUse = true;
        static string Random() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var verifier = Random(); var state = Random(); var nonce = Random();
        var challenge = Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        try
        {
            listener.Start(4); int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var redirect = $"http://127.0.0.1:{port}/auth/callback";
            var query = new Dictionary<string, string> { ["client_id"] = request.ClientId ?? ChatGptSignInPolicy.DynamicClient,
                ["ext_agent_host_id"] = request.HostId, ["response_type"] = "code", ["redirect_uri"] = redirect,
                ["scope"] = ChatGptSignInPolicy.Scopes, ["resource"] = ChatGptSignInPolicy.Resource,
                ["state"] = state, ["nonce"] = nonce, ["code_challenge_method"] = "S256", ["code_challenge"] = challenge };
            if (request.ClientId is null) query["agent_name_hint"] = "The Order";
            if (request.ClientId is not null && request.IdTokenHint is not null) query["id_token_hint"] = request.IdTokenHint;
            if (request.ClientId is not null && request.LoginHint is { Length: > 0 and <= 240 }) query["login_hint"] = request.LoginHint;
            if (request.RequestConsent) query["prompt"] = "consent";
            var authorization = new Uri(ChatGptSignInPolicy.Authorize + "?" + string.Join('&', query.Select(p => Uri.EscapeDataString(p.Key) + "=" + Uri.EscapeDataString(p.Value))));
            await browser.OpenAsync(authorization, timeout.Token).ConfigureAwait(false);
            for (int attempt = 0; attempt < 64; attempt++)
            {
                using var connection = await listener.AcceptTcpClientAsync(timeout.Token).ConfigureAwait(false);
                if (((IPEndPoint?)connection.Client.RemoteEndPoint)?.Address.Equals(IPAddress.Loopback) != true) continue;
                using var readTimeout = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token); readTimeout.CancelAfter(TimeSpan.FromSeconds(3));
                await using var stream = connection.GetStream(); var bytes = new byte[16384]; int count = 0;
                try
                {
                    while (count < bytes.Length)
                    {
                        int read = await stream.ReadAsync(bytes.AsMemory(count, 1), readTimeout.Token).ConfigureAwait(false);
                        if (read == 0) break; count++;
                        if (count >= 4 && bytes.AsSpan(count - 4, 4).SequenceEqual("\r\n\r\n"u8)) break;
                    }
                }
                catch (OperationCanceledException) when (!timeout.IsCancellationRequested) { continue; }
                if (count == bytes.Length || count < 4 || !bytes.AsSpan(count - 4, 4).SequenceEqual("\r\n\r\n"u8)) continue;
                var lines = Encoding.ASCII.GetString(bytes, 0, count).Split("\r\n", StringSplitOptions.None);
                var first = lines[0].Split(' '); var hosts = lines.Where(l => l.StartsWith("Host:", StringComparison.OrdinalIgnoreCase)).ToArray();
                if (first.Length != 3 || first[0] != "GET" || first[2] != "HTTP/1.1" || hosts.Length != 1
                    || hosts[0][5..].Trim() != $"127.0.0.1:{port}" || !first[1].StartsWith("/auth/callback?", StringComparison.Ordinal)) continue;
                var values = new Dictionary<string, string>(StringComparer.Ordinal);
                try
                {
                    foreach (var field in first[1][15..].Split('&'))
                    {
                        var pair = field.Split('=', 2);
                        if (!values.TryAdd(Uri.UnescapeDataString(pair[0].Replace('+', ' ')), pair.Length == 2 ? Uri.UnescapeDataString(pair[1].Replace('+', ' ')) : "")) throw Invalid();
                    }
                }
                catch (UriFormatException) { throw Invalid(); }
                if (!values.TryGetValue("state", out var returned) || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(returned), Encoding.UTF8.GetBytes(state))) continue;
                string? code = values.GetValueOrDefault("code"), clientId = values.GetValueOrDefault("client_id") ?? request.ClientId;
                bool denied = values.ContainsKey("error");
                var html = denied ? "登录未完成。请返回 The Order。" : "ChatGPT 授权已返回。请回到 The Order 完成连接，可以关闭此页面。";
                var body = Encoding.UTF8.GetBytes("<!doctype html><meta charset=utf-8><title>The Order</title><p>" + html + "</p>");
                var header = Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\nCache-Control: no-store\r\nContent-Security-Policy: default-src 'none'; frame-ancestors 'none'\r\nReferrer-Policy: no-referrer\r\nConnection: close\r\nContent-Length: {body.Length}\r\n\r\n");
                // A browser closing this tab does not invalidate an already checked callback.
                try { await stream.WriteAsync(header, timeout.Token).ConfigureAwait(false); await stream.WriteAsync(body, timeout.Token).ConfigureAwait(false); }
                catch (IOException) { }
                if (denied) throw new ChatGptAuthException(ChatGptAuthFailure.AccessDenied, "ChatGPT 登录或额度授权未完成，原连接保持不变。");
                if (!ChatGptSignInPolicy.SafeToken(code) || !ChatGptSignInPolicy.ValidClient(clientId)
                    || request.ClientId is not null && request.ClientId != clientId) throw Invalid();
                return new(code!, clientId!, verifier, nonce, redirect);
            }
            throw Invalid();
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
        { throw new ChatGptAuthException(ChatGptAuthFailure.Cancelled, "ChatGPT 登录已超时，可重新登录。"); }
        catch (SocketException) { throw new ChatGptAuthException(ChatGptAuthFailure.Network, "无法建立本机登录回调，请检查网络后重试。"); }
        finally { listener.Stop(); }
    }
    private static ChatGptAuthException Invalid() => new(ChatGptAuthFailure.InvalidResponse, "ChatGPT 登录回调无法核实，未保存凭据。");
}
