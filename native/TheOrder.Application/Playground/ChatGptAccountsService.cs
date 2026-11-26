using System.Collections.Immutable;

namespace TheOrder.Application.Playground;

// One instance per application-owned profile. Refresh and token rotation share the same queue.
public sealed class ChatGptAccountsService(IChatGptAccountStore store, IChatGptBrowserSignIn browser,
    IChatGptOAuthClient oauth, IPlaygroundCredentials apiKeys, TimeProvider? time = null) : IChatGptAccounts, IPlaygroundCredentials
{
    private readonly SemaphoreSlim serial = new(1, 1), signingIn = new(1, 1);
    private readonly TimeProvider clock = time ?? TimeProvider.System;
    private long intent;
    private ChatGptAccountState Empty() => new(1, 0, store.Identity, "urn:uuid:" + Guid.NewGuid().ToString("D"), null, []);
    private static ChatGptAccountInfo Info(ChatGptRegistration account) => new(account.ClientId, account.Label,
        account.Email, account.Connected, account.PlanEnabled, account.Welcomed);
    private Task Save(ChatGptAccountState before, ChatGptAccountState after, CancellationToken cancellation)
        => store.SaveAsync(after with { Revision = before.Revision + 1 }, before.Revision, cancellation);
    private static ChatGptRegistration Find(ChatGptAccountState state, string? clientId) =>
        state.Accounts.FirstOrDefault(a => a.ClientId == clientId)
        ?? throw new ChatGptAuthException(ChatGptAuthFailure.NeedsSignIn, "请选择 ChatGPT 账号并登录。");
    public async Task<ChatGptAccountSnapshot> ReadAccountsAsync(CancellationToken cancellation = default)
    {
        await serial.WaitAsync(cancellation).ConfigureAwait(false);
        try { var state = await store.ReadAsync(cancellation).ConfigureAwait(false); return state is null ? new(null, [])
            : new(state.SelectedClientId, [.. state.Accounts.Select(Info)]); }
        finally { serial.Release(); }
    }
    public async Task<ChatGptAccountInfo> SignInAsync(string? clientId, bool requestConsent, CancellationToken cancellation = default)
    {
        await signingIn.WaitAsync(cancellation).ConfigureAwait(false);
        try
        {
            var generation = Interlocked.Increment(ref intent);
            ChatGptSignInRequest request;
            await serial.WaitAsync(cancellation).ConfigureAwait(false);
            try
            {
                var state = await store.ReadAsync(cancellation).ConfigureAwait(false);
                if (state is null) { var empty = Empty(); await Save(empty, empty, cancellation).ConfigureAwait(false); state = empty with { Revision = 1 }; }
                if (clientId is null && state.Accounts.Length >= 16) throw new ChatGptAuthException(ChatGptAuthFailure.Storage, "已保存的 ChatGPT 连接达到上限。");
                var previous = clientId is null ? null : Find(state, clientId);
                request = new(state.HostId, previous?.ClientId ?? state.PendingClientId, previous?.IdToken,
                    previous?.Email, requestConsent);
            }
            finally { serial.Release(); }
            var code = await browser.AuthorizeAsync(request, cancellation).ConfigureAwait(false);
            await serial.WaitAsync(cancellation).ConfigureAwait(false);
            try
            {
                EnsureIntent(generation, cancellation);
                if (request.ClientId is not null && request.ClientId != code.ClientId) throw InvalidIdentity();
                if (clientId is null)
                {
                    var state = await store.ReadAsync(cancellation).ConfigureAwait(false) ?? throw InvalidIdentity();
                    await Save(state, state with { PendingClientId = code.ClientId }, cancellation).ConfigureAwait(false);
                }
            }
            finally { serial.Release(); }
            // Retain registration even if this one-time code expires. No automatic token POST retry.
            var session = await oauth.ExchangeAsync(code, cancellation).ConfigureAwait(false);
            await serial.WaitAsync(cancellation).ConfigureAwait(false);
            try
            {
                EnsureIntent(generation, cancellation);
                var state = await store.ReadAsync(cancellation).ConfigureAwait(false) ?? throw InvalidIdentity();
                var previous = state.Accounts.FirstOrDefault(a => a.ClientId == code.ClientId);
                if (session.ClientId != code.ClientId || previous is not null && previous.Subject != session.Subject) throw InvalidIdentity();
                if (clientId is not null && Find(state, clientId).Subject != session.Subject) throw InvalidIdentity();
                var label = previous?.Label ?? (session.Email ?? "ChatGPT") + " ·连接 " + (state.Accounts.Length + 1);
                session = session with { Label = label, Welcomed = previous?.Welcomed ?? false };
                var next = state with { Accounts = [.. state.Accounts.Where(a => a.ClientId != session.ClientId), session],
                    SelectedClientId = session.ClientId, PendingClientId = state.PendingClientId == session.ClientId ? null : state.PendingClientId };
                await Save(state, next, cancellation).ConfigureAwait(false);
                return Info(session);
            }
            finally { serial.Release(); }
        }
        finally { signingIn.Release(); }
    }
    private void EnsureIntent(long generation, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        if (generation != Volatile.Read(ref intent)) throw new OperationCanceledException("ChatGPT 连接选择已变化。", cancellation);
    }
    private static ChatGptAuthException InvalidIdentity() => new(ChatGptAuthFailure.InvalidResponse, "ChatGPT 登录身份与所选连接不一致，原连接已保留。");
    public async Task SelectAsync(string clientId, CancellationToken cancellation = default)
    {
        Interlocked.Increment(ref intent); await serial.WaitAsync(cancellation).ConfigureAwait(false);
        try { var state = await store.ReadAsync(cancellation).ConfigureAwait(false) ?? throw InvalidIdentity(); _ = Find(state, clientId);
            if (state.SelectedClientId != clientId) await Save(state, state with { SelectedClientId = clientId }, cancellation).ConfigureAwait(false); }
        finally { serial.Release(); }
    }
    public async Task MarkWelcomedAsync(string clientId, CancellationToken cancellation = default)
    {
        await serial.WaitAsync(cancellation).ConfigureAwait(false);
        try { var state = await store.ReadAsync(cancellation).ConfigureAwait(false) ?? throw InvalidIdentity(); var account = Find(state, clientId);
            if (!account.Welcomed) await Save(state, state with { Accounts = state.Accounts.Replace(account, account with { Welcomed = true }) }, cancellation).ConfigureAwait(false); }
        finally { serial.Release(); }
    }
    public async Task<bool> SignOutAsync(string clientId, CancellationToken cancellation = default)
    {
        Interlocked.Increment(ref intent); await serial.WaitAsync(cancellation).ConfigureAwait(false);
        try
        {
            var state = await store.ReadAsync(cancellation).ConfigureAwait(false) ?? throw InvalidIdentity();
            var account = Find(state, clientId); bool revoked = account.RefreshToken is null;
            if (account.RefreshToken is not null)
            {
                try { revoked = await oauth.RevokeAsync(account, cancellation).ConfigureAwait(false); }
                catch (Exception e) when (e is not OutOfMemoryException) { revoked = false; }
            }
            // Local sign-out completes even when revocation cannot be confirmed. Preserve the
            // client/account mapping and host ID, never the expired tokens or ID-token hint.
            await Save(state, state with { Accounts = state.Accounts.Replace(account, account.WithoutTokens()) }, CancellationToken.None).ConfigureAwait(false);
            return revoked;
        }
        finally { serial.Release(); }
    }
    public Task<bool> HasAsync(PlaygroundProviderKind provider, string endpoint, CancellationToken cancellation)
        => provider == PlaygroundProviderKind.ChatGPT ? HasPlan(cancellation) : apiKeys.HasAsync(provider, endpoint, cancellation);
    private async Task<bool> HasPlan(CancellationToken cancellation)
    { var snapshot = await ReadAccountsAsync(cancellation).ConfigureAwait(false); return snapshot.Accounts.Any(a => a.ClientId == snapshot.SelectedClientId && a.PlanEnabled); }
    public async Task<bool> HasForAccountAsync(PlaygroundProviderKind provider, string endpoint, string? accountId, CancellationToken cancellation)
    {
        if (provider != PlaygroundProviderKind.ChatGPT) return await apiKeys.HasForAccountAsync(provider, endpoint, accountId, cancellation).ConfigureAwait(false);
        _ = PlaygroundEndpointPolicy.Normalize(provider, endpoint);
        var snapshot = await ReadAccountsAsync(cancellation).ConfigureAwait(false);
        return snapshot.Accounts.Any(a => a.ClientId == accountId && a.PlanEnabled);
    }
    public Task<string?> ReadAsync(PlaygroundProviderKind provider, string endpoint, CancellationToken cancellation)
        => ReadToken(provider, endpoint, null, true, cancellation);
    public Task<string?> ReadForAccountAsync(PlaygroundProviderKind provider, string endpoint, string? accountId, CancellationToken cancellation)
        => ReadToken(provider, endpoint, accountId, false, cancellation);
    private async Task<string?> ReadToken(PlaygroundProviderKind provider, string endpoint, string? clientId, bool selected, CancellationToken cancellation)
    {
        if (provider != PlaygroundProviderKind.ChatGPT) return await apiKeys.ReadForAccountAsync(provider, endpoint, clientId, cancellation).ConfigureAwait(false);
        _ = PlaygroundEndpointPolicy.Normalize(provider, endpoint);
        long expectedIntent = Volatile.Read(ref intent);
        await serial.WaitAsync(cancellation).ConfigureAwait(false);
        try
        {
            var state = await store.ReadAsync(cancellation).ConfigureAwait(false) ?? throw InvalidIdentity();
            EnsureIntent(expectedIntent, cancellation);
            var account = Find(state, selected ? state.SelectedClientId : clientId);
            if (!account.Connected) throw new ChatGptAuthException(ChatGptAuthFailure.NeedsSignIn, "此 ChatGPT 账号已退出，请重新登录。");
            if (!account.PlanEnabled) throw new ChatGptAuthException(ChatGptAuthFailure.AccessDenied, "尚未授权使用 ChatGPT 额度，请在连接设置中启用。");
            if (account.ExpiresAt <= clock.GetUtcNow().AddSeconds(60))
            {
                if (account.RefreshToken is null) throw new ChatGptAuthException(ChatGptAuthFailure.NeedsSignIn, "ChatGPT 登录已过期，请重新登录。");
                ChatGptRegistration refreshed;
                // An accepted refresh can rotate the token even if this turn is stopped.
                // Let the bounded exchange finish and persist its result before observing cancellation.
                try { refreshed = await oauth.RefreshAsync(account, CancellationToken.None).ConfigureAwait(false); }
                catch (ChatGptAuthException e) when (e.Kind == ChatGptAuthFailure.NeedsSignIn)
                { await Save(state, state with { Accounts = state.Accounts.Replace(account, account.WithoutTokens()) }, CancellationToken.None).ConfigureAwait(false); throw; }
                if (refreshed.ClientId != account.ClientId || refreshed.Subject != account.Subject) throw InvalidIdentity();
                await Save(state, state with { Accounts = state.Accounts.Replace(account, refreshed) }, CancellationToken.None).ConfigureAwait(false);
                account = refreshed;
            }
            if (!account.PlanEnabled) throw new ChatGptAuthException(ChatGptAuthFailure.AccessDenied, "ChatGPT 额度授权已变化，请重新授权。");
            EnsureIntent(expectedIntent, cancellation);
            return account.AccessToken;
        }
        finally { serial.Release(); }
    }
    public Task SaveAsync(PlaygroundProviderKind provider, string endpoint, string key, CancellationToken cancellation)
        => provider == PlaygroundProviderKind.ChatGPT ? Task.FromException(new InvalidOperationException("请使用 ChatGPT 登录，无需填写 API Key。"))
            : apiKeys.SaveAsync(provider, endpoint, key, cancellation);
    public Task ClearAsync(PlaygroundProviderKind provider, string endpoint, CancellationToken cancellation)
        => provider == PlaygroundProviderKind.ChatGPT ? Task.FromException(new InvalidOperationException("请在 ChatGPT 账号设置中退出登录。"))
            : apiKeys.ClearAsync(provider, endpoint, cancellation);
}
