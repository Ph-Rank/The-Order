using TheOrder.Application;
using Microsoft.UI.Xaml;
using Microsoft.UI.Windowing;
using TheOrder.WinUI.Startup;
using TheOrder.Platform.Windows;
using TheOrder.Presentation;

namespace TheOrder.WinUI;

public partial class App : Microsoft.UI.Xaml.Application
{
    internal static App? Instance { get; private set; }
    private NativeHost? host;
    private WindowsTrayLifecycle? tray;
    private WindowsBrandIcons? brandIcons;
    private IntPtr brandWindow;
    private readonly WindowLifetime windowLifetime = new();
    private bool exiting, allowClose;
    public bool TrayAvailable => tray?.Available == true;
    public bool WindowHidden => windowLifetime.Hidden;
    public App()
    {
        Instance = this;
        if (Environment.GetEnvironmentVariable("THE_ORDER_NATIVE_VISUAL_TEST") == "1" && Program.Profile?.Manifest.ProfileKind == TheOrder.Application.NativeProfileKind.Test)
            UnhandledException += (_, error) => TraceVisualFailure(error.Exception);
        InitializeComponent();
    }
    internal static void TraceVisualFailure(Exception exception)
    {
        if (Environment.GetEnvironmentVariable("THE_ORDER_NATIVE_VISUAL_TEST") != "1" || Program.Profile?.Manifest.ProfileKind != TheOrder.Application.NativeProfileKind.Test) return;
        try { if (Program.Profile.Configuration(false).DataDirectory is { } directory) File.WriteAllText(Path.Combine(directory, "visual-fatal.txt"), exception.ToString()); } catch { }
    }
    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        var configuration = Program.Profile?.Configuration(false) ?? new TheOrder.Application.NativeConfiguration(null);
        var languageStore = new LanguagePreferencesStore(configuration);
#if NATIVE_MULTILINGUAL_ENABLED
        const bool languageSelection = true;
#else
        const bool languageSelection = false;
#endif
        var languages = new LanguageSettings(languageStore, WindowsUiLanguage.Current(), languageSelection);
        await languages.InitializeAsync(); // Before constructing any XAML or view-model labels.
        var contentStore = new ContentLanguagePreferencesStore(configuration);
        var contentLanguages = new ContentLanguageSettings(contentStore);
        await contentLanguages.InitializeAsync();
        host = Composition.Create(languages, languageStore, contentLanguages, contentStore);
        host.LogStarted();
        UnhandledException += (_, error) => host?.LogFatal(error.Exception);
        host.Window.AppWindow.Closing += (sender, close) =>
        {
            if (allowClose) return;
            close.Cancel = true;
            if (exiting) return;
            windowLifetime.TrayAvailable = TrayAvailable;
            if (windowLifetime.CloseToTray()) { host.Window.SetVisualWindowVisible(false); host.Window.AppWindow.Hide(); _ = CheckpointHiddenAsync(); }
            // No tray means no way back to a hidden window. × then takes the same orderly
            // exit as the tray menu (drafts, Session finalization, cleanup) and can be cancelled.
            else { host.Window.ShowLifecycleError(windowLifetime.Error!); _ = RequestExitAsync(); }
        };
        host.Window.Closed += (_, _) => { tray?.Dispose(); tray = null; WindowsBrandIcons.Detach(brandWindow); brandIcons?.Dispose(); brandIcons = null; brandWindow = IntPtr.Zero; host = null; Instance = null; };
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(host.Window);
        brandWindow = hwnd;
        try
        {
            var failure = Environment.GetEnvironmentVariable("THE_ORDER_NATIVE_DIAGNOSTICS") == "1" && Environment.GetEnvironmentVariable("THE_ORDER_NATIVE_TRAY_FAILURE") == "1";
            tray = new(hwnd, TrayAction, QuerySystemEnd, EndSystemSession, failure);
            tray.Unavailable += TrayUnavailable;
            tray.SystemSettingsChanged += host.Window.RefreshSystemMaterialPolicy;
            tray.SystemSettingsChanged += RefreshBrandIcons;
            if (!tray.Available) TrayUnavailable();
        }
        catch (Exception) { TrayUnavailable(); }
        host.Window.Activate();
        _ = InitializeBrandIconsAsync();
        if (Program.Activations > 0) RequestActivation();
        if (Program.JumpActivation is { } jump) RequestGameActivation(jump);
    }
    private async Task InitializeBrandIconsAsync()
    {
        try {
            var icons = await WindowsBrandIcons.LoadAsync(Path.Combine(AppContext.BaseDirectory, "Assets", "Brand.png"));
            if (host is null || allowClose || brandWindow == IntPtr.Zero) { icons.Dispose(); return; }
            brandIcons = icons; RefreshBrandIcons();
        } catch (Exception error) { TraceVisualFailure(error); host?.Window.ShowLifecycleError(UiText.Get("应用图标未能加载，已保留系统图标。")); }
    }
    internal void RefreshBrandIcons()
    {
        var owner = host;
        if (owner is null) return;
        owner.Window.DispatcherQueue.TryEnqueue(() => {
            if (host != owner || brandWindow == IntPtr.Zero || brandIcons is null) return;
            bool light = WindowsBrandIcons.SystemLight(); brandIcons.Apply(brandWindow, light); tray?.SetIcon(brandIcons.Small(light));
            if (Program.Profile?.Manifest.ProfileKind == NativeProfileKind.Production)
                WindowsShortcutTheme.Synchronize(AppContext.BaseDirectory, light);
        });
    }
    private async Task CheckpointHiddenAsync()
    { try { if (host is { } owner) await owner.Sessions.CheckpointAsync(); } catch { RequestActivation(); host?.Window.ShowLifecycleError(UiText.Get("游玩记录暂未保存，请检查后重试。")); } }
    private void TrayUnavailable() { RequestActivation(); host?.Window.ShowLifecycleError(UiText.Get("托盘不可用：关闭窗口会按正常流程退出 The Order，先处理草稿并保存游玩记录。")); }
    internal void RefreshTray(SessionPresentation value) => tray?.Update(value.Title, value.State, value.CanPause, value.CanResume);
    internal void RequestActivation()
    {
        var window = host?.Window;
        window?.DispatcherQueue.TryEnqueue(() =>
        {
            if (host is null || allowClose) return;
            windowLifetime.Show(); window.AppWindow.Show(); window.SetVisualWindowVisible(true);
            if (window.AppWindow.Presenter is OverlappedPresenter presenter && presenter.State == OverlappedPresenterState.Minimized) presenter.Restore();
            window.Activate();
        });
    }
    internal void RequestGameActivation(RecentGameActivation request)
    {
        RequestActivation(); var window = host?.Window;
        window?.DispatcherQueue.TryEnqueue(async () => { try { await window.ActivateRecentGameAsync(request); } catch (Exception e) { TraceVisualFailure(e); window.ShowLifecycleError("最近游玩入口未完成，请从书架重试。"); } });
    }
    private async void TrayAction(TrayCommand command)
    {
        try
        {
            if (host is not { } owner) return;
            if (command == TrayCommand.Show) RequestActivation();
            else if (command == TrayCommand.Pause) await owner.Sessions.PauseAsync();
            else if (command == TrayCommand.Resume) await owner.Sessions.ResumeAsync();
            else if (command == TrayCommand.Exit) await RequestExitAsync();
        }
        catch (Exception) { RequestActivation(); host?.Window.ShowLifecycleError(UiText.Get("托盘操作未完成，请在窗口中重试。")); }
    }
    internal async Task RequestExitAsync() => await ExitCoreAsync(null);
    internal Task<bool> RequestUpgradeAsync(VerifiedAppUpdate package) => ExitCoreAsync(package);
    private async Task<bool> ExitCoreAsync(VerifiedAppUpdate? update)
    {
        if (exiting || host is not { } owner) return false;
        exiting = true; RequestActivation(); owner.Window.SetCommandsBlocked(true);
        try
        {
            owner.Window.CancelBackgroundRequests();
            return await AppUpdateShutdown.RunAsync(owner.Window.ResolveUnsavedAsync,
                async () => update is null ? null : await owner.Updates.PrepareAsync(update, CancellationToken.None),
                async () =>
                {
                    await owner.Window.DrainPersonalRequestsAsync();
                    // No session or library was opened when page initialization failed.
                    // Such a failure must not trap the user behind the Ready guard.
                    if (owner.Window.StartupHasFailed && !owner.Sessions.Ready && owner.Sessions.Library is null) return true;
                    if (await owner.Sessions.PrepareExitAsync()) return true;
                    owner.Window.ShowLifecycleError(owner.Sessions.Error ?? UiText.Get("退出已取消，请重试。")); return false;
                }, () => owner.DisposeAsync().AsTask(), () =>
                {
                    tray?.Dispose(); tray = null; allowClose = true;
                    owner.Window.DispatcherQueue.TryEnqueue(() => owner.Window.Close());
                });
        }
        catch (Exception error) { owner.LogFatal(error); owner.Window.ShowLifecycleError(error is AppUpdateException ? error.Message : UiText.Get("退出未完成，窗口已保留。请重试。")); return false; }
        finally { if (!allowClose) { exiting = false; owner.Window.SetCommandsBlocked(false); } }
    }
    private bool QuerySystemEnd(bool logoff)
    {
        if (host is not { } owner) return true;
        owner.Window.SetCommandsBlocked(true); owner.Window.CancelBackgroundRequests();
        var work = Task.Run(() => owner.Sessions.PrepareSystemEndAsync(TimeSpan.FromSeconds(4)));
        try { work.Wait(TimeSpan.FromMilliseconds(4200)); } catch { }
        return true; // Bounded best effort, never veto shutdown because storage is unavailable.
    }
    private async void EndSystemSession(bool ending)
    {
        if (host is not { } owner) return;
        if (ending) { tray?.Dispose(); tray = null; return; } // OS can terminate now; only checkpoint completion is bounded above.
        try { await owner.Sessions.CancelSystemEndAsync(); }
        catch { owner.Window.ShowLifecycleError(UiText.Get("系统取消退出后，记录核验未完成，请重试退出。")); }
        owner.Window.SetCommandsBlocked(false);
    }
}
