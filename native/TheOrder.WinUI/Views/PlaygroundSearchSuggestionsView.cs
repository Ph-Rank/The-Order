using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.Web.WebView2.Core;
using Windows.Foundation;

namespace TheOrder.WinUI.Views;

// Server-provided public search markup only. No credentials, private context,
// network access, scripts or native bridge enter this renderer.
internal sealed class PlaygroundSearchSuggestionsView : Grid, IDisposable
{
    private readonly TextBlock notice = new() { Text = "正在准备 Google 搜索建议…", FontSize = 12, TextWrapping = TextWrapping.Wrap };
    private readonly string html;
    private readonly string? dataDirectory;
    private readonly Func<string, Task> openLink;
    private WebView2? web;
    private ScrollViewer? scrollHost;
    private bool disposed, loaded, attempted;
    private int generation, blocked;
    internal string State { get; private set; } = "idle";
    internal int BlockedRequests => blocked;
    public PlaygroundSearchSuggestionsView(string html, string? dataDirectory, Func<string, Task> openLink)
    {
        this.html = html; this.dataDirectory = dataDirectory; this.openLink = openLink;
        MinHeight = 56; Height = 116; Children.Add(notice);
        Loaded += (_, _) =>
        {
            loaded = true;
            for (var ancestor = VisualTreeHelper.GetParent(this); ancestor is not null; ancestor = VisualTreeHelper.GetParent(ancestor))
                if (ancestor is ScrollViewer scroll) { scrollHost = scroll; scrollHost.ViewChanged += Scrolled; break; }
            TryStartVisible();
        };
        LayoutUpdated += (_, _) => TryStartVisible();
        Unloaded += (_, _) => { loaded = false; attempted = false; DetachScroll(); Close(); };
        SizeChanged += (_, e) => Height = e.NewSize.Width < 480 ? 164 : 116;
    }
    private void TryStartVisible()
    {
        if (disposed || !loaded || attempted || web is not null || ActualWidth <= 0 || ActualHeight <= 0 || XamlRoot is null) return;
        try
        {
            var bounds = TransformToVisual(null).TransformBounds(new Rect(0, 0, ActualWidth, ActualHeight));
            if (!Intersects(bounds, new Rect(0, 0, XamlRoot.Size.Width, XamlRoot.Size.Height))) return;
            for (var ancestor = VisualTreeHelper.GetParent(this); ancestor is not null; ancestor = VisualTreeHelper.GetParent(ancestor))
                if (ancestor is ScrollViewer scroll && !Intersects(bounds, scroll.TransformToVisual(null).TransformBounds(new Rect(0, 0, scroll.ActualWidth, scroll.ActualHeight)))) return;
            attempted = true; _ = StartAsync();
        }
        catch { /* A recycled or detached row has no visible browser surface. */ }
    }
    private static bool Intersects(Rect a, Rect b) => a.X < b.X + b.Width && a.X + a.Width > b.X && a.Y < b.Y + b.Height && a.Y + a.Height > b.Y;
    private void Scrolled(object? sender, ScrollViewerViewChangedEventArgs e) => TryStartVisible();
    private void DetachScroll() { if (scrollHost is not null) scrollHost.ViewChanged -= Scrolled; scrollHost = null; }
    private async Task StartAsync()
    {
        if (disposed || web is not null) return;
        if (string.IsNullOrWhiteSpace(dataDirectory) || html.Length > 262144)
        { State = "unavailable"; notice.Text = "Google 搜索建议未能呈现；此公开资料答复的展示仍需核验。"; return; }
        int run = ++generation; State = "loading";
        var current = new WebView2(); web = current; Children.Insert(0, current);
        try
        {
            var directory = Path.GetFullPath(dataDirectory);
            if (Directory.Exists(directory) && (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) throw new IOException();
            var environment = await CoreWebView2Environment.CreateWithOptionsAsync(null, directory, new CoreWebView2EnvironmentOptions());
            if (disposed || run != generation) { current.Close(); return; }
            var options = environment.CreateCoreWebView2ControllerOptions(); options.IsInPrivateModeEnabled = true; options.ProfileName = "PlaygroundPublicSearch";
            await current.EnsureCoreWebView2Async(environment, options);
            if (disposed || run != generation) { current.Close(); return; }
            var core = current.CoreWebView2;
            core.Settings.IsScriptEnabled = false; core.Settings.AreHostObjectsAllowed = false; core.Settings.IsWebMessageEnabled = false;
            core.Settings.AreDevToolsEnabled = false; core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.IsPasswordAutosaveEnabled = false; core.Settings.IsGeneralAutofillEnabled = false;
            core.Settings.IsStatusBarEnabled = false; core.Settings.IsZoomControlEnabled = false;
            core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
            core.WebResourceRequested += (_, e) => { blocked++; e.Response = environment.CreateWebResourceResponse(null, 403, "Blocked", "Content-Type: text/plain"); };
            core.NavigationStarting += async (_, e) =>
            {
                if (e.Uri == "about:blank") return;
                e.Cancel = true;
                if (e.IsUserInitiated && PlaygroundMarkdownView.Allowed(e.Uri)) { try { await openLink(e.Uri); } catch { } }
            };
            core.NewWindowRequested += async (_, e) =>
            {
                e.Handled = true;
                if (e.IsUserInitiated && PlaygroundMarkdownView.Allowed(e.Uri)) { try { await openLink(e.Uri); } catch { } }
            };
            core.DownloadStarting += (_, e) => e.Cancel = true;
            core.PermissionRequested += (_, e) => e.State = CoreWebView2PermissionState.Deny;
            core.ProcessFailed += (_, _) => { if (run == generation) Fail(); };
            core.NavigationCompleted += (_, e) =>
            {
                if (run != generation) return;
                if (!e.IsSuccess) { Fail(); return; }
                State = "ready"; notice.Visibility = Visibility.Collapsed;
            };
            core.NavigateToString("<!doctype html><html><head><meta charset=\"utf-8\"><meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'none'; script-src 'none'; style-src 'unsafe-inline'; img-src 'none'; font-src 'none'; connect-src 'none'; frame-src 'none'; object-src 'none'; base-uri 'none'; form-action 'none'\"><meta name=\"color-scheme\" content=\"light dark\"></head><body style=\"margin:0;background:transparent;font-family:Segoe UI,sans-serif\">" + html + "</body></html>");
        }
        catch { if (run == generation && !disposed) Fail(); }
    }
    private void Fail()
    {
        Close();
        State = "unavailable"; notice.Text = "Google 搜索建议未能呈现；外部资源已阻止，此答复的展示仍需核验。";
        notice.Visibility = Visibility.Visible;
    }
    private void Close()
    {
        generation++; if (web is { } previous) { web = null; Children.Remove(previous); try { previous.Close(); } catch { } }
        if (!disposed) { State = "idle"; notice.Visibility = Visibility.Visible; }
    }
    public void Dispose() { disposed = true; DetachScroll(); Close(); }
}
