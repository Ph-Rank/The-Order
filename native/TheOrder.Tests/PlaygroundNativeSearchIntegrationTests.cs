using System.Collections.Immutable;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using TheOrder.Application;
using TheOrder.Application.Playground;
using TheOrder.Domain;
using TheOrder.Infrastructure.PlaygroundProviders;
using Xunit;

namespace TheOrder.Tests;

// The real coordinator and real protocol adapters share a synthetic HTTP edge.
// No network, production store or real credential is involved.
public sealed class PlaygroundNativeSearchIntegrationTests
{
    private const string Url = "https://example.org/official";
    private const string Question = "《樱之诗》2015版与10周年版的语音覆盖范围有什么区别？请查官方资料并给出出处，不涉及剧情。";
    private const string PrivateSystem = "PRIVATE-SYSTEM-CANARY：偏好简洁讨论。";

    [Fact]
    public async Task GoogleNativeResearchShowsRealCitationsAndSuggestionsWithoutPersistingGroundedTextOrPrivateFields()
    {
        var grounded = new JsonObject
        {
            ["status"] = "completed", ["model"] = "gemini-4-argon",
            ["steps"] = new JsonArray(
                new JsonObject { ["type"] = "google_search_call", ["id"] = "lookup", ["arguments"] = new JsonObject { ["queries"] = new JsonArray("サクラノ詩 公式 ボイス") } },
                new JsonObject { ["type"] = "google_search_result", ["call_id"] = "lookup", ["search_suggestions"] = "<div>native search suggestion</div>", ["result"] = new JsonArray() },
                new JsonObject { ["type"] = "model_output", ["role"] = "model", ["content"] = new JsonArray(new JsonObject
                { ["type"] = "text", ["text"] = "官方资料。", ["annotations"] = new JsonArray(new JsonObject
                    { ["type"] = "url_citation", ["url"] = Url, ["title"] = "官方来源", ["start_index"] = 0, ["end_index"] = 12 }) }) }),
            ["usage"] = new JsonObject { ["total_input_tokens"] = 20, ["total_output_tokens"] = 5, ["total_thought_tokens"] = 3,
                ["grounding_tool_count"] = new JsonArray(new JsonObject { ["type"] = "google_search", ["count"] = 1 }) }
        };
        int generations = 0;
        using var handler = new Handler((path, _) =>
        {
            Assert.EndsWith("/interactions", path);
            return generations++ switch
            {
                0 => Json(new JsonObject { ["status"] = "requires_action", ["model"] = "gemini-4-argon",
                    ["steps"] = new JsonArray(new JsonObject { ["type"] = "function_call", ["id"] = "ask-public-search",
                        ["name"] = "public_search", ["arguments"] = new JsonObject(), ["signature"] = "synthetic-search-permission-signature" }) }),
                1 => Json(grounded),
                _ => throw new InvalidOperationException("Google display-only research must not return to the private discussion model")
            };
        });
        using var http = new HttpClient(handler);
        var store = new Store(); var credentials = new Credentials();
        var provider = new PlaygroundProviderRegistry(http, credentials).Providers.Single(x => x.Kind == PlaygroundProviderKind.Gemini);
        var library = Library();
        await using var service = new PlaygroundService(store, credentials, [provider], () => library);
        var conversation = await Ready(service, provider, "gemini-4-argon");
        await service.SendAsync(conversation, Question);
        var answer = service.Snapshot.Messages.Last(x => x.Role == "assistant");
        Assert.Equal(PlaygroundMessageStatus.Complete, answer.Status);
        Assert.Equal("官方资料。", answer.Text); Assert.Equal(PlaygroundRetention.SessionOnly, answer.Retention);
        Assert.Equal("<div>native search suggestion</div>", answer.SearchSuggestionsHtml);
        var source = Assert.Single(answer.Sources); Assert.Equal(Url, source.Url); Assert.True(source.HasExactRange);
        Assert.Equal(new[] { "/v1beta/interactions", "/v1beta/interactions" }, handler.Requests.Select(x => x.Path));
        var discussion = JsonNode.Parse(handler.Requests[0].Body)!;
        Assert.True(discussion["stream"]!.GetValue<bool>());
        Assert.Contains(discussion["tools"]!.AsArray(), tool => tool?["name"]?.GetValue<string>() == "public_search");
        Assert.All(discussion["tools"]!.AsArray(), tool => Assert.Equal("function", tool!["type"]!.GetValue<string>()));
        Assert.Contains("PRIVATE-SYSTEM-CANARY", handler.Requests[0].Body);
        var sent = handler.Requests[1]; var body = JsonNode.Parse(sent.Body)!;
        Assert.Equal("google_search", Assert.Single(body["tools"]!.AsArray())!["type"]!.GetValue<string>());
        Assert.False(body["stream"]!.GetValue<bool>()); Assert.False(body["store"]!.GetValue<bool>());
        Assert.Equal("medium", body["generation_config"]!["thinking_level"]!.GetValue<string>());
        Assert.Equal("medium", discussion["generation_config"]!["thinking_level"]!.GetValue<string>());
        Assert.DoesNotContain("PRIVATE-NOTE-CANARY", sent.Body); Assert.DoesNotContain("PRIVATE-NAME-CANARY", sent.Body);
        Assert.DoesNotContain("PRIVATE-SYSTEM-CANARY", sent.Body); Assert.DoesNotContain("synthetic-search-permission-signature", sent.Body);
        Assert.DoesNotContain("ask-public-search", sent.Body); Assert.Null(body["previous_interaction_id"]);
        Assert.All(store.Writes, saved =>
        {
            // Compare decoded visible text, rather than letting JSON's default
            // Unicode escaping make a negative privacy assertion vacuous.
            string durable = JsonSerializer.Serialize(saved, new JsonSerializerOptions
                { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
            Assert.DoesNotContain("官方资料。", durable);
            Assert.DoesNotContain("<div>native search suggestion</div>", durable);
            Assert.DoesNotContain("synthetic-search-permission-signature", durable);
        });
    }

    [Fact]
    public async Task DeepSeekNativeSearchThenRegisteredFetchRunsThroughTheRealCoordinatorAndReadOnlyAdapter()
    {
        string sourceId = "deepseek-web-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Url)))[..20].ToLowerInvariant();
        int generations = 0;
        using var handler = new Handler((path, body) => path switch
        {
            "/v1/tools/search" => Json(new JsonObject { ["results"] = new JsonArray(new JsonObject
                { ["title"] = "官方说明", ["url"] = Url, ["summary"] = "搜索摘要。", ["content"] = "UNREQUESTED-FULL-PAGE-CANARY" }) }),
            "/v1/tools/fetch" => Json(new JsonObject { ["title"] = "官方说明", ["content"] = "官方网页正文，仅供只读引用。" }),
            "/responses" when generations == 0 => PublicTool("ask-public-search", "public_search", "{}", ref generations),
            "/responses" when generations == 1 => PublicTool("read-source", "public_fetch", JsonSerializer.Serialize(new { source_id = sourceId }), ref generations),
            "/responses" => Json(new JsonObject { ["status"] = "completed", ["output"] = new JsonArray(new JsonObject
                { ["type"] = "message", ["role"] = "assistant", ["content"] = new JsonArray(new JsonObject { ["type"] = "output_text", ["text"] = "已查阅官方网页。" }) }) }),
            _ => throw new InvalidOperationException("Unexpected synthetic request")
        });
        using var http = new HttpClient(handler);
        var store = new Store(); var credentials = new Credentials();
        var provider = new PlaygroundProviderRegistry(http, credentials).Providers.Single(x => x.Kind == PlaygroundProviderKind.DeepSeek);
        var library = Library();
        await using var service = new PlaygroundService(store, credentials, [provider], () => library);
        var conversation = await Ready(service, provider, "deepseek-flash");
        await service.SendAsync(conversation, Question);
        var answer = service.Snapshot.Messages.Last(x => x.Role == "assistant");
        Assert.Equal(PlaygroundMessageStatus.Complete, answer.Status); Assert.Equal("已查阅官方网页。", answer.Text);
        Assert.Equal(new[] { "/responses", "/v1/tools/search", "/responses", "/v1/tools/fetch", "/responses" }, handler.Requests.Select(x => x.Path));
        var search = JsonNode.Parse(handler.Requests[1].Body)!; Assert.False(search["need_content"]!.GetValue<bool>());
        Assert.Equal(3, search.AsObject().Count);
        Assert.Equal(Url, JsonNode.Parse(handler.Requests[3].Body)!["url"]!.GetValue<string>());
        Assert.Single(JsonNode.Parse(handler.Requests[3].Body)!.AsObject());
        Assert.Contains("PRIVATE-SYSTEM-CANARY", handler.Requests[0].Body);
        foreach (var request in handler.Requests.Where(x => x.Path is "/v1/tools/search" or "/v1/tools/fetch"))
        {
            Assert.DoesNotContain("PRIVATE-NOTE-CANARY", request.Body); Assert.DoesNotContain("PRIVATE-NAME-CANARY", request.Body);
            Assert.DoesNotContain("PRIVATE-SYSTEM-CANARY", request.Body); Assert.DoesNotContain("ask-public-search", request.Body);
        }
        foreach (var request in handler.Requests.Where(x => x.Path == "/responses"))
        {
            var body = JsonNode.Parse(request.Body)!;
            Assert.All(body["tools"]!.AsArray(), tool => Assert.Equal("function", tool!["type"]!.GetValue<string>()));
            Assert.Contains(body["tools"]!.AsArray(), tool => tool?["name"]?.GetValue<string>() == "public_search");
            Assert.Equal("medium", body["reasoning"]!["effort"]!.GetValue<string>());
            Assert.DoesNotContain("UNREQUESTED-FULL-PAGE-CANARY", request.Body);
        }
        Assert.Contains("官方网页正文", JsonNode.Parse(handler.Requests.Last().Body)!.ToJsonString(new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
        Assert.Contains(answer.Sources, source => source.Id == sourceId && source.Url == Url);
    }

    [Theory]
    [InlineData(PlaygroundProviderKind.Gemini, "gemini-4-argon")]
    [InlineData(PlaygroundProviderKind.DeepSeek, "deepseek-flash")]
    public async Task SearchPermissionDoesNotPresearchWhenTheDiscussionModelDoesNotRequestIt(PlaygroundProviderKind kind, string model)
    {
        const string text = "根据已有知识讨论，不代表已联网核实。";
        using var handler = new Handler((_, _) => Json(kind == PlaygroundProviderKind.Gemini
            ? new JsonObject { ["status"] = "completed", ["steps"] = new JsonArray(new JsonObject { ["type"] = "model_output",
                ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = text }) }) }
            : new JsonObject { ["status"] = "completed", ["output"] = new JsonArray(new JsonObject { ["type"] = "message", ["role"] = "assistant",
                ["content"] = new JsonArray(new JsonObject { ["type"] = "output_text", ["text"] = text }) }) }));
        using var http = new HttpClient(handler);
        var store = new Store(); var credentials = new Credentials();
        var provider = new PlaygroundProviderRegistry(http, credentials).Providers.Single(x => x.Kind == kind);
        var library = Library();
        await using var service = new PlaygroundService(store, credentials, [provider], () => library);
        var conversation = await Ready(service, provider, model);
        await service.SendAsync(conversation, Question);
        var sent = Assert.Single(handler.Requests); var body = JsonNode.Parse(sent.Body)!;
        Assert.True(body["stream"]!.GetValue<bool>());
        Assert.Contains(body["tools"]!.AsArray(), tool => tool?["name"]?.GetValue<string>() == "public_search");
        Assert.All(body["tools"]!.AsArray(), tool => Assert.Equal("function", tool!["type"]!.GetValue<string>()));
        var answer = service.Snapshot.Messages.Last(x => x.Role == "assistant");
        Assert.Equal(PlaygroundMessageStatus.Complete, answer.Status); Assert.Equal(text, answer.Text);
        Assert.Equal(PlaygroundRetention.Portable, answer.Retention); Assert.Null(answer.SearchSuggestionsHtml);
        Assert.DoesNotContain(answer.Sources, source => source.Kind == PlaygroundSourceKind.Web);
    }

    private static HttpResponseMessage PublicTool(string id, string name, string arguments, ref int generations)
    {
        generations++;
        return Json(new JsonObject { ["status"] = "completed", ["output"] = new JsonArray(new JsonObject
            { ["type"] = "function_call", ["id"] = "item-" + id, ["call_id"] = id, ["name"] = name, ["arguments"] = arguments }) });
    }

    private static async Task<Guid> Ready(PlaygroundService service, IPlaygroundProvider provider, string model)
    {
        await service.InitializeAsync();
        await service.ConfigureAsync(new() { Enabled = true, DiscussionConsent = true, Permissions = new(true, true, true, false),
            Connections = [new(provider.Kind, provider.DefaultEndpoint, model, true)] });
        var id = await service.CreateConversationAsync(false);
        var options = service.Snapshot.Conversations.Single(x => x.Id == id).Options;
        await service.UpdateConversationAsync(id, options with { Provider = provider.Kind, Endpoint = provider.DefaultEndpoint, Model = model,
            // Legacy Deep is accepted as permission-only Smart; neither an
            // explicit request to verify nor the old enum may force presearch.
            Search = PlaygroundSearchMode.Deep, Spoiler = PlaygroundSpoilerMode.Full, ReasoningEffort = "medium", SystemInstructions = PrivateSystem });
        return id;
    }
    private static LibrarySnapshot Library()
    {
        var metadata = new WorkMetadata("1", "樱之诗", "サクラノ詩", [], null, null, null, null, "公开简介", null, null,
            [], [], [], [], [], null, null, "2026-10-31T00:00:00Z");
        var entry = new LibraryEntry("g", "PRIVATE-NAME-CANARY", true, metadata, [], null, "planned", null, false, [], [],
            "PRIVATE-NOTE-CANARY", 0, 0, [], "2026-10-31T00:00:00Z", "2026-10-31T00:00:00Z", null, null, new(false, null));
        return new([entry], LibraryRecovery.Main);
    }
    private static HttpResponseMessage Json(JsonNode node) => new(HttpStatusCode.OK) { Content = new StringContent(node.ToJsonString(), Encoding.UTF8, "application/json") };
    private sealed class Handler(Func<string, string, HttpResponseMessage> response) : HttpMessageHandler
    {
        public List<(string Path, string Body)> Requests { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string path = request.RequestUri!.AbsolutePath, body = await request.Content!.ReadAsStringAsync(cancellationToken);
            Requests.Add((path, body)); return response(path, body);
        }
    }
    private sealed class Credentials : IPlaygroundCredentials
    {
        public Task<bool> HasAsync(PlaygroundProviderKind p, string e, CancellationToken c) => Task.FromResult(true);
        public Task<string?> ReadAsync(PlaygroundProviderKind p, string e, CancellationToken c) => Task.FromResult<string?>("synthetic-unused-credential");
        public Task SaveAsync(PlaygroundProviderKind p, string e, string k, CancellationToken c) => throw new NotSupportedException();
        public Task ClearAsync(PlaygroundProviderKind p, string e, CancellationToken c) => throw new NotSupportedException();
    }
    private sealed class Store : IPlaygroundStore
    {
        public PlaygroundIdentity Identity { get; } = new(Guid.NewGuid(), Guid.NewGuid());
        public PlaygroundState? Value; public List<PlaygroundState> Writes { get; } = [];
        public Task<PlaygroundState?> LoadAsync(CancellationToken c) => Task.FromResult(Value);
        public Task SaveAsync(PlaygroundState value, long expectedRevision, CancellationToken c)
        { c.ThrowIfCancellationRequested(); Assert.Equal(Value?.Revision ?? 0, expectedRevision); Value = value; Writes.Add(value); return Task.CompletedTask; }
    }
}
