using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using TheOrder.Application.Playground;
using TheOrder.Infrastructure.PlaygroundProviders;
using Xunit;

namespace TheOrder.Tests;

public sealed class PlaygroundModelCatalogProviderTests
{
    [Fact]
    public async Task GoogleReadsBeyondFivePagesAndPreservesCamelAndSnakeGenerationMetadata()
    {
        var pages = Enumerable.Range(0, 6).Select(index => new JsonObject
        {
            ["models"] = new JsonArray(new JsonObject { ["name"] = "models/gemini-3." + index + "-flash", ["displayName"] = "Catalog fixture",
                ["version"] = "001", [index % 2 == 0 ? "supportedGenerationMethods" : "supported_actions"] = new JsonArray("generateContent") }),
            ["nextPageToken"] = index < 5 ? "next+" + index : null
        }).Select(Json).ToArray();
        using var fixture = new Fixture(PlaygroundProviderKind.Gemini, pages);
        var result = await fixture.Provider.ListModelsAsync(fixture.Provider.DefaultEndpoint, default);
        Assert.Equal(6, result.Length); Assert.Equal(6, fixture.Handler.Requests.Count);
        Assert.All(result, model => { Assert.True(model.GenerationMethodsReported); Assert.Equal("generateContent", Assert.Single(model.GenerationMethods)); Assert.Equal("001", model.Version); });
        Assert.Equal("?pageSize=100&pageToken=next%2B0", fixture.Handler.Requests[1].Uri.Query);
        Assert.Equal("gemini-3.5-flash", Assert.Single(PlaygroundModelSelection.SelectLatest(PlaygroundProviderKind.Gemini, result)).Id);
        Assert.All(fixture.Handler.Requests, request => { Assert.Equal(HttpMethod.Get, request.Method); Assert.Null(request.Body); });
    }

    [Fact]
    public async Task ClaudeUsesAfterIdHasMoreAndCreationMetadataAcrossPages()
    {
        using var fixture = new Fixture(PlaygroundProviderKind.Claude,
            Json(JsonNode.Parse("""{"data":[{"id":"claude-opus-5-5","display_name":"Claude fixture","created_at":"2026-09-21T00:00:00Z","lifecycle":"active"}],"has_more":true,"last_id":"claude-opus-5-5"}""")!.AsObject()),
            Json(JsonNode.Parse("""{"data":[{"id":"claude-sonnet-4-6","created_at":"2026-08-01T00:00:00Z"}],"has_more":false,"last_id":"claude-sonnet-4-6"}""")!.AsObject()));
        var result = await fixture.Provider.ListModelsAsync(fixture.Provider.DefaultEndpoint, default);
        Assert.Equal(2, result.Length); Assert.Equal("?limit=100&after_id=claude-opus-5-5", fixture.Handler.Requests[1].Uri.Query);
        Assert.Equal(DateTimeOffset.Parse("2026-09-21T00:00:00Z"), result[0].CreatedAt); Assert.Equal("active", result[0].Lifecycle);
        Assert.All(fixture.Handler.Requests, request => Assert.Equal(HttpMethod.Get, request.Method));
    }

    [Fact]
    public async Task RepeatedPageCursorFailsRatherThanReturningTheFirstPartialCatalog()
    {
        const string page = """{"models":[{"name":"models/gemini-3.8-flash"}],"nextPageToken":"same"}""";
        using var fixture = new Fixture(PlaygroundProviderKind.Gemini, Json(JsonNode.Parse(page)!.AsObject()), Json(JsonNode.Parse(page)!.AsObject()));
        var error = await Assert.ThrowsAsync<PlaygroundProviderException>(() => fixture.Provider.ListModelsAsync(fixture.Provider.DefaultEndpoint, default));
        Assert.Equal(PlaygroundFailureKind.InvalidResponse, error.Failure.Kind); Assert.Equal(2, fixture.Handler.Requests.Count);
    }

    [Fact]
    public async Task LaterPageHttpFailureDoesNotBecomeAnEmptyOrStaleSuccessfulCatalog()
    {
        using var fixture = new Fixture(PlaygroundProviderKind.Gemini,
            Json(JsonNode.Parse("""{"models":[{"name":"models/gemini-4-argon"}],"nextPageToken":"next"}""")!.AsObject()),
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("{}") });
        var error = await Assert.ThrowsAsync<PlaygroundProviderException>(() => fixture.Provider.ListModelsAsync(fixture.Provider.DefaultEndpoint, default));
        Assert.Equal(503, error.Failure.HttpStatus); Assert.Equal(2, fixture.Handler.Requests.Count);
    }

    [Theory]
    [InlineData("""{"data":[{"id":"claude-opus-5-5"}],"has_more":true}""")]
    [InlineData("""{"data":[],"has_more":true,"last_id":"missing"}""")]
    [InlineData("""{"data":[{"id":"claude-opus-5-5"}],"has_more":"yes"}""")]
    public async Task ClaudeRejectsIncompleteOrMalformedPagination(string body)
    {
        using var fixture = new Fixture(PlaygroundProviderKind.Claude, Json(JsonNode.Parse(body)!.AsObject()));
        var error = await Assert.ThrowsAsync<PlaygroundProviderException>(() => fixture.Provider.ListModelsAsync(fixture.Provider.DefaultEndpoint, default));
        Assert.Equal(PlaygroundFailureKind.InvalidResponse, error.Failure.Kind); Assert.Single(fixture.Handler.Requests);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"data":"invalid"}""")]
    [InlineData("""{"data":[{"id":"gpt-6.1-sol","created":-1}]}""")]
    public async Task InvalidCatalogDoesNotMasqueradeAsARealEmptyList(string body)
    {
        using var fixture = new Fixture(PlaygroundProviderKind.OpenAI, Json(JsonNode.Parse(body)!.AsObject()));
        await Assert.ThrowsAsync<PlaygroundProviderException>(() => fixture.Provider.ListModelsAsync(fixture.Provider.DefaultEndpoint, default));
        Assert.Single(fixture.Handler.Requests);
    }

    [Fact]
    public async Task GenuineEmptyAndVisibleUnknownModelsRemainDistinctFromFailedOrInventedLists()
    {
        using var fixture = new Fixture(PlaygroundProviderKind.OpenAI, Json(JsonNode.Parse("""{"data":[]}""")!.AsObject()),
            Json(JsonNode.Parse("""{"data":[{"id":"gpt-99-sol","created":1790000000},{"id":"gpt-99-sol","created":1790000000}]}""")!.AsObject()));
        Assert.Empty(await fixture.Provider.ListModelsAsync(fixture.Provider.DefaultEndpoint, default));
        var result = await fixture.Provider.ListModelsAsync(fixture.Provider.DefaultEndpoint, default);
        var model = Assert.Single(result); Assert.False(model.Capabilities.Verified); Assert.NotNull(model.CreatedAt);
        Assert.Equal("gpt-99-sol", Assert.Single(PlaygroundModelSelection.SelectLatest(PlaygroundProviderKind.OpenAI, result)).Id);
    }

    [Fact]
    public async Task GoogleCombinesBothMetadataFormsAndExplicitNonTextActionsStayExcluded()
    {
        using var fixture = new Fixture(PlaygroundProviderKind.Gemini, Json(JsonNode.Parse("""{"models":[{"name":"models/gemini-4-argon","supportedGenerationMethods":["countTokens"],"supported_actions":["generateContent"]},{"name":"models/gemini-99-flash","supported_actions":["embedContent"]}]}""")!.AsObject()));
        var result = await fixture.Provider.ListModelsAsync(fixture.Provider.DefaultEndpoint, default);
        Assert.Equal(2, result.Length); Assert.Equal(2, result[0].GenerationMethods.Length);
        Assert.Equal("gemini-4-argon", Assert.Single(PlaygroundModelSelection.SelectLatest(PlaygroundProviderKind.Gemini, result)).Id);
    }

    [Fact]
    public async Task DirectoryCancellationDoesNotReturnItsAlreadyReceivedPartialPage()
    {
        using var cancellation = new CancellationTokenSource();
        using var fixture = new Fixture(PlaygroundProviderKind.Gemini, Json(JsonNode.Parse("""{"models":[{"name":"models/gemini-4-argon"}],"nextPageToken":"next"}""")!.AsObject()));
        fixture.Handler.AfterResponse = () => cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Provider.ListModelsAsync(fixture.Provider.DefaultEndpoint, cancellation.Token));
        Assert.Single(fixture.Handler.Requests);
    }

    private static HttpResponseMessage Json(JsonObject value) => new(HttpStatusCode.OK) { Content = new StringContent(value.ToJsonString(), Encoding.UTF8, "application/json") };
    private sealed record Request(Uri Uri, HttpMethod Method, string? Body);
    private sealed class Fixture : IDisposable
    {
        private readonly HttpClient client;
        public Handler Handler { get; }
        public IPlaygroundProvider Provider { get; }
        public Fixture(PlaygroundProviderKind kind, params HttpResponseMessage[] responses)
        { Handler = new(responses); client = new(Handler); Provider = new PlaygroundProviderRegistry(client, new Credentials()).Providers.Single(x => x.Kind == kind); }
        public void Dispose() => client.Dispose();
    }
    private sealed class Handler(params HttpResponseMessage[] responses) : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> remaining = new(responses);
        public List<Request> Requests { get; } = [];
        public Action? AfterResponse;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            Requests.Add(new(request.RequestUri!, request.Method, request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellation)));
            if (!remaining.TryDequeue(out var response)) throw new InvalidOperationException("Unexpected synthetic GET; no network exists.");
            response.RequestMessage = request; AfterResponse?.Invoke(); return response;
        }
    }
    private sealed class Credentials : IPlaygroundCredentials
    {
        public Task<bool> HasAsync(PlaygroundProviderKind provider, string endpoint, CancellationToken cancellation) => Task.FromResult(true);
        public Task<string?> ReadAsync(PlaygroundProviderKind provider, string endpoint, CancellationToken cancellation)
        { cancellation.ThrowIfCancellationRequested(); return Task.FromResult<string?>("synthetic-directory-key"); }
        public Task SaveAsync(PlaygroundProviderKind provider, string endpoint, string key, CancellationToken cancellation) => throw new NotSupportedException();
        public Task ClearAsync(PlaygroundProviderKind provider, string endpoint, CancellationToken cancellation) => throw new NotSupportedException();
    }
}
