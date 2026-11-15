using System.ComponentModel;
using System.Security.Cryptography;
using System.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using TheOrder.Application;
using TheOrder.Presentation;

namespace TheOrder.WinUI.Views;

internal sealed class BookReadingView : StackPanel, IDisposable
{
    private readonly Expander section = new SmoothExpander() { Header = "阅读与购买", HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch };
    private readonly Expander sources = new SmoothExpander { Header = "资料与来源", HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch };
    private readonly StackPanel entries = new() { Spacing = 12 }, evidence = new() { Spacing = 8 };
    private readonly TextBlock status = Text(13);
    private readonly Button refresh = new() { Content = "刷新入口" }, cancel = new() { Content = "取消读取", Visibility = Visibility.Collapsed };
    private BookReadingViewModel? model;
    private bool syncing, disposed;
    public BookReadingView()
    {
        AutomationProperties.SetAutomationId(section, "BookReadingExpand"); AutomationProperties.SetAutomationId(sources, "BookReadingSources");
        AutomationProperties.SetAutomationId(refresh, "BookReadingRefresh"); AutomationProperties.SetAutomationId(cancel, "BookReadingCancel");
        AutomationProperties.SetAutomationId(status, "BookReadingStatus"); AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);
        sources.Content = new StackPanel { Spacing = 10, Children = { refresh, evidence } };
        section.Content = new StackPanel { Spacing = 12, Children = { status, entries, cancel, sources } }; Children.Add(section);
        section.Expanding += async (_, _) => { if (!syncing && model is not null) await model.ExpandAsync(); };
        section.Collapsed += (_, _) => { if (!syncing) model?.Collapse(); };
        refresh.Click += async (_, _) => { if (model is not null) await model.ExpandAsync(true); };
        cancel.Click += (_, _) => model?.Cancel();
        Visibility = Visibility.Collapsed;
    }
    public void Configure(IAniListReader? aniList, IBangumiSubjectDetailsReader subjects)
    {
        if (model is not null) { model.PropertyChanged -= Changed; model.Dispose(); }
        model = aniList is null ? null : new(aniList, subjects); if (model is not null) model.PropertyChanged += Changed;
    }
    public void SetSource(BookSource? source, AniListDetails? aniList, BangumiSubjectDetails? bangumi)
    {
        if (disposed || model is null) return;
        if (model.Source?.Fingerprint != source?.Fingerprint)
        {
            syncing = true; section.IsExpanded = false; syncing = false;
            sources.IsExpanded = false;
        }
        model.SetSource(source, aniList, bangumi); Visibility = source is null ? Visibility.Collapsed : Visibility.Visible;
        section.Header = source?.Identity.Level is BookEntityLevel.Volume or BookEntityLevel.Publication ? "阅读与购买 · 当前卷册" : "阅读与购买";
    }
    private BookOutletLink[] shownLinks = [];
    private void Changed(object? sender, PropertyChangedEventArgs args) => Render();
    private void Render()
    {
        if (disposed || model is null) return;
        var reveal = !ReferenceEquals(shownLinks, model.Links) && model.Links.Length > 0;
        shownLinks = model.Links;
        entries.Children.Clear(); evidence.Children.Clear();
        status.Text = model.Message; status.Visibility = model.Busy || status.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        refresh.IsEnabled = model.CanLoad; cancel.Visibility = model.Busy ? Visibility.Visible : Visibility.Collapsed;
        if (model.Source is not { } current) return;
        var flow = new TagFlowPanel { HorizontalSpacing = 8, VerticalSpacing = 8, MaximumItemWidth = 430 };
        foreach (var link in model.Links.Where(x => x.Kind != BookOutletKind.Information).OrderBy(x => x.Kind))
        {
            var caption = string.Join(" · ", new[] { BookOutlets.KindName(link), BookOutlets.LanguageName(link.Language) });
            var scope = BookOutlets.ScopeName(link, current);
            var label = new StackPanel { Spacing = 3, IsHitTestVisible = false, Children = { Text(14, link.Platform), Text(12, caption) } };
            if (scope != "系列入口") label.Children.Add(Text(12, scope));
            var button = new ExternalLinkButton { Content = label, Padding = new(12, 8, 12, 8), MaxWidth = 430 };
            var id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(BookOutlets.Key(link))))[..16];
            AutomationProperties.SetAutomationId(button, "BookPlatform-" + id);
            AutomationProperties.SetName(button, link.Platform + " · " + caption + " · " + scope);
            ToolTipService.SetToolTip(button, scope + (link.Storefront is { } market ? "\n商店站点 · " + market + "（不代表可用地区）" : "") + "\n" + link.Url);
            button.Click += async (_, _) => await OpenAsync(link.Url, BookOutlets.Key(link)); flow.Children.Add(button);
        }
        if (flow.Children.Count > 0) entries.Children.Add(flow);
        if (entries.Children.Count == 0 && !model.Busy && model.Message.Length == 0) entries.Children.Add(Text(13, "暂未收录阅读或购买入口。"));
        foreach (var link in model.Links)
        {
            var row = new StackPanel { Spacing = 3 };
            if (link.Kind == BookOutletKind.Information)
            {
                var button = new ExternalLinkButton { Content = link.Platform + " · 资料来源" };
                button.Click += async (_, _) => await OpenAsync(link.Url, BookOutlets.Key(link)); row.Children.Add(button);
            }
            else row.Children.Add(Text(13, link.Platform + " · " + BookOutlets.KindName(link)));
            foreach (var proof in link.Sources)
            {
                row.Children.Add(Text(12, $"{proof.Identity.Provider} · {proof.Identity.Id} · {proof.Identity.Level} · {proof.Format} · {proof.FetchedAtUtc.ToLocalTime():yyyy-MM-dd HH:mm}"));
                row.Children.Add(Text(12, "原始类型 · " + proof.Original.Type + (proof.Original.Notes is { Length: > 0 } note ? " · " + note : "")));
                if (proof.CrossReference is { } crossReference) row.Children.Add(Text(12, crossReference));
            }
            row.Children.Add(Text(12, link.Url)); evidence.Children.Add(row);
        }
        foreach (var group in model.Excluded.GroupBy(x => x.Reason)) evidence.Children.Add(Text(12, group.Key + " · " + group.Count() + " 项"));
        evidence.Children.Add(Text(12, "译本和地区可用性以平台为准。"));
        if (reveal) InteractionMotion.RevealContent(entries, .72f);
    }
    private async Task OpenAsync(string url, string linkKey)
    {
        var version = model?.Version; var source = model?.Source?.Fingerprint;
        try
        {
            if (source is null || model?.Links.Any(x => BookOutlets.Key(x) == linkKey && x.Url == url) != true || !WatchLinks.SafeUrl(url)
                || !await Windows.System.Launcher.LaunchUriAsync(new Uri(url))) throw new InvalidOperationException();
        }
        catch
        {
            if (model?.Version != version || model?.Source?.Fingerprint != source) return;
            status.Text = "无法打开平台页面，请检查默认浏览器后重试。"; status.Visibility = Visibility.Visible;
        }
    }
    internal object Evidence => new { source = model?.Source?.Identity, expanded = section.IsExpanded, busy = model?.Busy,
        links = model?.Links, excluded = model?.Excluded, status = status.Text, sourcesExpanded = sources.IsExpanded,
        visibleEntries = section.IsExpanded ? entries.Children.Count : 0 };
    public void Deactivate() { model?.Deactivate(); Visibility = Visibility.Collapsed; }
    public void Dispose() { if (disposed) return; Deactivate(); disposed = true; if (model is not null) { model.PropertyChanged -= Changed; model.Dispose(); } }
    private static TextBlock Text(double size, string text = "") => new() { FontSize = size, Text = text, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
}
