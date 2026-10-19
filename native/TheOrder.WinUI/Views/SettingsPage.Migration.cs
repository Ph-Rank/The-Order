using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TheOrder.Application;

namespace TheOrder.WinUI.Views;

public sealed partial class SettingsPage
{
    public Func<Task<string?>>? ChooseMigrationFile { get; set; }
    public Func<LibraryMigrationPreview, CancellationToken, Task<ClientResult>>? ImportMigration { get; set; }
    private ContentDialog? migrationDialog;
    private int migrationGeneration;
    private bool migrationFlowActive;
    private void OnMigrationState(object? sender, PropertyChangedEventArgs e) => SynchronizeMigration();
    private void SynchronizeMigration()
    {
        MigrationSelect.IsEnabled = !migrationFlowActive && Migration.CanSelect && ViewModel.CanEdit;
        MigrationProgress.Visibility = Visible(Migration.IsBusy);
        MigrationCancel.Visibility = Visible(Migration.IsBusy);
    }
    internal void CancelMigrationDialog()
    { migrationGeneration++; migrationDialog?.Hide(); Migration.Cancel(); }
    private void OnCancelMigration(object sender, RoutedEventArgs e) => Migration.Cancel();
    private async void OnImportManager(object sender, RoutedEventArgs e)
    {
        if (migrationFlowActive || !Migration.CanSelect || !ViewModel.CanEdit) return;
        migrationFlowActive = true; SynchronizeMigration();
        var revision = ++migrationGeneration;
        try
        {
            if (PrepareBackupAction is { } prepare && !await prepare()) return;
            var path = ChooseMigrationFile is { } choose ? await choose() : null;
            if (path is null || revision != migrationGeneration || !IsLoaded) return;
            await Migration.PreviewAsync(path);
            if (revision != migrationGeneration || !IsLoaded || Migration.Preview is not { } preview) return;
            var content = new Grid { MaxWidth = 560, RowSpacing = 12 };
            content.RowDefinitions.Add(new() { Height = GridLength.Auto });
            content.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
            content.RowDefinitions.Add(new() { Height = GridLength.Auto });
            var summary = new TextBlock { Text = Migration.Summary, TextWrapping = TextWrapping.Wrap };
            content.Children.Add(summary);
            // ListView virtualizes large exports; no thousands of controls in a StackPanel.
            var rows = new ListView { ItemsSource = Migration.Rows, ItemTemplate = (DataTemplate)Resources["MigrationRowTemplate"],
                SelectionMode = ListViewSelectionMode.None, MaxHeight = 320, MinHeight = 80 };
            Grid.SetRow(rows, 1); content.Children.Add(rows);
            var notice = new TextBlock { Text = Migration.Message + "\n\n导入前自动备份。进度取较高状态，笔记、评分和时长冲突保留原值；身份待核对的条目跳过。游戏文件及账号不迁移。", TextWrapping = TextWrapping.Wrap };
            Grid.SetRow(notice, 2); content.Children.Add(notice);
            var dialog = new ContentDialog { XamlRoot = XamlRoot, RequestedTheme = ActualTheme, Title = "导入 PotatoVN 书架",
                Content = content, PrimaryButtonText = "备份并导入", IsPrimaryButtonEnabled = preview.CanApply,
                CloseButtonText = "取消", DefaultButton = ContentDialogButton.Close };
            migrationDialog = dialog;
            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary && revision == migrationGeneration && IsLoaded && ImportMigration is { } apply)
                await Migration.ApplyAsync(apply);
            else Migration.Clear();
        }
        catch { ViewModel.ReportUnexpected(); }
        finally { migrationDialog = null; migrationFlowActive = false; SynchronizeMigration(); }
    }
}
