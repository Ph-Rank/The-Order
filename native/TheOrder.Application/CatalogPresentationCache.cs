using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using TheOrder.Domain;

namespace TheOrder.Application;

// Public, derived display facts only. This cache is never an identity/recognition authority,
// never part of library.json, and contains no paths, personal fields, sessions or credentials.
public sealed record CachedCatalogWork(WorkMetadata Metadata, CatalogPresentation? Presentation)
{
    public static CachedCatalogWork From(WorkMetadata work) => new(work, work.Presentation);
    [JsonIgnore]
    public WorkMetadata Work => Metadata with { Presentation = Presentation };
}
public sealed record CatalogPresentationEntry(string Algorithm, CachedCatalogWork Current, CachedCatalogWork? Reference,
    string Signature, BannerChoice? Banner, bool Prepared)
{
    public int MediaSchema {get;init;}
    public bool MediaPrepared {get;init;}
    public int ArtworkSchema { get; init; }
    public int CoverSchema { get; init; }
    public int BannerSchema { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool VndbPromoChecked { get; init; }
    public BannerAssessment[] BannerAssessments { get; init; } = [];
    public bool CatalogCoverKnown { get; init; }
    public WorkCover? CatalogCover { get; init; }
    public CoverDecision? CoverDecision { get; init; }
    [JsonIgnore]
    public WorkMetadata Work => Current.Work;
    [JsonIgnore]
    public WorkMetadata? ReferenceWork => Reference?.Work;
}
public interface ICatalogPresentationCache
{
    Task<CatalogPresentationEntry?> ReadAsync(string id, CancellationToken cancellation);
    Task WriteAsync(CatalogPresentationEntry entry, CancellationToken cancellation);
}
public sealed record CatalogPreparationMetrics(long CacheHits, long CatalogLoads, long Analyses, long Prepared, long Failed, int Queued);

// One bounded background worker shared by import, existing-library warming and catalog browsing.
// GetAsync NEVER screens pictures: opening a cover renders cached/local facts immediately.
// First screening belongs to PrepareAsync (import) or Queue (existing/online works).
public sealed partial class CatalogPresentationService(IOnlineCatalog catalog, IImagePipeline images, IRemoteCoverRegistry registry,
    ICatalogPresentationCache disk, Func<IArtworkTextDetector> detectorFactory, OnlineAuthContext auth, IPublicArtworkSource? artwork = null) : IAsyncDisposable
{
    public const string Algorithm = "cg-ppocrv3-03f550c6-v1";
    private const int ArtworkSchemaVersion = 4;
    private const int CoverSchemaVersion = 2;
    private const int BannerSchemaVersion = 1;
    public const int MaximumEntryBytes = 1024 * 1024;
    private const int MemoryItems = 128, MemoryBytes = 16 * 1024 * 1024, MaximumQueue = 2000;
    private readonly object gate = new();
    private readonly Dictionary<string, (CatalogPresentationEntry Entry, int Bytes)> memory = [];
    private readonly LinkedList<string> lru = [];
    private readonly Queue<string> waiting = [];
    private readonly HashSet<string> queued = [];
    private readonly HashSet<string> displayRefresh = [];
    private readonly SemaphoreSlim operations = new(1), analysis = new(1);
    private readonly CancellationTokenSource lifetime = new();
    private Task worker = Task.CompletedTask;
    private bool disposed, workerRunning;
    private int bytes;
    private long cacheHits, catalogLoads, analyses, prepared, failed;
    public event Action<string>? Prepared;
    public CatalogPreparationMetrics Metrics { get { lock (gate) return new(cacheHits, catalogLoads, analyses, prepared, failed, queued.Count); } }
    public static bool ValidId(string id) => id.Length is > 0 and <= 16 && id.All(char.IsAsciiDigit);
    private static bool NeedsDisplay(CatalogPresentationEntry entry) =>
        entry.Work.Presentation is { } p && ((p.LanguageSchema ?? 0) < CatalogLanguage.DisplaySchema || CatalogRelatedWorks.NeedsRefresh(p)) ||
        entry.ReferenceWork?.Presentation is { } r && ((r.LanguageSchema ?? 0) < CatalogLanguage.DisplaySchema || CatalogRelatedWorks.NeedsRefresh(r));
    private static bool CoverReady(CatalogPresentationEntry entry) => entry.CoverSchema == CoverSchemaVersion && entry.CatalogCoverKnown &&
        entry.CoverDecision is { Complete: true } decision && decision.Algorithm == CoverAesthetics.Algorithm &&
        decision.InputSignature == CoverAesthetics.Signature(CoverAesthetics.Candidates(entry.Work, entry.CatalogCover)) && decision.SelectedUrl == entry.Work.CoverUrl;
    private static bool NeedsVndbPromo(CatalogPresentationEntry entry) =>
        entry.Banner is null && !entry.VndbPromoChecked && entry.Work.Presentation is not null && ArtworkSources.VndbWorkId(entry.Work) is not null;
    public static bool NeedsPreparation(CatalogPresentationEntry entry) =>
        !entry.Prepared || entry.BannerSchema != BannerSchemaVersion || NeedsVndbPromo(entry) ||
        !entry.MediaPrepared || entry.MediaSchema != MediaSchema || !CoverReady(entry) || entry.ArtworkSchema != ArtworkSchemaVersion || NeedsDisplay(entry);
    public WorkMetadata? PreparedWork(string id) { lock (gate) return memory.GetValueOrDefault(id).Entry?.Work; }
    public static string Signature(CachedCatalogWork current, CachedCatalogWork? reference)
        => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { Algorithm, Current = current, Reference = reference })));
    public static bool Valid(CatalogPresentationEntry entry, string id)
    {
        if (entry.Current?.Metadata is not { } work || !WellFormed(entry.Current) || entry.Reference is { } r && !WellFormed(r) ||
            entry.Algorithm != Algorithm || work.Id != id || !ValidId(id) ||
            entry.Signature != Signature(entry.Current, entry.Reference)) return false;
        if (entry.Reference is { } related && CatalogReferences.Preferred(entry.Work)?.Id != related.Metadata.Id) return false;
        if (entry.Banner is not { } banner) return true;
        if (!banner.TextScreened || !double.IsFinite(banner.Score)) return false;
        var source = banner.SourceWorkId == id ? entry.Work : entry.ReferenceWork;
        return source is not null && source.Id == banner.SourceWorkId && BannerSelection.Eligible(source).Any(a => a.Url == banner.Url);
    }
    private static bool WellFormed(CachedCatalogWork cached)
    {
        var w = cached.Metadata;
        if (w is null || w.Title is null || w.OriginalTitle is null || w.Description is null || w.FetchedAt is null ||
            w.Aliases.IsDefault || w.Developers.IsDefault || w.Tags.IsDefault || w.Ratings.IsDefault || w.Characters.IsDefault || w.References.IsDefault) return false;
        return cached.Presentation is not { } p || !(p.Art.IsDefault || p.Companies.IsDefault || p.Engines.IsDefault ||
            p.Series.IsDefault || p.Tags.IsDefault || p.Characters.IsDefault || p.Credits.IsDefault || p.RelatedWorks.IsDefault);
    }
    private CatalogPresentationEntry? Cached(string id)
    {
        lock (gate)
        {
            if (!memory.TryGetValue(id, out var value)) return null;
            lru.Remove(id); lru.AddLast(id); cacheHits++; return value.Entry;
        }
    }
    private void Remember(CatalogPresentationEntry entry)
    {
        int size = JsonSerializer.SerializeToUtf8Bytes(entry).Length;
        if (size > MaximumEntryBytes) return;
        lock (gate)
        {
            if (memory.Remove(entry.Work.Id, out var old)) bytes -= old.Bytes;
            lru.Remove(entry.Work.Id);
            while (lru.First is { } first && (memory.Count >= MemoryItems || bytes + size > MemoryBytes))
            { lru.RemoveFirst(); if (memory.Remove(first.Value, out var removed)) bytes -= removed.Bytes; }
            memory[entry.Work.Id] = (entry, size); lru.AddLast(entry.Work.Id); bytes += size;
        }
    }
    private CatalogPresentationEntry Rebind(CatalogPresentationEntry entry)
    {
        if (entry.Banner is not { } banner) return entry;
        var source = banner.SourceWorkId == entry.Work.Id ? entry.Work : entry.ReferenceWork!;
        var key = registry.Register(banner.Url, source.FetchedAt);
        return key is null ? entry with { Banner = null, Prepared = false } : entry with { Banner = banner with { Key = key } };
    }
    private async Task<CatalogPresentationEntry?> ReadCachedAsync(string id, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (Cached(id) is { } cached) return cached;
        // Disk hits must not queue behind an unrelated cold network request.
        CatalogPresentationEntry? saved = null;
        try { saved = await disk.ReadAsync(id, token).ConfigureAwait(false); }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException or DataIsolationException) { }
        token.ThrowIfCancellationRequested();
        if (saved is not null && Valid(saved, id))
        {
            saved = Rebind(saved);
            lock (gate)
            {
                // A refresh/preparation may have completed while this disk read was pending.
                if (Cached(id) is { } latest) return latest;
                Remember(saved);
                if (NeedsDisplay(saved)) displayRefresh.Add(id);
                return saved;
            }
        }
        return Cached(id);
    }
    public async Task<CatalogPresentationEntry> GetAsync(string id, bool refresh = false, CancellationToken cancellation = default)
    {
        if (!ValidId(id)) throw new InvalidDataException("Invalid public work identifier");
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellation, lifetime.Token);
        var token = stop.Token; token.ThrowIfCancellationRequested();
        if (!refresh && await ReadCachedAsync(id, token).ConfigureAwait(false) is { } cached) return cached;
        var prior = refresh ? Cached(id) : null;
        await operations.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (!refresh && Cached(id) is { } again) return again;
            var identity = await auth.CaptureAsync(token).ConfigureAwait(false);
            using var request = CancellationTokenSource.CreateLinkedTokenSource(token, identity.Changed);
            var work = await catalog.DetailsAsync(id, refresh, request.Token).ConfigureAwait(false);
            lock (gate) catalogLoads++;
            WorkMetadata? reference = null;
            // A version fallback is fetched by the preparation worker only if own CGs fail.
            var current = CachedCatalogWork.From(work); var related = reference is null ? null : CachedCatalogWork.From(reference);
            var entry = new CatalogPresentationEntry(Algorithm, current, related, Signature(current, related), null, false)
                { CatalogCoverKnown = true, CatalogCover = CoverAesthetics.CatalogDefault(work) };
            // Enrich old display caches once in the existing import/warming worker.
            // The CG algorithm is unchanged: reuse its already screened picture when
            // the new public metadata still contains that same source/version/URL.
            if (prior is { Prepared: true } && NeedsDisplay(prior) && work.Presentation?.LanguageSchema == CatalogLanguage.DisplaySchema)
            {
                var upgraded = entry with { Banner = prior.Banner, Prepared = true, ArtworkSchema = prior.ArtworkSchema,
                    BannerSchema = prior.BannerSchema, BannerAssessments = prior.BannerAssessments };
                if (Valid(upgraded, id)) entry = upgraded;
            }
            auth.Accept(identity, () => { token.ThrowIfCancellationRequested(); Remember(entry); return true; });
            await SaveAsync(entry, token).ConfigureAwait(false);
            return entry;
        }
        finally { operations.Release(); }
    }
    private async Task<bool> SaveAsync(CatalogPresentationEntry entry, CancellationToken cancellation)
    {
        try { await disk.WriteAsync(entry, cancellation).ConfigureAwait(false); return true; }
        catch (Exception e) when(e is IOException or UnauthorizedAccessException or DataIsolationException) { return false; }
    }
    public async Task<bool> PrepareAsync(string id, CancellationToken cancellation = default)
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellation, lifetime.Token);
        var token = stop.Token; await analysis.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var entry = await GetAsync(id, cancellation: token).ConfigureAwait(false);
            bool enrich; lock (gate) enrich = displayRefresh.Remove(id);
            if (enrich)
            {
                // One background attempt per process. A network failure does not
                // discard an already screened CG or break offline reopening.
                try
                {
                    entry = await GetAsync(id, refresh: true, cancellation: token).ConfigureAwait(false);
                }
                catch (OnlineException) { /* Keep the verified CG; cached media can still be warmed offline. */ }
            }
            // A failed reference request must be retryable, and may not silently change version identity.
            if (entry.ArtworkSchema != ArtworkSchemaVersion) entry = await SupplementAsync(entry, token).ConfigureAwait(false);
            entry=await PrepareMediaAsync(entry,token).ConfigureAwait(false);
            bool screened = entry.Prepared && entry.BannerSchema == BannerSchemaVersion;
            if (screened && !NeedsVndbPromo(entry)) return entry.MediaPrepared;
            var identity = await auth.CaptureAsync(token).ConfigureAwait(false);
            using var request = CancellationTokenSource.CreateLinkedTokenSource(token, identity.Changed);
            await using var detector = detectorFactory();
            lock (gate) analyses++;
            var result = screened
                ? new BannerSelection.Outcome(null, true) { Assessments = entry.BannerAssessments }
                : await BannerSelection.AnalyzeAsync(entry.Work, null, images, registry, request.Token, detector).ConfigureAwait(false);
            var current = entry.Current;
            bool promoChecked = entry.VndbPromoChecked;
            if (result.Choice is null && NeedsVndbPromo(entry) && entry.Work.Presentation is { } display)
            {
                var extra = artwork is null ? new ArtworkSupplement([], true) : await artwork.ReadPromoAsync(entry.Work, request.Token).ConfigureAwait(false);
                promoChecked = extra.Complete;
                if (!extra.Art.IsDefaultOrEmpty)
                {
                    var promoWork = entry.Work with { BannerUrl = null, Covers = [], Presentation = display with { Art = extra.Art } };
                    var promo = await BannerSelection.AnalyzeAsync(promoWork, null, images, registry, request.Token, detector).ConfigureAwait(false);
                    promoChecked &= promo.Complete || promo.Choice is not null;
                    result = result with { Choice = promo.Choice, Assessments = [.. result.Assessments.Where(a => a.Kind != ArtworkSources.VndbPromoKind), .. promo.Assessments] };
                    if (promo.Choice is { } chosen)
                    {
                        var art = extra.Art.First(a => a.Url == chosen.Url);
                        current = CachedCatalogWork.From(entry.Work with
                        { Presentation = display with { Art = [art, .. display.Art.Where(a => a.Url != art.Url).Take(63)] } });
                    }
                }
            }
            if (result.Choice is null && !screened && CatalogReferences.Preferred(entry.Work) is { } relation)
            {
                if (entry.Reference is null)
                {
                    try
                    {
                        var reference = await catalog.DetailsAsync(relation.Id, false, request.Token).ConfigureAwait(false);
                        lock (gate) catalogLoads++;
                        if (reference.Id == relation.Id)
                        {
                            if (artwork is not null) reference = MergeArtwork(reference, await artwork.ReadAsync(reference, request.Token).ConfigureAwait(false));
                            var related = CachedCatalogWork.From(reference);
                            var revised = entry with { Reference = related, Signature = Signature(entry.Current, related) };
                            auth.Accept(identity, () => { Remember(revised); return true; }); entry = revised;
                        }
                    }
                    catch (OnlineException) { }
                }
                if (entry.ReferenceWork is { } referenceWork)
                    result = await BannerSelection.ContinueWithReferenceAsync(entry.Work, result, referenceWork, images, registry, request.Token, detector).ConfigureAwait(false);
            }
            bool complete = (result.Choice is not null || result.Complete) &&
                (CatalogReferences.Preferred(entry.Work) is null || entry.Reference is not null || result.Choice is not null);
            var updated = entry with { Current = current, Signature = Signature(current, entry.Reference),
                Banner = result.Choice ?? (result.Complete ? null : entry.Banner), VndbPromoChecked = promoChecked,
                BannerSchema = complete ? BannerSchemaVersion : 0, BannerAssessments = result.Assessments,
                Prepared = complete && entry.ArtworkSchema == ArtworkSchemaVersion };
            if (result.Choice is { } choice)
            {
                // Prime the actual displayed size too, so the first click does not need another download.
                var full = await images.LoadAsync(choice.Key, ArtworkSizing.MaximumEdge, request.Token).ConfigureAwait(false);
                if (full.Pixels is null) updated = updated with { Prepared = false };
                else updated = updated with { Banner = choice with { Width = full.Pixels.Width, Height = full.Pixels.Height } };
                if(registry is IRemoteArtworkPreparation source&&!await source.EnsureCachedAsync(choice.Key,request.Token).ConfigureAwait(false))updated=updated with{MediaPrepared=false};
            }
            await operations.WaitAsync(request.Token).ConfigureAwait(false);
            try
            {
                bool accepted = auth.Accept(identity, () => {
                    request.Token.ThrowIfCancellationRequested();
                    lock (gate)
                    {
                        if (memory.TryGetValue(id, out var active) && active.Entry.Signature != entry.Signature) return false;
                        Remember(updated); if (updated.Prepared) prepared++; else failed++;
                        return true;
                    }
                });
                if (!accepted) return false;
                if(!await SaveAsync(updated, request.Token).ConfigureAwait(false))
                {updated=updated with{MediaPrepared=false};Remember(updated);}
            }
            finally { operations.Release(); }
            Prepared?.Invoke(id);
            return updated.Prepared && updated.MediaPrepared && !NeedsVndbPromo(updated);
        }
        finally { analysis.Release(); }
    }
    public void Queue(IEnumerable<string> ids,bool priority=false)
    {
        lock (gate)
        {
            if (disposed) return;
            var ordered=ids.Where(ValidId).Distinct(StringComparer.Ordinal).Take(MaximumQueue).ToArray();
            var requested=ordered.ToHashSet(StringComparer.Ordinal);
            foreach (var id in ordered)
                if (ValidId(id) && queued.Count < MaximumQueue && queued.Add(id)) waiting.Enqueue(id);
            // A foreground retry of an old/incomplete cache comes before unseen library warming.
            // Never duplicate or interrupt the currently running atomic preparation.
            if(priority)
            {
                var pending=waiting.ToArray();waiting.Clear();
                foreach(var id in pending.Where(requested.Contains).Concat(pending.Where(i=>!requested.Contains(i))))waiting.Enqueue(id);
            }
            if (workerRunning) return;
            workerRunning = true;
            worker = Task.Run(async () => {
                while (true)
                {
                    string id;
                    lock (gate) { if (disposed || !waiting.TryDequeue(out id!)) { workerRunning = false; return; } }
                    try { await PrepareAsync(id, lifetime.Token).ConfigureAwait(false); }
                    catch (OperationCanceledException) { if (lifetime.IsCancellationRequested) return; }
                    catch (Exception) { lock (gate) failed++; }
                    finally { lock (gate) queued.Remove(id); }
                }
            });
        }
    }
    public Task WhenIdleAsync() { lock (gate) return worker; }
    public async ValueTask DisposeAsync()
    {
        Task work;
        lock (gate) { if (disposed) return; disposed = true; waiting.Clear(); queued.Clear(); work = worker; }
        lifetime.Cancel(); try { await work.ConfigureAwait(false); } catch (OperationCanceledException) { }
        await analysis.WaitAsync().ConfigureAwait(false); analysis.Release();
        await operations.WaitAsync().ConfigureAwait(false); operations.Release();
    }
}
