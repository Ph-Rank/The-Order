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
        Assert.Equal(AccountId, Assert.Single(rig.Credentials.Accounts)); Assert.Equal("/v1/models", Assert.Single(rig.Requests).Uri.AbsolutePath);
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
    [Fact] public async Task PublicSearchUsesSeparatePublicOnlyStreamAndSameAccount()
    {
        var output = new JsonArray(new JsonObject { ["type"] = "web_search_call", ["status"] = "completed", ["id"] = "ws_1",
            ["action"] = new JsonObject { ["type"] = "search", ["query"] = "public question", ["sources"] = new JsonArray(new JsonObject { ["type"] = "url", ["url"] = "https://example.org/source" }) } }, Message("public answer"));
        using var rig = new Rig(Completed(output));
        var result = await rig.Provider.SearchAsync(new(Guid.NewGuid(), Guid.NewGuid(), PlaygroundProviderKind.ChatGPT,
            ChatGptSignInPolicy.Resource, "gpt-6.1-sol", "public question", 0, 4096) { ChatGptAccountId = AccountId }, default);
        Assert.Equal("public answer", result.Text); var body = JsonNode.Parse(Assert.Single(rig.Requests).Body)!;
        Assert.True(body["stream"]!.GetValue<bool>()); Assert.False(body["store"]!.GetValue<bool>()); Assert.Null(body["max_output_tokens"]);
        Assert.Equal("web_search", body["tools"]![0]!["type"]!.GetValue<string>()); Assert.Single(body["input"]!.AsArray());
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
    private sealed class Rig : HttpMessageHandler
    {
        private readonly HttpClient http; private readonly string content, mediaType; private readonly HttpStatusCode status;
        public Credentials Credentials = new(); public IPlaygroundProvider Provider;
        public List<(Uri Uri, string Body)> Requests = [];
        public Rig(string stream) { content = stream; mediaType = "text/event-stream"; status = HttpStatusCode.OK; http = new(this, false); Provider = new PlaygroundProviderRegistry(http, Credentials).Providers.Single(p => p.Kind == PlaygroundProviderKind.ChatGPT); }
        public Rig(JsonObject json, HttpStatusCode status = HttpStatusCode.OK) { content = json.ToJsonString(); mediaType = "application/json"; this.status = status; http = new(this, false); Provider = new PlaygroundProviderRegistry(http, Credentials).Providers.Single(p => p.Kind == PlaygroundProviderKind.ChatGPT); }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellation)
        {
            Assert.Equal("Bearer synthetic-bearer", request.Headers.Authorization!.ToString());
            Requests.Add((request.RequestUri!, request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellation)));
            return new(status) { RequestMessage = request, Content = new StringContent(content, Encoding.UTF8, mediaType) };
        }
        protected override void Dispose(bool disposing) { if (disposing) http.Dispose(); base.Dispose(disposing); }
    }
}
