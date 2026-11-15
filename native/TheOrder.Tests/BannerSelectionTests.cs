using System.Collections.Immutable;
using TheOrder.Application;
using TheOrder.Domain;
using TheOrder.Infrastructure.Legacy;
using Xunit;
using static TheOrder.Tests.RecognitionBoundaryTests;
namespace TheOrder.Tests;

public sealed class BannerSelectionTests
{
    private static RasterPixels Raster(int width,int height,Func<int,int,(byte R,byte G,byte B)> color)
    {
        var bytes=new byte[width*height*4];for(int y=0;y<height;y++)for(int x=0;x<width;x++){var c=color(x,y);int p=(y*width+x)*4;bytes[p]=c.B;bytes[p+1]=c.G;bytes[p+2]=c.R;bytes[p+3]=255;}return new(width,height,bytes);
    }
    [Fact] public void PortraitAndTinyImagesAreExcludedAndRequestsStayBounded()
    {
        var art=Enumerable.Range(0,64).Select(i=>new CatalogArt("https://example.org/"+i,1280,720,"screenshot")).ToImmutableArray();
        var work=Work() with { Presentation=new("curated",art,[],[],[],[],[],[],[]),Covers=[new("portrait","https://example.org/poster",900,1200,99999,null),new("small","https://example.org/small",400,200,99999,null)] };
        var candidates=BannerSelection.Candidates(work).ToArray(); Assert.Equal(BannerSelection.MaximumPreviews,candidates.Length);
        Assert.DoesNotContain(candidates,a=>a.Url.EndsWith("poster")||a.Url.EndsWith("small"));Assert.Equal(work.Id,Work().Id);
    }
    [Theory][InlineData(4.2)][InlineData(5.0)][InlineData(0.7)]
    public void HeavyCropKeepsWholeSceneInsteadOfMagnifyingAndRemovingFaces(double aspect)
    {
        var image=Raster(160,90,(_,_)=>((byte)180,(byte)100,(byte)80));var original=image.Bgra.ToArray();
        var crop=BannerSelection.Plan(image,aspect);Assert.True(crop.FitEntire);Assert.Same(image,BannerSelection.Crop(image,crop));Assert.Equal(original,image.Bgra);
    }
    [Theory][InlineData(1.7)][InlineData(2.0)][InlineData(2.4)]
    public void GentleCropRemainsInsideSourceAndDoesNotModifyIt(double aspect)
    {
        var image=Raster(160,90,(x,y)=>((byte)x,(byte)y,(byte)90));var original=image.Bgra.ToArray();
        var crop=BannerSelection.Plan(image,aspect);Assert.False(crop.FitEntire);Assert.InRange(crop.X,0,image.Width-crop.Width);Assert.InRange(crop.Y,0,image.Height-crop.Height);
        var result=BannerSelection.Crop(image,crop);Assert.Equal(crop.Width*crop.Height*4,result.Bgra.Length);Assert.Equal(original,image.Bgra);
        Assert.InRange(result.Width/(double)result.Height,aspect-.04,aspect+.04);
    }
    [Fact] public void FlatWhiteAndTextInterfaceDoNotOutrankUsableColorSceneAtSameResolution()
    {
        var scene=Raster(160,90,(x,y)=>((byte)(130+x/2),(byte)(70+y),(byte)(50+x/3)));
        var white=Raster(160,90,(_,_)=>((byte)255,(byte)255,(byte)255));
        var ui=Raster(160,90,(x,y)=>x%12<2||y%12<2?((byte)0,(byte)0,(byte)0):((byte)255,(byte)255,(byte)255));
        Assert.True(BannerSelection.Score(scene,1280,720)>BannerSelection.Score(white,1280,720));Assert.True(BannerSelection.Score(scene,1280,720)>BannerSelection.Score(ui,1280,720));
    }
    [Fact] public async Task BrokenArtworkDoesNotHideAnotherValidImageAndNoForeignPosterIsChosen()
    {
        var good=Raster(160,90,(_,_)=>((byte)180,(byte)100,(byte)80));
        await using var images=new FakeImages(key=>key!.EndsWith("bad")?new(null,"unavailable"):new(good,"loaded"));
        var work=Work() with { Presentation=new("curated",[new("https://example.org/bad",1280,720,"banner"),new("https://example.org/good",1280,720,"screenshot")],[],[],[],[],[],[],[]) };
        var choice=await BannerSelection.SelectAsync(work,images,new Registry(),default);Assert.Equal("https://example.org/good",choice!.Url);Assert.Equal(2,images.Calls);
    }
    [Fact] public async Task CancellationStopsAnalysisBeforeMoreImagesAreRequested()
    {
        using var cancel=new CancellationTokenSource();await using var images=new FakeImages(_=>{cancel.Cancel();return new(null,"cancelled");});
        var work=Work() with { Presentation=new("curated",[new("https://example.org/1",1280,720,"banner"),new("https://example.org/2",1280,720,"screenshot")],[],[],[],[],[],[],[]) };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>BannerSelection.SelectAsync(work,images,new Registry(),cancel.Token));Assert.Equal(1,images.Calls);
    }
    [Fact] public void InvalidCropCannotOverflowBoundsOrExposeSourceBuffer()
    {
        var image=Raster(160,90,(_,_)=>((byte)180,(byte)100,(byte)80));
        Assert.Throws<ArgumentOutOfRangeException>(()=>BannerSelection.Crop(image,new(int.MaxValue,0,2,2,false)));
        Assert.Throws<ArgumentOutOfRangeException>(()=>BannerSelection.Crop(image,new(0,0,int.MaxValue,2,false)));
    }
    [Fact] public void PreviewSlotsIncludeTheEndOfLargeBatchesWithoutDependingOnWorkIdentity()
    {
        var art=Enumerable.Range(0,64).Select(i=>new CatalogArt("https://example.org/scene-"+i,1280,720,"screenshot")).ToImmutableArray();
        var work=Work() with {Presentation=new("curated",art,[],[],[],[],[],[],[])};
        var candidates=BannerSelection.Candidates(work).ToArray();
        Assert.Equal(BannerSelection.MaximumPreviews,candidates.Length);Assert.Contains(candidates,a=>a.Url.EndsWith("scene-63"));Assert.Equal(BannerSelection.MaximumPreviews,candidates.DistinctBy(a=>a.Url).Count());
        Assert.Equal(candidates,BannerSelection.Candidates(work with {Id="998"}).ToArray());
    }
    [Fact] public void CompleteLandscapeBackdropHasNoBlankSeamAndNeverWritesSourcePixels()
    {
        var image=Raster(160,90,(x,y)=>((byte)(60+x/2),(byte)(80+y/2),(byte)150));var original=image.Bgra.ToArray();
        var crop=BannerSelection.Plan(image,4.5);var backdrop=BannerSelection.ComposeBackdrop(image,crop,4.5);
        Assert.True(crop.FitEntire);Assert.NotSame(image,backdrop);Assert.InRange(backdrop.Width/(double)backdrop.Height,4.4,4.6);Assert.Equal(original,image.Bgra);
        Assert.All(Enumerable.Range(0,backdrop.Width*backdrop.Height),i=>Assert.Equal(255,backdrop.Bgra[i*4+3]));
        Assert.True(backdrop.Bgra[0]>0);Assert.True(backdrop.Bgra[(backdrop.Width-1)*4]>0);
        Assert.InRange(Math.Max(backdrop.Width,backdrop.Height),1,1200);
    }
    [Fact] public void UnsafeBackdropCropCannotBypassBoundsValidation()
    {
        var image=Raster(160,90,(_,_)=>((byte)180,(byte)100,(byte)80));
        Assert.Throws<ArgumentOutOfRangeException>(()=>BannerSelection.ComposeBackdrop(image,new(int.MaxValue,0,2,2,true),4));
    }
    [Fact] public void BrightMenuLabelsDoNotForceDarkCinematicArtworkIntoFallback()
    {
        var scene=Raster(256,144,(x,y)=>((byte)(30+x/8),(byte)(35+y/8),(byte)(70+x/5)));
        var menu=Raster(256,144,(x,y)=>y%20 is >4 and <9 && x%24 is >4 and <10?((byte)255,(byte)255,(byte)255):((byte)60,(byte)100,(byte)75));
        Assert.True(BannerSelection.Score(scene,640,360)>BannerSelection.Score(menu,1280,720));
    }
    [Fact] public async Task AllUnusableArtworkLeavesNeutralBackgroundInsteadOfChoosingAWhitePage()
    {
        var white=Raster(160,90,(_,_)=>((byte)255,(byte)255,(byte)255));await using var images=new FakeImages(_=>new(white,"loaded"));
        var work=Work() with {Presentation=new("curated",[new("https://example.org/white",1280,720,"banner")],[],[],[],[],[],[],[])};
        Assert.Null(await BannerSelection.SelectAsync(work,images,new Registry(),default));
    }
    [Fact] public void NormalCgAspectWinsOverAnEquallyUsableUltraWideStrip()
    {
        var cg = Raster(256,144,(x,y)=>((byte)(90+x/4),(byte)(70+y/3),(byte)150));
        var strip = Raster(256,80,(x,y)=>((byte)(90+x/4),(byte)(70+y/2),(byte)150));
        Assert.True(BannerSelection.Score(cg,1280,720) > BannerSelection.Score(strip,1920,600));
        var crop = BannerSelection.Plan(cg,BannerSelection.PreferredAspect);
        Assert.Equal(0,crop.X); Assert.Equal(0,crop.Y);
        Assert.Equal(cg.Width,crop.Width); Assert.Equal(cg.Height,crop.Height);
        Assert.Same(cg,BannerSelection.ComposeBackdrop(cg,crop,BannerSelection.PreferredAspect));
    }
    [Theory][InlineData(false)][InlineData(true)]
    public async Task OfficialLibraryLandscapeRanksAtDesignedAspectAndStillRejectsDialogue(bool dialogue)
    {
        var hero = Raster(256,80,(x,y)=>((byte)(90+x/4),(byte)(70+y/2),(byte)150));
        var cg = Raster(256,144,(x,y)=>((byte)(90+x/4),(byte)(70+y/3),(byte)150));
        await using var images = new FakeImages(key=>new(key!.EndsWith("hero")?hero:cg,"loaded"),BannerSelection.TextAnalysisEdge);
        var work = Work() with {Presentation=new("curated",[new("https://example.org/hero",1920,600,"steam-hero"),new("https://example.org/cg",700,394,"screenshot")],[],[],[],[],[],[],[])};
        await using var detector = new LibraryHeroDetector(dialogue);
        var choice = await BannerSelection.SelectAsync(work,images,new Registry(),default,detector);
        Assert.Equal(dialogue?"https://example.org/cg":"https://example.org/hero",choice!.Url);
        Assert.True(choice.TextScreened); Assert.Equal(work.Id,choice.SourceWorkId); Assert.Equal(2,images.Calls);
    }
    [Theory]
    [InlineData(ArtworkSources.VndbPromoKind, "title", true)]
    [InlineData("vndb-screenshot", "title", false)]
    [InlineData(ArtworkSources.VndbPromoKind, "dialogue", false)]
    [InlineData(ArtworkSources.VndbPromoKind, "menu", false)]
    public async Task OnlyVerifiedPromoAllowsAGroupedTitle(string kind, string text, bool eligible)
    {
        var raster = Raster(256, 144, (x, y) => ((byte)(90 + x / 4), (byte)(70 + y / 3), (byte)150));
        await using var images = new FakeImages(_ => new(raster, "loaded"), BannerSelection.TextAnalysisEdge);
        var work = Work() with
        {
            Presentation = new("curated", [new("https://example.org/wide", 1200, 675, kind)], [], [], [], [], [], [], [])
        };
        await using var detector = new RegionsDetector(text switch
        {
            "title" => [new(.342, .787, .371, .053, .99), new(.383, .931, .236, .021, .98)],
            "dialogue" => [new(.12, .76, .70, .026, .99)],
            _ => [new(.342, .30, .371, .053, .99), new(.72, .76, .16, .024, .99)]
        });
        var result = await BannerSelection.AnalyzeAsync(work, null, images, new Registry(), default, detector);
        Assert.Equal(eligible, result.Choice is { TextScreened: true });
        Assert.Equal(eligible ? "eligible" : "overlay-text", Assert.Single(result.Assessments).Result);
    }

    private sealed class RegionsDetector(ImmutableArray<ArtworkTextRegion> regions) : IArtworkTextDetector
    {
        public Task<ArtworkTextDetection> AnalyzeAsync(RasterPixels pixels, CancellationToken cancellation)
            => Task.FromResult(new ArtworkTextDetection(true, regions, "fixture"));
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class LibraryHeroDetector(bool dialogue):IArtworkTextDetector
    {
        public Task<ArtworkTextDetection> AnalyzeAsync(RasterPixels pixels,CancellationToken cancellation)=>Task.FromResult(new ArtworkTextDetection(true,
            dialogue&&pixels.Width/(double)pixels.Height>2.6?[new(.1,.7,.8,.1,.9)]:[],"fixture"));
        public ValueTask DisposeAsync()=>ValueTask.CompletedTask;
    }
    private static WorkMetadata RelatedCurrent(string relation = "remake_of") => Work("333", "Synthetic Edition") with {
        Presentation = new("curated", [new("https://example.org/current",1280,720,"screenshot")], [], [], [], [], [], [], [new(CatalogEntityKind.Work,"90","Synthetic Original",Relation:relation)]) };
    private static WorkMetadata Original() => Work("90", "Synthetic Original") with {
        Presentation = new("curated", [new("https://example.org/original",1280,720,"screenshot")], [], [], [], [], [], [], []) };
    [Fact] public async Task OwnUsableCgWinsAndNoOriginalImageIsRequested()
    {
        var scene = Raster(160,90,(_,_)=>((byte)180,(byte)100,(byte)80));
        await using var images = new FakeImages(_=>new(scene,"loaded"));
        var work = RelatedCurrent(); var original = Original();
        var choice = await BannerSelection.SelectAsync(work,original,images,new Registry(),default);
        Assert.Equal("333",choice!.SourceWorkId); Assert.Null(choice.ReferenceTitle);
        Assert.Equal("https://example.org/current",choice.Url); Assert.Equal(1,images.Calls);
    }
    [Theory][InlineData("remake_of")][InlineData("remaster_of")][InlineData("port_of")][InlineData("alternative_version")]
    public async Task MissingOrUnusableOwnCgCanBorrowExplicitVersionWithoutChangingLibrary(string relation)
    {
        var scene = Raster(160,90,(_,_)=>((byte)180,(byte)100,(byte)80));
        var white = Raster(160,90,(_,_)=>((byte)255,(byte)255,(byte)255));
        await using var images = new FakeImages(key=>new(key!.EndsWith("current")?white:scene,"loaded"));
        var work = RelatedCurrent(relation); var original = Original();
        var entry = Entry("Personal name") with { Metadata=work, Notes="My notes", TotalPlaySeconds=123 };
        var codec = new NativeLibraryCodec(); var before=codec.Encode([entry],Clock);
        var choice = await BannerSelection.SelectAsync(work,original,images,new Registry(),default);
        Assert.Equal("90",choice!.SourceWorkId); Assert.Equal(original.Title,choice.ReferenceTitle);
        Assert.Equal("https://example.org/original",choice.Url); Assert.Equal(2,images.Calls);
        Assert.Equal(before,codec.Encode([entry],Clock)); Assert.Equal("333",entry.Metadata.Id);
    }
    [Theory][InlineData("same_series")][InlineData("fandisc_of")][InlineData("sequel_of")][InlineData("shares_character")]
    public async Task OtherRelationsCannotBorrowAnUnrelatedCg(string relation)
    {
        await using var images = new FakeImages(_=>new(null,"unavailable"));
        Assert.Null(await BannerSelection.SelectAsync(RelatedCurrent(relation),Original(),images,new Registry(),default));
        Assert.Equal(1,images.Calls);
    }
    [Fact] public async Task AmbiguousVersionsAndWrongReferenceIdsAreRejectedBeforeBorrowing()
    {
        await using var images = new FakeImages(_=>new(null,"unavailable"));
        var current=RelatedCurrent();
        Assert.Null(await BannerSelection.SelectAsync(current,Original() with {Id="91"},images,new Registry(),default));
        var ambiguous = current with {Presentation=current.Presentation! with {RelatedWorks=current.Presentation.RelatedWorks.Add(new(CatalogEntityKind.Work,"91","Other",Relation:"remake_of"))}};
        Assert.Null(await BannerSelection.SelectAsync(ambiguous,Original(),images,new Registry(),default));
        Assert.Equal(2,images.Calls);
    }
    [Fact] public async Task BorrowingRetainsBoundedPreviewsAndCancellation()
    {
        var current=RelatedCurrent();var old=Original() with {Presentation=Original().Presentation! with {Art=Enumerable.Range(0,64).Select(i=>new CatalogArt("https://example.org/old-"+i,1280,720,"screenshot")).ToImmutableArray()}};
        await using var images = new FakeImages(_=>new(null,"unavailable"));
        Assert.Null(await BannerSelection.SelectAsync(current,old,images,new Registry(),default));Assert.Equal(1 + BannerSelection.MaximumPreviews,images.Calls);
        using var cancel=new CancellationTokenSource();
        await using var stopped = new FakeImages(_=>{cancel.Cancel();return new(null,"unavailable");});
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>BannerSelection.SelectAsync(current,old,stopped,new Registry(),cancel.Token));Assert.Equal(1,stopped.Calls);
    }
    private sealed class Registry:IRemoteCoverRegistry {public string? Register(string? url,string revision)=>url;}
    private sealed class FakeImages(Func<string?,ImageResult> load,int expectedEdge=256):IImagePipeline
    {
        public int Calls;public ImageMetrics Metrics=>new(Calls,Calls,0,0,0,0,0,0,0,0);
        public Task<ImageResult> LoadAsync(string? key,int edge,CancellationToken c){c.ThrowIfCancellationRequested();Calls++;Assert.Equal(expectedEdge,edge);return Task.FromResult(load(key));}
        public ValueTask DisposeAsync()=>ValueTask.CompletedTask;
    }
}
