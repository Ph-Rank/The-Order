using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TheOrder.Application.Migrations;
using TheOrder.Domain;

namespace TheOrder.WinUI.Views;

public sealed partial class SettingsPage
{
    public Func<string, CancellationToken, Task<ManagerExportPackage>>? PreviewManagerExport { get; set; }
    public Func<string, Task<string?>>? ChooseManagerExportDestination { get; set; }
    public Func<string, ManagerExportPackage, CancellationToken, Task<string?>>? SaveManagerExport { get; set; }
    private bool managerExportActive;
    private CancellationTokenSource? managerExportCancellation;
    private ContentDialog? managerExportDialog;
    private async Task<ContentDialogResult> ShowManagerExportAsync(ContentDialog dialog)
    {
        managerExportDialog = dialog;
        try { return await dialog.ShowAsync(); }
        finally { if (ReferenceEquals(managerExportDialog, dialog)) managerExportDialog = null; }
    }
    private async void OnExportManager(object sender, RoutedEventArgs e)
    {
        if (managerExportActive || migrationFlowActive || !Migration.CanSelect || !ViewModel.CanEdit
            || PreviewManagerExport is not { } preview || SaveManagerExport is not { } save || ChooseManagerExportDestination is not { } choose) return;
        managerExportActive = true; SynchronizeMigration(); var revision = migrationGeneration;
        using var cancellation = new CancellationTokenSource(); managerExportCancellation = cancellation;
        try
        {
            if (PrepareBackupAction is { } prepare && !await prepare()) return;
            var managers = new[] { "potatovn", "vnite", "reinamanager", "playnite", "lunabox" };
            var choice = new ComboBox { Header = "目标管理器", ItemsSource = managers.Select(MigrationFormats.Display).ToArray(), SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
            var body = new StackPanel { Spacing = 16, MaxWidth = 520 }; body.Children.Add(choice);
            body.Children.Add(Text("保留现有 The Order 备份功能。兼容 ZIP 只包含游戏库和个人记录，不含游戏文件、存档、账号、脚本或插件。\n\n恢复通常会替换目标管理器的游戏库，请先备份目标库；不同格式无法表达的字段会在下一步说明。"));
            if (await ShowManagerExportAsync(new() { XamlRoot = XamlRoot, RequestedTheme = ActualTheme, Title = "导出兼容 ZIP",
                Content = body, PrimaryButtonText = "查看导出预览", CloseButtonText = "取消", DefaultButton = ContentDialogButton.Close }) != ContentDialogResult.Primary
                || revision != migrationGeneration || !IsLoaded) return;
            var manager = managers[choice.SelectedIndex]; ManagerExportProgress.Visibility = Visibility.Visible;
            ManagerExportMessage.Visibility = Visibility.Visible; ManagerExportMessage.Text = "正在生成兼容备份…";
            var package = await preview(manager, cancellation.Token);
            if (revision != migrationGeneration || !IsLoaded) return;
            var text = $"{MigrationFormats.Display(manager)} · {package.Entries} 部作品\n\n"
                + (manager == "reinamanager" ? "ZIP 解压后，在 ReinaManager 中导入其中的 .db 文件。\n\n" : "在目标管理器的备份恢复中选择此 ZIP。\n\n")
                + string.Join("\n\n", package.Notices.Select(n => $"{n.Message}（{n.Count} 项）"))
                + "\n\n无法直接表示的记录保存在 ZIP 的补充文件中，目标管理器不会自动读取这些补充资料。完整还原 The Order 书架，请使用原有的书架备份。";
            ManagerExportProgress.Visibility = Visibility.Collapsed;
            if (await ShowManagerExportAsync(new() { XamlRoot = XamlRoot, RequestedTheme = ActualTheme, Title = "确认导出范围",
                Content = new ScrollViewer { Content = Text(text), MaxHeight = 420, MaxWidth = 540, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled },
                PrimaryButtonText = "保存 ZIP…", CloseButtonText = "取消", DefaultButton = ContentDialogButton.Close }) != ContentDialogResult.Primary
                || revision != migrationGeneration || !IsLoaded) { ManagerExportMessage.Text = "已取消，未保存导出文件。"; return; }
            var path = await choose(manager);
            if (path is null || revision != migrationGeneration || !IsLoaded) { ManagerExportMessage.Text = "已取消，未保存导出文件。"; return; }
            ManagerExportProgress.Visibility = Visibility.Visible;
            var warning = await save(path, package, cancellation.Token);
            if (revision == migrationGeneration && IsLoaded) ManagerExportMessage.Text = $"已导出 {package.Entries} 部作品，并校验 ZIP 完整性。" + (warning is null ? "" : "文件已保存，请检查磁盘状态。");
        }
        catch (OperationCanceledException) { if (IsLoaded) ManagerExportMessage.Text = "已取消导出。"; }
        catch { if (IsLoaded) ManagerExportMessage.Text = "兼容导出未完成。请检查目标格式支持范围、文件占用及磁盘空间；书架未改动。"; }
        finally
        {
            managerExportCancellation = null; managerExportActive = false;
            ManagerExportProgress.Visibility = Visibility.Collapsed; SynchronizeMigration();
        }
    }
}
