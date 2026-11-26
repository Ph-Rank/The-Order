using System.Collections.Immutable;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using TheOrder.Application.Playground;
using TheOrder.Infrastructure.PlaygroundProviders;
using Xunit;

namespace TheOrder.Tests;

public sealed class ChatGptProviderTests
{
    private const string AccountId = "oaiapp_synthetic";
    private static PlaygroundProviderRequest Request => new(Guid.NewGuid(), Guid.NewGuid(), PlaygroundProviderKind.ChatGPT,
        ChatGptSignInPolicy.Resource, "gpt-6.1-sol", "Synthetic instructions", [new("user", "first question"), new("assistant", "first answer"), new("user", "next question")])
        { ChatGptAccountId = AccountId, Binding = "synthetic-binding", ReasoningEffort = "high" };
    private static JsonObject Message(string text) => new() { ["type"] = "message", ["role"] = "assistant",
        ["content"] = new JsonArray(new JsonObject { ["type"] = "output_text", ["text"] = text }) };
    private static string Completed(JsonArray output) => "data: " + new JsonObject { ["type"] = "response.completed",
        ["response"] = new JsonObject { ["status"] = "completed", ["output"] = output,
            ["usage"] = new JsonObject { ["input_tokens"] = 10, ["output_tokens"] = 2 } } }.ToJsonString() + "\n\n";
    private static string FunctionCall(string ns = "the_order") => Completed(new JsonArray(new JsonObject {
        ["type"] = "function_call", ["call_id"] = "call_1", ["name"] = "lookup", ["namespace"] = ns, ["arguments"] = "{}" }));
    private static readonly PlaygroundToolDefinition Tool = new("lookup", "Synthetic read only lookup", "{\"type\":\"object\",\"properties\":{}}");
    private static async Task<List<PlaygroundProviderEvent>> Collect(IPlaygroundProvider provider, PlaygroundProviderRequest request)
    { var events = new List<PlaygroundProviderEvent>(); await foreach (var ev in provider.SendAsync(request, default)) events.Add(ev); return events; }

    [Fact] public async Task CatalogUsesAccountSlugVisibilityAndServerOrder()
    {
        using var rig = new Rig(new JsonObject { ["models"] = new JsonArray(
            new JsonObject { ["slug"] = "future-model", ["display_name"] = "First choice", ["visibility"] = "list" },
            new JsonObject { ["slug"] = "hidden-model", ["display_name"] = "Hidden", ["visibility"] = "hidden" },
            new JsonObject { ["slug"] = "gpt-6.1-sol", ["display_name"] = "GPT Sol", ["visibility"] = "list" }) });
        var models = await rig.Provider.ListModelsForAccountAsync(ChatGptSignInPolicy.Resource, AccountId, default);
        Assert.Equal(new[] { "future-model", "gpt-6.1-sol" }, models.Select(m => m.Id)); Assert.Equal("First choice", models[0].Name);
        Assert.Equal(models, PlaygroundModelSelection.SelectLatest(PlaygroundProviderKind.ChatGPT, models));
        Assert.Equal(AccountId, Assert.Single(rig.Credentials.Accounts)); var sent = Assert.Single(rig.Requests);
        Assert.Equal("/v1/models", sent.Uri.AbsolutePath);
        Assert.Equal("application/json", Assert.Single(sent.Accept));
    }
    [Fact] public async Task PlanRequestsUseStatelessStreamNamespaceAndNoUnsupportedCapsOrApiFallback()
    {
        using var rig = new Rig(FunctionCall()); var request = Request with { Tools = [Tool], MaximumOutputTokens = 4096, MaximumToolCalls = 1 };
        var events = await Collect(rig.Provider, request); Assert.Single(events, e => e.Kind == PlaygroundProviderEventKind.ToolCall);
        var sent = Assert.Single(rig.Requests); var body = JsonNode.Parse(sent.Body)!;
        Assert.Equal("https://api.openai.com/v1/responses", sent.Uri.AbsoluteUri); Assert.True(body["stream"]!.GetValue<bool>()); Assert.False(body["store"]!.GetValue<bool>());
        Assert.Equal(3, body["input"]!.AsArray().Count); Assert.Equal("namespace", body["tools"]![0]!["type"]!.GetValue<string>());
        Assert.Equal("the_order", body["tools"]![0]!["name"]!.GetValue<string>()); Assert.Equal("lookup", body["tools"]![0]!["tools"]![0]!["name"]!.GetValue<string>());
        foreach (string field in new[] { "max_output_tokens", "max_tool_calls", "previous_response_id", "metadata", "temperature", "conversation", "background" }) Assert.Null(body[field]);
        Assert.Equal(AccountId, Assert.Single(rig.Credentials.Accounts)); Assert.DoesNotContain("synthetic-bearer", sent.Body);
        Assert.DoesNotContain("synthetic-bearer", JsonSerializer.Serialize(events));
        Assert.Equal("使用 ChatGPT 额度", PlaygroundPricing.Describe(PlaygroundProviderKind.ChatGPT, request.Model, DateTimeOffset.UtcNow));
        Assert.Null(PlaygroundPricing.Calculate(PlaygroundProviderKind.ChatGPT, request.Model, new(10, 2), DateTimeOffset.UtcNow).Maximum);
    }
    [Fact]
    public async Task ChatGptReadsLongUnicodeReplyThroughItsCompletedStream()
    {
        string[] chunks = Enumerable.Range(1, 8).Select(index => $"第{index}段\n" + new string('文', 1900) + "\n").ToArray();
        string text = string.Concat(chunks);
        var stream = new StringBuilder();
        foreach (string chunk in chunks)
        {
            var delta = new JsonObject { ["type"] = "response.output_text.delta", ["delta"] = chunk };
            stream.Append("data: ").Append(delta.ToJsonString()).Append("\n\n");
        }
        stream.Append(Completed(new JsonArray(Message(text))));

        using var rig = new Rig(stream.ToString());
        var events = await Collect(rig.Provider, Request with { Model = "gpt-6.2-sol" });

        Assert.Equal(text, string.Concat(events.Where(e => e.Kind == PlaygroundProviderEventKind.Text).Select(e => e.Text)));
        Assert.Single(events, e => e.Kind == PlaygroundProviderEventKind.Completed);
        Assert.DoesNotContain(events, e => e.Failure is not null);
        var body = JsonNode.Parse(Assert.Single(rig.Requests).Body)!;
        Assert.Equal("high", body["reasoning"]!["effort"]!.GetValue<string>());
        Assert.Null(body["max_output_tokens"]);
    }
    [Fact] public async Task ContinuationCannotMoveToAnotherAccountEvenWithSameConversationAndTurn()
    {
        using var rig = new Rig(FunctionCall()); var request = Request with { Tools = [Tool] };
        var first = await Collect(rig.Provider, request); var continuation = Assert.Single(first, e => e.Continuation is not null).Continuation!;
        var next = await Collect(rig.Provider, request with { ChatGptAccountId = "oaiapp_other", Continuation = continuation,
            ToolResults = [new("tool", "Synthetic result", "call_1", "lookup")] });
        Assert.Equal(PlaygroundFailureKind.InvalidResponse, Assert.Single(next).Failure!.Kind); Assert.Single(rig.Requests); Assert.Single(rig.Credentials.Accounts);
    }
    [Fact] public async Task WrongNamespaceCannotExecuteLocalFunction()
    {
        using var rig = new Rig(FunctionCall("other_app")); var events = await Collect(rig.Provider, Request with { Tools = [Tool] });
        Assert.Contains(events, e => e.Failure?.Kind == PlaygroundFailureKind.InvalidResponse);
        Assert.DoesNotContain(events, e => e.Kind == PlaygroundProviderEventKind.ToolCall);
    }
    [Theory]
    [InlineData("subscription_sharing_usage_limit_exceeded", PlaygroundFailureKind.Quota)]
    [InlineData("subscription_sharing_usage_unavailable", PlaygroundFailureKind.Network)]
    [InlineData("subscription_sharing_user_not_eligible", PlaygroundFailureKind.AccessDenied)]
    [InlineData("subscription_sharing_unsupported_capability", PlaygroundFailureKind.Unsupported)]
    [InlineData("chatpass_v2_scope_not_authorized", PlaygroundFailureKind.Authentication)]
    public async Task PlanErrorsRemainActionableAndNeverRetryOrFallBack(string code, PlaygroundFailureKind expected)
    {
        using var rig = new Rig(new JsonObject { ["error"] = new JsonObject { ["code"] = code, ["message"] = "SERVER-PRIVATE-PROSE" } }, HttpStatusCode.Forbidden);
        var events = await Collect(rig.Provider, Request); var failure = Assert.Single(events).Failure!;
        Assert.Equal(expected, failure.Kind); Assert.DoesNotContain("SERVER-PRIVATE-PROSE", failure.Message); Assert.Single(rig.Requests);
        if (expected == PlaygroundFailureKind.Quota) Assert.Contains("管理 ChatGPT 用量", failure.Message);
    }
    [Fact] public async Task MidStreamQuotaFailureRetainsPartialTextWithoutCompleted()
    {
        string partial = "data: {\"type\":\"response.output_text.delta\",\"delta\":\"partial\"}\n\n";
        string failed = "data: {\"type\":\"response.failed\",\"response\":{\"error\":{\"code\":\"subscription_sharing_usage_limit_exceeded\"}}}\n\n";
        using var rig = new Rig(partial + failed); var events = await Collect(rig.Provider, Request);
        Assert.Contains(events, e => e.Text == "partial"); Assert.Equal(PlaygroundFailureKind.Quota, events.Last().Failure!.Kind);
        Assert.DoesNotContain(events, e => e.Kind == PlaygroundProviderEventKind.Completed); Assert.Single(rig.Requests);
    }
    [Theory] [InlineData("json")] [InlineData("incomplete")] [InlineData("truncated")]
    public async Task OnlyTypedCompletedStreamIsSuccessful(string mode)
    {
        using var rig = mode == "json" ? new Rig(new JsonObject { ["status"] = "completed", ["output"] = new JsonArray(Message("answer")) })
            : new Rig(mode == "truncated" ? "data: {\"type\":\"response.output_text.delta\",\"delta\":\"partial\"}\n\n"
                : "data: {\"type\":\"response.incomplete\",\"response\":{\"status\":\"incomplete\",\"output\":[]}}\n\n");
        var events = await Collect(rig.Provider, Request); Assert.Contains(events, e => e.Kind == PlaygroundProviderEventKind.Error);
        Assert.DoesNotContain(events, e => e.Kind == PlaygroundProviderEventKind.Completed); Assert.Single(rig.Requests);
    }
    [Fact]
    public async Task GameScreenshotTurnRequestsOnlySseWhenPeerOffersJson()
    {
        var image = new PlaygroundAttachment("frame.png", "") { IsGameFrame = true, MediaType = "image/png",
            ImageWidth = 1, ImageHeight = 1,
            ImageBase64 = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/f6kAAAAASUVORK5CYII=" };
        using var rig = new Rig(Completed(new JsonArray(Message("first route")))) { OffersJson = true };
        var events = await Collect(rig.Provider, Request with {
            Model = "gpt-6.2-sol", ReasoningEffort = "medium",
            Messages = [new("user", "Which route should I play first?") { Attachments = [image] }]
        });
        Assert.Contains(events, e => e.Text == "first route");
        Assert.Contains(events, e => e.Kind == PlaygroundProviderEventKind.Completed);
        var sent = Assert.Single(rig.Requests);
        Assert.Equal("text/event-stream", Assert.Single(sent.Accept));
        var body = JsonNode.Parse(sent.Body)!;
        Assert.True(body["stream"]!.GetValue<bool>());
        Assert.False(body["store"]!.GetValue<bool>());
        Assert.Contains(body["input"]![0]!["content"]!.AsArray(), part => part?["type"]?.GetValue<string>() == "input_image");
    }

    [Fact]
    public async Task JsonErrorOnSuccessStatusIsClassifiedWithoutDisclosingServerProse()
    {
        using var rig = new Rig(new JsonObject { ["error"] = new JsonObject {
            ["code"] = "subscription_sharing_unsupported_capability", ["param"] = "input[0].private-field",
            ["message"] = "PRIVATE-SERVER-PROSE" } }) { RequestId = "req_fixture-123" };
        var failure = Assert.Single(await Collect(rig.Provider, Request)).Failure!;
        Assert.Equal(PlaygroundFailureKind.Unsupported, failure.Kind);
        Assert.Equal(200, failure.HttpStatus);
        Assert.Contains("JSON 错误", failure.Message);
        Assert.Contains("subscription_sharing_unsupported_capability", failure.Message);
        Assert.Contains("参数 input", failure.Message);
        Assert.Contains("req_fixture-123", failure.Message);
        Assert.DoesNotContain("PRIVATE-SERVER-PROSE", failure.Message);
        Assert.DoesNotContain("private-field", failure.Message);
        Assert.Single(rig.Requests);
    }

    [Theory]
    [InlineData("text/html", "网页")]
    [InlineData("text/plain", "纯文本")]
    [InlineData(null, "网页")]
    public async Task MissingSseDoesNotDispatchOrExposeUnexpectedContent(string? mediaType, string description)
    {
        using var rig = new Rig("<html>PRIVATE-UPSTREAM-DATA</html>", mediaType) { RequestId = "private@invalid.example" };
        var failure = Assert.Single(await Collect(rig.Provider, Request)).Failure!;
        Assert.Equal(PlaygroundFailureKind.InvalidResponse, failure.Kind);
        Assert.Equal(200, failure.HttpStatus);
        Assert.Contains(description, failure.Message);
        Assert.DoesNotContain("PRIVATE-UPSTREAM-DATA", failure.Message);
        Assert.DoesNotContain("private@invalid.example", failure.Message);
        Assert.Single(rig.Requests);
    }

    [Fact]
    public async Task UnstructuredDirectAdmissionFailureReportsStatusAndSafeBodyShape()
    {
        using var rig = new Rig(new JsonObject { ["detail"] = "PRIVATE-ADMISSION-MESSAGE" }, HttpStatusCode.Forbidden)
            { RequestId = "req_admission-123" };
        var failure = Assert.Single(await Collect(rig.Provider, Request)).Failure!;
        Assert.Equal(PlaygroundFailureKind.AccessDenied, failure.Kind);
        Assert.Equal(403, failure.HttpStatus);
        Assert.Contains("JSON 接入错误", failure.Message);
        Assert.Contains("req_admission-123", failure.Message);
        Assert.DoesNotContain("PRIVATE-ADMISSION-MESSAGE", failure.Message);
        Assert.Single(rig.Requests);
    }

    [Fact]
    public async Task HeaderlessFragmentedEventsRetainTheirFirstPacketAndComplete()
    {
        string opening = "\uFEFF: keepalive\r\n\r\ndata: {\"type\":\"response.output_text.delta\",\"delta\":\"stream answer\"}\r\n\r\n";
        using var rig = new Rig(opening + Completed(new JsonArray(Message("stream answer"))), null) { Fragment = true };
        var events = await Collect(rig.Provider, Request);
        Assert.Equal("stream answer", Assert.Single(events, ev => ev.Kind == PlaygroundProviderEventKind.Text).Text);
        Assert.Single(events, ev => ev.Kind == PlaygroundProviderEventKind.Completed);
        Assert.DoesNotContain(events, ev => ev.Kind == PlaygroundProviderEventKind.Error);
        Assert.NotNull(Assert.Single(rig.Requests).ClientId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("\r\n \t")]
    public async Task EmptyHeaderlessReplyReportsClientRequestIdentifier(string payload)
    {
        using var rig = new Rig(payload, null) { Fragment = true };
        var failure = Assert.Single(await Collect(rig.Provider, Request)).Failure!;
        Assert.Equal(PlaygroundFailureKind.InvalidResponse, failure.Kind);
        Assert.Equal(200, failure.HttpStatus);
        Assert.Contains("空响应", failure.Message);
        var clientId = Assert.Single(rig.Requests).ClientId;
        Assert.True(Guid.TryParseExact(clientId, "N", out _));
        Assert.Contains("客户端请求编号 " + clientId, failure.Message);
    }

    [Fact]
    public async Task HeaderlessJsonRejectionIsStillClassified()
    {
        using var rig = new Rig("""{"error":{"code":"subscription_sharing_usage_limit_exceeded","message":"PRIVATE-FIXTURE"}}""", null);
        var events = await Collect(rig.Provider, Request);
        var failure = Assert.Single(events).Failure!;
        Assert.Equal(PlaygroundFailureKind.Quota, failure.Kind);
        Assert.Contains("JSON 错误", failure.Message);
        Assert.DoesNotContain("PRIVATE-FIXTURE", failure.Message);
        Assert.Single(rig.Requests);
    }

    [Theory]
    [InlineData("json")]
    [InlineData("non-sse-data")]
    [InlineData("missing-terminal")]
    [InlineData("mismatched-terminal")]
    public async Task HeaderlessTransportDoesNotTreatUnconfirmedResponsesAsCompleted(string mode)
    {
        string reply = mode switch
        {
            "json" => """{"status":"completed","output":[]}""",
            "non-sse-data" => "PRIVATE-NON-SSE\n" + Completed(new JsonArray(Message("fake"))),
            "missing-terminal" => "data: {\"type\":\"response.output_text.delta\",\"delta\":\"partial\"}\n\n",
            _ => "data: {\"type\":\"response.incomplete\",\"response\":{\"status\":\"completed\",\"output\":[]}}\n\n"
        };
        using var rig = new Rig(reply, null);
        var events = await Collect(rig.Provider, Request);
        var error = Assert.Single(events, ev => ev.Kind == PlaygroundProviderEventKind.Error);
        Assert.DoesNotContain(events, ev => ev.Kind == PlaygroundProviderEventKind.Completed);
        Assert.DoesNotContain("PRIVATE-NON-SSE", error.Failure!.Message);
        if (mode == "missing-terminal") Assert.Contains(events, ev => ev.Text == "partial");
        Assert.Single(rig.Requests);
    }

    [Theory]
    [InlineData("text/event-stream", true)]
    [InlineData(null, false)]
    public async Task PublicSearchUsesSeparatePublicOnlyStreamAndSameAccount(string? responseMediaType, bool gentleSpoilers)
    {
        var output = new JsonArray(new JsonObject { ["type"] = "web_search_call", ["status"] = "completed", ["id"] = "ws_1",
            ["action"] = new JsonObject { ["type"] = "search", ["query"] = "public question", ["sources"] = new JsonArray(new JsonObject { ["type"] = "url", ["url"] = "https://example.org/source" }) } }, Message("public answer"));
        using var rig = new Rig(Completed(output), responseMediaType);
        var result = await rig.Provider.SearchAsync(new(Guid.NewGuid(), Guid.NewGuid(), PlaygroundProviderKind.ChatGPT,
            ChatGptSignInPolicy.Resource, "gpt-6.1-sol", "public question", 0, 4096)
            { ChatGptAccountId = AccountId, AvoidUnrequestedSpoilers = gentleSpoilers }, default);
        Assert.Equal("public answer", result.Text);
        var sent = Assert.Single(rig.Requests);
        Assert.Equal("text/event-stream", Assert.Single(sent.Accept));
        var body = JsonNode.Parse(sent.Body)!;
        Assert.True(body["stream"]!.GetValue<bool>()); Assert.False(body["store"]!.GetValue<bool>()); Assert.Null(body["max_output_tokens"]);
        Assert.Equal("web_search", body["tools"]![0]!["type"]!.GetValue<string>()); Assert.Single(body["input"]!.AsArray());
        Assert.Contains("Follow the question's requested plot scope", body["instructions"]!.GetValue<string>());
        string guidance = body["instructions"]!.GetValue<string>();
        Assert.Contains("let the question determine the framing", guidance);
        Assert.DoesNotContain("spoiler-free", guidance);
        Assert.Contains("Give concrete evidence", guidance);
        Assert.DoesNotContain("Give general advice", guidance);
        Assert.Equal(gentleSpoilers, guidance.Contains("Avoid volunteering major twists or endings the user did not ask about", StringComparison.Ordinal));
        Assert.Equal(AccountId, Assert.Single(rig.Credentials.Accounts));
    }
    private sealed class Credentials : IPlaygroundCredentials
    {
        public List<string?> Accounts = [];
        public Task<bool> HasAsync(PlaygroundProviderKind provider, string endpoint, CancellationToken cancellation) => Task.FromResult(true);
        public Task<string?> ReadAsync(PlaygroundProviderKind provider, string endpoint, CancellationToken cancellation) => throw new InvalidOperationException("No fallback to API key");
        public Task<string?> ReadForAccountAsync(PlaygroundProviderKind provider, string endpoint, string? accountId, CancellationToken cancellation)
        { Assert.Equal(PlaygroundProviderKind.ChatGPT, provider); Accounts.Add(accountId); return Task.FromResult<string?>("synthetic-bearer"); }
        public Task SaveAsync(PlaygroundProviderKind provider, string endpoint, string key, CancellationToken cancellation) => throw new NotSupportedException();
        public Task ClearAsync(PlaygroundProviderKind provider, string endpoint, CancellationToken cancellation) => throw new NotSupportedException();
    }
    private sealed class FragmentedReply(byte[] bytes) : MemoryStream(bytes)
    {
        public override bool CanSeek => false;
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => base.ReadAsync(buffer[..Math.Min(buffer.Length, 1)], cancellationToken);
    }

    private sealed class Rig : HttpMessageHandler
    {
        private readonly HttpClient http; private readonly string content; private readonly string? mediaType; private readonly HttpStatusCode status;
        public bool OffersJson { get; init; }
        public bool Fragment { get; init; }
        public string? RequestId { get; init; }
        public Credentials Credentials = new(); public IPlaygroundProvider Provider;
        public List<(Uri Uri, string Body, string[] Accept, string? ClientId)> Requests = [];
        public Rig(string stream, string? responseMediaType = "text/event-stream") { content = stream; mediaType = responseMediaType; status = HttpStatusCode.OK; http = new(this, false); Provider = new PlaygroundProviderRegistry(http, Credentials).Providers.Single(p => p.Kind == PlaygroundProviderKind.ChatGPT); }
        public Rig(JsonObject json, HttpStatusCode status = HttpStatusCode.OK) { content = json.ToJsonString(); mediaType = "application/json"; this.status = status; http = new(this, false); Provider = new PlaygroundProviderRegistry(http, Credentials).Providers.Single(p => p.Kind == PlaygroundProviderKind.ChatGPT); }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellation)
        {
            Assert.Equal("Bearer synthetic-bearer", request.Headers.Authorization!.ToString());
            string[] accept = request.Headers.Accept.Select(item => item.MediaType!).ToArray();
            string? clientId = request.Headers.TryGetValues("X-Client-Request-Id", out var ids) ? Assert.Single(ids) : null;
            Requests.Add((request.RequestUri!, request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellation), accept, clientId));
            bool returnsJson = OffersJson && accept.Contains("application/json", StringComparer.OrdinalIgnoreCase);
            var body = returnsJson ? """{"status":"completed","output":[]}""" : content;
            HttpContent payload = Fragment ? new StreamContent(new FragmentedReply(Encoding.UTF8.GetBytes(body))) : new StringContent(body, Encoding.UTF8);
            string? type = returnsJson ? "application/json" : mediaType;
            payload.Headers.ContentType = type is null ? null : new(type);
            var response = new HttpResponseMessage(status) { RequestMessage = request, Content = payload };
            if (RequestId is not null) response.Headers.TryAddWithoutValidation("x-request-id", RequestId);
            return response;
        }
        protected override void Dispose(bool disposing) { if (disposing) http.Dispose(); base.Dispose(disposing); }
    }
}
