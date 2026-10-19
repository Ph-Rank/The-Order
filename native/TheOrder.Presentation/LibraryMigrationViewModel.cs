using System.Collections.Immutable;
using TheOrder.Application;
using TheOrder.Application.Migrations;

namespace TheOrder.Presentation;

public sealed record LibraryMigrationRow(string Name, string Action, string Detail);
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
        ? $"备份共 {p.Dataset.RawCount} 部 · 新增 {p.Added} · 补全 {p.Filled} · 待核对 {p.Review} · 已导入 {p.AlreadyImported}"
        : "";
    public Task PreviewAsync(string selectedFile)
    {
        if (!CanSelect) return pending;
        Clear(); Message = "正在读取备份并核对书架…";
        return pending = Run(async (token, revision) =>
        {
            var preview = await service.PreviewAsync(selectedFile, token);
            token.ThrowIfCancellationRequested(); if (revision != generation || stopping) return;
            Preview = preview;
            var games = preview.Dataset.Games.GroupBy(g => ImportHash.Key(preview.Dataset.Manager, g.SourceId)).ToDictionary(g => g.Key, g => g.First());
            Rows = [.. preview.Rows.Select(r => new LibraryMigrationRow(games[r.RecordKey].Name, Action(r), Detail(r)))];
            Message = preview.Dataset.RawCount == 0 ? "备份中的书架为空。"
                : !preview.CanApply ? "没有可直接导入的条目。已导入或需要核对的条目均不会重复写入。"
                : $"将新增 {preview.Added} 部，补全 {preview.Filled} 部；其余 {preview.Skipped} 部跳过。";
            if (!preview.Dataset.Issues.IsEmpty || preview.Dataset.Games.Any(g => !g.Issues.IsEmpty))
                Message += " 部分字段无法可靠转换，已保留现有资料并跳过这些字段。";
        });
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
    private async Task Run(Func<CancellationToken, long, Task> action, bool committing = false)
    {
        IsBusy = true; cancellation = new(); var revision = ++generation; Notify();
        try { await action(cancellation.Token, revision); }
        catch (OperationCanceledException) { Message = "已取消，书架未改动。"; }
        catch (Exception)
        {
            Message = committing ? "导入结果暂未确认，请查看书架与导入前备份，勿反复提交。"
                : "无法读取此备份。请选择 PotatoVN 导出的 ZIP，检查文件是否完整；当前书架未改动。";
            Preview = null; Rows = [];
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
    { ImportAction.Create => "新增", ImportAction.FillMissing => "补全缺项", ImportAction.AlreadyImported => "已导入 · 跳过", _ => row.Match == ImportMatch.Conflict ? "存在冲突 · 跳过" : "待核对 · 跳过" };
    private static string Detail(ImportRow row) => row.Reasons.Any(r => r is "NOTES_CONFLICT" or "RATING_CONFLICT" or "PLAYTIME_OVERLAP_CONFLICT")
        ? "笔记、评分或时长与书架不同，保留现有记录。"
        : row.Reasons.Contains("SOURCE_RECORD_CHANGED_OR_DUPLICATED") ? "来源记录发生变化或重复，保留现有记录。"
        : row.Action == ImportAction.Create ? "保留来源身份与历史时长，不自动绑定在线作品。"
        : row.Action == ImportAction.FillMissing ? "作品身份和安装位置相符，只补全缺项。"
        : row.Action == ImportAction.AlreadyImported ? "同一来源记录，不重复添加或累计时长。"
        : "作品、版本或安装位置需要核对，本次不会自动合并。";
}
