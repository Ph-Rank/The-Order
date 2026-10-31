using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using TheOrder.Application;
using TheOrder.Presentation;

namespace TheOrder.WinUI.Views;

// One collapsed, read-only section inside the host's existing ScrollViewer.
public sealed class CatalogReleasesView : StackPanel, IDisposable
{
    private sealed record FilterOption(string? Value, string Label) { public override string ToString() => Label; }
    private readonly Expander section = new SmoothExpander() { Header = "发行与版本资料 · NextMoe", HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch };
    private readonly StackPanel body = new() { Spacing = 12 }, rows = new() { Spacing = 8 };
    private readonly TextBlock message = Text("展开后按需读取发行资料。", 14), provenance = Text("", 12), count = Text("", 13);
    private readonly Button refresh = new() { Content = "刷新发行资料" }, next = new() { Content = "读取下一页" }, cancel = new() { Content = "取消读取" }, clear = new() { Content = "清除筛选" };
    private readonly ComboBox kinds = Filter("发行类型", "CatalogReleasesKind"), languages = Filter("语言", "CatalogReleasesLanguage"), platforms = Filter("平台", "CatalogReleasesPlatform");
    private readonly HashSet<string> expandedRows = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Expander> rowExpanders = new(StringComparer.Ordinal);
    private CatalogReleasesViewModel? model;
    private CatalogReleaseEdition[]? shownRows, filterRows;
    private string? shownWork;
    private bool syncing, disposed;
    public Func<string, Task>? OpenLink { get; set; }

    public CatalogReleasesView()
    {
        Spacing = 10; Visibility = Visibility.Collapsed;
        AutomationProperties.SetAutomationId(this, "CatalogReleases"); AutomationProperties.SetAutomationId(section, "CatalogReleasesExpand");
        AutomationProperties.SetAutomationId(refresh, "CatalogReleasesRefresh"); AutomationProperties.SetAutomationId(next, "CatalogReleasesNext");
        AutomationProperties.SetAutomationId(cancel, "CatalogReleasesCancel"); AutomationProperties.SetAutomationId(message, "CatalogReleasesStatus");
        AutomationProperties.SetAutomationId(clear, "CatalogReleasesClearFilters"); AutomationProperties.SetAutomationId(count, "CatalogReleasesCount");
        body.Children.Add(Text("按类型、语言或平台筛选已载入的发行。展开单条可查看该版本的引擎和来源。", 13));
        var filters = new TagFlowPanel { HorizontalSpacing = 8, VerticalSpacing = 8 };
        filters.Children.Add(kinds); filters.Children.Add(languages); filters.Children.Add(platforms); filters.Children.Add(clear); body.Children.Add(filters);
        var actions = new TagFlowPanel { HorizontalSpacing = 8, VerticalSpacing = 6 };
        actions.Children.Add(refresh); actions.Children.Add(cancel); body.Children.Add(actions); body.Children.Add(message); body.Children.Add(count);
        provenance.Opacity = .72; body.Children.Add(provenance); body.Children.Add(rows); body.Children.Add(next);
        section.Content = body; Children.Add(section);
        section.Expanding += async (_, _) => { if (!syncing && model is not null) await model.ExpandAsync(); };
        section.Collapsed += (_, _) => { if (!syncing) model?.Collapse(); };
        refresh.Click += async (_, _) => { if (model is not null) await model.RefreshAsync(); };
        next.Click += async (_, _) => { if (model is not null) await model.NextAsync(); };
        cancel.Click += (_, _) => model?.Cancel(); clear.Click += (_, _) => model?.ClearFilters();
        kinds.SelectionChanged += FilterChanged; languages.SelectionChanged += FilterChanged; platforms.SelectionChanged += FilterChanged;
        Unloaded += (_, _) => { if (!IsLoaded) Deactivate(); };
    }
    public void Configure(ICatalogReleaseReader reader, Func<long>? authGeneration = null)
    {
        if (disposed) return;
        if (model is not null) { model.PropertyChanged -= StateChanged; model.Dispose(); }
        model = new(reader, authGeneration); model.PropertyChanged += StateChanged; shownRows = filterRows = null; Render();
    }
    public void SetSource(string? workId) => model?.SetSource(workId);
    public void Deactivate() => model?.Deactivate();
    public Task ExpandAsync() => model?.ExpandAsync() ?? Task.CompletedTask;
    public void Collapse() => model?.Collapse();
    public Task RefreshAsync() => model?.RefreshAsync() ?? Task.CompletedTask;
    public Task NextAsync() => model?.NextAsync() ?? Task.CompletedTask;
    public void Cancel() => model?.Cancel();
    public void FilterForFixture(string? kind, string? language, string? platform) => model?.SetFilters(kind, language, platform);
    public bool ExpandRowForFixture(string releaseId)
    {
        if (!rowExpanders.TryGetValue(releaseId, out var row)) return false;
        row.IsExpanded = true; return true;
    }
    private void FilterChanged(object sender, SelectionChangedEventArgs args)
    {
        if (!syncing) model?.SetFilters((kinds.SelectedItem as FilterOption)?.Value,
            (languages.SelectedItem as FilterOption)?.Value, (platforms.SelectedItem as FilterOption)?.Value);
    }
    private void StateChanged(object? sender, PropertyChangedEventArgs e) { if (!disposed) Render(); }
    private void Render()
    {
        Visibility = model?.WorkId is null ? Visibility.Collapsed : Visibility.Visible;
        syncing = true; section.IsExpanded = model?.Expanded ?? false;
        if (model is null) { syncing = false; return; }
        if (shownWork != model.WorkId || model.Items.Length == 0) { expandedRows.Clear(); shownWork = model.WorkId; }
        if (!ReferenceEquals(filterRows, model.Items))
        {
            filterRows = model.Items;
            kinds.ItemsSource = new[] { new FilterOption(null, UiText.Get("所有类型")) }.Concat(model.Kinds.Select(value => new FilterOption(value, UiText.Get(CatalogReleaseFacts.KindCaption(value)) + (CatalogReleaseFacts.KindCaption(value) == "其他发行" ? " · " + value : "")))).ToArray();
            languages.ItemsSource = new[] { new FilterOption(null, UiText.Get("所有语言")) }.Concat(model.Languages.Select(value => new FilterOption(value, UiText.Get(CatalogReleaseFacts.LanguageCaption(value)) + " · " + value))).ToArray();
            platforms.ItemsSource = new[] { new FilterOption(null, UiText.Get("所有平台")) }.Concat(model.Platforms.Select(value => new FilterOption(value, UiText.Get(CatalogReleaseFacts.PlatformCaption(value))))).ToArray();
        }
        Select(kinds, model.KindFilter); Select(languages, model.LanguageFilter); Select(platforms, model.PlatformFilter); syncing = false;
        kinds.IsEnabled = languages.IsEnabled = platforms.IsEnabled = model.Items.Length > 0; clear.IsEnabled = model.HasFilters;
        message.Text = UiText.Get(model.Message.Length > 0 ? model.Message : "展开后按需读取发行资料。");
        count.Text = model.Loaded ? $"{UiText.Get("显示")} {model.VisibleItems.Length} / {model.Items.Length} {UiText.Get("条已载入发行")}" + (model.NextCursor is not null ? UiText.Get(" · 尚有后续记录") : "") : "";
        refresh.IsEnabled = model.CanLoad; next.IsEnabled = model.CanNext;
        next.Visibility = model.NextCursor is not null && model.Items.Length < CatalogReleasesViewModel.MaximumRows ? Visibility.Visible : Visibility.Collapsed;
        cancel.Visibility = model.IsBusy ? Visibility.Visible : Visibility.Collapsed;
        provenance.Text = model.FetchedAtUtc is not null ? "资料来自 NextMoe 作品目录；空缺表示来源未收录。" : "";
        if (ReferenceEquals(shownRows, model.VisibleItems)) return;
        shownRows = model.VisibleItems; rows.Children.Clear(); rowExpanders.Clear();
        if (model.Loaded && model.HasFilters && model.VisibleItems.Length == 0)
            rows.Children.Add(Text("已载入记录中没有符合条件的发行。可清除筛选；若有下一页，也可继续读取。", 14));
        foreach (var row in model.VisibleItems) AddRow(row);
    }
    private void AddRow(CatalogReleaseEdition row)
    {
        var heading = new StackPanel { Spacing = 4, IsHitTestVisible = false, HorizontalAlignment = HorizontalAlignment.Stretch };
        var title = Text(string.IsNullOrWhiteSpace(row.Title) ? "发行标题未收录" : row.Title, 15); title.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold; heading.Children.Add(title);
        var facts = new List<string> { UiText.Get(CatalogReleaseFacts.KindCaption(row.Kind)), row.Date ?? UiText.Get("日期未收录") };
        if (row.Languages.Length > 0) facts.Add(string.Join(" / ", row.Languages.Select(value => UiText.Get(CatalogReleaseFacts.LanguageCaption(value)))));
        if (row.Platforms.Length > 0) facts.Add(string.Join(" / ", row.Platforms.Select(CatalogReleaseFacts.PlatformCaption)));
        var subtitle = Text(string.Join(" · ", facts), 13); subtitle.Opacity = .75; heading.Children.Add(subtitle);
        var card = new StackPanel { Spacing = 8 };
        if (!string.IsNullOrWhiteSpace(row.OriginalTitle) && row.OriginalTitle != row.Title) card.Children.Add(Text(row.OriginalTitle, 13));
        card.Children.Add(Text(UiText.Get("版本类型 · ") + UiText.Get(CatalogReleaseFacts.KindCaption(row.Kind)) + " · " + row.Kind, 13));
        card.Children.Add(Text(UiText.Get("语言 · ") + (row.Languages.Length > 0 ? string.Join(" / ", row.Languages) : UiText.Get("未收录")), 13));
        card.Children.Add(Text(UiText.Get("平台 · ") + (row.Platforms.Length > 0 ? string.Join(" / ", row.Platforms) : UiText.Get("未收录")), 13));
        card.Children.Add(Text(row.Engine is { } engine ? $"{UiText.Get("引擎")} · {engine.Title} · NextMoe {engine.Id}" : UiText.Get("该发行的引擎未收录"), 13));
        var refs = new TagFlowPanel { HorizontalSpacing = 8, VerticalSpacing = 4 };
        foreach (var reference in row.References)
        {
            var label = CatalogContentView.SiteName(reference.Source) + " · " + reference.Id;
            if (SafeSource(reference.Url))
            {
                var link = new HyperlinkButton { Content = label, Padding = new Thickness(0, 2, 0, 2), HorizontalAlignment = HorizontalAlignment.Left };
                AutomationProperties.SetName(link, label + UiText.Get("，原始发行资料"));
                link.Click += async (_, _) =>
                {
                    var currentModel = model; var version = currentModel?.Version; var id = currentModel?.WorkId;
                    try { if (OpenLink is not null) await OpenLink(reference.Url!); }
                    catch { if (!disposed && ReferenceEquals(model, currentModel) && model?.Version == version && model?.WorkId == id) message.Text = UiText.Get("原始发行资料暂时无法打开，可稍后重试。"); }
                };
                refs.Children.Add(link);
            }
            else refs.Children.Add(Text(label, 12));
        }
        if (refs.Children.Count > 0) card.Children.Add(refs); else card.Children.Add(Text("原始来源编号未收录", 12));
        var identity = Text($"{UiText.Get("目录发行")} {row.Id} · {UiText.Get("所属作品")} {row.WorkId}", 12); identity.Opacity = .72; card.Children.Add(identity);
        var entry = new SmoothExpander { Header = heading, Content = card, IsExpanded = expandedRows.Contains(row.Id), HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        entry.Expanding += (_, _) => expandedRows.Add(row.Id); entry.Collapsed += (_, _) => expandedRows.Remove(row.Id);
        AutomationProperties.SetAutomationId(entry, "CatalogRelease-" + row.Id); AutomationProperties.SetName(entry, (row.Title ?? UiText.Get("发行标题未收录")) + "，" + string.Join("，", facts));
        rowExpanders[row.Id] = entry; rows.Children.Add(entry);
    }
    private static void Select(ComboBox box, string? value) => box.SelectedItem = box.Items.Cast<FilterOption>().FirstOrDefault(option => option.Value == value);
    private static ComboBox Filter(string name, string id)
    {
        var box = new ComboBox { MinWidth = 130, MaxWidth = 220, HorizontalAlignment = HorizontalAlignment.Left };
        AutomationProperties.SetName(box, UiText.Get(name)); AutomationProperties.SetAutomationId(box, id); return box;
    }
    private static bool SafeSource(string? url) => Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == "https"
        && uri.UserInfo.Length == 0 && uri.IsDefaultPort && uri.Host is "vndb.org" or "bgm.tv" or "store.steampowered.com"
            or "erogamescape.dyndns.org" or "howlongtobeat.com" or "www.dlsite.com" or "www.getchu.com";
    private static TextBlock Text(string value, double size) => new() { Text = UiText.Get(value), FontSize = size,
        TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
    internal object Evidence => new { workId = model?.WorkId, expanded = model?.Expanded, state = model?.State.ToString(),
        busy = model?.IsBusy, count = model?.Items.Length, visibleCount = model?.VisibleItems.Length, kind = model?.KindFilter, language = model?.LanguageFilter, platform = model?.PlatformFilter,
        visibleIds = model?.VisibleItems.Select(row => row.Id).ToArray(), expandedIds = expandedRows.ToArray(), next = model?.NextCursor, message = model?.Message,
        items = model?.Items.Select(row => new { row.Id, row.WorkId, row.Title, row.Kind, row.Date, row.Languages, row.Platforms, row.Engine,
            references = row.References.Select(reference => new { reference.Source, reference.Id }).ToArray() }).ToArray() };
    public void Dispose() { if (disposed) return; disposed = true; if (model is not null) { model.PropertyChanged -= StateChanged; model.Dispose(); } }
}
