using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.Windows.AppLifecycle;
using TheOrder.Platform.Windows;
using TheOrder.Application;
using TheOrder.Infrastructure.Legacy;

namespace TheOrder.WinUI;

internal static class Program
{
    internal static readonly System.Diagnostics.Stopwatch StartupClock = System.Diagnostics.Stopwatch.StartNew();
    internal static double ProfileOpenMilliseconds;
    private static AppInstance? primary;
    private static AppInstance? current;
    public static int Activations;
    public static string ActivationArguments = "";
    internal static RecentGameActivation? JumpActivation;
    internal static NativeProfileStore? Profile;
    [STAThread]
    public static int Main(string[] args)
    {
        // A bounded auxiliary mode must never register an app instance, open a
        // user Profile, initialize WinUI or redirect a normal activation.
        if (WindowsAudioGuard.IsInvocation(args)) return WindowsAudioGuard.Run(args);
        if (WindowsIsolatedMigrationSource.IsInvocation(args)) return WindowsIsolatedMigrationSource.Run(args,
            new TheOrder.Infrastructure.Migration.ManagerImportAdapter(new TheOrder.Infrastructure.Recognition.TitleIndex(
                () => File.OpenRead(Path.Combine(AppContext.BaseDirectory, "data", "title-index.json.gz")))));
        _ = StartupClock.Elapsed; // Force the marker before profile I/O even on beforefieldinit runtimes.
        if (!OperatingSystem.IsWindows() || !WindowsCompatibility.IsSupported(Environment.OSVersion.Version))
        {
            MessageBox(IntPtr.Zero, "The Order 需要 Windows 10 1809（系统版本 17763）或更高版本的 64 位 Windows。尚未打开书架资料。", "The Order", 0x10);
            return 1;
        }
        WinRT.ComWrappersSupport.InitializeComWrappers();
        try
        {
            var directory = Environment.GetEnvironmentVariable("THE_ORDER_NATIVE_DATA_DIR");
            // Existing Phase 1–5 raw synthetic fixtures remain an explicit tooling-only entry.
            var fixture = Environment.GetEnvironmentVariable("THE_ORDER_NATIVE_FIXTURE_PROFILE") == "1";
#if NATIVE_PRODUCTION_CANDIDATE
            const NativeProfileKind buildKind = NativeProfileKind.Production;
#else
            const NativeProfileKind buildKind = NativeProfileKind.Development;
#endif
            var authorizeProduction = args.Contains("--allow-production-profile", StringComparer.Ordinal);
            if (AppReleaseIdentity.IsBeta && fixture) throw new NativeProfileException("BetaRawFixtureForbidden");
            if (fixture && authorizeProduction) throw new NativeProfileException("ProductionAuthorizationFixtureForbidden");
            var request = fixture ? null : NativeProfilePaths.Select(buildKind, Environment.GetEnvironmentVariable("THE_ORDER_NATIVE_PROFILE_KIND"), directory, authorizeProduction);
            JumpActivation = RecentGameActivation.Parse(args);
            if (JumpActivation is { } jump)
            {
                if (fixture) throw new NativeProfileException("JumpActivationFixtureForbidden");
                request = NativeProfilePaths.Select(buildKind, jump.Kind, jump.Root, jump.Kind == nameof(NativeProfileKind.Production))
                    with { ExpectedProfileId = jump.ProfileId, ExpectedStoreId = jump.StoreId };
                var root = NativeProfilePaths.Validate(request);
                if (!File.Exists(Path.Combine(root, NativeProfileStore.ManifestName))) throw new NativeProfileException("JumpProfileMissing");
            }
            if (request is not null && JumpActivation is null)
                request = NativeChannelProfiles.Resolve(request, AppContext.BaseDirectory, () =>
                {
                    var result = MessageBox(IntPtr.Zero, "发现旧版 Beta 的独立书架。是否继续使用它？\n\n是：保留旧 Beta 书架、个人记录和账号。\n否：使用原正式版书架（没有则创建空书架）。\n取消：退出，不打开或合并资料。\n\n选定后，正式版与 Beta 切换都会沿用这份书架。", "选择继续使用的书架", 0x123);
                    return result == 6 ? true : result == 7 ? false : null;
                });
            var key = request is null ? NativeInstanceIdentity.ForProfile(directory) : NativeInstanceIdentity.ForNativeProfile(request);
            current = AppInstance.GetCurrent();
            current.Activated += Activated; // Subscribe before registration exposes us to a simultaneous secondary.
            var activation = current.GetActivatedEventArgs();
            primary = AppInstance.FindOrRegisterForKey(key);
            if (!primary.IsCurrent)
            {
                AllowSetForegroundWindow(primary.ProcessId);
                using var done = new ManualResetEvent(false);
                var redirect = Task.Run(async () => { try { await primary.RedirectActivationToAsync(activation); } finally { try { done.Set(); } catch (ObjectDisposedException) { } } });
                // Pump COM on the STA; a failed/timed-out redirect never creates a second writer.
                var wait = CoWaitForMultipleObjects(0, 10000, 1, [done.SafeWaitHandle.DangerousGetHandle()], out _);
                if (wait != 0) return 2;
                try { redirect.GetAwaiter().GetResult(); return 0; }
                catch { return 3; } // Failed forwarding still exits without creating an application/writer.
            }
            if (request is not null)
            {
                Profile = NativeProfileStore.Open(request, new NativeLibraryCodec());
                NativeChannelProfiles.Remember(Profile, AppContext.BaseDirectory);
            }
            if (Profile is not null) WindowsRecentGames.SetAppId(Profile);
            ProfileOpenMilliseconds = StartupClock.Elapsed.TotalMilliseconds;
            Microsoft.UI.Xaml.Application.Start(p =>
            {
                SynchronizationContext.SetSynchronizationContext(new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
                _ = new App();
            });
            return 0;
        }
        catch (NativeProfileException e) when (e.Code == "ChannelProfileChoiceCanceled") { return 0; }
        catch (Exception e) { MessageBox(IntPtr.Zero, UiText.Get("The Order 无法安全启动：") + e.GetType().Name + "。请核对启动入口和资料目录，退出仍在运行的旧版本后重试。没有打开第二个书架。", "The Order", 0x10); return 1; }
        finally { Profile?.Dispose(); if (current is not null) current.Activated -= Activated; if (primary?.IsCurrent == true) primary.UnregisterKey(); }
    }
    private static void Activated(object? sender, AppActivationArguments args)
    {
        // Only exact profile-bound recent-game IDs can enter the ordinary launch workflow.
        var text = (args.Data as Windows.ApplicationModel.Activation.ILaunchActivatedEventArgs)?.Arguments ?? "";
        ActivationArguments = text.Length <= 32768 ? text : "";
        Interlocked.Increment(ref Activations); App.Instance?.RequestActivation();
        var tokens = WindowsRecentGames.Split(ActivationArguments);
        if (tokens.Length > 0 && tokens[0].EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) tokens = tokens[1..];
        if (RecentGameActivation.Parse(tokens) is { } jump) App.Instance?.RequestGameActivation(jump);
    }
    [DllImport("ole32.dll")] private static extern int CoWaitForMultipleObjects(uint flags, uint timeout, uint count, IntPtr[] handles, out uint index);
    [DllImport("user32.dll")] private static extern bool AllowSetForegroundWindow(uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int MessageBox(IntPtr hwnd, string text, string caption, uint type);
}
