using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using Windows.UI.ViewManagement;

namespace TheOrder.WinUI.Views;

internal static class InteractionMotion
{
    private static readonly UISettings settings = new();
    internal static bool Allowed { get; set; } = true;
    internal static bool Enabled { get { try { return Allowed && settings.AnimationsEnabled; } catch { return false; } } }

    internal static void Reveal(FrameworkElement element)
    {
        var visual = ElementCompositionPreview.GetElementVisual(element);
        visual.StopAnimation("Opacity"); visual.Opacity = 1;
        if (!Enabled || !element.IsLoaded) return;
        using var animation = visual.Compositor.CreateScalarKeyFrameAnimation();
        animation.InsertKeyFrame(0, .94f); animation.InsertKeyFrame(1, 1);
        animation.Duration = TimeSpan.FromMilliseconds(140);
        visual.StartAnimation("Opacity", animation);
    }
    internal static void WatchVisibility(FrameworkElement element)
        => element.RegisterPropertyChangedCallback(UIElement.VisibilityProperty, (_, _) => {
            if (element.Visibility == Visibility.Visible) Reveal(element);
        });
}
