using TheOrder.Application.Migrations;

namespace TheOrder.Application;

public sealed partial class TransactionalLibrary
{
    // No public production entry point. The Windows gateway grants access only to its marked Test profiles.
    internal Task<CommitResult<MigrationCommit>> ApplyMigrationAsync(MigrationPlan plan, CancellationToken cancellation = default)
        => Mutate(games =>
        {
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
        }, cancellation, skipUnchanged: true);
}
