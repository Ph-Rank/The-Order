using System.ComponentModel;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using TheOrder.Application;
using TheOrder.Presentation;

namespace TheOrder.WinUI.Views;

// The same validated document feeds desktop reading and the native game renderer.
public sealed class WalkthroughReaderView : Grid, IDisposable
{
    private readonly WalkthroughViewModel model;
    private readonly Func<long> auth;
    private readonly DispatcherTimer scopeTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly TextBlock status = Text("", 14), attribution = Text("", 13), routeFacts = Text("", 14);
    private readonly ComboBox guides = new() { Header = "攻略来源与版本", PlaceholderText = "选择一份攻略", HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ComboBox routes = new() { Header = "路线段", HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly Button read = new() { Content = "读取网络攻略" }, next = new() { Content = "下一页攻略" }, cancel = new() { Content = "取消读取" };
    private readonly HyperlinkButton source = new() { Content = "原攻略出处", Padding = new Thickness(0) };
    private readonly SelectorBar modes = new();
    private readonly SelectorBarItem bodyTab = new() { Text = "连续阅读" }, graphTab = new() { Text = "流程图" };
    private readonly WalkthroughGraphView flow = new() { Visibility = Visibility.Collapsed };
    private readonly ToggleSwitch original = new() { Header = "原文对照", OffContent = "优先参考中文", OnContent = "显示原文与参考中文" };
    private readonly Grid body = new() { ColumnSpacing = 20, Margin = new Thickness(0, 12, 0, 0) };
    private readonly StackPanel steps = new() { Spacing = 2 };
    private readonly StackPanel routePanel = new() { Spacing = 10 };
    private readonly ScrollViewer stepScroll = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalContentAlignment = HorizontalAlignment.Left };
    private readonly Button moreSteps = new() { Content = "继续展开步骤" };
    private readonly ScrollViewer map = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalContentAlignment = HorizontalAlignment.Left };
    private readonly StackPanel overview = new() { Spacing = 20, Padding = new Thickness(0,0,0,16) };
    private readonly List<Button> overviewRoutes = [];
    private WalkthroughSummary[] shownGuides = [];
    private WalkthroughDocument? shownDocument;
    private bool syncing, disposed;
    private long authVersion;
    private int shownSteps = 60;
    private (WalkthroughDocument? Document, int Route, bool Graph, bool Original, int Count)? renderedRoute;
    private bool initialRead;
    private bool listFallback;
    private Action? reflow;
    private object? endEvidence;
    private bool ShowGraph => modes.SelectedItem == graphTab;
    public WalkthroughViewModel Model => model;
    public void SetGraphForFixture(bool value) { modes.SelectedItem = value ? graphTab : bodyTab; RenderRoute(); }
    public void SetOriginalForFixture(bool value) { original.IsOn = value; RenderRoute(); }
    public void InvokeRouteForFixture(string key)
    {
        var document = model.Document ?? throw new InvalidOperationException();
        int index = Array.FindIndex(document.Segments.ToArray(), s => s.Key == key);
        if (index < 0) throw new InvalidDataException();
        OpenStep(index, 0);
    }
    public object LayoutForFixture() => new { width = ActualWidth, guides = model.Items.Length,
        selected = model.Document?.Summary.Id, routes = model.Document?.Segments.Length,
        steps = model.Document?.Segments.Sum(s => s.Steps.Length), sectionKeys = model.Document?.Sections.Select(s=>s.Key), graph = ShowGraph,
        routeIndex = routes.SelectedIndex, graphNodes = overviewRoutes.Count,
        cards = steps.Children.Count, primaryFont = 16, originalVisible = original.IsOn,
        dateHeadings = model.Document?.Segments.ElementAtOrDefault(routes.SelectedIndex)?.Steps.Count(GuideReadingText.IsHeading) ?? 0,
        status = status.Text, bodyWidth = body.ActualWidth, mapWidth = overview.ActualWidth,
        overviewSelectableText = overviewRoutes.Any(HasSelectableText),
        listWidth = guides.ActualWidth, bodyOffset = stepScroll.VerticalOffset, flow = flow.Evidence, end = endEvidence,
        modeControl = "SelectorBar", overflow = body.ActualWidth > ActualWidth + 1 || guides.ActualWidth > ActualWidth + 1 };
    internal Task ProbeGraphForFixtureAsync(string? action = null, string? id = null) => flow.ProbeForFixtureAsync(action,id);
    internal Task CaptureGraphForFixtureAsync(string path) => flow.CaptureForFixtureAsync(path);
    internal async Task EndForFixtureAsync()
    {
        if (shownDocument is not { } document || routes.SelectedIndex < 0) throw new InvalidOperationException();
        SetGraphForFixture(false); shownSteps = document.Segments[routes.SelectedIndex].Steps.Length; RenderRoute();
        UpdateLayout(); await Task.Delay(120); stepScroll.ChangeView(null,stepScroll.ScrollableHeight,null,true); await Task.Delay(120); UpdateLayout();
        var last = steps.Children.OfType<Border>().LastOrDefault() ?? throw new InvalidOperationException();
        var clips = new List<object>(); bool visible = true;
        for (DependencyObject? parent = last; parent is not null; parent = VisualTreeHelper.GetParent(parent))
            if (parent is ScrollViewer scroll && scroll.ActualHeight > 0)
            {
                var top = last.TransformToVisual(scroll).TransformPoint(new Windows.Foundation.Point());
                bool fits = top.Y >= -1 && top.Y + last.ActualHeight <= scroll.ViewportHeight + 1;
                visible &= fits; clips.Add(new {scroll.Name,top=top.Y,bottom=top.Y+last.ActualHeight,viewport=scroll.ViewportHeight,fits});
            }
        endEvidence = new {visible,offset=stepScroll.VerticalOffset,maximum=stepScroll.ScrollableHeight,
            lastIndex=document.Segments[routes.SelectedIndex].Steps.Length-1,clips};
    }
    public Func<string, Task>? OpenLink { get; set; }
    public WalkthroughReaderView(IWalkthroughReader reader, string? workId,
        Func<long> authGeneration, Func<bool>? sourceCurrent = null)
    {
        auth = authGeneration; authVersion = auth();
        model = new(reader, authGeneration, sourceCurrent); model.SetSource(workId); model.PropertyChanged += StateChanged;
        RowDefinitions.Add(new() { Height = GridLength.Auto }); RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) });
        AutomationProperties.SetAutomationId(this, "NetworkWalkthroughReader");
        AutomationProperties.SetAutomationId(read, "WalkthroughRead"); AutomationProperties.SetAutomationId(guides, "WalkthroughSources");
        AutomationProperties.SetAutomationId(routes, "WalkthroughRoutes"); AutomationProperties.SetAutomationId(modes, "WalkthroughReadingModes");
        AutomationProperties.SetName(guides, "攻略来源与版本"); guides.Header = null;
        AutomationProperties.SetAutomationId(status, "WalkthroughStatus"); AutomationProperties.SetAutomationId(body, "WalkthroughBody");
        var header = new StackPanel { Spacing = 8 };
        var actions = new TagFlowPanel { HorizontalSpacing = 8, VerticalSpacing = 6 };
        actions.Children.Add(read); actions.Children.Add(cancel); actions.Children.Add(next); actions.Children.Add(status);
        header.Children.Add(actions); header.Children.Add(guides); Children.Add(header);
        body.ColumnDefinitions.Add(new() { Width = new(210) }); body.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        body.RowDefinitions.Add(new() { Height = GridLength.Auto }); body.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) });
        routePanel.Children.Add(routes); routePanel.Children.Add(routeFacts);
        var attributionInfo = new SmoothExpander { Header = "来源与适用版本", HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch, FontSize = 12, Padding = new Thickness(12),
            Content = new StackPanel { Spacing = 8, Children = { attribution, source,
                Text("请核对攻略版本。路线与参考译文可能有误，以游戏中的选项为准。", 12) } } };
        routePanel.Children.Add(attributionInfo); body.Children.Add(routePanel);
        var narrowAttribution = new Button { Content = "来源与版本", FontSize = 12, Padding = new Thickness(8, 6, 8, 6), Visibility = Visibility.Collapsed };
        var sourceSummary = Text("", 13); var originalSource = new HyperlinkButton { Content = "原攻略出处", Padding = new Thickness(0), HorizontalAlignment = HorizontalAlignment.Left };
        var sourceFlyout = new Flyout { Content = new StackPanel { Spacing = 10, MaxWidth = 320, Children = { sourceSummary, originalSource } } };
        sourceFlyout.Opening += (_, _) => { sourceSummary.Text = attribution.Text; originalSource.IsEnabled = Safe(model.Document?.Summary.SourceUrl); };
        originalSource.Click += async (_, _) => await OpenSourceAsync(); narrowAttribution.Flyout = sourceFlyout; routePanel.Children.Add(narrowAttribution);
        var reading = new Grid { RowSpacing = 10 };
        reading.RowDefinitions.Add(new() { Height = GridLength.Auto }); reading.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) });
        var options = new Grid { ColumnSpacing = 16 };
        options.ColumnDefinitions.Add(new() { Width = new(1,GridUnitType.Star) }); options.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        modes.Items.Add(graphTab); modes.Items.Add(bodyTab); modes.SelectedItem = graphTab;
        original.Header = null; original.OffContent = "原文对照"; original.OnContent = "原文对照";
        original.FontSize = 13; original.VerticalAlignment = VerticalAlignment.Center;
        options.Children.Add(modes); SetColumn(original,1); options.Children.Add(original); reading.Children.Add(options);
        var documentPanel = new StackPanel { Spacing = 12, HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(0,0,0,24) };
        stepScroll.SizeChanged += (_, e) => documentPanel.Width = Math.Min(680, Math.Max(0, e.NewSize.Width - 18));
        documentPanel.Children.Add(steps); documentPanel.Children.Add(moreSteps); stepScroll.Content = documentPanel;
        SetRow(stepScroll, 1); reading.Children.Add(stepScroll); map.Content = overview; SetRow(map, 1); reading.Children.Add(map);
        SetRow(flow,1); reading.Children.Add(flow);
        flow.ReadRequested += OpenStep;
        flow.FallbackRequested += () => { listFallback = true; renderedRoute = null; RenderRoute(); };
        map.SizeChanged += (_,e) => overview.Width = Math.Max(0,e.NewSize.Width-16);
        SetColumn(reading, 1); body.Children.Add(reading); SetRow(body, 1); Children.Add(body);
        read.Click += async (_, _) => await ReadAsync();
        next.Click += async (_, _) => await model.NextAsync(); cancel.Click += (_, _) => model.Cancel();
        guides.SelectionChanged += async (_, _) => { if (!syncing && guides.SelectedIndex >= 0 && guides.SelectedIndex < shownGuides.Length) await model.SelectAsync(shownGuides[guides.SelectedIndex].Id); };
        routes.SelectionChanged += (_, _) => { if (!syncing) { shownSteps = 60; RenderRoute(); } };
        modes.SelectionChanged += (_, _) => { listFallback = false; if (!syncing) RenderRoute(); };
        original.Toggled += (_, _) => RenderRoute();
        moreSteps.Click += (_, _) => { shownSteps += 60; RenderRoute(); };
        source.Click += async (_, _) => await OpenSourceAsync();
        ActualThemeChanged += (_, _) => { renderedRoute = null; RenderRoute(); };
        reflow = () => {
            bool narrow = ActualWidth < 660, graph = ShowGraph;
            body.ColumnDefinitions[0].Width = narrow || graph ? new(0) : new(210);
            body.ColumnSpacing = graph ? 0 : narrow ? 0 : 20;
            routePanel.Visibility = graph ? Visibility.Collapsed : Visibility.Visible;
            SetColumn(routePanel, narrow ? 1 : 0); SetRowSpan(routePanel, narrow ? 1 : 2);
            SetRow(reading, narrow && !graph ? 1 : 0); SetRowSpan(reading, narrow && !graph ? 1 : 2);
            routePanel.Orientation = narrow ? Orientation.Horizontal : Orientation.Vertical;
            routes.Header = narrow ? null : "路线段"; routes.Width = narrow ? Math.Max(120, ActualWidth - 126) : double.NaN;
            attributionInfo.Visibility = narrow ? Visibility.Collapsed : Visibility.Visible;
            narrowAttribution.Visibility = narrow ? Visibility.Visible : Visibility.Collapsed;
            routeFacts.Visibility = narrow ? Visibility.Collapsed : Visibility.Visible;
            routePanel.Margin = new Thickness(0, 0, 0, narrow ? 10 : 0);
        };
        SizeChanged += (_, _) => reflow();
        scopeTimer.Tick += (_, _) => {
            if (!model.IsSourceCurrent && model.WorkId is not null) { model.SetSource(null); return; }
            if (authVersion == auth()) return;
            authVersion = auth(); model.Invalidate();
        };
        Loaded += (_, _) => scopeTimer.Start();
        Unloaded += (_, _) => { if (IsLoaded) return; scopeTimer.Stop(); flow.Hide(); if (model.Busy) model.Cancel(); };
        Render();
    }
    public async Task PrepareAsync()
    {
        if (disposed || initialRead || model.WorkId is null) return;
        initialRead = true; await ReadAsync();
    }
    private async Task OpenSourceAsync()
    {
        var url = model.Document?.Summary.SourceUrl; if (!Safe(url)) return;
        try { if (OpenLink is not null) await OpenLink(url!); else await Windows.System.Launcher.LaunchUriAsync(new Uri(url!)); }
        catch { status.Text = "原攻略出处暂未能打开。"; }
    }
    private async Task ReadAsync()
    {
        await model.RefreshAsync();
        if (!disposed && model.IsSourceCurrent && !model.Busy && model.Items.Length == 1 && model.Document is null)
            await model.SelectAsync(model.Items[0].Id);
    }
    private void StateChanged(object? sender, PropertyChangedEventArgs args) => Render();
    private void Render()
    {
        if (disposed) return; syncing = true;
        try
        {
            status.Text = model.Message; read.IsEnabled = !model.Busy && model.WorkId is not null;
            next.Visibility = model.NextCursor is null ? Visibility.Collapsed : Visibility.Visible; next.IsEnabled = !model.Busy;
            cancel.Visibility = model.Busy ? Visibility.Visible : Visibility.Collapsed;
            cancel.IsEnabled = model.Busy; read.Content = model.Document is null ? "读取攻略" : "重新读取";
            if (!shownGuides.SequenceEqual(model.Items))
            {
                shownGuides = model.Items.ToArray(); guides.Items.Clear();
                foreach (var guide in shownGuides) guides.Items.Add(SourceName(guide.Source) + " · " + CatalogReleaseFacts.LanguageCaption(guide.Language) + " · " + guide.Release);
            }
            guides.IsEnabled = !model.Busy && shownGuides.Length > 0;
            guides.Visibility = shownGuides.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
            guides.SelectedIndex = Array.FindIndex(shownGuides, s => s.Id == model.SelectedId);
            body.Visibility = model.Document is null ? Visibility.Collapsed : Visibility.Visible;
            if (!ReferenceEquals(shownDocument, model.Document))
            {
                shownDocument = model.Document; routes.Items.Clear(); shownSteps = 60;
                if (shownDocument is { } document)
                {
                    attribution.Text = SourceName(document.Summary.Source) + " · " + document.Summary.Release + " · 更新 " + document.Summary.UpdatedAt.Split('T')[0]
                        + (document.GameHasHints ? "\n本游戏自带路线提示，可先使用游戏内提示。" : "");
                    source.Visibility = Safe(document.Summary.SourceUrl) ? Visibility.Visible : Visibility.Collapsed;
                    foreach (var segment in document.Segments)
                    {
                        string section = document.Sections.FirstOrDefault(s => s.Key == segment.Section)?.Title.Display ?? "路线";
                        routes.Items.Add(section + " · " + segment.From.Label.Display + " → " + segment.To.Label.Display);
                    }
                    routes.SelectedIndex = document.Segments.IsEmpty ? -1 : 0;
                }
                RenderRoute();
            }
        }
        finally { syncing = false; }
    }
    private void RenderRoute()
    {
        var key = (shownDocument, routes.SelectedIndex, ShowGraph, original.IsOn, shownSteps);
        if (renderedRoute == key) return;
        int start = renderedRoute is { } previous && ReferenceEquals(previous.Document, shownDocument)
            && previous.Route == routes.SelectedIndex && !previous.Graph && !ShowGraph
            && previous.Original == original.IsOn && previous.Count < shownSteps ? previous.Count : 0;
        renderedRoute = key;
        if (start == 0) { steps.Children.Clear(); overview.Children.Clear(); overviewRoutes.Clear(); }
        stepScroll.Visibility = ShowGraph ? Visibility.Collapsed : Visibility.Visible;
        original.Visibility = ShowGraph ? Visibility.Collapsed : Visibility.Visible;
        map.Visibility = ShowGraph && listFallback ? Visibility.Visible : Visibility.Collapsed;
        flow.Visibility = ShowGraph && !listFallback ? Visibility.Visible : Visibility.Collapsed;
        reflow?.Invoke();
        moreSteps.Visibility = Visibility.Collapsed;
        if (shownDocument is not { } document || routes.SelectedIndex < 0 || routes.SelectedIndex >= document.Segments.Length) { flow.Hide(); return; }
        var segment = document.Segments[routes.SelectedIndex];
        routeFacts.Text = segment.From.Label.Display + " → " + segment.To.Label.Display
            + (segment.From.Requirement is null ? "" : "\n解锁条件：先完成 " + segment.From.Requirement)
            + (segment.Condition.Display.Length > 0 ? "\n条件：" + segment.Condition.Display : "");
        if (ShowGraph)
        {
            if (listFallback) { flow.Hide(); DrawMap(document); }
            else flow.Show(document,ActualTheme,segment.Section);
            return;
        }
        flow.Hide();
        int operation = segment.Steps.Take(start).Count(s => !GuideReadingText.IsHeading(s));
        for (int index = start; index < Math.Min(shownSteps, segment.Steps.Length); index++)
        {
            var step = segment.Steps[index]; var content = new StackPanel { Spacing = 5 };
            bool heading = GuideReadingText.IsHeading(step);
            if (!heading) content.Children.Add(Text($"{++operation:00}  {Kind(step.Kind)}" + (step.Achievement ? " · 成就相关" : ""), 12, true));
            content.Children.Add(Text(step.Text.Display.Length > 0 ? step.Text.Display : step.Text.Original, heading ? 18 : 16,
                heading || step.Kind is "choice" or "save" or "load"));
            if (original.IsOn && step.Text.Display != step.Text.Original)
            {
                var sourceText = Text(step.Text.Original, 14); sourceText.Opacity = .75; content.Children.Add(sourceText);
            }
            if (!step.Alternatives.IsEmpty) content.Children.Add(Text("其他选项", 12));
            foreach (var alternative in step.Alternatives) {
                content.Children.Add(Text("· " + alternative.Display, 14));
                if (original.IsOn && alternative.Display != alternative.Original) content.Children.Add(Text(alternative.Original, 13));
            }
            var card = new Border { Child = content, Padding = new Thickness(2, heading ? 18 : 9, 12, 9),
                BorderThickness = heading ? new Thickness(0, 0, 0, 1) : new Thickness(0),
                BorderBrush = new SolidColorBrush(ActualTheme == ElementTheme.Dark ? Windows.UI.Color.FromArgb(45, 200, 203, 210) : Windows.UI.Color.FromArgb(40, 90, 92, 98)) };
            AutomationProperties.SetAutomationId(card, "WalkthroughStep-" + index); steps.Children.Add(card);
        }
        moreSteps.Visibility = shownSteps < segment.Steps.Length ? Visibility.Visible : Visibility.Collapsed;
    }
    private void OpenStep(int route, int index)
    {
        if (shownDocument is not { } document || route < 0 || route >= document.Segments.Length
            || index < 0 || index > document.Segments[route].Steps.Length) return;
        syncing = true;
        try { routes.SelectedIndex = route; modes.SelectedItem = bodyTab; shownSteps = Math.Max(60,index+60); }
        finally { syncing = false; }
        RenderRoute(); UpdateLayout();
        DispatcherQueue.TryEnqueue(() => {
            if (disposed || ShowGraph || routes.SelectedIndex != route) return;
            if (steps.Children.OfType<Border>().FirstOrDefault(b => AutomationProperties.GetAutomationId(b)=="WalkthroughStep-"+index) is { } target)
            {
                var point = target.TransformToVisual((UIElement)stepScroll.Content).TransformPoint(new Windows.Foundation.Point());
                stepScroll.ChangeView(null,Math.Max(0,point.Y-8),null,true);
            }
            else stepScroll.ChangeView(null,0,null,true);
        });
    }
    private void DrawMap(WalkthroughDocument document)
    {
        var entries=WalkthroughOverview.Build(document);
        var guide=Text("按路线阅读分支。存档出处可单独查看，选择任意条目进入正文。",13);guide.Opacity=.75;overview.Children.Add(guide);
        string Section(int index)=>document.Sections.FirstOrDefault(s=>s.Key==document.Segments[index].Section)?.Title.Display??"路线";
        TextBlock Label(string value,double size,bool strong=false)
        {var text=Text(value,size,strong);text.IsTextSelectionEnabled=false;return text;}
        void Open(int index)
        {
            OpenStep(index,0);
        }
        FrameworkElement Branch(WalkthroughOverviewEntry entry,int depth)
        {
            var segment=document.Segments[entry.Index];var branch=new StackPanel { Spacing=0 };
            var row=new Grid { ColumnSpacing=10,Margin=new Thickness(Math.Min(depth,3)*14,0,0,0) };
            row.ColumnDefinitions.Add(new(){Width=new(18)});row.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});
            var connector=new Grid { IsHitTestVisible=false };
            connector.Children.Add(new Border { BorderBrush=(Brush)Microsoft.UI.Xaml.Application.Current.Resources["DividerBrush"],BorderThickness=new Thickness(1,0,0,0),Margin=new Thickness(5,0,0,0) });
            connector.Children.Add(new Border { Height=1,Background=(Brush)Microsoft.UI.Xaml.Application.Current.Resources["TextSecondaryBrush"],VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(5,0,0,0),Opacity=.5 });
            row.Children.Add(connector);
            var content=new StackPanel { Spacing=5,IsHitTestVisible=false };
            string from=segment.From.Kind switch { "save"=>"读取存档 · "+segment.From.Label.Display,"entry" or "start"=>"起点 · "+segment.From.Label.Display,_=>"接续 · "+segment.From.Label.Display };
            content.Children.Add(Label(from,12));
            var heading=Label(segment.To.Label.Display,16,true);heading.TextWrapping=TextWrapping.Wrap;content.Children.Add(heading);
            var conditions=string.Join(" · ",new[]{segment.From.Requirement is null?null:"需先完成 "+segment.From.Requirement,segment.Condition.Display}.Where(x=>!string.IsNullOrWhiteSpace(x)));
            if(conditions.Length>0){var condition=Label(conditions,12);condition.Opacity=.8;content.Children.Add(condition);}
            var saves=segment.Steps.Where(x=>x.Kind=="save").Select(x=>x.Text.Display.Length>0?x.Text.Display:x.SaveKey).OfType<string>().Distinct().ToArray();
            string saved=saves.Length==0?"":" · 存档："+string.Join("、",saves.Take(3))+(saves.Length>3?$" 等 {saves.Length} 处":"");
            var facts=Label($"{segment.Steps.Count(s=>!GuideReadingText.IsHeading(s))} 个步骤"+saved,12);facts.Opacity=.7;content.Children.Add(facts);
            var open=Label("阅读步骤  →",12);open.Foreground=(Brush)Microsoft.UI.Xaml.Application.Current.Resources["AccentTextFillColorPrimaryBrush"];content.Children.Add(open);
            var button=new Button { Content=content,Padding=new Thickness(12,10,12,10),HorizontalAlignment=HorizontalAlignment.Stretch,
                HorizontalContentAlignment=HorizontalAlignment.Stretch,Margin=new Thickness(0,0,0,8) };
            AutomationProperties.SetAutomationId(button,"WalkthroughRoute-"+segment.Key);
            AutomationProperties.SetName(button,Section(entry.Index)+"，"+from+"到"+segment.To.Label.Display+"，阅读步骤");
            button.Click+=(_,_)=>Open(entry.Index);overviewRoutes.Add(button);SetColumn(button,1);row.Children.Add(button);branch.Children.Add(row);
            if(entry.ParentIndex is null&&entry.Sources.Length>0)
            {
                var references=new StackPanel { Spacing=2,Margin=new Thickness(28,0,0,10) };
                if(entry.Sources.Length>1)references.Children.Add(Label("同名存档有多个记录位置，请核对：",12));
                foreach(var reference in entry.Sources.Take(8))
                {
                    var sourceSegment=document.Segments[reference.SourceIndex];
                    var label=Label((reference.Kind=="save"?"存档出处：":"接续出处：")+Section(reference.SourceIndex)+" · "+sourceSegment.To.Label.Display,12);label.IsHitTestVisible=false;
                    var link=new HyperlinkButton { Content=label,Padding=new Thickness(0,2,0,2),HorizontalAlignment=HorizontalAlignment.Left,HorizontalContentAlignment=HorizontalAlignment.Stretch };
                    AutomationProperties.SetName(link,label.Text);link.Click+=(_,_)=>Open(reference.SourceIndex);references.Children.Add(link);
                }
                branch.Children.Add(references);
            }
            foreach(var child in entries.Where(x=>x.ParentIndex==entry.Index))branch.Children.Add(Branch(child,depth+1));
            return branch;
        }
        foreach(var group in entries.GroupBy(e=>document.Segments[e.Index].Section))
        {
            var section=new StackPanel { Spacing=10 };
            var heading=new StackPanel { Spacing=3,Margin=new Thickness(0,0,0,2) };
            heading.Children.Add(Label(Section(group.First().Index),19,true));var total=Label($"{group.Count()} 条路线段",12);total.Opacity=.65;heading.Children.Add(total);section.Children.Add(heading);
            foreach(var root in group.Where(x=>x.ParentIndex is null))section.Children.Add(Branch(root,0));
            overview.Children.Add(section);
        }
        if(document.Segments.Length>entries.Length)overview.Children.Add(Text("此概览显示前 200 条路线段；全部路线仍可通过路线选择器阅读。",13));
    }
    private Border Card(UIElement content) => new() { Child = content, Padding = new Thickness(16), CornerRadius = new CornerRadius(10),
        BorderThickness = new Thickness(1), BorderBrush = new SolidColorBrush(ActualTheme == ElementTheme.Dark ? Colors.DimGray : Colors.LightGray),
        Background = new SolidColorBrush(ActualTheme == ElementTheme.Dark ? Windows.UI.Color.FromArgb(255, 32, 33, 39) : Windows.UI.Color.FromArgb(255, 248, 248, 251)) };
    private static bool HasSelectableText(DependencyObject element)
    {
        if(element is TextBlock { IsTextSelectionEnabled: true })return true;
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(element);i++)if(HasSelectableText(VisualTreeHelper.GetChild(element,i)))return true;
        return false;
    }
    private static TextBlock Text(string value, double size, bool strong = false) => new() { Text = value, FontSize = size,
        FontFamily = new FontFamily(strong && size >= 24 ? "SimSun" : size <= 15 ? "Segoe UI" : "Microsoft YaHei UI"),
        FontWeight = strong ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal,
        TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
    private static string Kind(string kind) => kind switch { "save" => "存档", "load" => "读档", "choice" => "选择", "action" => "操作", _ => "提示" };
    private static string SourceName(string source) => source switch { "seiya" => "誠也の部屋", "foolmaker" => "愚者之馆", "native" => "鲲社区", _ => source };
    private static bool Safe(string? url) => Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" && uri.UserInfo.Length == 0;
    public void Dispose() { if (disposed) return; disposed = true; scopeTimer.Stop(); flow.Dispose(); model.PropertyChanged -= StateChanged; model.Dispose(); }
}
