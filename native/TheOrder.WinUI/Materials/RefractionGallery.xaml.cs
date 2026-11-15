using TheOrder.WinUI.Views;
using System.Numerics;
using System.Diagnostics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.UI;
using Windows.UI.ViewManagement;
using Path = System.IO.Path;

namespace TheOrder.WinUI.Materials;

/// <summary>
/// Development/Test-only Gallery. Its owner controls window visibility through
/// SetAdvanced and owns final Dispose. Unload releases every composition resource;
/// reload creates a new bounded tree. No timer or perpetual animation is owned.
/// </summary>
public sealed partial class RefractionGallery : UserControl, IDisposable
{
    private readonly UISettings? uiSettings;
    private readonly AccessibilitySettings? accessibility;
    private CompositionCapabilities? capabilities;
    private RefractionLens? lens;
    private bool advancedAllowed;
    private bool ownerAllowed;
    private bool ownerMotionAllowed = true;
    private bool loaded;
    private bool disposed;
    private bool initialized;
    private string? failure;
    private string? failurePhase;
    private string? failureHResult;
    private readonly Dictionary<string, NotificationWarning> notificationWarnings = new(StringComparer.Ordinal);
    private double patternPosition;
    private int allocationCycles;
    private bool effectsSubscribed;
    private bool animationsSubscribed;
    private bool contrastSubscribed;
    private bool capabilitySubscribed;
    private int cleanupFailures;
    private bool syncingPatternSlider;
    private bool? systemMotionAllowed;
    private bool refreshQueued;
    private int callbackGeneration;
    private long updatesReleased, sourceUpdates;
    private RefractionLens? failedRelease;
    private string testPolicy = "normal";
    private string interaction = "normal";
    private bool failInitialization;
    private Vector3 finiteMotionStart;
    private double finiteMotionStartPosition;
    private long finiteMotionUntil;
    private Color? appliedBackground;
    private Color? appliedForeground;
    private int? appliedBodyState;
    private readonly List<Image> coverImages = [];
    private readonly HashSet<Image> pendingCovers = [];
    private int coverFailures;
    private int appliedBackgroundMode = -1;
    private bool syncingReview;
    private LensOptics CurrentOptics => new LensOptics((float)StrengthSlider.Value,
        ComparisonMode.OptionIndex == 2 ? (float)DispersionSlider.Value : 0,
        (float)EdgeSlider.Value, (float)HighlightSlider.Value).Bounded();

    public RefractionGallery() : this(true) { }
    public RefractionGallery(bool advancedAllowed = true, bool requested = false)
    {
        ownerAllowed = advancedAllowed;
        this.advancedAllowed = requested;
        InitializeComponent();
        var phase = "initialize.uiSettings";
        try
        {
            uiSettings = new UISettings();
            phase = "initialize.accessibilitySettings";
            accessibility = new AccessibilitySettings();
        }
        catch (Exception exception) { RecordFailure(phase, exception); }
        if (ownedSceneEnabled) ComparisonMode.OptionIndex = 3;
        BuildPattern();
        initialized = true;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        AdvancedSwitch.IsOn = requested;
        AdvancedSwitch.IsEnabled = ownerAllowed;
        UpdateState();
    }

    internal object ResourceCounts => new
    {
        SurfaceCount = lens?.SurfaceCount ?? 0,
        BrushCount = lens?.BrushCount ?? 0,
        VisualCount = lens?.VisualCount ?? 0,
        ClipCount = lens?.ClipCount ?? 0,
        GeometryCount = lens?.GeometryCount ?? 0,
        EffectFactoryCount = lens?.EffectFactoryCount ?? 0,
        EventSubscriptionCount = (effectsSubscribed ? 1 : 0) + (animationsSubscribed ? 1 : 0) +
            (contrastSubscribed ? 1 : 0) + (capabilitySubscribed ? 1 : 0),
        CleanupFailures = cleanupFailures,
        OwnedDeviceCount = optical is null ? 0 : 1,
        Optical = OpticalEvidence,
        XamlPatternElementCount = PatternLayer.Children.Count,
        TimerCount = 0,
        PerpetualAnimationCount = 0,
        FiniteAnimationWithinRequestedWindow = finiteMotionUntil > Stopwatch.GetTimestamp(),
        OwnerMotionAllowed = ownerMotionAllowed,
        SystemMotionAllowed = systemMotionAllowed,
        AllocationCycles = allocationCycles,
        PatternPosition = patternPosition,
        AdvancedActive = MaterialActive,
        Failure = failure,
        FailurePhase = failurePhase,
        FailureHResult = failureHResult,
        NotificationWarnings = notificationWarnings.Values.ToArray(),
        NotificationRoute = "Available events and existing owner policy refresh; no new timer",
        Source = UsesOwnedScene ? "Gallery-owned dynamic scene; no arbitrary XAML capture" : UsesOptical ? "Gallery-only high-resolution snapshot and finite shader translation" : "live app-internal CompositionVisualSurface",
        Model = UsesOptical ? "bounded per-pixel curved bevel; one sample per channel; no blur" : "affine RGB dispersion approximation; radial corners; quiet 1:1 center; one opaque source",
        Comparison = ComparisonMode.OptionIndex, Background = BackgroundMode.OptionIndex,
        Optics = new { CurrentOptics.Refraction, CurrentOptics.Dispersion, CurrentOptics.EdgeWidth, CurrentOptics.Highlight },
        PendingGalleryCovers = pendingCovers.Count, GalleryCoverFailures = coverFailures,
        GalleryCoverImages = coverImages.Count, AdditionalCaptureCount = snapshotCount,
        Direct3DTextureCount = optical?.Ready == true ? 3 : 0,
        Geometry = GeometryEvidence(),
        Requested = advancedAllowed, Actual = MaterialActive ? "Advanced" : "Solid",
        FallbackReason = MaterialActive ? null : failure ?? (!loaded ? "Unloaded" : !ownerAllowed ? "OwnerPolicyOrHidden" : !advancedAllowed ? "Disabled" : testPolicy != "normal" ? testPolicy : "CapabilityUnavailable"),
        UpdateCount = updatesReleased + (lens?.UpdateCount ?? 0), SourceUpdateCount = sourceUpdates,
        RetainedFailedResources = failedRelease?.RemainingResources ?? 0,
        PendingCallbackCount = refreshQueued ? 1 : 0, TestPolicy = testPolicy, Interaction = interaction
    };

    private object? GeometryEvidence()
    {
        if (!loaded || disposed) return null;
        var p = LensHost.TransformToVisual(Stage).TransformPoint(new Windows.Foundation.Point());
        return new { StageWidth = Stage.ActualWidth, StageHeight = Stage.ActualHeight,
            LensWidth = LensHost.ActualWidth, LensHeight = LensHost.ActualHeight, LensX = p.X, LensY = p.Y,
            RasterizationScale = XamlRoot?.RasterizationScale, CenterSampling = "1:1; spectral effect excluded", SourceOpaque = true };
    }

    internal void SetAdvanced(bool allowed)
    {
        if (disposed || ownerAllowed == allowed) return;
        ownerAllowed = allowed;
        if (initialized) AdvancedSwitch.IsEnabled = allowed;
        RefreshMaterial();
    }

    internal void SetMotionAllowed(bool allowed)
    {
        if (disposed || ownerMotionAllowed == allowed) return;
        ownerMotionAllowed = allowed;
        if (!allowed)
        {
            try { StopFiniteMotion(restoreStart: true); }
            catch (Exception exception) { SetCompositionFailure(exception, "motion.ownerStop"); }
        }
        UpdateState();
    }

    internal void StepPattern(double position)
    {
        if (disposed || !initialized) return;
        patternPosition = Math.Clamp(position, 0, 100); sourceUpdates++;
        StopOpticalMotion();
        try
        {
            var visual = ElementCompositionPreview.GetElementVisual(PatternLayer);
            visual.StopAnimation("Offset");
            finiteMotionUntil = 0;
            visual.Offset = new Vector3((float)(-patternPosition * 1.2), (float)(-patternPosition * .16), 0);
            SyncPatternSlider();
            DrawOptical((float)(-patternPosition * 1.2), (float)(-patternPosition * .16));
        }
        catch (Exception e) { SetCompositionFailure(e, "motion.stepPattern"); }
        UpdateState();
    }

    private void BuildPattern()
    {
        backgroundBuilds++;
        ClearCoverImages(); PatternLayer.Children.Clear();
        PatternLayer.Clip = new RectangleGeometry { Rect = new Windows.Foundation.Rect(0, 0, 1300, 660) };
        appliedBackgroundMode = BackgroundMode.OptionIndex;
        snapshotGeneration++; snapshotDirty = true;
        StopOpticalMotion(); OpticalPanel.Visibility = Visibility.Collapsed;
        PatternSource.Background = new SolidColorBrush(appliedBackgroundMode == 4 ? Color.FromArgb(255, 31, 36, 43) : Color.FromArgb(255, 237, 239, 240));
        PatternLayer.Background = PatternSource.Background;
        scenePattern = UsesOwnedScene;
        PatternSource.Visibility = scenePattern ? Visibility.Collapsed : Visibility.Visible;
        scene = null;
        if (scenePattern)
        {
            scene = GalleryScene.Create(appliedBackgroundMode,
                Environment.GetEnvironmentVariable("THE_ORDER_NATIVE_MATERIAL_REFERENCE"),
                Environment.GetEnvironmentVariable("THE_ORDER_NATIVE_PERFORMANCE_COVERS"));
            return;
        }
        if (appliedBackgroundMode is 3 or 4 or 8 or 9) return; // 8/9 are owned-scene only
        if (appliedBackgroundMode is 6 or 7)
        {
            var reference = Environment.GetEnvironmentVariable("THE_ORDER_NATIVE_MATERIAL_REFERENCE");
            if (string.IsNullOrEmpty(reference) || !Path.GetFullPath(reference).Contains(Path.DirectorySeparatorChar + ".artifacts" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Explicit isolated artwork directory required");
            var file = Path.Combine(Path.GetDirectoryName(reference)!, appliedBackgroundMode == 6 ? "steam-1230140.jpg" : "steam-1094820.jpg");
            if (!File.Exists(file)) throw new InvalidDataException("Test artwork missing");
            var image = new Image { Width = 1300, Height = 1950, Stretch = Stretch.Fill };
            image.ImageOpened += OnCoverOpened; image.ImageFailed += OnCoverFailed;
            coverImages.Add(image); pendingCovers.Add(image); image.Source = new BitmapImage(new Uri(file));
            Canvas.SetLeft(image, 0); Canvas.SetTop(image, -430); PatternLayer.Children.Add(image);
            return;
        }
        if (appliedBackgroundMode == 5)
        {
            var path = Environment.GetEnvironmentVariable("THE_ORDER_NATIVE_MATERIAL_REFERENCE");
            if (string.IsNullOrEmpty(path) || !Path.GetFullPath(path).Contains(Path.DirectorySeparatorChar + ".artifacts" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
                throw new InvalidOperationException("Explicit isolated reference image required");
            // Original user-supplied pixels, positioned/clipped by XAML, not edited.
            var image = new Image { Width = 1300, Height = 2826, Stretch = Stretch.Fill };
            image.ImageOpened += OnCoverOpened; image.ImageFailed += OnCoverFailed;
            coverImages.Add(image); pendingCovers.Add(image); image.Source = new BitmapImage(new Uri(path));
            Canvas.SetLeft(image, -65); Canvas.SetTop(image, -866); PatternLayer.Children.Add(image);
            return;
        }
        if (appliedBackgroundMode == 2)
        {
            var directory = Environment.GetEnvironmentVariable("THE_ORDER_NATIVE_PERFORMANCE_COVERS");
            if (string.IsNullOrEmpty(directory) || !Path.GetFullPath(directory).Contains(Path.DirectorySeparatorChar + ".artifacts" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Explicit Test cover directory required");
            foreach (var n in new[] { 0, 3, 6, 9, 12, 15 })
            {
                var file = Path.Combine(directory, $"cover-{n:D5}.jpg");
                if (!File.Exists(file)) throw new InvalidDataException("Frozen Test cover missing");
                var image = new Image { Width = 200, Height = 300, Stretch = Stretch.UniformToFill };
                image.ImageOpened += OnCoverOpened; image.ImageFailed += OnCoverFailed;
                coverImages.Add(image); pendingCovers.Add(image);
                image.Source = new BitmapImage(new Uri(file));
                Canvas.SetLeft(image, 24 + coverImages.Count * 215 - 215); Canvas.SetTop(image, -10);
                PatternLayer.Children.Add(image);
            }
            return;
        }
        if (appliedBackgroundMode == 0)
        {
            for (var row = 0; row < 8; row++)
            {
                var text = new TextBlock { Text = "GLASS  光学边缘  ガラス  0123456789  —  The Order", FontSize = row % 2 == 0 ? 24 : 16, Foreground = new SolidColorBrush(Color.FromArgb(255, 20, 22, 25)), FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
                Canvas.SetLeft(text, 10 + row % 2 * 30); Canvas.SetTop(text, 14 + row * 46); PatternLayer.Children.Add(text);
            }
            for (var i = 0; i < 18; i++) AddBlock(18 + i * 61, 0, i % 2 == 0 ? 2 : 1, 660, Color.FromArgb(255, 24, 26, 29));
            return;
        }
        for (var row = 0; row < 11; row++)
        for (var column = 0; column < 22; column++)
        {
            var square = new Rectangle { Width = 64, Height = 64,
                Fill = new SolidColorBrush((row + column) % 2 == 0 ? Color.FromArgb(255, 223, 234, 237) : Color.FromArgb(255, 244, 246, 238)) };
            Canvas.SetLeft(square, column * 64); Canvas.SetTop(square, row * 64);
            PatternLayer.Children.Add(square);
        }
        AddBlock(74, 40, 116, 224, Color.FromArgb(255, 77, 137, 159));
        AddBlock(348, 164, 184, 128, Color.FromArgb(255, 204, 137, 89));
        AddBlock(704, 56, 154, 192, Color.FromArgb(255, 131, 165, 141));
        for (var i = 0; i < 26; i++)
        {
            var line = new Rectangle { Width = 2, Height = 680,
                Fill = new SolidColorBrush(Color.FromArgb(150, 33, 75, 91)) };
            Canvas.SetLeft(line, 18 + i * 48); PatternLayer.Children.Add(line);
        }
        for (var i = 0; i < 12; i++)
        {
            var line = new Rectangle { Width = 1300, Height = 1,
                Fill = new SolidColorBrush(Color.FromArgb(180, 44, 68, 78)) };
            Canvas.SetTop(line, 18 + i * 48); PatternLayer.Children.Add(line);
        }
    }

    private void OnCoverOpened(object sender, RoutedEventArgs e) { if (sender is Image image) pendingCovers.Remove(image); CaptureOpticalSource(); }
    private void OnCoverFailed(object sender, ExceptionRoutedEventArgs e) { if (sender is Image image) pendingCovers.Remove(image); coverFailures++; SetCompositionFailure(new InvalidDataException("Gallery image decode failed"), "gallery.imageDecode"); UpdateState(); }
    private void ClearCoverImages() { foreach (var image in coverImages) { image.ImageOpened -= OnCoverOpened; image.ImageFailed -= OnCoverFailed; image.Source = null; } coverImages.Clear(); pendingCovers.Clear(); }

    private void AddBlock(double x, double y, double width, double height, Color color)
    {
        var block = new Rectangle { Width = width, Height = height, Fill = new SolidColorBrush(color) };
        Canvas.SetLeft(block, x); Canvas.SetTop(block, y); PatternLayer.Children.Add(block);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (disposed || loaded) return;
        loaded = true;
        ActualThemeChanged += OnGalleryThemeChanged;
        RefreshMaterial();
    }

    // The owner is about to replace this Gallery. Release while it is still in the
    // live tree: a SwapChainPanel detached after Unloaded keeps presenting its last
    // swapchain buffers until it is rendered again (TC1 swap-before evidence).
    internal void ReleaseBeforeLeave()
    {
        if (disposed || !loaded) return;
        loaded = false; callbackGeneration++; refreshQueued = false;
        ReleaseMaterial();
        UpdateState();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        loaded = false; callbackGeneration++; refreshQueued = false;
        ActualThemeChanged -= OnGalleryThemeChanged;
        ReleaseMaterial();
        DetachSettings();
        DetachCapabilities();
    }

    private void OnEffectsChanged(UISettings sender, object args) => QueueRefresh();
    private void OnContrastChanged(AccessibilitySettings sender, object args) => QueueRefresh();
    private void OnCapabilitiesChanged(CompositionCapabilities sender, object args) => QueueRefresh();
    private void OnAnimationsChanged(UISettings sender, UISettingsAnimationsEnabledChangedEventArgs args)
        => QueueRefresh();
    private void QueueRefresh()
    {
        if (disposed || !loaded || refreshQueued) return;
        refreshQueued = true; var generation = callbackGeneration;
        try
        {
            if (!DispatcherQueue.TryEnqueue(() =>
            {
                if (disposed || !loaded || generation != callbackGeneration) return;
                refreshQueued = false;
                try
                {
                    var settings = ReadSettings();
                    if (!ownerMotionAllowed || settings.Animations != true) StopFiniteMotion(restoreStart: true);
                    RefreshMaterial();
                }
                catch (Exception exception) { SetCompositionFailure(exception, "callback.refresh"); }
            })) refreshQueued = false;
        }
        catch (Exception exception) { refreshQueued = false; RecordFailure("callback.enqueue", exception); }
    }

    private void AttachSettings()
    {
        if (uiSettings is not null && !effectsSubscribed)
            effectsSubscribed = TryAttach("subscribe.advancedEffects", () => uiSettings.AdvancedEffectsEnabledChanged += OnEffectsChanged);
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041) && uiSettings is not null && !animationsSubscribed)
            animationsSubscribed = TryAttach("subscribe.animations", () =>
            {
                if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
                    uiSettings.AnimationsEnabledChanged += OnAnimationsChanged;
            });
        if (accessibility is not null && !contrastSubscribed)
            contrastSubscribed = TryAttach("subscribe.highContrast", () => accessibility.HighContrastChanged += OnContrastChanged);
    }

    private readonly record struct NotificationWarning(string Phase, string ExceptionType, string HResult);

    private bool TryAttach(string phase, Action subscribe)
    {
        try { subscribe(); return true; }
        catch (Exception exception) { RecordNotificationWarning(phase, exception); return false; }
    }

    private void RecordNotificationWarning(string phase, Exception exception)
        => notificationWarnings[phase] = new(phase, exception.GetType().Name, $"0x{exception.HResult:X8}");

    private void DetachSettings()
    {
        if (effectsSubscribed && uiSettings is not null)
            TryDetach("unsubscribe.advancedEffects", () =>
            {
                uiSettings.AdvancedEffectsEnabledChanged -= OnEffectsChanged;
                effectsSubscribed = false;
            });
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041) && animationsSubscribed && uiSettings is not null)
            TryDetach("unsubscribe.animations", () =>
            {
                if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
                    uiSettings.AnimationsEnabledChanged -= OnAnimationsChanged;
                animationsSubscribed = false;
            });
        if (contrastSubscribed && accessibility is not null)
            TryDetach("unsubscribe.highContrast", () =>
            {
                accessibility.HighContrastChanged -= OnContrastChanged;
                contrastSubscribed = false;
            });
    }

    private void DetachCapabilities()
    {
        var currentCapabilities = capabilities;
        if (capabilitySubscribed && currentCapabilities is not null)
            TryDetach("unsubscribe.compositionCapabilities", () =>
            {
                currentCapabilities.Changed -= OnCapabilitiesChanged;
                capabilitySubscribed = false;
            });
        if (!capabilitySubscribed) capabilities = null;
    }

    private void TryDetach(string phase, Action detach)
    {
        try { detach(); }
        catch (Exception exception) { cleanupFailures++; RecordNotificationWarning(phase, exception); }
    }

    private readonly record struct Settings(bool? AdvancedEffects, bool? HighContrast, bool? Animations);

    private Settings ReadSettings()
    {
        var settings = uiSettings;
        var contrast = accessibility;
        var effects = settings is null ? null : Probe("query.advancedEffects", () => settings.AdvancedEffectsEnabled);
        var highContrast = contrast is null ? null : Probe("query.highContrast", () => contrast.HighContrast);
        systemMotionAllowed = settings is null ? null : Probe("query.animations", () => settings.AnimationsEnabled);
        return new(effects, highContrast, systemMotionAllowed);
    }

    private bool? Probe(string phase, Func<bool> read)
    {
        try { return read(); }
        catch (Exception exception) { RecordFailure(phase, exception); return null; }
    }

    private void RecordFailure(string phase, Exception exception)
    {
        failure = exception.GetType().Name;
        failurePhase = phase;
        failureHResult = $"0x{exception.HResult:X8}";
    }

    private void RefreshMaterial()
    {
        if (!initialized || disposed) return;
        StrengthSlider.IsEnabled = DispersionSlider.IsEnabled = EdgeSlider.IsEnabled = HighlightSlider.IsEnabled = !UsesOptical;
        DispersionSwitch.IsEnabled = UsesOptical;
        if (loaded && ownerAllowed && advancedAllowed && failure is null && testPolicy == "normal")
        {
            AttachSettings();
            try { capabilities ??= new CompositionCapabilities(); }
            catch (Exception e) { RecordFailure("initialize.compositionCapabilities", e); }
            if (capabilities is not null && !capabilitySubscribed)
                capabilitySubscribed = TryAttach("subscribe.compositionCapabilities", () => capabilities.Changed += OnCapabilitiesChanged);
        }
        var settings = ReadSettings();
        var composition = capabilities;
        var supported = composition is null ? null : Probe("query.compositionCapabilities", composition.AreEffectsSupported);
        var permitted = ComparisonMode.OptionIndex != 0 && testPolicy == "normal" && loaded && ownerAllowed && advancedAllowed && failure is null && settings.AdvancedEffects == true &&
            settings.HighContrast == false && supported == true;
        if (!permitted)
        {
            var keepScene = UsesOwnedScene && loaded && ownerAllowed && failure is null;
            ReleaseMaterial(keepScene);
            if (keepScene)
            {
                try { UpdateOwnedScene(false); }
                catch (Exception exception) { RecordFailure("scene.background", exception); ReleaseMaterial(); }
            }
            DetachSettings(); DetachCapabilities();
            SetStablePalette(false);
            UpdateState();
            return;
        }
        try
        {
            if (UsesOptical)
            {
                UpdateOptical(); SetStablePalette(true); LensHighlight.Visibility = Visibility.Collapsed;
                UpdateState(); return;
            }
            if (optical is not null) ReleaseOptical();
            if (lens is null)
            {
                lens = new RefractionLens(PatternSource, LensHost, failInitialization);
                allocationCycles++;
            }
            lens.Update(CurrentOptics);
            LensHighlight.Opacity = CurrentOptics.Highlight;
            LensHighlight.Visibility = Visibility.Visible;
            SetStablePalette(true);
        }
        catch (Exception e)
        {
            // Public composition resources may fail on device/capability transitions.
            // Stay on solid until the owner recreates this experimental Gallery.
            RecordFailure("composition.createOrUpdate", e);
            ReleaseMaterial();
            DetachSettings(); DetachCapabilities();
            SetStablePalette(false);
        }
        UpdateState();
    }

    private void SetStablePalette(bool transparent)
    {
        var dark = ActualTheme == ElementTheme.Dark;
        var background = dark ? Color.FromArgb(transparent ? (byte)24 : (byte)255, 32, 36, 39) : Color.FromArgb(transparent ? (byte)24 : (byte)255, 250, 251, 251);
        var foregroundColor = dark ? Color.FromArgb(255, 243, 244, 245) : Color.FromArgb(255, 27, 32, 35);
        try
        {
            if (ReadSettings().HighContrast == true && uiSettings is not null)
            {
                background = uiSettings.GetColorValue(UIColorType.Background);
                foregroundColor = uiSettings.GetColorValue(UIColorType.Foreground);
            }
        }
        catch (Exception exception)
        {
            RecordFailure("query.highContrastPalette", exception);
            background = Color.FromArgb(255, 240, 245, 246);
        }
        // With an active frosted body the tinted glass carries the text; otherwise keep the solid reading panel.
        var frostPanel = transparent && (FrostActive || ClearActive) && ReadSettings().HighContrast != true;
        // rev2 continuous glass: the shader draws the only edge; no XAML stroke, highlight or backing layer.
        var bodyState = frostPanel ? bodyMode : 0;
        if (appliedBackground.HasValue && appliedBackground.Value.Equals(background) &&
            appliedForeground.HasValue && appliedForeground.Value.Equals(foregroundColor) && appliedBodyState == bodyState) return;
        try
        {
            StableForeground.Background = new SolidColorBrush(frostPanel ? Color.FromArgb(0, 0, 0, 0) : background);
            StableTextPanel.Background = new SolidColorBrush(frostPanel ? Color.FromArgb(0, 0, 0, 0) : dark ? Color.FromArgb(255, 32, 36, 39) : Color.FromArgb(255, 250, 251, 251));
            appliedBodyState = bodyState;
            // Stroke only: changing BorderThickness resizes LensHost and the event-driven draw would
            // use stale lens extents. Restore the same DesignTokens brush the ThemeResource resolves to.
            if (bodyState is 2 or 3) { LensFrame.BorderBrush = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0)); lensStrokeOverridden = true; }
            else if (lensStrokeOverridden && ThemeStroke(dark, ReadSettings().HighContrast == true) is { } stroke) LensFrame.BorderBrush = stroke;
            LensHighlight.Visibility = transparent && bodyState is not (2 or 3) ? Visibility.Visible : Visibility.Collapsed;
            var foreground = new SolidColorBrush(foregroundColor);
            StableIcon.Foreground = foreground;
            StableTitle.Foreground = foreground;
            StableBody.Foreground = foreground;
            StableCaption.Foreground = foreground;
            appliedBackground = background;
            appliedForeground = foregroundColor;
        }
        catch (Exception exception) { RecordFailure("presentation.stablePalette", exception); }
    }

    private bool lensStrokeOverridden;
    private static Brush? ThemeStroke(bool dark, bool highContrast)
    {
        foreach (var dictionary in Microsoft.UI.Xaml.Application.Current.Resources.MergedDictionaries)
            if (dictionary.Source?.OriginalString.EndsWith("DesignTokens.xaml", StringComparison.OrdinalIgnoreCase) == true &&
                dictionary.ThemeDictionaries.TryGetValue(highContrast ? "HighContrast" : dark ? "Dark" : "Light", out var theme) &&
                theme is ResourceDictionary resources && resources.TryGetValue("StrokeBrush", out var brush))
                return brush as Brush;
        return null;
    }

    private void ReleaseMaterial(bool keepScene = false)
    {
        if (keepScene) { StopOpticalMotion(); sceneOpticalActive = false; }
        else ReleaseOptical();
        try { ElementCompositionPreview.GetElementVisual(PatternLayer).StopAnimation("Offset"); } catch (Exception) { }
        finiteMotionUntil = 0;
        if (lens is not null) { updatesReleased += lens.UpdateCount; lens.Dispose(); cleanupFailures += lens.CleanupFailures; if (lens.RemainingResources != 0) { failedRelease = lens; failure = "NativeReleaseFailed"; } lens = null; }
    }

    private void SetCompositionFailure(Exception exception, string phase)
    {
        RecordFailure(phase, exception);
        ReleaseMaterial();
        DetachSettings(); DetachCapabilities();
        SetStablePalette(false);
    }

    private void SyncPatternSlider()
    {
        if (PatternSlider.Value == patternPosition) return;
        syncingPatternSlider = true;
        try { PatternSlider.Value = patternPosition; }
        finally { syncingPatternSlider = false; }
    }

    private void StopFiniteMotion(bool restoreStart)
    {
        var active = finiteMotionUntil > Stopwatch.GetTimestamp();
        StopOpticalMotion();
        var visual = ElementCompositionPreview.GetElementVisual(PatternLayer);
        visual.StopAnimation("Offset");
        finiteMotionUntil = 0;
        if (restoreStart && active)
        {
            // Getters expose base values, not compositor-interpolated values. Hold
            // the known start state instead of claiming an unobservable current frame.
            visual.Offset = finiteMotionStart;
            patternPosition = finiteMotionStartPosition;
            SyncPatternSlider();
            DrawOptical((float)(-patternPosition * 1.2), (float)(-patternPosition * .16));
        }
    }

    private void UpdateState()
    {
        if (!initialized) return;
        try
        {
            var settings = ReadSettings();
            if (failure is not null && MaterialActive) { ReleaseMaterial(); SetStablePalette(false); }
            var caption = "安静中心 · 透明圆角边缘";
            if (StableCaption.Text != caption) StableCaption.Text = caption;
            var state = lens is null
                ? $"稳定实色降级 · 原型资源 0 · {failure ?? (settings.HighContrast == true ? "高对比度" : settings.AdvancedEffects == false ? "系统已关闭透明效果" : "原型未启用") }"
                : $"{(CurrentOptics.Dispersion > 0 ? "折射＋RGB 色散近似" : "仅折射")} · 一个实时背景源 · 推荐色散 0.75／诊断 2 · 背景位置 {patternPosition:0}";
            if (optical is not null) state = $"{(MaterialActive ? ComparisonMode.OptionIndex == 3 ? "候选 1 · 圆润厚玻璃" : "候选 2 · 日常玻璃" : "高级材质关闭")} · {(UsesOwnedScene ? "Gallery 自有动态背景" : "Gallery 快照背景")} · {(dispersionEnabled ? "色散开" : "色散关")} · {(snapshotBusy ? "更新中" : "静止时不绘制")}";
            if (MaterialState.Text != state) MaterialState.Text = state;
        }
        catch (Exception exception) { RecordFailure("presentation.state", exception); ReleaseMaterial(); }
    }

    private void OnStageSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!initialized) return;
        LensFrame.Width = Math.Min(320, Math.Max(180, e.NewSize.Width - 32));
        PatternSource.Clip = new RectangleGeometry { Rect = new Windows.Foundation.Rect(0, 0, e.NewSize.Width, e.NewSize.Height) };
        var limit = Math.Max(0, Math.Min(100, (e.NewSize.Width - LensFrame.Width - 4) / 2));
        LensSlider.Minimum = -limit; LensSlider.Maximum = limit; LensSlider.Value = Math.Clamp(LensSlider.Value, -limit, limit);
        ControlActions.Orientation = ReviewModes.Orientation = e.NewSize.Width < 700 ? Orientation.Vertical : Orientation.Horizontal;
        var narrow = e.NewSize.Width < 500;
        var panels = new FrameworkElement[] { PatternSlider.Parent as FrameworkElement ?? PatternSlider, LensSliderPanel, StrengthSliderPanel, DispersionSliderPanel, EdgeSliderPanel, HighlightSliderPanel };
        for (var i = 1; i < panels.Length; i++) { Grid.SetColumn(panels[i], narrow ? 0 : i % 3); Grid.SetRow(panels[i], narrow ? i : i / 3); }
        while (SlidersLayout.RowDefinitions.Count < 6) SlidersLayout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RefreshMaterial();
    }
    private void OnGalleryThemeChanged(FrameworkElement sender, object e) => RefreshMaterial();
    private void SetInteraction(string value) { if (interaction == value) return; interaction = value; RefreshMaterial(); }
    private void OnLensHover(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e) => SetInteraction("hover");
    private void OnLensExit(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e) => SetInteraction("normal");
    private void OnLensPress(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e) => SetInteraction("pressed");
    private void OnLensRelease(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e) => SetInteraction("hover");
    internal void TestCommand(string value)
    {
        if (value.Length == 9 && value.StartsWith("captureS", StringComparison.Ordinal) && value[8] is >= '0' and <= '5')
        { SetCaptureIsolation(value[8] - '0'); return; }
        switch (value) {
            case "on": AdvancedSwitch.IsOn = true; break;
            case "off": AdvancedSwitch.IsOn = false; break;
            case "a": ComparisonMode.OptionIndex = 0; break;
            case "b": ComparisonMode.OptionIndex = 1; break;
            case "c": ComparisonMode.OptionIndex = 2; break;
            case "candidate1": ComparisonMode.OptionIndex = 3; break;
            case "candidate2": ComparisonMode.OptionIndex = 4; break;
            case "dispersionOff": DispersionSwitch.IsOn = false; break;
            case "dispersionOn": DispersionSwitch.IsOn = true; break;
            case "frostOn": BodyMode.OptionIndex = 1; break;
            case "frostOff": BodyMode.OptionIndex = 0; break;
            case "continuous": BodyMode.OptionIndex = 2; break;
            case "clear": BodyMode.OptionIndex = 3; break;
            case "ambientCover": BackgroundMode.OptionIndex = 8; break;
            case "ambientReference": BackgroundMode.OptionIndex = 9; break;
            case "recommended": OnRecommended(this, new RoutedEventArgs()); break;
            case "diagnostic": DispersionSlider.Value = 2; break;
            case "text": BackgroundMode.OptionIndex = 0; break;
            case "grid": BackgroundMode.OptionIndex = 1; break;
            case "covers": BackgroundMode.OptionIndex = 2; break;
            case "artLight": BackgroundMode.OptionIndex = 6; break;
            case "artDark": BackgroundMode.OptionIndex = 7; break;
            case "reference": BackgroundMode.OptionIndex = 5; break;
            case "slowMotion": PlayPatternMotion(3); break;
            case "fastMotion": PlayPatternMotion(.75); break;
            case "plainLight": BackgroundMode.OptionIndex = 3; break;
            case "plainDark": BackgroundMode.OptionIndex = 4; break;
            case "fault": SetCompositionFailure(new InvalidOperationException("TestDeviceLost"), "Test.DeviceLost"); break;
            case "replace": ReleaseMaterial(); BuildPatternReplacement(); break;
            case "initFailure": ReleaseMaterial(); failInitialization = true; break;
            case "releasePendingSnapshot": TestReleasePendingSnapshot(); break;
            case "cancelUpdate": QueueRefresh(); callbackGeneration++; refreshQueued = false; testPolicy = "unknown"; break;
            case "disposeQueued": QueueRefresh(); Dispose(); return;
            case "largeText": StableTitle.FontSize = 27; StableBody.FontSize = 21; StableCaption.FontSize = 18; break;
            case "normalText": StableTitle.FontSize = 18; StableBody.FontSize = 14; StableCaption.FontSize = 12; break;
            case "normal": case "highContrast": case "unknown": testPolicy = value; break;
            case "hover": case "pressed": SetInteraction(value); break;
            default: throw new InvalidDataException("Unknown material Test seam");
        }
        RefreshMaterial();
    }
    private void OnReviewChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!initialized || syncingReview) return;
        try { if (scenePattern != UsesOwnedScene) { ReleaseMaterial(); BuildPattern(); } RefreshMaterial(); }
        catch (Exception exception) { SetCompositionFailure(exception, "scene.switch"); }
    }
    private void OnDispersionChanged(object sender, RoutedEventArgs e) { dispersionEnabled = DispersionSwitch.IsOn; if (initialized) RefreshMaterial(); }
    private void OnBodyModeChanged(object sender, SelectionChangedEventArgs e) { bodyMode = Math.Clamp(((SelectedFirstComboBox)sender).OptionIndex, 0, 3); if (initialized) RefreshMaterial(); }
    private void OnBackgroundChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!initialized || syncingReview || appliedBackgroundMode == BackgroundMode.OptionIndex) return;
        try { BuildPattern(); StepPattern(0); RefreshMaterial(); }
        catch (Exception exception) { SetCompositionFailure(exception, "gallery.background"); UpdateState(); }
    }
    private void OnRecommended(object sender, RoutedEventArgs e)
    {
        if (!initialized) return; syncingReview = true;
        try { var p = LensOptics.Recommended; StrengthSlider.Value = p.Refraction; DispersionSlider.Value = p.Dispersion; EdgeSlider.Value = p.EdgeWidth; HighlightSlider.Value = p.Highlight; }
        finally { syncingReview = false; }
        RefreshMaterial();
    }
    private void BuildPatternReplacement() { PatternLayer.Children.Clear(); BuildPattern(); StepPattern(0); }
    private void OnAdvancedToggled(object sender, RoutedEventArgs e)
    {
        if (!initialized) return;
        advancedAllowed = AdvancedSwitch.IsOn;
        RefreshMaterial();
    }
    private void OnPatternChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (initialized && !syncingPatternSlider) StepPattern(e.NewValue);
    }
    private void OnLensChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (!initialized) return;
        LensFrame.RenderTransform = new TranslateTransform { X = e.NewValue };
        RefreshMaterial();
    }
    private void OnStrengthChanged(object sender, RangeBaseValueChangedEventArgs e) { if (initialized && !syncingReview) RefreshMaterial(); }
    private void OnResetPattern(object sender, RoutedEventArgs e)
    {
        StepPattern(0);
        LensSlider.Value = 0;
    }
    private void OnMovePattern(object sender, RoutedEventArgs e)
        => PlayPatternMotion(3);
    private void PlayPatternMotion(double seconds)
    {
        if (!initialized || disposed) return;
        var settings = ReadSettings();
        if (!ownerMotionAllowed || settings.Animations != true)
        {
            try { StopFiniteMotion(restoreStart: true); }
            catch (Exception exception) { SetCompositionFailure(exception, "motion.disabledStop"); }
            RefreshMaterial();
            return;
        }
        try
        {
            if (UsesOptical && optical?.Ready == true)
            {
                finiteMotionStartPosition = patternPosition;
                StartOpticalMotion(seconds); UpdateState(); return;
            }
            var visual = ElementCompositionPreview.GetElementVisual(PatternLayer);
            var startOffset = visual.Offset;
            finiteMotionStart = startOffset;
            finiteMotionStartPosition = patternPosition;
            patternPosition = 0;
            SyncPatternSlider();
            visual.StopAnimation("Offset");
            using var animation = visual.Compositor.CreateVector3KeyFrameAnimation();
            using var easing = visual.Compositor.CreateLinearEasingFunction();
            animation.InsertKeyFrame(0, startOffset);
            animation.InsertKeyFrame(.5f, new Vector3(-120, -16, 0), easing);
            animation.InsertKeyFrame(1, Vector3.Zero, easing);
            animation.Duration = TimeSpan.FromSeconds(seconds);
            animation.IterationBehavior = AnimationIterationBehavior.Count;
            animation.IterationCount = 1;
            visual.Offset = Vector3.Zero;
            visual.StartAnimation("Offset", animation);
            finiteMotionUntil = Stopwatch.GetTimestamp() + (long)(Stopwatch.Frequency * seconds);
        }
        catch (Exception e2) { SetCompositionFailure(e2, "motion.start"); }
        UpdateState();
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        loaded = false; callbackGeneration++; refreshQueued = false;
        ActualThemeChanged -= OnGalleryThemeChanged;
        Loaded -= OnLoaded;
        Unloaded -= OnUnloaded;
        DetachSettings();
        DetachCapabilities();
        ReleaseMaterial();
        failedRelease?.Dispose();
        failedOpticalRelease?.Dispose();
        ClearCoverImages();
        scene = null;
    }
}
