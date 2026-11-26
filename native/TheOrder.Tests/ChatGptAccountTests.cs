using System.Collections.Immutable;
using System.Text;
using TheOrder.Application;
using TheOrder.Application.Playground;
using TheOrder.Infrastructure;
using TheOrder.Infrastructure.Legacy;
using TheOrder.Platform.Windows;
using Xunit;

namespace TheOrder.Tests;

public sealed class ChatGptAccountTests
{
    private static readonly string Endpoint = ChatGptSignInPolicy.Resource;
    private static ChatGptRegistration Account(string id, string subject = "synthetic-sub", bool expired = false) => new(id, subject,
        "same@example.invalid", "Connection " + id, "access-" + id, "refresh-" + id, "id-" + id,
        DateTimeOffset.UtcNow.AddMinutes(expired ? -1 : 10), [.. ChatGptSignInPolicy.Scopes.Split(' ')]);
    [Fact] public async Task LazyReadCreatesNothingAndFailedCodeReusesSavedRegistrationAndHost()
    {
        var rig = new Rig(); Assert.Empty((await rig.Service.ReadAccountsAsync()).Accounts); Assert.Null(rig.Store.State);
        rig.OAuth.OnExchange = code =>
        {
            Assert.Equal(code.ClientId, rig.Store.State!.PendingClientId);
            return Task.FromException<ChatGptRegistration>(new ChatGptAuthException(ChatGptAuthFailure.AccessDenied, "expired synthetic code"));
        };
        await Assert.ThrowsAsync<ChatGptAuthException>(() => rig.Service.SignInAsync(null, false));
        string host = rig.Store.State!.HostId; Assert.Equal("oaiapp_1", rig.Store.State.PendingClientId);
        rig.OAuth.OnExchange = code => Task.FromResult(Account(code.ClientId));
        var info = await rig.Service.SignInAsync(null, false);
        Assert.Equal("oaiapp_1", info.ClientId); Assert.Equal(host, rig.Browser.Requests[1].HostId);
        Assert.Equal("oaiapp_1", rig.Browser.Requests[1].ClientId); Assert.Null(rig.Store.State.PendingClientId);
    }
    [Fact] public async Task SameEmailKeepsSeparateRegistrationsAndWrongSubjectNeverOverwrites()
    {
        var rig = new Rig(); var a = await rig.Service.SignInAsync(null, false); var b = await rig.Service.SignInAsync(null, false);
        Assert.NotEqual(a.ClientId, b.ClientId); Assert.Equal(a.Email, b.Email); Assert.NotEqual(a.Label, b.Label);
        rig.OAuth.OnExchange = code => Task.FromResult(Account(code.ClientId, "changed-sub"));
        await Assert.ThrowsAsync<ChatGptAuthException>(() => rig.Service.SignInAsync(a.ClientId, false));
        Assert.Equal(2, rig.Store.State!.Accounts.Length); Assert.Equal("synthetic-sub", rig.Store.State.Accounts[0].Subject);
        Assert.Equal(b.ClientId, rig.Store.State.SelectedClientId);
    }
    [Fact] public async Task ExplicitAccountNeverFallsBackToGloballySelectedAccount()
    {
        var rig = new Rig(); var a = await rig.Service.SignInAsync(null, false); var b = await rig.Service.SignInAsync(null, false);
        Assert.Equal(b.ClientId, rig.Store.State!.SelectedClientId);
        Assert.Equal("access-" + a.ClientId, await rig.Service.ReadForAccountAsync(PlaygroundProviderKind.ChatGPT, Endpoint, a.ClientId, default));
        await Assert.ThrowsAsync<ChatGptAuthException>(() => rig.Service.ReadForAccountAsync(PlaygroundProviderKind.ChatGPT, Endpoint, null, default));
        Assert.Equal(0, rig.OAuth.Refreshes);
    }
    [Fact] public async Task ConcurrentRenewalsPublishOneRotatedTokenSet()
    {
        var rig = new Rig(); var info = await rig.Service.SignInAsync(null, false);
        var old = rig.Store.State!.Accounts[0]; rig.Store.State = rig.Store.State with { Accounts = [old with { ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(-1) }] };
        rig.OAuth.OnRefresh = async account => { await Task.Delay(20); return account with { AccessToken = "rotated-access", RefreshToken = "rotated-refresh", ExpiresAt = DateTimeOffset.UtcNow.AddHours(1) }; };
        var tokens = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => rig.Service.ReadForAccountAsync(PlaygroundProviderKind.ChatGPT, Endpoint, info.ClientId, default)));
        Assert.All(tokens, token => Assert.Equal("rotated-access", token)); Assert.Equal(1, rig.OAuth.Refreshes);
        Assert.Equal("rotated-refresh", rig.Store.State.Accounts[0].RefreshToken);
    }
    [Theory] [InlineData(true)] [InlineData(false)]
    public async Task TerminalRefreshClearsOnlyUnusableSessionWhileNetworkFailurePreservesIt(bool terminal)
    {
        var rig = new Rig(); var first = await rig.Service.SignInAsync(null, false); var other = await rig.Service.SignInAsync(null, false);
        var old = rig.Store.State!.Accounts[0]; rig.Store.State = rig.Store.State with { Accounts = rig.Store.State.Accounts.Replace(old, old with { ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(-1) }) };
        rig.OAuth.OnRefresh = _ => Task.FromException<ChatGptRegistration>(new ChatGptAuthException(terminal ? ChatGptAuthFailure.NeedsSignIn : ChatGptAuthFailure.Network, "synthetic"));
        await Assert.ThrowsAsync<ChatGptAuthException>(() => rig.Service.ReadForAccountAsync(PlaygroundProviderKind.ChatGPT, Endpoint, first.ClientId, default));
        Assert.Equal(!terminal, rig.Store.State.Accounts[0].Connected); Assert.True(rig.Store.State.Accounts[1].Connected);
        Assert.Equal(other.ClientId, rig.Store.State.SelectedClientId);
    }
    [Fact] public async Task SignOutClearsAllTokensEvenWhenRemoteRevocationCannotBeConfirmed()
    {
        var rig = new Rig(); var info = await rig.Service.SignInAsync(null, false); string host = rig.Store.State!.HostId;
        rig.OAuth.Revoked = false; Assert.False(await rig.Service.SignOutAsync(info.ClientId));
        var stored = Assert.Single(rig.Store.State.Accounts); Assert.Null(stored.AccessToken); Assert.Null(stored.RefreshToken); Assert.Null(stored.IdToken);
        Assert.Equal(host, rig.Store.State.HostId); Assert.Equal(info.ClientId, stored.ClientId); Assert.Empty(stored.Scopes);
        await rig.Service.SignInAsync(info.ClientId, false);
        Assert.Equal(info.ClientId, rig.Browser.Requests.Last().ClientId); Assert.Null(rig.Browser.Requests.Last().IdTokenHint);
    }
    [Fact] public async Task LateLoginCannotResurrectSignedOutAccount()
    {
        var rig = new Rig(); var info = await rig.Service.SignInAsync(null, false);
        var pending = new TaskCompletionSource<ChatGptAuthorizationCode>(TaskCreationOptions.RunContinuationsAsynchronously);
        rig.Browser.OnAuthorize = (_, _) => pending.Task;
        var login = rig.Service.SignInAsync(info.ClientId, false);
        await rig.Service.SignOutAsync(info.ClientId);
        pending.SetResult(new("late", info.ClientId, new('a', 43), "nonce", "http://127.0.0.1:12345/auth/callback"));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => login);
        Assert.False(rig.Store.State!.Accounts[0].Connected); Assert.Equal(1, rig.OAuth.Exchanges);
    }
    [Fact] public async Task IdentityOnlyGrantCannotSupplyInferenceCredential()
    {
        var rig = new Rig(); rig.OAuth.OnExchange = code => Task.FromResult(Account(code.ClientId) with { Scopes = ["openid", "profile"] });
        var info = await rig.Service.SignInAsync(null, false); Assert.False(info.PlanEnabled);
        Assert.False(await rig.Service.HasForAccountAsync(PlaygroundProviderKind.ChatGPT, Endpoint, info.ClientId, default));
        await Assert.ThrowsAsync<ChatGptAuthException>(() => rig.Service.ReadForAccountAsync(PlaygroundProviderKind.ChatGPT, Endpoint, info.ClientId, default));
        Assert.Equal(0, rig.OAuth.Refreshes);
    }
    [Fact] public async Task LateRefreshIsPersistedButCannotSupplyCredentialAfterSignOutIntent()
    {
        var rig = new Rig(); var info = await rig.Service.SignInAsync(null, false);
        var old = rig.Store.State!.Accounts[0]; rig.Store.State = rig.Store.State with { Accounts = [old with { ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(-1) }] };
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var released = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        rig.OAuth.OnRefresh = async account => { started.SetResult(); await released.Task;
            return account with { AccessToken = "late-access", RefreshToken = "late-refresh", ExpiresAt = DateTimeOffset.UtcNow.AddHours(1) }; };
        var read = rig.Service.ReadForAccountAsync(PlaygroundProviderKind.ChatGPT, Endpoint, info.ClientId, default);
        await started.Task; var signOut = rig.Service.SignOutAsync(info.ClientId); released.SetResult();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => read); await signOut;
        Assert.False(rig.Store.State.Accounts[0].Connected); Assert.Null(rig.Store.State.Accounts[0].RefreshToken);
    }
    [Fact] public async Task CancellingTurnDuringRefreshKeepsRotatedTokensWithoutSendingIt()
    {
        var rig = new Rig();
        var info = await rig.Service.SignInAsync(null, false);
        var old = rig.Store.State!.Accounts[0];
        rig.Store.State = rig.Store.State with { Accounts = [old with { ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(-1) }] };
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var released = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        rig.OAuth.OnRefresh = async account =>
        {
            started.SetResult();
            await released.Task;
            return account with { AccessToken = "new-access", RefreshToken = "new-refresh", ExpiresAt = DateTimeOffset.UtcNow.AddHours(1) };
        };
        using var cancellation = new CancellationTokenSource();
        var waiting = rig.Service.ReadForAccountAsync(PlaygroundProviderKind.ChatGPT, Endpoint, info.ClientId, cancellation.Token);
        await started.Task;
        cancellation.Cancel();
        released.SetResult();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
        Assert.Equal("new-refresh", rig.Store.State.Accounts[0].RefreshToken);
        Assert.Equal("new-access", await rig.Service.ReadForAccountAsync(PlaygroundProviderKind.ChatGPT, Endpoint, info.ClientId, default));
        Assert.Equal(1, rig.OAuth.Refreshes);
    }
    [Fact] public void ApiConversationSerializationDoesNotAddOAuthFieldsToLegacyRecords()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(new PlaygroundConversationOptions());
        Assert.DoesNotContain("ChatGptAccountId", json);
        var connection = System.Text.Json.JsonSerializer.Serialize(new PlaygroundConnection(PlaygroundProviderKind.OpenAI, Endpoint));
        Assert.DoesNotContain("ChatGptAccountId", connection);
    }
    [Fact] public async Task OAuthStoreIsEncryptedRevisionAndProfileBoundAndNotPartOfChatState()
    {
        string root = Path.Combine(Path.GetTempPath(), "chatgpt-store-" + Guid.NewGuid().ToString("N"));
        string otherRoot = root + "-other";
        try
        {
            using var profile = NativeProfileStore.Open(new(NativeProfileKind.Test, root), new NativeLibraryCodec());
            var store = new WindowsChatGptAccountStore(profile.Configuration());
            Assert.Null(await store.ReadAsync(default)); Assert.False(Directory.Exists(Path.Combine(root, "playground")));
            var state = new ChatGptAccountState(1, 1, store.Identity, "urn:uuid:" + Guid.NewGuid().ToString("D"), "oaiapp_1", [Account("oaiapp_1")]);
            await store.SaveAsync(state, 0, default);
            var loaded = (await new WindowsChatGptAccountStore(profile.Configuration()).ReadAsync(default))!;
            Assert.Equal(state.HostId, loaded.HostId); Assert.Equal("refresh-oaiapp_1", loaded.Accounts[0].RefreshToken);
            var path = Path.Combine(root, "playground", "chatgpt-accounts-v1.dpapi"); var bytes = await File.ReadAllBytesAsync(path);
            Assert.DoesNotContain("refresh-oaiapp", Encoding.UTF8.GetString(bytes)); Assert.DoesNotContain("same@example", Encoding.UTF8.GetString(bytes));
            await Assert.ThrowsAsync<InvalidDataException>(() => store.SaveAsync(state with { Revision = 2 }, 0, default));
            Assert.Equal(1, (await store.ReadAsync(default))!.Revision);
            using var other = NativeProfileStore.Open(new(NativeProfileKind.Test, otherRoot), new NativeLibraryCodec());
            Directory.CreateDirectory(Path.Combine(otherRoot, "playground")); File.Copy(path, Path.Combine(otherRoot, "playground", Path.GetFileName(path)));
            await Assert.ThrowsAsync<InvalidDataException>(() => new WindowsChatGptAccountStore(other.Configuration()).ReadAsync(default));
            Assert.Null(await new WindowsPlaygroundStore(profile.Configuration()).LoadAsync(default));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); if (Directory.Exists(otherRoot)) Directory.Delete(otherRoot, true); }
    }
    private sealed class Rig
    {
        public Store Store = new(); public Browser Browser = new(); public OAuth OAuth = new(); public ChatGptAccountsService Service;
        public Rig() { Service = new(Store, Browser, OAuth, new ApiKeys()); }
    }
    private sealed class Store : IChatGptAccountStore
    {
        public PlaygroundIdentity Identity { get; } = new(Guid.NewGuid(), Guid.NewGuid()); public ChatGptAccountState? State;
        public Task<ChatGptAccountState?> ReadAsync(CancellationToken cancellation) => Task.FromResult(State);
        public Task SaveAsync(ChatGptAccountState next, long expectedRevision, CancellationToken cancellation)
        { Assert.Equal(expectedRevision, State?.Revision ?? 0); Assert.Equal(expectedRevision + 1, next.Revision); State = next; return Task.CompletedTask; }
    }
    private sealed class Browser : IChatGptBrowserSignIn
    {
        public List<ChatGptSignInRequest> Requests = [];
        public Func<ChatGptSignInRequest, CancellationToken, Task<ChatGptAuthorizationCode>>? OnAuthorize;
        public Task<ChatGptAuthorizationCode> AuthorizeAsync(ChatGptSignInRequest request, CancellationToken cancellation)
        { Requests.Add(request); return OnAuthorize?.Invoke(request, cancellation) ?? Task.FromResult(new ChatGptAuthorizationCode("code", request.ClientId ?? "oaiapp_" + Requests.Count,
            new('a', 43), "nonce", "http://127.0.0.1:12345/auth/callback")); }
    }
    private sealed class OAuth : IChatGptOAuthClient
    {
        public int Exchanges, Refreshes; public bool Revoked = true;
        public Func<ChatGptAuthorizationCode, Task<ChatGptRegistration>>? OnExchange;
        public Func<ChatGptRegistration, Task<ChatGptRegistration>>? OnRefresh;
        public Task<ChatGptRegistration> ExchangeAsync(ChatGptAuthorizationCode code, CancellationToken cancellation)
        { Exchanges++; return OnExchange?.Invoke(code) ?? Task.FromResult(Account(code.ClientId)); }
        public async Task<ChatGptRegistration> RefreshAsync(ChatGptRegistration account, CancellationToken cancellation)
        {
            Refreshes++;
            var refreshed = OnRefresh?.Invoke(account) ?? Task.FromResult(account with { ExpiresAt = DateTimeOffset.UtcNow.AddHours(1) });
            return await refreshed.WaitAsync(cancellation);
        }
        public Task<bool> RevokeAsync(ChatGptRegistration account, CancellationToken cancellation) => Task.FromResult(Revoked);
    }
    private sealed class ApiKeys : IPlaygroundCredentials
    {
        public Task<bool> HasAsync(PlaygroundProviderKind provider, string endpoint, CancellationToken cancellation) => Task.FromResult(false);
        public Task<string?> ReadAsync(PlaygroundProviderKind provider, string endpoint, CancellationToken cancellation) => throw new InvalidOperationException("ChatGPT cannot use API key fallback");
        public Task SaveAsync(PlaygroundProviderKind provider, string endpoint, string key, CancellationToken cancellation) => throw new NotSupportedException();
        public Task ClearAsync(PlaygroundProviderKind provider, string endpoint, CancellationToken cancellation) => throw new NotSupportedException();
    }
}
