using System.Collections.Immutable;

namespace TheOrder.Application.Playground;

public static class ChatGptSignInPolicy
{
    public const string Issuer = "https://auth.openai.com";
    public const string Authorize = Issuer + "/api/accounts/authorize";
    public const string Token = Issuer + "/api/accounts/oauth/token";
    public const string Discovery = Issuer + "/.well-known/openid-configuration";
    public const string Jwks = Issuer + "/.well-known/jwks.json";
    public const string Resource = "https://api.openai.com/v1";
    public const string Usage = "https://chatgpt.com/settings/usage";
    public const string UsageLimitMessage = "此应用可用的 ChatGPT 额度已达到上限。可打开「管理 ChatGPT 用量」，或从「连接与授权 → OpenAI」手动切换到 API Key。";
    public const string DynamicClient = "dynamic_agent_client";
    public const string PlanScope = "chatgpt.tokens.use.direct";
    public const string Scopes = "openid profile email offline_access resource.invoke " + PlanScope;
    public static bool ValidClient(string? value) => value is { Length: > 0 and <= 256 } && value != DynamicClient
        && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '.');
    public static bool ValidHost(string? value) => value is not null && value.StartsWith("urn:uuid:", StringComparison.Ordinal)
        && Guid.TryParseExact(value[9..], "D", out var id) && id != Guid.Empty;
    public static bool SafeToken(string? value) => value is { Length: > 0 and <= 65536 }
        && value.All(c => c >= 0x21 && c <= 0x7e);
}

// This record is a protected storage/transport value, never observable UI or chat state.
public sealed record ChatGptRegistration(string ClientId, string Subject, string? Email, string Label,
    string? AccessToken, string? RefreshToken, string? IdToken, DateTimeOffset ExpiresAt,
    ImmutableArray<string> Scopes, bool Welcomed = false)
{
    public bool Connected => AccessToken is not null;
    public bool PlanEnabled => Connected && Scopes.Contains(ChatGptSignInPolicy.PlanScope, StringComparer.Ordinal);
    public ChatGptRegistration WithoutTokens() => this with { AccessToken = null, RefreshToken = null, IdToken = null,
        ExpiresAt = DateTimeOffset.MinValue, Scopes = [] };
    public override string ToString() => "[protected ChatGPT registration]";
}
public sealed record ChatGptAccountState(int Version, long Revision, PlaygroundIdentity Identity, string HostId,
    string? SelectedClientId, ImmutableArray<ChatGptRegistration> Accounts, string? PendingClientId = null)
{
    public override string ToString() => "[protected ChatGPT account state]";
}
public sealed record ChatGptAccountInfo(string ClientId, string Label, string? Email, bool Connected, bool PlanEnabled, bool Welcomed);
public sealed record ChatGptAccountSnapshot(string? SelectedClientId, ImmutableArray<ChatGptAccountInfo> Accounts);
public sealed record ChatGptSignInRequest(string HostId, string? ClientId, string? IdTokenHint, string? LoginHint, bool RequestConsent)
{ public override string ToString() => "[ChatGPT sign-in request]"; }
public sealed record ChatGptAuthorizationCode(string Code, string ClientId, string Verifier, string Nonce, string RedirectUri)
{ public override string ToString() => "[one-time ChatGPT authorization]"; }
public enum ChatGptAuthFailure { Cancelled, InvalidResponse, Network, NeedsSignIn, AccessDenied, Storage, Unsupported }
public sealed class ChatGptAuthException(ChatGptAuthFailure kind, string message) : Exception(message)
{ public ChatGptAuthFailure Kind { get; } = kind; }
public interface IChatGptAccountStore
{
    PlaygroundIdentity Identity { get; }
    Task<ChatGptAccountState?> ReadAsync(CancellationToken cancellation);
    Task SaveAsync(ChatGptAccountState state, long expectedRevision, CancellationToken cancellation);
}
public interface IChatGptBrowserSignIn
{ Task<ChatGptAuthorizationCode> AuthorizeAsync(ChatGptSignInRequest request, CancellationToken cancellation); }
public interface IChatGptOAuthClient
{
    Task<ChatGptRegistration> ExchangeAsync(ChatGptAuthorizationCode code, CancellationToken cancellation);
    Task<ChatGptRegistration> RefreshAsync(ChatGptRegistration account, CancellationToken cancellation);
    Task<bool> RevokeAsync(ChatGptRegistration account, CancellationToken cancellation);
}
public interface IChatGptAccounts
{
    Task<ChatGptAccountSnapshot> ReadAccountsAsync(CancellationToken cancellation = default);
    Task<ChatGptAccountInfo> SignInAsync(string? clientId, bool requestConsent, CancellationToken cancellation = default);
    Task SelectAsync(string clientId, CancellationToken cancellation = default);
    Task<bool> SignOutAsync(string clientId, CancellationToken cancellation = default);
    Task MarkWelcomedAsync(string clientId, CancellationToken cancellation = default);
}
