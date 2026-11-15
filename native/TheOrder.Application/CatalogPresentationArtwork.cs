using System.Collections.Immutable;
using TheOrder.Domain;

namespace TheOrder.Application;
public sealed partial class CatalogPresentationService
{
    private async Task<CatalogPresentationEntry> SupplementAsync(CatalogPresentationEntry entry, CancellationToken cancellation)
    {
        var identity = await auth.CaptureAsync(cancellation).ConfigureAwait(false);
        using var request = CancellationTokenSource.CreateLinkedTokenSource(cancellation, identity.Changed);
        var extra = artwork is null ? new ArtworkSupplement([], true) : await artwork.ReadAsync(entry.Work, request.Token).ConfigureAwait(false);
        // Offline/public-provider failures preserve already screened media. A failed request
        // remains retryable and cannot masquerade as a successfully empty image catalogue.
        if (!extra.Complete && extra.Art.IsDefaultOrEmpty && extra.Covers.IsDefaultOrEmpty) return entry;
        var current = CachedCatalogWork.From(MergeArtwork(entry.Work, extra, entry.Banner?.SourceWorkId == entry.Work.Id ? entry.Banner.Url : null));
        var retainedBanner = entry.Banner is { } prior && (prior.SourceWorkId != entry.Work.Id || BannerSelection.Eligible(current.Work).Any(a => a.Url == prior.Url)) ? entry.Banner : null;
        var updated = entry with { Current = current, Banner = retainedBanner, Signature = Signature(current, entry.Reference), ArtworkSchema = extra.Complete ? ArtworkSchemaVersion : 0, Prepared = false, MediaPrepared = false };
        await operations.WaitAsync(request.Token).ConfigureAwait(false);
        try
        {
            bool accepted = auth.Accept(identity, () => {
                request.Token.ThrowIfCancellationRequested();
                lock (gate)
                {
                    if (memory.TryGetValue(entry.Work.Id, out var active) && active.Entry.Signature != entry.Signature) return false;
                    Remember(updated); return true;
                }
            });
            if (!accepted) return entry;
            await SaveAsync(updated, request.Token).ConfigureAwait(false);
        }
        finally { operations.Release(); }
        return updated;
    }
    private static WorkMetadata MergeArtwork(WorkMetadata work, ArtworkSupplement extra, string? retainedUrl = null)
    {
        var covers = (work.Covers ?? []).Concat(extra.Covers.IsDefault ? [] : extra.Covers).DistinctBy(c => c.Url).Take(500).ToImmutableArray();
        // At most 64 scene candidates. Keep own artwork and a bounded set from this exact VNDB ID.
        var original = work.Presentation?.Art ?? [];
        var added = extra.Art.IsDefault ? [] : extra.Art.OrderByDescending(a => a.Kind == "vndb-curated").Take(24).ToImmutableArray();
        var own = original.Where(a => !a.Kind.StartsWith("vndb-", StringComparison.Ordinal) ||
            a.Kind == ArtworkSources.VndbPromoKind && a.Url == retainedUrl).Take(64).ToArray();
        var budget = 64 - added.Length;
        var selected = own.Length <= budget ? own : Enumerable.Range(0, budget).Select(i => own[(int)Math.Round(i * (own.Length - 1d) / (budget - 1))]).ToArray();
        if (retainedUrl is not null && selected.Length > 0 && !selected.Any(a => a.Url == retainedUrl) && own.FirstOrDefault(a => a.Url == retainedUrl) is { } screened)
            selected[^1] = screened;
        var art = selected.Concat(added).DistinctBy(a => a.Url).ToImmutableArray();
        var presentation = work.Presentation is { } p ? p with { Art = art } : null;
        return work with { Covers = covers, CoverUrl = CoverQuality.Select(covers, work.CoverUrl), Presentation = presentation };
    }
}
