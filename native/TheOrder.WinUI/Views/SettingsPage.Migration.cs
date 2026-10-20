using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TheOrder.Application;
using TheOrder.Application.Migrations;
using TheOrder.Domain;
using TheOrder.Presentation;

namespace TheOrder.WinUI.Views;

public sealed partial class SettingsPage
{
    public Func<Task<string?>>? ChooseMigrationFile { get; set; }
    public Func<LibraryMigrationPreview, CancellationToken, Task<ClientResult>>? ImportMigration { get; set; }
    private ContentDialog? migrationDialog;
    private int migrationGeneration;
    private bool migrationFlowActive;
    private string? migrationReviewKey;
    private void OnMigrationState(object? sender, PropertyChangedEventArgs e) => SynchronizeMigration();
    private void SynchronizeMigration()
    {
        MigrationSelect.IsEnabled = !migrationFlowActive && !managerExportActive && Migration.CanSelect && ViewModel.CanEdit;
        ManagerExportButton.IsEnabled = !migrationFlowActive && !managerExportActive && Migration.CanSelect && ViewModel.CanEdit;
        MigrationProgress.Visibility = Visible(Migration.IsBusy);
        MigrationCancel.Visibility = Visible(Migration.IsBusy);
    }
    internal void CancelMigrationDialog()
    { migrationGeneration++; migrationDialog?.Hide(); Migration.Cancel(); managerExportCancellation?.Cancel(); managerExportDialog?.Hide(); }
    private void OnCancelMigration(object sender, RoutedEventArgs e) => Migration.Cancel();
    private void OnReviewMigrationRow(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string key } && Migration.CanSelect
            && Migration.Rows.Any(r => r.RecordKey == key && r.CanReview))
        { migrationReviewKey = key; migrationDialog?.Hide(); }
    }
    private async void OnImportManager(object sender, RoutedEventArgs e)
    {
        if (migrationFlowActive || managerExportActive || !Migration.CanSelect || !ViewModel.CanEdit) return;
        migrationFlowActive = true; SynchronizeMigration();
        var revision = ++migrationGeneration;
        try
        {
            if (PrepareBackupAction is { } prepare && !await prepare()) return;
            var path = ChooseMigrationFile is { } choose ? await choose() : null;
            if (path is null || revision != migrationGeneration || !IsLoaded) return;
            await Migration.PreviewAsync(path);
            while (revision == migrationGeneration && IsLoaded && Migration.Preview is { } preview)
            {
                migrationReviewKey = null;
                var content = new Grid { MaxWidth = 560, RowSpacing = 12 };
                content.RowDefinitions.Add(new() { Height = GridLength.Auto });
                content.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
                content.RowDefinitions.Add(new() { Height = GridLength.Auto });
                content.Children.Add(new TextBlock { Text = Migration.Summary, TextWrapping = TextWrapping.Wrap });
                // Virtualized rows keep large exports responsive. Review opens only after this dialog closes.
                var rows = new ListView { ItemsSource = Migration.Rows, ItemTemplate = (DataTemplate)Resources["MigrationRowTemplate"],
                    SelectionMode = ListViewSelectionMode.None, MaxHeight = 340, MinHeight = 80 };
                Grid.SetRow(rows, 1); content.Children.Add(rows);
                var notice = new TextBlock { Text = Migration.Message
                    + "\n\n待核验条目可逐条确认，未确认的暂不导入。进度取较高状态；笔记、评分保留本地非空值。导入前自动备份，不迁移游戏文件或账号。", TextWrapping = TextWrapping.Wrap };
                Grid.SetRow(notice, 2); content.Children.Add(notice);
                var dialog = new ContentDialog { XamlRoot = XamlRoot, RequestedTheme = ActualTheme, Title = "导入 " + MigrationFormats.Display(preview.Dataset.Manager) + " 书架",
                    Content = content, PrimaryButtonText = "备份并导入", IsPrimaryButtonEnabled = preview.CanApply,
                    CloseButtonText = "取消", DefaultButton = ContentDialogButton.Close };
                migrationDialog = dialog;
                var result = await dialog.ShowAsync();
                migrationDialog = null;
                if (revision != migrationGeneration || !IsLoaded) break;
                if (migrationReviewKey is { } key)
                {
                    var choice = await ShowMigrationReviewAsync(preview, key);
                    if (revision != migrationGeneration || !IsLoaded) break;
                    if (choice is not null) await Migration.ReviewAsync(choice);
                    continue;
                }
                if (result == ContentDialogResult.Primary && ImportMigration is { } apply) await Migration.ApplyAsync(apply);
                else Migration.Clear();
                break;
            }
        }
        catch { ViewModel.ReportUnexpected(); }
        finally { migrationDialog = null; migrationReviewKey = null; migrationFlowActive = false; SynchronizeMigration(); }
    }
    private async Task<ImportReviewDecision?> ShowMigrationReviewAsync(LibraryMigrationPreview preview, string key)
    {
        var row = preview.Rows.Single(r => r.RecordKey == key);
        var source = preview.Dataset.Games.Single(g => ImportHash.Key(preview.Dataset.Manager, g.SourceId) == key);
        if (row.ReviewCandidates.IsEmpty) return null;
        var previous = preview.Reviews.SingleOrDefault(r => r.RecordKey == key);
        var panel = new StackPanel { Spacing = 12, MaxWidth = 560 };
        panel.Children.Add(Text(MigrationReview.Explain(row.Reasons)));
        panel.Children.Add(Text(MigrationFormats.Display(preview.Dataset.Manager) + " · " + source.Name));
        panel.Children.Add(Text($"{StatusNames.Display(source.Status)} · 累计 {Duration(source.TotalSeconds)}"));
        var targets = new ComboBox { Header = "对应当前书架", HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemsSource = row.ReviewCandidates.Select(c => c.Name).ToArray() };
        panel.Children.Add(targets);
        var targetInfo = Text(""); panel.Children.Add(targetInfo);
        var history = new ComboBox { Header = "累计时长", HorizontalAlignment = HorizontalAlignment.Stretch };
        history.Items.Add(new ComboBoxItem { Content = "相加两边时长" });
        history.Items.Add(new ComboBoxItem { Content = "保留本地时长" });
        history.Items.Add(new ComboBoxItem { Content = "采用来源累计值（不相加）" });
        panel.Children.Add(history);
        var timeNote = Text(""); panel.Children.Add(timeNote);
        var evidence = Text("");
        panel.Children.Add(new Expander { Header = "编号、安装位置与差异", HorizontalAlignment = HorizontalAlignment.Stretch, Content = evidence });
        var confirm = new CheckBox { Content = "我已核对作品与版本，确认对应此条目", IsChecked = previous is not null };
        panel.Children.Add(confirm);
        var dialog = new ContentDialog { XamlRoot = XamlRoot, RequestedTheme = ActualTheme, Title = "核验导入条目",
            Content = new ScrollViewer { Content = panel, MaxHeight = 440, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled },
            PrimaryButtonText = "确认对应", IsPrimaryButtonEnabled = confirm.IsChecked == true,
            SecondaryButtonText = row.HumanReviewed ? "撤销本条核验" : "", CloseButtonText = "返回预览", DefaultButton = ContentDialogButton.Close };
        confirm.Checked += (_, _) => dialog.IsPrimaryButtonEnabled = true;
        confirm.Unchecked += (_, _) => dialog.IsPrimaryButtonEnabled = false;
        targets.SelectionChanged += (_, _) =>
        {
            if (targets.SelectedIndex < 0) return;
            var c = row.ReviewCandidates[targets.SelectedIndex];
            targetInfo.Text = $"本地 · {StatusNames.Display(c.Status)} · 累计 {Duration(c.TotalSeconds)}\n导入后进度：{StatusNames.Display(GameProgress.Higher(c.Status, source.Status))}"
                + (c.NotesConflict || c.RatingConflict ? "\n笔记或评分有差异，保留本地值。" : "");
            ((ComboBoxItem)history.Items[0]).IsEnabled = c.CanAddSourceTotal;
            ((ComboBoxItem)history.Items[2]).IsEnabled = c.CanUseSourceTotal;
            history.SelectedIndex = previous?.TargetKey == c.TargetKey
                ? previous.History switch { ImportHistoryChoice.AddSourceTotal when c.CanAddSourceTotal => 0, ImportHistoryChoice.UseSourceTotal when c.CanUseSourceTotal => 2, _ => 1 }
                : c.TotalSeconds == 0 && c.CanUseSourceTotal ? 2 : c.CanAddSourceTotal ? 0 : 1;
            timeNote.Text = $"相加时本次新增累计 {Duration(c.AdditionalSourceSeconds)}，可补入有日期的记录 {Duration(c.NewlyDatedSeconds)}。保留可靠的逐日记录和会话，不推算日期或启动次数。"
                + (c.DuplicateSessionSeconds > 0 ? $"已扣除完全相同的会话 {Duration(c.DuplicateSessionSeconds)}。" : "")
                + "两边若包含其他重复历史，请保留其中一边。" + MigrationReview.ExplainHistoryWarnings(c.HistoryWarnings)
                + (c.CanUseSourceTotal ? "" : " 来源累计值少于已知会话或修正记录，不能覆盖。");
            evidence.Text = "来源编号：" + Ids(source.Identities) + "\n本地编号：" + Ids(c.Identities)
                + "\n\n来源安装位置：\n" + Paths(source.Installations.Select(i => i.Folder))
                + "\n本地安装位置：\n" + Paths(c.Folders) + (c.InstallationCount > c.Folders.Length ? $"\n另有 {c.InstallationCount - c.Folders.Length} 个安装。" : "")
                + (c.ExactInstallation ? "\n安装位置相同。" : "\n安装位置存在差异，本次不修改本地安装。")
                + (source.OriginalName.Length > 0 ? "\n来源原名：" + source.OriginalName : "")
                + (source.Edition is not null ? "\n来源版本：" + source.Edition : "")
                + (c.CatalogName.Length > 0 ? "\n本地资料名：" + c.CatalogName : "")
                + "\n\n确认只用于本条迁移，不改写在线作品对应。";
            if (previous?.TargetKey != c.TargetKey) confirm.IsChecked = false;
        };
        targets.SelectedIndex = Math.Max(0, Array.FindIndex(row.ReviewCandidates.ToArray(), c => c.TargetKey == previous?.TargetKey));
        migrationDialog = dialog;
        var result = await dialog.ShowAsync(); migrationDialog = null;
        if (result == ContentDialogResult.Secondary && row.HumanReviewed) return new(key, null);
        return result == ContentDialogResult.Primary && confirm.IsChecked == true && targets.SelectedIndex >= 0
            ? new(key, row.ReviewCandidates[targets.SelectedIndex].TargetKey, true,
                history.SelectedIndex switch { 0 => ImportHistoryChoice.AddSourceTotal, 2 => ImportHistoryChoice.UseSourceTotal, _ => ImportHistoryChoice.KeepExisting }) : null;
    }
    private static TextBlock Text(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
    private static string Duration(long seconds) => $"{seconds / 3600} 小时 {seconds % 3600 / 60} 分 {seconds % 60} 秒";
    private static string Ids(IEnumerable<ImportedIdentity> ids) => string.Join(" · ", ids.Select(i => i.Provider + " " + i.Id)) is { Length: > 0 } value ? value : "未提供可核对编号";
    private static string Paths(IEnumerable<string> paths) => string.Join("\n", paths.Take(6)) is { Length: > 0 } value ? value : "未提供";
}
