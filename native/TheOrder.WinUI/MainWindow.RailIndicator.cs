using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;

namespace TheOrder.WinUI;

public sealed partial class MainWindow
{
    private NavigationViewItem? railTarget, drawnRailTarget;
    private bool railUpdateQueued, railTranslationReady;
    private float railPosition = float.NaN;
    private long railAnimationCount;

    private void QueueRailIndicator(NavigationViewItem? target = null)
    {
        railTarget = target ?? railTarget;
        if (railUpdateQueued || railTarget is null) return;
        railUpdateQueued = DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, PositionRailIndicator);
    }

    private void PositionRailIndicator()
    {
        railUpdateQueued = false;
        if (railTarget is not { IsLoaded: true } target || !Navigation.IsHitTestVisible || target.ActualHeight <= 0)
        {
            RailIndicator.Visibility = Visibility.Collapsed;
            return;
        }

        try
        {
            var location = target.TransformToVisual(Navigation).TransformPoint(new(0, 0));
            var y = (float)(location.Y + (target.ActualHeight - RailIndicator.Height) / 2);
            var move = drawnRailTarget is not null && !ReferenceEquals(drawnRailTarget, target);
            if (!move && y == railPosition && RailIndicator.Visibility == Visibility.Visible && Views.InteractionMotion.Enabled) return;
            if (!railTranslationReady)
            {
                ElementCompositionPreview.SetIsTranslationEnabled(RailIndicator, true);
                railTranslationReady = true;
            }
            var visual = ElementCompositionPreview.GetElementVisual(RailIndicator);
            var end = new Vector3(0, y, 0);
            if (move && Views.InteractionMotion.Enabled)
            {
                using var easing = visual.Compositor.CreateCubicBezierEasingFunction(new(.2f, .65f), new(.3f, 1));
                using var animation = visual.Compositor.CreateVector3KeyFrameAnimation();
                animation.InsertExpressionKeyFrame(0, "this.StartingValue");
                animation.InsertKeyFrame(1, end, easing);
                animation.Duration = TimeSpan.FromMilliseconds(210);
                animation.StopBehavior = AnimationStopBehavior.SetToFinalValue;
                visual.StartAnimation("Translation", animation);
                railAnimationCount++;
            }
            else
            {
                visual.StopAnimation("Translation");
                visual.Properties.InsertVector3("Translation", end);
            }
            drawnRailTarget = target;
            railPosition = y;
            RailIndicator.Visibility = Visibility.Visible;
        }
        catch (Exception error)
        {
            App.TraceVisualFailure(error);
            RailIndicator.Visibility = Visibility.Collapsed;
        }
    }
}
