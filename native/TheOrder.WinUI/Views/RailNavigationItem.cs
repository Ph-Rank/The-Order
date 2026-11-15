using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

namespace TheOrder.WinUI.Views;

// Keep WinUI's selection, keyboard, focus, accessibility and theme states. Only
// remove the stock leaf item's asymmetric icon/chevron space inside its surface.
public sealed class RailNavigationItem : NavigationViewItem
{
    public RailNavigationItem()
    {
        DefaultStyleKey = typeof(NavigationViewItem);
        Loaded += (_, _) => CenterLeafContent();
        ActualThemeChanged += (_, _) => CenterLeafContent();
    }
    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate(); CenterLeafContent();
    }
    private void CenterLeafContent()
    {
        if (Icon is not null || MenuItems.Count != 0 || HasUnrealizedChildren
            || GetTemplateChild("NavigationViewItemPresenter") is not NavigationViewItemPresenter presenter) return;
        presenter.ApplyTemplate();
        if (Find<Microsoft.UI.Xaml.Shapes.Rectangle>(presenter, "SelectionIndicator") is { } indicator)
            indicator.Fill = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        if (Find<Grid>(presenter, "ContentGrid") is { } grid)
        { grid.Margin = new Thickness(0); grid.HorizontalAlignment = HorizontalAlignment.Stretch; }
        if (Find<Border>(presenter, "IconColumn") is { } icon)
        { icon.Width = 0; icon.Visibility = Visibility.Collapsed; }
        if (Find<ContentPresenter>(presenter, "ContentPresenter") is { } content)
        { content.Margin = new Thickness(4, 0, 4, 0); content.HorizontalAlignment = HorizontalAlignment.Stretch; }
    }
    private static T? Find<T>(DependencyObject root, string name) where T : FrameworkElement
    {
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T element && element.Name == name) return element;
            if (Find<T>(child, name) is { } found) return found;
        }
        return null;
    }
}
