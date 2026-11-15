using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TheOrder.Platform.Windows;

namespace TheOrder.WinUI.Views;

public sealed partial class SettingsPage
{
    private WindowsClosePreference? closePreference;
    private bool synchronizingCloseAction;
    internal bool ExitOnClose { get; private set; }

    internal void ConfigureClosePreference(WindowsClosePreference preference, bool exitOnClose, bool loaded)
    {
        closePreference = loaded ? preference : null;
        ExitOnClose = exitOnClose;
        ShowCloseAction(loaded ? "" : "无法读取关闭设置，已使用默认行为。");
        CloseActionChoice.IsEnabled = loaded;
    }

    private async void OnCloseActionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (synchronizingCloseAction || closePreference is not { } preference || CloseActionChoice.OptionIndex < 0) return;
        var exit = CloseActionChoice.OptionIndex == 1;
        if (ExitOnClose == exit) return;

        CloseActionChoice.IsEnabled = false;
        try
        {
            await preference.SaveAsync(exit);
            ExitOnClose = exit;
            ShowCloseAction();
        }
        catch { ShowCloseAction("关闭设置未保存，请重试。"); }
        finally { CloseActionChoice.IsEnabled = true; }
    }

    private void ShowCloseAction(string message = "")
    {
        synchronizingCloseAction = true;
        CloseActionChoice.OptionIndex = ExitOnClose ? 1 : 0;
        synchronizingCloseAction = false;
        CloseActionStatus.Text = message;
        CloseActionStatus.Visibility = VisibleText(message);
    }
}
