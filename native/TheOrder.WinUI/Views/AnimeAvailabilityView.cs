using System.Text;
using System.Text.Json;
using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.Web.WebView2.Core;
using TheOrder.Application;
using Windows.Storage.Streams;

namespace TheOrder.WinUI.Views;

// Public PV only. No OAuth, game/profile bridge, remote HTML or video downloads.
// A single private WebView exists only while the user expands PV; collapse releases it.
internal sealed class AnimeAvailabilityView : StackPanel, IDisposable
{
    private readonly TextBlock playerStatus = Text(13);
    private readonly Expander pv = new() { Header = "预告片 · PV", HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch };
    private readonly StackPanel pvBody = new() { Spacing = 10 };
    private readonly Grid host = new() { MaxWidth = 960, HorizontalAlignment = HorizontalAlignment.Center, Visibility = Visibility.Collapsed };
    private readonly Button retry = new() { Content = "重新加载 PV", CornerRadius = new(6), Visibility = Visibility.Collapsed };
    private readonly Button original = new() { Padding = new(12, 8, 12, 8), CornerRadius = new(6) };
    private CompositionRoundedRectangleGeometry? playerGeometry;
    private CompositionGeometricClip? playerClip;
    private PointerEventHandler? wheelHandler;
    private PointerEventHandler? interceptWheel;
    private int nativeWheels;
    private int? mediaId; private AniListTrailer? trailer; private WebView2? web; private long version; private bool disposed;
    private string state = "idle"; private int? error; private InMemoryRandomAccessStream? document;
    private string? playerProbe;
    public AnimeAvailabilityView(string automationPrefix = "Anime", string attribution = "AniList 收录的预告片，由原平台播放器提供播放。")
    {
        Spacing = 12; Children.Add(pv);
        AutomationProperties.SetAutomationId(this, automationPrefix + "Availability"); AutomationProperties.SetAutomationId(retry, automationPrefix + "RetryPV");
        AutomationProperties.SetAutomationId(original, automationPrefix + "PVOriginal");
        AutomationProperties.SetAutomationId(pv, automationPrefix + "PV");
        if (automationPrefix != "Anime") pv.Header = "播放影像 · YouTube";
        var actions = new TagFlowPanel { HorizontalSpacing = 10, VerticalSpacing = 8, CenterInlineItems = false, Children = { original, retry } };
        pvBody.Children.Add(Text(13, attribution));
        pvBody.Children.Add(Text(12, "可在播放器的“设置 → 画质”中选择清晰度；可用选项由 YouTube 提供。"));
        pvBody.Children.Add(actions); pvBody.Children.Add(playerStatus); pvBody.Children.Add(host); pv.Content = pvBody;
        retry.Click += async (_, _) => { Stop(); await StartAsync(); };
        original.Click += async (_, _) => { if (trailer is { } value) await OpenLinkAsync(value.Url); };
        pv.Expanding += async (_, _) => { if (!disposed && web is null) await StartAsync(); };
        pv.Collapsed += (_, _) => Stop(); host.SizeChanged += (_, _) => FitPlayer();
        pvBody.SizeChanged += (_, _) => { if (pvBody.ActualWidth > 0) host.Width = Math.Min(960, pvBody.ActualWidth); FitPlayer(); };
        Unloaded += (_, _) => { if (!IsLoaded) Stop(); };
    }
    public void Show(int? id, bool isAnime, AniListTrailer? value, bool canLoad = true)
    {
        if (disposed) return;
        if (mediaId != id || trailer != value || !isAnime) { Stop(); pv.IsExpanded = false; }
        mediaId = id; trailer = value; Visibility = isAnime && value is not null ? Visibility.Visible : Visibility.Collapsed;
        pv.IsEnabled = canLoad;
        original.Content = new TextBlock { Text = (value?.Site == "dailymotion" ? "Dailymotion" : "YouTube") + " ↗", IsHitTestVisible = false };
        AutomationProperties.SetName(original, "在原平台打开预告片");
        ToolTipService.SetToolTip(original, value?.Url);
        if (value is { CanEmbed: false }) playerStatus.Text = "此视频平台暂不支持应用内播放，可在原站观看。";
        if (canLoad && pv.IsExpanded && value?.CanEmbed == true && web is null && state != "failed") _ = StartAsync();
    }
    private async Task OpenLinkAsync(string url)
    {
        try { if (!AniListPlayback.SafeExternalUrl(url) || !await Windows.System.Launcher.LaunchUriAsync(new Uri(url))) throw new InvalidOperationException(); }
        catch { if (!disposed) { playerStatus.Text = "暂时无法打开原平台，请稍后重试。"; } }
    }
    private void FitPlayer()
    {
        if (host.ActualWidth <= 0 || web is null) return;
        host.Height = Math.Max(200, host.ActualWidth * 9 / 16);
        var visual = ElementCompositionPreview.GetElementVisual(host);
        playerGeometry ??= visual.Compositor.CreateRoundedRectangleGeometry();
        playerClip ??= visual.Compositor.CreateGeometricClip(playerGeometry);
        playerGeometry.Size = new((float)host.ActualWidth, (float)host.Height);
        playerGeometry.CornerRadius = new(10);
        visual.Clip = playerClip;
    }
    internal async Task StartAsync()
    {
        if (disposed || web is not null || trailer is not { CanEmbed: true } value || !AniListPlayback.ValidTrailer(value)) return;
        var run = ++version; state = "loading"; error = null; retry.Visibility = Visibility.Collapsed;
        playerStatus.Text = "正在加载原站播放器…"; host.Visibility = Visibility.Visible;
        var current = new WebView2 { DefaultBackgroundColor = Microsoft.UI.Colors.Transparent, MinWidth = 200, MinHeight = 200, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch };
        // Register before attachment/EnsureCoreWebView2Async: WebView2 installs
        // its normal wheel handlers when creating the core browser. Handling here
        // prevents Chromium from consuming vertical wheel input first.
        interceptWheel = (_, args) =>
        {
            if (run != version || disposed || !pv.IsExpanded) return;
            var properties = args.GetCurrentPoint(current).Properties;
            if (!properties.IsHorizontalMouseWheel && properties.MouseWheelDelta != 0) args.Handled = true;
        };
        current.AddHandler(UIElement.PointerWheelChangedEvent, interceptWheel, true);
        // Resume at the parent only after WebView2's own input handlers were skipped.
        // Clearing Handled on WebView2 itself is too late: Chromium has consumed
        // the input and interferes with WinUI's native inertia and wheel distance.
        wheelHandler = (_, args) =>
        {
            if (run != version || disposed || !pv.IsExpanded) return;
            var point = args.GetCurrentPoint(current);
            if (point.Properties.IsHorizontalMouseWheel) return;
            if (point.Properties.MouseWheelDelta == 0) return;
            nativeWheels++; args.Handled = false;
        };
        host.AddHandler(UIElement.PointerWheelChangedEvent, wheelHandler, true);
        web = current; host.Children.Add(current); pv.IsExpanded = true; FitPlayer();
        try
        {
            var profile = Program.Profile ?? throw new InvalidOperationException(); profile.ValidateAccess();
            var browserRoot = Path.Combine(profile.Root, "native-pv-browser-v1");
            if (Directory.Exists(browserRoot) && (File.GetAttributes(browserRoot) & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException();
            var environment = await CoreWebView2Environment.CreateWithOptionsAsync(null, browserRoot, new CoreWebView2EnvironmentOptions());
            if (run != version || disposed) { current.Close(); return; }
            var options = environment.CreateCoreWebView2ControllerOptions(); options.IsInPrivateModeEnabled = true; options.ProfileName = "PublicPV";
            await current.EnsureCoreWebView2Async(environment, options);
            if (run != version || disposed) { current.Close(); return; }
            var core = current.CoreWebView2;
            core.Settings.AreHostObjectsAllowed = false; core.Settings.AreDevToolsEnabled = false; core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.IsPasswordAutosaveEnabled = false; core.Settings.IsGeneralAutofillEnabled = false; core.Settings.IsStatusBarEnabled = false;
            document = new(); using (var writer = new DataWriter(document.GetOutputStreamAt(0))) { writer.WriteBytes(Encoding.UTF8.GetBytes(AniListPlayback.PlayerDocument(value))); await writer.StoreAsync(); }
            if (run != version || disposed) { current.Close(); return; }
            core.AddWebResourceRequestedFilter(AniListPlayback.DocumentUrl, CoreWebView2WebResourceContext.Document);
            core.WebResourceRequested += (_, args) => { if (run == version && args.Request.Uri == AniListPlayback.DocumentUrl && document is not null) args.Response = environment.CreateWebResourceResponse(document, 200, "OK", "Content-Type: text/html; charset=utf-8\r\nReferrer-Policy: strict-origin-when-cross-origin\r\nCache-Control: no-store"); };
            core.NavigationStarting += (_, args) => { if (args.Uri != AniListPlayback.DocumentUrl && args.Uri != "about:blank") args.Cancel = true; };
            core.PermissionRequested += (_, args) => args.State = CoreWebView2PermissionState.Deny;
            core.DownloadStarting += (_, args) => args.Cancel = true;
            core.NewWindowRequested += async (_, args) => { args.Handled = true; if (args.IsUserInitiated && run == version && AniListPlayback.SafeExternalUrl(args.Uri)) await OpenLinkAsync(args.Uri); };
            core.WebMessageReceived += (_, args) =>
            {
                if (run != version || args.Source != AniListPlayback.DocumentUrl) return;
                try
                {
                    using var json = JsonDocument.Parse(args.WebMessageAsJson); var kind = json.RootElement.GetProperty("kind").GetString(); var value = json.RootElement.GetProperty("value").GetInt32();
                    if (kind == "ready") { state = "ready"; playerStatus.Text = "点击原站播放器开始播放 · YouTube"; }
                    else if (kind == "state") { state = value switch { 1 => "playing", 2 => "paused", 0 => "ended", 3 => "buffering", _ => "ready" }; playerStatus.Text = state == "playing" ? "正在播放 · YouTube" : "PV · YouTube"; }
                    else if (kind == "error") { error = value; state = "failed"; playerStatus.Text = AniListPlayback.PlayerError(value); retry.Visibility = Visibility.Visible; }
                }
                catch (Exception e) when (e is JsonException or InvalidOperationException or KeyNotFoundException) { }
            };
            core.ProcessFailed += (_, _) => { if (run == version) Fail("播放器暂时不可用，请重新加载或在原站观看。"); };
            core.Navigate(AniListPlayback.DocumentUrl);
            // Bound readiness wait; Close prevents late startup/results reviving an old view.
            for (var i = 0; i < 100 && run == version && state == "loading"; i++) await Task.Delay(200);
            if (run == version && state == "loading") Fail("播放器加载超时，请重试或在原站观看。");
        }
        catch (Exception) { if (run == version && !disposed) Fail("无法加载播放器。请确认已安装 Microsoft Edge WebView2，并检查网络；仍可在原站观看。"); }
    }
    private void Fail(string message) { Stop(); state = "failed"; playerStatus.Text = message; retry.Visibility = Visibility.Visible; }
    internal void Stop()
    {
        version++; var previous = web; web = null;
        if (previous is not null && interceptWheel is not null) previous.RemoveHandler(UIElement.PointerWheelChangedEvent, interceptWheel);
        if (wheelHandler is not null) host.RemoveHandler(UIElement.PointerWheelChangedEvent, wheelHandler);
        interceptWheel = null; wheelHandler = null; try { previous?.Close(); } catch { } host.Children.Clear(); document?.Dispose(); document = null;
        if (playerClip is not null) { ElementCompositionPreview.GetElementVisual(host).Clip = null; playerClip.Dispose(); playerClip = null; playerGeometry?.Dispose(); playerGeometry = null; }
        host.Visibility = Visibility.Collapsed; retry.Visibility = Visibility.Collapsed;
        state = "idle"; error = null; playerProbe = null; playerStatus.Text = "展开后自动加载预告片，收起或离开页面时关闭。";
    }
    internal object Evidence => new { state, error, hasPlayer = web?.CoreWebView2 is not null, inPrivate = web?.CoreWebView2?.Profile.IsInPrivateModeEnabled, mediaId, trailer,
        playerWidth = host.ActualWidth, playerHeight = host.ActualHeight, playerProbe, message = playerStatus.Text,
        expanded = pv.IsExpanded, initialLoadButton = false, playerCentered = host.HorizontalAlignment == HorizontalAlignment.Center, playerRounded = playerClip is not null, nativeWheels };
    public void Expand() => pv.IsExpanded = true;
    internal void ExpandForFixture() => Expand();
    internal void CollapseForFixture() => pv.IsExpanded = false;
    // Fixed action, available only through the existing Test-scoped fixture.
    internal async Task PlayForFixtureAsync() { if (web?.CoreWebView2 is { } core && state is "ready" or "paused") await core.ExecuteScriptAsync("if(typeof player!=='undefined'&&player&&player.playVideo){player.mute();player.playVideo();}"); }
    internal async Task ProbeForFixtureAsync() { if (web?.CoreWebView2 is { } core) playerProbe = await core.ExecuteScriptAsync("({page:location.href,ready:document.readyState,api:typeof YT,apiPlayer:typeof YT==='undefined'?null:typeof YT.Player,callback:typeof window.onYouTubeIframeAPIReady,iframe:document.querySelector('iframe')?.src??null,player:typeof player!=='undefined'&&player&&player.getPlayerState?({state:player.getPlayerState(),seconds:player.getCurrentTime(),duration:player.getDuration()}):null})"); }
    public void Dispose() { if (disposed) return; Stop(); disposed = true; trailer = null; }
    private static TextBlock Text(double size, string value = "") => new() { FontSize = size, Text = value, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
}
