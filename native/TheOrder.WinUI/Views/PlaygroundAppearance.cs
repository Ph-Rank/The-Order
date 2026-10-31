using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace TheOrder.WinUI.Views;

internal static class PlaygroundAppearance
{
    private static ResourceDictionary? dictionary;
    private static volatile bool highContrast;
    internal static bool HighContrast => highContrast;
    internal static event Action? PolicyChanged;
    // The window already owns the OS accessibility observer. A notification
    // subscription failure must never change the meaning of high contrast.
    internal static void SetHighContrast(bool enabled)
    {
        if (highContrast == enabled) return;
        highContrast = enabled; PolicyChanged?.Invoke();
    }
    private static ResourceDictionary Dictionary => dictionary ??= CreateResources();
    public static ResourceDictionary CreateResources() => new() { Source = new Uri("ms-appx:///Views/PlaygroundTheme.xaml") };
    public static void Scope(FrameworkElement element)
    {
        if (!element.Resources.MergedDictionaries.Any(x => x.Source?.OriginalString == "ms-appx:///Views/PlaygroundTheme.xaml")) element.Resources.MergedDictionaries.Add(CreateResources());
    }
    public static Style Style(string key) => (Style)Dictionary[key];
    public static void Apply(Control control, string key = "PGGhostButton") => control.Style = Style(key);
    public static Brush Brush(FrameworkElement element, string key)
    {
        string theme = highContrast ? "HighContrast" : ThemeFor(element) == ElementTheme.Light ? "Light" : "Dark";
        try { return (Brush)((ResourceDictionary)Dictionary.ThemeDictionaries[theme])[key]; }
        catch
        {
            var current = (ResourceDictionary)Dictionary.ThemeDictionaries[theme];
            return (Brush)current[key is "PGTextBrush" or "PGSecondaryBrush" ? "PGTextBrush" : "PGCanvasBrush"];
        }
    }
    internal static ElementTheme ThemeFor(FrameworkElement element)
    {
        try
        {
            for (DependencyObject? node = element; node is not null; node = VisualTreeHelper.GetParent(node))
                if (node is PlaygroundView view) return view.PaletteTheme;
        }
        catch { /* A detached row falls back to its own native theme. */ }
        return element.ActualTheme;
    }
    internal static string ColorOf(Brush? brush) => brush is SolidColorBrush solid
        ? $"#{solid.Color.A:X2}{solid.Color.R:X2}{solid.Color.G:X2}{solid.Color.B:X2}" : brush?.GetType().Name ?? "none";
    internal static object PaletteEvidence => new { highContrast,
        darkCanvas = PaletteColor("Dark", "PGCanvasBrush"), lightCanvas = PaletteColor("Light", "PGCanvasBrush"),
        darkText = PaletteColor("Dark", "PGTextBrush"), lightText = PaletteColor("Light", "PGTextBrush") };
    private static string PaletteColor(string theme, string key)
    { try { return ColorOf((Brush)((ResourceDictionary)Dictionary.ThemeDictionaries[theme])[key]); } catch { return "unavailable"; } }
    public static FontIcon Icon(string glyph, double size = 16) => new() { Glyph = glyph, FontFamily = new FontFamily("Segoe Fluent Icons"), FontSize = size, IsTextScaleFactorEnabled = false };
    public static StackPanel IconLabel(string glyph, string text, double size = 13) => new() { Orientation = Orientation.Horizontal, Spacing = 8,
        Children = { Icon(glyph), new TextBlock { Text = text, FontSize = size, VerticalAlignment = VerticalAlignment.Center } } };
    public static void Tip(DependencyObject target, string text)
    { Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(target, text); ToolTipService.SetToolTip(target, text); }
}
