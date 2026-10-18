using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using TheOrder.Application;
using TheOrder.Infrastructure.Online;
using Xunit;

namespace TheOrder.Tests;

public sealed class IgdbProductionEndpointTests
{
    [Theory]
    [InlineData(NativeProfileKind.Production)]
    [InlineData(NativeProfileKind.Beta)]
    public void PublicProfilesHaveTheSameCredentialFreeBuiltInHttpsService(NativeProfileKind kind)
    {
        var uri = IgdbServiceEndpoint.Resolve(kind, null);
        Assert.Equal(IgdbServiceEndpoint.PublicServiceUrl, uri!.AbsoluteUri);
        Assert.True(IgdbServiceEndpoint.Valid(uri));
        Assert.DoesNotContain("bangumi", uri.Host);
    }

    [Theory]
    [InlineData(NativeProfileKind.Test)]
    [InlineData(NativeProfileKind.Development)]
    public void IsolatedProfilesDoNotAccidentallyUsePublicProductionService(NativeProfileKind kind)
    { Assert.Null(IgdbServiceEndpoint.Resolve(kind, null)); Assert.Null(IgdbServiceEndpoint.Resolve(null, null)); }

    [Theory]
    [InlineData("http://127.0.0.1:8787/")]
    [InlineData("http://localhost/")]
    [InlineData("https://127.0.0.1/")]
    [InlineData("https://localhost/")]
    [InlineData("https://worker.local/")]
    [InlineData("https://worker.internal/")]
    [InlineData("https://worker.test:8443/")]
    [InlineData("https://user:secret@worker.test/")]
    [InlineData("https://worker.test/?token=secret")]
    [InlineData("https://worker.test/#secret")]
    [InlineData("https://worker.test/api/")]
    [InlineData("not a URL")]
    public void FormalProfileRejectsTestOrMalformedServiceConfiguration(string value)
    { Assert.Null(IgdbServiceEndpoint.Resolve(NativeProfileKind.Production, value)); }

    [Fact]
    public void OnlyExplicitTestProfileCanUseLoopbackAndPublicEndpointCanBeReconfigured()
    {
        Assert.Equal("http://127.0.0.1:8787/", IgdbServiceEndpoint.Resolve(NativeProfileKind.Test, "http://127.0.0.1:8787/")!.AbsoluteUri);
        Assert.Null(IgdbServiceEndpoint.Resolve(NativeProfileKind.Development, "http://127.0.0.1:8787/"));
        Assert.Equal("https://replacement.test/", IgdbServiceEndpoint.Resolve(NativeProfileKind.Production, "https://replacement.test/")!.AbsoluteUri);
    }

    [Fact]
    public async Task WireContainsOnlyPublicTitleAndExternalIdsWithNoLibraryIdentityOrCredentials()
    {
        using var client = new HttpClient(new Handler(async request =>
        {
            Assert.Equal("https://the-order-igdb-game-videos.zyx2251721565.workers.dev/v1/game-videos/query", request.RequestUri!.AbsoluteUri);
            Assert.Null(request.Headers.Authorization); Assert.False(request.Headers.Contains("Client-ID"));
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            Assert.Equal(new[] { "id", "steamId", "title" }, json.RootElement.EnumerateObject().Select(x => x.Name).Order().ToArray());
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(new IgdbResult([], true, DateTimeOffset.UtcNow)) };
        }));
        var provider = new IgdbGameVideoProvider(client, IgdbServiceEndpoint.Resolve(NativeProfileKind.Production, null));
        Assert.Empty((await provider.QueryAsync(new("Source title", "123", 1), default)).Games);
    }

    [Fact]
    public async Task RejectedFormalLoopbackNeverPerformsNetworkIo()
    {
        using var client = new HttpClient(new Handler(_ => throw new InvalidOperationException("Must not connect")));
        var provider = new IgdbGameVideoProvider(client, new("http://localhost:8787/"));
        var error = await Assert.ThrowsAsync<GameVideoException>(() => provider.QueryAsync(new("Test"), default));
        Assert.Equal("unconfigured", error.Code);
    }

    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> action) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellation) => action(request); }
}
