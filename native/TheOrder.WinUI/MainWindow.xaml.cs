using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Windowing;
using TheOrder.Application;
using TheOrder.Presentation;
using TheOrder.Platform.Windows;
using TheOrder.WinUI.Views;
using TheOrder.WinUI.Startup;
using System.Diagnostics;
using System.Text.Json;

namespace TheOrder.WinUI;

public sealed partial class MainWindow : Window
{
    public WorkspaceViewModel ViewModel { get; }
    private LibraryPage libraryPage = null!;
    private DetailsPage detailsPage = null!;
    private SettingsPage settingsPage = null!;
    private StatisticsPage statisticsPage = null!;
    private WorkspacePage detailsParent = WorkspacePage.Library;
    private readonly AppUpdateViewModel updates;
    private DispatcherTimer? updateTimer;
    private bool updatesStopped;
    private Views.OnlinePage onlinePage = null!;
    private readonly OnlineViewModel online;
    private readonly RecognitionViewModel recognition;
    private ImportPage importPage = null!;
    private InstallationsPage installationsPage = null!;
    private readonly FilesViewModel files;
    public LaunchViewModel Launch { get; }
    public AppSessionHost Sessions { get; }
    private bool commandsBlocked;
    private string? lifecycleMessage;
    private readonly DispatcherTimer launchTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly InstallationNavigation installationNavigation;
    private readonly IImagePipeline images;
    private readonly CatalogPresentationService presentations;
    private readonly CatalogBrowseSettings browsingSettings;
    private readonly CatalogBrowsePolicy browsingPolicy;
    private readonly LanguageSettings languageSettings;
    private readonly ContentLanguageSettings contentLanguageSettings;
    private readonly GlobalLaunchSettings launchDefaults;
    private readonly LaunchPolicy launchPolicy;
    private bool navigating, synchronizing;
    private Task? initialization;
    private DispatcherTimer? diagnosticsTimer;
    private readonly SyntheticScenario? scenario;
    private readonly SyntheticOnline? onlineFixture;
    private readonly bool performanceFixture;
    private readonly NextMoeSettingsViewModel nextMoeSettings;
    private readonly BangumiSettingsViewModel bangumiSettings;
    private readonly LibraryBackupViewModel backups;
    private readonly DispatcherTimer backupTimer = new() { Interval = TimeSpan.FromMinutes(5) };
    public MainWindow(WorkspaceViewModel workspace, NativeConfiguration configuration, FilesViewModel files, IImagePipeline images, IOfflineFilesClient filesClient, OnlineViewModel online, RecognitionViewModel recognition, LaunchViewModel launch, AppSessionHost sessions, IMaterialPreferencesStore materialPreferences, IPersonalBannerStore personalBanners, OnlineCoverSelectionClient coverSelection, IRemoteCoverRegistry coverRegistry, IOnlineCatalog catalog, IEntityCatalog entities, CatalogPresentationService presentations, OfflineLibraryClient library, CatalogBrowseClient browsing, CatalogBrowsePolicy browsingPolicy, CatalogBrowseSettings browsingSettings, LanguageSettings languages, ContentLanguageSettings contentLanguages, LaunchFileDiscovery launchFiles, AppUpdateViewModel updates, NextMoeSettingsViewModel nextMoeSettings, LibraryBackupViewModel backups, LibrarySourcesService sources, WindowsLibrarySourcesWatcher sourceWatcher, LibraryExtrasClient extras, MetadataSourceClient metadataSources, WindowsLocalResources resources, IBangumiRelationsReader bangumiRelations, IBangumiSubjectDetailsReader bangumiSubjects, IRelatedSemanticReader relationSemantics, IMusicBrainzReader musicBrainz, IAniListReader aniList, IWatchLinkPreferencesStore watchPreferences, IBangumiWatchLinksReader watchReader, BangumiSettingsViewModel bangumiSettings, IBookBibliographyReader bookBibliography, IBangumiBookRelationsReader bookRelations, IBangumiEpisodesReader episodes, ICatalogReleaseReader catalogReleases, IMusicBrainzEntityReader musicEntities, GlobalLaunchSettings launchDefaults, LaunchPolicy launchPolicy, GameVideoReader gameVideos, IMediaIdentityDecisions identityDecisions, LibraryMigrationService migrationService, SyntheticScenario? scenario = null, SyntheticOnline? onlineFixture = null)
    {
        Sessions = sessions; this.launchDefaults = launchDefaults; this.launchPolicy = launchPolicy;
        this.nextMoeSettings = nextMoeSettings; this.bangumiSettings = bangumiSettings;
        this.backups = backups;
        this.sources = sources; this.sourceWatcher = sourceWatcher; this.extras = extras; this.metadataSources = metadataSources; this.resources = resources;
        this.updates = updates;
        this.scenario = scenario; this.online = online; this.onlineFixture = onlineFixture; Launch = launch;
        this.recognition = recognition;
        this.launchFiles = launchFiles; this.launchFilesClient = filesClient;
        this.files = files; this.images = images; this.presentations = presentations;
        this.browsingPolicy = browsingPolicy; this.browsingSettings = browsingSettings; languageSettings = languages; contentLanguageSettings = contentLanguages;
        recognition.PrepareImportedArtwork = presentations.PrepareAsync;
        presentations.Prepared += OnArtworkPrepared;
        this.materialPreferences = materialPreferences;
        recognitionPreferences = new TheOrder.Platform.Windows.RecognitionPreferencesStore(configuration);
        controlledAdvanced = true; // Glass is an ordinary setting; Windows policy still takes precedence.
        performanceFixture = configuration.Diagnostics && Program.Profile?.Manifest.ProfileKind == NativeProfileKind.Test
            && Environment.GetEnvironmentVariable("THE_ORDER_NATIVE_PERFORMANCE_OUTPUT") is not null;
        installationNavigation = new(files);
        ViewModel = workspace; InitializeComponent();
        AppTitleBar.Title = Title = AppReleaseIdentity.IsBeta ? $"The Order {AppReleaseIdentity.Version} · Beta" : "The Order";
        if (Program.Profile?.Manifest.ProfileKind == NativeProfileKind.Test && Environment.GetEnvironmentVariable("THE_ORDER_NATIVE_READ_ONLY_OAUTH_ACCEPTANCE") == "1")
            AppTitleBar.Title = Title = $"The Order {typeof(App).Assembly.GetName().Version!.ToString(3)} Test · 只读登录验收";
        updates.PropertyChanged += OnAppUpdateState;
        bangumiSettings.Access.Changed += OnBangumiAccessChanged;
        if (configuration.Profile is NativeProfileStore { Manifest.ProfileKind: NativeProfileKind.Production })
        {
            updateTimer = new() { Interval = TimeSpan.FromSeconds(15) };
            updateTimer.Tick += OnAutomaticUpdate;
        }
        Root.Language = UiText.Culture.Name;
        InitializeWindowChrome();
        Navigation.IsEnabled = false;
        UpdateStartupTheme();
        Root.ActualThemeChanged += (_, _) => UpdateStartupTheme();
        initializePages = () =>
        {
        libraryPage = new(workspace, configuration.Diagnostics, images) { OpenDetails = id => Navigate(WorkspacePage.Details, id), ResolveUnsaved = ResolveUnsavedAsync, OpenImport = () => Navigate(WorkspacePage.Import), QuickLaunch = QuickLaunchAsync };
        statisticsPage = new(() => extras.Snapshot) { OpenDetails = id => Navigate(WorkspacePage.Details, id, WorkspacePage.Statistics), OpenImport = () => Navigate(WorkspacePage.Import) };
        detailsPage = new(workspace, images, launch, recognition) { GoBack = GoBackAsync, ResolveUnsaved = ResolveUnsavedAsync, ActivateLaunch = ActivateLaunchAsync, ManageInstallations = async () => { if (workspace.SelectedId is { } id) await installationNavigation.OpenAsync(id, ResolveUnsavedAsync, () => Navigate(WorkspacePage.Installations)); },
            ChangeCover = async () => { if (await ResolveUnsavedAsync() && workspace.Draft?.Source is { } details && await PickFile(".png", ".jpg", ".jpeg") is { } path) await workspace.ExecuteFileMutationAsync(token => filesClient.CoverAsync(details, path, token), details.Id); } };
        detailsPage.ChooseWorkCover = () => ChooseWorkCoverAsync(coverSelection, coverRegistry);
        detailsPage.ConfigurePersonalSync(nextMoeSettings);
        detailsPage.ConfigureCatalog(browsing, browsing, coverRegistry, () => online.AuthGeneration, presentations, () => library.CurrentSnapshot, personalBanners, catalog as IRelatedGameIdentityReader);
        detailsPage.ConfigureBangumi(bangumiRelations, bangumiSubjects, relationSemantics, () => bangumiSettings.Access.Capture().Scope);
        detailsPage.ConfigureMusic(musicBrainz, musicEntities);
        detailsPage.ConfigureAniList(aniList, watchPreferences, watchReader);
        detailsPage.ConfigureBooks(bookBibliography, bookRelations);
        detailsPage.ConfigureGameVideos(gameVideos, identityDecisions);
        detailsPage.ConfigureEpisodes(episodes);
        detailsPage.ConfigureReleases(catalogReleases, () => online.AuthGeneration);
        detailsPage.ChangeBanner = () => ChangePersonalBannerAsync(personalBanners, restore: false);
        detailsPage.RestoreDefaultBanner = () => ChangePersonalBannerAsync(personalBanners, restore: true);
        detailsPage.RestoreDefaultCover = async () => { if (await ResolveUnsavedAsync() && workspace.Draft?.Source is { } details)
            await workspace.ExecuteFileMutationAsync(token => filesClient.CoverAsync(details, null, token), details.Id); };
        onlinePage = new(online, images, browsing, browsing, coverRegistry, presentations, () => library.CurrentSnapshot);
        onlinePage.ConfigureRelatedWorks(bangumiRelations, bangumiSubjects, relationSemantics, catalog as IRelatedGameIdentityReader, () => bangumiSettings.Access.Capture().Scope);
        onlinePage.ConfigureMusic(musicBrainz, musicEntities);
        onlinePage.ConfigureAniList(aniList, watchPreferences, watchReader);
        onlinePage.ConfigureBooks(bookBibliography, bookRelations);
        onlinePage.ConfigureEpisodes(episodes);
        onlinePage.ConfigureReleases(catalogReleases, () => online.AuthGeneration);
        onlinePage.NavigationChanged += RefreshBackNavigation;
        libraryPage.RecognizeSelected = async ids => { if (await ResolveUnsavedAsync()) { await recognition.StartAsync(ids, RecognitionMode.Reidentify); if (!ids.IsEmpty) await Navigate(WorkspacePage.Details, ids[0]); } };
        detailsPage.Recognize = async () => { if (workspace.SelectedId is { } id && await ResolveUnsavedAsync()) { await recognition.StartAsync([id], RecognitionMode.Reidentify); } };
        libraryPage.OpenPendingReview = OpenPendingReviewAsync;
        libraryPage.OpenSettings = async () => { await Navigate(WorkspacePage.Settings); settingsPage.RevealSources(); };
        libraryPage.RemoveSelected = ids => RemoveFromLibraryAsync(library, ids);
        recognition.ReviewCompleted = async (completed, next) =>
        {
            if (workspace.Page == WorkspacePage.Details && workspace.SelectedId == completed)
                await Navigate(next is null ? WorkspacePage.Library : WorkspacePage.Details, next);
        };
        recognition.PropertyChanged += OnRecognitionState;
        settingsPage = new(workspace, online, recognition, browsingSettings, languages, contentLanguages, updates, nextMoeSettings, backups, bangumiSettings, launchDefaults, new LibraryMigrationViewModel(migrationService)) { ChooseLaunchHelper = () => PickFile(".exe"), ChangeMaterialMode = SetMaterialModeAsync,
            ChooseBackupDestination = PickBackupDestination, ChooseBackupFile = () => PickFile(".json"),
            ChooseMigrationFile = () => PickFile(".zip"), ImportMigration = (preview, token) => ImportManagerLibraryAsync(preview, migrationService, token),
            PrepareBackupAction = ResolveUnsavedAsync, RestoreLibrary = preview => RestoreLibraryAsync(preview, library),
            InstallUpdate = package => App.Instance?.RequestUpgradeAsync(package) ?? Task.FromResult(false) };
        var managerExport = new TheOrder.Application.Migrations.LibraryManagerExportService(library,
            new TheOrder.Infrastructure.Legacy.NativeLibraryCodec(), new TheOrder.Infrastructure.Migration.ManagerArchiveWriter(), new WindowsManagerExportSink(configuration));
        settingsPage.PreviewManagerExport = managerExport.PreviewAsync;
        settingsPage.SaveManagerExport = managerExport.SaveAsync;
        settingsPage.ChooseManagerExportDestination = PickManagerExportDestination;
        settingsPage.CredentialSaved = async () =>
        {
            libraryPage.ShowCatalogReminder(online.CatalogReminder);
            if (online.CanAccessCatalog && recognition.UseOnline) _ = ResumeRecognitionAsync(retryPending: true);
            await Task.CompletedTask;
        };
        online.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(OnlineViewModel.CatalogReminder)) libraryPage.ShowCatalogReminder(online.CatalogReminder); };
        nextMoeSettings.Access.Changed += OnNextMoeAccessChanged;
        nextMoeSettings.IdentityChanged = async () => { await online.CheckCredentialAsync(); if (settingsPage.CredentialSaved is { } ready) await ready(); };
        var personalSyncTimer = new Microsoft.UI.Xaml.DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        personalSyncTimer.Tick += async (_, _) => { if (nextMoeSettings.Automatic) await nextMoeSettings.AutomaticAsync(); };
        personalSyncTimer.Start();
        Closed += (_, _) => { personalSyncTimer.Stop(); nextMoeSettings.Dispose(); };
        var bangumiRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        bangumiRefreshTimer.Tick += async (_, _) => { if (startupReady && !commandsBlocked && !Sessions.Closing) await bangumiSettings.RefreshIfDueAsync(); };
        bangumiRefreshTimer.Start();
        Closed += (_, _) => bangumiRefreshTimer.Stop();
        browsingPolicy.Changed += OnBrowsingChanged;
        detailsPage.RefreshOnline = async () => { if (await ResolveUnsavedAsync() && workspace.SelectedId is { } id) {
            await online.RefreshEntryAsync(id);
            if (workspace.PersistenceResult?.IsCommitted == true && workspace.Draft?.Source.Metadata is { } fresh)
            { await presentations.GetAsync(fresh.Id, refresh: true); presentations.Queue([fresh.Id]); detailsPage.RefreshPrepared(fresh.Id); }
        } };
        detailsPage.RemoveWishlist = async () => { if (workspace.SelectedId is { } id) await RemoveFromLibraryAsync(library, [id]); };
        files.RecognizeImported = recognition.StartImportedAsync;
        importPage = new(files, workspace, recognition) { ChooseFolder = PickFolder };
        installationsPage = new(files, workspace) { ChooseFolder = PickFolder, ChooseExecutable = () => PickFile(".exe", ".cmd") };
        ConfigureP1Pages();
        InitializeVisualSystem(configuration);
        workspace.ThemeChanged += ApplyTheme; workspace.PropertyChanged += OnState;
        ApplyTheme(); ShowPage();
        InitializeShellInput(configuration.Diagnostics);
        launchTimer.Tick += OnLaunchRefresh; launchTimer.Start();
        backupTimer.Tick += async (_, _) => { if (!commandsBlocked && Sessions.Ready && !Sessions.Closing) await backups.AutomaticAsync(); };
        if (configuration.Diagnostics)
        {
            DiagnosticsPanel.Visibility = Visibility.Visible;
            if (onlineFixture?.BoundaryFixture == true) RecognitionBoundaryControls.Visibility = Visibility.Visible;
            diagnosticsTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
            diagnosticsTimer.Tick += OnDiagnostics; diagnosticsTimer.Start();
        }
        };
        Root.Loaded += (_, _) =>
        {
            if (initialization is not null) return;
            var scale = Root.XamlRoot.RasterizationScale;
            var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest).WorkArea;
            var width = Math.Min((int)(1160 * scale), (int)(area.Width * .95));
            var height = Math.Min((int)(820 * scale), (int)(area.Height * .95));
            AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(area.X + (area.Width - width) / 2, area.Y + (area.Height - height) / 2, width, height));
            initialization = Initialize();
            if (performanceFixture) _ = RunPerformanceAfterInitializationAsync();
        };
    }
    private async Task Initialize()
    {
        try
        {
            await startup.PrepareAsync(async () =>
            {
                initializePages?.Invoke(); initializePages = null;
                await launchDefaults.InitializeAsync();
                await Sessions.InitializeAsync(); await ViewModel.InitializeAsync();
                if (!Sessions.Ready || !ViewModel.IsLoaded || ViewModel.Notice.Outcome == ClientOutcome.LoadFailure)
                    throw new IOException("Startup library unavailable");
                await LoadMaterialPreferencesAsync(); await LoadRecognitionPreferencesAsync(); await browsingSettings.InitializeAsync();
                await online.CheckCredentialAsync(); await updates.ReadReceiptAsync(); await nextMoeSettings.InitializeAsync(); await bangumiSettings.InitializeAsync();
                libraryPage.ShowCatalogReminder(online.CatalogReminder);
            }, () => libraryPage.WaitForInitialArtworkAsync(), NextStartupFrameAsync);
            StartupRing.IsActive = false; StartupSurface.Visibility = Visibility.Collapsed;
            Navigation.Opacity = 1; Navigation.IsHitTestVisible = true;
            if (!commandsBlocked && !Sessions.Closing) { updateTimer?.Start(); libraryPage.FocusResults(); }
            backgroundInitialization = InitializeBackgroundAsync();
        }
        catch (Exception exception)
        {
            App.TraceVisualFailure(exception); lifecycleMessage = UiText.Get("启动恢复未完成，已禁用操作；请退出后重试。"); ViewModel.ReportUnexpected();
            StartupRing.IsActive = false; StartupRing.Visibility = Visibility.Collapsed;
            StartupError.Text = "书架未能完整加载，原资料已保留。请退出后重试。";
            StartupError.Visibility = StartupExit.Visibility = Visibility.Visible;
        }
        RefreshSession();
        startupFinished.TrySetResult(startupReady);
    }
    public void SetCommandsBlocked(bool value)
    { commandsBlocked = value; nextMoeSettings.PauseCommands(value); if (value) { coverDialog?.Hide(); p1Dialog?.Hide(); settingsPage?.CancelBackupDialog(); settingsPage?.CancelMigrationDialog(); } PageContent.IsEnabled = !value; RefreshSession(); }
    public void ShowLifecycleError(string message) { lifecycleMessage = message; RefreshSession(); }
    public void CancelBackgroundRequests() { CancelOnlineRequests(); recognition.CancelAll(); files.CancelScan(); ViewModel.CancelPendingOperation(); nextMoeSettings.Cancel(); bangumiSettings.Cancel(); }
    public Task DrainPersonalRequestsAsync() => Task.WhenAll(nextMoeSettings.DrainAsync(), bangumiSettings.DrainAsync(), p1Operation);
    private async Task ResumeRecognitionAsync(bool retryPending)
    { try { await recognition.StartUnidentifiedAsync(retryPending); } catch (OperationCanceledException) { } catch { ViewModel.ReportUnexpected(); } }
    private async void OnPauseTiming(object sender, RoutedEventArgs e) { try { await Sessions.PauseAsync(); } catch { ShowLifecycleError(UiText.Get("暂停计时未完成，请重试。")); } RefreshSession(); }
    private async void OnResumeTiming(object sender, RoutedEventArgs e) { try { await Sessions.ResumeAsync(); } catch { ShowLifecycleError(UiText.Get("恢复计时未完成，请重试。")); } RefreshSession(); }
    private void RefreshSession()
    {
        Navigation.IsEnabled = startupReady && Sessions.Ready && !Sessions.Closing && !commandsBlocked;
        var value = SessionPresentation.From(Sessions.Session, Sessions.Library, Sessions.CanMutate && !commandsBlocked);
        var notice = lifecycleMessage ?? backgroundAudio.Failure;
        LifecycleNotice.IsOpen = notice is not null; LifecycleNotice.Message = notice ?? "";
        SessionTitle.Text = value.Title; SessionState.Text = Sessions.Error ?? (Sessions.Ready ? value.State : UiText.Get("正在恢复上次会话…"));
        SessionDuration.Text = value.Duration; SessionFlyoutDuration.Text = value.Duration; SessionHistory.Text = value.History;
        SessionChipTitle.Text = Sessions.Session?.Session is null ? (Sessions.Error is not null ? UiText.Get("会话异常") : UiText.Get("正在恢复会话")) : value.Title;
        SessionHistory.Visibility = string.IsNullOrEmpty(value.History) ? Visibility.Collapsed : Visibility.Visible;
        PauseTiming.IsEnabled = value.CanPause; ResumeTiming.IsEnabled = value.CanResume;
        UpdateSessionChrome(value);
        if (visualFixtureTimer is not null && round3TimerOverride is not null) { SessionDuration.Text = round3TimerOverride; SessionDuration.Visibility = Visibility.Visible; }
        App.Instance?.RefreshTray(value);
    }
    private DispatcherTimer? noticeTimer;
    private ClientResult? timedNotice;
    private void OnState(object? sender, PropertyChangedEventArgs e)
    {
        backups.RefreshAvailability();
        if (ViewModel.Page == WorkspacePage.Statistics && statisticsPage?.IsLoaded == true) _ = statisticsPage.RefreshAsync();
        if (e.PropertyName == nameof(WorkspaceViewModel.Page)) ShowPage();
        if (e.PropertyName == nameof(WorkspaceViewModel.Notice)) {
            noticeTimer?.Stop();
            NoticeBar.Severity = ViewModel.Notice.Outcome switch
            {
                ClientOutcome.Committed => InfoBarSeverity.Success, ClientOutcome.CommittedWithWarning or ClientOutcome.ReadOnly or ClientOutcome.Conflict or ClientOutcome.CandidateStale => InfoBarSeverity.Warning,
                ClientOutcome.ValidationFailure or ClientOutcome.IoFailure or ClientOutcome.LoadFailure => InfoBarSeverity.Error, _ => InfoBarSeverity.Informational
            };
            if (ViewModel.Notice.Outcome is ClientOutcome.Success or ClientOutcome.Committed) {
                timedNotice = ViewModel.Notice;
                if (noticeTimer is null) { noticeTimer = new() { Interval = TimeSpan.FromSeconds(5) }; noticeTimer.Tick += (_,_) => { noticeTimer.Stop(); if (ReferenceEquals(timedNotice, ViewModel.Notice)) NoticeBar.IsOpen = false; }; }
                noticeTimer.Start();
            }
        }
    }
    private void ShowPage()
    {
        synchronizing = true;
        LeaveGallery();
        // Swapchain owners release while still in the live tree (proven early-release order).
        if (ViewModel.Page != WorkspacePage.Library) libraryPage.ReleaseStageBeforeLeave();
        if (ViewModel.Page != WorkspacePage.Details) detailsPage.ReleaseStageBeforeLeave();
        PageContent.Content = ViewModel.Page switch { WorkspacePage.Statistics => statisticsPage, WorkspacePage.Online => onlinePage, WorkspacePage.Details => detailsPage, WorkspacePage.Settings => settingsPage, WorkspacePage.Import => importPage, WorkspacePage.Installations => installationsPage, _ => libraryPage };
        Navigation.SelectedItem = ViewModel.Page == WorkspacePage.Statistics || detailsParent == WorkspacePage.Statistics && ViewModel.Page is WorkspacePage.Details or WorkspacePage.Installations ? StatisticsNavigation : ViewModel.Page == WorkspacePage.Settings ? SettingsNavigation : ViewModel.Page == WorkspacePage.Online ? OnlineNavigation : LibraryNavigation;
        if (ViewModel.Page == WorkspacePage.Statistics) _ = statisticsPage.RefreshAsync();
        RefreshBackNavigation();
        synchronizing = false;
        AnimatePage();
    }
    private async Task Navigate(WorkspacePage page, string? id = null, WorkspacePage returnTo = WorkspacePage.Library)
    {
        if (!startupReady || commandsBlocked) return;
        if (page == WorkspacePage.Recognition) { await OpenPendingReviewAsync(); return; }
        installationNavigation.Invalidate();
        if (page != WorkspacePage.Online) online.Leave();
        if (navigating) return; navigating = true;
        try { if (await ResolveInstallationDraft()) { if (await ViewModel.NavigateAsync(page, id, ChooseUnsaved)) detailsParent = page == WorkspacePage.Details ? returnTo : page == WorkspacePage.Installations ? detailsParent : WorkspacePage.Library; ShowPage(); } }
        catch (Exception) { ViewModel.ReportUnexpected(); }
        finally { navigating = false; }
    }
    private async void OnNavigationInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        if (args.InvokedItemContainer is NavigationViewItem { Tag: "visual-gallery" }) { ShowGallery(); return; }
        if (args.InvokedItemContainer is NavigationViewItem { Tag: "visual-states" }) { ShowSessionStateFixtures(); return; }
        if (!synchronizing && args.InvokedItemContainer is NavigationViewItem item) await Navigate((item.Tag as string) switch { "statistics" => WorkspacePage.Statistics, "settings" => WorkspacePage.Settings, "online" => WorkspacePage.Online, "recognition" => WorkspacePage.Recognition, _ => WorkspacePage.Library });
    }
    private async void OnTitleBack(TitleBar sender, object args) => await GoBackAsync();
    private async void OnRailBack(object sender, RoutedEventArgs e) => await GoBackAsync();
    private void OnArtworkPrepared(string id) => DispatcherQueue.TryEnqueue(() => {
        if (presentations.PreparedWork(id) is { } work)
            ViewModel.RefreshPreparedCover(id, ImageSourceKeys.Remote(work.CoverUrl, work.FetchedAt));
        detailsPage.RefreshPrepared(id); onlinePage.RefreshPrepared(id);
    });
    private void OnRecognitionState(object? sender, PropertyChangedEventArgs e)
    { if (e.PropertyName == nameof(RecognitionViewModel.Reviews)) libraryPage.ShowPendingReviews(recognition.Reviews.Count);
        if (e.PropertyName == nameof(RecognitionViewModel.ProgressText)) libraryPage.ShowRecognition(recognition);
        if (recognitionPreferencesLoaded && !recognitionPreferencesReadOnly && e.PropertyName is nameof(RecognitionViewModel.UseOnline) or nameof(RecognitionViewModel.IncludePe)) QueueRecognitionPreferences(); }
    private async Task OpenPendingReviewAsync()
    {
        recognition.RefreshIfDirty();
        if (recognition.Reviews.FirstOrDefault() is { } first) await Navigate(WorkspacePage.Details, first.GameId);
    }
    private async Task RemoveFromLibraryAsync(OfflineLibraryClient library, System.Collections.Immutable.ImmutableArray<string> ids)
    {
        if (ids.IsDefaultOrEmpty || !await ResolveUnsavedAsync()) return;
        var dialog = new ContentDialog { XamlRoot = Root.XamlRoot, RequestedTheme = Root.ActualTheme,
            Title = $"从书架移除 {ids.Length} 部作品？", Content = "将移除所选作品的书架条目、个人编辑、安装和游玩记录。不会删除游戏文件。此操作不可撤销。",
            PrimaryButtonText = "从书架移除", CloseButtonText = "取消", DefaultButton = ContentDialogButton.Close };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        var result = await ViewModel.ExecuteFileMutationAsync(token => library.RemoveEntriesAsync(ids, token));
        if (result.IsCommitted)
        {
            await recognition.ForgetRemovedAsync(ids);
            if (ViewModel.SelectedId is { } selected && ids.Contains(selected)) await Navigate(WorkspacePage.Library);
        }
    }
    private void RefreshBackNavigation()
    {
        var canGoBack = ShellNavigationPolicy.CanGoBack(ViewModel.Page, onlinePage.IsBrowsing);
        RailBack.Visibility = canGoBack ? Visibility.Visible : Visibility.Collapsed;
        RailBack.IsEnabled = canGoBack && !commandsBlocked;
    }
    private async Task GoBackAsync()
    {
        if (!startupReady || commandsBlocked || !ShellNavigationPolicy.CanGoBack(ViewModel.Page, onlinePage.IsBrowsing)) return;
        if (ViewModel.Page == WorkspacePage.Details && await detailsPage.GoBackCatalogAsync()) return;
        if (ViewModel.Page == WorkspacePage.Online) { await onlinePage.GoBackAsync(); return; }
        if (ShellNavigationPolicy.Parent(ViewModel.Page, detailsParent) is { } parent)
            await Navigate(parent, parent == WorkspacePage.Details
                ? ShellNavigationPolicy.ParentGameId(ViewModel.Page, ViewModel.SelectedId, files.GameId) : null, detailsParent);
    }
    private async Task<UnsavedChoice> ChooseUnsaved()
    {
        var dialog = new ContentDialog { XamlRoot = Root.XamlRoot, RequestedTheme = Root.ActualTheme, Title = UiText.Get("保留尚未保存的修改？"), Content = UiText.Get("离开前可以保存修改，或放弃草稿。"), PrimaryButtonText = UiText.Get("保存并继续"), SecondaryButtonText = UiText.Get("放弃草稿"), CloseButtonText = UiText.Get("继续编辑"), DefaultButton = ContentDialogButton.Close };
        return await dialog.ShowAsync() switch { ContentDialogResult.Primary => UnsavedChoice.Save, ContentDialogResult.Secondary => UnsavedChoice.Discard, _ => UnsavedChoice.Stay };
    }
    public async Task<bool> ResolveUnsavedAsync() => !navigating && await ResolveLaunchDefaults() && await ResolveInstallationDraft() && await ViewModel.ResolveUnsavedAsync(ChooseUnsaved);
    private async Task<bool> ResolveLaunchDefaults()
    {
        if (!launchDefaults.HasChanges) return true;
        var dialog = new ContentDialog { XamlRoot = Root.XamlRoot, RequestedTheme = Root.ActualTheme,
            Title = "保留全局启动设置的修改？", Content = "辅助工具的默认行为尚未保存。",
            PrimaryButtonText = "保存并继续", SecondaryButtonText = "放弃修改", CloseButtonText = "继续编辑", DefaultButton = ContentDialogButton.Close };
        var choice = await dialog.ShowAsync();
        if (choice == ContentDialogResult.Secondary) { launchDefaults.Cancel(); return true; }
        if (choice == ContentDialogResult.Primary) { await launchDefaults.SaveAsync(); return !launchDefaults.HasChanges; }
        return false;
    }
    private async Task<bool> ResolveInstallationDraft()
    {
        await files.WaitForMutationAsync();
        if (files.Draft is null) return true;
        var dialog = new ContentDialog { XamlRoot = Root.XamlRoot, RequestedTheme = Root.ActualTheme, Title = UiText.Get("保留安装草稿？"), Content = UiText.Get("安装路径和参数尚未保存。"), PrimaryButtonText = UiText.Get("保存并继续"), SecondaryButtonText = UiText.Get("放弃草稿"), CloseButtonText = UiText.Get("继续编辑"), DefaultButton = ContentDialogButton.Close };
        var choice = await dialog.ShowAsync();
        if (choice == ContentDialogResult.Secondary) { files.CancelDraft(); return true; }
        if (choice == ContentDialogResult.Primary) { await files.SaveAsync(); return files.Draft is null; }
        return false;
    }
    private async Task<string?> PickFolder() => (await new Microsoft.Windows.Storage.Pickers.FolderPicker(AppWindow.Id) { CommitButtonText = UiText.Get("选择此文件夹") }.PickSingleFolderAsync())?.Path;
    private async Task<string?> PickFile(params string[] extensions)
    {
        var picker = new Microsoft.Windows.Storage.Pickers.FileOpenPicker(AppWindow.Id);
        foreach (var extension in extensions) picker.FileTypeFilter.Add(extension);
        return (await picker.PickSingleFileAsync())?.Path;
    }
    private void ApplyTheme()
    {
        Root.RequestedTheme = ViewModel.Theme switch { NativeTheme.Light => ElementTheme.Light, NativeTheme.Dark => ElementTheme.Dark, _ => ElementTheme.Default };
        ApplyMaterialPolicy();
    }
    public void CancelOnlineRequests() { online.Leave(); onlinePage?.Cancel(); detailsPage?.CancelCatalog(); }
    private void OnBrowsingChanged() { online.ResetBrowsing(); onlinePage.ResetBrowsing(); detailsPage.ResetBrowsing(); }
    private void OnLaunchRefresh(object? sender, object e)
    {
        Launch.Refresh(); RefreshSession();
        RefreshBackgroundAudio();
        recognition.RefreshIfDirty();
        if (startupReady && Sessions.Ready && !Sessions.Closing && !commandsBlocked)
            _ = ViewModel.SynchronizeSessionLibraryAsync(Sessions.Library);
    }
    private void OnNextMoeAccessChanged(object? sender, EventArgs e) => DispatcherQueue.TryEnqueue(() =>
    {
        if (Sessions.Closing) return;
        online.RefreshAccessState(); nextMoeSettings.RefreshAccessState();
    });
    private void OnBangumiAccessChanged() => DispatcherQueue.TryEnqueue(() => { bangumiSettings.RefreshState(); detailsPage?.InvalidateBangumiAccess(); onlinePage?.InvalidateBangumiAccess(); });
    public async ValueTask StopAsync()
    {
        settingsPage.CancelMigrationDialog(); await settingsPage.Migration.DisposeAsync();
        nextMoeSettings.Access.Changed -= OnNextMoeAccessChanged;
        bangumiSettings.Access.Changed -= OnBangumiAccessChanged; bangumiSettings.Dispose();
        detailsPage?.DisposeBangumi();
        onlinePage?.DisposeRelated();
        await StopP1Async();
        backupTimer.Stop();
        if (initialization is not null) await initialization;
        await backgroundInitialization;
        updatesStopped = true;
        if (updateTimer is not null) { updateTimer.Stop(); updateTimer.Tick -= OnAutomaticUpdate; }
        updates.PropertyChanged -= OnAppUpdateState;
        CancelOnlineRequests();
        await materialWrite;
        DisposeVisualSystem();
        DisposeShellInput();
        onlineFixture?.ReleaseCandidate();
        launchTimer.Stop(); launchTimer.Tick -= OnLaunchRefresh;
        await Launch.DisposeAsync();
        installationNavigation.Invalidate();
        if (diagnosticsTimer is not null) { diagnosticsTimer.Stop(); diagnosticsTimer.Tick -= OnDiagnostics; }
        noticeTimer?.Stop(); timedNotice = null;
        ViewModel.ThemeChanged -= ApplyTheme; ViewModel.PropertyChanged -= OnState;
        // Startup may still hold user preference overrides. Finish loading/merging them
        // before disposal cancels the lifetime, then drain the resulting persistence queue.
        if (initialization is not null) await initialization;
        recognition.PropertyChanged -= OnRecognitionState;
        presentations.Prepared -= OnArtworkPrepared;
        await recognitionPreferenceWrite;
        await recognitionPreferences.DisposeAsync();
        browsingPolicy.Changed -= OnBrowsingChanged;
        await browsingSettings.DisposeAsync();
        await languageSettings.DisposeAsync();
        await contentLanguageSettings.DisposeAsync();
        await launchDefaults.DisposeAsync();
        await recognition.DisposeAsync();
        await presentations.DisposeAsync();
        await online.DisposeAsync();
        await files.DisposeAsync();
        await backups.DrainAsync();
        await backups.AutomaticAsync(onExit: true);
        await backups.DisposeAsync();
        await ViewModel.DisposeAsync();
    }
    private void OnDelayDetails(object sender, RoutedEventArgs e) => scenario?.DelayDetails();
    private void OnDelayCandidate(object sender, RoutedEventArgs e) => onlineFixture?.DelayCandidate();
    private void OnReleaseCandidate(object sender, RoutedEventArgs e) => onlineFixture?.ReleaseCandidate();
    private async void OnSyntheticAuthChange(object sender, RoutedEventArgs e)
    { if (onlineFixture?.BoundaryFixture == true) await online.SaveCredentialAsync("synthetic-only-replacement"); }
    private void OnFailRefresh(object sender, RoutedEventArgs e) => scenario?.FailNextCommittedRefresh();
    private void OnDelayBatch(object sender, RoutedEventArgs e) => scenario?.DelayBatch();
    private void OnDelayQuery(object sender, RoutedEventArgs e) => scenario?.DelayQuery();
    private void OnDelayScan(object sender, RoutedEventArgs e) => scenario?.DelayScan();
    private void OnFailReload(object sender, RoutedEventArgs e) => scenario?.FailNextReload();
    private void OnRelease(object sender, RoutedEventArgs e) => scenario?.Release();
    private void OnCancelOperation(object sender, RoutedEventArgs e) => ViewModel.CancelPendingOperation();
    private void OnDiagnostics(object? sender, object e)
    {
        if (performanceFixture)
        {
            double now = Program.StartupClock.Elapsed.TotalMilliseconds;
            if (performanceLastUiTick > 0 && performanceUiGaps.Count < 32768) performanceUiGaps.Add(now - performanceLastUiTick);
            performanceLastUiTick = now;
        }
        using var process = Process.GetCurrentProcess();
        var state = ViewModel.IsLoading ? "loading" : !ViewModel.IsLoaded ? "error" : ViewModel.IsReadOnly ? "read-only"
            : ViewModel.IsSearching ? "searching" : ViewModel.TotalCount == 0 ? "empty" : ViewModel.Items.Count == 0 ? "zero-results" : "normal";
        var snapshot = new
        {
            schema = 1, timestamp = DateTimeOffset.UtcNow, processId = Environment.ProcessId,
            visual = VisualEvidence(),
            launch = new { outcome = Launch.Runtime.Outcome.ToString(), tracking = Launch.Runtime.Tracking.ToString(), game = Launch.Runtime.Snapshot?.Game.GameId,
                operation = Launch.Runtime.Snapshot?.OperationId, generation = Launch.Runtime.Snapshot?.Generation, initialPid = Launch.Runtime.Initial?.Pid,
                currentPid = Launch.Runtime.Current?.Pid, creationTime = Launch.Runtime.Current?.CreationFileTime.ToString(System.Globalization.CultureInfo.InvariantCulture), created = Launch.Runtime.ProcessCreated,
                startMs = Launch.Runtime.StartMilliseconds, attachMs = Launch.Runtime.AttachMilliseconds, handoffMs = Launch.Runtime.HandoffMilliseconds,
                exitOccurredAtUtc = Launch.Runtime.ExitOccurredAtUtc,
                lastRequest = Launch.LastRequestOutcome?.ToString(), selected = Launch.SelectedInstallationId, availability = Launch.Availability.ToString() },
            build = typeof(App).Assembly.ManifestModule.ModuleVersionId.ToString(),
            session = new { id = Sessions.Session?.Session?.Id, lifecycle = Sessions.Session?.Session?.Native?.Lifecycle.ToString(), paused = Sessions.Session?.Session?.Native?.UserPaused,
                installation = Sessions.Session?.Session?.Native?.InstallationId, recoveryCount = Sessions.Session?.Session?.Native?.Recovery.Count, seconds = Sessions.Session?.Session?.Seconds,
                tracking = Sessions.Session?.Tracking.ToString(), timing = Sessions.Session?.Timing.ToString(), error = Sessions.Error,
                pid = Sessions.Session?.Current?.Pid, creationTime = Sessions.Session?.Current?.CreationFileTime.ToString(System.Globalization.CultureInfo.InvariantCulture),
                accumulated100ns = Sessions.Session?.Session?.Native?.Accumulated100ns.ToString(System.Globalization.CultureInfo.InvariantCulture),
                count = Sessions.Library?.Entries.FirstOrDefault(e => e.Id == Sessions.Session?.Session?.Native?.GameId)?.PlayCount,
                totalSeconds = Sessions.Library?.Entries.FirstOrDefault(e => e.Id == Sessions.Session?.Session?.Native?.GameId)?.TotalPlaySeconds },
            lifecycle = new { ready = Sessions.Ready, closing = Sessions.Closing, activations = Program.Activations, tray = App.Instance?.TrayAvailable, hidden = App.Instance?.WindowHidden },
            checkpoint = typeof(App).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false).OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion,
            itemCount = ViewModel.Items.Count, totalCount = ViewModel.TotalCount, viewMode = ViewModel.Layout,
            theme = Root.ActualTheme.ToString(), readOnly = ViewModel.IsReadOnly, state, page = galleryVisible ? visualScreen : ViewModel.Page.ToString(),
            viewport = libraryPage.RuntimeViewport(), workingSetBytes = process.WorkingSet64, privateBytes = process.PrivateMemorySize64,
            processCpuMs = process.TotalProcessorTime.TotalMilliseconds, processHandles = process.HandleCount,
            dirty = ViewModel.IsDirty, saving = ViewModel.IsSaving, detailsLoading = ViewModel.IsDetailsLoading,
            preferenceState = ViewModel.PreferenceState.ToString(), savedFilterCount = ViewModel.SavedFilters.Count,
            notice = ViewModel.Notice.Message, persistence = ViewModel.PersistenceResult?.Outcome.ToString(), refresh = ViewModel.LastRefresh.ToString(),
            scenario = scenario?.Status ?? "disabled"
            , importStage = files.Stage.ToString(), candidateCount = files.Candidates.Count,
            scanRoot = files.ScanRoot, scanDirectories = files.Progress?.Directories ?? 0, scanEntries = files.Progress?.Entries ?? 0,
            duplicateCandidates = files.Candidates.Count(c => c.Preflight.Duplicate is not null),
            installationCount = files.Installations?.Rows.Length ?? 0,
            missingInstallations = files.Installations?.Rows.Count(r => r.Status is PreflightStatus.NotFound or PreflightStatus.BrokenLink) ?? 0,
            installationDraft = files.Draft is not null, imageMetrics = images.Metrics,
            localCoverCount = ViewModel.Items.Count(i => i.HasLocalCover),
            onlineState = online.SearchState.ToString(), onlineCount = online.Items.Count, onlineDetails = online.DetailState.ToString(), onlineWorkId = online.Details?.Id, onlineHasCover = online.DetailCoverKey is not null, onlineConnection = online.ConnectionState.ToString(),
            onlineAuthGeneration = online.AuthGeneration, onlineCredentialPresent = online.HasCredential, syntheticDetailRequests = onlineFixture?.DetailRequests,
            catalogBrowsing = new { showNsfw = browsingSettings.ShowNsfw, generation = browsingPolicy.Capture().Generation, canChange = browsingSettings.CanChange, message = browsingSettings.Message },
            globalLaunch = new { saved = launchDefaults.Saved, canChange = launchDefaults.CanChange, hasChanges = launchDefaults.HasChanges, message = launchDefaults.Message },
            contentLanguage = new { preferred = ContentLanguage.Preferred, fallback = ContentLanguage.Current.Fallback, canChange = contentLanguageSettings.CanChange },
            language = new { effective = UiText.Language, selected = languageSettings.Selected, system = languageSettings.SystemLanguage, canChange = languageSettings.CanChange, restartRequired = UiText.Resolve(languageSettings.Selected, languageSettings.SystemLanguage) != UiText.Language },
            onlineQueryHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(online.Query.Trim()))),
            recognition = new {
                progress = recognition.Progress, reviewCount = recognition.Reviews.Count,
                selectedGame = recognition.SelectedGameId, selectedReview = recognition.SelectedId,
                status = recognition.SelectedReview?.Status.ToString(), candidateCount = recognition.Candidates.Count,
                candidates = recognition.SelectedReview?.Result?.Candidates.Select(c => new { id = c.Work.Id, high = c.HighConfidence, veto = c.Vetoed, uncertain = c.VersionNeedsConfirmation, score = c.Confidence }),
                selectedCandidate = recognition.SelectedCandidateId, canApply = recognition.CanApply,
                displayedCandidate = recognition.DisplayedCandidateId, remoteFacts = recognition.RemoteFacts, hasRemoteCover = recognition.RemoteCoverKey is not null,
                localCandidates = recognition.SelectedReview?.Result?.Identity.LocalCandidates.Length,
                unavailable = recognition.SelectedReview?.Result?.EnrichmentUnavailable,
                algorithm = recognition.SelectedReview?.AlgorithmVersion, decision = recognition.LastDecision?.Outcome.ToString(),
                lastJob = recognition.LastJobState?.ToString(), online = recognition.UseOnline, persistenceError = recognition.PersistenceError
            }
        };
        RuntimeState.Text = "THE_ORDER_RUNTIME " + JsonSerializer.Serialize(snapshot);
    }
}
