using System.Collections.Immutable;
using TheOrder.Domain;

namespace TheOrder.Application;

// All remote writes pass a validated plan and durable journal.
public sealed partial class PersonalSyncCoordinator(INextMoeAccess auth, INextMoePersonalApi remote, IPersonalSyncStore store,
    IPersonalSyncLibrary library, IOnlineCatalog catalog, TimeProvider? clock = null)
{
    private readonly SemaphoreSlim serial = new(1, 1);
    private PersonalSyncPreview? active;
    private RemoteSnapshot? activeRemote;
    private AccountSyncState? activeState;
    private DateTimeOffset Now => (clock ?? TimeProvider.System).GetUtcNow();
    private sealed record RemoteSnapshot(ImmutableArray<CloudPlaytime> Times, ImmutableArray<CloudWorkState> States,
        ImmutableArray<CloudRating> Ratings, ImmutableArray<CloudFolder> Folders, ImmutableDictionary<string, ImmutableArray<string>> Members,
        string? TimeCursor, string? StateCursor, string? RatingCursor, ImmutableDictionary<string, string?> FolderCursors);
    public async Task<AccountSyncState> CurrentAsync(CancellationToken cancellation)
    {
        var fence = await auth.AuthContext.CaptureAsync(cancellation).ConfigureAwait(false);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellation, fence.Changed);
        var status = await auth.StatusAsync(stop.Token).ConfigureAwait(false);
        if (status.Mode != NextMoeAccessMode.Account || status.Account is null) throw new OnlineException(OnlineFailure.UserIdentityRequired);
        var state = await store.LoadAsync(status.Account.Id, NextMoeOAuthIdentity.ClientId, stop.Token).ConfigureAwait(false);
        state ??= new(status.Account.Id, NextMoeOAuthIdentity.ClientId, AuthorizedSettings(status), [], [], [], []);
        return auth.AuthContext.Accept(fence, () => { stop.Token.ThrowIfCancellationRequested(); return state; });
    }
    public Task<PersonalSyncPreview> PreviewAsync(PersonalSyncSettings settings, CancellationToken cancellation)
        => PreviewCore(settings, false, cancellation);
    private async Task<PersonalSyncPreview> PreviewCore(PersonalSyncSettings settings, bool incremental, CancellationToken cancellation)
    {
        await serial.WaitAsync(cancellation).ConfigureAwait(false);
        try
        {
            if (!Enum.IsDefined(settings.Playtime) || !Enum.IsDefined(settings.State) || !Enum.IsDefined(settings.Folders)) throw new OnlineException(OnlineFailure.InvalidRequest);
            var fence = await auth.AuthContext.CaptureAsync(cancellation).ConfigureAwait(false);
            using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellation, fence.Changed);
            var state = await CurrentAsync(stop.Token).ConfigureAwait(false);
            state = await FinishCatalogRedirectAsync(state, fence, stop.Token).ConfigureAwait(false);
            var local = await library.SnapshotAsync(stop.Token).ConfigureAwait(false);
            var cloud = await ReadRemote(settings, state, stop.Token, incremental).ConfigureAwait(false);
            var rows = ImmutableArray.CreateBuilder<PersonalSyncRow>();
            var duplicates = local.Where(MetadataMixing.HasNextMoeIdentity).GroupBy(x => x.Metadata!.Id).Where(x => x.Count() > 1).Select(x => x.Key).ToHashSet(StringComparer.Ordinal);
            foreach (var entry in local.Where(x => x.Metadata is not null))
            {
                if (!MetadataMixing.HasNextMoeIdentity(entry))
                {
                    if (settings.Playtime != PersonalSyncDirection.Off || settings.State != PersonalSyncDirection.Off || settings.ReadRatings || settings.Folders != PersonalSyncDirection.Off)
                        rows.Add(new("direct-source:" + entry.Id, PersonalSyncKind.State, entry.Metadata!.Id, entry.Id, entry.Name, "独立资料源", "未关联 NextMoe", true,
                            "先关联 NextMoe 目录中的同一作品，再同步个人记录；现有资料和记录保留。", null, null));
                    continue;
                }
                var work = entry.Metadata!.Id; var basis = state.Works.SingleOrDefault(x => x.EntryId == entry.Id && x.WorkId == work);
                var imported = state.Imported.SingleOrDefault(x => x.WorkId == work);
                if (duplicates.Contains(work))
                { rows.Add(new("identity:" + entry.Id, PersonalSyncKind.State, work, entry.Id, entry.Name, "重复关联", "需要核对", true,
                    "本地有多个条目关联同一作品，请先合并或明确处理；不会自动上传。", null, null)); continue; }
                if (settings.Playtime != PersonalSyncDirection.Off)
                    rows.Add(PersonalSyncPlanning.Time(entry, cloud.Times.SingleOrDefault(x => x.WorkId == work)?.Minutes, basis?.AgreedMinutes is null ? null : basis, imported, settings.Playtime));
                if (settings.State != PersonalSyncDirection.Off)
                    rows.Add(PersonalSyncPlanning.State(entry, cloud.States.SingleOrDefault(x => x.WorkId == work), basis?.CloudState is null ? null : basis, settings.State));
                if (settings.ReadRatings && cloud.Ratings.SingleOrDefault(x => x.WorkId == work) is { } rating)
                    rows.Add(new("rating:" + entry.Id, PersonalSyncKind.Rating, work, entry.Id, entry.Name,
                        entry.PersonalRating?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "未评分", rating.Overall.ToString(), false,
                        "云端整数评分保存为独立来源；本地 0 分或小数评分保持原值，评分不会上传。", null,
                        new(work, Rating: rating.Overall, FetchedAt: rating.UpdatedAt)));
            }
            if (settings.Folders != PersonalSyncDirection.Off)
            {
                var linked = local.Where(x => MetadataMixing.HasNextMoeIdentity(x) && !duplicates.Contains(x.Metadata!.Id)).ToDictionary(x => x.Metadata!.Id, x => x, StringComparer.Ordinal);
                foreach (var mapping in state.Folders)
                {
                    if (!cloud.Members.TryGetValue(mapping.FolderId, out var members))
                    { rows.Add(new("missing-folder:" + mapping.FolderId, PersonalSyncKind.Folder, "0", null, mapping.Name, "已设置本地映射", "云端收藏夹不存在", true,
                        "原收藏夹已删除或不可访问，请重新选择收藏夹。不会删除本地成员。", null, null, FolderId: mapping.FolderId)); continue; }
                    var localSet = mapping.Members.ToHashSet(StringComparer.Ordinal); var cloudSet = members.ToHashSet(StringComparer.Ordinal);
                    var baseline = mapping.CloudBaseline.ToHashSet(StringComparer.Ordinal);
                    foreach (var work in localSet.Union(cloudSet).Order(StringComparer.Ordinal))
                    {
                        bool localHas = localSet.Contains(work), cloudHas = cloudSet.Contains(work), existed = baseline.Contains(work);
                        if (localHas == cloudHas) continue;
                        linked.TryGetValue(work, out var entry);
                        bool deletion = existed && (!localHas || !cloudHas);
                        rows.Add(new("folder:" + mapping.FolderId + ":" + work, PersonalSyncKind.Folder, work, entry?.Id,
                            entry?.Name ?? "目录作品 " + work, localHas ? "在本地同步收藏列表中" : "不在本地同步收藏列表中",
                            cloudHas ? "在「" + mapping.Name + "」中" : "不在云端收藏夹中", deletion,
                            deletion ? "检测到移除；不会自动传播删除。可选择保留成员，移除云端成员需单独明确确认。" : "独立收藏列表映射，不改安装目录、个人分类或标签。",
                            !cloudHas && localHas ? new(work) : null,
                            cloudHas || deletion && localHas ? new(work, FetchedAt: Now.ToString("o")) : null, FolderId: mapping.FolderId,
                            DeleteCloud: !localHas && cloudHas && existed, DeleteLocalMembership: !cloudHas && localHas && existed));
                    }
                }
            }
            var preview = new PersonalSyncPreview(Guid.NewGuid(), fence, state.AccountId, settings, local, rows.ToImmutable(), cloud.Folders,
                local.Length, cloud.Times.Length + cloud.States.Length + cloud.Ratings.Length + cloud.Members.Values.Sum(x => x.Length), Now);
            auth.AuthContext.Accept(fence, () => { active = preview; activeRemote = cloud; activeState = state; return true; });
            return preview;
        }
        finally { serial.Release(); }
    }
    private async Task<RemoteSnapshot> ReadRemote(PersonalSyncSettings settings, AccountSyncState state, CancellationToken cancellation, bool incremental = false)
    {
        var cached = incremental && readCache is { } saved && saved.Account == state.AccountId && saved.Generation == auth.AuthContext.Generation
            && saved.Settings == settings && Now - saved.FullRead < TimeSpan.FromMinutes(30) ? saved : null;
        var times = settings.Playtime == PersonalSyncDirection.Off ? (Items: ImmutableArray<CloudPlaytime>.Empty, Cursor: (string?)null) : await All(remote.PlaytimesAsync, 20000, cancellation, cached?.Snapshot.TimeCursor).ConfigureAwait(false);
        var states = settings.State == PersonalSyncDirection.Off ? (Items: ImmutableArray<CloudWorkState>.Empty, Cursor: (string?)null) : await All(remote.StatesAsync, 20000, cancellation, cached?.Snapshot.StateCursor).ConfigureAwait(false);
        var ratings = !settings.ReadRatings ? (Items: ImmutableArray<CloudRating>.Empty, Cursor: (string?)null) : await All(remote.RatingsAsync, 20000, cancellation, cached?.Snapshot.RatingCursor).ConfigureAwait(false);
        var folders = settings.Folders == PersonalSyncDirection.Off ? (Items: ImmutableArray<CloudFolder>.Empty, Cursor: (string?)null) : await All(remote.FoldersAsync, 200, cancellation).ConfigureAwait(false);
        if (folders.Items.Any(x => x.OwnerId != state.AccountId)) throw new OnlineException(OnlineFailure.InvalidResponse);
        var members = ImmutableDictionary.CreateBuilder<string, ImmutableArray<string>>(StringComparer.Ordinal);
        var cursors = ImmutableDictionary.CreateBuilder<string, string?>(StringComparer.Ordinal);
        foreach (var mapping in state.Folders)
            if (folders.Items.Any(x => x.Id == mapping.FolderId))
            {
                // Full snapshots are deliberate: deleted members have no incremental tombstone.
                var read = await All((cursor, token) => remote.FolderItemsAsync(mapping.FolderId, cursor, token), 10000, cancellation).ConfigureAwait(false);
                members.Add(mapping.FolderId, read.Items.Distinct(StringComparer.Ordinal).ToImmutableArray()); cursors.Add(mapping.FolderId, read.Cursor);
            }
        if (times.Items.GroupBy(x => x.WorkId).Any(x => x.Count() > 1) || states.Items.GroupBy(x => x.WorkId).Any(x => x.Count() > 1)
            || ratings.Items.GroupBy(x => x.WorkId).Any(x => x.Count() > 1) || folders.Items.GroupBy(x => x.Id).Any(x => x.Count() > 1)) throw new OnlineException(OnlineFailure.InvalidResponse);
        var snapshot = new RemoteSnapshot(cached?.Snapshot.TimeCursor is null ? times.Items : Merge(cached.Snapshot.Times, times.Items, x => x.WorkId),
            cached?.Snapshot.StateCursor is null ? states.Items : Merge(cached.Snapshot.States, states.Items, x => x.WorkId),
            cached?.Snapshot.RatingCursor is null ? ratings.Items : Merge(cached.Snapshot.Ratings, ratings.Items, x => x.WorkId),
            folders.Items, members.ToImmutable(), times.Cursor ?? cached?.Snapshot.TimeCursor, states.Cursor ?? cached?.Snapshot.StateCursor,
            ratings.Cursor ?? cached?.Snapshot.RatingCursor, cursors.ToImmutable());
        readCache = new(state.AccountId, auth.AuthContext.Generation, settings, snapshot, cached?.FullRead ?? Now);
        return snapshot;
    }
    private sealed record ReadCache(string Account, long Generation, PersonalSyncSettings Settings, RemoteSnapshot Snapshot, DateTimeOffset FullRead);
    private ReadCache? readCache;
    private (long Generation, DateTimeOffset Time) nextAutomatic;
    private static ImmutableArray<T> Merge<T>(ImmutableArray<T> old, ImmutableArray<T> incoming, Func<T, string> key)
    { var values = old.ToDictionary(key, StringComparer.Ordinal); foreach (var item in incoming) values[key(item)] = item; return [.. values.Values]; }
    internal static async Task<(ImmutableArray<T> Items, string? Cursor)> All<T>(Func<string?, CancellationToken, Task<CloudPage<T>>> read, int maximum, CancellationToken cancellation, string? startingCursor = null)
    {
        var result = ImmutableArray.CreateBuilder<T>(); string? cursor = startingCursor, last = startingCursor; var seen = new HashSet<string>(StringComparer.Ordinal);
        if (startingCursor is not null) seen.Add(startingCursor);
        for (int pages = 0; pages <= maximum / 100 + 1; pages++)
        {
            var page = await read(cursor, cancellation).ConfigureAwait(false);
            if (page.Items.IsDefault || page.Items.Length > 100 || result.Count + page.Items.Length > maximum) throw new OnlineException(OnlineFailure.ResponseTooLarge);
            result.AddRange(page.Items);
            if (page.NextCursor is not null) last = page.NextCursor;
            if (!page.HasMore) return (result.ToImmutable(), last);
            if (page.NextCursor is null || !seen.Add(page.NextCursor) || page.Items.Length == 0) throw new OnlineException(OnlineFailure.InvalidResponse);
            cursor = page.NextCursor;
        }
        throw new OnlineException(OnlineFailure.ResponseTooLarge);
    }
    public async Task MapFolderAsync(CloudFolder folder, string[] localWorkIds, CancellationToken cancellation)
    {
        await serial.WaitAsync(cancellation).ConfigureAwait(false);
        try
        {
            var fence = await auth.AuthContext.CaptureAsync(cancellation).ConfigureAwait(false);
            using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellation, fence.Changed);
            var state = await CurrentAsync(stop.Token).ConfigureAwait(false);
            if (folder.OwnerId != state.AccountId || localWorkIds.Length > 10000) throw new OnlineException(OnlineFailure.InvalidRequest);
            var local = await library.SnapshotAsync(stop.Token).ConfigureAwait(false);
            var existing = state.Folders.SingleOrDefault(x => x.FolderId == folder.Id);
            if (localWorkIds.Any(id => !MetadataMixing.IsNextMoeWorkId(id)
                || local.Count(x => MetadataMixing.HasNextMoeIdentity(x) && x.Metadata?.Id == id) != 1 && existing?.Members.Contains(id, StringComparer.Ordinal) != true)) throw new OnlineException(OnlineFailure.InvalidRequest);
            var mapping = new SyncFolderMapping(folder.Id, existing?.LocalCollectionId ?? Guid.NewGuid().ToString("D"), folder.Name,
                localWorkIds.Distinct(StringComparer.Ordinal).ToArray(), existing?.CloudBaseline ?? [], existing?.Cursor, existing?.UpdatedAt);
            var next = state with { Folders = [.. state.Folders.Where(x => x.FolderId != folder.Id), mapping] };
            stop.Token.ThrowIfCancellationRequested(); await store.SaveAsync(next, CancellationToken.None).ConfigureAwait(false);
            active = null; activeState = null; activeRemote = null;
        }
        finally { serial.Release(); }
    }
    public Task<PersonalSyncResult> ApplyAsync(PersonalSyncPreview preview, IReadOnlyDictionary<string, PersonalSyncChoice> choices,
        bool confirmCloudRemoval, IProgress<(int Completed, int Total)>? progress, CancellationToken cancellation)
        => ApplyCoreAsync(preview, choices, confirmCloudRemoval, progress, cancellation, automatic: false);

    private async Task<PersonalSyncResult> ApplyCoreAsync(PersonalSyncPreview preview, IReadOnlyDictionary<string, PersonalSyncChoice> choices,
        bool confirmCloudRemoval, IProgress<(int Completed, int Total)>? progress, CancellationToken cancellation, bool automatic,
        bool repairAssociations = true, ImmutableArray<NextMoeAssociationIssue> previousIssues = default)
    {
        await serial.WaitAsync(cancellation).ConfigureAwait(false);
        try
        {
            if (active?.Id != preview.Id || activeRemote is null || activeState is null || Now - preview.CreatedAt > TimeSpan.FromMinutes(10)) throw new OnlineException(OnlineFailure.Conflict);
            auth.AuthContext.Accept(preview.Auth, () => true);
            using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellation, preview.Auth.Changed);
            var state = await CurrentAsync(stop.Token).ConfigureAwait(false);
            if (state.AccountId != preview.AccountId || state.Pending.Length != 0 || state.FolderCreations?.Length > 0 || state.CatalogRedirect is not null)
                throw new OnlineException(OnlineFailure.Conflict);
            var selected = preview.Rows.Where(x => choices.TryGetValue(x.Id, out var choice) && choice != PersonalSyncChoice.Skip).ToArray();
            if (choices.Keys.Except(preview.Rows.Select(x => x.Id), StringComparer.Ordinal).Any()) throw new OnlineException(OnlineFailure.InvalidRequest);
            if (selected.Any(row => !Enum.IsDefined(choices[row.Id])
                || choices[row.Id] == PersonalSyncChoice.Upload && row.Upload is null && !row.DeleteCloud
                || choices[row.Id] == PersonalSyncChoice.Import && row.Import is null)) throw new OnlineException(OnlineFailure.InvalidRequest);
            var freshLocal = await library.SnapshotAsync(stop.Token).ConfigureAwait(false);
            var duplicateWorkIds = freshLocal.Where(MetadataMixing.HasNextMoeIdentity).GroupBy(x => x.Metadata!.Id, StringComparer.Ordinal)
                .Where(group => group.Count() > 1).Select(group => group.Key).ToHashSet(StringComparer.Ordinal);
            IEnumerable<(string WorkId, string Title)> syncTargets = selected.Select(row => (row.WorkId, row.Title));
            if (automatic)
                syncTargets = syncTargets.Concat(freshLocal.Where(MetadataMixing.HasNextMoeIdentity).Select(entry => (entry.Metadata!.Id, entry.Name)));
            var workTargets = syncTargets.DistinctBy(target => target.WorkId, StringComparer.Ordinal).ToArray();
            var ids = workTargets.Select(target => target.WorkId).ToArray();
            var unavailable = new HashSet<string>(StringComparer.Ordinal);
            if (catalog is ICatalogSyncIdentities batchIdentities)
            {
                foreach (var batch in ids.Chunk(100))
                {
                    var confirmed = await batchIdentities.ConfirmWorkIdsAsync([.. batch], stop.Token).ConfigureAwait(false);
                    unavailable.UnionWith(batch.Except(confirmed, StringComparer.Ordinal));
                    if (!automatic && unavailable.Count != 0) throw new OnlineException(OnlineFailure.Conflict);
                }
            }
            else
            {
                foreach (var id in ids)
                {
                    try
                    {
                        if ((await catalog.DetailsAsync(id, false, stop.Token).ConfigureAwait(false)).Id == id) continue;
                        if (!automatic) throw new OnlineException(OnlineFailure.Conflict);
                        unavailable.Add(id);
                    }
                    catch (OnlineException e) when (automatic && e.Failure == OnlineFailure.NotFound)
                    { unavailable.Add(id); }
                }
            }

            string? associationWarning = null;
            ImmutableArray<NextMoeAssociationIssue> associationIssues = [];
            if (unavailable.Count > 0)
            {
                var repair = await RepairMissingAssociationsAsync(workTargets, unavailable, freshLocal, state, preview.Auth,
                    previousIssues.IsDefault ? [] : previousIssues, automatic && repairAssociations, stop.Token).ConfigureAwait(false);
                associationIssues = repair.Issues;
                associationWarning = associationIssues.IsEmpty ? null : AssociationMessage(associationIssues);
                if (repair.Repaired > 0)
                    return new(0, 0, 0, "已更新 NextMoe 关联，正在重新核对同步。", associationWarning)
                    { RepairedAssociations = repair.Repaired, AssociationIssues = associationIssues };

                selected = selected.Where(row => !unavailable.Contains(row.WorkId)).ToArray();
                if (selected.Length == 0)
                    return new(0, 0, 0, associationWarning ?? "没有可同步的项目。", associationWarning)
                    { AssociationIssues = associationIssues };
            }

            if (automatic && selected.Length == 0) return new(0, 0, preview.Rows.Count(row => row.Conflict), "本轮没有可同步的项目。");

            var freshRemote = await ReadRemote(preview.Settings, state, stop.Token).ConfigureAwait(false);
            if (selected.Any(x => x.Kind == PersonalSyncKind.Rating && x.Import?.Rating != freshRemote.Ratings.SingleOrDefault(y => y.WorkId == x.WorkId)?.Overall))
                throw new OnlineException(OnlineFailure.Conflict);

            foreach (var row in selected)
            {
                var local = row.EntryId is null ? null : freshLocal.SingleOrDefault(x => x.Id == row.EntryId);
                var expected = row.EntryId is null ? null : preview.LocalSnapshot.SingleOrDefault(x => x.Id == row.EntryId);
                if (duplicateWorkIds.Contains(row.WorkId)
                    || row.EntryId is not null && (local is null || expected is null || !MetadataMixing.HasNextMoeIdentity(local)
                        || local.Metadata?.Id != row.WorkId || local.EffectivePlaySeconds != expected.EffectivePlaySeconds || local.Status != expected.Status))
                    throw new OnlineException(OnlineFailure.Conflict);
                if (row.Kind == PersonalSyncKind.Playtime && !PersonalSyncPlanning.SameTime(activeRemote.Times.SingleOrDefault(x => x.WorkId == row.WorkId), freshRemote.Times.SingleOrDefault(x => x.WorkId == row.WorkId))
                    || row.Kind == PersonalSyncKind.State && !PersonalSyncPlanning.SameState(activeRemote.States.SingleOrDefault(x => x.WorkId == row.WorkId), freshRemote.States.SingleOrDefault(x => x.WorkId == row.WorkId))
                    || row.Kind == PersonalSyncKind.Folder && (!freshRemote.Members.TryGetValue(row.FolderId!, out var members) || members.Contains(row.WorkId) != activeRemote.Members[row.FolderId!].Contains(row.WorkId)))
                    throw new OnlineException(OnlineFailure.Conflict);
                if (choices[row.Id] == PersonalSyncChoice.Upload && row.Upload is null && !row.DeleteCloud
                    || choices[row.Id] == PersonalSyncChoice.Import && row.Import is null) throw new OnlineException(OnlineFailure.InvalidRequest);
                if (row.DeleteCloud && choices[row.Id] == PersonalSyncChoice.Upload && !confirmCloudRemoval) throw new OnlineException(OnlineFailure.Forbidden);
                // A merged/redirected work must be explicitly relinked; never turn a suggestion into a write.
            }
            var imported = state.Imported.ToDictionary(x => x.WorkId, StringComparer.Ordinal);
            var mappings = state.Folders.ToDictionary(x => x.FolderId, StringComparer.Ordinal);
            var successful = new HashSet<string>(StringComparer.Ordinal); int done = 0, failed = 0;
            // Persist accepted settings separately from login; no initial preview causes a write.
            state = state with { Settings = preview.Settings }; await SaveFenced(state, preview.Auth, stop.Token).ConfigureAwait(false);
            foreach (var row in selected.Where(x => choices[x.Id] == PersonalSyncChoice.Import))
            {
                stop.Token.ThrowIfCancellationRequested();
                if (row.Kind == PersonalSyncKind.Folder)
                {
                    var mapping = mappings[row.FolderId!]; mappings[row.FolderId!] = mapping with { Members = row.DeleteLocalMembership
                        ? mapping.Members.Where(x => x != row.WorkId).ToArray() : [.. mapping.Members.Append(row.WorkId).Distinct(StringComparer.Ordinal)] };
                    if (row.EntryId is null && !row.DeleteLocalMembership)
                    {
                        var work = await catalog.DetailsAsync(row.WorkId, false, stop.Token).ConfigureAwait(false);
                        if (work.Id != row.WorkId) throw new OnlineException(OnlineFailure.Conflict);
                        await library.AddWishlistAsync(work, stop.Token).ConfigureAwait(false);
                    }
                }
                else
                {
                    var value = row.Import!; imported.TryGetValue(row.WorkId, out var before);
                    imported[row.WorkId] = new(row.WorkId, row.Kind == PersonalSyncKind.Playtime ? Math.Max(value.Minutes ?? 0, before?.Minutes ?? 0) : before?.Minutes,
                        row.Kind == PersonalSyncKind.Rating ? value.Rating : before?.Rating,
                        row.Kind == PersonalSyncKind.State ? value.State : before?.State, row.Kind == PersonalSyncKind.State ? value.Completion : before?.Completion, value.FetchedAt);
                    if (row.Kind == PersonalSyncKind.State && row.LocalStateToApply is { } localStatus)
                        await library.ApplyStateAsync(preview.LocalSnapshot.Single(x => x.Id == row.EntryId), localStatus, stop.Token).ConfigureAwait(false);
                }
                state = state with { Imported = imported.Values.ToArray(), Folders = mappings.Values.ToArray() };
                await SaveFenced(state, preview.Auth, stop.Token).ConfigureAwait(false); successful.Add(row.Id); done++; progress?.Report((done, selected.Length));
            }
            foreach (var group in selected.Where(x => choices[x.Id] == PersonalSyncChoice.Upload && !x.DeleteCloud).GroupBy(x => (x.Kind, x.FolderId)))
                foreach (var batch in group.Chunk(100))
                {
                    var writes = batch.Select(x => x.Upload!).ToImmutableArray();
                    // Already matching values establish baselines without redundant POSTs.
                    var needed = batch.Where(x => x.Kind switch { PersonalSyncKind.Playtime => freshRemote.Times.SingleOrDefault(y => y.WorkId == x.WorkId)?.Minutes != x.Upload!.Minutes,
                        PersonalSyncKind.State => freshRemote.States.SingleOrDefault(y => y.WorkId == x.WorkId) is not { } cloud || cloud.State != x.Upload!.State || cloud.Completion != x.Upload.Completion,
                        _ => !freshRemote.Members[x.FolderId!].Contains(x.WorkId) }).ToArray();
                    if (needed.Length != 0)
                    {
                        var before = needed.Select(x => x.Kind switch { PersonalSyncKind.Playtime => new PersonalWrite(x.WorkId, freshRemote.Times.SingleOrDefault(y => y.WorkId == x.WorkId)?.Minutes),
                            PersonalSyncKind.State => new PersonalWrite(x.WorkId, State: freshRemote.States.SingleOrDefault(y => y.WorkId == x.WorkId)?.State, Completion: freshRemote.States.SingleOrDefault(y => y.WorkId == x.WorkId)?.Completion),
                            _ => new PersonalWrite(x.WorkId, freshRemote.Members[x.FolderId!].Contains(x.WorkId) ? 1 : 0) }).ToArray();
                        var intent = new PendingPersonalWrite(Guid.NewGuid().ToString("D"), group.Key.Kind, group.Key.FolderId, needed.Select(x => x.Upload!).ToArray(), Now.ToString("o"), before);
                        state = state with { Pending = [.. state.Pending, intent] }; await SaveFenced(state, preview.Auth, stop.Token).ConfigureAwait(false);
                        var results = await remote.WriteAsync(intent.Kind, intent.FolderId, [.. intent.Items], intent.Id, stop.Token).ConfigureAwait(false);
                        foreach (var result in results)
                        { var row = needed.Single(x => x.WorkId == result.WorkId); if (result.Success) successful.Add(row.Id); else failed++; }
                        // Only known per-item results can close the intent. Timeouts/invalid replies keep it recoverable.
                        state = state with { Pending = state.Pending.Where(x => x.Id != intent.Id).ToArray() }; await SaveFenced(state, preview.Auth, stop.Token).ConfigureAwait(false);
                    }
                    foreach (var row in batch.Except(needed)) successful.Add(row.Id);
                    done += batch.Length; progress?.Report((done, selected.Length));
                }
            foreach (var row in selected.Where(x => choices[x.Id] == PersonalSyncChoice.Upload && x.DeleteCloud))
            {
                // Default choices never select this. The dedicated confirmation is mandatory.
                await remote.DeleteFolderItemAsync(row.FolderId!, row.WorkId, stop.Token).ConfigureAwait(false);
                successful.Add(row.Id); done++; progress?.Report((done, selected.Length));
            }
            var after = await library.SnapshotAsync(stop.Token).ConfigureAwait(false);
            var checkpoints = state.Works.ToDictionary(x => x.EntryId, StringComparer.Ordinal);
            foreach (var entry in after.Where(x => x.Metadata is not null))
            {
                var timeRow = selected.SingleOrDefault(x => x.EntryId == entry.Id && x.Kind == PersonalSyncKind.Playtime && successful.Contains(x.Id));
                var stateRow = selected.SingleOrDefault(x => x.EntryId == entry.Id && x.Kind == PersonalSyncKind.State && successful.Contains(x.Id));
                if (timeRow is null && stateRow is null) continue;
                var captured = preview.LocalSnapshot.Single(x => x.Id == entry.Id);
                checkpoints.TryGetValue(entry.Id, out var before);
                if (before?.WorkId != entry.Metadata!.Id) before = null;
                var cloudTime = timeRow is null ? before?.CloudMinutes : choices[timeRow.Id] == PersonalSyncChoice.Upload ? timeRow.Upload!.Minutes : timeRow.Import!.Minutes;
                var agreed = timeRow is null ? before?.AgreedMinutes : Math.Max(cloudTime ?? 0, captured.EffectivePlaySeconds / 60);
                long carry = before?.CarriedSeconds ?? 0;
                if (timeRow is not null && choices[timeRow.Id] == PersonalSyncChoice.Upload)
                    carry = before is null ? captured.EffectivePlaySeconds % 60 : ((PersonalSyncPlanning.IndependentSeconds(captured, before) ?? 0) + before.CarriedSeconds) % 60;
                else if (timeRow is not null) carry = captured.EffectivePlaySeconds / 60 >= (cloudTime ?? 0) ? captured.EffectivePlaySeconds % 60 : 0;
                var cloudState = stateRow is null ? before?.CloudState : choices[stateRow.Id] == PersonalSyncChoice.Upload ? stateRow.Upload!.State : stateRow.Import!.State;
                var completion = stateRow is null ? before?.Completion : choices[stateRow.Id] == PersonalSyncChoice.Upload ? stateRow.Upload!.Completion : stateRow.Import!.Completion;
                checkpoints[entry.Id] = new(entry.Id, entry.Metadata!.Id, timeRow is null ? before?.LocalSeconds ?? captured.EffectivePlaySeconds : captured.EffectivePlaySeconds,
                    cloudTime, agreed, stateRow is null ? before?.LocalState ?? captured.Status : choices[stateRow.Id] == PersonalSyncChoice.Import ? stateRow.LocalStateToApply! : captured.Status, cloudState, completion,
                    timeRow is null ? before?.SessionIds ?? [.. captured.Sessions.Select(x => x.Id)] : [.. captured.Sessions.Select(x => x.Id)],
                    timeRow is null ? before?.UpdatedAt ?? Now.ToString("o") : Now.ToString("o"), carry);
            }
            foreach (var folder in mappings.Values.ToArray())
                if (freshRemote.Members.TryGetValue(folder.FolderId, out var members))
                {
                    var baseline = members.ToHashSet(StringComparer.Ordinal);
                    foreach (var row in selected.Where(x => x.Kind == PersonalSyncKind.Folder && x.FolderId == folder.FolderId && successful.Contains(x.Id)))
                    { if (row.DeleteCloud && choices[row.Id] == PersonalSyncChoice.Upload || row.DeleteLocalMembership && choices[row.Id] == PersonalSyncChoice.Import) baseline.Remove(row.WorkId); else baseline.Add(row.WorkId); }
                    // Unavailable works retain their previous baseline so skipped additions can be retried.
                    if (unavailable.Count > 0)
                    {
                        baseline.ExceptWith(unavailable);
                        baseline.UnionWith(folder.CloudBaseline.Where(unavailable.Contains));
                    }
                    // A skipped removal retains its baseline so a later poll cannot treat it as a new addition.
                    foreach (var row in preview.Rows.Where(x => x.FolderId == folder.FolderId
                        && (x.DeleteCloud || x.DeleteLocalMembership) && !successful.Contains(x.Id)))
                        baseline.Add(row.WorkId);
                    mappings[folder.FolderId] = folder with { CloudBaseline = [.. baseline], Cursor = freshRemote.FolderCursors.GetValueOrDefault(folder.FolderId), UpdatedAt = Now.ToString("o"), Authorized = failed == 0 };
                }
            state = state with { Works = checkpoints.Values.ToArray(), Folders = mappings.Values.ToArray(), LastSuccess = failed == 0 ? Now.ToString("o") : state.LastSuccess,
                PlaytimeCursor = freshRemote.TimeCursor ?? state.PlaytimeCursor, StateCursor = freshRemote.StateCursor ?? state.StateCursor, RatingCursor = freshRemote.RatingCursor ?? state.RatingCursor };
            await SaveFenced(state, preview.Auth, stop.Token).ConfigureAwait(false); active = null; activeRemote = null; activeState = null;
            var message = failed > 0 ? $"已完成 {successful.Count} 项，{failed} 项未成功。失败项可重新预览后重试。"
                : associationWarning is null ? "所选项目已同步，本地历史和私人资料保留。" : $"已完成 {successful.Count} 项。";
            return new(successful.Count, failed, preview.Rows.Count(x => x.Conflict && (!choices.TryGetValue(x.Id, out var choice) || choice == PersonalSyncChoice.Skip)),
                message, associationWarning) { AssociationIssues = associationIssues };
        }
        finally { serial.Release(); }
    }
    private async Task SaveFenced(AccountSyncState state, OnlineAuthSnapshot fence, CancellationToken cancellation)
    {
        auth.AuthContext.Accept(fence, () => { cancellation.ThrowIfCancellationRequested(); return true; });
        // Existing atomic replacement does not interrupt once durable I/O starts; account/path remains immutable.
        await store.SaveAsync(state, CancellationToken.None).ConfigureAwait(false);
        cancellation.ThrowIfCancellationRequested();
    }
    public async Task<CloudFolder> CreateMappedFolderAsync(string name, string[] localMembers, CancellationToken cancellation)
    {
        await serial.WaitAsync(cancellation).ConfigureAwait(false);
        try
        {
            var fence = await auth.AuthContext.CaptureAsync(cancellation).ConfigureAwait(false);
            using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellation, fence.Changed);
            var state = await CurrentAsync(stop.Token).ConfigureAwait(false);
            if (state.FolderCreations?.Length > 0) throw new OnlineException(OnlineFailure.Conflict);
            var local = await library.SnapshotAsync(stop.Token).ConfigureAwait(false);
            if (name.Length is < 1 or > 100 || localMembers.Length > 10000 || localMembers.Any(id => local.Count(x => MetadataMixing.HasNextMoeIdentity(x) && x.Metadata?.Id == id) != 1)) throw new OnlineException(OnlineFailure.InvalidRequest);
            var intent = new PendingFolderCreation(Guid.NewGuid().ToString("D"), name, localMembers.Distinct(StringComparer.Ordinal).ToArray(), Now.ToString("o"));
            state = state with { FolderCreations = [intent] }; await SaveFenced(state, fence, stop.Token).ConfigureAwait(false);
            var folder = await remote.CreateFolderAsync(intent.Name, intent.Id, stop.Token).ConfigureAwait(false);
            if (folder.OwnerId != state.AccountId) throw new OnlineException(OnlineFailure.InvalidResponse);
            var mapping = new SyncFolderMapping(folder.Id, Guid.NewGuid().ToString("D"), folder.Name, intent.LocalMembers, []);
            state = state with { FolderCreations = [], Folders = [.. state.Folders, mapping] };
            await SaveFenced(state, fence, stop.Token).ConfigureAwait(false); active = null; return folder;
        }
        finally { serial.Release(); }
    }
    public async Task<PersonalSyncResult> RetryPendingAsync(CancellationToken cancellation)
    {
        await serial.WaitAsync(cancellation).ConfigureAwait(false);
        try
        {
            var fence = await auth.AuthContext.CaptureAsync(cancellation).ConfigureAwait(false);
            using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellation, fence.Changed);
            var state = await CurrentAsync(stop.Token).ConfigureAwait(false); int succeeded = 0, failed = 0;
            foreach (var creation in state.FolderCreations ?? [])
            {
                if (!DateTimeOffset.TryParse(creation.CreatedAt, out var at) || Now - at > TimeSpan.FromHours(23)) throw new OnlineException(OnlineFailure.Conflict);
                var folder = await remote.CreateFolderAsync(creation.Name, creation.Id, stop.Token).ConfigureAwait(false);
                if (folder.OwnerId != state.AccountId) throw new OnlineException(OnlineFailure.InvalidResponse);
                state = state with { FolderCreations = state.FolderCreations!.Where(x => x.Id != creation.Id).ToArray(),
                    Folders = [.. state.Folders.Where(x => x.FolderId != folder.Id), new(folder.Id, Guid.NewGuid().ToString("D"), folder.Name, creation.LocalMembers, [])] };
                await SaveFenced(state, fence, stop.Token).ConfigureAwait(false); succeeded++;
            }
            var fresh = await ReadRemote(state.Settings with { ReadRatings = false }, state, stop.Token).ConfigureAwait(false);
            foreach (var intent in state.Pending)
            {
                if (!DateTimeOffset.TryParse(intent.CreatedAt, out var at) || Now - at > TimeSpan.FromHours(23) || intent.Before?.Length != intent.Items.Length)
                    throw new OnlineException(OnlineFailure.Conflict);
                foreach (var item in intent.Items)
                {
                    var before = intent.Before!.Single(x => x.WorkId == item.WorkId);
                    bool stable = intent.Kind switch {
                        PersonalSyncKind.Playtime => fresh.Times.SingleOrDefault(x => x.WorkId == item.WorkId)?.Minutes is var minutes && (minutes == before.Minutes || minutes == item.Minutes),
                        PersonalSyncKind.State => fresh.States.SingleOrDefault(x => x.WorkId == item.WorkId) is var status &&
                            (status?.State == before.State && status?.Completion == before.Completion || status?.State == item.State && status?.Completion == item.Completion),
                        PersonalSyncKind.Folder => fresh.Members.ContainsKey(intent.FolderId!), _ => false };
                    if (!stable) throw new OnlineException(OnlineFailure.Conflict);
                }
                if (catalog is ICatalogSyncIdentities identities)
                {
                    var ids = intent.Items.Select(x => x.WorkId).ToImmutableArray();
                    if (ids.Except(await identities.ConfirmWorkIdsAsync(ids, stop.Token).ConfigureAwait(false), StringComparer.Ordinal).Any()) throw new OnlineException(OnlineFailure.Conflict);
                }
                else foreach (var item in intent.Items)
                    if ((await catalog.DetailsAsync(item.WorkId, false, stop.Token).ConfigureAwait(false)).Id != item.WorkId) throw new OnlineException(OnlineFailure.Conflict);
                var results = await remote.WriteAsync(intent.Kind, intent.FolderId, [.. intent.Items], intent.Id, stop.Token).ConfigureAwait(false);
                succeeded += results.Count(x => x.Success); failed += results.Count(x => !x.Success);
                state = state with { Pending = state.Pending.Where(x => x.Id != intent.Id).ToArray() };
                await SaveFenced(state, fence, stop.Token).ConfigureAwait(false);
            }
            active = null;
            return new(succeeded, failed, 0, $"恢复检查完成：成功 {succeeded} 项，失败 {failed} 项。请重新预览以确认新的同步基线。");
        }
        finally { serial.Release(); }
    }
    public async Task<int> ImportRatingsAsync(CancellationToken cancellation)
    {
        await serial.WaitAsync(cancellation).ConfigureAwait(false);
        try
        {
            var fence = await auth.AuthContext.CaptureAsync(cancellation).ConfigureAwait(false);
            using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellation, fence.Changed);
            var state = await CurrentAsync(stop.Token).ConfigureAwait(false);
            var cloud = await All(remote.RatingsAsync, 20000, stop.Token).ConfigureAwait(false);
            if (cloud.Items.GroupBy(rating => rating.WorkId, StringComparer.Ordinal).Any(group => group.Count() > 1))
                throw new OnlineException(OnlineFailure.InvalidResponse);

            var local = await library.SnapshotAsync(stop.Token).ConfigureAwait(false);
            var ratings = cloud.Items.ToDictionary(rating => rating.WorkId, StringComparer.Ordinal);
            var imported = state.Imported.ToDictionary(facts => facts.WorkId, StringComparer.Ordinal);
            var count = 0;
            var changed = false;
            foreach (var group in local.Where(MetadataMixing.HasNextMoeIdentity).GroupBy(entry => entry.Metadata!.Id, StringComparer.Ordinal))
            {
                if (group.Count() != 1) continue;
                var id = group.Key;
                ratings.TryGetValue(id, out var rating);
                if (rating is not null) count++;
                imported.TryGetValue(id, out var previous);
                if (rating?.Overall == previous?.Rating) continue;
                var facts = (previous ?? new ImportedPersonalFacts(id)) with
                { Rating = rating?.Overall, FetchedAt = rating?.UpdatedAt ?? previous?.FetchedAt };
                if (facts.Rating is null && facts.Minutes is null && facts.State is null) imported.Remove(id);
                else imported[id] = facts;
                changed = true;
            }
            if (changed)
                await SaveFenced(state with { Imported = [.. imported.Values] }, fence, stop.Token).ConfigureAwait(false);
            return auth.AuthContext.Accept(fence, () => { stop.Token.ThrowIfCancellationRequested(); return count; });
        }
        finally { serial.Release(); }
    }

    private static PersonalSyncSettings AuthorizedSettings(NextMoeAuthStatus status)
    {
        var folders = status.CanWriteFolders ? PersonalSyncDirection.Bidirectional
            : status.CanReadFolders ? PersonalSyncDirection.Import : PersonalSyncDirection.Off;
        return new(PersonalSyncDirection.Bidirectional, PersonalSyncDirection.Bidirectional, folders,
            Automatic: !status.NeedsLogin && status.CanWriteFolders);
    }

    public async Task<PersonalSyncResult?> AutomaticAsync(CancellationToken cancellation, bool force = false)
    {
        var fence = await auth.AuthContext.CaptureAsync(cancellation).ConfigureAwait(false);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellation, fence.Changed);
        var status = await auth.StatusAsync(stop.Token).ConfigureAwait(false);
        if (status is not { Mode: NextMoeAccessMode.Account, Account: { }, NeedsLogin: false, CanWriteFolders: true }) return null;
        if (!force && nextAutomatic.Generation == fence.Generation && nextAutomatic.Time > Now) return null;

        var state = await CurrentAsync(stop.Token).ConfigureAwait(false);
        if (state.Pending.Length != 0 || state.FolderCreations?.Length > 0)
            return new(0, 0, 0, "有未核对结果的 NextMoe 同步任务。");
        var local = await library.SnapshotAsync(stop.Token).ConfigureAwait(false);
        if (local.Any(x => x.Sessions.Any(s => s.Native is { IsTerminal: false }))) return null;
        if (state.Folders.Length == 0 && !local.Any(MetadataMixing.HasNextMoeIdentity)) return null;

        nextAutomatic = (fence.Generation, Now.AddMinutes(5));
        var preview = await PreviewCore(AuthorizedSettings(status), true, stop.Token).ConfigureAwait(false);
        state = await CurrentAsync(stop.Token).ConfigureAwait(false);
        var choices = AuthorizedChoices(preview, state);
        var result = await ApplyCoreAsync(preview, choices, false, null, stop.Token, automatic: true).ConfigureAwait(false);
        if (result.RepairedAssociations == 0)
            return !force && choices.Count == 0 && result.AssociationWarning is null ? null : result;

        preview = await PreviewCore(preview.Settings, false, stop.Token).ConfigureAwait(false);
        state = await CurrentAsync(stop.Token).ConfigureAwait(false);
        choices = AuthorizedChoices(preview, state);
        var retried = choices.Count == 0
            ? new PersonalSyncResult(0, 0, preview.Rows.Count(row => row.Conflict), "本轮没有可同步的项目。",
                result.AssociationWarning) { AssociationIssues = result.AssociationIssues }
            : await ApplyCoreAsync(preview, choices, false, null, stop.Token, automatic: true, repairAssociations: false,
                previousIssues: result.AssociationIssues).ConfigureAwait(false);
        return retried with { Message = $"已更新 {result.RepairedAssociations} 部作品的 NextMoe 关联。{retried.Message}" };
    }

    private static Dictionary<string, PersonalSyncChoice> AuthorizedChoices(PersonalSyncPreview preview, AccountSyncState state)
    {
        var choices = DefaultChoices(preview);
        var selected = new Dictionary<string, PersonalSyncChoice>(StringComparer.Ordinal);
        var imported = state.Imported.ToDictionary(x => x.WorkId, StringComparer.Ordinal);
        foreach (var row in preview.Rows)
        {
            if (row.Conflict || row.DeleteCloud || row.DeleteLocalMembership) continue;
            var choice = choices[row.Id];
            if (choice == PersonalSyncChoice.Skip) continue;
            if (row.Kind == PersonalSyncKind.Folder)
            {
                selected.Add(row.Id, choice);
                continue;
            }

            imported.TryGetValue(row.WorkId, out var previous);
            if (row.Kind == PersonalSyncKind.Playtime)
            {
                if (row.Import is { } cloudTime && (choice == PersonalSyncChoice.Import || row.Upload?.Minutes == cloudTime.Minutes))
                {
                    if (cloudTime.Minutes != previous?.Minutes) selected.Add(row.Id, PersonalSyncChoice.Import);
                }
                else if (row.Upload?.Minutes > (row.Import?.Minutes ?? 0))
                    selected.Add(row.Id, PersonalSyncChoice.Upload);
            }
            else if (row.Kind == PersonalSyncKind.State)
            {
                if (row.Import is { } cloudState && (choice == PersonalSyncChoice.Import || row.Upload?.State == cloudState.State))
                {
                    if (cloudState.State != previous?.State || cloudState.Completion != previous?.Completion)
                        selected.Add(row.Id, PersonalSyncChoice.Import);
                }
            }
        }
        return selected;
    }
    // Explicit recovery escape hatch: inspect current cloud state, retire ambiguous intents without resending them.
    public async Task<PersonalSyncResult> ReviewPendingAsync(CancellationToken cancellation)
    {
        await serial.WaitAsync(cancellation).ConfigureAwait(false);
        try
        {
            var fence = await auth.AuthContext.CaptureAsync(cancellation).ConfigureAwait(false);
            using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellation, fence.Changed);
            var state = await CurrentAsync(stop.Token).ConfigureAwait(false);
            var settings = state.Settings with { Playtime = state.Pending.Any(x => x.Kind == PersonalSyncKind.Playtime) ? PersonalSyncDirection.Import : state.Settings.Playtime,
                State = state.Pending.Any(x => x.Kind == PersonalSyncKind.State) ? PersonalSyncDirection.Import : state.Settings.State,
                Folders = state.FolderCreations?.Length > 0 || state.Pending.Any(x => x.Kind == PersonalSyncKind.Folder) ? PersonalSyncDirection.Import : state.Settings.Folders,
                ReadRatings = false };
            await ReadRemote(settings, state, stop.Token).ConfigureAwait(false);
            await SaveFenced(state with { Pending = [], FolderCreations = [] }, fence, stop.Token).ConfigureAwait(false);
            active = null; activeRemote = null; activeState = null;
            return new(0, 0, 0, "已读取当前云端记录并结束旧任务，未重新发送写入。请重新预览，选择新的同步基线；若曾创建收藏夹，可从云端列表重新选择。" );
        }
        finally { serial.Release(); }
    }
    public static Dictionary<string, PersonalSyncChoice> DefaultChoices(PersonalSyncPreview preview)
        => preview.Rows.ToDictionary(x => x.Id, x => x.Conflict ? PersonalSyncChoice.Skip : x.Kind == PersonalSyncKind.Rating ? PersonalSyncChoice.Import :
            (x.Kind switch { PersonalSyncKind.Playtime => preview.Settings.Playtime, PersonalSyncKind.State => preview.Settings.State, _ => preview.Settings.Folders }) switch
            { PersonalSyncDirection.Import => x.Import is null ? PersonalSyncChoice.Skip : PersonalSyncChoice.Import,
                PersonalSyncDirection.Upload => x.Upload is null ? PersonalSyncChoice.Skip : PersonalSyncChoice.Upload,
                PersonalSyncDirection.Bidirectional => x.Recommended ?? (x.Import is not null && (x.Upload is null || x.Kind == PersonalSyncKind.Folder) ? PersonalSyncChoice.Import
                    : x.Upload is null ? PersonalSyncChoice.Skip : PersonalSyncChoice.Upload), _ => PersonalSyncChoice.Skip }, StringComparer.Ordinal);
}
