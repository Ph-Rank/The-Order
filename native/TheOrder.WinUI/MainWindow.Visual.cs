using System.Text.Json;
using TheOrder.WinUI.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using TheOrder.Application;
using TheOrder.Domain;
using TheOrder.Presentation;
using TheOrder.WinUI.Materials;

namespace TheOrder.WinUI;

public sealed partial class MainWindow
{
    private MaterialPolicy? materialPolicy;
    private RefractionGallery? gallery;
    private bool requestedGlass;
    private long pageSwapCount;
    private bool visualWindowVisible = true;
    private bool galleryVisible, visualStopped, fixtureBusy;
    private string direction = "B", fixtureNonce = "";
    private string visualScreen = "VisualGallery";
    private string? fixtureDirectory;
    private DispatcherTimer? visualFixtureTimer;
    private object? firstStartupFrame;
    private Task? startupFrameCapture;
    private ResourceDictionary? visualTokens;
    private bool pageMotionFailed, failPageMotionForTest;
    private string? pageMotionFailure;
    private long pageAnimationCount;

    private void InitializeVisualSystem(NativeConfiguration configuration)
    {
        materialPolicy = new(DispatcherQueue, ApplyMaterialPolicy);
        if (Program.Profile?.Manifest.ProfileKind == NativeProfileKind.Test
            && Environment.GetEnvironmentVariable("THE_ORDER_NATIVE_VISUAL_TEST") == "1")
        {
            // Exercise the first no-motion policy application, before any page animation exists.
            materialPolicy.ReducedMotion = Environment.GetEnvironmentVariable("THE_ORDER_NATIVE_REDUCED_MOTION_TEST") == "1";
            failPageMotionForTest = Environment.GetEnvironmentVariable("THE_ORDER_NATIVE_PAGE_MOTION_FAILURE_TEST") == "1";
            ReadSystemAccentOnce();
            direction = Environment.GetEnvironmentVariable("THE_ORDER_NATIVE_VISUAL_DIRECTION") == "A" ? "A" : "B";
            var directory = Environment.GetEnvironmentVariable("THE_ORDER_NATIVE_VISUAL_CONTROL");
            if (!string.IsNullOrWhiteSpace(directory) && Path.IsPathFullyQualified(directory))
            {
                fixtureDirectory = Path.GetFullPath(directory);
                Directory.CreateDirectory(fixtureDirectory);
                var previousRequest = Path.Combine(fixtureDirectory, "request.json");
                try
                {
                    if (File.Exists(previousRequest) && new FileInfo(previousRequest).Length <= 16384)
                    {
                        using var previous = JsonDocument.Parse(File.ReadAllText(previousRequest));
                        fixtureNonce = previous.RootElement.GetProperty("nonce").GetString() ?? "";
                    }
                }
                catch (Exception) { /* An incomplete old Test request has no authority. */ }
                visualFixtureTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
                visualFixtureTimer.Tick += OnVisualFixture;
                if (Root.IsLoaded) StartVisualFixture(Root, new RoutedEventArgs());
                else Root.Loaded += StartVisualFixture;
            }
            if (configuration.Diagnostics && !CleanPlaygroundPreview)
            {
            var item = TestRailItem("材质", Symbol.Pictures, "visual-gallery");
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(item, "VisualGallery");
            Navigation.FooterMenuItems.Add(item);
            var states = TestRailItem("会话", Symbol.Play, "visual-states");
            Navigation.FooterMenuItems.Add(states);
            }
        }
        UpdateNavigationChrome();
        ApplyMaterialPolicy();
    }

    private static NavigationViewItem TestRailItem(string label, Symbol icon, string tag)
    {
        var content = new StackPanel { Spacing = 6, HorizontalAlignment = HorizontalAlignment.Center };
        content.Children.Add(new SymbolIcon(icon) { Width = 23, Height = 23, HorizontalAlignment = HorizontalAlignment.Center });
        content.Children.Add(new TextBlock { Text = label, FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center });
        return new Views.RailNavigationItem { Content = content, Tag = tag, Height = 74, Padding = new Thickness(0), HorizontalContentAlignment = HorizontalAlignment.Center };
    }
    // One compact labeled rail at every window width.
    private void OnNavigationDisplayModeChanged(NavigationView sender, NavigationViewDisplayModeChangedEventArgs args) => UpdateNavigationChrome();
    private void UpdateNavigationChrome()
    {
        if (ContentLayer is null) return;
        ContentLayer.CornerRadius = new CornerRadius(0);
        ContentLayer.BorderThickness = new Thickness(0);
        QueueRailIndicator();
    }
    private void OnPaneToggle(TitleBar sender, object args) => Navigation.IsPaneOpen = !Navigation.IsPaneOpen;
    private void OnShellSizeChanged(object sender, SizeChangedEventArgs args) => QueueRailIndicator();

    private void StartVisualFixture(object sender, RoutedEventArgs e)
    {
        visualFixtureTimer?.Start();
        if (fixtureDirectory is not null)
        {
            CompositionTarget.Rendering += RecordFirstStartupFrame;
            File.WriteAllText(Path.Combine(fixtureDirectory, "fixture-ready.json"), JsonSerializer.Serialize(new { ready = true, isolatedTest = true }));
        }
    }

    private void RecordFirstStartupFrame(object? sender, object e)
    {
        CompositionTarget.Rendering -= RecordFirstStartupFrame;
        var logoBounds = StartupLogoStage.TransformToVisual(Root).TransformBounds(
            new Windows.Foundation.Rect(0, 0, StartupLogoStage.ActualWidth, StartupLogoStage.ActualHeight));
        var ringCenter = StartupRing.TransformToVisual(Root).TransformPoint(
            new Windows.Foundation.Point(StartupRing.ActualWidth / 2, StartupRing.ActualHeight / 2));
        firstStartupFrame = new
        {
            theme = Root.ActualTheme.ToString(),
            loading = StartupSurface.Visibility == Visibility.Visible,
            titleBarHasExtraContent = TitleBarHasExtraContent,
            backdrop = windowBackdrop.UsingMica ? "Mica" : "Solid",
            background = OpaqueCanvas.Visibility == Visibility.Visible
                ? (OpaqueCanvas.Background as SolidColorBrush)?.Color.ToString()
                : null,
            caption = AppWindow.TitleBar.PreferredTheme.ToString(),
            logo = (StartupLogo.Source as Microsoft.UI.Xaml.Media.Imaging.BitmapImage)?.UriSource?.Segments.LastOrDefault(),
            logoSize = logoBounds.Width,
            logoCenter = new { x = logoBounds.X + logoBounds.Width / 2, y = logoBounds.Y + logoBounds.Height / 2 },
            ringCenter = new { x = ringCenter.X, y = ringCenter.Y }
        };
        var captureOrbit = Environment.GetEnvironmentVariable("THE_ORDER_NATIVE_STARTUP_CAPTURE") == "1";
        if (StartupSurface.Visibility == Visibility.Visible && (captureOrbit || CaptureStartupTransition))
            startupFrameCapture = CaptureStartupFramesAsync(captureOrbit);
    }

    private bool CaptureStartupTransition => visualFixtureTimer is not null &&
        Environment.GetEnvironmentVariable("THE_ORDER_NATIVE_STARTUP_FADE_CAPTURE") == "1";

    private async Task CaptureStartupHomeFadeAsync()
    {
        if (!CaptureStartupTransition) return;
        try
        {
            await NextStartupFrameAsync();
            await Task.Delay(104);
            if (startupHomeFade is not null) await CaptureRound3Async("startup-home-fade");
        }
        catch (Exception error) { App.TraceVisualFailure(error); }
    }

    private async Task CaptureStartupFramesAsync(bool captureOrbit)
    {
        try
        {
            // This runs only in the isolated visual fixture. The real startup never waits for the sweep.
            await CaptureRound3Async("startup");
            if (!captureOrbit || !Views.InteractionMotion.Enabled || materialPolicy?.HighContrast == true) return;

            var deadline = System.Diagnostics.Stopwatch.StartNew();
            while (startupLogoLighting is null && deadline.ElapsedMilliseconds < 1000)
                await Task.Delay(16);
            if (startupLogoLighting is not { } lighting) return;

            var elapsed = lighting.GetCurrentTime();
            var captureClock = System.Diagnostics.Stopwatch.StartNew();
            foreach (var step in new[] { 0, 2, 4, 6, 8, 10, 12 })
            {
                var delay = TimeSpan.FromMilliseconds(step * StartupLightStepMilliseconds + 24) - elapsed - captureClock.Elapsed;
                if (delay > TimeSpan.Zero) await Task.Delay(delay);
                await CaptureRound3Async($"startup-orbit-{step}");
            }
        }
        catch (Exception error) { App.TraceVisualFailure(error); }
    }

    private void ApplyMaterialPolicy()
    {
        if (visualStopped || materialPolicy is null) return;
        RefreshPlaygroundPolicy();
        var effectiveMode = EffectiveMaterialMode;
        Views.InteractionMotion.ApplyPolicy(materialPolicy.MotionAllowed, materialPolicy.HighContrast);
        materialPolicy.AppOpaque = effectiveMode == Views.MaterialMode.Opaque;
        materialPolicy.Apply(Microsoft.UI.Xaml.Application.Current.Resources);
        RefreshCaptionTheme();
        if (startupHomeFade is not null && !CanAnimateStartupHome) HideStartupSurface();
        UpdateStartupTheme();
        RefreshStartupLogoLighting();
        var visible = visualWindowVisible && AppWindow.IsVisible && App.Instance?.WindowHidden != true;
        // Win10 uses a solid window canvas. That does not disable supported in-app
        // Acrylic or the artwork-owned banner glass; their existing policy is independent.
        ApplyBackdrop(materialPolicy.EffectsAllowed);
        // Hidden windows hold no glass renderer: stages drop to their XAML layers and release swapchains.
        var stageAllowed = materialPolicy.EffectsAllowed && visible;
        var highContrast = materialPolicy.HighContrast;
        libraryPage.SetStagePolicy(stageAllowed, highContrast, effectiveMode);
        detailsPage.SetStagePolicy(stageAllowed, highContrast, effectiveMode);
        onlinePage.SetStagePolicy(stageAllowed, highContrast, effectiveMode);
        statisticsPage.SetHighContrast(highContrast);
        settingsPage.ShowMaterialState(materialMode, materialPolicy.EffectsAllowed, highContrast,
            controlledAdvanced, materialReadOnly || !materialInitialized, materialPreferenceNotice);
        gallery?.SetAdvanced(materialPolicy.EffectsAllowed && visible);
        gallery?.SetMotionAllowed(materialPolicy.MotionAllowed && visible);
        if (!materialPolicy.MotionAllowed && Root.XamlRoot is not null)
        {
            ResetPageMotion();
            QueueRailIndicator();
        }
    }

    internal void RefreshSystemMaterialPolicy() => materialPolicy?.NotifyNativeSettingsChanged();

    internal void SetVisualWindowVisible(bool visible)
    {
        visualWindowVisible = visible;
        if (!visible) { detailsPage.SuspendGameVideos(); onlinePage.SuspendGameVideos(); }
        ApplyMaterialPolicy();
    }

    private static (string Glyph, string BrushKey) SessionVisualFacts(NativeSession native, SessionTracking tracking, SessionTiming timing)
    {
        if (native.Lifecycle == SessionLifecycle.Completed) return ("\uE73E", "SuccessTextBrush");
        if (native.Lifecycle == SessionLifecycle.Incomplete) return ("\uE783", "WarningTextBrush");
        if (native.Lifecycle == SessionLifecycle.RecoveryPending) return ("\uE777", "RecoveryTextBrush");
        if (native.UserPaused) return ("\uE769", "WarningTextBrush");
        if (tracking != SessionTracking.LaunchScope) return ("\uE7BA", "WarningTextBrush");
        return timing == SessionTiming.Running ? ("\uE768", "SuccessTextBrush") : ("\uE823", "TextSecondaryBrush");
    }

    private Brush ResolveSessionBrush(string key, Brush fallback)
    {
        // Resolve only our dictionary, never materialize lazy system resources.
        var resources = Microsoft.UI.Xaml.Application.Current.Resources;
        if (visualTokens is null)
        {
            foreach (var dictionary in resources.MergedDictionaries)
                if (dictionary.Source?.ToString().EndsWith("Resources/DesignTokens.xaml", StringComparison.OrdinalIgnoreCase) == true) { visualTokens = dictionary; break; }
        }
        var theme = materialPolicy?.HighContrast == true ? "HighContrast" : Root.ActualTheme.ToString();
        if (visualTokens?.ThemeDictionaries.TryGetValue(theme, out var value) == true && value is ResourceDictionary tokens
            && tokens.TryGetValue(key, out var brush) && brush is Brush result) return result;
        return fallback;
    }

    private void AnimatePage()
    {
        galleryVisible = false;
        if (pageMotionFailed || materialPolicy?.MotionAllowed != true || Root.XamlRoot is null || !startupReady) return;
        InteractionMotion.EnterPage(PageContent, () =>
        {
            if (failPageMotionForTest)
            {
                failPageMotionForTest = false;
                throw new ArgumentException("Synthetic page-motion failure after opacity started.");
            }
            pageAnimationCount++;
        }, error =>
        {
            pageMotionFailed = true;
            pageMotionFailure = $"{error.GetType().Name}: 0x{error.HResult:X8}";
            App.Instance?.LogPageMotionFailure(error);
        });
    }

    private void ResetPageMotion() => InteractionMotion.StopPage(PageContent);

    private object PageMotionEvidence() => new
    {
        failed = pageMotionFailed, failure = pageMotionFailure, animationCount = pageAnimationCount,
        opacity = Root.XamlRoot is null ? 1f : ElementCompositionPreview.GetElementVisual(PageContent).Opacity,
        entrance = InteractionMotion.PageEvidence(PageContent)
    };

    private void LeaveGallery()
    {
        if (gallery is not null && ReferenceEquals(PageContent.Content, gallery)) gallery.ReleaseBeforeLeave();
    }

    private void ShowGallery()
    {
        if (visualFixtureTimer is null || visualStopped) return;
        gallery ??= new(materialPolicy?.EffectsAllowed == true, requestedGlass);
        detailsPage.ReleaseStageBeforeLeave();
        PageContent.Content = gallery; galleryVisible = true;
        visualScreen = "VisualGallery";
        ApplyMaterialPolicy();
    }

    private bool SkipCaptureTreeProbe => visualFixtureTimer is not null &&
        Environment.GetEnvironmentVariable("THE_ORDER_NATIVE_CAPTURE_SKIP_TREE_PROBE") == "1";
    private object VisualEvidence() => new
    {
        contentLanguage = ContentLanguage.Current, direction, isolatedTest = visualFixtureTimer is not null, galleryVisible, visualScreen = galleryVisible ? visualScreen : ViewModel.Page.ToString(),
        startup = new { ready = startupReady, failed = StartupHasFailed, libraryLoaded = ViewModel.IsLoaded, sessionsReady = Sessions.Ready, loading = StartupSurface.Visibility == Visibility.Visible, firstFrame = firstStartupFrame, logoLightPlayed = startupLogoLightPlayed }, pageMotion = PageMotionEvidence(),
        material = materialPolicy?.Evidence(), gallery = gallery?.ResourceCounts, materialMode = materialMode.ToString(), effectiveMaterialMode = EffectiveMaterialMode.ToString(), controlledAdvanced,
        diagnosticsPolling = diagnosticsTimer is not null, backdrop = SystemBackdrop?.GetType().Name ?? "Solid",
        gameVideos = detailsPage.GameVideoEvidence,
        catalogPreparation = presentations.Metrics, stages = new { library = libraryPage.StageEvidence, details = detailsPage.StageEvidence, online = onlinePage.StageEvidence }, shell = ShellEvidence(),
        round3 = Round3Evidence(), managed = MaterialManagedEvidence(), displayBitmaps = Views.DisplayBitmaps.Evidence(images), bitmapSurfaces = SkipCaptureTreeProbe ? (int?)null : bitmapTreeProbe.Read(() => CountMaterialBitmaps(Root)), pendingNavigation = navigating, pendingDetails = ViewModel.IsDetailsLoading, staleCovers = SkipCaptureTreeProbe ? (int?)null : staleTreeProbe.Read(() => CountStaleCoverSources(Root)), captureTreeProbeSkipped = SkipCaptureTreeProbe,
        updates = new { updates.SelectedChannel, updates.SupportsChannelSwitch, updates.InstallText, updates.IsChannelChange, updates.CurrentVersion, latest = updates.Latest?.Version, updates.Message, updates.Highlights, updates.CanCheck, updates.CanInstall, updates.CanCancel, updates.IsBusy, updates.Progress, badgeVisible = statusPage.HasNotice("app-update") },
        nextMoe = new { mode = nextMoeSettings.Status?.Mode.ToString(), nextMoeSettings.Status?.NeedsLogin, nextMoeSettings.TimeDirection, nextMoeSettings.StateDirection,
            nextMoeSettings.FolderDirection, nextMoeSettings.ReadRatings, nextMoeSettings.Automatic, nextMoeSettings.HasPending, nextMoeSettings.HasPreview, nextMoeSettings.PreviewSummary, nextMoeSettings.Message,
            rows = nextMoeSettings.Rows.Select(x => new { x.Title, x.Values, x.Explanation, x.Choice }) },
        settings = settingsPage.LayoutEvidence(),
        treeProbe = new { bitmapFailures = bitmapTreeProbe.Failures, bitmapError = bitmapTreeProbe.LastError, staleFailures = staleTreeProbe.Failures, staleError = staleTreeProbe.LastError },
        glassWindowVisible = AppWindow.IsVisible, glassWindowActive = MaterialWindowActive, accent = TestAccentEvidence(),
        widthDip = Root.ActualWidth, heightDip = Root.ActualHeight,
        dpiScale = Root.XamlRoot?.RasterizationScale,
        foregroundSource = "existing SessionPresentation and WorkspaceViewModel"
    };

    // Explicit Test-only, bounded command fixture. It calls the same page/query
    // operations as the UI, never launches games or accepts arbitrary code.
    private async void OnVisualFixture(object? sender, object e)
    {
        if (fixtureBusy || fixtureDirectory is null || visualStopped || !Sessions.Ready || !startupReady) return;
        var request = Path.Combine(fixtureDirectory, "request.json");
        if (!File.Exists(request) || new FileInfo(request).Length > 16384) return;
        fixtureBusy = true;
        try
        {
            using var document = JsonDocument.Parse(await File.ReadAllTextAsync(request));
            var root = document.RootElement;
            var nonce = root.GetProperty("nonce").GetString() ?? "";
            if (nonce == fixtureNonce || nonce.Length is 0 or > 80) return;
            fixtureNonce = nonce;
            var action = root.GetProperty("action").GetString();
            switch (action)
            {
                case "page":
                    if (Enum.TryParse<WorkspacePage>(root.GetProperty("value").GetString(), out var page) && Enum.IsDefined(page))
                        await Navigate(page, root.TryGetProperty("id", out var id) ? id.GetString() : null);
                    else throw new InvalidDataException("Unknown page.");
                    break;
                case "theme":
                    if (!Enum.TryParse<NativeTheme>(root.GetProperty("value").GetString(), out var theme) || !Enum.IsDefined(theme)) throw new InvalidDataException("Unknown theme.");
                    await ViewModel.ChangeAppearanceAsync(theme, ViewModel.Layout); break;
                case "layout":
                    var layout = root.GetProperty("value").GetString();
                    if (layout is not ("grid" or "list")) throw new InvalidDataException("Unknown layout.");
                    await ViewModel.ChangeAppearanceAsync(ViewModel.Theme, layout); break;
                case "resize":
                    if (AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter && presenter.State != Microsoft.UI.Windowing.OverlappedPresenterState.Restored)
                        presenter.Restore();
                    var scale = Root.XamlRoot.RasterizationScale;
                    var pgWorkArea = Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(AppWindow.Id, Microsoft.UI.Windowing.DisplayAreaFallback.Nearest).WorkArea;
                    int pgWidth = Math.Min((int)(Math.Clamp(root.GetProperty("width").GetInt32(), 480, 1600) * scale), pgWorkArea.Width);
                    int pgHeight = Math.Min((int)(Math.Clamp(root.GetProperty("height").GetInt32(), 480, 1000) * scale), pgWorkArea.Height);
                    AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(pgWorkArea.X + (pgWorkArea.Width - pgWidth) / 2, pgWorkArea.Y + (pgWorkArea.Height - pgHeight) / 2, pgWidth, pgHeight)); break;
                case "solid": materialPolicy!.ForceSolid = root.GetProperty("value").GetBoolean(); ApplyMaterialPolicy(); break;
                case "motion": materialPolicy!.ReducedMotion = root.GetProperty("value").GetBoolean(); ApplyMaterialPolicy(); break;
                case "gallery": ShowGallery(); break;
                case "states": ShowSessionStateFixtures(); break;
                case "glass": var glassValue = root.GetProperty("value").GetString() ?? ""; if (glassValue is "on" or "off") requestedGlass = glassValue == "on"; gallery?.TestCommand(glassValue); break;
                case "accent": SetTestAccent(root.GetProperty("value").GetString() ?? ""); break;
                case "galleryNew": gallery?.Dispose(); gallery = null; ShowGallery(); break;
                case "hide": SetVisualWindowVisible(false); AppWindow.Hide(); break;
                case "show": AppWindow.Show(); Activate(); SetVisualWindowVisible(true); break;
                case "replay": await MaterialReplayAsync(root.GetProperty("value")); break;
                case "galleryStep": gallery?.StepPattern(root.GetProperty("value").GetDouble()); break;
                case "search": await ViewModel.SetQueryAsync(ViewModel.Query with { Text = root.GetProperty("value").GetString() ?? "" }); break;
                case "filter": libraryPage.ShowVisualFilter(); break;
                case "closeFilter": libraryPage.HideVisualFilter(); break;
                case "capture": await CaptureRound3Async(root.GetProperty("value").GetString() ?? ""); break;
                case "dialog": ShowRound3Dialog(); break;
                case "closeDialog": round3Dialog?.Hide(); break;
                case "focusSearch": libraryPage.OpenSearch(); break;
                case "editName":
                    var name = root.GetProperty("value").GetString() ?? "";
                    if (name.Length > 2000 || ViewModel.Draft is null) throw new InvalidDataException("Test draft unavailable or name too long.");
                    ViewModel.Draft.Name = name; break;
                case "saveDraft":
                    if (!(await ViewModel.SaveAsync()).IsCommitted) throw new InvalidDataException("Test save did not commit.");
                    break;
                case "accessibilityProbe":
                    await File.WriteAllTextAsync(Path.Combine(fixtureDirectory, "accessibility.json"), JsonSerializer.Serialize(AccessibilityEvidence())); break;
                case "materialMode":
                    if (!Enum.TryParse<Views.MaterialMode>(root.GetProperty("value").GetString(), out var mode) || !Enum.IsDefined(mode)) throw new InvalidDataException("Unknown material mode.");
                    await SetMaterialModeAsync(mode); break;
                case "scroll": libraryPage.ScrollTest(root.GetProperty("value").GetDouble()); break;
                case "accentEvent": SimulateSystemAccentEvent(root.GetProperty("value").GetString() ?? ""); break;
                case "selection": libraryPage.SetSelectionModeTest(root.GetProperty("value").GetBoolean()); break;
                case "contentLanguage":
                    var language = root.GetProperty("value");
                    await contentLanguageSettings.ChangeAsync(new(language.GetProperty("preferred").GetString() ?? "", language.GetProperty("fallback").GetString() ?? "original"));
                    break;
                case "probe": break;
                case "statistics": await StatisticsFixtureAsync(root); break;
                case "interactions": await InteractionFixtureAsync(root); break;
                case "playground": await PlaygroundFixtureAsync(root); break;
                case "playgroundClean": DiagnosticsPanel.Visibility = Visibility.Collapsed; break;
                case "musicScroll": await MusicScrollFixtureAsync(root); break;
                case "walkthrough": await WalkthroughFixtureAsync(root); break;
                case "acgnControl": await RunAcgnControlFixtureAsync(root); break;
                case "audioGuardSmoke": await RunAudioGuardFixtureAsync(root); break;
                case "bangumi":
                    if (Program.Profile?.Manifest.ProfileKind != NativeProfileKind.Test || ViewModel.Page != WorkspacePage.Details)
                        throw new InvalidOperationException("Related works fixture requires isolated Test details.");
                    BangumiRelationKind? relatedKind = null;
                    if (root.TryGetProperty("kind", out var requestedKind))
                    {
                        if (!Enum.TryParse<BangumiRelationKind>(requestedKind.GetString(), out var parsedKind) || !Enum.IsDefined(parsedKind))
                            throw new InvalidDataException("Unknown related media kind.");
                        relatedKind = parsedKind;
                    }
                    await detailsPage.BangumiFixtureAsync(root.GetProperty("value").GetString() ?? "", relatedKind, root.TryGetProperty("subjectId", out var relatedSubject) ? relatedSubject.GetString() : null);
                    await File.WriteAllTextAsync(Path.Combine(fixtureDirectory, "bangumi-evidence.json"), JsonSerializer.Serialize(detailsPage.BangumiEvidence));
                    if (!detailsPage.HasExclusiveContentBody) throw new InvalidOperationException("Details replacement bodies overlap.");
                    break;
                case "checkUpdate": await Navigate(WorkspacePage.Settings); await updates.CheckAsync(); settingsPage.RevealUpdates(); break;
                case "updateChannel":
                    if (!Enum.TryParse<AppUpdateChannel>(root.GetProperty("value").GetString(), false, out var requestedChannel) || !Enum.IsDefined(requestedChannel)) throw new InvalidDataException("Unknown channel");
                    await Navigate(WorkspacePage.Settings); settingsPage.ChooseUpdateChannelTest((int)requestedChannel);
                    for (var wait = 0; updates.IsBusy && wait < 100; wait++) await Task.Delay(20);
                    settingsPage.RevealUpdates(); break;
                case "automaticUpdate": await updates.CheckAutomaticallyAsync(); break;
                case "personalFixture": await RunPersonalFixtureAsync(root.GetProperty("value").GetString()); break;
                case "opticalProbe":
                    await File.WriteAllTextAsync(Path.Combine(fixtureDirectory, "optical-probe.json"), JsonSerializer.Serialize(OpticalRenderer.Diagnostics()));
                    break;
                case "stageAudit":
                    var audit = await libraryPage.AuditStageAsync();
                    await File.WriteAllTextAsync(Path.Combine(fixtureDirectory, "stage-audit.json"), JsonSerializer.Serialize(audit));
                    break;
                case "heroFrames":
                    var heroAudit = await AuditHeroFramesAsync(root.GetProperty("id").GetString());
                    await File.WriteAllTextAsync(Path.Combine(fixtureDirectory, "hero-frames.json"), JsonSerializer.Serialize(heroAudit));
                    break;
                case "artworkRegression": await RunArtworkRegressionAsync(); break;
                case "artworkDetails": await RunArtworkDetailsAsync(root.GetProperty("value").GetString() ?? ""); break;
                case "heroStageAudit":
                    var heroStageAudit = await detailsPage.AuditStageAsync();
                    await File.WriteAllTextAsync(Path.Combine(fixtureDirectory, "hero-stage-audit.json"), JsonSerializer.Serialize(heroStageAudit));
                    break;
                case "treeProbeFault":
                    _ = bitmapTreeProbe.Read(() => throw new System.Runtime.InteropServices.COMException("Test tree read failure", unchecked((int)0x80004005)));
                    break;
                case "p1Dialog": case "p1Close": case "p1Tab": case "p1Shortcut": case "p1GuideView": case "p1LocalView": case "p1Capture": case "p1Evidence": case "p1Statistics": case "p1Fill": case "p1Invoke": case "p1Installation": case "p1CancelInstallation": case "p1ScrollInstallation":
                case "p1ContextMenu": case "p1ContextClose": case "p1ContextInvoke": case "p1LaunchSettings": case "p1DismissDiscovery": case "p1SourceToggle": case "p1ReviewDiscovery": case "p1IgnoreCandidate": case "p1Recognize": case "p1ImportSelect": case "p1ImportInvoke": case "p1DetailsInspect": case "p1PagesInspect":
                    await RunP1FixtureAsync(root); break;
                case "exit": if (App.Instance is { } app) await app.RequestExitAsync(); return;
                default: throw new InvalidDataException("Unsupported visual fixture action.");
            }
            await Task.Delay(350);
            OnDiagnostics(null, new object());
            await File.WriteAllTextAsync(Path.Combine(fixtureDirectory, "receipt.json"), JsonSerializer.Serialize(new { nonce, action, success = true, evidence = VisualEvidence(), runtime = RuntimeState.Text }));
        }
        catch (Exception exception)
        {
            if (Program.Profile?.Manifest.ProfileKind == NativeProfileKind.Test && Environment.GetEnvironmentVariable("THE_ORDER_NATIVE_WALKTHROUGH_TEST") == "1")
                await File.WriteAllTextAsync(Path.Combine(fixtureDirectory, "walkthrough-fixture-error.txt"), exception.ToString());
            if (Program.Profile?.Manifest.ProfileKind == NativeProfileKind.Test && Environment.GetEnvironmentVariable("THE_ORDER_NATIVE_PLAYGROUND_FIXTURE") == "1")
                await File.WriteAllTextAsync(Path.Combine(fixtureDirectory, "playground-fixture-error.txt"), exception.ToString());
            await File.WriteAllTextAsync(Path.Combine(fixtureDirectory, "receipt.json"), JsonSerializer.Serialize(new { nonce = fixtureNonce, success = false, error = exception.GetType().Name }));
        }
        finally { fixtureBusy = false; }
    }

    private void DisposeVisualSystem()
    {
        visualStopped = true;
        if (startupHomeFade is not null) HideStartupSurface();
        ResetPageMotion();
        libraryPage?.ReleaseContinueCover(); detailsPage?.ReleaseStageBeforeLeave(); onlinePage?.Cancel();
        Root.Loaded -= StartVisualFixture;
        if (visualFixtureTimer is not null) CompositionTarget.Rendering -= RecordFirstStartupFrame;
        if (visualFixtureTimer is not null) { visualFixtureTimer.Stop(); visualFixtureTimer.Tick -= OnVisualFixture; }
        gallery?.Dispose(); gallery = null;
        materialPolicy?.Dispose(); materialPolicy = null;
    }

    private void ShowSessionStateFixtures()
    {
        if (visualFixtureTimer is null || visualStopped) return;
        var stack = new StackPanel { Padding = new Thickness(28, 20, 28, 20), Spacing = 12 };
        TextBlock Text(string text, string style) => new() { Text = text, Style = (Style)Microsoft.UI.Xaml.Application.Current.Resources[style] };
        stack.Children.Add(Text("会话状态 · 合成展示", "PageTitleStyle"));
        stack.Children.Add(Text("仅验证现有 SessionPresentation 的文字、图标与层级。没有启动游戏、写入会话或累计时长。", "SecondaryTextStyle"));
        var now = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var native = new NativeSession(1, "visual-00", "synthetic", now, now, null, now.AddHours(1), 3600 * NativeSession.TicksPerSecond,
            SessionLifecycle.Open, null, false, 1, Guid.Parse("b6cd8182-046b-420e-924c-ed6c93922207"), new(0, null), null);
        var states = new[] {
            ("Active", native, SessionTracking.LaunchScope, SessionTiming.Running),
            ("Paused", native with { UserPaused = true }, SessionTracking.LaunchScope, SessionTiming.Stopped),
            ("TrackingLimited", native, SessionTracking.CurrentProcessOnly, SessionTiming.Running),
            ("RecoveryPending", native with { Lifecycle = SessionLifecycle.RecoveryPending, Reason = SessionReason.ManagerExit }, SessionTracking.Unknown, SessionTiming.Stopped),
            ("Incomplete", native with { Lifecycle = SessionLifecycle.Incomplete, Reason = SessionReason.UserStoppedObservation }, SessionTracking.Detached, SessionTiming.Stopped),
            ("Completed", native with { Lifecycle = SessionLifecycle.Completed, Reason = SessionReason.ExitConfirmed, EndedAtUtc = now.AddHours(1), RecoveryToken = null }, SessionTracking.Detached, SessionTiming.Stopped)
        };
        foreach (var (label, record, tracking, timing) in states)
        {
            var presentation = SessionPresentation.From(new(record.Project("visual-fixture"), tracking, timing, true, record.Accumulated100ns, null, null), null, true);
            var body = new StackPanel { Spacing = 4 };
            body.Children.Add(Text(label + " · Test fixture", "MetadataTextStyle"));
            var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
            var facts = SessionVisualFacts(record, tracking, timing);
            var state = Text(presentation.State, "BodyTextStyle");
            var brush = ResolveSessionBrush(facts.BrushKey, state.Foreground);
            line.Children.Add(new FontIcon { Glyph = facts.Glyph, FontSize = 18, Foreground = brush });
            state.Foreground = brush;
            line.Children.Add(state);
            body.Children.Add(line); body.Children.Add(Text(presentation.Duration, "MetadataTextStyle"));
            stack.Children.Add(new Border { Style = (Style)Microsoft.UI.Xaml.Application.Current.Resources["SurfaceStyle"], Padding = new Thickness(16), Child = body });
        }
        LeaveGallery();
        detailsPage.ReleaseStageBeforeLeave();
        PageContent.Content = new ScrollViewer { Content = stack, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        galleryVisible = true; visualScreen = "SessionStateFixtures";
    }
}
