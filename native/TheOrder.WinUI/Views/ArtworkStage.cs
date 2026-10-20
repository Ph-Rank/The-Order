using System.Numerics;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using TheOrder.Application;
using TheOrder.Presentation;
using TheOrder.WinUI.Materials;

namespace TheOrder.WinUI.Views;

// App quality setting (设置 › 材质效果). System rules (MaterialPolicy) always win.
public enum MaterialMode { Auto, Advanced, Standard, Opaque }
public enum StageVariant { Clear, Continuous, Unrefracted }
public enum StagePolarity { Deepen, Lift }

// Artwork stage: an app-rendered ambient band built from the game's own decoded cover pixels, with one
// Liquid Glass plate. The background is stable (it changes only with cover, size, DPI or policy), so the
// glass is event-driven: one scene revision per change, draws only on geometry change, zero idle work.
// Clear/continuous bodies retain their bounded tint. User choice (2026-10-10):
// the Gallery's second, daily-glass optical profile, with dispersion disabled.
public sealed partial class ArtworkStage : Grid
{
    public const string ClearConfiguration = "日常玻璃 · 清透 · 23/20/16/0/.32 · tint α .055/.075 · bound .30/.10 · edge .20 · aligned source";
    public const string ContinuousConfiguration = "日常玻璃 · 连续 · 23/20/16/0/.32 · σ12 · tint α .22/.30 · bound .42/.08 · edge .20";
    public const string UnrefractedConfiguration = "轻玻璃 · 无折射／无色散 · 圆角18 · σ1 · tint α .055/.075 · bound .30/.10 · edge .04";
    private readonly Image fallbackArt = new() { Stretch = Stretch.UniformToFill, IsHitTestVisible = false };
    private readonly Rectangle fallbackScrim = new() { IsHitTestVisible = false };
    private readonly SwapChainPanel panel = new() { IsHitTestVisible = false, Visibility = Visibility.Collapsed };
    // SwapChainPanel's opaque DirectX output can bypass an ancestor clip. A tiny
    // XAML corner mask above it keeps the same rounded silhouette as fallback art.
    private readonly Microsoft.UI.Xaml.Shapes.Path cornerMask = new() { IsHitTestVisible = false };
    private readonly Grid artworkLayers = new();
    private readonly Canvas cornerCanvas = new() { IsHitTestVisible = false };
    private readonly RectangleGeometry cornerOuter = new();
    private readonly PathGeometry cornerInner = new();
    private CompositionRoundedRectangleGeometry? stageGeometry;
    private CompositionGeometricClip? stageClip;
    private readonly ImageRequestSlot request = new();
    private readonly float[] constants = new float[28];
    private DispatcherQueueTimer? debounce;
    private OpticalRenderer? optical;
    private RasterPixels? pixels;
    private RasterPixels? originalLandscape;
    private string? requestedImageKey;
    private int requestedImageEdge, loadedImageEdge;
    private string? landscapeComposition;
    public bool AdaptiveLandscape { get; set; }
    public bool NaturalLandscape { get; set; }
    private bool detachedPlate;
    public bool DetachedPlate
    {
        get => detachedPlate;
        set { if (value == detachedPlate) return; detachedPlate = value; SetPresentationReady(false); Invalidate(immediate: true); }
    }
    public int SourceWidth => originalLandscape?.Width ?? 0;
    public int SourceHeight => originalLandscape?.Height ?? 0;
    public event Action? SourceChanged;
    public event Action? PresentationChanged;
    public bool PresentationReady { get; private set; }
    private bool imageFailed;
    internal bool ImageUnavailable => imageFailed && pixelsKey != coverKey;
    internal void RetryFailedImage()
    {
        if (!ImageUnavailable || coverKey is null) return;
        request.Clear(); requestedImageKey = null; requestedImageEdge = 0;
        imageFailed = false; LastFailure = null; SetPresentationReady(false);
        SourceChanged?.Invoke(); Invalidate(immediate: true);
    }
    // A successful draw alone can precede the final XAML arrange. A hero reveals
    // its artwork and foreground together only when this geometry also matches.
    internal bool HasCurrentPresentation => PresentationReady && (coverKey is null || imageFailed
        || Effective != "Glass" || DetachedPlate || loaded && !retirementPending && optical is not null
        && panel.Visibility == Visibility.Visible && geometry == CurrentGeometry()
        && Math.Abs(sceneWidth - ActualWidth) < .5 && Math.Abs(sceneHeight - ActualHeight) < .5
        && (!double.IsFinite(Width) || Math.Abs(Width - ActualWidth) < .5)
        && (!double.IsFinite(Height) || Math.Abs(Height - ActualHeight) < .5));
    private void SetPresentationReady(bool value)
    {
        if (PresentationReady == value) return;
        PresentationReady = value; PresentationChanged?.Invoke();
    }
    internal (bool Right, double Cost) SuggestPlacement(double width, double height)
        => originalLandscape is null ? (false, 0) : BannerAesthetics.Placement(originalLandscape, width, height);
    private string? pixelsKey, sceneKey, geometry;
    private double sceneWidth, sceneHeight, sceneScale;
    private bool loaded, allowed, highContrast, attached, initFailed, retirementPending;
    private UIElement? overlay;
    public IImagePipeline? Images { get; set; }
    public Border? Plate { get; set; }
    public StageVariant Variant { get; set; } = StageVariant.Clear;
    public float AmbientSigma { get; set; } = 18;
    public int DecodeEdge { get; set; } = 512;
    private bool containBackdrop;
    public bool ContainBackdrop {
        get => containBackdrop;
        set { if (containBackdrop == value) return; containBackdrop = value; fallbackArt.Stretch = value ? Stretch.Uniform : Stretch.UniformToFill; sceneKey = null; Invalidate(immediate: true); }
    }
    public double StageCornerRadius { get; set; }
    public MaterialMode Mode { get; private set; } = MaterialMode.Auto;
    public StagePolarity Polarity { get; private set; } = StagePolarity.Deepen;
    public string Effective { get; private set; } = "Standard";
    public string? LastFailure { get; private set; }
    public long SceneRevisions { get; private set; }
    public long Draws { get; private set; }
    public long RenderersCreated { get; private set; }
    public long RenderersReleased { get; private set; }
    public long FrostPasses { get; private set; }
    // Text colour of the plate: polarity-driven over artwork, theme-driven when the stage is opaque/HC.
    public event Action<Brush?>? ForegroundChanged;
    public UIElement? Overlay
    {
        get => overlay;
        set { if (overlay is not null) Children.Remove(overlay); overlay = value; if (value is not null) Children.Add(value); }
    }
    private string? coverKey;
    public string? CoverKey
    {
        get => coverKey;
        set
        {
            if (coverKey == value) return;
            SetPresentationReady(false);
            // Retire the old artwork before awaiting another cover. A missing/failed cover must not
            // leave the preceding game's swapchain or fallback bitmap behind the new foreground.
            request.Clear(); Release(); fallbackArt.Source = null;
            coverKey = value; pixels = null; originalLandscape = null; landscapeComposition = null; pixelsKey = null; sceneKey = null;
            requestedImageKey = null; requestedImageEdge = loadedImageEdge = 0;
            imageFailed = false; LastFailure = null;
            Effective = EffectiveMode(); ApplyLayers();
            ForegroundChanged?.Invoke(null);
            SourceChanged?.Invoke();
            Invalidate(immediate: true);
        }
    }
    public ArtworkStage()
    {
        var corners = new GeometryGroup { FillRule = FillRule.EvenOdd }; corners.Children.Add(cornerOuter); corners.Children.Add(cornerInner); cornerMask.Data = corners;
        artworkLayers.Children.Add(fallbackArt); artworkLayers.Children.Add(fallbackScrim); artworkLayers.Children.Add(panel);
        cornerCanvas.Children.Add(cornerMask); Children.Add(artworkLayers); Children.Add(cornerCanvas);
        Loaded += (_, _) => { loaded = true; Attach(); ApplyClip(); Invalidate(immediate: true); };
        // Re-parenting (e.g. grid header → list header) can raise the old Unloaded after the new Loaded.
        Unloaded += (_, _) => { if (IsLoaded) return; loaded = false; SetPresentationReady(false); Detach(); Release(); ReleaseClip(); request.Clear(); requestedImageKey = null; };
        ActualThemeChanged += (_, _) =>
        {
            if (!ContainBackdrop && !AdaptiveLandscape) return;
            // Theme propagation can raise this event before inherited brushes/ActualTheme settle.
            // Refresh once after propagation, without a polling timer or a new image analysis.
            DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () => Invalidate(immediate: true));
        };
        SizeChanged += (_, _) => { ApplyClip(); if (optical is not null) DrawNow(); Invalidate(immediate: false); };
    }
    // Called by the window policy owner: effects allowed and window visible, plus the user's quality mode.
    public void SetPolicy(bool effectsAllowed, bool contrast, MaterialMode mode)
    {
        var changed = effectsAllowed != allowed || contrast != highContrast || mode != Mode;
        allowed = effectsAllowed; highContrast = contrast;
        if (mode != Mode) { Mode = mode; initFailed = false; }
        if (changed) { SetPresentationReady(false); Invalidate(immediate: true); }
    }
    // Before the page leaves the visual tree (proven early-release order for swapchains).
    public void ReleaseBeforeLeave() { loaded = false; SetPresentationReady(false); request.Clear(); requestedImageKey = null; Detach(); Release(); ReleaseClip(); }
    // After an explicit early release while staying in the tree (re-parenting), resume event-driven drawing.
    public void Reattach() { if (!IsLoaded) return; loaded = true; Attach(); ApplyClip(); Invalidate(immediate: true); }
    private void Attach()
    {
        if (attached) return; attached = true;
        LayoutUpdated += OnLayoutUpdated;
        if (XamlRoot is not null) XamlRoot.Changed += OnRootChanged;
    }
    private void Detach()
    {
        if (!attached) return; attached = false;
        LayoutUpdated -= OnLayoutUpdated;
        if (XamlRoot is not null) XamlRoot.Changed -= OnRootChanged;
        debounce?.Stop();
    }
    private void OnRootChanged(XamlRoot sender, XamlRootChangedEventArgs args) { if (sender.RasterizationScale != sceneScale) Invalidate(immediate: false); }
    private void OnLayoutUpdated(object? sender, object e)
    {
        if (optical is null || Plate is null || !loaded) return;
        if (Math.Abs(sceneWidth - ActualWidth) >= .5 || Math.Abs(sceneHeight - ActualHeight) >= .5) Refresh();
        else if (CurrentGeometry() != geometry) DrawNow();
    }
    private string CurrentGeometry()
    {
        var rect = PlateRect();
        return $"{rect.X:F1}/{rect.Y:F1}/{rect.Width:F1}/{rect.Height:F1}/{ActualWidth:F1}/{ActualHeight:F1}";
    }
    private Windows.Foundation.Rect PlateRect()
    {
        if (Plate is null || Plate.ActualWidth <= 0) return default;
        var p = Plate.TransformToVisual(this).TransformPoint(new Windows.Foundation.Point());
        return new(p.X, p.Y, Plate.ActualWidth, Plate.ActualHeight);
    }
    private void ApplyClip()
    {
        if (!loaded || ActualWidth <= 0 || ActualHeight <= 0) return;
        if (!double.IsFinite(StageCornerRadius) || StageCornerRadius <= 0) { ReleaseClip(); cornerMask.Visibility = Visibility.Collapsed; return; }
        var visual = ElementCompositionPreview.GetElementVisual(artworkLayers);
        var radius = Math.Min(StageCornerRadius, Math.Min(ActualWidth, ActualHeight) / 2);
        stageGeometry ??= visual.Compositor.CreateRoundedRectangleGeometry();
        stageClip ??= visual.Compositor.CreateGeometricClip(stageGeometry);
        stageGeometry.Size = new Vector2((float)ActualWidth, (float)ActualHeight); stageGeometry.CornerRadius = new Vector2((float)radius);
        visual.Clip = stageClip;
        var bounds = new Windows.Foundation.Rect(0, 0, ActualWidth, ActualHeight);
        cornerOuter.Rect = bounds;
        var rounded = new PathFigure { StartPoint = new(radius, 0), IsClosed = true, IsFilled = true };
        void Line(double x, double y) => rounded.Segments.Add(new LineSegment { Point = new(x, y) });
        void Arc(double x, double y) => rounded.Segments.Add(new ArcSegment { Point = new(x, y), Size = new(radius, radius), SweepDirection = SweepDirection.Clockwise });
        Line(ActualWidth - radius, 0); Arc(ActualWidth, radius);
        Line(ActualWidth, ActualHeight - radius); Arc(ActualWidth - radius, ActualHeight);
        Line(radius, ActualHeight); Arc(0, ActualHeight - radius);
        Line(0, radius); Arc(radius, 0);
        cornerInner.Figures.Clear(); cornerInner.Figures.Add(rounded);
        cornerMask.Visibility = Visibility.Visible;
    }
    private void ReleaseClip()
    {
        if (stageClip is null) return;
        ElementCompositionPreview.GetElementVisual(artworkLayers).Clip = null;
        stageClip.Dispose(); stageClip = null; stageGeometry?.Dispose(); stageGeometry = null;
    }
    private void Invalidate(bool immediate)
    {
        if (!loaded) return;
        if (immediate) { Refresh(); return; }
        debounce ??= CreateDebounce();
        debounce.Stop(); debounce.Start();
    }
    private DispatcherQueueTimer CreateDebounce()
    {
        var timer = DispatcherQueue.CreateTimer(); timer.Interval = TimeSpan.FromMilliseconds(120); timer.IsRepeating = false;
        timer.Tick += (_, _) => Refresh(); return timer;
    }
    private string EffectiveMode() => highContrast ? "HighContrast" : Mode == MaterialMode.Opaque || !allowed ? "Opaque"
        : Mode == MaterialMode.Standard || (Mode == MaterialMode.Auto && initFailed) ? "Standard" : "Glass";
    private void Refresh()
    {
        if (!loaded) return;
        // OpticalDestroy can fail to detach. Keep its owner for a later lifecycle retry,
        // and never draw or replace it while retirement is incomplete.
        if (retirementPending) { Release(); if (retirementPending) return; }
        Effective = EffectiveMode();
        if (Effective != "Glass" || DetachedPlate) Release();
        ComposeLandscape();
        UpdateArtworkPolarity();
        ApplyLayers();
        if (coverKey is null || imageFailed)
        { SetPresentationReady(true); return; }
        if (Effective is "Opaque" or "HighContrast") SetPresentationReady(true);
        if (pixels is null || pixelsKey != coverKey) { StartLoad(); return; }
        if (AdaptiveLandscape && loadedImageEdge < RequiredImageEdge()) StartLoad();
        if (Effective != "Glass" || DetachedPlate) { SetPresentationReady(true); return; }
        try
        {
            var scale = XamlRoot?.RasterizationScale ?? 1;
            if (ActualWidth < 1 || ActualHeight < 1) return;
            if (optical is null) { optical = new OpticalRenderer(panel); RenderersCreated++; sceneKey = null; }
            var key = $"{pixelsKey}/{ActualWidth:F0}/{ActualHeight:F0}/{scale}/{Variant}/{AmbientSigma}/{Polarity}/{ContainBackdrop}";
            if (key != sceneKey)
            {
                var scrim = AdaptiveLandscape ? 0u : Polarity == StagePolarity.Deepen ? 0x38000000u : 0x2EFFFFFFu;
                optical.RenderArtwork(pixels, ActualWidth, ActualHeight, scale, AmbientSigma, Polarity == StagePolarity.Lift ? 0xFFF0F0F0u : 0xFF171923u, scrim, ContainBackdrop);
                sceneWidth = ActualWidth; sceneHeight = ActualHeight; sceneScale = scale; sceneKey = key; SceneRevisions++;
                if (Variant is StageVariant.Continuous or StageVariant.Unrefracted) { optical.Frost(Variant == StageVariant.Unrefracted ? 1 : 12); FrostPasses++; }
                else if (optical.FrostBytes != 0) optical.Frost(0);
            }
            DrawNow();
        }
        catch (Exception exception) { Fail(exception); }
    }
    private void DrawNow()
    {
        if (optical is null || Plate is null || sceneWidth <= 0) return;
        try
        {
            var rect = PlateRect();
            if (rect.Width < 64 || rect.Height < 64) { panel.Visibility = Visibility.Collapsed; return; }
            // The final title footprint can differ from the initial layout.
            // Rebuild only when it changes the readable polarity, not on idle.
            if (UpdateArtworkPolarity()) { Refresh(); return; }
            var scale = (float)(XamlRoot?.RasterizationScale ?? 1);
            var lift = Polarity == StagePolarity.Lift; var clear = Variant == StageVariant.Clear;
            var flat = Variant == StageVariant.Unrefracted; var lightTint = clear || flat;
            var c = constants;
            c[0] = (float)ActualWidth; c[1] = (float)ActualHeight; c[2] = (float)sceneWidth; c[3] = (float)sceneHeight;
            c[4] = (float)(rect.X + rect.Width / 2); c[5] = (float)(rect.Y + rect.Height / 2); c[6] = (float)rect.Width / 2; c[7] = (float)rect.Height / 2;
            var profile = OpticalProfile.Daily;
            c[8] = flat ? 18 : 23; c[9] = flat ? 4 : profile.BevelWidth; c[10] = flat ? 0 : profile.Depth; c[11] = 0; c[12] = flat ? .10f : profile.Gloss; c[13] = 0; c[14] = c[15] = 0; c[16] = scale;
            c[17] = 0; c[18] = clear ? 1 : 0; c[19] = 0;
            c[20] = c[21] = c[22] = lift ? 250 / 255f : 32 / 255f;
            c[23] = lightTint ? (lift ? .055f : .075f) : (lift ? .22f : .30f);
            c[24] = 1; c[25] = lightTint ? (lift ? .30f : .10f) : (lift ? .42f : .08f); c[26] = lift ? 1 : -1; c[27] = flat ? .04f : .20f;
            optical.Draw((int)Math.Ceiling(ActualWidth * scale), (int)Math.Ceiling(ActualHeight * scale), c);
            geometry = $"{rect.X:F1}/{rect.Y:F1}/{rect.Width:F1}/{rect.Height:F1}/{ActualWidth:F1}/{ActualHeight:F1}";
            Draws++;
            panel.Visibility = Visibility.Visible; fallbackArt.Visibility = Visibility.Collapsed; fallbackScrim.Visibility = Visibility.Collapsed;
            if (Plate.Background is not SolidColorBrush { Color.A: 0 }) Plate.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            SetPresentationReady(true);
        }
        catch (Exception exception) { Fail(exception); }
    }
    private void StartLoad()
    {
        if (coverKey is null) return;
        if (Images is null) { ImageFailed("unavailable"); return; }
        var key = coverKey;
        var edge = RequiredImageEdge();
        if (requestedImageKey == key && requestedImageEdge == edge) return;
        requestedImageKey = key; requestedImageEdge = edge;
        var lease = request.Begin(key);
        _ = LoadAsync(Images, key, edge, lease.Generation, lease.Cancellation);
    }
    private int RequiredImageEdge() => AdaptiveLandscape
        ? ArtworkSizing.Edge(ActualWidth, ActualHeight, XamlRoot?.RasterizationScale ?? 1, DecodeEdge)
        : Math.Clamp(DecodeEdge, 512, ArtworkSizing.MaximumEdge);
    private async Task LoadAsync(IImagePipeline images, string key, int edge, long version, CancellationToken cancellation)
    {
        try
        {
            var result = await images.LoadAsync(key, edge, cancellation);
            if (!loaded || !request.IsCurrent(key, version) || coverKey != key) return;
            if (result.Pixels is not { } loadedPixels) { ImageFailed("unavailable"); return; }
            pixels = loadedPixels; originalLandscape = AdaptiveLandscape ? loadedPixels : null; landscapeComposition = null; pixelsKey = key;
            loadedImageEdge = edge; sceneKey = null;
            SourceChanged?.Invoke();
            ComposeLandscape();
            if (!AdaptiveLandscape) Polarity = ContainBackdrop ? ActualTheme == ElementTheme.Light ? StagePolarity.Lift : StagePolarity.Deepen : MeanLuminance(loadedPixels) > .30 ? StagePolarity.Lift : StagePolarity.Deepen;
            fallbackArt.Source = DisplayBitmaps.Get(images, pixels!);
            Refresh();
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception exception) { if (loaded && request.IsCurrent(key, version) && coverKey == key) ImageFailed(exception.GetType().Name); }
    }
    private void ImageFailed(string reason)
    {
        LastFailure = "cover:" + reason;
        // Failure of a higher-resolution upgrade keeps the valid existing frame.
        if (pixelsKey == coverKey && pixels is not null && fallbackArt.Source is not null) return;
        pixels = originalLandscape = null; pixelsKey = null; fallbackArt.Source = null;
        imageFailed = true; ApplyLayers();
        // The host must discard dimensions reserved for an image that never arrived.
        // A successful lower-resolution frame above remains visible on upgrade failure.
        SourceChanged?.Invoke(); SetPresentationReady(true);
    }
    private void ComposeLandscape()
    {
        if (!AdaptiveLandscape || originalLandscape is null || Images is null || ActualWidth < 1 || ActualHeight < 1) return;
        if (NaturalLandscape)
        {
            pixels = originalLandscape;
            containBackdrop = false;
            fallbackArt.Stretch = Stretch.Uniform;
            fallbackArt.Source = DisplayBitmaps.Get(Images, pixels);
            return;
        }
        var key = $"{ActualWidth:F0}/{ActualHeight:F0}"; if (landscapeComposition == key) return;
        var plan = BannerSelection.Plan(originalLandscape, ActualWidth / ActualHeight);
        pixels = BannerSelection.ComposeBackdrop(originalLandscape, plan, ActualWidth / ActualHeight); landscapeComposition = key; sceneKey = null;
        containBackdrop = false; fallbackArt.Stretch = Stretch.UniformToFill;
        fallbackArt.Source = DisplayBitmaps.Get(Images, pixels);
    }
    private bool UpdateArtworkPolarity()
    {
        if (!AdaptiveLandscape || DetachedPlate || pixels is null || ActualWidth < 1 || ActualHeight < 1) return false;
        var rect = PlateRect();
        bool lightText = rect.Width > 0 && rect.Height > 0
            ? ArtworkReadability.PreferLightText(pixels, rect.X / ActualWidth, rect.Y / ActualHeight, rect.Width / ActualWidth, rect.Height / ActualHeight)
            : ArtworkReadability.PreferLightText(pixels, .03, .60, .60, .37);
        var chosen = lightText ? StagePolarity.Deepen : StagePolarity.Lift;
        if (chosen == Polarity) return false;
        Polarity = chosen; sceneKey = null;
        return true;
    }
    // Mean linear luminance over a sparse grid of the decoded cover (once per cover).
    internal static double MeanLuminance(RasterPixels value)
    {
        static double Linear(byte channel) { var c = channel / 255d; return c <= .04045 ? c / 12.92 : Math.Pow((c + .055) / 1.055, 2.4); }
        double sum = 0; var count = 0; var stepX = Math.Max(1, value.Width / 48); var stepY = Math.Max(1, value.Height / 48);
        for (var y = 0; y < value.Height; y += stepY)
            for (var x = 0; x < value.Width; x += stepX)
            {
                var i = (y * value.Width + x) * 4;
                sum += .2126 * Linear(value.Bgra[i + 2]) + .7152 * Linear(value.Bgra[i + 1]) + .0722 * Linear(value.Bgra[i]); count++;
            }
        return count == 0 ? 0 : sum / count;
    }
    private void ApplyLayers()
    {
        cornerMask.Fill = OwnedBrush(ref canvasBrush, "SolidBackgroundFillColorBase");
        // CG title polarity follows its local artwork, independently of the
        // page palette. Theme switches still refresh the outer corner mask.
        if (ContainBackdrop && pixels is not null) { Polarity = ActualTheme == ElementTheme.Light ? StagePolarity.Lift : StagePolarity.Deepen; fallbackArt.HorizontalAlignment = HorizontalAlignment.Right; fallbackArt.Width = ActualHeight * pixels.Width / pixels.Height; }
        else { fallbackArt.HorizontalAlignment = HorizontalAlignment.Stretch; fallbackArt.Width = double.NaN; }
        // Transparency, energy saving and graphics fallback govern effects, not content.
        // Keep the decoded static picture when Acrylic/DirectX are unavailable.
        var art = Effective != "HighContrast" && fallbackArt.Source is not null && pixelsKey == coverKey;
        var glassShowing = Effective == "Glass" && optical is not null && panel.Visibility == Visibility.Visible;
        fallbackArt.Visibility = art && !glassShowing ? Visibility.Visible : Visibility.Collapsed;
        fallbackScrim.Visibility = fallbackArt.Visibility;
        if (AdaptiveLandscape)
        {
            // Keep the whole CG clear in the middle and blend its edges into the page palette.
            var color = (OwnedBrush(ref canvasBrush, "SolidBackgroundFillColorBase") as SolidColorBrush)?.Color
                ?? (ActualTheme == ElementTheme.Light ? Microsoft.UI.Colors.White : Microsoft.UI.Colors.Black);
            var gradient = new LinearGradientBrush { StartPoint = new(0, 0), EndPoint = new(0, 1) };
            gradient.GradientStops.Add(new() { Offset = 0, Color = Windows.UI.Color.FromArgb(0x68, color.R, color.G, color.B) });
            gradient.GradientStops.Add(new() { Offset = .18, Color = Windows.UI.Color.FromArgb(0x08, color.R, color.G, color.B) });
            gradient.GradientStops.Add(new() { Offset = .60, Color = Windows.UI.Color.FromArgb(0x08, color.R, color.G, color.B) });
            gradient.GradientStops.Add(new() { Offset = 1, Color = Windows.UI.Color.FromArgb(0xBE, color.R, color.G, color.B) });
            fallbackScrim.Fill = gradient;
        }
        else fallbackScrim.Fill = new SolidColorBrush(Polarity == StagePolarity.Deepen ? Windows.UI.Color.FromArgb(0x8C, 0, 0, 0) : Windows.UI.Color.FromArgb(0x80, 0xFF, 0xFF, 0xFF));
        if (NaturalLandscape) fallbackScrim.Visibility = Visibility.Collapsed;
        fallbackArt.Opacity = NaturalLandscape ? 1 : AdaptiveLandscape ? .90 : 1;
        // Overlay theme first (R3), then surfaces: the plate must never resolve theme brushes under a stale theme.
        var overArt = !DetachedPlate && (Effective is "Glass" or "Standard") && (art || glassShowing);
        ForegroundChanged?.Invoke(overArt ? new SolidColorBrush(Polarity == StagePolarity.Deepen ? Microsoft.UI.Colors.White : Windows.UI.Color.FromArgb(0xE4, 0, 0, 0)) : null);
        // Fluent layer/card colors are absent in the HighContrast dictionary.
        // Resolving them through a dynamically loaded brush can yield WinUI's
        // unresolved-color sentinel. Use the selected system contrast palette.
        Background = Effective == "HighContrast" ? OwnedBrush(ref contrastBrush, "SystemColorWindowColor")
            : Effective == "Opaque" || !art && !glassShowing ? OwnedBrush(ref layerBrush, "LayerFillColorDefault") : ContainBackdrop ? new SolidColorBrush(Polarity == StagePolarity.Lift ? Windows.UI.Color.FromArgb(255, 240, 240, 240) : Windows.UI.Color.FromArgb(255, 23, 25, 35)) : AdaptiveLandscape ? OwnedBrush(ref canvasBrush, "SolidBackgroundFillColorBase") : null;
        if (Plate is not null && (Effective != "Glass" || DetachedPlate || coverKey is null || imageFailed))
        {
            // Standard: system in-app Acrylic, Fluent radius. Opaque/HC: card surface.
            Plate.CornerRadius = new CornerRadius(Variant == StageVariant.Unrefracted ? 18 : 8);
            Plate.Background = !DetachedPlate && Effective == "Standard" && art
                ? new AcrylicBrush { TintColor = Polarity == StagePolarity.Deepen ? Windows.UI.Color.FromArgb(255, 32, 32, 32) : Windows.UI.Color.FromArgb(255, 250, 250, 250), TintOpacity = AdaptiveLandscape ? .32 : .55, TintLuminosityOpacity = AdaptiveLandscape ? .40 : .65,
                    FallbackColor = Polarity == StagePolarity.Deepen ? Windows.UI.Color.FromArgb(255, 44, 44, 44) : Windows.UI.Color.FromArgb(255, 245, 245, 245) }
                : Effective == "HighContrast" ? OwnedBrush(ref contrastBrush, "SystemColorWindowColor")
                : OwnedBrush(ref contentBrush, "CardBackgroundFillColorDefault");
        }
        else if (Plate is not null)
        {
            Plate.CornerRadius = new CornerRadius(Variant == StageVariant.Unrefracted ? 18 : 23);
            // The optical frame owns this surface. Hero presentation stays hidden
            // until both the first draw and the final foreground layout are ready.
            if (Plate.Background is not SolidColorBrush { Color.A: 0 })
                Plate.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        }
    }
    // Stage-owned theme brushes. Shared app brushes must not be assigned here: the plate can carry a different
    // RequestedTheme (polarity), and a shared brush's ThemeResource would be re-resolved for every user of it.
    private Brush? layerBrush, contentBrush, contrastBrush, canvasBrush;
    private static Brush OwnedBrush(ref Brush? cache, string colorKey) => cache ??= (Brush)Microsoft.UI.Xaml.Markup.XamlReader.Load(
        $"<SolidColorBrush xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' Color='{{ThemeResource {colorKey}}}'/>");
    private void Fail(Exception exception)
    {
        LastFailure = exception.GetType().Name + ":0x" + exception.HResult.ToString("X8");
        initFailed = true; Release(); Effective = "Standard"; ApplyLayers(); SetPresentationReady(true);
    }
    private void Release()
    {
        panel.Visibility = Visibility.Collapsed;
        var owner = optical; sceneKey = null; geometry = null; sceneWidth = 0;
        if (owner is null) return;
        retirementPending = true;
        try { owner.Dispose(); optical = null; retirementPending = false; RenderersReleased++; }
        catch (Exception exception) { LastFailure = "release:" + exception.GetType().Name; }
    }
    internal object Evidence { get { var plateBounds = PlateRect(); return new
    {
        effective = Effective, presentationReady = PresentationReady, currentPresentation = HasCurrentPresentation, imageFailed, mode = Mode.ToString(), variant = Variant.ToString(), configuration = Variant switch { StageVariant.Clear => ClearConfiguration, StageVariant.Unrefracted => UnrefractedConfiguration, _ => ContinuousConfiguration },
        refractionDepth = constants[10], dispersion = constants[11], gloss = constants[12], edgeDensity = constants[27],
        naturalLandscape = NaturalLandscape, detachedPlate = DetachedPlate, sourceWidth = SourceWidth, sourceHeight = SourceHeight,
        visibility = Visibility.ToString(), layoutWidth = double.IsFinite(Width) ? Width : (double?)null, layoutHeight = double.IsFinite(Height) ? Height : (double?)null,
        coverKey, loadedCoverKey = pixelsKey, fallbackPresent = fallbackArt.Source is not null, fallbackVisible = fallbackArt.Visibility == Visibility.Visible, plateBrush = Plate?.Background?.GetType().Name,
        plate = new { x = plateBounds.X, y = plateBounds.Y, width = plateBounds.Width, height = plateBounds.Height, insideFrame = plateBounds.X >= -.5 && plateBounds.Y >= -.5 && plateBounds.X + plateBounds.Width <= ActualWidth + .5 && plateBounds.Y + plateBounds.Height <= ActualHeight + .5 },
        backgroundColor = (Background as SolidColorBrush)?.Color.ToString(), plateColor = (Plate?.Background as SolidColorBrush)?.Color.ToString(),
        loaded, actualTheme = ActualTheme.ToString(), requestedTheme = RequestedTheme.ToString(), containBackdrop, adaptiveLandscape = AdaptiveLandscape, imageOpacity = fallbackArt.Opacity, decodeEdge = DecodeEdge, polarity = Polarity.ToString(), ambientSigma = AmbientSigma, allowed, highContrast, rendererActive = optical is not null, panelVisible = panel.Visibility == Visibility.Visible,
        sceneRevisions = SceneRevisions, draws = Draws, frostPasses = FrostPasses, renderersCreated = RenderersCreated, renderersReleased = RenderersReleased,
        sourceBytes = optical?.SourceBytes ?? 0, frostBytes = optical?.FrostBytes ?? 0, coverLoaded = pixelsKey is not null && pixelsKey == coverKey, failure = LastFailure, attached, retirementPending
    }; } }
}
