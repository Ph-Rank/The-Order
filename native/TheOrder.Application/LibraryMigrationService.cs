using System.Collections.Immutable;
using TheOrder.Application.Migrations;

namespace TheOrder.Application;

// Consent belongs to the already-open writer and its exact snapshot, never an arbitrary target path.
public sealed class LibraryMigrationPreview
{
    internal LibraryMigrationPreview(Guid owner, LibrarySnapshot baseline, MigrationPlan plan)
    { Owner = owner; Baseline = baseline; Plan = plan; }
    internal Guid Owner { get; }
    internal LibrarySnapshot Baseline { get; }
    internal MigrationPlan Plan { get; }
    public ImportDataset Dataset => Plan.Dataset;
    public ImmutableArray<ImportRow> Rows => Plan.Rows;
    public int Added => Rows.Count(r => r.Action == ImportAction.Create);
    public int Filled => Rows.Count(r => r.Action == ImportAction.FillMissing);
    public int Review => Rows.Count(r => r.Match is ImportMatch.Candidate or ImportMatch.Conflict);
    public int AlreadyImported => Rows.Count(r => r.Action == ImportAction.AlreadyImported);
    public int Skipped => Dataset.RawCount - Added - Filled;
    public bool CanApply => Added + Filled > 0;
}
public interface ILibraryMigrationSource
{
    Task<ImportDataset> ReadAsync(string selectedFile, CancellationToken cancellation);
}
public interface ILibraryMigrationService
{
    Task<LibraryMigrationPreview> PreviewAsync(string selectedFile, CancellationToken cancellation);
    Task<ClientResult> ApplyAsync(LibraryMigrationPreview preview, CancellationToken cancellation);
}
public sealed class LibraryMigrationService(OfflineLibraryClient library, ILibraryMigrationSource source, ILibraryBackupStore backups)
    : ILibraryMigrationService
{
    public async Task<LibraryMigrationPreview> PreviewAsync(string selectedFile, CancellationToken cancellation)
        => await library.Required().PreviewActiveMigrationAsync(await source.ReadAsync(selectedFile, cancellation).ConfigureAwait(false), cancellation).ConfigureAwait(false);
    public async Task<ClientResult> ApplyAsync(LibraryMigrationPreview preview, CancellationToken cancellation)
    {
        try
        {
            var result = await library.Required().ApplyActiveMigrationAsync(preview, async (bytes, count, token) =>
            {
                var saved = await backups.SaveSnapshotAsync(bytes, count, "导入前", token).ConfigureAwait(false);
                var restored = await backups.ReadSnapshotAsync(saved.Record.Id, token).ConfigureAwait(false);
                if (!bytes.Span.SequenceEqual(restored)) throw new InvalidDataException("IMPORT_BACKUP_VERIFICATION_FAILED");
            }, cancellation).ConfigureAwait(false);
            var message = $"已导入 {result.Value.Created} 部，补全 {result.Value.Filled} 部；跳过 {result.Value.Skipped} 部。"
                + (result.Value.Created + result.Value.Filled > 0 ? "导入前备份已保存。" : "书架没有变化。" );
            if (result.Warning is not null || result.CancellationAfterCommit)
                return new(ClientOutcome.CommittedWithWarning, message + " 修改已保存，请勿重复提交；可在备份与恢复中检查。" );
            return new(ClientOutcome.Committed, message);
        }
        catch (StaleLibraryRequestException) { return new(ClientOutcome.Conflict, "书架已变化，请重新选择备份并查看预览后导入。" ); }
        catch (SessionConflictException) { return new(ClientOutcome.ValidationFailure, "请先结束计时并处理待恢复的游玩会话，再导入。" ); }
        catch (OperationCanceledException) { return new(ClientOutcome.Cancelled, "已取消导入，书架未改动。" ); }
        catch (Exception e) when (OfflineLibraryClient.Expected(e))
        { return new(ClientOutcome.IoFailure, "导入未完成。请检查备份空间、文件占用或权限；现有书架已保留。" ); }
    }
}
