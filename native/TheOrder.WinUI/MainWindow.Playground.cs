using Microsoft.UI.Xaml;
using System.Collections.Immutable;
using System.Net;
using TheOrder.Application;
using TheOrder.Application.Playground;
using TheOrder.Domain;
using TheOrder.Infrastructure.PlaygroundProviders;
using TheOrder.Platform.Windows;
using TheOrder.Presentation;
using TheOrder.WinUI.Views;

namespace TheOrder.WinUI;

public sealed partial class MainWindow
{
    private static bool CleanPlaygroundPreview => Program.Profile?.Manifest.ProfileKind == NativeProfileKind.Test
        && Environment.GetEnvironmentVariable("THE_ORDER_NATIVE_PLAYGROUND_CLEAN") == "1";
    private NativeConfiguration? playgroundConfiguration;
    private OfflineLibraryClient? playgroundLibrary;
    private WindowsPlaygroundNavigation? playgroundNavigationPreferences;
    private PlaygroundService? playground;
    private PlaygroundView? playgroundView;
    private WindowsPlaygroundAttachments? playgroundAttachments;
    private HttpClient? playgroundHttp;
    private bool playgroundNavigationVisible = true;
    private Task playgroundOpening = Task.CompletedTask;
    private void RefreshPlaygroundNotices()
    {
        NoticeStack.Visibility = ViewModel.Page == WorkspacePage.Playground && !NoticeStack.Children.OfType<Microsoft.UI.Xaml.Controls.InfoBar>().Any(bar => bar.IsOpen)
            ? Visibility.Collapsed : Visibility.Visible;
    }
    private void RefreshPlaygroundPolicy()
    {
        if (materialPolicy is not null) PlaygroundAppearance.SetHighContrast(materialPolicy.HighContrast);
        playgroundView?.FollowWindowTheme(Root.ActualTheme);
        // The framed workbench owns its inner canvas. Its outside margin shares
        // the original shell backdrop with the navigation rail in every theme.
    }

    private void ConfigurePlayground(NativeConfiguration configuration, OfflineLibraryClient library)
    {
        // No AI model discovery, chat-store loading, credential reads or requests at startup.
        if (configuration.Profile is not NativeProfileStore)
        { PlaygroundNavigation.Visibility = Visibility.Collapsed; return; }
        playgroundConfiguration = configuration; playgroundLibrary = library;
        playgroundNavigationPreferences = new(configuration);
        Root.ActualThemeChanged += (_, _) => RefreshPlaygroundPolicy();
        foreach (var bar in NoticeStack.Children.OfType<Microsoft.UI.Xaml.Controls.InfoBar>())
            bar.RegisterPropertyChangedCallback(Microsoft.UI.Xaml.Controls.InfoBar.IsOpenProperty, (_, _) => RefreshPlaygroundNotices());
    }
    private async Task LoadPlaygroundNavigationAsync()
    {
        if (playgroundNavigationPreferences is null) return;
        try { playgroundNavigationVisible = await playgroundNavigationPreferences.ReadVisibleAsync(); ApplyPlaygroundNavigation(); }
        catch { ApplyPlaygroundNavigation("对话入口设置未能读取，原文件已保留；其他功能可继续使用。"); }
    }
    private void ApplyPlaygroundNavigation(string? notice = null)
    {
        PlaygroundNavigation.Visibility = playgroundNavigationVisible ? Visibility.Visible : Visibility.Collapsed;
        settingsPage?.ShowPlaygroundVisibility(playgroundNavigationVisible, notice);
    }
    private async Task ChangePlaygroundVisibilityAsync(bool visible)
    {
        try
        {
            if (playgroundNavigationPreferences is null) return;
            await playgroundNavigationPreferences.SetVisibleAsync(visible);
            playgroundNavigationVisible = visible;
            if (!visible && ViewModel.Page == WorkspacePage.Playground) await Navigate(WorkspacePage.Library);
            ApplyPlaygroundNavigation();
        }
        catch { ApplyPlaygroundNavigation("入口设置未能保存，原设置已保留。请稍后重试。"); }
    }
    private async void OnHidePlayground(object sender, RoutedEventArgs e) => await ChangePlaygroundVisibilityAsync(false);
    private PlaygroundView EnsurePlaygroundView()
    {
        if (playgroundView is not null) { playgroundView.FollowWindowTheme(Root.ActualTheme); return playgroundView; }
        var configuration = playgroundConfiguration ?? throw new InvalidOperationException("Playground 环境尚未就绪。");
        var library = playgroundLibrary ?? throw new InvalidOperationException("书架尚未就绪。");
        playgroundHttp = new(new SocketsHttpHandler
        {
            AllowAutoRedirect = false, UseCookies = false,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5), ConnectTimeout = TimeSpan.FromSeconds(10), MaxConnectionsPerServer = 3
        }) { Timeout = Timeout.InfiniteTimeSpan };
        var credentials = new WindowsPlaygroundCredentials(configuration);
        var registry = new PlaygroundProviderRegistry(playgroundHttp, credentials);
        var store = new WindowsPlaygroundStore(configuration);
        playground = CreatePlaygroundFixture(configuration, library) ?? new PlaygroundService(store, credentials,
            registry.Providers, () => library.CurrentSnapshot, ReadPlaygroundSession);
        playgroundView = new(playground)
        {
            BrowserDataDirectory = Path.Combine(configuration.Profile!.Root, "playground", "browser"),
            PickAttachments = PickPlaygroundAttachmentsAsync,
            OpenSource = OpenPlaygroundSourceAsync,
            RestorePreviousState = () => store.RestoreLastGoodAsync(default),
            ProgressWorks = () => playground.Snapshot.Settings.Permissions.Library
                ? (library.CurrentSnapshot?.Entries ?? []).Select(e => (PlaygroundService.WorkKeyFor(e), e.Name)).ToArray()
                : []
        };
        playgroundOpening = OpenPlaygroundAsync();
        return playgroundView;
    }
    private async Task<ImmutableArray<PlaygroundAttachment>> PickPlaygroundAttachmentsAsync(CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        // Only an explicit native picker selection grants this read. No directory
        // scan, background import or provider file-upload API is involved.
        string? path = await PickFile(".png", ".jpg", ".jpeg", ".webp", ".txt", ".md", ".rtf", ".doc", ".docx", ".xls", ".xlsx");
        cancellation.ThrowIfCancellationRequested();
        if (path is null) return [];
        playgroundAttachments ??= new(new WindowsFilesystemIdentity(), new TheOrder.Infrastructure.LocalDocumentParser());
        return await playgroundAttachments.ReadAsync([path], cancellation);
    }
    private async Task OpenPlaygroundAsync()
    {
        if (playgroundView is null || playground is null) return;
        await InitializePlaygroundFixtureAsync(playground);
        await playgroundView.InitializeAsync();
        if (Program.Profile?.Manifest.ProfileKind == NativeProfileKind.Test && fixtureDirectory is not null)
            await File.WriteAllTextAsync(Path.Combine(fixtureDirectory, "playground-ready.json"),
                System.Text.Json.JsonSerializer.Serialize(new { pid = Environment.ProcessId, ready = true, at = DateTimeOffset.UtcNow }));
    }
    private PlaygroundActiveSession? ReadPlaygroundSession()
    {
        var value = Sessions.Session;
        return value?.Session?.Native is { IsTerminal: false } native
            ? new(native.GameId, value.Timing.ToString(), value.Durable100ns / NativeSession.TicksPerSecond, DateTimeOffset.UtcNow) : null;
    }
    private async Task OpenPlaygroundSourceAsync(PlaygroundSource source)
    {
        if (source.Kind == PlaygroundSourceKind.LocalRecord)
        {
            var entry = playgroundLibrary?.CurrentSnapshot?.Entries.FirstOrDefault(e => e.Id == source.TargetId);
            if (entry is null || PlaygroundService.WorkKeyFor(entry) != source.WorkKey)
            { ViewModel.ReportUnexpected(); return; }
            await Navigate(WorkspacePage.Details, entry.Id, WorkspacePage.Playground);
        }
        else if (source.Kind == PlaygroundSourceKind.Web && PlaygroundEndpointPolicy.SafePublicUrl(source.Url))
            await Windows.System.Launcher.LaunchUriAsync(new Uri(source.Url!));
    }
    private async Task StopPlaygroundAsync()
    {
        playground?.Stop();
        try { await playgroundOpening; } catch { /* The page displays the initialization failure. */ }
        playgroundView?.Dispose();
        if (playground is not null) await playground.DisposeAsync();
        playgroundHttp?.Dispose();
    }
}
