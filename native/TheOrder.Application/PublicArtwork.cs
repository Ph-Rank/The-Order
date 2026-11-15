using System.Collections.Immutable;
using TheOrder.Domain;

namespace TheOrder.Application;

public sealed record ArtworkSupplement(ImmutableArray<CatalogArt> Art, bool Complete, ImmutableArray<WorkCover> Covers = default);
public interface IPublicArtworkSource
{
    // Exact independent work IDs only. No title search, credentials or recursive relations.
    Task<ArtworkSupplement> ReadAsync(WorkMetadata work, CancellationToken cancellation);
    Task<ArtworkSupplement> ReadPromoAsync(WorkMetadata work, CancellationToken cancellation)
        => Task.FromResult(new ArtworkSupplement([], true));
}
