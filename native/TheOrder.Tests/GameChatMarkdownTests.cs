using System.Text;
using TheOrder.Application;
using TheOrder.Presentation;
using Xunit;

namespace TheOrder.Tests;

public sealed class GameChatMarkdownTests
{
    [Fact]
    public void GameReplyPreservesMarkdownStructureAndUnicodeStyles()
    {
        var raw = """
            ## 初次游玩

            建议先完成**伊吹风子线**，然后继续。

            - *核对存档*
            - 使用 `Save 02`
            - ~~已取消~~

            > 留意读档点。

            [来源](https://example.org/guide)

            | 路线 | 存档 |
            | --- | --- |
            | 风子 | 01 |
            """;
        var line = GameChatMarkdown.Format(new("assistant", raw));
        Assert.StartsWith("初次游玩", line.Text);
        Assert.Contains("• 核对存档\n• 使用 Save 02", line.Text);
        Assert.Contains("│ 留意读档点。", line.Text);
        Assert.Contains("来源 (https://example.org/guide)", line.Text);
        Assert.Contains("路线  │  存档", line.Text);
        Assert.DoesNotContain("**", line.Text);
        var bytes = Encoding.UTF8.GetBytes(line.Text);
        Assert.Contains(line.Styles!, s => s.Style == GameChatStyle.Bold
            && Encoding.UTF8.GetString(bytes, s.Offset, s.Length) == "伊吹风子线");
        Assert.Contains(line.Styles!, s => (s.Style & GameChatStyle.Heading) != 0);
        Assert.Contains(line.Styles!, s => s.Style == GameChatStyle.Strike);
        Assert.Contains(line.Styles!, s => s.Style == GameChatStyle.Code
            && Encoding.UTF8.GetString(bytes, s.Offset, s.Length) == "Save 02");
        var serialized = GameChatWire.Encode(new("", "gpt-fixture", "high", false, true, true, true, [], [line]));
        Assert.Contains("伊吹风子线", Encoding.UTF8.GetString(serialized));
    }

    [Fact]
    public void UserDraftsAndUntrustedImageTargetsStayPlain()
    {
        var user = new GameChatLine("user", "**字面上的星号**");
        Assert.Same(user, GameChatMarkdown.Format(user));
        var reply = GameChatMarkdown.Format(new("assistant", "![作品封面](https://example.org/cover.jpg) 和 [链接](javascript:alert%281%29)"));
        Assert.Equal("作品封面 和 链接", reply.Text);
        Assert.Empty(reply.Styles!);
    }

    [Fact]
    public void LongRepliesStopOnACharacterBoundary()
    {
        var raw = "**" + string.Concat(Enumerable.Repeat("字🙂", 16000)) + "**";
        var answer = GameChatMarkdown.Format(new("assistant", raw));
        Assert.EndsWith("（长回复可在主程序继续阅读）", answer.Text);
        Assert.True(answer.Text.Length <= GameChatWire.MaxMessageCharacters);
        Assert.DoesNotContain("\uFFFD", Encoding.UTF8.GetString(Encoding.UTF8.GetBytes(answer.Text)));
    }
}
