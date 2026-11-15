using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using TheOrder.WinUI.Materials;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace TheOrder.WinUI.Views;

public sealed class LoadingMark : UserControl
{
    public static readonly DependencyProperty IsActiveProperty = DependencyProperty.Register(
        nameof(IsActive), typeof(bool), typeof(LoadingMark),
        new PropertyMetadata(false, (sender, _) => ((LoadingMark)sender).Refresh()));

    private const float EmblemSize = 128;
    private MaterialResources? resources;
    private SpriteVisual? mark;
    private ContainerVisual? orbit;
    private CompositionColorBrush? restingBrush;
    private readonly List<(CompositionColorGradientStop Lit, CompositionColorGradientStop Clear, byte Alpha)> lights = [];
    private bool running;
    private bool useFallback;

    public bool IsActive { get => (bool)GetValue(IsActiveProperty); set => SetValue(IsActiveProperty, value); }

    public LoadingMark()
    {
        IsHitTestVisible = false;
        IsTabStop = false;
        Loaded += (_, _) => { InteractionMotion.PolicyChanged += Refresh; Refresh(); };
        Unloaded += (_, _) => { InteractionMotion.PolicyChanged -= Refresh; Release(); };
        ActualThemeChanged += (_, _) => Refresh();
        RegisterPropertyChangedCallback(VisibilityProperty, (_, _) => Refresh());
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new ProgressPeer(this);

    private void Refresh()
    {
        if (!IsLoaded) return;
        var visible = IsActive && Visibility == Visibility.Visible;
        if (useFallback) { ShowFallback(visible); return; }
        if (mark is null && !visible) return;

        try
        {
            if (mark is null) CreateMark();
            mark!.IsVisible = visible;
            var light = ActualTheme == ElementTheme.Light;
            var highContrast = InteractionMotion.HighContrast;
            restingBrush!.Color = highContrast ? new UISettings().GetColorValue(UIColorType.Foreground)
                : light ? Color.FromArgb(255, 166, 166, 166) : Color.FromArgb(255, 131, 158, 174);
            var highlight = light ? Color.FromArgb(255, 79, 129, 163) : Color.FromArgb(255, 156, 221, 255);
            foreach (var (lit, clear, alpha) in lights)
            {
                lit.Color = Color.FromArgb(alpha, highlight.R, highlight.G, highlight.B);
                clear.Color = Color.FromArgb(0, highlight.R, highlight.G, highlight.B);
            }

            var animate = visible && !highContrast && InteractionMotion.Enabled;
            orbit!.IsVisible = animate;
            if (running == animate) return;
            running = animate;
            if (!animate)
            {
                orbit.StopAnimation(nameof(Visual.RotationAngleInDegrees));
                return;
            }

            var compositor = orbit.Compositor;
            using var linear = compositor.CreateLinearEasingFunction();
            using var rotation = compositor.CreateScalarKeyFrameAnimation();
            rotation.InsertKeyFrame(0, 0);
            rotation.InsertKeyFrame(1, 360, linear);
            rotation.Duration = TimeSpan.FromMilliseconds(1080);
            rotation.IterationBehavior = AnimationIterationBehavior.Forever;
            orbit.StartAnimation(nameof(Visual.RotationAngleInDegrees), rotation);
        }
        catch (Exception error)
        {
            App.TraceVisualFailure(error);
            UseStaticEmblem();
        }
    }

    private void CreateMark()
    {
        var compositor = ElementCompositionPreview.GetElementVisual(this).Compositor;
        var owned = resources = new MaterialResources();
        var scene = owned.Own(compositor.CreateContainerVisual());
        scene.Size = new(EmblemSize);
        var rest = owned.Own(compositor.CreateSpriteVisual());
        rest.RelativeSizeAdjustment = Vector2.One;
        rest.Brush = restingBrush = owned.Own(compositor.CreateColorBrush());
        scene.Children.InsertAtBottom(rest);

        // The light orbits in a layer twice as wide as the emblem. The emblem mask stays fixed.
        orbit = owned.Own(compositor.CreateContainerVisual());
        orbit.Size = new(EmblemSize * 2);
        orbit.Offset = new(-EmblemSize / 2, -EmblemSize / 2, 0);
        orbit.CenterPoint = new(EmblemSize, EmblemSize, 0);
        scene.Children.InsertAtTop(orbit);
        AddLight(compositor, owned, new(.315f, .5f), 71);
        AddLight(compositor, owned, new(.685f, .5f), 71);
        AddLight(compositor, owned, new(.5f, .315f), 255);

        var surface = owned.Own(compositor.CreateVisualSurface());
        surface.SourceVisual = scene;
        surface.SourceSize = scene.Size;
        var source = owned.Own(compositor.CreateSurfaceBrush(surface));
        var emblem = owned.Own(LoadedImageSurface.StartLoadFromUri(new("ms-appx:///Assets/LoadingMark.png")));
        emblem.LoadCompleted += (_, args) =>
        {
            if (ReferenceEquals(resources, owned) && args.Status != LoadedImageSourceLoadStatus.Success)
                UseStaticEmblem();
        };
        var silhouette = owned.Own(compositor.CreateSurfaceBrush(emblem));
        silhouette.Stretch = CompositionStretch.Uniform;
        var brush = owned.Own(compositor.CreateMaskBrush());
        brush.Source = source;
        brush.Mask = silhouette;
        mark = owned.Own(compositor.CreateSpriteVisual());
        mark.RelativeSizeAdjustment = Vector2.One;
        mark.Brush = brush;
        ElementCompositionPreview.SetElementChildVisual(this, mark);
    }

    private void AddLight(Compositor compositor, MaterialResources owned, Vector2 center, byte alpha)
    {
        var brush = owned.Own(compositor.CreateRadialGradientBrush());
        brush.EllipseCenter = center;
        brush.EllipseRadius = new(.24f, .26f);
        var lit = owned.Own(compositor.CreateColorGradientStop(.06f, default));
        var clear = owned.Own(compositor.CreateColorGradientStop(1, default));
        brush.ColorStops.Add(lit);
        brush.ColorStops.Add(clear);
        lights.Add((lit, clear, alpha));
        var layer = owned.Own(compositor.CreateSpriteVisual());
        layer.RelativeSizeAdjustment = Vector2.One;
        layer.Brush = brush;
        orbit!.Children.InsertAtTop(layer);
    }

    private void UseStaticEmblem()
    {
        Release();
        useFallback = true;
        ShowFallback(IsActive && Visibility == Visibility.Visible);
    }

    private void ShowFallback(bool visible)
    {
        if (!visible) { Content = null; return; }
        var asset = ActualTheme == ElementTheme.Light ? "Brand.Light.png" : "Brand.Dark.png";
        Content = new Image { Source = new BitmapImage(new($"ms-appx:///Assets/{asset}")), Stretch = Stretch.Uniform };
    }

    private void Release()
    {
        if (mark is not null) ElementCompositionPreview.SetElementChildVisual(this, null);
        var owned = resources;
        resources = null;
        owned?.Dispose();
        lights.Clear();
        mark = null;
        orbit = null;
        restingBrush = null;
        running = useFallback = false;
        Content = null;
    }

    private sealed class ProgressPeer(LoadingMark owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.ProgressBar;
        protected override bool IsControlElementCore() => owner.IsActive && base.IsControlElementCore();
        protected override bool IsContentElementCore() => owner.IsActive && base.IsContentElementCore();
    }
}
