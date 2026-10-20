using System.Collections.Immutable;
using TheOrder.Application;
using TheOrder.Application.Migrations;

namespace TheOrder.Presentation;

public sealed record LibraryMigrationRow(string Name, string Action, string Detail)
{
    public string RecordKey { get; init; } = "";
    public bool CanReview { get; init; }
}
public sealed class LibraryMigrationViewModel(ILibraryMigrationService service) : ObservableState, IAsyncDisposable
{
    private CancellationTokenSource? cancellation;
    private Task pending = Task.CompletedTask;
    private bool stopping;
    private long generation;
    public bool IsBusy { get; private set; }
    public bool CanSelect => !IsBusy && !stopping;
    public LibraryMigrationPreview? Preview { get; private set; }
    public bool CanApply => !IsBusy && !stopping && Preview?.CanApply == true;
    public string Message { get; private set; } = "";
    public ImmutableArray<LibraryMigrationRow> Rows { get; private set; } = [];
    public string Summary => Preview is { } p
        ? $"备份共 {p.Dataset.RawCount} 部 · 新增 {p.Added} · 更新 {p.Filled} · 待核对 {p.Review} · 已导入 {p.AlreadyImported}"
            + (p.Dataset.Issues.Count(i => i.Code == "NON_GALGAME_OR_UNVERIFIED_IDENTITY_EXCLUDED") is > 0 and var excluded ? $" · 类型不符或未确认 {excluded}" : "")
        : "";
    public Task PreviewAsync(string selectedFile)
    {
        if (!CanSelect) return pending;
        Clear(); Message = "正在读取备份并核对书架…";
        return pending = Run(async (token, revision) =>
        {
            var preview = await service.PreviewAsync(selectedFile, token);
            token.ThrowIfCancellationRequested(); if (revision != generation || stopping) return;
            PublishPreview(preview);
        });
    }
    public Task ReviewAsync(ImportReviewDecision decision)
    {
        if (!CanSelect || Preview is not { } preview) return pending;
        Message = "正在更新核验结果…";
        return pending = Run(async (token, revision) =>
        {
            var reviewed = await service.ReviewAsync(preview, decision, token);
            token.ThrowIfCancellationRequested(); if (revision != generation || stopping) return;
            PublishPreview(reviewed);
        }, reviewing: true);
    }
    private void PublishPreview(LibraryMigrationPreview preview)
    {
        Preview = preview;
        var games = preview.Dataset.Games.GroupBy(g => ImportHash.Key(preview.Dataset.Manager, g.SourceId)).ToDictionary(g => g.Key, g => g.First());
        Rows = [.. preview.Rows.Select(r => new LibraryMigrationRow(games[r.RecordKey].Name, Action(r), Detail(r))
        { RecordKey = r.RecordKey, CanReview = !r.ReviewCandidates.IsEmpty && (r.HumanReviewed || r.Match is ImportMatch.Candidate or ImportMatch.Conflict
            || r.Reasons.Contains("HIGHER_PROGRESS_ONLY_OTHER_CONFLICTS_PRESERVED")) })
            .Concat(preview.Dataset.Exclusions.Select(e => new LibraryMigrationRow(e.Name, "已排除", "非 Galgame 或缺少可靠类型证据，不导入。") { RecordKey = e.RecordKey }))];
        Message = preview.Dataset.RawCount == 0 ? "备份中的书架为空。"
            : preview.Dataset.Games.IsEmpty && preview.Dataset.Issues.Any(i => i.Code == "NON_GALGAME_OR_UNVERIFIED_IDENTITY_EXCLUDED") ? "未找到可确认类型的 Galgame 条目；其他类型或缺少分类证据的条目未导入。"
            : !preview.CanApply ? Rows.Any(r => r.CanReview) ? "请选择待核验条目，核验后即可导入。" : "没有可导入的条目；已导入的记录不会重复写入。"
            : $"将新增 {preview.Added} 部，更新 {preview.Filled} 部；其余 {preview.Skipped} 部不改动。";
        if (!preview.Dataset.Issues.IsEmpty || preview.Dataset.Games.Any(g => !g.Issues.IsEmpty))
            Message += " 部分字段无法可靠转换，这些字段不会写入。";
    }
    public Task ApplyAsync(Func<LibraryMigrationPreview, CancellationToken, Task<ClientResult>>? coordinator = null)
    {
        if (!CanApply || Preview is not { } preview) return pending;
        Message = "正在备份当前书架并导入…";
        return pending = Run(async (token, _) =>
        {
            var result = await (coordinator ?? service.ApplyAsync)(preview, token);
            // A durable receipt wins over cancellation. Never label a completed write as cancelled.
            Message = result.Message; Preview = null; Rows = [];
        }, committing: true);
    }
    private async Task Run(Func<CancellationToken, long, Task> action, bool committing = false, bool reviewing = false)
    {
        IsBusy = true; cancellation = new(); var revision = ++generation; Notify();
        try { await action(cancellation.Token, revision); }
        catch (OperationCanceledException)
        { Message = "已取消，书架未改动。"; if (reviewing) { Preview = null; Rows = []; } }
        catch (StaleLibraryRequestException) when (reviewing)
        { Message = "书架已变化，请重新选择备份并核验。"; Preview = null; Rows = []; }
        catch (Exception)
        {
            Message = reviewing ? "核验未采用。请检查对应条目和时长选择；现有书架未改动。"
                : committing ? "导入结果暂未确认，请查看书架与导入前备份，勿反复提交。"
                : "无法读取此备份。请选择受支持管理器的完整备份 ZIP；ReinaManager 请先将一致性备份 .db 压缩为 ZIP。当前书架未改动。";
            if (!reviewing) { Preview = null; Rows = []; }
        }
        finally { cancellation.Dispose(); cancellation = null; IsBusy = false; Notify(); }
    }
    public void Cancel() => cancellation?.Cancel();
    public void Clear()
    {
        if (IsBusy) { Cancel(); return; }
        ++generation; Preview = null; Rows = []; Message = ""; Notify();
    }
    public Task DrainAsync() => pending;
    private void Notify()
    {
        Changed(nameof(IsBusy)); Changed(nameof(CanSelect)); Changed(nameof(CanApply)); Changed(nameof(Preview));
        Changed(nameof(Rows)); Changed(nameof(Summary)); Changed(nameof(Message));
    }
    public async ValueTask DisposeAsync() { stopping = true; Cancel(); await pending; Notify(); }
    private static string Action(ImportRow row) => row.Action switch
    { ImportAction.Create => "新增", ImportAction.FillMissing => row.HumanReviewed ? "已核验 · 合并个人记录" : row.Fields.Contains("status") ? "更新进度" : row.Fields.Contains("history-dates") ? "补全游玩日期" : "补全缺项",
        ImportAction.AlreadyImported => "已导入 · 不重复写入", _ => !row.ReviewCandidates.IsEmpty ? "待核验" : "来源冲突 · 暂不可导入" };
    private static string Detail(ImportRow row) => row.HumanReviewed
        ? "保留现有作品资料、名称、笔记和评分。" + (row.Reasons.Contains("USER_ADDED_SOURCE_TOTAL") ? "相加时保留可靠的游玩日期；同一来源的已计入部分不重复累计。" + MigrationReview.ExplainHistoryWarnings(row.HistoryWarnings) : row.Reasons.Contains("USER_ADOPTED_SOURCE_TOTAL") ? "采用来源累计时长，不与本地相加。" : "保留本地时长。")
        : row.Fields.Contains("history-dates") && !row.Fields.Contains("status") ? "补回此前相加时未保留的来源日期，累计时长不变。" + MigrationReview.ExplainHistoryWarnings(row.HistoryWarnings)
        : row.Action == ImportAction.FillMissing && row.Fields.Contains("status") && row.StatusBefore is { } before && row.StatusAfter is { } after
        ? $"进度：{StatusNames.Display(before)} → {StatusNames.Display(after)}。" + (row.Fields.Contains("history-dates") ? "同时补回来源日期，累计时长不变。" : "其他有冲突的资料保留现有记录。")
        : row.Action == ImportAction.Create ? "保留来源身份与历史时长，不自动绑定在线作品。"
        : row.Action == ImportAction.FillMissing ? "作品身份和安装位置相符，只补全缺项。"
        : row.Action == ImportAction.AlreadyImported ? "同一来源记录，不重复添加或累计时长。"
        : MigrationReview.Explain(row.Reasons);
}
