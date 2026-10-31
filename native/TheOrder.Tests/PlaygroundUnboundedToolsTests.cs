using System.Text.Json;
using TheOrder.Application.Playground;
using Xunit;

namespace TheOrder.Tests;

public sealed partial class PlaygroundCoreTests
{
    private static IEnumerable<PlaygroundProviderEvent> AnotherLocalRound(PlaygroundProviderRequest request, string id, string tape = "fixture-turn-state")
        => [new(PlaygroundProviderEventKind.Continuation) { Continuation = new(request.Binding, tape) },
            new(PlaygroundProviderEventKind.ToolCall) { ToolCall = new(id, "recent_play", "{\"limit\":1}") },
            new(PlaygroundProviderEventKind.Completed) { StopReason = "tool_calls" }];

    [Theory] [InlineData(0)] [InlineData(1)] [InlineData(6)]
    public async Task ToolRoundsCanExceedFormerLimitsAndIgnorePreservedLegacyCounter(int legacy)
    {
        var provider = new Provider(); await using var r = new Rig(provider); var id = await r.Ready(new(Library: true));
        await r.Service.ConfigureAsync(r.Service.Snapshot.Settings with { MaximumToolRounds = legacy });
        provider.Reply = request => provider.Requests.Count <= 18 ? AnotherLocalRound(request, "call-" + provider.Requests.Count) : Answer("十九次请求后正常完成。");
        await r.Service.SendAsync(id, "检查当前资料。");
        Assert.Equal(19, provider.Requests.Count); Assert.Equal(PlaygroundMessageStatus.Complete, Last(r).Status);
        Assert.All(provider.Requests, request => Assert.Equal(0, request.MaximumToolCalls));
        Assert.Equal(legacy, r.Store.Value!.Settings.MaximumToolRounds);
    }
    [Fact] public async Task OneResponseCanReturnMoreThanTwelvePermittedLocalCalls()
    {
        var provider = new Provider(); await using var r = new Rig(provider); var id = await r.Ready(new(Library: true));
        provider.Reply = request => request.Continuation is null
            ? new[] { new PlaygroundProviderEvent(PlaygroundProviderEventKind.Continuation) { Continuation = new(request.Binding, "fixture-state") } }
                .Concat(Enumerable.Range(0, 24).Select(n => new PlaygroundProviderEvent(PlaygroundProviderEventKind.ToolCall)
                    { ToolCall = new("parallel-" + n, "recent_play", "{\"limit\":1}") }))
                .Append(new(PlaygroundProviderEventKind.Completed) { StopReason = "tool_calls" })
            : Answer("全部已核对。");
        await r.Service.SendAsync(id, "检查当前资料。");
        Assert.Equal(2, provider.Requests.Count); Assert.Equal(24, provider.Requests[1].ToolResults.Length);
        Assert.Equal(PlaygroundMessageStatus.Complete, Last(r).Status);
    }
    [Fact] public async Task StopStillTerminatesAContinuingToolChainBeyondFormerLimit()
    {
        var provider = new Provider(); await using var r = new Rig(provider); var id = await r.Ready(new(Library: true));
        await r.Service.ConfigureAsync(r.Service.Snapshot.Settings with { MaximumToolRounds = 1 });
        provider.Reply = request =>
        {
            if (provider.Requests.Count == 17) r.Service.Stop();
            return AnotherLocalRound(request, "continue-" + provider.Requests.Count);
        };
        await r.Service.SendAsync(id, "检查当前资料。");
        Assert.Equal(17, provider.Requests.Count); Assert.Equal(PlaygroundMessageStatus.Cancelled, Last(r).Status);
        Assert.False(r.Service.IsGenerating);
    }
    [Fact] public async Task ContextBudgetStillStopsBeforeAnotherRequestWithoutCountingToolRounds()
    {
        var provider = new Provider(); await using var r = new Rig(provider); var id = await r.Ready(new(Library: true));
        await r.Service.ConfigureAsync(r.Service.Snapshot.Settings with { MaximumContextCharacters = 12000, MaximumToolRounds = 0 });
        provider.Reply = request => AnotherLocalRound(request, "large", new string('x', 30000));
        await r.Service.SendAsync(id, "检查当前资料。");
        Assert.Single(provider.Requests); Assert.Equal(PlaygroundMessageStatus.Failed, Last(r).Status);
        Assert.Contains("上下文", Last(r).Error);
    }
    [Theory] [InlineData(0, 0)] [InlineData(7, 12)] [InlineData(int.MaxValue, int.MaxValue)]
    public async Task LegacySearchValuesPersistButDoNotSetUpstreamInvocationCounts(int smart, int deep)
    {
        await using var r = new Rig(); var id = await r.Ready(new(Library: true));
        await r.Service.ConfigureAsync(r.Service.Snapshot.Settings with { MaximumSearches = smart, DeepMaximumSearches = deep });
        await r.Service.UpdateConversationAsync(id, r.Service.Snapshot.Conversations.Single().Options with { Search = PlaygroundSearchMode.Deep });
        r.Provider.Reply = request => ToolAnswer(request, "public_search", new { }, "本轮由模型选择核对公开资料。");
        await r.Service.SendAsync(id, "请核实《魔女的夜宴》的访谈。");
        Assert.Equal(0, Assert.Single(r.Provider.Searches).MaximumSearches);
        Assert.Equal(smart, r.Store.Value!.Settings.MaximumSearches); Assert.Equal(deep, r.Store.Value.Settings.DeepMaximumSearches);
        Assert.Equal(PlaygroundMessageStatus.Complete, Last(r).Status);
    }
}
