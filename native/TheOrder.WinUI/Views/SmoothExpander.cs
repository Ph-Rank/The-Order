using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace TheOrder.WinUI.Views;

public sealed class SmoothExpander : Expander
{
    internal bool ContentShown => GetTemplateChild("DisclosureBody") is FrameworkElement { Visibility: Visibility.Visible };
    public SmoothExpander() => DefaultStyleKey = typeof(Expander);
}

// The following sections and the scroll range follow the visible body height on
// each frame. The content keeps its full layout inside the moving clip.
public sealed class DisclosureBody : Panel
{
    private const double DurationMilliseconds = 200;
    public static readonly DependencyProperty IsExpandedProperty = DependencyProperty.Register(
        nameof(IsExpanded), typeof(bool), typeof(DisclosureBody),
        new PropertyMetadata(false, (owner, _) => ((DisclosureBody)owner).ChangeState()));

    private readonly RectangleGeometry viewport = new();
    private double fraction;
    private double from;
    private long started;
    private bool rendering;

    public bool IsExpanded
    {
        get => (bool)GetValue(IsExpandedProperty);
        set => SetValue(IsExpandedProperty, value);
    }

    public DisclosureBody()
    {
        Clip = viewport;
        Complete();
        Loaded += (_, _) => Complete();
        Unloaded += (_, _) => Complete();
    }

    private void ChangeState()
    {
        var target = IsExpanded ? 1d : 0d;
        if (!IsLoaded || !InteractionMotion.Enabled || fraction == target)
        {
            Complete();
            return;
        }

        Visibility = Visibility.Visible;
        IsHitTestVisible = IsExpanded;
        from = fraction;
        started = 0;
        if (rendering) return;
        CompositionTarget.Rendering += Render;
        rendering = true;
    }

    private void Render(object? sender, object args)
    {
        if (started == 0) started = Stopwatch.GetTimestamp();
        var progress = Stopwatch.GetElapsedTime(started).TotalMilliseconds / DurationMilliseconds;
        if (progress >= 1 || !InteractionMotion.Enabled)
        {
            Complete();
            return;
        }

        var eased = progress * progress * (3 - 2 * progress);
        fraction = from + ((IsExpanded ? 1d : 0d) - from) * eased;
        InvalidateMeasure();
    }

    private void Complete()
    {
        if (rendering)
        {
            CompositionTarget.Rendering -= Render;
            rendering = false;
        }
        fraction = IsExpanded ? 1 : 0;
        IsHitTestVisible = IsExpanded;
        Visibility = IsExpanded ? Visibility.Visible : Visibility.Collapsed;
        InvalidateMeasure();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (Children.Count == 0) return default;
        var body = Children[0];
        body.Measure(new Size(availableSize.Width, double.PositiveInfinity));
        return new Size(body.DesiredSize.Width, body.DesiredSize.Height * fraction);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        viewport.Rect = new Rect(0, 0, finalSize.Width, finalSize.Height);
        if (Children.Count > 0)
            Children[0].Arrange(new Rect(0, 0, finalSize.Width, Children[0].DesiredSize.Height));
        return finalSize;
    }
}
