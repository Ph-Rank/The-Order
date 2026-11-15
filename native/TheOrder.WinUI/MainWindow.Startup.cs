using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using TheOrder.Presentation;
using TheOrder.WinUI.Views;
using Windows.UI.ViewManagement;

namespace TheOrder.WinUI;

public sealed partial class MainWindow
{
    private readonly StartupReadiness startup = new();
    private readonly TaskCompletionSource<bool> startupFinished = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Action? initializePages;
    private Task backgroundInitialization = Task.CompletedTask;
    private const int StartupLightSteps = 8;
    private const int StartupLightStepMilliseconds = 180;
    private Storyboard? startupLogoLighting, startupHomeFade;
    private Image[]? startupLogoLightLayers;
    private bool startupLogoLightPlayed;
    private bool startupReady => startup.CanReveal;
    internal bool StartupHasFailed { get; private set; }

    private void UpdateStartupTheme()
    {
        if (StartupSurface.Visibility != Visibility.Visible) return;

        var dark = Root.ActualTheme == ElementTheme.Dark;
        var highContrast = materialPolicy?.HighContrast ?? ReadStartupHighContrast();
        if (highContrast && OpaqueCanvas.Background is SolidColorBrush background)
        {
            var color = background.Color;
            dark = 299 * color.R + 587 * color.G + 114 * color.B < 128_000;
        }

        var asset = $"{(highContrast ? "Brand" : "StartupLogo")}.{(dark ? "Dark" : "Light")}.png";
        if ((StartupLogo.Source as BitmapImage)?.UriSource?.Segments.LastOrDefault() != asset)
        {
            StopStartupLogoLighting();
            StartupLogo.Source = StartupBitmap(asset);
        }
    }

    private static bool ReadStartupHighContrast()
    {
        try { return new AccessibilitySettings().HighContrast; }
        catch (Exception error) { App.TraceVisualFailure(error); return false; }
    }

    private static BitmapImage StartupBitmap(string name) => new(new Uri("ms-appx:///Assets/" + name));

    private void OnStartupLogoOpened(object sender, RoutedEventArgs e) => RefreshStartupLogoLighting();

    private void RefreshStartupLogoLighting()
    {
        if (materialPolicy is null || StartupSurface.Visibility != Visibility.Visible) return;
        if (!StartupRing.IsActive || !visualWindowVisible || !InteractionMotion.Enabled || materialPolicy.HighContrast)
        {
            StopStartupLogoLighting();
            return;
        }
        if (startupLogoLighting is not null || StartupLogo.Source is not BitmapImage { UriSource: { } uri } bitmap)
            return;

        try
        {
            var layers = startupLogoLightLayers ?? CreateStartupLightLayers(Path.GetFileNameWithoutExtension(uri.Segments[^1]));
            if (startupLogoLighting is not null || bitmap.PixelWidth <= 0 ||
                layers.Any(layer => layer.Source is not BitmapImage { PixelWidth: > 0 })) return;

            var lighting = new Storyboard { RepeatBehavior = RepeatBehavior.Forever };
            for (var step = 0; step < layers.Length; step++)
                lighting.Children.Add(StartupLightTrack(layers[step], step));
            var ringRotation = new DoubleAnimation
            {
                From = 0,
                To = 360,
                Duration = TimeSpan.FromMilliseconds(StartupLightSteps * StartupLightStepMilliseconds)
            };
            Storyboard.SetTarget(ringRotation, StartupRing);
            Storyboard.SetTargetProperty(ringRotation, "(UIElement.RenderTransform).(RotateTransform.Angle)");
            lighting.Children.Add(ringRotation);
            startupLogoLighting = lighting;
            lighting.Begin();
            startupLogoLightPlayed = true;
        }
        catch (Exception error)
        {
            StopStartupLogoLighting();
            App.TraceVisualFailure(error);
        }
    }

    private Image[] CreateStartupLightLayers(string asset)
    {
        var layers = new Image[StartupLightSteps];
        for (var step = 0; step < layers.Length; step++)
        {
            var layer = new Image { Opacity = 0, IsHitTestVisible = false };
            AutomationProperties.SetAccessibilityView(layer, AccessibilityView.Raw);
            layers[step] = layer;
        }
        startupLogoLightLayers = layers;
        foreach (var layer in layers)
        {
            layer.ImageOpened += OnStartupLogoOpened;
            StartupLogoStage.Children.Add(layer);
        }
        for (var step = 0; step < layers.Length; step++)
            layers[step].Source = StartupBitmap($"{asset}.Orbit{step}.png");
        return layers;
    }

    private static DoubleAnimationUsingKeyFrames StartupLightTrack(Image layer, int litStep)
    {
        var animation = new DoubleAnimationUsingKeyFrames();
        Storyboard.SetTarget(animation, layer);
        Storyboard.SetTargetProperty(animation, nameof(UIElement.Opacity));
        // Blend adjacent lighting positions over the fixed logo. The last frame repeats the first.
        for (var step = 0; step <= StartupLightSteps; step++)
        {
            animation.KeyFrames.Add(new LinearDoubleKeyFrame
            {
                KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(step * StartupLightStepMilliseconds)),
                Value = step % StartupLightSteps == litStep ? 1 : 0
            });
        }
        return animation;
    }

    private void StopStartupLogoLighting()
    {
        try { startupLogoLighting?.Stop(); }
        catch (Exception error) { App.TraceVisualFailure(error); }
        startupLogoLighting = null;
        if (startupLogoLightLayers is not { } layers) return;
        startupLogoLightLayers = null;
        foreach (var layer in layers)
        {
            layer.ImageOpened -= OnStartupLogoOpened;
            layer.Source = null;
            StartupLogoStage.Children.Remove(layer);
        }
    }

    private bool CanAnimateStartupHome =>
        !visualStopped && visualWindowVisible && AppWindow.IsVisible &&
        !commandsBlocked && !Sessions.Closing && InteractionMotion.Enabled;

    private void RevealStartupHome()
    {
        Navigation.IsHitTestVisible = true;
        QueueRailIndicator();
        if (CanAnimateStartupHome)
        {
            try
            {
                // Both layers fade over the same window backdrop as the title bar.
                var fade = new Storyboard();
                fade.Children.Add(StartupFadeTrack(StartupSurface, from: 1, to: 0));
                fade.Children.Add(StartupFadeTrack(WorkspaceSurface, from: 0, to: 1));
                fade.Completed += OnStartupHomeFadeCompleted;
                startupHomeFade = fade;
                StartupSurface.IsHitTestVisible = false;
                fade.Begin();
                _ = CaptureStartupHomeFadeAsync();
                return;
            }
            catch (Exception error) { App.TraceVisualFailure(error); }
        }
        HideStartupSurface();
    }

    private static DoubleAnimation StartupFadeTrack(UIElement element, double from, double to)
    {
        var animation = new DoubleAnimation
        {
            From = from,
            To = to,
            Duration = TimeSpan.FromMilliseconds(220),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
        };
        Storyboard.SetTarget(animation, element);
        Storyboard.SetTargetProperty(animation, nameof(UIElement.Opacity));
        return animation;
    }

    private void OnStartupHomeFadeCompleted(object? sender, object args) => HideStartupSurface();

    private void HideStartupSurface()
    {
        StartupSurface.Visibility = Visibility.Collapsed;
        WorkspaceSurface.Opacity = 1;
        StopStartupHomeFade();
        StartupRing.IsActive = false;
        StopStartupLogoLighting();
    }

    private void StopStartupHomeFade()
    {
        if (startupHomeFade is not { } fade) return;
        startupHomeFade = null;
        fade.Completed -= OnStartupHomeFadeCompleted;
        try { fade.Stop(); }
        catch (Exception error) { App.TraceVisualFailure(error); }
    }

    private Task NextStartupFrameAsync()
    {
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler<object>? rendering = null;
        rendering = (_, _) =>
        {
            CompositionTarget.Rendering -= rendering;
            if (!DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () => ready.TrySetResult())) ready.TrySetResult();
        };
        CompositionTarget.Rendering += rendering;
        // Hidden-window shutdown still drains initialization; it cannot depend on a frame
        // that Windows may stop presenting. Completing the wait does not reveal a failed page.
        return CompleteFrameAsync();
        async Task CompleteFrameAsync()
        {
            try { await ready.Task.WaitAsync(TimeSpan.FromSeconds(2)); }
            catch (TimeoutException) { CompositionTarget.Rendering -= rendering; }
        }
    }
    private async Task InitializeBackgroundAsync()
    {
        await Task.Yield();
        await backups.InitializeAsync();
        await InitializeP1Async();
        if (!commandsBlocked && !Sessions.Closing)
        {
            backupTimer.Start();
            await backups.AutomaticAsync();
            if (ViewModel.CanEdit && (online.CanAccessCatalog || !recognition.UseOnline)) _ = ResumeRecognitionAsync(retryPending: false);
            if (recognition.UseOnline)
                presentations.Queue(Sessions.Library?.Entries.Where(e => e.Metadata is not null).Select(e => e.Metadata!.Id) ?? []);
        }
    }
    private async void OnStartupExit(object sender, RoutedEventArgs e)
    { if (App.Instance is { } app) await app.RequestExitAsync(); }
}
