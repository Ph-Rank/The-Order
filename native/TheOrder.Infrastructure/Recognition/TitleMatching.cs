using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using TheOrder.Infrastructure.Legacy;

namespace TheOrder.Infrastructure.Recognition;

public sealed record TitleComparison(double Score, bool Exact, string? Conflict, string Version, bool Distinctive);
public sealed record TitleMarkers(string Base, System.Collections.Immutable.ImmutableHashSet<string> Editions, System.Collections.Immutable.ImmutableHashSet<string> Expansions,
    System.Collections.Immutable.ImmutableArray<string> Chapters, System.Collections.Immutable.ImmutableArray<string> Numbers)
{
    public bool Any => !Editions.IsEmpty || !Expansions.IsEmpty || !Chapters.IsEmpty;
}

// Pure compatibility algorithm. No filesystem, network, repository, or UI access.
public static class TitleMatching
{
    public const string AlgorithmVersion = "recognition-v2-release-aware-8";
    private const string TsukihimeRemakeSubtitle = " a piece of blue glass moon";
    internal const string TsukihimeRemakeTitle = "Tsukihime -A piece of blue glass moon-";
    private const string Packaging = @"^(?:(?:pc|windows|win(?:32|64)|x(?:86|64)|chs|cht|cn|jp|en)|(?:简体|繁体|官方)?中文(?:版)?|官中(?:版)?|汉化(?:硬盘|整合)?版?|(?:日文|英文|日语|英语|简中|繁中)版?|免安装(?:版)?|硬盘版|绿色版|直装版|解压即玩)$";
    private const string Suffix = @"(?:[\s._-]+(?:chs|cht|x64|x86|win32|win64)|[\s._-]*(?:(?:简体|繁体|官方)中文(?:版)?|官中(?:版)?|汉化(?:硬盘|整合)?版|免安装(?:版)?|硬盘版|绿色版|直装版|解压即玩))$";
    private static readonly HashSet<string> Generic = new(("game games gamedata game32 game64 data datafiles engine content assets plugins binaries win32 win64 windows x86 x64 www backup save saves savedata managed runtime resources system start startup launch launcher main config settings setup install installer uninstall directx dotnet advhd siglusengine onscripter nscripter kirikiri krkr krkrz artemis bgi rio reallive malie unityplayer 游戏 游戏目录 游戏文件 游戏本体 本体 新建文件夹").Split(' '), StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<(string,bool), Regex> Patterns = new();
    internal static Regex Rx(string pattern, bool ignoreCase = false) => Patterns.GetOrAdd((pattern,ignoreCase), k => new(k.Item1, RegexOptions.CultureInvariant | RegexOptions.ECMAScript | (k.Item2 ? RegexOptions.IgnoreCase : RegexOptions.None), TimeSpan.FromSeconds(1)));
    internal static string Lower(string value) => LegacyPathCase.Lower(value);
    internal static string Trim(string value) => value.Trim(LegacyLibraryCodec.Whitespace);
    private static readonly (int[][] Latin,int[][] Lnm) Unicode = LoadUnicode();
    private static (int[][],int[][]) LoadUnicode()
    {
        using var stream = typeof(TitleMatching).Assembly.GetManifestResourceStream("TheOrder.Infrastructure.Recognition.RecognitionUnicode.json")!;
        using var document = JsonDocument.Parse(stream);
        int[][] Read(string key) => document.RootElement.GetProperty(key).EnumerateArray().Select(r=>r.EnumerateArray().Select(v=>v.GetInt32()).ToArray()).ToArray();
        return (Read("latin"),Read("lettersNumbersMarks"));
    }
    private static bool Contains(int[][] ranges,int cp)
    {
        int low=0,high=ranges.Length-1;
        while(low<=high) { int mid=(low+high)/2; if(cp<ranges[mid][0]) high=mid-1; else if(cp>ranges[mid][1]) low=mid+1; else return true; }
        return false;
    }
    private static string ValidUnicode(string value) => Rx(@"[\uD800-\uDBFF](?![\uDC00-\uDFFF])|(?<![\uD800-\uDBFF])[\uDC00-\uDFFF]").Replace(value," ");
    private static string Layout(string value) => Rx(@"\s+").Replace(value," ");
    internal static string Prepared(string value)
    {
        if(value.Length>500) return "";
        string result=Trim(Rx(@"[\x00-\x1f\x7f]").Replace(ValidUnicode(value).Normalize(NormalizationForm.FormKC)," "));
        result=Rx(@"\[([^\[\]]*)\]|【([^【】]*)】|\(([^()]*)\)").Replace(result,m=> {
            var tokens=Rx(@"[\s,+/|・]+").Split(Trim(m.Groups.Cast<Group>().Skip(1).First(g=>g.Success).Value)).Where(t=>t.Length>0).ToArray();
            return tokens.Length>0&&tokens.All(t=>Rx(Packaging,true).IsMatch(t))?" ":m.Value;
        });
        string old; do {old=result; result=Trim(Rx(Suffix,true).Replace(result,""));} while(old!=result);
        return Layout(result);
    }
    private static string Fold(string value)
    {
        var result=new StringBuilder(); bool latin=false;
        foreach(var rune in value.Normalize(NormalizationForm.FormD).EnumerateRunes()) {
            bool mark=Rune.GetUnicodeCategory(rune) is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark;
            if(mark&&latin) continue;
            result.Append(rune.ToString()); latin=Contains(Unicode.Latin,rune.Value);
        }
        return result.ToString().Normalize(NormalizationForm.FormC);
    }
    private static string Separate(string value)
    {
        var b=new StringBuilder(); foreach(var rune in value.EnumerateRunes()) b.Append(Contains(Unicode.Lnm,rune.Value)?rune.ToString():" ");
        return Trim(Layout(b.ToString()));
    }
    public static string Normalize(string value) => Separate(Lower(Fold(Prepared(value))));
    // Legacy index keys deliberately differ from title comparison: no accent fold,
    // and combining marks are omitted. Do not silently unify these algorithms.
    public static string IndexKey(string value) => string.Concat(Lower(Prepared(value)).EnumerateRunes().Where(r=>Rune.IsLetterOrDigit(r)||Rune.GetUnicodeCategory(r) is UnicodeCategory.LetterNumber or UnicodeCategory.OtherNumber).Select(r=>r.ToString()));
    internal static string Compact(string value) => Rx(@"\s+").Replace(value,"");
    private static bool Meaningful(string value) => value.Length>0&&!Generic.Contains(Compact(value));
    private static readonly (string Key,string Pattern)[] Editions = [
        ("remake",@"\bremake(?:\s+for\s+(?:fhd|qhd|hd))?(?:\s+edition)?\b"), ("remastered",@"\bremaster(?:ed)?(?:\s+edition)?\b"),
        ("reboot",@"\bre\s*boot(?:\s+edition)?\b"), ("reflection-blue",@"\breflection\s*blue(?:\s+edition)?\b"),("sana",@"\bsana(?:\s+edition)?\b"),
        .. new[]{"qhd","fhd","hd","4k"}.Select(k=>("resolution:"+k,@"\b"+k+@"(?:\s+edition)?\b")),
        ("r18",@"\br\s*18(?:\s+edition)?\b"),("all-ages",@"\ball\s*ages(?:\s+edition)?\b|全年[齢龄齡](?:版)?"),
        .. new[]{"complete","ultimate","deluxe","definitive","extended","renewal","special","perfect"}.Select(k=>(k,@"\b"+k+@"\s+edition\b")),
        ("remake",@"リメイク(?:版)?|重[製制]版"),("remastered",@"リマスター(?:版)?") ];
    private static readonly (string Key,string Pattern)[] Expansions = [("fd",@"\b(?:fd|fan\s*disc)(?:\s+edition)?\b|ファンディスク"),("after",@"\bafter(?:\s*story)?\b|アフターストーリー"),("new-episode",@"\bnew\s*(?:episode|chapter)\b|新[章篇編编]")];
    private static readonly Dictionary<string,string> Numbers = new[]{"one","two","three","four","five","six","seven","eight","nine","ten","i","ii","iii","iv","v","vi","vii","viii","ix","x"}.Select((s,i)=>(s,n:(i%10+1).ToString(CultureInfo.InvariantCulture))).ToDictionary(x=>x.s,x=>x.n);
    private static readonly Dictionary<char,int> East = new() {['〇']=0,['零']=0,['一']=1,['壱']=1,['壹']=1,['二']=2,['弐']=2,['貳']=2,['贰']=2,['三']=3,['参']=3,['參']=3,['四']=4,['五']=5,['六']=6,['七']=7,['八']=8,['九']=9};
    private static string Number(string value) => double.TryParse(value,NumberStyles.None,CultureInfo.InvariantCulture,out var n)?n.ToString("G",CultureInfo.InvariantCulture):"NaN";
    private static string Chapter(string value)
    {
        if(Rx(@"^\d+$").IsMatch(value)) return Number(value);
        if(value.All(East.ContainsKey)) return Number(string.Concat(value.Select(c=>East[c].ToString(CultureInfo.InvariantCulture))));
        int total=0,digit=0; foreach(char c in value) {if(East.TryGetValue(c,out var d)) digit=d; else {total+=(digit==0?1:digit)*(c=='百'?100:10);digit=0;}}
        return (total+digit).ToString(CultureInfo.InvariantCulture);
    }
    private sealed record Parsed(string Normalized,string Base,HashSet<string> Editions,HashSet<string> Expansions,string[] Numbers,List<string> Chapters);
    private static Parsed Parse(string value)
    {
        string normalized=Normalize(value),basis=normalized; HashSet<string> editions=[],expansions=[]; List<string> chapters=[];
        if (normalized is ("tsukihime" + TsukihimeRemakeSubtitle) or ("月姫" + TsukihimeRemakeSubtitle) or ("月姬" + TsukihimeRemakeSubtitle))
        {
            editions.Add("remake");
            basis = normalized[..^TsukihimeRemakeSubtitle.Length];
        }
        foreach(var m in Editions) { if(Rx(m.Pattern,true).IsMatch(normalized)) editions.Add(m.Key);basis=Rx(m.Pattern,true).Replace(basis," "); }
        foreach(var p in new[]{@"\b(\d+)(?:st|nd|rd|th)?\s+anniversary(?:\s+edition)?\b",@"(\d+)周年(?:記念|纪念|紀念)?(?:版)?"})
            basis=Rx(p).Replace(basis,m=>{editions.Add("anniversary:"+Number(m.Groups[1].Value));return " ";});
        basis=Rx(@"\bv\s*(\d+(?:\s+\d+)*)\b").Replace(basis,m=>{editions.Add("version:"+Rx(@"\s+").Replace(m.Groups[1].Value,"."));return " ";});
        foreach(var m in Expansions) {if(Rx(m.Pattern,true).IsMatch(basis)) expansions.Add(m.Key);basis=Rx(m.Pattern,true).Replace(basis," ");}
        basis=Rx(@"\b(?:episodes?|ep|chapters?|chap|volumes?|vol|part|act)\s*(\d+|viii|vii|iii|vi|iv|ii|ix|one|two|three|four|five|six|seven|eight|nine|ten|[ivx])\b").Replace(basis,m=>{string n=Numbers.GetValueOrDefault(m.Groups[1].Value)??Number(m.Groups[1].Value);chapters.Add(n);return " episode "+n+" ";});
        basis=Rx(@"第([0-9〇零一二三四五六七八九十百壱壹弐貳贰参參拾]+)(?:章|話|话|巻|卷|部|作)").Replace(basis,m=>{string n=Chapter(m.Groups[1].Value);chapters.Add(n);return " episode "+n+" ";});
        basis=Rx(@"(^|\s)([〇零一二三四五六七八九十百壱壹弐貳贰参參拾]+)(?=\s|$)").Replace(basis,m=>m.Groups[1].Value+Chapter(m.Groups[2].Value));
        basis=Rx(@"\b([ivx]+)$").Replace(basis,m=>Numbers.GetValueOrDefault(m.Groups[1].Value)??m.Value);
        return new(normalized,Trim(Layout(basis)),editions,expansions,[..Rx(@"\d+").Matches(basis).Select(m=>Number(m.Value))],chapters);
    }
    public static bool HasVersion(string value) {var p=Parse(value);return p.Editions.Count+p.Expansions.Count+p.Chapters.Count>0;}
    // The title without edition, expansion or chapter markers, or null when it has none.
    public static string? BaseTitle(string value) {var p=Parse(value);return p.Editions.Count+p.Expansions.Count>0&&p.Chapters.Count==0&&Meaningful(p.Base)&&Informative(p.Base)?p.Base:null;}
    // Read-only view of the same parse used by Compare; it does not change any comparison.
    public static TitleMarkers Markers(string value) {var p=Parse(value);return new(Compact(p.Base),[..p.Editions],[..p.Expansions],[..p.Chapters],[..p.Numbers]);}
    public static string[] Variants(string value)
    {
        if(value.Length>500) return [];
        var cleaned=Prepared(value);var normalized=Normalize(value);if(!Meaningful(normalized))return [];
        var p=Parse(value); List<string> candidates=[Trim(value),cleaned];var folded=Fold(cleaned);if(folded!=cleaned)candidates.Add(folded);
        candidates.Add(Separate(cleaned));
        candidates.Add(Trim(Rx(@"[!:;☆†]").Replace(Rx(@"(?<=[\p{L}\p{N}])[-‐‑‒–—.]+(?=[\p{L}\p{N}])").Replace(cleaned,""),"")));
        if(Rx(@"^[a-z]{1,4}\s+\d",true).IsMatch(cleaned))candidates.Add(Rx(@"^([a-z]{1,4})\s+(?=\d)",true).Replace(cleaned,"$1:"));
        candidates.Add(normalized);
        if(Rx(@"\bedition$").IsMatch(normalized)) {var noEdition=Rx(@"\s+edition$").Replace(normalized,"");candidates.Add(noEdition);if(Rx("^[a-z0-9 ]+$",true).IsMatch(noEdition))candidates.Add(Compact(noEdition));}
        if(p.Editions.Count+p.Expansions.Count>0&&Meaningful(p.Base)) {candidates.Add(p.Base);if(Rx("^[a-z0-9 ]+$",true).IsMatch(p.Base))candidates.Add(Compact(p.Base));}
        HashSet<string> seen=new(StringComparer.Ordinal);
        return [..candidates.Select(c=>Trim(Layout(c))).Where(c=>Meaningful(Normalize(c))&&seen.Add(Lower(c))).Take(8)];
    }
    private static string Relation(HashSet<string> a,HashSet<string> b)
    {
        if(a.SetEquals(b))return "same";if(a.Count==0||b.Count==0)return "unspecified";if(!a.Overlaps(b))return "conflict";
        foreach(var family in new[]{new[]{"remake","remastered","reboot"},["sana","reflection-blue"],["r18","all-ages"]}) {
            var left=family.Where(a.Contains).ToArray();var right=family.Where(b.Contains).ToArray();if(left.Length>0&&right.Length>0&&!left.Intersect(right).Any())return "conflict";
        }
        foreach(var prefix in new[]{"resolution:","anniversary:","version:"}) {var left=a.Where(x=>x.StartsWith(prefix,StringComparison.Ordinal)).ToArray();var right=b.Where(x=>x.StartsWith(prefix,StringComparison.Ordinal)).ToArray();if(left.Length>0&&right.Length>0&&!left.Intersect(right).Any())return "conflict";}
        return "unspecified";
    }
    private static double Dice(string a,string b)
    {
        if(a==b)return a.Length>0?1:0;var l=a.EnumerateRunes().ToArray();var r=b.EnumerateRunes().ToArray();if(l.Length<2||r.Length<2)return 0;
        Dictionary<(Rune,Rune),int> pairs=[];for(int i=1;i<l.Length;i++){var key=(l[i-1],l[i]);pairs[key]=pairs.GetValueOrDefault(key)+1;}
        int common=0;for(int i=1;i<r.Length;i++){var key=(r[i-1],r[i]);if(pairs.GetValueOrDefault(key)>0){common++;pairs[key]--;}}
        return 2d*common/(l.Length+r.Length-2);
    }
    private static double Edit(string a,string b)
    {
        var l=a.EnumerateRunes().ToArray();var r=b.EnumerateRunes().ToArray();if(l.Length==0||r.Length==0)return 0;
        var previous=Enumerable.Range(0,r.Length+1).ToArray();for(int i=1;i<=l.Length;i++){var row=new int[r.Length+1];row[0]=i;for(int j=1;j<=r.Length;j++)row[j]=Math.Min(Math.Min(row[j-1]+1,previous[j]+1),previous[j-1]+(l[i-1]==r[j-1]?0:1));previous=row;}
        return 1-(double)previous[r.Length]/Math.Max(l.Length,r.Length);
    }
    private static bool Informative(string value) => Meaningful(value)&&(Rx("^[a-z0-9]+$",true).IsMatch(Compact(value))?Compact(value).Length>=4:Compact(value).EnumerateRunes().Count()>=2);
    public static TitleComparison Compare(string local,string candidate)
    {
        var l=Parse(local);var r=Parse(candidate);var a=Compact(l.Normalized);var b=Compact(r.Normalized);bool distinctive=Informative(l.Normalized)&&Informative(r.Normalized);
        if(!Meaningful(l.Normalized)||!Meaningful(r.Normalized))return new(0,false,"目录或引擎名称，缺少作品标识","unspecified",false);
        if(a==b)return new(1,true,null,"same",distinctive);
        var edition=Relation(l.Editions,r.Editions);var expansion=Relation(l.Expansions,r.Expansions);
        string version=edition=="conflict"||expansion=="conflict"?"conflict":edition=="unspecified"||expansion=="unspecified"?"unspecified":"same";
        string? conflict=null;double ceiling=.96;
        if(version=="conflict"){conflict="版本或追加篇标识不同";ceiling=.35;}
        if(!l.Numbers.SequenceEqual(r.Numbers)){if(l.Numbers.Length>0&&r.Numbers.Length>0){version="conflict";conflict="作品或篇章编号不同";ceiling=.25;}else{if(version!="conflict")version="unspecified";conflict??="作品编号未完整对应";ceiling=Math.Min(ceiling,.65);}}
        if(l.Expansions.Contains("new-episode")&&r.Chapters.Count>0||r.Expansions.Contains("new-episode")&&l.Chapters.Count>0){version="conflict";conflict="新章与编号章节不同";ceiling=Math.Min(ceiling,.25);}
        double score=.6*Dice(a,b)+.4*Edit(a,b);var baseA=Compact(l.Base);if(baseA.Length>0&&baseA==Compact(r.Base))score=Math.Max(score,version=="same"?.96:.88);
        if(version=="unspecified")ceiling=Math.Min(ceiling,.88);
        var shorter=a.Length<=b.Length?a:b;var longer=a.Length<=b.Length?b:a;var shortTitle=a.Length<=b.Length?l.Normalized:r.Normalized;
        if(Rx("^[a-z]+$",true).IsMatch(shortTitle)&&longer.Contains(shorter,StringComparison.Ordinal)){var longerTitle=a.Length<=b.Length?r.Normalized:l.Normalized;if(!longerTitle.Split(' ').Contains(shorter))ceiling=Math.Min(ceiling,shorter.Length<=4?.3:.6);}
        if(!distinctive)ceiling=Math.Min(ceiling,.45);
        return new(Math.Floor(Math.Max(0,Math.Min(ceiling,score))*1000+.5)/1000,false,conflict,version,distinctive);
    }
}
