using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using TheOrder.Application;
using TheOrder.Presentation;

namespace TheOrder.WinUI.Views;

// All content participates in the host's single outer ScrollViewer.
public sealed class BookPublicationView : StackPanel, IDisposable
{
    private readonly Expander section = Expand("书籍与出版资料"), raw = Expand("书目来源与原始字段"), relations = Expand("系列与单行本"), ndl = Expand("查找出版版本 · NDL");
    private readonly StackPanel content = new() { Spacing = 14 }, facts = new() { Spacing = 8 }, rawBody = new() { Spacing = 8 }, searchBody = new() { Spacing = 10 }, savedBody = new() { Spacing = 8 }, previewBody = new() { Spacing = 10 };
    private readonly TagFlowPanel relationCards = Flow(), candidateCards = Flow(), actions = Flow();
    private readonly Grid searchForm = new() { ColumnSpacing = 12, RowSpacing = 10 };
    private readonly List<Button> savedActions = [];
    private readonly TextBlock heading = Text(20), identity = Text(13), incoming = Text(13), message = Text(14), relationNotice = Text(13), searchNotice = Text(13), confirmation = Text(13);
    private readonly TextBlock chapterNotice = Text(13);
    private readonly TextBox queryTitle = new() { Header = "原标题", PlaceholderText = "书名", MaxLength = 200, HorizontalAlignment = HorizontalAlignment.Stretch }, queryIsbn = new() { Header = "ISBN（可选）", PlaceholderText = "输入 ISBN 精确查找", MaxLength = 40, HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly Button back = Button("返回上一本"), cancel = Button("取消读取"), reloadSaved = Button("刷新已保存版本"), refreshRelations = Button("刷新关系"), search = Button("查找"), next = Button("查看更多版本"), confirm = Button("确认此出版版本"), refreshPreview = Button("刷新记录");
    private BookDetailsViewModel? model;
    private readonly BookReadingView reading = new();
    private AniListDetails? rootReadingAniList;
    private string? rootReadingFingerprint;
    private bool readingAllowed;
    private string? shownSource;
    private BangumiSubjectDetails? shownBangumiFacts;
    private AniListDetails? shownAniListFacts;
    private BangumiRelationsLoadResult? shownRelations;
    private BookPublication[]? shownCandidates;
    private BookPublicationBinding[]? shownBindings;
    private BookPublicationResult? shownPreview;
    private bool syncing, disposed;
    private long renderVersion;

    public BookPublicationView()
    {
        Spacing = 10; Visibility = Visibility.Collapsed;
        AutomationProperties.SetAutomationId(this, "BookPublication"); AutomationProperties.SetAutomationId(section, "BookExpand");
        AutomationProperties.SetAutomationId(relations, "BookRelations"); AutomationProperties.SetAutomationId(ndl, "BookSearchSection");
        AutomationProperties.SetAutomationId(queryTitle, "BookSearchTitle"); AutomationProperties.SetAutomationId(queryIsbn, "BookSearchIsbn");
        AutomationProperties.SetAutomationId(search, "BookSearch"); AutomationProperties.SetAutomationId(confirm, "BookConfirm");
        AutomationProperties.SetAutomationId(message, "BookStatus"); AutomationProperties.SetAutomationId(back, "BookBack");
        heading.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold; identity.Opacity = incoming.Opacity = .75;
        message.MaxWidth = searchNotice.MaxWidth = relationNotice.MaxWidth = confirmation.MaxWidth = 960;
        message.HorizontalAlignment = searchNotice.HorizontalAlignment = relationNotice.HorizontalAlignment = confirmation.HorizontalAlignment = HorizontalAlignment.Left;
        confirm.Style = (Style)Microsoft.UI.Xaml.Application.Current.Resources["AccentButtonStyle"];
        actions.Children.Add(back); actions.Children.Add(cancel); actions.Children.Add(reloadSaved);
        content.Children.Add(heading); content.Children.Add(identity); content.Children.Add(incoming); content.Children.Add(facts); content.Children.Add(actions); content.Children.Add(message);
        content.Children.Insert(4, chapterNotice); chapterNotice.Opacity = .75;
        raw.Content = rawBody;
        var relationBody = new StackPanel { Spacing = 10, Children = { relationNotice, refreshRelations, relationCards } };
        relations.Content = relationBody; content.Children.Add(relations); content.Children.Add(savedBody);
        searchForm.ColumnDefinitions.Add(new() { Width = new(2, GridUnitType.Star) }); searchForm.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); searchForm.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        searchForm.RowDefinitions.Add(new() { Height = GridLength.Auto }); searchForm.RowDefinitions.Add(new() { Height = GridLength.Auto }); searchForm.RowDefinitions.Add(new() { Height = GridLength.Auto });
        search.VerticalAlignment = VerticalAlignment.Bottom; searchForm.Children.Add(queryTitle); searchForm.Children.Add(queryIsbn); searchForm.Children.Add(search);
        searchForm.SizeChanged += (_, _) => ArrangeSearchForm(); ArrangeSearchForm();
        searchBody.Children.Add(searchForm); searchBody.Children.Add(Text(12, "有 ISBN 时优先按编号查找；确认前请核对卷次和版本。"));
        searchBody.Children.Add(searchNotice); searchBody.Children.Add(candidateCards); searchBody.Children.Add(next);
        ndl.Content = searchBody; content.Children.Add(ndl); content.Children.Add(previewBody); content.Children.Add(confirmation);
        var publicationActions = Flow(); publicationActions.Children.Add(confirm); publicationActions.Children.Add(refreshPreview); content.Children.Add(publicationActions);
        content.Children.Add(raw);
        section.Content = content; Children.Add(reading); Children.Add(section);
        section.Expanding += async (_, _) => { if (!syncing && model is not null) await model.ExpandAsync(); };
        section.Collapsed += (_, _) => { if (!syncing) model?.Collapse(); };
        relations.Expanding += async (_, _) => { if (!syncing && model is not null) await model.LoadRelationsAsync(); };
        back.Click += (_, _) => GoBack(); cancel.Click += (_, _) => model?.Cancel();
        reloadSaved.Click += async (_, _) => { if (model is not null) await model.ReloadBindingsAsync(); };
        refreshRelations.Click += async (_, _) => { if (model is not null) await model.LoadRelationsAsync(true); };
        search.Click += async (_, _) => await SearchAsync(); queryTitle.KeyDown += async (_, e) => { if (e.Key == Windows.System.VirtualKey.Enter) { e.Handled = true; await SearchAsync(); } };
        queryIsbn.KeyDown += async (_, e) => { if (e.Key == Windows.System.VirtualKey.Enter) { e.Handled = true; await SearchAsync(); } };
        next.Click += async (_, _) => await NextAsync();
        confirm.Click += async (_, _) => { if (model is not null) await model.ConfirmAsync(); };
        refreshPreview.Click += async (_, _) => { if (model?.Preview is { } preview) await model.PreviewAsync(preview.Publication.Id, true); };
        foreach (var panel in new[] { relationCards, candidateCards }) panel.SizeChanged += (_, _) => Clamp(panel);
        Unloaded += (_, _) => { if (!IsLoaded) Deactivate(); };
    }
    public void Configure(IBangumiSubjectDetailsReader subjects, IBookBibliographyReader books, IBangumiBookRelationsReader bookRelations, IAniListReader? aniList = null)
    {
        if (model is not null) { model.PropertyChanged -= StateChanged; model.Dispose(); }
        model = new(subjects, books, bookRelations); model.PropertyChanged += StateChanged; reading.Configure(aniList, subjects); Render();
    }
    public void SetSource(BangumiSubjectDetails? bangumi, AniListDetails? currentAniList, bool bridgeTrusted, bool linksAllowed = true)
    {
        readingAllowed = linksAllowed;
        var root = bridgeTrusted && bangumi?.Kind == BangumiRelationKind.Book ? BookSource.From(bangumi)
            : currentAniList?.Media.Kind == AniListMediaKind.Manga ? BookSource.From(currentAniList) : null;
        rootReadingFingerprint = root?.Fingerprint;
        rootReadingAniList = linksAllowed && (root?.Identity.Provider == "AniList" || bridgeTrusted && root?.Identity.Level is BookEntityLevel.Unknown or BookEntityLevel.Series) ? currentAniList : null;
        model?.SetSource(bangumi, currentAniList, bridgeTrusted);
    }
    private void StateChanged(object? sender, PropertyChangedEventArgs e) { if (!disposed) Render(); }
    private void Render()
    {
        renderVersion++; var source = model?.Source;
        Visibility = Visible(source is not null); if (model is null || source is null) return;
        reading.SetSource(readingAllowed ? source : null,
            readingAllowed && source.Fingerprint == rootReadingFingerprint ? rootReadingAniList : null,
            readingAllowed ? model.Bangumi : null);
        syncing = true; section.IsExpanded = model.Expanded; syncing = false;
        heading.Text = model.Bangumi is { } titleSource ? ContentLanguage.BangumiTitle(titleSource).Text : model.AniList is { } aniTitle ? ContentLanguage.AniListTitle(aniTitle.Media).Text : source.Title; heading.Visibility = Visible(model.CanBack);
        identity.Text = source.Identity.Provider + (source.Identity.Level == BookEntityLevel.Unknown ? "" : " · " + LevelName(source.Identity.Level));
        incoming.Text = model.Incoming; incoming.Visibility = Visible(incoming.Text.Length > 0);
        message.Text = model.Message; message.Visibility = Visible(message.Text.Length > 0);
        chapterNotice.Text = model.ChapterDirectoryNotice; chapterNotice.Visibility = Visible(chapterNotice.Text.Length > 0);
        back.Visibility = Visible(model.CanBack); cancel.Visibility = Visible(model.IsBusy); back.IsEnabled = model.CanBack;
        reloadSaved.IsEnabled = model.CanLoad; reloadSaved.Visibility = Visible(model.Bindings.Length > 0 || !model.BindingsLoaded || model.State is BookDetailsState.Failed or BookDetailsState.Cancelled);
        if (shownSource != source.Fingerprint)
        {
            shownSource = source.Fingerprint; queryTitle.Text = model.DefaultTitle.Length <= 200 ? model.DefaultTitle : model.DefaultTitle[..200]; queryIsbn.Text = model.DefaultIsbn;
            syncing = true; raw.IsExpanded = false; relations.IsExpanded = model.Relations is not null; ndl.IsExpanded = model.Search is not null; syncing = false;
            shownRelations = null; shownCandidates = null; shownBindings = null; shownPreview = null;
            relationCards.Children.Clear(); candidateCards.Children.Clear(); savedBody.Children.Clear(); previewBody.Children.Clear();
            shownBangumiFacts = model.Bangumi; shownAniListFacts = model.AniList; RenderFacts();
        }
        if (!ReferenceEquals(shownBangumiFacts, model.Bangumi) || !ReferenceEquals(shownAniListFacts, model.AniList))
        { shownBangumiFacts = model.Bangumi; shownAniListFacts = model.AniList; RenderFacts(); }
        relations.Visibility = Visible(source.Identity.Provider == "Bangumi");
        relations.IsEnabled = ndl.IsEnabled = model.CanLoad;
        relationNotice.Text = model.Relations is { } r ? $"{r.Snapshot.Items.Count(x => x.Kind == BangumiRelationKind.Book)} 项关联 · Bangumi" + Cache(r.FromCache, r.IsStale, r.Warning)
            : "按 Bangumi 关系浏览系列、单行本和篇章。";
        if (!ReferenceEquals(shownRelations, model.Relations)) { shownRelations = model.Relations; RenderRelations(); }
        var refreshSaved = !ReferenceEquals(shownCandidates, model.Candidates)
            || !ReferenceEquals(shownBindings, model.Bindings) || !ReferenceEquals(shownPreview, model.Preview);
        if (!ReferenceEquals(shownCandidates, model.Candidates)) { shownCandidates = model.Candidates; RenderCandidates(); }
        shownBindings = model.Bindings;
        if (!ReferenceEquals(shownPreview, model.Preview)) { shownPreview = model.Preview; RenderPreview(); }
        if (refreshSaved) RenderSaved();
        foreach (var button in relationCards.Children.OfType<Button>().Concat(candidateCards.Children.OfType<Button>()).Concat(savedActions)) button.IsEnabled = model.CanLoad;
        search.IsEnabled = queryTitle.IsEnabled = queryIsbn.IsEnabled = refreshRelations.IsEnabled = model.CanLoad;
        next.Visibility = Visible(model.Search?.Page.NextStart is not null); next.IsEnabled = model.CanLoad;
        searchNotice.Text = model.Search is { } result ? $"显示 {model.Candidates.Length} / {result.Page.Total} 条出版记录" + Cache(result.FromCache, result.IsStale, result.Warning) : "";
        searchNotice.Visibility = Visible(searchNotice.Text.Length > 0);
        confirm.Visibility = refreshPreview.Visibility = confirmation.Visibility = Visible(model.Preview is not null);
        confirm.IsEnabled = model.CanConfirm; refreshPreview.IsEnabled = model.CanLoad;
        confirmation.Text = model.PreviewIsBound ? "已保存此出版版本。" : model.ConfirmationBlock;
        confirmation.Visibility = Visible(confirmation.Text.Length > 0 && model.Preview is not null);
    }
    private void RenderFacts()
    {
        facts.Children.Clear(); rawBody.Children.Clear(); if (model?.Source is not { } source) return;
        Fact(facts, "原标题", source.OriginalTitle); Fact(facts, "类型", FormatName(source.Format));
        Fact(rawBody, "数据来源", source.Identity.Provider); Fact(rawBody, "条目编号", source.Identity.Id); Fact(rawBody, "记录层级", LevelName(source.Identity.Level));
        if (model.Bangumi is { } bg)
        {
            var b = bg.BookFacts; Fact(facts, "发行日期", bg.ReleaseDate);
            if (b is not null)
            {
                Fact(facts, "作者 / 原作", b.Authors); Fact(facts, "作画 / 插画", b.Illustrators); Fact(facts, "出版社", b.Publishers); Fact(facts, "连载刊物", b.Magazines);
                Fact(facts, "文库 / 丛书", b.Imprints); Fact(facts, "卷次", b.Volume); Fact(facts, "卷 / 话数", string.Join(" · ", new[] { b.Volumes is > 0 ? $"{b.Volumes} 卷" : null, b.Chapters is > 0 ? $"{b.Chapters} 话" : null }.Where(x => x is not null))); Fact(facts, "ISBN", b.Isbns);
                if (b.RawFacts.Length > 0) rawBody.Children.Add(SectionTitle("Bangumi 原始字段"));
                foreach (var item in b.RawFacts) Fact(rawBody, item.Name, item.Values);
            }
            rawBody.Children.Add(SourceLink("Bangumi 书籍资料 ↗", bg.Url, false));
        }
        else
        {
            if (model.AniList is { } ani)
            {
                foreach (var group in ani.Staff.GroupBy(x => x.Role))
                    Fact(facts, RoleName(group.Key), group.Select(x => ContentLanguage.Person(x.Person, "Staff").Text).Distinct().ToArray());
                Fact(facts, "开始日期", ani.Media.StartDate); Fact(facts, "卷 / 话数", string.Join(" · ", new[] { ani.Media.Volumes is > 0 ? $"{ani.Media.Volumes} 卷" : null, ani.Media.Chapters is > 0 ? $"{ani.Media.Chapters} 话" : null }.Where(x => x is not null)));
                rawBody.Children.Add(SourceLink("AniList 书籍资料 ↗", ani.Media.Url, false));
                if (ani.Staff.Length > 0) rawBody.Children.Add(SectionTitle("人员职责原文"));
                foreach (var group in ani.Staff.GroupBy(x => x.Role))
                    Fact(rawBody, group.Key, group.Select(x => x.Person.NativeName.Length > 0 ? x.Person.NativeName : x.Person.Name).Distinct().ToArray());
            }
            rawBody.Children.Add(Text(13, "尚无已确认的 Bangumi 对应编号，因此只展示当前 AniList 条目的书籍资料。"));
        }
        rawBody.Children.Add(Text(12, "卷次与关系沿用来源标注；未标明的层级不按标题推断。"));
    }
    private void RenderRelations()
    {
        relationCards.Children.Clear();
        foreach (var item in model?.Relations?.Snapshot.Items.Where(x => x.Kind == BangumiRelationKind.Book) ?? [])
        {
            var button = Card(item.Title, "→ " + item.Relation + (item.OriginalTitle.Length > 0 && item.OriginalTitle != item.Title ? " · " + item.OriginalTitle : ""));
            AutomationProperties.SetAutomationId(button, "BookOpen-" + item.Id);
            button.Click += async (_, _) => await OpenRelatedVisualAsync(item.Id);
            relationCards.Children.Add(button);
        }
        Clamp(relationCards);
    }
    private void RenderCandidates()
    {
        candidateCards.Children.Clear();
        foreach (var item in model?.Candidates ?? [])
        {
            var description = string.Join(" · ", new[] { string.IsNullOrWhiteSpace(item.Volume) ? null : "卷次 " + item.Volume, string.IsNullOrWhiteSpace(item.PublicationDate) ? null : "出版 " + item.PublicationDate }.Where(x => x is not null));
            var button = Card(item.Title, description);
            if (button.Content is StackPanel card)
            {
                if (item.Publishers.Length > 0) card.Children.Add(Secondary(12, "出版社 · " + string.Join(" / ", item.Publishers)));
                if (item.Isbns.Length > 0) card.Children.Add(Secondary(12, "ISBN " + string.Join(" / ", item.Isbns)));
                if (item.IsAggregate) card.Children.Add(Text(12, "集合记录 · 仅供参考"));
            }
            ToolTipService.SetToolTip(button, item.Title + "\nNDL " + item.Id);
            AutomationProperties.SetAutomationId(button, "BookPreview-" + item.Id);
            button.Click += async (_, _) => await PreviewVisualAsync(item.Id); candidateCards.Children.Add(button);
        }
        Clamp(candidateCards);
    }
    private void RenderSaved()
    {
        savedBody.Children.Clear(); savedActions.Clear(); if (model?.Bindings.Length is not > 0) return;
        savedBody.Children.Add(SectionTitle($"已保存的出版版本 · {model.Bindings.Length}"));
        foreach (var binding in model.Bindings)
        {
            var row = new StackPanel { Spacing = 6 };
            var publication = model.Preview?.Publication is { } preview && preview.Id == binding.PublicationId ? preview : model.Candidates.FirstOrDefault(x => x.Id == binding.PublicationId);
            row.Children.Add(Text(14, publication?.Title ?? $"NDL 出版记录 {binding.PublicationId}"));
            if (binding.NeedsReview) row.Children.Add(Text(12, "来源资料或层级已变化 · 原关联保留，请重新核对"));
            if (publication is not null) row.Children.Add(Secondary(12, string.Join(" · ", new[] { publication.Volume, publication.PublicationDate, string.Join(" / ", publication.Publishers) }.Where(x => !string.IsNullOrWhiteSpace(x)))));
            var load = Button("查看版本"); var forget = Button("解除关联");
            AutomationProperties.SetAutomationId(load, "BookSaved-" + binding.PublicationId); AutomationProperties.SetAutomationId(forget, "BookForget-" + binding.PublicationId);
            ToolTipService.SetToolTip(load, $"NDL {binding.PublicationId}\n确认于 {binding.ConfirmedAtUtc.ToLocalTime():yyyy-MM-dd}");
            load.Click += async (_, _) => await PreviewVisualAsync(binding.PublicationId);
            forget.Click += async (_, _) => { if (model is not null) await model.ForgetAsync(binding.PublicationId); };
            savedActions.Add(load); savedActions.Add(forget);
            var buttons = Flow(); buttons.Children.Add(load); buttons.Children.Add(forget); row.Children.Add(buttons); savedBody.Children.Add(row);
        }
    }
    private void RenderPreview()
    {
        previewBody.Children.Clear(); if (model?.Preview is not { } result) return;
        var p = result.Publication; var localizedTitle = ContentLanguage.PublicationTitle(p);
        previewBody.Children.Add(SectionTitle("出版版本详情")); previewBody.Children.Add(Text(18, localizedTitle.Text));
        var languageInfo = new ContentLanguageInfo(); languageInfo.Show(localizedTitle); previewBody.Children.Add(languageInfo);
        if (p.IsAggregate) previewBody.Children.Add(Text(13, "集合 / 汇总记录 · 不可确认为独立出版物"));
        Fact(previewBody, "卷次 / 版次", string.Join(" · ", new[] { p.Volume, p.Edition }.Where(x => !string.IsNullOrWhiteSpace(x))));
        Fact(previewBody, "出版日期", p.PublicationDate); Fact(previewBody, "出版社", p.Publishers); Fact(previewBody, "文库 / 丛书", p.BibliographicSeries);
        Fact(previewBody, "作者等（原文）", p.CreatorStatements); Fact(previewBody, "ISBN", p.Isbns); Fact(previewBody, "套装 ISBN", p.SetIsbns); Fact(previewBody, "语言", p.Languages.Select(LanguageName).ToArray());
        var source = new StackPanel { Spacing = 8 }; Fact(source, "NDL 编号", p.Id); Fact(source, "记录类型", p.IsAggregate ? "集合 / 汇总记录" : "独立出版物");
        if (result.FromSearch) source.Children.Add(Text(12, "当前为检索结果；保存关联时会独立核验编号、版本与 ISBN。"));
        source.Children.Add(Text(12, "NDL · " + p.License)); source.Children.Add(SourceLink("NDL 原始记录 ↗", p.Url, true));
        var provenance = Expand("此版本的资料来源"); provenance.Content = source; previewBody.Children.Add(provenance);
        var warning = Cache(result.FromCache, result.IsStale, result.Warning).Trim(' ', '·');
        if (warning.Length > 0) previewBody.Children.Add(Text(13, warning));
    }
    private HyperlinkButton SourceLink(string text, string url, bool isNdl)
    {
        var link = new HyperlinkButton { Content = text, Padding = new(0), HorizontalAlignment = HorizontalAlignment.Left };
        link.Click += async (_, _) =>
        {
            var expected = model?.Source?.Fingerprint; var version = model?.Version;
            try
            {
                bool valid = isNdl ? model?.Preview?.Publication is { } p && BookValidation.NdlId(p.Id) && url == p.Url
                    : model?.Bangumi is { } b ? url == b.Url : model?.AniList is { } a && url == a.Media.Url;
                if (!valid || !await Windows.System.Launcher.LaunchUriAsync(new Uri(url))) throw new InvalidOperationException();
            }
            catch { if (!disposed && model?.Expanded == true && model.Source?.Fingerprint == expected && model.Version == version) { message.Text = "原始来源暂时无法打开，请稍后重试。"; message.Visibility = Visibility.Visible; } }
        }; return link;
    }
    private Task SearchAsync() => model?.SearchAsync(queryTitle.Text, queryIsbn.Text) ?? Task.CompletedTask;
    private async Task PreviewVisualAsync(string id)
    {
        if (model is not { } current) return;
        var source = current.Source?.Fingerprint; var operation = current.PreviewAsync(id); var version = current.Version;
        await operation;
        bool Accept() => !disposed && ReferenceEquals(model, current) && current.Version == version
            && current.Expanded && current.Source?.Fingerprint == source && current.Preview?.Publication.Id == id;
        if (Accept()) Bring(previewBody, Accept);
    }
    private async Task OpenRelatedVisualAsync(string id)
    {
        if (model is not { } current) return;
        var operation = current.OpenRelatedAsync(id); var version = current.Version;
        await operation;
        if (!disposed && ReferenceEquals(model, current) && current.Version == version && current.Expanded
            && current.Source?.Identity is { Provider: "Bangumi" } source && source.Id == id) FocusHeading();
    }
    private async Task NextAsync()
    {
        if (model is null) return;
        var source = model.Source?.Fingerprint; var previousCount = model.Candidates.Length;
        var operation = model.NextAsync(); var generation = model.Version;
        await operation;
        if (disposed || model.Version != generation || model.Source?.Fingerprint != source || model.Candidates.Length <= previousCount) return;
        var version = renderVersion;
        DispatcherQueue.TryEnqueue(() =>
        {
            if (disposed || model.Version != generation || model.Source?.Fingerprint != source || version != renderVersion || candidateCards.Children.ElementAtOrDefault(previousCount) is not Button target) return;
            target.Focus(FocusState.Programmatic); target.StartBringIntoView();
        });
    }
    public bool GoBack() { var result = model?.GoBack() == true; if (result) FocusHeading(); return result; }
    private void Bring(FrameworkElement element, Func<bool>? accept = null)
    {
        var version = renderVersion;
        DispatcherQueue.TryEnqueue(() => { if (!disposed && version == renderVersion && element.IsLoaded && (accept is null || accept()))
        { UpdateLayout(); element.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = false }); } });
    }
    private void FocusHeading() { var version = renderVersion; DispatcherQueue.TryEnqueue(() => { if (!disposed && version == renderVersion) { section.StartBringIntoView(); if (back.Visibility == Visibility.Visible) back.Focus(FocusState.Programmatic); else section.Focus(FocusState.Programmatic); } }); }
    public void Cancel() => model?.Cancel();
    public void Deactivate() { model?.Deactivate(); reading.Deactivate(); shownSource = null; renderVersion++; }
    public void Dispose() { if (disposed) return; Deactivate(); disposed = true; reading.Dispose(); if (model is not null) { model.PropertyChanged -= StateChanged; model.Dispose(); } }
    internal async Task FixtureAsync(string action, string? id)
    {
        if (model?.Source is null) return;
        switch (action)
        {
            case "bookExpand": await model.ExpandAsync(); break;
            case "bookCollapse": model.Collapse(); break;
            case "bookRelations": await model.ExpandAsync(); syncing = true; relations.IsExpanded = true; syncing = false; await model.LoadRelationsAsync(); break;
            case "bookOpen" when id is not null && relations.IsExpanded: await model.OpenRelatedAsync(id); break;
            case "bookSearch": await model.ExpandAsync(); ndl.IsExpanded = true; await SearchAsync(); break;
            case "bookPreview" when id is not null: await model.PreviewAsync(id); break;
            case "bookConfirm": await model.ConfirmAsync(); break;
            case "bookForget" when id is not null: await model.ForgetAsync(id); break;
            case "bookReload": await model.ReloadBindingsAsync(); break;
            case "bookNext": await NextAsync(); break;
            case "bookBack": GoBack(); break;
            case "bookCancel": model.Cancel(); break;
            case "bookInspect" when id is "relations": Bring(relations); return;
            case "bookInspect" when id is "candidates": Bring(candidateCards); return;
            case "bookInspect" when id is "preview": Bring(previewBody); return;
            case "bookInspect" when id is "saved": Bring(savedBody); return;
            case "bookInspect" when id is "search": ndl.IsExpanded = true; Bring(ndl); return;
        }
        if (action is "bookPreview" or "bookConfirm") Bring(previewBody);
        else if (action == "bookRelations") Bring(relations);
        else if (action == "bookSearch") Bring(candidateCards);
        else Bring(section);
    }
    internal object Evidence => new { source = model?.Source?.Identity, title = model?.Source?.Title, displayedTitle = heading.Text, chapterNotice = chapterNotice.Text, contentLanguage = ContentLanguage.Current, expanded = model?.Expanded, state = model?.State.ToString(), busy = model?.IsBusy,
        historyDepth = model?.HistoryDepth, status = model?.Message, incoming = model?.Incoming, canConfirm = model?.CanConfirm, confirmationBlock = model?.ConfirmationBlock,
        relationExpanded = relations.IsExpanded, relations = model?.Relations?.Snapshot.Items.Where(x => x.Kind == BangumiRelationKind.Book).Select(x => new { x.Id, x.Relation }).ToArray(),
        candidates = model?.Candidates.Select(x => new { x.Id, x.Title, x.Isbns, x.IsAggregate }).ToArray(), nextStart = model?.Search?.Page.NextStart,
        preview = model?.Preview?.Publication, previewFromSearch = model?.Preview?.FromSearch, saved = model?.Bindings.Select(x => x.PublicationId).ToArray(), bindingsLoaded = model?.BindingsLoaded, reading = reading.Evidence,
        candidateHitTargets = candidateCards.Children.OfType<Button>().Select(InteractiveCardEvidence.Hit).ToArray(), width = ActualWidth };
    private static string LevelName(BookEntityLevel value) => value switch { BookEntityLevel.Series => "系列", BookEntityLevel.Volume => "单卷", BookEntityLevel.Publication => "出版物", BookEntityLevel.Aggregate => "集合", _ => "层级未注明" };
    private static string FormatName(string value) => value switch { "MANGA" => "漫画", "NOVEL" => "小说", "ONE_SHOT" => "短篇漫画", _ => value };
    private static string LanguageName(string value) => value switch { "jpn" => "日语", "eng" => "英语", "chi" or "zho" => "中文", "kor" => "韩语", "fre" or "fra" => "法语", "ger" or "deu" => "德语", "und" => "未注明", _ => value };
    private static string RoleName(string value) => value switch { "Story" => "故事", "Art" => "作画", "Story & Art" or "Story and Art" => "故事 / 作画", "Original Creator" => "原作", "Illustration" or "Illustrations" => "插画", "Character Design" => "角色设计", "Translation" or "Translator" => "翻译", _ => value };
    private static string Cache(bool cached, bool stale, string? warning) => (stale ? " · 暂时显示上次资料" : "") + (string.IsNullOrWhiteSpace(warning) ? "" : " · " + warning);
    private void ArrangeSearchForm()
    {
        var narrow = searchForm.ActualWidth < 620;
        Grid.SetColumn(queryTitle, 0); Grid.SetRow(queryTitle, 0); Grid.SetColumnSpan(queryTitle, narrow ? 3 : 1);
        Grid.SetColumn(queryIsbn, narrow ? 0 : 1); Grid.SetRow(queryIsbn, narrow ? 1 : 0); Grid.SetColumnSpan(queryIsbn, narrow ? 2 : 1);
        Grid.SetColumn(search, 2); Grid.SetRow(search, narrow ? 1 : 0);
    }
    private static void Fact(StackPanel target, string name, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        var row = new Grid { ColumnSpacing = 14 };
        row.ColumnDefinitions.Add(new() { Width = new(96) }); row.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        var label = Secondary(13, name); var content = Text(14, value); content.MaxWidth = 850; content.HorizontalAlignment = HorizontalAlignment.Left;
        row.Children.Add(label); Grid.SetColumn(content, 1); row.Children.Add(content); target.Children.Add(row);
    }
    private static void Fact(StackPanel target, string name, string[] values) { if (values.Length > 0) Fact(target, name, string.Join(" / ", values)); }
    private static TextBlock Text(double size, string value = "") => new() { Text = value, FontSize = size, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
    private static TextBlock Secondary(double size, string value) { var text = Text(size, value); text.Opacity = .75; return text; }
    private static TextBlock SectionTitle(string value) { var text = Text(16, value); text.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold; return text; }
    private static Button Button(string value) => new() { Content = value, CornerRadius = new(6), MinHeight = 32, HorizontalAlignment = HorizontalAlignment.Left };
    private static Expander Expand(string value) => new() { Header = value, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch };
    private static TagFlowPanel Flow() => new() { HorizontalSpacing = 10, VerticalSpacing = 8, MaximumItemWidth = 340, CenterInlineItems = false };
    private static Button Card(string title, string subtitle)
    {
        var name = Text(15, title); name.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold; name.MaxLines = 3; name.TextTrimming = TextTrimming.CharacterEllipsis;
        var text = new StackPanel { Spacing = 6, IsHitTestVisible = false, Children = { name } };
        if (!string.IsNullOrWhiteSpace(subtitle)) text.Children.Add(Text(13, subtitle));
        var button = new Button { Content = text, Width = 330, MaxWidth = 330, Tag = 330d, CornerRadius = new(8), Padding = new(12), HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Top };
        ToolTipService.SetToolTip(button, title + (subtitle.Length == 0 ? "" : "\n" + subtitle));
        AutomationProperties.SetName(button, title + (subtitle.Length == 0 ? "" : "，" + subtitle));
        return button;
    }
    private static void Clamp(TagFlowPanel panel)
    {
        if (panel.ActualWidth <= 0) return;
        var columns = Math.Max(1, (int)((panel.ActualWidth + panel.HorizontalSpacing) / (330 + panel.HorizontalSpacing)));
        var width = (panel.ActualWidth - panel.HorizontalSpacing * (columns - 1)) / columns;
        panel.MaximumItemWidth = width;
        foreach (var child in panel.Children.OfType<FrameworkElement>())
            if (child.Tag is double) { child.MaxWidth = width; child.Width = width; }
    }
    private static Visibility Visible(bool value) => value ? Visibility.Visible : Visibility.Collapsed;
}
