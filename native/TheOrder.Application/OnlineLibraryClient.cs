using System.Collections.Immutable;
using TheOrder.Domain;

namespace TheOrder.Application;

public sealed class OnlineLibraryClient(OfflineLibraryClient offline, IOnlineCatalog catalog)
{
    public async Task<ClientResult> AddAsync(string workId, CancellationToken cancellation)
    {
        try { var work = await catalog.DetailsAsync(workId, false, cancellation).ConfigureAwait(false); return OfflineLibraryClient.Present(await offline.Required().AddAsync(work, cancellation).ConfigureAwait(false)); }
        catch (Exception e) when (OfflineLibraryClient.Expected(e)) { return OfflineLibraryClient.Failure(e); }
    }
    public async Task<ClientResult> RefreshAsync(string gameId, CancellationToken cancellation,
        Func<LibraryEntry, WorkMetadata, CancellationToken, Task<bool>>? confirmRedirect = null)
    {
        var library = offline.Required(); var entry = library.Snapshot.Entries.FirstOrDefault(e => e.Id == gameId);
        if (entry?.Metadata is null) return new(ClientOutcome.ValidationFailure, UiText.Get("此条目尚未关联在线作品。"));
        if (!MetadataMixing.HasNextMoeIdentity(entry)) return new(ClientOutcome.ValidationFailure, "请在「游玩记录、攻略与资料源」中重新获取直接资料，或先关联 NextMoe 目录作品。");
        var originalId = entry.Metadata.Id;
        // The UI supplies a confirmation callback for a verified catalog redirect.
        var token = await library.BeginRequestAsync(gameId, cancellation);
        try
        {
            var work = await catalog.DetailsAsync(originalId, true, cancellation);
            if (work.Id != originalId)
            {
                if (!MetadataMixing.IsNextMoeWorkId(work.Id))
                    return new(ClientOutcome.ValidationFailure, "NextMoe 返回的作品编号无效。");
                if (library.Snapshot.Entries.Any(other => other.Id != gameId && other.Metadata?.Id == work.Id))
                    return new(ClientOutcome.Conflict, $"书架上已有作品关联到 #{work.Id}。请在书架核对两部作品。");
                if (confirmRedirect is null)
                    return new(ClientOutcome.Conflict, $"请确认把 NextMoe 关联从 #{originalId} 更新至 #{work.Id}。");
                if (!await confirmRedirect(entry, work, cancellation))
                    return new(ClientOutcome.Cancelled, "已取消更新作品关联。");
                return OfflineLibraryClient.Present(await library.CommitCatalogRedirectAsync(
                    gameId, originalId, work, token, cancellation).ConfigureAwait(false));
            }
            return OfflineLibraryClient.Present(await library.CommitLinkAsync(gameId, work, token, preserveName: true, preserveMergedName: true, cancellation: cancellation).ConfigureAwait(false));
        }
        catch (Exception e) when (OfflineLibraryClient.Expected(e)) { return OfflineLibraryClient.Failure(e); }
        finally { await library.FinishRequestAsync(token).ConfigureAwait(false); }
    }
    public async Task<ClientResult> RemoveWishlistAsync(string gameId, CancellationToken cancellation)
    {
        try { return OfflineLibraryClient.Present(await offline.Required().DeleteWishlistAsync(gameId, cancellation).ConfigureAwait(false)); }
        catch (Exception e) when (OfflineLibraryClient.Expected(e)) { return OfflineLibraryClient.Failure(e); }
    }
}
public sealed partial class TransactionalLibrary
{
    internal Task<CommitResult<LibraryEntry>> CommitCatalogRedirectAsync(string id, string originalId, WorkMetadata canonical,
        RequestToken token, CancellationToken cancellation) => Mutate(games =>
    {
        AssertToken(id, token);
        var entry = Find(games, id);
        if (entry.Metadata?.Id != originalId || !MetadataMixing.HasNextMoeIdentity(entry))
            throw new StaleLibraryRequestException();

        var changed = ReplaceCatalogAssociation(games, entry, canonical);
        return (games.Replace(entry, changed), changed);
    }, cancellation, id);

    internal Task<CommitResult<LibraryEntry>> RepairCatalogAssociationAsync(LibraryExtrasDetails details, string oldId,
        WorkMetadata canonical, bool confirmed, CancellationToken cancellation) => Mutate(games =>
    {
        var entry = Find(games, details.Entry.Id);
        AssertExtras(details.Token, entry);
        if (entry.CollectionBindings != details.Entry.CollectionBindings)
            throw new StaleLibraryRequestException();
        if (canonical.Id == oldId || !MetadataMixing.IsNextMoeWorkId(canonical.Id))
            throw new InvalidDataException("无法确认 NextMoe 的合并目标。");
        if (games.Any(other => other.Id != entry.Id && (other.Metadata?.Id == canonical.Id
            || CollectionUpdate.WorkIdFor(other, CollectionSite.NextMoe) is { } workId && (workId == canonical.Id || !confirmed && workId == oldId))))
            throw new InvalidDataException($"书架里有其他作品使用 #{oldId} 或 #{canonical.Id}，请选择对应条目。");

        LibraryEntry changed;
        if (entry.Metadata?.Id == oldId && MetadataMixing.HasNextMoeIdentity(entry))
        {
            if (!confirmed && entry.CollectionBindings?.NextMoe is { } bound && bound != oldId && bound != canonical.Id)
                throw new InvalidDataException($"已手动绑定到 #{bound}，请确认要使用的 NextMoe 关联。");
            changed = ReplaceCatalogAssociation(games, entry, canonical, replaceSyncBinding: confirmed);
        }
        else if (entry.CollectionBindings?.NextMoe == oldId)
            changed = entry with { CollectionBindings = entry.CollectionBindings with { NextMoe = canonical.Id } };
        else
            throw new StaleLibraryRequestException();

        LibraryExtras.Validate(changed);
        return (games.Replace(entry, changed), changed);
    }, cancellation);

    private LibraryEntry ReplaceCatalogAssociation(ImmutableArray<LibraryEntry> games, LibraryEntry entry, WorkMetadata canonical,
        bool replaceSyncBinding = false)
    {
        var originalId = entry.Metadata?.Id;
        var work = codec.NormalizeWork(canonical);
        if (!MetadataMixing.IsNextMoeWorkId(work.Id))
            throw new InvalidDataException("NextMoe 返回的作品编号无效。");
        if (games.Any(other => other.Id != entry.Id && (other.Metadata?.Id == work.Id
            || CollectionUpdate.WorkIdFor(other, CollectionSite.NextMoe) == work.Id)))
            throw new InvalidDataException($"书架上已有作品关联到 #{work.Id}，请选择对应条目。");

        var mix = entry.MetadataMix;
        if (mix is not null)
        {
            foreach (var layer in mix.Layers)
                if (!CollectionUpdate.TryReadSourceWorkId(work, layer.Source, out var reference)
                    || reference is not null && reference != layer.SourceId)
                    throw new InvalidDataException("新目录编号与已确认的补充资料冲突，请先核对资料源。");
            mix = mix with { Base = work };
        }

        var bindings = entry.CollectionBindings;
        if (bindings?.NextMoe is { } bound && (bound == originalId || replaceSyncBinding))
            bindings = bindings with { NextMoe = work.Id };
        var changed = entry with
        {
            CollectionBindings = bindings,
            MetadataMix = mix,
            Metadata = mix is null ? work : MetadataMixing.Compose(mix),
            NameIsCustom = true,
            CoverChoice = entry.CoverChoice is { } cover && cover.WorkId == originalId ? cover with { WorkId = work.Id } : null
        };
        LibraryExtras.Validate(changed);
        return changed;
    }

    public Task<CommitResult<bool>> DeleteWishlistAsync(string id, CancellationToken cancellation) => Mutate(games =>
    {
        var entry = Find(games, id); if (!entry.Installations.IsEmpty) throw new InvalidDataException(UiText.Get("此条目已有本地安装，不能作为愿望条目移除。"));
        return (games.Remove(entry), true);
    }, cancellation);
}
public sealed class CombinedCoverSources(LocalCoverSources local, OfflineLibraryClient library, IRemoteCoverRegistry registry, IImageSourceProvider remote, IPersonalBannerStore? personalBanners = null) : IImageSourceProvider
{
    private readonly object gate = new();
    private LibrarySnapshot? indexed;
    private Dictionary<string, (string? Url, string Revision)> sources = [];
    public async Task<byte[]?> ReadAsync(string key, CancellationToken cancellation)
    {
        if (key.StartsWith("local-banner-", StringComparison.Ordinal))
            return personalBanners is null ? null : await personalBanners.ReadAsync(key, cancellation).ConfigureAwait(false);
        var bytes = library.Capability.Loaded ? await local.ReadAsync(key, cancellation).ConfigureAwait(false) : null; if (bytes is not null) return bytes;
        if (library.Capability.Loaded)
        {
            lock (gate)
            {
                var snapshot = library.Required().Snapshot;
                if (!ReferenceEquals(snapshot, indexed))
                {
                    sources = [];
                    foreach (var entry in snapshot.Entries)
                    {
                        cancellation.ThrowIfCancellationRequested();
                        var revision = entry.Metadata?.FetchedAt ?? "";
                        if (entry.LocalCover is null && ImageSourceKeys.For(entry) is { } id) sources[id] = (CoverSources.Selected(entry), revision);
                        if (ArtworkSources.Landscape(entry.Metadata) is { } banner && ImageSourceKeys.Remote(banner, revision) is { } bannerKey)
                            sources[bannerKey] = (banner, revision);
                        if (entry.Metadata is { } work)
                            foreach (var character in work.Characters.Take(24))
                                if (ImageSourceKeys.Remote(character.ImageUrl, revision) is { } portrait) sources[portrait] = (character.ImageUrl, revision);
                    }
                    indexed = snapshot;
                }
                if (sources.TryGetValue(key, out var source)) registry.Register(source.Url, source.Revision);
            }
        }
        return await remote.ReadAsync(key, cancellation).ConfigureAwait(false);
    }
}
