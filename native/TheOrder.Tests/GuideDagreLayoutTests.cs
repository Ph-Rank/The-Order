using System.Collections.Immutable;
using TheOrder.Application;
using TheOrder.Presentation;
using Xunit;
namespace TheOrder.Tests;
public sealed class GuideDagreLayoutTests
{
    [Fact] public async Task SharedDagreProducesBoundedGeometryAndKeepsOriginalStepRanges()
    {
        var doc=WalkthroughTests.Document();var flow=await new GuideDagreLayout().CreateAsync(doc,default);Assert.NotNull(flow);
        Assert.Equal(4,flow.Nodes.Length);Assert.Single(flow.Nodes.Where(n=>n.Kind=="reference"));Assert.Equal("存档 01",flow.Nodes.Single(n=>n.Kind=="save").Label);Assert.Equal(48,flow.Nodes.Single(n=>n.Kind=="save").Height);
        Assert.All(flow.Nodes,n=>Assert.True(float.IsFinite(n.X)&&n.Width>0&&n.Height>0));
        Assert.Equal(3,flow.Edges.Length);Assert.All(flow.Edges,e=>Assert.True(e.Points.Length>=2));
        var bytes=GuideOverlayWire.Encode(GuideOverlayDocument.From("g","test",doc) with{Flow=flow});Assert.True(bytes.Length<GuideOverlayWire.MaximumBytes);
        Assert.Equal(4,GuideOverlayWire.Version);
    }
    [Fact] public async Task LayoutHonorsCancellationBeforeDoingWork()
    {using var cancellation=new CancellationTokenSource();cancellation.Cancel();await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>new GuideDagreLayout().CreateAsync(WalkthroughTests.Document(),cancellation.Token));}
    [Fact] public async Task EveryArrowTouchesTheActualSaveDiamondInsteadOfItsBoundingBox()
    {
        var flow=await new GuideDagreLayout().CreateAsync(WalkthroughTests.Document(),default);Assert.NotNull(flow);
        foreach(var edge in flow.Edges)
            foreach(var endpoint in new[]{(node:flow.Nodes[edge.From],point:edge.Points[0]),(node:flow.Nodes[edge.To],point:edge.Points[^1])})
                if(endpoint.node.Kind=="save")
                    Assert.InRange(Math.Abs(endpoint.point.X-endpoint.node.X)/(endpoint.node.Width/2)+Math.Abs(endpoint.point.Y-endpoint.node.Y)/(endpoint.node.Height/2),.999f,1.001f);
    }
    [Fact] public async Task OrthogonalSpansHaveSmallRoundedCornersAndContinuousTangentDirections()
    {
        var flow=await new GuideDagreLayout().CreateAsync(WalkthroughTests.Document(),default);Assert.NotNull(flow);
        foreach(var edge in flow.Edges){Assert.Equal(0,(edge.Points.Length-1)%3);
            int turns=0;for(int i=0;i+3<edge.Points.Length;i+=3){var a=edge.Points[i];var b=edge.Points[i+3];
                // A cubic is either a straight axis-aligned span, or a <=14px fillet.
                if(Math.Abs(a.X-b.X)>.001&&Math.Abs(a.Y-b.Y)>.001){turns++;Assert.InRange(Math.Abs(a.X-b.X),0,14.001);Assert.InRange(Math.Abs(a.Y-b.Y),0,14.001);}}
            Assert.InRange(turns,0,1);
            for(int i=3;i+2<edge.Points.Length;i+=3){var join=edge.Points[i];var previous=edge.Points[i-1];var next=edge.Points[i+1];
                float ax=join.X-previous.X,ay=join.Y-previous.Y,bx=next.X-join.X,by=next.Y-join.Y;
                Assert.InRange(Math.Abs(ax*by-ay*bx),0,.01);Assert.True(ax*bx+ay*by>0);
            }
            var source=flow.Nodes[edge.From];var target=flow.Nodes[edge.To];
            Assert.Equal((source.Y+target.Y)/2,edge.Label!.Y,3);
            Assert.Equal(target.X,edge.Points[^1].X,3);Assert.Equal(target.Y-target.Height/2,edge.Points[^1].Y,3);
            if(source.Kind!="save"){Assert.Equal(source.X,edge.Points[0].X,3);Assert.Equal(source.Y+source.Height/2,edge.Points[0].Y,3);Assert.Equal(source.X,edge.Points[1].X,3);}
        }
    }
    [Theory] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)] [InlineData(6)] [InlineData(7)]
    public async Task SaveExitPortsFollowFanCountAndAllTargetsAreEnteredFromAbove(int exits)
    {
        var save=new WalkthroughNode("save","s",new("SAVE 01","SAVE 01",false));
        var endings=Enumerable.Range(0,exits).Select(i=>new WalkthroughNode("ending","end"+i,new("END "+i,"END "+i,false))).ToImmutableArray();
        var original=WalkthroughTests.Document();
        var doc=original with{Nodes=endings.Insert(0,save),Segments=endings.Select((end,i)=>new WalkthroughSegment("r"+i,original.Sections[0].Key,save,end,new("","",false),[new("choice",new("Choose","Choose",false),null,[])])).ToImmutableArray()};
        var flow=await new GuideDagreLayout().CreateAsync(doc,default);Assert.NotNull(flow);
        var source=flow.Nodes.Single(n=>n.Kind=="save");int left=0,bottom=0,right=0;
        foreach(var edge in flow.Edges){var p=edge.Points[0];var target=flow.Nodes[edge.To];
            if(p.X<source.X-.01){left++;Assert.Equal(source.X-source.Width/2,p.X,3);Assert.Equal(source.Y,p.Y,3);Assert.True(edge.Points[1].X<p.X);}
            else if(p.X>source.X+.01){right++;Assert.Equal(source.X+source.Width/2,p.X,3);Assert.Equal(source.Y,p.Y,3);Assert.True(edge.Points[1].X>p.X);}
            else{bottom++;Assert.Equal(source.Y+source.Height/2,p.Y,3);Assert.Equal(p.X,edge.Points[1].X,3);Assert.True(edge.Points[1].Y>p.Y);}
            Assert.Equal(target.X,edge.Points[^1].X,3);Assert.Equal(target.Y-target.Height/2,edge.Points[^1].Y,3);Assert.Equal(target.X,edge.Points[^2].X,3);
            Assert.InRange(edge.Label!.X,0,flow.Width);Assert.InRange(edge.Label.Y,0,flow.Height);
        }
        Assert.Equal(exits/2,left);Assert.Equal(exits%2,bottom);Assert.Equal(exits/2,right);
    }
    [Fact] public void WireRejectsNonfiniteGeometryAndDanglingStepReferences()
    {
        var d=GuideOverlayDocument.From("g","test",WalkthroughTests.Document());
        var flow=new GuideFlowLayout(float.NaN,100,[],[],false);Assert.Throws<InvalidDataException>(()=>GuideOverlayWire.Encode(d with{Flow=flow}));
        flow=new(100,100,[new("save","存档 01",40,40,90,34)],[new(0,0,0,999,1,1,0,false,[new(1,1),new(2,2)])],false);
        Assert.Throws<InvalidDataException>(()=>GuideOverlayWire.Encode(d with{Flow=flow}));
    }
}
