using System.Collections.Immutable;
using TheOrder.Application;
using TheOrder.Presentation;
using Xunit;

namespace TheOrder.Tests;

public sealed class WalkthroughOverviewTests
{
    private static WalkthroughText Text(string value)=>new(value,value,false);
    private static WalkthroughSegment Segment(string key,string section="a",string fromKind="start",string from="START",string to="END",params string[] saves)=>
        new(key,section,new(fromKind,from,Text(from)),new("ending",to,Text(to)),Text(""),saves.Select(x=>new WalkthroughStep("save",Text(x),x,[])).ToImmutableArray());
    private static WalkthroughDocument Document(params WalkthroughSegment[] segments)=>new(new("1","1","native",null,"ja","",0,0,segments.Length,""),false,[],[],[..segments]);
    [Fact] public void UniqueSaveCreatesExplicitBranchWithinSection()
    {var map=WalkthroughOverview.Build(Document(Segment("first",saves:["01"]),Segment("branch",fromKind:"save",from:"01")));Assert.Equal(0,map[1].ParentIndex);Assert.Equal("save",Assert.Single(map[1].Sources).Kind);}
    [Fact] public void ContinuationUsesExactEndpointIdentity()
    {var map=WalkthroughOverview.Build(Document(Segment("first",to:"ending-one"),Segment("after",fromKind:"ending",from:"ending-one")));Assert.Equal(0,map[1].ParentIndex);Assert.Equal("continue",Assert.Single(map[1].Sources).Kind);}
    [Fact] public void ReusedSaveDoesNotInventSingleParent()
    {var map=WalkthroughOverview.Build(Document(Segment("first",saves:["01"]),Segment("second",saves:["01"]),Segment("branch",fromKind:"save",from:"01")));Assert.Null(map[2].ParentIndex);Assert.Equal(new[]{0,1},map[2].Sources.Select(x=>x.SourceIndex));}
    [Fact] public void CrossSectionReferenceRemainsNamedWithoutCrossingTreeLine()
    {var map=WalkthroughOverview.Build(Document(Segment("first",saves:["01"]),Segment("branch","b","save","01")));Assert.Null(map[1].ParentIndex);Assert.Equal(0,Assert.Single(map[1].Sources).SourceIndex);}
    [Fact] public void MissingSelfAndForwardReferencesCannotCreateCycle()
    {var map=WalkthroughOverview.Build(Document(Segment("first",fromKind:"save",from:"02",saves:["01"]),Segment("second",fromKind:"save",from:"01",saves:["02"]),Segment("missing",fromKind:"save",from:"absent")));Assert.Null(map[0].ParentIndex);Assert.Equal(1,Assert.Single(map[0].Sources).SourceIndex);Assert.Equal(0,map[1].ParentIndex);Assert.Empty(map[2].Sources);}
    [Fact] public void RepeatedSaveInSameSegmentIsOneReferenceAndDisplayLimitIsBounded()
    {var map=WalkthroughOverview.Build(Document(Segment("first",saves:["01","01"]),Segment("branch",fromKind:"save",from:"01")));Assert.Single(map[1].Sources);Assert.Single(WalkthroughOverview.Build(Document(Segment("one"),Segment("two")),1));Assert.Empty(WalkthroughOverview.Build(Document(Segment("one")),0));}
    [Fact] public void EntryAndEndingWithSameKeyAreDifferentIdentities()
    {var map=WalkthroughOverview.Build(Document(Segment("first",to:"START"),Segment("restart",fromKind:"entry",from:"START")));Assert.Empty(map[1].Sources);Assert.Null(map[1].ParentIndex);}
    [Fact] public void ExplicitSaveEndpointIsAProducerWithoutRepeatedSaveStep()
    {var first=Segment("first") with { To=new("save","01",Text("存档01")) };var map=WalkthroughOverview.Build(Document(first,Segment("branch",fromKind:"save",from:"01")));Assert.Equal(0,map[1].ParentIndex);Assert.Equal(0,Assert.Single(map[1].Sources).SourceIndex);}
}
