using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace TheOrder.WinUI.Views;

public sealed partial class SettingsPage
{
    internal Func<Task>? ImportNextMoeRatings { get; set; }
    internal Func<Task>? WriteCollections { get; set; }

    private async void OnImportNextMoeRatings(object sender, RoutedEventArgs e)
    {
        if (ImportNextMoeRatings is { } read) await read();
    }

    private async void OnWriteCollections(object sender, RoutedEventArgs e)
    {
        if (WriteCollections is not { } write) return;
        WriteCollectionsButton.IsEnabled = false;
        try { await write(); }
        finally { WriteCollectionsButton.IsEnabled = true; }
    }
    private void OnNextMoeState(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(NextMoe.AccessMode) or nameof(NextMoe.IsAccount) or nameof(NextMoe.CanRecover))) return;
        SynchronizeNextMoe();
    }
    private void SynchronizeNextMoe()
    {
        synchronizing = true; NextMoeAccessChoice.OptionIndex = NextMoe.AccessMode;
        ApiKeyPanel.IsExpanded = NextMoe.AccessMode == 2;
        NextMoeAccountText.Visibility = NextMoe.IsAccount ? Visibility.Visible : Visibility.Collapsed;
        RecoverLoginButton.Visibility = NextMoe.CanRecover ? Visibility.Visible : Visibility.Collapsed;
        DispatcherQueue.TryEnqueue(ResizeSettingsControls);
        synchronizing = false;
    }
    private async void OnNextMoeAccess(object sender, SelectionChangedEventArgs e)
    {
        if (synchronizing || !IsLoaded || NextMoeAccessChoice.OptionIndex < 0) return;
        var selected = NextMoeAccessChoice.OptionIndex;
        if (selected == 2 && !Online.HasCredential) { ApiKeyPanel.IsExpanded = true; editingCredential = true; SynchronizeCredential(); return; }
        await NextMoe.SelectModeAsync(selected); SynchronizeNextMoe();
    }
    private async void OnNextMoeLogin(object sender, RoutedEventArgs e) => await NextMoe.LoginAsync();
    private async void OnNextMoeLogout(object sender, RoutedEventArgs e) => await NextMoe.LogoutAsync();
    private async void OnRecoverLogin(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = "重置无法读取的本机登录", Content = "只重置本机登录状态，随后可以重新授权。原 API Key、书架和账号同步记录保留，不修改云端数据。",
            PrimaryButtonText = "重置本机登录", CloseButtonText = "取消", DefaultButton = ContentDialogButton.Close };
        if (await dialog.ShowInShellAsync() == ContentDialogResult.Primary) await NextMoe.RecoverLoginAsync();
    }
    private void OnCancelNextMoe(object sender, RoutedEventArgs e) => NextMoe.Cancel();
}
