using System.Security.Cryptography;

namespace TheOrder.Application;

public enum NextMoeAccessMode { Anonymous, Account, ApiKey }
public sealed record NextMoeAccount(string Id, string Name);
public sealed record NextMoeAuthStatus(NextMoeAccessMode Mode, NextMoeAccount? Account, bool NeedsLogin, string[] Scopes);
// A persisted refresh-in-progress flag makes a lost rotation fail closed after a crash.
public sealed record NextMoeAuthRecord(int Schema, NextMoeAccessMode Mode, NextMoeAccount? Account,
    string? RefreshToken, string[] Scopes, bool RefreshInProgress = false)
{ public override string ToString() => $"NextMoeAuthRecord [{Mode}, protected]"; }
public static class NextMoeAuthValidation
{
    public static void Validate(NextMoeAuthRecord record)
    {
        if (record is null || record.Schema != 1 || !Enum.IsDefined(record.Mode) || record.Scopes is null || record.Scopes.Length > 16
            || record.Scopes.Any(x => string.IsNullOrEmpty(x) || x.Length > 100 || x.Any(char.IsWhiteSpace)) || record.Scopes.Distinct(StringComparer.Ordinal).Count() != record.Scopes.Length
            || record.Mode == NextMoeAccessMode.Account && record.Account is null
            || record.Account is null && record.RefreshToken is not null
            || record.Account is { } account && (string.IsNullOrWhiteSpace(account.Id) || account.Id.Length > 256 || account.Id.Any(char.IsControl)
                || account.Name is null || account.Name.Length > 256 || account.Name.Any(char.IsControl))
            || record.RefreshToken is { } refresh && (refresh.Length is < 1 or > 16384 || refresh.Any(c => c < 0x21 || c > 0x7e)))
            throw new OnlineException(OnlineFailure.CredentialStorageCorrupt);
    }
}
public interface INextMoeAuthStore
{
    Task<NextMoeAuthRecord?> LoadAsync(CancellationToken cancellation);
    Task SaveAsync(NextMoeAuthRecord record, CancellationToken cancellation);
}
public sealed record OAuthTokens(string AccessToken, string RefreshToken, DateTimeOffset ExpiresAt, string[] Scopes, NextMoeAccount Account)
{ public override string ToString() => "OAuthTokens [protected]"; }
public sealed record OAuthAuthorizationCode(string Code, string Verifier, Uri Redirect)
{ public override string ToString() => "OAuthAuthorizationCode [protected]"; }
public interface IOAuthLogin { Task<OAuthAuthorizationCode> AuthorizeAsync(string clientId, string[] scopes, CancellationToken cancellation); }
public interface INextMoeOAuth
{
    Task<OAuthTokens> ExchangeAsync(string clientId, OAuthAuthorizationCode code, CancellationToken cancellation);
    Task<OAuthTokens> RefreshAsync(string clientId, string refreshToken, CancellationToken cancellation);
}
public interface INextMoeAccess : ICredentialProvider, IOnlineAuthContextSource
{
    Task<NextMoeAuthStatus> StatusAsync(CancellationToken cancellation);
    Task<string> UserTokenAsync(string? scope, CancellationToken cancellation);
    Task<bool> RefreshRejectedAsync(string rejectedAccess, CancellationToken cancellation);
}
public static class NextMoeOAuthIdentity
{
    // Public application number verified against the official authorization endpoint, not a secret.
    public const string ClientId = "2df77ee1e0167493048a94b677c73e10";
    public static readonly string[] CatalogScopes = ["openid", "profile", "catalog:read"];
    public static readonly string[] FolderReadScopes = ["openid", "profile", "catalog:read", "folder:read"];
    public static readonly string[] FolderWriteScopes = ["openid", "profile", "catalog:read", "folder:read", "folder:write"];
}

// Identity transitions use the existing request admission fence. Refresh keeps the same identity.
// Legacy key storage remains intact when selecting anonymous/account access.
public sealed class NextMoeAuthentication(ICredentialSettings keys, ICredentialProvider keyReader, INextMoeAuthStore store,
    INextMoeOAuth oauth, IOAuthLogin login, string clientId = NextMoeOAuthIdentity.ClientId, TimeProvider? clock = null)
    : ICredentialSettings, INextMoeAccess
{
    public OnlineAuthContext AuthContext { get; } = new();
    private readonly SemaphoreSlim serial = new(1, 1);
    private NextMoeAuthRecord? current;
    private OAuthTokens? access;
    private bool refreshFailed;
    private bool unavailable;
    // Display-only committed identity; never reads or exposes tokens, and never refreshes a session.
    public NextMoeAuthStatus? CurrentStatus => current is { } record ? new(record.Mode, record.Account,
        record.Mode == NextMoeAccessMode.Account && (refreshFailed || string.IsNullOrEmpty(record.RefreshToken)), [.. record.Scopes]) : null;
    public bool CatalogAvailable => !unavailable && current is { } record
        && (record.Mode != NextMoeAccessMode.Account || !refreshFailed && !string.IsNullOrEmpty(record.RefreshToken));
    public event EventHandler? Changed;
    private DateTimeOffset Now => (clock ?? TimeProvider.System).GetUtcNow();
    private async Task Initialize(CancellationToken cancellation)
    {
        if (current is not null) return;
        try
        {
        var loaded = await store.LoadAsync(cancellation).ConfigureAwait(false);
        if (loaded is null)
        {
            // Only the old protected-key adapter decides whether a valid legacy key exists.
            var mode = await keys.HasCredentialAsync(cancellation).ConfigureAwait(false) ? NextMoeAccessMode.ApiKey : NextMoeAccessMode.Anonymous;
            var initial = new NextMoeAuthRecord(1, mode, null, null, []);
            await store.SaveAsync(initial, cancellation).ConfigureAwait(false); current = initial;
        }
        else { NextMoeAuthValidation.Validate(loaded); current = loaded; }
        refreshFailed = current.RefreshInProgress;
        unavailable = false;
        }
        catch { unavailable = true; throw; }
    }
    public async Task RecoverCorruptLoginAsync(CancellationToken cancellation)
    {
        AuthContext.BeginMutation();
        try
        {
            await serial.WaitAsync(cancellation).ConfigureAwait(false);
            try
            {
                try { await Initialize(cancellation).ConfigureAwait(false); throw new OnlineException(OnlineFailure.InvalidRequest); }
                catch (OnlineException e) when (e.Failure == OnlineFailure.CredentialStorageCorrupt)
                {
                    var next = new NextMoeAuthRecord(1, NextMoeAccessMode.Anonymous, null, null, []);
                    await store.SaveAsync(next, cancellation).ConfigureAwait(false);
                    current = next; access = null; refreshFailed = unavailable = false;
                }
            }
            finally { serial.Release(); }
        }
        finally { AuthContext.EndMutation(); Changed?.Invoke(this, EventArgs.Empty); }
    }
    public async Task<NextMoeAuthStatus> StatusAsync(CancellationToken cancellation)
    {
        await serial.WaitAsync(cancellation).ConfigureAwait(false);
        try { await Initialize(cancellation).ConfigureAwait(false); return CurrentStatus!; }
        finally { serial.Release(); }
    }
    public async Task<string?> ReadAsync(CancellationToken cancellation)
    {
        await serial.WaitAsync(cancellation).ConfigureAwait(false);
        try
        {
            await Initialize(cancellation).ConfigureAwait(false);
            return current!.Mode switch { NextMoeAccessMode.Anonymous => null,
                NextMoeAccessMode.ApiKey => await keyReader.ReadAsync(cancellation).ConfigureAwait(false) ?? throw new OnlineException(OnlineFailure.NoCredential),
                _ => (await GetAccountToken(cancellation).ConfigureAwait(false)).AccessToken };
        }
        finally { serial.Release(); }
    }
    public async Task<string> UserTokenAsync(string? scope, CancellationToken cancellation)
    {
        await serial.WaitAsync(cancellation).ConfigureAwait(false);
        try
        {
            await Initialize(cancellation).ConfigureAwait(false);
            if (current!.Mode != NextMoeAccessMode.Account) throw new OnlineException(OnlineFailure.UserIdentityRequired);
            var token = await GetAccountToken(cancellation).ConfigureAwait(false);
            if (scope is not null && !token.Scopes.Contains(scope, StringComparer.Ordinal)
                && !(scope == "folder:read" && token.Scopes.Contains("folder:write", StringComparer.Ordinal))) throw new OnlineException(OnlineFailure.ScopeRequired);
            return token.AccessToken;
        }
        finally { serial.Release(); }
    }
    private async Task<OAuthTokens> GetAccountToken(CancellationToken cancellation)
    {
        if (refreshFailed || current!.RefreshInProgress || string.IsNullOrEmpty(current.RefreshToken)) throw new OnlineException(OnlineFailure.LoginRequired);
        if (access is { } valid && valid.ExpiresAt > Now.AddSeconds(45)) return valid;
        var pending = current with { RefreshInProgress = true };
        await store.SaveAsync(pending, cancellation).ConfigureAwait(false); current = pending;
        try
        {
            var next = await oauth.RefreshAsync(clientId, pending.RefreshToken!, cancellation).ConfigureAwait(false);
            if (next.Account.Id != pending.Account!.Id) throw new OnlineException(OnlineFailure.LoginRequired);
            var committed = new NextMoeAuthRecord(1, NextMoeAccessMode.Account, next.Account, next.RefreshToken, next.Scopes);
            // A server rotation already happened. Finish the bounded local atomic save even if the caller canceled.
            await store.SaveAsync(committed, CancellationToken.None).ConfigureAwait(false);
            current = committed; access = next; cancellation.ThrowIfCancellationRequested(); return next;
        }
        catch
        {
            if (current.RefreshInProgress) { refreshFailed = true; access = null; Changed?.Invoke(this, EventArgs.Empty); }
            throw;
        }
    }
    public async Task<bool> RefreshRejectedAsync(string rejectedAccess, CancellationToken cancellation)
    {
        await serial.WaitAsync(cancellation).ConfigureAwait(false);
        try
        {
            await Initialize(cancellation).ConfigureAwait(false);
            if (current!.Mode != NextMoeAccessMode.Account) return false;
            if (access?.AccessToken == rejectedAccess) access = null;
            await GetAccountToken(cancellation).ConfigureAwait(false); return true;
        }
        finally { serial.Release(); }
    }
    private async Task Transition(Func<CancellationToken, Task> change, CancellationToken cancellation)
    {
        AuthContext.BeginMutation();
        try { await serial.WaitAsync(cancellation).ConfigureAwait(false);
            try { await Initialize(cancellation).ConfigureAwait(false); await change(cancellation).ConfigureAwait(false); }
            finally { serial.Release(); } }
        finally { AuthContext.EndMutation(); Changed?.Invoke(this, EventArgs.Empty); }
    }
    public Task SelectAsync(NextMoeAccessMode mode, CancellationToken cancellation) => Transition(async token =>
    {
        if (!Enum.IsDefined(mode)) throw new OnlineException(OnlineFailure.InvalidRequest);
        if (mode == NextMoeAccessMode.ApiKey && !await keys.HasCredentialAsync(token).ConfigureAwait(false)) throw new OnlineException(OnlineFailure.NoCredential);
        if (mode == NextMoeAccessMode.Account && current!.Account is null) throw new OnlineException(OnlineFailure.LoginRequired);
        var next = current! with { Mode = mode }; await store.SaveAsync(next, token).ConfigureAwait(false); current = next;
    }, cancellation);
    public Task SignInAsync(bool folderRead, bool folderWrite, CancellationToken cancellation) => Transition(async token =>
    {
        var scopes = folderWrite ? NextMoeOAuthIdentity.FolderWriteScopes : folderRead ? NextMoeOAuthIdentity.FolderReadScopes : NextMoeOAuthIdentity.CatalogScopes;
        var code = await login.AuthorizeAsync(clientId, scopes, token).ConfigureAwait(false);
        var next = await oauth.ExchangeAsync(clientId, code, token).ConfigureAwait(false);
        if (scopes.Except(next.Scopes, StringComparer.Ordinal).Any()) throw new OnlineException(OnlineFailure.ScopeRequired);
        var record = new NextMoeAuthRecord(1, NextMoeAccessMode.Account, next.Account, next.RefreshToken, next.Scopes);
        await store.SaveAsync(record, CancellationToken.None).ConfigureAwait(false); current = record; access = next; refreshFailed = false;
    }, cancellation);
    public Task SignOutAsync(CancellationToken cancellation) => Transition(async token =>
    {
        var next = new NextMoeAuthRecord(1, NextMoeAccessMode.Anonymous, null, null, []);
        await store.SaveAsync(next, token).ConfigureAwait(false); current = next; access = null; refreshFailed = false;
    }, cancellation);
    public async Task<bool> HasCredentialAsync(CancellationToken cancellation)
    {
        // Historical UI uses this as key-presence, never as anonymous catalog availability.
        await serial.WaitAsync(cancellation).ConfigureAwait(false);
        try { await Initialize(cancellation).ConfigureAwait(false); return await keys.HasCredentialAsync(cancellation).ConfigureAwait(false); }
        finally { serial.Release(); }
    }
    public Task SaveAsync(string secret, CancellationToken cancellation) => Transition(async token =>
    {
        await keys.SaveAsync(secret, token).ConfigureAwait(false);
        var next = current! with { Mode = NextMoeAccessMode.ApiKey }; await store.SaveAsync(next, CancellationToken.None).ConfigureAwait(false); current = next;
    }, cancellation);
    public Task ClearAsync(CancellationToken cancellation) => Transition(async token =>
    {
        await keys.ClearAsync(token).ConfigureAwait(false);
        var next = current! with { Mode = current.Mode == NextMoeAccessMode.ApiKey ? NextMoeAccessMode.Anonymous : current.Mode };
        await store.SaveAsync(next, CancellationToken.None).ConfigureAwait(false); current = next;
    }, cancellation);
}
