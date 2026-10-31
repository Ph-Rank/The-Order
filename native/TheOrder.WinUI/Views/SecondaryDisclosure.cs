using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace TheOrder.WinUI.Views;

internal static class SecondaryDisclosure
{
    internal static void Apply(Expander section)
    {
        section.FontSize = 13; section.MinHeight = 34; section.Padding = new Thickness(12);
        var transparent = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        section.Resources["ExpanderHeaderBackground"] = transparent;
        section.Resources["ExpanderHeaderBorderBrush"] = transparent;
        section.Resources["ExpanderHeaderBorderThickness"] = new Thickness(0);
    }
}
