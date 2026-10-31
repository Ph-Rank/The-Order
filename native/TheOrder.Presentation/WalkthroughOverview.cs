using System.Collections.Immutable;
using TheOrder.Application;

namespace TheOrder.Presentation;

public sealed record WalkthroughOverviewLink(int SourceIndex, string Kind, string Label);
public sealed record WalkthroughOverviewEntry(int Index, int? ParentIndex, ImmutableArray<WalkthroughOverviewLink> Sources);

// Only explicit endpoint/save identities create relationships. An ambiguous save
// retains all references instead of inventing a single play order.
public static class WalkthroughOverview
{
    public static ImmutableArray<WalkthroughOverviewEntry> Build(WalkthroughDocument document, int limit = 200)
    {
        int count=Math.Min(document.Segments.Length,Math.Clamp(limit,0,200));
        var saves=new Dictionary<string,List<int>>(StringComparer.Ordinal);
        var endpoints=new Dictionary<(string Kind,string Key),List<int>>();
        for(int i=0;i<count;i++)
        {
            var segment=document.Segments[i];
            var saveKeys=segment.Steps.Where(x=>x.Kind=="save"&&!string.IsNullOrWhiteSpace(x.SaveKey)).Select(x=>x.SaveKey!)
                .Concat(segment.To.Kind=="save"?[segment.To.Key]:[]).Distinct(StringComparer.Ordinal);
            foreach(var key in saveKeys)
            {if(!saves.TryGetValue(key,out var owners))saves[key]=owners=[];owners.Add(i);}
            if(segment.To.Kind is not ("entry" or "start" or "end")&&!string.IsNullOrWhiteSpace(segment.To.Key))
            {var key=(segment.To.Kind,segment.To.Key);if(!endpoints.TryGetValue(key,out var owners))endpoints[key]=owners=[];owners.Add(i);}
        }
        var result=ImmutableArray.CreateBuilder<WalkthroughOverviewEntry>(count);
        for(int i=0;i<count;i++)
        {
            var segment=document.Segments[i];var links=ImmutableArray.CreateBuilder<WalkthroughOverviewLink>();
            var owners=segment.From.Kind=="save"?saves.GetValueOrDefault(segment.From.Key):endpoints.GetValueOrDefault((segment.From.Kind,segment.From.Key));
            if(segment.From.Kind is not ("start" or "entry")&&owners is not null)
                foreach(int source in owners.Where(x=>x!=i).Distinct())links.Add(new(source,segment.From.Kind=="save"?"save":"continue",segment.From.Label.Display));
            // Earlier exact references within this section make a readable forest.
            // Cross-section and forward references remain named links, not crossing lines.
            int? parent=links.Count==1&&links[0].SourceIndex<i&&document.Segments[links[0].SourceIndex].Section==segment.Section?links[0].SourceIndex:null;
            result.Add(new(i,parent,links.ToImmutable()));
        }
        return result.ToImmutable();
    }
}
