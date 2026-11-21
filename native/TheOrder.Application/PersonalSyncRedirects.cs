using System.Collections.Immutable;
using TheOrder.Domain;

namespace TheOrder.Application;

public sealed partial class PersonalSyncCoordinator
{
    private sealed record CatalogMergeCandidate(LibraryExtrasDetails Origin, string OldId, WorkMetadata Canonical);

    public async Task<NextMoeMergeResult> RepairCollectionMergeAsync(string entryId, string oldId, string canonicalHint,
        OnlineAuthSnapshot fence, CancellationToken cancellation)
    {
        await serial.WaitAsync(cancellation).ConfigureAwait(false);
        try
        {
            auth.AuthContext.Accept(fence, () => true);
            using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellation, fence.Changed);
            var state = await CurrentAsync(stop.Token).ConfigureAwait(false);
            state = await FinishCatalogRedirectAsync(state, fence, stop.Token).ConfigureAwait(false);
            var candidate = await ReadCatalogMergeAsync(entryId, oldId, canonicalHint, stop.Token).ConfigureAwait(false);
            if (await AnotherWorkUsesMergeTargetAsync(candidate, stop.Token).ConfigureAwait(false))
                return new(null, candidate.Canonical.Id, $"多部书架作品对应 #{candidate.Canonical.Id}，请选择对应作品。");
            var result = await CommitCatalogMergeAsync(candidate, state, fence, stop.Token).ConfigureAwait(false);
            return new(result.Error is null ? candidate.Canonical.Id : null, candidate.Canonical.Id, result.Error);
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested && fence.Changed.IsCancellationRequested)
        {
            return new(null, canonicalHint, "NextMoe 授权已变化，请重新核对。");
        }
        catch (OnlineException e) when (!cancellation.IsCancellationRequested)
        {
            return new(null, canonicalHint, e.Message);
        }
        catch (Exception e) when (e is InvalidDataException or StaleLibraryRequestException or LibraryReadOnlyException)
        {
            return new(null, canonicalHint, e is StaleLibraryRequestException ? "作品关联已变化，请重新核对。" : e.Message);
        }
        finally { serial.Release(); }
    }

    public async Task<ClientResult> ConfirmCatalogMergeAsync(CollectionBindingPreview preview, string oldId,
        OnlineAuthSnapshot fence, CancellationToken cancellation)
    {
        await serial.WaitAsync(cancellation).ConfigureAwait(false);
        try
        {
            auth.AuthContext.Accept(fence, () => true);
            using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellation, fence.Changed);
            // The action offers this target only after the sync found an official redirect for oldId.
            var proof = await ReadCatalogMergeAsync(preview.Details.Entry.Id, oldId, preview.WorkId, stop.Token).ConfigureAwait(false);
            if (proof.Canonical.Id != preview.WorkId)
                return new(ClientOutcome.Conflict, "合并目标已变化，请重新读取预览。");

            var state = await CurrentAsync(stop.Token).ConfigureAwait(false);
            state = await FinishCatalogRedirectAsync(state, fence, stop.Token).ConfigureAwait(false);
            var result = await CommitCatalogMergeAsync(proof with { Origin = preview.Details }, state, fence, stop.Token, confirmed: true).ConfigureAwait(false);
            return result.Error is null ? new(ClientOutcome.Committed, "已更新 NextMoe 关联。")
                : new(ClientOutcome.ValidationFailure, result.Error);
        }
        catch (OnlineException e) { return new(ClientOutcome.Conflict, e.Message); }
        catch (Exception e) when (OfflineLibraryClient.Expected(e)) { return OfflineLibraryClient.Failure(e); }
        finally { serial.Release(); }
    }

    private async Task<bool> AnotherWorkUsesMergeTargetAsync(CatalogMergeCandidate candidate, CancellationToken cancellation)
    {
        var entries = await library.SnapshotAsync(cancellation).ConfigureAwait(false);
        var otherIds = entries.Where(entry => entry.Id != candidate.Origin.Entry.Id)
            .SelectMany(entry => new[] { entry.Metadata?.Id, entry.CollectionBindings?.NextMoe })
            .OfType<string>().Where(MetadataMixing.IsNextMoeWorkId).Distinct(StringComparer.Ordinal).ToArray();
        if (otherIds.Contains(candidate.OldId) || otherIds.Contains(candidate.Canonical.Id)) return true;

        foreach (var batch in otherIds.Chunk(100))
        {
            var confirmed = catalog is ICatalogSyncIdentities identities
                ? await identities.ConfirmWorkIdsAsync([.. batch], cancellation).ConfigureAwait(false) : [];
            foreach (var missing in batch.Except(confirmed, StringComparer.Ordinal))
            {
                try
                {
                    var work = await catalog.DetailsAsync(missing, true, cancellation).ConfigureAwait(false);
                    if (work.Id == candidate.Canonical.Id) return true;
                }
                catch (OnlineException e) when (e.Failure == OnlineFailure.NotFound) { }
            }
        }
        return false;
    }

    private async Task<CatalogMergeCandidate> ReadCatalogMergeAsync(string entryId, string oldId, string? canonicalHint,
        CancellationToken cancellation)
    {
        if (!CatalogWorkReference.TryParse(oldId, out var old) || old != oldId
            || canonicalHint is not null && (!CatalogWorkReference.TryParse(canonicalHint, out var hint) || hint != canonicalHint || hint == oldId))
            throw new InvalidDataException("NextMoe 返回的合并编号无效。");
        var origin = await library.ReadAssociationAsync(entryId, cancellation).ConfigureAwait(false);
        if (origin.Entry.Metadata?.Id != oldId && origin.Entry.CollectionBindings?.NextMoe != oldId)
            throw new StaleLibraryRequestException();

        // A different result from DetailsAsync can only come from following validated catalog merges.
        // A hint is accepted solely from the authenticated collection endpoint's ENTITY_MERGED response.
        var canonical = await catalog.DetailsAsync(canonicalHint ?? oldId, true, cancellation).ConfigureAwait(false);
        if (canonical.Id == oldId || !CatalogWorkReference.TryParse(canonical.Id, out var id) || id != canonical.Id)
            throw new InvalidDataException("NextMoe 暂无法确认这部作品的合并目标。");
        cancellation.ThrowIfCancellationRequested();
        return new(origin, oldId, canonical);
    }

    private async Task<(AccountSyncState State, string? Error)> CommitCatalogMergeAsync(CatalogMergeCandidate candidate,
        AccountSyncState state, OnlineAuthSnapshot fence, CancellationToken cancellation, bool confirmed = false)
    {
        if (state.Pending.Length != 0 || state.FolderCreations?.Length > 0)
            return (state, "有同步任务待恢复检查，请核对结果后重试。");

        auth.AuthContext.Accept(fence, () => true);
        if (candidate.Origin.Entry.Metadata?.Id == candidate.OldId)
        {
            state = state with { CatalogRedirect = new(candidate.Origin.Entry.Id, candidate.OldId, candidate.Canonical.Id) };
            await SaveFenced(state, fence, cancellation).ConfigureAwait(false);
        }

        string? error = null;
        try
        {
            await library.RepairAssociationAsync(candidate.Origin, candidate.OldId, candidate.Canonical, confirmed, cancellation).ConfigureAwait(false);
        }
        catch (StaleLibraryRequestException)
        {
            error = "作品关联已变化，请重新核对。";
        }
        catch (Exception e) when (e is InvalidDataException or LibraryReadOnlyException)
        {
            error = e.Message;
        }

        state = await FinishCatalogRedirectAsync(state, fence, cancellation).ConfigureAwait(false);
        return (state, error);
    }

    private async Task<AccountSyncState> FinishCatalogRedirectAsync(AccountSyncState state, OnlineAuthSnapshot fence, CancellationToken cancellation)
    {
        if (state.CatalogRedirect is not { } redirect) return state;
        var entries = await library.SnapshotAsync(cancellation).ConfigureAwait(false);
        var entry = entries.FirstOrDefault(entry => entry.Id == redirect.EntryId);
        // Only a durable, matching library association authorizes moving the cloud-sync baselines.
        if (entry?.Metadata?.Id == redirect.NewId
            && !entries.Any(other => other.Id != entry.Id && (other.Metadata?.Id == redirect.OldId || other.Metadata?.Id == redirect.NewId)))
        {
            state = MoveCatalogBaselines(state, redirect);
            readCache = null;
        }
        state = state with { CatalogRedirect = null };
        await SaveFenced(state, fence, cancellation).ConfigureAwait(false);
        return state;
    }

    private static AccountSyncState MoveCatalogBaselines(AccountSyncState state, PendingCatalogRedirect redirect)
    {
        string Replace(string id) => id == redirect.OldId ? redirect.NewId : id;
        string[] ReplaceAll(string[] ids) => ids.Contains(redirect.OldId, StringComparer.Ordinal)
            ? [.. ids.Select(Replace).Distinct(StringComparer.Ordinal)] : ids;

        var imported = state.Imported;
        if (imported.FirstOrDefault(fact => fact.WorkId == redirect.OldId) is { } previous)
        {
            var current = imported.FirstOrDefault(fact => fact.WorkId == redirect.NewId);
            var moved = current is null ? previous with { WorkId = redirect.NewId } : current with
            {
                Minutes = new[] { current.Minutes, previous.Minutes }.Max(),
                Rating = current.Rating ?? previous.Rating,
                State = current.State ?? previous.State,
                Completion = current.State is null ? previous.Completion : current.Completion,
                FetchedAt = current.FetchedAt ?? previous.FetchedAt
            };
            imported = [.. imported.Where(fact => fact.WorkId != redirect.OldId && fact.WorkId != redirect.NewId), moved];
        }

        return state with
        {
            Works = [.. state.Works.Select(checkpoint => checkpoint.EntryId == redirect.EntryId && checkpoint.WorkId == redirect.OldId
                ? checkpoint with { WorkId = redirect.NewId } : checkpoint)],
            Imported = imported,
            Folders = [.. state.Folders.Select(folder => folder with
            {
                Members = ReplaceAll(folder.Members),
                CloudBaseline = ReplaceAll(folder.CloudBaseline)
            })]
        };
    }

    private async Task<(AccountSyncState State, int Repaired, ImmutableArray<NextMoeAssociationIssue> Issues)> RepairMissingAssociationsAsync(
        (string WorkId, string Title)[] targets, HashSet<string> unavailable, ImmutableArray<LibraryEntry> entries, AccountSyncState state, OnlineAuthSnapshot fence,
        ImmutableArray<NextMoeAssociationIssue> previousIssues, bool canRepair, CancellationToken cancellation)
    {
        var issues = ImmutableArray.CreateBuilder<NextMoeAssociationIssue>();
        var candidates = new List<CatalogMergeCandidate>();
        foreach (var row in targets.Where(row => unavailable.Contains(row.WorkId)))
        {
            var matches = entries.Where(entry => entry.Metadata?.Id == row.WorkId).Take(2).ToArray();
            var affected = matches.Length == 0 ? new LibraryEntry?[] { null } : matches;
            if (canRepair && matches.Length == 1)
            {
                try
                {
                    candidates.Add(await ReadCatalogMergeAsync(matches[0].Id, row.WorkId, null, cancellation).ConfigureAwait(false));
                    continue;
                }
                catch (Exception e) when (e is OnlineException or InvalidDataException or StaleLibraryRequestException)
                {
                    issues.Add(new(matches[0].Id, matches[0].Name, row.WorkId, null,
                        e is StaleLibraryRequestException ? "作品关联已变化，请重新核对。" : e.Message));
                    continue;
                }
            }

            foreach (var entry in affected)
            {
                var prior = previousIssues.FirstOrDefault(issue => issue.EntryId == entry?.Id && issue.WorkId == row.WorkId);
                issues.Add(prior ?? new(entry?.Id, entry?.Name ?? row.Title, row.WorkId, null,
                    matches.Length > 1 ? "多部书架作品使用同一个 NextMoe 编号。" : "NextMoe 暂无法确认作品关联。"));
            }
        }

        var count = 0;
        foreach (var group in candidates.GroupBy(item => item.Canonical.Id, StringComparer.Ordinal))
            foreach (var candidate in group)
            {
                string? error;
                if (group.Count() > 1)
                    error = $"多部书架作品合并到 #{group.Key}，请选择对应作品。";
                else
                    (state, error) = await CommitCatalogMergeAsync(candidate, state, fence, cancellation).ConfigureAwait(false);

                var entry = candidate.Origin.Entry;
                if (error is null) count++;
                else issues.Add(new(entry.Id, entry.Name, candidate.OldId, candidate.Canonical.Id, error));
            }

        return (state, count, issues.ToImmutable());
    }

    private static string AssociationMessage(ImmutableArray<NextMoeAssociationIssue> issues)
    {
        var titles = string.Join("、", issues.Take(3).Select(issue => $"《{issue.Title}》"));
        if (issues.Length > 3) titles += "等";
        return $"已跳过 {issues.Length} 部关联暂无法确认的作品（{titles}）。请核对 NextMoe 关联。";
    }
}
