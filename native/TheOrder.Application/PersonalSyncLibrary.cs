using System.Collections.Immutable;
using TheOrder.Domain;

namespace TheOrder.Application;

public sealed class PersonalSyncLibrary(OfflineLibraryClient local) : IPersonalSyncLibrary
{
    public Task<ImmutableArray<LibraryEntry>> SnapshotAsync(CancellationToken cancellation)
    { cancellation.ThrowIfCancellationRequested(); return Task.FromResult(local.Required().Snapshot.Entries); }
    public async Task ApplyStateAsync(LibraryEntry expected, string status, CancellationToken cancellation)
        => await local.Required().ApplySyncStateAsync(expected, status, cancellation).ConfigureAwait(false);
    public Task<LibraryExtrasDetails> ReadAssociationAsync(string entryId, CancellationToken cancellation)
        => local.Required().ExtrasAsync(entryId, cancellation);
    public async Task RepairAssociationAsync(LibraryExtrasDetails details, string oldId, WorkMetadata canonical, bool confirmed, CancellationToken cancellation)
        => await local.Required().RepairCatalogAssociationAsync(details, oldId, canonical, confirmed, cancellation).ConfigureAwait(false);
    public async Task<string> AddWishlistAsync(WorkMetadata metadata, CancellationToken cancellation)
    {
        await local.Required().AddAsync(metadata, cancellation).ConfigureAwait(false);
        return local.Required().Snapshot.Entries.Single(x => x.Metadata?.Id == metadata.Id).Id;
    }
}
public sealed partial class TransactionalLibrary
{
    public Task<CommitResult<bool>> ApplySyncStateAsync(LibraryEntry expected, string status, CancellationToken cancellation)
        => Mutate(games =>
        {
            var currentEntry = games.FirstOrDefault(x => x.Id == expected.Id) ?? throw new StaleLibraryRequestException();
            if (currentEntry.Metadata?.Id != expected.Metadata?.Id || currentEntry.Status != expected.Status) throw new StaleLibraryRequestException();
            _ = PersonalSyncPlanning.CloudStatus(status);
            return (games.Replace(currentEntry, currentEntry with { Status = status }), true);
        }, cancellation);
}
