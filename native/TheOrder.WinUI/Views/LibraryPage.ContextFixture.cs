using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace TheOrder.WinUI.Views;

public sealed partial class LibraryPage
{
    // Called only from MainWindow's bounded synthetic Test fixture.
    internal async Task ShowCardMenuFixtureAsync(string id)
    {
        var item = ViewModel.Items.Single(x => x.Id == id); Active.ScrollIntoView(item);
        for (int i = 0; i < 20 && Active.ContainerFromItem(item) is not FrameworkElement; i++) await Task.Delay(25);
        if (Active.ContainerFromItem(item) is not FrameworkElement target) throw new InvalidOperationException("Card not realized.");
        target.Focus(FocusState.Programmatic); ShowCardMenu(item, target); await Task.Delay(200);
    }
    internal void CloseCardMenuFixture() => activeCardMenu?.Hide();
    internal MenuFlyoutItem ContextItemFixture(string text) => activeCardMenu?.Items.OfType<MenuFlyoutItem>().Single(x => x.Text == text)
        ?? throw new InvalidOperationException("Menu not open.");
    internal FrameworkElement? ContextPresenterFixture()
    {
        if (activeCardMenu is null || XamlRoot is null) return null;
        foreach (var popup in VisualTreeHelper.GetOpenPopupsForXamlRoot(XamlRoot))
        {
            var pending = new Queue<DependencyObject>(); if (popup.Child is not null) pending.Enqueue(popup.Child);
            int count = 0;
            while (pending.TryDequeue(out var node) && count++ < 200)
            {
                if (node is MenuFlyoutPresenter presenter) return presenter;
                for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++) pending.Enqueue(VisualTreeHelper.GetChild(node, i));
            }
        }
        return null;
    }
    internal object? ContextMenuEvidenceFixture() =>
        activeCardMenu?.Items.OfType<MenuFlyoutItem>().Select(x => new { x.Text, x.IsEnabled }).ToArray();
}
