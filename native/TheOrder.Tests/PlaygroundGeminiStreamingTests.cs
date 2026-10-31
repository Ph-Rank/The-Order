using System.Collections.Immutable;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using TheOrder.Application.Playground;
using TheOrder.Infrastructure.PlaygroundProviders;
using Xunit;

namespace TheOrder.Tests;

// Schema matches the bounded live synthetic red/blue-image stream captured on
// 2026-10-30. Thought signatures are synthetic; no credential or private input.
public sealed class PlaygroundGeminiStreamingTests
{
    [Fact]
    public async Task GaTypedCompletionWithoutStepsPreservesTextAndNestedUsage()
    {
        using var fixture = new Fixture(TextSteps + Terminal("completed", """{"total_input_tokens":1919,"total_output_tokens":7,"total_thought_tokens":181,"total_cached_tokens":0,"total_tokens":2107}""") + Done);
        var events = await Collect(fixture.Provider, Request() with { ConversationBinding = "complete-usage-chain" });
        Assert.DoesNotContain(events, e => e.Kind == PlaygroundProviderEventKind.Error);
        Assert.Equal("左：红色；右：蓝色", string.Concat(events.Where(e => e.Kind == PlaygroundProviderEventKind.Text).Select(e => e.Text)));
        Assert.Equal("end_turn", Assert.Single(events, e => e.Kind == PlaygroundProviderEventKind.Completed).StopReason);
        var usage = Assert.Single(events, e => e.Kind == PlaygroundProviderEventKind.Usage).Usage!;
        Assert.Equal(1919, usage.InputTokens); Assert.Equal(188, usage.OutputTokens); Assert.Equal(181, usage.ReasoningTokens); Assert.Equal(0, usage.CachedInputTokens);
        Assert.Equal(2107, usage.InputTokens + usage.OutputTokens);
    }

    [Theory]
    [InlineData("")]
    [InlineData("event: done\ndata: [DONE]\n\n")]
    [InlineData("event: interaction.completed\ndata: {\"event_type\":\"interaction.completed\",\"interaction\":{\"status\":\"completed\"}}\n")]
    public async Task EofDoneOrUnterminatedTerminalNeverClaimsCompletion(string suffix)
    {
        using var fixture = new Fixture(TextSteps + suffix);
        var events = await Collect(fixture.Provider, Request() with { ConversationBinding = "truncated-chain" });
        Assert.Single(events, e => e.Kind == PlaygroundProviderEventKind.Error);
        Assert.DoesNotContain(events, e => e.Kind is PlaygroundProviderEventKind.Completed or PlaygroundProviderEventKind.ToolCall or PlaygroundProviderEventKind.Continuation or PlaygroundProviderEventKind.ConversationTape);
    }

    [Theory]
    [InlineData("failed", PlaygroundFailureKind.InvalidResponse)]
    [InlineData("cancelled", PlaygroundFailureKind.Cancelled)]
    [InlineData("requires_continuation", PlaygroundFailureKind.InvalidResponse)]
    [InlineData("in_progress", PlaygroundFailureKind.InvalidResponse)]
    public async Task TypedEventStillRequiresARealSuccessfulStatus(string status, PlaygroundFailureKind expected)
    {
        using var fixture = new Fixture(TextSteps + Terminal(status) + Done);
        var events = await Collect(fixture.Provider, Request());
        Assert.Equal(expected, Assert.Single(events, e => e.Kind == PlaygroundProviderEventKind.Error).Failure!.Kind);
        Assert.DoesNotContain(events, e => e.Kind == PlaygroundProviderEventKind.Completed);
    }

    [Fact]
    public async Task GaIncompleteKeepsTypedTruncationAndDoesNotReleaseTools()
    {
        using var fixture = new Fixture(ToolSteps(closed: false) + Terminal("incomplete", """{"total_input_tokens":12,"total_output_tokens":0,"total_thought_tokens":9}""") + Done);
        var events = await Collect(fixture.Provider, Request() with { ConversationBinding = "token-limited-chain" });
        Assert.Equal("max_tokens", Assert.Single(events, e => e.Kind == PlaygroundProviderEventKind.Completed).StopReason);
        Assert.DoesNotContain(events, e => e.Kind is PlaygroundProviderEventKind.ToolCall or PlaygroundProviderEventKind.Continuation or PlaygroundProviderEventKind.ConversationTape);
        Assert.Equal(9, Assert.Single(events, e => e.Kind == PlaygroundProviderEventKind.Usage).Usage!.OutputTokens);
    }

    [Fact]
    public async Task GaRequiresActionPreservesThoughtSignatureAndResumesTheClosedToolStep()
    {
        using var fixture = new Fixture(ToolSteps(closed: true) + Terminal("requires_action") + Done,
            TextSteps + Terminal("completed") + Done);
        var request = Request(); var first = await Collect(fixture.Provider, request);
        var call = Assert.Single(first, e => e.Kind == PlaygroundProviderEventKind.ToolCall).ToolCall!;
        Assert.Equal("lookup", call.Name); Assert.Equal("{\"n\":1}", JsonNode.Parse(call.ArgumentsJson)!.ToJsonString());
        var continuation = Assert.Single(first, e => e.Kind == PlaygroundProviderEventKind.Continuation).Continuation!;
        var resumed = await Collect(fixture.Provider, request with { Continuation = continuation, ToolResults = [new("tool", "synthetic result", call.Id, call.Name)] });
        Assert.DoesNotContain(resumed, e => e.Kind == PlaygroundProviderEventKind.Error);
        Assert.Equal(2, fixture.Bodies.Count);
        Assert.Contains("synthetic-thought-signature", fixture.Bodies[1]);
        Assert.Contains("synthetic-function-signature", fixture.Bodies[1]);
        Assert.Contains("synthetic result", fixture.Bodies[1]);
    }

    [Theory]
    [InlineData("requires_action")]
    [InlineData("completed")]
    public async Task TypedCompletionDoesNotAuthorizeAnUnclosedToolStep(string status)
    {
        using var fixture = new Fixture(ToolSteps(closed: false) + Terminal(status) + Done);
        var events = await Collect(fixture.Provider, Request());
        Assert.Single(events, e => e.Kind == PlaygroundProviderEventKind.Error);
        Assert.DoesNotContain(events, e => e.Kind is PlaygroundProviderEventKind.ToolCall or PlaygroundProviderEventKind.Continuation or PlaygroundProviderEventKind.Completed);
    }

    [Fact]
    public async Task PreviewStatusUpdateTerminalRemainsSupported()
    {
        using var fixture = new Fixture(TextSteps + Event("interaction.status_update", """{"status":"completed","usage":{"total_input_tokens":17,"total_output_tokens":4}}"""));
        var events = await Collect(fixture.Provider, Request());
        Assert.DoesNotContain(events, e => e.Kind == PlaygroundProviderEventKind.Error);
        Assert.Single(events, e => e.Kind == PlaygroundProviderEventKind.Completed);
        Assert.Equal(4, Assert.Single(events, e => e.Kind == PlaygroundProviderEventKind.Usage).Usage!.OutputTokens);
    }

    [Fact]
    public async Task GaTypedFinalAfterLegacyStatusProvidesAuthoritativeUsageAndTape()
    {
        string status = Event("interaction.status_update", """{"status":"completed","usage":{"total_input_tokens":17,"total_output_tokens":4}}""");
        using var fixture = new Fixture(TextSteps + status
            + Terminal("completed", """{"total_input_tokens":19,"total_output_tokens":5,"total_thought_tokens":10}""") + Done);
        var events = await Collect(fixture.Provider, Request() with { ConversationBinding = "final-usage-chain" });
        Assert.DoesNotContain(events, e => e.Kind == PlaygroundProviderEventKind.Error);
        var usage = Assert.Single(events, e => e.Kind == PlaygroundProviderEventKind.Usage).Usage!;
        Assert.Equal(19, usage.InputTokens); Assert.Equal(15, usage.OutputTokens); Assert.Equal(10, usage.ReasoningTokens);
        Assert.Single(events, e => e.Kind == PlaygroundProviderEventKind.ConversationTape);
    }

    [Fact]
    public async Task TruncatedGaFrameAfterLegacyStatusIsStillAnInterruptedStream()
    {
        string legacy = Event("interaction.status_update", """{"status":"completed"}""");
        using var fixture = new Fixture(TextSteps + legacy + "event: interaction.completed\ndata: {\"event_type\":\"interaction.completed\"");
        var events = await Collect(fixture.Provider, Request() with { ConversationBinding = "truncated-final-chain" });
        Assert.Single(events, e => e.Kind == PlaygroundProviderEventKind.Error);
        Assert.DoesNotContain(events, e => e.Kind is PlaygroundProviderEventKind.Completed or PlaygroundProviderEventKind.ConversationTape);
    }

    [Fact]
    public async Task FinalWithoutToolsPublishesThoughtTapeForAnIndependentUserTurn()
    {
        using var fixture = new Fixture(TextSteps + Terminal("completed") + Done, TextSteps + Terminal("completed") + Done);
        var firstRequest = Request() with { ConversationBinding = "synthetic-chain" };
        var first = await Collect(fixture.Provider, firstRequest);
        var tape = Assert.Single(first, e => e.Kind == PlaygroundProviderEventKind.ConversationTape).ConversationTape!;
        Assert.Equal("synthetic-chain", tape.Binding);
        Assert.Equal("[provider-private-conversation-state]", tape.ToString());
        Assert.Contains("synthetic-thought-signature", tape.Data);
        string original = tape.Data;
        var next = firstRequest with { TurnId = Guid.NewGuid(), Binding = "next-turn-binding", ConversationTape = tape,
            Messages = [new("user", "Independent next question")] };
        var second = await Collect(fixture.Provider, next);
        Assert.DoesNotContain(second, e => e.Kind == PlaygroundProviderEventKind.Error);
        Assert.Single(second, e => e.Kind == PlaygroundProviderEventKind.ConversationTape);
        var body = JsonNode.Parse(fixture.Bodies[1])!; var input = body["input"]!.AsArray();
        Assert.Equal(new[] { "user_input", "thought", "model_output", "user_input" },
            input.Select(step => step!["type"]!.GetValue<string>()).ToArray());
        Assert.Equal("synthetic-thought-signature", input[1]!["signature"]!.GetValue<string>());
        Assert.Equal("Independent next question", input[3]!["content"]![0]!["text"]!.GetValue<string>());
        Assert.False(body["store"]!.GetValue<bool>()); Assert.Null(body["previous_interaction_id"]);
        Assert.Equal(original, tape.Data);
    }

    [Fact]
    public async Task FinalTapeKeepsToolsResultsAndAllThoughtsAcrossUserTurns()
    {
        using var fixture = new Fixture(ToolSteps(true) + Terminal("requires_action") + Done,
            TextSteps + Terminal("completed") + Done, TextSteps + Terminal("completed") + Done);
        var request = Request() with { ConversationBinding = "synthetic-tool-chain" };
        var first = await Collect(fixture.Provider, request);
        Assert.DoesNotContain(first, e => e.Kind == PlaygroundProviderEventKind.ConversationTape);
        var call = Assert.Single(first, e => e.Kind == PlaygroundProviderEventKind.ToolCall).ToolCall!;
        var resumed = await Collect(fixture.Provider, request with
        {
            Continuation = Assert.Single(first, e => e.Kind == PlaygroundProviderEventKind.Continuation).Continuation,
            ToolResults = [new("tool", "synthetic-tool-fact", call.Id, call.Name)]
        });
        var tape = Assert.Single(resumed, e => e.Kind == PlaygroundProviderEventKind.ConversationTape).ConversationTape!;
        var next = await Collect(fixture.Provider, request with { TurnId = Guid.NewGuid(), Binding = "new-tool-turn",
            ConversationTape = tape, Messages = [new("user", "Use the previous tool facts")] });
        Assert.DoesNotContain(next, e => e.Kind == PlaygroundProviderEventKind.Error);
        var input = JsonNode.Parse(fixture.Bodies[2])!["input"]!.AsArray();
        Assert.Equal(2, input.Count(step => step?["type"]?.GetValue<string>() == "thought"));
        Assert.Contains(input, step => step?["type"]?.GetValue<string>() == "function_call" && step?["id"]?.GetValue<string>() == call.Id);
        Assert.Contains(input, step => step?["type"]?.GetValue<string>() == "function_result"
            && step?["call_id"]?.GetValue<string>() == call.Id && step?["result"]?.GetValue<string>() == "synthetic-tool-fact");
        Assert.Equal("synthetic-thought-signature", input.First(step => step?["type"]?.GetValue<string>() == "thought")!["signature"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("binding")]
    [InlineData("inner-binding")]
    [InlineData("conversation")]
    [InlineData("model")]
    [InlineData("endpoint")]
    [InlineData("same-turn")]
    [InlineData("oversized")]
    [InlineData("image")]
    [InlineData("kind")]
    public async Task ChangedConversationTapeScopeIsRejectedBeforeCredentialsOrHttp(string change)
    {
        using var fixture = new Fixture(TextSteps + Terminal("completed") + Done);
        var original = Request() with { ConversationBinding = "original-chain" };
        var first = await Collect(fixture.Provider, original);
        var tape = Assert.Single(first, e => e.Kind == PlaygroundProviderEventKind.ConversationTape).ConversationTape!;
        var request = original with { TurnId = Guid.NewGuid(), Binding = "independent-turn", ConversationTape = tape,
            Messages = [new("user", "Next question")] };
        var envelope = JsonNode.Parse(tape.Data)!;
        switch (change)
        {
            case "binding": request = request with { ConversationBinding = "new-chain" }; break;
            case "inner-binding": request = request with { ConversationBinding = "new-chain", ConversationTape = new("new-chain", tape.Data) }; break;
            case "conversation": request = request with { ConversationId = Guid.NewGuid() }; break;
            case "model": request = request with { Model = "gemini-4-argon" }; break;
            case "same-turn": request = request with { TurnId = original.TurnId }; break;
            case "oversized": request = request with { ConversationTape = new(tape.Binding, new string('x', 2 * 1024 * 1024 + 1)) }; break;
            case "endpoint": envelope["endpoint"] = "https://example.org"; request = request with { ConversationTape = new(tape.Binding, envelope.ToJsonString()) }; break;
            case "kind": envelope["kind"] = "turn-continuation"; request = request with { ConversationTape = new(tape.Binding, envelope.ToJsonString()) }; break;
            case "image":
                envelope["items"]!.AsArray().Add(new JsonObject { ["type"] = "user_input", ["content"] = new JsonArray(
                    new JsonObject { ["type"] = "image", ["data"] = "synthetic-image-not-allowed-in-tape", ["mime_type"] = "image/png" }) });
                request = request with { ConversationTape = new(tape.Binding, envelope.ToJsonString()) }; break;
        }
        var rejected = await Collect(fixture.Provider, request);
        Assert.Equal(PlaygroundFailureKind.InvalidResponse, Assert.Single(rejected).Failure!.Kind);
        Assert.Single(fixture.Bodies); Assert.Equal(1, fixture.CredentialReads);
    }

    [Fact]
    public async Task PortableAssistantHistoryIsQuotedInsteadOfForgedAsNativeModelOutput()
    {
        using var fixture = new Fixture(TextSteps + Terminal("completed") + Done);
        var events = await Collect(fixture.Provider, Request() with { Messages = [new("user", "Earlier question"),
            new("assistant", "PORTABLE-ASSISTANT-CANARY"), new("user", "Current question")] });
        Assert.DoesNotContain(events, e => e.Kind == PlaygroundProviderEventKind.Error);
        var input = JsonNode.Parse(Assert.Single(fixture.Bodies))!["input"]!.AsArray();
        Assert.All(input, step => Assert.Equal("user_input", step!["type"]!.GetValue<string>()));
        string historical = input[1]!["content"]![0]!["text"]!.GetValue<string>();
        Assert.Contains("历史助手答复", historical); Assert.Contains("不是玩家原话", historical); Assert.Contains("PORTABLE-ASSISTANT-CANARY", historical);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TurnOnlyContinuationCannotBeReusedForAnIndependentUserTurn(bool preserveOldOuterBinding)
    {
        using var fixture = new Fixture(ToolSteps(true) + Terminal("requires_action") + Done);
        var request = Request() with { ConversationBinding = "tool-chain" };
        var events = await Collect(fixture.Provider, request);
        var call = Assert.Single(events, e => e.Kind == PlaygroundProviderEventKind.ToolCall).ToolCall!;
        var continuation = Assert.Single(events, e => e.Kind == PlaygroundProviderEventKind.Continuation).Continuation!;
        var invalid = request with { TurnId = Guid.NewGuid(), Binding = preserveOldOuterBinding ? request.Binding : "new-user-turn",
            Continuation = continuation, ToolResults = [new("tool", "synthetic fact", call.Id, call.Name)] };
        var rejected = await Collect(fixture.Provider, invalid);
        Assert.Equal(PlaygroundFailureKind.InvalidResponse, Assert.Single(rejected).Failure!.Kind);
        Assert.Single(fixture.Bodies); Assert.Equal(1, fixture.CredentialReads);
    }

    private const string Done = "event: done\ndata: [DONE]\n\n";
    private static string Event(string type, string fields)
    {
        var json = JsonNode.Parse(fields)!.AsObject(); json["event_type"] = type;
        return "event: " + type + "\ndata: " + json.ToJsonString() + "\n\n";
    }
    private static string Terminal(string status, string usage = "null") => Event("interaction.completed", "{\"interaction\":{\"object\":\"interaction\",\"status\":\"" + status + "\",\"usage\":" + usage + "}}");
    private static readonly string ThoughtSteps = Event("interaction.created", """{"interaction":{"object":"interaction","model":"gemini-3.8-flash"}}""")
        + Event("interaction.status_update", """{"status":"in_progress"}""")
        + Event("step.start", """{"index":0,"step":{"type":"thought"}}""")
        + Event("step.delta", """{"index":0,"delta":{"type":"thought_signature","signature":"synthetic-thought-signature"}}""")
        + Event("step.stop", """{"index":0}""");
    private static readonly string TextSteps = ThoughtSteps
        + Event("step.start", """{"index":1,"step":{"type":"model_output"}}""")
        + Event("step.delta", """{"index":1,"delta":{"type":"text","text":"左：红色；右：蓝色"}}""")
        + Event("step.stop", """{"index":1}""");
    private static string ToolSteps(bool closed) => ThoughtSteps
        + Event("step.start", """{"index":1,"step":{"type":"function_call","id":"call-fixture","name":"lookup","signature":"synthetic-function-signature"}}""")
        + Event("step.delta", """{"index":1,"delta":{"type":"arguments_delta","arguments":"{\"n\":1}"}}""")
        + (closed ? Event("step.stop", """{"index":1}""") : "");
    private static PlaygroundProviderRequest Request() => new(Guid.NewGuid(), Guid.NewGuid(), PlaygroundProviderKind.Gemini,
        PlaygroundProviderEndpoints.Default(PlaygroundProviderKind.Gemini), "gemini-3.8-flash", "Synthetic instructions", [new("user", "Synthetic question")])
        { Binding = "synthetic-binding", Tools = [new("lookup", "Synthetic tool", """{"type":"object","properties":{"n":{"type":"integer"}},"required":["n"]}""")] };
    private static async Task<List<PlaygroundProviderEvent>> Collect(IPlaygroundProvider provider, PlaygroundProviderRequest request)
    { var events = new List<PlaygroundProviderEvent>(); await foreach (var item in provider.SendAsync(request, default)) events.Add(item); return events; }

    private sealed class Fixture : IDisposable
    {
        private readonly HttpClient client;
        private readonly Handler handler;
        private readonly Credentials credentials = new();
        public List<string> Bodies => handler.Bodies;
        public int CredentialReads => credentials.Reads;
        public IPlaygroundProvider Provider { get; }
        public Fixture(params string[] responses)
        { handler = new(responses); client = new(handler); Provider = new PlaygroundProviderRegistry(client, credentials).Providers.Single(p => p.Kind == PlaygroundProviderKind.Gemini); }
        public void Dispose() => client.Dispose();
    }
    private sealed class Credentials : IPlaygroundCredentials
    {
        public int Reads;
        public Task<bool> HasAsync(PlaygroundProviderKind provider, string endpoint, CancellationToken cancellation) => Task.FromResult(true);
        public Task<string?> ReadAsync(PlaygroundProviderKind provider, string endpoint, CancellationToken cancellation)
        { Reads++; return Task.FromResult<string?>("synthetic-fixture-key"); }
        public Task SaveAsync(PlaygroundProviderKind provider, string endpoint, string key, CancellationToken cancellation) => throw new NotSupportedException();
        public Task ClearAsync(PlaygroundProviderKind provider, string endpoint, CancellationToken cancellation) => throw new NotSupportedException();
    }
    private sealed class Handler(string[] responses) : HttpMessageHandler
    {
        private readonly Queue<string> remaining = new(responses);
        public List<string> Bodies { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellation)
        {
            Bodies.Add(await request.Content!.ReadAsStringAsync(cancellation));
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new Fragmented(Encoding.UTF8.GetBytes(remaining.Dequeue()))) };
            response.Content.Headers.ContentType = new("text/event-stream"); response.RequestMessage = request; return response;
        }
    }
    private sealed class Fragmented(byte[] bytes) : MemoryStream(bytes, false)
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => base.ReadAsync(buffer[..Math.Min(1, buffer.Length)], cancellationToken);
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => base.ReadAsync(buffer, offset, Math.Min(1, count), cancellationToken);
    }
}
