using TheOrder.Application.Migrations;

namespace TheOrder.Application;

public sealed partial class TransactionalLibrary
{
    internal Task<LibraryMigrationPreview> PreviewActiveMigrationAsync(ImportDataset dataset, CancellationToken cancellation = default)
        => Enqueue(() =>
        {
            if (current.Access != LibraryAccess.ValidWritable) throw new LibraryReadOnlyException(current.ReadOnlyReason!);
            var plan = MigrationResolver.Preview(dataset, current.Entries, codec, cancellation, ImportHash.Text(epoch.ToString()));
            return Task.FromResult(new LibraryMigrationPreview(epoch, current, plan));
        }, cancellation);
    internal Task<CommitResult<MigrationCommit>> ApplyActiveMigrationAsync(LibraryMigrationPreview preview,
        Func<ReadOnlyMemory<byte>, int, CancellationToken, Task> backup, CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(preview); ArgumentNullException.ThrowIfNull(backup);
        byte[] rollback = []; int count = 0;
        return ApplyMigrationCoreAsync(preview.Plan, cancellation, () =>
        {
            if (preview.Owner != epoch || !ReferenceEquals(preview.Baseline, current)) throw new StaleLibraryRequestException();
            rollback = codec.Encode([.. current.Entries.Select(PortableSessionHistory)], now()); count = current.Entries.Length;
        }, () => backup(rollback, count, cancellation));
    }
    // CLI remains restricted by its marked Test-only Windows gateway.
    internal Task<CommitResult<MigrationCommit>> ApplyMigrationAsync(MigrationPlan plan, CancellationToken cancellation = default)
        => ApplyMigrationCoreAsync(plan, cancellation);
    private Task<CommitResult<MigrationCommit>> ApplyMigrationCoreAsync(MigrationPlan plan, CancellationToken cancellation,
        Action? verifyConsent = null, Func<Task>? backup = null)
        => Mutate(games =>
        {
            verifyConsent?.Invoke();
            ImportModel.Validate(plan.Dataset);
            if (ImportHash.Snapshot(games, codec) != plan.TargetDigest) throw new StaleLibraryRequestException();
            if (pendingOwnedSessions.Count != 0 || games.Any(e => e.Sessions.Any(s => s.Native is { IsTerminal: false })))
                throw new SessionConflictException("Migration cannot modify a library with live or pending sessions.");
            if (plan.Operations.IsEmpty) return (games, new MigrationCommit(0, 0, plan.Rows.Length, plan.Id));
            var result = games.ToBuilder(); int created = 0, filled = 0;
            foreach (var op in plan.Operations)
            {
                cancellation.ThrowIfCancellationRequested();
                if (op.TargetId is null) { result.Add(MigrationProjection.Apply(op, plan.Dataset, null, now())); created++; }
                else
                {
                    var index = -1; for (var i = 0; i < result.Count; i++) if (result[i].Id == op.TargetId) { index = i; break; }
                    if (index < 0) throw new StaleLibraryRequestException();
                    result[index] = MigrationProjection.Apply(op, plan.Dataset, result[index], now()); filled++;
                }
            }
            return (result.ToImmutable(), new MigrationCommit(created, filled, plan.Rows.Length - created - filled, plan.Id));
        }, cancellation, skipUnchanged: true, verifyBeforeCommit: backup);
}
