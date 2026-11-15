using System.Collections.Immutable;
using System.Text.Json;
using TheOrder.Application;
using TheOrder.Application.Playground;
using TheOrder.Platform.Windows;
using Xunit;

namespace TheOrder.Tests;

public sealed partial class PlaygroundCoreTests
{
    [Fact]
    public async Task GameFrameIsVisibleForCurrentSceneWithoutOpeningUnplayedStoryAndNeverPersistsPixels()
    {
        await using var r = new Rig(new Provider { ImageInputSupported = true });
        var id = await r.Ready(new(false, false, false, false), spoiler: PlaygroundSpoilerMode.LibraryStatus);
        var frame = Pixel("当前游戏画面.png") with { IsGameFrame = true };
        await r.Service.SendAsync(id, "这里这句话是什么意思？", [frame]);
        Assert.Contains(r.Provider.Requests.Single().Messages.SelectMany(m => m.Attachments), a => a.ImageBase64 == frame.ImageBase64);
        Assert.Contains("不代表已通关", r.Provider.Requests.Single().Messages.First(m => m.Attachments.Length != 0).Text);
        Assert.Equal(0, r.LibraryReads);
        Assert.All(r.Store.Writes, s => Assert.DoesNotContain(frame.ImageBase64!, JsonSerializer.Serialize(s)));
        Assert.All(r.Service.Snapshot.Messages.SelectMany(m => m.Attachments), a => Assert.Null(a.ImageBase64));
        Assert.All(WindowsPlaygroundStore.PersistentProjection(r.Service.Snapshot).Messages.SelectMany(m => m.Attachments), a => Assert.Null(a.ImageBase64));
        await r.Service.SendAsync(id, "继续说说这句话。");
        Assert.Empty(r.Provider.Requests.Last().Messages.SelectMany(m => m.Attachments));
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
