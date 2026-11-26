using System.Collections.Immutable;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using TheOrder.Application.Playground;
using TheOrder.Infrastructure.PlaygroundProviders;
using TheOrder.Platform.Windows;
using Xunit;

namespace TheOrder.Tests;

// Local loopback, generated signing keys and an in-memory HTTP transport only.
public sealed class ChatGptOAuthTests
{
    private const string ClientId = "oaiapp_synthetic", HostId = "urn:uuid:161ed5e8-3a8d-43de-a30b-6e289edbb6c3";
    private static readonly ChatGptAuthorizationCode Code = new("synthetic-code", ClientId, new('a', 43), "synthetic-nonce", "http://127.0.0.1:43210/auth/callback");
    private static Dictionary<string, string> Query(string value) => new Uri(value).Query.TrimStart('?').Split('&')
        .Select(s => s.Split('=', 2)).ToDictionary(p => Uri.UnescapeDataString(p[0]), p => Uri.UnescapeDataString(p[1].Replace('+', ' ')), StringComparer.Ordinal);

    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task BrowserFlowUsesPkceFreshStateNonceAndSameIssuedRegistration(bool returning)
    {
        var browser = new Browser(async uri =>
        {
            var q = Query(uri.AbsoluteUri); using var http = new HttpClient();
            using var response = await http.GetAsync(q["redirect_uri"] + "?state=" + q["state"] + "&code=synthetic-code&client_id=" + ClientId);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.DoesNotContain("synthetic-code", await response.Content.ReadAsStringAsync());
        });
        var request = new ChatGptSignInRequest(HostId, returning ? ClientId : null, returning ? "synthetic-id-hint" : null, "same@example.invalid", returning);
        var result = await new WindowsChatGptSignIn(browser).AuthorizeAsync(request, default); await browser.Callback;
        var query = Query(browser.Authorization!.AbsoluteUri);
        Assert.Equal(returning ? ClientId : ChatGptSignInPolicy.DynamicClient, query["client_id"]);
        Assert.Equal(HostId, query["ext_agent_host_id"]); Assert.Equal(ChatGptSignInPolicy.Scopes, query["scope"]);
        Assert.Equal(ChatGptSignInPolicy.Resource, query["resource"]); Assert.Equal("S256", query["code_challenge_method"]);
        Assert.Equal(Base64UrlEncoder.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(result.Verifier))), query["code_challenge"]);
        Assert.Equal(result.Nonce, query["nonce"]); Assert.Equal(result.RedirectUri, query["redirect_uri"]);
        Assert.Equal("/auth/callback", new Uri(result.RedirectUri).AbsolutePath); Assert.Equal("127.0.0.1", new Uri(result.RedirectUri).Host);
        Assert.Equal(returning, query.ContainsKey("id_token_hint")); Assert.Equal(!returning, query.ContainsKey("agent_name_hint"));
        Assert.Equal(returning, query.ContainsKey("prompt")); Assert.DoesNotContain(result.Code, result.ToString());
        using var socket = new TcpClient(); await Assert.ThrowsAnyAsync<SocketException>(() => socket.ConnectAsync(IPAddress.Loopback, new Uri(result.RedirectUri).Port));
    }
    [Theory] [InlineData("denied")] [InlineData("wrong-client")] [InlineData("duplicate")]
    public async Task RejectedCallbackNeverReturnsAuthorizationCode(string mode)
    {
        var browser = new Browser(async uri =>
        {
            var q = Query(uri.AbsoluteUri); using var http = new HttpClient();
            string suffix = mode == "denied" ? "&error=access_denied" : "&code=synthetic-code&client_id=oaiapp_other";
            if (mode == "duplicate") suffix = "&code=a&code=b&client_id=" + ClientId;
            try { using var response = await http.GetAsync(q["redirect_uri"] + "?state=" + q["state"] + suffix); }
            catch (HttpRequestException) when (mode == "duplicate") { }
        });
        await Assert.ThrowsAsync<ChatGptAuthException>(() => new WindowsChatGptSignIn(browser).AuthorizeAsync(new(HostId, ClientId, null, null, false), default));
        await browser.Callback;
    }
    [Fact] public async Task MismatchedStateIsIgnoredThenFreshValidCallbackSucceeds()
    {
        var browser = new Browser(async uri =>
        {
            var q = Query(uri.AbsoluteUri); using var http = new HttpClient();
            try { using var ignored = await http.GetAsync(q["redirect_uri"] + "?state=wrong&code=untrusted&client_id=" + ClientId); }
            catch (HttpRequestException) { }
            using var good = await http.GetAsync(q["redirect_uri"] + "?state=" + q["state"] + "&code=valid&client_id=" + ClientId);
        });
        var code = await new WindowsChatGptSignIn(browser).AuthorizeAsync(new(HostId, null, null, null, false), default); await browser.Callback;
        Assert.Equal("valid", code.Code);
    }
    [Fact] public async Task CancelClosesListenerWithoutOpeningAnotherLogin()
    {
        var browser = new Browser(_ => Task.CompletedTask); using var stop = new CancellationTokenSource();
        var login = new WindowsChatGptSignIn(browser).AuthorizeAsync(new(HostId, null, null, null, false), stop.Token);
        stop.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => login);
        using var socket = new TcpClient(); await Assert.ThrowsAnyAsync<SocketException>(() => socket.ConnectAsync(IPAddress.Loopback, new Uri(Query(browser.Authorization!.AbsoluteUri)["redirect_uri"]).Port));
    }
    [Theory] [InlineData("valid")] [InlineData("issuer")] [InlineData("audience")] [InlineData("nonce")]
    [InlineData("expired")] [InlineData("signature")] [InlineData("missing-id")] [InlineData("authorized-party")]
    public async Task CodeExchangeRequiresVerifiedIdTokenAndServerGrantedPlanScope(string mode)
    {
        using var rig = new OAuthRig();
        var token = rig.IdToken(mode); rig.Reply = _ => Tokens(token);
        if (mode == "missing-id") rig.Reply = _ => { var json = Tokens(null); return json; };
        if (mode != "valid")
        {
            var failure = await Assert.ThrowsAsync<ChatGptAuthException>(() => rig.OAuth.ExchangeAsync(Code, default));
            Assert.Equal(ChatGptAuthFailure.InvalidResponse, failure.Kind); Assert.DoesNotContain("synthetic-access", failure.ToString());
        }
        else
        {
            var account = await rig.OAuth.ExchangeAsync(Code, default);
            Assert.Equal("synthetic-sub", account.Subject); Assert.Equal(ClientId, account.ClientId); Assert.True(account.PlanEnabled);
            Assert.DoesNotContain("synthetic-access", account.ToString());
        }
        var sent = Assert.Single(rig.TokenForms);
        Assert.Equal("authorization_code", sent["grant_type"]); Assert.Equal(Code.Verifier, sent["code_verifier"]);
        Assert.Equal(ClientId, sent["client_id"]); Assert.False(sent.ContainsKey("client_secret"));
    }
    [Fact] public async Task IdentityOnlyLoginNeverInfersPlanGrantFromRequestedScope()
    {
        using var rig = new OAuthRig(); rig.Reply = _ => { var json = Tokens(rig.IdToken("valid")); json["scope"] = "openid profile email"; return json; };
        var account = await rig.OAuth.ExchangeAsync(Code, default); Assert.True(account.Connected); Assert.False(account.PlanEnabled);
    }
    [Fact] public async Task RefreshRotatesTokensAndPreservesOmittedRefreshTokenAndScope()
    {
        using var rig = new OAuthRig(); var old = rig.Account;
        rig.Reply = _ => { var json = Tokens(null); json["access_token"] = "rotated-access"; json["refresh_token"] = "rotated-refresh"; return json; };
        var rotated = await rig.OAuth.RefreshAsync(old, default); Assert.Equal("rotated-refresh", rotated.RefreshToken);
        rig.Reply = _ => { var json = Tokens(null); json.Remove("refresh_token"); json.Remove("scope"); return json; };
        var retained = await rig.OAuth.RefreshAsync(rotated, default); Assert.Equal(rotated.RefreshToken, retained.RefreshToken); Assert.True(retained.PlanEnabled);
        Assert.All(rig.TokenForms, form => { Assert.Equal(ClientId, form["client_id"]); Assert.Equal(ChatGptSignInPolicy.Resource, form["resource"]); Assert.False(form.ContainsKey("scope")); });
    }
    [Fact] public async Task RefreshRejectsNewSubjectAndTerminalGrantFailureNeedsSignIn()
    {
        using var rig = new OAuthRig(); rig.Reply = _ => Tokens(rig.IdToken("valid", "other-sub"));
        Assert.Equal(ChatGptAuthFailure.InvalidResponse, (await Assert.ThrowsAsync<ChatGptAuthException>(() => rig.OAuth.RefreshAsync(rig.Account, default))).Kind);
        rig.Status = HttpStatusCode.BadRequest; rig.Reply = _ => new JsonObject { ["error"] = "invalid_grant" };
        Assert.Equal(ChatGptAuthFailure.NeedsSignIn, (await Assert.ThrowsAsync<ChatGptAuthException>(() => rig.OAuth.RefreshAsync(rig.Account, default))).Kind);
    }
    [Fact] public async Task RevocationUsesDiscoveryEmpty200AndNeverSendsTokensToUnknownHost()
    {
        using var rig = new OAuthRig(); Assert.True(await rig.OAuth.RevokeAsync(rig.Account, default)); Assert.Equal(1, rig.Revocations);
        using var bad = new OAuthRig { Revocation = "https://example.invalid/revoke" };
        Assert.False(await bad.OAuth.RevokeAsync(bad.Account, default)); Assert.Equal(0, bad.Revocations);
    }

    private sealed class Browser(Func<Uri, Task> callback) : IChatGptSystemBrowser
    {
        public Uri? Authorization; public Task Callback = Task.CompletedTask;
        public Task OpenAsync(Uri uri, CancellationToken cancellation) { Authorization = uri; Callback = callback(uri); return Task.CompletedTask; }
    }
    private static JsonObject Tokens(string? id) => new() { ["access_token"] = "synthetic-access", ["refresh_token"] = "synthetic-refresh",
        ["id_token"] = id, ["token_type"] = "Bearer", ["expires_in"] = 3600, ["scope"] = ChatGptSignInPolicy.Scopes };
    private sealed class OAuthRig : HttpMessageHandler, IDisposable
    {
        private readonly RSA rsa = RSA.Create(2048);
        private readonly HttpClient http;
        public ChatGptOAuthClient OAuth { get; }
        public Func<Dictionary<string, string>, JsonObject>? Reply;
        public HttpStatusCode Status = HttpStatusCode.OK;
        public string Revocation = ChatGptSignInPolicy.Issuer + "/api/accounts/oauth/revoke";
        public List<Dictionary<string, string>> TokenForms = [];
        public int Revocations;
        public ChatGptRegistration Account => new(ClientId, "synthetic-sub", "test@example.invalid", "Synthetic", "access", "refresh", null,
            DateTimeOffset.UtcNow.AddMinutes(-1), [.. ChatGptSignInPolicy.Scopes.Split(' ')]);
        public OAuthRig() { http = new(this, false); OAuth = new(http); }
        public string IdToken(string mode, string subject = "synthetic-sub")
        {
            using var other = RSA.Create(2048);
            var key = new RsaSecurityKey(mode == "signature" ? other : rsa) { KeyId = "synthetic-key" };
            var now = DateTime.UtcNow;
            return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor {
                Issuer = mode == "issuer" ? "https://example.invalid" : ChatGptSignInPolicy.Issuer,
                Audience = mode == "audience" ? "oaiapp_wrong" : ClientId, IssuedAt = now.AddMinutes(-10), NotBefore = now.AddMinutes(-10),
                Expires = mode == "expired" ? now.AddMinutes(-5) : now.AddMinutes(5), SigningCredentials = new(key, SecurityAlgorithms.RsaSha256),
                Claims = new Dictionary<string, object> { ["sub"] = subject, ["email"] = "test@example.invalid", ["nonce"] = mode == "nonce" ? "wrong" : Code.Nonce,
                    ["azp"] = mode == "authorized-party" ? "oaiapp_other" : ClientId } });
        }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellation)
        {
            string uri = request.RequestUri!.AbsoluteUri; JsonObject? json; HttpStatusCode status = HttpStatusCode.OK;
            Assert.Null(request.Headers.Authorization);
            if (uri == ChatGptSignInPolicy.Discovery) json = new() { ["issuer"] = ChatGptSignInPolicy.Issuer,
                ["authorization_endpoint"] = ChatGptSignInPolicy.Authorize, ["token_endpoint"] = ChatGptSignInPolicy.Token,
                ["jwks_uri"] = ChatGptSignInPolicy.Jwks, ["revocation_endpoint"] = Revocation };
            else if (uri == ChatGptSignInPolicy.Jwks)
            { var key = JsonWebKeyConverter.ConvertFromRSASecurityKey(new(rsa) { KeyId = "synthetic-key" });
                json = new() { ["keys"] = new JsonArray(JsonNode.Parse(JsonSerializer.Serialize(key))) }; }
            else if (uri == ChatGptSignInPolicy.Token)
            {
                var form = Query("http://127.0.0.1/?" + await request.Content!.ReadAsStringAsync(cancellation)); TokenForms.Add(form);
                json = Reply?.Invoke(form) ?? Tokens(IdToken("valid")); status = Status;
            }
            else { Assert.Equal(Revocation, uri); var form = Query("http://127.0.0.1/?" + await request.Content!.ReadAsStringAsync(cancellation));
                Assert.Equal("refresh", form["token"]); Assert.Equal(ClientId, form["client_id"]); Revocations++; json = null; }
            return new(status) { RequestMessage = request, Content = new StringContent(json?.ToJsonString() ?? "", Encoding.UTF8, "application/json") };
        }
        protected override void Dispose(bool disposing) { if (disposing) { rsa.Dispose(); http.Dispose(); } base.Dispose(disposing); }
    }
}
