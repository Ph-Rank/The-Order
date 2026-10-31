using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using TheOrder.Application.Playground;
using TheOrder.Infrastructure.PlaygroundProviders;
using Xunit;

namespace TheOrder.Tests;

// Native request encoding against an in-memory HTTP handler only. No real
// credential store, DNS, external service, or paid request is involved.
public sealed class PlaygroundProviderControlsTests
{
    private const string CustomInstructions = "[synthetic host policy]\n[synthetic editable system] 用中文，先讨论配乐。";
    private static readonly PlaygroundToolDefinition LocalTool = new("local_read", "Synthetic read-only local tool",
        """{"type":"object","properties":{},"additionalProperties":false}""");

    public static IEnumerable<object[]> VerifiedModelEfforts()
    {
        (PlaygroundProviderKind Kind, string[] Models)[] families =
        [
            (PlaygroundProviderKind.OpenAI, ["gpt-6-astra", "gpt-6.1-sol", "gpt-6.1-terra", "gpt-6-luna", "gpt-5.4", "gpt-5.5"]),
            (PlaygroundProviderKind.Gemini, ["gemini-4-argon", "gemini-3.8-flash", "gemini-3.6-flash"]),
            (PlaygroundProviderKind.Claude, ["claude-opus-5-5", "claude-opus-5", "claude-opus-4-6", "claude-sonnet-4-6"]),
            (PlaygroundProviderKind.Grok, ["grok-4.7"]),
            (PlaygroundProviderKind.DeepSeek, ["deepseek-flash", "deepseek-v4-pro"])
        ];
        foreach (var family in families)
        foreach (string model in family.Models)
        foreach (string effort in new[] { "low", "medium", "high" }) yield return [family.Kind, model, effort];
    }

    [Theory]
    [MemberData(nameof(VerifiedModelEfforts))]
    public async Task EachVerifiedModelSendsTheSelectedLevelAndEditableSystemWithoutImplicitSearch(
        PlaygroundProviderKind kind, string model, string effort)
    {
        using var fixture = new Fixture(kind, Final(kind));
        var request = Request(fixture) with { Model = model, ReasoningEffort = effort, Instructions = CustomInstructions };
        Assert.Contains(effort, fixture.Provider.Capabilities(model, request.Endpoint).ReasoningEfforts);
        var events = await Collect(fixture.Provider, request);
        Assert.DoesNotContain(events, e => e.Kind == PlaygroundProviderEventKind.Error);
        Assert.Single(events, e => e.Kind == PlaygroundProviderEventKind.Completed);
        var sent = Assert.Single(fixture.Handler.Bodies);
        Assert.Equal(effort, Effort(sent, kind));
        Assert.Equal(CustomInstructions, System(sent, kind));
        Assert.Null(sent["tools"]); Assert.Null(sent["tool_choice"]);
        if (kind == PlaygroundProviderKind.Claude)
        { Assert.Equal("adaptive", sent["thinking"]!["type"]!.GetValue<string>()); Assert.Null(sent["thinking"]!["budget_tokens"]); }
        if (kind == PlaygroundProviderKind.DeepSeek && effort == "medium")
        {
            // This is the documented compatibility alias: it does not mean
            // DeepSeek has a distinct middle tier, or silently selects max.
            Assert.Contains("实际与高档相同", fixture.Provider.Capabilities(model, request.Endpoint).Notice);
            Assert.NotEqual("max", Effort(sent, kind));
        }
    }

    [Theory]
    [InlineData(PlaygroundProviderKind.OpenAI)]
    [InlineData(PlaygroundProviderKind.Gemini)]
    [InlineData(PlaygroundProviderKind.Claude)]
    [InlineData(PlaygroundProviderKind.Grok)]
    [InlineData(PlaygroundProviderKind.DeepSeek)]
    public async Task LocalToolPermissionNeverAddsAHostedSearchTool(PlaygroundProviderKind kind)
    {
        using var fixture = new Fixture(kind, Final(kind));
        var request = Request(fixture) with { Tools = [LocalTool], ReasoningEffort = "low" };
        Assert.Single(await Collect(fixture.Provider, request), e => e.Kind == PlaygroundProviderEventKind.Completed);
        var sent = Assert.Single(fixture.Handler.Bodies);
        var tool = Assert.Single(sent["tools"]!.AsArray())!;
        Assert.Equal(LocalTool.Name, tool["name"]!.GetValue<string>());
        if (kind != PlaygroundProviderKind.Claude) Assert.Equal("function", tool["type"]!.GetValue<string>());
        else Assert.Null(tool["type"]);
        Assert.Null(sent["tool_choice"]);
        Assert.DoesNotContain("google_search", sent.ToJsonString());
        Assert.DoesNotContain("web_search", sent.ToJsonString());
    }

    [Theory]
    [InlineData(PlaygroundProviderKind.OpenAI)]
    [InlineData(PlaygroundProviderKind.Gemini)]
    [InlineData(PlaygroundProviderKind.Claude)]
    [InlineData(PlaygroundProviderKind.Grok)]
    [InlineData(PlaygroundProviderKind.DeepSeek)]
    public async Task ModelEnumerationDoesNotGrantUnknownModelsAdvancedParametersOrTools(PlaygroundProviderKind kind)
    {
        using var fixture = new Fixture(kind, Final(kind));
        var request = Request(fixture) with { Model = "future-unverified-model", Tools = [LocalTool], ReasoningEffort = "medium" };
        Assert.False(fixture.Provider.Capabilities(request.Model, request.Endpoint).Verified);
        Assert.Single(await Collect(fixture.Provider, request), e => e.Kind == PlaygroundProviderEventKind.Completed);
        var sent = Assert.Single(fixture.Handler.Bodies);
        Assert.Null(sent["tools"]); Assert.Null(Effort(sent, kind));
        Assert.Null(sent["thinking"]); Assert.Equal(CustomInstructions, System(sent, kind));
    }

    [Theory]
    [InlineData(PlaygroundProviderKind.OpenAI)]
    [InlineData(PlaygroundProviderKind.Gemini)]
    [InlineData(PlaygroundProviderKind.Claude)]
    [InlineData(PlaygroundProviderKind.Grok)]
    [InlineData(PlaygroundProviderKind.DeepSeek)]
    public async Task EveryToolContinuationUsesTheCurrentSystemInstructions(PlaygroundProviderKind kind)
    {
        using var fixture = new Fixture(kind, Final(kind, tool: true), Final(kind));
        var request = Request(fixture) with { Tools = [LocalTool], ReasoningEffort = "low" };
        var first = await Collect(fixture.Provider, request);
        var continuation = Assert.Single(first, e => e.Kind == PlaygroundProviderEventKind.Continuation).Continuation!;
        var call = Assert.Single(first, e => e.Kind == PlaygroundProviderEventKind.ToolCall).ToolCall!;
        const string changed = "[synthetic host policy]\n[edited system] 简短回答。";
        var resumed = await Collect(fixture.Provider, request with
        {
            Instructions = changed, Continuation = continuation,
            ToolResults = [new("tool", "synthetic result", call.Id, call.Name)]
        });
        Assert.DoesNotContain(resumed, e => e.Kind == PlaygroundProviderEventKind.Error);
        Assert.Single(resumed, e => e.Kind == PlaygroundProviderEventKind.Completed);
        Assert.Equal(2, fixture.Handler.Bodies.Count);
        Assert.Equal(CustomInstructions, System(fixture.Handler.Bodies[0], kind));
        Assert.Equal(changed, System(fixture.Handler.Bodies[1], kind));
        Assert.DoesNotContain("[synthetic editable system]", fixture.Handler.Bodies[1].ToJsonString());
    }

    public static IEnumerable<object[]> HostedResearchEfforts()
    {
        foreach (var kind in new[] { PlaygroundProviderKind.OpenAI, PlaygroundProviderKind.Gemini, PlaygroundProviderKind.Claude, PlaygroundProviderKind.Grok })
        foreach (string effort in new[] { "low", "medium", "high" }) yield return [kind, effort];
    }

    [Theory]
    [MemberData(nameof(HostedResearchEfforts))]
    public async Task SeparateHostedResearchFollowsTheSelectedEffortWithoutPrivateInstructions(
        PlaygroundProviderKind kind, string effort)
    {
        using var fixture = new Fixture(kind, Final(kind, research: true));
        var request = new PlaygroundPublicSearchRequest(Guid.NewGuid(), Guid.NewGuid(), kind,
            fixture.Provider.DefaultEndpoint, Model(kind), "《合成作品》公开发售时间") { ReasoningEffort = effort };
        var result = await fixture.Provider.SearchAsync(request, default);
        Assert.Equal(kind == PlaygroundProviderKind.Gemini ? PlaygroundRetention.SessionOnly : PlaygroundRetention.Portable, result.Retention);
        var sent = Assert.Single(fixture.Handler.Bodies);
        Assert.Equal(effort, Effort(sent, kind));
        Assert.Single(sent["tools"]!.AsArray()); Assert.False(sent["stream"]!.GetValue<bool>());
        Assert.DoesNotContain("[synthetic editable system]", sent.ToJsonString());
        Assert.DoesNotContain("synthetic private discussion", sent.ToJsonString());
        Assert.DoesNotContain(LocalTool.Name, sent.ToJsonString());
        Assert.Contains("Do not access personal data", System(sent, kind));
        Assert.Null(sent["max_tool_calls"]); Assert.Null(sent["previous_response_id"]);
        if (kind == PlaygroundProviderKind.Gemini) Assert.False(sent["store"]!.GetValue<bool>());
    }

    [Theory]
    [InlineData("low")]
    [InlineData("medium")]
    [InlineData("high")]
    public async Task DeepSeekSearchRemainsAQueryOnlyOfficialSearchRequest(string effort)
    {
        using var fixture = new Fixture(PlaygroundProviderKind.DeepSeek, new JsonObject { ["results"] = new JsonArray() });
        _ = await fixture.Provider.SearchAsync(new(Guid.NewGuid(), Guid.NewGuid(), PlaygroundProviderKind.DeepSeek,
            fixture.Provider.DefaultEndpoint, Model(PlaygroundProviderKind.DeepSeek), "《合成作品》公开时间") { ReasoningEffort = effort }, default);
        var sent = Assert.Single(fixture.Handler.Bodies);
        Assert.Equal("/v1/tools/search", Assert.Single(fixture.Handler.Paths));
        Assert.Equal(3, sent.Count); Assert.Equal("《合成作品》公开时间", sent["query"]!.GetValue<string>());
        Assert.Equal(10, sent["count"]!.GetValue<int>()); Assert.False(sent["need_content"]!.GetValue<bool>());
        Assert.Null(sent["instructions"]); Assert.Null(sent["reasoning"]);
    }

    private static PlaygroundProviderRequest Request(Fixture fixture) => new(Guid.NewGuid(), Guid.NewGuid(), fixture.Kind,
        fixture.Provider.DefaultEndpoint, Model(fixture.Kind), CustomInstructions, [new("user", "synthetic private discussion")])
        { Phase = PlaygroundPhase.Discussion, Binding = "synthetic-turn" };
    private static string Model(PlaygroundProviderKind kind) => kind switch
    { PlaygroundProviderKind.OpenAI => "gpt-6.1-sol", PlaygroundProviderKind.Gemini => "gemini-4-argon", PlaygroundProviderKind.Claude => "claude-opus-5-5", PlaygroundProviderKind.Grok => "grok-4.7", _ => "deepseek-flash" };
    private static string? Effort(JsonNode body, PlaygroundProviderKind kind) => (kind == PlaygroundProviderKind.Gemini
        ? body["generation_config"]?["thinking_level"] : kind == PlaygroundProviderKind.Claude ? body["output_config"]?["effort"] : body["reasoning"]?["effort"])?.GetValue<string>();
    private static string? System(JsonNode body, PlaygroundProviderKind kind) => body[kind == PlaygroundProviderKind.Gemini ? "system_instruction" : kind == PlaygroundProviderKind.Claude ? "system" : "instructions"]?.GetValue<string>();
    private static async Task<List<PlaygroundProviderEvent>> Collect(IPlaygroundProvider provider, PlaygroundProviderRequest request)
    { var result = new List<PlaygroundProviderEvent>(); await foreach (var value in provider.SendAsync(request, default)) result.Add(value); return result; }

    private static JsonObject Final(PlaygroundProviderKind kind, bool tool = false, bool research = false)
    {
        const string url = "https://example.org/synthetic-official";
        if (kind == PlaygroundProviderKind.Gemini)
        {
            var steps = new JsonArray();
            if (tool) steps.Add(new JsonObject { ["type"] = "function_call", ["id"] = "call-1", ["name"] = LocalTool.Name, ["arguments"] = new JsonObject(), ["signature"] = "synthetic-signature" });
            if (research)
            {
                steps.Add(new JsonObject { ["type"] = "google_search_call", ["id"] = "search-1", ["arguments"] = new JsonObject { ["queries"] = new JsonArray("synthetic public release") } });
                steps.Add(new JsonObject { ["type"] = "google_search_result", ["call_id"] = "search-1", ["result"] = new JsonArray(new JsonObject { ["url"] = url, ["title"] = "Synthetic official source" }) });
            }
            if (!tool) steps.Add(new JsonObject { ["type"] = "model_output", ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = "synthetic answer" }) });
            return new() { ["status"] = tool ? "requires_action" : "completed", ["steps"] = steps };
        }
        if (kind == PlaygroundProviderKind.Claude)
        {
            var content = new JsonArray();
            if (tool) content.Add(new JsonObject { ["type"] = "tool_use", ["id"] = "call-1", ["name"] = LocalTool.Name, ["input"] = new JsonObject() });
            if (research)
            {
                content.Add(new JsonObject { ["type"] = "server_tool_use", ["id"] = "search-1", ["name"] = "web_search", ["input"] = new JsonObject { ["query"] = "synthetic public release" } });
                content.Add(new JsonObject { ["type"] = "web_search_tool_result", ["tool_use_id"] = "search-1", ["content"] = new JsonArray(new JsonObject { ["type"] = "web_search_result", ["url"] = url, ["title"] = "Synthetic official source" }) });
            }
            if (!tool) content.Add(new JsonObject { ["type"] = "text", ["text"] = "synthetic answer" });
            return new() { ["stop_reason"] = tool ? "tool_use" : "end_turn", ["content"] = content };
        }
        var output = new JsonArray();
        if (tool) output.Add(new JsonObject { ["type"] = "function_call", ["id"] = "item-1", ["call_id"] = "call-1", ["name"] = LocalTool.Name, ["arguments"] = "{}" });
        if (research) output.Add(new JsonObject { ["type"] = "web_search_call", ["id"] = "search-1", ["status"] = "completed", ["action"] = new JsonObject { ["type"] = "search", ["queries"] = new JsonArray("synthetic public release") } });
        if (!tool) output.Add(new JsonObject { ["type"] = "message", ["role"] = "assistant", ["content"] = new JsonArray(new JsonObject { ["type"] = "output_text", ["text"] = "synthetic answer" }) });
        return new() { ["status"] = "completed", ["output"] = output };
    }

    private sealed class Fixture : IDisposable
    {
        private readonly HttpClient client;
        public PlaygroundProviderKind Kind { get; }
        public CaptureHandler Handler { get; }
        public IPlaygroundProvider Provider { get; }
        public Fixture(PlaygroundProviderKind kind, params JsonObject[] responses)
        {
            Kind = kind; Handler = new(responses); client = new(Handler) { Timeout = Timeout.InfiniteTimeSpan };
            Provider = new PlaygroundProviderRegistry(client, new Credentials()).Providers.Single(p => p.Kind == kind);
        }
        public void Dispose() => client.Dispose();
    }
    private sealed class CaptureHandler(JsonObject[] responses) : HttpMessageHandler
    {
        private readonly Queue<JsonObject> pending = new(responses);
        public List<JsonObject> Bodies { get; } = [];
        public List<string> Paths { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Paths.Add(request.RequestUri!.AbsolutePath);
            Bodies.Add(JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken))!.AsObject());
            if (!pending.TryDequeue(out var value)) throw new InvalidOperationException("Unexpected synthetic request; no network handler exists.");
            return new(HttpStatusCode.OK) { RequestMessage = request, Content = new StringContent(value.ToJsonString(), Encoding.UTF8, "application/json") };
        }
    }
    private sealed class Credentials : IPlaygroundCredentials
    {
        public Task<bool> HasAsync(PlaygroundProviderKind provider, string endpoint, CancellationToken cancellation) => Task.FromResult(true);
        public Task<string?> ReadAsync(PlaygroundProviderKind provider, string endpoint, CancellationToken cancellation) => Task.FromResult<string?>("synthetic-controls-fixture-key");
        public Task SaveAsync(PlaygroundProviderKind provider, string endpoint, string key, CancellationToken cancellation) => throw new NotSupportedException();
        public Task ClearAsync(PlaygroundProviderKind provider, string endpoint, CancellationToken cancellation) => throw new NotSupportedException();
    }
}
