using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Windowing;
using TheOrder.Application;
using TheOrder.Presentation;
using Windows.System;
using Windows.UI;
using Windows.UI.ViewManagement;
using TheOrder.WinUI.Startup;
using TheOrder.WinUI.Materials;

namespace TheOrder.WinUI;

// Window caption, Mica, shell shortcuts and live Windows accent updates.
public sealed partial class MainWindow
{
    // Material choice is available in ordinary settings; Windows policy remains authoritative.
    private Views.MaterialMode materialMode = Views.MaterialMode.Standard;
    private readonly IMaterialPreferencesStore materialPreferences;
    private readonly bool controlledAdvanced;
    private Task materialWrite = Task.CompletedTask;
    private bool materialReadOnly;
    private bool materialInitialized;
    private string? materialPreferenceNotice;
    private MicaBackdrop? mica;
    private ElementTheme? micaTheme;
    private readonly WindowCapabilities windowCapabilities = WindowCapabilities.Detect();
    private readonly WindowBackdropState windowBackdrop = new();
    private bool customTitleBar;
    private string? chromeWarning;
    private UISettings? colorSettings;
    private long accentEvents;
    private string accentSource = "Windows theme resources (startup)";
    private bool diagnosticsToggle;

    private void InitializeWindowChrome()
    {
        // Windows App SDK backports customized title bars to Win10. If the host
        // rejects customization, retain the real system caption and its controls;
        // the XAML title-bar content stays available as an in-window command row.
        if (windowCapabilities.CustomTitleBar)
        {
            try
            {
                ExtendsContentIntoTitleBar = true;
                SetTitleBar(AppTitleBar);
                AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
                customTitleBar = true;
            }
            catch (Exception exception) { chromeWarning = exception.GetType().Name; }
        }
        if (!customTitleBar)
        {
            ExtendsContentIntoTitleBar = false;
            SetTitleBar(null);
        }
    }

    private void InitializeShellInput(bool diagnostics)
    {
        diagnosticsToggle = diagnostics;
        void Accelerator(VirtualKey key, VirtualKeyModifiers modifiers, Action action)
        {
            var accelerator = new KeyboardAccelerator { Key = key, Modifiers = modifiers };
            accelerator.Invoked += (_, args) => { args.Handled = true; if (startupReady && !commandsBlocked) action(); };
            Root.KeyboardAccelerators.Add(accelerator);
        }
        Root.KeyboardAcceleratorPlacementMode = KeyboardAcceleratorPlacementMode.Hidden;
        Accelerator(VirtualKey.F, VirtualKeyModifiers.Control, () => { if (ViewModel.Page == WorkspacePage.Library) libraryPage.OpenSearch(); else if (ViewModel.Page == WorkspacePage.Online) onlinePage.FocusSearch(); });
        Accelerator(VirtualKey.Left, VirtualKeyModifiers.Menu, () => _ = GoBackAsync());
        Accelerator(VirtualKey.GoBack, VirtualKeyModifiers.None, () => _ = GoBackAsync());
        if (diagnostics) Accelerator(VirtualKey.D, VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift,
            () => DiagnosticsPanel.Visibility = DiagnosticsPanel.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible);
        // Mouse back button (XButton1) goes back like Alt+Left.
        Root.PointerPressed += (_, args) => { if (startupReady && !commandsBlocked && args.GetCurrentPoint(Root).Properties.IsXButton1Pressed) { args.Handled = true; _ = GoBackAsync(); } };
        Root.ActualThemeChanged += OnCaptionThemeChanged;
        RefreshCaptionTheme();
        try { colorSettings = new UISettings(); colorSettings.ColorValuesChanged += OnSystemColorsChanged; }
        catch (Exception) { colorSettings = null; accentSource = "UISettings unavailable; startup theme resources"; }
    }

    private void DisposeShellInput()
    {
        Root.ActualThemeChanged -= OnCaptionThemeChanged;
        if (colorSettings is not null) { colorSettings.ColorValuesChanged -= OnSystemColorsChanged; colorSettings = null; }
    }

    private void OnCaptionThemeChanged(FrameworkElement sender, object args)
    {
        RefreshCaptionTheme();
        if (!visualStopped && materialPolicy is { } policy) ApplyBackdrop(policy.EffectsAllowed);
    }
    private void RefreshCaptionTheme()
    {
        if (visualStopped) return;
        // The OS owns caption glyphs. Legacy can retain a light caption after
        // leaving a contrast theme, even while XAML has returned to Dark.
        // Let contrast themes use the system palette, then explicitly restore
        // the live XAML theme instead of a stale OS/RequestedTheme default.
        try
        {
            // Win10's system caption colors are OS-owned; do not assume that
            // AppWindow color customization behaves like Win11. HC is OS-owned too.
            AppWindow.TitleBar.PreferredTheme = !windowCapabilities.CaptionTheme || materialPolicy?.HighContrast == true
                ? TitleBarTheme.Legacy
                : Root.ActualTheme == ElementTheme.Dark ? TitleBarTheme.Dark : TitleBarTheme.Light;
        }
        catch (Exception exception) { chromeWarning ??= exception.GetType().Name; }
        // TitleBar's BitmapIcon visual retained its initial foreground in a real
        // Light-theme check. Explicit black/light pixels avoid that copy boundary.
        var dark = Root.ActualTheme == ElementTheme.Dark;
        try {
            if (materialPolicy?.HighContrast == true && colorSettings is not null) {
                var foreground=colorSettings.GetColorValue(UIColorType.Foreground);
                dark=(foreground.R*299+foreground.G*587+foreground.B*114)>=128000;
            }
        } catch (Exception) { /* Keep the live theme's contrasting silhouette. */ }
        var asset=dark?"Brand.Dark.png":"Brand.Light.png";
        if (AppTitleBar.IconSource is not ImageIconSource brand || brand.ImageSource is not Microsoft.UI.Xaml.Media.Imaging.BitmapImage image
            || !image.UriSource.AbsoluteUri.EndsWith(asset,StringComparison.Ordinal))
            AppTitleBar.IconSource=new ImageIconSource { ImageSource=new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri("ms-appx:///Assets/"+asset)) };
    }

    private bool TitleBarHasExtraContent => AppTitleBar.Content is not null
        || AppTitleBar.LeftHeader is not null || AppTitleBar.RightHeader is not null
        || !string.IsNullOrEmpty(AppTitleBar.Subtitle);

    // Library context menu / Ctrl+Enter. Uses the same LaunchViewModel readiness as Details: only a
    // prepared Ready installation launches; anything needing review or a choice opens Details instead.
    private async Task QuickLaunchAsync(GameSummary game)
    {
        if (commandsBlocked || navigating) return;
        try
        {
            await Launch.OpenAsync(game.Id);
            if (commandsBlocked || navigating || Launch.GameId != game.Id) return;
            if (!Launch.CanLaunch)
            {
                await Navigate(WorkspacePage.Details, game.Id);
                return;
            }
            await LaunchAndReportAsync(game.Name);
        }
        catch (Exception) { ViewModel.ReportUnexpected(); }
    }

    // Separate optional companion leaves the strict native-ui v1 schema unchanged.
    internal Views.MaterialMode MaterialModeSetting => materialMode;
    private async Task LoadMaterialPreferencesAsync()
    {
        try
        {
            var read = await materialPreferences.LoadAsync();
            materialMode = read.Preference.Mode == "Opaque" ? Views.MaterialMode.Standard : Enum.Parse<Views.MaterialMode>(read.Preference.Mode);
            materialReadOnly = read.ReadOnly; materialPreferenceNotice = read.Warning;
        }
        catch (Exception) { materialMode = Views.MaterialMode.Standard; materialReadOnly = true; materialPreferenceNotice = UiText.Get("材质设置未能读取，已使用标准材质；原文件保留。"); }
        materialInitialized = true;
        ApplyMaterialPolicy();
    }
    internal Task SetMaterialModeAsync(Views.MaterialMode mode)
    {
        if (commandsBlocked || !materialInitialized || materialReadOnly || !materialWrite.IsCompleted) return materialWrite;
        return materialWrite = SaveMaterialModeAsync(mode);
    }
    private async Task SaveMaterialModeAsync(Views.MaterialMode mode)
    {
        settingsPage.SetMaterialSaving(true);
        try
        {
            await materialPreferences.SaveAsync(new(mode.ToString()));
            if (visualStopped) return;
            materialMode = mode; materialPreferenceNotice = null;
        }
        catch (Exception) { materialPreferenceNotice = UiText.Get("材质设置未保存，已保留上次选项；请重试。"); }
        finally { settingsPage.SetMaterialSaving(false); if (!visualStopped) ApplyMaterialPolicy(); }
    }
    private Views.MaterialMode EffectiveMaterialMode => Enum.Parse<Views.MaterialMode>(MaterialActivationPolicy.Effective(
        materialMode.ToString(), Program.Profile?.Manifest.ProfileKind ?? NativeProfileKind.Development, controlledAdvanced));

    private void ApplyBackdrop(bool translucent)
    {
        windowBackdrop.Apply(translucent, windowCapabilities.Mica, () =>
        {
            if (windowCapabilities.Scenario == "mica-attachment-failure-simulation")
                throw new InvalidOperationException("TestMicaAttachmentFailure");
            if (micaTheme != Root.ActualTheme)
            {
                // Start the new backdrop with the new theme in the same frame as the XAML resources.
                // Retinting an attached backdrop can briefly leave the new foreground on the old tint.
                micaTheme = Root.ActualTheme;
                mica = new MicaBackdrop();
            }
            mica ??= new MicaBackdrop();
            if (!ReferenceEquals(SystemBackdrop, mica)) SystemBackdrop = mica;
            OpaqueCanvas.Visibility = Visibility.Collapsed;
        }, () =>
        {
            // Show the canvas before detaching a rejected material. A failed detach
            // still leaves a readable, theme-correct surface, including the first frame.
            OpaqueCanvas.Visibility = Visibility.Visible;
            try { if (SystemBackdrop is not null) SystemBackdrop = null; }
            catch (Exception exception) { chromeWarning ??= exception.GetType().Name; }
        });
    }

    // Live accent: WinUI resolves SystemAccentColor* once per process. On a Windows personalization change
    // the same names are refreshed at application scope and theme resources are re-resolved.
    private void OnSystemColorsChanged(UISettings sender, object args)
    {
        Color[] palette;
        try { palette = [sender.GetColorValue(UIColorType.Accent), sender.GetColorValue(UIColorType.AccentLight1), sender.GetColorValue(UIColorType.AccentLight2), sender.GetColorValue(UIColorType.AccentLight3),
            sender.GetColorValue(UIColorType.AccentDark1), sender.GetColorValue(UIColorType.AccentDark2), sender.GetColorValue(UIColorType.AccentDark3)]; }
        catch (Exception) { return; }
        DispatcherQueue.TryEnqueue(() => ApplyAccentPalette(palette, "Windows UISettings.ColorValuesChanged"));
    }
    private static readonly string[] accentKeys = ["SystemAccentColor", "SystemAccentColorLight1", "SystemAccentColorLight2", "SystemAccentColorLight3", "SystemAccentColorDark1", "SystemAccentColorDark2", "SystemAccentColorDark3"];
    private void ApplyAccentPalette(Color[] palette, string source)
    {
        if (visualStopped) return;
        var resources = Microsoft.UI.Xaml.Application.Current.Resources;
        var changed = false;
        for (var i = 0; i < accentKeys.Length; i++)
        {
            if (resources.TryGetValue(accentKeys[i], out var current) && current is Color existing && existing == palette[i]) continue;
            resources[accentKeys[i]] = palette[i]; changed = true;
        }
        accentEvents++; accentSource = source;
        if (changed) UpdateAccentBrushes(palette);
    }
    // Fluent accent brushes are shared instances (control styles alias them via StaticResource), so updating their
    // colours in the Light and Dark dictionaries reaches every control. Shades follow the Fluent theme mapping.
    private static readonly (string Key, int Light, int Dark)[] accentBrushes =
    [
        ("AccentFillColorDefaultBrush", 4, 2), ("AccentFillColorSecondaryBrush", 4, 2), ("AccentFillColorTertiaryBrush", 4, 2),
        ("AccentTextFillColorPrimaryBrush", 5, 3), ("AccentTextFillColorSecondaryBrush", 6, 3), ("AccentTextFillColorTertiaryBrush", 4, 2),
        ("AccentFillColorSelectedTextBackgroundBrush", 0, 0),
        ("AccentBrush", 5, 3), ("AccentFillBrush", 4, 2), ("SelectionBrush", 4, 2)
    ];
    private void UpdateAccentBrushes(Color[] palette)
    {
        var updated = 0;
        foreach (var dictionary in Microsoft.UI.Xaml.Application.Current.Resources.MergedDictionaries)
            // WinUI keys its dark theme dictionary "Default"; app dictionaries use "Dark".
            foreach (var theme in new[] { "Light", "Dark", "Default" })
            {
                if (!dictionary.ThemeDictionaries.TryGetValue(theme, out var value) || value is not ResourceDictionary themed) continue;
                foreach (var (key, light, dark) in accentBrushes)
                    if (themed.TryGetValue(key, out var brush) && brush is SolidColorBrush solid) { solid.Color = palette[theme == "Light" ? light : dark]; updated++; }
            }
        accentBrushUpdates = updated;
    }
    private int accentBrushUpdates;
    // Test-only: drives the same live-update path with a labelled simulated palette (no personalization change).
    private void SimulateSystemAccentEvent(string value)
    {
        if (visualFixtureTimer is null) throw new InvalidOperationException("Test capability required");
        if (value == "system")
        {
            if (colorSettings is not null) OnSystemColorsChanged(colorSettings, new object());
            return;
        }
        Color baseColor = value switch
        {
            "green" => Color.FromArgb(255, 16, 124, 16), "purple" => Color.FromArgb(255, 136, 23, 152), "orange" => Color.FromArgb(255, 202, 80, 16),
            "red" => Color.FromArgb(255, 196, 43, 28), _ => throw new InvalidDataException("Unknown Test accent")
        };
        Color Mix(double t, byte target) => Color.FromArgb(255, (byte)Math.Round(baseColor.R + (target - baseColor.R) * t), (byte)Math.Round(baseColor.G + (target - baseColor.G) * t), (byte)Math.Round(baseColor.B + (target - baseColor.B) * t));
        ApplyAccentPalette([baseColor, Mix(.25, 255), Mix(.5, 255), Mix(.75, 255), Mix(.25, 0), Mix(.5, 0), Mix(.75, 0)], "Test simulation · " + value);
    }

    private object ShellEvidence()
    {
        Color? accent = null;
        try { if (Microsoft.UI.Xaml.Application.Current.Resources.TryGetValue("SystemAccentColor", out var value) && value is Color color) accent = color; } catch (Exception) { }
        return new
        {
            selectedId = ViewModel.SelectedId, queryText = ViewModel.Query.Text, sort = ViewModel.Query.Sort, layout = ViewModel.Layout,
            titleBar = AppTitleBar.ActualHeight, captionTheme = AppWindow.TitleBar.PreferredTheme.ToString(), backEnabled = AppTitleBar.IsBackButtonEnabled, search = libraryPage.SearchLength, searchFocus = libraryPage.SearchFocus,
            paneMode = Navigation.DisplayMode.ToString(), paneOpen = Navigation.IsPaneOpen, titleBarHasExtraContent = TitleBarHasExtraContent,
            exitButtonPresent = false, accentEvents, accentSource, accentBrushUpdates, accentRgb = accent is { } a ? $"{a.R:X2}{a.G:X2}{a.B:X2}" : "framework",
            launchNotice = lastLaunchSeverity?.ToString(), diagnosticsToggle,
            compatibility = new { hostVersion = windowCapabilities.HostVersion.ToString(), minimumBuild = TheOrder.Platform.Windows.WindowsCompatibility.MinimumBuild,
                windowCapabilities.Scenario, customTitleBar, windowCapabilities.CaptionTheme, windowCapabilities.MonospaceFont,
                micaSupported = windowCapabilities.Mica, backdrop = windowBackdrop.UsingMica ? "Mica" : "Solid",
                canvasVisible = OpaqueCanvas.Visibility == Visibility.Visible, windowBackdrop.Failure, chromeWarning }
        };
    }
}
