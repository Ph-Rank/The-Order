using System.Collections.Immutable;
using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using TheOrder.Application;
using TheOrder.Domain;
using TheOrder.Presentation;
namespace TheOrder.WinUI.Views;

internal sealed record RelatedWorksBrowseState(bool Complete, BangumiRelationKind? Kind, int Limit, string? FocusKey, bool Sources, bool Evidence);

// Unified presentation, independently namespaced provider identities and evidence.
public sealed class BangumiRelatedView : StackPanel, IDisposable
{
    private const int PageSize = 24;
    private readonly TextBlock heading = new() { FontSize = 22, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Text = "关联作品" };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, FontSize = 13 };
    private readonly ProgressRing progress = new() { Width = 18, Height = 18 };
    private readonly Button reload = new() { Content = "刷新关联", MinHeight = 32 };
    private readonly Button cancel = new() { Content = "取消读取", MinHeight = 32 };
    private readonly Button all = new() { Content = "查看全部关联作品", HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = new(6) };
    private readonly Button more = new() { Content = "显示更多", HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = new(6) };
    private readonly Button overview = new() { Content = "概览", MinHeight = 32, CornerRadius = new(6) };
    private readonly TagFlowPanel actions = new() { HorizontalSpacing = 8, VerticalSpacing = 6 };
    private readonly TagFlowPanel filters = new() { HorizontalSpacing = 8, VerticalSpacing = 6 };
    private readonly StackPanel groups = new() { Spacing = 16 };
    private readonly StackPanel sourceBody = new() { Spacing = 8 };
    private readonly Expander sources = new SmoothExpander() { Header = "关联来源与关系说明", HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch };
    private readonly Expander rawSources = new SmoothExpander() { Header = "详细证据", HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch };
    private readonly Dictionary<BangumiRelationKind, Button> choices = [];
    private readonly List<(RelatedWorkCard Item, Button Button, CoverPresenter Picture)> displayed = [];
    private static readonly BangumiRelationKind[] Kinds = [BangumiRelationKind.Anime, BangumiRelationKind.Music, BangumiRelationKind.Book, BangumiRelationKind.Game];
    private BangumiRelatedViewModel? model;
    private IImagePipeline? images;
    private IRemoteCoverRegistry? registry;
    private Func<string, Task>? openLink;
    private ImmutableArray<CatalogEntityRef> catalogRows = [];
    private ImmutableArray<RelatedWorkCard> rows = [];
    private ImmutableArray<RelatedWorkCard> renderedRows;
    private (bool Complete, BangumiRelationKind? Kind, int Limit, string? Work, string? Revision, DateTimeOffset? Fetched)? renderedKey;
    private WorkMetadata? catalogWork;
    private BangumiRelationsSnapshot? shown;
    private BangumiRelationIdentity? shownIdentity;
    private BangumiRelationKind? selected;
    private bool complete, disposed;
    private int limit = PageSize;
    private long generation;
    private string? navigationFocusKey;
    private IRelatedSemanticReader? semanticReader;
    private ImmutableArray<RelatedSemanticEvidence> semanticEvidence = [];
    private CancellationTokenSource? semanticStop;
    private BangumiRelationsSnapshot? semanticSnapshot;
    private string? semanticWarning;
    private Func<BangumiRelationIdentity, bool>? currentIdentity;
    private IRelatedGameIdentityReader? gameIdentityReader;
    private ImmutableArray<RelatedGameIdentity> gameIdentities = [];
    private CancellationTokenSource? gameIdentityStop;
    private WorkMetadata? gameIdentitySnapshot;
    private string? gameIdentityWarning;
    public Func<RelatedWorkCard, Task>? OpenMedia { get; set; }
    public Func<RelatedWorkCard, Task>? OpenCatalog { get; set; }
    internal Action? RefreshContext { get; set; }
    internal Action? PresentationChanged { get; set; }
    internal bool IsSettled => model?.IsBusy != true && semanticStop is null && gameIdentityStop is null;
    internal RelatedWorksBrowseState CaptureBrowsing() => new(complete, selected, limit, navigationFocusKey, sources.IsExpanded, rawSources.IsExpanded);
    internal void RestoreBrowsing(RelatedWorksBrowseState state)
    {
        complete = state.Complete; selected = state.Kind; limit = Math.Clamp(state.Limit, PageSize, 768);
        navigationFocusKey = state.FocusKey; sources.IsExpanded = state.Sources; rawSources.IsExpanded = state.Evidence; Render();
    }
    public BangumiRelatedView()
    {
        SecondaryDisclosure.Apply(sources); SecondaryDisclosure.Apply(rawSources);
        Spacing = 12;
        AutomationProperties.SetAutomationId(this, "RelatedWorks");
        AutomationProperties.SetAutomationId(heading, "BangumiRelatedExpander");
        AutomationProperties.SetAutomationId(status, "BangumiRelatedStatus");
        AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);
        AutomationProperties.SetAutomationId(reload, "BangumiRelatedReload");
        AutomationProperties.SetAutomationId(cancel, "BangumiRelatedCancel");
        AutomationProperties.SetAutomationId(all, "RelatedWorksAll");
        AutomationProperties.SetAutomationId(more, "BangumiRelatedMore");
        actions.Children.Add(progress); actions.Children.Add(status); actions.Children.Add(cancel);
        overview.Click += (_, _) => { complete = false; selected = null; limit = PageSize; Render(); };
        filters.Children.Add(overview);
        foreach (var kind in Kinds)
        {
            var choice = new Button { Content = KindName(kind), MinHeight = 32, CornerRadius = new(6) };
            AutomationProperties.SetAutomationId(choice, "BangumiRelated" + kind);
            choice.Click += (_, _) => Select(kind); choices[kind] = choice; filters.Children.Add(choice);
        }
        sources.Content = sourceBody;
        AutomationProperties.SetAutomationId(sources, "RelatedWorksSources");
        AutomationProperties.SetAutomationId(rawSources, "RelatedWorksRawEvidence");
        rawSources.Expanding += (_, _) => rawSources.Content = RawEvidence();
        Children.Add(heading); Children.Add(filters); Children.Add(actions); Children.Add(groups);
        Children.Add(more); Children.Add(all); Children.Add(sources);
        reload.Click += async (_, _) => { StopGameIdentities(); gameIdentitySnapshot = null; gameIdentityWarning = null; RefreshContext?.Invoke(); if (model is not null) await model.RefreshAsync(); };
        cancel.Click += (_, _) => model?.Cancel();
        all.Click += (_, _) => { complete = true; selected = null; limit = PageSize; Render(); FocusFirst(); };
        more.Click += (_, _) =>
        {
            var previousKeys = displayed.Select(x => x.Item.Key).ToHashSet(StringComparer.Ordinal);
            limit += PageSize; Render();
            if (displayed.FirstOrDefault(x => !previousKeys.Contains(x.Item.Key)).Item is { } firstNew) FocusKey(firstNew.Key);
        };
        Unloaded += (_, _) => { if (!IsLoaded) Deactivate(); };
    }
    private static Visibility V(bool value) => value ? Visibility.Visible : Visibility.Collapsed;
    private static string KindName(BangumiRelationKind kind) => kind switch
    { BangumiRelationKind.Anime => "动画", BangumiRelationKind.Music => "音乐", BangumiRelationKind.Book => "书籍", _ => "游戏" };
    public void Configure(IBangumiRelationsReader reader, IImagePipeline pipeline, IRemoteCoverRegistry? coverRegistry,
        Func<string, Task> open, Func<BangumiRelationIdentity, bool> isCurrent)
    {
        if (model is not null) { model.PropertyChanged -= OnState; model.Dispose(); }
        images = pipeline; registry = coverRegistry; openLink = open; currentIdentity = isCurrent;
        model = new(reader, isCurrent); model.PropertyChanged += OnState; Synchronize();
    }
    public void ConfigureSemantics(IRelatedSemanticReader reader) => semanticReader = reader;
    public void ConfigureGameIdentities(IRelatedGameIdentityReader? reader) => gameIdentityReader = reader;
    public void SetCatalog(WorkMetadata? work)
    {
        if (ReferenceEquals(catalogWork, work)) return;
        StopGameIdentities(); gameIdentitySnapshot = null; gameIdentities = []; gameIdentityWarning = null;
        if (catalogWork?.Id != work?.Id) { complete = false; selected = null; limit = PageSize; sources.IsExpanded = rawSources.IsExpanded = false; }
        catalogWork = work; catalogRows = work?.Presentation?.RelatedWorks ?? [];
        Reproject(); Render(); StartGameIdentities();
    }
    public void SetContext(BangumiRelationIdentity? identity, string? reason)
    {
        model?.SetContext(identity, reason);
        if (model is { IsExpanded: false }) _ = model.ExpandAsync();
    }
    private void StopSemantics() { semanticStop?.Cancel(); semanticStop = null; }
    private void StopGameIdentities() { gameIdentityStop?.Cancel(); gameIdentityStop = null; }
    public void CancelPending() { model?.Cancel(); StopSemantics(); StopGameIdentities(); }
    public void SuspendNavigation() { model?.Cancel(); StopSemantics(); StopGameIdentities(); }
    public void ResumeNavigation()
    {
        Synchronize();
        if (model is { IsExpanded: true, State: BangumiRelatedState.Cancelled }) _ = model.ExpandAsync();
    }
    public void Deactivate() { generation++; StopSemantics(); StopGameIdentities(); model?.Deactivate(); }
    private void OnState(object? sender, PropertyChangedEventArgs args) => Synchronize();
    private void Synchronize()
    {
        if (disposed || model is null) return;
        var identityChanged = shownIdentity != model.Identity;
        if (identityChanged)
        { generation++; StopSemantics(); StopGameIdentities(); semanticEvidence = []; semanticSnapshot = null; semanticWarning = null; shownIdentity = model.Identity; complete = false; selected = null; limit = PageSize; }
        var changed = identityChanged || !ReferenceEquals(shown, model.Result?.Snapshot);
        shown = model.Result?.Snapshot;
        if (changed) { StopSemantics(); semanticSnapshot = null; semanticEvidence = []; Reproject(); }
        progress.IsActive = model.IsBusy; progress.Visibility = V(model.IsBusy);
        cancel.Visibility = V(model.CanCancel);
        status.Text = model.IsBusy || model.State is BangumiRelatedState.Failed or BangumiRelatedState.Cancelled or BangumiRelatedState.NoIdentity
            ? model.Message : model.Result?.Warning ?? (model.State == BangumiRelatedState.Empty && catalogRows.IsEmpty ? "暂无公开关联作品。" : "");
        status.Visibility = V(status.Text.Length > 0); actions.Visibility = V(model.IsBusy || status.Text.Length > 0);
        reload.IsEnabled = model.CanLoad; Render(); StartGameIdentities();
        if (model.IsExpanded && shown is not null && semanticStop is null && !ReferenceEquals(semanticSnapshot, shown)) _ = LoadSemanticsAsync(shown);
    }
    private async Task LoadSemanticsAsync(BangumiRelationsSnapshot snapshot)
    {
        if (semanticReader is null || model?.Identity is not { } identity || !(currentIdentity?.Invoke(identity) ?? false)) return;
        using var request = new CancellationTokenSource(); semanticStop = request; var version = generation;
        bool Accept() => !disposed && !request.IsCancellationRequested && version == generation && model?.Identity == identity && ReferenceEquals(shown, snapshot) && (currentIdentity?.Invoke(identity) ?? false);
        try
        {
            var evidence = await semanticReader.LoadAsync(identity.SubjectId, snapshot.Items, request.Token);
            if (!Accept()) return;
            semanticEvidence = evidence; semanticSnapshot = snapshot; semanticWarning = null;
            Reproject(); Render();
        }
        catch (OperationCanceledException) { }
        catch
        {
            if (Accept()) { semanticSnapshot = snapshot; semanticWarning = "部分关系尚未取得补充证据，按原站关系保守展示。"; RenderSources(); }
        }
        finally { if (ReferenceEquals(semanticStop, request)) { semanticStop = null; PresentationChanged?.Invoke(); } }
    }
    private void StartGameIdentities()
    {
        if (gameIdentityReader is null || gameIdentityStop is not null || catalogWork is not { } snapshot ||
            ReferenceEquals(gameIdentitySnapshot, snapshot) || model?.Identity is not { } identity ||
            !(currentIdentity?.Invoke(identity) ?? false)) return;
        var games = catalogRows.Where(CatalogRelatedWorks.Eligible).DistinctBy(row => row.Id).Take(64).ToImmutableArray();
        if (games.IsEmpty) { gameIdentitySnapshot = snapshot; return; }
        _ = LoadGameIdentitiesAsync(snapshot, games, identity);
    }
    private async Task LoadGameIdentitiesAsync(WorkMetadata snapshot, ImmutableArray<CatalogEntityRef> games, BangumiRelationIdentity identity)
    {
        using var request = new CancellationTokenSource(); gameIdentityStop = request; var version = generation;
        bool Accept() => !disposed && !request.IsCancellationRequested && version == generation &&
            ReferenceEquals(catalogWork, snapshot) && model?.Identity == identity && (currentIdentity?.Invoke(identity) ?? false);
        try
        {
            var result = await gameIdentityReader!.LoadRelatedIdentitiesAsync(games, request.Token);
            if (!Accept()) return;
            gameIdentities = result; gameIdentitySnapshot = snapshot; gameIdentityWarning = null;
            Reproject(); Render();
        }
        catch (OperationCanceledException) { }
        catch
        {
            if (Accept()) { gameIdentitySnapshot = snapshot; gameIdentityWarning = "部分作品尚未核验跨来源编号，暂时保留各来源条目。"; RenderSources(); }
        }
        finally { if (ReferenceEquals(gameIdentityStop, request)) { gameIdentityStop = null; PresentationChanged?.Invoke(); } }
    }
    private void Reproject() => rows = RelatedWorksProjection.Build(catalogRows, model?.Items ?? [], semanticEvidence, gameIdentities);
    private void Select(BangumiRelationKind kind) { complete = true; selected = kind; limit = PageSize; Render(); }
    private void Render()
    {
        if (disposed) return;
        var key = (complete, selected, limit, catalogWork?.Id, catalogWork?.FetchedAt, shown?.FetchedAtUtc);
        if (renderedKey == key && !renderedRows.IsDefault && renderedRows.SequenceEqual(rows)
            && (rows.IsEmpty || displayed.Count > 0)) { if (sources.IsExpanded) RenderSources(); return; }
        renderedKey = key; renderedRows = rows;
        var focus = XamlRoot is null ? null : Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(XamlRoot);
        var focusedKey = displayed.FirstOrDefault(x => ReferenceEquals(x.Button, focus)).Item?.Key;
        groups.Children.Clear(); displayed.Clear();
        heading.Text = complete ? selected is { } selectedKind ? $"关联{KindName(selectedKind)} · {rows.Count(x => x.Kind == selectedKind)}" : $"全部关联作品 · {rows.Length}" : $"关联作品 · {rows.Length}";
        overview.Style = (Style)Microsoft.UI.Xaml.Application.Current.Resources[!complete ? "AccentButtonStyle" : "DefaultButtonStyle"];
        filters.Visibility = V(rows.Length > 0);
        foreach (var kind in Kinds)
        {
            var count = rows.Count(x => x.Kind == kind); choices[kind].Content = $"{KindName(kind)} {count}"; choices[kind].IsEnabled = count > 0;
            choices[kind].Style = (Style)Microsoft.UI.Xaml.Application.Current.Resources[complete && selected == kind ? "AccentButtonStyle" : "DefaultButtonStyle"];
            AutomationProperties.SetName(choices[kind], $"查看全部{KindName(kind)}关联，{count} 项");
        }
        var filtered = rows.Where(x => selected is null || x.Kind == selected).ToArray();
        var page = complete ? filtered.Take(limit).ToArray() : rows.GroupBy(x => x.Kind).SelectMany(g => g.Take(g.Key == BangumiRelationKind.Anime ? 4 : 3)).ToArray();
        foreach (var kind in Kinds)
        {
            var items = page.Where(x => x.Kind == kind).ToArray(); if (items.Length == 0) continue;
            var block = new StackPanel { Spacing = 8 }; var head = new TagFlowPanel { HorizontalSpacing = 10 };
            head.Children.Add(new TextBlock { Text = KindName(kind), FontSize = 18, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
            if (!complete)
            {
                var count = rows.Count(x => x.Kind == kind);
                var browse = new HyperlinkButton
                {
                    Content = $"查看全部 {count} 项",
                    FontSize = 13,
                    Opacity = count > items.Length ? 0.92 : 0.68,
                    Padding = new Thickness(0)
                };
                browse.Click += (_, _) => { Select(kind); FocusFirst(); }; head.Children.Add(browse);
            }
            block.Children.Add(head);
            // The overview is one compact row flow per medium; semantic subheadings belong to the full list.
            foreach (var group in items.GroupBy(x => complete ? x.GroupName : string.Empty))
            {
                if (complete) block.Children.Add(new TextBlock { Text = group.Key, FontSize = 13, Opacity = .75 });
                var flow = new TagFlowPanel { MaximumItemWidth = 280, HorizontalSpacing = 12, VerticalSpacing = 10, CenterInlineItems = false };
                foreach (var item in group) flow.Children.Add(Card(item)); block.Children.Add(flow);
            }
            groups.Children.Add(block);
        }
        more.Visibility = V(complete && filtered.Length > page.Length); more.Content = $"显示更多（还有 {filtered.Length - page.Length} 项）";
        all.Visibility = V(!complete && rows.Length > 0); all.Content = $"查看全部关联作品（{rows.Length}）";
        RenderSources(); if (focusedKey is not null) FocusKey(focusedKey);
        PresentationChanged?.Invoke();
    }
    private Button Card(RelatedWorkCard item)
    {
        var revision = item.Bangumi is not null ? shown?.FetchedAtUtc.ToString("O") ?? "related" : catalogWork?.FetchedAt ?? "related";
        var picture = new CoverPresenter { Pipeline = images!, SourceKey = registry?.Register(item.CoverUrl, revision), PreserveEntireImage = true, ImageCornerRadius = 4 };
        var grid = new Grid { Width = 256, ColumnSpacing = 12, Height = 90 };
        grid.ColumnDefinitions.Add(new() { Width = new GridLength(62) }); grid.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(new Border
        {
            Width = 62,
            Height = 86,
            CornerRadius = new CornerRadius(5),
            Background = (Microsoft.UI.Xaml.Media.Brush)Microsoft.UI.Xaml.Application.Current.Resources["SubtleFillColorSecondaryBrush"],
            Child = picture,
            VerticalAlignment = VerticalAlignment.Center
        });
        var text = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = item.Title, MaxLines = 2, TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.Wrap, FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        text.Children.Add(new TextBlock { Text = item.Label, MaxLines = 1, TextTrimming = TextTrimming.CharacterEllipsis, FontSize = 12, Opacity = .82 });
        text.Children.Add(new TextBlock { Text = item.Provider, FontSize = 11, Opacity = .60 });
        Grid.SetColumn(text, 1); grid.Children.Add(text);
        var button = new Button { Content = grid, Padding = new Thickness(10, 8, 10, 8), CornerRadius = new CornerRadius(8), HorizontalContentAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetAutomationId(button, item.Bangumi is { } bgm ? "BangumiRelatedSubject" + bgm.Id : "NextMoeRelatedWork" + item.Id);
        AutomationProperties.SetName(button, $"{item.Title}，{KindName(item.Kind)}，{item.Label}，查看详情");
        ToolTipService.SetToolTip(button, item.Title + "\n" + item.Label + " · " + item.Provider);
        button.Click += async (_, _) =>
        {
            navigationFocusKey = item.Key;
            var version = generation;
            try { if (item.Bangumi is not null && OpenMedia is not null) await OpenMedia(item); else if (item.Catalog is not null && OpenCatalog is not null) await OpenCatalog(item); }
            catch { if (!disposed && version == generation) { status.Text = "暂时无法打开这部作品，请重试。"; status.Visibility = actions.Visibility = Visibility.Visible; } }
        };
        displayed.Add((item, button, picture)); return button;
    }
    private void RenderSources()
    {
        sourceBody.Children.Clear();
        var linked = rows.Where(row => row.LinkedBangumiSubjectId is not null).ToArray();
        sourceBody.Children.Add(new TextBlock { Text = $"KunGal · {catalogRows.Count(CatalogRelatedWorks.Eligible)} 项游戏关联", TextWrapping = TextWrapping.Wrap });
        sourceBody.Children.Add(new TextBlock { Text = $"Bangumi · {model?.Items.Length ?? 0} 项跨媒体关联", TextWrapping = TextWrapping.Wrap });
        if (linked.Length > 0) sourceBody.Children.Add(new TextBlock { Text = $"{linked.Length} 项重复游戏已合并，详情使用 KunGal。", TextWrapping = TextWrapping.Wrap });
        sourceBody.Children.Add(new TextBlock { Text = "关系以原站标注为准；“动画关联”不一定是直接改编。", TextWrapping = TextWrapping.Wrap, Opacity = .75 });
        if (semanticWarning is not null) sourceBody.Children.Add(new TextBlock { Text = semanticWarning, TextWrapping = TextWrapping.Wrap, FontSize = 13 });
        if (gameIdentityWarning is not null) sourceBody.Children.Add(new TextBlock { Text = gameIdentityWarning, TextWrapping = TextWrapping.Wrap, FontSize = 13 });
        if (shown?.Truncated == true) sourceBody.Children.Add(new TextBlock { Text = "来源关联较多，当前保留已取得的结果。", TextWrapping = TextWrapping.Wrap });
        if (shown?.UnsupportedCount > 0) sourceBody.Children.Add(new TextBlock { Text = $"原站另有 {shown.UnsupportedCount} 项其他媒体类型。", TextWrapping = TextWrapping.Wrap });
        if (model?.Identity is { } identity)
        {
            var link = new HyperlinkButton { Content = "Bangumi 原作与关联 ↗", Padding = new Thickness(0), HorizontalAlignment = HorizontalAlignment.Left };
            AutomationProperties.SetAutomationId(link, "BangumiRelatedSource");
            link.Click += async (_, _) =>
            {
                var version = generation;
                try { if (openLink is not null) await openLink("https://bgm.tv/subject/" + identity.SubjectId); }
                catch { if (!disposed && generation == version && model?.Identity == identity && (currentIdentity?.Invoke(identity) ?? false)) { status.Text = "未能打开来源网页。"; actions.Visibility = status.Visibility = Visibility.Visible; } }
            };
            sourceBody.Children.Add(link); sourceBody.Children.Add(reload);
        }
        rawSources.Content = rawSources.IsExpanded ? RawEvidence() : null;
        sourceBody.Children.Add(rawSources);
    }
    private StackPanel RawEvidence()
    {
        var evidence = new StackPanel { Spacing = 16 };
        if (model?.Result is not null) evidence.Children.Add(new TextBlock { Text = model.Message, TextWrapping = TextWrapping.Wrap, FontSize = 13 });
        foreach (var item in rows)
        {
            var entry = new StackPanel { Spacing = 6 };
            entry.Children.Add(new TextBlock { Text = item.Title, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
            entry.Children.Add(new TextBlock { Text = item.Label + " · " + item.Provider, TextWrapping = TextWrapping.Wrap });
            entry.Children.Add(new TextBlock { Text = item.Evidence, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true, FontSize = 13, Opacity = .75 });
            if (item.LinkedBangumiSubjectId is { } linkedId)
            {
                var link = new HyperlinkButton { Content = "Bangumi 条目 ↗", Padding = new Thickness(0), HorizontalAlignment = HorizontalAlignment.Left };
                var url = "https://bgm.tv/subject/" + linkedId;
                link.Click += async (_, _) =>
                {
                    var version = generation;
                    try { if (openLink is not null) await openLink(url); }
                    catch { if (!disposed && version == generation) { status.Text = "未能打开来源网页。"; actions.Visibility = status.Visibility = Visibility.Visible; } }
                };
                entry.Children.Add(link);
            }
            evidence.Children.Add(entry);
        }
        return evidence;
    }
    private void FocusFirst(int index = 0) { if (displayed.ElementAtOrDefault(index).Item is { } item) FocusKey(item.Key); }
    private void FocusKey(string key)
    {
        var version = generation;
        var target = displayed.FirstOrDefault(x => x.Item.Key == key).Button;
        if (target is null) return;
        void Restore() { if (!disposed && version == generation && IsLoaded && target.IsLoaded && displayed.Any(x => x.Button == target)) target.Focus(FocusState.Programmatic); }
        void LoadedTarget(object sender, RoutedEventArgs args) { target.Loaded -= LoadedTarget; Restore(); }
        if (target.IsLoaded) DispatcherQueue.TryEnqueue(Restore); else target.Loaded += LoadedTarget;
    }
    internal void RestoreNavigationFocus()
    { if (navigationFocusKey is { } key) { navigationFocusKey = null; FocusKey(key); } }
    public Task ExpandAsync() => model?.ExpandAsync() ?? Task.CompletedTask;
    internal Task ExpandForFixtureAsync() => ExpandAsync();
    internal void CollapseForFixture() => model?.Collapse();
    internal void SelectForFixture(BangumiRelationKind kind) => Select(kind);
    internal void AllForFixture() { complete = true; selected = null; limit = PageSize; Render(); }
    internal Task OpenForFixtureAsync(string id)
    {
        var item = rows.FirstOrDefault(x => x.Bangumi?.Id == id) ?? throw new InvalidDataException("Related subject absent.");
        navigationFocusKey = item.Key;
        return OpenMedia?.Invoke(item) ?? Task.CompletedTask;
    }
    internal Task OpenCatalogForFixtureAsync(string id)
    {
        var item = rows.FirstOrDefault(x => x.Catalog?.Id == id) ?? throw new InvalidDataException("Related catalog work absent.");
        navigationFocusKey = item.Key;
        return OpenCatalog?.Invoke(item) ?? Task.CompletedTask;
    }
    internal Task RefreshForFixtureAsync() => model?.RefreshAsync() ?? Task.CompletedTask;
    internal void CancelForFixture() => model?.Cancel();
    internal void SourcesForFixture() { sources.IsExpanded = true; sources.StartBringIntoView(); }
    internal void RawEvidenceForFixture() { sources.IsExpanded = true; rawSources.IsExpanded = true; rawSources.StartBringIntoView(); }
    internal object Evidence => new
    {
        state = model?.State.ToString(), expanded = model?.IsExpanded, status = status.Text, subjectId = model?.Identity?.SubjectId,
        selectedKind = selected?.ToString() ?? "All", total = rows.Length, bangumiTotal = model?.Items.Length ?? 0, catalogTotal = catalogRows.Length,
        fromCache = model?.Result?.FromCache, stale = model?.Result?.IsStale, warning = model?.Result?.Warning, complete, semanticWarning, semanticBusy = semanticStop is not null, semanticEvidence,
        gameIdentityBusy = gameIdentityStop is not null, gameIdentityWarning, gameIdentities, mergedGames = rows.Count(row => row.LinkedBangumiSubjectId is not null),
        filterCorners = choices.Values.Prepend(overview).Select(button => new { corner = button.CornerRadius.TopLeft, style = ReferenceEquals(button.Style, Microsoft.UI.Xaml.Application.Current.Resources["AccentButtonStyle"]) ? "Accent" : "Default" }).ToArray(),
        kinds = Kinds.Select(kind => new { kind = kind.ToString(), count = rows.Count(x => x.Kind == kind) }).ToArray(),
        visibleCards = displayed.Count, canReload = reload.IsEnabled, canCancel = model?.CanCancel, width = ActualWidth,
        sourcesExpanded = sources.IsExpanded, rawEvidenceExpanded = rawSources.IsExpanded,
        sourceSummary = sourceBody.Children.OfType<TextBlock>().Select(x => x.Text).ToArray(),
        focusedCard = XamlRoot is null ? null : displayed.FirstOrDefault(x => ReferenceEquals(x.Button, Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(XamlRoot))).Item?.Key,
        displayed = displayed.Select(x => new { x.Item.Id, x.Item.Title, x.Item.Label, x.Item.Provider, x.Item.RawRelation, x.Item.Evidence, x.Item.Url, x.Item.GroupName, x.Item.Priority, x.Item.LinkedBangumiSubjectId }).ToArray(),
        cardSizes = displayed.Select(x => new { width = x.Button.ActualWidth, height = x.Button.ActualHeight }).ToArray(),
        images = displayed.Select(x => new { x.Picture.SourceStatus, x.Picture.HasImage }).ToArray()
    };
    public void Dispose()
    {
        disposed = true; generation++; StopSemantics(); StopGameIdentities();
        if (model is not null) { model.PropertyChanged -= OnState; model.Dispose(); }
        RefreshContext = null; PresentationChanged = null; OpenMedia = null; OpenCatalog = null; groups.Children.Clear(); displayed.Clear();
    }
}
