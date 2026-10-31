using TheOrder.Application;
using TheOrder.Platform.Windows;
using Microsoft.UI.Xaml.Controls;

namespace TheOrder.WinUI;

public sealed partial class MainWindow
{
    private IWalkthroughReader? walkthroughReader;
    private WindowsWalkthroughSessions? walkthroughSessions;
    private GuideLaunchNotice? shownGuideNotice;
    private TextBlock? guideStatusLabel;
    private string? guideStatusGameId;
    public void ConfigureWalkthroughs(IWalkthroughReader reader, WindowsWalkthroughSessions sessions)
    { walkthroughReader = reader; walkthroughSessions = sessions; }
    private void RefreshGuideStatus()
    {
        var current = walkthroughSessions?.Notice;
        if (current is null || ReferenceEquals(current, shownGuideNotice)) return;
        if (guideStatusLabel is not null && current.GameId == guideStatusGameId) guideStatusLabel.Text = current.Message;
        shownGuideNotice = current;
        GuideNotice.Title = current.Title + " · 游戏内攻略";
        GuideNotice.Message = current.Message;
        GuideNotice.Severity = current.Warning ? InfoBarSeverity.Warning : InfoBarSeverity.Informational;
        GuideNotice.IsOpen = true;
    }
}
