using TheOrder.Application.Playground;
using Xunit;

namespace TheOrder.Tests;

public sealed class PlaygroundPricingTests
{
    private static readonly DateTimeOffset October = new(2026, 10, 31, 12, 0, 0, TimeSpan.Zero);
    [Theory]
    [InlineData(PlaygroundProviderKind.OpenAI, "gpt-6.1-sol", 2, 10)]
    [InlineData(PlaygroundProviderKind.OpenAI, "gpt-6-astra", 10, 50)]
    [InlineData(PlaygroundProviderKind.Claude, "claude-fable-5-1", 10, 50)]
    [InlineData(PlaygroundProviderKind.Claude, "claude-sonnet-5-5", 2, 10)]
    [InlineData(PlaygroundProviderKind.Grok, "grok-4.7", 2, 6)]
    public void CatalogQuotesVerifiedExactIdsAndUnits(PlaygroundProviderKind provider, string model, int input, int output)
    {
        var quote = Assert.IsType<PlaygroundPriceQuote>(PlaygroundPricing.Quote(provider, model, October));
        Assert.Equal(input, quote.InputPerMillion); Assert.Equal(output, quote.OutputPerMillion);
        Assert.Equal("USD", quote.Currency); Assert.StartsWith("https://", quote.SourceUrl);
        Assert.Contains("百万 token", PlaygroundPricing.Describe(provider, model, October));
        Assert.Null(PlaygroundPricing.Quote(provider, model + "-unverified", October));
        Assert.Contains("尚未核实", PlaygroundPricing.Describe(provider, model + "-unverified", October));
    }
    [Theory]
    [InlineData("gemini-4-argon", "2026-11-30T23:59:59Z", 2, 10)]
    [InlineData("gemini-4-argon", "2026-12-01T00:00:00Z", 4, 20)]
    [InlineData("gemini-3.8-flash", "2026-12-31T23:59:59Z", .75, 3.75)]
    [InlineData("gemini-3.8-flash", "2027-01-01T00:00:00Z", 1.5, 7.5)]
    public void GeminiPromotionUsesRequestDate(string model, string date, double input, double output)
    {
        var quote = Assert.IsType<PlaygroundPriceQuote>(PlaygroundPricing.Quote(PlaygroundProviderKind.Gemini, model, DateTimeOffset.Parse(date)));
        Assert.Equal((decimal)input, quote.InputPerMillion); Assert.Equal((decimal)output, quote.OutputPerMillion);
    }
    [Fact]
    public void CachedInputAndThinkingAreNotDoubleCharged()
    {
        var cost = PlaygroundPricing.Calculate(PlaygroundProviderKind.OpenAI, "gpt-6.1-sol", new(1000, 100, 80, 400), October);
        Assert.Equal(.00224m, cost.Minimum); Assert.Equal(cost.Minimum, cost.Maximum); Assert.False(cost.IsActual); Assert.True(cost.IsComplete);
        var google = PlaygroundPricing.Calculate(PlaygroundProviderKind.Gemini, "gemini-3.8-flash", new(1000, 188, 181, 0), October);
        Assert.Equal(.001455m, google.Minimum);
    }
    [Fact]
    public void CacheWriteLifetimesAreSeparateInputBuckets()
    {
        var openAi = PlaygroundPricing.Calculate(PlaygroundProviderKind.OpenAI, "gpt-6.1-sol", new(1000, 0, 0, 100)
            { CacheWriteInputTokens = 400, CacheWrite30mInputTokens = 300, CacheWrite12hInputTokens = 100 }, October);
        Assert.Equal(.00226m, openAi.Minimum); Assert.Equal(openAi.Minimum, openAi.Maximum);
        var claude = PlaygroundPricing.Calculate(PlaygroundProviderKind.Claude, "claude-opus-5-5", new(1000, 0, 0, 100)
            { CacheWriteInputTokens = 400, CacheWrite5mInputTokens = 300, CacheWrite1hInputTokens = 100 }, October);
        Assert.Equal(.00432m, claude.Minimum); Assert.Equal(claude.Minimum, claude.Maximum);
        var unknownLifetime = PlaygroundPricing.Calculate(PlaygroundProviderKind.Claude, "claude-opus-5-5", new(1000, 0, 0, 0)
            { CacheWriteInputTokens = 1000 }, October);
        Assert.Equal(.005m, unknownLifetime.Minimum); Assert.Equal(.008m, unknownLifetime.Maximum);
    }
    [Theory]
    [InlineData(272000, .554)]
    [InlineData(272001, 1.103004)]
    public void OpenAiLongThresholdIsAppliedToWholeIndividualRequest(long input, double expected)
    {
        var cost = PlaygroundPricing.Calculate(PlaygroundProviderKind.OpenAI, "gpt-6.1-sol", new(input, 1000, 500, 0), October);
        Assert.Equal((decimal)expected, cost.Minimum); Assert.Equal(cost.Minimum, cost.Maximum);
    }
    [Fact]
    public void OtherProviderLongThresholdsRemainDistinct()
    {
        var grok = PlaygroundPricing.Calculate(PlaygroundProviderKind.Grok, "grok-4.7", new(200000, 1000, 0, 0), October);
        Assert.Equal(.812m, grok.Minimum);
        var haiku = PlaygroundPricing.Calculate(PlaygroundProviderKind.Claude, "claude-haiku-5-5", new(100001, 1000, 0, 1), October);
        Assert.Equal(.05250005m, haiku.Minimum);
        var opus = PlaygroundPricing.Calculate(PlaygroundProviderKind.Claude, "claude-opus-5-5", new(900000, 1000, 0, 0), October);
        Assert.Equal(3.62m, opus.Minimum);
    }
    [Fact]
    public void ServerReportedCostOverridesAllEstimatedTokensAndSearches()
    {
        var actual = PlaygroundPricing.Calculate(PlaygroundProviderKind.Grok, "grok-4.7",
            new(1000000, 100000, 50000, 0, 80) { ReportedCostUsd = .0037756m }, October, PlaygroundChargeKind.PublicSearch);
        Assert.True(actual.IsActual); Assert.True(actual.IsComplete); Assert.Equal(.0037756m, actual.Minimum);
        Assert.Equal(actual.Minimum, actual.Maximum); Assert.StartsWith("实际 US$", PlaygroundPricing.Format(actual));
    }
    [Fact]
    public void SearchFeesDoNotAssumeGoogleFreeAllowanceOrReplaceResearchModel()
    {
        var google = PlaygroundPricing.Calculate(PlaygroundProviderKind.Gemini, "gemini-4-argon",
            new(1000, 1000, 0, 0, 2), October, PlaygroundChargeKind.PublicSearch);
        Assert.Equal(.012m, google.Minimum); Assert.Equal(.04m, google.Maximum);
        var otherModel = PlaygroundPricing.Calculate(PlaygroundProviderKind.OpenAI, "gpt-6-astra",
            new(1000, 1000, 0, 0, 2) { BillingModel = "gpt-6.1-sol" }, October, PlaygroundChargeKind.PublicSearch);
        Assert.Equal(.032m, otherModel.Minimum);
        var missingSearches = PlaygroundPricing.Calculate(PlaygroundProviderKind.OpenAI, "gpt-6.1-sol",
            new(1000, 1000, 0, 0), October, PlaygroundChargeKind.PublicSearch);
        Assert.False(missingSearches.IsComplete); Assert.Contains("部分估算", PlaygroundPricing.Format(missingSearches));
    }
    [Fact]
    public void DeepSeekPeakWindowsAreCertainOffPeakButUncertainChineseHolidays()
    {
        var weekend = PlaygroundPricing.Calculate(PlaygroundProviderKind.DeepSeek, "deepseek-flash", new(1000000, 1000000, 0, 0), October);
        Assert.Equal(.75m, weekend.Minimum); Assert.Equal(weekend.Minimum, weekend.Maximum);
        var weekday = PlaygroundPricing.Calculate(PlaygroundProviderKind.DeepSeek, "deepseek-flash", new(1000000, 1000000, 0, 0), DateTimeOffset.Parse("2026-11-02T02:00:00Z"));
        Assert.Equal(.75m, weekday.Minimum); Assert.Equal(1.5m, weekday.Maximum); Assert.Contains("节假日", weekday.Notice);
    }
    [Fact]
    public void DeepSeekSearchAndFetchPromotionCutoffUsesSuccessfulRequestsWithoutModelTokens()
    {
        var free = PlaygroundPricing.Calculate(PlaygroundProviderKind.DeepSeek, "deepseek-flash", new(Searches: 3),
            DateTimeOffset.Parse("2026-10-31T15:59:59Z"), PlaygroundChargeKind.PublicSearch);
        var paid = PlaygroundPricing.Calculate(PlaygroundProviderKind.DeepSeek, "deepseek-v4-pro", new(Fetches: 2),
            DateTimeOffset.Parse("2026-10-31T16:00:00Z"), PlaygroundChargeKind.PublicFetch);
        Assert.Equal(0m, free.Minimum); Assert.True(free.IsComplete); Assert.False(free.IsActual);
        Assert.Equal(.0015m, paid.Minimum); Assert.Equal(paid.Minimum, paid.Maximum);
    }
    [Fact]
    public void MissingUsageUnknownModelsAndInvalidMetadataNeverTurnIntoAFreeBill()
    {
        Assert.Null(PlaygroundPricing.Calculate(PlaygroundProviderKind.OpenAI, "new-model", new(1000, 1000), October).Minimum);
        Assert.Equal("费用未知", PlaygroundPricing.Format(PlaygroundPricing.Calculate(PlaygroundProviderKind.OpenAI, "gpt-6.1-sol", null, October)));
        Assert.False(PlaygroundPricing.IsValidUsage(new(-1, 0)));
        Assert.False(PlaygroundPricing.IsValidUsage(new(100, 2, 3)));
        Assert.False(PlaygroundPricing.IsValidUsage(new(10, 10, 0, 11)));
        Assert.False(PlaygroundPricing.IsValidUsage(new(10, 10) { CacheWriteInputTokens = 1, CacheWrite12hInputTokens = 2 }));
        Assert.False(PlaygroundPricing.IsValidCost(new("USD", -1, -1, true, true, "invalid")));
    }
    [Fact]
    public void OldAggregatedMessagesAreExplicitlyPartialAndCannotPretendToBeAnInvoice()
    {
        var message = new PlaygroundMessage(Guid.NewGuid(), Guid.NewGuid(), "assistant", "旧回复", October, PlaygroundMessageStatus.Complete)
        { Provider = PlaygroundProviderKind.OpenAI, Model = "gpt-6.1-sol", Usage = new(400000, 1000, 0, 0) };
        var cost = PlaygroundPricing.ForMessage(message); Assert.False(cost.IsActual); Assert.False(cost.IsComplete);
        Assert.Contains("旧记录", cost.Notice); Assert.Contains("逐请求", cost.Notice); Assert.Contains("估算", PlaygroundPricing.Format(cost));
        var exact = new PlaygroundCostEstimate("USD", .003m, .003m, true, true, "服务商报告");
        Assert.Equal(exact, PlaygroundPricing.ForMessage(message with { Provider = PlaygroundProviderKind.Grok,
            Usage = message.Usage! with { ReportedCostUsd = .003m }, Cost = exact }));
        Assert.False(PlaygroundPricing.ForMessage(message with { Cost = exact }).IsActual);
    }
    [Fact]
    public void LegacyClaudeIndependentCacheBucketLoadsWithoutChangingNativeUsageRulesOrRecord()
    {
        var usage = new PlaygroundUsage(100, 10, 5, 200);
        var message = new PlaygroundMessage(Guid.NewGuid(), Guid.NewGuid(), "assistant", "旧Claude讨论", October, PlaygroundMessageStatus.Complete)
        { Provider = PlaygroundProviderKind.Claude, Model = "claude-opus-4-6", Usage = usage };
        Assert.True(PlaygroundPricing.IsLegacyClaudeUsage(message)); Assert.True(PlaygroundPricing.IsValidStoredUsage(message));
        Assert.False(PlaygroundPricing.IsValidUsage(usage));
        var cost = PlaygroundPricing.ForMessage(message); Assert.Equal(.00085m, cost.Minimum);
        Assert.False(cost.IsActual); Assert.False(cost.IsComplete); Assert.Contains("部分估算", PlaygroundPricing.Format(cost));
        Assert.Contains("缓存写入", cost.Notice); Assert.Equal(100, message.Usage!.InputTokens); Assert.Equal(200, message.Usage.CachedInputTokens);
        Assert.Null(message.Cost); Assert.Null(message.Usage.BillingModel);
    }
    [Fact]
    public void NewClaudeShapesAndInvalidLegacyNumbersCannotUseTheOldBucketException()
    {
        var message = new PlaygroundMessage(Guid.NewGuid(), Guid.NewGuid(), "assistant", "旧Claude讨论", October, PlaygroundMessageStatus.Complete)
        { Provider = PlaygroundProviderKind.Claude, Model = "claude-opus-4-6", Usage = new(100, 10, 5, 200) };
        Assert.False(PlaygroundPricing.IsValidStoredUsage(message with { Usage = message.Usage! with { BillingModel = "claude-opus-4-6" } }));
        Assert.False(PlaygroundPricing.IsValidStoredUsage(message with { Cost = PlaygroundPricing.Unknown("new attempt") }));
        Assert.False(PlaygroundPricing.IsValidStoredUsage(message with { Provider = PlaygroundProviderKind.OpenAI }));
        Assert.False(PlaygroundPricing.IsValidStoredUsage(message with { Role = "user" }));
        Assert.False(PlaygroundPricing.IsValidStoredUsage(message with { Usage = message.Usage! with { CachedInputTokens = -1 } }));
        Assert.False(PlaygroundPricing.IsValidStoredUsage(message with { Usage = message.Usage! with { InputTokens = -1 } }));
    }
}

public sealed partial class PlaygroundCoreTests
{
    [Fact]
    public async Task CoreInitializationKeepsLegacyClaudeChatAndIndependentUsageBucketsWithoutMigration()
    {
        var store = new Store(); var provider = new Provider(PlaygroundProviderKind.Claude);
        var conversation = Guid.NewGuid(); var at = DateTimeOffset.Parse("2026-10-30T00:00:00Z");
        var message = new PlaygroundMessage(Guid.NewGuid(), conversation, "assistant", "LEGACY-CLAUDE-CHAT-CANARY", at, PlaygroundMessageStatus.Complete)
        { Provider = provider.Kind, Model = "claude-opus-4-6", Usage = new(100, 10, 5, 200) };
        store.Value = new() { Identity = store.Identity,
            Conversations = [new(conversation, "旧Claude会话", at, at, new() { Provider = provider.Kind, Endpoint = provider.DefaultEndpoint, Model = "claude-opus-4-6" })],
            Messages = [message] };
        await using var service = new PlaygroundService(store, new Credentials(), [provider], () => throw new InvalidOperationException("no library access"));
        await service.InitializeAsync(); var restored = Assert.Single(service.Snapshot.Messages);
        Assert.Equal(message, restored); Assert.Equal(100, restored.Usage!.InputTokens); Assert.Equal(200, restored.Usage.CachedInputTokens);
        Assert.Null(restored.Cost); Assert.Empty(store.Writes); Assert.Empty(provider.Requests);
    }
    [Fact]
    public async Task CostsAreCalculatedPerHttpRequestBeforeSummingAcrossToolRounds()
    {
        await using var r = new Rig(); var id = await r.Ready(new(Library: true));
        await r.Service.UpdateConversationAsync(id, r.Service.Snapshot.Conversations.Single().Options with { Model = "gpt-6.1-sol" });
        r.Provider.Reply = request => (request.ToolResults.IsEmpty
            ? ToolAnswer(request, "recent_play", new { limit = 1 }, "unused") : Answer("已完成。")).Prepend(
                new(PlaygroundProviderEventKind.Usage) { Usage = new(200000, 10, 5, 0) });
        await r.Service.SendAsync(id, "查看当前记录。");
        var cost = Assert.IsType<PlaygroundCostEstimate>(Last(r).Cost);
        Assert.Equal(.8002m, cost.Minimum); Assert.Equal(cost.Minimum, cost.Maximum); Assert.True(cost.IsComplete);
        Assert.Equal(400000, Last(r).Usage!.InputTokens); Assert.Equal(cost, r.Store.Value!.Messages.Last().Cost);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedStreamsRetainReportedCostOnceAndMissingUsageStaysUnknown(bool usage)
    {
        var provider = new Provider(PlaygroundProviderKind.Grok); await using var r = new Rig(provider); var id = await r.Ready();
        await r.Service.UpdateConversationAsync(id, r.Service.Snapshot.Conversations.Single().Options with { Model = "grok-4.7" });
        provider.Reply = _ => (usage ? new[] { new PlaygroundProviderEvent(PlaygroundProviderEventKind.Usage)
            { Usage = new(20, 4, 1, 0) { ReportedCostUsd = .004m } } } : []).Concat(
                [new PlaygroundProviderEvent(PlaygroundProviderEventKind.Error) { Failure = new(PlaygroundFailureKind.Network, "合成失败") }]);
        await r.Service.SendAsync(id, "一般讨论。"); Assert.Equal(PlaygroundMessageStatus.Failed, Last(r).Status);
        var cost = Assert.IsType<PlaygroundCostEstimate>(Last(r).Cost);
        Assert.Equal(usage, cost.IsActual); Assert.Equal(usage ? .004m : (decimal?)null, cost.Minimum);
        Assert.Single(provider.Requests);
    }
    [Fact]
    public async Task StopRejectsLateTextButKeepsLateProviderBillingOnTheOriginalAttempt()
    {
        var provider = new Provider(PlaygroundProviderKind.Grok) { Block = true, IgnoreCancellation = true };
        await using var r = new Rig(provider); var id = await r.Ready();
        await r.Service.UpdateConversationAsync(id, r.Service.Snapshot.Conversations.Single().Options with { Model = "grok-4.7" });
        provider.Reply = _ => Answer("不得显示迟到正文").Prepend(new(PlaygroundProviderEventKind.Usage)
            { Usage = new(20, 4, 1, 0) { ReportedCostUsd = .004m } });
        var send = r.Service.SendAsync(id, "一般讨论。"); await provider.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        r.Service.Stop(); provider.Release.TrySetResult(); await send;
        Assert.Equal(PlaygroundMessageStatus.Cancelled, Last(r).Status); Assert.DoesNotContain("迟到正文", Last(r).Text);
        var cost = Assert.IsType<PlaygroundCostEstimate>(Last(r).Cost); Assert.True(cost.IsActual); Assert.Equal(.004m, cost.Minimum);
        Assert.Single(provider.Requests);
    }
}
