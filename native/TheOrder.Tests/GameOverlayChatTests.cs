using System.Collections.Immutable;
using System.Text.Json;
using TheOrder.Application;
using TheOrder.Application.Playground;
using TheOrder.Platform.Windows;
using TheOrder.Presentation;
using Xunit;

namespace TheOrder.Tests;

public sealed partial class PlaygroundCoreTests
{
    [Fact]
    public async Task GameFrameIsVisibleWithGentleSpoilerPreferenceAndNeverPersistsPixels()
    {
        await using var r = new Rig(new Provider { ImageInputSupported = true });
        var id = await r.Ready(new(false, false, false, false), spoiler: PlaygroundSpoilerMode.LibraryStatus);
        var frame = Pixel("当前游戏画面.png") with { IsGameFrame = true };
        await r.Service.SendAsync(id, "这里这句话是什么意思？", [frame]);
        Assert.Contains(r.Provider.Requests.Single().Messages.SelectMany(m => m.Attachments), a => a.ImageBase64 == frame.ImageBase64);
        Assert.Contains("游玩进度以用户明确说明为准", r.Provider.Requests.Single().Messages.First(m => m.Attachments.Length != 0).Text);
        Assert.Equal(0, r.LibraryReads);
        Assert.All(r.Store.Writes, s => Assert.DoesNotContain(frame.ImageBase64!, JsonSerializer.Serialize(s)));
        Assert.All(r.Service.Snapshot.Messages.SelectMany(m => m.Attachments), a => Assert.Null(a.ImageBase64));
        Assert.All(WindowsPlaygroundStore.PersistentProjection(r.Service.Snapshot).Messages.SelectMany(m => m.Attachments), a => Assert.Null(a.ImageBase64));
        await r.Service.SendAsync(id, "继续说说这句话。");
        Assert.Empty(r.Provider.Requests.Last().Messages.SelectMany(m => m.Attachments));
    }

    [Fact]
    public async Task TurningSpoilerProtectionOffAppliesToCurrentAndPreviouslyAttachedGameFrames()
    {
        await using var rig = new Rig(new Provider { ImageInputSupported = true });
        var id = await rig.Ready(new(Library: true), spoiler: PlaygroundSpoilerMode.LibraryStatus);
        var options = rig.Service.Snapshot.Conversations.Single().Options;
        var frame = Pixel("当前游戏画面.png") with { IsGameFrame = true };
        await rig.Service.SendAsync(id, "解释这一幕。", [frame]);
        Assert.Contains("剧情偏好：避免主动揭示用户没有问到的关键反转和结局", rig.Provider.Requests[0].Instructions);
        Assert.Contains("用户问到的剧情照常展开讨论", rig.Provider.Requests[0].Instructions);

        await rig.Service.UpdateConversationAsync(id, options with { Spoiler = PlaygroundSpoilerMode.Full });
        rig.Provider.Reply = _ => Answer("这个结局需要先读完前置路线。");
        await rig.Service.SendAsync(id, "说明这些路线的剧情承接。", [frame]);
        var request = rig.Provider.Requests.Last();
        Assert.DoesNotContain("防剧透", request.Instructions);
        Assert.DoesNotContain("剧情偏好：", request.Instructions);
        Assert.Contains(request.Messages, message => message.Attachments.Length == 1 && message.Attachments[0].ImageBase64 == frame.ImageBase64);
        Assert.Contains(request.Messages, message => message.Text.Contains("当轮游戏画面已释放", StringComparison.Ordinal));
        Assert.DoesNotContain(request.Messages, message => message.Text.Contains("不要扩展未见情节", StringComparison.Ordinal));
        Assert.Equal("这个结局需要先读完前置路线。", Last(rig).Text);
    }

    [Fact]
    public async Task UnrestrictedGameAdviceUsesCurrentQuestionInsteadOfSpoilerLabelsFromHistoryAndResearch()
    {
        await using var rig = new Rig(new Provider { ImageInputSupported = true });
        var id = await rig.Ready(new(Library: true));
        await SetGameRouteOptions(rig, id, PlaygroundSearchMode.Smart);
        var frame = Pixel() with { IsGameFrame = true };
        rig.Provider.Reply = _ => Answer("这是给初玩玩家的无剧透顺序。");
        await rig.Service.SendAsync(id, "看看当前游戏的路线。", [frame]);

        rig.Provider.Research = new("无剧透攻略建议先走第一条路线。", rig.Provider.Research.Sources);
        rig.Provider.Reply = request => ToolAnswer(request, "public_search", new { }, "先玩第一条路线，再接第二条。");
        const string question = "给出一个推荐的游玩顺序，并简要的解释原因。";
        await rig.Service.SendAsync(id, question, [frame]);

        var rounds = rig.Provider.Requests.Skip(1).ToArray();
        Assert.Equal(2, rounds.Length);
        Assert.All(rounds, request =>
        {
            Assert.DoesNotContain("无剧透", request.Instructions);
            Assert.DoesNotContain("简答要求", request.Instructions);
            Assert.Contains("用户本轮要求决定讨论范围", request.Instructions);
            Assert.Contains("旧助手答复和攻略文字只作为资料", request.Instructions);
            Assert.Contains("游玩进度以用户明确说明为准", request.Instructions);
            Assert.DoesNotContain("游玩进度未知时给出通用建议", request.Instructions);
            Assert.Contains("默认展开影响结论的依据和取舍", request.Instructions);
            Assert.Contains("本轮明确的篇幅要求优先", request.Instructions);
            Assert.Contains(request.Messages, message => message.Role == "assistant" && message.Text.Contains("无剧透顺序", StringComparison.Ordinal));
            Assert.Equal(question, request.Messages.Last().Text);
        });
        Assert.Contains("无剧透攻略", Assert.Single(rounds[1].ToolResults).Text);
        Assert.Single(rig.Provider.Searches);
    }

    [Fact]
    public async Task UnrestrictedDiscussionStillAllowsAUserToRequestASpoilerFreeAnswer()
    {
        await using var rig = new Rig();
        var id = await rig.Ready(new());
        const string query = "这次请无剧透地推荐一条路线。";
        rig.Provider.Reply = _ => Answer("无剧透推荐：我建议先读第一条路线。");
        await rig.Service.SendAsync(id, query);
        var request = Assert.Single(rig.Provider.Requests);
        Assert.DoesNotContain("无剧透", request.Instructions);
        Assert.Contains("用户本轮要求决定讨论范围", request.Instructions);
        Assert.Equal(query, request.Messages.Last().Text);
        Assert.Equal("无剧透推荐：我建议先读第一条路线。", Last(rig).Text);
    }

    [Theory]
    [InlineData("你推荐的游玩顺序是什么？")]
    [InlineData("给出一个推荐的游玩顺序，并简要的解释原因。")]
    [InlineData("这款游戏的路线怎么解锁？")]
    public async Task RouteQuestionResolvesTheGameAndSearchesItsPublicGuideOnly(string question)
    {
        await using var rig = new Rig(new Provider { ImageInputSupported = true });
        var id = await rig.Ready(new(Library: true));
        await SetGameRouteOptions(rig, id, PlaygroundSearchMode.Smart);
        rig.Provider.Reply = request => ToolAnswer(request, "public_search", new { }, "按公开攻略解释路线 [[web_1]]");
        await rig.Service.SendAsync(id, question, [Pixel("private-frame.png") with { IsGameFrame = true }]);

        var request = rig.Provider.Requests.First();
        Assert.Equal("high", request.ReasoningEffort);
        Assert.Contains("サノバウィッチ", request.Instructions);
        Assert.Contains(request.Tools, tool => tool.Name == "public_search" && !tool.ParametersJson.Contains("\"query\"", StringComparison.Ordinal));
        var search = Assert.Single(rig.Provider.Searches);
        Assert.Contains("《サノバウィッチ》", search.Query);
        Assert.Contains("解锁条件", search.Query);
        Assert.DoesNotContain("PRIVATE-TITLE", search.Query);
        Assert.DoesNotContain("private-frame.png", search.Query);
        Assert.DoesNotContain("当前个人评价", search.Query);
        Assert.DoesNotContain(@"D:\", search.Query);
        Assert.Contains(Last(rig).Sources, source => source.Id == "web_1");
    }

    [Theory]
    [InlineData("search-off", "你推荐的游玩顺序是什么？")]
    [InlineData("library-off", "你推荐的游玩顺序是什么？")]
    [InlineData("saved-context-off", "你推荐的游玩顺序是什么？")]
    [InlineData("relinked", "你推荐的游玩顺序是什么？")]
    [InlineData("private", "根据我的笔记推荐游玩顺序。")]
    [InlineData("other-game", "另一部游戏的游玩顺序是什么？")]
    public async Task RouteContextDoesNotEnableUnverifiedOrUnauthorizedSearch(string scenario, string question)
    {
        await using var rig = new Rig();
        var id = await rig.Ready(new(Library: scenario != "library-off"));
        await SetGameRouteOptions(rig, id, scenario == "search-off" ? PlaygroundSearchMode.Off : PlaygroundSearchMode.Smart,
            useSavedContext: scenario != "saved-context-off", relinked: scenario == "relinked");
        await rig.Service.SendAsync(id, question);
        Assert.DoesNotContain(Assert.Single(rig.Provider.Requests).Tools, tool => tool.Name == "public_search");
        Assert.Empty(rig.Provider.Searches);
        if (scenario is "library-off" or "saved-context-off") Assert.Equal(0, rig.LibraryReads);
    }

    private static async Task SetGameRouteOptions(Rig rig, Guid id, PlaygroundSearchMode search, bool useSavedContext = true, bool relinked = false)
    {
        var entry = Entry() with { Name = "PRIVATE-TITLE" };
        string oldWorkKey = PlaygroundService.WorkKeyFor(entry);
        string digest = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(oldWorkKey)));
        if (relinked) entry = entry with { Metadata = entry.Metadata! with { Id = "different-id" } };
        rig.Library = new([entry], rig.Library!.Recovery);
        var options = rig.Service.Snapshot.Conversations.Single().Options;
        await rig.Service.UpdateConversationAsync(id, options with
        {
            GameOverlayKey = $"game:{entry.Id}:{digest}",
            Search = search,
            ReasoningEffort = "high",
            UseSavedContext = useSavedContext
        });
    }

    [Theory]
    [InlineData(PlaygroundProviderKind.OpenAI)]
    [InlineData(PlaygroundProviderKind.Gemini)]
    public async Task GameQuoteUsesVendorsNativeSearchWithOnlyTheBoundedVisibleQuote(PlaygroundProviderKind provider)
    {
        await using var r = new Rig(new Provider(provider) { ImageInputSupported = true });
        var id = await r.Ready();
        var options = r.Service.Snapshot.Conversations.Single().Options;
        await r.Service.UpdateConversationAsync(id, options with { Search = PlaygroundSearchMode.Smart });
        r.Provider.Reply = request => ToolAnswer(request, "search_game_quote", new { quote = "人間は考える葦である" }, "出处来自公开资料 [[web_1]]");
        await r.Service.SendAsync(id, "这里这句话是有什么引用吗？", [Pixel() with { IsGameFrame = true }]);
        var query = Assert.Single(r.Provider.Searches).Query;
        Assert.Contains("人間は考える葦である", query);
        Assert.DoesNotContain("PRIVATE", query); Assert.DoesNotContain("这里", query);
        Assert.Contains(r.Provider.Requests.First().Tools, t => t.Name == "search_game_quote");
        Assert.Contains(Last(r).Sources, s => s.Id == "web_1");
    }

    [Theory]
    [InlineData("sk-test-private-credential")]
    [InlineData("我的账号是12345678901")]
    [InlineData("https://example.org/private")]
    [InlineData("邮箱 example@example.org")]
    [InlineData("C:\\Users\\Private\\notes.txt")]
    public async Task GameQuoteRejectsSensitiveQueriesWithoutCallingSearch(string quote)
    {
        await using var r = new Rig(new Provider { ImageInputSupported = true }); var id = await r.Ready();
        await r.Service.UpdateConversationAsync(id, r.Service.Snapshot.Conversations.Single().Options with { Search = PlaygroundSearchMode.Smart });
        r.Provider.Reply = request => ToolAnswer(request, "search_game_quote", new { quote }, "无法核对");
        await r.Service.SendAsync(id, "查这句话", [Pixel() with { IsGameFrame = true }]);
        Assert.Empty(r.Provider.Searches);
        Assert.Contains(r.Provider.Requests.Last().ToolResults, m => m.Text.Contains("invalid_arguments", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DifferentVisibleQuotesHaveIndependentResultsAndRepeatsReuseTheirOwnResult()
    {
        await using var r = new Rig(new Provider { ImageInputSupported = true }); var id = await r.Ready();
        await r.Service.UpdateConversationAsync(id, r.Service.Snapshot.Conversations.Single().Options with { Search = PlaygroundSearchMode.Smart });
        string[] quotes = ["人間は考える葦である", "A thing of beauty is a joy for ever", "人間は考える葦である"];
        r.Provider.Reply = request =>
        {
            int round = r.Provider.Requests.Count;
            if (round > quotes.Length) return Answer("已分别核实两句。");
            r.Provider.Research = new(quotes[round - 1] + " [[web_1]]", [new("web_1", PlaygroundSourceKind.Web, "官方来源") { Url = "https://example.org/quote/" + round }]);
            return [new(PlaygroundProviderEventKind.Continuation) { Continuation = new(request.Binding, "synthetic-round-" + round) },
                new(PlaygroundProviderEventKind.ToolCall) { ToolCall = new("q" + round, "search_game_quote", JsonSerializer.Serialize(new { quote = quotes[round - 1] })) },
                new(PlaygroundProviderEventKind.Completed) { StopReason = "tool_calls" }];
        };
        await r.Service.SendAsync(id, "看看这两句的引用", [Pixel() with { IsGameFrame = true }]);
        Assert.Equal(2, r.Provider.Searches.Count);
        Assert.Contains(quotes[0], r.Provider.Searches[0].Query); Assert.Contains(quotes[1], r.Provider.Searches[1].Query);
        Assert.Contains(quotes[0], r.Provider.Requests.Last().ToolResults.Last().Text);
        Assert.Contains(Last(r).Sources, s => s.Url == "https://example.org/quote/1");
        Assert.Contains(Last(r).Sources, s => s.Url == "https://example.org/quote/2" && s.Id != "web_1");
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task GameQuoteToolRequiresBothCurrentFrameAndSearchPermission(bool frame, bool search)
    {
        await using var r = new Rig(new Provider { ImageInputSupported = true }); var id = await r.Ready();
        await r.Service.UpdateConversationAsync(id, r.Service.Snapshot.Conversations.Single().Options with { Search = search ? PlaygroundSearchMode.Smart : PlaygroundSearchMode.Off });
        await r.Service.SendAsync(id, "这里这句话有引用吗", frame ? [Pixel() with { IsGameFrame = true }] : []);
        Assert.DoesNotContain(r.Provider.Requests.Single().Tools, t => t.Name == "search_game_quote");
        Assert.Empty(r.Provider.Searches);
    }

    [Fact]
    public async Task UnsupportedImageModelDoesNotSendOrPretendToSeeFrame()
    {
        await using var r = new Rig(); var id = await r.Ready();
        await r.Service.SendAsync(id, "这里是什么", [Pixel() with { IsGameFrame = true }]);
        Assert.Empty(r.Provider.Requests); Assert.Equal(PlaygroundMessageStatus.Failed, Last(r).Status);
        await Assert.ThrowsAsync<InvalidOperationException>(() => r.Service.RetryAsync(id));
    }

    [Theory]
    [InlineData(PlaygroundProviderKind.OpenAI)]
    [InlineData(PlaygroundProviderKind.Gemini)]
    public async Task OpeningTheActualGameChatHostDoesNotWriteUntilItsFirstMessage(PlaygroundProviderKind provider)
    {
        await using var rig = new Rig(new Provider(provider));
        var existing = await rig.Ready();
        int count = rig.Store.Writes.Count;
        using var chat = new WindowsGameChat("Local\\TheOrder.Guide." + Guid.NewGuid().ToString("N"),
            "fixture-game-key", "合成作品", _ => Task.FromResult(rig.Service), () => true, default, GameChatMarkdown.Format);
        var initialLength = chat.View.ReadInt32(12);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(6));
        while (chat.View.ReadInt32(12) == initialLength)
        {
            chat.Tick(true);
            await Task.Delay(25, deadline.Token);
        }
        Assert.Equal(count, rig.Store.Writes.Count);
        Assert.Equal(existing, Assert.Single(rig.Service.Snapshot.Conversations).Id);

        var text = System.Text.Encoding.UTF8.GetBytes("第一次发消息。");
        chat.View.Write(44, 0);
        chat.View.WriteArray(GameChatWire.Input, text, 0, text.Length);
        chat.View.Write(32, text.Length);
        chat.View.Write(24, 1);
        chat.View.Write(16, 1);
        while (!deadline.IsCancellationRequested && !rig.Service.Snapshot.Messages.Any(m => m.Role == "assistant" && m.Status == PlaygroundMessageStatus.Complete))
        {
            chat.Tick(true);
            await Task.Delay(25);
        }
        var displayLength = chat.View.ReadInt32(GameChatWire.State);
        var statusBytes = new byte[displayLength];
        chat.View.ReadArray(GameChatWire.State + 4, statusBytes, 0, displayLength);
        Assert.True(!deadline.IsCancellationRequested, System.Text.Encoding.UTF8.GetString(statusBytes)
            + string.Join(", ", rig.Service.Snapshot.Messages.Select(m => m.Status + " " + m.Error)));
        var game = Assert.Single(rig.Service.Snapshot.Conversations, c => c.Options.GameOverlayKey == "fixture-game-key");
        Assert.Contains(rig.Store.Writes[count].Messages, m => m.ConversationId == game.Id && m.Role == "user");
        Assert.Contains(rig.Store.Writes[count].Conversations, c => c.Id == game.Id);
        Assert.Single(rig.Provider.Requests);
    }

    [Fact]
    public async Task StartupPrunesOnlyOldEmptyAutomaticallyNamedGameConversations()
    {
        await using var rig = new Rig();
        var id = await rig.Ready();
        var ordinary = Assert.Single(rig.Service.Snapshot.Conversations);
        Assert.Equal(id, ordinary.Id);
        var old = ordinary with { Id = Guid.NewGuid(), Title = "游戏中 · 旧条目", Options = ordinary.Options with { GameOverlayKey = "old-game" } };
        var renamed = old with { Id = Guid.NewGuid(), Title = "我手动命名的游戏会话" };
        var used = old with { Id = Guid.NewGuid(), Title = "游戏中 · 有消息", Options = old.Options with { GameOverlayKey = "other-game" } };
        rig.Store.Value = rig.Store.Value! with { Conversations = [ordinary, old, renamed, used],
            Messages = [new(Guid.NewGuid(), used.Id, "user", "保留这条消息", ordinary.CreatedAt, PlaygroundMessageStatus.Complete, Guid.NewGuid())] };
        await using var reopened = new PlaygroundService(rig.Store, rig.Credentials, [rig.Provider], () => rig.Library, () => null);
        await reopened.InitializeAsync();
        Assert.Equal(new[] { ordinary.Id, renamed.Id, used.Id }, reopened.Snapshot.Conversations.Select(c => c.Id));
        Assert.Equal(used.Id, Assert.Single(reopened.Snapshot.Messages).ConversationId);
        Assert.DoesNotContain(rig.Store.Value!.Conversations, c => c.Id == old.Id);
    }

    [Fact]
    public void GameChatProjectionBoundsLongUnicodeRepliesWithoutImagesOrCredentials()
    {
        var lines = Enumerable.Range(0, 32).Select(_ => new GameChatLine("assistant", new string('界', 40000), "")).ToArray();
        byte[] wire = GameChatWire.Encode(new("", "fixture", "high", false, true, true, true, ["fixture"], lines));
        Assert.InRange(wire.Length, 1, GameChatWire.StateBytes);
        Assert.Equal(GameChatWire.Pixels + GameChatWire.PixelBytes, GameChatWire.MaximumBytes);
        wire = GameChatWire.Encode(new("", "fixture", "high", false, true, true, true, [], [new("assistant", "前\0后" + new string('界', 41000))]));
        using var reader = new BinaryReader(new MemoryStream(wire), System.Text.Encoding.UTF8);
        string ReadText() => System.Text.Encoding.UTF8.GetString(reader.ReadBytes(reader.ReadInt32()));
        ReadText(); ReadText(); ReadText(); for (int i = 0; i < 6; i++) reader.ReadInt32(); ReadText();
        string body = ReadText(); Assert.DoesNotContain('\0', body); Assert.Contains("主程序继续阅读", body);
    }
}
