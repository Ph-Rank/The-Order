using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using TheOrder.Application.Playground;
using Windows.Storage.Streams;

namespace TheOrder.WinUI.Views;

internal static class PlaygroundAttachmentPreview
{
    private static readonly SemaphoreSlim imageDecoder = new(1, 1);
    internal static bool IsImage(PlaygroundAttachment file) => file.ImageBase64 is { Length: > 0 } && file.MediaType is "image/png" or "image/jpeg";
    internal static Image Thumbnail(PlaygroundAttachment file)
    {
        var image = new Image { Width = 56, Height = 42, Stretch = Stretch.Uniform };
        bool loading = false;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(image, file.Name);
        image.Loaded += async (_, _) =>
        {
            if (loading || image.Source is not null) return; loading = true;
            try { image.Source = await DecodeAsync(file, 128); }
            catch { ToolTipService.SetToolTip(image, "图片快照暂时无法预览"); }
            finally { loading = false; }
        };
        return image;
    }
    private static async Task<BitmapImage> DecodeAsync(PlaygroundAttachment file, int width)
    {
        await imageDecoder.WaitAsync();
        try
        {
            // Platform supplies a bounded, metadata-free PNG/JPEG snapshot.
            // Neither thumbnails nor full previews retain/open an original path.
            var bytes = Convert.FromBase64String(file.ImageBase64 ?? "");
            using var stream = new InMemoryRandomAccessStream();
            using (var writer = new DataWriter(stream)) { writer.WriteBytes(bytes); await writer.StoreAsync(); await writer.FlushAsync(); writer.DetachStream(); }
            stream.Seek(0); var bitmap = new BitmapImage { DecodePixelWidth = width, DecodePixelType = DecodePixelType.Physical };
            await bitmap.SetSourceAsync(stream); return bitmap;
        }
        finally { imageDecoder.Release(); }
    }
    internal static async Task ShowAsync(XamlRoot? root, ElementTheme theme, PlaygroundAttachment file)
    {
        if (root is null) return;
        object content;
        if (IsImage(file))
        {
            var bitmap = await DecodeAsync(file, (int)Math.Clamp(root.Size.Width * root.RasterizationScale, 640, 2560));
            var image = new Image { Source = bitmap, Stretch = Stretch.Uniform, MaxWidth = Math.Max(260, Math.Min(760, root.Size.Width - 150)), MaxHeight = Math.Max(180, Math.Min(600, root.Size.Height - 170)) };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(image, "图片快照：" + file.Name);
            content = new ScrollViewer { Content = image, ZoomMode = ZoomMode.Enabled, MinZoomFactor = 1, MaxZoomFactor = 4,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                MaxHeight = image.MaxHeight, MaxWidth = image.MaxWidth, HorizontalContentAlignment = HorizontalAlignment.Center };
        }
        else
        {
            var text = new TextBox { Text = file.Text, IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
                IsSpellCheckEnabled = false, MinWidth = 260, MinHeight = 180, MaxHeight = Math.Max(180, Math.Min(520, root.Size.Height - 170)) };
            PlaygroundAppearance.Apply(text, "PGTextBox");
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(text, "附件文本快照：" + file.Name); content = text;
        }
        var dialog = new ContentDialog { XamlRoot = root, RequestedTheme = theme, Title = file.Name, Content = content, CloseButtonText = "关闭",
            Style = PlaygroundAppearance.Style("PGDialog"), CloseButtonStyle = PlaygroundAppearance.Style("PGGhostButton") };
        PlaygroundAppearance.Scope(dialog); dialog.Resources["ContentDialogMaxWidth"] = 820d; await dialog.ShowAsync();
    }
}
