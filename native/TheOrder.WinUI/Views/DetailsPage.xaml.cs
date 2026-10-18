using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TheOrder.Presentation;
using TheOrder.Application;

namespace TheOrder.WinUI.Views;

public sealed record DetailCharacterCard(string Name, string Role, string Voice, string? ImageKey);
public sealed record DetailSourceLink(string Title, string Url);

public sealed partial class DetailsPage : Page, IImageHost
{
    public WorkspaceViewModel ViewModel { get; }
    public IImagePipeline Images { get; }
    public LaunchViewModel Launch { get; }
    public RecognitionViewModel Recognition { get; }
    private string? displayedGame;
    private GameDetails? displayedDetail;
    private BannerChoice? heroArtwork;
    private readonly BannerPresentation heroPresentation;
    private bool synchronizingReview;
    public Func<Task>? ManageInstallations { get; set; }
    public Func<Task>? OpenLaunchSettings { get; set; }
    private async void OnLaunchSettings(object sender, RoutedEventArgs e) { try { if (OpenLaunchSettings is not null) await OpenLaunchSettings(); } catch { ViewModel.ReportUnexpected(); } }
    public Func<Task>? OpenGameTools { get; set; }
    private async void OnGameTools(object sender, RoutedEventArgs e) { try { if (OpenGameTools is not null) await OpenGameTools(); } catch { ViewModel.ReportUnexpected(); } }
    public Func<Task>? ActivateLaunch { get; set; }
    public Func<Task>? RefreshOnline { get; set; }
    public Func<Task>? Recognize { get; set; }
    private async void OnRecognize(object sender, RoutedEventArgs e) { try { if (Recognize is not null) await Recognize(); } catch (Exception) { ViewModel.ReportUnexpected(); } }
    public Func<Task>? RemoveWishlist { get; set; }
    private async void OnRefreshOnline(object sender, RoutedEventArgs e) { try { if (RefreshOnline is not null) await RefreshOnline(); } catch { ViewModel.ReportUnexpected(); } }
    private async void OnRemoveWishlist(object sender, RoutedEventArgs e) { try { if (RemoveWishlist is not null) await RemoveWishlist(); } catch { ViewModel.ReportUnexpected(); } }
    public Func<Task>? ChangeCover { get; set; }
    public Func<Task>? ChooseWorkCover { get; set; }
    public Func<Task>? RestoreDefaultCover { get; set; }
    public Func<Task>? ChangeBanner { get; set; }
    public Func<Task>? RestoreDefaultBanner { get; set; }
    private async void OnBanner(object sender, RoutedEventArgs e) { try { if (ChangeBanner is not null) await ChangeBanner(); } catch { ViewModel.ReportUnexpected(); } }
    private async void OnDefaultBanner(object sender, RoutedEventArgs e) { try { if (RestoreDefaultBanner is not null) await RestoreDefaultBanner(); } catch { ViewModel.ReportUnexpected(); } }
    private async void OnWorkCover(object sender, RoutedEventArgs e) { try { if (ChooseWorkCover is not null) await ChooseWorkCover(); } catch { ViewModel.ReportUnexpected(); } }
    private async void OnDefaultCover(object sender, RoutedEventArgs e) { try { if (RestoreDefaultCover is not null) await RestoreDefaultCover(); } catch { ViewModel.ReportUnexpected(); } }
    private async void OnInstallations(object sender, RoutedEventArgs e) { try { if (ManageInstallations is not null) await ManageInstallations(); } catch (Exception) { ViewModel.ReportUnexpected(); } }
    private void OnLaunchMenuOpening(object sender, object e)
    {
        LaunchInstallationsMenu.Items.Clear(); LaunchInstallationsMenu.IsEnabled = Launch.CanActivate;
        LaunchInstallationsMenu.Visibility = Launch.Installations.Length > 1 ? Visibility.Visible : Visibility.Collapsed;
        var game = Launch.GameId;
        foreach (var installation in Launch.Installations)
        {
            var item = new ToggleMenuFlyoutItem { Text = installation.Label, IsChecked = installation.Id == Launch.SelectedInstallationId };
            item.Click += async (_, _) => { try { if (game == Launch.GameId) await Launch.SelectAsync(installation.Id); } catch { ViewModel.ReportUnexpected(); } };
            LaunchInstallationsMenu.Items.Add(item);
        }
    }
    private async void OnCover(object sender, RoutedEventArgs e) { try { if (ChangeCover is not null) await ChangeCover(); } catch (Exception) { ViewModel.ReportUnexpected(); } }
    public Func<Task<bool>>? ResolveUnsaved { get; set; }
    private async void OnFinishEditing(object sender, RoutedEventArgs e)
    {
        try { if (!ViewModel.IsDirty || ResolveUnsaved is not null && await ResolveUnsaved()) { SetEditing(false); EditDetailsButton.Focus(FocusState.Programmatic); } }
        catch (Exception) { ViewModel.ReportUnexpected(); }
    }
    public Func<Task>? GoBack { get; set; }
    public DetailsPage(WorkspaceViewModel viewModel, IImagePipeline images, LaunchViewModel launch, RecognitionViewModel recognition)
    {
        ViewModel = viewModel; Images = images; Launch = launch; Recognition = recognition; InitializeComponent();
        RichCatalog.Sources = SourcesInfo;
        HeroTitle.RegisterPropertyChangedCallback(TextBlock.TextProperty, (_, _) => UpdateHeroFrame(HeroFrame.ActualWidth));
        ReviewHost.Content = new RecognitionReview(recognition, images);
        // Current banner preference: no refraction; a lightly blurred, readable overlay.
        HeroStage.Variant = StageVariant.Unrefracted; HeroStage.AmbientSigma = 18; HeroStage.StageCornerRadius = 8;
        HeroStage.DecodeEdge = 1200; HeroStage.Images = images; HeroStage.Plate = HeroPlate;
        HeroStage.NaturalLandscape = true;
        heroPresentation = new(HeroFrame, HeroStage);
        heroPresentation.SetPending(true);
        HeroStage.SourceChanged += () => UpdateHeroFrame(HeroFrame.ActualWidth);
        // The plate can be light over bright artwork while the app is dark. Shared text styles alone
        // do not guarantee that polarity, so use the stage foreground while art is visible and return
        // to normal Fluent/HighContrast styles when it is not.
        HeroStage.ForegroundChanged += brush =>
        {
            HeroPlate.RequestedTheme = brush is null ? ElementTheme.Default
                : HeroStage.Polarity == StagePolarity.Deepen ? ElementTheme.Dark : ElementTheme.Light;
            HeroFacts.SetTextForeground(brush);
            if (brush is null) HeroPersonalDivider.ClearValue(Border.BackgroundProperty);
            else HeroPersonalDivider.Background = brush;
            foreach (var text in new[] { HeroTitle, HeroStatusText, HeroRatingText, HeroPlayText, LastPlayedText, PersonalLabelsText })
                if (brush is null) text.ClearValue(TextBlock.ForegroundProperty); else text.Foreground = brush;
        };
        Loaded += async (_, _) =>
        {
            ViewModel.PropertyChanged += OnState; Recognition.PropertyChanged += OnRecognitionState; displayedDetail = null; SynchronizeHero();
            try { if (ViewModel.SelectedId is { } id) await Launch.OpenAsync(id); } catch { ViewModel.ReportUnexpected(); }
        };
        Unloaded += (_, _) => { ViewModel.PropertyChanged -= OnState; Recognition.PropertyChanged -= OnRecognitionState; Recognition.SelectForGame(null); Launch.Leave(); CancelCatalog(); };
    }
    private void OnState(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(WorkspaceViewModel.Draft) or nameof(WorkspaceViewModel.SelectedId) or nameof(WorkspaceViewModel.IsDetailsLoading)) SynchronizeHero();
    }
    private void SynchronizeHero()
    {
        var detail = ViewModel.Draft?.Source;
        // Cancel as soon as navigation starts, before another local detail is ready.
        if (ViewModel.IsDetailsLoading) BangumiRelated.Deactivate();
        HeroStage.AmbientSigma = 0;
        if (displayedGame != detail?.Id) { displayedGame = detail?.Id; SetEditing(false); }
        EditDetailsButton.IsEnabled = EditorCard.Visibility != Visibility.Visible && ViewModel.CanEditDetails;
        if (!ReferenceEquals(displayedDetail, detail)) {
        displayedDetail = detail;
        _ = RefreshPersonalFactsAsync();
        var work = detail?.Metadata;
        HeroFacts.SetWork(work, detail?.Personal.Name);
        SynchronizeCatalog(detail);
        PersonalNotesText.Text = detail?.Personal.Notes ?? "";
        NotesSection.Visibility = ActionVisible(!string.IsNullOrWhiteSpace(PersonalNotesText.Text));
        PersonalLabelsText.Text = detail is null ? "" : string.Join(" · ", detail.Personal.Categories.Concat(detail.Personal.Tags));
        PersonalLabelsText.Visibility = ActionVisible(PersonalLabelsText.Text.Length > 0);
        LastPlayedText.Text = UiText.Get("最近游玩 · ") + LastPlayedNames.Display(detail?.LastPlayedAt, detail?.HasPlayHistory ?? false, DateTimeOffset.Now);
        ArtworkSourceText.Text = string.Join("\n", new[] { detail?.Relationship, detail?.CoverDescription, detail?.BackdropDescription });
        SourceLinks.ItemsSource = work is null ? null : CandidateDetailLinks.From(work).Select(l => new DetailSourceLink(l.Title, l.Url)).ToArray();
        }
        SynchronizeGameVideos(detail);
        if (!ViewModel.IsDetailsLoading) SynchronizeBangumi();
        SynchronizeReview();
        var title = ViewModel.Draft?.Source.Personal.Name ?? "";
        Breadcrumb.ItemsSource = new[] { UiText.Get("书架"), title.Length > 40 ? title[..40] + "…" : title };
    }
    private void OnRecognitionState(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    { if (e.PropertyName == nameof(RecognitionViewModel.Reviews)) SynchronizeReview(); }
    private void SynchronizeReview()
    {
        if (synchronizingReview || !IsLoaded) return;
        synchronizingReview = true;
        try { Recognition.SelectForGame(ViewModel.Draft?.Source.Id); ReviewSection.Visibility = ActionVisible(Recognition.SelectedGameId == ViewModel.Draft?.Source.Id && Recognition.HasSelection); }
        finally { synchronizingReview = false; }
    }
    private void SetEditing(bool value)
    { EditorCard.Visibility = ActionVisible(value); ReadOnlyDetails.Visibility = ActionVisible(!value); EditDetailsButton.IsEnabled = !value && ViewModel.CanEditDetails; }
    private void OnEdit(object sender, RoutedEventArgs e) { SetEditing(true); NameEditor.Focus(FocusState.Programmatic); }
    internal void BeginEditing() => SetEditing(true);
    private async void OnSourceLink(object sender, RoutedEventArgs e)
    {
        if (sender is not HyperlinkButton { Tag: string url } || !CandidateDetailLinks.Allowed(url)) return;
        try { if (!await Windows.System.Launcher.LaunchUriAsync(new Uri(url))) ViewModel.ReportUnexpected(); } catch { ViewModel.ReportUnexpected(); }
    }
    private async void OnBreadcrumb(BreadcrumbBar sender, BreadcrumbBarItemClickedEventArgs args)
    { try { if (args.Index == 0 && GoBack is not null) await GoBack(); } catch { ViewModel.ReportUnexpected(); } }
    private async void OnLaunch(object sender, RoutedEventArgs e) { try { if (ActivateLaunch is not null) await ActivateLaunch(); else await Launch.LaunchAsync(); } catch { ViewModel.ReportUnexpected(); } }
    private async void OnConfirmPosition(object sender, RoutedEventArgs e) { try { await Launch.ConfirmAsync(); } catch { ViewModel.ReportUnexpected(); } }
    private void OnCancelLaunch(object sender, RoutedEventArgs e) => Launch.Cancel();
    // 完全清透 hero stage: the window routes policy (effects allowed and visible) and early release here.
    internal void SetStagePolicy(bool effectsAllowed, bool highContrast, MaterialMode mode) { HeroStage.SetPolicy(effectsAllowed, highContrast, mode); browser?.SetStagePolicy(effectsAllowed, highContrast, mode); }
    internal void ReleaseStageBeforeLeave() => HeroStage.ReleaseBeforeLeave();
    internal Task<object> AuditStageAsync() => HeroStage.AuditLifecycleAsync();
    internal object FramePresentationEvidence => new { game = ViewModel.Draft?.Source.Id, frameOpacity = HeroFrame.Opacity,
        catalogPending = heroPresentation.Pending, revealQueued = heroPresentation.Queued,
        artwork = HeroStage.CoverKey is not null, ready = HeroStage.HasCurrentPresentation,
        effective = HeroStage.Effective, stageWidth = HeroStage.ActualWidth, stageHeight = HeroStage.ActualHeight,
        safeToShow = !heroPresentation.Pending && (HeroStage.CoverKey is null || HeroStage.HasCurrentPresentation) };
    internal object StageEvidence => new { placement = "Details hero", frameOpacity = HeroFrame.Opacity, presentationPending = heroPresentation.Pending, revealQueued = heroPresentation.Queued, catalogDataMilliseconds, catalogPrepared, catalogMediaPrepared, cardImages = RichCatalog.ImageEvidence, width = HeroStage.ActualWidth, height = HeroStage.ActualHeight, aspect = HeroStage.ActualHeight > 0 ? HeroStage.ActualWidth / HeroStage.ActualHeight : 0, hasCg = HeroStage.CoverKey is not null, textScreened = heroArtwork?.TextScreened, artworkSourceId = heroArtwork?.SourceWorkId, artworkReference = heroArtwork?.ReferenceTitle, catalogBrowser = CatalogHost.Visibility == Visibility.Visible ? browser?.Current : null, catalogHero = browser?.HeroEvidence, tagGroups = browser?.ShelfEvidence, overlayBounds = HeroOverlayBounds, overlay = new { title = HeroTitle.Text, titleWidth = HeroTitle.ActualWidth, titleHeight = HeroTitle.ActualHeight, titleFontSize = HeroTitle.FontSize, titleMaxLines = HeroTitle.MaxLines, titleWrapping = HeroTitle.TextWrapping.ToString(), zIndex = Canvas.GetZIndex(HeroLayout), facts = HeroFacts.Evidence }, actions = HeroActions.Children.OfType<FrameworkElement>().Where(e => e.Visibility == Visibility.Visible).Select(e => new { name = e.Name, width = e.ActualWidth, height = e.ActualHeight, x = e.TransformToVisual(HeroActions).TransformPoint(new Windows.Foundation.Point()).X, y = e.TransformToVisual(HeroActions).TransformPoint(new Windows.Foundation.Point()).Y }).ToArray(), plateTheme = HeroPlate.RequestedTheme.ToString(), plateWidth = HeroPlate.ActualWidth, plateHeight = HeroPlate.ActualHeight, stage = HeroStage.Evidence };
    private void OnDetailsSizeChanged(object sender, SizeChangedEventArgs e)
    {
        // Reflow the existing controls; drafts, launch state and image ownership stay in place.
        var narrow = e.NewSize.Width < 720;
        DetailsColumn.Padding = narrow ? new Thickness(16, 8, 16, 16) : new Thickness(32, 12, 32, 28);
        TopHeaderBar.ColumnDefinitions[1].Width = narrow ? new GridLength(0) : GridLength.Auto;
        Grid.SetColumn(HeroActions, narrow ? 0 : 1);
        Grid.SetRow(HeroActions, narrow ? 1 : 0);
        HeroActions.HorizontalAlignment = narrow ? HorizontalAlignment.Left : HorizontalAlignment.Right;
        var compactEditor = e.NewSize.Width < 600;
        EditorCard.Padding = compactEditor ? new Thickness(16, 16, 16, 16) : new Thickness(24, 20, 24, 20);
        EditorHeader.ColumnDefinitions[1].Width = compactEditor ? new GridLength(0) : GridLength.Auto;
        Grid.SetColumn(EditorHeaderActions, compactEditor ? 0 : 1);
        Grid.SetRow(EditorHeaderActions, compactEditor ? 1 : 0);
        EditorHeaderActions.ColumnDefinitions[0].Width = compactEditor ? new GridLength(1, GridUnitType.Star) : GridLength.Auto;
        PersonalFields.ColumnDefinitions[1].Width = compactEditor ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        PersonalFields.ColumnDefinitions[2].Width = compactEditor ? new GridLength(0) : GridLength.Auto;
        Grid.SetColumn(RatingEditor, compactEditor ? 0 : 1); Grid.SetRow(RatingEditor, compactEditor ? 1 : 0);
        Grid.SetColumn(FavoriteEditor, compactEditor ? 0 : 2); Grid.SetRow(FavoriteEditor, compactEditor ? 2 : 0);
    }
    private object HeroOverlayBounds => new { desiredHeight = HeroPlate.DesiredSize.Height,
        elements = new FrameworkElement[] { HeroTitle, HeroFacts, HeroStatusText, HeroRatingText, HeroPlayText, LastPlayedText, PersonalLabelsText }
            .Where(e => e.Visibility == Visibility.Visible && e.ActualHeight > 0).Select(e => {
                var point = e.TransformToVisual(HeroPlate).TransformPoint(new Windows.Foundation.Point());
                return new { name = e.Name, x = point.X, y = point.Y, width = e.ActualWidth, height = e.ActualHeight,
                    insidePlate = point.X >= -.5 && point.Y >= -.5 && point.X + e.ActualWidth <= HeroPlate.ActualWidth + .5 && point.Y + e.ActualHeight <= HeroPlate.ActualHeight + .5 };
            }).ToArray() };
    private void OnHeroSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateHeroFrame(e.NewSize.Width);
    }
    private void SetHeroArtwork(BannerChoice? choice) { heroArtwork = choice; HeroStage.CoverKey = choice?.Key; UpdateHeroFrame(HeroFrame.ActualWidth); }
    private void UpdateHeroFrame(double width)
    {
        if (width < 1) return;
        var frame = BannerFrameLayout.ArtworkFrame(HeroFrame, HeroStage, heroArtwork);
        var compact = frame.Width < 560; var dense = frame.Height is > 0 and < 340;
        HeroTitleLayout.Fit(HeroTitle, HeroPlate, frame.Width, compact, dense);
        HeroStatusText.FontSize = HeroRatingText.FontSize = compact ? 12 : 13;
        HeroFacts.Dense = dense;
        HeroFacts.Compact = compact;
        BannerFrameLayout.Apply(HeroFrame, HeroStage, HeroLayout, HeroPlate, heroArtwork);
    }
    public string StatusLabel(string status) => UiText.Get("当前状态 · ") + StatusNames.Display(status);
    private static string CharacterRole(string role) => role.Trim().ToLowerInvariant() switch
    { "main" => UiText.Get("主角"), "secondary" => UiText.Get("配角"), "appears" or "cameo" => UiText.Get("客串"), _ => role };
    public string AlternateTitle(string primary, string other) => string.Equals(primary?.Trim(), other?.Trim(), StringComparison.Ordinal) ? "" : other ?? "";
    public Visibility AlternateTitleVisible(string primary, string other) => string.IsNullOrWhiteSpace(AlternateTitle(primary, other)) ? Visibility.Collapsed : Visibility.Visible;
    public Visibility HasInstallations(int count) => count > 0 ? Visibility.Visible : Visibility.Collapsed;
    public Visibility NoInstallations(int count) => count == 0 ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ActionVisible(bool enabled) => enabled ? Visibility.Visible : Visibility.Collapsed;
    public Visibility TextVisible(string? text) => !string.IsNullOrWhiteSpace(text) ? Visibility.Visible : Visibility.Collapsed;
    // The accent slot shows 启动游戏 unless the existing launch state asks for confirmation (then 确认启动位置).
    public Visibility LaunchVisible(int installations, bool confirm) => installations > 0 && !confirm ? Visibility.Visible : Visibility.Collapsed;
    // Saved rating only; the editor's in-progress text never reaches the hero.
    public string RatingLabel(double? rating) => rating is null ? UiText.Get("未评分") : UiText.Format($"{rating:0.#} 分");
    public string DirtyText(bool dirty) => dirty ? UiText.Get("有尚未保存的修改") : UiText.Get("当前资料已保存");
    private async void OnSave(object sender, RoutedEventArgs e) { await ViewModel.SaveAsync(); if (!ViewModel.IsDirty) { SetEditing(false); EditDetailsButton.Focus(FocusState.Programmatic); } }
    private async void OnCancel(object sender, RoutedEventArgs e)
    {
        await ViewModel.CancelAndReloadDetailsAsync();
        if (!ViewModel.IsDirty) { SetEditing(false); EditDetailsButton.Focus(FocusState.Programmatic); }
    }
}
