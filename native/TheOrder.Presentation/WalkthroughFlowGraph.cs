using System.Collections.Immutable;
using System.Text;
using System.Text.RegularExpressions;
using TheOrder.Application;

namespace TheOrder.Presentation;

public sealed record WalkthroughFlowNode(string Id,string Kind,string Label,string Original,string? Requirement,string? EndingType=null);
public sealed record WalkthroughFlowEdge(string Id,string From,string To,int RouteIndex,int Start,int Count,int Choices,string Section,string Kind);
public sealed record WalkthroughFlowGraph(ImmutableArray<WalkthroughFlowNode> Nodes,ImmutableArray<WalkthroughFlowEdge> Edges,bool Truncated)
{
    public const int MaximumNodes=512,MaximumEdges=768;
    private static readonly Regex NumberedSave = new(@"^(?:(?:存档|存檔|セーブ|SAVE)[\s:：·#-]*)?([0-9]{1,4}(?:\s*[-/]\s*[0-9]{1,4})*)\s*(?:号|號|番)?$",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant|RegexOptions.NonBacktracking);
    public static string SaveLabel(string label)
    {
        var match=NumberedSave.Match(label.Normalize(NormalizationForm.FormKC).Trim());
        // Normalize the UI term only. Custom names and all source records retain
        // their meaning; node identity continues to use the API's typed key.
        return match.Success?"存档 "+match.Groups[1].Value:label.StartsWith("存档",StringComparison.Ordinal)?label.Replace("存档 ·", "存档", StringComparison.Ordinal):"存档 "+label;
    }
    // The provider explicitly distinguishes heroine, normal, bad and after.
    // Unknown classifications remain neutral; names never establish a true/BE.
    public static int EndingWeight(string kind,string? endingType)=>kind=="collect"?0:kind is not ("ending" or "reference")?2:endingType switch
    {"heroine" or "true" or "good"=>3,"normal" or "bad"=>1,"after" or "cg" or "collect"=>0,_=>2};
    public static float NodeHeight(string kind,string? endingType)=>kind=="save"?48:kind is "ending" or "collect"?EndingWeight(kind,endingType) switch {3=>46,2=>38,1=>34,_=>24}:34;
    public static WalkthroughFlowGraph Build(WalkthroughDocument document)
    {
        var nodes=new Dictionary<(string Kind,string Key),WalkthroughFlowNode>();
        var declared=document.Nodes.ToDictionary(n=>(n.Kind,n.Key));
        var edges=ImmutableArray.CreateBuilder<WalkthroughFlowEdge>();
        bool truncated=false;
        string? Node(WalkthroughNode node,string occurrence)
        {
            // Collection endpoints have no shared identity in the API.
            var key=(node.Kind,node.Kind=="collect"?occurrence:node.Key);
            if(nodes.TryGetValue(key,out var found))return found.Id;
            if(nodes.Count>=MaximumNodes){truncated=true;return null;}
            var value=declared.GetValueOrDefault((node.Kind,node.Key))??node;
            var result=new WalkthroughFlowNode("n"+nodes.Count,node.Kind,node.Kind=="save"?SaveLabel(value.Label.Display):value.Label.Display,value.Label.Original,value.Requirement,value.EndingType);
            nodes[key]=result;return result.Id;
        }
        void Edge(string? from,string? to,int route,int start,int count,string kind="route")
        {
            if(from is null||to is null)return;
            if(from==to&&count==0)return;
            if(edges.Count>=MaximumEdges){truncated=true;return;}
            var segment=document.Segments[route];
            edges.Add(new("e"+edges.Count,from,to,route,start,count,
                segment.Steps.Skip(start).Take(count).Count(s=>s.Kind=="choice"),segment.Section,kind));
        }
        for(int route=0;route<document.Segments.Length;route++)
        {
            if(edges.Count>=MaximumEdges){truncated=true;break;}
            var segment=document.Segments[route];
            string? current=Node(segment.From,segment.Key+"-from");int start=0;
            for(int step=0;step<segment.Steps.Length;step++)
            {
                var record=segment.Steps[step];
                if(record.Kind is not ("save" or "load")||string.IsNullOrEmpty(record.SaveKey))continue;
                var node=declared.GetValueOrDefault(("save",record.SaveKey))??new("save",record.SaveKey,record.Text);
                var target=Node(node,segment.Key+"-"+step);
                // A repeated declaration/load of the current save belongs to the
                // next edge. It must not fabricate a self-loop or lose that step.
                if(current==target)continue;
                Edge(current,target,route,start,step-start+1);
                current=target;start=step+1;
            }
            Edge(current,Node(segment.To,segment.Key+"-to"),route,start,segment.Steps.Length-start);
            if(segment.From.RequirementKey is { } required&&declared.TryGetValue(("ending",required),out var prerequisite))
            {
                var from=Node(prerequisite,segment.Key+"-prerequisite");var to=Node(segment.From,segment.Key+"-from");
                if(!edges.Any(e=>e.Kind=="unlock"&&e.From==from&&e.To==to))Edge(from,to,route,0,0,"unlock");
            }
        }
        return ReadingForest(nodes.Values.ToImmutableArray(),edges.ToImmutable(),truncated);
    }
    private static WalkthroughFlowGraph ReadingForest(ImmutableArray<WalkthroughFlowNode> originals,ImmutableArray<WalkthroughFlowEdge> source,bool truncated)
    {
        // Crossing joins/cycles use named reference terminals. They are diagram
        // references, not fabricated game actions. Every edge keeps its exact
        // route and step range. Non-save continuations have one vertical exit.
        var nodes=originals.ToList();var byId=originals.ToDictionary(n=>n.Id);
        var outgoing=source.GroupBy(e=>e.From).ToDictionary(g=>g.Key,g=>g.ToArray());
        var incoming=source.Select(e=>e.To).ToHashSet();var seen=new HashSet<string>();
        var rewritten=new Dictionary<string,WalkthroughFlowEdge>();
        string? Reference(string caption,string kind,string? endingType=null)
        {
            if(nodes.Count>=MaximumNodes){truncated=true;return null;}
            string id="r"+nodes.Count;nodes.Add(new(id,kind,caption,caption,null,endingType));return id;
        }
        void Visit(string id)
        {
            if(!seen.Add(id)||!outgoing.TryGetValue(id,out var links))return;
            for(int i=0;i<links.Length;i++)
            {
                var edge=links[i];string? from=id;
                if(i>0&&byId[id].Kind!="save")from=Reference("从 "+byId[id].Label+" 继续","entry");
                if(from is null)continue;
                string? to=edge.To;bool existing=seen.Contains(to);
                if(existing)to=Reference("转至 "+byId[to].Label,"reference",byId[to].Kind=="collect"?"collect":byId[to].EndingType);
                if(to is null)continue;
                rewritten[edge.Id]=edge with{From=from,To=to};
                if(!existing)Visit(edge.To);
            }
        }
        foreach(var node in originals.Where(n=>!incoming.Contains(n.Id)))Visit(node.Id);
        foreach(var node in originals)Visit(node.Id);
        return new(nodes.ToImmutableArray(),source.Where(e=>rewritten.ContainsKey(e.Id)).Select(e=>rewritten[e.Id]).ToImmutableArray(),truncated);
    }
}
