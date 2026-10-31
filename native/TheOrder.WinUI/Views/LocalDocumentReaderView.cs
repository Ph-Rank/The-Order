using System.Collections.Immutable;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using TheOrder.Application;
using global::Windows.Graphics.Imaging;

namespace TheOrder.WinUI.Views;

public sealed class LocalDocumentReaderView(Func<LocalReadingFile, CancellationToken, Task<ILocalReadingDocument>> open,
    Func<LocalReadingFile, LocalReadingPosition?>? restorePosition = null,
    Action<LocalReadingFile, int, float, float, bool>? rememberPosition = null,
    Func<ImmutableArray<LocalReadingFile>, string?>? lastFile = null) : Grid, IDisposable
{
    private readonly ComboBox files = new() { PlaceholderText = "选择文件", HorizontalAlignment = HorizontalAlignment.Stretch, MinWidth = 0 };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12, Opacity = .8 };
    private readonly TextBlock position = new() { VerticalAlignment = VerticalAlignment.Center, MinWidth = 52, TextAlignment = TextAlignment.Center };
    private readonly StackPanel content = new() { Spacing = 10, Padding = new Thickness(12, 8, 16, 24), HorizontalAlignment = HorizontalAlignment.Left };
    private readonly ScrollViewer scrolling = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalContentAlignment = HorizontalAlignment.Left };
    private readonly CancellationTokenSource lifetime = new();
    private readonly SemaphoreSlim reading = new(1, 1);
    private ImmutableArray<LocalReadingFile> choices = []; private ILocalReadingDocument? document; private string? currentPath;
    private int page, revision; private double zoom = 1; private bool initialized, changing, disposed;
    private ReadingPage? rendered;
    private readonly Image pageImage = new() { Stretch = Stretch.Uniform };
    private byte[]? imagePixels;
    private readonly Button fileActions = new() { Content = "文件", Padding = new(10, 4, 10, 4), MinHeight = 30 };
    private LocalReadingFile? shownFile; private bool restoring; private int edgeWheel;
    internal object Evidence => new { files = choices.Select(f => f.Name).ToArray(), selected = files.SelectedIndex, page, pages = document?.PageCount ?? 0,
        status = status.Text, width = ActualWidth, height = ActualHeight, viewportWidth = scrolling.ViewportWidth, viewportHeight = scrolling.ViewportHeight,
        extentWidth = scrolling.ExtentWidth, extentHeight = scrolling.ExtentHeight, horizontalOffset = scrolling.HorizontalOffset, verticalOffset = scrolling.VerticalOffset,
        zoom, imageWidth = rendered?.ImageWidth ?? 0, imageHeight = rendered?.ImageHeight ?? 0,
        pageBounds = rendered?.Rgba is { Length: > 0 } ? new { x = pageImage.TransformToVisual(scrolling).TransformPoint(new(0, 0)).X, width = pageImage.ActualWidth, height = pageImage.ActualHeight } : null,
        blocks = rendered?.Blocks.Select(b => new { b.Kind, b.Text, rows = b.Rows.IsDefault ? 0 : b.Rows.Length }).ToArray() };
    public void SetFileManagement(FrameworkElement management)
    {
        fileActions.Flyout = new Flyout { Content = new ScrollViewer { Content = management, MaxHeight = 360, MaxWidth = 470, Padding = new(2), HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled } };
        AutomationProperties.SetName(fileActions, "添加或管理阅读文件");
    }
    internal async Task PageForTestAsync(int index) { if (document is null || index < 0 || index >= document.PageCount) throw new InvalidDataException(); page = index; await LoadAsync(); }
    internal void ZoomForTest(double value) { if (value is < .7 or > 3) throw new InvalidDataException(); zoom = value; Draw(); }
    public void Initialize()
    {
        if (initialized) return; initialized = true;
        RowDefinitions.Add(new() { Height = GridLength.Auto }); RowDefinitions.Add(new() { Height = GridLength.Auto }); RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) });
        var toolbar = new Grid { ColumnSpacing = 12, RowSpacing = 6 };
        toolbar.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); toolbar.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        toolbar.RowDefinitions.Add(new() { Height = GridLength.Auto }); toolbar.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var fileTools = new Grid { ColumnSpacing = 6 }; fileTools.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); fileTools.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        fileTools.Children.Add(files); Grid.SetColumn(fileActions, 1); fileTools.Children.Add(fileActions); toolbar.Children.Add(fileTools);
        var pageTools = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        toolbar.Children.Add(pageTools);
        void ArrangeToolbar() { bool narrow = ActualWidth < 640; Grid.SetColumnSpan(fileTools, narrow ? 2 : 1); Grid.SetColumn(pageTools, narrow ? 0 : 1); Grid.SetRow(pageTools, narrow ? 1 : 0); Grid.SetColumnSpan(pageTools, narrow ? 2 : 1); pageTools.HorizontalAlignment = narrow ? HorizontalAlignment.Left : HorizontalAlignment.Right; }
        SizeChanged += (_, _) => ArrangeToolbar(); ArrangeToolbar();
        Button Action(string text, Func<Task> action) { var button = new Button { Content = text, MinHeight = 30, Padding = new(9, 4, 9, 4) }; button.Click += async (_, _) => await action(); return button; }
        var previous = Action("‹", () => TurnAsync(-1)); AutomationProperties.SetName(previous, "上一页"); ToolTipService.SetToolTip(previous, "上一页"); pageTools.Children.Add(previous);
        var pageChooser = new Button { Content = position, Padding = new(6, 4, 6, 4), MinHeight = 30 }; AutomationProperties.SetName(pageChooser, "跳转页码");
        pageChooser.Click += (_, _) => {
            if (document is null) return;
            var number = new NumberBox { Header = "前往页码", Minimum = 1, Maximum = document.PageCount, Value = page + 1, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact, Width = 190 };
            var flyout = new Flyout(); var go = Action("前往", async () => { if (double.IsFinite(number.Value)) { Remember(); page = Math.Clamp((int)number.Value - 1, 0, document.PageCount - 1); flyout.Hide(); await LoadAsync(); } });
            flyout.Content = new StackPanel { Spacing = 10, Children = { number, go } }; flyout.ShowAt(pageChooser);
        }; pageTools.Children.Add(pageChooser);
        var next = Action("›", () => TurnAsync(1)); AutomationProperties.SetName(next, "下一页"); ToolTipService.SetToolTip(next, "下一页"); pageTools.Children.Add(next);
        pageTools.Children.Add(Action("−", () => { zoom = Math.Max(.7, zoom / 1.2); Draw(); return Task.CompletedTask; }));
        pageTools.Children.Add(Action("+", () => { zoom = Math.Min(3, zoom * 1.2); Draw(); return Task.CompletedTask; }));
        pageTools.Children.Add(Action("适应页面", () => { zoom = 1; Draw(); return Task.CompletedTask; }));
        Children.Add(toolbar); SetRow(status, 1); status.Margin = new(0, 6, 0, 6); Children.Add(status); SetRow(scrolling, 2); scrolling.Margin = new(0, 8, 0, 0); Children.Add(scrolling); scrolling.Content = content;
        if (string.IsNullOrEmpty(AutomationProperties.GetAutomationId(this))) AutomationProperties.SetAutomationId(this, "LocalDocumentReader"); AutomationProperties.SetName(files, "阅读文件");
        AutomationProperties.SetLiveSetting(status, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite);
        files.SelectionChanged += async (_, _) => { if (changing) return; Remember(); page = 0; zoom = 1; await LoadAsync(restore: true); };
        scrolling.SizeChanged += (_, _) => { if (rendered is not null) Draw(); };
        scrolling.ViewChanged += (_, _) => { if (!restoring) Remember(); };
        AddHandler(PointerWheelChangedEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler(async (_, e) => {
            if (rendered is null || restoring || document is null || string.Equals(Path.GetExtension(shownFile?.Name), ".pdf", StringComparison.OrdinalIgnoreCase)) return;
            int delta = e.GetCurrentPoint(scrolling).Properties.MouseWheelDelta;
            bool boundary = delta < 0 ? scrolling.VerticalOffset >= scrolling.ScrollableHeight - 1 : scrolling.VerticalOffset <= 1;
            edgeWheel = boundary ? edgeWheel + 1 : 0;
            if (edgeWheel >= 2) { edgeWheel = 0; await TurnAsync(delta < 0 ? 1 : -1, continuous: true); }
        }), true);
        KeyDown += async (_, e) => {
            if (e.OriginalSource is TextBox or NumberBox or ComboBox) return;
            if (Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down)
                || Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Shift).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down)) return;
            if (e.Key is Windows.System.VirtualKey.Left or Windows.System.VirtualKey.Right) { e.Handled = true; await TurnAsync(e.Key == Windows.System.VirtualKey.Right ? 1 : -1); }
            else if (e.Key is Windows.System.VirtualKey.PageDown or Windows.System.VirtualKey.PageUp) {
                e.Handled = true; bool forward = e.Key == Windows.System.VirtualKey.PageDown;
                if (forward ? scrolling.VerticalOffset >= scrolling.ScrollableHeight - 1 : scrolling.VerticalOffset <= 1) await TurnAsync(forward ? 1 : -1, continuous: true);
                else scrolling.ChangeView(null, Math.Clamp(scrolling.VerticalOffset + (forward ? 1 : -1) * scrolling.ViewportHeight * .85, 0, scrolling.ScrollableHeight), null);
            }
        };
    }
    public async Task SetFilesAsync(ImmutableArray<LocalReadingFile> values, string? selected = null, bool load = true)
    {
        Initialize(); Remember(); changing = true; selected ??= lastFile?.Invoke(values); choices = values; files.Items.Clear(); foreach (var value in values) files.Items.Add(value.Name);
        int index = selected is null ? 0 : Array.FindIndex(values.ToArray(), v => v.Path == selected); files.SelectedIndex = values.IsEmpty ? -1 : Math.Max(0, index); changing = false; page = 0;
        revision++; rendered = null; content.Children.Clear(); status.Text = "选择文件后可只读阅读。"; position.Text = "";
        if (load) await LoadAsync(restore: true);
    }
    public Task EnsureReadingAsync() => rendered is null ? LoadAsync(restore: true) : Task.CompletedTask;
    internal void ScrollForTest(float progress) { scrolling.ChangeView(null, scrolling.ScrollableHeight * Math.Clamp(progress, 0, 1), null, true); Remember(); }
    internal async Task FileForTestAsync(int index) { if (index < 0 || index >= choices.Length) throw new InvalidDataException(); Remember(); changing = true; files.SelectedIndex = index; changing = false; page = 0; zoom = 1; await LoadAsync(restore: true); }
    public void Remember() { if (rendered is not null && shownFile is not null && !restoring) rememberPosition?.Invoke(shownFile, page, scrolling.ScrollableHeight > 0 ? (float)(scrolling.VerticalOffset / scrolling.ScrollableHeight) : 0, (float)zoom, false); }
    private async Task TurnAsync(int direction, bool continuous = false)
    { if (restoring || document is null || page + direction < 0 || page + direction >= document.PageCount) return; Remember(); page += direction; await LoadAsync(fraction: continuous && direction < 0 ? 1 : 0); }
    private async Task LoadAsync(bool restore = false, float fraction = 0)
    {
        int expected = ++revision; var selected = files.SelectedIndex; if (disposed) return;
        if (selected < 0 || selected >= choices.Length) { content.Children.Clear(); rendered = null; imagePixels = null; pageImage.Source = null; status.Visibility = Visibility.Visible; status.Text = "从“文件”中手动添加攻略或说明，即可在这里及游戏内只读阅读。"; position.Text = ""; return; }
        var file = choices[selected]; int targetPage = page; status.Text = "正在读取…"; restoring = true;
        bool held = false;
        try
        {
            await reading.WaitAsync(lifetime.Token); held = true; if (expected != revision) return;
            if (document is null || currentPath != file.Path) { document?.Dispose(); document = null; currentPath = null; document = await open(file, lifetime.Token); currentPath = file.Path; }
            var saved = restore ? restorePosition?.Invoke(file) : null;
            if (saved is not null) { targetPage = Math.Clamp(saved.Page, 0, document.PageCount - 1); zoom = Math.Clamp(saved.Zoom, .7, 3); fraction = saved.Progress; }
            var result = await document.PageAsync(targetPage, lifetime.Token); if (disposed || expected != revision) return;
            restoring = true; page = targetPage; rendered = result; shownFile = file; position.Text = $"{targetPage + 1} / {document.PageCount}"; status.Text = document.Notice; status.Visibility = Visibility.Collapsed; ToolTipService.SetToolTip(files, document.Notice); Draw(); scrolling.UpdateLayout(); scrolling.ChangeView(0, scrolling.ScrollableHeight * fraction, null, true); restoring = false;
            rememberPosition?.Invoke(file, page, fraction, (float)zoom, true);
        }
        catch (OperationCanceledException) { }
        catch (Exception error) when (error is not OutOfMemoryException)
        { if (!disposed && expected == revision) { rendered = null; imagePixels = null; pageImage.Source = null; content.Children.Clear(); status.Visibility = Visibility.Visible; status.Text = error is InvalidDataException ? error.Message : "文件无法读取，可能已移动、已加密或格式损坏。原文件保持不变。"; } }
        finally { if (held) reading.Release(); if (expected == revision) restoring = false; }
    }
    private void Draw()
    {
        if (disposed || rendered is null) return; content.Children.Clear();
        double width = Math.Max(160, Math.Min(760, scrolling.ActualWidth - 36)); content.Width = width * zoom;
        bool pdf = shownFile is not null && Path.GetExtension(shownFile.Name).Equals(".pdf", StringComparison.OrdinalIgnoreCase);
        content.Padding = pdf ? new(14, 8, 14, 24) : new(12, 8, 16, 24);
        scrolling.HorizontalContentAlignment = pdf ? HorizontalAlignment.Center : HorizontalAlignment.Left;
        content.HorizontalAlignment = pdf ? HorizontalAlignment.Center : HorizontalAlignment.Left;
        if (rendered.Rgba is { Length: > 0 } pixels)
        {
            if (pdf) width = Math.Min(width, Math.Max(1, scrolling.ActualHeight - 40) * rendered.ImageWidth / rendered.ImageHeight);
            pageImage.Width = width * zoom; pageImage.Height = width * zoom * rendered.ImageHeight / rendered.ImageWidth;
            content.Children.Add(pageImage); if (!ReferenceEquals(imagePixels, pixels)) { imagePixels = pixels; pageImage.Source = null; _ = SetImageAsync(pageImage, pixels, rendered.ImageWidth, rendered.ImageHeight); } content.Width = width * zoom + content.Padding.Left + content.Padding.Right;
        }
        else { imagePixels = null; pageImage.Source = null; }
        foreach (var block in rendered.Blocks)
        {
            if (block.Kind == "space") { content.Children.Add(new Border { Height = 4 }); continue; }
            if (block.Kind == "table")
            {
                content.Children.Add(Text(block.Text, 12)); var table = new Grid(); int columns = block.Rows.IsDefault ? 0 : block.Rows.Max(r => r.Length);
                for (int c = 0; c < columns; c++) table.ColumnDefinitions.Add(new() { Width = new(Math.Max(125, width / Math.Max(1, columns)) * zoom) });
                for (int r = 0; r < block.Rows.Length; r++)
                {
                    table.RowDefinitions.Add(new() { Height = GridLength.Auto });
                    for (int c = 0; c < block.Rows[r].Length; c++)
                    {
                        var cell = new Border { Child = Text(block.Rows[r][c], 14), Padding = new(8), BorderThickness = new(0, 0, 1, 1), BorderBrush = (Brush)Microsoft.UI.Xaml.Application.Current.Resources["DividerBrush"] };
                        Grid.SetRow(cell, r); Grid.SetColumn(cell, c); table.Children.Add(cell);
                    }
                }
                content.Children.Add(table); content.Width = Math.Max(content.Width, columns * Math.Max(125, width / Math.Max(1, columns)) * zoom);
            }
            else content.Children.Add(Text(block.Text, block.Kind == "heading" ? 18 : 14));
        }
        TextBlock Text(string value, double size) => new() { Text = value, FontSize = size * zoom, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true, LineHeight = size * zoom * 1.65 };
    }
    private async Task SetImageAsync(Image target, byte[] pixels, int width, int height)
    {
        try
        {
            using var rgba = SoftwareBitmap.CreateCopyFromBuffer(pixels.AsBuffer(), BitmapPixelFormat.Rgba8, width, height, BitmapAlphaMode.Straight);
            using var bgra = SoftwareBitmap.Convert(rgba, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
            var source = new SoftwareBitmapSource(); await source.SetBitmapAsync(bgra); if (!disposed && ReferenceEquals(imagePixels, pixels)) target.Source = source;
        }
        catch { if (!disposed && ReferenceEquals(imagePixels, pixels)) { status.Visibility = Visibility.Visible; status.Text = "图片显示失败，可尝试重新选择文件。"; } }
    }
    public void Dispose() { if (disposed) return; Remember(); disposed = true; lifetime.Cancel(); imagePixels = null; rendered = null; pageImage.Source = null; content.Children.Clear(); _ = CloseAsync(); }
    private async Task CloseAsync() { await reading.WaitAsync(); try { document?.Dispose(); document = null; } finally { reading.Release(); lifetime.Dispose(); } }
}
