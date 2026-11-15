using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using TheOrder.Application;
using TheOrder.Domain;
using TheOrder.Platform.Windows;
using Xunit;
using static TheOrder.Tests.RecognitionBoundaryTests;

namespace TheOrder.Tests;

public sealed class CatalogPreparationTests
{
    private static WorkMetadata Artwork(string id = "200", string url = "https://example.org/cg") => Work(id) with
    { BannerUrl = null, Covers = [], Presentation = new("kungal", [new(url,1280,720,"screenshot")], [],[],[],[],[],[],[]) { LanguageSchema = CatalogLanguage.DisplaySchema } };
    private sealed class Catalog : IOnlineCatalog
    {
        public WorkMetadata Current = Artwork(); public int Calls; public bool Offline; public Func<string, CancellationToken, Task>? BeforeDetails;
        public async Task<WorkMetadata> DetailsAsync(string id, bool refresh, CancellationToken token)
        { token.ThrowIfCancellationRequested(); Calls++; if (BeforeDetails is not null) await BeforeDetails(id, token); if (Offline) throw new OnlineException(OnlineFailure.Offline); return Current with { Id = id }; }
        public Task<OnlinePage> SearchAsync(string query,string? cursor,CancellationToken token) => throw new NotSupportedException();
        public Task TestConnectionAsync(CancellationToken token) => Task.CompletedTask;
        public void ClearCache() { }
    }
    private sealed class Store : ICatalogPresentationCache
    {
        public CatalogPresentationEntry? Value; public int Writes; public Exception? ReadFailure;
        public Task<CatalogPresentationEntry?> ReadAsync(string id,CancellationToken token) => ReadFailure is { } error
            ? Task.FromException<CatalogPresentationEntry?>(error) : Task.FromResult(Value?.Work.Id == id ? Value : null);
        public Task WriteAsync(CatalogPresentationEntry entry,CancellationToken token) { token.ThrowIfCancellationRequested(); Value = entry; Writes++; return Task.CompletedTask; }
    }
    private sealed class Registry : IRemoteCoverRegistry
    { public string? Register(string? url,string revision) => url is null ? null : ImageSourceKeys.Remote(url,revision); }
    private sealed class Images : IImagePipeline
    {
        public bool Missing; public int Calls;
        public ImageMetrics Metrics => new(Calls,Calls,0,0,0,0,0,0,0,0);
        public Task<ImageResult> LoadAsync(string? key,int edge,CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); Calls++;
            var bytes = new byte[160*90*4]; for(int y=0;y<90;y++) for(int x=0;x<160;x++)
            { int i=(y*160+x)*4; bytes[i]=(byte)(70+x/3);bytes[i+1]=(byte)(40+y);bytes[i+2]=(byte)(90+x/2);bytes[i+3]=255; }
            return Task.FromResult(new ImageResult(Missing ? null : new(160,90,bytes),Missing ? "offline" : "loaded"));
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private sealed class Detector : IArtworkTextDetector
    {
        public int Calls; public bool Available=true, Dialogue; public Func<CancellationToken,Task>? Hold;
        public async Task<ArtworkTextDetection> AnalyzeAsync(RasterPixels pixels,CancellationToken token)
        { Calls++; if(Hold is not null) await Hold(token); token.ThrowIfCancellationRequested(); return new(Available,Dialogue ? [new(.1,.75,.7,.025,.99)] : [],Available ? "synthetic" : "unavailable"); }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private static CatalogPresentationService Service(Catalog catalog,Images images,ICatalogPresentationCache store,Detector detector)
        => new(catalog,images,new Registry(),store,()=>detector,new OnlineAuthContext());
    [Fact] public async Task ColdStartReadsSavedCoverWithoutCatalogImagesOrOcr()
    {
        var store = new Store(); await using var seedImages = new Images();
        var work = Artwork() with { CoverUrl = "https://example.org/saved-cover" };
        await using (var seed = Service(new Catalog { Current = work }, seedImages, store, new Detector())) Assert.True(await seed.PrepareAsync("200"));
        var writes = store.Writes; var catalog = new Catalog { Offline = true }; var detector = new Detector(); await using var images = new Images();
        await using var service = Service(catalog, images, store, detector);
        var saved = await service.ReadCoverAsync("200", default);
        Assert.NotNull(saved); Assert.Equal(ImageSourceKeys.Remote(work.CoverUrl, work.FetchedAt), saved.Key);
        Assert.Equal(0, catalog.Calls); Assert.Equal(0, images.Calls); Assert.Equal(0, detector.Calls);
        Assert.Equal(writes, store.Writes); Assert.Equal(0, service.Metrics.Queued);
    }
    [Theory][InlineData("missing")][InlineData("signature")][InlineData("schema")][InlineData("decision")]
    public async Task UnusableSavedCoverFallsBackWithoutStartingOnlinePreparation(string problem)
    {
        var store = new Store(); await using var seedImages = new Images();
        await using (var seed = Service(new Catalog { Current = Artwork() with { CoverUrl = "https://example.org/cover" } }, seedImages, store, new Detector()))
            Assert.True(await seed.PrepareAsync("200"));
        store.Value = problem switch {
            "missing" => null, "signature" => store.Value! with { Signature = "damaged" },
            "schema" => store.Value! with { CoverSchema = 0 }, _ => store.Value! with { CoverDecision = store.Value!.CoverDecision! with { Complete = false } }
        };
        var catalog = new Catalog { Offline = true }; var detector = new Detector(); await using var images = new Images();
        await using var service = Service(catalog, images, store, detector);
        Assert.Null(await service.ReadCoverAsync("200", default));
        Assert.Null(await service.ReadCoverAsync("../../other-profile", default));
        Assert.Equal(0, catalog.Calls); Assert.Equal(0, images.Calls); Assert.Equal(0, detector.Calls); Assert.Equal(0, service.Metrics.Queued);
    }
    [Theory][InlineData(false)][InlineData(true)] public async Task UnreadableSavedCoverDoesNotBlockTheOfflineShelf(bool denied)
    {
        var catalog = new Catalog { Offline = true }; await using var images = new Images();
        var store = new Store { ReadFailure = denied ? new UnauthorizedAccessException() : new IOException() };
        await using var service = Service(catalog, images, store, new Detector());
        Assert.Null(await service.ReadCoverAsync("200", default)); Assert.Equal(0, catalog.Calls);
    }
    [Fact] public async Task CancelledCachedCoverReadDoesNotContinueToNetwork()
    {
        var store = new HeldReadStore(null!); var catalog = new Catalog(); await using var images = new Images();
        await using var service = Service(catalog, images, store, new Detector()); using var cancel = new CancellationTokenSource();
        var read = service.ReadCoverAsync("200", cancel.Token); await store.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        cancel.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => read); Assert.Equal(0, catalog.Calls);
    }
    [Fact] public async Task CachedDetailsDoNotWaitForAnUnrelatedColdNetworkRequest()
    {
        var store = new Store(); await using var images = new Images();
        await using (var seed = Service(new Catalog(), images, store, new Detector())) Assert.True(await seed.PrepareAsync("200"));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var catalog = new Catalog { BeforeDetails = async (_, token) => { entered.SetResult(); await release.Task.WaitAsync(token); } };
        await using var service = Service(catalog, images, store, new Detector()); var cold = service.GetAsync("201");
        try { await entered.Task.WaitAsync(TimeSpan.FromSeconds(3)); Assert.NotNull((await service.GetAsync("200").WaitAsync(TimeSpan.FromSeconds(3))).Banner); }
        finally { release.TrySetResult(); await cold; }
        Assert.Equal(1, catalog.Calls);
    }
    [Fact] public async Task PendingOldDiskReadCannotReplaceCompletedRefresh()
    {
        var seedStore = new Store(); await using var images = new Images();
        await using (var seed = Service(new Catalog(), images, seedStore, new Detector())) Assert.True(await seed.PrepareAsync("200"));
        var store = new HeldReadStore(seedStore.Value!); var catalog = new Catalog { Current = Artwork(url: "https://example.org/new-cg") };
        await using var service = Service(catalog, images, store, new Detector()); var oldRead = service.GetAsync("200");
        await store.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        try
        {
            var current = await service.GetAsync("200", refresh: true); store.Release.SetResult();
            var read = await oldRead;
            Assert.Equal(current.Signature, read.Signature); Assert.Contains(read.Work.Presentation!.Art, a => a.Url.EndsWith("new-cg"));
            Assert.Equal(current.Signature, (await service.GetAsync("200")).Signature);
        }
        finally { store.Release.TrySetResult(); }
    }
    [Fact] public async Task OfflineArtworkSupplementPreservesScreenedBannerAndRemainsRetryable()
    {
        var store = new Store(); await using var images = new Images();
        await using (var seed = Service(new Catalog(), images, store, new Detector())) Assert.True(await seed.PrepareAsync("200"));
        store.Value = store.Value! with { ArtworkSchema = 0 }; var banner = store.Value.Banner; var detector = new Detector(); var catalog = new Catalog { Offline = true };
        await using var service = new CatalogPresentationService(catalog, images, new Registry(), store, () => detector, new OnlineAuthContext(), new OfflineArtwork());
        Assert.True(await service.PrepareAsync("200")); var current = await service.GetAsync("200");
        Assert.Equal(banner, current.Banner); Assert.True(current.Prepared); Assert.True(CatalogPresentationService.NeedsPreparation(current));
        Assert.Equal(0, detector.Calls); Assert.Equal(0, catalog.Calls);
    }
    private sealed class OfflineArtwork : IPublicArtworkSource
    { public Task<ArtworkSupplement> ReadAsync(WorkMetadata work, CancellationToken cancellation) => Task.FromResult(new ArtworkSupplement([], false)); }
    private sealed class HeldReadStore(CatalogPresentationEntry old) : ICatalogPresentationCache
    {
        public TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously), Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<CatalogPresentationEntry?> ReadAsync(string id, CancellationToken token) { Entered.SetResult(); await Release.Task.WaitAsync(token); return old; }
        public Task WriteAsync(CatalogPresentationEntry entry, CancellationToken token) => Task.CompletedTask;
    }
    private static CatalogPresentationEntry Legacy(CatalogPresentationEntry entry, int? schema = null)
    {
        var old = entry.Current with { Presentation = entry.Current.Presentation! with { LanguageSchema = schema } };
        return entry with { Current = old, Signature = CatalogPresentationService.Signature(old, entry.Reference) };
    }
    [Theory][InlineData(null)][InlineData(1)][InlineData(2)] public async Task LanguageEnrichmentReusesVerifiedCgAndOnlyWarmsItsCachedDisplayPixels(int? schema)
    {
        var store = new Store(); var images = new Images(); var detector = new Detector();
        await using (var before = Service(new Catalog(), images, store, detector)) Assert.True(await before.PrepareAsync("200"));
        store.Value = Legacy(store.Value!, schema); var old = store.Value;
        var catalog = new Catalog(); var nextImages = new Images(); var nextDetector = new Detector();
        await using var after = Service(catalog, nextImages, store, nextDetector); int changed = 0; after.Prepared += _ => changed++;
        Assert.Equal(schema,(await after.GetAsync("200")).Work.Presentation!.LanguageSchema);
        Assert.True(await after.PrepareAsync("200")); var upgraded = await after.GetAsync("200");
        Assert.Equal(CatalogLanguage.DisplaySchema, upgraded.Work.Presentation!.LanguageSchema); Assert.Equal(old.Banner!.Url, upgraded.Banner!.Url);
        Assert.True(upgraded.Prepared); Assert.True(upgraded.MediaPrepared);Assert.Equal(1, catalog.Calls); Assert.Equal(1, nextImages.Calls); Assert.Equal(0, nextDetector.Calls);
        Assert.Equal(1, changed); Assert.True(await after.PrepareAsync("200")); Assert.Equal(1, catalog.Calls);
    }
    [Fact] public async Task LegacyCacheEnrichmentOfflineKeepsCgAndDoesNotRetryOnEveryClick()
    {
        var store = new Store(); var images = new Images(); var detector = new Detector();
        await using (var before = Service(new Catalog(), images, store, detector)) Assert.True(await before.PrepareAsync("200"));
        store.Value = Legacy(store.Value!); var offline = new Catalog { Offline = true }; var nextDetector = new Detector();
        await using var after = Service(offline, images, store, nextDetector);
        for (int i = 0; i < 4; i++) { Assert.True(await after.PrepareAsync("200")); Assert.NotNull((await after.GetAsync("200")).Banner); }
        Assert.Equal(1, offline.Calls); Assert.Equal(0, nextDetector.Calls); Assert.Null(store.Value.Work.Presentation!.LanguageSchema);
    }
    [Fact] public async Task OpeningFactsNeverRunsTheInitialSelection()
    {
        var catalog=new Catalog();var store=new Store();var detector=new Detector();await using var images=new Images();
        await using var service=Service(catalog,images,store,detector);
        var entry=await service.GetAsync("200");Assert.False(entry.Prepared);Assert.Null(entry.Banner);
        Assert.Equal(0,detector.Calls);Assert.Equal(0,images.Calls);
    }
    [Fact] public async Task BannerUpgradeKeepsLegacySelectionReadableAndRescoresOnceInPreparationOnly()
    {
        var store=new Store(); await using var images=new Images();
        await using(var seed=Service(new Catalog(),images,store,new Detector())) Assert.True(await seed.PrepareAsync("200"));
        store.Value=store.Value! with {BannerSchema=0}; var prior=store.Value;
        var detector=new Detector(); var catalog=new Catalog {Offline=true};
        await using var service=Service(catalog,images,store,detector);
        var visible=await service.GetAsync("200");
        Assert.Equal(prior.Banner,visible.Banner); Assert.True(CatalogPresentationService.NeedsPreparation(visible)); Assert.Equal(0,detector.Calls);
        Assert.True(await service.PrepareAsync("200"));
        var ready=await service.GetAsync("200"); Assert.Equal(1,ready.BannerSchema); Assert.NotNull(ready.Banner!.Composition);
        Assert.Equal(prior.Work.CoverUrl,ready.Work.CoverUrl); Assert.Equal(prior.CoverDecision,ready.CoverDecision);
        Assert.Single(ready.BannerAssessments); Assert.Equal(1,detector.Calls); Assert.Equal(0,catalog.Calls);
        Assert.True(await service.PrepareAsync("200")); Assert.Equal(1,detector.Calls);
    }
    [Fact] public async Task BannerUpgradeFailurePreservesPreviouslyScreenedArtworkAndCanRetry()
    {
        var store=new Store(); await using var images=new Images();
        await using(var seed=Service(new Catalog(),images,store,new Detector())) Assert.True(await seed.PrepareAsync("200"));
        store.Value=store.Value! with {BannerSchema=0}; var prior=store.Value.Banner;
        var detector=new Detector {Available=false};
        await using var service=Service(new Catalog {Offline=true},images,store,detector);
        Assert.False(await service.PrepareAsync("200")); Assert.Equal(prior,(await service.GetAsync("200")).Banner);
        Assert.Equal(0,store.Value.BannerSchema); detector.Available=true;
        Assert.True(await service.PrepareAsync("200")); Assert.Equal(1,store.Value.BannerSchema);
    }
    [Theory][InlineData(false)][InlineData(true)]
    public async Task OldRelatedCacheEnrichmentNeverLeaksUnverifiedCardsOrRescreensValidCg(bool offline)
    {
        var store = new Store(); var images = new Images();
        await using (var before = Service(new Catalog(), images, store, new Detector())) Assert.True(await before.PrepareAsync("200"));
        var prior = store.Value!;
        var legacy = prior.Current with { Presentation = prior.Work.Presentation! with
        { RelatedWorks = [new(CatalogEntityKind.Work,"21","Unverified cached media",ImageUrl:"https://images.example/unverified")] } };
        store.Value = prior with { Current = legacy, Signature = CatalogPresentationService.Signature(legacy,prior.Reference) };
        var current = Artwork() with { Presentation = Artwork().Presentation! with { RelatedSchema = CatalogRelatedWorks.Schema,
            RelatedWorks = [new CatalogEntityRef(CatalogEntityKind.Work,"21","Verified game",ImageUrl:"https://images.example/game"){KunGalGalgameId="21"}] } };
        var catalog = new Catalog { Offline = offline, Current = current }; var detector = new Detector(); var nextImages = new Images();
        await using var after = Service(catalog,nextImages,store,detector);
        var loaded = await after.GetAsync("200"); Assert.NotNull(loaded.Banner); Assert.True(CatalogPresentationService.NeedsPreparation(loaded));
        Assert.Empty(CatalogLanguage.Project(loaded.Work).Presentation!.RelatedWorks);
        Assert.True(await after.PrepareAsync("200")); var upgraded = await after.GetAsync("200");
        Assert.Equal(prior.Banner!.Url,upgraded.Banner!.Url); Assert.Equal(0,detector.Calls); Assert.Equal(1,catalog.Calls);
        Assert.Equal(offline ? 0 : 1,CatalogLanguage.Project(upgraded.Work).Presentation!.RelatedWorks.Length);
        Assert.Equal(offline ? 1 : 2,nextImages.Calls); Assert.Equal("200",upgraded.Work.Id);
    }
    [Fact] public async Task ImportPreparationAndRepeatedDetailsHaveNoRepeatedCatalogImageOrDetectorWork()
    {
        var catalog=new Catalog();var store=new Store();var detector=new Detector();await using var images=new Images();
        await using var service=Service(catalog,images,store,detector);
        Assert.True(await service.PrepareAsync("200"));int api=catalog.Calls,pixels=images.Calls,inference=detector.Calls;
        var first=await service.GetAsync("200");Assert.True(first.Banner!.TextScreened);
        for(int i=0;i<6;i++) { Assert.Same(first,await service.GetAsync("200"));Assert.True(await service.PrepareAsync("200")); }
        Assert.Equal(api,catalog.Calls);Assert.Equal(pixels,images.Calls);Assert.Equal(inference,detector.Calls);Assert.True(store.Value!.Prepared);
    }
    [Fact] public async Task RestartUsesStoredPublicFactsAndCgEvenWhenOffline()
    {
        var store=new Store();await using var images=new Images();var detector=new Detector();
        await using(var before=Service(new Catalog(),images,store,detector))Assert.True(await before.PrepareAsync("200"));
        var offline=new Catalog{Offline=true};var nextDetector=new Detector();
        await using var after=Service(offline,images,store,nextDetector);
        var entry=await after.GetAsync("200");Assert.True(entry.Prepared);Assert.NotNull(entry.Work.Presentation);
        Assert.Equal(0,offline.Calls);Assert.Equal(0,nextDetector.Calls);Assert.True(await after.PrepareAsync("200"));
    }
    [Theory][InlineData(true)][InlineData(false)]
    public async Task TransientImageOrDetectorFailureCannotBecomePermanentNoCg(bool image)
    {
        var catalog=new Catalog();var store=new Store();var detector=new Detector{Available=image};await using var images=new Images{Missing=image};
        await using var service=Service(catalog,images,store,detector);
        Assert.False(await service.PrepareAsync("200"));Assert.False(store.Value!.Prepared);Assert.Null(store.Value.Banner);
        images.Missing=false;detector.Available=true;Assert.True(await service.PrepareAsync("200"));Assert.NotNull((await service.GetAsync("200")).Banner);
    }
    [Fact] public async Task ConfirmedDialogueRejectionIsCachedAsNoCgWithoutRepeatedInference()
    {
        var detector=new Detector{Dialogue=true};var catalog=new Catalog();var store=new Store();await using var images=new Images();
        await using var service=Service(catalog,images,store,detector);
        Assert.True(await service.PrepareAsync("200"));var entry=await service.GetAsync("200");Assert.True(entry.Prepared);Assert.Null(entry.Banner);
        int count=detector.Calls;Assert.True(await service.PrepareAsync("200"));Assert.Equal(count,detector.Calls);
    }
    [Fact]
    public async Task LegacyNoBannerCacheRetriesPromoAndRetainsTheSelectedArtworkAcrossRestart()
    {
        using var fixture = new ProfileFixture();
        using var profile = fixture.Open();
        var store = new IsolatedCatalogPresentationCache(profile.Configuration());
        var original = Artwork() with { CoverUrl = "https://example.org/poster" };
        await using var images = new Images();
        await using (var seed = Service(new Catalog { Current = original }, images, store, new Detector { Dialogue = true }))
            Assert.True(await seed.PrepareAsync("200"));
        var legacy = (await store.ReadAsync("200", default))!;
        var current = CachedCatalogWork.From(legacy.Work with { References = [new("vndb", "v26523", null)] });
        await store.WriteAsync(legacy with { Current = current, Signature = CatalogPresentationService.Signature(current, legacy.Reference) }, default);

        var catalog = new Catalog { Offline = true };
        var detector = new Detector();
        var promos = new PromoArtwork { Offline = true };
        await using (var service = new CatalogPresentationService(catalog, images, new Registry(), store, () => detector, new OnlineAuthContext(), promos))
        {
            var cached = await service.GetAsync("200");
            Assert.True(cached.Prepared);
            Assert.Null(cached.Banner);
            Assert.True(CatalogPresentationService.NeedsPreparation(cached));
            Assert.Equal(0, promos.Calls);
            Assert.False(await service.PrepareAsync("200"));
            Assert.True(CatalogPresentationService.NeedsPreparation(await service.GetAsync("200")));
            Assert.Equal(0, detector.Calls);

            promos.Offline = false;
            service.Queue(["200"], priority: true);
            await service.WhenIdleAsync();
            var updated = await service.GetAsync("200");
            Assert.False(CatalogPresentationService.NeedsPreparation(updated));
            Assert.Equal(ArtworkSources.VndbPromoKind, updated.Banner!.Kind);
            Assert.Equal(original.CoverUrl, updated.Work.CoverUrl);
            Assert.Equal(2, updated.BannerAssessments.Length);
            Assert.Equal(1, detector.Calls);
            Assert.Equal(2, promos.Calls);
        }
        await using var reopened = new CatalogPresentationService(catalog, images, new Registry(), store, () => detector, new OnlineAuthContext(), promos);
        Assert.True(await reopened.PrepareAsync("200"));
        Assert.Equal(PromoArtwork.Url, (await reopened.GetAsync("200")).Banner!.Url);
        Assert.Equal(0, catalog.Calls);
        Assert.Equal(1, detector.Calls);
        Assert.Equal(2, promos.Calls);
    }

    private sealed class PromoArtwork : IPublicArtworkSource
    {
        public const string Url = "https://t.vndb.org/cv/01/1.jpg";
        public int Calls;
        public bool Offline;
        public Task<ArtworkSupplement> ReadAsync(WorkMetadata work, CancellationToken cancellation)
            => Task.FromResult(new ArtworkSupplement([], true));
        public Task<ArtworkSupplement> ReadPromoAsync(WorkMetadata work, CancellationToken cancellation)
        {
            Calls++;
            return Task.FromResult(Offline ? new ArtworkSupplement([], false) :
                new ArtworkSupplement([new(Url, 1200, 675, ArtworkSources.VndbPromoKind)], true));
        }
    }

    [Fact] public async Task CancelledAnalysisNeverPublishesPreparedOrSelectedResult()
    {
        var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var detector=new Detector{Hold=async token=>{entered.SetResult();await Task.Delay(Timeout.Infinite,token);}};
        var store=new Store();await using var images=new Images();await using var service=Service(new Catalog(),images,store,detector);
        using var cancel=new CancellationTokenSource();var task=service.PrepareAsync("200",cancel.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));cancel.Cancel();await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>task);
        Assert.False(store.Value!.Prepared);Assert.Null(store.Value.Banner);
    }
    [Fact] public async Task RefreshSupersedesOlderInFlightArtworkAndNewSetIsScreened()
    {
        var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var detector=new Detector{Hold=async token=>{entered.TrySetResult();await release.Task.WaitAsync(token);}};
        var catalog=new Catalog();var store=new Store();await using var images=new Images();await using var service=Service(catalog,images,store,detector);
        var old=service.PrepareAsync("200");await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        catalog.Current=Artwork(url:"https://example.org/new");await service.GetAsync("200",refresh:true);
        release.SetResult();Assert.False(await old);Assert.Null((await service.GetAsync("200")).Banner);
        detector.Hold=null;Assert.True(await service.PrepareAsync("200"));Assert.Equal("https://example.org/new",(await service.GetAsync("200")).Banner!.Url);
    }
    [Fact] public async Task QueueDeduplicatesAndDisposeDrainsCancelledNativeWork()
    {
        var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var detector=new Detector{Hold=async token=>{entered.TrySetResult();await Task.Delay(Timeout.Infinite,token);}};
        await using var images=new Images();var service=Service(new Catalog(),images,new Store(),detector);
        service.Queue(["200","200","201"]);await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await service.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));Assert.Equal(1,detector.Calls);Assert.Equal(0,service.Metrics.Queued);
    }
    [Theory][InlineData("algorithm")][InlineData("foreign")][InlineData("signature")][InlineData("unsafe-choice")]
    public async Task StaleOrForeignCachedSelectionCannotBeReused(string fault)
    {
        var store=new Store();var catalog=new Catalog();await using var images=new Images();var detector=new Detector();
        await using(var first=Service(catalog,images,store,detector))Assert.True(await first.PrepareAsync("200"));
        store.Value=fault switch {
            "algorithm"=>store.Value! with{Algorithm="future"},
            "foreign"=>store.Value! with{Current=CachedCatalogWork.From(Artwork("201"))},
            "signature"=>store.Value! with{Signature=new string('0',64)},
            _=>store.Value! with{Banner=store.Value!.Banner! with{Url="https://example.org/foreign"}}
        };
        int calls=catalog.Calls;await using var next=Service(catalog,images,store,detector);
        Assert.False((await next.GetAsync("200")).Prepared);Assert.True(catalog.Calls>calls);
    }
    [Theory][InlineData(.12,.75,.70,.025,true)][InlineData(.69,.97,.29,.014,false)]
    [InlineData(.86,.96,.125,.025,false)][InlineData(.1,.96,.7,.02,true)][InlineData(.7,.7,.1,.025,true)]
    public void SmallSingleCornerCopyrightIsDistinctFromDialogueAndMenuRows(double x,double y,double w,double h,bool reject)
        => Assert.Equal(reject,ArtworkTextRules.ContainsOverlayText(new(true,[new(x,y,w,h,.99)],"synthetic")));
    [Fact] public void MultipleSmallFooterLabelsStillCountAsAnInterface()
        => Assert.True(ArtworkTextRules.ContainsOverlayText(new(true,[new(.69,.97,.12,.014,.99),new(.85,.97,.12,.014,.99)],"synthetic")));
    [Fact] public async Task DiskRoundTripRetainsPresentationAndCannotChangeLibrary()
    {
        using var fixture=new ProfileFixture();using var profile=fixture.Open();
        var store=new IsolatedCatalogPresentationCache(profile.Configuration());var before=File.ReadAllBytes(Path.Combine(fixture.Root,"library.json"));
        var catalog=new Catalog();await using var images=new Images();var detector=new Detector();
        await using(var first=Service(catalog,images,store,detector))Assert.True(await first.PrepareAsync("200"));
        await using var second=Service(new Catalog{Offline=true},images,store,new Detector());
        var entry=await second.GetAsync("200");Assert.Equal("kungal",entry.Work.Presentation!.IntroSource);Assert.NotNull(entry.Banner);
        Assert.Equal(before,File.ReadAllBytes(Path.Combine(fixture.Root,"library.json")));
    }
    [Theory][InlineData("identity")][InlineData("hash")][InlineData("version")]
    public async Task DamagedDiskEnvelopeIsAMissAndOriginalEvidenceIsRetained(string fault)
    {
        using var fixture=new ProfileFixture();using var profile=fixture.Open();var store=new IsolatedCatalogPresentationCache(profile.Configuration());
        await using var images=new Images();await using(var service=Service(new Catalog(),images,store,new Detector()))Assert.True(await service.PrepareAsync("200"));
        var file=Assert.Single(Directory.GetFiles(Path.Combine(fixture.Root,"native-catalog-"+CatalogPresentationService.Algorithm),"*.json"));
        var json=JsonNode.Parse(File.ReadAllText(file))!;
        if(fault=="identity")json["Identity"]="foreign";else if(fault=="hash")json["Hash"]="bad";else json["Version"]=2;
        File.WriteAllText(file,json.ToJsonString());var evidence=File.ReadAllBytes(file);
        Assert.Null(await store.ReadAsync("200",default));Assert.Equal(evidence,File.ReadAllBytes(file));
        if (fault is "identity" or "version")
        {
            var current = CachedCatalogWork.From(Artwork());
            await store.WriteAsync(new(CatalogPresentationService.Algorithm,current,null,CatalogPresentationService.Signature(current,null),null,false),default);
            Assert.Equal(evidence,File.ReadAllBytes(file));
        }
    }
}
