using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using TheOrder.Presentation;

namespace TheOrder.WinUI;

public sealed partial class MainWindow
{
    private readonly StartupReadiness startup = new();
    private readonly TaskCompletionSource<bool> startupFinished = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Action? initializePages;
    private Task backgroundInitialization = Task.CompletedTask;
    private bool startupReady => startup.CanReveal;
    internal bool StartupHasFailed { get; private set; }
    private void UpdateStartupTheme() => StartupLogo.Source = new BitmapImage(new Uri(Root.ActualTheme == ElementTheme.Dark
        ? "ms-appx:///Assets/Brand.Dark.png" : "ms-appx:///Assets/Brand.Light.png"));
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
