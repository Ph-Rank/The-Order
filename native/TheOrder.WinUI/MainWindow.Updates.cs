using System.ComponentModel;
using Microsoft.UI.Xaml.Controls;
using TheOrder.Presentation;

namespace TheOrder.WinUI;

public sealed partial class MainWindow
{
    private void OnAppUpdateState(object? sender, PropertyChangedEventArgs e)
    {
        if (updatesStopped || e.PropertyName != nameof(updates.Message)) return;
        statusPage.SetNotice("app-update", updates.Latest is { } release
            ? new("更新与反馈", updates.IsChannelChange ? $"可切换至 {release.Version}。" : $"版本 {release.Version} 可用。",
                InfoBarSeverity.Informational, new("查看更新", OpenStatusUpdatesAsync, "StatusOpenUpdates"))
            : null);
        if (updates.IsBusy)
            statusPage.SetTask("app-update", "程序更新", updates.Message, updates.Downloading ? updates.Progress : null,
                updates.CanCancel ? new("取消更新", CancelStatusUpdateAsync, "CancelStatusUpdate") : null);
        else
            statusPage.ClearTask("app-update");
    }

    private async Task OpenStatusUpdatesAsync()
    {
        await Navigate(WorkspacePage.Settings);
        if (ViewModel.Page == WorkspacePage.Settings) settingsPage.RevealUpdates();
    }

    private Task CancelStatusUpdateAsync()
    {
        updates.Cancel();
        return Task.CompletedTask;
    }
    private async void OnAutomaticUpdate(object? sender, object e)
    {
        if (updatesStopped) return;
        if (updateTimer is not null) updateTimer.Interval = TimeSpan.FromHours(12);
        await updates.CheckAutomaticallyAsync();
    }
}
