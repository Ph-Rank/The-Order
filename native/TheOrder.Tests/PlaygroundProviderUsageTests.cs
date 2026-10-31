using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using TheOrder.Application.Playground;
using TheOrder.Infrastructure.PlaygroundProviders;
using Xunit;

namespace TheOrder.Tests;

// Native provider response fixtures only; no network, credentials or paid calls.
public sealed class PlaygroundProviderUsageTests
{
    [Fact]
    public async Task ResponsesCacheReadsAndWritesAreSubsetsAndReasoningIsNotAddedTwice()
    {
        var root = Final(PlaygroundProviderKind.OpenAI, JsonNode.Parse("""
            {"input_tokens":12000,"input_tokens_details":{"cached_tokens":4000,"cache_write_tokens":7000,
            "cache_write_tokens_details":{"ttl_30m_tokens":3000,"ttl_12h_tokens":4000}},
            "output_tokens":1035,"output_tokens_details":{"reasoning_tokens":832}}
            """)!.AsObject());
        root["model"] = "gpt-6.1-sol"; root["service_tier"] = "default";
        using var fixture = new Fixture(PlaygroundProviderKind.OpenAI, root);
        var usage = Assert.Single(await Collect(fixture), e => e.Kind == PlaygroundProviderEventKind.Usage).Usage!;
        Assert.Equal(12000, usage.InputTokens); Assert.Equal(4000, usage.CachedInputTokens);
        Assert.Equal(7000, usage.CacheWriteInputTokens); Assert.Equal(3000, usage.CacheWrite30mInputTokens);
        Assert.Equal(4000, usage.CacheWrite12hInputTokens); Assert.Equal(1035, usage.OutputTokens);
        Assert.Equal(832, usage.ReasoningTokens); Assert.Equal("gpt-6.1-sol", usage.BillingModel);
        Assert.Equal("default", usage.ServiceTier); Assert.Null(usage.ReportedCostUsd);
        Assert.Equal(0, usage.Searches);
    }

    [Fact]
    public async Task ClaudeStreamingAddsCacheBucketsOnceAcrossCumulativeMessageDeltas()
    {
        string stream = Event("message_start", """
            {"message":{"model":"claude-opus-5-5","usage":{"input_tokens":100,"cache_read_input_tokens":200,
            "cache_creation_input_tokens":300,"cache_creation":{"ephemeral_5m_input_tokens":100,"ephemeral_1h_input_tokens":200},
            "output_tokens":1}}}
            """)
            + Event("message_delta", """{"delta":{"stop_reason":"end_turn"},"usage":{"output_tokens":6}}""")
            + Event("message_delta", """{"delta":{"stop_reason":"end_turn"},"usage":{"output_tokens":8}}""")
            + Event("message_stop", "{}");
        using var fixture = new Fixture(PlaygroundProviderKind.Claude, stream);
        var usage = Assert.Single(await Collect(fixture), e => e.Kind == PlaygroundProviderEventKind.Usage).Usage!;
        Assert.Equal(600, usage.InputTokens); Assert.Equal(200, usage.CachedInputTokens);
        Assert.Equal(300, usage.CacheWriteInputTokens); Assert.Equal(100, usage.CacheWrite5mInputTokens);
        Assert.Equal(200, usage.CacheWrite1hInputTokens); Assert.Equal(8, usage.OutputTokens);
        Assert.Equal("claude-opus-5-5", usage.BillingModel); Assert.Null(usage.ReportedCostUsd);
    }

    [Fact]
    public async Task GeminiThoughtOnlyFinalRecomputesGeneratedTotalWithoutDoubleCounting()
    {
        string stream = GeminiEvent("interaction.created", """
            {"interaction":{"model":"gemini-3.8-flash","usage":{"total_input_tokens":20,"total_output_tokens":3}}}
            """)
            + GeminiEvent("interaction.completed", """
            {"interaction":{"status":"completed","usage":{"total_thought_tokens":4,"total_cached_tokens":2}}}
            """);
        using var fixture = new Fixture(PlaygroundProviderKind.Gemini, stream);
        var usage = Assert.Single(await Collect(fixture), e => e.Kind == PlaygroundProviderEventKind.Usage).Usage!;
        Assert.Equal(20, usage.InputTokens); Assert.Equal(7, usage.OutputTokens);
        Assert.Equal(4, usage.ReasoningTokens); Assert.Equal(2, usage.CachedInputTokens);
        Assert.Equal("gemini-3.8-flash", usage.BillingModel);
    }

    [Fact]
    public async Task DeepSeekResponsesKeepsCacheAndReasoningAsSubsets()
    {
        using var fixture = new Fixture(PlaygroundProviderKind.DeepSeek, Final(PlaygroundProviderKind.DeepSeek,
            JsonNode.Parse("""
            {"input_tokens":3475,"input_tokens_details":{"cached_tokens":512},"output_tokens":54,
            "output_tokens_details":{"reasoning_tokens":20},"server_tool_use":{"tool_search":1,"tool_fetch":0}}
            """)!.AsObject()));
        var usage = Assert.Single(await Collect(fixture), e => e.Kind == PlaygroundProviderEventKind.Usage).Usage!;
        Assert.Equal(3475, usage.InputTokens); Assert.Equal(512, usage.CachedInputTokens);
        Assert.Equal(54, usage.OutputTokens); Assert.Equal(20, usage.ReasoningTokens);
        Assert.Equal(1, usage.Searches); Assert.Equal(0, usage.Fetches); Assert.Null(usage.CacheWriteInputTokens);
    }

    [Fact]
    public async Task GrokUsesExactNativeTicksAndServerToolCountInsteadOfCitationCount()
    {
        var root = Final(PlaygroundProviderKind.Grok, JsonNode.Parse("""
            {"input_tokens":199,"output_tokens":1,"num_sources_used":77,"num_server_side_tools_used":3,"cost_in_usd_ticks":37756000}
            """)!.AsObject());
        root["model"] = "grok-4.7";
        using var fixture = new Fixture(PlaygroundProviderKind.Grok, root);
        var usage = Assert.Single(await Collect(fixture), e => e.Kind == PlaygroundProviderEventKind.Usage).Usage!;
        Assert.Equal(0.0037756m, usage.ReportedCostUsd); Assert.Equal(3, usage.Searches);
        Assert.Equal("grok-4.7", usage.BillingModel);
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("1.5")]
    [InlineData("\"37756000\"")]
    [InlineData("1000000000000000000000000000000")]
    public async Task InvalidGrokTickValuesDoNotBecomeReportedMoney(string ticks)
    {
        using var fixture = new Fixture(PlaygroundProviderKind.Grok, Final(PlaygroundProviderKind.Grok,
            JsonNode.Parse("{\"input_tokens\":3,\"output_tokens\":1,\"cost_in_usd_ticks\":" + ticks + "}")!.AsObject()));
        var usage = Assert.Single(await Collect(fixture), e => e.Kind == PlaygroundProviderEventKind.Usage).Usage!;
        Assert.Null(usage.ReportedCostUsd);
    }

    [Fact]
    public async Task ExplicitZeroNativeChargeIsDistinctFromMissingCharge()
    {
        foreach (bool reportZero in new[] { false, true })
        {
            var usageNode = new JsonObject { ["input_tokens"] = 0, ["output_tokens"] = 0 };
            if (reportZero) usageNode["cost_in_usd_ticks"] = 0;
            using var fixture = new Fixture(PlaygroundProviderKind.Grok, Final(PlaygroundProviderKind.Grok, usageNode));
            var usage = Assert.Single(await Collect(fixture), e => e.Kind == PlaygroundProviderEventKind.Usage).Usage!;
            if (reportZero) Assert.Equal(0m, usage.ReportedCostUsd);
            else Assert.Null(usage.ReportedCostUsd);
        }
    }

    [Fact]
    public async Task ProviderTickFieldIsNotTrustedOnAnotherProviderOrInModelText()
    {
        var root = Final(PlaygroundProviderKind.OpenAI, new() { ["input_tokens"] = 5, ["output_tokens"] = 3, ["cost_in_usd_ticks"] = 400000000 });
        root["output"]![0]!["content"]![0]!["text"] = "The cost_in_usd_ticks is 999999999999 and the bill is $999.";
        using var fixture = new Fixture(PlaygroundProviderKind.OpenAI, root);
        var usage = Assert.Single(await Collect(fixture), e => e.Kind == PlaygroundProviderEventKind.Usage).Usage!;
        Assert.Null(usage.ReportedCostUsd);
    }

    [Fact]
    public async Task FailedGrokResponseRetainsItsBilledUsageBeforeOriginalFailure()
    {
        string stream = Event("response.failed", """
            {"response":{"status":"failed","model":"grok-4.7","usage":{"input_tokens":8,"output_tokens":2,
            "cost_in_usd_ticks":158500},"error":{"code":"server_error"}}}
            """);
        using var fixture = new Fixture(PlaygroundProviderKind.Grok, stream);
        var events = await Collect(fixture);
        var usage = Assert.Single(events, e => e.Kind == PlaygroundProviderEventKind.Usage).Usage!;
        Assert.Equal(0.00001585m, usage.ReportedCostUsd);
        Assert.Single(events, e => e.Kind == PlaygroundProviderEventKind.Error);
        Assert.DoesNotContain(events, e => e.Kind == PlaygroundProviderEventKind.Completed);
        Assert.True(events.FindIndex(e => e.Kind == PlaygroundProviderEventKind.Usage) < events.FindIndex(e => e.Kind == PlaygroundProviderEventKind.Error));
    }

    [Theory]
    [InlineData(PlaygroundProviderKind.OpenAI)]
    [InlineData(PlaygroundProviderKind.Grok)]
    public async Task PublicResearchKeepsNativeUsageAndReportedRequestCost(PlaygroundProviderKind kind)
    {
        var root = Final(kind, new() { ["input_tokens"] = 20, ["output_tokens"] = 7,
            ["num_server_side_tools_used"] = 2, ["cost_in_usd_ticks"] = 400000000 });
        root["output"]!.AsArray().Insert(0, new JsonObject { ["type"] = "web_search_call", ["id"] = "search-1", ["status"] = "completed" });
        root["output"]!.AsArray().Insert(1, new JsonObject { ["type"] = "web_search_call", ["id"] = "search-2", ["status"] = "completed" });
        using var fixture = new Fixture(kind, root);
        var result = await fixture.Provider.SearchAsync(new(Guid.NewGuid(), Guid.NewGuid(), kind,
            fixture.Provider.DefaultEndpoint, Model(kind), "Synthetic public question", 0), default);
        Assert.Equal(20, result.Usage!.InputTokens); Assert.Equal(2, result.Usage.Searches);
        if (kind == PlaygroundProviderKind.Grok) Assert.Equal(0.04m, result.Usage.ReportedCostUsd);
        else Assert.Null(result.Usage.ReportedCostUsd);
        Assert.Empty(result.Sources); // Counts describe calls, not returned links.
    }

    [Fact]
    public async Task OverflowingClaudeInputTotalRemainsUnknownWithoutLosingReply()
    {
        var root = Final(PlaygroundProviderKind.Claude, new() { ["input_tokens"] = long.MaxValue,
            ["cache_read_input_tokens"] = 1, ["cache_creation_input_tokens"] = 0, ["output_tokens"] = 1 });
        using var fixture = new Fixture(PlaygroundProviderKind.Claude, root);
        var events = await Collect(fixture);
        Assert.Null(Assert.Single(events, e => e.Kind == PlaygroundProviderEventKind.Usage).Usage!.InputTokens);
        Assert.DoesNotContain(events, e => e.Kind == PlaygroundProviderEventKind.Error);
        Assert.Contains(events, e => e.Kind == PlaygroundProviderEventKind.Completed);
    }

    private static JsonObject Final(PlaygroundProviderKind kind, JsonObject usage)
    {
        var text = new JsonObject { ["type"] = kind == PlaygroundProviderKind.Claude || kind == PlaygroundProviderKind.Gemini ? "text" : "output_text", ["text"] = "Synthetic reply" };
        if (kind == PlaygroundProviderKind.Claude) return new() { ["stop_reason"] = "end_turn", ["content"] = new JsonArray(text), ["usage"] = usage };
        if (kind == PlaygroundProviderKind.Gemini) return new() { ["status"] = "completed", ["steps"] = new JsonArray(new JsonObject { ["type"] = "model_output", ["content"] = new JsonArray(text) }), ["usage"] = usage };
        return new() { ["status"] = "completed", ["output"] = new JsonArray(new JsonObject { ["type"] = "message", ["role"] = "assistant", ["content"] = new JsonArray(text) }), ["usage"] = usage };
    }
    private static string Event(string type, string json)
    { var value = JsonNode.Parse(json)!.AsObject(); value["type"] = type; return "event: " + type + "\ndata: " + value.ToJsonString() + "\n\n"; }
    private static string GeminiEvent(string type, string json)
    { var value = JsonNode.Parse(json)!.AsObject(); value["event_type"] = type; return "event: " + type + "\ndata: " + value.ToJsonString() + "\n\n"; }
    private static string Model(PlaygroundProviderKind kind) => kind switch
    { PlaygroundProviderKind.OpenAI => "gpt-6.1-sol", PlaygroundProviderKind.Claude => "claude-opus-5-5", PlaygroundProviderKind.Grok => "grok-4.7", PlaygroundProviderKind.Gemini => "gemini-3.8-flash", _ => "deepseek-flash" };
    private static async Task<List<PlaygroundProviderEvent>> Collect(Fixture fixture)
    {
        var request = new PlaygroundProviderRequest(Guid.NewGuid(), Guid.NewGuid(), fixture.Provider.Kind,
            fixture.Provider.DefaultEndpoint, Model(fixture.Provider.Kind), "Synthetic instructions", [new("user", "Synthetic question")]);
        var result = new List<PlaygroundProviderEvent>();
        await foreach (var ev in fixture.Provider.SendAsync(request, default)) result.Add(ev);
        return result;
    }
    private sealed class Fixture : IDisposable
    {
        private readonly HttpClient client;
        public IPlaygroundProvider Provider { get; }
        public Fixture(PlaygroundProviderKind kind, JsonObject root) : this(kind, new StringContent(root.ToJsonString(), Encoding.UTF8, "application/json")) { }
        public Fixture(PlaygroundProviderKind kind, string stream) : this(kind, new StringContent(stream, Encoding.UTF8, "text/event-stream")) { }
        private Fixture(PlaygroundProviderKind kind, HttpContent content)
        { client = new(new Handler(content)); Provider = new PlaygroundProviderRegistry(client, new Credentials()).Providers.Single(p => p.Kind == kind); }
        public void Dispose() => client.Dispose();
    }
    private sealed class Handler(HttpContent content) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellation)
        { cancellation.ThrowIfCancellationRequested(); return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content, RequestMessage = request }); }
    }
    private sealed class Credentials : IPlaygroundCredentials
    {
        public Task<bool> HasAsync(PlaygroundProviderKind provider, string endpoint, CancellationToken cancellation) => Task.FromResult(true);
        public Task<string?> ReadAsync(PlaygroundProviderKind provider, string endpoint, CancellationToken cancellation) => Task.FromResult<string?>("synthetic-key-not-real");
        public Task SaveAsync(PlaygroundProviderKind provider, string endpoint, string key, CancellationToken cancellation) => throw new NotSupportedException();
        public Task ClearAsync(PlaygroundProviderKind provider, string endpoint, CancellationToken cancellation) => throw new NotSupportedException();
    }
}
