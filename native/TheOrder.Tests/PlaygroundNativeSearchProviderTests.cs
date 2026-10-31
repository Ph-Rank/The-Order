using System.Collections.Immutable;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using TheOrder.Application.Playground;
using TheOrder.Infrastructure.PlaygroundProviders;
using Xunit;

namespace TheOrder.Tests;

// Synthetic public pages and native responses only. The handler has no network
// delegate and credentials below are deliberately not real API credentials.
public sealed class PlaygroundNativeSearchProviderTests
{
    private const string SourceUrl = "https://example.org/official";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GoogleResearchReadsRealAnnotationsUtf8RangesSuggestionsAndReportedCounts(bool suggestionsOnStep)
    {
        var search = new JsonObject { ["type"] = "google_search_result", ["call_id"] = "public-search", ["result"] = new JsonArray() };
        const string suggestions = "<div>synthetic search suggestions</div>";
        if (suggestionsOnStep) search["search_suggestions"] = suggestions;
        else search["result"]!.AsArray().Add(new JsonObject { ["search_suggestions"] = suggestions });
        var root = GoogleFinal(new JsonObject { ["type"] = "google_search_call", ["id"] = "public-search",
            ["arguments"] = new JsonObject { ["queries"] = new JsonArray("公開作品 公式") } }, search,
            Output(Text("序章 "), Text("前置🙂引用段", Citation(10, 19))));
        root["usage"] = new JsonObject { ["total_input_tokens"] = 20, ["total_output_tokens"] = 8, ["total_thought_tokens"] = 9,
            ["grounding_tool_count"] = new JsonArray(
                new JsonObject { ["type"] = "google_search", ["count"] = 2 },
                new JsonObject { ["type"] = "other_tool", ["count"] = 90 },
                new JsonObject { ["type"] = "google_search", ["count"] = 3 }) };
        using var fixture = new Fixture(PlaygroundProviderKind.Gemini, Json(root));
        var result = await fixture.Provider.SearchAsync(SearchRequest(fixture), default);
        Assert.Equal("序章 前置🙂引用段", result.Text);
        Assert.Equal(suggestions, result.SearchSuggestionsHtml);
        Assert.Equal(PlaygroundRetention.SessionOnly, result.Retention);
        Assert.Equal(5, result.Usage!.Searches);
        Assert.Equal(17, result.Usage.OutputTokens);
        var source = Assert.Single(result.Sources);
        Assert.Equal(SourceUrl, source.Url); Assert.True(source.HasExactRange);
        Assert.Equal(7, source.Start); Assert.Equal(10, source.End);
        var sent = JsonNode.Parse(Assert.Single(fixture.Handler.Requests).Body)!;
        Assert.False(sent["stream"]!.GetValue<bool>()); Assert.False(sent["store"]!.GetValue<bool>());
        Assert.Equal("high", sent["generation_config"]!["thinking_level"]!.GetValue<string>());
        Assert.Equal(65536, sent["generation_config"]!["max_output_tokens"]!.GetValue<int>());
        Assert.Equal("google_search", Assert.Single(sent["tools"]!.AsArray())!["type"]!.GetValue<string>());
        Assert.Null(sent["max_tool_calls"]); Assert.Null(sent["max_turns"]);
    }

    [Theory]
    [InlineData(2, 6)]
    [InlineData(10, 18)]
    [InlineData(10, 200)]
    public async Task InvalidGoogleByteRangeKeepsUrlWithoutInventingPrecisePositions(int start, int end)
    {
        using var fixture = new Fixture(PlaygroundProviderKind.Gemini, Json(GoogleFinal(Output(Text("前置🙂引用段", Citation(start, end))))));
        var events = await Collect(fixture.Provider, Request(fixture));
        var source = Assert.Single(events, x => x.Kind == PlaygroundProviderEventKind.Source).Source!;
        Assert.Equal(SourceUrl, source.Url); Assert.False(source.HasExactRange);
        Assert.Null(source.Start); Assert.Null(source.End);
    }

    [Fact]
    public async Task GoogleAnnotationDeltaWithAmbiguousTextPartKeepsAnInexactSource()
    {
        string stream = Event("step.start", new() { ["index"] = 0, ["step"] = Output(Text("前文"), Text("后文")) })
            + Event("step.delta", Delta(0, new() { ["type"] = "text_annotation_delta", ["annotations"] = new JsonArray(Citation(0, 3)) }))
            + Event("step.stop", new() { ["index"] = 0 })
            + Event("interaction.completed", new() { ["interaction"] = new JsonObject { ["status"] = "completed" } });
        using var fixture = new Fixture(PlaygroundProviderKind.Gemini, Sse(stream));
        var events = await Collect(fixture.Provider, Request(fixture));
        var source = Assert.Single(events, x => x.Kind == PlaygroundProviderEventKind.Source).Source!;
        Assert.Equal(SourceUrl, source.Url); Assert.False(source.HasExactRange);
        Assert.Null(source.Start); Assert.Null(source.End);
    }

    [Fact]
    public async Task GoogleSeparateAnnotationAndSummaryDeltasRetainCanonicalHierarchyForTheNextTurn()
    {
        string stream = Event("step.start", new() { ["index"] = 0, ["step"] = new JsonObject { ["type"] = "thought" } })
            + Event("step.delta", Delta(0, new() { ["type"] = "thought_summary", ["content"] = Text("synthetic summary one") }))
            + Event("step.delta", Delta(0, new() { ["type"] = "thought_summary", ["content"] = Text("synthetic summary two") }))
            + Event("step.delta", Delta(0, new() { ["type"] = "thought_signature", ["signature"] = "complete-synthetic-signature" }))
            + Event("step.stop", new() { ["index"] = 0 })
            + Event("step.start", new() { ["index"] = 1, ["step"] = new JsonObject { ["type"] = "model_output" } })
            + Event("step.delta", Delta(1, new() { ["type"] = "text", ["text"] = "前置🙂引用段" }))
            + Event("step.delta", Delta(1, new() { ["type"] = "text_annotation_delta", ["annotations"] = new JsonArray(Citation(10, 19)) }))
            + Event("step.stop", new() { ["index"] = 1 })
            + Event("interaction.completed", new() { ["interaction"] = new JsonObject { ["status"] = "completed" } });
        using var fixture = new Fixture(PlaygroundProviderKind.Gemini, Sse(stream), Json(GoogleFinal(Output(Text("next answer")))));
        var request = Request(fixture) with { ConversationBinding = "synthetic-conversation-chain" };
        var first = await Collect(fixture.Provider, request);
        Assert.DoesNotContain(first, x => x.Kind == PlaygroundProviderEventKind.Error);
        var source = Assert.Single(first, x => x.Kind == PlaygroundProviderEventKind.Source).Source!;
        Assert.True(source.HasExactRange); Assert.Equal(4, source.Start); Assert.Equal(7, source.End);
        var tape = Assert.Single(first, x => x.Kind == PlaygroundProviderEventKind.ConversationTape).ConversationTape!;
        var second = await Collect(fixture.Provider, request with { TurnId = Guid.NewGuid(), Binding = "new-turn",
            ConversationTape = tape, Messages = [new("user", "fresh user question")] });
        Assert.DoesNotContain(second, x => x.Kind == PlaygroundProviderEventKind.Error);
        var items = JsonNode.Parse(fixture.Handler.Requests[1].Body)!["input"]!.AsArray();
        var thought = Assert.Single(items, x => x?["type"]?.GetValue<string>() == "thought")!;
        Assert.Equal("complete-synthetic-signature", thought["signature"]!.GetValue<string>());
        Assert.Equal(2, thought["summary"]!.AsArray().Count); Assert.Null(thought["content"]);
        var output = Assert.Single(items, x => x?["type"]?.GetValue<string>() == "model_output")!;
        Assert.Null(output["annotations"]);
        var annotation = Assert.Single(output["content"]![0]!["annotations"]!.AsArray())!;
        Assert.Equal(10, annotation["start_index"]!.GetValue<int>());
        Assert.Equal(19, annotation["end_index"]!.GetValue<int>());
    }

    [Fact]
    public async Task GoogleJsonStepsAreReplayedWholeRatherThanOnlyTheirVisibleText()
    {
        var response = GoogleFinal(new JsonObject { ["type"] = "thought", ["signature"] = "native-thought-signature",
                ["summary"] = new JsonArray(Text("synthetic native summary")) },
            new JsonObject { ["type"] = "google_search_call", ["id"] = "search-one", ["signature"] = "native-search-call-signature",
                ["arguments"] = new JsonObject { ["queries"] = new JsonArray("公開作品") } },
            new JsonObject { ["type"] = "google_search_result", ["call_id"] = "search-one", ["signature"] = "native-result-signature",
                ["result"] = new JsonArray(new JsonObject { ["search_suggestions"] = "<div>synthetic suggestions</div>" }) },
            Output(Text("前置🙂引用段", Citation(10, 19))));
        var originalSteps = response["steps"]!.DeepClone().AsArray();
        using var fixture = new Fixture(PlaygroundProviderKind.Gemini, Json(response), Json(GoogleFinal(Output(Text("continued")))));
        var request = Request(fixture) with { ConversationBinding = "native-json-chain" };
        var first = await Collect(fixture.Provider, request);
        var tape = Assert.Single(first, x => x.Kind == PlaygroundProviderEventKind.ConversationTape).ConversationTape!;
        var second = await Collect(fixture.Provider, request with { TurnId = Guid.NewGuid(), Binding = "new-json-turn",
            ConversationTape = tape, Messages = [new("user", "next question")] });
        Assert.DoesNotContain(second, x => x.Kind == PlaygroundProviderEventKind.Error);
        var input = JsonNode.Parse(fixture.Handler.Requests[1].Body)!["input"]!.AsArray();
        Assert.Equal(originalSteps.Count + 2, input.Count);
        for (int index = 0; index < originalSteps.Count; index++) Assert.True(JsonNode.DeepEquals(originalSteps[index], input[index + 1]));
    }

    [Theory]
    [InlineData(PlaygroundProviderKind.Gemini)]
    [InlineData(PlaygroundProviderKind.OpenAI)]
    [InlineData(PlaygroundProviderKind.Grok)]
    public async Task PrivateDiscussionDoesNotGainHostedSearchFromCapabilities(PlaygroundProviderKind kind)
    {
        var response = kind == PlaygroundProviderKind.Gemini ? GoogleFinal(Output(Text("ordinary reply")))
            : new JsonObject { ["status"] = "completed", ["output"] = new JsonArray(new JsonObject { ["type"] = "message", ["role"] = "assistant",
                ["content"] = new JsonArray(new JsonObject { ["type"] = "output_text", ["text"] = "ordinary reply" }) }) };
        using var fixture = new Fixture(kind, Json(response));
        _ = await Collect(fixture.Provider, Request(fixture));
        Assert.Null(JsonNode.Parse(Assert.Single(fixture.Handler.Requests).Body)!["tools"]);
    }

    [Fact]
    public async Task DeepSeekSearchUsesSummaryAndNeverCopiesUnsolicitedPageContent()
    {
        var results = new JsonArray(
            new JsonObject { ["url"] = SourceUrl, ["title"] = "公式資料", ["summary"] = "作品の発売日は公表されています。", ["content"] = "UNSOLICITED-FULL-PAGE" },
            new JsonObject { ["url"] = "https://example.org/snippet", ["snippet"] = "兼容摘要" },
            new JsonObject { ["url"] = "https://example.org/description", ["description"] = "另一个摘要" },
            new JsonObject { ["url"] = "https://example.org/content-only", ["content"] = "ANOTHER-FULL-PAGE" });
        using var fixture = new Fixture(PlaygroundProviderKind.DeepSeek, Json(new JsonObject { ["results"] = results }));
        var result = await fixture.Provider.SearchAsync(SearchRequest(fixture), default);
        Assert.Contains("作品の発売日は公表されています。", result.Text);
        Assert.Contains("兼容摘要", result.Text); Assert.Contains("另一个摘要", result.Text);
        Assert.DoesNotContain("FULL-PAGE", result.Text); Assert.DoesNotContain("\\u", result.Text);
        Assert.Equal(4, result.Sources.Length); Assert.Equal(1, result.Usage!.Searches);
        var sent = Assert.Single(fixture.Handler.Requests);
        Assert.Equal("/v1/tools/search", sent.Uri.AbsolutePath);
        var body = JsonNode.Parse(sent.Body)!;
        Assert.False(body["need_content"]!.GetValue<bool>()); Assert.Equal(10, body["count"]!.GetValue<int>());
        Assert.Null(body["max_tool_calls"]);
    }

    [Fact]
    public async Task DeepSeekSearchBoundsTitlesAbstractsAndAggregateOutput()
    {
        var results = new JsonArray();
        for (int index = 0; index < 10; index++) results.Add(new JsonObject { ["url"] = "https://example.org/" + new string('x', 1500) + index,
            ["title"] = new string('題', 800), ["summary"] = new string('文', 5000), ["content"] = "NO-FULL-PAGE" });
        using var fixture = new Fixture(PlaygroundProviderKind.DeepSeek, Json(new JsonObject { ["results"] = results }));
        var result = await fixture.Provider.SearchAsync(SearchRequest(fixture), default);
        Assert.InRange(result.Text.Length, 1, 20000); Assert.Contains("省略", result.Text);
        Assert.DoesNotContain("NO-FULL-PAGE", result.Text);
        Assert.All(result.Sources, x => { Assert.InRange(x.Title.Length, 1, 300); Assert.InRange(x.Quote!.Length, 1, 1240); });
        Assert.InRange(result.Sources.Length, 1, 9);
    }

    [Fact]
    public async Task DeepSeekSourcesRemainDistinctAcrossSeveralSearches()
    {
        using var fixture = new Fixture(PlaygroundProviderKind.DeepSeek,
            Json(new JsonObject { ["results"] = new JsonArray(new JsonObject { ["url"] = SourceUrl, ["summary"] = "one" }) }),
            Json(new JsonObject { ["results"] = new JsonArray(new JsonObject { ["url"] = "https://example.org/other", ["summary"] = "two" }) }));
        var first = await fixture.Provider.SearchAsync(SearchRequest(fixture), default);
        var second = await fixture.Provider.SearchAsync(SearchRequest(fixture), default);
        Assert.NotEqual(Assert.Single(first.Sources).Id, Assert.Single(second.Sources).Id);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DeepSeekLongPublicQuestionSendsEveryClauseWithin128RunesAndCountsRequests(bool includeEmoji)
    {
        const string anchor = "《樱之诗》";
        string clause = includeEmoji ? "角色关系🙂与版本资料需要核实，" : "本篇剧本与语音版本需要核实官方发布资料。";
        string question = anchor + string.Concat(Enumerable.Repeat(clause, 24)) + "最后还要核实特典载体和官方建议阅读顺序。";
        var responses = Enumerable.Range(0, 20).Select(_ => Json(new JsonObject { ["results"] = new JsonArray(
            new JsonObject { ["url"] = SourceUrl, ["title"] = "公式資料", ["summary"] = "仅返回合成摘要。" }) })).ToArray();
        using var fixture = new Fixture(PlaygroundProviderKind.DeepSeek, responses);
        var result = await fixture.Provider.SearchAsync(SearchRequest(fixture) with { Query = question }, default);
        var queries = fixture.Handler.Requests.Select(request => JsonNode.Parse(request.Body)!["query"]!.GetValue<string>()).ToArray();
        Assert.True(queries.Length > 1); Assert.Equal(queries.Length, result.Usage!.Searches);
        Assert.All(queries, query => { Assert.StartsWith(anchor + " ", query); Assert.InRange(query.EnumerateRunes().Count(), 1, 128); });
        Assert.Equal(question, string.Concat(queries.Select(query => query[(anchor.Length + 1)..])));
        Assert.Contains(queries, query => query.Contains("官方建议阅读顺序", StringComparison.Ordinal));
        Assert.Single(result.Sources); Assert.InRange(result.Text.Length, 1, 20000);
        Assert.All(fixture.Handler.Requests, request => Assert.Equal(10, JsonNode.Parse(request.Body)!["count"]!.GetValue<int>()));
    }

    [Fact]
    public async Task DeepSeekPartialSegmentFailureDoesNotReturnACompleteResearchResultOrRetry()
    {
        using var fixture = new Fixture(PlaygroundProviderKind.DeepSeek,
            Json(new JsonObject { ["results"] = new JsonArray() }),
            new HttpResponseMessage(HttpStatusCode.BadGateway) { Content = new StringContent("{}") });
        string question = "《樱之诗》" + string.Concat(Enumerable.Repeat("还需核实版本资料以及特典载体。", 20));
        var error = await Assert.ThrowsAsync<PlaygroundProviderException>(() => fixture.Provider.SearchAsync(
            SearchRequest(fixture) with { Query = question }, default));
        Assert.Equal(2, fixture.Handler.Requests.Count);
        Assert.Contains("未完整完成", error.Failure.Message); Assert.Contains("1 次成功 HTTP", error.Failure.Message);
        Assert.Contains("可能计费", error.Failure.Message); Assert.Contains("未自动重试", error.Failure.Message);
    }

    [Fact]
    public async Task DeepSeekFetchReturnsOnlyBoundedUnicodeContentAndItsAdmittedUrl()
    {
        using var fixture = new Fixture(PlaygroundProviderKind.DeepSeek, Json(new JsonObject { ["url"] = "https://untrusted.example/replacement",
            ["title"] = "公式ページ", ["content"] = "読み取り本文" + new string('文', 20001), ["metadata"] = "DO-NOT-FORWARD" }));
        var result = await fixture.Provider.FetchAsync(FetchRequest(fixture, SourceUrl), default);
        var text = JsonNode.Parse(result.Text)!;
        Assert.True(text["truncated"]!.GetValue<bool>()); Assert.Equal(20000, text["content"]!.GetValue<string>().Length);
        Assert.Equal(SourceUrl, text["url"]!.GetValue<string>());
        Assert.Contains("読み取り本文", result.Text); Assert.DoesNotContain("\\u", result.Text);
        Assert.DoesNotContain("DO-NOT-FORWARD", result.Text); Assert.DoesNotContain("untrusted.example", result.Text);
        Assert.Equal(SourceUrl, Assert.Single(result.Sources).Url); Assert.Equal(1, result.Usage!.Fetches);
        var sent = Assert.Single(fixture.Handler.Requests);
        Assert.Equal("https://api.deepseek.com/v1/tools/fetch", sent.Uri.AbsoluteUri);
        var body = JsonNode.Parse(sent.Body)!.AsObject(); Assert.Single(body); Assert.Equal(SourceUrl, body["url"]!.GetValue<string>());
    }

    [Fact]
    public async Task EmptyFetchIsNotPresentedAsAReadPage()
    {
        using var fixture = new Fixture(PlaygroundProviderKind.DeepSeek, Json(new JsonObject { ["content"] = "" }));
        var result = await fixture.Provider.FetchAsync(FetchRequest(fixture, SourceUrl), default);
        Assert.Equal("empty", JsonNode.Parse(result.Text)!["status"]!.GetValue<string>());
        Assert.Empty(result.Sources); Assert.Equal(1, result.Usage!.Fetches);
    }

    [Theory]
    [InlineData("http://127.0.0.1/private")]
    [InlineData("https://10.0.0.1/private")]
    [InlineData("https://secret.internal/private")]
    [InlineData("https://name:password@example.org/private")]
    [InlineData("file:///D:/private.txt")]
    [InlineData("javascript:alert(1)")]
    public async Task FetchRejectsPrivateCredentialedOrNonWebAddressesBeforeCredentials(string url)
    {
        using var fixture = new Fixture(PlaygroundProviderKind.DeepSeek);
        await Assert.ThrowsAsync<PlaygroundProviderException>(() => fixture.Provider.FetchAsync(FetchRequest(fixture, url), default));
        Assert.Empty(fixture.Handler.Requests); Assert.Equal(0, fixture.Credentials.Reads);
    }

    [Theory]
    [InlineData(PlaygroundProviderKind.Gemini)]
    [InlineData(PlaygroundProviderKind.OpenAI)]
    [InlineData(PlaygroundProviderKind.Grok)]
    public async Task FetchNeverFallsBackToAnotherProvider(PlaygroundProviderKind kind)
    {
        using var fixture = new Fixture(kind);
        var error = await Assert.ThrowsAsync<PlaygroundProviderException>(() => fixture.Provider.FetchAsync(FetchRequest(fixture, SourceUrl), default));
        Assert.Equal(PlaygroundFailureKind.Unsupported, error.Failure.Kind);
        Assert.Empty(fixture.Handler.Requests); Assert.Equal(0, fixture.Credentials.Reads);
    }

    [Theory]
    [InlineData("https://untrusted.example", "deepseek-flash")]
    [InlineData("https://api.deepseek.com", "unverified-model")]
    public async Task FetchRejectsForeignEndpointOrUnverifiedModelBeforeCredentials(string endpoint, string model)
    {
        using var fixture = new Fixture(PlaygroundProviderKind.DeepSeek);
        var request = FetchRequest(fixture, SourceUrl) with { Endpoint = endpoint, Model = model };
        await Assert.ThrowsAsync<PlaygroundProviderException>(() => fixture.Provider.FetchAsync(request, default));
        Assert.Empty(fixture.Handler.Requests); Assert.Equal(0, fixture.Credentials.Reads);
    }

    [Fact]
    public async Task FetchCancellationDoesNotDispatchOrRetry()
    {
        using var fixture = new Fixture(PlaygroundProviderKind.DeepSeek);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Provider.FetchAsync(FetchRequest(fixture, SourceUrl), cancellation.Token));
        Assert.Empty(fixture.Handler.Requests);
    }

    [Fact]
    public async Task FetchHttpFailureIsNotRetried()
    {
        using var fixture = new Fixture(PlaygroundProviderKind.DeepSeek, new HttpResponseMessage(HttpStatusCode.BadGateway) { Content = new StringContent("{}") });
        await Assert.ThrowsAsync<PlaygroundProviderException>(() => fixture.Provider.FetchAsync(FetchRequest(fixture, SourceUrl), default));
        Assert.Single(fixture.Handler.Requests);
    }

    private static JsonObject Citation(int start, int end) => new() { ["type"] = "url_citation", ["url"] = SourceUrl, ["title"] = "公式資料", ["start_index"] = start, ["end_index"] = end };
    private static JsonObject Text(string text, JsonObject? citation = null)
    { var part = new JsonObject { ["type"] = "text", ["text"] = text }; if (citation is not null) part["annotations"] = new JsonArray(citation); return part; }
    private static JsonObject Output(params JsonObject[] parts) => new() { ["type"] = "model_output", ["content"] = new JsonArray(parts.Cast<JsonNode?>().ToArray()) };
    private static JsonObject GoogleFinal(params JsonObject[] steps) => new() { ["status"] = "completed", ["steps"] = new JsonArray(steps.Cast<JsonNode?>().ToArray()) };
    private static JsonObject Delta(int index, JsonObject delta) => new() { ["index"] = index, ["delta"] = delta };
    private static string Event(string name, JsonObject json) { json["event_type"] = name; return "event: " + name + "\ndata: " + json.ToJsonString() + "\n\n"; }
    private static HttpResponseMessage Json(JsonObject value) => new(HttpStatusCode.OK) { Content = new StringContent(value.ToJsonString(), Encoding.UTF8, "application/json") };
    private static HttpResponseMessage Sse(string value)
    { var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new Fragmented(Encoding.UTF8.GetBytes(value))) }; response.Content.Headers.ContentType = new("text/event-stream"); return response; }
    private static string Model(PlaygroundProviderKind kind) => kind switch { PlaygroundProviderKind.Gemini => "gemini-3.8-flash", PlaygroundProviderKind.OpenAI => "gpt-6.1-sol", PlaygroundProviderKind.Grok => "grok-4.7", _ => "deepseek-flash" };
    private static PlaygroundProviderRequest Request(Fixture fixture) => new(Guid.NewGuid(), Guid.NewGuid(), fixture.Provider.Kind, fixture.Provider.DefaultEndpoint,
        Model(fixture.Provider.Kind), "Synthetic instructions", [new("user", "Synthetic public question")]) { Binding = "synthetic-turn" };
    private static PlaygroundPublicSearchRequest SearchRequest(Fixture fixture) => new(Guid.NewGuid(), Guid.NewGuid(), fixture.Provider.Kind, fixture.Provider.DefaultEndpoint, Model(fixture.Provider.Kind), "公開作品 公式", 0, 65536);
    private static PlaygroundPublicFetchRequest FetchRequest(Fixture fixture, string url) => new(Guid.NewGuid(), Guid.NewGuid(), fixture.Provider.Kind, fixture.Provider.DefaultEndpoint, Model(fixture.Provider.Kind), url);
    private static async Task<List<PlaygroundProviderEvent>> Collect(IPlaygroundProvider provider, PlaygroundProviderRequest request)
    { var events = new List<PlaygroundProviderEvent>(); await foreach (var item in provider.SendAsync(request, default)) events.Add(item); return events; }
    private sealed class Fixture : IDisposable
    {
        private readonly HttpClient client;
        public FixtureHandler Handler { get; }
        public FixtureCredentials Credentials { get; } = new();
        public IPlaygroundProvider Provider { get; }
        public Fixture(PlaygroundProviderKind kind, params HttpResponseMessage[] responses)
        { Handler = new(responses); client = new(Handler); Provider = new PlaygroundProviderRegistry(client, Credentials).Providers.Single(x => x.Kind == kind); }
        public void Dispose() => client.Dispose();
    }
    private sealed class FixtureCredentials : IPlaygroundCredentials
    {
        public int Reads;
        public Task<bool> HasAsync(PlaygroundProviderKind provider, string endpoint, CancellationToken cancellation) => Task.FromResult(true);
        public Task<string?> ReadAsync(PlaygroundProviderKind provider, string endpoint, CancellationToken cancellation)
        { cancellation.ThrowIfCancellationRequested(); Reads++; return Task.FromResult<string?>("synthetic-key-not-real"); }
        public Task SaveAsync(PlaygroundProviderKind provider, string endpoint, string key, CancellationToken cancellation) => throw new NotSupportedException();
        public Task ClearAsync(PlaygroundProviderKind provider, string endpoint, CancellationToken cancellation) => throw new NotSupportedException();
    }
    private sealed record Captured(Uri Uri, string Body);
    private sealed class FixtureHandler(params HttpResponseMessage[] responses) : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> remaining = new(responses);
        public List<Captured> Requests { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested(); Requests.Add(new(request.RequestUri!, await request.Content!.ReadAsStringAsync(cancellation)));
            if (!remaining.TryDequeue(out var response)) throw new InvalidOperationException("Unexpected request; this synthetic handler cannot access a network.");
            response.RequestMessage = request; return response;
        }
    }
    private sealed class Fragmented(byte[] bytes) : MemoryStream(bytes, false)
    { public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => base.ReadAsync(buffer[..Math.Min(3, buffer.Length)], cancellationToken); }
}
