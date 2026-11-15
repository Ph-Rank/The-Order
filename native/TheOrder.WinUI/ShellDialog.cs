using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace TheOrder.WinUI;

internal static class ShellDialog
{
    internal static async Task<ContentDialogResult> ShowInShellAsync(this ContentDialog dialog)
    {
        var xamlRoot = dialog.XamlRoot;
        if (xamlRoot?.Content is not FrameworkElement root || root.FindName("DialogLayer") is not Grid host)
            return await dialog.ShowAsync();

        // Keep both the scrim and the dialog inside the row below the native caption.
        // The WinUI dialog still owns its keyboard focus, closing and command result.
        var returnFocus = FocusManager.GetFocusedElement(xamlRoot) as Control;
        dialog.TabFocusNavigation = KeyboardNavigationMode.Cycle;
        dialog.Opened += OnOpened;
        host.Visibility = Visibility.Visible;
        host.Children.Add(dialog);
        try { return await dialog.ShowAsync(ContentDialogPlacement.InPlace); }
        finally
        {
            dialog.Opened -= OnOpened;
            host.Children.Remove(dialog);
            if (host.Children.Count == 0)
            {
                host.Visibility = Visibility.Collapsed;
                if (returnFocus?.IsLoaded == true) returnFocus.Focus(FocusState.Programmatic);
            }
        }

        void OnOpened(ContentDialog sender, ContentDialogOpenedEventArgs args)
        {
            host.DispatcherQueue.TryEnqueue(() =>
            {
                if (!host.Children.Contains(sender)) return;
                for (var node = FocusManager.GetFocusedElement(xamlRoot) as DependencyObject; node is not null; node = VisualTreeHelper.GetParent(node))
                    if (ReferenceEquals(node, sender)) return;
                if (FocusManager.FindFirstFocusableElement(sender) is Control first) first.Focus(FocusState.Programmatic);
            });
        }
    }
}
