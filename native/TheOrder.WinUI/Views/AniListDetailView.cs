using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using TheOrder.Application;
using TheOrder.Presentation;

namespace TheOrder.WinUI.Views;

// One outer scroller, no scrollable character/staff lists inside it. This view
// replaces the existing media body and owns only read-only exploration state.
public sealed class AniListDetailView : Grid, IDisposable
{
    private readonly CoverPresenter cover = new() { Width = 136, Height = 190, PreserveEntireImage = true, ImageCornerRadius = 8, VerticalAlignment = VerticalAlignment.Top };
    private readonly Grid heading = new() { ColumnSpacing = 20 };
    private readonly TextBlock title = Text(28), alternate = Text(14), type = Text(14), relation = Text(14), status = Text(14), selection = Text(13);
    private readonly DetailSynopsis synopsis = new(), bridgeSynopsis = new() { Heading = "" };
    private readonly TagFlowPanel facts = Flow(), actions = Flow(), candidates = Flow(360), characterCards = Flow(270), relatedCards = Flow(340), tags = Flow();
    private readonly StackPanel candidateSection = new() { Spacing = 12 }, sourceBody = new() { Spacing = 10 };
    private readonly TextBlock relatedNotice = Text(13, "确认对应作品后，可以继续浏览关联作品。");
    private readonly CreditColumnsPanel staffBody = new();
    private readonly Expander matchReview = Expand("核对条目信息");
    private string? shownReview;
    private readonly Dictionary<string, (bool Main, bool Other, bool OtherOpen)> synopsisStates = new();
    private string? synopsisIdentity;
    private readonly ProgressRing ring = new() { Width = 18, Height = 18 };
    private readonly Button cancel = Button("取消读取"), refresh = Button("刷新资料"), confirm = Button("确认使用此条目"), reject = Button("排除此条目"), choose = Button("选择其他条目"), search = Button("查找"), more = Button("更多角色与制作人员"), toCharacters = Button("角色与声优"), toStaff = Button("制作人员"), toRelations = Button("前后作");
    private readonly Button toEpisodes = Button("分集目录"), toTrailer = Button("PV"), toWatch = Button("观看渠道");
    private readonly Button retry = Button("重试");
    private readonly Button retryCover = Button("重试封面");
    private readonly Button addRelated = Button("添加相关作品"), savePending = Button("保存为待核验"), restoreAutomatic = Button("恢复自动选择");
    private readonly Expander userSelections = Expand("我的关联与待核验选择");
    private ContentDialog? associationDialog;
    private async Task<ContentDialogResult> ShowAssociationDialogAsync(ContentDialog dialog)
    {
        associationDialog = dialog;
        try { return await dialog.ShowAsync(); }
        finally { if (ReferenceEquals(associationDialog, dialog)) associationDialog = null; }
    }
    private readonly TextBox searchText = new() { PlaceholderText = "原文标题，或 AniList 作品链接", MaxLength = 2000, HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly Expander characters = Expand("角色与日语声优"), staff = Expand("制作人员"), genres = Expand("类型与标签"), related = Expand("前后作与其他关联"), sources = Expand("资料来源与对应关系");
    private readonly Expander bridgeSummary = Expand("Bangumi 简介");
    private readonly ScrollViewer scroll;
    private readonly StackPanel body = new() { Spacing = 22 };
    private readonly AnimeAvailabilityView availability = new();
    private readonly WatchLinksView watchLinks = new();
    private readonly BookPublicationView publications = new();
    private readonly BangumiEpisodesView episodeList = new();
    private string? episodeSource;
    private IBangumiSubjectDetailsReader? bookSubjects;
    private IBookBibliographyReader? bookReader;
    private IAniListReader? bookLinkReader;
    private IBangumiBookRelationsReader? bookRelations;
    private AniListDetailsViewModel? model; private IImagePipeline? images; private IRemoteCoverRegistry? registry;
    private Func<string, Task>? openBangumi; private string origin = "", label = "", evidence = "";
    private bool active, disposed; private long coverVersion, restored, scrollInputVersion; private string? requestedCover;
    private AniListResult[]? shownPages; private AniListMedia[]? shownCandidates;
    private int[]? shownRejected;
    private IBangumiWatchLinksReader? watchData; private WatchDataQuery? watchQuery; private WatchDataResult? watchResult;
    private CancellationTokenSource? watchStop; private long watchVersion;
    public AniListDetailView()
    {
        AutomationProperties.SetAutomationId(this, "AniListDetail"); AutomationProperties.SetAutomationId(title, "AniListDetailTitle");
        AutomationProperties.SetAutomationId(confirm, "AniListConfirm"); AutomationProperties.SetAutomationId(characters, "AniListCharacters");
        AutomationProperties.SetAutomationId(staff, "AniListStaff"); AutomationProperties.SetAutomationId(related, "AniListRelations");
        AutomationProperties.SetAutomationId(status, "AniListStatus"); AutomationProperties.SetAutomationId(searchText, "AniListSearch");
        title.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold; type.Opacity = alternate.Opacity = selection.Opacity = .75; relation.Opacity = .8;
        heading.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); heading.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        heading.Children.Add(cover); var labels = new StackPanel { Spacing = 9, Children = { type, title, alternate, facts, selection } }; SetColumn(labels, 1); heading.Children.Add(labels);
        confirm.Style = (Style)Microsoft.UI.Xaml.Application.Current.Resources["AccentButtonStyle"];
        actions.Children.Add(ring); actions.Children.Add(cancel); actions.Children.Add(retry); actions.Children.Add(confirm); actions.Children.Add(addRelated); actions.Children.Add(savePending); actions.Children.Add(reject); actions.Children.Add(choose); actions.Children.Add(toCharacters); actions.Children.Add(toStaff); actions.Children.Add(toRelations);
        AutomationProperties.SetAutomationId(addRelated, "AniListAddRelated"); AutomationProperties.SetAutomationId(savePending, "AniListSavePending");
        AutomationProperties.SetAutomationId(userSelections, "AniListUserSelections"); AutomationProperties.SetAutomationId(restoreAutomatic, "AniListRestoreAutomatic");
        AutomationProperties.SetAutomationId(retry, "AniListRetry");
        retryCover.Visibility = Visibility.Collapsed; actions.Children.Add(retryCover); AutomationProperties.SetAutomationId(retryCover, "AniListRetryCover");
        retryCover.Click += (_, _) => cover.RetryFailed();
        cover.SourceStateChanged += () => retryCover.Visibility = Visible(active && !cover.HasImage && cover.SourceKey is not null && cover.SourceStatus is not ("loading" or "not-started"));
        actions.Children.Add(toEpisodes); actions.Children.Add(toTrailer); actions.Children.Add(toWatch);
        foreach (var (control, id) in new[] { (toCharacters, "AniListJumpCharacters"), (toStaff, "AniListJumpStaff"), (toRelations, "AniListJumpRelations"), (toEpisodes, "AniListJumpEpisodes"), (toTrailer, "AniListJumpPV"), (toWatch, "AniListJumpWatch") })
            AutomationProperties.SetAutomationId(control, id);
        var searchRow = new Grid { ColumnSpacing = 8 }; searchRow.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); searchRow.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        searchRow.Children.Add(searchText); SetColumn(search, 1); searchRow.Children.Add(search);
        candidateSection.Children.Add(Text(20, "选择对应作品")); candidateSection.Children.Add(searchRow); candidateSection.Children.Add(candidates);
        characters.Content = characterCards; staff.Content = staffBody; genres.Content = tags; related.Content = new StackPanel { Spacing = 12, Children = { relatedNotice, relatedCards } }; sources.Content = sourceBody;
        bridgeSummary.Content = bridgeSynopsis;
        body.Children.Add(heading); body.Children.Add(relation); body.Children.Add(status); body.Children.Add(actions); body.Children.Add(userSelections); body.Children.Add(watchLinks); body.Children.Add(availability); body.Children.Add(candidateSection);
        body.Children.Insert(4, matchReview);
        body.Children.Add(synopsis); body.Children.Add(languageInfo); body.Children.Add(bridgeSummary); body.Children.Add(episodeList); body.Children.Add(publications); body.Children.Add(genres); body.Children.Add(characters); body.Children.Add(staff); body.Children.Add(more); body.Children.Add(related); body.Children.Add(sources);
        scroll = new() { Content = body, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalContentAlignment = HorizontalAlignment.Stretch }; Children.Add(scroll);
        // Observe even natively handled wheel input so a delayed navigation restore
        // cannot overwrite a user's scroll. Do not implement a second scroll path.
        scroll.AddHandler(UIElement.PointerWheelChangedEvent, new PointerEventHandler((_, _) => scrollInputVersion++), true);
        scroll.PointerPressed += (_, _) => scrollInputVersion++; scroll.KeyDown += (_, _) => scrollInputVersion++;
        cancel.Click += (_, _) => model?.Cancel(); refresh.Click += async (_, _) => await RefreshAsync();
        retry.Click += async (_, _) => await RefreshAsync();
        confirm.Click += async (_, _) =>
        {
            if (model?.CanConfirm != true) return;
            var selectedModel = model; var selectedSource = model.Source; var selectedMedia = model.Media;
            var assessment = AniListMatching.Assess(model.Source!, model.Displayed!.Details.Media);
            if (assessment.Conflict || assessment.Evidence.Any(x => x.Contains("不同", StringComparison.Ordinal) || x.StartsWith("请核对", StringComparison.Ordinal)))
            {
                var dialog = new ContentDialog { XamlRoot = XamlRoot, RequestedTheme = ActualTheme, Title = "核对差异后关联？",
                    Content = string.Join("\n", assessment.Evidence) + "\n\n保存为你的明确选择，个人标题、封面及笔记会保留。",
                    PrimaryButtonText = "仍要关联", CloseButtonText = "取消", DefaultButton = ContentDialogButton.Close };
                if (await ShowAssociationDialogAsync(dialog) != ContentDialogResult.Primary) return;
            }
            if (ReferenceEquals(model, selectedModel) && model.Source == selectedSource && model.Media == selectedMedia && model.CanConfirm) await model.ConfirmAsync();
        };
        addRelated.Click += async (_, _) =>
        {
            if (model?.CanAddRelated != true) return;
            var selectedModel = model; var source = model.Source; var target = model.Media;
            var note = new TextBox { Header = "说明（可选）", MaxLength = 1000, TextWrapping = TextWrapping.Wrap, AcceptsReturn = true };
            var dialog = new ContentDialog { XamlRoot = XamlRoot, RequestedTheme = ActualTheme, Title = "添加为相关作品？",
                Content = new StackPanel { Spacing = 12, Children = { Text(14, $"《{source?.Title}》 → 《{target?.Title}》\n关系：相关作品。两个条目的身份、简介与出版资料分别保留。"), note } },
                PrimaryButtonText = "添加相关作品", CloseButtonText = "取消", DefaultButton = ContentDialogButton.Close };
            if (await ShowAssociationDialogAsync(dialog) == ContentDialogResult.Primary && ReferenceEquals(model, selectedModel) && model.Source == source && model.Media == target && model.CanAddRelated)
                await model.AddRelatedAsync(note.Text);
        };
        savePending.Click += async (_, _) => { if (model?.CanSavePending == true) await model.SavePendingAsync(); };
        restoreAutomatic.Click += async (_, _) => { if (model?.CanRestoreAutomatic == true) await model.RestoreAutomaticAsync(); };
        AutomationProperties.SetAutomationId(reject, "AniListReject");
        ToolTipService.SetToolTip(reject, "取消关联并排除自动匹配；仍可手动核对后重新确认。");
        reject.Click += async (_, _) => { if (model?.RelatedSelection == true && model.Media is { } media) await model.RemoveUserLinkAsync(MediaIdentityDecisionRules.Target(media)); else if (model?.CanReject == true) await model.RejectAsync(); else if (model is not null) await model.RevokeAsync(); };
        choose.Click += async (_, _) => { if (model is not null) { await model.ChooseAgainAsync(); candidateSection.StartBringIntoView(); } }; more.Click += async (_, _) => { if (model is not null) await model.MoreAsync(); };
        toCharacters.Click += (_, _) => ShowSection(characters); toStaff.Click += (_, _) => ShowSection(staff); toRelations.Click += (_, _) => ShowSection(related);
        toEpisodes.Click += async (_, _) => await ShowEpisodesAsync();
        toTrailer.Click += (_, _) => { availability.Expand(); availability.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = false, VerticalAlignmentRatio = .05 }); };
        toWatch.Click += (_, _) => watchLinks.Expand();
        foreach (var panel in new[] { candidates, characterCards, relatedCards }) panel.SizeChanged += (_, _) => ClampCards(panel);
        search.Click += async (_, _) => await SearchAsync(); searchText.KeyDown += async (_, e) => { if (e.Key == Windows.System.VirtualKey.Enter) { e.Handled = true; await SearchAsync(); } };
        SizeChanged += (_, _) => { var narrow = ActualWidth < 520; cover.Width = narrow ? 96 : 136; cover.Height = narrow ? 136 : 190; title.FontSize = narrow ? 23 : 28; heading.ColumnSpacing = narrow ? 12 : 20; };
        Unloaded += (_, _) => { if (!IsLoaded) Deactivate(); };
    }
    public void Configure(IBangumiSubjectDetailsReader subjects, IAniListReader reader, IImagePipeline pipeline, IRemoteCoverRegistry? coverRegistry, Func<string, Task> bangumi, IWatchLinkPreferencesStore? watchPreferences = null, IBangumiWatchLinksReader? watchReader = null)
    {
        Deactivate(); if (model is not null) { model.PropertyChanged -= StateChanged; model.Dispose(); }
        images = pipeline; registry = coverRegistry; openBangumi = bangumi; cover.Pipeline = images;
        watchLinks.Configure(watchPreferences);
        watchData = watchReader;
        bookSubjects = subjects;
        bookLinkReader = reader;
        if (bookReader is not null && bookRelations is not null) publications.Configure(subjects, bookReader, bookRelations, reader);
        model = new(subjects, reader); model.PropertyChanged += StateChanged;
    }
    public void ConfigureBooks(IBookBibliographyReader books, IBangumiBookRelationsReader relations)
    {
        bookReader = books; bookRelations = relations;
        if (bookSubjects is not null) publications.Configure(bookSubjects, books, relations, bookLinkReader);
        if (active && !disposed) Render();
    }
    public void ConfigureEpisodes(IBangumiEpisodesReader episodes) => episodeList.Configure(episodes);
    public Task OpenAsync(BangumiRelationItem item, string originTitle, string? relationLabel, string? relationEvidence, CancellationToken cancellation)
    {
        active = true; origin = originTitle; label = relationLabel ?? item.Relation; evidence = relationEvidence ?? ""; shownPages = null; shownCandidates = null;
        availability.Stop();
        publications.Deactivate();
        watchLinks.Show(null, []);
        requestedCover = null; cover.SourceKey = null; searchText.Text = ""; restored = 0; shownReview = null;
        synopsisStates.Clear(); synopsisIdentity = null; synopsis.Expanded = bridgeSynopsis.Expanded = bridgeSummary.IsExpanded = false;
        foreach (var section in new[] { characters, staff, genres, related, sources }) section.IsExpanded = false;
        scroll.ChangeView(null, 0, null); return model?.OpenAsync(item, cancellation) ?? Task.CompletedTask;
    }
    private void StateChanged(object? sender, PropertyChangedEventArgs e) { if (active && !disposed) Render(); }
    private readonly ContentLanguageInfo languageInfo = new();
    private void Render()
    {
        var details = model?.Displayed?.Details; var media = model?.Media; var bridge = model?.IsBridge == true ? model?.Bridge?.Details : null;
        if (model?.IsBusy == true) availability.Stop();
        var isAnime = media is not null ? media.Kind == AniListMediaKind.Anime : model?.Item?.Kind == BangumiRelationKind.Anime;
        var preview = model?.HasUnconfirmedPreview == true;
        // A Bangumi-only page may use its own identity. Combining it with an
        // AniList identity requires the view model's explicit verified bridge.
        var useBangumiSource = model?.IsBridge == true && !preview && (media is null || model?.HasTrustedBridge == true);
        var episodeId = useBangumiSource && bridge?.Kind == BangumiRelationKind.Anime ? bridge.Id : null;
        if (episodeSource != episodeId) { episodeSource = episodeId; episodeList.SetSource(episodeId); }
        // An internal AniList relation has no established Bangumi identity. A
        // candidate preview also cannot reuse the starting bridge's book ID.
        publications.SetSource(bridge, details, useBangumiSource, linksAllowed: !preview && model?.IsBusy != true);
        var watchIdentity = isAnime ? new AnimeWatchIdentity(media?.Id, useBangumiSource ? model?.Item?.Id : null) : null;
        var query = watchIdentity is null || preview || model?.IsBusy == true ? null : new WatchDataQuery(watchIdentity, media?.Format ?? bridge?.Platform, media?.StartDate ?? bridge?.ReleaseDate);
        if (watchQuery != query) { CancelWatchRead(); watchQuery = query; watchResult = null; if (query is not null && watchData is not null) { watchStop = new(); _ = ReadWatchAsync(query, ++watchVersion, watchStop.Token); } }
        var linkData = watchIdentity is null ? [] : AniListWatchLinksAdapter.From(watchIdentity, details).Concat(watchResult?.Links ?? []).ToArray();
        watchLinks.Show(watchIdentity, linkData, watchResult); availability.Show(media?.Id, isAnime, details?.Trailer, model?.CanLoad == true);
        type.Text = media is null ? (model?.Item?.Kind == BangumiRelationKind.Anime ? "动画" : "书籍") + " · Bangumi" : media.TypeName + " · AniList";
        var localizedTitle = media is not null ? ContentLanguage.AniListTitle(media, bridge, useBangumiSource) : bridge is not null ? ContentLanguage.BangumiTitle(bridge) : null;
        title.Text = localizedTitle?.Text is { Length: > 0 } chosenTitle ? chosenTitle : media?.Title ?? bridge?.Title ?? model?.Item?.Title ?? "关联作品";
        alternate.Text = string.Join(" / ", (localizedTitle?.Variants.Select(x => x.Text) ?? []).Where(x => !string.IsNullOrWhiteSpace(x) && x != title.Text).Distinct());
        alternate.Visibility = Visible(alternate.Text.Length > 0); selection.Text = model?.State == AniListDetailState.Preview ? "预览 · 尚未确认对应作品" : model?.SelectionReason.StartsWith("用户核对", StringComparison.Ordinal) == true ? "已确认对应作品" : model?.SelectionReason ?? ""; selection.Visibility = Visible(selection.Text.Length > 0);
        facts.Children.Clear();
        if (media is not null)
        {
            AddFact(Format(media.Format)); AddFact(Status(media.Status, isAnime)); if (media.StartDate is { } start) AddFact((isAnime ? "首播 · " : "开始 · ") + start);
            if (media.Episodes is { } eps) AddFact(eps + " 集"); if (media.Chapters is { } chapters) AddFact(chapters + " 话"); if (media.Volumes is { } volumes) AddFact(volumes + " 卷");
            if (details?.Studios.Length > 0) AddFact("制作 · " + string.Join(" / ", details.Studios));
        }
        else { if (bridge?.ReleaseDate is { } date) AddFact(date); if (bridge?.Platform is { } platform) AddFact(platform); }
        relation.Text = model?.IncomingOrigin is { } previous ? $"与《{previous}》的关联 · {Relation(model.IncomingRelation ?? "OTHER")}" : $"与《{origin}》的关联 · {label}";
        if (model?.RelatedSelection == true && model.Source is { } relatedSource) relation.Text = $"与《{relatedSource.Title}》的关联 · 用户添加的相关作品";
        status.Text = model?.Message ?? "";
        RenderMatchReview(media);
        status.Visibility = Visible(status.Text.Length > 0); ring.IsActive = model?.IsBusy == true; ring.Visibility = cancel.Visibility = Visible(ring.IsActive);
        retry.Visibility = Visible(model?.State is AniListDetailState.Failed or AniListDetailState.Cancelled); retry.IsEnabled = model?.CanLoad == true;
        confirm.Visibility = Visible(model?.State == AniListDetailState.Preview); confirm.IsEnabled = model?.CanConfirm == true;
        confirm.Content = model?.CanConfirm == true && model.Source is { } manualSource && media is not null && AniListMatching.Assess(manualSource, media).Conflict ? "仍要关联此条目" : "确认对应条目";
        addRelated.Visibility = Visible(model?.State == AniListDetailState.Preview || model?.RelatedSelection == true); addRelated.IsEnabled = model?.CanAddRelated == true;
        addRelated.Style = model?.CanConfirm == true ? null : (Style)Microsoft.UI.Xaml.Application.Current.Resources["AccentButtonStyle"];
        savePending.Visibility = Visible(model?.PendingReference is not null && details is null); savePending.IsEnabled = model?.CanSavePending == true;
        RenderUserSelections();
        reject.Visibility = Visible(model?.IsBridge == true && (details is not null || model?.SavedUserTargetId is not null));
        reject.IsEnabled = model?.CanReject == true || model?.CanRevoke == true;
        reject.Content = model?.RelatedSelection == true ? "移除相关作品" : details is null || model?.State == AniListDetailState.Ready ? "取消关联" : "排除此条目";
        choose.Visibility = Visible(model?.IsBridge == true && details is not null); choose.IsEnabled = model?.CanLoad == true;
        foreach (var button in new[] { toCharacters, toStaff, toRelations }) { button.Visibility = Visible(details is not null); button.IsEnabled = model?.CanLoad == true; }
        toCharacters.Content = isAnime ? "角色与声优" : "角色"; toStaff.Content = isAnime ? "制作人员" : "作者与制作"; toRelations.Content = "关联作品";
        toEpisodes.Visibility = Visible(episodeId is not null); toEpisodes.IsEnabled = model?.CanLoad == true;
        toTrailer.Visibility = Visible(isAnime && details?.Trailer is not null); toTrailer.IsEnabled = model?.CanLoad == true;
        toWatch.Visibility = Visible(isAnime); toWatch.IsEnabled = model?.CanLoad == true && !preview;
        actions.Visibility = Visible(actions.Children.OfType<FrameworkElement>().Any(x => x.Visibility == Visibility.Visible));
        candidateSection.Visibility = Visible(model?.IsBridge == true && details is null && model?.Source is not null);
        search.IsEnabled = searchText.IsEnabled = refresh.IsEnabled = model?.CanLoad == true;
        // Only an established bridge can supply the displayed work's primary synopsis.
        // A mismatched preview keeps the original subject clearly separate.
        var localizedSynopsis = ContentLanguage.Synopsis(details, bridge, useBangumiSource);
        var bridgePrimary = localizedSynopsis.Selected?.Identity.Provider == "Bangumi";
        var synopsisKey = media is not null ? "anilist/" + media.Id + "/" + preview : "bangumi/" + bridge?.Id;
        var previousState = (Main: synopsis.Expanded, Other: bridgeSynopsis.Expanded, OtherOpen: bridgeSummary.IsExpanded);
        if (synopsisIdentity != synopsisKey && synopsisIdentity is not null)
        {
            synopsisStates[synopsisIdentity] = previousState;
            if (synopsisStates.Count > 32) synopsisStates.Remove(synopsisStates.Keys.First());
        }
        var restoredSynopsis = synopsisIdentity == synopsisKey ? previousState : synopsisStates.GetValueOrDefault(synopsisKey);
        synopsisIdentity = synopsisKey;
        synopsis.Text = localizedSynopsis.Text.Length > 0 ? localizedSynopsis.Text : "原站暂未提供简介。";
        // A failed/unloaded source is not an authoritative empty synopsis.
        synopsis.Visibility = Visible(details is not null || bridge is not null);
        languageInfo.Show(localizedTitle ?? ContentLanguage.Select([]), localizedSynopsis,
            media is null ? ContentLanguage.Select([]) : ContentLanguage.Aliases(new("AniList", media.Kind.ToString(), media.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)), media.Synonyms));
        synopsis.Heading = localizedSynopsis.Selected is null ? "简介" : "简介 · " + localizedSynopsis.Selected.Identity.Provider;
        bridgeSynopsis.Text = bridgePrimary ? details?.Description ?? "" : model?.IsBridge == true ? bridge?.Summary ?? "" : "";
        bridgeSummary.Header = bridgePrimary ? "其他简介 · AniList 原文" : preview ? "原条目简介 · Bangumi" : "其他简介 · Bangumi";
        bridgeSummary.Visibility = Visible(bridgeSynopsis.Text.Length > 0 && bridgeSynopsis.Text != synopsis.Text);
        synopsis.Expanded = restoredSynopsis.Main; bridgeSynopsis.Expanded = restoredSynopsis.Other; bridgeSummary.IsExpanded = restoredSynopsis.OtherOpen;
        var bridgeCover = useBangumiSource ? bridge?.CoverUrl ?? model?.Item?.CoverUrl : null;
        var coverUrl = media?.CoverUrl ?? bridgeCover;
        SetCover(coverUrl, bridgeCover);
        if (!ReferenceEquals(shownCandidates, model?.Candidates) || !ReferenceEquals(shownRejected, model?.RejectedIds))
        { shownCandidates = model?.Candidates; shownRejected = model?.RejectedIds; RenderCandidates(); }
        if (!ReferenceEquals(shownPages, model?.PeoplePages)) { shownPages = model?.PeoplePages; RenderDetails(details); }
        foreach (var button in relatedCards.Children.OfType<Button>()) button.IsEnabled = model?.CanLoad == true && model?.State == AniListDetailState.Ready;
        relatedNotice.Visibility = Visible(preview);
        more.Visibility = Visible(model?.PeoplePages.LastOrDefault()?.Details is { } page && (page.MoreCharacters || page.MoreStaff)); more.IsEnabled = model?.CanMore == true;
        RenderSources(media);
        if (model is not null && !model.IsBusy && restored != model.RestoreVersion)
        {
            restored = model.RestoreVersion; var target = model.RestoreOffset;
            var expectedId = model.Media?.Id; var restoreVersion = model.RestoreVersion; var inputVersion = scrollInputVersion;
            DispatcherQueue.TryEnqueue(async () =>
            {
                for (int i = 0; i < 3; i++)
                {
                    if (!active || disposed || model is null || model.Media?.Id != expectedId || model.RestoreVersion != restoreVersion || scrollInputVersion != inputVersion) return;
                    scroll.UpdateLayout(); scroll.ChangeView(null, target, null, true);
                    await Task.Delay(60);
                }
            });
        }
    }
    private async Task ReadWatchAsync(WatchDataQuery query, long version, CancellationToken cancellation)
    {
        try
        {
            var result = await watchData!.ReadAsync(query, cancellation);
            if (!AcceptWatch(version, query, cancellation)) return; watchResult = result; Render();
            if (await watchData.UpdateIfDueAsync(cancellation))
            {
                result = await watchData.ReadAsync(query, cancellation);
                if (AcceptWatch(version, query, cancellation)) { watchResult = result; Render(); }
            }
        }
        catch (OperationCanceledException) { }
        catch { if (AcceptWatch(version, query, cancellation)) { watchResult = new([], WatchDataMatch.NotRecorded, "放送数据暂不可读取，已保留其他来源的链接。", ""); Render(); } }
    }
    private bool AcceptWatch(long version, WatchDataQuery query, CancellationToken cancellation) => active && !disposed && !cancellation.IsCancellationRequested && version == watchVersion && watchQuery == query;
    private void CancelWatchRead() { watchVersion++; watchStop?.Cancel(); watchStop?.Dispose(); watchStop = null; watchQuery = null; }
    private void RenderMatchReview(AniListMedia? media)
    {
        var source = model?.Source;
        var show = model?.State == AniListDetailState.Preview && source is not null && media is not null;
        matchReview.Visibility = Visible(show);
        if (!show) return;
        var assessment = AniListMatching.Assess(source!, media!);
        // Replace only the ordinary assessment message, never a network/save failure.
        if (status.Text == string.Join(" · ", assessment.Evidence))
            status.Text = model?.CanConfirm == true ? "请核对下方差异；确认后保存为你的明确选择。" : "媒体、形式或层级不同，可添加为相关作品；两者资料分别保留。";
        var key = source!.Fingerprint + "/" + media!.Id + "/" + media.Title + "/" + media.Format + "/" + media.StartDate + "/" + string.Join("|", assessment.Evidence);
        if (shownReview == key) return;
        shownReview = key;
        matchReview.Header = assessment.Conflict ? "条目信息有冲突 · 查看差异" : "核对条目信息";
        var comparison = new StackPanel { Spacing = 12 };
        var columns = new CreditColumnsPanel();
        void Add(string provider, string name, string format, string? date)
        {
            var caption = Text(12, provider); caption.Opacity = .75;
            var heading = Text(15, name); heading.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
            var card = new StackPanel { Spacing = 6, Children = { caption, heading, Text(13, format), Text(13, "记录日期 · " + (date ?? "未提供")) } };
            columns.Children.Add(new Border { Style = (Style)Microsoft.UI.Xaml.Application.Current.Resources["CatalogCreditCardStyle"], Padding = new(12), Child = card });
        }
        Add("Bangumi · 原条目", source.Title, source.Platform ?? "未提供形式", source.Date);
        Add("AniList · 候选", media.Title, Format(media.Format), media.StartDate);
        comparison.Children.Add(columns);
        var raw = new StackPanel { Spacing = 6 };
        foreach (var item in assessment.Evidence) raw.Children.Add(Text(13, item));
        var original = Expand("完整匹配依据"); original.Content = raw; comparison.Children.Add(original);
        matchReview.Content = comparison; matchReview.IsExpanded = assessment.Conflict;
    }
    private void RenderUserSelections()
    {
        if (userSelections.Content is StackPanel old) old.Children.Clear();
        var panel = new StackPanel { Spacing = 10 };
        foreach (var link in model?.UserLinks?.Items ?? [])
        {
            var row = new StackPanel { Spacing = 5 };
            row.Children.Add(Text(14, link.Title));
            row.Children.Add(Text(12, $"{link.Target.Provider} · {link.Target.Resource} · {link.Target.Id} · " + (link.State == MediaUserLinkState.Pending ? "待核验（不融合资料）" : "相关作品 → 独立条目")));
            if (link.Note.Length > 0) row.Children.Add(Text(12, link.Note));
            var commands = Flow(); var open = Button(link.State == MediaUserLinkState.Pending ? "重试查看" : "查看作品"); var remove = Button("移除关联");
            open.IsEnabled = remove.IsEnabled = model?.CanLoad == true;
            AutomationProperties.SetAutomationId(open, "AniListUserLink-" + link.Target.Id);
            open.Click += async (_, _) => { if (model is not null) await model.OpenUserLinkAsync(link); };
            remove.Click += async (_, _) => { if (model is not null) await model.RemoveUserLinkAsync(link.Target); };
            commands.Children.Add(open); commands.Children.Add(remove); row.Children.Add(commands); panel.Children.Add(row);
        }
        restoreAutomatic.Visibility = Visible(model?.CanRestoreAutomatic == true); restoreAutomatic.IsEnabled = model?.CanRestoreAutomatic == true;
        panel.Children.Add(restoreAutomatic); userSelections.Content = panel;
        userSelections.Visibility = Visible(model?.IsBridge == true && (model.UserLinks?.Items.Length > 0 || model.CanRestoreAutomatic));
    }
    private void RenderCandidates()
    {
        candidates.Children.Clear();
        foreach (var candidate in (model?.Candidates ?? []).OrderByDescending(x => model?.Source?.ExpectedFormat == x.Format))
        {
            var factsText = string.Join(" · ", new[] { Format(candidate.Format), candidate.StartDate, candidate.Episodes is { } count ? count + " 集" : null, candidate.Volumes is { } volumes ? volumes + " 卷" : null, candidate.Chapters is { } chapters ? chapters + " 话" : null }.Where(x => x is not null));
            var body = new StackPanel { Spacing = 6, Children = { Text(16, candidate.Title), Text(13, candidate.NativeTitle), Text(13, factsText), Text(12, "AniList · " + candidate.Id + " · 查看并关联") } };
            var assessment = model?.Source is { } source ? AniListMatching.Assess(source, candidate) : null;
            if (assessment?.Conflict == true) body.Children.Add(Text(13, "证据有差异，请先核对"));
            if (model?.RejectedIds.Contains(candidate.Id) == true) body.Children.Add(Text(13, "已排除自动关联 · 可手动核对"));
            var button = Card(body, 330); AutomationProperties.SetAutomationId(button, "AniListCandidate-" + candidate.Id);
            button.Click += async (_, _) => { if (model?.CanLoad == true) { await model.PreviewAsync(candidate.Id); scroll.ChangeView(null, 0, null, true); } };
            candidates.Children.Add(button);
        }
        ClampCards(candidates);
    }
    private void RenderDetails(AniListDetails? details)
    {
        characterCards.Children.Clear(); staffBody.Children.Clear(); tags.Children.Clear(); relatedCards.Children.Clear();
        foreach (var section in new[] { characters, staff, genres, related }) section.Visibility = Visible(details is not null);
        if (details is null) return;
        foreach (var value in details.Genres.Concat(details.Tags).Distinct().Select(ContentLanguage.Term).Distinct()) tags.Children.Add(new Border { Background = (Microsoft.UI.Xaml.Media.Brush)Microsoft.UI.Xaml.Application.Current.Resources["CardBackgroundFillColorDefaultBrush"], CornerRadius = new(5), Padding = new(8, 4, 8, 4), Child = Text(13, value) });
        genres.Header = $"类型与标签 · {tags.Children.Count}";
        var allCharacters = (model?.PeoplePages ?? []).SelectMany(x => x.Details.Characters).DistinctBy(x => x.Person.Id).ToArray();
        var anime = details.Media.Kind == AniListMediaKind.Anime;
        characters.Header = $"{(anime ? "角色与日语声优" : "角色")} · {allCharacters.Length}";
        foreach (var character in allCharacters)
        {
            var image = Picture(character.Person.ImageUrl, 54, 76);
            var labels = new StackPanel { Spacing = 5, Children = { Text(15, ContentLanguage.Person(character.Person, "Character").Text), Text(12, Role(character.Role)) } };
            if (character.Person.NativeName.Length > 0 && ContentLanguage.Person(character.Person, "Character").Text != character.Person.NativeName) labels.Children.Add(Text(12, character.Person.NativeName));
            if (anime) labels.Children.Add(Text(13, VoiceCaption(character)));
            characterCards.Children.Add(Frame(Row(image, labels), 255));
        }
        var allStaff = (model?.PeoplePages ?? []).SelectMany(x => x.Details.Staff).DistinctBy(x => (x.Person.Id, x.Role)).ToArray(); staff.Header = $"{(anime ? "制作人员" : "作者与制作")} · {allStaff.Length}";
        foreach (var group in allStaff.GroupBy(x => x.Role))
        {
            var role = Text(12, StaffRole(group.Key)); role.Opacity = .75;
            var card = new StackPanel { Spacing = 6, Children = { role, Text(15, string.Join(" / ", group.Select(x => PersonName(x.Person)))) } };
            var nativeNames = string.Join(" / ", group.Select(x => x.Person.NativeName).Where(x => x.Length > 0).Distinct());
            if (nativeNames.Length > 0 && nativeNames != string.Join(" / ", group.Select(x => PersonName(x.Person)))) card.Children.Add(Text(12, nativeNames));
            staffBody.Children.Add(new Border { Style = (Style)Microsoft.UI.Xaml.Application.Current.Resources["CatalogCreditCardStyle"], Child = card, Padding = new(12) });
        }
        related.Header = $"关联作品 · {details.Relations.Length}";
        foreach (var item in details.Relations.OrderBy(x => x.Relation is "SEQUEL" or "PREQUEL" ? 0 : x.Relation == "ADAPTATION" ? 1 : 2))
        {
            var labels = new StackPanel { Spacing = 5, Children = { Text(16, ContentLanguage.AniListTitle(item.Media).Text), Text(13, Relation(item.Relation) + " · " + item.Media.TypeName
                + (item.Media.Format.Length == 0 ? " · 发行形式待定" : "")) } };
            var button = Card(Row(Picture(item.Media.CoverUrl, 48, 68), labels), 320); AutomationProperties.SetAutomationId(button, "AniListRelation-" + item.Media.Id);
            button.Click += async (_, _) => { if (model is not null) { await model.ExploreAsync(item, scroll.VerticalOffset); scroll.ChangeView(null, 0, null, true); } }; relatedCards.Children.Add(button);
        }
        ClampCards(characterCards); ClampCards(relatedCards);
    }
    private void RenderSources(AniListMedia? media)
    {
        sourceBody.Children.Clear();
        sourceBody.Children.Add(Text(14, model?.IsBridge == true ? "作品发现与原始关系 · Bangumi" : "当前作品关系 · AniList"));
        if (media is not null) { sourceBody.Children.Add(Text(14, "简介、角色与制作资料 · AniList")); sourceBody.Children.Add(Link("在 AniList 查看此作品", media.Url, true)); }
        if (model?.Item is { } item) sourceBody.Children.Add(Link(model.IsBridge ? "Bangumi 原始条目" : "起始 Bangumi 条目", item.Url, false));
        if (model?.IsBridge == true && model.Source is { } source && media is not null)
        {
            var raw = new StackPanel { Spacing = 8 };
            foreach (var itemEvidence in AniListMatching.Assess(source, media).Evidence) raw.Children.Add(Text(13, itemEvidence));
            if (evidence.Length > 0) raw.Children.Add(Text(13, "原始关系 · " + evidence));
            foreach (var url in source.ExternalLinks) raw.Children.Add(Text(13, "外部编号证据 · " + url));
            sourceBody.Children.Add(new Expander { Header = "对应证据", HorizontalAlignment = HorizontalAlignment.Stretch, Content = raw });
        }
        sourceBody.Children.Add(refresh);
    }
    private HyperlinkButton Link(string name, string url, bool aniList)
    {
        var link = new HyperlinkButton { Content = name, Padding = new(0), HorizontalAlignment = HorizontalAlignment.Left };
        link.Click += async (_, _) => { try { if (aniList) { if (!AniListReference.TryParse(url, out var parsed) || parsed!.MyAnimeList || !await Windows.System.Launcher.LaunchUriAsync(new Uri(url))) throw new InvalidOperationException(); } else if (openBangumi is not null) await openBangumi(url); } catch { if (active) { status.Text = "原站暂时无法打开，请稍后重试。"; status.Visibility = Visibility.Visible; } } }; return link;
    }
    private void SetCover(string? url, string? fallback)
    {
        if (requestedCover == url) return; requestedCover = url; var version = ++coverVersion;
        cover.SourceKey = registry?.Register(url, "anilist-cover-v1");
        if (url is null || url == fallback || fallback is null) return;
        DispatcherQueue.TryEnqueue(async () => { await cover.PendingImage; if (active && !disposed && coverVersion == version && !cover.HasImage && cover.SourceStatus is not ("loading" or "not-started")) cover.SourceKey = registry?.Register(fallback, "bangumi-anilist-fallback"); });
    }
    private CoverPresenter Picture(string? url, double width, double height) => new() { Width = width, Height = height, PreserveEntireImage = true, ImageCornerRadius = 5, Pipeline = images, SourceKey = registry?.Register(url, "anilist-person-v1"), VerticalAlignment = VerticalAlignment.Top };
    private static Grid Row(FrameworkElement image, FrameworkElement labels) { var row = new Grid { ColumnSpacing = 12 }; row.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); row.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); row.Children.Add(image); SetColumn(labels, 1); row.Children.Add(labels); return row; }
    private static Button Card(UIElement child, double width)
    {
        child.IsHitTestVisible = false;
        return new() { Content = child, Width = width, MaxWidth = width, Tag = width, CornerRadius = new(8), Padding = new(12), HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Top };
    }
    private static Border Frame(UIElement child, double width) => new() { Child = child, Width = width, MaxWidth = width, Tag = width, Style = (Style)Microsoft.UI.Xaml.Application.Current.Resources["CatalogCreditCardStyle"], Padding = new(12) };
    private static void ClampCards(TagFlowPanel panel) { if (panel.ActualWidth <= 0) return; foreach (var child in panel.Children.OfType<FrameworkElement>()) if (child.Tag is double width) child.MaxWidth = Math.Min(width, panel.ActualWidth); }
    private void ShowSection(Expander section) { section.IsExpanded = true; DispatcherQueue.TryEnqueue(() => { if (active && !disposed && section.IsLoaded) { scroll.UpdateLayout(); var y = section.TransformToVisual(body).TransformPoint(new(0, 0)).Y; scroll.ChangeView(null, y, null, true); } }); }
    private Task ShowEpisodesAsync()
    {
        var loading = episodeList.ExpandAsync();
        scroll.UpdateLayout(); scroll.ChangeView(null, episodeList.TransformToVisual(body).TransformPoint(new(0, 0)).Y, null, true);
        // The directory grows below this header. Completion must not scroll back
        // over a subsequent section jump, especially with keyboard/UIA input.
        return loading;
    }
    private void AddFact(string value) { if (value.Length > 0) facts.Children.Add(Text(13, value)); }
    private Task SearchAsync() => model?.SearchAsync(string.IsNullOrWhiteSpace(searchText.Text) ? null : searchText.Text.Trim()) ?? Task.CompletedTask;
    internal Task RefreshAsync() => model?.RefreshAsync() ?? Task.CompletedTask;
    internal void Cancel() { publications.Cancel(); model?.Cancel(); }
    internal bool GoBack() => publications.GoBack() || model?.GoBack() == true;
    internal async Task FixtureAsync(string action, string? id)
    {
        if (model is null) return;
        if (action.StartsWith("book", StringComparison.Ordinal)) { await publications.FixtureAsync(action, id); return; }
        if (action == "aniEpisodeFilter") { episodeList.FilterForFixture(int.TryParse(id, out var filter) ? filter : null); return; }
        if (action == "aniEpisodeExpand" && int.TryParse(id, out var episode)) { episodeList.ExpandEpisodeForFixture(episode); return; }
        if (action == "aniEpisodes")
        {
            var idBefore = episodeSource; var stateBefore = model; var restoreBefore = model.RestoreVersion;
            await episodeList.VisualExpandAsync();
            if (active && !disposed && ReferenceEquals(model, stateBefore) && model.RestoreVersion == restoreBefore && episodeSource == idBefore && idBefore is not null)
            { scroll.UpdateLayout(); scroll.ChangeView(null, episodeList.TransformToVisual(body).TransformPoint(new(0, 0)).Y, null, true); }
            return;
        }
        switch (action)
        {
            case "aniPreview" when int.TryParse(id, out var value): await model.PreviewAsync(value); break;
            case "aniConfirm": await model.ConfirmAsync(); break;
            case "aniUserLinks": ShowSection(userSelections); break;
            case "aniOpenSaved" when model.UserLinks?.Items.FirstOrDefault(x => x.Target.Id == id) is { } saved: await model.OpenUserLinkAsync(saved); break;
            case "aniRemoveSaved" when model.UserLinks?.Items.FirstOrDefault(x => x.Target.Id == id) is { } saved: await model.RemoveUserLinkAsync(saved.Target); break;
            case "aniDecisionCancel": associationDialog?.Hide(); break;
            case "aniDecisionConfirm":
                if (associationDialog is null) throw new InvalidOperationException("Owned association dialog required.");
                var pendingButtons = new Stack<DependencyObject>(); pendingButtons.Push(associationDialog); Button? primary = null;
                while (pendingButtons.TryPop(out var node))
                {
                    if (node is Button { Name: "PrimaryButton", IsEnabled: true } button) { primary = button; break; }
                    for (var i = 0; i < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(node); i++) pendingButtons.Push(Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(node, i));
                }
                if (primary is null) throw new InvalidOperationException("Native primary button not found.");
                var peer = new Microsoft.UI.Xaml.Automation.Peers.ButtonAutomationPeer(primary);
                if (peer.GetPattern(Microsoft.UI.Xaml.Automation.Peers.PatternInterface.Invoke) is not Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider invoke) throw new InvalidOperationException();
                invoke.Invoke(); await Task.Delay(250); break;
            case "aniCharacters": ShowSection(characters); break;
            case "aniStaff": ShowSection(staff); break;
            case "aniRelations": ShowSection(related); break;
            case "aniReview": ShowSection(matchReview); break;
            case "aniContentOriginal":
                languageInfo.IsExpanded = !languageInfo.IsExpanded;
                scroll.UpdateLayout(); scroll.ChangeView(null, languageInfo.TransformToVisual(body).TransformPoint(new(0, 0)).Y, null, true); return;
            case "aniSources": ShowSection(sources); break;
            case "aniSynopsis": synopsis.ExpandForFixture(); synopsis.StartBringIntoView(); break;
            case "aniPV": availability.ExpandForFixture(); availability.StartBringIntoView(); break;
            case "aniPVLoad": await availability.StartAsync(); availability.StartBringIntoView(); break;
            case "aniPVStart": _ = availability.StartAsync(); break;
            case "aniPVCollapse": availability.CollapseForFixture(); break;
            case "aniPVPlay": await availability.PlayForFixtureAsync(); break;
            case "aniPVProbe": await availability.ProbeForFixtureAsync(); break;
            case "aniPVStop": availability.Stop(); break;
            case "aniChoose": await model.ChooseAgainAsync(); candidateSection.StartBringIntoView(); break;
            case "watchSources": watchLinks.SourcesForFixture(); break;
            case "watchShow": watchLinks.ShowForFixture(); break;
            case "watchCollapse": watchLinks.CollapseForFixture(); break;
            case "aniExplore" when int.TryParse(id, out var value) && model.Displayed?.Details.Relations.FirstOrDefault(x => x.Media.Id == value) is { } item: await model.ExploreAsync(item, scroll.VerticalOffset); break;
            case "aniMore": await model.MoreAsync(); break;
        }
    }
    internal object Evidence => new { state = model?.State.ToString(), subjectId = model?.Item?.Id, mediaId = model?.Media?.Id, title = title.Text, kind = model?.Media?.Kind.ToString(), historyDepth = model?.HistoryDepth, canConfirm = model?.CanConfirm, canAddRelated = model?.CanAddRelated, relatedSelection = model?.RelatedSelection, trustedBridge = model?.HasTrustedBridge, savedLinks = model?.UserLinks?.Items.Select(x => new { x.Target, x.State, x.Relation }).ToArray(), confirmButtonEnabled = confirm.IsEnabled, fromCache = model?.Displayed?.FromCache, stale = model?.Displayed?.IsStale, restoredOffset = model?.RestoreOffset,
        candidates = model?.Candidates.Select(x => x.Id).ToArray(), candidateHitTargets = candidates.Children.OfType<Button>().Select(InteractiveCardEvidence.Hit).ToArray(), peoplePage = model?.PeoplePages.LastOrDefault()?.Details.PeoplePage, characters = characterCards.Children.Count, characterImages = characterCards.Children.OfType<Border>().Select(x => ((Grid)x.Child).Children.OfType<CoverPresenter>().Select(i => new { i.SourceStatus, i.HasImage, i.ActualWidth }).FirstOrDefault()).ToArray(), staff = model?.PeoplePages.SelectMany(x => x.Details.Staff).DistinctBy(x => (x.Person.Id, x.Role)).Count(), staffGroups = staffBody.Children.Count, relations = model?.Displayed?.Details.Relations.Select(x => new { x.Media.Id, x.Relation }).ToArray(), summary = synopsis.Text, synopsisVisible = synopsis.Visibility == Visibility.Visible, retryVisible = retry.Visibility == Visibility.Visible, contentLanguage = languageInfo.Evidence, synopsis = synopsis.Evidence, status = status.Text, playback = availability.Evidence, watching = watchLinks.Evidence, books = publications.Evidence, episodes = episodeList.Inspect(), scrollOffset = scroll.VerticalOffset, scrollInputVersion, cover.HasImage, cover.SourceStatus, width = ActualWidth };
    public void Deactivate() { active = false; CancelWatchRead(); watchResult = null; availability.Stop(); publications.Deactivate(); episodeSource = null; episodeList.SetSource(null); coverVersion++; model?.Deactivate(); cover.SourceKey = null; ring.IsActive = false; characterCards.Children.Clear(); relatedCards.Children.Clear(); shownPages = null; }
    public void Dispose() { if (disposed) return; Deactivate(); disposed = true; availability.Dispose(); watchLinks.Dispose(); publications.Dispose(); episodeList.Dispose(); if (model is not null) { model.PropertyChanged -= StateChanged; model.Dispose(); } cover.Pipeline = null; images = null; registry = null; openBangumi = null; bookSubjects = null; bookReader = null; bookRelations = null; }
    private static string PersonName(AniListPerson p) => ContentLanguage.Person(p, "Staff").Text;
    private static string VoiceCaption(AniListCharacter character) => character.VoiceRoles.Length > 0
        ? "日语配音 · " + string.Join(" / ", character.VoiceRoles.Select(x => PersonName(x.Person)
            + (x.RoleNotes is { Length: > 0 } note ? "（" + (note switch { "Child" => "幼年", "Old" => "老年", _ => note }) + "）" : "")
            + (x.DubGroup is { Length: > 0 } group ? " · " + group : "")))
        : character.VoiceActors.Length > 0 ? "日语配音 · " + string.Join(" / ", character.VoiceActors.Select(PersonName)) : "暂无声优资料";
    private static Visibility Visible(bool value) => value ? Visibility.Visible : Visibility.Collapsed;
    private static TextBlock Text(double size, string text = "") => new() { FontSize = size, Text = text, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
    private static TagFlowPanel Flow(double max = 400) => new() { HorizontalSpacing = 12, VerticalSpacing = 10, MaximumItemWidth = max, CenterInlineItems = false };
    private static Button Button(string content) => new() { Content = content, CornerRadius = new(6), MinHeight = 32, HorizontalAlignment = HorizontalAlignment.Left };
    private static Expander Expand(string heading) => new() { Header = heading, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch };
    private static string Format(string value) => value switch { "TV" => "电视动画", "TV_SHORT" => "电视短篇", "MOVIE" => "剧场版", "OVA" => "OVA", "ONA" => "网络动画", "SPECIAL" => "特别篇", "MUSIC" => "音乐影像", "MANGA" => "漫画", "NOVEL" => "小说", "ONE_SHOT" => "短篇漫画", _ => value };
    private static string Status(string value, bool anime) => value switch { "FINISHED" => "已完结", "RELEASING" => anime ? "播出中" : "连载中", "NOT_YET_RELEASED" => anime ? "尚未播出" : "尚未发行", "CANCELLED" => "已取消", "HIATUS" => "暂停", _ => value };
    private static string Role(string value) => value switch { "MAIN" => "主要角色", "SUPPORTING" => "配角", "BACKGROUND" => "背景角色", _ => value };
    private static string StaffRole(string value) => value switch { "Director" => "导演", "Series Composition" => "系列构成", "Original Creator" => "原作", "Story" => "故事", "Art" => "作画", "Story & Art" => "故事与作画", "Illustration" => "插画", "Music" => "音乐", "Character Design" => "角色设计", "Animation Director" => "作画监督", "Sound Director" => "音响监督", "Script" => "脚本", _ => value };
    private static string Relation(string value) => value switch { "SEQUEL" => "续作", "PREQUEL" => "前作", "ADAPTATION" => "改编关联", "SOURCE" => "原作", "SIDE_STORY" => "外传", "SPIN_OFF" => "衍生", "ALTERNATIVE" => "不同版本 / 演绎", "SUMMARY" => "总集篇", "PARENT" => "本篇", "CHARACTER" => "角色关联", "COMPILATION" => "合集", "CONTAINS" => "收录", _ => "其他关联" };
}
