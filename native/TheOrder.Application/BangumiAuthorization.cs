using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json.Serialization;

namespace TheOrder.Application;

public enum BangumiAuthorizationFailure { InvalidToken, Unauthorized, Offline, Timeout, RateLimited, Server, InvalidResponse, Storage, NotConfigured, Denied, ServiceUnavailable, Forbidden }
public sealed class BangumiAuthorizationException(BangumiAuthorizationFailure failure) : Exception(failure switch
{
    BangumiAuthorizationFailure.InvalidToken => "请输入有效的番组计划访问令牌。",
    BangumiAuthorizationFailure.Unauthorized => "番组计划授权无效或已过期，请重新连接或更新手动令牌。",
    BangumiAuthorizationFailure.Forbidden => "番组计划拒绝了本次访问，请稍后重试或检查账号权限；已保存授权保留。",
    BangumiAuthorizationFailure.Offline => "无法连接番组计划，请检查网络后重试。",
    BangumiAuthorizationFailure.Timeout => "番组计划验证超时，请重试。",
    BangumiAuthorizationFailure.RateLimited => "番组计划请求过于频繁，请稍后重试。",
    BangumiAuthorizationFailure.Server => "番组计划暂时不可用，请稍后重试。",
    BangumiAuthorizationFailure.ServiceUnavailable => "Bangumi 授权服务暂时不可用，请稍后重试；匿名浏览仍可使用。",
    BangumiAuthorizationFailure.Storage => "本机授权无法读取或保存，原资料已保留。",
    BangumiAuthorizationFailure.NotConfigured => "快捷登录尚未开通，请先使用手动令牌授权。",
    BangumiAuthorizationFailure.Denied => "未同意番组计划授权，原登录状态已保留。",
    _ => "番组计划返回的授权信息无效，请重试。"
}) { public BangumiAuthorizationFailure Failure { get; } = failure; }
public sealed record BangumiAccount(string Id, string Username, string Name);
public interface IBangumiCredentialStore
{
    Task<string?> ReadAsync(CancellationToken cancellation);
    Task SaveAsync(string token, CancellationToken cancellation);
    Task ClearAsync(CancellationToken cancellation);
    async Task<BangumiCredential?> ReadCredentialAsync(CancellationToken cancellation)
    { var value = await ReadAsync(cancellation).ConfigureAwait(false); return value is null ? null : BangumiCredential.Manual(value); }
    Task SaveCredentialAsync(BangumiCredential value, CancellationToken cancellation) => value.IsOAuth
        ? throw new BangumiAuthorizationException(BangumiAuthorizationFailure.Storage)
        : value.WithSecrets((token, _) => SaveAsync(token, cancellation));
}
public interface IBangumiAccountProvider
{ Task<BangumiAccount> ValidateAsync(string token, CancellationToken cancellation); }

// The token is deliberately not a public property, serializable model, or ToString field.
public sealed class BangumiAccessSnapshot
{
    private readonly string? token;
    private readonly DateTimeOffset? expires;
    private readonly TimeProvider clock;
    internal BangumiAccessSnapshot(string? value, CancellationToken changed, DateTimeOffset? expires = null, TimeProvider? clock = null)
    {
        token = value; Changed = changed; this.expires = expires; this.clock = clock ?? TimeProvider.System;
        Scope = value is null ? "anonymous" : "authorized-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    }
    public string Scope { get; }
    internal bool Expired => token is not null && expires <= clock.GetUtcNow();
    internal DateTimeOffset? Expiration => expires;
    public bool Authorized => token is not null && !Expired;
    [JsonIgnore] public CancellationToken Changed { get; }
    public void Authorize(HttpRequestMessage request)
    {
        Changed.ThrowIfCancellationRequested();
        if (Expired) throw new OperationCanceledException("番组计划授权已过期。");
        var uri = request.RequestUri;
        if (request.Method != HttpMethod.Get || uri is null || uri.Scheme != "https" || !uri.IsDefaultPort
            || uri.IdnHost != "api.bgm.tv" || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0
            || !AllowedRead(uri)
            || request.Headers.Authorization is not null || request.Headers.Contains("Cookie"))
            throw new BangumiAuthorizationException(BangumiAuthorizationFailure.InvalidResponse);
        if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }
    private static bool AllowedRead(Uri uri)
    {
        if (uri.Query.Length == 0)
            return Regex.IsMatch(uri.AbsolutePath, "^/v0/subjects/[1-9][0-9]{0,9}(?:/(?:subjects|characters|persons))?$", RegexOptions.CultureInvariant);
        // Canonical provider-generated paging only. No arbitrary query keys,
        // duplicate subject ids or token-bearing URLs can carry credentials.
        if (uri.AbsolutePath != "/v0/episodes" || uri.OriginalString.Contains('%')) return false;
        var match = Regex.Match(uri.Query, "^\\?subject_id=([1-9][0-9]{0,9})&limit=50&offset=(0|[1-9][0-9]{0,4})$", RegexOptions.CultureInvariant);
        return match.Success && BangumiRelationIdentityResolver.ValidSubjectId(match.Groups[1].Value)
            && int.TryParse(match.Groups[2].Value, out var offset) && offset <= 10000 && offset % 50 == 0;
    }
    public override string ToString() => Authorized ? "Bangumi access: authorized" : "Bangumi access: anonymous";
}

public sealed class BangumiAccessContext
{
    private readonly object gate = new();
    private CancellationTokenSource change = new();
    private BangumiAccessSnapshot current;
    public BangumiAccessContext() => current = new(null, change.Token);
    public event Action? Changed;
    public bool Rejected { get; private set; }
    public BangumiAccessSnapshot Capture()
    {
        CancellationTokenSource? old = null; BangumiAccessSnapshot value;
        lock (gate)
        {
            if (current.Expired) { old = change; change = new(); current = new(null, change.Token); Rejected = true; }
            value = current;
        }
        if (old is not null) { old.Cancel(); old.Dispose(); Changed?.Invoke(); }
        return value;
    }
    public bool IsCurrent(BangumiAccessSnapshot value) { lock (gate) return ReferenceEquals(current, value); }
    public static bool ValidScope(string? scope) => scope == "anonymous" || scope is { Length: 75 }
        && scope.StartsWith("authorized-", StringComparison.Ordinal) && scope.AsSpan(11).ToArray().All(c => c is >= '0' and <= '9' or >= 'A' and <= 'F');
    internal void Replace(string? token, DateTimeOffset? expires = null, TimeProvider? clock = null)
    {
        CancellationTokenSource old;
        lock (gate)
        {
            var next = new BangumiAccessSnapshot(token, CancellationToken.None);
            if (current.Scope == next.Scope && !Rejected && current.Expiration == expires) return;
            Rejected = false;
            old = change; change = new(); current = new(token, change.Token, expires, clock);
        }
        old.Cancel(); old.Dispose(); Changed?.Invoke();
    }
    public void Reject(BangumiAccessSnapshot value)
    {
        CancellationTokenSource old;
        lock (gate)
        {
            if (!ReferenceEquals(current, value) || !value.Authorized) return;
            old = change; change = new(); current = new(null, change.Token); Rejected = true;
        }
        old.Cancel(); old.Dispose(); Changed?.Invoke();
    }
    internal void RequireCurrent(BangumiAccessSnapshot value)
    { _ = Capture(); value.Changed.ThrowIfCancellationRequested(); if (!IsCurrent(value)) throw new OperationCanceledException("番组计划访问方式已变化。"); }
}

public sealed class BangumiAuthorization(IBangumiCredentialStore store, IBangumiAccountProvider accounts, BangumiAccessContext access,
    IBangumiOAuth? oauth = null, TimeProvider? clock = null)
{
    public const string TokenPage = "https://next.bgm.tv/demo/access-token";
    private readonly SemaphoreSlim serial = new(1);
    public BangumiAccessContext Access => access;
    public bool HasSavedToken { get; private set; }
    public BangumiAccount? Account { get; private set; }
    private readonly TimeProvider time = clock ?? TimeProvider.System;
    private BangumiCredential? saved;
    public bool ReauthorizationRequired { get; private set; }
    public bool QuickLoginAvailable => oauth?.Configured == true;
    public bool UsesQuickLogin => saved?.IsOAuth == true;
    public bool NeedsRefresh => !ReauthorizationRequired && saved is { IsOAuth: true, Expires: { } expires }
        && (expires <= time.GetUtcNow().AddMinutes(5) || access.Rejected);
    public bool RequiresLogin => ReauthorizationRequired || saved is { IsOAuth: true, Expires: { } expires } && expires <= time.GetUtcNow();
    public static string NormalizeToken(string input)
    {
        var value = input?.Trim();
        if (value is not { Length: >= 8 and <= 4096 } || !Regex.IsMatch(value, "^[A-Za-z0-9._~+/-]+={0,2}$", RegexOptions.CultureInvariant))
            throw new BangumiAuthorizationException(BangumiAuthorizationFailure.InvalidToken);
        return value;
    }
    public async Task InitializeAsync(CancellationToken cancellation)
    {
        await serial.WaitAsync(cancellation).ConfigureAwait(false);
        try
        {
            saved = await store.ReadCredentialAsync(cancellation).ConfigureAwait(false);
            cancellation.ThrowIfCancellationRequested();
            HasSavedToken = saved is not null;
            var scope = access.Capture().Scope;
            access.Replace(saved?.WithSecrets((token, _) => token), saved?.Expires, time);
            if (scope != access.Capture().Scope) Account = null;
            if (RequiresLogin) access.Reject(access.Capture());
            if (NeedsRefresh) await RefreshCoreAsync(cancellation).ConfigureAwait(false);
            else if (saved is { IsOAuth: true } credential && !ReauthorizationRequired)
            {
                var account = await credential.WithSecrets((token, _) => accounts.ValidateAsync(token, cancellation)).ConfigureAwait(false);
                cancellation.ThrowIfCancellationRequested();
                if (account.Id != credential.AccountId) { ReauthorizationRequired = true; access.Reject(access.Capture()); throw new BangumiAuthorizationException(BangumiAuthorizationFailure.InvalidResponse); }
                Account = account;
            }
        }
        catch (BangumiAuthorizationException error) when (error.Failure == BangumiAuthorizationFailure.Unauthorized)
        { ReauthorizationRequired = true; access.Reject(access.Capture()); throw; }
        finally { serial.Release(); }
    }
    public async Task<BangumiAccount> SaveAsync(string input, CancellationToken cancellation)
    {
        var token = NormalizeToken(input);
        await serial.WaitAsync(cancellation).ConfigureAwait(false);
        try
        {
            var account = await accounts.ValidateAsync(token, cancellation).ConfigureAwait(false);
            cancellation.ThrowIfCancellationRequested();
            var credential = BangumiCredential.Manual(token);
            await store.SaveCredentialAsync(credential, cancellation).ConfigureAwait(false);
            // A completed atomic replacement must also become the in-memory value.
            saved = credential; HasSavedToken = true; Account = account; ReauthorizationRequired = false; access.Replace(token); return account;
        }
        finally { serial.Release(); }
    }
    public async Task<BangumiAccount> TestAsync(CancellationToken cancellation)
    {
        await serial.WaitAsync(cancellation).ConfigureAwait(false);
        try
        {
            if (NeedsRefresh) await RefreshCoreAsync(cancellation).ConfigureAwait(false);
            var credential = await store.ReadCredentialAsync(cancellation).ConfigureAwait(false)
                ?? throw new BangumiAuthorizationException(BangumiAuthorizationFailure.InvalidToken);
            var token = credential.WithSecrets((value, _) => value);
            var account = await accounts.ValidateAsync(NormalizeToken(token), cancellation).ConfigureAwait(false);
            if (credential.IsOAuth && account.Id != credential.AccountId)
            { ReauthorizationRequired = true; access.Reject(access.Capture()); throw new BangumiAuthorizationException(BangumiAuthorizationFailure.InvalidResponse); }
            cancellation.ThrowIfCancellationRequested(); saved = credential; Account = account; HasSavedToken = true; ReauthorizationRequired = false; access.Replace(token, credential.Expires, time); return account;
        }
        catch (BangumiAuthorizationException error) when (error.Failure == BangumiAuthorizationFailure.Unauthorized)
        { ReauthorizationRequired = true; access.Reject(access.Capture()); throw; }
        finally { serial.Release(); }
    }
    public async Task ClearAsync(CancellationToken cancellation)
    {
        await serial.WaitAsync(cancellation).ConfigureAwait(false);
        try { await store.ClearAsync(cancellation).ConfigureAwait(false); saved = null; HasSavedToken = false; Account = null; ReauthorizationRequired = false; access.Replace(null); }
        finally { serial.Release(); }
    }
    public async Task SignInAsync(CancellationToken cancellation)
    {
        if (!QuickLoginAvailable) throw new BangumiAuthorizationException(BangumiAuthorizationFailure.NotConfigured);
        await serial.WaitAsync(cancellation).ConfigureAwait(false);
        try { await CommitOAuthAsync(await oauth!.SignInAsync(cancellation).ConfigureAwait(false), cancellation).ConfigureAwait(false); }
        finally { serial.Release(); }
    }
    public async Task RefreshAsync(CancellationToken cancellation)
    {
        await serial.WaitAsync(cancellation).ConfigureAwait(false);
        try { if (NeedsRefresh) await RefreshCoreAsync(cancellation).ConfigureAwait(false); }
        finally { serial.Release(); }
    }
    private async Task RefreshCoreAsync(CancellationToken cancellation)
    {
        try
        {
            if (saved is null || oauth?.Configured != true || oauth.Service != saved.Service)
                throw new BangumiAuthorizationException(BangumiAuthorizationFailure.NotConfigured);
            var refreshed = await oauth.RefreshAsync(saved, cancellation).ConfigureAwait(false);
            if (refreshed.AccountId != saved.AccountId) throw new BangumiAuthorizationException(BangumiAuthorizationFailure.InvalidResponse);
            await CommitOAuthAsync(refreshed, cancellation).ConfigureAwait(false);
        }
        catch (BangumiAuthorizationException error)
        {
            if (error.Failure == BangumiAuthorizationFailure.Unauthorized) ReauthorizationRequired = true;
            if (RequiresLogin || error.Failure == BangumiAuthorizationFailure.Unauthorized) access.Reject(access.Capture());
            throw;
        }
        catch (OperationCanceledException)
        { if (RequiresLogin) access.Reject(access.Capture()); throw; }
    }
    private async Task CommitOAuthAsync(BangumiCredential value, CancellationToken cancellation)
    {
        if (!value.IsOAuth || value.Service != oauth?.Service || value.Expires <= time.GetUtcNow())
            throw new BangumiAuthorizationException(BangumiAuthorizationFailure.InvalidResponse);
        var account = await value.WithSecrets((token, _) => accounts.ValidateAsync(token, cancellation)).ConfigureAwait(false);
        if (account.Id != value.AccountId) throw new BangumiAuthorizationException(BangumiAuthorizationFailure.InvalidResponse);
        cancellation.ThrowIfCancellationRequested();
        await store.SaveCredentialAsync(value, cancellation).ConfigureAwait(false);
        saved = value; Account = account; HasSavedToken = true; ReauthorizationRequired = false; access.Replace(value.WithSecrets((token, _) => token), value.Expires, time);
    }
}
