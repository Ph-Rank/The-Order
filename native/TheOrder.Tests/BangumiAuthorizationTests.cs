using System.Collections.Immutable;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using TheOrder.Application;
using TheOrder.Infrastructure.Legacy;
using TheOrder.Infrastructure.Online;
using TheOrder.Platform.Windows;
using TheOrder.Presentation;
using Xunit;

namespace TheOrder.Tests;

public sealed class BangumiAuthorizationTests
{
    private const string TokenA = "Synthetic_Bangumi_Token_A_123456789";
    private const string TokenB = "Synthetic_Bangumi_Token_B_987654321";
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-13T10:00:00Z");
    private sealed class Store : IBangumiCredentialStore
    {
        public string? Value;
        public int Saves;
        public bool FailSave;
        public Action? Committed;
        public Task<string?> ReadAsync(CancellationToken token) { token.ThrowIfCancellationRequested(); return Task.FromResult(Value); }
        public Task SaveAsync(string value, CancellationToken token)
        { token.ThrowIfCancellationRequested(); if (FailSave) throw new BangumiAuthorizationException(BangumiAuthorizationFailure.Storage); Value = value; Saves++; Committed?.Invoke(); return Task.CompletedTask; }
        public Task ClearAsync(CancellationToken token) { token.ThrowIfCancellationRequested(); Value = null; Committed?.Invoke(); return Task.CompletedTask; }
    }
    private sealed class AccountProvider : IBangumiAccountProvider
    {
        public Func<string, CancellationToken, Task<BangumiAccount>> Action = (_, _) => Task.FromResult(new BangumiAccount("123", "synthetic", "合成账号"));
        public Task<BangumiAccount> ValidateAsync(string token, CancellationToken cancellation) => Action(token, cancellation);
    }
    private static (BangumiAuthorization Auth, Store Store, AccountProvider Accounts, BangumiAccessContext Access) Fixture(string? saved = null)
    { var store = new Store { Value = saved }; var provider = new AccountProvider(); var access = new BangumiAccessContext(); return (new(store, provider, access), store, provider, access); }
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> action) : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellation)
        { Calls++; return action(request, cancellation); }
    }
    private static HttpResponseMessage Response(string body, int status = 200) => new((HttpStatusCode)status)
    { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    private sealed class Time
    {
        public DateTimeOffset Value = Now;
        public Task Delay(TimeSpan duration, CancellationToken cancellation)
        { cancellation.ThrowIfCancellationRequested(); Value += duration; return Task.CompletedTask; }
    }
    private static string Subject(bool nsfw = true, int kind = 4) => $$"""{"id":13,"type":{{kind}},"nsfw":{{nsfw.ToString().ToLowerInvariant()}},"name":"合成作品","name_cn":"","summary":"合成简介","date":"2004-08-13","infobox":[]}""";
    private const string Relations = """[{"id":51,"type":2,"name":"Synthetic Anime","name_cn":"合成动画","relation":"动画"}]""";

    [Theory]
    [InlineData("")][InlineData("short")][InlineData("ABC\r\nAuthorization: x")][InlineData("中文令牌12345678")]
    [InlineData("Bearer synthetic-token")][InlineData("https://next.bgm.tv/example")][InlineData("abcdefgh ijklmnop")]
    public void InvalidInputCannotBecomeAHeader(string input)
    { Assert.Equal(BangumiAuthorizationFailure.InvalidToken, Assert.Throws<BangumiAuthorizationException>(() => BangumiAuthorization.NormalizeToken(input)).Failure); }
    [Fact] public async Task TrimIsAllowedButTheSecretNeverAppearsInSnapshotStringsOrJson()
    {
        Assert.Equal(TokenA, BangumiAuthorization.NormalizeToken("  " + TokenA + "\n"));
        var fixture = Fixture(TokenA); await fixture.Auth.InitializeAsync(CancellationToken.None);
        var snapshot = fixture.Access.Capture(); Assert.True(snapshot.Authorized);
        Assert.DoesNotContain(TokenA, snapshot.ToString()); Assert.DoesNotContain(TokenA, JsonSerializer.Serialize(snapshot));
        Assert.True(BangumiAccessContext.ValidScope(snapshot.Scope));
    }
    [Fact] public async Task SaveValidatesBeforeAtomicCommitAndPreservesPriorAuthorizationOnFailure()
    {
        var f = Fixture(TokenA); await f.Auth.InitializeAsync(CancellationToken.None); var old = f.Access.Capture();
        f.Accounts.Action = (_, _) => throw new BangumiAuthorizationException(BangumiAuthorizationFailure.Unauthorized);
        await Assert.ThrowsAsync<BangumiAuthorizationException>(() => f.Auth.SaveAsync(TokenB, CancellationToken.None));
        Assert.Equal(TokenA, f.Store.Value); Assert.Equal(0, f.Store.Saves); Assert.Same(old, f.Access.Capture());
        f.Accounts.Action = (_, _) => Task.FromResult(new BangumiAccount("1", "fixture", "Fixture")); f.Store.FailSave = true;
        await Assert.ThrowsAsync<BangumiAuthorizationException>(() => f.Auth.SaveAsync(TokenB, CancellationToken.None));
        Assert.Same(old, f.Access.Capture()); Assert.Equal(TokenA, f.Store.Value);
    }
    [Fact] public async Task AuthorizationChangesCancelOldRequestsAndScopesDifferAcrossAccounts()
    {
        var f = Fixture(TokenA); await f.Auth.InitializeAsync(CancellationToken.None); var a = f.Access.Capture();
        await f.Auth.SaveAsync(TokenB, CancellationToken.None); var b = f.Access.Capture();
        Assert.True(a.Changed.IsCancellationRequested); Assert.NotEqual(a.Scope, b.Scope);
        await f.Auth.SaveAsync(TokenA, CancellationToken.None); var secondA = f.Access.Capture();
        Assert.True(b.Changed.IsCancellationRequested); Assert.Equal(a.Scope, secondA.Scope); Assert.False(f.Access.IsCurrent(a));
        await f.Auth.ClearAsync(CancellationToken.None); Assert.True(secondA.Changed.IsCancellationRequested);
        Assert.Equal("anonymous", f.Access.Capture().Scope); Assert.Null(f.Store.Value);
    }
    [Fact] public async Task LateCancellationAfterCommitStillPublishesTheCommittedCredential()
    {
        var f = Fixture(TokenA); await f.Auth.InitializeAsync(CancellationToken.None);
        using var cancel = new CancellationTokenSource(); f.Store.Committed = cancel.Cancel;
        await f.Auth.SaveAsync(TokenB, cancel.Token);
        Assert.Equal(TokenB, f.Store.Value); Assert.True(f.Access.Capture().Authorized);
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.bgm.tv/v0/subjects/13"); f.Access.Capture().Authorize(request);
        Assert.Equal(TokenB, request.Headers.Authorization!.Parameter);
    }
    [Theory]
    [InlineData("https://musicbrainz.org/ws/2/release/13")][InlineData("https://lain.bgm.tv/pic/cover/x.jpg")]
    [InlineData("https://api.nextmoe.dev/v2/works/13")][InlineData("https://api.bgm.tv.evil.test/v0/subjects/13")]
    [InlineData("http://api.bgm.tv/v0/subjects/13")][InlineData("https://api.bgm.tv/v0/subjects/13?token=x")]
    [InlineData("https://api.bgm.tv/v0/subjects/13/image")]
    public async Task CredentialCannotBeAttachedToAnUnexpectedDestination(string url)
    {
        var f = Fixture(TokenA); await f.Auth.InitializeAsync(CancellationToken.None);
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        Assert.Throws<BangumiAuthorizationException>(() => f.Access.Capture().Authorize(request)); Assert.Null(request.Headers.Authorization);
    }
    [Fact] public async Task AccountVerificationUsesOnlyTheOfficialEndpointAndSanitizesUnauthorizedErrors()
    {
        var handler = new Handler((request, _) =>
        {
            Assert.Equal(HttpMethod.Get, request.Method); Assert.Equal("https://api.bgm.tv/v0/me", request.RequestUri!.AbsoluteUri);
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme); Assert.Equal(TokenA, request.Headers.Authorization?.Parameter);
            Assert.Contains("TheOrder/1.1.0", request.Headers.UserAgent.ToString()); Assert.Contains("Ph-Rank", request.Headers.UserAgent.ToString()); Assert.False(request.Headers.Contains("Cookie"));
            return Task.FromResult(Response("""{"id":123,"username":"synthetic","nickname":"合成账号"}"""));
        });
        var provider = new BangumiAccountProvider(new HttpClient(handler)); var account = await provider.ValidateAsync(TokenA, CancellationToken.None);
        Assert.Equal("123", account.Id); Assert.Equal("合成账号", account.Name);
        var bad = new BangumiAccountProvider(new HttpClient(new Handler((_, _) => Task.FromResult(Response(TokenA, 401)))));
        var error = await Assert.ThrowsAsync<BangumiAuthorizationException>(() => bad.ValidateAsync(TokenA, CancellationToken.None));
        Assert.Equal(BangumiAuthorizationFailure.Unauthorized, error.Failure); Assert.DoesNotContain(TokenA, error.ToString());
    }
    [Theory]
    [InlineData("[]")][InlineData("{}")][InlineData("{\"id\":\"123\",\"username\":\"x\"}")]
    [InlineData("{\"id\":1,\"username\":\"\"}")][InlineData("{\"id\":0,\"username\":\"fixture\"}")]
    public async Task MalformedAccountResponsesAreRejected(string body)
    {
        var provider = new BangumiAccountProvider(new HttpClient(new Handler((_, _) => Task.FromResult(Response(body)))));
        Assert.Equal(BangumiAuthorizationFailure.InvalidResponse, (await Assert.ThrowsAsync<BangumiAuthorizationException>(() => provider.ValidateAsync(TokenA, CancellationToken.None))).Failure);
    }
    [Theory]
    [InlineData(302, BangumiAuthorizationFailure.InvalidResponse)]
    [InlineData(403, BangumiAuthorizationFailure.Forbidden)]
    [InlineData(429, BangumiAuthorizationFailure.RateLimited)]
    [InlineData(503, BangumiAuthorizationFailure.Server)]
    public async Task AccountStatusErrorsRemainDistinct(int status, BangumiAuthorizationFailure expected)
    {
        var provider = new BangumiAccountProvider(new HttpClient(new Handler((_, _) => Task.FromResult(Response("{}", status)))));
        Assert.Equal(expected, (await Assert.ThrowsAsync<BangumiAuthorizationException>(() => provider.ValidateAsync(TokenA, CancellationToken.None))).Failure);
    }
    [Fact] public async Task AccountCancellationIsNotReportedAsTimeoutAndNetworkErrorsNeverEchoTokens()
    {
        var provider = new BangumiAccountProvider(new HttpClient(new Handler(async (_, token) => { await Task.Delay(Timeout.Infinite, token); return Response("{}"); })), TimeSpan.FromMilliseconds(20));
        Assert.Equal(BangumiAuthorizationFailure.Timeout, (await Assert.ThrowsAsync<BangumiAuthorizationException>(() => provider.ValidateAsync(TokenA, CancellationToken.None))).Failure);
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.ValidateAsync(TokenA, cancel.Token));
        var offline = new BangumiAccountProvider(new HttpClient(new Handler((_, _) => throw new HttpRequestException(TokenA))));
        var error = await Assert.ThrowsAsync<BangumiAuthorizationException>(() => offline.ValidateAsync(TokenA, CancellationToken.None));
        Assert.Equal(BangumiAuthorizationFailure.Offline, error.Failure); Assert.DoesNotContain(TokenA, error.ToString());
    }
    [Fact] public async Task AuthorizedRelationsAllowRestrictedSourceWithoutSharingDefaultHeaders()
    {
        var f = Fixture(TokenA); await f.Auth.InitializeAsync(CancellationToken.None); var time = new Time();
        var handler = new Handler((request, _) =>
        {
            Assert.Equal(TokenA, request.Headers.Authorization?.Parameter);
            return Task.FromResult(Response(request.RequestUri!.AbsolutePath.EndsWith("/subjects") ? Relations : Subject()));
        });
        using var client = new HttpClient(handler); var provider = new BangumiRelationsProvider(client, () => time.Value, time.Delay, access: f.Access);
        var snapshot = await provider.FetchAsync("13", CancellationToken.None); Assert.Single(snapshot.Items);
        Assert.Null(client.DefaultRequestHeaders.Authorization); Assert.Equal(2, handler.Calls);
        var anon = new BangumiRelationsProvider(new HttpClient(new Handler((_, _) => Task.FromResult(Response(Subject())))));
        Assert.Equal(BangumiRelationsFailure.Unavailable, (await Assert.ThrowsAsync<BangumiRelationsException>(() => anon.FetchAsync("13", CancellationToken.None))).Failure);
    }
    [Fact] public async Task AuthorizedMusicDetailsKeepTheOriginalMediaIdentityAndFacts()
    {
        var f = Fixture(TokenA); await f.Auth.InitializeAsync(CancellationToken.None);
        var provider = new BangumiSubjectDetailsProvider(new HttpClient(new Handler((request, _) =>
        { Assert.Equal(TokenA, request.Headers.Authorization?.Parameter); return Task.FromResult(Response(Subject(kind: 3))); })), access: f.Access);
        var value = await provider.FetchAsync("13", BangumiRelationKind.Music, CancellationToken.None);
        Assert.Equal("13", value.Id); Assert.Equal(BangumiRelationKind.Music, value.Kind); Assert.NotNull(value.MusicFacts);
    }
    [Fact] public async Task ExpiredCredentialDisablesAuthorizedAccessAndDoesNotReadResponseSecrets()
    {
        var f = Fixture(TokenA); await f.Auth.InitializeAsync(CancellationToken.None);
        var provider = new BangumiRelationsProvider(new HttpClient(new Handler((_, _) => Task.FromResult(Response(TokenA, 401)))), access: f.Access);
        Assert.Equal(BangumiRelationsFailure.Unauthorized, (await Assert.ThrowsAsync<BangumiRelationsException>(() => provider.FetchAsync("13", CancellationToken.None))).Failure);
        Assert.True(f.Access.Rejected); Assert.Equal("anonymous", f.Access.Capture().Scope); Assert.Equal(TokenA, f.Store.Value);
    }
    [Fact] public async Task RevocationRejectsALateResponseEvenIfTheHandlerIgnoresCancellation()
    {
        var f = Fixture(TokenA); await f.Auth.InitializeAsync(CancellationToken.None);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = new BangumiSubjectDetailsProvider(new HttpClient(new Handler(async (_, _) =>
        { entered.SetResult(); await release.Task; return Response(Subject(kind: 3)); })), access: f.Access);
        var task = provider.FetchAsync("13", BangumiRelationKind.Music, CancellationToken.None);
        await entered.Task; await f.Auth.ClearAsync(CancellationToken.None); release.SetResult();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
    }
    private sealed class Cache : IBangumiRelationsCache
    {
        public readonly Dictionary<string, BangumiRelationsSnapshot> Values = [];
        public Task<BangumiRelationsSnapshot?> ReadAsync(BangumiRelationIdentity identity, CancellationToken token)
        { token.ThrowIfCancellationRequested(); return Task.FromResult(Values.GetValueOrDefault(identity.CacheKey)); }
        public Task WriteAsync(BangumiRelationIdentity identity, BangumiRelationsSnapshot value, CancellationToken token)
        { token.ThrowIfCancellationRequested(); Values[identity.CacheKey] = value; return Task.CompletedTask; }
    }
    private sealed class RelationsProvider : IBangumiRelationsProvider
    {
        public int Calls;
        public bool Denied;
        public Task<BangumiRelationsSnapshot> FetchAsync(string id, CancellationToken token)
        { token.ThrowIfCancellationRequested(); Calls++; if (Denied) throw new BangumiRelationsException(BangumiRelationsFailure.Unavailable, "not available"); return Task.FromResult(new BangumiRelationsSnapshot(id, Now, [new("51", BangumiRelationKind.Anime, "Test", "Test", "动画", null)])); }
    }
    [Fact] public async Task AnonymousAndAuthorizedRelationsHaveSeparateCacheEntriesAndDenialsDoNotFallback()
    {
        var f = Fixture(); var cache = new Cache(); var provider = new RelationsProvider();
        var service = new BangumiRelationsService(provider, cache, () => Now, f.Access);
        var identity = new BangumiRelationIdentity("fixture", "life", "86", "13", new('A', 64));
        await service.LoadAsync(identity, false, CancellationToken.None); Assert.Equal(1, provider.Calls);
        await f.Auth.SaveAsync(TokenA, CancellationToken.None); await service.LoadAsync(identity, false, CancellationToken.None);
        Assert.Equal(2, provider.Calls); Assert.Equal(2, cache.Values.Count);
        provider.Denied = true;
        await Assert.ThrowsAsync<BangumiRelationsException>(() => service.LoadAsync(identity, true, CancellationToken.None));
        await f.Auth.ClearAsync(CancellationToken.None);
        Assert.True((await service.LoadAsync(identity, false, CancellationToken.None)).FromCache); Assert.Equal(3, provider.Calls);
    }
    [Fact] public async Task SettingsValidationFailurePreservesAuthorizationAndCancelStopsValidation()
    {
        var f = Fixture(TokenA); using var model = new BangumiSettingsViewModel(f.Auth); await model.InitializeAsync();
        f.Accounts.Action = (_, _) => throw new BangumiAuthorizationException(BangumiAuthorizationFailure.Unauthorized);
        await model.SaveAsync(TokenB); Assert.True(model.HasAuthorization); Assert.Equal(TokenA, f.Store.Value); Assert.Contains("无效", model.Message);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Accounts.Action = async (_, token) => { entered.SetResult(); await Task.Delay(Timeout.Infinite, token); return new("1", "test", "Test"); };
        var task = model.SaveAsync(TokenB); await entered.Task; model.Cancel(); await task;
        Assert.False(model.Busy); Assert.Equal("已取消操作。", model.Message); Assert.Equal(TokenA, f.Store.Value);
        await model.ClearAsync(); Assert.False(model.HasAuthorization); Assert.Contains("匿名", model.Status);
    }
}

[Collection("Native profile resource")]
public sealed class BangumiAuthorizationStorageTests
{
    private const string Token = "Synthetic_Only_Authorization_Token_123456789";
    private sealed class Fixture : IDisposable
    {
        public string Root = Path.Combine(Path.GetTempPath(), "the-order-bangumi-auth-" + Guid.NewGuid().ToString("N"));
        public NativeProfileStore Profile;
        public Fixture() => Profile = NativeProfileStore.Open(new(NativeProfileKind.Test, Root), new NativeLibraryCodec());
        public string PathForToken => Path.Combine(Root, WindowsBangumiCredentialStore.FileName);
        public WindowsBangumiCredentialStore Store(Func<PersistenceStage, Task>? probe = null) => new(Profile.Configuration(), probe);
        public void Reopen() { Profile.Dispose(); Profile = NativeProfileStore.Open(new(NativeProfileKind.Test, Root), new NativeLibraryCodec()); }
        public void Dispose()
        {
            Profile.Dispose(); var root = Path.GetFullPath(Root); var temp = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()));
            if (!root.StartsWith(temp + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || !Path.GetFileName(root).StartsWith("the-order-bangumi-auth-", StringComparison.Ordinal)) throw new InvalidOperationException("Fixture escaped temp root");
            Directory.Delete(root, true);
        }
    }
    [Fact] public async Task TokenIsEncryptedBoundAndSurvivesRestartWithoutChangingTheShelf()
    {
        using var f = new Fixture(); var library = File.ReadAllBytes(Path.Combine(f.Root, "library.json"));
        await f.Store().SaveAsync(Token, CancellationToken.None);
        Assert.DoesNotContain(Token, Encoding.UTF8.GetString(File.ReadAllBytes(f.PathForToken)));
        f.Reopen(); Assert.Equal(Token, await f.Store().ReadAsync(CancellationToken.None));
        Assert.Equal(library, File.ReadAllBytes(Path.Combine(f.Root, "library.json")));
        await f.Store().ClearAsync(CancellationToken.None); Assert.Null(await f.Store().ReadAsync(CancellationToken.None));
        Assert.True(File.Exists(f.PathForToken));
    }
    [Fact] public async Task ForeignProfileAndCorruptFilesArePreservedRatherThanOverwritten()
    {
        using var first = new Fixture(); using var second = new Fixture();
        await first.Store().SaveAsync(Token, CancellationToken.None); var foreign = File.ReadAllBytes(first.PathForToken);
        File.WriteAllBytes(second.PathForToken, foreign);
        await Assert.ThrowsAsync<BangumiAuthorizationException>(() => second.Store().ReadAsync(CancellationToken.None));
        await Assert.ThrowsAsync<BangumiAuthorizationException>(() => second.Store().SaveAsync(Token, CancellationToken.None));
        await Assert.ThrowsAsync<BangumiAuthorizationException>(() => second.Store().ClearAsync(CancellationToken.None));
        Assert.Equal(foreign, File.ReadAllBytes(second.PathForToken));
        var corrupt = Encoding.UTF8.GetBytes("SYNTHETIC CORRUPT"); File.WriteAllBytes(second.PathForToken, corrupt);
        await Assert.ThrowsAsync<BangumiAuthorizationException>(() => second.Store().SaveAsync(Token, CancellationToken.None));
        Assert.Equal(corrupt, File.ReadAllBytes(second.PathForToken));
    }
    [Fact] public async Task CancellationBeforeReplacementRetainsTheOldCredential()
    {
        using var f = new Fixture(); await f.Store().SaveAsync(Token, CancellationToken.None); var bytes = File.ReadAllBytes(f.PathForToken);
        using var cancel = new CancellationTokenSource();
        var store = f.Store(stage => { if (stage == PersistenceStage.BeforeReplace) cancel.Cancel(); return Task.CompletedTask; });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.SaveAsync("Synthetic_Replacement_123456", cancel.Token));
        Assert.Equal(bytes, File.ReadAllBytes(f.PathForToken)); Assert.Equal(Token, await f.Store().ReadAsync(CancellationToken.None));
    }
    [Fact] public async Task RelationCacheKeepsScopesSeparateAndReadsLegacyAnonymousEnvelope()
    {
        using var f = new Fixture(); var cache = new IsolatedBangumiRelationsCache(f.Profile.Configuration());
        var anon = new BangumiRelationIdentity("fixture", "life", "86", "13", new('A', 64));
        var authorized = anon with { AccessScope = "authorized-" + new string('B', 64) };
        var time = DateTimeOffset.Parse("2026-10-13T10:00:00Z");
        var a = new BangumiRelationsSnapshot("13", time, [new("51", BangumiRelationKind.Anime, "Public", "Public", "动画", null)]);
        var b = a with { Items = [new("52", BangumiRelationKind.Anime, "Authorized", "Authorized", "动画", null)] };
        await cache.WriteAsync(anon, a, CancellationToken.None); await cache.WriteAsync(authorized, b, CancellationToken.None);
        Assert.Equal("Public", (await cache.ReadAsync(anon, CancellationToken.None))!.Items[0].Title);
        Assert.Equal("Authorized", (await cache.ReadAsync(authorized, CancellationToken.None))!.Items[0].Title);
        var file = Path.Combine(f.Root, IsolatedBangumiRelationsCache.DirectoryName, anon.CacheKey + ".json");
        var json = JsonNode.Parse(File.ReadAllBytes(file))!.AsObject(); json["Identity"]!.AsObject().Remove("AccessScope"); File.WriteAllText(file, json.ToJsonString());
        f.Reopen(); cache = new(f.Profile.Configuration());
        Assert.Equal("Public", (await cache.ReadAsync(anon, CancellationToken.None))!.Items[0].Title);
        Assert.Null(await cache.ReadAsync(authorized with { AccessScope = "authorized-" + new string('C', 64) }, CancellationToken.None));
    }
}
