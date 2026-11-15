using TheOrder.Application;
using TheOrder.Infrastructure.Online;
using TheOrder.WinUI.Startup;
using TheOrder.WinUI.Views;

namespace TheOrder.WinUI;

public sealed partial class MainWindow
{
    private GitHubFeedbackClient? feedbackClient;
    private NativeLifecycleLog? feedbackLog;
    private FeedbackDialog? feedbackDialog;

    internal void ConfigureFeedback(GitHubFeedbackClient client, NativeLifecycleLog? log)
    {
        feedbackClient = client;
        feedbackLog = log;
    }

    private async Task OpenFeedbackAsync()
    {
        if (commandsBlocked || feedbackDialog is not null || feedbackClient is not { } client || Root.XamlRoot is not { } root) return;
        try
        {
            var environment = new FeedbackEnvironment(AppReleaseIdentity.Version,
                Environment.OSVersion.Version.ToString(), (int)Math.Round(root.RasterizationScale * 100));
            var dialog = new FeedbackDialog(root, client, environment, feedbackLog?.RecentWarnings() ?? "")
            {
                RequestedTheme = Root.ActualTheme
            };
            feedbackDialog = dialog;
            await dialog.ShowInShellAsync();
        }
        catch (Exception error) { App.TraceVisualFailure(error); ViewModel.ReportUnexpected(); }
        finally { feedbackDialog = null; }
    }
}
