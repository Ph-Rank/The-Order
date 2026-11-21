using Microsoft.UI.Xaml;

namespace TheOrder.WinUI.Views;

public sealed partial class SettingsPage
{
    private void OnVndbAuthorize(object sender, RoutedEventArgs e)
    {
        VndbEditor.Visibility = Visibility.Visible;
        VndbToken.Focus(FocusState.Programmatic);
    }

    private void CloseVndbEditor()
    {
        VndbToken.Password = "";
        VndbEditor.Visibility = Visibility.Collapsed;
    }

    private void OnVndbClose(object sender, RoutedEventArgs e) => CloseVndbEditor();
    private async void OnVndbSave(object sender, RoutedEventArgs e)
    {
        var token = VndbToken.Password;
        VndbToken.Password = "";
        await Vndb.SaveAsync(token);
        if (Vndb.Message.StartsWith("已保存授权", StringComparison.Ordinal)) CloseVndbEditor();
    }
    private async void OnVndbTest(object sender, RoutedEventArgs e) => await Vndb.TestAsync();
    private async void OnVndbRemove(object sender, RoutedEventArgs e)
    {
        CloseVndbEditor();
        await Vndb.ClearAsync();
    }
}
