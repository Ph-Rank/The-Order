using System.Collections.Immutable;
using System.Text.Json;
using Jint;
using TheOrder.Application;

namespace TheOrder.Presentation;

public sealed class GuideDagreLayout : IGuideFlowLayout
{
    private static readonly Lazy<string> Script=new(()=> {
        using var stream=typeof(GuideDagreLayout).Assembly.GetManifestResourceStream("TheOrder.GuideFlow.dagre.js") ?? throw new InvalidOperationException();
        using var reader=new StreamReader(stream);return reader.ReadToEnd();
    });
    private static readonly Lazy<string> Routing=new(()=> {using var stream=typeof(GuideDagreLayout).Assembly.GetManifestResourceStream("TheOrder.GuideFlow.routing.js")??throw new InvalidOperationException();using var reader=new StreamReader(stream);return reader.ReadToEnd();});
    private const string LayoutScript="""
    const input=JSON.parse(guideInput);
    const g=new dagre.graphlib.Graph({multigraph:true}).setGraph({rankdir:'TB',nodesep:28,ranksep:36,edgesep:12,marginx:24,marginy:24}).setDefaultEdgeLabel(()=>({}));
    for(const n of input.nodes)g.setNode(n.id,{kind:n.kind,label:n.label,endingType:n.endingType,width:n.width,height:n.height});
    for(const e of input.edges)g.setEdge(e.from,e.to,{width:e.choices?90:72,height:e.choices?28:24,labelpos:'c',labeloffset:0},e.id);
    const routed=guideBuildRoutes(g,input.edges);
    JSON.stringify({width:routed.width,height:routed.height,nodes:input.nodes.map(n=>g.node(n.id)),edges:routed.edges});
    """;
    public Task<GuideFlowLayout?> CreateAsync(WalkthroughDocument document,CancellationToken cancellation)=>Task.Run(()=> {
        cancellation.ThrowIfCancellationRequested();
        var model=WalkthroughFlowGraph.Build(document);if(model.Nodes.IsEmpty)return null;
        // Approximate single-line font metrics without a desktop/GDI dependency.
        // Capped widths retain the original full labels for inspection/body reading.
        float Width(string text)=>Math.Clamp(text.EnumerateRunes().Sum(r=>r.Value>=0x2e80?13f:7f)+30,90,230);
        float SaveWidth(string text)=>Math.Clamp((text.EnumerateRunes().Sum(r=>r.Value>=0x2e80?13f:7f)+16)/.66f,104,360);
        var input=new {nodes=model.Nodes.Select(n=>new {id=n.Id,kind=n.Kind,label=n.Label,endingType=n.EndingType,width=n.Kind=="save"?SaveWidth(n.Label):WalkthroughFlowGraph.EndingWeight(n.Kind,n.EndingType)==0?Math.Clamp(n.Label.EnumerateRunes().Sum(r=>r.Value>=0x2e80?10.5f:6f)+22,76,160):Math.Max(WalkthroughFlowGraph.EndingWeight(n.Kind,n.EndingType)==3?116:90,Width(n.Label)),height=WalkthroughFlowGraph.NodeHeight(n.Kind,n.EndingType)}),
            edges=model.Edges.Select(e=>new {id=e.Id,from=e.From,to=e.To,choices=e.Choices})};
        using var engine=new Engine(options=>options.Strict().LimitMemory(64_000_000).MaxStatements(5_000_000)
            .TimeoutInterval(TimeSpan.FromSeconds(2)).LimitRecursion(256).CancellationToken(cancellation));
        // No CLR/module/IO bindings. API strings enter as JSON data, never code.
        // Dagre clones its JSON-only layout options. Jint has no browser
        // structuredClone host API; these options contain no transferable types.
        engine.Execute("function structuredClone(value){return JSON.parse(JSON.stringify(value));}");
        engine.Execute(Script.Value);engine.Execute(Routing.Value);engine.SetValue("guideInput",JsonSerializer.Serialize(input));
        string result=engine.Evaluate(LayoutScript).AsString();if(result.Length>2_000_000)throw new InvalidDataException();
        using var json=JsonDocument.Parse(result);var root=json.RootElement;
        float Number(JsonElement value) {float n=value.GetSingle();if(!float.IsFinite(n)||n<0||n>32768)throw new InvalidDataException();return n;}
        var geometry=root.GetProperty("nodes").EnumerateArray().ToArray();var nodes=model.Nodes.Select((n,i)=>new GuideFlowNode(n.Kind,n.Label,
            Number(geometry[i].GetProperty("x")),Number(geometry[i].GetProperty("y")),Number(geometry[i].GetProperty("width")),Number(geometry[i].GetProperty("height")),n.EndingType??"")).ToImmutableArray();
        var ids=model.Nodes.Select((n,i)=>(n.Id,i)).ToDictionary(x=>x.Id,x=>x.i);var sections=document.Sections.Select((s,i)=>(s.Key,i)).ToDictionary(x=>x.Key,x=>x.i);
        var lines=root.GetProperty("edges").EnumerateArray().ToArray();int points=0;
        var edges=model.Edges.Select((e,i)=> {
            var line=lines[i].GetProperty("points").EnumerateArray().Select(p=>new GuideFlowPoint(Number(p.GetProperty("x")),Number(p.GetProperty("y")))).ToImmutableArray();
            points+=line.Length;if(line.Length is <2 or >1024||points>32768)throw new InvalidDataException();
            var label=lines[i].GetProperty("label");return new GuideFlowEdge(ids[e.From],ids[e.To],e.RouteIndex,e.Start,e.Count,e.Choices,sections.GetValueOrDefault(e.Section),e.Kind=="unlock",line,new(Number(label.GetProperty("x")),Number(label.GetProperty("y"))));
        }).ToImmutableArray();
        return new GuideFlowLayout(Number(root.GetProperty("width")),Number(root.GetProperty("height")),nodes,edges,model.Truncated);
    },cancellation);
}
