using System.Diagnostics;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using TheOrder.Application;
using TheOrder.Presentation;
using TheOrder.WinUI.Views;

namespace TheOrder.WinUI;

public sealed partial class MainWindow
{
    private MusicBrainzAlbumView? musicScrollFixture;
    private object? musicScrollPreviousBody;
    private async Task MusicScrollFixtureAsync(JsonElement request)
    {
        if (Program.Profile?.Manifest.ProfileKind != NativeProfileKind.Test || fixtureDirectory is null || !startupReady
            || Environment.GetEnvironmentVariable("THE_ORDER_NATIVE_MUSIC_SCROLL_TEST") != "1")
            throw new InvalidOperationException("Explicit isolated music Test required.");
        var action=request.GetProperty("value").GetString();
        if(action=="open")
        {
            if(musicScrollFixture is not null) throw new InvalidOperationException();
            await Navigate(WorkspacePage.Details,"ui-2"); NoticeBar.IsOpen=false; await Task.Delay(350);
            var nodes=P1Elements(detailsPage).OfType<FrameworkElement>().ToArray();
            var host=nodes.OfType<ContentControl>().Single(x=>x.Name=="RelatedMediaHost");
            musicScrollPreviousBody=host.Content;
            int count=request.TryGetProperty("count",out var requested)?requested.GetInt32():20;
            if(count is <1 or >4964)throw new InvalidDataException();
            var reader=new MusicScrollData(count);
            musicScrollFixture=new(); musicScrollFixture.Configure(reader,reader,images,null,_=>Task.FromException(new InvalidOperationException("No external fixture links")));
            nodes.Single(x=>x.Name=="DetailsRoot").Visibility=Visibility.Collapsed;
            nodes.Single(x=>x.Name=="CatalogHost").Visibility=Visibility.Collapsed;
            host.Content=musicScrollFixture;host.Visibility=Visibility.Visible;
            await musicScrollFixture.OpenAsync(new("900001",BangumiRelationKind.Music,"合成音乐滚动验证","Synthetic scrolling album","原声音乐",null),"合成测试作品",null,null,CancellationToken.None);
            await Task.Delay(500);
            if(!musicScrollFixture.IsLoaded || host.Visibility!=Visibility.Visible || JsonSerializer.SerializeToElement(musicScrollFixture.Evidence).GetProperty("state").GetString()!="Ready")
                throw new InvalidOperationException("Synthetic album did not reach Ready; scroll evidence would be invalid.");
        }
        var view=musicScrollFixture??throw new InvalidOperationException("Open music fixture first");
        var scroll=P1Elements(view).OfType<ScrollViewer>().Single(x=>x.Content is StackPanel panel
            && panel.Children.OfType<StackPanel>().Any(y=>AutomationProperties.GetAutomationId(y)=="MusicAlbumTracks"));
        if(action=="replay")
        {
            var watch=Stopwatch.StartNew();var rows=new List<object>();var intervals=new List<double>();double previous=0;int command=0;
            EventHandler<object> sample=(_,_)=>{var now=watch.Elapsed.TotalMilliseconds;if(previous>0)intervals.Add(now-previous);previous=now;
                rows.Add(new{ms=now,command,offset=scroll.VerticalOffset,extent=scroll.ExtentHeight,viewport=scroll.ViewportHeight,range=scroll.ScrollableHeight});};
            CompositionTarget.Rendering+=sample;
            try
            {
                scroll.ChangeView(null,0,null,true);await Task.Delay(250);
                for(int n=0;n<22;n++){command++;scroll.ChangeView(null,scroll.VerticalOffset+96,null,true);await Task.Delay(70);}
                command++;await Task.Delay(1200);
                for(int n=0;n<12;n++){command++;scroll.ChangeView(null,Math.Max(0,scroll.VerticalOffset-96),null,true);await Task.Delay(70);}
                command++;await Task.Delay(1200);
            }
            finally{CompositionTarget.Rendering-=sample;}
            await File.WriteAllTextAsync(Path.Combine(fixtureDirectory,"music-scroll-trace.json"),JsonSerializer.Serialize(new{rows,intervals,isolatedTest=true,productionAccess=false,view=view.Evidence}));
        }
        else if(action=="offset")
        {var offset=request.GetProperty("offset").GetDouble();if(!double.IsFinite(offset)||offset<0||offset>1000000)throw new InvalidDataException();scroll.ChangeView(null,offset,null,true);await Task.Delay(250);}
        else if(action=="disc")
        {var index=request.GetProperty("index").GetInt32();if(index is <1 or >3)throw new InvalidDataException();var disc=P1Elements(view).OfType<Expander>().Single(x=>AutomationProperties.GetAutomationId(x)=="MusicDisc-"+index);disc.IsExpanded=request.GetProperty("expanded").GetBoolean();await Task.Delay(350);}
        else if(action=="track")
        {var index=request.GetProperty("index").GetInt32();if(index is <1 or >100)throw new InvalidDataException();view.TrackForFixture(index);await Task.Delay(300);}
        else if(action=="more")
        {
            var more=P1Elements(view).OfType<Button>().Single(x=>AutomationProperties.GetAutomationId(x)=="MusicTracksMore-1");
            if(more.Visibility!=Visibility.Visible)throw new InvalidOperationException("No remaining tracks");
            var peer=new Microsoft.UI.Xaml.Automation.Peers.ButtonAutomationPeer(more);
            ((Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider)peer.GetPattern(Microsoft.UI.Xaml.Automation.Peers.PatternInterface.Invoke)).Invoke();
            await Task.Delay(350);
        }
        else if(action is "estimated" or "fixed")
        {
            // Negative control of the prior layout algorithm, only on the owned
            // synthetic Test view. Normal product views always use exact batches.
            scroll.ChangeView(null,0,null,true);
            foreach(var list in P1Elements(view).OfType<ItemsRepeater>())list.Layout=action=="estimated"?new StackLayout():new StableTrackLayout();
            await Task.Delay(500);
        }
        else if(action=="jump"){view.TracksForFixture(1);await Task.Delay(300);}
        else if(action=="close")
        {
            var nodes=P1Elements(detailsPage).OfType<FrameworkElement>().ToArray();var host=nodes.OfType<ContentControl>().Single(x=>x.Name=="RelatedMediaHost");
            host.Content=musicScrollPreviousBody;host.Visibility=Visibility.Collapsed;
            nodes.Single(x=>x.Name=="DetailsRoot").Visibility=Visibility.Visible;view.Dispose();musicScrollFixture=null;musicScrollPreviousBody=null;
        }
        else if(action is not ("open" or "inspect" or "replay" or "offset" or "disc" or "track" or "jump" or "more" or "estimated" or "fixed"))throw new InvalidDataException();
        await File.WriteAllTextAsync(Path.Combine(fixtureDirectory,"music-scroll-evidence.json"),JsonSerializer.Serialize(new{action,view=view.Evidence,extent=scroll.ExtentHeight,range=scroll.ScrollableHeight,offset=scroll.VerticalOffset,viewport=scroll.ViewportHeight,isolatedTest=true,productionAccess=false}));
    }
    private sealed class MusicScrollData(int count):IBangumiSubjectDetailsReader,IMusicBrainzReader
    {
        private const string Release="11111111-2222-4333-8444-555555555555",Group="22222222-3333-4444-8555-666666666666";
        private static MusicBrainzReleaseCandidate Candidate=>new(Release,Group,"合成音乐滚动验证","合成音乐人","2004-08-13","JP","Official",null,[],3,null);
        private MusicBrainzAlbum Album=>new(Candidate,"合成音乐滚动验证","Album",["Soundtrack"],Enumerable.Range(1,3).Select(d=>new MusicBrainzDisc(d,"","CD",Enumerable.Range(1,d==1?count:d==2?17:19).Select(n=>new MusicBrainzTrack(n,n.ToString(),n%7==0?"用于换行验证的长曲目名称：在雨季结束之前寻找失去名字的旅人 / Long track title for scrolling regression":"合成曲目 "+n,"合成音乐人",123000,null,[new("composer","合成作曲者",null)])).ToArray())).ToArray(),[],null,DateTimeOffset.UtcNow);
        public Task<BangumiSubjectDetailsResult> LoadAsync(string id,BangumiRelationKind kind,bool refresh,CancellationToken cancellation)=>Task.FromResult(new BangumiSubjectDetailsResult(new(id,kind,"合成音乐滚动验证","Synthetic scrolling album",null,string.Join("\n\n",Enumerable.Repeat("隔离测试简介，用于核对曲目后方阅读区域。此数据不会写入正式书架。",5)),"2004-08-13",null,null,null,null,DateTimeOffset.UtcNow),false,false,null));
        public Task<MusicBrainzMatchResult> MatchAsync(MusicAlbumSource source,bool refresh,CancellationToken cancellation)=>Task.FromResult(new MusicBrainzMatchResult(new([Candidate],1,false,null,null),null,Release,false,false,null));
        public Task<MusicBrainzAlbumResult> LoadAlbumAsync(string id,bool refresh,CancellationToken cancellation)=>Task.FromResult(new MusicBrainzAlbumResult(Album,false,false,null));
        public Task<MusicBrainzSearchResult> EditionsAsync(string id,CancellationToken cancellation)=>Task.FromResult(new MusicBrainzSearchResult([Candidate],1,false,null,null));
        public Task ConfirmAsync(MusicAlbumSource source,MusicBrainzAlbum album,CancellationToken cancellation)=>Task.FromException(new InvalidOperationException("No fixture identity writes"));
    }
}
