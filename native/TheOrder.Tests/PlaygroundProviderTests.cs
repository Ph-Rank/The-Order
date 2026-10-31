using System.Collections.Immutable;
using System.Buffers.Binary;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using TheOrder.Application.Playground;
using TheOrder.Infrastructure.PlaygroundProviders;
using Xunit;

namespace TheOrder.Tests;

// All messages, keys and responses below are synthetic. The handler cannot
// delegate to a network transport and these tests never use a real account.
public sealed class PlaygroundProviderTests
{
    [Theory]
    [InlineData(PlaygroundProviderKind.OpenAI)]
    [InlineData(PlaygroundProviderKind.Gemini)]
    [InlineData(PlaygroundProviderKind.Claude)]
    [InlineData(PlaygroundProviderKind.Grok)]
    [InlineData(PlaygroundProviderKind.DeepSeek)]
    public async Task FragmentedUtf8StreamUsesEachNativeWireAndTypedCompletion(PlaygroundProviderKind kind)
    {
        using var fixture = new Fixture(kind, StreamResponse(TextStream(kind, "你好🙂"), 1));
        var events = await Collect(fixture.Provider, Request(kind));
        Assert.Equal("你好🙂", string.Concat(events.Where(e => e.Kind == PlaygroundProviderEventKind.Text).Select(e => e.Text)));
        Assert.Equal("end_turn", Assert.Single(events, e => e.Kind == PlaygroundProviderEventKind.Completed).StopReason);
        Assert.DoesNotContain(events, e => e.Kind == PlaygroundProviderEventKind.Error);
        var sent = Assert.Single(fixture.Handler.Requests);
        Assert.Equal(ExpectedPath(kind), sent.Uri.AbsolutePath);
        Assert.Equal(kind == PlaygroundProviderKind.Gemini ? Fixture.Secret : "Bearer " + Fixture.Secret, sent.Authentication);
        Assert.DoesNotContain(Fixture.Secret, sent.Body);
        Assert.DoesNotContain(Fixture.Secret, JsonSerializer.Serialize(events));
        Assert.Equal(PlaygroundProviderEndpoints.Default(kind), Assert.Single(fixture.Credentials.Reads).Endpoint);
        var body = JsonNode.Parse(sent.Body)!;
        Assert.True(body["stream"]!.GetValue<bool>());
        Assert.Null(body["previous_response_id"]);
        if (kind == PlaygroundProviderKind.Gemini) { Assert.NotNull(body["input"]); Assert.NotNull(body["system_instruction"]); Assert.False(body["store"]!.GetValue<bool>()); }
        else if (kind == PlaygroundProviderKind.Claude) { Assert.NotNull(body["messages"]); Assert.NotNull(body["system"]); Assert.Equal("2026-10-21", sent.ApiVersion); }
        else { Assert.NotNull(body["input"]); Assert.NotNull(body["instructions"]); if (kind == PlaygroundProviderKind.DeepSeek) Assert.Null(body["store"]); }
        var usage = Assert.Single(events, e => e.Kind == PlaygroundProviderEventKind.Usage).Usage!;
        Assert.Equal(17, usage.InputTokens); Assert.Equal(4, usage.OutputTokens);
    }

    [Theory]
    [InlineData(PlaygroundProviderKind.OpenAI)]
    [InlineData(PlaygroundProviderKind.Gemini)]
    [InlineData(PlaygroundProviderKind.Claude)]
    [InlineData(PlaygroundProviderKind.Grok)]
    [InlineData(PlaygroundProviderKind.DeepSeek)]
    public async Task InterleavedToolArgumentsAreOnlyReleasedAfterTerminalAndResumeWithMatchingTape(PlaygroundProviderKind kind)
    {
        using var fixture = new Fixture(kind, StreamResponse(ToolStream(kind)), StreamResponse(TextStream(kind, "已读取")));
        var request = Request(kind) with { Tools = [Tool], MaximumToolCalls = 3 };
        var events = await Collect(fixture.Provider, request);
        var calls = events.Where(e => e.ToolCall is not null).Select(e => e.ToolCall!).ToArray();
        Assert.Equal(3, calls.Length);
        Assert.Equal([1, 2, 3], calls.Select(c => JsonNode.Parse(c.ArgumentsJson)!["n"]!.GetValue<int>()).ToArray());
        Assert.Equal("tool_calls", Assert.Single(events, e => e.Kind == PlaygroundProviderEventKind.Completed).StopReason);
        var continuation = Assert.Single(events, e => e.Continuation is not null).Continuation!;
        Assert.DoesNotContain(Fixture.Secret, continuation.Data);
        Assert.Equal("[provider-private-state]", continuation.ToString());
        var resumed = await Collect(fixture.Provider, request with { Continuation = continuation,
            ToolResults = calls.Select(c => new PlaygroundWireMessage("tool", "synthetic-result", c.Id, c.Name)).ToImmutableArray() });
        Assert.DoesNotContain(resumed, e => e.Kind == PlaygroundProviderEventKind.Error);
        Assert.Equal(2, fixture.Handler.Requests.Count);
        string second = fixture.Handler.Requests[1].Body;
        Assert.Contains("synthetic-result", second);
        if (kind == PlaygroundProviderKind.Gemini) Assert.Contains("synthetic-signature", second);
        if (kind == PlaygroundProviderKind.Claude)
        {
            var messages = JsonNode.Parse(second)!["messages"]!.AsArray();
            var results = messages.Last()!["content"]!.AsArray();
            Assert.Equal("user", messages.Last()!["role"]!.GetValue<string>());
            Assert.Equal(calls.Select(c => c.Id), results.Select(r => r!["tool_use_id"]!.GetValue<string>()));
            Assert.All(results, r => Assert.Equal("tool_result", r!["type"]!.GetValue<string>()));
            Assert.Equal(2, messages.Count(m => m!["role"]!.GetValue<string>() == "user"));
        }
    }

    [Theory]
    [InlineData(PlaygroundProviderKind.OpenAI, 0)]
    [InlineData(PlaygroundProviderKind.Gemini, 0)]
    [InlineData(PlaygroundProviderKind.Claude, 0)]
    [InlineData(PlaygroundProviderKind.Grok, 0)]
    [InlineData(PlaygroundProviderKind.DeepSeek, 0)]
    [InlineData(PlaygroundProviderKind.OpenAI, 1)]
    [InlineData(PlaygroundProviderKind.Gemini, 1)]
    [InlineData(PlaygroundProviderKind.Claude, 1)]
    [InlineData(PlaygroundProviderKind.Grok, 1)]
    [InlineData(PlaygroundProviderKind.DeepSeek, 1)]
    public async Task MoreThanTheFormerParallelToolLimitCompletesWithoutSendingOptionalCaps(PlaygroundProviderKind kind, int legacyCount)
    {
        using var fixture = new Fixture(kind, StreamResponse(ToolStream(kind, numberOfCalls: 24), chunk: 65536),
            StreamResponse(TextStream(kind, "All synthetic tool results accepted"), chunk: 65536));
        var request = Request(kind) with { Tools = [Tool], MaximumToolCalls = legacyCount };
        var first = await Collect(fixture.Provider, request);
        Assert.DoesNotContain(first, e => e.Kind == PlaygroundProviderEventKind.Error);
        var calls = first.Where(e => e.Kind == PlaygroundProviderEventKind.ToolCall).Select(e => e.ToolCall!).ToArray();
        Assert.Equal(24, calls.Length);
        var resumed = await Collect(fixture.Provider, request with
        {
            Continuation = Assert.Single(first, e => e.Kind == PlaygroundProviderEventKind.Continuation).Continuation,
            ToolResults = calls.Select(call => new PlaygroundWireMessage("tool", "synthetic completed result", call.Id, call.Name)).ToImmutableArray()
        });
        Assert.DoesNotContain(resumed, e => e.Kind == PlaygroundProviderEventKind.Error);
        Assert.Single(resumed, e => e.Kind == PlaygroundProviderEventKind.Completed);
        Assert.Equal(2, fixture.Handler.Requests.Count);
        foreach (var captured in fixture.Handler.Requests)
        {
            var body = JsonNode.Parse(captured.Body)!;
            Assert.Null(body["max_tool_calls"]); Assert.Null(body["max_turns"]);
            Assert.All(body["tools"]!.AsArray(), tool => Assert.Null(tool!["max_uses"]));
        }
        var second = JsonNode.Parse(fixture.Handler.Requests[1].Body)!;
        int resultCount = kind == PlaygroundProviderKind.Claude
            ? second["messages"]!.AsArray().SelectMany(m => m?["content"] as JsonArray ?? []).Count(block => block?["type"]?.GetValue<string>() == "tool_result")
            : second["input"]!.AsArray().Count(item => item?["type"]?.GetValue<string>() is "function_result" or "function_call_output");
        Assert.Equal(24, resultCount);
    }

    [Theory]
    [InlineData(PlaygroundProviderKind.OpenAI)]
    [InlineData(PlaygroundProviderKind.Gemini)]
    [InlineData(PlaygroundProviderKind.Claude)]
    [InlineData(PlaygroundProviderKind.Grok)]
    [InlineData(PlaygroundProviderKind.DeepSeek)]
    public async Task IncompleteParallelToolResultsAreRejectedBeforeAnotherRequest(PlaygroundProviderKind kind)
    {
        using var fixture = new Fixture(kind, StreamResponse(ToolStream(kind)));
        var request = Request(kind) with { Tools = [Tool], MaximumToolCalls = 3 };
        var events = await Collect(fixture.Provider, request);
        var calls = events.Where(e => e.ToolCall is not null).Select(e => e.ToolCall!).ToArray();
        var continuation = Assert.Single(events, e => e.Continuation is not null).Continuation!;
        var rejected = await Collect(fixture.Provider, request with { Continuation = continuation,
            ToolResults = calls.Take(2).Select(c => new PlaygroundWireMessage("tool", "synthetic-result", c.Id, c.Name)).ToImmutableArray() });
        Assert.Equal(PlaygroundFailureKind.InvalidResponse, Assert.Single(rejected, e => e.Kind == PlaygroundProviderEventKind.Error).Failure!.Kind);
        Assert.DoesNotContain(rejected, e => e.Kind is PlaygroundProviderEventKind.Completed or PlaygroundProviderEventKind.ToolCall);
        Assert.Single(fixture.Handler.Requests);
        Assert.Single(fixture.Credentials.Reads);
    }

    [Theory]
    [InlineData(PlaygroundProviderKind.OpenAI)]
    [InlineData(PlaygroundProviderKind.Gemini)]
    [InlineData(PlaygroundProviderKind.Claude)]
    [InlineData(PlaygroundProviderKind.Grok)]
    [InlineData(PlaygroundProviderKind.DeepSeek)]
    public async Task HalfStreamDoesNotExecuteAnyToolOrClaimCompletion(PlaygroundProviderKind kind)
    {
        string stream = ToolStream(kind); int terminal = stream.LastIndexOf("event:", StringComparison.Ordinal);
        using var fixture = new Fixture(kind, StreamResponse(stream[..terminal]));
        var events = await Collect(fixture.Provider, Request(kind) with { Tools = [Tool] });
        Assert.DoesNotContain(events, e => e.Kind is PlaygroundProviderEventKind.ToolCall or PlaygroundProviderEventKind.Continuation or PlaygroundProviderEventKind.Completed);
        Assert.Equal(PlaygroundFailureKind.InvalidResponse, Assert.Single(events, e => e.Kind == PlaygroundProviderEventKind.Error).Failure!.Kind);
        Assert.Single(fixture.Handler.Requests);
    }

    [Theory]
    [InlineData(PlaygroundProviderKind.OpenAI)]
    [InlineData(PlaygroundProviderKind.Gemini)]
    [InlineData(PlaygroundProviderKind.Claude)]
    [InlineData(PlaygroundProviderKind.Grok)]
    [InlineData(PlaygroundProviderKind.DeepSeek)]
    public async Task UnknownManualModelUsesOnlyBasicTextParameters(PlaygroundProviderKind kind)
    {
        using var fixture = new Fixture(kind, StreamResponse(TextStream(kind, "ok")));
        Assert.False(fixture.Provider.Capabilities("future-unverified-model", fixture.Provider.DefaultEndpoint).Tools);
        var events = await Collect(fixture.Provider, Request(kind) with { Model = "future-unverified-model", Tools = [Tool], ReasoningEffort = "high" });
        Assert.Contains(events, e => e.Kind == PlaygroundProviderEventKind.Completed);
        var body = JsonNode.Parse(Assert.Single(fixture.Handler.Requests).Body)!;
        Assert.Null(body["tools"]); Assert.Null(body["reasoning"]); Assert.Null(body["thinking"]); Assert.Null(body["output_config"]); Assert.Null(body["include"]);
        if (kind == PlaygroundProviderKind.Gemini) Assert.Null(body["generation_config"]!["thinking_level"]);
    }

    [Theory]
    [InlineData("low")]
    [InlineData("xhigh")]
    [InlineData("none")]
    public async Task GrokReasoningUsesItsVerifiedResponsesEffortAndCannotBeDisabled(string effort)
    {
        using var fixture = new Fixture(PlaygroundProviderKind.Grok, StreamResponse(TextStream(PlaygroundProviderKind.Grok, "ok")));
        var capabilities = fixture.Provider.Capabilities("grok-4.7", fixture.Provider.DefaultEndpoint);
        Assert.DoesNotContain("none", capabilities.ReasoningEfforts);
        var events = await Collect(fixture.Provider, Request(PlaygroundProviderKind.Grok) with { ReasoningEffort = effort });
        Assert.Contains(events, e => e.Kind == PlaygroundProviderEventKind.Completed);
        var body = JsonNode.Parse(Assert.Single(fixture.Handler.Requests).Body)!;
        if (effort == "none") Assert.Null(body["reasoning"]);
        else Assert.Equal(effort, body["reasoning"]!["effort"]!.GetValue<string>());
        Assert.Null(body["reasoning_effort"]);
        Assert.Null(body["presence_penalty"]); Assert.Null(body["frequency_penalty"]); Assert.Null(body["stop"]);
    }

    [Fact]
    public async Task UnpaginatedOpenAiModelsAfterTheFirstHundredRemainSelectable()
    {
        var data = new JsonArray(Enumerable.Range(0, 120).Select(i => (JsonNode)new JsonObject { ["id"] = "gpt-future-" + i }).ToArray());
        data.Add(new JsonObject { ["id"] = "gpt-6.1-terra" }); data.Add(new JsonObject { ["id"] = "gpt-6-luna" });
        using var fixture = new Fixture(PlaygroundProviderKind.OpenAI, JsonResponse(new JsonObject { ["data"] = data }));
        var models = await fixture.Provider.ListModelsAsync(fixture.Provider.DefaultEndpoint, CancellationToken.None);
        Assert.Equal(122, models.Length);
        Assert.Contains(models, m => m.Id == "gpt-6.1-terra" && m.Capabilities.Tools);
        Assert.Contains(models, m => m.Id == "gpt-6-luna" && m.Capabilities.ImageInput);
        Assert.Single(fixture.Handler.Requests);
    }

    [Theory]
    [InlineData("gpt-6.1-terra")]
    [InlineData("gpt-6-luna")]
    public async Task VerifiedTerraAndLunaAllowTheirNativeToolsAndNoneEffort(string model)
    {
        using var fixture = new Fixture(PlaygroundProviderKind.OpenAI, StreamResponse(TextStream(PlaygroundProviderKind.OpenAI, "ok")));
        var capability = fixture.Provider.Capabilities(model, fixture.Provider.DefaultEndpoint);
        Assert.True(capability.Verified); Assert.True(capability.Tools); Assert.True(capability.ImageInput);
        var events = await Collect(fixture.Provider, Request(PlaygroundProviderKind.OpenAI) with { Model = model, Tools = [Tool], ReasoningEffort = "none" });
        Assert.Contains(events, e => e.Kind == PlaygroundProviderEventKind.Completed);
        var body = JsonNode.Parse(Assert.Single(fixture.Handler.Requests).Body)!;
        Assert.Equal("none", body["reasoning"]!["effort"]!.GetValue<string>());
        Assert.Single(body["tools"]!.AsArray());
    }

    [Theory]
    [InlineData(PlaygroundProviderKind.OpenAI, "gpt-6.1-sol")]
    [InlineData(PlaygroundProviderKind.OpenAI, "gpt-6-astra")]
    [InlineData(PlaygroundProviderKind.OpenAI, "gpt-6.1-terra")]
    [InlineData(PlaygroundProviderKind.OpenAI, "gpt-6-luna")]
    [InlineData(PlaygroundProviderKind.Gemini, "gemini-4-argon")]
    [InlineData(PlaygroundProviderKind.Gemini, "gemini-3.8-flash")]
    [InlineData(PlaygroundProviderKind.Gemini, "gemini-3.6-flash")]
    [InlineData(PlaygroundProviderKind.Claude, "claude-opus-5-5")]
    [InlineData(PlaygroundProviderKind.Claude, "claude-opus-5")]
    [InlineData(PlaygroundProviderKind.Claude, "claude-opus-4-6")]
    [InlineData(PlaygroundProviderKind.Claude, "claude-sonnet-4-6")]
    [InlineData(PlaygroundProviderKind.Grok, "grok-4.7")]
    [InlineData(PlaygroundProviderKind.DeepSeek, "deepseek-flash")]
    [InlineData(PlaygroundProviderKind.DeepSeek, "deepseek-v4-pro")]
    public async Task VerifiedModelsEncodeRealHighAnd65536WithoutSixteenKClamp(PlaygroundProviderKind kind, string model)
    {
        using var fixture = new Fixture(kind, StreamResponse(TextStream(kind, "synthetic high answer")));
        var capability = fixture.Provider.Capabilities(model, fixture.Provider.DefaultEndpoint);
        Assert.Contains("high", capability.ReasoningEfforts);
        var events = await Collect(fixture.Provider, Request(kind) with { Model = model, MaximumOutputTokens = 65536 });
        Assert.Single(events, e => e.Kind == PlaygroundProviderEventKind.Completed);
        var body = JsonNode.Parse(Assert.Single(fixture.Handler.Requests).Body)!;
        if (kind == PlaygroundProviderKind.Gemini)
        {
            Assert.Equal(65536, body["generation_config"]!["max_output_tokens"]!.GetValue<int>());
            Assert.Equal("high", body["generation_config"]!["thinking_level"]!.GetValue<string>());
            Assert.False(body["store"]!.GetValue<bool>());
        }
        else if (kind == PlaygroundProviderKind.Claude)
        {
            Assert.Equal(65536, body["max_tokens"]!.GetValue<int>());
            Assert.Equal("adaptive", body["thinking"]!["type"]!.GetValue<string>());
            Assert.Equal("high", body["output_config"]!["effort"]!.GetValue<string>());
            Assert.Null(body["thinking"]!["budget_tokens"]);
        }
        else
        {
            Assert.Equal(65536, body["max_output_tokens"]!.GetValue<int>());
            Assert.Equal("high", body["reasoning"]!["effort"]!.GetValue<string>());
        }
    }

    [Theory]
    [InlineData(PlaygroundProviderKind.OpenAI)]
    [InlineData(PlaygroundProviderKind.Gemini)]
    [InlineData(PlaygroundProviderKind.Claude)]
    [InlineData(PlaygroundProviderKind.Grok)]
    public async Task HostedResearchAlsoEncodesHighAnd65536(PlaygroundProviderKind kind)
    {
        using var fixture = new Fixture(kind, JsonResponse(SearchResult(kind)));
        _ = await fixture.Provider.SearchAsync(new(Guid.NewGuid(), Guid.NewGuid(), kind,
            fixture.Provider.DefaultEndpoint, Model(kind), "synthetic public source", 1, 65536), CancellationToken.None);
        var body = JsonNode.Parse(Assert.Single(fixture.Handler.Requests).Body)!;
        if (kind == PlaygroundProviderKind.Gemini)
        {
            Assert.Equal(65536, body["generation_config"]!["max_output_tokens"]!.GetValue<int>());
            Assert.Equal("high", body["generation_config"]!["thinking_level"]!.GetValue<string>());
        }
        else if (kind == PlaygroundProviderKind.Claude)
        {
            Assert.Equal(65536, body["max_tokens"]!.GetValue<int>());
            Assert.Equal("adaptive", body["thinking"]!["type"]!.GetValue<string>());
            Assert.Equal("high", body["output_config"]!["effort"]!.GetValue<string>());
        }
        else
        {
            Assert.Equal(65536, body["max_output_tokens"]!.GetValue<int>());
            Assert.Equal("high", body["reasoning"]!["effort"]!.GetValue<string>());
        }
    }

    [Fact]
    public async Task LargeFinalTextHasBoundedRoomAboveTheOldOutputAndSseLineCaps()
    {
        string text = new('x', 300000);
        using var fixture = new Fixture(PlaygroundProviderKind.OpenAI,
            StreamResponse(TextStream(PlaygroundProviderKind.OpenAI, text), chunk: 65536));
        var events = await Collect(fixture.Provider, Request(PlaygroundProviderKind.OpenAI) with { MaximumOutputTokens = 65536 });
        Assert.DoesNotContain(events, e => e.Kind == PlaygroundProviderEventKind.Error);
        Assert.Equal(text, string.Concat(events.Where(e => e.Kind == PlaygroundProviderEventKind.Text).Select(e => e.Text)));
        Assert.Single(events, e => e.Kind == PlaygroundProviderEventKind.Completed);
    }

    [Theory]
    [InlineData(PlaygroundProviderKind.OpenAI)]
    [InlineData(PlaygroundProviderKind.Claude)]
    [InlineData(PlaygroundProviderKind.Grok)]
    [InlineData(PlaygroundProviderKind.DeepSeek)]
    public async Task GeminiConversationTapeNeverCrossesIntoAnotherProvider(PlaygroundProviderKind kind)
    {
        using var fixture = new Fixture(kind);
        var events = await Collect(fixture.Provider, Request(kind) with { ConversationBinding = "gemini-chain",
            ConversationTape = new("gemini-chain", "synthetic Gemini-only native state") });
        Assert.Equal(PlaygroundFailureKind.InvalidResponse, Assert.Single(events).Failure!.Kind);
        Assert.Empty(fixture.Handler.Requests); Assert.Empty(fixture.Credentials.Reads);
    }

    [Theory]
    [InlineData(PlaygroundProviderKind.OpenAI, 0)]
    [InlineData(PlaygroundProviderKind.Gemini, 0)]
    [InlineData(PlaygroundProviderKind.Claude, 0)]
    [InlineData(PlaygroundProviderKind.Grok, 0)]
    [InlineData(PlaygroundProviderKind.DeepSeek, 0)]
    [InlineData(PlaygroundProviderKind.OpenAI, 2)]
    [InlineData(PlaygroundProviderKind.Gemini, 2)]
    [InlineData(PlaygroundProviderKind.Claude, 2)]
    [InlineData(PlaygroundProviderKind.Grok, 2)]
    [InlineData(PlaygroundProviderKind.DeepSeek, 2)]
    public async Task PublicSearchOmitsOptionalExecutionCapsAndPreservesGrounding(PlaygroundProviderKind kind, int legacyCount)
    {
        using var fixture = new Fixture(kind, JsonResponse(SearchResult(kind)));
        var result = await fixture.Provider.SearchAsync(new(Guid.NewGuid(), Guid.NewGuid(), kind,
            fixture.Provider.DefaultEndpoint, Model(kind), "synthetic public release date", legacyCount), CancellationToken.None);
        Assert.NotEmpty(result.Text); Assert.NotEmpty(result.Sources);
        Assert.All(result.Sources, source => Assert.Equal("https://example.org/official", source.Url));
        var sent = Assert.Single(fixture.Handler.Requests); var body = JsonNode.Parse(sent.Body)!;
        Assert.DoesNotContain(Fixture.Secret, sent.Body);
        Assert.DoesNotContain("private-history-marker", sent.Body);
        Assert.DoesNotContain("search_library", sent.Body);
        if (kind == PlaygroundProviderKind.DeepSeek)
        {
            Assert.Equal("/v1/tools/search", sent.Uri.AbsolutePath); Assert.Equal(10, body["count"]!.GetValue<int>());
            Assert.Null(body["max_tool_calls"]); Assert.Equal(1, result.Usage!.Searches);
        }
        else
        {
            Assert.Equal(ExpectedPath(kind), sent.Uri.AbsolutePath);
            Assert.False(body["stream"]!.GetValue<bool>());
            Assert.Null(body["max_tool_calls"]); Assert.Null(body["max_turns"]);
            if (kind == PlaygroundProviderKind.Claude) Assert.Null(body["tools"]![0]!["max_uses"]);
        }
        if (kind == PlaygroundProviderKind.Gemini)
        {
            Assert.Equal(PlaygroundRetention.SessionOnly, result.Retention);
            Assert.Equal("<div>synthetic Google Search suggestions</div>", result.SearchSuggestionsHtml);
            Assert.All(result.Sources, source =>
            {
                Assert.True(source.HasExactRange); Assert.Equal(0, source.Start); Assert.Equal(5, source.End);
                Assert.Equal(PlaygroundRetention.SessionOnly, source.Retention);
            });
        }
        else Assert.Equal(PlaygroundRetention.Portable, result.Retention);
    }

    [Theory]
    [InlineData(PlaygroundProviderKind.Gemini)]
    [InlineData(PlaygroundProviderKind.Claude)]
    public async Task HostedSearchErrorInsideHttp200IsNotSuccessfulResearch(PlaygroundProviderKind kind)
    {
        var response = SearchResult(kind);
        if (kind == PlaygroundProviderKind.Claude)
            response["content"]![1]!["content"] = new JsonObject { ["type"] = "web_search_tool_result_error", ["error_code"] = "overloaded" };
        else response["steps"]![1]!["is_error"] = true;
        using var fixture = new Fixture(kind, JsonResponse(response));
        var failure = await Assert.ThrowsAsync<PlaygroundProviderException>(() => fixture.Provider.SearchAsync(
            new(Guid.NewGuid(), Guid.NewGuid(), kind, fixture.Provider.DefaultEndpoint, Model(kind), "public synthetic query"), CancellationToken.None));
        Assert.Equal(PlaygroundFailureKind.Network, failure.Failure.Kind); Assert.Single(fixture.Handler.Requests);
    }

    [Theory]
    [InlineData(401, PlaygroundFailureKind.Authentication)]
    [InlineData(402, PlaygroundFailureKind.Quota)]
    [InlineData(403, PlaygroundFailureKind.AccessDenied)]
    [InlineData(404, PlaygroundFailureKind.ModelUnavailable)]
    [InlineData(429, PlaygroundFailureKind.RateLimited)]
    [InlineData(503, PlaygroundFailureKind.Network)]
    public async Task HttpErrorsAreTypedRedactedAndNeverAutomaticallyRetried(int status, PlaygroundFailureKind expected)
    {
        var response = new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent("server error with " + Fixture.Secret) };
        response.Headers.RetryAfter = new(TimeSpan.FromSeconds(30));
        using var fixture = new Fixture(PlaygroundProviderKind.OpenAI, response);
        var events = await Collect(fixture.Provider, Request(PlaygroundProviderKind.OpenAI));
        var failure = Assert.Single(events).Failure!;
        Assert.Equal(expected, failure.Kind); Assert.Equal(status, failure.HttpStatus);
        Assert.Equal(30, failure.RetryAfterSeconds); Assert.DoesNotContain(Fixture.Secret, failure.Message);
        Assert.Single(fixture.Handler.Requests);
    }

    [Fact]
    public async Task FinalCrossHostRedirectIsRejectedAndNotRetried()
    {
        var response = JsonResponse(SearchResult(PlaygroundProviderKind.OpenAI));
        response.RequestMessage = new HttpRequestMessage(HttpMethod.Post, "https://other.example.org/responses");
        using var fixture = new Fixture(PlaygroundProviderKind.OpenAI, response);
        var events = await Collect(fixture.Provider, Request(PlaygroundProviderKind.OpenAI));
        Assert.Equal(PlaygroundFailureKind.AccessDenied, Assert.Single(events).Failure!.Kind);
        Assert.Single(fixture.Handler.Requests);
    }

    [Theory]
    [InlineData("http://api.openai.com/v1")]
    [InlineData("https://api.openai.com.evil.example/v1")]
    [InlineData("https://api.openai.com:444/v1")]
    [InlineData("https://user:fixture@api.openai.com/v1")]
    [InlineData("https://api.openai.com/v1?key=fixture")]
    [InlineData("https://api.openai.com/v1/other")]
    public async Task UnsupportedEndpointsAreRejectedBeforeCredentialAccessOrNetwork(string endpoint)
    {
        using var fixture = new Fixture(PlaygroundProviderKind.OpenAI);
        var events = await Collect(fixture.Provider, Request(PlaygroundProviderKind.OpenAI) with { Endpoint = endpoint });
        Assert.Equal(PlaygroundFailureKind.Unsupported, Assert.Single(events).Failure!.Kind);
        Assert.Empty(fixture.Handler.Requests); Assert.Empty(fixture.Credentials.Reads);
    }

    [Fact]
    public async Task BadBindingAndCrossTurnContinuationAreRejectedBeforeSending()
    {
        using var fixture = new Fixture(PlaygroundProviderKind.Gemini, StreamResponse(ToolStream(PlaygroundProviderKind.Gemini)));
        var request = Request(PlaygroundProviderKind.Gemini) with { Tools = [Tool] };
        var first = await Collect(fixture.Provider, request);
        var state = Assert.Single(first, e => e.Continuation is not null).Continuation!;
        foreach (var changed in new[] { request with { Binding = "other" }, request with { TurnId = Guid.NewGuid() }, request with { ConversationId = Guid.NewGuid() }, request with { Model = "gemini-3.6-flash" } })
        {
            var events = await Collect(fixture.Provider, changed with { Continuation = state });
            Assert.Equal(PlaygroundFailureKind.InvalidResponse, Assert.Single(events).Failure!.Kind);
        }
        Assert.Single(fixture.Handler.Requests);
    }

    [Fact]
    public async Task ModelEnumerationIsPaginatedDeduplicatedAndDoesNotInventCapabilities()
    {
        using var fixture = new Fixture(PlaygroundProviderKind.Gemini,
            JsonResponse(JsonNode.Parse("""{"models":[{"name":"models/gemini-3.8-flash","displayName":"Gemini fixture"}],"nextPageToken":"page+two"}""")!),
            JsonResponse(JsonNode.Parse("""{"models":[{"name":"models/gemini-3.8-flash"},{"name":"models/future-manual-model"}]}""")!));
        var models = await fixture.Provider.ListModelsAsync(fixture.Provider.DefaultEndpoint, CancellationToken.None);
        Assert.Equal(2, models.Length); Assert.True(models[0].Capabilities.Tools); Assert.False(models[1].Capabilities.Verified);
        Assert.Equal("?pageSize=100&pageToken=page%2Btwo", fixture.Handler.Requests[1].Uri.Query);
        Assert.False(fixture.Provider.Capabilities("", fixture.Provider.DefaultEndpoint).Verified);
    }

    [Theory]
    [InlineData("data: {\"type\":\"response.output_text.delta\",\"delta\":\"partial\"}\n\ndata: [DONE]\n\n")]
    [InlineData("data: {\"type\":\"response.completed\",\"response\":{\"status\":\"completed\",\"output\":[]}}")]
    [InlineData("data: {this is not json}\n\n")]
    public async Task BrokenSseDoesNotBecomeACompletedMessage(string stream)
    {
        using var fixture = new Fixture(PlaygroundProviderKind.OpenAI, StreamResponse(stream, 1));
        var events = await Collect(fixture.Provider, Request(PlaygroundProviderKind.OpenAI));
        Assert.DoesNotContain(events, e => e.Kind == PlaygroundProviderEventKind.Completed);
        Assert.Equal(PlaygroundFailureKind.InvalidResponse, events.Last().Failure!.Kind);
    }

    [Fact]
    public async Task InvalidUtf8IsAnErrorAndNeverSilentlyReplaced()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new FragmentedStream([0xff, 0xfe, 0x61], 1)) };
        response.Content.Headers.ContentType = new("text/event-stream");
        using var fixture = new Fixture(PlaygroundProviderKind.OpenAI, response);
        var events = await Collect(fixture.Provider, Request(PlaygroundProviderKind.OpenAI));
        Assert.DoesNotContain(events, e => e.Kind == PlaygroundProviderEventKind.Completed);
        Assert.Equal(PlaygroundFailureKind.InvalidResponse, events.Last().Failure!.Kind);
    }

    [Fact]
    public async Task DuplicateSequenceIsDeduplicatedButConflictingSequenceIsRejected()
    {
        var delta = new JsonObject { ["type"] = "response.output_text.delta", ["sequence_number"] = 1, ["delta"] = "ok" };
        string terminal = Event(new() { ["type"] = "response.completed", ["sequence_number"] = 2, ["response"] = new JsonObject { ["status"] = "completed", ["output"] = new JsonArray(Message("ok")) } });
        using (var fixture = new Fixture(PlaygroundProviderKind.OpenAI, StreamResponse(Event(delta) + Event(delta) + terminal)))
        {
            var events = await Collect(fixture.Provider, Request(PlaygroundProviderKind.OpenAI));
            Assert.Equal("ok", string.Concat(events.Where(e => e.Kind == PlaygroundProviderEventKind.Text).Select(e => e.Text)));
            Assert.Contains(events, e => e.Kind == PlaygroundProviderEventKind.Completed);
        }
        string original = Event(delta); delta["delta"] = "conflict";
        using (var fixture = new Fixture(PlaygroundProviderKind.OpenAI, StreamResponse(original + Event(delta) + terminal)))
        {
            var events = await Collect(fixture.Provider, Request(PlaygroundProviderKind.OpenAI));
            Assert.DoesNotContain(events, e => e.Kind == PlaygroundProviderEventKind.Completed);
            Assert.Equal(PlaygroundFailureKind.InvalidResponse, events.Last().Failure!.Kind);
        }
    }

    [Theory]
    [InlineData("{\"n\":", "search_library")]
    [InlineData("{}", "delete_library")]
    [InlineData("[]", "search_library")]
    public async Task IncompleteOrForeignToolIsRejectedBeforeAnyExecution(string arguments, string name)
    {
        var response = new JsonObject { ["status"] = "completed", ["output"] = new JsonArray(new JsonObject { ["type"] = "function_call", ["call_id"] = "call-1", ["name"] = name, ["arguments"] = arguments }) };
        using var fixture = new Fixture(PlaygroundProviderKind.OpenAI, JsonResponse(response));
        var events = await Collect(fixture.Provider, Request(PlaygroundProviderKind.OpenAI) with { Tools = [Tool] });
        Assert.DoesNotContain(events, e => e.Kind is PlaygroundProviderEventKind.Completed or PlaygroundProviderEventKind.ToolCall or PlaygroundProviderEventKind.Continuation);
        Assert.Equal(PlaygroundFailureKind.InvalidResponse, Assert.Single(events).Failure!.Kind);
    }

    [Fact]
    public async Task TokenLimitedReplyKeepsTextButCannotExecuteEvenCompleteToolArguments()
    {
        var response = new JsonObject { ["status"] = "incomplete", ["output"] = new JsonArray(Message("partial"), new JsonObject { ["type"] = "function_call", ["call_id"] = "call-1", ["name"] = Tool.Name, ["arguments"] = "{\"n\":1}" }) };
        using var fixture = new Fixture(PlaygroundProviderKind.OpenAI, JsonResponse(response));
        var events = await Collect(fixture.Provider, Request(PlaygroundProviderKind.OpenAI) with { Tools = [Tool] });
        Assert.Equal("partial", string.Concat(events.Where(e => e.Kind == PlaygroundProviderEventKind.Text).Select(e => e.Text)));
        Assert.Equal("max_tokens", Assert.Single(events, e => e.Kind == PlaygroundProviderEventKind.Completed).StopReason);
        Assert.DoesNotContain(events, e => e.Kind is PlaygroundProviderEventKind.ToolCall or PlaygroundProviderEventKind.Continuation);
    }

    [Fact]
    public async Task RefusalHasAnExplicitTerminalAndSearchWithoutSearchCannotClaimResearch()
    {
        var refusal = Message(""); refusal["content"] = new JsonArray(new JsonObject { ["type"] = "refusal", ["refusal"] = "Synthetic refusal" });
        using var fixture = new Fixture(PlaygroundProviderKind.OpenAI,
            JsonResponse(new JsonObject { ["status"] = "completed", ["output"] = new JsonArray(refusal) }),
            JsonResponse(new JsonObject { ["status"] = "completed", ["output"] = new JsonArray(Message("Model knowledge only")) }));
        var events = await Collect(fixture.Provider, Request(PlaygroundProviderKind.OpenAI));
        Assert.Contains(events, e => e.Kind == PlaygroundProviderEventKind.Refusal);
        Assert.Equal("refusal", events.Last().StopReason);
        var error = await Assert.ThrowsAsync<PlaygroundProviderException>(() => fixture.Provider.SearchAsync(new(Guid.NewGuid(), Guid.NewGuid(), PlaygroundProviderKind.OpenAI,
            fixture.Provider.DefaultEndpoint, Model(PlaygroundProviderKind.OpenAI), "public query"), CancellationToken.None));
        Assert.Equal(PlaygroundFailureKind.Unsupported, error.Failure.Kind);
    }

    [Fact]
    public async Task CitationOffsetsUseTheirTextPartAndUnsafeLinksAreExcluded()
    {
        var part1 = new JsonObject { ["type"] = "output_text", ["text"] = "First. " };
        var part2 = new JsonObject { ["type"] = "output_text", ["text"] = "Second.", ["annotations"] = new JsonArray(
            new JsonObject { ["type"] = "url_citation", ["url"] = "https://example.org/source", ["start_index"] = 0, ["end_index"] = 6 },
            new JsonObject { ["type"] = "url_citation", ["url"] = "http://127.0.0.1/private", ["start_index"] = 0, ["end_index"] = 6 },
            new JsonObject { ["type"] = "url_citation", ["url"] = "javascript:alert(1)", ["start_index"] = 0, ["end_index"] = 6 }) };
        using var fixture = new Fixture(PlaygroundProviderKind.OpenAI, JsonResponse(new JsonObject { ["status"] = "completed", ["output"] = new JsonArray(
            new JsonObject { ["type"] = "message", ["role"] = "assistant", ["content"] = new JsonArray(part1, part2) }) }));
        var events = await Collect(fixture.Provider, Request(PlaygroundProviderKind.OpenAI));
        var source = Assert.Single(events, e => e.Kind == PlaygroundProviderEventKind.Source).Source!;
        Assert.True(source.HasExactRange); Assert.Equal(7, source.Start); Assert.Equal(13, source.End);
    }

    [Fact]
    public async Task CancellationIsTypedAndDoesNotRetry()
    {
        using var fixture = new Fixture(PlaygroundProviderKind.OpenAI);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        var events = new List<PlaygroundProviderEvent>();
        await foreach (var item in fixture.Provider.SendAsync(Request(PlaygroundProviderKind.OpenAI), cancellation.Token)) events.Add(item);
        Assert.Equal(PlaygroundFailureKind.Cancelled, Assert.Single(events).Failure!.Kind); Assert.Empty(fixture.Handler.Requests);
    }

    [Theory]
    [InlineData(PlaygroundProviderKind.OpenAI)]
    [InlineData(PlaygroundProviderKind.DeepSeek)]
    public async Task OnlyDocumentedReasoningValuesAreSentForVerifiedModels(PlaygroundProviderKind kind)
    {
        using var fixture = new Fixture(kind, StreamResponse(TextStream(kind, "ok")), StreamResponse(TextStream(kind, "ok")));
        var capabilities = fixture.Provider.Capabilities(Model(kind), fixture.Provider.DefaultEndpoint);
        Assert.Contains("high", capabilities.ReasoningEfforts);
        _ = await Collect(fixture.Provider, Request(kind) with { ReasoningEffort = "high" });
        Assert.Equal("high", JsonNode.Parse(fixture.Handler.Requests[0].Body)!["reasoning"]!["effort"]!.GetValue<string>());
        _ = await Collect(fixture.Provider, Request(kind) with { ReasoningEffort = "unsupported-value" });
        Assert.Null(JsonNode.Parse(fixture.Handler.Requests[1].Body)!["reasoning"]);
    }

    [Fact]
    public async Task QuotaReportedAs429IsNotMisreportedAsTransientRateLimit()
    {
        using var fixture = new Fixture(PlaygroundProviderKind.OpenAI, new HttpResponseMessage(HttpStatusCode.TooManyRequests)
        { Content = new StringContent("""{"error":{"code":"insufficient_quota","message":"synthetic-fixture-key-never-real"}}""") });
        var events = await Collect(fixture.Provider, Request(PlaygroundProviderKind.OpenAI));
        var failure = Assert.Single(events).Failure!;
        Assert.Equal(PlaygroundFailureKind.Quota, failure.Kind); Assert.False(failure.Retryable);
        Assert.DoesNotContain(Fixture.Secret, failure.Message); Assert.Single(fixture.Handler.Requests);
    }

    [Theory]
    [InlineData(PlaygroundProviderKind.OpenAI)]
    [InlineData(PlaygroundProviderKind.Gemini)]
    [InlineData(PlaygroundProviderKind.Claude)]
    [InlineData(PlaygroundProviderKind.Grok)]
    [InlineData(PlaygroundProviderKind.DeepSeek)]
    public async Task ImagesUseEachNativeVisionBlockAndNeverBecomeTextOrRemoteUrls(PlaygroundProviderKind kind)
    {
        using var fixture = new Fixture(kind, StreamResponse(TextStream(kind, "看到图片")));
        var image = SyntheticImage();
        var request = Request(kind) with { Messages = [new("user", "请分析") { Attachments = [image] }] };
        Assert.True(fixture.Provider.Capabilities(request.Model, request.Endpoint).ImageInput);
        var events = await Collect(fixture.Provider, request);
        Assert.DoesNotContain(events, e => e.Kind == PlaygroundProviderEventKind.Error);
        var body = JsonNode.Parse(Assert.Single(fixture.Handler.Requests).Body)!;
        var parts = (JsonArray)body[kind == PlaygroundProviderKind.Claude ? "messages" : "input"]![0]!["content"]!;
        var part = Assert.Single(parts, p => p?["type"]?.GetValue<string>() == (kind is PlaygroundProviderKind.Gemini or PlaygroundProviderKind.Claude ? "image" : "input_image"))!;
        if (kind == PlaygroundProviderKind.Gemini) { Assert.Equal(image.ImageBase64, part["data"]!.GetValue<string>()); Assert.Equal("image/png", part["mime_type"]!.GetValue<string>()); }
        else if (kind == PlaygroundProviderKind.Claude) { Assert.Equal("base64", part["source"]!["type"]!.GetValue<string>()); Assert.Equal("image/png", part["source"]!["media_type"]!.GetValue<string>()); Assert.Equal(image.ImageBase64, part["source"]!["data"]!.GetValue<string>()); }
        else Assert.Equal("data:image/png;base64," + image.ImageBase64, part["image_url"]!.GetValue<string>());
        Assert.DoesNotContain(parts, p => p?["type"]?.GetValue<string>() is "input_text" or "text" && p?["text"]?.GetValue<string>().Contains(image.ImageBase64!, StringComparison.Ordinal) == true);
        Assert.DoesNotContain("__the_order_image", body.ToJsonString());
    }

    [Theory]
    [InlineData(PlaygroundProviderKind.OpenAI)]
    [InlineData(PlaygroundProviderKind.Gemini)]
    [InlineData(PlaygroundProviderKind.Claude)]
    [InlineData(PlaygroundProviderKind.Grok)]
    [InlineData(PlaygroundProviderKind.DeepSeek)]
    public async Task VisionToolContinuationKeepsOnlyFingerprintAndRestoresSameImage(PlaygroundProviderKind kind)
    {
        using var fixture = new Fixture(kind, StreamResponse(ToolStream(kind)), StreamResponse(TextStream(kind, "已完成")));
        var image = SyntheticImage(512, 512);
        Assert.True(image.ImageBase64!.Length > 512 * 1024);
        var request = Request(kind) with { Messages = [new("user", "比较库记录与图像") { Attachments = [image] }], Tools = [Tool] };
        var first = await Collect(fixture.Provider, request);
        var continuation = Assert.Single(first, e => e.Continuation is not null).Continuation!;
        Assert.True(continuation.Data.Length < 4096);
        Assert.Contains("__the_order_image", continuation.Data);
        Assert.DoesNotContain(image.ImageBase64!, continuation.Data);
        var calls = first.Where(e => e.ToolCall is not null).Select(e => e.ToolCall!).ToArray();
        var second = await Collect(fixture.Provider, request with { Continuation = continuation,
            ToolResults = calls.Select(c => new PlaygroundWireMessage("tool", "fixture result", c.Id, c.Name)).ToImmutableArray() });
        Assert.DoesNotContain(second, e => e.Kind == PlaygroundProviderEventKind.Error);
        Assert.Equal(2, fixture.Handler.Requests.Count);
        var sent = JsonNode.Parse(fixture.Handler.Requests[1].Body)!;
        var parts = (JsonArray)sent[kind == PlaygroundProviderKind.Claude ? "messages" : "input"]![0]!["content"]!;
        var part = Assert.Single(parts, p => p?["type"]?.GetValue<string>() == (kind is PlaygroundProviderKind.Gemini or PlaygroundProviderKind.Claude ? "image" : "input_image"))!;
        string actualImage = kind == PlaygroundProviderKind.Gemini ? part["data"]!.GetValue<string>()
            : kind == PlaygroundProviderKind.Claude ? part["source"]!["data"]!.GetValue<string>() : part["image_url"]!.GetValue<string>()["data:image/png;base64,".Length..];
        Assert.Equal(image.ImageBase64, actualImage);
        Assert.DoesNotContain("__the_order_image", fixture.Handler.Requests[1].Body);
    }

    [Fact]
    public async Task RemovedOrReplacedContinuationImageIsRejectedBeforeKeyOrNetwork()
    {
        using var fixture = new Fixture(PlaygroundProviderKind.OpenAI, StreamResponse(ToolStream(PlaygroundProviderKind.OpenAI)));
        var image = SyntheticImage();
        var request = Request(PlaygroundProviderKind.OpenAI) with { Messages = [new("user", "原问题") { Attachments = [image] }], Tools = [Tool] };
        var first = await Collect(fixture.Provider, request);
        var state = Assert.Single(first, e => e.Continuation is not null).Continuation!;
        foreach (var changed in new[] { request with { Messages = [new("user", "原问题")] },
            request with { Messages = [new("user", "原问题") { Attachments = [image with { Name = "replaced.png" }] }] },
            request with { Messages = [new("user", "原问题") { Attachments = [SyntheticImage(seed: 17)] }] },
            request with { TurnId = Guid.NewGuid() } })
        {
            var events = await Collect(fixture.Provider, changed with { Continuation = state });
            Assert.Single(events, e => e.Kind == PlaygroundProviderEventKind.Error);
        }
        Assert.Single(fixture.Handler.Requests);
        Assert.Single(fixture.Credentials.Reads);
    }

    [Theory]
    [InlineData(PlaygroundProviderKind.OpenAI)]
    [InlineData(PlaygroundProviderKind.Gemini)]
    [InlineData(PlaygroundProviderKind.Claude)]
    [InlineData(PlaygroundProviderKind.Grok)]
    [InlineData(PlaygroundProviderKind.DeepSeek)]
    public async Task UnverifiedVisionMalformedImagesAndPublicResearchNeverReadCredentials(PlaygroundProviderKind kind)
    {
        using var fixture = new Fixture(kind);
        var request = Request(kind) with { Messages = [new("user", "分析") { Attachments = [SyntheticImage()] }] };
        foreach (var invalid in new[] { request with { Model = "future-unverified-model" },
            request with { Phase = PlaygroundPhase.PublicResearch },
            request with { Messages = [new("assistant", "不允许模型附图") { Attachments = [SyntheticImage()] }] },
            request with { Messages = [new("user", "坏图") { Attachments = [SyntheticImage() with { ImageBase64 = "not-base64" }] }] } })
            Assert.Single(await Collect(fixture.Provider, invalid), e => e.Kind == PlaygroundProviderEventKind.Error);
        if (kind == PlaygroundProviderKind.DeepSeek)
        {
            Assert.False(fixture.Provider.Capabilities("deepseek-v4-pro", request.Endpoint).ImageInput);
            Assert.Single(await Collect(fixture.Provider, request with { Model = "deepseek-v4-pro" }), e => e.Kind == PlaygroundProviderEventKind.Error);
        }
        Assert.Empty(fixture.Handler.Requests); Assert.Empty(fixture.Credentials.Reads);
    }

    private static PlaygroundAttachment SyntheticImage(int width = 1, int height = 1, int seed = 11)
    {
        using var png = new MemoryStream(); png.Write([137, 80, 78, 71, 13, 10, 26, 10]);
        void Chunk(string name, byte[] data)
        {
            Span<byte> number = stackalloc byte[4]; BinaryPrimitives.WriteInt32BigEndian(number, data.Length); png.Write(number);
            byte[] type = Encoding.ASCII.GetBytes(name); png.Write(type); png.Write(data);
            uint crc = uint.MaxValue;
            foreach (byte value in type.Concat(data)) { crc ^= value; for (int bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0 : 0xedb88320u); }
            BinaryPrimitives.WriteUInt32BigEndian(number, ~crc); png.Write(number);
        }
        byte[] header = new byte[13]; BinaryPrimitives.WriteInt32BigEndian(header, width); BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height); header[8] = 8; header[9] = 6; Chunk("IHDR", header);
        var random = new Random(seed); byte[] row = new byte[1 + width * 4]; using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Fastest, true))
            for (int y = 0; y < height; y++) { random.NextBytes(row); row[0] = 0; zlib.Write(row); }
        Chunk("IDAT", compressed.ToArray()); Chunk("IEND", []);
        return new("image.png", "") { MediaType = "image/png", ImageBase64 = Convert.ToBase64String(png.ToArray()), ImageWidth = width, ImageHeight = height };
    }

    private static readonly PlaygroundToolDefinition Tool = new("search_library", "Synthetic read-only fixture", """{"type":"object","properties":{"n":{"type":"integer"}},"required":["n"],"additionalProperties":false}""");
    private static PlaygroundProviderRequest Request(PlaygroundProviderKind kind) => new(Guid.NewGuid(), Guid.NewGuid(), kind,
        PlaygroundProviderEndpoints.Default(kind), Model(kind), "Synthetic instructions", [new("user", "private-history-marker")]) { Binding = "synthetic-binding" };
    private static string Model(PlaygroundProviderKind kind) => kind switch
    { PlaygroundProviderKind.OpenAI => "gpt-6-astra", PlaygroundProviderKind.Gemini => "gemini-3.8-flash", PlaygroundProviderKind.Claude => "claude-opus-5-5", PlaygroundProviderKind.Grok => "grok-4.7", _ => "deepseek-flash" };
    private static string ExpectedPath(PlaygroundProviderKind kind) => kind switch
    { PlaygroundProviderKind.Gemini => "/v1beta/interactions", PlaygroundProviderKind.Claude => "/v1/messages", PlaygroundProviderKind.DeepSeek => "/responses", _ => "/v1/responses" };
    private static async Task<List<PlaygroundProviderEvent>> Collect(IPlaygroundProvider provider, PlaygroundProviderRequest request)
    { var result = new List<PlaygroundProviderEvent>(); await foreach (var item in provider.SendAsync(request, CancellationToken.None)) result.Add(item); return result; }
    private static string Event(JsonObject value)
    {
        string kind = value["type"]?.GetValue<string>() ?? value["event_type"]!.GetValue<string>();
        return "event: " + kind + "\r\ndata: " + value.ToJsonString(new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }) + "\r\n\r\n";
    }
    private static string TextStream(PlaygroundProviderKind kind, string text)
    {
        if (kind == PlaygroundProviderKind.Claude)
            return Event(new() { ["type"] = "message_start", ["message"] = new JsonObject { ["usage"] = new JsonObject { ["input_tokens"] = 17, ["output_tokens"] = 1 } } })
                + Event(new() { ["type"] = "content_block_start", ["index"] = 0, ["content_block"] = new JsonObject { ["type"] = "text", ["text"] = "" } })
                + Event(new() { ["type"] = "content_block_delta", ["index"] = 0, ["delta"] = new JsonObject { ["type"] = "text_delta", ["text"] = text } })
                + Event(new() { ["type"] = "content_block_stop", ["index"] = 0 })
                + Event(new() { ["type"] = "message_delta", ["delta"] = new JsonObject { ["stop_reason"] = "end_turn" }, ["usage"] = new JsonObject { ["output_tokens"] = 4 } })
                + Event(new() { ["type"] = "message_stop" });
        if (kind == PlaygroundProviderKind.Gemini)
            return Event(new() { ["event_type"] = "step.start", ["index"] = 0, ["step"] = new JsonObject { ["type"] = "model_output", ["content"] = new JsonArray() } })
                + Event(new() { ["event_type"] = "step.delta", ["index"] = 0, ["delta"] = new JsonObject { ["type"] = "text", ["text"] = text } })
                + Event(new() { ["event_type"] = "step.stop", ["index"] = 0 })
                + Event(new() { ["event_type"] = "interaction.status_update", ["status"] = "completed", ["usage"] = new JsonObject { ["total_input_tokens"] = 17, ["total_output_tokens"] = 4 } });
        return Event(new() { ["type"] = "response.output_text.delta", ["sequence_number"] = 1, ["delta"] = text })
            + Event(new() { ["type"] = "response.completed", ["sequence_number"] = 2, ["response"] = new JsonObject { ["status"] = "completed", ["output"] = new JsonArray(Message(text)), ["usage"] = new JsonObject { ["input_tokens"] = 17, ["output_tokens"] = 4 } } });
    }
    private static string ToolStream(PlaygroundProviderKind kind, int numberOfCalls = 3)
    {
        var stream = new StringBuilder(); var calls = new JsonArray();
        for (int i = 0; i < numberOfCalls; i++)
        {
            var call = kind == PlaygroundProviderKind.Claude
                ? new JsonObject { ["type"] = "tool_use", ["id"] = "call-" + i, ["name"] = Tool.Name, ["input"] = new JsonObject() }
                : kind == PlaygroundProviderKind.Gemini
                ? new JsonObject { ["type"] = "function_call", ["id"] = "call-" + i, ["name"] = Tool.Name, ["arguments"] = new JsonObject(), ["signature"] = "synthetic-signature" }
                : new JsonObject { ["type"] = "function_call", ["id"] = "item-" + i, ["call_id"] = "call-" + i, ["name"] = Tool.Name, ["arguments"] = "" };
            calls.Add(call.DeepClone());
            stream.Append(Event(kind == PlaygroundProviderKind.Claude ? new() { ["type"] = "content_block_start", ["index"] = i, ["content_block"] = call }
                : kind == PlaygroundProviderKind.Gemini ? new() { ["event_type"] = "step.start", ["index"] = i, ["step"] = call }
                : new() { ["type"] = "response.output_item.added", ["output_index"] = i, ["item"] = call }));
        }
        foreach (string fragment in new[] { "{\"n\":", "NUMBER", "}" })
            for (int i = 0; i < numberOfCalls; i++)
            {
                string value = fragment == "NUMBER" ? (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture) : fragment;
                stream.Append(Event(kind == PlaygroundProviderKind.Claude ? new() { ["type"] = "content_block_delta", ["index"] = i, ["delta"] = new JsonObject { ["type"] = "input_json_delta", ["partial_json"] = value } }
                    : kind == PlaygroundProviderKind.Gemini ? new() { ["event_type"] = "step.delta", ["index"] = i, ["delta"] = new JsonObject { ["type"] = "arguments_delta", ["arguments"] = value } }
                    : new() { ["type"] = "response.function_call_arguments.delta", ["output_index"] = i, ["delta"] = value }));
            }
        for (int i = 0; i < numberOfCalls; i++)
        {
            if (kind == PlaygroundProviderKind.Claude) stream.Append(Event(new() { ["type"] = "content_block_stop", ["index"] = i }));
            else if (kind == PlaygroundProviderKind.Gemini) stream.Append(Event(new() { ["event_type"] = "step.stop", ["index"] = i }));
            else calls[i]!["arguments"] = "{\"n\":" + (i + 1) + "}";
        }
        if (kind == PlaygroundProviderKind.Claude)
            stream.Append(Event(new() { ["type"] = "message_delta", ["delta"] = new JsonObject { ["stop_reason"] = "tool_use" } })).Append(Event(new() { ["type"] = "message_stop" }));
        else if (kind == PlaygroundProviderKind.Gemini) stream.Append(Event(new() { ["event_type"] = "interaction.status_update", ["status"] = "requires_action" }));
        else stream.Append(Event(new() { ["type"] = "response.completed", ["response"] = new JsonObject { ["status"] = "completed", ["output"] = calls } }));
        return stream.ToString();
    }
    [Fact] public async Task EmptySearchStillCountsTheExecutedRequest()
    {
        using var fixture = new Fixture(PlaygroundProviderKind.DeepSeek, JsonResponse(new JsonObject { ["results"] = new JsonArray() }));
        var result = await fixture.Provider.SearchAsync(new(Guid.NewGuid(), Guid.NewGuid(), PlaygroundProviderKind.DeepSeek,
            fixture.Provider.DefaultEndpoint, Model(PlaygroundProviderKind.DeepSeek), "synthetic public title", 1), default);
        Assert.Empty(result.Sources); Assert.Equal(1, result.Usage!.Searches); Assert.Single(fixture.Handler.Requests);
    }
    private static JsonObject Message(string text) => new() { ["type"] = "message", ["role"] = "assistant", ["content"] = new JsonArray(new JsonObject { ["type"] = "output_text", ["text"] = text }) };
    private static JsonObject SearchResult(PlaygroundProviderKind kind)
    {
        const string url = "https://example.org/official";
        if (kind == PlaygroundProviderKind.DeepSeek) return new() { ["results"] = new JsonArray(new JsonObject { ["title"] = "Official fixture", ["url"] = url, ["snippet"] = "Synthetic public source." }) };
        if (kind == PlaygroundProviderKind.Gemini) return new() { ["status"] = "completed", ["steps"] = new JsonArray(
            new JsonObject { ["type"] = "google_search_call", ["id"] = "search-1", ["arguments"] = new JsonObject { ["queries"] = new JsonArray("synthetic public release date") } },
            new JsonObject { ["type"] = "google_search_result", ["call_id"] = "search-1", ["search_suggestions"] = "<div>synthetic Google Search suggestions</div>", ["result"] = new JsonArray(new JsonObject { ["title"] = "Official fixture", ["url"] = url }) },
            new JsonObject { ["type"] = "model_output", ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = "已查询资料", ["annotations"] = new JsonArray(new JsonObject { ["type"] = "url_citation", ["url"] = url, ["title"] = "Official fixture", ["start_index"] = 0, ["end_index"] = 15 }) }) }) };
        if (kind == PlaygroundProviderKind.Claude) return new() { ["stop_reason"] = "end_turn", ["content"] = new JsonArray(
            new JsonObject { ["type"] = "server_tool_use", ["name"] = "web_search", ["id"] = "search-1", ["input"] = new JsonObject { ["query"] = "synthetic public release date" } },
            new JsonObject { ["type"] = "web_search_tool_result", ["tool_use_id"] = "search-1", ["content"] = new JsonArray(new JsonObject { ["type"] = "web_search_result", ["url"] = url, ["title"] = "Official fixture", ["encrypted_content"] = "synthetic-encrypted" }) },
            new JsonObject { ["type"] = "text", ["text"] = "Public source.", ["citations"] = new JsonArray(new JsonObject { ["type"] = "web_search_result_location", ["url"] = url, ["title"] = "Official fixture", ["cited_text"] = "Synthetic public source." }) }),
            ["usage"] = new JsonObject { ["input_tokens"] = 20, ["output_tokens"] = 7, ["server_tool_use"] = new JsonObject { ["web_search_requests"] = 1 } } };
        var message = Message("Public source.");
        message["content"]![0]!["annotations"] = new JsonArray(new JsonObject { ["type"] = "url_citation", ["url"] = url, ["title"] = "Official fixture", ["start_index"] = 0, ["end_index"] = 6 });
        return new() { ["status"] = "completed", ["output"] = new JsonArray(new JsonObject { ["type"] = "web_search_call", ["id"] = "search-1", ["status"] = "completed", ["action"] = new JsonObject { ["type"] = "search", ["sources"] = new JsonArray(new JsonObject { ["type"] = "url", ["url"] = url }) } }, message),
            ["usage"] = new JsonObject { ["input_tokens"] = 20, ["output_tokens"] = 7 } };
    }
    private static HttpResponseMessage JsonResponse(JsonNode json) => new(HttpStatusCode.OK) { Content = new StringContent(json.ToJsonString(), Encoding.UTF8, "application/json") };
    private static HttpResponseMessage StreamResponse(string value, int chunk = 3)
    { var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new FragmentedStream(Encoding.UTF8.GetBytes(value), chunk)) }; response.Content.Headers.ContentType = new("text/event-stream"); return response; }
    private sealed record Captured(Uri Uri, string Body, string Authentication, string ApiVersion);
    private sealed class Fixture : IDisposable
    {
        public const string Secret = "synthetic-fixture-key-never-real";
        public FixtureCredentials Credentials { get; } = new();
        public FixtureHandler Handler { get; }
        private readonly HttpClient client;
        public IPlaygroundProvider Provider { get; }
        public Fixture(PlaygroundProviderKind kind, params HttpResponseMessage[] responses)
        { Handler = new(responses); client = new(Handler); Provider = new PlaygroundProviderRegistry(client, Credentials).Providers.Single(p => p.Kind == kind); }
        public void Dispose() => client.Dispose();
    }
    private sealed class FixtureCredentials : IPlaygroundCredentials
    {
        public List<(PlaygroundProviderKind Provider, string Endpoint)> Reads { get; } = [];
        public Task<bool> HasAsync(PlaygroundProviderKind provider, string endpoint, CancellationToken cancellation) => Task.FromResult(true);
        public Task<string?> ReadAsync(PlaygroundProviderKind provider, string endpoint, CancellationToken cancellation)
        { Reads.Add((provider, endpoint)); return Task.FromResult<string?>(Fixture.Secret); }
        public Task SaveAsync(PlaygroundProviderKind provider, string endpoint, string key, CancellationToken cancellation) => throw new NotSupportedException();
        public Task ClearAsync(PlaygroundProviderKind provider, string endpoint, CancellationToken cancellation) => throw new NotSupportedException();
    }
    private sealed class FixtureHandler(params HttpResponseMessage[] responses) : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> pending = new(responses);
        public List<Captured> Requests { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(new(request.RequestUri!, request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken),
                request.Headers.TryGetValues("x-goog-api-key", out var google) ? google.Single() : request.Headers.Authorization?.ToString() ?? "",
                request.Headers.TryGetValues("anthropic-version", out var version) ? version.Single() : ""));
            if (!pending.TryDequeue(out var response)) throw new InvalidOperationException("Unexpected synthetic request; no network is available.");
            response.RequestMessage ??= request; return response;
        }
    }
    private sealed class FragmentedStream(byte[] bytes, int maximumChunk) : MemoryStream(bytes, false)
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => base.ReadAsync(buffer[..Math.Min(buffer.Length, maximumChunk)], cancellationToken);
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => base.ReadAsync(buffer, offset, Math.Min(count, maximumChunk), cancellationToken);
    }
}
