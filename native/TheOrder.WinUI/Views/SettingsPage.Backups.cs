using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TheOrder.Application;

namespace TheOrder.WinUI.Views;

public sealed partial class SettingsPage
{
    public Func<Task<string?>>? ChooseBackupDestination { get; set; }
    public Func<Task<string?>>? ChooseBackupFile { get; set; }
    public Func<Task<bool>>? PrepareBackupAction { get; set; }
    public Func<LibraryRestorePreview, Task>? RestoreLibrary { get; set; }
    private bool synchronizingBackups;
    private ContentDialog? backupDialog;
    private int backupDialogGeneration;
    internal void CancelBackupDialog() { backupDialogGeneration++; backupDialog?.Hide(); }
    private async Task<ContentDialogResult> ShowBackupDialogAsync(ContentDialog dialog)
    {
        backupDialog = dialog;
        try { return await dialog.ShowInShellAsync(); }
        finally { if (ReferenceEquals(backupDialog, dialog)) backupDialog = null; }
    }
    private void OnBackupState(object? sender, PropertyChangedEventArgs e) => SynchronizeBackups();
    private void SynchronizeBackups()
    {
        synchronizingBackups = true; AutomaticBackup.IsOn = Backups.Automatic;
        RestoreAutomaticBackup.IsEnabled = Backups.CanOperate && !Backups.Records.IsEmpty;
        synchronizingBackups = false;
    }
    private async void OnAutomaticBackup(object sender, RoutedEventArgs e)
    { if (!synchronizingBackups && Backups.CanChangeAutomatic) await Backups.SetAutomaticAsync(AutomaticBackup.IsOn); }
    private async void OnExportBackup(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!Backups.CanOperate || PrepareBackupAction is { } prepare && !await prepare()) return;
            if (ChooseBackupDestination is { } choose && await choose() is { } path) await Backups.ExportAsync(path);
        }
        catch { ViewModel.ReportUnexpected(); }
    }
    private async void OnRestoreBackup(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!Backups.CanOperate || PrepareBackupAction is { } prepare && !await prepare()) return;
            if (ChooseBackupFile is { } choose && await choose() is { } path) await ShowRestorePreview(path, false);
        }
        catch { ViewModel.ReportUnexpected(); }
    }
    private async void OnRestoreAutomaticBackup(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!Backups.CanOperate || Backups.Records.IsEmpty || PrepareBackupAction is { } prepare && !await prepare()) return;
            var list = new ListView { SelectionMode = ListViewSelectionMode.Single, MaxHeight = 320 };
            foreach (var record in Backups.Records) list.Items.Add(new ListViewItem
            { Content = $"{record.CreatedAt.ToLocalTime():yyyy-MM-dd HH:mm:ss} · {record.Entries} 部 · {record.Reason}", Tag = record.Id });
            list.SelectedIndex = 0;
            var dialog = new ContentDialog { XamlRoot = XamlRoot, RequestedTheme = ActualTheme, Title = "自动备份",
                Content = list, PrimaryButtonText = "预览恢复", CloseButtonText = "取消", DefaultButton = ContentDialogButton.Close };
            if (await ShowBackupDialogAsync(dialog) == ContentDialogResult.Primary && list.SelectedItem is ListViewItem { Tag: string id })
                await ShowRestorePreview(id, true);
        }
        catch { ViewModel.ReportUnexpected(); }
    }
    private async Task ShowRestorePreview(string source, bool savedSnapshot)
    {
        var generation = backupDialogGeneration;
        var choice = new SelectedFirstComboBox { Header = "恢复方式", HorizontalAlignment = HorizontalAlignment.Stretch };
        choice.Options.Add(new ComboBoxItem { Content = "添加缺失条目（保留现有作品及个人记录）" });
        choice.Options.Add(new ComboBoxItem { Content = "恢复整份书架（覆盖当前书架及个人记录）" });
        choice.OptionIndex = 0;
        var description = new TextBlock { TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
        var scope = new TextBlock { Text = "备份包含作品资料、个人记录、安装位置和本地封面。不包含游戏文件、游戏存档、登录凭据、设置或自选横幅。恢复前会另存当前书架，自动云端同步会暂停。", TextWrapping = TextWrapping.Wrap };
        var content = new StackPanel { Spacing = 16, MaxWidth = 520 }; content.Children.Add(choice); content.Children.Add(description); content.Children.Add(scope);
        var dialog = new ContentDialog { XamlRoot = XamlRoot, RequestedTheme = ActualTheme, Title = "恢复书架",
            Content = content, PrimaryButtonText = "确认恢复", IsPrimaryButtonEnabled = false, CloseButtonText = "取消", DefaultButton = ContentDialogButton.Close };
        async Task Preview()
        {
            choice.IsEnabled = false; dialog.IsPrimaryButtonEnabled = false; description.Text = "正在校验备份…";
            await Backups.PreviewAsync(source, savedSnapshot, (LibraryRestoreMode)choice.OptionIndex);
            if (generation != backupDialogGeneration) return;
            if (Backups.Preview is { } preview && preview.Mode == (LibraryRestoreMode)choice.OptionIndex)
            {
                description.Text = preview.Mode == LibraryRestoreMode.AddMissing
                    ? $"当前 {preview.CurrentCount} 部；备份 {preview.BackupCount} 部。将添加 {preview.Added} 部，跳过 {preview.Skipped} 部重复作品。"
                    : $"当前 {preview.CurrentCount} 部将恢复为备份中的 {preview.BackupCount} 部，个人记录一并恢复到备份时的状态。";
                description.Text += "\n\n" + string.Join("、", preview.Names) + (preview.BackupCount > preview.Names.Length ? "…" : "");
                dialog.IsPrimaryButtonEnabled = true;
            }
            else description.Text = Backups.Message;
            choice.IsEnabled = true;
        }
        await Preview();
        if (generation != backupDialogGeneration) return;
        choice.OptionChanged += async (_, _) => { try { await Preview(); } catch { dialog.IsPrimaryButtonEnabled = false; ViewModel.ReportUnexpected(); } };
        if (await ShowBackupDialogAsync(dialog) == ContentDialogResult.Primary && generation == backupDialogGeneration && Backups.Preview is { } selected && RestoreLibrary is { } restore)
            await restore(selected);
    }
}
