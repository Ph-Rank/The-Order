using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Storage;
using System.Reflection;

namespace TheOrder.WinUI;

public sealed partial class MainWindow
{
    private ContentDialog? round3Dialog;
    private object Round3Evidence() => new {
        assembly = typeof(MainWindow).Assembly.ManifestModule.ModuleVersionId,
        version = typeof(MainWindow).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
        library = libraryPage.VisualLayoutEvidence(),
        focus = Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(Root.XamlRoot)?.GetType().Name,
        textScale = new Windows.UI.ViewManagement.UISettings().TextScaleFactor,
        shell = "continuous canvas; independent toolbar; no enclosing glass", fixtureOnly = visualFixtureTimer is not null
    };
    private async Task CaptureRound3Async(string label)
    {
        if (visualFixtureTimer is null || fixtureDirectory is null || label.Length is 0 or > 80 || label.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-'))
            throw new InvalidOperationException("Bounded Test capture required");
        var path = Path.Combine(fixtureDirectory, label + ".png");
        if (File.Exists(path)) throw new InvalidOperationException("Evidence overwrite forbidden");
        var target = new RenderTargetBitmap(); await target.RenderAsync(Root);
        var pixels = (await target.GetPixelsAsync()).ToArray();
        await File.WriteAllBytesAsync(path, []);
        var file = await StorageFile.GetFileFromPathAsync(path);
        using var stream = await file.OpenAsync(FileAccessMode.ReadWrite);
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)target.PixelWidth, (uint)target.PixelHeight, 96, 96, pixels);
        await encoder.FlushAsync();
        // Root render excludes popup HWNDs and system titlebar. Desktop captures verify those separately.
    }
    private void ShowRound3Dialog()
    {
        if (visualFixtureTimer is null) return;
        round3Dialog = new ContentDialog { XamlRoot = Root.XamlRoot, RequestedTheme = Root.ActualTheme,
            Title = "操作确认 · Test", Content = "保持可读正文与系统键盘行为。取消返回原页面。", CloseButtonText = "取消", PrimaryButtonText = "确认" };
        _ = round3Dialog.ShowInShellAsync();
    }
}
