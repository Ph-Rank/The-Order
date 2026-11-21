using System.Net;
using System.Text;
using TheOrder.Application;
using TheOrder.Infrastructure.Online;
using Xunit;
using static TheOrder.Tests.OnlineTransportTests;

namespace TheOrder.Tests;

public sealed class NextMoeAuthTests
{
    internal sealed class Clock : TimeProvider { public DateTimeOffset Now = DateTimeOffset.Parse("2026-10-11T10:00:00Z"); public override DateTimeOffset GetUtcNow() => Now; }
    internal sealed class Store : INextMoeAuthStore
    {
        public NextMoeAuthRecord? Record;
        public int Writes, FailAt;
        public Task<NextMoeAuthRecord?> LoadAsync(CancellationToken c) { c.ThrowIfCancellationRequested(); return Task.FromResult(Record); }
        public Task SaveAsync(NextMoeAuthRecord value, CancellationToken c)
        { c.ThrowIfCancellationRequested(); if (++Writes == FailAt) throw new OnlineException(OnlineFailure.CredentialStorage); Record = value with { Scopes = [.. value.Scopes] }; return Task.CompletedTask; }
    }
    internal sealed class Login : IOAuthLogin
    {
        public string[] Scopes = [];
        public Task<OAuthAuthorizationCode> AuthorizeAsync(string id, string[] scopes, CancellationToken c)
        { c.ThrowIfCancellationRequested(); Scopes = [.. scopes]; return Task.FromResult(new OAuthAuthorizationCode("synthetic-code", "synthetic-verifier", new("http://127.0.0.1:12345/callback"))); }
    }
    internal sealed class OAuth(Clock clock, Login login) : INextMoeOAuth
    {
        public int Refreshes;
        public string Account = "42";
        public bool Fail;
        public string[]? GrantedScopes;
        public Func<CancellationToken, Task>? Pause;
        public OAuthTokens Tokens(int n) => new("synthetic-access-" + n, "synthetic-refresh-" + n, clock.Now.AddMinutes(15),
            GrantedScopes ?? (login.Scopes.Length == 0 ? [.. NextMoeOAuthIdentity.CatalogScopes] : [.. login.Scopes]), new(Account, "合成用户"));
        public Task<OAuthTokens> ExchangeAsync(string id, OAuthAuthorizationCode code, CancellationToken c) => Task.FromResult(Tokens(0));
        public async Task<OAuthTokens> RefreshAsync(string id, string refresh, CancellationToken c)
        { int n = Interlocked.Increment(ref Refreshes); if (Pause is { } pause) await pause(c); if (Fail) throw new OnlineException(OnlineFailure.LoginRequired); return Tokens(n); }
    }
    internal sealed class Fixture
    {
        public readonly Credential Keys = new(null); public readonly Store Storage = new(); public readonly Clock Time = new(); public readonly Login Browser = new();
        public readonly OAuth Protocol; public readonly NextMoeAuthentication Auth;
        public Fixture() { Protocol = new(Time, Browser); Auth = new(Keys, Keys, Storage, Protocol, Browser, clock: Time); }
        public Task SignIn() => Auth.SignInAsync(default);
    }
    [Fact] public async Task NewUserAnonymousAndLegacyKeyRetained()
    {
        var anonymous = new Fixture(); Assert.Null(await anonymous.Auth.ReadAsync(default));
        Assert.Equal(NextMoeAccessMode.Anonymous, (await anonymous.Auth.StatusAsync(default)).Mode);
        var old = new Fixture(); old.Keys.Value = "synthetic-old-key";
        Assert.Equal("synthetic-old-key", await old.Auth.ReadAsync(default)); Assert.Equal(NextMoeAccessMode.ApiKey, old.Storage.Record!.Mode);
        await old.SignIn(); Assert.Equal("synthetic-old-key", old.Keys.Value); Assert.Equal(NextMoeOAuthIdentity.LoginScopes, old.Browser.Scopes);
        await old.Auth.SelectAsync(NextMoeAccessMode.Anonymous, default); Assert.Null(await old.Auth.ReadAsync(default)); Assert.Equal("synthetic-old-key", old.Keys.Value);
        await old.Auth.SelectAsync(NextMoeAccessMode.ApiKey, default); Assert.Equal("synthetic-old-key", await old.Auth.ReadAsync(default));
    }
    [Fact] public async Task AnonymousRequestHasNoAuthorizationAndNeverUsesPrivateIdentity()
    {
        var f = new Fixture(); bool sent = false;
        using var client = new HttpClient(new Handler((request, _) => { Assert.Null(request.Headers.Authorization); Assert.Equal("api.nextmoe.dev", request.RequestUri!.Host); sent = true; return Task.FromResult(Json("{\"object\":\"list\",\"items\":[]}")); }));
        var transport = new CatalogTransport(client, f.Auth, new("https://api.nextmoe.dev/v2/"));
        await transport.Operation(async budget => { using var result = await transport.GetAsync("catalog/works", new Dictionary<string, string>(), budget); return true; }, default);
        Assert.True(sent); Assert.Equal(OnlineFailure.UserIdentityRequired, (await Assert.ThrowsAsync<OnlineException>(() => f.Auth.UserTokenAsync(null, default))).Failure);
        await Assert.ThrowsAsync<OnlineException>(() => transport.Operation(async budget => { using var result = await transport.GetAsync("me/playtimes", new Dictionary<string, string>(), budget); return true; }, default));
    }
    [Fact] public async Task ConcurrentRefreshIsSingleRotationAndRestartUsesNewToken()
    {
        var f = new Fixture(); await f.SignIn(); f.Time.Now = f.Time.Now.AddMinutes(20);
        var values = await Task.WhenAll(Enumerable.Range(0, 25).Select(_ => f.Auth.ReadAsync(default)));
        Assert.All(values, value => Assert.Equal("synthetic-access-1", value)); Assert.Equal(1, f.Protocol.Refreshes); Assert.False(f.Storage.Record!.RefreshInProgress);
        Assert.Equal("synthetic-refresh-1", f.Storage.Record.RefreshToken);
        var restarted = new NextMoeAuthentication(f.Keys, f.Keys, f.Storage, f.Protocol, f.Browser, clock: f.Time);
        Assert.Equal("synthetic-access-2", await restarted.ReadAsync(default)); Assert.Equal(2, f.Protocol.Refreshes);
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task LostRotationOrPersistenceFailureRequiresExplicitLogin(bool failPersistence)
    {
        var f = new Fixture(); await f.SignIn(); f.Time.Now = f.Time.Now.AddMinutes(20);
        if (failPersistence) f.Storage.FailAt = f.Storage.Writes + 2; else f.Protocol.Fail = true;
        await Assert.ThrowsAsync<OnlineException>(() => f.Auth.ReadAsync(default)); Assert.True(f.Storage.Record!.RefreshInProgress);
        Assert.True((await f.Auth.StatusAsync(default)).NeedsLogin); int count = f.Protocol.Refreshes;
        await Assert.ThrowsAsync<OnlineException>(() => f.Auth.ReadAsync(default)); Assert.Equal(count, f.Protocol.Refreshes);
        var restarted = new NextMoeAuthentication(f.Keys, f.Keys, f.Storage, f.Protocol, f.Browser, clock: f.Time);
        Assert.Equal(OnlineFailure.LoginRequired, (await Assert.ThrowsAsync<OnlineException>(() => restarted.ReadAsync(default))).Failure);
        Assert.Equal(NextMoeAccessMode.Account, (await restarted.StatusAsync(default)).Mode);
    }
    [Fact] public async Task RestoredRefreshInProgressDoesNotRetryExpiredRefreshToken()
    {
        var f = new Fixture(); f.Storage.Record = new(1, NextMoeAccessMode.Account, new("42", "合成用户"), "synthetic-refresh", [.. NextMoeOAuthIdentity.CatalogScopes], true);
        await Assert.ThrowsAsync<OnlineException>(() => f.Auth.ReadAsync(default)); Assert.Equal(0, f.Protocol.Refreshes);
    }
    [Fact] public async Task LogoutClearsOnlyLocalAccountAndKeepsApiKey()
    {
        var f = new Fixture(); f.Keys.Value = "synthetic-key"; await f.SignIn(); await f.Auth.SignOutAsync(default);
        Assert.Null(f.Storage.Record!.RefreshToken); Assert.Null(f.Storage.Record.Account); Assert.Equal(NextMoeAccessMode.Anonymous, f.Storage.Record.Mode);
        Assert.Equal("synthetic-key", f.Keys.Value); Assert.Null(await f.Auth.ReadAsync(default));
    }
    [Fact] public async Task RemovingKeyReturnsToAnonymousWithoutClearingDormantAccount()
    {
        var f = new Fixture(); await f.SignIn(); await f.Auth.SaveAsync("synthetic-key", default); await f.Auth.ClearAsync(default);
        Assert.Equal(NextMoeAccessMode.Anonymous, f.Storage.Record!.Mode); Assert.NotNull(f.Storage.Record.Account); Assert.Null(f.Keys.Value);
    }
    [Fact] public async Task FirstLoginRequestsAllSupportedScopesTogether()
    {
        var f = new Fixture();
        await f.SignIn();
        Assert.Equal(new[] { "openid", "profile", "catalog:read", "folder:read", "folder:write" }, f.Browser.Scopes);
        var status = await f.Auth.StatusAsync(default);
        Assert.True(status.CanReadFolders);
        Assert.True(status.CanWriteFolders);
        Assert.Equal("synthetic-access-0", await f.Auth.UserTokenAsync("folder:write", default));
    }

    [Fact] public async Task MissingFolderGrantIsNotInferredFromRequestedScopes()
    {
        var f = new Fixture();
        var previous = new NextMoeAuthRecord(1, NextMoeAccessMode.Account, new("42", "合成用户"), "synthetic-old-refresh",
            [.. NextMoeOAuthIdentity.CatalogScopes]);
        f.Storage.Record = previous;
        f.Protocol.GrantedScopes = [.. NextMoeOAuthIdentity.CatalogScopes];
        Assert.Equal(OnlineFailure.ScopeRequired, (await Assert.ThrowsAsync<OnlineException>(f.SignIn)).Failure);
        var status = await f.Auth.StatusAsync(default);
        Assert.False(status.CanReadFolders);
        Assert.False(status.CanWriteFolders);
        Assert.Equal(previous, f.Storage.Record);
        Assert.Equal(OnlineFailure.ScopeRequired, (await Assert.ThrowsAsync<OnlineException>(
            () => f.Auth.UserTokenAsync("folder:read", default))).Failure);
    }
    [Fact] public async Task ApiKeyDoesNotRepresentUserAndFailedRefreshNeverChangesIdentity()
    {
        var f = new Fixture(); await f.Auth.SaveAsync("synthetic-key", default);
        await Assert.ThrowsAsync<OnlineException>(() => f.Auth.UserTokenAsync(null, default));
        await f.SignIn(); f.Protocol.Account = "99"; f.Time.Now = f.Time.Now.AddMinutes(20);
        await Assert.ThrowsAsync<OnlineException>(() => f.Auth.ReadAsync(default)); Assert.Equal("42", (await f.Auth.StatusAsync(default)).Account!.Id);
    }
    [Fact] public async Task IdentityChangeRejectsLateCatalogAcceptance()
    {
        var f = new Fixture(); var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var client = new HttpClient(new Handler(async (_, _) => { started.SetResult(); await release.Task; return Json("{\"object\":\"list\",\"items\":[]}"); }));
        var transport = new CatalogTransport(client, f.Auth, new("https://api.nextmoe.dev/v2/")); bool accepted = false;
        var old = transport.Operation(async budget => { using var body = await transport.GetAsync("catalog/works", new Dictionary<string, string>(), budget); accepted = true; return true; }, default);
        await started.Task; await f.Auth.SaveAsync("synthetic-new-key", default); release.SetResult();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => old); Assert.False(accepted);
    }
    [Fact] public async Task AuthStoreFailureDoesNotQuietlyBecomeAnonymous()
    {
        var f = new Fixture(); f.Storage.FailAt = 1;
        Assert.Equal(OnlineFailure.CredentialStorage, (await Assert.ThrowsAsync<OnlineException>(() => f.Auth.ReadAsync(default))).Failure);
        Assert.Null(f.Storage.Record);
    }
    [Fact] public async Task RefreshCancellationLeavesRecoverableFailClosedMarker()
    {
        var f = new Fixture(); await f.SignIn(); f.Time.Now = f.Time.Now.AddMinutes(20);
        using var stop = new CancellationTokenSource(); f.Protocol.Pause = async c => { stop.Cancel(); await Task.Delay(1, c); };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Auth.ReadAsync(stop.Token));
        Assert.True(f.Storage.Record!.RefreshInProgress); Assert.True((await f.Auth.StatusAsync(default)).NeedsLogin);
    }
}
