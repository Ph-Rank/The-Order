using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using TheOrder.Application;
using TheOrder.Presentation;

namespace TheOrder.WinUI.Views;

// Composition with an Expander avoids inheriting its theme target type. The
// containing detail page owns scrolling; episode rows do not create ScrollViewers.
public sealed class BangumiEpisodesView : Grid, IDisposable
{
    private readonly Expander section = new SmoothExpander() { Header = "分集目录 · Bangumi", HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch };
    private readonly StackPanel body = new() { Spacing = 12 }, rows = new() { Spacing = 8 };
    private readonly TextBlock notice = Text(""), provenance = Text("保留 Bangumi 原始章节类型与同类排序；目录项数可能与作品集数不同。"), filterNotice = Text("", 12);
    private readonly TagFlowPanel filters = new() { HorizontalSpacing = 6, VerticalSpacing = 6 };
    private readonly Button refresh = Button("刷新目录"), cancel = Button("取消读取"), retry = Button("重试");
    private readonly ScrollPageFooter more = new();
    private readonly ExternalLinkButton source = new() { Content = "在 Bangumi 查看分集目录" };
    private BangumiEpisodesViewModel? model;
    private BangumiEpisode[]? shown;
    private int? shownType;
    private readonly Dictionary<int, Action> expandEpisode = [];
    private readonly HashSet<int> expandedEpisodes = [];
    private readonly Dictionary<int, (BangumiEpisode Episode, BangumiEpisodeRowView View, Border Container)> renderedRows = [];
    private readonly Dictionary<int, TextBlock> groupHeadings = [];
    private readonly Dictionary<int, ToggleButton> filterButtons = [];
    private readonly List<HyperlinkButton> episodeLinks = [];
    private readonly List<BangumiEpisodeRowView> episodeRows = [];
    private Task expanding = Task.CompletedTask;
    private bool disposed;

    public BangumiEpisodesView()
    {
        AutomationProperties.SetAutomationId(this, "BangumiEpisodes");
        AutomationProperties.SetAutomationId(section, "BangumiEpisodesExpander");
        var actions = new TagFlowPanel { HorizontalSpacing = 8, VerticalSpacing = 6, Children = { refresh, cancel, retry } };
        body.Children.Add(provenance); body.Children.Add(notice); body.Children.Add(actions); body.Children.Add(filters); body.Children.Add(filterNotice); body.Children.Add(rows); body.Children.Add(more); body.Children.Add(source);
        section.Content = body; Children.Add(section); Visibility = Visibility.Collapsed;
        section.Expanding += async (_, _) => { if (model is not null) { expanding = model.ExpandAsync(); await expanding; } };
        section.Collapsed += (_, _) => model?.Cancel();
        refresh.Click += async (_, _) => { if (model is not null) await model.RefreshAsync(); };
        more.LoadNext = () => model?.MoreAsync() ?? Task.CompletedTask;
        retry.Click += async (_, _) => { if (model is not null) await model.RefreshAsync(); };
        cancel.Click += (_, _) => model?.Cancel();
        source.Click += async (_, _) => { if (model?.SubjectId is { } id) await OpenSourceAsync(new Uri("https://bgm.tv/subject/" + id + "/ep")); };
    }
    public void Configure(IBangumiEpisodesReader reader)
    {
        if (disposed) return;
        if (model is not null) { model.PropertyChanged -= ModelChanged; model.Dispose(); }
        model = new(reader); model.PropertyChanged += ModelChanged; shown = null; Render();
    }
    public void SetSource(string? verifiedBangumiId)
    {
        if (disposed) return;
        section.IsExpanded = false; shown = null; expandedEpisodes.Clear();
        model?.SetSource(verifiedBangumiId); Render();
    }
    private void ModelChanged(object? sender, PropertyChangedEventArgs e) => Render();
    private void Render()
    {
        if (disposed) return;
        Visibility = model?.SubjectId is not null ? Visibility.Visible : Visibility.Collapsed;
        section.Header = model?.Result is not null ? $"分集目录 · Bangumi · {model.Episodes.Length} / {model.Total} 项" : "分集目录 · Bangumi";
        notice.Text = model?.Message ?? ""; notice.Visibility = notice.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        refresh.IsEnabled = retry.IsEnabled = model?.CanLoad == true;
        cancel.Visibility = model?.IsBusy == true ? Visibility.Visible : Visibility.Collapsed;
        retry.Visibility = model?.State is BangumiEpisodesState.Failed or BangumiEpisodesState.Cancelled ? Visibility.Visible : Visibility.Collapsed;
        more.Update(model is { HasUnloadedEpisodes: true, AtDisplayLimit: false }
            ? (model.SubjectId, model.NextOffset) : null, model?.IsBusy == true);
        source.IsEnabled = model?.SubjectId is not null;
        var displayed = model?.VisibleEpisodes ?? [];
        filterNotice.Text = model?.HasUnloadedEpisodes == true
            ? $"已载入 {model.Episodes.Length} / {model.Total} 项；筛选仅作用于已载入章节。"
            : model?.Result is not null ? $"显示 {displayed.Length} / {model.Episodes.Length} 项" : "";
        filterNotice.Visibility = filterNotice.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (ReferenceEquals(shown, model?.Episodes) && shownType == model?.SelectedType) return;
        var filterChanged = shownType != model?.SelectedType;
        shown = model?.Episodes; shownType = model?.SelectedType;
        var ids = (shown ?? []).Select(x => x.Id).ToHashSet();
        expandedEpisodes.IntersectWith(ids);
        foreach (var stale in renderedRows.Keys.Where(id => !ids.Contains(id)).ToArray()) renderedRows.Remove(stale);
        expandEpisode.Clear(); episodeLinks.Clear(); episodeRows.Clear();
        var chips = new List<UIElement>();
        var types = model?.TypeFilters ?? [];
        foreach (var filter in types)
        {
            if (!filterButtons.TryGetValue(filter.Type ?? -1, out var chip))
            {
                chip = new ToggleButton { CornerRadius = new(6), Padding = new(10, 5, 10, 5) };
                AutomationProperties.SetAutomationId(chip, "BangumiEpisodeFilter-" + (filter.Type?.ToString() ?? "all"));
                chip.Click += (_, _) => { if (model?.SelectedType == filter.Type) chip.IsChecked = true; else model?.SetType(filter.Type); };
                filterButtons.Add(filter.Type ?? -1, chip);
            }
            chip.Content = $"{filter.Caption} {filter.Count}"; chip.IsChecked = filter.Type == shownType;
            AutomationProperties.SetName(chip, filter.Caption + "，已载入 " + filter.Count + " 项");
            chips.Add(chip);
        }
        Reconcile(filters.Children, chips);
        var elements = new List<UIElement>();
        if (displayed.Length == 0 && model?.Result is not null && model.Episodes.Length > 0)
            elements.Add(Text(model.HasUnloadedEpisodes ? "已载入的章节中没有此类型。" : "当前目录没有此类型章节。"));
        int? group = null;
        foreach (var episode in displayed)
        {
            if (shownType is null && types.Length > 2 && group != episode.Type)
            {
                if (!groupHeadings.TryGetValue(episode.Type, out var header))
                {
                    header = Text(BangumiEpisodeDisplay.TypeName(episode.Type), 14);
                    header.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold; header.Margin = new(0, 6, 0, 2);
                    groupHeadings.Add(episode.Type, header);
                }
                elements.Add(header); group = episode.Type;
            }
            if (!renderedRows.TryGetValue(episode.Id, out var saved) || saved.Episode != episode)
            {
                var row = new BangumiEpisodeRowView(episode, OpenSourceAsync, expanded =>
                {
                    if (expanded) expandedEpisodes.Add(episode.Id); else expandedEpisodes.Remove(episode.Id);
                });
                if (expandedEpisodes.Contains(episode.Id)) row.Expand();
                var frame = new Border { Child = row, Padding = new(14, 12, 14, 12), CornerRadius = new(6), BorderThickness = new(1),
                    BorderBrush = (Microsoft.UI.Xaml.Media.Brush)Microsoft.UI.Xaml.Application.Current.Resources["CardStrokeColorDefaultBrush"] };
                saved = (episode, row, frame); renderedRows[episode.Id] = saved;
                if (!filterChanged) InteractionMotion.RevealContent(frame, .72f);
            }
            if (saved.View.CanExpand) expandEpisode[episode.Id] = saved.View.Expand;
            episodeRows.Add(saved.View); episodeLinks.Add(saved.View.SourceLink); elements.Add(saved.Container);
        }
        Reconcile(rows.Children, elements);
        if (filterChanged && displayed.Length > 0) InteractionMotion.RevealContent(rows, .74f);
    }
    private static void Reconcile(UIElementCollection current, List<UIElement> target)
    {
        var retained = target.ToHashSet();
        for (var i = current.Count - 1; i >= 0; i--) if (!retained.Contains(current[i])) current.RemoveAt(i);
        for (var i = 0; i < target.Count; i++)
        {
            var element = target[i];
            if (i < current.Count && ReferenceEquals(current[i], element)) continue;
            if (current.Contains(element)) current.Remove(element);
            current.Insert(i, element);
        }
    }
    private async Task OpenSourceAsync(Uri uri)
    {
        var current = model; var id = current?.SubjectId; var version = current?.Version;
        if (current is null || id is null || disposed) return;
        try { if (!await Windows.System.Launcher.LaunchUriAsync(uri)) throw new InvalidOperationException(); }
        catch
        {
            if (!disposed && ReferenceEquals(model, current) && model.SubjectId == id && model.Version == version)
            { notice.Text = "Bangumi 暂时无法打开，请稍后重试。"; notice.Visibility = Visibility.Visible; }
        }
    }
    private static TextBlock Text(string value, double size = 13) => new() { Text = value, FontSize = size, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
    private static Button Button(string value) => new() { Content = value, Padding = new(12, 6, 12, 6), HorizontalAlignment = HorizontalAlignment.Left };
    public async Task ExpandAsync()
    {
        var current = model; var id = current?.SubjectId;
        if (current is null || id is null) return;
        // Reveal loading/cancel UI immediately, rather than making a jump action
        // appear unresponsive until the remote directory finishes loading.
        section.IsExpanded = true;
        await expanding;
    }
    internal Task VisualExpandAsync() => ExpandAsync();
    public bool FilterForFixture(int? type)
    {
        if (disposed || model is null || !BangumiEpisodeDisplay.ValidType(type)) return false;
        model.SetType(type); return true;
    }
    public bool ExpandEpisodeForFixture(int episodeId)
    {
        if (disposed || !expandEpisode.TryGetValue(episodeId, out var expand)) return false;
        expand(); return true;
    }
    public object Inspect() => new { SubjectId = model?.SubjectId, Expanded = section.IsExpanded, State = model?.State.ToString(), Count = model?.Episodes.Length ?? 0, Total = model?.Total ?? 0, Message = model?.Message, CanMore = model?.CanMore ?? false,
        SelectedType = model?.SelectedType, DisplayedCount = model?.VisibleEpisodes.Length ?? 0, HasUnloaded = model?.HasUnloadedEpisodes ?? false,
        Types = model?.TypeFilters, ExpandedEpisodes = expandedEpisodes.Order().ToArray(), VisibleEpisodes = model?.VisibleEpisodes.Select(x => new { x.Id, x.Type, x.Sort }).ToArray(),
        Links = episodeLinks.Select(InteractiveCardEvidence.Hit).ToArray(), Rows = episodeRows.Select(x => x.Evidence).ToArray() };
    public void Dispose() { if (disposed) return; disposed = true; if (model is not null) { model.PropertyChanged -= ModelChanged; model.Dispose(); } rows.Children.Clear(); filters.Children.Clear(); expandEpisode.Clear(); expandedEpisodes.Clear(); renderedRows.Clear(); groupHeadings.Clear(); filterButtons.Clear(); episodeLinks.Clear(); episodeRows.Clear(); shown = null; }
}

// A shared page scroller, a readable title column, and separate source/detail actions.
// Provider prose stays verbatim: a staff list in the summary is not inferred as structured credits.
internal sealed class BangumiEpisodeRowView : Grid
{
    private readonly StackPanel prose = new() { Spacing = 12, MaxWidth = 760, HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly HyperlinkButton toggle = Action("展开");
    private readonly TextBlock title;
    private readonly DisclosureBody detail = new();
    private readonly BangumiEpisode episode;
    private readonly Action<bool> changed;
    private bool compact;
    internal ExternalLinkButton SourceLink { get; } = new() { Content = "原站" };
    internal bool CanExpand { get; }

    internal BangumiEpisodeRowView(BangumiEpisode episode, Func<Uri, Task> openSource, Action<bool> changed)
    {
        this.episode = episode; this.changed = changed; ColumnSpacing = 14; RowSpacing = 4;
        ColumnDefinitions.Add(new() { Width = new(32) });
        ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        for (var i = 0; i < 4; i++) RowDefinitions.Add(new() { Height = GridLength.Auto });
        var number = Text(BangumiEpisodeDisplay.Number(episode), 15); number.Opacity = .65; number.Margin = new(0, 3, 0, 0);
        AutomationProperties.SetName(number, BangumiEpisodeDisplay.TypeName(episode.Type) + " " + number.Text);
        Children.Add(number);
        title = Text(episode.Title.Length > 0 ? episode.Title : "暂无标题", 15);
        title.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold; title.LineHeight = 23;
        SetColumn(title, 1); Children.Add(title);
        var facts = Text(string.Join("   ·   ", new[] { episode.AirDate, episode.Duration.Length > 0 ? episode.Duration : null }.Where(x => !string.IsNullOrEmpty(x))), 12);
        facts.Opacity = .72; facts.Visibility = facts.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        SetColumn(facts, 1); SetRow(facts, 1); Children.Add(facts);
        AutomationProperties.SetAutomationId(SourceLink, "BangumiEpisodeSource-" + episode.Id);
        AutomationProperties.SetName(SourceLink, title.Text + "，在 Bangumi 查看本集");
        ToolTipService.SetToolTip(SourceLink, "在 Bangumi 查看本集");
        SourceLink.Click += async (_, _) => await openSource(BangumiEpisodeDisplay.EpisodeUri(episode.Id));
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Top, Children = { toggle, SourceLink } };
        SetColumn(actions, 2); SetRowSpan(actions, 2); Children.Add(actions);
        if (episode.OriginalTitle.Length > 0 && episode.OriginalTitle != episode.Title)
            prose.Children.Add(Group("原标题", Text(episode.OriginalTitle, 14)));
        if (episode.Summary.Length > 0)
        {
            var summary = Text(episode.Summary, 14); summary.LineHeight = 24;
            prose.Children.Add(Group("本集说明 · 原文", summary));
        }
        CanExpand = prose.Children.Count > 0;
        toggle.Visibility = CanExpand ? Visibility.Visible : Visibility.Collapsed;
        AutomationProperties.SetAutomationId(toggle, "BangumiEpisodeDescription-" + episode.Id);
        AutomationProperties.SetName(toggle, title.Text + " · 展开或收起原文与说明");
        detail.Children.Add(new Border { Child = prose, HorizontalAlignment = HorizontalAlignment.Left, MaxWidth = 760,
            BorderThickness = new(0, 1, 0, 0), BorderBrush = (Microsoft.UI.Xaml.Media.Brush)Microsoft.UI.Xaml.Application.Current.Resources["DividerBrush"],
            Padding = new(0, 12, 0, 2), Margin = new(0, 8, 0, 0) });
        SetColumn(detail, 1); SetColumnSpan(detail, 2); SetRow(detail, 3); Children.Add(detail);
        toggle.Click += (_, _) => SetExpanded(!detail.IsExpanded);
        void Reflow()
        {
            compact = ActualWidth < 520;
            SetColumn(actions, compact ? 1 : 2); SetRow(actions, compact ? 2 : 0); SetRowSpan(actions, compact ? 1 : 2);
            SetColumnSpan(title, compact ? 2 : 1); SetColumnSpan(facts, compact ? 2 : 1);
        }
        SizeChanged += (_, _) => Reflow(); Reflow();
    }
    private void SetExpanded(bool value)
    {
        if (!CanExpand) return;
        detail.IsExpanded = value;
        ((TextBlock)toggle.Content).Text = value ? "收起" : "展开"; changed(value);
    }
    internal void Expand() => SetExpanded(true);
    private static StackPanel Group(string caption, TextBlock content)
    {
        var label = Text(caption, 12); label.Opacity = .65;
        return new() { Spacing = 5, Children = { label, content } };
    }
    private static HyperlinkButton Action(string value) => new() { Content = new TextBlock { Text = value, FontSize = 13, IsHitTestVisible = false },
        MinHeight = 32, Padding = new(0), HorizontalAlignment = HorizontalAlignment.Left };
    private static TextBlock Text(string value, double size) => new() { Text = value, FontSize = size, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
    internal object Evidence => new { episode.Id, compact, expanded = detail.IsExpanded, width = ActualWidth, titleWidth = title.ActualWidth,
        titleSize = title.FontSize, proseWidth = prose.ActualWidth, source = InteractiveCardEvidence.Hit(SourceLink),
        toggle = CanExpand ? InteractiveCardEvidence.Hit(toggle) : null };
}
