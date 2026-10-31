using System.Collections.Immutable;
using TheOrder.Application;
using TheOrder.Presentation;
using Xunit;

namespace TheOrder.Tests;

public sealed class WalkthroughFlowGraphTests
{
    private static WalkthroughText T(string text)=>new(text,text,false);
    private static WalkthroughNode N(string kind,string key)=>new(kind,key,T(key));
    private static WalkthroughStep S(string kind,string? save=null)=>new(kind,T(kind),save,[]);
    private static WalkthroughDocument D(params WalkthroughSegment[] routes)=>new(new("1","1","native",null,"ja","",0,0,routes.Length,""),false,[],[N("entry","start"),N("save","01"),N("save","02"),N("ending","end")],[..routes]);
    private static WalkthroughSegment R(string key,WalkthroughNode? from=null,WalkthroughNode? to=null,params WalkthroughStep[] steps)=>new(key,"c1",from??N("entry","start"),to??N("ending","end"),T(""),[..steps]);
    [Fact] public void SavesSplitStepsIntoActualDecisionEdges()
    {var graph=WalkthroughFlowGraph.Build(D(R("a",steps:[S("choice"),S("save","01"),S("choice"),S("save","02"),S("action")])));Assert.Equal(4,graph.Nodes.Length);Assert.Equal(new[]{2,2,1},graph.Edges.Select(e=>e.Count));Assert.Equal(new[]{0,2,4},graph.Edges.Select(e=>e.Start));Assert.Equal(new[]{1,1,0},graph.Edges.Select(e=>e.Choices));}
    [Fact] public void SharedSaveJoinsDifferentRoutesWithoutSharingStepRanges()
    {var graph=WalkthroughFlowGraph.Build(D(R("a",steps:[S("save","01"),S("choice")]),R("b",N("save","01"),steps:[S("load","01"),S("choice")])));var save=Assert.Single(graph.Nodes.Where(n=>n.Kind=="save"));Assert.Contains(graph.Edges,e=>e.RouteIndex==1&&e.From==save.Id&&e.Count==2&&e.Start==0);}
    [Fact] public void NodeTypeIsPartOfIdentity()
    {var graph=WalkthroughFlowGraph.Build(D(R("a",N("entry","same"),N("ending","same"),S("choice"))));Assert.Equal(2,graph.Nodes.Length);Assert.NotEqual(graph.Edges[0].From,graph.Edges[0].To);}
    [Fact] public void CollectionEndpointsRemainSeparateOccurrences()
    {var graph=WalkthroughFlowGraph.Build(D(R("a",to:N("collect","")),R("b",to:N("collect",""))));Assert.Equal(2,graph.Nodes.Count(n=>n.Kind=="collect"));}
    [Fact] public void ExplicitUnlockKeyCreatesOnlyADashedPrerequisiteEdge()
    {var from=N("entry","restart") with {Requirement="end",RequirementKey="end"};var graph=WalkthroughFlowGraph.Build(D(R("a",from)));var edge=Assert.Single(graph.Edges.Where(e=>e.Kind=="unlock"));Assert.Equal(0,edge.Count);Assert.Equal(0,edge.Choices);}
    [Fact] public void RepeatedSaveDoesNotLoseAnySteps()
    {var graph=WalkthroughFlowGraph.Build(D(R("a",steps:[S("save","01"),S("save","01"),S("choice")])));Assert.Equal(3,graph.Edges.Sum(e=>e.Count));Assert.DoesNotContain(graph.Edges,e=>e.From==e.To);}
    [Fact] public void ProjectionIsBoundedAndReportsIncompleteDiagram()
    {var routes=Enumerable.Range(0,900).Select(i=>R("r"+i,N("entry","s"+i),N("ending","t"+i),S("choice"))).ToArray();var graph=WalkthroughFlowGraph.Build(D(routes));Assert.True(graph.Truncated);Assert.InRange(graph.Nodes.Length,1,WalkthroughFlowGraph.MaximumNodes);Assert.InRange(graph.Edges.Length,1,WalkthroughFlowGraph.MaximumEdges);}
    [Theory]
    [InlineData("セーブ01","存档 01")]
    [InlineData("セーブ０２","存档 02")]
    [InlineData("SAVE 03","存档 03")]
    [InlineData("存檔：04","存档 04")]
    [InlineData("存档 05","存档 05")]
    [InlineData("After the concert","存档 After the concert")]
    [InlineData("1-12","存档 1-12")]
    [InlineData("SAVE 2-103","存档 2-103")]
    [InlineData("存档 · １２-３０","存档 12-30")]
    public void SaveCaptionsRemoveOnlyTheRecognizedRepeatedTerm(string source,string expected)
    {Assert.Equal(expected,WalkthroughFlowGraph.SaveLabel(source));}
    [Fact] public void SaveCaptionNormalizationDoesNotChangeIdentityOrOriginalText()
    {
        var from=new WalkthroughNode("save","slot-one",T("セーブ01"));var graph=WalkthroughFlowGraph.Build(D(R("a",from)));
        var node=Assert.Single(graph.Nodes.Where(n=>n.Kind=="save"));Assert.Equal("存档 01",node.Label);Assert.Equal("セーブ01",node.Original);
        Assert.Equal("slot-one",from.Key);Assert.Equal("セーブ01",from.Label.Display);
    }
    [Fact] public void PrimaryAndSecondaryEndingHierarchyUsesExplicitTypesAndKeepsCollectionsQuiet()
    {
        Assert.True(WalkthroughFlowGraph.NodeHeight("ending","heroine")>WalkthroughFlowGraph.NodeHeight("ending","normal"));
        Assert.True(WalkthroughFlowGraph.NodeHeight("ending","normal")>WalkthroughFlowGraph.NodeHeight("ending","after"));
        Assert.Equal(0,WalkthroughFlowGraph.EndingWeight("collect",null));Assert.Equal(2,WalkthroughFlowGraph.EndingWeight("ending",null));
    }
    [Fact] public void SharedDestinationsUseNamedReferencesWithoutMergingDistinctStepRanges()
    {
        var graph=WalkthroughFlowGraph.Build(D(R("a",steps:[S("choice")]),R("b",steps:[S("action"),S("choice")])));
        Assert.Equal(new[]{1,2},graph.Edges.Select(e=>e.Count));Assert.Equal(new[]{0,1},graph.Edges.Select(e=>e.RouteIndex));
        Assert.Contains(graph.Nodes,n=>n.Kind=="reference"&&n.Label.StartsWith("转至 ",StringComparison.Ordinal));
        Assert.All(graph.Edges.GroupBy(e=>e.To),g=>Assert.Single(g));
        Assert.All(graph.Nodes.Where(n=>n.Kind!="save"),n=>Assert.InRange(graph.Edges.Count(e=>e.From==n.Id),0,1));
    }
    [Fact] public void CyclicSaveReferencesBecomeTerminalsInsteadOfUnboundedExpansion()
    {
        var graph=WalkthroughFlowGraph.Build(D(R("a",N("save","01"),N("save","02"),S("choice")),R("b",N("save","02"),N("save","01"),S("choice"))));
        Assert.Equal(2,graph.Edges.Length);Assert.Equal(2,graph.Nodes.Count(n=>n.Kind=="save"));Assert.Single(graph.Nodes.Where(n=>n.Kind=="reference"));
        Assert.False(graph.Truncated);Assert.Equal(2,graph.Edges.Sum(e=>e.Count));
    }
}
