using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace TheOrder.WinUI.Views;

// Measure the artwork at its final size while exposing only the current
// viewport height to the details column.
public sealed class DetailsBannerHost : Panel
{
    private readonly RectangleGeometry viewport = new();
    private bool usePreviewHeight = true;

    public DetailsBannerHost() => Clip = viewport;

    internal bool UsePreviewHeight
    {
        get => usePreviewHeight;
        set
        {
            if (usePreviewHeight == value) return;
            usePreviewHeight = value;
            InvalidateMeasure();
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (Children.Count != 2) return default;
        var frame = Children[0];
        var preview = Children[1];
        var constraint = new Size(availableSize.Width, double.PositiveInfinity);
        frame.Measure(constraint);
        preview.Measure(constraint);
        return new Size(Math.Max(frame.DesiredSize.Width, preview.DesiredSize.Width),
            UsePreviewHeight ? preview.DesiredSize.Height : frame.DesiredSize.Height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var bounds = new Rect(0, 0, finalSize.Width, finalSize.Height);
        viewport.Rect = bounds;
        if (Children.Count == 2)
        {
            var frameHeight = Children[0].DesiredSize.Height;
            Children[0].Arrange(new Rect(0, 0, finalSize.Width, frameHeight));
            // Both layers meet the same moving clip. A rounded preview that ended
            // at the clip would expose two bright wedges of artwork underneath.
            Children[1].Arrange(new Rect(0, 0, finalSize.Width, Math.Max(finalSize.Height, frameHeight)));
        }
        return finalSize;
    }
}

// Prepare artwork, glass and readable foreground together. Fade the XAML
// cover over the finished scene, including scenes backed by a swap chain.
internal sealed class DetailsBannerPresentation
{
    private readonly DetailsBannerHost host;
    private readonly Grid frame;
    private readonly ArtworkStage stage;
    private readonly FrameworkElement preview;
    private readonly FrameworkElement previewContent;
    private const double RevealMilliseconds = 190;
    private long revealStarted;
    private double startHeight;
    private bool pending, rendering, shown;
    internal bool Pending => pending;
    internal bool Queued => rendering && !IsRevealing;
    internal bool IsRevealing => revealStarted != 0;
    internal int RevealCount { get; private set; }

    internal DetailsBannerPresentation(DetailsBannerHost host, Grid frame, ArtworkStage stage,
        FrameworkElement preview, FrameworkElement previewContent)
    {
        this.host = host;
        this.frame = frame;
        this.stage = stage;
        this.preview = preview;
        this.previewContent = previewContent;
        frame.Loaded += (_, _) => TryReveal();
        frame.Unloaded += (_, _) => { if (!frame.IsLoaded) Reset(); };
        frame.SizeChanged += (_, _) => { if (IsRevealing) CompleteReveal(); };
        frame.LayoutUpdated += (_, _) => TryReveal();
        stage.PresentationChanged += TryReveal;
    }

    internal void SetPending(bool value)
    {
        pending = value;
        if (value) Reset();
        else TryReveal();
    }

    internal void Reset()
    {
        StopTransition();
        shown = false;
        frame.Opacity = 0;
        frame.IsHitTestVisible = false;
        host.UsePreviewHeight = true;
        preview.Opacity = 1;
        previewContent.Opacity = 1;
        preview.Visibility = Visibility.Visible;
    }

    internal void ApplyMotionPolicy()
    {
        if (IsRevealing && !InteractionMotion.Enabled) CompleteReveal();
    }

    private bool Ready => !pending && frame.IsLoaded && (stage.CoverKey is null || stage.HasCurrentPresentation);

    private void TryReveal()
    {
        if (shown || rendering || !Ready) return;
        CompositionTarget.Rendering += Render;
        rendering = true;
    }

    private void Render(object? sender, object args)
    {
        if (IsRevealing)
        {
            var progress = Stopwatch.GetElapsedTime(revealStarted).TotalMilliseconds / RevealMilliseconds;
            if (progress >= 1)
            {
                CompleteReveal();
                return;
            }
            var expansion = progress * progress * (3 - 2 * progress);
            host.Height = startHeight + (frame.DesiredSize.Height - startHeight) * expansion;
            preview.Opacity = 1 - progress * progress;
            // Let the temporary title leave before the one in the artwork becomes readable.
            previewContent.Opacity = 1 - Math.Clamp((progress - .15) / .3, 0, 1);
            return;
        }

        // A new image or layout may have invalidated readiness since scheduling.
        if (!Ready)
        {
            StopTransition();
            return;
        }
        shown = true;
        frame.Opacity = 1;
        if (!InteractionMotion.Enabled || host.ActualHeight <= 0)
        {
            CompleteReveal();
            return;
        }

        // A slow first draw must not consume time from the reveal.
        startHeight = host.ActualHeight;
        revealStarted = Stopwatch.GetTimestamp();
        RevealCount++;
    }

    private void CompleteReveal()
    {
        preview.Visibility = Visibility.Collapsed;
        host.UsePreviewHeight = false;
        frame.IsHitTestVisible = true;
        StopTransition();
    }

    private void StopTransition()
    {
        if (rendering)
        {
            CompositionTarget.Rendering -= Render;
            rendering = false;
        }
        if (!IsRevealing) return;
        revealStarted = 0;
        host.ClearValue(FrameworkElement.HeightProperty);
    }
}
