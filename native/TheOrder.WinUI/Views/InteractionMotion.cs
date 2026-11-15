using System.Runtime.CompilerServices;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.UI.ViewManagement;

namespace TheOrder.WinUI.Views;

internal readonly record struct PageFadeEvidence(int Starts, bool Pending, float InitialOpacity, int DurationMilliseconds, float Opacity);

internal static class InteractionMotion
{
    private static readonly UISettings settings = new();
    private static readonly ConditionalWeakTable<FrameworkElement, PageEntrance> entrances = new();
    private static bool allowed = true;
    internal static bool HighContrast { get; private set; }
    internal static event Action? PolicyChanged;

    internal static void ApplyPolicy(bool motionAllowed, bool highContrast)
    {
        if (allowed == motionAllowed && HighContrast == highContrast) return;
        allowed = motionAllowed;
        HighContrast = highContrast;
        PolicyChanged?.Invoke();
    }
    internal static bool Enabled { get { try { return allowed && settings.AnimationsEnabled; } catch { return false; } } }

    internal static void EnterPage(FrameworkElement element, Action? started = null, Action<Exception>? failed = null)
    {
        element.Visibility = Visibility.Visible;
        if (!Enabled) { StopPage(element); return; }
        entrances.GetValue(element, static host => new(host)).Begin(started, failed);
    }

    internal static void StopPage(FrameworkElement element)
    {
        if (entrances.TryGetValue(element, out var entrance)) entrance.Stop();
    }

    internal static PageFadeEvidence? PageEvidence(FrameworkElement element) =>
        entrances.TryGetValue(element, out var entrance) ? entrance.Evidence : null;

    internal static void RevealContent(FrameworkElement element, float startOpacity = .5f)
    {
        if (!Enabled) return;
        if (element.IsLoaded) { FadeIn(element, startOpacity, 180); return; }
        element.Loaded += OnLoaded;
        void OnLoaded(object sender, RoutedEventArgs args)
        {
            element.Loaded -= OnLoaded;
            FadeIn(element, startOpacity, 180);
        }
    }

    internal static void SetVisible(FrameworkElement element, bool visible)
    {
        var next = visible ? Visibility.Visible : Visibility.Collapsed;
        if (element.Visibility == next) return;
        element.Visibility = next;
        if (visible) RevealContent(element, .72f);
        else StopReveal(element);
    }

    internal static void StopReveal(FrameworkElement element)
    {
        try { ElementCompositionPreview.GetElementVisual(element).StopAnimation("Opacity"); }
        catch (Exception error) { App.TraceVisualFailure(error); }
    }

    private static void FadeIn(FrameworkElement element, float startOpacity, int milliseconds)
    {
        if (!element.IsLoaded || !Enabled) return;
        try
        {
            var visual = ElementCompositionPreview.GetElementVisual(element);
            using var easing = visual.Compositor.CreateCubicBezierEasingFunction(new(.18f, .65f), new(.28f, 1));
            using var animation = visual.Compositor.CreateScalarKeyFrameAnimation();
            animation.InsertKeyFrame(0, startOpacity);
            animation.InsertKeyFrame(1, 1, easing);
            animation.Duration = TimeSpan.FromMilliseconds(milliseconds);
            animation.StopBehavior = Microsoft.UI.Composition.AnimationStopBehavior.SetToFinalValue;
            visual.StartAnimation("Opacity", animation);
        }
        catch (Exception error) { App.TraceVisualFailure(error); }
    }

    internal static void SetReposition(FrameworkElement element, bool enabled)
    {
        element.Transitions = enabled && Enabled
            ? new TransitionCollection { new RepositionThemeTransition { IsStaggeringEnabled = false } }
            : null;
    }
    private sealed class PageEntrance(FrameworkElement host)
    {
        private const float InitialOpacity = .22f;
        private const int DurationMilliseconds = 220;
        private Visual? visual;
        private bool pending;
        private int starts;
        private Action? started;
        private Action<Exception>? failed;

        internal PageFadeEvidence Evidence => new(starts, pending, InitialOpacity, DurationMilliseconds, visual?.Opacity ?? 1);

        internal void Begin(Action? onStarted, Action<Exception>? onFailure)
        {
            started = onStarted;
            failed = onFailure;
            try
            {
                // Set the start opacity before the first commit of the destination.
                // Begin the clock after layout so a slow measure cannot consume the fade.
                visual ??= ElementCompositionPreview.GetElementVisual(host);
                visual.StopAnimation("Opacity");
                visual.Opacity = InitialOpacity;
                if (pending) return;
                pending = true;
                CompositionTarget.Rendering += OnRendering;
                host.Unloaded += OnUnloaded;
            }
            catch (Exception error) { Fail(error); }
        }

        private void OnRendering(object? sender, object args)
        {
            Unsubscribe();
            if (!Enabled || !host.IsLoaded || host.Visibility != Visibility.Visible) { Stop(); return; }
            try
            {
                var target = visual!;
                using var easing = target.Compositor.CreateCubicBezierEasingFunction(new(.2f, .55f), new(.3f, 1));
                using var animation = target.Compositor.CreateScalarKeyFrameAnimation();
                animation.InsertKeyFrame(0, InitialOpacity);
                animation.InsertKeyFrame(1, 1, easing);
                animation.Duration = TimeSpan.FromMilliseconds(DurationMilliseconds);
                animation.StopBehavior = AnimationStopBehavior.SetToFinalValue;
                target.Opacity = 1;
                target.StartAnimation("Opacity", animation);
                started?.Invoke();
                starts++;
                started = null;
                failed = null;
            }
            catch (Exception error) { Fail(error); }
        }

        private void OnUnloaded(object sender, RoutedEventArgs args)
        {
            if (!host.IsLoaded) Stop();
        }

        private void Unsubscribe()
        {
            if (!pending) return;
            CompositionTarget.Rendering -= OnRendering;
            host.Unloaded -= OnUnloaded;
            pending = false;
        }

        internal void Stop()
        {
            Unsubscribe();
            started = null;
            failed = null;
            if (visual is not { } target) return;
            visual = null;
            try { target.StopAnimation("Opacity"); target.Opacity = 1; }
            catch (Exception error) { App.TraceVisualFailure(error); }
        }

        private void Fail(Exception error)
        {
            var report = failed;
            Stop();
            if (report is null) App.TraceVisualFailure(error);
            else report(error);
        }
    }
}
