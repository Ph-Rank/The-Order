using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using TheOrder.Application;

namespace TheOrder.Platform.Windows;

public interface IOAuthSystemBrowser { Task OpenAsync(Uri authorization, CancellationToken cancellation); }
public sealed class WindowsOAuthSystemBrowser : IOAuthSystemBrowser
{
    public Task OpenAsync(Uri authorization, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        if (authorization.Scheme != "https" || authorization.Host != "account.nextmoe.com" || authorization.Port != 443
            || authorization.AbsolutePath != "/api/v1/oauth/authorize" || authorization.UserInfo.Length > 0 || authorization.Fragment.Length > 0) throw new OnlineException(OnlineFailure.InvalidRequest);
        try { using var process = Process.Start(new ProcessStartInfo(authorization.AbsoluteUri) { UseShellExecute = true }); return Task.CompletedTask; }
        catch { throw new OnlineException(OnlineFailure.Offline); }
    }
}
// Raw bounded loopback HTTP avoids HttpListener URL ACLs/admin rights. Only IPv4 literal loopback.
public sealed class WindowsOAuthLogin(IOAuthSystemBrowser browser, TimeSpan? duration = null) : IOAuthLogin
{
    internal static string RandomUrlSafe(int bytes) => UrlSafe(RandomNumberGenerator.GetBytes(bytes));
    internal static string UrlSafe(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    public async Task<OAuthAuthorizationCode> AuthorizeAsync(string clientId, string[] scopes, CancellationToken cancellation)
    {
        if (clientId.Length is < 1 or > 256 || !clientId.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_')
            || scopes.Except(NextMoeOAuthIdentity.LoginScopes, StringComparer.Ordinal).Any()) throw new OnlineException(OnlineFailure.InvalidRequest);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation); timeout.CancelAfter(duration ?? TimeSpan.FromMinutes(5));
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Server.ExclusiveAddressUse = true;
        var verifier = RandomUrlSafe(32); var state = RandomUrlSafe(32);
        var challenge = UrlSafe(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        try
        {
            listener.Start(4); int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var redirect = new Uri($"http://127.0.0.1:{port}/callback");
            var parameters = new Dictionary<string, string> { ["client_id"] = clientId, ["response_type"] = "code", ["redirect_uri"] = redirect.AbsoluteUri,
                ["scope"] = string.Join(' ', scopes), ["state"] = state, ["code_challenge"] = challenge, ["code_challenge_method"] = "S256" };
            var authorization = new Uri("https://account.nextmoe.com/api/v1/oauth/authorize?" + string.Join('&', parameters.Select(x => Uri.EscapeDataString(x.Key) + "=" + Uri.EscapeDataString(x.Value))));
            await browser.OpenAsync(authorization, timeout.Token).ConfigureAwait(false);
            for (int attempts = 0; attempts < 64; attempts++)
            {
                using var connection = await listener.AcceptTcpClientAsync(timeout.Token).ConfigureAwait(false);
                if (((IPEndPoint?)connection.Client.RemoteEndPoint)?.Address.Equals(IPAddress.Loopback) != true) continue;
                using var readTimeout = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token); readTimeout.CancelAfter(TimeSpan.FromSeconds(5));
                await using var stream = connection.GetStream();
                var header = new byte[8192]; int count = 0;
                try
                {
                    while (count < header.Length)
                    {
                        var read = await stream.ReadAsync(header.AsMemory(count, 1), readTimeout.Token).ConfigureAwait(false);
                        if (read == 0) break; count++;
                        if (count >= 4 && header[count - 4] == 13 && header[count - 3] == 10 && header[count - 2] == 13 && header[count - 1] == 10) break;
                    }
                }
                catch (OperationCanceledException) when (!timeout.IsCancellationRequested) { continue; }
                var text = Encoding.ASCII.GetString(header, 0, count); var lines = text.Split("\r\n", StringSplitOptions.None);
                var first = lines[0].Split(' ');
                var host = lines.Where(x => x.StartsWith("Host:", StringComparison.OrdinalIgnoreCase)).Select(x => x[5..].Trim()).ToArray();
                bool valid = count < header.Length && text.EndsWith("\r\n\r\n", StringComparison.Ordinal) && first.Length == 3
                    && first[0] == "GET" && first[2] == "HTTP/1.1" && host.Length == 1 && host[0] == $"127.0.0.1:{port}"
                    && first[1].StartsWith("/callback?", StringComparison.Ordinal) && !lines.Any(x => x.StartsWith("Transfer-Encoding:", StringComparison.OrdinalIgnoreCase)
                        || x.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase) && x[15..].Trim() != "0");
                Dictionary<string, string>? values = null;
                if (valid)
                {
                    try
                    {
                        values = first[1][10..].Split('&').Select(part => part.Split('=', 2)).ToDictionary(x => Uri.UnescapeDataString(x[0]),
                            x => x.Length == 2 ? Uri.UnescapeDataString(x[1].Replace('+', ' ')) : "", StringComparer.Ordinal);
                        valid = values.TryGetValue("state", out var receivedState) && receivedState.Length == state.Length
                            && CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(receivedState), Encoding.ASCII.GetBytes(state))
                            && values.Keys.All(x => x is "state" or "code" or "error" or "error_description" or "iss")
                            && (!values.TryGetValue("iss", out var issuer) || issuer == "https://account.nextmoe.com");
                    }
                    catch (Exception e) when (e is ArgumentException or UriFormatException) { valid = false; }
                }
                var code = valid && values!.TryGetValue("code", out var receivedCode) ? receivedCode : null;
                bool denied = valid && values!.ContainsKey("error") && code is null;
                valid &= code is { Length: > 0 and <= 4096 } && !code.Any(char.IsControl) && !values!.ContainsKey("error");
                var message = valid ? "授权已收到，可以关闭此页面。" : denied ? "登录未完成，可以关闭此页面。" : "无效回调，未更改登录状态。";
                var body = Encoding.UTF8.GetBytes(message);
                var responseHeader = Encoding.ASCII.GetBytes($"HTTP/1.1 {(valid || denied ? "200 OK" : "400 Bad Request")}\r\nContent-Type: text/plain; charset=utf-8\r\nCache-Control: no-store\r\nContent-Security-Policy: default-src 'none'\r\nConnection: close\r\nContent-Length: {body.Length}\r\n\r\n");
                try { await stream.WriteAsync(responseHeader, timeout.Token).ConfigureAwait(false); await stream.WriteAsync(body, timeout.Token).ConfigureAwait(false); }
                catch (IOException) { /* A closed browser tab does not consume another authorization code. */ }
                if (denied) throw new OperationCanceledException("用户未授权 NextMoe 登录。", cancellation);
                if (valid) return new(code!, verifier, redirect);
            }
            throw new OnlineException(OnlineFailure.InvalidResponse);
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested && timeout.IsCancellationRequested) { throw new OnlineException(OnlineFailure.Timeout); }
        catch (SocketException) { throw new OnlineException(OnlineFailure.Offline); }
        finally { listener.Stop(); }
    }
}
