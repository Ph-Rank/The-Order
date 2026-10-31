using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using TheOrder.Application;

namespace TheOrder.WinUI.Views;

// Provider-independent renderer. PV is a separate component and is never placed
// among full-episode playback links. Future sources supply the same plain model.
internal sealed class WatchLinksView : StackPanel
{
    private readonly Expander section = new SmoothExpander() { Header = "正版观看", HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch };
    private readonly StackPanel body = new() { Spacing = 12 };
    private readonly TextBlock caption = Text(13), warning = Text(13);
    private readonly TextBlock dataStatus = Text(13);
    private readonly TagFlowPanel cards = new() { HorizontalSpacing = 10, VerticalSpacing = 8, CenterInlineItems = false, MaximumItemWidth = 340 };
    private readonly StackPanel sourceBody = new() { Spacing = 10 };
    private readonly Expander sources = new SmoothExpander() { Header = "观看链接来源", HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch };
    private AnimeWatchIdentity? identity; private WatchLink[] values = [];
    private WatchDataResult? mapping;
    private const WatchLinkSort sort = WatchLinkSort.BilibiliFirst; private bool disposed;
    public WatchLinksView()
    {
        HorizontalAlignment = HorizontalAlignment.Stretch;
        AutomationProperties.SetAutomationId(section, "WatchLinks"); section.Content = body; Children.Add(section);
        sources.Content = sourceBody; body.Children.Add(caption); body.Children.Add(cards); body.Children.Add(dataStatus); body.Children.Add(warning); body.Children.Add(sources);
        warning.Visibility = Visibility.Collapsed;
        cards.SizeChanged += (_, _) => { if (cards.ActualWidth > 0) foreach (var card in cards.Children.OfType<FrameworkElement>()) card.MaxWidth = Math.Min(340, cards.ActualWidth); };
    }
    // Retain the optional store boundary for compatibility; existing preference
    // files are untouched. The current UI always uses Bilibili-first ordering.
    public void Configure(IWatchLinkPreferencesStore? preferenceStore) { }
    public void Show(AnimeWatchIdentity? current, IEnumerable<WatchLink> data, WatchDataResult? result = null)
    {
        if (disposed) return;
        if (identity is null || current is null || !identity.Matches(current)) section.IsExpanded = false;
        values = data.Where(x => x.Kind is WatchLinkKind.Playback or WatchLinkKind.Information).Take(128).ToArray(); identity = current; mapping = result;
        Visibility = current is null ? Visibility.Collapsed : Visibility.Visible; Render();
    }
    private void Render()
    {
        cards.Children.Clear(); sourceBody.Children.Clear(); if (identity is null) return;
        var combined = WatchLinks.Merge(identity, values, sort); var watching = combined.Where(x => x.Kind is WatchLinkKind.Playback or WatchLinkKind.Information).ToArray();
        caption.Text = watching.Length > 0 ? "收录播放入口与平台作品页；地区、订阅和当前版权以平台为准，来源收录不代表现在可播放。"
            : "暂未收录可靠的播放入口，可继续查看原站；这不代表作品没有正版资源。";
        dataStatus.Text = mapping?.Message ?? ""; dataStatus.Visibility = dataStatus.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        foreach (var item in watching)
        {
            var content = new StackPanel { IsHitTestVisible = false, Spacing = 4, Children = { Text(15, item.Platform + " ↗"), Text(12, Region(item)) } };
            var link = new Button { Content = content, CornerRadius = new(6), Padding = new(12, 8, 12, 8), HorizontalContentAlignment = HorizontalAlignment.Left, MaxWidth = cards.ActualWidth > 0 ? Math.Min(340, cards.ActualWidth) : 340 };
            AutomationProperties.SetAutomationId(link, "WatchPlatform-" + WatchLinks.PlatformKey(item));
            var purpose = item.Kind == WatchLinkKind.Playback ? "播放入口" : "平台作品页，非直接播放链接";
            AutomationProperties.SetName(link, item.Platform + " · " + purpose + " · " + Region(item));
            ToolTipService.SetToolTip(link, purpose + "\n" + item.Url + (item.Notes is { Length: > 0 } notes ? "\n" + notes : ""));
            link.Click += async (_, _) => await OpenAsync(item.Url); cards.Children.Add(link);
        }
        foreach (var item in combined)
        {
            var record = new StackPanel { Spacing = 5, Children = { Text(14, item.Platform + " · " + Region(item)), Text(13, (item.Kind == WatchLinkKind.Information ? "平台作品页 · " : "播放入口 · ") + Verification(item) + "；未核验当前播放权限") } };
            record.Children.Add(Link("打开原链接", item.Url));
            if (item.Notes is { Length: > 0 }) record.Children.Add(Text(12, item.Notes));
            foreach (var proof in item.Sources.Where(x => WatchLinks.Classify(x.Url, WatchLinkKind.Playback) != WatchLinkKind.Information)) { record.Children.Add(Link(proof.Source, proof.Url)); if (proof.DataVersion is { } version) record.Children.Add(Text(12, "数据版本 · " + version)); record.Children.Add(Text(12, proof.Note)); }
            sourceBody.Children.Add(record);
        }
        sources.Visibility = combined.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }
    private HyperlinkButton Link(string text, string url) { var link = new HyperlinkButton { Content = text, Padding = new(0), HorizontalAlignment = HorizontalAlignment.Left }; link.Click += async (_, _) => await OpenAsync(url); return link; }
    private async Task OpenAsync(string url)
    {
        try { if (!WatchLinks.SafeUrl(url) || !await Windows.System.Launcher.LaunchUriAsync(new Uri(url))) throw new InvalidOperationException(); }
        catch { if (!disposed) { warning.Text = "暂时无法打开平台，请稍后重试。"; warning.Visibility = Visibility.Visible; } }
    }
    internal object Evidence => new { identity, sort, links = identity is null ? [] : WatchLinks.Merge(identity, values, sort), expanded = section.IsExpanded, visibleCards = section.IsExpanded ? cards.Children.Count : 0, totalCards = cards.Children.Count,
        sortControls = body.Children.OfType<ComboBox>().Count(), subtitles = cards.Children.OfType<Button>().Select(x => (x.Content as StackPanel)?.Children.OfType<TextBlock>().LastOrDefault()?.Text).ToArray(),
        sourceDetailsExpanded = sources.IsExpanded, hitTargets = cards.Children.OfType<Button>().Select(InteractiveCardEvidence.Hit).ToArray(), mapping };
    public void Expand() { section.IsExpanded = true; section.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = false, VerticalAlignmentRatio = .05 }); }
    internal void ShowForFixture() => Expand();
    internal void CollapseForFixture() { section.IsExpanded = false; section.StartBringIntoView(); }
    internal void SourcesForFixture() { section.IsExpanded = true; sources.IsExpanded = true; sources.StartBringIntoView(); }
    public void Dispose() { disposed = true; values = []; identity = null; cards.Children.Clear(); sourceBody.Children.Clear(); }
    private static string Region(WatchLink value) => value.Regions.Length == 0 ? "未标注地区" : string.Join(" / ", value.Regions.Select(x => x switch { "CN" => "中国大陆", "HK" => "香港", "MO" => "澳门", "TW" => "台湾", "JP" => "日本", _ => x }));
    private static string Verification(WatchLink value) => value.Verification == WatchLinkVerification.PageIdentityChecked ? "页面对应已核对" : value.Verification == WatchLinkVerification.UserSupplied ? "用户提供" : "来源收录";
    private static TextBlock Text(double size, string value = "") => new() { FontSize = size, Text = value, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
}
