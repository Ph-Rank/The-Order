using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TheOrder.Application;
using TheOrder.WinUI.Views;

namespace TheOrder.WinUI;

public sealed partial class MainWindow
{
    private Window? walkthroughFixtureWindow;
    private WalkthroughReaderView? walkthroughFixtureView;
    private LocalDocumentReaderView? localReadingFixture;
    private async Task WalkthroughFixtureAsync(JsonElement request)
    {
        if (Program.Profile?.Manifest.ProfileKind != NativeProfileKind.Test || visualFixtureTimer is null || fixtureDirectory is null
            || Environment.GetEnvironmentVariable("THE_ORDER_NATIVE_WALKTHROUGH_TEST") != "1")
            throw new InvalidOperationException("Explicit isolated walkthrough Test capability required");
        if (!await startupFinished.Task.WaitAsync(TimeSpan.FromSeconds(30))) throw new InvalidOperationException("Test startup incomplete");
        var action = request.GetProperty("value").GetString();
        switch (action)
        {
            case "local-file":
                var filename = request.GetProperty("file").GetString();
                if (filename is not ("sample.txt" or "sample.pdf" or "sample.doc" or "sample.docx" or "sample.xls" or "sample.xlsx" or "sample.png" or "sample.jpeg" or "sample.jpg")) throw new InvalidDataException("Synthetic fixture file required");
                walkthroughFixtureWindow?.Close(); walkthroughFixtureView?.Dispose(); localReadingFixture?.Dispose(); walkthroughFixtureView = null;
                localReadingFixture = new((walkthroughSessions ?? throw new InvalidOperationException()).OpenLocalFileAsync) { Margin = new Thickness(20) };
                walkthroughFixtureWindow = new Window { Title = "The Order · 只读文档验证（Test）", Content = new Border { Child = localReadingFixture, Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 32, 34, 39)) } };
                walkthroughFixtureWindow.Activate(); walkthroughFixtureWindow.AppWindow.Resize(new Windows.Graphics.SizeInt32(1600, 1300));
                await localReadingFixture.SetFilesAsync([new(Path.Combine(fixtureDirectory, "documents", filename), filename, "guide")]);
                break;
            case "local-page": await (localReadingFixture?.PageForTestAsync(request.GetProperty("page").GetInt32()) ?? throw new InvalidOperationException()); break;
            case "local-zoom": localReadingFixture?.ZoomForTest(request.GetProperty("zoom").GetDouble()); break;
            case "local-size":
                int localWidth = request.GetProperty("width").GetInt32(); if (localWidth is not (480 or 900)) throw new InvalidDataException();
                double localScale = localReadingFixture?.XamlRoot?.RasterizationScale ?? 1;
                walkthroughFixtureWindow?.AppWindow.Resize(new Windows.Graphics.SizeInt32((int)(localWidth * localScale), (int)(640 * localScale))); break;
            case "local-capture":
                string captureName = request.GetProperty("name").GetString() ?? "";
                if (captureName.Length is < 1 or > 60 || captureName.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-')) throw new InvalidDataException();
                if (localReadingFixture is null) throw new InvalidOperationException();
                var capture = new Microsoft.UI.Xaml.Media.Imaging.RenderTargetBitmap(); await capture.RenderAsync(localReadingFixture);
                var capturePixels = System.Runtime.InteropServices.WindowsRuntime.WindowsRuntimeBufferExtensions.ToArray(await capture.GetPixelsAsync());
                var capturePath = Path.Combine(fixtureDirectory, captureName + ".png"); if (File.Exists(capturePath)) throw new InvalidOperationException("Evidence overwrite forbidden");
                await File.WriteAllBytesAsync(capturePath, []);
                var captureFile = await Windows.Storage.StorageFile.GetFileFromPathAsync(capturePath); using (var captureStream = await captureFile.OpenAsync(Windows.Storage.FileAccessMode.ReadWrite))
                { var encoder = await Windows.Graphics.Imaging.BitmapEncoder.CreateAsync(Windows.Graphics.Imaging.BitmapEncoder.PngEncoderId, captureStream); encoder.SetPixelData(Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8, Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied, (uint)capture.PixelWidth, (uint)capture.PixelHeight, 96, 96, capturePixels); await encoder.FlushAsync(); }
                break;
            case "local-close": walkthroughFixtureWindow?.Close(); localReadingFixture?.Dispose(); walkthroughFixtureWindow = null; localReadingFixture = null; break;
            case "open":
                string work = request.GetProperty("work").GetString() ?? "";
                if (work is not ("198" or "384" or "5882" or "2156")) throw new InvalidDataException("Bounded public samples only");
                walkthroughFixtureWindow?.Close(); walkthroughFixtureView?.Dispose();
                browsingPolicy.Set(request.TryGetProperty("nsfw", out var nsfw) && nsfw.GetBoolean());
                walkthroughFixtureView = new(walkthroughReader ?? throw new InvalidOperationException(), work, () => online.AuthGeneration)
                    { Margin = new Thickness(24) };
                walkthroughFixtureWindow = new Window { Title = "The Order · 网络攻略（Test）", Content = new Border { Child = walkthroughFixtureView } };
                if (walkthroughFixtureWindow.Content is Border surface)
                {
                    void Background() => surface.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                        surface.ActualTheme == ElementTheme.Dark ? Windows.UI.Color.FromArgb(255, 21, 23, 29) : Windows.UI.Color.FromArgb(255, 252, 252, 255));
                    surface.ActualThemeChanged += (_, _) => Background(); Background();
                }
                walkthroughFixtureWindow.Activate();
                walkthroughFixtureWindow.AppWindow.Resize(new Windows.Graphics.SizeInt32(1800, 1500));
                break;
            case "read": await (walkthroughFixtureView?.Model.RefreshAsync() ?? throw new InvalidOperationException()); break;
            case "select":
                await (walkthroughFixtureView?.Model.SelectAsync(request.GetProperty("guide").GetString() ?? "") ?? throw new InvalidOperationException()); break;
            case "graph": walkthroughFixtureView?.SetGraphForFixture(request.GetProperty("enabled").GetBoolean()); break;
            case "original": walkthroughFixtureView?.SetOriginalForFixture(request.GetProperty("enabled").GetBoolean()); break;
            case "route": walkthroughFixtureView?.InvokeRouteForFixture(request.GetProperty("guide").GetString() ?? ""); break;
            case "theme":
                if (walkthroughFixtureWindow?.Content is FrameworkElement themed)
                    themed.RequestedTheme = request.GetProperty("guide").GetString() == "light" ? ElementTheme.Light : ElementTheme.Dark;
                break;
            case "resize":
                int width = request.GetProperty("width").GetInt32(); if (width is not (480 or 900 or 1160)) throw new InvalidDataException();
                double scale = walkthroughFixtureView?.XamlRoot?.RasterizationScale ?? 1;
                walkthroughFixtureWindow?.AppWindow.Resize(new Windows.Graphics.SizeInt32((int)(width * scale), (int)(800 * scale))); break;
            case "close": walkthroughFixtureWindow?.Close(); walkthroughFixtureView?.Dispose(); walkthroughFixtureWindow = null; walkthroughFixtureView = null; break;
            default: throw new InvalidDataException("Unknown walkthrough action");
        }
        await Task.Delay(180);
        if (walkthroughFixtureWindow?.Content is FrameworkElement root) root.UpdateLayout();
        var proof = new { checkedUtc = DateTimeOffset.UtcNow, action, isolatedTest = true, realCredentials = false,
            surface = walkthroughFixtureView?.LayoutForFixture(), local = localReadingFixture?.Evidence };
        await File.AppendAllTextAsync(Path.Combine(fixtureDirectory, "walkthrough-evidence.jsonl"), JsonSerializer.Serialize(proof) + "\n");
    }
}
