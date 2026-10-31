using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Markdig.Extensions.Tables;
using Markdig.Extensions.TaskLists;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using TheOrder.Application;
using TheOrder.Presentation;
using Windows.UI.Text;
namespace TheOrder.WinUI.Views;
// Native AST renderer: no HTML, scripting, browser or credential-bearing requests.
public sealed class MarkdownView : StackPanel, IImageHost
{
    private static readonly FontFamily MonospaceFont = new(TheOrder.Platform.Windows.WindowsCompatibility.IsWindows11(Environment.OSVersion.Version) ? "Cascadia Mono" : "Consolas");
    public static readonly DependencyProperty TextProperty=DependencyProperty.Register(nameof(Text),typeof(string),typeof(MarkdownView),new PropertyMetadata("",(d,e)=>((MarkdownView)d).Render()));
    public string Text {get=>(string)GetValue(TextProperty);set=>SetValue(TextProperty,value??"");}
    public IImagePipeline Images {get;set;}=null!;
    public IRemoteCoverRegistry? Registry {get;set;}
    public Func<string,Task>? OpenLink {get;set;}
    private int nodes,images;
    private (string Text, IImagePipeline? Images, IRemoteCoverRegistry? Registry, Func<string,Task>? Link, ElementTheme Theme)? rendered;
    internal int RenderCount { get; private set; }
    public MarkdownView(){Spacing=12;Loaded+=(_,_)=>Render();ActualThemeChanged+=(_,_)=>Render();}
    private void Render()
    {
        var key=(Text??"",Images,Registry,OpenLink,ActualTheme);
        if(rendered==key)return;
        rendered=key;RenderCount++;Children.Clear();nodes=images=0;
        try{Blocks(MarkdownContent.Parse(Text??""),this,0);}catch{Children.Clear();Children.Add(Plain(Text??""));}
        if((Text?.Length??0)>MarkdownContent.MaximumCharacters)Children.Add(Plain(UiText.Get("内容超过显示长度限制；原始文本已保留。")));
    }
    private static TextBlock Plain(string text)=>new(){Text=text,TextWrapping=TextWrapping.Wrap,IsTextSelectionEnabled=true,FontSize=15};
    private void Blocks(ContainerBlock source,Panel target,int depth)
    {
        if(depth>16)return;
        foreach(var block in source){if(++nodes>4096){target.Children.Add(Plain(UiText.Get("内容较长，已达到本页显示上限。")));return;}
            if(block is Table table){var grid=new Grid{ColumnSpacing=12,RowSpacing=8};int rowIndex=0;foreach(var row in table.OfType<TableRow>().Take(100)){grid.RowDefinitions.Add(new(){Height=GridLength.Auto});int col=0;foreach(var cell in row.OfType<TableCell>().Take(20)){if(grid.ColumnDefinitions.Count<=col)grid.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});var body=new StackPanel{MinWidth=100,MaxWidth=380,Spacing=6};Blocks(cell,body,depth+1);var border=new Border{Padding=new Thickness(8),Child=body,BorderThickness=new Thickness(0,0,0,1)};border.BorderBrush=(Brush)Microsoft.UI.Xaml.Application.Current.Resources["DividerBrush"];Grid.SetRow(border,rowIndex);Grid.SetColumn(border,col++);grid.Children.Add(border);}rowIndex++;}target.Children.Add(new ScrollViewer{Content=grid,HorizontalScrollMode=ScrollMode.Enabled,HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,VerticalScrollMode=ScrollMode.Disabled});}
            else if(block is ListBlock list){int number=int.TryParse(list.OrderedStart,out var start)?start:1;foreach(var item in list.OfType<ListItemBlock>()){var row=new Grid{ColumnSpacing=8};row.ColumnDefinitions.Add(new(){Width=GridLength.Auto});row.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});row.Children.Add(Plain(list.IsOrdered?(number++)+".":"•"));var body=new StackPanel{Spacing=6};Blocks(item,body,depth+1);Grid.SetColumn(body,1);row.Children.Add(body);target.Children.Add(row);}}
            else if(block is QuoteBlock quote){var body=new StackPanel{Spacing=8};Blocks(quote,body,depth+1);target.Children.Add(new Border{Child=body,Padding=new Thickness(12,4,0,4),BorderThickness=new Thickness(3,0,0,0),BorderBrush=(Brush)Microsoft.UI.Xaml.Application.Current.Resources["AccentBrush"]});}
            else if(block is CodeBlock code){target.Children.Add(new Border{Padding=new Thickness(12),CornerRadius=new CornerRadius(6),Background=(Brush)Microsoft.UI.Xaml.Application.Current.Resources["SubtleFillColorSecondaryBrush"],Child=new ScrollViewer{HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollMode=ScrollMode.Enabled,Content=new TextBlock{Text=code.Lines.ToString(),FontFamily=MonospaceFont,FontSize=13,IsTextSelectionEnabled=true}}});}
            else if(block is ThematicBreakBlock){target.Children.Add(new Border{Height=1,Background=(Brush)Microsoft.UI.Xaml.Application.Current.Resources["DividerBrush"]});}
            else if(block is LeafBlock leaf){var text=new RichTextBlock{FontSize=block is HeadingBlock heading?Math.Max(16,28-heading.Level*2):15,LineHeight=26,IsTextSelectionEnabled=true,TextWrapping=TextWrapping.Wrap};if(block is HeadingBlock)text.FontWeight=Microsoft.UI.Text.FontWeights.SemiBold;var paragraph=new Paragraph();if(leaf.Inline is not null)Inlines(leaf.Inline,paragraph.Inlines,depth+1,target);text.Blocks.Add(paragraph);target.Children.Add(text);}
            else if(block is ContainerBlock container)Blocks(container,target,depth+1);
        }
    }
    private void Inlines(ContainerInline source,InlineCollection target,int depth,Panel imageTarget)
    {
        if(depth>24)return;
        foreach(var inline in source){if(++nodes>4096)return;switch(inline){
            case LiteralInline literal:target.Add(new Run{Text=literal.Content.ToString()});break;
            case LineBreakInline:target.Add(new LineBreak());break;
            case CodeInline code:target.Add(new Run{Text=code.Content,FontFamily=MonospaceFont});break;
            case TaskList task:target.Add(new Run{Text=task.Checked?"☑ ":"☐ "});break;
            case EmphasisInline emphasis:Span span=emphasis.DelimiterChar=='~'?new Span{TextDecorations=TextDecorations.Strikethrough}:emphasis.DelimiterCount>=2?new Bold():new Italic();Inlines(emphasis,span.Inlines,depth+1,imageTarget);target.Add(span);break;
            case LinkInline link:
                if(link.IsImage){var alt=string.Concat(link.OfType<LiteralInline>().Select(l=>l.Content.ToString()));var key=MarkdownContent.AllowedLink(link.Url)&&link.Url!.StartsWith("https://",StringComparison.Ordinal)&&images<12?Registry?.Register(link.Url,"markdown"):null;if(key is not null){images++;imageTarget.Children.Add(new CoverPresenter{SourceKey=key,MaxWidth=640,Height=280,PreserveEntireImage=true,HorizontalAlignment=HorizontalAlignment.Left});}else target.Add(new Run{Text=alt});}
                else if(MarkdownContent.ResolveLink(link.Url) is { } url){var hyper=new Hyperlink();Inlines(link,hyper.Inlines,depth+1,imageTarget);hyper.Click+=async(_,_)=>{try{if(OpenLink is not null)await OpenLink(url);else await Windows.System.Launcher.LaunchUriAsync(new Uri(url));}catch{/* Keep the readable text; no automatic navigation retry. */}};target.Add(hyper);}
                else Inlines(link,target,depth+1,imageTarget);break;
            case AutolinkInline auto when MarkdownContent.AllowedLink(auto.Url):var ah=new Hyperlink();ah.Inlines.Add(new Run{Text=auto.Url});ah.Click+=async(_,_)=>{try{if(OpenLink is not null)await OpenLink(auto.Url);else await Windows.System.Launcher.LaunchUriAsync(new Uri(auto.Url));}catch{}};target.Add(ah);break;
            case ContainerInline container:Inlines(container,target,depth+1,imageTarget);break;
            default:target.Add(new Run{Text=inline.ToString()??""});break;
        }}
    }
}
