using System.Collections.Immutable;
using System.Text.Json;
using TheOrder.Application;
using TheOrder.Application.Playground;
using Xunit;

namespace TheOrder.Tests;

public sealed partial class PlaygroundCoreTests
{
    private static async Task<Guid> ReadyNativeTape(Rig rig)
    {
        var id = await rig.Ready();
        await rig.Service.UpdateConversationAsync(id, rig.Service.Snapshot.Conversations.Single(c => c.Id == id).Options with { Search = PlaygroundSearchMode.Smart });
        return id;
    }
    private static Provider NativeTapeProvider(bool threeToolRounds = false)
    {
        var rounds = new Dictionary<Guid, int>();
        return new(PlaygroundProviderKind.Gemini) { Reply = request =>
        {
            int round = rounds.GetValueOrDefault(request.TurnId); rounds[request.TurnId] = round + 1;
            if (threeToolRounds && round < 3)
            {
                var call = round switch
                {
                    0 => new PlaygroundToolCall("lookup", "search_library", "{\"query\":\"魔女的夜宴\"}"),
                    1 => new PlaygroundToolCall("detail", "get_work", JsonSerializer.Serialize(new { work_key = PlaygroundService.WorkKeyFor(Entry()) })),
                    _ => new PlaygroundToolCall("history", "play_history", "{\"from\":\"2026-10-29T00:00:00Z\",\"to\":\"2026-10-30T00:00:00Z\",\"limit\":8}")
                };
                return [new(PlaygroundProviderEventKind.Continuation) { Continuation = new(request.Binding, "fixture-tool-tape") },
                    new(PlaygroundProviderEventKind.ToolCall) { ToolCall = call }, new(PlaygroundProviderEventKind.Completed) { StopReason = "tool_calls" }];
            }
            return [new(PlaygroundProviderEventKind.Text) { Text = "合成助手答复，未代填任何个人评分。" },
                new(PlaygroundProviderEventKind.ConversationTape) { ConversationTape = new(request.ConversationBinding, "PRIVATE-NATIVE-TAPE-" + request.TurnId) },
                new(PlaygroundProviderEventKind.Completed) { StopReason = "end_turn" }];
        } };
    }
    [Fact] public async Task HighDefaultsPreserveExplicitOldConfiguration()
    {
        Assert.Equal(65536, new PlaygroundSettings().MaximumOutputTokens); Assert.Equal(256000, new PlaygroundSettings().MaximumContextCharacters);
        Assert.Equal(900, new PlaygroundSettings().TimeoutSeconds); Assert.Equal("high", new PlaygroundConversationOptions().ReasoningEffort);
        await using var r = new Rig(); var id = await r.Ready();
        await r.Service.ConfigureAsync(r.Service.Snapshot.Settings with { MaximumOutputTokens = 1024, MaximumContextCharacters = 12000, TimeoutSeconds = 30 });
        await r.Service.UpdateConversationAsync(id, r.Service.Snapshot.Conversations.Single().Options with { ReasoningEffort = "low" });
        await r.Service.SendAsync(id, "讨论音乐。");
        Assert.Equal(1024, r.Provider.Requests.Last().MaximumOutputTokens); Assert.Equal("low", r.Provider.Requests.Last().ReasoningEffort);
        Assert.Equal(12000, r.Store.Value!.Settings.MaximumContextCharacters); Assert.Equal(30, r.Store.Value.Settings.TimeoutSeconds);
    }
    [Fact] public async Task NativeTwoUserTurnsRetainThreeToolRoundsAndRefreshNullRatingSources()
    {
        var provider = NativeTapeProvider(true); await using var r = new Rig(provider);
        r.Library = new([Entry() with { PersonalRating = null }], LibraryRecovery.Main);
        var id = await ReadyNativeTape(r);
        await r.Service.SendAsync(id, "请读取魔女的夜宴的记录。");
        Assert.Equal(4, provider.Requests.Count); Assert.Equal(PlaygroundMessageStatus.Complete, Last(r).Status);
        await r.Service.SendAsync(id, "然后继续核对一下。");
        Assert.Equal(8, provider.Requests.Count);
        var second = provider.Requests[4]; Assert.NotNull(second.ConversationTape);
        Assert.DoesNotContain(second.Messages, m => m.Role == "assistant");
        Assert.Contains("\"personal_rating\":null", string.Join("\n", second.Messages.Select(m => m.Text)));
        Assert.Contains("\"personal_rating\":null", provider.Requests[5].ToolResults.Single().Text);
        var source = Assert.Single(Last(r).Sources.Where(s => s.Kind == PlaygroundSourceKind.LocalRecord));
        Assert.Contains(source.Id, string.Join("\n", second.Messages.Select(m => m.Text)));
        Assert.DoesNotContain("PRIVATE-NATIVE-TAPE", JsonSerializer.Serialize(r.Store.Value));
    }
    [Fact] public async Task LibraryValueAndIdentityChangesDropNativeTapeAndUseFreshFacts()
    {
        var provider = NativeTapeProvider(); await using var r = new Rig(provider);
        r.Library = new([Entry() with { PersonalRating = null }], LibraryRecovery.Main); var id = await ReadyNativeTape(r);
        await r.Service.SendAsync(id, "说说魔女的夜宴。");
        r.Library = new([Entry() with { PersonalRating = 95 }], LibraryRecovery.Main);
        await r.Service.SendAsync(id, "继续。");
        Assert.Null(provider.Requests.Last().ConversationTape);
        Assert.Contains("\"personal_rating\":95", string.Join("\n", provider.Requests.Last().Messages.Select(m => m.Text)));
        var changed = Entry() with { Metadata = Entry().Metadata! with { Id = "different-work" } };
        r.Library = new([changed], LibraryRecovery.Main); await r.Service.SendAsync(id, "继续。");
        Assert.Null(provider.Requests.Last().ConversationTape);
        Assert.DoesNotContain(provider.Requests.Last().Messages, m => m.Role == "assistant");
    }
    [Theory] [InlineData("stop")] [InlineData("revoke")] [InlineData("model")] [InlineData("context")]
    public async Task NativeTapeInvalidatesAtUserAndPermissionBoundaries(string boundary)
    {
        var provider = NativeTapeProvider(); await using var r = new Rig(provider); var id = await ReadyNativeTape(r);
        await r.Service.SendAsync(id, "说说魔女的夜宴。");
        if (boundary == "stop") r.Service.Stop();
        if (boundary == "revoke") await r.Service.ConfigureAsync(r.Service.Snapshot.Settings with { Permissions = new() });
        if (boundary == "model") await r.Service.UpdateConversationAsync(id, r.Service.Snapshot.Conversations.Single().Options with { Model = "fixture-changed" });
        if (boundary == "context") await r.Service.UpdateConversationAsync(id, r.Service.Snapshot.Conversations.Single().Options with { UseSavedContext = false });
        await r.Service.SendAsync(id, "继续。"); Assert.Null(provider.Requests.Last().ConversationTape);
        if (boundary is "revoke" or "context") Assert.DoesNotContain(provider.Requests.Last().Messages, m => m.Text.Contains("已授权的本地工具资料", StringComparison.Ordinal));
    }
    [Fact] public async Task ForgottenEvidenceCannotReturnThroughNativeTape()
    {
        var provider = NativeTapeProvider(); await using var r = new Rig(provider); var id = await ReadyNativeTape(r);
        const string opinion = "我喜欢魔女的夜宴的角色塑造，因为人物冲突有层次。";
        await r.Service.SendAsync(id, opinion);
        var memory = Assert.Single(r.Service.Snapshot.Memories.Where(m => m.MessageId == r.Service.Snapshot.Messages.First().Id));
        await r.Service.ForgetMemoryAsync(memory.Id); await r.Service.SendAsync(id, "继续讨论。");
        Assert.Null(provider.Requests.Last().ConversationTape);
        Assert.DoesNotContain(opinion, string.Join("\n", provider.Requests.Last().Messages.Select(m => m.Text)));
    }
    [Fact] public async Task LateCancelledNativeTapeNeverReappearsOnNextTurn()
    {
        var provider = NativeTapeProvider(); await using var r = new Rig(provider); var id = await ReadyNativeTape(r);
        await r.Service.SendAsync(id, "第一轮。");
        provider.Block = true; provider.IgnoreCancellation = true;
        provider.Started = new(TaskCreationOptions.RunContinuationsAsynchronously); provider.Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var send = r.Service.SendAsync(id, "第二轮。"); await provider.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        r.Service.Stop(); provider.Release.TrySetResult(); await send;
        Assert.Equal(PlaygroundMessageStatus.Cancelled, Last(r).Status);
        provider.Block = false; await r.Service.SendAsync(id, "第三轮。"); Assert.Null(provider.Requests.Last().ConversationTape);
        await r.Service.SendAsync(id, "第四轮。"); Assert.NotNull(provider.Requests.Last().ConversationTape);
    }
    [Fact] public async Task LibraryDelegateFailureReleasesActiveRound()
    {
        var store = new Store(); var credentials = new Credentials(); var provider = NativeTapeProvider(); bool fail = true;
        var stableLibrary = new LibrarySnapshot([Entry()], LibraryRecovery.Main);
        await using var service = new PlaygroundService(store, credentials, [provider], () => fail ? throw new InvalidOperationException("fixture") : stableLibrary);
        await service.ConfigureAsync(new() { Enabled = true, Permissions = new(Library: true), Connections = [new(provider.Kind, provider.DefaultEndpoint, "fixture", true)] });
        var id = await service.CreateConversationAsync(); await service.UpdateConversationAsync(id, service.Snapshot.Conversations.Single().Options with { Spoiler = PlaygroundSpoilerMode.Full, Search = PlaygroundSearchMode.Off });
        await service.SendAsync(id, "第一次。"); Assert.False(service.IsGenerating);
        fail = false; await service.SendAsync(id, "第二次。"); Assert.False(service.IsGenerating); Assert.Single(provider.Requests);
        Assert.Equal(PlaygroundMessageStatus.Complete, service.Snapshot.Messages.Last().Status);
    }
    [Fact] public async Task LargeHighOutputCanCompleteAndPersistBeyondFormerNinetySixKCharacters()
    {
        var provider = new Provider { Reply = _ => [new(PlaygroundProviderEventKind.Text) { Text = new string('a', 150000) }, new(PlaygroundProviderEventKind.Completed) { StopReason = "end_turn" }] };
        await using var r = new Rig(provider); var id = await r.Ready(); await r.Service.SendAsync(id, "仅合成长输出验证。");
        Assert.Equal(PlaygroundMessageStatus.Complete, Last(r).Status); Assert.Equal(150000, r.Store.Value!.Messages.Last().Text.Length);
    }
    [Theory]
    [InlineData("我最近主要在玩哪几部？帮我回顾一下最近几次的游玩情况。")]
    [InlineData("请核实一下我最近主要在玩的作品和游玩记录。")]
    public async Task NaturalRecentQuestionPrefetchesCurrentFactsBeforeFirstProviderRequest(string question)
    {
        await using var r = new Rig();
        r.Library = new([Entry() with { PersonalRating = null }], LibraryRecovery.Main);
        var id = await r.Ready(new(Library: true));
        await r.Service.UpdateConversationAsync(id, r.Service.Snapshot.Conversations.Single().Options with { Search = PlaygroundSearchMode.Smart });
        await r.Service.SendAsync(id, question);
        var request = Assert.Single(r.Provider.Requests);
        string wire = string.Join("\n", request.Messages.Select(m => m.Text));
        Assert.Contains("已授权的本地工具资料", wire);
        Assert.Contains("\"personal_rating\":null", wire);
        var source = Assert.Single(Last(r).Sources.Where(s => s.Kind == PlaygroundSourceKind.LocalRecord));
        Assert.Contains(source.Id, wire);
        Assert.Empty(r.Provider.Searches);
        Assert.Equal(PlaygroundMessageStatus.Complete, Last(r).Status);
    }
}
