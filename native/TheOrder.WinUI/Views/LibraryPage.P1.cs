using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace TheOrder.WinUI.Views;
public sealed partial class LibraryPage
{
    public Func<Task>? OpenSources { get; set; }
    public Func<Task>? OpenStatistics { get; set; }
    internal void ConfigureP1()
    {
        if (LibraryMore.Flyout is not MenuFlyout menu) return;
        var statistics = new MenuFlyoutItem { Text = "游玩统计" }; statistics.Click += async (_, _) => { try { if (OpenStatistics is not null) await OpenStatistics(); } catch { ViewModel.ReportUnexpected(); } };
        var sources = new MenuFlyoutItem { Text = "自动发现游戏（文件夹 / Steam）" }; sources.Click += OnSources;
        menu.Items.Insert(0, sources); menu.Items.Insert(0, statistics);
    }
    private async void OnSources(object sender, RoutedEventArgs e) { try { if (OpenSources is not null) await OpenSources(); } catch { ViewModel.ReportUnexpected(); } }
}
