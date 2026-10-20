using System.ComponentModel;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using TheOrder.Application;
using TheOrder.Presentation;

namespace TheOrder.WinUI.Views;

public sealed class MusicListeningView : Grid, IDisposable
{
    private readonly Expander section = new() { Header = "正版收听与购买", HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch };
    private readonly StackPanel entries = new() { Spacing = 14 }, saved = new() { Spacing = 6 }, evidence = new() { Spacing = 8 };
    private readonly StackPanel options = new() { Spacing = 12, Visibility = Visibility.Collapsed };
    private readonly TextBlock status = Text(13);
    private readonly Button load = Button("刷新入口"), cancel = Button("取消");
    private readonly Button details = new() { Content = "资料与来源 ⌄" };
    private MusicListeningViewModel? model;
    private ContentDialog? dialog;
    private bool disposed;
    public MusicListeningView()
    {
        Visibility = Visibility.Collapsed;
        AutomationProperties.SetAutomationId(section, "MusicListeningExpand"); AutomationProperties.SetAutomationId(load, "MusicListeningLoad");
        AutomationProperties.SetAutomationId(details, "MusicListeningDetails");
        AutomationProperties.SetAutomationId(cancel, "MusicListeningCancel"); AutomationProperties.SetAutomationId(status, "MusicListeningStatus");
        AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);
        var toolbar = new TagFlowPanel { HorizontalSpacing = 10, VerticalSpacing = 6, Children = { details, cancel } };
        options.Children.Add(load);
        options.Children.Add(evidence);
        var attribution = Text(12, "MusicBrainz 收录 · 地区及订阅条件以平台为准"); attribution.Opacity = .7;
        section.Content = new StackPanel { Spacing = 12, Children = { status, entries, saved, toolbar, options, attribution } };
        Children.Add(section);
        section.Expanding += async (_, _) => { if (model is not null) await model.ExpandAsync(); };
        section.Collapsed += (_, _) => { if (model?.Busy == true) model.Cancel(); };
        load.Click += async (_, _) => { if (model is not null) await model.ExpandAsync(true); };
        cancel.Click += (_, _) => model?.Cancel();
        details.Click += (_, _) => { options.Visibility = options.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible; details.Content = options.Visibility == Visibility.Visible ? "收起资料与来源 ⌃" : "资料与来源 ⌄"; };
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
        if (model.Reference != reference) { dialog?.Hide(); section.IsExpanded = false; options.Visibility = Visibility.Collapsed; details.Content = "资料与来源 ⌄"; }
        model.SetContext(reference); Visibility = MusicOutlets.Reference(model.Reference) ? Visibility.Visible : Visibility.Collapsed;
    }
    private void Changed(object? sender, PropertyChangedEventArgs args) => Render();
    private static bool Useful(MusicOutletLink value) => value.Kind != MusicOutletKind.Information;
    private void Render()
    {
        if (disposed || model is null) return;
        status.Text = model.Message; status.Visibility = Visible(model.Busy || model.NeedsAttention);
        load.IsEnabled = model.CanLoad; cancel.Visibility = Visible(model.Busy);
        entries.Children.Clear(); saved.Children.Clear(); evidence.Children.Clear();
        var current = model.Current?.Snapshot;
        if (current is not null)
        {
            if (current.Links.Any(Useful)) AddEdition(entries, current, model.Digital is null ? null : "当前发行版", false);
            AddEvidence(current, "当前发行版");
        }
        if (model.Digital is { } others)
        {
            foreach (var edition in others.Editions.OrderByDescending(x => x.Snapshot.Links.Any(l => l.Kind == MusicOutletKind.Listening)))
            {
                if (edition.Snapshot.Links.Any(Useful)) AddEdition(entries, edition.Snapshot, "其他数字版", true);
                AddEvidence(edition.Snapshot, "其他数字版");
            }
        }
        if (current is not null && entries.Children.Count == 0 && !model.Busy && !model.NeedsAttention)
            entries.Children.Add(Text(13, model.Digital is null ? "当前发行版未收录收听或购买入口。" : "本次未找到收听或购买入口。"));
        foreach (var note in new[] { model.Current?.Warning, model.Digital?.Warning }.OfType<string>().Distinct()) evidence.Children.Add(Text(12, note));
        if (model.Digital is not null) evidence.Children.Add(Text(12, "数字版入口属于同专辑组的其他发行物，曲目或母带可能不同；当前发行版保持不变。"));
        RenderSaved();
    }
    private void AddEdition(StackPanel body, MusicOutletSnapshot snapshot, string? heading, bool digital)
    {
        var platforms = new StackPanel { Spacing = 6 }; AddLinks(platforms, snapshot.Links.Where(Useful));
        if (heading is null) { body.Children.Add(platforms); return; }
        var label = string.Join(" · ", new[] { digital ? "数字版" : heading, digital ? snapshot.Date : null, digital && snapshot.TrackCount is { } n ? $"{n} 首" : null }.Where(x => !string.IsNullOrWhiteSpace(x)));
        var text = Text(13, label); text.FontWeight = FontWeights.SemiBold;
        var metadata = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center, Children = { text } };
        if (snapshot.Title != model?.Current?.Snapshot.Title) metadata.Children.Add(Text(13, snapshot.Title));
        if (snapshot.Artist != model?.Current?.Snapshot.Artist) { var performer = Text(12, snapshot.Artist); performer.Opacity = .75; metadata.Children.Add(performer); }
        var row = new Grid { ColumnSpacing = 16, RowSpacing = 6 };
        row.ColumnDefinitions.Add(new() { Width = new(216) }); row.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        row.RowDefinitions.Add(new() { Height = GridLength.Auto }); row.RowDefinitions.Add(new() { Height = GridLength.Auto });
        row.Children.Add(metadata); row.Children.Add(platforms);
        bool? wasNarrow = null;
        row.SizeChanged += (_, _) =>
        {
            var narrow = row.ActualWidth < 620; if (wasNarrow == narrow) return; wasNarrow = narrow;
            row.ColumnDefinitions[0].Width = narrow ? new(1, GridUnitType.Star) : new(216);
            row.ColumnDefinitions[1].Width = narrow ? new(0) : new(1, GridUnitType.Star);
            Grid.SetColumn(platforms, narrow ? 0 : 1); Grid.SetRow(platforms, narrow ? 1 : 0);
        };
        body.Children.Add(row);
    }
    private void AddLinks(StackPanel body, IEnumerable<MusicOutletLink> input)
    {
        foreach (var group in MusicOutlets.Merge(input, MusicOutletSort.PlatformName).OrderBy(PlatformName, StringComparer.CurrentCulture).GroupBy(x => x.Kind).OrderBy(x => x.Key))
        {
            var row = new TagFlowPanel { HorizontalSpacing = 8, VerticalSpacing = 6 };
            var kind = group.Key switch { MusicOutletKind.Listening => "收听", MusicOutletKind.Purchase => "购买", _ => "资料" };
            var label = Text(12, kind); label.Opacity = .7; row.Children.Add(label);
            foreach (var value in group)
            {
                var name = PlatformName(value);
                var content = Text(14, name + " ↗"); content.IsHitTestVisible = false;
                var link = new Button { Content = content, CornerRadius = new(5), Padding = new(12, 5, 12, 5), MinHeight = 32, MaxWidth = 260, HorizontalContentAlignment = HorizontalAlignment.Left };
                ToolTipService.SetToolTip(link, string.Join("\n", new[] { MusicOutlets.PurposeLabel(value), value.Regions.Length == 0 ? null : "地区 · " + string.Join(" / ", value.Regions), value.Url }.OfType<string>()));
                AutomationProperties.SetAutomationId(link, "MusicOutlet-" + value.Entity.Id + "-" + value.Kind + "-" + name);
                AutomationProperties.SetName(link, name + " · " + kind);
                link.Click += async (_, _) => await Open(value.Url); row.Children.Add(link);
            }
            body.Children.Add(row);
        }
    }
    private static string PlatformName(MusicOutletLink link) => new Uri(link.Url).Host switch
    {
        "www.amazon.co.jp" or "www.amazon.com" or "amazon.co.jp" or "amazon.com" => "Amazon",
        "www.discogs.com" or "discogs.com" => "Discogs", "vgmdb.net" or "www.vgmdb.net" => "VGMdb",
        "store.steampowered.com" => "Steam", _ => link.Platform
    };
    private void AddEvidence(MusicOutletSnapshot snapshot, string heading)
    {
        evidence.Children.Add(Text(13, heading + " · " + snapshot.Title));
        AddLinks(evidence, snapshot.Links.Where(x => !Useful(x)));
        evidence.Children.Add(Text(12, $"{snapshot.Artist} · {snapshot.Date}\n{snapshot.Entity.Kind} · {snapshot.Entity.Id} · {snapshot.FetchedAtUtc:yyyy-MM-dd HH:mm} UTC"));
        foreach (var value in MusicOutlets.Merge(snapshot.Links, MusicOutletSort.PlatformName).OrderBy(PlatformName, StringComparer.CurrentCulture))
        {
            foreach (var source in value.Sources) evidence.Children.Add(Text(12, PlatformName(value) + " · " + source.Source + " · " + source.Note + "\n" + source.Url));
            foreach (var relation in value.Relationships)
                evidence.Children.Add(Text(12, $"{relation.Type} · {relation.Direction} · {relation.Entity.Kind}:{relation.Entity.Id}\n{relation.OriginalUrl}"));
        }
    }
    private void RenderSaved()
    {
        if (model?.Choices?.Items.Length is not > 0) return;
        saved.Children.Add(Text(13, "已保存的个人页面"));
        foreach (var choice in model.Choices.Items)
        {
            if (!MediaUserLinkRules.Valid(choice) || choice.Target.Provider != "iTunes" || !AppleAlbumCandidate.TryAlbumUrl(choice.Note, choice.Target.Resource[6..], out var id) || id != choice.Target.Id) continue;
            var row = new TagFlowPanel { HorizontalSpacing = 10, VerticalSpacing = 6 };
            var link = new HyperlinkButton { Content = new TextBlock { Text = choice.Title + " ↗", TextWrapping = TextWrapping.Wrap, IsHitTestVisible = false }, Padding = new(0), HorizontalAlignment = HorizontalAlignment.Left };
            ToolTipService.SetToolTip(link, "你保存的独立商店关联 · " + choice.Target.Resource[6..] + " · " + choice.Note);
            link.Click += async (_, _) => await Open(choice.Note); row.Children.Add(link);
            var remove = Button("移除"); remove.Click += async (_, _) =>
            {
                var current = model; var reference = current?.Reference;
                if (current is null || dialog is not null) return;
                dialog = new() { XamlRoot = XamlRoot, RequestedTheme = ActualTheme, Title = "移除这个商店关联？", Content = "当前发行版和其他资料保持。", PrimaryButtonText = "移除", CloseButtonText = "取消", DefaultButton = ContentDialogButton.Close };
                try { if (await dialog.ShowAsync() == ContentDialogResult.Primary && ReferenceEquals(current, model) && current.Reference == reference) await current.RemoveAppleAsync(choice); }
                finally { dialog = null; }
            }; row.Children.Add(remove); saved.Children.Add(row);
        }
    }
    private async Task Open(string url)
    {
        try { if (!WatchLinks.SafeUrl(url) || !await Windows.System.Launcher.LaunchUriAsync(new Uri(url))) throw new InvalidOperationException(); }
        catch { status.Text = "暂时无法打开平台页面，请稍后重试。"; status.Visibility = Visibility.Visible; }
    }
    private static TextBlock Text(double size, string value = "") => new() { Text = value, FontSize = size, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
    private static Button Button(string value) => new() { Content = value, MinHeight = 32, CornerRadius = new(5), HorizontalAlignment = HorizontalAlignment.Left };
    private static Visibility Visible(bool value) => value ? Visibility.Visible : Visibility.Collapsed;
    internal object Evidence => new { reference = model?.Reference, expanded = section.IsExpanded, optionsExpanded = options.Visibility == Visibility.Visible,
        busy = model?.Busy, current = model?.Current, digital = model?.Digital, apple = model?.Apple, saved = model?.Choices?.Items, status = status.Text,
        statusVisible = status.Visibility == Visibility.Visible, sectionHeight = section.ActualHeight, contentRows = entries.Children.Count };
    public void Cancel() => model?.Cancel();
    public void Deactivate() { dialog?.Hide(); model?.SetContext(null); section.IsExpanded = false; Visibility = Visibility.Collapsed; }
    public void Dispose() { if (disposed) return; Deactivate(); disposed = true; if (model is not null) { model.PropertyChanged -= Changed; model.Dispose(); } }
}
