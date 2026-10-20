using System.ComponentModel;
using System.Globalization;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using TheOrder.Application;
using TheOrder.Presentation;

namespace TheOrder.WinUI.Views;

// Reuses the readonly media host and shell back path. This page is instantiated once per host;
// no music requests happen until a Bangumi music card is opened.
public sealed class MusicBrainzAlbumView : Grid, IDisposable
{
    private readonly TextBlock title = Text(28), type = Text(14), artist = Text(16);
    private readonly TextBlock originalTitle = Text(14), releaseLine = Text(14);
    private readonly ContentControl artistLinks = new() { HorizontalContentAlignment = HorizontalAlignment.Stretch };
    private MusicBrainzArtistCredit[]? shownArtists;
    private readonly TextBlock relation = Text(14), status = Text(14), choiceReason = Text(13);
    private readonly ContentLanguageInfo languageInfo = new();
    private readonly DetailSynopsis summary = new() { Heading = "简介 · Bangumi" };
    private readonly MusicListeningView listening = new();
    private readonly TagFlowPanel releaseFacts = MusicDetailFormatting.Facts();
    private readonly TextBlock coverCredit = Text(14);
    private readonly CoverPresenter cover = new() { Width = 136, Height = 136, PreserveEntireImage = true, ImageCornerRadius = 8 };
    private readonly Grid header = new() { ColumnSpacing = 20 };
    private readonly ProgressRing ring = new() { Width = 18, Height = 18 };
    private readonly Button cancel = Button("取消读取"), retry = Button("重试"), editions = Button("切换发行版"), refresh = Button("刷新音乐资料");
    private readonly Button confirm = Button("确认使用此发行版"), reject = Button("排除此发行版"), chooseAgain = Button("重新选择"), showTracks = Button("曲目列表 ↓");
    private readonly TextBox searchQuery = new() { PlaceholderText = "专辑名，或 MusicBrainz 发行版 / 专辑组链接", MaxLength = 2000, HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly Button search = Button("查找专辑");
    private readonly Button retryCover = Button("重试封面");
    private readonly Button addRelated = Button("添加相关发行版"), savePending = Button("保存为待核验"), restoreAutomatic = Button("恢复自动选择");
    private readonly Expander userSelections = new() { Header = "我的关联与待核验选择", HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch };
    private readonly Grid searchRow = new() { ColumnSpacing = 8 };
    private readonly CreditColumnsPanel candidates = new();
    private readonly StackPanel discs = new() { Spacing = 12 }, personnel = new() { Spacing = 8 };
    private readonly StackPanel candidateSection = new() { Spacing = 12 }, tracksSection = new() { Spacing = 12 }, summarySection = new() { Spacing = 12 };
    private readonly TagFlowPanel actions = new() { HorizontalSpacing = 8, VerticalSpacing = 8 }, confirmActions = new() { HorizontalSpacing = 8, VerticalSpacing = 8 };
    private readonly Expander credits = new() { Header = "制作人员", HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch };
    private readonly Expander sources = new() { Header = "资料来源", HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch };
    private readonly Expander raw = new() { Header = "关系说明与匹配依据", HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch };
    private readonly StackPanel sourceBody = new() { Spacing = 8 }, rawBody = new() { Spacing = 8 };
    private readonly ScrollViewer scroll;
    private MusicAlbumViewModel? model;
    private IRemoteCoverRegistry? registry;
    private Func<string, Task>? bangumiLink;
    private MusicBrainzAlbum? shownAlbum;
    private MusicBrainzSearchResult? shownSearch;
    private string? coverKey;
    private string origin = "", relationLabel = "", relationEvidence = "";
    private long generation;
    private bool active, disposed;
    private bool startPending, startQueued, coverFallback;
    private MusicBrainzEntityDetailView? entityView;
    private bool entityActive;
    private double entityReturnOffset;
    private double? pendingAlbumOffset;
    private long albumRestoreVersion;
    private bool restoringAlbum;
    private string? requestedCover;
    public MusicBrainzAlbumView()
    {
        AutomationProperties.SetAutomationId(this, "MusicAlbumDetail");
        AutomationProperties.SetAutomationId(title, "MusicAlbumTitle");
        AutomationProperties.SetAutomationId(status, "MusicAlbumStatus"); AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);
        AutomationProperties.SetAutomationId(cancel, "MusicAlbumCancel"); AutomationProperties.SetAutomationId(retry, "MusicAlbumRetry");
        AutomationProperties.SetAutomationId(editions, "MusicAlbumEditions"); AutomationProperties.SetAutomationId(confirm, "MusicAlbumConfirm");
        AutomationProperties.SetAutomationId(searchQuery, "MusicAlbumSearchQuery"); AutomationProperties.SetAutomationId(search, "MusicAlbumSearch");
        AutomationProperties.SetAutomationId(showTracks, "MusicAlbumShowTracks"); AutomationProperties.SetAutomationId(tracksSection, "MusicAlbumTracks");
        AutomationProperties.SetAutomationId(sources, "MusicAlbumSources");
        title.FontWeight = FontWeights.SemiBold; type.Opacity = choiceReason.Opacity = .75; credits.Visibility = Visibility.Collapsed;
        confirm.Style = (Style)Microsoft.UI.Xaml.Application.Current.Resources["AccentButtonStyle"];
        cover.ImageCornerRadius = 7;
        var coverFrame = new Border { Style = (Style)Microsoft.UI.Xaml.Application.Current.Resources["CatalogCreditCardStyle"], Padding = new Thickness(0), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Child = cover };
        header.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); header.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        header.Children.Add(coverFrame);
        originalTitle.Opacity = .7;
        releaseLine.Opacity = .8; relation.Opacity = .8;
        releaseFacts.HorizontalSpacing = 18; releaseFacts.VerticalSpacing = 6; releaseFacts.MaximumItemWidth = 340;
        var info = new StackPanel { Spacing = 6, Children = { type, title, originalTitle, artist, artistLinks, releaseLine, releaseFacts, choiceReason } }; SetColumn(info, 1); header.Children.Add(info);
        actions.Children.Add(ring); actions.Children.Add(cancel); actions.Children.Add(retry); actions.Children.Add(showTracks); actions.Children.Add(editions);
        confirmActions.Children.Add(confirm); confirmActions.Children.Add(addRelated); confirmActions.Children.Add(chooseAgain);
        actions.Children.Add(savePending);
        retryCover.Visibility = Visibility.Collapsed; actions.Children.Add(retryCover); AutomationProperties.SetAutomationId(retryCover, "MusicAlbumRetryCover");
        retryCover.Click += (_, _) => cover.RetryFailed();
        cover.SourceStateChanged += () => retryCover.Visibility = Visible(active && !cover.HasImage && cover.SourceKey is not null && cover.SourceStatus is not ("loading" or "not-started"));
        AutomationProperties.SetAutomationId(addRelated, "MusicAlbumAddRelated"); AutomationProperties.SetAutomationId(savePending, "MusicAlbumSavePending"); AutomationProperties.SetAutomationId(restoreAutomatic, "MusicAlbumRestoreAutomatic");
        AutomationProperties.SetAutomationId(reject, "MusicAlbumReject");
        ToolTipService.SetToolTip(reject, "取消关联并排除自动匹配；仍可手动核对后重新确认。");
        searchRow.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); searchRow.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        searchRow.Children.Add(searchQuery); SetColumn(search, 1); searchRow.Children.Add(search);
        candidateSection.Children.Add(Text(22, "选择发行版")); candidateSection.Children.Add(searchRow); candidateSection.Children.Add(candidates);
        tracksSection.Children.Add(Text(22, "曲目列表")); tracksSection.Children.Add(discs);
        credits.Content = personnel; summarySection.Children.Add(summary);
        raw.Content = rawBody; sourceBody.Children.Add(raw); sourceBody.Children.Add(refresh); sources.Content = sourceBody;
        var overview = new StackPanel { Spacing = 12, Children = { header, relation, status, actions, confirmActions, userSelections, candidateSection } };
        var body = new StackPanel { Spacing = 20, MaxWidth = 1160, HorizontalAlignment = HorizontalAlignment.Stretch,
            Children = { overview, listening, tracksSection, credits, summarySection, languageInfo, sources } };
        scroll = new() { Content = body, HorizontalContentAlignment = HorizontalAlignment.Stretch, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Children.Add(scroll);
        scroll.Loaded += (_, _) => QueueStart(); body.SizeChanged += (_, _) => { QueueStart(); RestoreAlbumPosition(); };
        void UserInput() { startPending = false; pendingAlbumOffset = null; albumRestoreVersion++; }
        scroll.AddHandler(UIElement.PointerWheelChangedEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler((_, _) => UserInput()), true);
        scroll.PointerPressed += (_, _) => UserInput();
        scroll.KeyDown += (_, _) => UserInput();
        cancel.Click += (_, _) => model?.Cancel(); retry.Click += async (_, _) => await RefreshAsync(); refresh.Click += async (_, _) => await RefreshAsync();
        editions.Click += async (_, _) => { if (model is not null) await model.ChooseEditionAsync(); };
        confirm.Click += async (_, _) =>
        {
            if (model?.CanConfirm != true) return;
            var selected = model; var source = model.Source; var target = model.Preview;
            if (MusicBrainzMatching.Assess(source!, target!.Album.Release).HasConflict)
            {
                var dialog = new ContentDialog { XamlRoot = XamlRoot, RequestedTheme = ActualTheme, Title = "仍要使用这个发行版？",
                    Content = "日期、目录编号或其他发行资料存在差异。请核对版本和曲目；原有个人资料会保留。", PrimaryButtonText = "仍要关联", CloseButtonText = "取消", DefaultButton = ContentDialogButton.Close };
                if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
            }
            if (ReferenceEquals(model, selected) && model.Source == source && model.Preview == target && model.CanConfirm) await model.ConfirmAsync();
        };
        addRelated.Click += async (_, _) => { if (model?.CanAddRelated == true) await model.AddRelatedAsync(); };
        savePending.Click += async (_, _) => { if (model?.CanSavePending == true) await model.SavePendingAsync(); };
        restoreAutomatic.Click += async (_, _) => { if (model?.CanRestoreAutomatic == true) await model.RestoreAutomaticAsync(); };
        reject.Click += async (_, _) => { if (model?.RelatedSelection == true && model.Displayed is { } selected) await model.RemoveUserLinkAsync(MediaIdentityDecisionRules.Target(selected.Album.Release.Id)); else if (model?.CanReject == true) await model.RejectAsync(); else if (model is not null) await model.RevokeAsync(); };
        chooseAgain.Click += (_, _) => model?.BackToEditions();
        showTracks.Click += (_, _) => tracksSection.StartBringIntoView();
        search.Click += async (_, _) => await SearchAsync();
        searchQuery.KeyDown += async (_, e) => { if (e.Key == Windows.System.VirtualKey.Enter && model?.CanLoad == true) { e.Handled = true; await SearchAsync(); } };
        SizeChanged += (_, _) => { var narrow = ActualWidth < 600; cover.Width = cover.Height = narrow ? 96 : 136; title.FontSize = narrow ? 22 : 26; header.ColumnSpacing = narrow ? 14 : 18; };
        Unloaded += (_, _) => { if (!IsLoaded) Deactivate(); };
    }
    public void Configure(IBangumiSubjectDetailsReader subjects, IMusicBrainzReader music, IImagePipeline images,
        IRemoteCoverRegistry? coverRegistry, Func<string, Task> openBangumi, IMusicBrainzEntityReader? entities = null)
    {
        Deactivate(); if (model is not null) { model.PropertyChanged -= StateChanged; model.Dispose(); }
        registry = coverRegistry; cover.Pipeline = images; bangumiLink = openBangumi;
        model = new(subjects, music); model.PropertyChanged += StateChanged;
        listening.Configure(music as IMusicListeningReader);
        if (entityView is not null) { entityView.Dispose(); Children.Remove(entityView); entityView = null; }
        if (entities is not null)
        {
            entityView = new() { Visibility = Visibility.Collapsed };
            entityView.Configure(entities, OpenMusicSourceAsync, music as IMusicListeningReader);
            entityView.ReturnToAlbum = ReturnFromEntity;
            Children.Add(entityView);
        }
    }
    public Task OpenAsync(BangumiRelationItem item, string originTitle, string? labelText, string? evidence, CancellationToken cancellation)
    {
        active = true; generation++; startPending = true; origin = originTitle; relationLabel = labelText ?? item.Relation; relationEvidence = evidence ?? "";
        entityView?.Deactivate(); entityActive = false; if (entityView is not null) entityView.Visibility = Visibility.Collapsed; scroll.Visibility = Visibility.Visible;
        pendingAlbumOffset = null; albumRestoreVersion++;
        shownAlbum = null; shownSearch = null; coverKey = null; sources.IsExpanded = raw.IsExpanded = credits.IsExpanded = false;
        candidates.Children.Clear(); discs.Children.Clear(); personnel.Children.Clear(); credits.Visibility = Visibility.Collapsed; cover.SourceKey = null;
        requestedCover = null; coverFallback = false;
        searchQuery.Text = "";
        scroll.ChangeView(null, 0, null); return model?.OpenAsync(item, cancellation) ?? Task.CompletedTask;
    }
    private void StateChanged(object? sender, PropertyChangedEventArgs e) { if (active && !disposed) Render(); }
    private void Render()
    {
        var album = model?.Displayed?.Album; var source = model?.RelatedSelection == true ? null : model?.SourceDetails?.Details; var item = model?.Item;
        listening.SetContext(album is null ? null : new(MusicBrainzEntityKind.Release, album.Release.Id));
        var localizedTitle = album is not null ? ContentLanguage.MusicTitle(album, source, model?.State == MusicAlbumState.Ready) : source is not null ? ContentLanguage.BangumiTitle(source) : null;
        title.Text = localizedTitle?.Text is { Length: > 0 } selectedTitle ? selectedTitle : album?.Release.Title ?? source?.Title ?? item?.Title ?? "音乐";
        originalTitle.Text = localizedTitle?.OriginalText ?? "";
        originalTitle.Visibility = Visible(originalTitle.Text.Length > 0 && originalTitle.Text != title.Text);
        type.Text = album is null ? "音乐 · Bangumi" : (album.GroupType switch { "Album" => "专辑", "Single" => "单曲发行", "EP" => "EP", _ => "音乐发行" }) + " · MusicBrainz";
        artist.Text = album?.Release.Artist is { Length: > 0 } performer ? performer == "Various Artists" ? "Various Artists（合辑）" : performer
            : string.Join(" / ", source?.MusicFacts?.Artists ?? []);
        RenderReleaseFacts(album, source);
        choiceReason.Text = model?.State == MusicAlbumState.Preview ? "发行版预览 · 尚未确认" : model?.SelectionReason ?? "";
        foreach (var text in new[] { artist, choiceReason }) text.Visibility = Visible(text.Text.Length > 0);
        choiceReason.Visibility = Visible(model?.State == MusicAlbumState.Preview);
        var creditedArtists = album?.Release.ArtistCredits;
        if (!ReferenceEquals(shownArtists, creditedArtists))
        {
            shownArtists = creditedArtists;
            artistLinks.Content = creditedArtists is { Length: > 0 }
                ? MusicDetailFormatting.Artists(creditedArtists, entityView is not null ? OpenEntityAsync : null) : null;
        }
        artistLinks.Visibility = Visible(creditedArtists is { Length: > 0 });
        if (creditedArtists is { Length: > 0 }) artist.Visibility = Visibility.Collapsed;
        relation.Text = origin.Length == 0 ? relationLabel : $"与《{origin}》的关联 · {relationLabel}";
        if (model?.RelatedSelection == true) relation.Text = $"与《{model.Source?.Title}》的关联 · 用户添加的相关发行版";
        status.Text = model?.Message ?? ""; status.Visibility = Visible(status.Text.Length > 0);
        ring.IsActive = model?.IsBusy == true; ring.Visibility = Visible(ring.IsActive);
        cancel.Visibility = Visible(model?.CanCancel == true); retry.Visibility = Visible(model?.State is MusicAlbumState.Failed or MusicAlbumState.Cancelled);
        retry.IsEnabled = refresh.IsEnabled = model?.CanLoad == true;
        editions.Visibility = Visible(album is not null && model?.State != MusicAlbumState.Choosing); editions.IsEnabled = model?.CanLoad == true;
        showTracks.Visibility = Visible(album?.Discs.Any(x => x.Tracks.Length > 0) == true);
        actions.Visibility = Visible(actions.Children.OfType<FrameworkElement>().Any(x => x.Visibility == Visibility.Visible));
        confirmActions.Visibility = Visible(model?.State == MusicAlbumState.Preview); confirm.IsEnabled = model?.CanConfirm == true;
        addRelated.IsEnabled = model?.CanAddRelated == true;
        addRelated.Style = model?.CanConfirm == true ? null : (Style)Microsoft.UI.Xaml.Application.Current.Resources["AccentButtonStyle"];
        savePending.Visibility = Visible(model?.PendingReference is not null && model?.Preview is null); savePending.IsEnabled = model?.CanSavePending == true;
        RenderUserSelections();
        reject.Visibility = Visible(model?.Displayed is not null || model?.SavedUserTargetId is not null);
        reject.IsEnabled = model?.CanReject == true || model?.CanRevoke == true;
        reject.Content = model?.RelatedSelection == true ? "移除相关发行版" : model?.Displayed is null || model?.State == MusicAlbumState.Ready ? "取消关联" : "排除此发行版";
        candidateSection.Visibility = Visible(model?.State == MusicAlbumState.Choosing || model?.State == MusicAlbumState.Failed && source is not null);
        search.IsEnabled = searchQuery.IsEnabled = model?.CanLoad == true;
        if (!ReferenceEquals(shownSearch, model?.Match?.Search)) { shownSearch = model?.Match?.Search; RenderCandidates(); }
        tracksSection.Visibility = Visible(album?.Discs.Length > 0);
        if (!ReferenceEquals(shownAlbum, album)) { shownAlbum = album; RenderDiscs(album); }
        var localizedSummary = ContentLanguage.Synopsis(null, source, true);
        languageInfo.Show(localizedTitle ?? ContentLanguage.Select([]), localizedSummary);
        summary.Text = localizedSummary.Text; summarySection.Visibility = Visible(summary.Text.Length > 0);
        RenderSources(album, item, source);
        var bridgeCover = model?.RelatedSelection == true || model?.State == MusicAlbumState.Preview ? null : source?.CoverUrl ?? item?.CoverUrl;
        SetCover(album?.CoverUrl ?? bridgeCover, bridgeCover);
        QueueStart();
    }
    private void RenderUserSelections()
    {
        if (userSelections.Content is StackPanel old) old.Children.Clear();
        var panel = new StackPanel { Spacing = 10 };
        foreach (var link in model?.UserLinks?.Items ?? [])
        {
            var row = new StackPanel { Spacing = 5 };
            row.Children.Add(Text(14, link.Title));
            row.Children.Add(Text(12, $"MusicBrainz · {link.Target.Resource} · {link.Target.Id} · " + (link.State == MediaUserLinkState.Pending ? "待核验（不融合资料）" : "相关发行版 → 独立资料")));
            var commands = new TagFlowPanel { HorizontalSpacing = 8, VerticalSpacing = 8 };
            var open = Button(link.State == MediaUserLinkState.Pending ? "重试查看" : "查看发行版"); var remove = Button("移除关联");
            open.IsEnabled = remove.IsEnabled = model?.CanLoad == true;
            open.Click += async (_, _) => { if (model is not null) await model.OpenUserLinkAsync(link); };
            remove.Click += async (_, _) => { if (model is not null) await model.RemoveUserLinkAsync(link.Target); };
            commands.Children.Add(open); commands.Children.Add(remove); row.Children.Add(commands); panel.Children.Add(row);
        }
        restoreAutomatic.Visibility = Visible(model?.CanRestoreAutomatic == true); restoreAutomatic.IsEnabled = model?.CanRestoreAutomatic == true;
        userSelections.Content = panel;
        userSelections.Visibility = Visible(model?.UserLinks?.Items.Length > 0);
    }
    private void RenderReleaseFacts(MusicBrainzAlbum? album, BangumiSubjectDetails? source)
    {
        releaseFacts.Children.Clear();
        releaseLine.Text = "";
        if (album is null)
        {
            releaseLine.Text = source?.ReleaseDate ?? "";
            MusicDetailFormatting.AddFact(releaseFacts, "目录编号", string.Join(" / ", source?.MusicFacts?.CatalogNumbers ?? []));
        }
        else
        {
            var release = album.Release;
            MusicDetailFormatting.AddFact(releaseFacts, "版本", release.Disambiguation);
            var allDigital = album.Discs.Length > 0 && album.Discs.All(x => x.Format == "Digital Media");
            var formats = album.Discs.Select(x => x.Format).Where(x => x.Length > 0).Distinct().Select(x => x == "Digital Media" ? "数字发行" : x);
            releaseLine.Text = string.Join(" · ", new[] { release.Date, Country(release.Country), string.Join(" / ", formats),
                album.Discs.Length + (allDigital ? " 组曲目" : " 碟"), album.Discs.Sum(x => x.Tracks.Length) + " 首" }.Where(x => !string.IsNullOrWhiteSpace(x)));
            MusicDetailFormatting.AddFact(releaseFacts, "厂牌", string.Join(" / ", release.Labels.Select(x => x.Name).Where(x => x.Length > 0).Distinct()));
            MusicDetailFormatting.AddFact(releaseFacts, "目录编号", string.Join(" / ", release.Labels.Select(x => x.CatalogNumber).OfType<string>().Distinct()));
        }
        releaseFacts.Visibility = Visible(releaseFacts.Children.Count > 0);
        releaseLine.Visibility = Visible(releaseLine.Text.Length > 0);
    }
    private void QueueStart()
    {
        if (!active || disposed || !startPending || startQueued) return;
        startQueued = true;
        DispatcherQueue.TryEnqueue(() =>
        {
            startQueued = false;
            if (!active || disposed || !startPending || !scroll.IsLoaded || scroll.ActualHeight <= 0) return;
            scroll.UpdateLayout();
            if (scroll.ChangeView(null, 0, null, true) || scroll.VerticalOffset == 0) startPending = false;
        });
    }
    private void RenderCandidates()
    {
        candidates.Children.Clear();
        if (shownSearch is null || model?.Source is not { } bridge) return;
        foreach (var candidate in shownSearch.Candidates)
        {
            var evidence = MusicBrainzMatching.Assess(bridge, candidate);
            var card = new StackPanel { Spacing = 6 };
            var name = Text(18, candidate.Title); name.FontWeight = FontWeights.SemiBold; name.MaxLines = 2; name.TextTrimming = TextTrimming.CharacterEllipsis;
            card.Children.Add(name); card.Children.Add(Text(14, EditionInfo(candidate)));
            var catalogues = string.Join(" / ", candidate.Labels.Select(x => x.CatalogNumber).Where(x => x is not null).Distinct());
            if (catalogues.Length > 0) card.Children.Add(Text(14, "目录编号 · " + catalogues));
            if (candidate.Barcode is { } barcode) card.Children.Add(Text(14, "条码 · " + barcode));
            if (candidate.Artist.Length > 0) card.Children.Add(Text(14, candidate.Artist));
            if (candidate.Status.Length > 0) card.Children.Add(Text(13, candidate.Status switch
            { "Official" => "正式发行", "Pseudo-Release" => "曲目别名版本", "Bootleg" => "非官方发行", _ => candidate.Status }));
            if (evidence.HasConflict) { var warning = Text(13, "发行信息有差异，请核对后选择"); warning.Opacity = .8; card.Children.Add(warning); }
            if (model?.Match?.RejectedIds.Contains(candidate.Id) == true) card.Children.Add(Text(13, "已排除自动关联 · 可手动核对"));
            card.Children.Add(new Expander { Header = "匹配依据", HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch, Content = Text(13, string.Join("\n", evidence.Evidence)) });
            var preview = Button("查看此发行版与曲目"); AutomationProperties.SetAutomationId(preview, "MusicEdition-" + candidate.Id);
            preview.Click += async (_, _) => { if (model is not null) await model.PreviewAsync(candidate.Id); }; card.Children.Add(preview);
            candidates.Children.Add(new Border { Style = (Style)Microsoft.UI.Xaml.Application.Current.Resources["SurfaceStyle"], Padding = new(16), Child = card });
        }
    }
    private void RenderDiscs(MusicBrainzAlbum? album)
    {
        discs.Children.Clear(); personnel.Children.Clear(); credits.Visibility = Visibility.Collapsed;
        if (album is null) return;
        if (album.Credits.Length > 0) personnel.Children.Add(MusicDetailFormatting.Credits(album.Credits, credit =>
        {
            if (credit.ArtistId is { } artistId && entityView is not null)
            {
                var item = new MusicBrainzReference(MusicBrainzEntityKind.Artist, artistId);
                var link = new HyperlinkButton { Content = new TextBlock { Text = MusicDetailFormatting.CreditPerson(credit), TextWrapping = TextWrapping.Wrap, IsHitTestVisible = false },
                    Padding = new(0, 5, 0, 5), MinHeight = 30, HorizontalAlignment = HorizontalAlignment.Left };
                ToolTipService.SetToolTip(link, credit.Role + " · " + credit.Name);
                link.Click += async (_, _) => await OpenEntityAsync(item); return link;
            }
            var text = Text(14, MusicDetailFormatting.CreditPerson(credit)); text.Margin = new(0, 6, 0, 6);
            ToolTipService.SetToolTip(text, credit.Role + " · " + credit.Name); return text;
        }));
        credits.Visibility = Visible(personnel.Children.Count > 0);
        foreach (var disc in album.Discs)
        {
            var digital = disc.Format == "Digital Media";
            var expander = new Expander { Header = (digital ? $"曲目组 {disc.Position}" : $"碟 {disc.Position}") + (disc.Title.Length > 0 ? " · " + disc.Title : "")
                + $" · {(digital ? "数字发行" : disc.Format)} · {disc.Tracks.Length} 首", IsExpanded = disc.Position == 1,
                HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch };
            AutomationProperties.SetAutomationId(expander, "MusicDisc-" + disc.Position);
            bool populated = false;
            void Populate()
            {
                if (populated) return; populated = true;
                // The page owns the only scrolling viewport. EffectiveViewport-based realization
                // keeps long discs bounded without an inner list scrollbar or trapped wheel input.
                var list = new ItemsRepeater { Layout = new StackLayout(), HorizontalAlignment = HorizontalAlignment.Stretch,
                    ItemsSource = disc.Tracks.Select(x => new MusicTrackRow(x, entityView is not null ? OpenEntityAsync : null, disc.Position)).ToArray(), ItemTemplate = (DataTemplate)Microsoft.UI.Xaml.Application.Current.Resources["MusicTrackTemplate"] };
                AutomationProperties.SetAutomationId(list, "MusicDiscTracks-" + disc.Position); expander.Content = list;
            }
            expander.Expanding += (_, _) => Populate(); if (expander.IsExpanded) Populate(); discs.Children.Add(expander);
        }
    }
    private void RenderSources(MusicBrainzAlbum? album, BangumiRelationItem? item, BangumiSubjectDetails? source)
    {
        sourceBody.Children.Clear(); rawBody.Children.Clear();
        sourceBody.Children.Add(Text(14, "作品关系与简介 · Bangumi"));
        if (item is not null) sourceBody.Children.Add(Link("Bangumi 原条目", item.Url, isMusic: false));
        if (album is not null)
        {
            sourceBody.Children.Add(Text(14, "发行版、曲目与制作人员 · MusicBrainz"));
            sourceBody.Children.Add(Link("MusicBrainz 发行版", album.Url, isMusic: true));
            if (album.Release.ReleaseGroupId is { } group) sourceBody.Children.Add(Link("MusicBrainz 专辑组", new MusicBrainzReference(MusicBrainzEntityKind.ReleaseGroup, group).Url, isMusic: true));
            coverCredit.Text = album.CoverUrl is not null && !coverFallback ? "封面 · Cover Art Archive" : "封面 · 沿用 Bangumi";
            sourceBody.Children.Add(coverCredit);
        }
        if (!string.IsNullOrWhiteSpace(model?.SelectionReason)) rawBody.Children.Add(Text(13, model.SelectionReason));
        rawBody.Children.Add(Text(14, "Bangumi 原始关系 · " + (item?.Relation ?? "未标明")));
        if (relationEvidence.Length > 0) rawBody.Children.Add(Text(14, relationEvidence));
        if (source?.MusicFacts is { } facts)
        {
            if (facts.CatalogNumbers.Length > 0) rawBody.Children.Add(Text(14, "Bangumi 目录编号 · " + string.Join(" / ", facts.CatalogNumbers)));
            foreach (var reference in facts.ExternalIds) rawBody.Children.Add(Link("来源提供的 MusicBrainz " + reference.Kind, reference.Url, true));
        }
        if (album is not null && model?.Source is { } bridge) rawBody.Children.Add(Text(14, string.Join(" · ", MusicBrainzMatching.Assess(bridge, album.Release).Evidence)));
        if (album is not null)
        {
            if (album.Release.Barcode is { } barcode) rawBody.Children.Add(Text(14, "MusicBrainz 条码 · " + barcode));
            if (album.Release.TextLanguage is not null || album.Release.TextScript is not null)
                rawBody.Children.Add(Text(14, "曲目文字 · " + string.Join(" / ", new[] { album.Release.TextLanguage is { } language ? MusicDetailFormatting.Language(language) : null, album.Release.TextScript }.OfType<string>())));
            if (album.Release.Packaging is { } packaging && packaging != "None")
                rawBody.Children.Add(Text(14, "包装 · " + MusicDetailFormatting.Packaging(packaging) + "（" + packaging + "）"));
            foreach (var release in album.Release.ReleaseEvents)
                if (release.Date is not null || release.Country is not null)
                    rawBody.Children.Add(Text(14, "发行地区与日期 · " + string.Join(" · ", new[] { Country(release.Country), release.Date }.OfType<string>())));
        }
        sourceBody.Children.Add(raw); sourceBody.Children.Add(refresh); sourceBody.Children.Add(restoreAutomatic); sourceBody.Children.Add(reject);
    }
    private Task SearchAsync()
    {
        if (MusicBrainzReference.TryParse(searchQuery.Text.Trim(), out var reference)
            && reference!.Kind is not (MusicBrainzEntityKind.Release or MusicBrainzEntityKind.ReleaseGroup))
        {
            status.Text = "查找专辑请填写发行版或专辑组链接；作品、录音和音乐家链接用于来源核对。";
            status.Visibility = Visibility.Visible; return Task.CompletedTask;
        }
        return model?.SearchAsync(searchQuery.Text) ?? Task.CompletedTask;
    }
    private HyperlinkButton Link(string name, string url, bool isMusic)
    {
        var link = new HyperlinkButton { Content = name, Padding = new(0), HorizontalAlignment = HorizontalAlignment.Left };
        link.Click += async (_, _) =>
        {
            try
            {
                if (isMusic)
                { if (!MusicBrainzReference.TryParse(url, out _) || !await Windows.System.Launcher.LaunchUriAsync(new Uri(url))) throw new InvalidOperationException(); }
                else if (bangumiLink is not null) await bangumiLink(url);
            }
            catch { if (active && !disposed) { status.Text = "未能打开来源网页，请稍后重试。"; status.Visibility = Visibility.Visible; } }
        }; return link;
    }
    private void SetCover(string? url, string? fallback)
    {
        if (requestedCover == url && coverKey is not null) return;
        requestedCover = url; coverFallback = false;
        var key = registry?.Register(url, "musicbrainz-album-v1"); if (coverKey == key) return;
        coverKey = key; cover.SourceKey = key; var version = ++generation;
        if (url is null || url == fallback) return;
        DispatcherQueue.TryEnqueue(async () =>
        {
            await cover.PendingImage;
            if (!disposed && active && generation == version && !cover.HasImage && cover.SourceStatus is not ("loading" or "not-started"))
            {
                coverFallback = true; coverCredit.Text = "封面 · 沿用 Bangumi";
                coverKey = registry?.Register(fallback, "bangumi-music-fallback"); cover.SourceKey = coverKey;
            }
        });
    }
    private static string? Country(string? value) => value switch { "JP" => "日本", "US" => "美国", "DE" => "德国", "XW" => "全球", _ => value };
    private static string EditionInfo(MusicBrainzReleaseCandidate release) => string.Join(" · ", new[]
    {
        release.Date, Country(release.Country), release.Disambiguation,
        release.MediaFormats.Length > 0 ? string.Join(" / ", release.MediaFormats.Select(x => x == "Digital Media" ? "数字发行" : x)) : null,
        release.DiscCount is { } n ? n + (release.MediaFormats.Length > 0 && release.MediaFormats.All(x => x == "Digital Media") ? " 组曲目" : " 碟") : null,
        release.TrackCount is { } t ? t + " 首" : null,
        release.Packaging is { } packaging && packaging != "None" ? MusicDetailFormatting.Packaging(packaging) : null
    }.Where(x => !string.IsNullOrWhiteSpace(x)));
    internal static string Credit(MusicBrainzCredit value) => MusicDetailFormatting.Credit(value);
    private static TextBlock Text(double size, string value = "") => new() { FontSize = size, Text = value, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
    private static Button Button(string value) => new() { Content = value, MinHeight = 32, CornerRadius = new(6), HorizontalAlignment = HorizontalAlignment.Left };
    private static Visibility Visible(bool value) => value ? Visibility.Visible : Visibility.Collapsed;
    internal Task RefreshAsync() => entityActive ? entityView?.RefreshAsync() ?? Task.CompletedTask : model?.RefreshAsync() ?? Task.CompletedTask;
    internal void Cancel() { if (entityActive) entityView?.Cancel(); else model?.Cancel(); }
    private static async Task OpenMusicSourceAsync(string url)
    {
        if (!MusicBrainzReference.TryParse(url, out _) || !await Windows.System.Launcher.LaunchUriAsync(new Uri(url)))
            throw new InvalidOperationException("MusicBrainz 原站暂时无法打开。");
    }
    private async Task OpenEntityAsync(MusicBrainzReference reference)
    {
        if (!active || disposed || entityView is null || !MusicBrainzReference.ValidId(reference.Id)
            || reference.Kind is not (MusicBrainzEntityKind.Work or MusicBrainzEntityKind.Artist or MusicBrainzEntityKind.Recording)) return;
        entityReturnOffset = scroll.VerticalOffset; startPending = false; pendingAlbumOffset = null; albumRestoreVersion++;
        listening.Cancel();
        entityActive = true; scroll.Visibility = Visibility.Collapsed; entityView.Visibility = Visibility.Visible;
        await entityView.OpenAsync(reference);
    }
    private void ReturnFromEntity()
    {
        if (disposed || !active || entityView is null) return;
        entityActive = false; startPending = false; entityView.Visibility = Visibility.Collapsed; scroll.Visibility = Visibility.Visible;
        pendingAlbumOffset = entityReturnOffset; var version = ++albumRestoreVersion;
        scroll.UpdateLayout(); RestoreAlbumPosition();
        DispatcherQueue.TryEnqueue(() => { if (!disposed && active && !entityActive && version == albumRestoreVersion) { scroll.UpdateLayout(); RestoreAlbumPosition(); } });
    }
    private void RestoreAlbumPosition()
    {
        if (restoringAlbum || !active || disposed || entityActive || pendingAlbumOffset is not { } target
            || scroll.ViewportHeight <= 0 || target > 0 && scroll.ScrollableHeight <= 0) return;
        restoringAlbum = true;
        try
        {
            scroll.ChangeView(null, target, null, true);
            if (Math.Abs(scroll.VerticalOffset - Math.Min(target, scroll.ScrollableHeight)) < .5) pendingAlbumOffset = null;
        }
        finally { restoringAlbum = false; }
    }
    internal bool GoBack() => entityActive && entityView?.GoBack() == true;
    internal Task EntityForFixture(MusicBrainzReference reference) => entityActive
        ? entityView?.FollowForFixture(reference) ?? Task.CompletedTask : OpenEntityAsync(reference);
    internal Task PreviewForFixture(string id) => model?.PreviewAsync(id) ?? Task.CompletedTask;
    internal Task ConfirmForFixture() => model?.ConfirmAsync() ?? Task.CompletedTask;
    internal void OriginalForFixture() { languageInfo.IsExpanded = true; languageInfo.StartBringIntoView(); }
    internal Task EditionsForFixture() => model?.ChooseEditionAsync() ?? Task.CompletedTask;
    internal void TracksForFixture(int? position)
    {
        var target = discs.Children.OfType<Expander>().FirstOrDefault(x => AutomationProperties.GetAutomationId(x) == "MusicDisc-" + (position ?? 1));
        if (target is null) return; target.IsExpanded = true;
        scroll.UpdateLayout(); scroll.ChangeView(null, target.TransformToVisual((UIElement)scroll.Content).TransformPoint(new(0, 0)).Y, null, true);
    }
    private MusicTrackRowView? fixtureTrack;
    internal void TrackForFixture(int position)
    {
        TracksForFixture(1); scroll.UpdateLayout();
        var disc = discs.Children.OfType<Expander>().FirstOrDefault();
        var element = (disc?.Content as ItemsRepeater)?.TryGetElement(position - 1);
        if (element is Border { Child: MusicTrackRowView track })
        { fixtureTrack = track; track.ExpandForFixture(); track.StartBringIntoView(); }
    }
    internal object Evidence => new
    {
        contentLanguage = languageInfo.Evidence, originalTitle = originalTitle.Text, state = model?.State.ToString(), subjectId = model?.Item?.Id, title = title.Text, status = status.Text, model?.SelectionReason,
        listening = listening.Evidence,
        artistCredits = model?.Displayed?.Album.Release.ArtistCredits,
        artistLinks = (artistLinks.Content as TagFlowPanel)?.Children.OfType<HyperlinkButton>().Select(InteractiveCardEvidence.Hit).ToArray(),
        releaseId = model?.Displayed?.Album.Release.Id, releaseGroupId = model?.Displayed?.Album.Release.ReleaseGroupId,
        date = model?.Displayed?.Album.Release.Date, labels = model?.Displayed?.Album.Release.Labels,
        discs = model?.Displayed?.Album.Discs.Select(x => new { x.Position, x.Title, x.Format, tracks = x.Tracks.Length,
            recordings = x.Tracks.Count(t => t.RecordingId is not null), works = x.Tracks.Sum(t => t.Works.Length), personnel = x.Tracks.Sum(t => t.Credits.Length) }).ToArray(),
        fromCache = model?.Displayed?.FromCache, stale = model?.Displayed?.IsStale, trackDetail = fixtureTrack?.Evidence,
        trackRows = discs.Children.OfType<Expander>().Select(x => x.Content).OfType<ItemsRepeater>()
            .SelectMany(x => Enumerable.Range(0, Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(x))
                .Select(n => (Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(x, n) as Border)?.Child).OfType<MusicTrackRowView>())
            .Select(x => x.Evidence).ToArray(),
        candidateIds = model?.Match?.Search.Candidates.Select(x => x.Id).ToArray(), canConfirm = model?.CanConfirm,
        cover.SourceStatus, cover.HasImage, coverUrl = model?.Displayed?.Album.CoverUrl, width = ActualWidth, titleWidth = title.ActualWidth,
        scrollOffset = scroll.VerticalOffset, visibleDiscs = discs.Children.OfType<Expander>().Where(x => x.IsExpanded).Select(x => AutomationProperties.GetAutomationId(x)).ToArray(),
        entityActive, entity = entityActive ? entityView?.Evidence : null,
        coverFallback, trackScrollers = CountScrollers(tracksSection), realizedTracks = discs.Children.OfType<Expander>().Select(x => x.Content).OfType<ItemsRepeater>()
            .Select(x => new { count = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(x),
                rowHeights = Enumerable.Range(0, Math.Min(5, Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(x)))
                    .Select(n => (Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(x, n) as FrameworkElement)?.ActualHeight).ToArray() }).ToArray()
    };
    private static int CountScrollers(DependencyObject root)
    {
        int count = root is ScrollViewer ? 1 : 0;
        for (int i = 0; i < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(root); i++)
            count += CountScrollers(Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(root, i));
        return count;
    }
    public void Deactivate() { active = false; generation++; startPending = false; pendingAlbumOffset = null; albumRestoreVersion++; model?.Deactivate(); listening.Deactivate(); entityView?.Deactivate(); entityActive = false; cover.SourceKey = null; coverKey = null; requestedCover = null; ring.IsActive = false; }
    public void Dispose() { if (disposed) return; Deactivate(); disposed = true; if (model is not null) { model.PropertyChanged -= StateChanged; model.Dispose(); } listening.Dispose(); entityView?.Dispose(); cover.Pipeline = null; }
    private const string TrackTemplate = """
    <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:views="using:TheOrder.WinUI.Views">
      <Border Padding="0,7" BorderBrush="{ThemeResource DividerBrush}" BorderThickness="0,0,0,1">
        <views:MusicTrackRowView Row="{Binding}"/>
      </Border>
    </DataTemplate>
    """;
}
[Microsoft.UI.Xaml.Data.Bindable]
public sealed class MusicTrackRow(MusicBrainzTrack track, Func<MusicBrainzReference, Task>? navigate = null, int discPosition = 1)
{
    public MusicBrainzTrack Track => track;
    public int DiscPosition => discPosition;
    public Func<MusicBrainzReference, Task>? Navigate => navigate;
    public bool IsExpanded { get; set; }
    public string Number => track.Number;
    public string Title => track.Title;
    public string Artist => track.Artist;
    public string Duration => track.LengthMs is { } duration ? TimeSpan.FromMilliseconds(duration).ToString(duration >= 3600000 ? @"h\:mm\:ss" : @"m\:ss", CultureInfo.InvariantCulture) : "";
    public string Credits => string.Join(" · ", track.Credits.Select(MusicBrainzAlbumView.Credit));
}
// The row expands in its parent's viewport. It never introduces another ScrollViewer.
public sealed class MusicTrackRowView : StackPanel
{
    private StackPanel? detailBody;
    private bool compact;
    public static readonly DependencyProperty RowProperty = DependencyProperty.Register(nameof(Row), typeof(MusicTrackRow), typeof(MusicTrackRowView),
        new PropertyMetadata(null, (sender, _) => ((MusicTrackRowView)sender).Render()));
    public MusicTrackRow? Row { get => GetValue(RowProperty) as MusicTrackRow; set => SetValue(RowProperty, value); }
    public MusicTrackRowView() { Spacing = 8; HorizontalAlignment = HorizontalAlignment.Stretch; }
    internal void ExpandForFixture() { if (Row is { } row) { row.IsExpanded = true; Render(); } }
    internal object Evidence => new { position = Row?.Track.Position, expanded = Row?.IsExpanded, recording = Row?.Track.RecordingId,
        works = Row?.Track.Works.Select(x => new { x.Id, x.Title }).ToArray(),
        compact, width = ActualWidth, detailWidth = detailBody?.ActualWidth,
        links = Descendants(this).OfType<HyperlinkButton>().Select(InteractiveCardEvidence.Hit).ToArray() };
    private void Render()
    {
        Children.Clear(); if (Row is not { } row) return;
        var grid = new Grid { ColumnSpacing = 10 };
        foreach (var width in new[] { new GridLength(28), new GridLength(1, GridUnitType.Star), new GridLength(.38, GridUnitType.Star), new GridLength(48), new GridLength(40) })
            grid.ColumnDefinitions.Add(new() { Width = width });
        grid.RowDefinitions.Add(new() { Height = GridLength.Auto }); grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var number = Text(row.Number, 13); number.Opacity = .65; number.Margin = new(0, 5, 0, 0); grid.Children.Add(number);
        var name = Text(row.Title, 15); name.FontWeight = FontWeights.SemiBold; name.LineHeight = 23; name.Margin = new(0, 4, 0, 0); Grid.SetColumn(name, 1); grid.Children.Add(name);
        var artist = Text(row.Artist, 13); artist.Opacity = .72; artist.Visibility = row.Artist.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        Grid.SetColumn(artist, 2); grid.Children.Add(artist);
        var duration = Text(row.Duration, 13); duration.HorizontalAlignment = HorizontalAlignment.Right; duration.Opacity = .7; duration.Margin = new(0, 5, 0, 0);
        Grid.SetColumn(duration, 3); grid.Children.Add(duration);
        bool? previousCompact = null;
        void Reflow()
        {
            var next = grid.ActualWidth < 620;
            if (previousCompact == next) return; previousCompact = compact = next;
            grid.ColumnDefinitions[2].Width = next || row.Artist.Length == 0 ? new(0) : new(.38, GridUnitType.Star);
            Grid.SetColumn(artist, next ? 1 : 2); Grid.SetRow(artist, next ? 1 : 0);
            artist.Margin = next ? new(0, 4, 0, 0) : new(0, 5, 0, 0);
            Grid.SetColumnSpan(name, next ? 2 : 1);
        }
        grid.SizeChanged += (_, _) => Reflow(); Reflow();
        var details = new StackPanel { Spacing = 12, MaxWidth = 780, HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new(38, 4, 0, 8), Visibility = row.IsExpanded ? Visibility.Visible : Visibility.Collapsed };
        detailBody = details;
        var track = row.Track;
        var recordingBody = new StackPanel { Spacing = 8 };
        if (track.ArtistCredits.Length > 0)
            recordingBody.Children.Add(Labeled("曲目署名", MusicDetailFormatting.Artists(track.ArtistCredits, row.Navigate)));
        // Credits retains the previous cache contract; linked Work credits appear once under their Work.
        var workCredits = track.Works.SelectMany(x => x.Credits).ToHashSet();
        var recordingCredits = track.Credits.Where(x => !workCredits.Contains(x)).ToArray();
        if (recordingCredits.Length > 0)
            recordingBody.Children.Add(MusicDetailFormatting.Credits(recordingCredits, credit => Credit(credit, row.Navigate)));
        if (track.RecordingId is { } recording)
            recordingBody.Children.Add(Link("录音资料 →", new(MusicBrainzEntityKind.Recording, recording), row.Navigate, $"MusicTrackRecording-{row.DiscPosition}-{track.Position}"));
        if (recordingBody.Children.Count > 0) details.Children.Add(recordingBody);
        foreach (var work in track.Works)
        {
            var workBody = new StackPanel { Spacing = 8 };
            workBody.Children.Add(Labeled("音乐作品", Link(work.Title + " →", new(MusicBrainzEntityKind.Work, work.Id), row.Navigate, $"MusicTrackWork-{row.DiscPosition}-{track.Position}-{work.Id}")));
            var facts = string.Join(" · ", new[] { MusicDetailFormatting.WorkType(work.Type),
                work.Languages.Length > 0 ? string.Join(" / ", work.Languages.Select(MusicDetailFormatting.Language)) : null }.Where(x => !string.IsNullOrWhiteSpace(x)));
            if (facts.Length > 0) { var fact = Text(facts, 12); fact.Opacity = .7; fact.Margin = new(92, 0, 0, 0); workBody.Children.Add(fact); }
            if (work.Credits.Length > 0) workBody.Children.Add(MusicDetailFormatting.Credits(work.Credits, credit => Credit(credit, row.Navigate)));
            details.Children.Add(new Border { BorderBrush = (Microsoft.UI.Xaml.Media.Brush)Microsoft.UI.Xaml.Application.Current.Resources["DividerBrush"],
                BorderThickness = new(2, 0, 0, 0), Padding = new(14, 4, 0, 4), Child = workBody });
        }
        if (details.Children.Count > 0)
        {
            var toggle = new HyperlinkButton { Content = row.IsExpanded ? "收起" : "详情", Padding = new(0), MinHeight = 30,
                VerticalAlignment = VerticalAlignment.Top };
            AutomationProperties.SetName(toggle, row.Title + " · 制作与来源");
            AutomationProperties.SetAutomationId(toggle, $"MusicTrackDetails-{row.DiscPosition}-{row.Track.Position}");
            toggle.Click += (_, _) => { row.IsExpanded = !row.IsExpanded; details.Visibility = row.IsExpanded ? Visibility.Visible : Visibility.Collapsed; toggle.Content = row.IsExpanded ? "收起" : "详情"; };
            Grid.SetColumn(toggle, 4); grid.Children.Add(toggle);
        }
        Children.Add(grid); Children.Add(details);
    }
    private static Grid Labeled(string caption, FrameworkElement value)
    {
        var result = new Grid { ColumnSpacing = 14 };
        result.ColumnDefinitions.Add(new() { Width = new(78) }); result.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        var label = Text(caption, 13); label.Opacity = .65; label.Margin = new(0, 7, 0, 0); result.Children.Add(label);
        Grid.SetColumn(value, 1); result.Children.Add(value); return result;
    }
    private static UIElement Credit(MusicBrainzCredit credit, Func<MusicBrainzReference, Task>? navigate)
    {
        FrameworkElement result = credit.ArtistId is { } id
            ? Link(MusicDetailFormatting.CreditPerson(credit), new(MusicBrainzEntityKind.Artist, id), navigate)
            : Text(MusicDetailFormatting.CreditPerson(credit), 13);
        if (result is TextBlock) result.Margin = new(0, 6, 0, 6);
        ToolTipService.SetToolTip(result, credit.Role + " · " + credit.Name); return result;
    }
    private static HyperlinkButton Link(string label, MusicBrainzReference reference, Func<MusicBrainzReference, Task>? navigate, string? automationId = null)
    {
        var link = new HyperlinkButton { Content = LinkText(label), Padding = new(0, 5, 0, 5), MinHeight = 30,
            HorizontalAlignment = HorizontalAlignment.Left, HorizontalContentAlignment = HorizontalAlignment.Left };
        if (automationId is not null) AutomationProperties.SetAutomationId(link, automationId);
        link.Click += async (_, _) =>
        {
            try
            {
                if (navigate is not null) { await navigate(reference); return; }
                if (!MusicBrainzReference.TryParse(reference.Url, out _) || !await Windows.System.Launcher.LaunchUriAsync(new Uri(reference.Url)))
                    throw new InvalidOperationException();
            }
            catch { link.Content = LinkText(label + "（未能打开，可重试）"); }
        };
        return link;
    }
    private static TextBlock Text(string value, double size) => new() { Text = value, FontSize = size, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
    private static TextBlock LinkText(string value) => new() { Text = value, FontSize = 13, TextWrapping = TextWrapping.Wrap, IsHitTestVisible = false };
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (int i = 0; i < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(parent, i); yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
}
