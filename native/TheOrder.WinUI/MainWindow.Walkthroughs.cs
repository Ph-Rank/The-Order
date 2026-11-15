using TheOrder.Application;
using TheOrder.Platform.Windows;
using Microsoft.UI.Xaml.Controls;

namespace TheOrder.WinUI;

public sealed partial class MainWindow
{
    private IWalkthroughReader? walkthroughReader;
    private WindowsWalkthroughSessions? walkthroughSessions;
    private GuideLaunchNotice? shownGuideNotice;
    private Action? refreshGuideToolsStatus;
    public void ConfigureWalkthroughs(IWalkthroughReader reader, WindowsWalkthroughSessions sessions)
    {
        walkthroughReader = reader;
        walkthroughSessions = sessions;
        sessions.ChatFactory = GameChatServiceAsync;
    }
    private void RefreshGuideStatus()
    {
        refreshGuideToolsStatus?.Invoke();
        var current = walkthroughSessions?.Notice;
        if (ReferenceEquals(current, shownGuideNotice)) return;
        shownGuideNotice = current;
        if (current?.Warning == true)
            statusPage.Post(new(current.Title + " · 游戏内助手", current.Message, InfoBarSeverity.Warning,
                new("游戏工具", () => OpenGuideNoticeToolsAsync(current.GameId), "GuideNoticeOpenGameTools"),
                RequiresAction: current.Action == GuideLaunchAction.ChangeShortcut));
    }

    private async Task OpenGuideNoticeToolsAsync(string gameId)
    {
        if (p1Stopping || commandsBlocked || p1Dialog is not null
            || extras.Snapshot?.Entries.Any(entry => entry.Id == gameId) != true
            || !await ResolveUnsavedAsync()) return;
        if (!p1Stopping && !commandsBlocked && p1Dialog is null)
            await OpenGameToolsAsync(gameId, showAssistant: true);
    }
}
