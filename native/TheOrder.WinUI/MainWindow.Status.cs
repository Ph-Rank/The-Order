using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using TheOrder.Application;
using TheOrder.Presentation;
using TheOrder.WinUI.Views;

namespace TheOrder.WinUI;

public sealed partial class MainWindow
{
    private string? lastSessionError, lastAudioError, lastStatisticsError;

    private void UpdateStatusBadge()
    {
        StatusBadge.Visibility = statusPage.HasUnreadAttention ? Visibility.Visible : Visibility.Collapsed;
        AutomationProperties.SetName(StatusNavigation, statusPage.HasUnreadAttention ? "状态，有未读提醒" : "状态");
    }

    private void ReportWorkspaceNotice()
    {
        var result = ViewModel.Notice;
        var severity = result.Outcome switch
        {
            ClientOutcome.Success or ClientOutcome.Committed => InfoBarSeverity.Success,
            ClientOutcome.CommittedWithWarning or ClientOutcome.ReadOnly or ClientOutcome.Conflict or ClientOutcome.CandidateStale => InfoBarSeverity.Warning,
            ClientOutcome.ValidationFailure or ClientOutcome.IoFailure or ClientOutcome.LoadFailure => InfoBarSeverity.Error,
            _ => InfoBarSeverity.Informational
        };
        statusPage.Post(new("书架", result.Message, severity,
            RequiresAction: result.Outcome is ClientOutcome.Conflict or ClientOutcome.CandidateStale));
    }

    private void RefreshLibraryAccessStatus() => statusPage.SetNotice("library-readonly", ViewModel.IsReadOnly
        ? new("当前书架为只读", ViewModel.CapabilityMessage, InfoBarSeverity.Warning, RequiresAction: true) : null);

    private void RefreshCatalogStatus() => statusPage.SetNotice("catalog", online.CatalogReminder is { } reminder
        ? new(reminder.Title, reminder.Message, InfoBarSeverity.Informational,
            new("前往设置", OpenStatusSourcesAsync, "CatalogAccessSettings"), RequiresAction: true) : null);

    private async Task OpenStatusSourcesAsync()
    {
        await Navigate(WorkspacePage.Settings);
        if (ViewModel.Page == WorkspacePage.Settings) settingsPage.RevealSources();
    }

    private void OnNextMoeStatus(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(NextMoeSettingsViewModel.Error) or nameof(NextMoeSettingsViewModel.HasPending)
            or nameof(NextMoeSettingsViewModel.Status))) return;
        SetNextMoeStatusNotice(nextMoeSettings.Error,
            nextMoeSettings.HasPending && nextMoeSettings.Status is { Mode: NextMoeAccessMode.Account, NeedsLogin: false });
    }

    private void SetNextMoeStatusNotice(string? error, bool recoveryRequired)
    {
        statusPage.SetNotice("nextmoe-sync", recoveryRequired
            ? new("NextMoe 同步", error ?? "有同步任务需要核对结果。", InfoBarSeverity.Warning,
                new("恢复检查", nextMoeSettings.RecoverPendingAsync, "NextMoeSyncRecovery"), RequiresAction: true)
            : error is not null ? new("NextMoe", error, InfoBarSeverity.Error,
                nextMoeSettings.AssociationIssues.Any(issue => issue.EntryId is not null)
                    ? new("查看未完成作品", openNextMoeSyncIssues, "NextMoeAssociationIssues") : null, Dismissible: true) : null);
    }

    private void OnBangumiStatus(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(BangumiSettingsViewModel.NeedsReauthorization)) return;
        statusPage.SetNotice("bangumi", bangumiSettings.NeedsReauthorization
            ? new("番组计划", bangumiSettings.Status, InfoBarSeverity.Warning,
                new("前往账号设置", OpenStatusBangumiAsync, "StatusOpenBangumi"), RequiresAction: true)
            : null);
    }

    private async Task OpenStatusBangumiAsync()
    {
        await Navigate(WorkspacePage.Settings);
        if (ViewModel.Page == WorkspacePage.Settings) settingsPage.RevealBangumi();
    }

    private void RefreshSessionStatus()
    {
        var sessionError = Sessions.Error;
        if (sessionError != lastSessionError)
        {
            lastSessionError = sessionError;
            if (sessionError is not null) statusPage.Post(new("游玩计时", sessionError, InfoBarSeverity.Error));
        }
        var audioError = backgroundAudio.Failure;
        if (audioError != lastAudioError)
        {
            lastAudioError = audioError;
            if (audioError is not null) statusPage.Post(new("后台静音", audioError, InfoBarSeverity.Error));
        }
    }

    private void RefreshRecognitionStatus()
    {
        statusPage.SetNotice("recognition-storage", recognition.PersistenceError is { } error
            ? new("识别记录", error, InfoBarSeverity.Error) : null);

        var progress = recognition.Progress;
        if (recognition.HasRecognitionWork)
        {
            double? percent = progress.Total > 0 ? 100d * progress.Processed / progress.Total : null;
            statusPage.SetTask("recognition", "作品识别", recognition.ProgressText, percent,
                new("取消识别", CancelStatusRecognitionAsync, "CancelStatusRecognition"));
        }
        else if (statusPage.HasTask("recognition"))
        {
            statusPage.ClearTask("recognition");
            if (p1Stopping || commandsBlocked || progress.Total == 0) return;
            bool requiresAction = progress.NeedsReview > 0 || progress.Stale > 0;
            var severity = progress.Failed > 0 ? InfoBarSeverity.Error
                : requiresAction ? InfoBarSeverity.Warning
                : progress.Cancelled > 0 ? InfoBarSeverity.Informational : InfoBarSeverity.Success;
            statusPage.Post(new("作品识别", recognition.ProgressText, severity, recognition.Reviews.Count > 0
                ? new("查看待核对", OpenPendingReviewAsync, "StatusPendingReview") : null, RequiresAction: requiresAction));
        }
    }

    private Task CancelStatusRecognitionAsync()
    {
        recognition.CancelAll();
        return Task.CompletedTask;
    }

    private void OnFilesStatus(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(FilesViewModel.ScanMessage)) return;
        if (files.IsScanning)
            statusPage.SetTask("import-scan", "扫描游戏文件夹", files.ScanMessage,
                cancel: new("取消扫描", CancelStatusScanAsync, "CancelStatusScan"));
        else if (statusPage.HasTask("import-scan"))
        {
            statusPage.ClearTask("import-scan");
            if (files.Stage == ImportStage.Failed)
                statusPage.Post(new("扫描游戏文件夹", files.ScanMessage, InfoBarSeverity.Error));
        }
    }

    private Task CancelStatusScanAsync()
    {
        files.CancelScan();
        return Task.CompletedTask;
    }

    private void OnStatisticsStatus(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(StatisticsViewModel.Error)) return;
        var error = statisticsPage.ViewModel.Error;
        if (error == lastStatisticsError) return;
        lastStatisticsError = error;
        if (error is not null) statusPage.Post(new("游玩统计", error, InfoBarSeverity.Error));
    }
}
