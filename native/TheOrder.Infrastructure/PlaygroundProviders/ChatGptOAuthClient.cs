using System.Collections.Immutable;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using TheOrder.Application.Playground;

namespace TheOrder.Infrastructure.PlaygroundProviders;

// HttpClient must have redirects and cookies disabled, as for the other Playground connections.
// All bearer material stays in the protected account store and these bounded OAuth exchanges.
public sealed class ChatGptOAuthClient(HttpClient client, TimeProvider? time = null) : IChatGptOAuthClient
{
    private readonly TimeProvider clock = time ?? TimeProvider.System;
    private readonly SemaphoreSlim metadataGate = new(1, 1);
    private IReadOnlyCollection<SecurityKey>? signingKeys;
    private DateTimeOffset keysExpire;
    private Uri? revocationEndpoint;

    public async Task<ChatGptRegistration> ExchangeAsync(ChatGptAuthorizationCode code, CancellationToken cancellation)
    {
        if (!ChatGptSignInPolicy.ValidClient(code.ClientId) || !ChatGptSignInPolicy.SafeToken(code.Code)
            || !ChatGptSignInPolicy.SafeToken(code.Verifier) || code.Verifier.Length is < 43 or > 128
            || !ChatGptSignInPolicy.SafeToken(code.Nonce) || code.Nonce.Length > 256
            || !Uri.TryCreate(code.RedirectUri, UriKind.Absolute, out var redirect) || redirect.Scheme != "http"
            || redirect.Host != "127.0.0.1" || redirect.AbsolutePath != "/auth/callback"
            || redirect.Query.Length != 0 || redirect.Fragment.Length != 0 || redirect.UserInfo.Length != 0) throw Invalid();
        var json = await TokenAsync(new() { ["grant_type"] = "authorization_code", ["client_id"] = code.ClientId,
            ["code"] = code.Code, ["code_verifier"] = code.Verifier, ["redirect_uri"] = code.RedirectUri,
            ["resource"] = ChatGptSignInPolicy.Resource }, false, cancellation).ConfigureAwait(false);
        string idToken = Token(json, "id_token", required: true)!;
        var identity = await ValidateIdentity(idToken, code.ClientId, code.Nonce, cancellation).ConfigureAwait(false);
        return new(code.ClientId, identity.Subject, identity.Email, identity.Email ?? "ChatGPT",
            Token(json, "access_token", true), Token(json, "refresh_token", false), idToken, Expiry(json), Scopes(json, []));
    }

    public async Task<ChatGptRegistration> RefreshAsync(ChatGptRegistration account, CancellationToken cancellation)
    {
        if (!ChatGptSignInPolicy.ValidClient(account.ClientId) || !ChatGptSignInPolicy.SafeToken(account.RefreshToken))
            throw SignInRequired();
        var json = await TokenAsync(new() { ["grant_type"] = "refresh_token", ["client_id"] = account.ClientId,
            ["refresh_token"] = account.RefreshToken!, ["resource"] = ChatGptSignInPolicy.Resource }, true, cancellation).ConfigureAwait(false);
        string? idToken = Token(json, "id_token", false);
        if (idToken is not null)
        {
            var identity = await ValidateIdentity(idToken, account.ClientId, null, cancellation).ConfigureAwait(false);
            if (identity.Subject != account.Subject) throw Invalid();
        }
        return account with { AccessToken = Token(json, "access_token", true),
            RefreshToken = Token(json, "refresh_token", false) ?? account.RefreshToken,
            IdToken = idToken ?? account.IdToken, ExpiresAt = Expiry(json), Scopes = Scopes(json, account.Scopes) };
    }

    public async Task<bool> RevokeAsync(ChatGptRegistration account, CancellationToken cancellation)
    {
        if (account.RefreshToken is null) return true;
        if (!ChatGptSignInPolicy.ValidClient(account.ClientId) || !ChatGptSignInPolicy.SafeToken(account.RefreshToken)) return false;
        for (int attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                await LoadMetadata(false, cancellation).ConfigureAwait(false);
                using var request = new HttpRequestMessage(HttpMethod.Post, revocationEndpoint!)
                { Content = new FormUrlEncodedContent(new Dictionary<string, string> {
                    ["token"] = account.RefreshToken, ["token_type_hint"] = "refresh_token", ["client_id"] = account.ClientId }) };
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation); timeout.CancelAfter(TimeSpan.FromSeconds(15));
                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
                CheckDestination(response, request.RequestUri!);
                if (response.StatusCode == HttpStatusCode.OK) return true; // RFC 7009 permits an empty body.
                if ((int)response.StatusCode < 500) return false;
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { throw; }
            catch (Exception e) when (e is HttpRequestException or OperationCanceledException or ChatGptAuthException or IOException) { }
            if (attempt < 2) await Task.Delay(TimeSpan.FromMilliseconds(250 * (1 << attempt)), cancellation).ConfigureAwait(false);
        }
        return false;
    }

    private async Task<JsonNode> TokenAsync(Dictionary<string, string> fields, bool refresh, CancellationToken cancellation)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, ChatGptSignInPolicy.Token)
            { Content = new FormUrlEncodedContent(fields) };
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation); timeout.CancelAfter(TimeSpan.FromSeconds(20));
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            CheckDestination(response, request.RequestUri!);
            if (response.StatusCode is HttpStatusCode.TooManyRequests or >= HttpStatusCode.InternalServerError)
                throw Network("ChatGPT 登录服务暂时不可用，请稍后重试。");
            var json = await ReadJson(response, 256 * 1024, timeout.Token).ConfigureAwait(false);
            if (response.StatusCode != HttpStatusCode.OK)
            {
                string error = Text(json["error"]);
                if (refresh && error is "invalid_grant" or "invalid_refresh_token" or "token_expired" or "refresh_token_expired"
                    or "refresh_token_invalidated" or "refresh_token_reused") throw SignInRequired();
                throw new ChatGptAuthException(ChatGptAuthFailure.AccessDenied,
                    refresh ? "ChatGPT 续期未被接受，请重新登录。" : "ChatGPT 登录未完成，请重新登录此连接。");
            }
            if (!string.Equals(Text(json["token_type"]), "Bearer", StringComparison.OrdinalIgnoreCase)) throw Invalid();
            return json;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { throw; }
        catch (Exception e) when (e is HttpRequestException or OperationCanceledException or IOException)
        { throw Network("无法完成 ChatGPT 登录请求，请检查网络后重试。"); }
        catch (JsonException) { throw Invalid(); }
    }

    private async Task<(string Subject, string? Email)> ValidateIdentity(string token, string clientId, string? nonce, CancellationToken cancellation)
    {
        await LoadMetadata(false, cancellation).ConfigureAwait(false);
        for (int attempt = 0; attempt < 2; attempt++)
        {
            var parameters = new TokenValidationParameters {
                ValidateIssuer = true, ValidIssuer = ChatGptSignInPolicy.Issuer,
                ValidateAudience = true, ValidAudience = clientId, ValidateIssuerSigningKey = true,
                IssuerSigningKeys = signingKeys, RequireSignedTokens = true, RequireExpirationTime = true,
                ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
                ValidateLifetime = true, ClockSkew = TimeSpan.FromSeconds(30),
                LifetimeValidator = (notBefore, expires, _, _) => expires.HasValue
                    && expires.Value > clock.GetUtcNow().UtcDateTime.AddSeconds(-30)
                    && (!notBefore.HasValue || notBefore.Value <= clock.GetUtcNow().UtcDateTime.AddSeconds(30)),
                IncludeTokenOnFailedValidation = false, LogTokenId = false };
            var handler = new JsonWebTokenHandler { MapInboundClaims = false, MaximumTokenSizeInBytes = 65536 };
            TokenValidationResult validation;
            try { validation = await handler.ValidateTokenAsync(token, parameters).ConfigureAwait(false); }
            catch (Exception e) when (e is not OutOfMemoryException) { throw Invalid(); }
            if (!validation.IsValid)
            {
                if (attempt == 0 && validation.Exception is SecurityTokenSignatureKeyNotFoundException)
                { await LoadMetadata(true, cancellation).ConfigureAwait(false); continue; }
                throw Invalid();
            }
            if (validation.SecurityToken is not JsonWebToken jwt
                || !jwt.TryGetPayloadValue<string>("sub", out var subject) || string.IsNullOrWhiteSpace(subject)
                || subject.Length > 1024 || subject.Any(char.IsControl)) throw Invalid();
            if (nonce is not null && (!jwt.TryGetPayloadValue<string>("nonce", out var actual) || actual != nonce)) throw Invalid();
            bool hasParty = jwt.TryGetPayloadValue<string>("azp", out var party);
            if (hasParty && party != clientId || jwt.Audiences.Skip(1).Any() && !hasParty) throw Invalid();
            string? email = jwt.TryGetPayloadValue<string>("email", out var mail) && mail is { Length: > 0 and <= 240 }
                && !mail.Any(char.IsControl) ? mail : null;
            return (subject, email);
        }
        throw Invalid();
    }

    private async Task LoadMetadata(bool force, CancellationToken cancellation)
    {
        await metadataGate.WaitAsync(cancellation).ConfigureAwait(false);
        try
        {
            if (!force && signingKeys is not null && keysExpire > clock.GetUtcNow()) return;
            var metadata = await GetJson(ChatGptSignInPolicy.Discovery, cancellation).ConfigureAwait(false);
            if (Text(metadata["issuer"]) != ChatGptSignInPolicy.Issuer
                || Text(metadata["authorization_endpoint"]) != ChatGptSignInPolicy.Authorize
                || Text(metadata["token_endpoint"]) != ChatGptSignInPolicy.Token
                || Text(metadata["jwks_uri"]) != ChatGptSignInPolicy.Jwks
                || !Uri.TryCreate(Text(metadata["revocation_endpoint"]), UriKind.Absolute, out var revoke)
                || revoke.Scheme != "https" || revoke.Host != "auth.openai.com" || revoke.Port != 443
                || revoke.UserInfo.Length != 0 || revoke.Query.Length != 0 || revoke.Fragment.Length != 0) throw Invalid();
            var jwks = await GetJson(ChatGptSignInPolicy.Jwks, cancellation).ConfigureAwait(false);
            if (jwks["keys"] is not JsonArray keys || keys.Count is < 1 or > 32) throw Invalid();
            IReadOnlyCollection<SecurityKey> parsed;
            try { parsed = new JsonWebKeySet(jwks.ToJsonString()).GetSigningKeys().Where(k => k.KeySize >= 2048).ToArray(); }
            catch (Exception e) when (e is not OutOfMemoryException) { throw Invalid(); }
            if (parsed.Count == 0) throw Invalid();
            signingKeys = parsed; revocationEndpoint = revoke; keysExpire = clock.GetUtcNow().AddHours(1);
        }
        finally { metadataGate.Release(); }
    }
    private async Task<JsonNode> GetJson(string uri, CancellationToken cancellation)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation); timeout.CancelAfter(TimeSpan.FromSeconds(20));
            using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            CheckDestination(response, new(uri));
            if (!response.IsSuccessStatusCode) throw Network("无法读取 ChatGPT 登录服务配置，请稍后重试。");
            return await ReadJson(response, 512 * 1024, timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { throw; }
        catch (Exception e) when (e is HttpRequestException or OperationCanceledException or IOException)
        { throw Network("无法验证 ChatGPT 登录服务，请检查网络后重试。"); }
        catch (JsonException) { throw Invalid(); }
    }
    private static async Task<JsonNode> ReadJson(HttpResponseMessage response, int maximum, CancellationToken cancellation)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellation).ConfigureAwait(false);
        using var buffer = new MemoryStream(); byte[] chunk = new byte[8192];
        while (true) { int count = await stream.ReadAsync(chunk, cancellation).ConfigureAwait(false); if (count == 0) break;
            if (buffer.Length + count > maximum) throw Invalid(); buffer.Write(chunk, 0, count); }
        return JsonNode.Parse(buffer.ToArray(), documentOptions: new() { MaxDepth = 24 }) is JsonObject json ? json : throw Invalid();
    }
    private static void CheckDestination(HttpResponseMessage response, Uri requested)
    { if (response.RequestMessage?.RequestUri is { } actual && actual != requested) throw Invalid(); }
    private DateTimeOffset Expiry(JsonNode json) => json["expires_in"] is JsonValue v && v.TryGetValue<int>(out int seconds)
        && seconds is > 0 and <= 86400 ? clock.GetUtcNow().AddSeconds(seconds) : throw Invalid();
    private static string? Token(JsonNode json, string field, bool required)
    {
        if (json[field] is null && !required) return null;
        string value = Text(json[field]); return ChatGptSignInPolicy.SafeToken(value) ? value : throw Invalid();
    }
    private static ImmutableArray<string> Scopes(JsonNode json, ImmutableArray<string> previous)
    {
        if (json["scope"] is null && !previous.IsDefaultOrEmpty) return previous;
        string text = Text(json["scope"]); var scopes = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (scopes.Length is < 1 or > 64 || scopes.Any(s => s.Length > 128 || s.Any(c => c <= 0x20 || c > 0x7e))) throw Invalid();
        return [.. scopes.Distinct(StringComparer.Ordinal)];
    }
    private static string Text(JsonNode? value) => value is JsonValue v && v.TryGetValue<string>(out var text) ? text : "";
    private static ChatGptAuthException Invalid() => new(ChatGptAuthFailure.InvalidResponse, "ChatGPT 登录响应未通过验证，原有账号已保留。");
    private static ChatGptAuthException Network(string message) => new(ChatGptAuthFailure.Network, message);
    private static ChatGptAuthException SignInRequired() => new(ChatGptAuthFailure.NeedsSignIn, "ChatGPT 登录已失效，请重新登录此账号。");
}
