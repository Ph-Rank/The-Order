using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TheOrder.Platform.Windows;

namespace TheOrder.WinUI.Views;

public sealed partial class SettingsPage
{
    private sealed record CacheRow(ImageCacheKind Kind, string Title, TextBlock Size, Button Clear)
    {
        public bool CanClear { get; set; }
    }

    private ImageCacheStorage? imageCacheStorage;
    private CacheRow[] cacheRows = [];
    private bool cacheBusy;

    internal void ConfigureImageCache(ImageCacheStorage storage)
    {
        imageCacheStorage = storage;
        cacheRows =
        [
            new(ImageCacheKind.OnlineArtwork, "在线图片", OnlineArtworkCacheSize, OnlineArtworkCacheClear),
            new(ImageCacheKind.Thumbnails, "缩略图", ThumbnailsCacheSize, ThumbnailsCacheClear),
            new(ImageCacheKind.HeroFrames, "横幅与玻璃成品", HeroFramesCacheSize, HeroFramesCacheClear)
        ];
    }

    private async void OnRefreshImageCache(object sender, RoutedEventArgs e) => await RefreshImageCacheAsync();

    private async Task RefreshImageCacheAsync()
    {
        if (cacheBusy || imageCacheStorage is not { } storage) return;
        SetCacheBusy(true);
        ShowCacheStatus("正在统计…");
        var incomplete = false;
        try
        {
            foreach (var row in cacheRows)
            {
                try
                {
                    var usage = await storage.InspectAsync(row.Kind);
                    ShowCacheUsage(row, usage);
                    incomplete |= usage.SkippedFiles > 0;
                }
                catch
                {
                    ShowCacheUsage(row, null);
                    incomplete = true;
                }
            }
            ShowCacheStatus(incomplete ? "部分缓存暂时无法统计。" : "");
        }
        finally { SetCacheBusy(false); }
    }

    private async void OnClearImageCache(object sender, RoutedEventArgs e)
    {
        if (cacheBusy || imageCacheStorage is not { } storage || cacheRows.FirstOrDefault(row => ReferenceEquals(row.Clear, sender)) is not { CanClear: true } row) return;
        SetCacheBusy(true);
        try
        {
            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot, RequestedTheme = ActualTheme,
                Title = "清理" + row.Title,
                Content = row.Kind == ImageCacheKind.OnlineArtwork
                    ? "已下载的在线图片将按需重新下载。"
                    : "这些图片会在下次需要时重新生成。",
                PrimaryButtonText = "清理", CloseButtonText = "取消", DefaultButton = ContentDialogButton.Close
            };
            if (await dialog.ShowInShellAsync() != ContentDialogResult.Primary) return;
            ShowCacheStatus("正在清理…");
            var usage = await storage.ClearAsync(row.Kind);
            ShowCacheUsage(row, usage);
            ShowCacheStatus(usage.SkippedFiles == 0
                ? $"已清理 {CacheSize(usage.RemovedBytes)}。"
                : $"已清理 {CacheSize(usage.RemovedBytes)}，部分文件暂时无法清理。");
        }
        catch { ShowCacheStatus("缓存清理未完成，请重新统计后重试。"); }
        finally { SetCacheBusy(false); }
    }

    private void SetCacheBusy(bool busy)
    {
        cacheBusy = busy;
        RefreshImageCache.IsEnabled = !busy;
        foreach (var row in cacheRows) row.Clear.IsEnabled = !busy && row.CanClear;
    }

    private static void ShowCacheUsage(CacheRow row, ImageCacheUsage? usage)
    {
        row.CanClear = usage is { Bytes: > 0 };
        row.Size.Text = usage is { } value
            ? (value.SkippedFiles > 0 ? "已知 " : "") + CacheSize(value.Bytes)
            : "无法统计";
    }

    private void ShowCacheStatus(string message)
    {
        ImageCacheStatus.Text = message;
        ImageCacheStatus.Visibility = VisibleText(message);
    }

    private static string CacheSize(long bytes)
    {
        const long KiB = 1024, MiB = 1024 * KiB, GiB = 1024 * MiB;
        if (bytes >= GiB) return $"{bytes / (double)GiB:0.##} GiB";
        if (bytes >= MiB) return $"{bytes / (double)MiB:0.##} MiB";
        if (bytes >= KiB) return $"{bytes / (double)KiB:0.##} KiB";
        return $"{bytes:N0} B";
    }
}
