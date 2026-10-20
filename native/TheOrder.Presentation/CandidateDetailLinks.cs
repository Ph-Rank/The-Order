using System.Text.RegularExpressions;
using TheOrder.Domain;
namespace TheOrder.Presentation;
public sealed record CandidateDetailLink(string Title, string Url);
public static class CandidateDetailLinks
{
    private static readonly Dictionary<string,string> Sites = new(StringComparer.OrdinalIgnoreCase) {
        ["www.kungal.com"]="KunGal",["kungal.com"]="KunGal",["vndb.org"]="VNDB",["bgm.tv"]="Bangumi",["bangumi.tv"]="Bangumi",["chii.in"]="Bangumi",["store.steampowered.com"]="Steam",["erogamescape.dyndns.org"]="ErogameScape",["howlongtobeat.com"]="HowLongToBeat",["www.dlsite.com"]="DLsite" };
    public static bool Allowed(string? value) => value is { Length: > 0 and <= 8192 }
        && Uri.TryCreate(value,UriKind.Absolute,out var uri)&&uri.Scheme=="https"&&uri.IsDefaultPort&&uri.UserInfo.Length==0&&Sites.ContainsKey(uri.IdnHost);
    public static IReadOnlyList<CandidateDetailLink> From(WorkMetadata work)
    {
        var links=new List<CandidateDetailLink>();var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(string? url) {
            if(!Allowed(url))return;var uri=new Uri(url!);var site=Sites[uri.IdnHost];
            // Work refs are authoritative anchors; releases, characters and staff are separate entities.
            if(site=="VNDB"&&!Regex.IsMatch(uri.AbsolutePath,@"^/v[0-9]+/?$"))return;
            if(site=="DLsite"&&!Regex.IsMatch(uri.AbsolutePath,@"^/(?:home|soft|pro|maniax|books|girls|bl|appx)/work/=/product_id/(?:RJ|VJ|BJ)[0-9]{6,12}\.html$"))return;
            if(seen.Add(site))links.Add(new(site,url!));
        }
        // KunGal's detail route uses the catalog work ID even when no community claim exists.
        // This is a browsing URL, not an additional independent recognition identifier.
        if (Regex.IsMatch(work.Id, "^[0-9]{1,20}$")) Add("https://www.kungal.com/galgame/" + work.Id);
        Add(work.WebUrl);
        foreach(var r in work.References.Where(r=>r.Source is "kungal" or "curated"))Add(r.Url);
        foreach(var r in work.References.Where(r=>r.Source=="vndb"&&Regex.IsMatch(r.Id,@"^v[0-9]+$")))Add(r.Url);
        foreach(var r in work.References.Take(128))Add(r.Url);
        return links.OrderBy(l=>l.Title=="KunGal"?0:1).ToArray();
    }
}
