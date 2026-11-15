using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TheOrder.Application;
using TheOrder.Domain;

namespace TheOrder.WinUI.Views;

public sealed partial class SettingsPage
{
    private void OnNextMoeState(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(NextMoe.AccessMode) or nameof(NextMoe.IsAccount) or nameof(NextMoe.HasPreview) or nameof(NextMoe.HasPending) or nameof(NextMoe.FolderDirection) or nameof(NextMoe.CanRecover))) return;
        var wasVisible = SyncPreviewRows.Visibility == Visibility.Visible;
        SynchronizeNextMoe();
        if (!wasVisible && NextMoe.HasPreview) InteractionMotion.RevealContent(SyncPreviewRows, .7f);
    }
    private void SynchronizeNextMoe()
    {
        synchronizing = true; NextMoeAccessChoice.OptionIndex = NextMoe.AccessMode;
        ApiKeyPanel.IsExpanded = NextMoe.AccessMode == 2;
        PersonalSyncPanel.Visibility = NextMoe.IsAccount ? Visibility.Visible : Visibility.Collapsed;
        SyncPreviewRows.Visibility = NextMoe.HasPreview ? Visibility.Visible : Visibility.Collapsed;
        SyncApplyButton.Visibility = NextMoe.HasPreview ? Visibility.Visible : Visibility.Collapsed;
        SyncRemovalCheck.Visibility = NextMoe.HasPreview && NextMoe.Rows.Any(x => x.Row.DeleteCloud) ? Visibility.Visible : Visibility.Collapsed;
        SyncRetryButton.Visibility = SyncReviewPendingButton.Visibility = NextMoe.HasPending ? Visibility.Visible : Visibility.Collapsed;
        NextMoeAccountText.Visibility = NextMoe.IsAccount ? Visibility.Visible : Visibility.Collapsed;
        SyncFolderPicker.Visibility = NextMoe.FolderDirection == 0 ? Visibility.Collapsed : Visibility.Visible;
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
    private async void OnPauseSync(object sender, RoutedEventArgs e) => await NextMoe.PauseAutomaticAsync();
    private async void OnUnmapFolder(object sender, RoutedEventArgs e)
    {
        if (MappedFolderChoice.SelectedOption is not SyncFolderMapping mapping) return;
        var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = "取消收藏夹关联", Content = "取消「" + mapping.Name + "」与本机独立收藏列表的关联。云端成员、书架条目和游戏文件保留。",
            PrimaryButtonText = "取消这项关联", CloseButtonText = "返回", DefaultButton = ContentDialogButton.Close };
        if (await dialog.ShowInShellAsync() == ContentDialogResult.Primary) await NextMoe.UnmapFolderAsync(mapping.FolderId);
    }
    private void OnCancelNextMoe(object sender, RoutedEventArgs e) => NextMoe.Cancel();
    private async void OnPreviewSync(object sender, RoutedEventArgs e) => await NextMoe.PreviewAsync();
    private async void OnRetrySync(object sender, RoutedEventArgs e) => await NextMoe.RetryAsync();
    private async void OnReviewPending(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = "核对并结束旧任务", Content = "先读取当前云端数据，再结束结果不确定的旧任务，不会重新发送写入。之后请重新预览并确认；如上次曾创建收藏夹，请从云端列表选择，避免再次创建。",
            PrimaryButtonText = "读取云端并结束旧任务", CloseButtonText = "取消", DefaultButton = ContentDialogButton.Close };
        if (await dialog.ShowInShellAsync() == ContentDialogResult.Primary) await NextMoe.ReviewPendingAsync();
    }
    private async void OnFolderPermission(object sender, RoutedEventArgs e)
    {
        var write = NextMoe.FolderDirection is 2 or 3;
        var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = "允许访问 NextMoe 收藏夹", Content = write
            ? "你已选择上传或双向同步，需要允许读取和写入你选定的收藏夹。重新授权本身不会上传数据；仍须预览并确认。"
            : "读取私人收藏夹需要额外授权。重新授权本身不会上传数据；导入仍须预览并确认。",
            PrimaryButtonText = "继续到系统浏览器授权", CloseButtonText = "取消", DefaultButton = ContentDialogButton.Close };
        if (await dialog.ShowInShellAsync() == ContentDialogResult.Primary) await NextMoe.LoginAsync(folderPermissions: true);
    }
    private async void OnApplySync(object sender, RoutedEventArgs e)
    {
        if (!NextMoe.HasPreview) return;
        var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = "确认同步", Content = NextMoe.PreviewSummary + "\n\n按预览中逐项选择的方式处理。安装路径、EXE、笔记、图片和凭据不会上传；云端汇总独立保存，本地历史保留。"
            + "\n请在同步完成前避免在其他设备修改相同记录。双向模式的后台上传仍需预览确认，新增云端记录可自动导入。",
            PrimaryButtonText = "确认执行所选项目", CloseButtonText = "取消", DefaultButton = ContentDialogButton.Close };
        if (await dialog.ShowInShellAsync() == ContentDialogResult.Primary) await NextMoe.ApplyAsync();
    }
    private async Task<(bool Confirmed, string Name, string[] WorkIds)> ChooseFolderMembers(string title, bool creating, string? folderId = null)
    {
        var works = await NextMoe.LocalWorksAsync(CancellationToken.None);
        var existing = folderId is null ? [] : await NextMoe.MappedMembersAsync(folderId);
        var choices = works.Select(x => new MemberChoice(x.Id, x.Name, x.Metadata!.Id)).ToArray();
        var list = new ListView { ItemsSource = choices, DisplayMemberPath = "Name",
            SelectionMode = ListViewSelectionMode.Multiple, MaxHeight = 350 };
        foreach (var item in choices.Where(x => existing.Contains(x.WorkId, StringComparer.Ordinal))) list.SelectedItems.Add(item);
        var name = new TextBox { Header = "新收藏夹名称", MaxLength = 100, PlaceholderText = "例如：正在游玩", Visibility = creating ? Visibility.Visible : Visibility.Collapsed };
        var content = new StackPanel { Spacing = 12 }; content.Children.Add(name);
        content.Children.Add(new TextBlock { Text = "选择独立同步收藏列表的成员。这里不改本地分类、标签或安装信息；成员上传将在下一次预览确认后进行。", TextWrapping = TextWrapping.Wrap }); content.Children.Add(list);
        var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = title, Content = content, PrimaryButtonText = creating ? "确认创建私密云端收藏夹" : "保存本地成员映射",
            CloseButtonText = "取消", DefaultButton = ContentDialogButton.Close };
        var accepted = await dialog.ShowInShellAsync() == ContentDialogResult.Primary;
        // Previously imported uninstalled members remain in the independent list, even if no bookshelf row is currently available.
        var selected = list.SelectedItems.Cast<MemberChoice>().Select(x => x.WorkId).Concat(existing.Where(id => !choices.Any(x => x.WorkId == id))).Distinct(StringComparer.Ordinal).ToArray();
        return (accepted, name.Text, selected);
    }
    public sealed record MemberChoice(string EntryId, string Name, string WorkId);
    private async void OnMapSyncFolder(object sender, RoutedEventArgs e)
    {
        if (SyncFolderChoice.SelectedOption is not CloudFolder folder) return;
        var choice = await ChooseFolderMembers("设置「" + folder.Name + "」的本地成员", false, folder.Id);
        if (choice.Confirmed) await NextMoe.MapFolderAsync(folder, choice.WorkIds);
    }
    private async void OnCreateSyncFolder(object sender, RoutedEventArgs e)
    {
        var choice = await ChooseFolderMembers("创建 NextMoe 收藏夹", true);
        if (choice.Confirmed) await NextMoe.CreateFolderAsync(choice.Name, choice.WorkIds);
    }
}
