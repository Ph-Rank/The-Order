using System.Collections.Immutable;
using System.Net;
using System.Text.Json;
using TheOrder.Application;
using TheOrder.Infrastructure.Online;
using Xunit;
using static TheOrder.Tests.OnlineTransportTests;

namespace TheOrder.Tests;

public sealed class NextMoePersonalApiTests
{
    private sealed class Fixture : IDisposable
    {
        internal readonly NextMoeAuthTests.Fixture Identity = new();
        internal readonly HttpClient Client;
        internal readonly NextMoeRateCoordinator Rate;
        internal readonly NextMoePersonalApi Api;
        internal readonly List<(string Method, string Url, string? Key, string? Body)> Requests = [];
        internal Fixture(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
        {
            Rate = new(Identity.Time, (wait, token) => { token.ThrowIfCancellationRequested(); Identity.Time.Now += wait; return Task.CompletedTask; });
            Client = new(new Handler(async (request, token) => {
                Assert.Equal("https", request.RequestUri!.Scheme); Assert.Equal("api.nextmoe.dev", request.RequestUri.Host);
                Assert.StartsWith("/v2/me/", request.RequestUri.AbsolutePath);
                Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
                Assert.StartsWith("synthetic-access-", request.Headers.Authorization.Parameter);
                Requests.Add((request.Method.Method, request.RequestUri.AbsoluteUri, request.Headers.TryGetValues("Idempotency-Key", out var key) ? key.Single() : null,
                    request.Content is null ? null : await request.Content.ReadAsStringAsync(token)));
                return await handler(request, token);
            }));
            Api = new(Client, Identity.Auth, Rate);
        }
        public void Dispose() => Client.Dispose();
        internal Task Login() => Identity.Auth.SignInAsync(default);
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task NonUserIdentityNeverSendsPrivateRequest(bool legacyKey)
    {
        using var f = new Fixture((_, _) => throw new InvalidOperationException("Private request escaped identity guard."));
        if (legacyKey) f.Identity.Keys.Value = "synthetic-legacy-key";
        var error = await Assert.ThrowsAsync<OnlineException>(() => f.Api.PlaytimesAsync(null, default));
        Assert.Equal(OnlineFailure.UserIdentityRequired, error.Failure); Assert.Empty(f.Requests);
    }
    [Fact] public async Task OlderCatalogGrantCanReadTimesAndStatesUntilReauthorizedForFolders()
    {
        using var f = new Fixture((_, _) => Task.FromResult(Json("{\"object\":\"list\",\"items\":[]}")));
        f.Identity.Storage.Record = new(1, NextMoeAccessMode.Account, new("42", "合成用户"), "synthetic-old-refresh",
            [.. NextMoeOAuthIdentity.CatalogScopes]);
        f.Identity.Protocol.GrantedScopes = [.. NextMoeOAuthIdentity.CatalogScopes];
        await f.Api.PlaytimesAsync(null, default); await f.Api.StatesAsync(null, default);
        Assert.Equal(OnlineFailure.ScopeRequired, (await Assert.ThrowsAsync<OnlineException>(() => f.Api.FoldersAsync(null, default))).Failure);
        Assert.Equal(2, f.Requests.Count);
        f.Identity.Protocol.GrantedScopes = null;
        await f.Login(); await f.Api.FoldersAsync(null, default); Assert.Equal(3, f.Requests.Count);
    }
    [Fact] public async Task CursorIsReceivedOpaqueAndSentExactlyRatherThanConstructed()
    {
        using var f = new Fixture((request, _) => Task.FromResult(Json(request.RequestUri!.Query.Contains("cursor=")
            ? "{\"object\":\"list\",\"items\":[]}" : "{\"object\":\"list\",\"items\":[{\"object\":\"playtime\",\"work_id\":\"200\",\"minutes\":7}],\"next_cursor\":\"cur_a.b_~x-2\"}")));
        await f.Login(); var first = await f.Api.PlaytimesAsync(null, default); Assert.True(first.HasMore); Assert.Equal(7, first.Items.Single().Minutes);
        var second = await f.Api.PlaytimesAsync(first.NextCursor, default); Assert.False(second.HasMore); Assert.Contains("cursor=cur_a.b_~x-2", f.Requests.Last().Url);
        await Assert.ThrowsAsync<OnlineException>(() => f.Api.PlaytimesAsync("https://evil.example/", default)); Assert.Equal(2, f.Requests.Count);
    }
    [Theory]
    [InlineData("[]")]
    [InlineData("{\"object\":\"list\",\"items\":[{\"object\":\"playtime\",\"work_id\":\"../secret\",\"minutes\":7}]}")]
    [InlineData("{\"object\":\"list\",\"items\":[{\"object\":\"playtime\",\"work_id\":\"200\",\"minutes\":-1}]}")]
    [InlineData("{\"object\":\"list\",\"items\":[{\"object\":\"playtime\",\"work_id\":\"200\",\"minutes\":0.5}]}")]
    [InlineData("{\"object\":\"list\",\"items\":[],\"next_cursor\":\"forged-cursor\"}")]
    public async Task MalformedPrivateReplyFailsClosed(string body)
    {
        using var f = new Fixture((_, _) => Task.FromResult(Json(body))); await f.Login();
        Assert.Equal(OnlineFailure.InvalidResponse, (await Assert.ThrowsAsync<OnlineException>(() => f.Api.PlaytimesAsync(null, default))).Failure);
    }
    [Fact] public async Task Batch207ChecksIndividualValuesAndKeepsExactIdempotencyBody()
    {
        using var f = new Fixture((_, _) => Task.FromResult(new HttpResponseMessage((HttpStatusCode)207) {
            Content = new StringContent("{\"items\":[{\"status\":200,\"object\":\"playtime\",\"work_id\":\"200\",\"minutes\":123},{\"status\":422,\"problem\":{\"code\":\"UNKNOWN_REFERENCE\"}}]}") }));
        await f.Login(); var key = Guid.NewGuid().ToString("D"); var items = ImmutableArray.Create(new PersonalWrite("200", 123), new PersonalWrite("201", 55));
        var result = await f.Api.WriteAsync(PersonalSyncKind.Playtime, null, items, key, default);
        Assert.True(result[0].Success); Assert.False(result[1].Success); Assert.Equal("201", result[1].WorkId);
        await f.Api.WriteAsync(PersonalSyncKind.Playtime, null, items, key, default);
        Assert.Equal(f.Requests[0].Body, f.Requests[1].Body); Assert.All(f.Requests, r => Assert.Equal(key, r.Key));
        using var payload = JsonDocument.Parse(f.Requests[0].Body!); Assert.Equal(123, payload.RootElement.GetProperty("items")[0].GetProperty("minutes").GetInt64());
        Assert.Equal(2, payload.RootElement.GetProperty("items")[0].EnumerateObject().Count());
    }
    [Theory]
    [InlineData("{\"items\":[]}")]
    [InlineData("{\"items\":[{\"status\":200,\"object\":\"playtime\",\"work_id\":\"999\",\"minutes\":3}]}")]
    [InlineData("{\"items\":[{\"status\":200,\"object\":\"playtime\",\"work_id\":\"200\",\"minutes\":99}]}")]
    [InlineData("{\"items\":[{\"status\":0}]}")]
    public async Task InvalidBatchCannotCloseDurableIntent(string body)
    {
        using var f = new Fixture((_, _) => Task.FromResult(Json(body))); await f.Login();
        Assert.Equal(OnlineFailure.InvalidResponse, (await Assert.ThrowsAsync<OnlineException>(() => f.Api.WriteAsync(PersonalSyncKind.Playtime, null, [new("200", 3)], Guid.NewGuid().ToString("D"), default))).Failure);
    }
    [Fact] public async Task CompletionAndPrivateFolderCreationUseOfficialRestrictedPayloads()
    {
        using var f = new Fixture((request, _) => Task.FromResult(Json(request.RequestUri!.AbsolutePath.EndsWith("work-states")
            ? "{\"items\":[{\"status\":200,\"object\":\"work_state\",\"work_id\":\"200\",\"state\":\"done\",\"completion\":\"all\"}]}"
            : "{\"object\":\"folder\",\"id\":\"10\",\"owner_uid\":\"42\",\"name\":\"合成收藏\",\"item_count\":0,\"updated_at\":\"2026-10-11T00:00:00Z\"}")));
        await f.Login(); await f.Api.WriteAsync(PersonalSyncKind.State, null, [new("200", State: "done", Completion: "all")], Guid.NewGuid().ToString("D"), default);
        Assert.Contains("\"completion\":\"all\"", f.Requests[0].Body);
        await f.Api.CreateFolderAsync("合成收藏", Guid.NewGuid().ToString("D"), default); Assert.Contains("\"visibility\":\"private\"", f.Requests[1].Body);
        await Assert.ThrowsAsync<OnlineException>(() => f.Api.WriteAsync(PersonalSyncKind.State, null, [new("200", State: "wish", Completion: "all")], Guid.NewGuid().ToString("D"), default));
        await Assert.ThrowsAsync<OnlineException>(() => f.Api.WriteAsync(PersonalSyncKind.Rating, null, [new("200", 9)], Guid.NewGuid().ToString("D"), default));
        await Assert.ThrowsAsync<OnlineException>(() => f.Api.WriteAsync(PersonalSyncKind.Playtime, null, [new("200", 60001)], Guid.NewGuid().ToString("D"), default)); Assert.Equal(2, f.Requests.Count);
    }
    [Fact] public async Task UnauthorizedUserRequestRefreshesOnceAndNoOtherIdentityIsUsed()
    {
        int attempts = 0;
        using var f = new Fixture((_, _) => Task.FromResult(++attempts == 1 ? Json("{\"code\":\"TOKEN_EXPIRED\"}", 401) : Json("{\"object\":\"list\",\"items\":[]}")));
        await f.Login(); await f.Api.PlaytimesAsync(null, default); Assert.Equal(1, f.Identity.Protocol.Refreshes); Assert.Equal(2, attempts);
    }
    [Fact] public async Task Repeated401StopsWithoutEndlessRefreshOrAnonymousFallback()
    {
        using var f = new Fixture((_, _) => Task.FromResult(Json("{\"code\":\"TOKEN_EXPIRED\"}", 401))); await f.Login();
        Assert.Equal(OnlineFailure.LoginRequired, (await Assert.ThrowsAsync<OnlineException>(() => f.Api.PlaytimesAsync(null, default))).Failure);
        Assert.Equal(1, f.Identity.Protocol.Refreshes); Assert.Equal(2, f.Requests.Count); Assert.Equal(NextMoeAccessMode.Account, (await f.Identity.Auth.StatusAsync(default)).Mode);
    }
    [Fact] public async Task RedirectNeverFollowsUserTokenToOtherHost()
    {
        using var f = new Fixture((_, _) => { var reply = new HttpResponseMessage(HttpStatusCode.Found); reply.Headers.Location = new("https://evil.example/private"); return Task.FromResult(reply); }); await f.Login();
        Assert.Equal(OnlineFailure.RedirectBlocked, (await Assert.ThrowsAsync<OnlineException>(() => f.Api.PlaytimesAsync(null, default))).Failure); Assert.Single(f.Requests);
    }
    [Fact] public async Task QuotaStopsFurtherCallsAndAccountChangeGetsIndependentSchedule()
    {
        using var f = new Fixture((_, _) => Task.FromResult(Json("{\"code\":\"QUOTA_EXCEEDED\"}", 429))); await f.Login();
        await Assert.ThrowsAsync<OnlineException>(() => f.Api.PlaytimesAsync(null, default)); await Assert.ThrowsAsync<OnlineException>(() => f.Api.PlaytimesAsync(null, default)); Assert.Single(f.Requests);
        f.Identity.Protocol.Account = "99"; await f.Login(); await Assert.ThrowsAsync<OnlineException>(() => f.Api.PlaytimesAsync(null, default)); Assert.Equal(2, f.Requests.Count);
    }
    [Fact] public async Task CancelAndChangedIdentityRejectLateReply()
    {
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var f = new Fixture(async (_, _) => { ready.SetResult(); await release.Task; return Json("{\"object\":\"list\",\"items\":[]}"); }); await f.Login();
        var read = f.Api.PlaytimesAsync(null, default); await ready.Task; await f.Identity.Auth.SignOutAsync(default); release.SetResult();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => read); Assert.Single(f.Requests);
    }
    [Fact] public async Task RateHeadersDelayAndCancellationDoesNotReserveSlot()
    {
        var clock = new NextMoeAuthTests.Clock(); var waits = new List<TimeSpan>();
        var rate = new NextMoeRateCoordinator(clock, (wait, token) => { token.ThrowIfCancellationRequested(); waits.Add(wait); clock.Now += wait; return Task.CompletedTask; });
        await rate.WaitAsync(1, NextMoeAccessMode.Anonymous, default); await rate.WaitAsync(1, NextMoeAccessMode.Anonymous, default); Assert.Equal(TimeSpan.FromSeconds(2), waits.Single());
        using var reply = new HttpResponseMessage(HttpStatusCode.OK); reply.Headers.TryAddWithoutValidation("RateLimit", "limit=30,remaining=0,reset=8"); reply.Headers.TryAddWithoutValidation("RateLimit-Policy", "60;w=60,1000;w=86400");
        rate.Observe(1, reply.Headers, null); await rate.WaitAsync(1, NextMoeAccessMode.Anonymous, default); Assert.Equal(TimeSpan.FromSeconds(8), waits.Last());
        using var cancel = new CancellationTokenSource(); cancel.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => rate.WaitAsync(1, NextMoeAccessMode.Anonymous, cancel.Token));
        await rate.WaitAsync(2, NextMoeAccessMode.Account, default); rate.Observe(1, reply.Headers, "QUOTA_EXCEEDED"); await rate.WaitAsync(2, NextMoeAccessMode.Account, default); Assert.Equal(TimeSpan.FromSeconds(.6), waits.Last());
    }
}
