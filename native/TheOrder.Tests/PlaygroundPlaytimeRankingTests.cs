using System.Collections.Immutable;
using System.Text.Json;
using TheOrder.Application;
using TheOrder.Application.Playground;
using TheOrder.Domain;
using Xunit;

namespace TheOrder.Tests;

public sealed partial class PlaygroundCoreTests
{
    private const string RankingQuestion = "简洁回复我游玩时间最长的三个游戏";
    private static LibraryEntry TimedEntry(string id, string name, long seconds, string? lastPlayed = null) => Entry() with
    { Id = id, Name = name, Metadata = null, TotalPlaySeconds = seconds, Sessions = [], PlayCount = 0, LastPlayedAt = lastPlayed };

    private static LibrarySnapshot RankingLibrary() => new([
        TimedEntry("first", "第一部", 152376, "2026-10-30T00:00:00Z"),
        TimedEntry("old", "很久没玩的人工记录", 0, "2026-08-01T00:00:00Z") with
        { ManualPlaytime = [new("manual", "2026-08-01", 138060, "PRIVATE-ADJUSTMENT-NOTE", "2026-08-01T00:00:00Z")],
            Installations = [new("a", @"D:\Private-A", @"D:\Private-A\game.exe", []), new("b", @"D:\Private-B", @"D:\Private-B\game.exe", [])] },
        TimedEntry("third", "第三部", 123381, "2026-10-29T00:00:00Z") with
        { ManualPlaytime = [new("undone", "2026-10-01", 200000, "撤销项", "2026-10-01T00:00:00Z", true)] },
        TimedEntry("fourth", "第四部", 85203, "2026-10-28T00:00:00Z"),
        .. Enumerable.Range(0, 12).Select(i => TimedEntry("recent" + i, "最近条目" + i, 1000 + i, "2026-10-30T12:00:00Z"))
    ], LibraryRecovery.Main);

    private static JsonDocument RankingContext(PlaygroundProviderRequest request) => JsonDocument.Parse(
        request.Messages.First(m => m.Text.Contains("\"scope\":\"entire_current_library\"", StringComparison.Ordinal)).Text.Split('\n', 2)[1]);

    [Theory]
    [InlineData(PlaygroundProviderKind.OpenAI)] [InlineData(PlaygroundProviderKind.Gemini)]
    [InlineData(PlaygroundProviderKind.Claude)] [InlineData(PlaygroundProviderKind.Grok)] [InlineData(PlaygroundProviderKind.DeepSeek)]
    public async Task PlaytimeRankingIncludesOldManualOnlyEntryAcrossEntireLibraryForAllProviders(PlaygroundProviderKind kind)
    {
        await using var r = new Rig(new Provider(kind)); r.Library = RankingLibrary(); var id = await r.Ready(new(Library: true));
        await r.Service.SendAsync(id, RankingQuestion);
        using var context = RankingContext(Assert.Single(r.Provider.Requests)); var data = context.RootElement.GetProperty("data");
        Assert.Equal(16, data.GetProperty("library_entry_count").GetInt32());
        Assert.Equal(16, data.GetProperty("entries_with_recorded_time").GetInt32());
        var works = data.GetProperty("works").EnumerateArray().ToArray();
        Assert.Equal(new[] { "第一部", "很久没玩的人工记录", "第三部" }, works.Select(w => w.GetProperty("title").GetString()));
        Assert.Equal(new long[] { 152376, 138060, 123381 }, works.Select(w => w.GetProperty("recorded_play_seconds").GetInt64()));
        Assert.Equal(new[] { 1, 2, 3 }, works.Select(w => w.GetProperty("rank").GetInt32()));
        Assert.Equal("38 小时 21 分 0 秒", works[1].GetProperty("display_duration").GetString());
        Assert.Equal(3, data.GetProperty("next_offset").GetInt32());
        Assert.All(works, w => Assert.Contains(Last(r).Sources, source => source.Id == w.GetProperty("source_id").GetString()));
        Assert.DoesNotContain("PRIVATE-ADJUSTMENT-NOTE", Wire(r.Provider)); Assert.DoesNotContain("Private-A", Wire(r.Provider));
        Assert.DoesNotContain("private.txt", Wire(r.Provider)); Assert.Empty(r.Provider.Searches);
        Assert.Equal(PlaygroundMessageStatus.Complete, Last(r).Status);
    }

    [Theory]
    [InlineData("我玩的游戏时长排名前十", 10)]
    [InlineData("游玩时间最长的两款作品", 2)]
    [InlineData("我游玩时间最长的游戏是哪个？", 1)]
    [InlineData("my top 3 games by play time", 3)]
    [InlineData("my most played games", 3)]
    public async Task PlaytimeRankingRecognizesNaturalCounts(string question, int count)
    {
        await using var r = new Rig(); r.Library = RankingLibrary(); var id = await r.Ready(new(Library: true));
        await r.Service.SendAsync(id, question); using var context = RankingContext(Assert.Single(r.Provider.Requests));
        Assert.Equal(count, context.RootElement.GetProperty("data").GetProperty("works").GetArrayLength());
    }

    [Theory]
    [InlineData("最近一周游玩时间最长的三个游戏")]
    [InlineData("已玩分类的游戏时长排名前三")]
    [InlineData("2026年游玩时间最长的三个游戏")]
    [InlineData("《魔女的夜宴》游玩时间最长的会话")]
    public async Task PlaytimeRankingDoesNotSilentlyReplaceScopedQuestionWithLifetimeTotals(string question)
    {
        await using var r = new Rig(); r.Library = RankingLibrary(); var id = await r.Ready(new(Library: true));
        await r.Service.SendAsync(id, question);
        Assert.DoesNotContain("\"scope\":\"entire_current_library\"", Wire(r.Provider));
    }

    [Fact]
    public async Task PlaytimeRankingToolsPageTiesDeterministicallyAndIncludeNegativeCorrections()
    {
        await using var r = new Rig(); r.Library = new([
            TimedEntry("z", "同秒数乙", 50), TimedEntry("a", "同秒数甲", 100) with
                { ManualPlaytime = [new("negative", "2026-10-01", -50, "", "2026-10-01T00:00:00Z")] },
            TimedEntry("none", "未记录", 0), TimedEntry("zero", "归零", 20) with
                { ManualPlaytime = [new("zero", "2026-10-01", -20, "", "2026-10-01T00:00:00Z")] }
        ], LibraryRecovery.Main);
        var id = await r.Ready(new(Library: true));
        r.Provider.Reply = request => ToolAnswer(request, "rank_playtime", new { limit = 1, offset = 1 }, "已读取排名。");
        await r.Service.SendAsync(id, "请核对书架记录。");
        using var result = JsonDocument.Parse(r.Provider.Requests[1].ToolResults.Single().Text);
        var data = result.RootElement.GetProperty("data");
        Assert.Equal(4, data.GetProperty("library_entry_count").GetInt32()); Assert.Equal(2, data.GetProperty("entries_with_recorded_time").GetInt32());
        var item = data.GetProperty("works")[0]; Assert.Equal("同秒数乙", item.GetProperty("title").GetString());
        Assert.True(item.GetProperty("tied_with_previous").GetBoolean()); Assert.Equal(2, item.GetProperty("rank").GetInt32());
        Assert.Equal(JsonValueKind.Null, data.GetProperty("next_offset").ValueKind);
    }

    [Theory]
    [InlineData("{\"limit\":0}")] [InlineData("{\"limit\":11}")] [InlineData("{\"offset\":-1}")]
    [InlineData("{\"offset\":100001}")] [InlineData("{\"offset\":\"1\"}")] [InlineData("{\"query\":\"private\"}")]
    public async Task PlaytimeRankingRejectsInvalidArguments(string arguments)
    {
        await using var r = new Rig(); var id = await r.Ready(new(Library: true));
        r.Provider.Reply = request => request.ToolResults.IsEmpty ? [
            new(PlaygroundProviderEventKind.Continuation) { Continuation = new(request.Binding, "synthetic-ranking") },
            new(PlaygroundProviderEventKind.ToolCall) { ToolCall = new("ranking", "rank_playtime", arguments) },
            new(PlaygroundProviderEventKind.Completed) { StopReason = "tool_calls" }
        ] : Answer("参数未执行。");
        await r.Service.SendAsync(id, "核对记录。");
        Assert.Contains("invalid_arguments", r.Provider.Requests[1].ToolResults.Single().Text);
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task PlaytimeRankingDistinguishesEmptyLibraryFromUnavailable(bool unavailable)
    {
        await using var r = new Rig(); r.Library = unavailable ? null : new([], LibraryRecovery.Main);
        var id = await r.Ready(new(Library: true)); await r.Service.SendAsync(id, RankingQuestion);
        var context = r.Provider.Requests[0].Messages.First(m => m.Text.StartsWith("已授权的本地工具资料", StringComparison.Ordinal)).Text;
        Assert.Contains(unavailable ? "\"status\":\"unknown\"" : "\"status\":\"empty\"", context);
    }

    [Theory] [InlineData("denied")] [InlineData("temporary")] [InlineData("disabled_context")]
    public async Task PlaytimeRankingHonorsSourcePermissionsAndSavedContext(string mode)
    {
        await using var r = new Rig(); var id = await r.Ready(new(Library: mode != "denied"), temporary: mode == "temporary");
        if (mode == "disabled_context") await r.Service.UpdateConversationAsync(id, ControlsOptions(r, id) with { UseSavedContext = false });
        r.Provider.Reply = request => ToolAnswer(request, "rank_playtime", new { }, "无可用授权资料。");
        await r.Service.SendAsync(id, RankingQuestion);
        Assert.Equal(0, r.LibraryReads); Assert.DoesNotContain(r.Provider.Requests[0].Tools, t => t.Name == "rank_playtime");
        Assert.Contains("permission_denied", r.Provider.Requests[1].ToolResults.Single().Text);
    }

    [Fact]
    public async Task PlaytimeRankingRefreshesAfterCorrectionAndDoesNotUsePriorAssistantRanking()
    {
        await using var r = new Rig(); r.Library = RankingLibrary(); var id = await r.Ready(new(Library: true));
        r.Provider.Reply = _ => Answer("旧答案：第一部、第三部、第四部。"); await r.Service.SendAsync(id, RankingQuestion);
        r.Library = r.Library with { Entries = r.Library.Entries.Select(e => e.Id == "fourth" ? e with { TotalPlaySeconds = 300000 } : e).ToImmutableArray() };
        await r.Service.SendAsync(id, RankingQuestion); using var context = RankingContext(r.Provider.Requests[1]);
        Assert.Equal("第四部", context.RootElement.GetProperty("data").GetProperty("works")[0].GetProperty("title").GetString());
    }

    [Fact]
    public async Task PlaytimeRankingFreshFactsOutrankLongOldAssistantTextInLimitedContext()
    {
        await using var r = new Rig(); r.Library = RankingLibrary(); var id = await r.Ready(new(Library: true));
        r.Provider.Reply = _ => Answer(new string('x', 29000)); await r.Service.SendAsync(id, "一般讨论。");
        await r.Service.ConfigureAsync(r.Service.Snapshot.Settings with { MaximumContextCharacters = 30000 });
        r.Provider.Reply = _ => Answer("已核对。"); await r.Service.SendAsync(id, RankingQuestion);
        using var context = RankingContext(r.Provider.Requests[1]);
        Assert.Equal("很久没玩的人工记录", context.RootElement.GetProperty("data").GetProperty("works")[1].GetProperty("title").GetString());
        Assert.DoesNotContain(r.Provider.Requests[1].Messages, m => m.Role == "assistant" && m.Text.Length == 29000);
        Assert.Equal(PlaygroundMessageStatus.Complete, Last(r).Status);
    }

    [Theory] [InlineData(PlaygroundSpoilerMode.Full)] [InlineData(PlaygroundSpoilerMode.LibraryStatus)]
    public async Task PlaytimeRankingChangedDuringReplyIsNotAcceptedAsCurrent(PlaygroundSpoilerMode spoiler)
    {
        var provider = new Provider { Block = true, IgnoreCancellation = true }; await using var r = new Rig(provider); r.Library = RankingLibrary();
        var id = await r.Ready(new(Library: true), spoiler: spoiler); var send = r.Service.SendAsync(id, RankingQuestion);
        try
        {
            await provider.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            r.Library = r.Library with { Entries = r.Library.Entries.Select(e => e.Id == "first" ? e with { TotalPlaySeconds = 1 } : e).ToImmutableArray() };
        }
        finally { provider.Release.TrySetResult(); }
        await send.WaitAsync(TimeSpan.FromSeconds(5)); Assert.NotEqual(PlaygroundMessageStatus.Complete, Last(r).Status);
        Assert.Contains("书架资料已变化", Last(r).Error);
    }

    [Fact]
    public async Task ConnectionNeedsConsentAndKeyButNeverLegacyEnableCheckbox()
    {
        await using var r = new Rig(); var id = await r.Ready(new());
        await r.Service.ConfigureAsync(r.Service.Snapshot.Settings with { Enabled = false, DiscussionConsent = true });
        Assert.Single(await r.Service.ListModelsAsync(r.Provider.Kind, r.Provider.DefaultEndpoint));
        await r.Service.SendAsync(id, "你好。"); Assert.Equal(PlaygroundMessageStatus.Complete, Last(r).Status);
        r.Credentials.Present = false;
        await Assert.ThrowsAsync<PlaygroundProviderException>(() => r.Service.SendAsync(id, "未保存Key不能发送。"));
        r.Credentials.Present = true;
        await r.Service.ConfigureAsync(r.Service.Snapshot.Settings with { DiscussionConsent = false });
        await Assert.ThrowsAsync<InvalidOperationException>(() => r.Service.SendAsync(id, "已撤销授权。"));
        Assert.Single(r.Provider.Requests);
    }

    [Fact]
    public async Task BuiltInDiscussionVoiceStaysSeparateFromUserPresetAndGivesStylePreferencePriority()
    {
        await using var r = new Rig(); var id = await r.Ready(new());
        Assert.Empty(ControlsOptions(r, id).SystemInstructions);
        const string preset = "用简短的列表、活泼的语气，扮演一个书评编辑。";
        await r.Service.UpdateConversationAsync(id, ControlsOptions(r, id) with { SystemInstructions = preset });
        await r.Service.SendAsync(id, "这个作品的节奏怎么理解？");
        string instructions = Assert.Single(r.Provider.Requests).Instructions;
        Assert.Contains("用户在本会话设定的角色、语气、篇幅、措辞和排版优先", instructions);
        Assert.Contains("不编造出错原因", instructions); Assert.Contains(preset, instructions);
        Assert.Equal(preset, ControlsOptions(r, id).SystemInstructions);
        Assert.DoesNotContain("默认交流风格", JsonSerializer.Serialize(r.Store.Value));
    }

    [Fact]
    public async Task EachConversationRetainsItsProviderModelAndReasoningAcrossOtherConversationEditsAndReload()
    {
        var google = new Provider(PlaygroundProviderKind.Gemini); await using var r = new Rig(new Provider(), google);
        var first = await r.Ready(new()); var second = await r.Service.CreateConversationAsync();
        var firstOptions = ControlsOptions(r, first) with { Provider = PlaygroundProviderKind.Gemini,
            Endpoint = google.DefaultEndpoint, Model = "gemini-fixture", ReasoningEffort = "low" };
        var secondOptions = ControlsOptions(r, second) with { Model = "gpt-fixture", ReasoningEffort = "high" };
        await r.Service.UpdateConversationAsync(first, firstOptions); await r.Service.UpdateConversationAsync(second, secondOptions);
        Assert.Equal(firstOptions, ControlsOptions(r, first)); Assert.Equal(secondOptions, ControlsOptions(r, second));
        await r.Service.UpdateConversationAsync(second, secondOptions with { Model = "new-gpt-fixture", ReasoningEffort = "low" });
        Assert.Equal(firstOptions, ControlsOptions(r, first));
        await using var reloaded = new PlaygroundService(r.Store, r.Credentials, [r.Provider, google], () => r.Library);
        await reloaded.InitializeAsync();
        Assert.Equal(firstOptions, reloaded.Snapshot.Conversations.Single(c => c.Id == first).Options);
        Assert.Equal("new-gpt-fixture", reloaded.Snapshot.Conversations.Single(c => c.Id == second).Options.Model);
        Assert.Equal("low", reloaded.Snapshot.Conversations.Single(c => c.Id == second).Options.ReasoningEffort);
    }
}
