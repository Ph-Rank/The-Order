using TheOrder.Domain;

namespace TheOrder.Application;

public sealed class CollectionBindingPreview
{
    internal CollectionBindingPreview(LibraryExtrasDetails details, CollectionSite site, string workId, WorkMetadata work)
    {
        Details = details;
        Site = site;
        WorkId = workId;
        Title = work.Title;
        ReleaseDate = work.ReleaseDate;
        Work = work;
    }

    internal LibraryExtrasDetails Details { get; }
    internal WorkMetadata Work { get; }
    public CollectionSite Site { get; }
    public string WorkId { get; }
    public string Title { get; }
    public string? ReleaseDate { get; }

    public CollectionUpdate? UpdateFor(CollectionFields fields)
        => CollectionUpdate.From(WithBinding(Details.Entry), Site, fields);

    internal LibraryEntry WithBinding(LibraryEntry entry)
    {
        var bindings = entry.CollectionBindings ?? new();
        return entry with
        {
            CollectionBindings = Site switch
            {
                CollectionSite.Vndb => bindings with { Vndb = WorkId },
                CollectionSite.Bangumi => bindings with { Bangumi = WorkId },
                CollectionSite.NextMoe => bindings with { NextMoe = WorkId },
                _ => throw new ArgumentOutOfRangeException(nameof(Site))
            }
        };
    }
}

public sealed class CollectionBindingClient(OfflineLibraryClient library, IOnlineCatalog catalog, MetadataSourceClient metadata)
{
    public async Task<CollectionBindingPreview> ReadAsync(string entryId, CollectionSite site, string input, bool allowAdult,
        CancellationToken cancellation)
    {
        var details = await library.Required().ExtrasAsync(entryId, cancellation).ConfigureAwait(false);
        WorkMetadata work;
        string id;
        if (site == CollectionSite.NextMoe)
        {
            if (!CatalogWorkReference.TryParse(input, out id)) throw new InvalidDataException("请输入 NextMoe 作品编号或作品链接。");
            work = await catalog.DetailsAsync(id, refresh: true, cancellation).ConfigureAwait(false);
            // The catalog follows verified merges and validates the ID in each response.
            if (!CatalogWorkReference.TryParse(work.Id, out id) || id != work.Id)
                throw new InvalidDataException("NextMoe 返回的作品编号无效。");
        }
        else
        {
            var provider = site switch
            {
                CollectionSite.Vndb => "vndb",
                CollectionSite.Bangumi => "bangumi",
                _ => throw new ArgumentOutOfRangeException(nameof(site))
            };
            id = DirectMetadataIdentity.Normalize(provider, input);
            work = await metadata.PreviewAsync(provider, id, allowAdult, cancellation).ConfigureAwait(false);
            if (work.Id != provider + ":" + id
                || !CollectionUpdate.TryReadSourceWorkId(work, provider, out var reference) || reference != id)
                throw new InvalidDataException("网站返回的作品与输入编号不一致。");
        }

        if (string.IsNullOrWhiteSpace(work.Title)) throw new InvalidDataException("网站返回的作品缺少标题。");
        cancellation.ThrowIfCancellationRequested();
        return new(details, site, id, work);
    }

    public async Task<ClientResult> BindAsync(CollectionBindingPreview preview, CancellationToken cancellation, bool catalogAssociation = false)
    {
        try
        {
            return OfflineLibraryClient.Present(await library.Required().BindCollectionAsync(preview, cancellation, catalogAssociation).ConfigureAwait(false));
        }
        catch (Exception error) when (OfflineLibraryClient.Expected(error))
        {
            return OfflineLibraryClient.Failure(error);
        }
    }
}

public sealed partial class TransactionalLibrary
{
    internal Task<CommitResult<LibraryEntry>> BindCollectionAsync(CollectionBindingPreview preview, CancellationToken cancellation, bool catalogAssociation)
        => Mutate(games =>
        {
            var entry = Find(games, preview.Details.Entry.Id);
            AssertExtras(preview.Details.Token, entry);
            if (entry.CollectionBindings != preview.Details.Entry.CollectionBindings) throw new StaleLibraryRequestException();
            if (catalogAssociation && preview.Site == CollectionSite.NextMoe)
            {
                var relinked = ReplaceCatalogAssociation(games, entry, preview.Work, replaceSyncBinding: true);
                return (games.Replace(entry, relinked), relinked);
            }
            if (games.Any(other => other.Id != entry.Id && CollectionUpdate.WorkIdFor(other, preview.Site) == preview.WorkId))
                throw new InvalidDataException($"编号 {preview.WorkId} 已用于同步书架中的另一部作品。");

            var changed = preview.WithBinding(entry);
            return changed.CollectionBindings == entry.CollectionBindings
                ? (games, entry)
                : (games.Replace(entry, changed), changed);
        }, cancellation, skipUnchanged: true);
}
