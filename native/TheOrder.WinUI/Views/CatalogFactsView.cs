using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using TheOrder.Domain;
using TheOrder.Application;

namespace TheOrder.WinUI.Views;

// The same read-only, current-version facts sit on the CG in Library and catalog details.
// Short landscapes place fact groups side by side to preserve the image overlay.
public sealed class CatalogFactsView : StackPanel
{
    private WorkMetadata? work;
    private string? displayedTitle;
    private bool compact;
    private bool dense;
    public bool Dense { get => dense; set { if (dense == value) return; dense = value; Spacing = value ? 3 : 5; Render(); } }
    private Brush? textForeground;
    public void SetTextForeground(Brush? brush)
    {
        textForeground = brush;
        foreach (var text in PlainTexts(this))
            if (brush is null) text.ClearValue(TextBlock.ForegroundProperty); else text.Foreground = brush;
    }
    private static IEnumerable<TextBlock> PlainTexts(Panel parent)
    {
        foreach (var child in parent.Children)
            if (child is TextBlock text) yield return text;
            else if (child is Panel panel) foreach (var nested in PlainTexts(panel)) yield return nested;
            else if (child is HyperlinkButton { Content: TextBlock label }) yield return label;
    }
    public Func<CatalogEntityRef, Task>? Browse { get; set; }
    public bool Compact
    {
        get => compact;
        set { if (compact == value) return; compact = value; Render(); }
    }
    public CatalogFactsView() { Spacing = 5; }
    public void SetWork(WorkMetadata? value, string? title = null)
    {
        work = value is null ? null : CatalogLanguage.Project(value); displayedTitle = title; Render();
    }
    private TextBlock Text(string value, double? size = null, double opacity = 1.0)
    {
        var text = new TextBlock
        {
            Text = value, FontSize = size ?? (compact ? 12 : dense ? 13 : 14), TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis, MaxLines = compact ? 1 : 3, Opacity = opacity,
            IsTextSelectionEnabled = true, VerticalAlignment = VerticalAlignment.Center
        };
        if (textForeground is not null) text.Foreground = textForeground;
        return text;
    }
    private UIElement Entity(CatalogEntityRef entity)
    {
        var link = new HyperlinkButton
        {
            Content = new TextBlock { Text = entity.Name, FontSize = compact ? 12 : dense ? 13 : 14,
                MaxLines = 1, TextTrimming = TextTrimming.CharacterEllipsis, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold },
            Padding = new Thickness(0, 0, 4, 0), MinHeight = 0, MinWidth = 0,
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(link, entity.Name + UiText.Get("，资料详情"));
        ToolTipService.SetToolTip(link, entity.Name);
        if (textForeground is not null && link.Content is TextBlock text) text.Foreground = textForeground;
        link.IsEnabled = TheOrder.Infrastructure.Online.RemoteWorkDto.ValidId(entity.Id);
        link.Click += async (_, _) => { if (Browse is not null) await Browse(entity); };
        return link;
    }
    private void Render()
    {
        Children.Clear(); Visibility = work is null ? Visibility.Collapsed : Visibility.Visible;
        if (work is null) return;
        var names = new[] { work.Title, work.OriginalTitle }.Where(n => !string.IsNullOrWhiteSpace(n) && n != displayedTitle).Distinct();
        var alternate = string.Join(" / ", names);
        if (!compact && alternate.Length > 0) Children.Add(Text(alternate, dense ? 12 : 13.5, 0.84));
        var companies = work.Presentation?.Companies ?? [];
        // Flow fact groups horizontally at all banner heights so short developer/date/engine
        // facts share one line instead of inflating plate height over the artwork.
        var factFlow = new TagFlowPanel
        {
            HorizontalSpacing = compact ? 14 : 18,
            VerticalSpacing = dense ? 3 : 4,
            MaximumItemWidth = double.PositiveInfinity,
            CenterInlineItems = false
        };
        void AddFact(UIElement element) => factFlow.Children.Add(element);
        var developerNames = work.Developers.IsEmpty ? companies.Select(c => c.Name).ToArray() : work.Developers.ToArray();
        if (developerNames.Length > 0)
        {
            var row = new TagFlowPanel { HorizontalSpacing = 6, VerticalSpacing = 2 };
            row.Children.Add(Text(work.Developers.IsEmpty ? UiText.Get("公司 ·") : UiText.Get("制作 ·"), opacity: 0.78));
            foreach (var name in developerNames.Take(compact ? 1 : 6))
            {
                var company = companies.FirstOrDefault(c => c.Name == name);
                if (company is not null) row.Children.Add(Entity(company));
                else
                {
                    var devText = Text(name);
                    devText.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
                    row.Children.Add(devText);
                }
            }
            ToolTipService.SetToolTip(row, string.Join(" / ", developerNames)); AddFact(row);
        }
        if (!string.IsNullOrWhiteSpace(work.ReleaseDate))
        {
            // Measure the label and date as one text paragraph. A nested flow can
            // break the date during arrange after its parent reserved one row.
            var release = Text(""); release.MaxLines = 2;
            release.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = UiText.Get("最早收录发行 · ") });
            release.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run {
                Text = CatalogReleaseFacts.WorkDate(work.ReleaseDate, work.ReleaseDatePrecision),
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
            AddFact(release);
        }
        if (work.Presentation is { Engines.IsEmpty: false } p)
        {
            var engines = new TagFlowPanel { HorizontalSpacing = 6, VerticalSpacing = 2 };
            engines.Children.Add(Text(UiText.Get("引擎 ·"), opacity: 0.78));
            foreach (var engine in p.Engines.Take(compact ? 1 : 4)) engines.Children.Add(Entity(engine));
            ToolTipService.SetToolTip(engines, UiText.Get("引擎 · ") + string.Join(" / ", p.Engines.Select(e => e.Name)));
            AddFact(engines);
        }
        if (factFlow.Children.Count > 0) Children.Add(factFlow);
        if (!work.Ratings.IsEmpty)
        {
            var ratings = work.Ratings.OrderBy(r => r.Source is "curated" or "kungal" or "nextmoe" ? 0 : 1).ToArray();
            var scores = new TagFlowPanel { HorizontalSpacing = compact ? 12 : 16, VerticalSpacing = 4 };
            foreach (var rating in ratings.Take(compact ? 2 : 6))
            {
                var text = Text("", compact || dense ? 12 : 13.5, 0.92);
                text.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = CatalogContentView.SiteName(rating.Source) + " " });
                text.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = $"{rating.Score:0.#}", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
                ToolTipService.SetToolTip(text, UiText.Format($"本版本评分 · {rating.Score:0.#}/{rating.Scale:0.#}") +
                    (rating.Votes is { } votes ? UiText.Format($"（{votes:0} 人）") : ""));
                scores.Children.Add(text);
            }
            Children.Add(scores);
        }
    }
    private static IEnumerable<TagFlowPanel> InlineRows(Panel parent)
    {
        foreach (var child in parent.Children)
            if (child is TagFlowPanel panel)
            {
                if (panel.Children.OfType<TagFlowPanel>().Any())
                    foreach (var nested in InlineRows(panel)) yield return nested;
                else yield return panel;
            }
    }
    internal object Evidence => new { workId = work?.Id, compact, width = ActualWidth, height = ActualHeight,
        rows = Children.Count, currentVersionOnly = true,
        inlineRows = InlineRows(this).Select(row => new { items = row.Children.OfType<FrameworkElement>().Select(e => new {
            kind = e.GetType().Name,
            text = e is TextBlock t ? t.Text : e is HyperlinkButton { Content: TextBlock c } ? c.Text : "",
            width = e.ActualWidth, height = e.ActualHeight,
            centerY = e.TransformToVisual(row).TransformPoint(new Windows.Foundation.Point(0, e.ActualHeight / 2)).Y
        }).ToArray() }).ToArray(),
        foreground = (textForeground as SolidColorBrush)?.Color.ToString(),
        textColors = PlainTexts(this).Select(t => (t.Foreground as SolidColorBrush)?.Color.ToString()).Distinct().ToArray() };
}
