using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace TheOrder.WinUI.Views;
public sealed partial class LibraryPage
{
    public Func<Task>? OpenSources { get; set; }
    public Func<Task>? ReviewDiscovery { get; set; }
    public Func<Task>? OpenStatistics { get; set; }
    public Func<Task>? DismissDiscovery { get; set; }
    private async void OnDismissDiscovery(object sender, object e)
    {
        if (lastDiscovery == 0) { DiscoveryNotice.IsOpen = false; return; }
        try { if (DismissDiscovery is not null) await DismissDiscovery(); }
        catch { DiscoveryNotice.IsOpen = true; ViewModel.ReportUnexpected(); }
    }
    internal void ConfigureP1()
    {
        if (LibraryMore.Flyout is not MenuFlyout menu) return;
        var statistics = new MenuFlyoutItem { Text = "游玩统计" }; statistics.Click += async (_, _) => { try { if (OpenStatistics is not null) await OpenStatistics(); } catch { ViewModel.ReportUnexpected(); } };
        var sources = new MenuFlyoutItem { Text = "自动发现游戏（文件夹 / Steam）" }; sources.Click += OnSources;
        menu.Items.Insert(0, sources); menu.Items.Insert(0, statistics);
    }
    private async void OnSources(object sender, RoutedEventArgs e) { try { if (OpenSources is not null) await OpenSources(); } catch { ViewModel.ReportUnexpected(); } }
    private async void OnReviewDiscovery(object sender, RoutedEventArgs e)
    { try { if (lastDiscoveryError is not null) { if (OpenSources is not null) await OpenSources(); } else if (ReviewDiscovery is not null) await ReviewDiscovery(); } catch { ViewModel.ReportUnexpected(); } }
    private int lastDiscovery;
    private string? lastDiscoveryError;
    internal void ShowDiscovery(int count, string? error = null)
    {
        if (count != lastDiscovery || error != lastDiscoveryError) DiscoveryNotice.IsOpen = count > 0 || error is not null;
        DiscoveryNotice.Title = error is null ? $"发现 {count} 个可导入的新游戏" : "游戏文件夹检查未完成";
        DiscoveryAction.Content = error is null ? "选择要导入的游戏" : "查看文件夹设置";
        DiscoveryNotice.Message = error ?? (count > 0 ? "关闭后这批游戏将不再提醒，可在“自动发现游戏”中恢复提醒。" : ""); lastDiscovery = count; lastDiscoveryError = error;
    }
}
