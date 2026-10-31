using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using TheOrder.Application;
using TheOrder.Presentation;
using Windows.Storage.Streams;

namespace TheOrder.WinUI.Views;

// A local, private, short-lived renderer. The page receives guide projections,
// never credentials, paths, a host object or general-purpose native commands.
internal sealed class WalkthroughGraphView : Grid, IDisposable
{
    private const string Host = "theorder-guide.invalid", DocumentUrl = "https://theorder-guide.invalid/index.html";
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private readonly Grid browser = new();
    private readonly TextBlock message = new() { Text = "正在准备流程图…", TextWrapping = TextWrapping.Wrap };
    private readonly Button retry = new() { Content = "重试流程图", Visibility = Visibility.Collapsed };
    private readonly Button fallback = new() { Content = "使用路线列表", Visibility = Visibility.Collapsed };
    private readonly StackPanel loading = new() { Spacing = 12, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, MaxWidth = 400 };
    private WebView2? web;
    private WalkthroughDocument? document;
    private WalkthroughFlowGraph? graph;
    private ElementTheme theme;
    private string initialSection = "";
    private int generation, revision, nodes, edges, blockedRequests;
    private bool active, disposed, hideCollections;
    private string state = "idle";
    private string? probe;
    public event Action<int,int>? ReadRequested;
    public event Action? FallbackRequested;
    public WalkthroughGraphView()
    {
        Children.Add(browser); loading.Children.Add(message); loading.Children.Add(retry); loading.Children.Add(fallback); Children.Add(loading);
        retry.Click += (_, _) => { Stop(); if (active) _ = StartAsync(); };
        fallback.Click += (_, _) => FallbackRequested?.Invoke();
        Loaded += (_, _) => { if (active && web is null) _ = StartAsync(); };
        Unloaded += (_, _) => { if (!IsLoaded) Stop(); };
    }
    public void Show(WalkthroughDocument value, ElementTheme actualTheme, string section)
    {
        bool changed = !ReferenceEquals(document,value) || theme != actualTheme;
        document=value; theme=actualTheme; initialSection=section; active=true;
        if (changed || graph is null) { graph=WalkthroughFlowGraph.Build(value); revision++; }
        if (web?.CoreWebView2 is not null && state == "ready") { if (changed) SendGraph(); }
        else if (IsLoaded && web is null) _ = StartAsync();
    }
    public void Hide() { active=false; Stop(); }
    private async Task StartAsync()
    {
        if (disposed || !active || document is null || web is not null) return;
        int run=++generation; revision++; state="loading"; probe=null; nodes=edges=blockedRequests=0;
        message.Text="正在准备流程图…";loading.Visibility=Visibility.Visible;retry.Visibility=fallback.Visibility=Visibility.Collapsed;
        var current=new WebView2 { HorizontalAlignment=HorizontalAlignment.Stretch, VerticalAlignment=VerticalAlignment.Stretch };
        web=current;browser.Children.Add(current);
        try
        {
            var profile=Program.Profile ?? throw new InvalidOperationException();profile.ValidateAccess();
            var root=Path.Combine(profile.Root,"native-guide-graph-browser-v1");
            if (Directory.Exists(root) && (File.GetAttributes(root)&FileAttributes.ReparsePoint)!=0) throw new InvalidOperationException();
            var assets=Path.Combine(AppContext.BaseDirectory,"Assets","GuideGraph");
            foreach (string file in new[]{"index.html","graph.js","graph.css","dagre.min.js","shape-routing.js"})
                if (!File.Exists(Path.Combine(assets,file))) throw new FileNotFoundException();
            var environment=await CoreWebView2Environment.CreateWithOptionsAsync(null,root,new CoreWebView2EnvironmentOptions());
            if (run!=generation || disposed) return;
            var options=environment.CreateCoreWebView2ControllerOptions();options.IsInPrivateModeEnabled=true;options.ProfileName="GuideGraph";
            await current.EnsureCoreWebView2Async(environment,options);
            if (run!=generation || disposed) return;
            var core=current.CoreWebView2;
            core.Settings.AreHostObjectsAllowed=false;core.Settings.AreDevToolsEnabled=false;core.Settings.AreDefaultContextMenusEnabled=false;
            core.Settings.IsPasswordAutosaveEnabled=false;core.Settings.IsGeneralAutofillEnabled=false;core.Settings.IsStatusBarEnabled=false;
            core.Settings.IsZoomControlEnabled=false;
            core.SetVirtualHostNameToFolderMapping(Host,assets,CoreWebView2HostResourceAccessKind.Deny);
            core.AddWebResourceRequestedFilter("*",CoreWebView2WebResourceContext.All);
            core.WebResourceRequested += (_,args) => {
                if (run!=generation) return;
                if (!Uri.TryCreate(args.Request.Uri,UriKind.Absolute,out var uri) || uri.Scheme!="https" || uri.Host!=Host || uri.Port!=443
                    || uri.Query.Length!=0 || uri.UserInfo.Length!=0 || uri.AbsolutePath is not ("/index.html" or "/graph.js" or "/graph.css" or "/dagre.min.js" or "/shape-routing.js"))
                { blockedRequests++;args.Response=environment.CreateWebResourceResponse(null,403,"Blocked","Content-Type: text/plain"); }
            };
            core.NavigationStarting += (_,args) => { if (args.Uri!=DocumentUrl && args.Uri!="about:blank") args.Cancel=true; };
            core.NewWindowRequested += (_,args) => args.Handled=true;
            core.DownloadStarting += (_,args) => args.Cancel=true;
            core.PermissionRequested += (_,args) => args.State=CoreWebView2PermissionState.Deny;
            core.WebMessageReceived += (_,args) => {
                if (run!=generation || args.Source!=DocumentUrl || disposed || !active) return;
                try
                {
                    var raw=args.WebMessageAsJson;if (raw.Length>16384) return;
                    using var json=JsonDocument.Parse(raw);var request=json.RootElement;
                    string? kind=request.GetProperty("kind").GetString();
                    if (kind=="ready") { SendGraph(); return; }
                    if (request.GetProperty("revision").GetInt32()!=revision) return;
                    if (kind=="rendered") {
                        int n=request.GetProperty("nodes").GetInt32(),e=request.GetProperty("edges").GetInt32();
                        if (n<0 || n>WalkthroughFlowGraph.MaximumNodes || e<0 || e>WalkthroughFlowGraph.MaximumEdges) return;
                        nodes=n;edges=e;state="ready";loading.Visibility=Visibility.Collapsed;return;
                    }
                    if(kind=="display") { hideCollections=request.GetProperty("hideCollections").GetBoolean();return; }
                    string? id=request.GetProperty("edgeId").GetString();
                    if (id is null || id.Length>16 || graph?.Edges.FirstOrDefault(e=>e.Id==id) is not { } edge) return;
                    if (kind=="inspect") Inspect(edge);
                    else if (kind=="read") DispatcherQueue.TryEnqueue(() => { if (run==generation && active && !disposed) ReadRequested?.Invoke(edge.RouteIndex,edge.Start); });
                }
                catch (Exception e) when (e is JsonException or InvalidOperationException or KeyNotFoundException or FormatException or OverflowException) { }
            };
            core.ProcessFailed += (_,_) => { if (run==generation) Fail(); };
            core.NavigationCompleted += (_,args) => { if (run==generation && !args.IsSuccess) Fail(); };
            core.Navigate(DocumentUrl);
            for (int i=0;i<75 && run==generation && state=="loading";i++) await Task.Delay(200);
            if (run==generation && state=="loading") Fail();
        }
        catch (Exception) { if (run==generation && !disposed) Fail(); }
    }
    private void SendGraph()
    {
        if (web?.CoreWebView2 is not { } core || document is null || graph is null) return;
        core.PostWebMessageAsJson(JsonSerializer.Serialize(new { kind="graph",revision,guideId=document.Summary.Id,initialSection,hideCollections,theme=theme==ElementTheme.Light?"light":"dark",graph,
            sections=document.Sections.Select(s=>new { s.Key,title=s.Title.Display }) },Json));
    }
    private void Inspect(WalkthroughFlowEdge edge)
    {
        if (document is null || web?.CoreWebView2 is not { } core) return;
        var segment=document.Segments[edge.RouteIndex];int budget=48000,taken=0;
        string Limited(string value) { int length=Math.Min(Math.Min(value.Length,4000),Math.Max(0,budget));budget-=length;return value[..length]; }
        var records=new List<object>();
        foreach (var step in segment.Steps.Skip(edge.Start).Take(Math.Min(edge.Count,60)))
        {
            if (budget<1000) break;
            records.Add(new { index=edge.Start+taken,step.Kind,display=Limited(step.Text.Display),original=Limited(step.Text.Original),
                alternatives=step.Alternatives.Take(24).Select(a=>Limited(a.Display)).ToArray(),step.Achievement });taken++;
        }
        core.PostWebMessageAsJson(JsonSerializer.Serialize(new { kind="inspection",revision,edgeId=edge.Id,
            from=Limited(graph!.Nodes.First(n=>n.Id==edge.From).Label),to=Limited(graph.Nodes.First(n=>n.Id==edge.To).Label),
            condition=Limited(segment.Condition.Display),steps=records,more=taken<edge.Count || budget<1000 },Json));
    }
    private void Fail()
    {
        Stop();state="failed";message.Text="流程图暂时无法加载。可以重试，或使用路线列表继续阅读。";
        loading.Visibility=Visibility.Visible;retry.Visibility=fallback.Visibility=Visibility.Visible;
    }
    private void Stop()
    {
        generation++;var previous=web;web=null;try { previous?.Close(); } catch { }
        browser.Children.Clear();state="idle";probe=null;
    }
    internal object Evidence => new {state,nodes,edges,revision,blockedRequests,hasBrowser=web?.CoreWebView2 is not null,
        inPrivate=web?.CoreWebView2?.Profile.IsInPrivateModeEnabled,width=ActualWidth,height=ActualHeight,hideCollections,probe};
    internal async Task ProbeForFixtureAsync(string? action=null,string? id=null)
    {
        if (Program.Profile?.Manifest.ProfileKind!=NativeProfileKind.Test || web?.CoreWebView2 is not { } core) throw new InvalidOperationException();
        if (action is not null)
        {
            if (action is "inspect" or "read") { if (graph?.Edges.Any(e=>e.Id==id)!=true) throw new InvalidDataException(); }
            else if (action=="filter") { if (id!="" && document?.Sections.Any(s=>s.Key==id)!=true) throw new InvalidDataException(); }
            else if(action=="collections") { if(id is not ("0" or "1"))throw new InvalidDataException(); }
            else if(action=="fit") { if(id is not ("all" or "width"))throw new InvalidDataException(); }
              else if(action=="picker") { if(id is not ("0" or "1"))throw new InvalidDataException(); }
            else throw new InvalidDataException();
            await core.ExecuteScriptAsync("window.guideGraphTest("+JsonSerializer.Serialize(new {kind=action,edgeId=id,key=id,hidden=id=="1",all=id=="all"},Json)+")");
        }
        await Task.Delay(100);if (web?.CoreWebView2 is { } current) probe=await current.ExecuteScriptAsync("window.guideGraphEvidence()");
    }
    internal async Task CaptureForFixtureAsync(string path)
    {
        if (Program.Profile?.Manifest.ProfileKind!=NativeProfileKind.Test || web?.CoreWebView2 is not { } core) throw new InvalidOperationException();
        using var stream=new InMemoryRandomAccessStream();await core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,stream);
        stream.Seek(0);using var reader=stream.AsStreamForRead();using var file=File.Create(path);await reader.CopyToAsync(file);
    }
    public void Dispose() { if (disposed) return;active=false;disposed=true;Stop();document=null;graph=null; }
}
