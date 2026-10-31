using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Markdig.Extensions.Tables;
using System.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using TheOrder.Presentation;
using TheOrder.Application.Playground;
using Windows.ApplicationModel.DataTransfer;

namespace TheOrder.WinUI.Views;

// A message-owned renderer. It never fetches images or interprets HTML. Finished
// blocks stay stable while the active tail is coalesced by PlaygroundView.
internal sealed class PlaygroundMarkdownView : StackPanel
{
    private readonly Func<string, Task> openLink;
    private readonly Func<PlaygroundSource, Task>? openSource;
    private readonly PlaygroundCitations citations;
    private readonly List<Border> surfaces = [];
    private readonly List<Border> rules = [];
    private readonly List<Hyperlink> links = [];
    private int nodes, hyperlinkDepth;
    private bool themePaintQueued;
    internal int RenderedCitations { get; private set; }
    public PlaygroundMarkdownView(string text, Func<string, Task> openLink, PlaygroundCitations citations, Func<PlaygroundSource, Task>? openSource = null)
    {
        this.openLink = openLink; this.openSource = openSource; Spacing = 12;
        this.citations = citations;
        try { Blocks(MarkdownContent.Parse(text), this, 0); }
        catch { Children.Clear(); RenderedCitations = 0; Children.Add(Plain(text)); }
        if (text.Length > MarkdownContent.MaximumCharacters) Children.Add(Plain("内容超过本次显示上限，原始消息仍保留。"));
        Loaded += (_, _) => QueuePaint(); ActualThemeChanged += (_, _) => QueuePaint();
    }
    private static TextBlock Plain(string value) => new() { Text = value, FontSize = 15.5, LineHeight = 27,
        TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
    private void Blocks(ContainerBlock source, Panel target, int depth)
    {
        if (depth > 16) return;
        foreach (var block in source)
        {
            if (++nodes > 4096) { target.Children.Add(Plain("本条消息较长，已达到显示上限。")); return; }
            if (block is CodeBlock code)
            {
                var codeText = code.Lines.ToString();
                var box = new Grid { RowSpacing = 8 };
                box.RowDefinitions.Add(new() { Height = GridLength.Auto }); box.RowDefinitions.Add(new() { Height = GridLength.Auto });
                var copy = new Button { Content = "复制代码", FontSize = 12, Padding = new Thickness(8, 4, 8, 4), HorizontalAlignment = HorizontalAlignment.Right };
                PlaygroundAppearance.Apply(copy); copy.Content = PlaygroundAppearance.IconLabel("\uE8C8", "复制代码", 12);
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(copy, "复制代码块");
                copy.Click += (_, _) => Copy(codeText);
                box.Children.Add(copy);
                var body = new ScrollViewer { HorizontalScrollMode = ScrollMode.Enabled, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                    VerticalScrollMode = ScrollMode.Disabled, Content = new TextBlock { Text = codeText, FontFamily = new FontFamily("Consolas"),
                        FontSize = 13, IsTextSelectionEnabled = true, TextWrapping = TextWrapping.NoWrap } };
                Grid.SetRow(body, 1); box.Children.Add(body);
                var surface = new Border { Child = box, Padding = new Thickness(12), CornerRadius = new CornerRadius(10) }; surfaces.Add(surface); target.Children.Add(surface);
            }
            else if (block is Table table)
            {
                var grid = new Grid { ColumnSpacing = 8, RowSpacing = 6 }; int r = 0;
                foreach (var row in table.OfType<TableRow>().Take(100))
                {
                    grid.RowDefinitions.Add(new() { Height = GridLength.Auto }); int c = 0;
                    foreach (var cell in row.OfType<TableCell>().Take(20))
                    {
                        if (grid.ColumnDefinitions.Count <= c) grid.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
                        var body = new StackPanel { MinWidth = 100, MaxWidth = 340, Spacing = 6 }; Blocks(cell, body, depth + 1);
                        var border = new Border { Child = body, Padding = new Thickness(8), BorderThickness = new Thickness(0, 0, 0, 1) }; rules.Add(border);
                        Grid.SetColumn(border, c++); Grid.SetRow(border, r); grid.Children.Add(border);
                    }
                    r++;
                }
                target.Children.Add(new ScrollViewer { Content = grid, HorizontalScrollMode = ScrollMode.Enabled,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollMode = ScrollMode.Disabled });
            }
            else if (block is ListBlock list)
            {
                int number = int.TryParse(list.OrderedStart, out int first) ? first : 1;
                foreach (var item in list.OfType<ListItemBlock>())
                {
                    var row = new Grid { ColumnSpacing = 8 }; row.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); row.ColumnDefinitions.Add(new());
                    row.Children.Add(Plain(list.IsOrdered ? (number++) + "." : "•"));
                    var body = new StackPanel { Spacing = 6 }; Blocks(item, body, depth + 1); Grid.SetColumn(body, 1); row.Children.Add(body); target.Children.Add(row);
                }
            }
            else if (block is QuoteBlock quote)
            {
                var body = new StackPanel { Spacing = 8 }; Blocks(quote, body, depth + 1);
                var rule = new Border { Child = body, Padding = new Thickness(14, 3, 0, 3), BorderThickness = new Thickness(2, 0, 0, 0) }; rules.Add(rule); target.Children.Add(rule);
            }
            else if (block is ThematicBreakBlock) { var rule = new Border { Height = 1 }; rules.Add(rule); target.Children.Add(rule); }
            else if (block is LeafBlock leaf)
            {
                var body = new RichTextBlock { FontSize = block is HeadingBlock heading ? Math.Max(16, 23 - heading.Level) : 15.5,
                    LineHeight = 27, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
                if (block is HeadingBlock) body.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
                var paragraph = new Paragraph(); if (leaf.Inline is not null) Inlines(leaf.Inline, paragraph.Inlines, 0); body.Blocks.Add(paragraph); target.Children.Add(body);
            }
            else if (block is ContainerBlock container) Blocks(container, target, depth + 1);
        }
    }
    private void Inlines(ContainerInline source, InlineCollection target, int depth)
    {
        if (depth > 24) return;
        for (var item = source.FirstChild; item is not null; item = item.NextSibling)
        {
            if (++nodes > 4096) return;
            switch (item)
            {
                case LiteralInline literal:
                    var literalText = new StringBuilder(literal.Content.ToString());
                    while (item.NextSibling is LiteralInline next && nodes < 4096) { literalText.Append(next.Content.ToString()); item = next; nodes++; }
                    Literal(literalText.ToString(), target); break;
                case LineBreakInline: target.Add(new LineBreak()); break;
                case CodeInline code: target.Add(new Run { Text = code.Content, FontFamily = new FontFamily("Consolas") }); break;
                case EmphasisInline emphasis:
                    Span span = emphasis.DelimiterChar == '~' ? new Span { TextDecorations = Windows.UI.Text.TextDecorations.Strikethrough }
                        : emphasis.DelimiterCount >= 2 ? new Bold() : new Italic();
                    Inlines(emphasis, span.Inlines, depth + 1); target.Add(span); break;
                case LinkInline link when !link.IsImage && CanOpen(link.Url):
                    var hyperlink = new Hyperlink(); hyperlinkDepth++;
                    try { Inlines(link, hyperlink.Inlines, depth + 1); } finally { hyperlinkDepth--; }
                    links.Add(hyperlink);
                    hyperlink.Click += async (_, _) => { try { await openLink(link.Url!); } catch { } }; target.Add(hyperlink); break;
                case AutolinkInline automatic when CanOpen(automatic.Url):
                    var auto = new Hyperlink(); auto.Inlines.Add(new Run { Text = automatic.Url });
                    links.Add(auto);
                    auto.Click += async (_, _) => { try { await openLink(automatic.Url); } catch { } }; target.Add(auto); break;
                case AutolinkInline automatic: target.Add(new Run { Text = automatic.Url }); break;
                case ContainerInline container: Inlines(container, target, depth + 1); break;
                default: target.Add(new Run { Text = item.ToString() ?? "" }); break;
            }
        }
    }
    private void Literal(string value, InlineCollection target)
    {
        foreach (var part in citations.Literal(value))
        {
            if (openSource is not null && part.Source is { } source)
            {
                RenderedCitations++;
                if (hyperlinkDepth != 0)
                    target.Add(new Run { Text = part.Text });
                else
                {
                    var marker = new Hyperlink { UnderlineStyle = UnderlineStyle.None, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
                    // WORD JOINER prohibits an automatic break before the
                    // reference. Unicode superscripts need no UIElement box,
                    // padding or baseline compensation in the native text run.
                    marker.Inlines.Add(new Run { Text = part.Text });
                    links.Add(marker);
                    Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(marker, "引用 " + part.Number + "：" + source.Title);
                    Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(marker, "PlaygroundCitation-" + RenderedCitations);
                    marker.Click += async (_, _) => { try { await openSource(source); } catch { } };
                    target.Add(marker);
                }
            }
            else target.Add(new Run { Text = part.Source is null ? part.Text : "[未核验引用]" });
        }
    }
    private bool CanOpen(string? value) => Allowed(value) && (openSource is null || citations.AllowsUrl(value!));
    private void Paint()
    {
        foreach (var surface in surfaces) surface.Background = PlaygroundAppearance.Brush(this, "PGSurfaceBrush");
        foreach (var rule in rules) { rule.BorderBrush = PlaygroundAppearance.Brush(this, "PGBorderBrush"); if (rule.Height == 1) rule.Background = rule.BorderBrush; }
        foreach (var link in links) link.Foreground = PlaygroundAppearance.Brush(this, "PGTextBrush");
    }
    private void QueuePaint()
    {
        if (themePaintQueued) return; themePaintQueued = true;
        DispatcherQueue.TryEnqueue(() => { themePaintQueued = false; if (IsLoaded) Paint(); });
    }
    internal void RefreshTheme() => Paint();
    internal static bool Allowed(string? value) => PlaygroundEndpointPolicy.SafePublicUrl(value);
    internal static void Copy(string value)
    {
        try { var package = new DataPackage(); package.SetText(value); Clipboard.SetContent(package); }
        catch { /* Clipboard contention must not affect the saved reply. */ }
    }
}
