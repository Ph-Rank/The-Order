using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using TheOrder.Application;
using TheOrder.Presentation;

namespace TheOrder.WinUI.Views;

public sealed class MusicListeningView : Grid, IDisposable
{
    private readonly Expander section = new() { Header = "正版收听与商店", HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch };
    private readonly StackPanel direct = new() { Spacing = 10 }, digital = new() { Spacing = 12 }, saved = new() { Spacing = 8 }, candidates = new() { Spacing = 12 }, evidence = new() { Spacing = 8 };
    private readonly TextBlock status = Text(13), caption = Text(13, "这里只提供来源已收录或你确认的页面。平台存在不代表当前地区、账号或订阅可以播放。");
    private readonly Button load = Button("重读入口"), other = Button("查找其他数字发行入口"), apple = Button("查找 iTunes 商店专辑"), cancel = Button("取消读取");
    private readonly ComboBox order = new() { Header = "入口排序", MinWidth = 150, SelectedIndex = 0 }, country = new() { Header = "iTunes 商店地区", MinWidth = 140, SelectedIndex = 0 };
    private readonly Expander details = new() { Header = "来源与版本说明", HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch };
    private MusicListeningViewModel? model;
    private ContentDialog? dialog;
    private bool disposed;
    public MusicListeningView()
    {
        Visibility = Visibility.Collapsed;
        AutomationProperties.SetAutomationId(section, "MusicListeningExpand"); AutomationProperties.SetAutomationId(load, "MusicListeningLoad");
        AutomationProperties.SetAutomationId(other, "MusicListeningDigital"); AutomationProperties.SetAutomationId(apple, "MusicListeningApple");
        AutomationProperties.SetAutomationId(cancel, "MusicListeningCancel"); AutomationProperties.SetAutomationId(status, "MusicListeningStatus");
        AutomationProperties.SetAutomationId(order, "MusicListeningOrder"); AutomationProperties.SetAutomationId(country, "MusicListeningCountry");
        AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);
        foreach (var item in new[] { "国内平台优先", "来源顺序", "平台名称" }) order.Items.Add(item);
        foreach (var item in new[] { "日本 JP", "中国大陆 CN", "美国 US" }) country.Items.Add(item);
        var controls = new TagFlowPanel { HorizontalSpacing = 10, VerticalSpacing = 8, Children = { order, load, cancel } };
        var extra = new TagFlowPanel { HorizontalSpacing = 10, VerticalSpacing = 8, Children = { other, country, apple } };
        details.Content = evidence;
        section.Content = new StackPanel { Spacing = 16, Children = { caption, controls, status, direct, extra, digital, saved, candidates, details } };
        Children.Add(section);
        section.Expanding += async (_, _) => { if (model is { Current: null, CanLoad: true }) await model.LoadAsync(); };
        load.Click += async (_, _) => { if (model is not null) await model.LoadAsync(true); };
        other.Click += async (_, _) => { if (model is not null) await model.FindDigitalAsync(); };
        apple.Click += async (_, _) => { if (model is not null) { var selected = MusicOutlets.StoreCountries[Math.Clamp(country.SelectedIndex, 0, 2)]; await model.FindAppleAsync(selected, model.Apple?.Query.Country == selected); } };
        cancel.Click += (_, _) => model?.Cancel(); order.SelectionChanged += (_, _) => Render();
    }
    public void Configure(IMusicListeningReader? reader)
    {
        dialog?.Hide();
        if (model is not null) { model.PropertyChanged -= Changed; model.Dispose(); }
        model = reader is null ? null : new(reader); if (model is not null) model.PropertyChanged += Changed;
    }
    public void SetContext(MusicBrainzReference? reference)
    {
        if (disposed || model is null) { Visibility = Visibility.Collapsed; return; }
        if (model.Reference != reference) { dialog?.Hide(); section.IsExpanded = false; details.IsExpanded = false; }
        model.SetContext(reference); Visibility = MusicOutlets.Reference(model.Reference) ? Visibility.Visible : Visibility.Collapsed;
    }
    private void Changed(object? sender, PropertyChangedEventArgs args) => Render();
    private MusicOutletSort Sort => (MusicOutletSort)Math.Clamp(order.SelectedIndex, 0, 2);
    private void Render()
    {
        if (disposed || model is null) return;
        status.Text = model.Message; load.IsEnabled = model.CanLoad; cancel.Visibility = Visible(model.Busy);
        other.IsEnabled = model.CanFindDigital; apple.IsEnabled = country.IsEnabled = model.CanFindApple;
        other.Visibility = country.Visibility = apple.Visibility = Visible(model.Reference?.Kind == MusicBrainzEntityKind.Release);
        direct.Children.Clear(); digital.Children.Clear(); candidates.Children.Clear(); saved.Children.Clear(); evidence.Children.Clear();
        if (model.Current is { } current)
        {
            direct.Children.Add(Text(15, current.Snapshot.Entity.Kind == MusicBrainzEntityKind.Release ? "当前发行版的来源外链" : "此录音的来源外链"));
            AddLinks(direct, current.Snapshot.Links);
            if (current.Snapshot.Links.Length == 0) direct.Children.Add(Text(13, "尚未收录可核对的外链，可按需查找其他数字版本或商店页面。"));
            evidence.Children.Add(Text(12, $"{current.Snapshot.Entity.Kind} · {current.Snapshot.Entity.Id} · 读取于 {current.Snapshot.FetchedAtUtc:yyyy-MM-dd HH:mm} UTC"));
        }
        if (model.Digital is { } others)
        {
            digital.Children.Add(Text(16, "其他数字发行版本"));
            digital.Children.Add(Text(13, "这些页面属于同专辑组的其他发行物，曲目、时长或母带可能不同；当前选择不变。"));
            foreach (var item in others.Editions)
            {
                var box = new StackPanel { Spacing = 8, Children = { Text(15, item.Snapshot.Title), Text(13, string.Join(" · ", new[] { item.Snapshot.Artist, item.Snapshot.Date, item.Snapshot.TrackCount is { } n ? $"{n} 首" : "" }.Where(x => !string.IsNullOrWhiteSpace(x)))) } };
                AddLinks(box, item.Snapshot.Links); digital.Children.Add(box);
            }
            if (others.Warning is { } warning) digital.Children.Add(Text(12, warning));
        }
        if (model.Choices?.Items.Length > 0)
        {
            saved.Children.Add(Text(16, "你选择的 iTunes 商店页面"));
            saved.Children.Add(Text(12, "独立关联，不代表相同发行版本或 Apple Music 订阅可听。"));
            foreach (var choice in model.Choices.Items)
            {
                if (!MediaUserLinkRules.Valid(choice) || choice.Target.Provider != "iTunes" || !AppleAlbumCandidate.TryAlbumUrl(choice.Note, choice.Target.Resource[6..], out var id) || id != choice.Target.Id) continue;
                var row = new TagFlowPanel { HorizontalSpacing = 10, VerticalSpacing = 6 };
                row.Children.Add(External(choice.Title + " · " + choice.Target.Resource[6..] + " 商店 ↗", choice.Note));
                var remove = Button("移除此页面"); remove.Click += async (_, _) =>
                {
                    var current = model; var reference = current?.Reference;
                    if (current is null || dialog is not null) return;
                    dialog = new() { XamlRoot = XamlRoot, RequestedTheme = ActualTheme, Title = "移除这个商店关联？", Content = "当前发行版和其他资料保持。", PrimaryButtonText = "移除", CloseButtonText = "取消", DefaultButton = ContentDialogButton.Close };
                    try { if (await dialog.ShowAsync() == ContentDialogResult.Primary && ReferenceEquals(current, model) && current.Reference == reference) await current.RemoveAppleAsync(choice); }
                    finally { dialog = null; }
                }; row.Children.Add(remove); saved.Children.Add(row);
            }
        }
        if (model.Apple is { } result)
        {
            candidates.Children.Add(Text(16, "iTunes 商店候选 · " + result.Query.Country));
            candidates.Children.Add(Text(12, "仅为商店目录；核对后可保存，不会自动绑定或推断订阅播放资格。"));
            foreach (var candidate in result.Candidates)
            {
                var box = new StackPanel { Spacing = 6, Children = { Text(15, candidate.Title), Text(13, candidate.Artist), Text(12, $"{candidate.ReleaseDate} · {candidate.TrackCount} 首 · {candidate.Country} 商店 · iTunes {candidate.Id}") } };
                if (model.Current?.Snapshot is { } source) foreach (var difference in candidate.Differences(source)) box.Children.Add(Text(12, difference));
                box.Children.Add(External("查看商店页面 ↗", candidate.Url));
                var save = Button("保存这个商店页面"); save.IsEnabled = model.CanLoad; AutomationProperties.SetAutomationId(save, "MusicAppleSave-" + candidate.Id);
                save.Click += async (_, _) => await ConfirmApple(candidate, result); box.Children.Add(save);
                candidates.Children.Add(new Border { Padding = new(12), CornerRadius = new(6), Background = (Microsoft.UI.Xaml.Media.Brush)Microsoft.UI.Xaml.Application.Current.Resources["CardBackgroundFillColorDefaultBrush"], Child = box });
            }
            evidence.Children.Add(Text(12, $"Apple 官方目录 · {result.Query.Country} · {result.FetchedAtUtc:yyyy-MM-dd HH:mm} UTC · 已排除 {result.Discarded} 条无法对应的结果"));
            if (result.Limited) evidence.Children.Add(Text(12, "仅检查有限候选，不能由唯一返回项推断确定身份。"));
        }
    }
    private void AddLinks(StackPanel body, IEnumerable<MusicOutletLink> input)
    {
        foreach (var group in MusicOutlets.Merge(input, Sort).GroupBy(x => x.Kind))
        {
            body.Children.Add(Text(13, group.Key switch { MusicOutletKind.Listening => "收听入口", MusicOutletKind.Purchase => "购买页面", _ => "资料链接" }));
            var flow = new TagFlowPanel { HorizontalSpacing = 8, VerticalSpacing = 8 };
            foreach (var value in group)
            {
                var label = value.Regions.Length == 0 ? "地区未注明" : "商店地区 · " + string.Join(" / ", value.Regions);
                var content = new StackPanel { IsHitTestVisible = false, Spacing = 4, Children = { Text(15, value.Platform + " ↗"), Text(12, label + " · " + (value.TargetLayer == "Album" ? "专辑页" : value.TargetLayer == "Recording" ? "曲目页" : "资料页")) } };
                var link = new Button { Content = content, CornerRadius = new(6), Padding = new(12, 8, 12, 8), HorizontalContentAlignment = HorizontalAlignment.Left, MaxWidth = 340 };
                ToolTipService.SetToolTip(link, value.Url); AutomationProperties.SetName(link, value.Platform + " · " + group.Key + " · " + label);
                link.Click += async (_, _) => await Open(value.Url); flow.Children.Add(link);
                foreach (var source in value.Sources) evidence.Children.Add(Text(12, value.Platform + " · " + source.Source + " · " + source.Note + " · " + source.Url));
            }
            body.Children.Add(flow);
        }
    }
    private async Task ConfirmApple(AppleAlbumCandidate candidate, AppleCatalogResult result)
    {
        var current = model; var reference = current?.Reference; var source = current?.Current?.Snapshot;
        if (current is null || source is null || current.Apple != result || !current.CanLoad || dialog is not null) return;
        var content = new StackPanel { Spacing = 8, Children = { Text(14, source.Title + " · " + source.Artist), Text(14, candidate.Title + " · " + candidate.Artist), Text(13, "保存为独立商店页面关联。不会更换当前发行版，也不表示 Apple Music 订阅可听。") } };
        foreach (var difference in candidate.Differences(source)) content.Children.Add(Text(13, difference));
        dialog = new() { XamlRoot = XamlRoot, RequestedTheme = ActualTheme, Title = "保存这个 iTunes 商店页面？", Content = content, PrimaryButtonText = "核对后保存", CloseButtonText = "取消", DefaultButton = ContentDialogButton.Close };
        try { if (await dialog.ShowAsync() == ContentDialogResult.Primary && ReferenceEquals(current, model) && current.Reference == reference && current.Apple == result) await current.SaveAppleAsync(candidate); }
        finally { dialog = null; }
    }
    private HyperlinkButton External(string label, string url) { var link = new HyperlinkButton { Content = new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap, IsHitTestVisible = false }, Padding = new(0), HorizontalAlignment = HorizontalAlignment.Left }; link.Click += async (_, _) => await Open(url); return link; }
    private async Task Open(string url)
    {
        try { if (!WatchLinks.SafeUrl(url) || !await Windows.System.Launcher.LaunchUriAsync(new Uri(url))) throw new InvalidOperationException(); }
        catch { status.Text = "暂时无法打开平台页面，请稍后重试。"; }
    }
    private static TextBlock Text(double size, string value = "") => new() { Text = value, FontSize = size, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
    private static Button Button(string value) => new() { Content = value, MinHeight = 32, CornerRadius = new(6), HorizontalAlignment = HorizontalAlignment.Left };
    private static Visibility Visible(bool value) => value ? Visibility.Visible : Visibility.Collapsed;
    internal object Evidence => new { reference = model?.Reference, expanded = section.IsExpanded, busy = model?.Busy, current = model?.Current, digital = model?.Digital, apple = model?.Apple, saved = model?.Choices?.Items, status = status.Text };
    public void Cancel() => model?.Cancel();
    public void Deactivate() { dialog?.Hide(); model?.SetContext(null); section.IsExpanded = false; Visibility = Visibility.Collapsed; }
    public void Dispose() { if (disposed) return; Deactivate(); disposed = true; if (model is not null) { model.PropertyChanged -= Changed; model.Dispose(); } }
}
