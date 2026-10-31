using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TheOrder.Application;
using TheOrder.Domain;
using TheOrder.Presentation;

namespace TheOrder.WinUI.Views;

public sealed record CatalogIncomingRelation(
    string OriginId, string OriginTitle, string Label, string RawRelation, string Evidence, string? SourceUrl);

// Read-only native browsing. All additions still use the existing explicit wishlist transaction.
public sealed partial class CatalogBrowserView : Grid, IImageHost
{
    private sealed record NavigationEntry(CatalogEntityRef Entity, CatalogIncomingRelation? Incoming, double ScrollOffset, RelatedWorksBrowseState? Related);
    private readonly IOnlineCatalog catalog;
    private readonly CatalogPresentationService presentations;
    private CatalogContentView? workContent;
    private ICatalogReleaseReader? releasesReader;
    private Func<long>? releasesAuth;
    public void ConfigureReleases(ICatalogReleaseReader reader, Func<long> auth) { releasesReader = reader; releasesAuth = auth; }
    private readonly IEntityCatalog entities;
    private readonly IRemoteCoverRegistry registry;
    private readonly Func<long> authGeneration;
    private readonly Func<LibrarySnapshot?> readLibrary;
    private readonly Stack<NavigationEntry> history = new();
    private readonly TextBlock title = new() { FontSize = 28, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock message = new() { TextWrapping = TextWrapping.Wrap };
    private readonly ProgressBar progress = new() { IsIndeterminate = true, Visibility = Visibility.Collapsed };
    private readonly StackPanel body = new() { Spacing = 20 };
    private readonly ScrollViewer scroll;
    private BannerView? hero;
    private bool stageAllowed, stageContrast;
    private MaterialMode stageMode = MaterialMode.Standard;
    public void SetStagePolicy(bool allowed, bool highContrast, MaterialMode mode) { stageAllowed = allowed; stageContrast = highContrast; stageMode = mode; hero?.SetPolicy(allowed, highContrast, mode); }
    internal object? HeroEvidence => hero?.Evidence;
    private CancellationTokenSource? stop;
    private long generation;
    private CatalogEntityRef? current;
    private CatalogIncomingRelation? incoming;
    private string? cursor;
    private readonly HashSet<string> seenWorks = new(), seenCursors = new();
    private TagFlowPanel? worksGrid;
    private TagFlowPanel? shelfGrid;
    private CatalogShelfMembership? shelfMembership;
    private TextBlock shelfHeading = new() { FontSize = 22 }, outsideHeading = new() { FontSize = 22 };
    private TextBlock shelfEmpty = new() { TextWrapping = TextWrapping.Wrap }, outsideEmpty = new() { TextWrapping = TextWrapping.Wrap };
    private readonly HashSet<string> shelfWorkIds = new(StringComparer.Ordinal);
    internal object? ShelfEvidence => current?.Kind == CatalogEntityKind.Tag && shelfMembership is not null
        ? new { loaded = seenWorks.Count, inShelf = shelfGrid?.Children.Count ?? 0, outside = worksGrid?.Children.Count ?? 0,
            more = cursor is not null, inShelfWorkIds = shelfWorkIds.ToArray(), outsideWorkIds = seenWorks.Except(shelfWorkIds).ToArray() } : null;
    private Button? more;
    public IImagePipeline Images { get; }
    public Func<Task>? ReturnToSearch { get; set; }
    public Func<string, Task>? AddWishlist { get; set; }
    public bool CanGoBack => mediaActive || history.Count > 0;
    public CatalogEntityRef? Current => current;
    internal object RelationEvidence => new
    {
        currentId = current?.Id,
        currentKind = current?.Kind.ToString(),
        incoming = incoming?.Label,
        origin = incoming?.OriginId,
        originTitle = incoming?.OriginTitle,
        rawRelation = incoming?.RawRelation,
        relationEvidence = incoming?.Evidence,
        sourceUrl = IncomingSourceUrl(incoming?.SourceUrl),
        historyDepth = history.Count,
        scroll = scroll.VerticalOffset,
        relations = relatedWorks?.Evidence,
        mediaVisible = mediaActive,
        media = mediaDetail?.Evidence,
        releases = workContent?.ReleaseEvidence,
        entityFacts = current?.Kind is not (null or CatalogEntityKind.Work) ? body.Children.OfType<TextBlock>().Select(x => x.Text).ToArray() : null,
        entitySources = body.Children.OfType<Expander>().Where(x => Equals(x.Tag, "entity-sources"))
            .SelectMany(x => x.Content is StackPanel panel ? panel.Children.OfType<TagFlowPanel>() : [])
            .SelectMany(x => x.Children.OfType<HyperlinkButton>()).Select(x => new { label = x.Content?.ToString(), url = x.Tag as string }).ToArray(),
        focus = XamlRoot is not null && Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(XamlRoot) is DependencyObject focused
            ? Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(focused) : null
    };

    public CatalogBrowserView(IOnlineCatalog catalog, IEntityCatalog entities, IImagePipeline images, IRemoteCoverRegistry registry, Func<long> authGeneration, CatalogPresentationService presentations, Func<LibrarySnapshot?> readLibrary)
    {
        this.presentations = presentations; this.catalog = catalog; this.entities = entities; Images = images; this.registry = registry; this.authGeneration = authGeneration; this.readLibrary = readLibrary;
        RowSpacing = 16;
        RowDefinitions.Add(new() { Height = GridLength.Auto }); RowDefinitions.Add(new() { Height = GridLength.Auto }); RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        var heading = new StackPanel { Spacing = 12 }; heading.Children.Add(title); Children.Add(heading);
        var status = new StackPanel { Spacing = 8 }; status.Children.Add(progress); status.Children.Add(message); SetRow(status, 1); Children.Add(status);
        scroll = new() { Content = body, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        SetRow(scroll, 2); Children.Add(scroll);
        body.SizeChanged += (_, _) => QueueRestore();
        AddHandler(UIElement.PointerWheelChangedEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler((_, _) => restoreTarget = null), true);
        AddHandler(UIElement.PointerPressedEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler((_, _) => restoreTarget = null), true);
        KeyDown += (_, _) => restoreTarget = null;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(this, "CatalogBrowser");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetLiveSetting(message, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite);
        // Preserve the live request if XAML delivers an old Unloaded after reattaching this page.
        Unloaded += (_, _) => { if (!IsLoaded) Cancel(); };
    }

    public Task OpenAsync(CatalogEntityRef entity, bool remember = true, WorkMetadata? loadedWork = null, CatalogIncomingRelation? incoming = null)
    {
        if (remember && current is not null && (current.Kind != entity.Kind || current.Id != entity.Id))
        {
            if (history.Count >= 32) history.Clear();
            history.Push(new(current, this.incoming, scroll.VerticalOffset, relatedWorks?.CaptureBrowsing()));
        }
        this.incoming = incoming;
        return LoadAsync(entity, loadedWork);
    }

    public async Task<bool> GoBackAsync()
    {
        if (mediaActive) { if (mediaDetail?.GoBackMedia() != true) CloseMedia(); return true; }
        if (!history.TryPop(out var previous)) return false;
        incoming = previous.Incoming;
        await LoadAsync(previous.Entity, null, restore: previous);
        return true;
    }
    public void Reset() { Cancel(); history.Clear(); current = null; incoming = null; body.Children.Clear(); }
    public void Cancel() { workContent?.DeactivateReleases(); generation++; restoreTarget = null; stop?.Cancel(); StopRelated(); hero?.ReleaseBeforeLeave(); hero = null; progress.Visibility = Visibility.Collapsed; }

    private async Task LoadAsync(CatalogEntityRef entity, WorkMetadata? loadedWork, bool refresh = false, NavigationEntry? restore = null)
    {
        Cancel(); stop?.Dispose(); stop = new(); var cancellation = stop.Token; var version = ++generation; var auth = authGeneration();
        restoreTarget = restore; catalogLoading = true;
        current = entity; title.Text = CatalogLanguage.Name(entity); title.Visibility = Visibility.Visible;
        body.Children.Clear(); seenWorks.Clear(); seenCursors.Clear(); shelfWorkIds.Clear(); cursor = null; worksGrid = null; shelfGrid = null; shelfMembership = null; more = null;
        AddIncomingRelation();
        message.Text = UiText.Get("正在读取资料…"); progress.Visibility = Visibility.Visible; scroll.ChangeView(null, 0, null);
        bool Accept() => version == generation && !cancellation.IsCancellationRequested && auth == authGeneration();
        try
        {
            if (entity.Kind == CatalogEntityKind.Work)
            {
                // Search/list projections are not full details and may never overwrite the prepared cache.
                var cached = await presentations.GetAsync(entity.Id, refresh, cancellation);
                var work = CatalogLanguage.Project(cached.Work);
                if (!Accept()) return;
                currentPresentation = cached;
                current = entity with { Id = work.Id, Name = work.Title }; title.Text = work.Title;
                var banner = new BannerView(Images, registry) { Browse = e => OpenAsync(e) }; hero = banner; banner.SetPolicy(stageAllowed, stageContrast, stageMode); banner.SetPresentation(cached); title.Visibility = Visibility.Collapsed;
                if (AddWishlist is not null) { var add = new Button { Content = UiText.Get("加入愿望单") }; add.Click += async (_, _) => { if (auth != authGeneration()) { message.Text = UiText.Get("凭据已改变，请重新读取。"); return; } await AddWishlist(work.Id); }; body.Children.Add(add); }
                body.Children.Add(banner);
                var content = Content();
                content.ShowRelatedWorks = relationsReader is null;
                workContent = content; content.Sources = SourceLinks(cached.Work, banner.Choice); content.SetWork(cached.Work, work.Title, collapseFacts: true); body.Children.Add(content);
                // A readable title is not identity evidence. Resolve related
                // media only from the untouched provider detail snapshot.
                ShowRelated(cached.Work, auth);
                if (restore?.Related is { } state) relatedWorks?.RestoreBrowsing(state);
                message.Text = UiText.Get("在线资料 · 未自动添加到书架");
                if (CatalogPresentationService.NeedsPreparation(cached)) { message.Text += UiText.Get(" · 图片正在后台准备"); presentations.Queue([work.Id],priority:true); }
            }
            else
            {
                var detail = await entities.EntityAsync(entity.Kind, entity.Id, cancellation); if (!Accept()) return;
                current = detail.Entity; title.Text = CatalogLanguage.Name(detail.Entity);
                if (detail.Entity.ImageUrl is { } url) body.Children.Add(new CoverPresenter { SourceKey = registry.Register(url, "entity"), Width = 180, Height = 220, PreserveEntireImage = true, HorizontalAlignment = HorizontalAlignment.Left });
                if (!detail.Facts.IsEmpty) body.Children.Add(new TextBlock { Text = string.Join("\n", detail.Facts), TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true });
                if (detail.Description.Length > 0)
                {
                    var selected = ContentLanguage.Catalog(new("NextMoe", entity.Kind.ToString(), entity.Id), "synopsis", detail.Introductions, detail.Description,
                        detail.Entity.Names?.FirstOrDefault(x => x.Source == "original")?.Language);
                    body.Children.Add(new MarkdownView { Text = selected.Text, Images = Images, Registry = registry, OpenLink = OpenLinkAsync });
                    var originalInfo = new ContentLanguageInfo(); originalInfo.Show(selected); body.Children.Add(originalInfo);
                }
                body.Children.Add(EntitySources(detail));
                if (!detail.Related.IsEmpty) { var links = new TagFlowPanel(); foreach (var item in detail.Related) links.Children.Add(item.Kind == CatalogEntityKind.Trait ? CatalogTagChip.Create(CatalogLanguage.Name(item), item, e => OpenAsync(e)) : EntityLink(item)); body.Children.Add(links); }
                if (entity.Kind == CatalogEntityKind.Trait)
                {
                    body.Children.Add(new TextBlock { Text = UiText.Get("相关角色"), FontSize = 22 }); worksGrid = new(); body.Children.Add(worksGrid);
                    more = new Button { Content = UiText.Get("加载下一页"), Visibility = Visibility.Collapsed, HorizontalAlignment = HorizontalAlignment.Left }; more.Click += async (_, _) => await LoadMoreAsync(); body.Children.Add(more);
                    await AppendCharactersAsync(detail.Entity, null, version, auth, cancellation);
                }
                else if (entity.Kind != CatalogEntityKind.Person)
                {
                    if (entity.Kind == CatalogEntityKind.Tag)
                    {
                        var snapshot = readLibrary();
                        if (snapshot is null || snapshot.Access == LibraryAccess.InvalidInput)
                        { Failure(UiText.Get("书架尚未成功读取，暂时无法区分相关作品。请在书架加载后重试。"), entity); return; }
                        shelfMembership = new(snapshot); shelfGrid = new(); worksGrid = new();
                        shelfHeading = new() { FontSize = 22 }; outsideHeading = new() { FontSize = 22 };
                        shelfEmpty = new() { TextWrapping = TextWrapping.Wrap }; outsideEmpty = new() { TextWrapping = TextWrapping.Wrap };
                        var owned = new StackPanel { Spacing = 12 }; owned.Children.Add(shelfHeading); owned.Children.Add(shelfEmpty); owned.Children.Add(shelfGrid);
                        var outside = new StackPanel { Spacing = 12 }; outside.Children.Add(outsideHeading); outside.Children.Add(outsideEmpty); outside.Children.Add(worksGrid);
                        body.Children.Add(owned); body.Children.Add(outside); UpdateShelfGroups();
                    }
                    else { body.Children.Add(new TextBlock { Text = UiText.Get("相关作品"), FontSize = 22 }); worksGrid = new(); body.Children.Add(worksGrid); }
                    more = new Button { Content = UiText.Get("加载下一页"), Visibility = Visibility.Collapsed, HorizontalAlignment = HorizontalAlignment.Left }; more.Click += async (_, _) => await LoadMoreAsync(); body.Children.Add(more);
                    await AppendWorksAsync(detail.Entity, null, version, auth, cancellation);
                }
                if (Accept()) message.Text = entity.Kind == CatalogEntityKind.Tag ? UiText.Format($"已加载 {seenWorks.Count} 部相关作品") : UiText.Get("目录资料");
            }
        }
        catch (OperationCanceledException) { if (version == generation) message.Text = UiText.Get("读取已取消。"); }
        catch (OnlineException e) { if (Accept()) Failure(e.Message, entity); }
        catch (Exception e) { if (Accept()) { App.TraceVisualFailure(e); Failure(UiText.Get("资料未能读取，请重试。"), entity); } }
        finally { if (version == generation) { catalogLoading = false; progress.Visibility = Visibility.Collapsed; if (auth != authGeneration()) { StopRelated(); body.Children.Clear(); AddIncomingRelation(); message.Text = UiText.Get("凭据已改变，请重新读取资料。"); } QueueRestore(); } }
    }

    internal async void RefreshPrepared(string id)
    {
        if (current is not { Kind: CatalogEntityKind.Work } work || work.Id != id || hero is null || stop is null) return;
        var version = generation; var auth = authGeneration(); var token = stop.Token;
        try {
            var cached = await presentations.GetAsync(id, cancellation: token);
            if (version != generation || token.IsCancellationRequested || auth != authGeneration()) return;
            currentPresentation = cached;
            hero.SetPresentation(cached);
            if (workContent is { } content) { content.Sources = SourceLinks(cached.Work, cached.Banner); content.SetWork(cached.Work, cached.Work.Title, collapseFacts: true); }
            UpdateRelated(cached.Work, auth);
            message.Text = cached.Prepared ? UiText.Get("在线资料 · 未自动添加到书架") : UiText.Get("图片准备未完成，可通过来源中的刷新重试");
        } catch (OperationCanceledException) { } catch (Exception e) { App.TraceVisualFailure(e); }
    }
    private void Failure(string text, CatalogEntityRef entity) { message.Text = text; var retry = new Button { Content = UiText.Get("重试"), HorizontalAlignment = HorizontalAlignment.Left }; retry.Click += async (_, _) => await LoadAsync(entity, null); body.Children.Add(retry); }
    private CatalogContentView Content() => new() { ReleasesAuthGeneration = releasesAuth, ReleasesReader = releasesReader, Images = Images, Registry = registry, Browse = e => OpenAsync(e), OpenLink = OpenLinkAsync };
    private HyperlinkButton EntityLink(CatalogEntityRef entity) { var button = new HyperlinkButton { Content = CatalogLanguage.Name(entity), Padding = new Thickness(0, 0, 12, 0) }; button.Click += async (_, _) => await OpenAsync(entity); return button; }
    private UIElement EntitySources(CatalogEntityDetail detail)
    {
        var source = new StackPanel { Spacing = 8 };
        source.Children.Add(new TextBlock { Text = "NextMoe", TextWrapping = TextWrapping.Wrap });
        var links = new TagFlowPanel { HorizontalSpacing = 12, VerticalSpacing = 8 };
        foreach (var reference in detail.References.Where(r => r.Url is not null).DistinctBy(r => r.Url))
        {
            var url = reference.Url!;
            var name = reference.Source switch { "vndb" => "VNDB", "bangumi" => "Bangumi", _ => reference.Source };
            var link = new HyperlinkButton { Content = name, Tag = url, Padding = new Thickness(0), HorizontalAlignment = HorizontalAlignment.Left };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(link, "CatalogEntitySource-" + reference.Source);
            ToolTipService.SetToolTip(link, url);
            link.Click += async (_, _) => await OpenSourceAsync(url);
            links.Children.Add(link);
        }
        if (links.Children.Count > 0) source.Children.Add(links);
        return new SmoothExpander { Header = UiText.Get("资料来源"), Tag = "entity-sources", Content = source, HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch, IsExpanded = false };
    }
    private UIElement SourceLinks(WorkMetadata work, BannerChoice? artwork)
    {
        var source = new StackPanel { Spacing = 8 };
        var refresh = new Button { Content = UiText.Get("刷新资料与图片"), HorizontalAlignment = HorizontalAlignment.Left };
        var reference = current is { Kind: CatalogEntityKind.Work } active && active.Id == work.Id
            ? active : new CatalogEntityRef(CatalogEntityKind.Work, work.Id, work.Title);
        refresh.Click += async (_, _) => await LoadAsync(reference, null, true, new(reference, incoming, scroll.VerticalOffset, relatedWorks?.CaptureBrowsing()));
        source.Children.Add(refresh);
        if (artwork?.ReferenceTitle is { } title) source.Children.Add(new TextBlock { Text = UiText.Get("横幅 CG · ") + title + UiText.Get("（关联版本）"), TextWrapping = TextWrapping.Wrap });
        var row = new TagFlowPanel();
        foreach (var link in CandidateDetailLinks.From(work)) { var b = new HyperlinkButton { Content = link.Title, Padding = new Thickness(0, 0, 16, 0) }; b.Click += async (_, _) => await OpenSourceAsync(link.Url); row.Children.Add(b); }
        source.Children.Add(row);
        return new SmoothExpander { Header = UiText.Get("资料与图片来源"), Content = source, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch, IsExpanded = false };
    }
    private void AddIncomingRelation()
    {
        if (incoming is not { } relation) return;
        var label = string.IsNullOrWhiteSpace(relation.Label) ? UiText.Get("关联作品") : relation.Label;
        var heading = new TextBlock
        {
            Text = UiText.Format($"与《{relation.OriginTitle}》的关系：{label}"),
            TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = true
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(heading, "CatalogIncomingRelation");
        var evidence = new StackPanel { Spacing = 8 };
        evidence.Children.Add(new TextBlock { Text = UiText.Get("游戏资料 · KunGal"), TextWrapping = TextWrapping.Wrap });
        var raw = new StackPanel { Spacing = 8 };
        raw.Children.Add(new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(relation.RawRelation) ? UiText.Get("原始关系：来源未提供") : UiText.Format($"原始关系：{relation.RawRelation}"),
            TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true
        });
        if (!string.IsNullOrWhiteSpace(relation.Evidence)) raw.Children.Add(new TextBlock
        {
            Text = UiText.Format($"原始说明：{relation.Evidence}"), TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true
        });
        if (IncomingSourceUrl(relation.SourceUrl) is { } url)
        {
            var source = new HyperlinkButton { Content = UiText.Get("KunGal 作品页 ↗"), Padding = new Thickness(0), HorizontalAlignment = HorizontalAlignment.Left };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(source, "CatalogIncomingSource");
            source.Click += async (_, _) => await OpenSourceAsync(url);
            evidence.Children.Add(source);
        }
        evidence.Children.Add(new SmoothExpander { Header = UiText.Get("详细证据"), Content = raw, IsExpanded = false,
            HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch });
        var details = new SmoothExpander {
            Header = UiText.Get("关系与来源"), Content = evidence, IsExpanded = false,
            HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(details, "CatalogIncomingEvidence");
        var section = new StackPanel { Spacing = 8 }; section.Children.Add(heading); section.Children.Add(details);
        body.Children.Add(section);
    }
    private static string? IncomingSourceUrl(string? value)
    {
        const string prefix = "https://www.kungal.com/galgame/";
        if (value is null || !value.StartsWith(prefix, StringComparison.Ordinal)) return null;
        var id = value[prefix.Length..];
        return id.Length is > 0 and <= 20 && id.All(c => c is >= '0' and <= '9') ? prefix + id : null;
    }
    private async Task OpenSourceAsync(string url)
    {
        if (!MarkdownContent.AllowedLink(url)) return;
        var version = generation;
        try { if (!await Windows.System.Launcher.LaunchUriAsync(new Uri(url)) && version == generation) message.Text = UiText.Get("链接未能打开。"); }
        catch { if (version == generation) message.Text = UiText.Get("链接未能打开。"); }
    }
    public async Task OpenLinkAsync(string url)
    {
        if (!MarkdownContent.AllowedLink(url)) return;
        if (KunGalNavigation.TryEntity(url, out var entity)) { await OpenAsync(entity); return; }
        var version = generation;
        try { if (!await Windows.System.Launcher.LaunchUriAsync(new Uri(url)) && generation == version) message.Text = UiText.Get("链接未能打开。"); } catch { if (generation == version) message.Text = UiText.Get("链接未能打开。"); }
    }
    private async Task LoadMoreAsync()
    {
        if (current is null || cursor is null || stop is null || more is null || !more.IsEnabled) return;
        more.IsEnabled = false; var version = generation; var auth = authGeneration(); var cancellation = stop.Token;
        try { if (current.Kind == CatalogEntityKind.Trait) await AppendCharactersAsync(current, cursor, version, auth, cancellation); else await AppendWorksAsync(current, cursor, version, auth, cancellation); }
        catch (OperationCanceledException) { }
        catch (OnlineException e) { if (version == generation && auth == authGeneration()) message.Text = e.Message; }
        catch (Exception) { if (version == generation) message.Text = UiText.Get("更多作品未能读取，请重试。"); }
        finally { if (version == generation && more is not null) more.IsEnabled = true; }
    }
    private async Task AppendWorksAsync(CatalogEntityRef entity, string? next, long version, long auth, CancellationToken cancellation)
    {
        var page = await entities.EntityWorksAsync(entity.Kind, entity.Id, next, cancellation);
        if (version != generation || cancellation.IsCancellationRequested || auth != authGeneration() || worksGrid is null) return;
        if (next is not null) seenCursors.Add(next);
        foreach (var work in page.Works.Select(CatalogLanguage.Project).Where(w => seenWorks.Count < 500 && seenWorks.Add(w.Id)))
        {
            var card = new StackPanel { Width = 150, Spacing = 6 }; card.Children.Add(new CoverPresenter { SourceKey = registry.Register(work.CoverUrl, work.FetchedAt), Width = 150, Height = 202 }); card.Children.Add(new TextBlock { Text = work.Title, TextAlignment = TextAlignment.Center, HorizontalAlignment = HorizontalAlignment.Stretch, TextWrapping = TextWrapping.Wrap, MaxLines = 2 });
            var button = new Button { Content = card, Padding = new Thickness(0), Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent), BorderThickness = new Thickness(0), Margin = new Thickness(0, 0, 16, 20) }; button.Click += async (_, _) => await OpenAsync(new(CatalogEntityKind.Work, work.Id, work.Title));
            if (shelfGrid is not null && shelfMembership?.Contains(work) == true) { shelfWorkIds.Add(work.Id); shelfGrid.Children.Add(button); }
            else worksGrid.Children.Add(button);
        }
        cursor = page.NextCursor is { } candidate && !seenCursors.Contains(candidate) && seenWorks.Count < 500 ? candidate : null;
        if (more is not null) more.Visibility = cursor is null ? Visibility.Collapsed : Visibility.Visible;
        if (shelfGrid is not null) { UpdateShelfGroups(); message.Text = UiText.Format($"已加载 {seenWorks.Count} 部相关作品"); }
        else if (seenWorks.Count == 0) body.Children.Add(new TextBlock { Text = UiText.Get("此项尚未收录关联作品。") });
    }
    private void UpdateShelfGroups()
    {
        if (shelfGrid is null || worksGrid is null) return;
        shelfHeading.Text = UiText.Format($"已在书架 · {shelfGrid.Children.Count}");
        outsideHeading.Text = UiText.Format($"未在书架 · {worksGrid.Children.Count}");
        shelfEmpty.Text = cursor is null ? UiText.Get("已加载的相关作品中没有已入库作品。") : UiText.Get("当前已加载结果中没有已入库作品，可继续加载。");
        outsideEmpty.Text = cursor is null ? UiText.Get("已加载的相关作品均已在书架。") : UiText.Get("当前已加载结果均已在书架，可继续加载。");
        shelfEmpty.Visibility = shelfGrid.Children.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        outsideEmpty.Visibility = worksGrid.Children.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }
    private async Task AppendCharactersAsync(CatalogEntityRef entity, string? next, long version, long auth, CancellationToken cancellation)
    {
        var page = await entities.TraitCharactersAsync(entity.Id, next, cancellation);
        if (version != generation || cancellation.IsCancellationRequested || auth != authGeneration() || worksGrid is null) return;
        if (next is not null) seenCursors.Add(next);
        foreach (var character in page.Items.Select(CatalogLanguage.Project).Where(c => seenWorks.Count < 500 && seenWorks.Add(c.Id)))
        {
            var card = new StackPanel { Width = 122, Spacing = 6 };
            card.Children.Add(new CoverPresenter { SourceKey = registry.Register(character.ImageUrl, "character"), Width = 122, Height = 146, PreserveEntireImage = true });
            card.Children.Add(new TextBlock { Text = character.Name, TextAlignment = TextAlignment.Center, HorizontalAlignment = HorizontalAlignment.Stretch, TextWrapping = TextWrapping.Wrap, MaxLines = 2 });
            var button = new Button { Content = card, Padding = new Thickness(0), Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent), BorderThickness = new Thickness(0), Margin = new Thickness(0, 0, 12, 16) };
            button.Click += async (_, _) => await OpenAsync(character); worksGrid.Children.Add(button);
        }
        cursor = page.NextCursor is { } candidate && !seenCursors.Contains(candidate) && seenWorks.Count < 500 ? candidate : null;
        if (more is not null) more.Visibility = cursor is null ? Visibility.Collapsed : Visibility.Visible;
        if (seenWorks.Count == 0) body.Children.Add(new TextBlock { Text = UiText.Get("此项尚未收录关联角色。") });
    }

}
