using System.Collections.Immutable;
using TheOrder.Application;
using Microsoft.Windows.Storage.Pickers;

namespace TheOrder.WinUI;

public sealed partial class MainWindow
{
    private async Task<ClientResult> ImportManagerLibraryAsync(LibraryMigrationPreview preview, LibraryMigrationService service, CancellationToken cancellation)
    {
        if (commandsBlocked || !ViewModel.CanEdit || !await ResolveUnsavedAsync())
            return new(ClientOutcome.ReadOnly, "请先处理未保存的修改或等待当前操作完成。" );
        cancellation.ThrowIfCancellationRequested();
        recognition.CancelAll();
        if (nextMoeSettings.Automatic)
        {
            await nextMoeSettings.PauseAutomaticAsync();
            if (nextMoeSettings.Automatic) return new(ClientOutcome.ValidationFailure, "自动同步未能暂停，请稍后重试。" );
        }
        await nextMoeSettings.DrainAsync();
        nextMoeSettings.PauseCommands(true);
        try
        {
            var result = await ViewModel.ExecuteFileMutationAsync(async token =>
            {
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, cancellation);
                return await service.ApplyAsync(preview, linked.Token);
            });
            if (result.IsCommitted) await backups.ReloadAfterRestoreAsync();
            return result;
        }
        finally { nextMoeSettings.PauseCommands(commandsBlocked); }
    }
    private async Task<string?> PickBackupDestination()
    {
        var picker = new FileSavePicker(AppWindow.Id)
        { SuggestedFileName = $"The-Order-Backup-{DateTime.Now:yyyyMMdd-HHmmss}", DefaultFileExtension = ".json", ShowOverwritePrompt = true };
        picker.FileTypeChoices.Add("JSON 书架备份", [".json"]);
        return (await picker.PickSaveFileAsync())?.Path;
    }
    private async Task<string?> PickManagerExportDestination(string manager)
    {
        var picker = new FileSavePicker(AppWindow.Id)
        { SuggestedFileName = $"The-Order-{manager}-{DateTime.Now:yyyyMMdd-HHmmss}" + (manager == "potatovn" ? ".pvnExport" : ""), DefaultFileExtension = ".zip", ShowOverwritePrompt = true };
        picker.FileTypeChoices.Add("ZIP 兼容备份", [".zip"]);
        return (await picker.PickSaveFileAsync())?.Path;
    }
    private async Task RestoreLibraryAsync(LibraryRestorePreview preview, OfflineLibraryClient library)
    {
        if (commandsBlocked || !ViewModel.CanEdit || !await ResolveUnsavedAsync()) return;
        recognition.CancelAll();
        if (nextMoeSettings.Automatic)
        {
            await nextMoeSettings.PauseAutomaticAsync();
            if (nextMoeSettings.Automatic) { ViewModel.ReportUnexpected(); return; }
        }
        await nextMoeSettings.DrainAsync();
        nextMoeSettings.PauseCommands(true);
        var previousIds = library.CurrentSnapshot?.Entries.Select(e => e.Id).ToImmutableArray() ?? [];
        try
        {
            var result = await ViewModel.ExecuteFileMutationAsync(token => backups.RestoreAsync(preview, token));
            if (result.IsCommitted)
            {
                if (preview.Mode == LibraryRestoreMode.ReplaceLibrary)
                {
                    try { await recognition.ForgetRemovedAsync(previousIds); }
                    catch { ShowLifecycleError("书架已恢复，但旧识别队列清理未完成。请退出后重试，不要重复恢复。自动云端同步已暂停。"); }
                }
                await backups.ReloadAfterRestoreAsync();
                await Navigate(TheOrder.Presentation.WorkspacePage.Library);
            }
        }
        finally { nextMoeSettings.PauseCommands(commandsBlocked); }
    }
}
