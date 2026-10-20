using System.Text.Json;
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
    private bool visualWindowVisible = true;
    private bool galleryVisible, visualStopped, fixtureBusy;
    private string direction = "B", fixtureNonce = "";
    private string visualScreen = "VisualGallery";
    private string? fixtureDirectory;
    private DispatcherTimer? visualFixtureTimer;
    private ResourceDictionary? visualTokens;

    private void InitializeVisualSystem(NativeConfiguration configuration)
    {
        materialPolicy = new(DispatcherQueue, ApplyMaterialPolicy);
        if (Program.Profile?.Manifest.ProfileKind == NativeProfileKind.Test
            && Environment.GetEnvironmentVariable("THE_ORDER_NATIVE_VISUAL_TEST") == "1")
        {
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
            if (configuration.Diagnostics)
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
        return new NavigationViewItem { Content = content, Tag = tag, Height = 74, Padding = new Thickness(0), HorizontalContentAlignment = HorizontalAlignment.Center };
    }
    // One compact labeled rail at every window width.
    private void OnNavigationDisplayModeChanged(NavigationView sender, NavigationViewDisplayModeChangedEventArgs args) => UpdateNavigationChrome();
    private void UpdateNavigationChrome()
    {
        if (ContentLayer is null) return;
        ContentLayer.CornerRadius = new CornerRadius(0);
        ContentLayer.BorderThickness = new Thickness(0);
    }
    private void OnPaneToggle(TitleBar sender, object args) => Navigation.IsPaneOpen = !Navigation.IsPaneOpen;
    private void OnShellSizeChanged(object sender, SizeChangedEventArgs args) { }

    private void StartVisualFixture(object sender, RoutedEventArgs e)
    {
        visualFixtureTimer?.Start();
        if (fixtureDirectory is not null)
            File.WriteAllText(Path.Combine(fixtureDirectory, "fixture-ready.json"), JsonSerializer.Serialize(new { ready = true, isolatedTest = true }));
    }
    private void ApplyMaterialPolicy()
    {
        if (visualStopped || materialPolicy is null) return;
        var effectiveMode = EffectiveMaterialMode;
        materialPolicy.AppOpaque = effectiveMode == Views.MaterialMode.Opaque;
        materialPolicy.Apply(Microsoft.UI.Xaml.Application.Current.Resources);
        RefreshCaptionTheme();
        var visible = visualWindowVisible && AppWindow.IsVisible && App.Instance?.WindowHidden != true;
        // Win10 uses a solid window canvas. That does not disable supported in-app
        // Acrylic or the artwork-owned banner glass; their existing policy is independent.
        ApplyBackdrop(materialPolicy.EffectsAllowed);
        // Hidden windows hold no glass renderer: stages drop to their XAML layers and release swapchains.
        var stageAllowed = materialPolicy.EffectsAllowed && visible;
        libraryPage.SetStagePolicy(stageAllowed, materialPolicy.HighContrast, effectiveMode);
        detailsPage.SetStagePolicy(stageAllowed, materialPolicy.HighContrast, effectiveMode);
        onlinePage.SetStagePolicy(stageAllowed, materialPolicy.HighContrast, effectiveMode);
        settingsPage.ShowMaterialState(materialMode, materialPolicy.EffectsAllowed, materialPolicy.HighContrast,
            controlledAdvanced, materialReadOnly || !materialInitialized, materialPreferenceNotice);
        gallery?.SetAdvanced(materialPolicy.EffectsAllowed && visible);
        gallery?.SetMotionAllowed(materialPolicy.MotionAllowed && visible);
        if (!materialPolicy.MotionAllowed && Root.XamlRoot is not null)
        {
            var visual = ElementCompositionPreview.GetElementVisual(PageContent);
            visual.StopAnimation("Opacity"); visual.Opacity = 1;
        }
    }

    internal void RefreshSystemMaterialPolicy() => materialPolicy?.NotifyNativeSettingsChanged();

    internal void SetVisualWindowVisible(bool visible)
    {
        visualWindowVisible = visible;
        if (!visible) detailsPage.SuspendGameVideos();
        ApplyMaterialPolicy();
    }

    private void UpdateSessionChrome(SessionPresentation presentation)
    {
        var native = Sessions.Session?.Session?.Native;
        var facts = SessionVisualFacts(native, Sessions.Session?.Tracking, Sessions.Session?.Timing, Sessions.Error is not null, !Sessions.Ready);
        SessionIcon.Glyph = facts.Glyph;
        var brush = ResolveSessionBrush(facts.BrushKey);
        if (!ReferenceEquals(SessionIcon.Foreground, brush)) SessionIcon.Foreground = brush;
        if (!ReferenceEquals(SessionState.Foreground, brush)) SessionState.Foreground = brush;
        SessionState.Visibility = string.IsNullOrEmpty(SessionState.Text) ? Visibility.Collapsed : Visibility.Visible;
        SessionDuration.Visibility = string.IsNullOrEmpty(presentation.Duration) ? Visibility.Collapsed : Visibility.Visible;
        PauseTiming.Visibility = presentation.CanPause ? Visibility.Visible : Visibility.Collapsed;
        ResumeTiming.Visibility = presentation.CanResume ? Visibility.Visible : Visibility.Collapsed;
        // The icon expresses session facts; personal library status never controls it.
        // The title-bar indicator replaces the old session bar; it appears only when there is a session fact to show.
        SessionSurface.Visibility = Sessions.Session?.Session is not null || Sessions.Error is not null || !Sessions.Ready ? Visibility.Visible : Visibility.Collapsed;
        SessionDetails.Visibility = Sessions.Session?.Session?.Native?.GameId is not null ? Visibility.Visible : Visibility.Collapsed;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(SessionSurface, "游玩会话：" + SessionChipTitle.Text + "，" + SessionState.Text);
        ApplyMaterialPolicy();
    }

    // Only existing snapshot facts select visual emphasis. No process or timing
    // fact is inferred from an icon, personal library status, or local UI clock.
    private static (string Glyph, string BrushKey) SessionVisualFacts(NativeSession? native, SessionTracking? tracking, SessionTiming? timing, bool error = false, bool recovering = false)
    {
        if (error) return ("\uEA39", "ErrorTextBrush");
        if (recovering) return ("\uE777", "RecoveryTextBrush");
        if (native is null) return ("\uE8F1", "TextSecondaryBrush");
        if (native.Lifecycle == SessionLifecycle.Completed) return ("\uE73E", "SuccessTextBrush");
        if (native.Lifecycle == SessionLifecycle.Incomplete) return ("\uE783", "WarningTextBrush");
        if (native.Lifecycle == SessionLifecycle.RecoveryPending) return ("\uE777", "RecoveryTextBrush");
        if (native.UserPaused) return ("\uE769", "WarningTextBrush");
        if (tracking != SessionTracking.LaunchScope) return ("\uE7BA", "WarningTextBrush");
        return timing == SessionTiming.Running ? ("\uE768", "SuccessTextBrush") : ("\uE823", "TextSecondaryBrush");
    }

    private Brush ResolveSessionBrush(string key)
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
        return SessionTitle.Foreground;
    }

    private void AnimatePage()
    {
        galleryVisible = false;
        if (materialPolicy?.MotionAllowed != true || Root.XamlRoot is null) return;
        var visual = ElementCompositionPreview.GetElementVisual(PageContent);
        visual.StopAnimation("Opacity");
        using var animation = visual.Compositor.CreateScalarKeyFrameAnimation();
        animation.InsertKeyFrame(0, .86f); animation.InsertKeyFrame(1, 1);
        animation.Duration = TimeSpan.FromMilliseconds((double)Microsoft.UI.Xaml.Application.Current.Resources["MotionPageMilliseconds"]);
        visual.StartAnimation("Opacity", animation);
    }

    // Called before PageContent is replaced so the Gallery releases its native
    // owner while still in the live tree. No-op unless the Test Gallery is shown.
    private void LeaveGallery()
    {
        if (gallery is not null && ReferenceEquals(PageContent.Content, gallery)) gallery.ReleaseBeforeLeave();
    }

    private void ShowGallery()
    {
        if (visualFixtureTimer is null || visualStopped) return;
        gallery ??= new(materialPolicy?.EffectsAllowed == true, requestedGlass);
        libraryPage.ReleaseStageBeforeLeave(); detailsPage.ReleaseStageBeforeLeave();
        PageContent.Content = gallery; galleryVisible = true;
        visualScreen = "VisualGallery";
        ApplyMaterialPolicy();
    }

    private bool SkipCaptureTreeProbe => visualFixtureTimer is not null &&
        Environment.GetEnvironmentVariable("THE_ORDER_NATIVE_CAPTURE_SKIP_TREE_PROBE") == "1";
    private object VisualEvidence() => new
    {
        contentLanguage = ContentLanguage.Current, direction, isolatedTest = visualFixtureTimer is not null, galleryVisible, visualScreen = galleryVisible ? visualScreen : ViewModel.Page.ToString(),
        material = materialPolicy?.Evidence(), gallery = gallery?.ResourceCounts, materialMode = materialMode.ToString(), effectiveMaterialMode = EffectiveMaterialMode.ToString(), controlledAdvanced,
        diagnosticsPolling = diagnosticsTimer is not null, backdrop = SystemBackdrop?.GetType().Name ?? "Solid",
        gameVideos = detailsPage.GameVideoEvidence,
        catalogPreparation = presentations.Metrics, stages = new { library = libraryPage.StageEvidence, details = detailsPage.StageEvidence, online = onlinePage.StageEvidence }, shell = ShellEvidence(),
        round3 = Round3Evidence(), managed = MaterialManagedEvidence(), displayBitmaps = Views.DisplayBitmaps.Evidence(images), bitmapSurfaces = SkipCaptureTreeProbe ? (int?)null : bitmapTreeProbe.Read(() => CountMaterialBitmaps(Root)), pendingNavigation = navigating, pendingDetails = ViewModel.IsDetailsLoading, staleCovers = SkipCaptureTreeProbe ? (int?)null : staleTreeProbe.Read(() => CountStaleCoverSources(Root)), captureTreeProbeSkipped = SkipCaptureTreeProbe,
        updates = new { updates.SelectedChannel, updates.SupportsChannelSwitch, updates.InstallText, updates.IsChannelChange, updates.CurrentVersion, latest = updates.Latest?.Version, updates.Message, updates.Highlights, updates.CanCheck, updates.CanInstall, updates.CanCancel, updates.IsBusy, updates.Progress, badgeVisible = UpdateBadge.Visibility == Visibility.Visible },
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
        if (fixtureBusy || fixtureDirectory is null || visualStopped || !Sessions.Ready) return;
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
                    AppWindow.Resize(new Windows.Graphics.SizeInt32((int)(Math.Clamp(root.GetProperty("width").GetInt32(), 480, 1600) * scale), (int)(Math.Clamp(root.GetProperty("height").GetInt32(), 480, 1000) * scale))); break;
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
                case "timer": round3TimerOverride = root.GetProperty("value").GetString() ?? ""; break;
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
                case "checkUpdate": await Navigate(WorkspacePage.Settings); await updates.CheckAsync(); settingsPage.ShowUpdatesTest(); break;
                case "updateChannel":
                    if (!Enum.TryParse<AppUpdateChannel>(root.GetProperty("value").GetString(), false, out var requestedChannel) || !Enum.IsDefined(requestedChannel)) throw new InvalidDataException("Unknown channel");
                    await Navigate(WorkspacePage.Settings); settingsPage.ChooseUpdateChannelTest((int)requestedChannel);
                    for (var wait = 0; updates.IsBusy && wait < 100; wait++) await Task.Delay(20);
                    settingsPage.ShowUpdatesTest(); break;
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
                case "heroStageAudit":
                    var heroStageAudit = await detailsPage.AuditStageAsync();
                    await File.WriteAllTextAsync(Path.Combine(fixtureDirectory, "hero-stage-audit.json"), JsonSerializer.Serialize(heroStageAudit));
                    break;
                case "treeProbeFault":
                    _ = bitmapTreeProbe.Read(() => throw new System.Runtime.InteropServices.COMException("Test tree read failure", unchecked((int)0x80004005)));
                    break;
                case "p1Dialog": case "p1Close": case "p1Tab": case "p1Capture": case "p1Evidence": case "p1Statistics": case "p1Fill": case "p1Invoke": case "p1Installation": case "p1CancelInstallation": case "p1ScrollInstallation":
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
            await File.WriteAllTextAsync(Path.Combine(fixtureDirectory, "receipt.json"), JsonSerializer.Serialize(new { nonce = fixtureNonce, success = false, error = exception.GetType().Name }));
        }
        finally { fixtureBusy = false; }
    }

    private void DisposeVisualSystem()
    {
        visualStopped = true;
        libraryPage?.ReleaseStageBeforeLeave(); detailsPage?.ReleaseStageBeforeLeave(); onlinePage?.Cancel();
        Root.Loaded -= StartVisualFixture;
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
            var brush = ResolveSessionBrush(facts.BrushKey);
            line.Children.Add(new FontIcon { Glyph = facts.Glyph, FontSize = 18, Foreground = brush });
            var state = Text(presentation.State, "BodyTextStyle"); state.Foreground = brush;
            line.Children.Add(state);
            body.Children.Add(line); body.Children.Add(Text(presentation.Duration, "MetadataTextStyle"));
            stack.Children.Add(new Border { Style = (Style)Microsoft.UI.Xaml.Application.Current.Resources["SurfaceStyle"], Padding = new Thickness(16), Child = body });
        }
        LeaveGallery();
        libraryPage.ReleaseStageBeforeLeave(); detailsPage.ReleaseStageBeforeLeave();
        PageContent.Content = new ScrollViewer { Content = stack, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        galleryVisible = true; visualScreen = "SessionStateFixtures";
    }
}
