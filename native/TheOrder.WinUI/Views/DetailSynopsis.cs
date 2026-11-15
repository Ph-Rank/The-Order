using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace TheOrder.WinUI.Views;

// Readable prose with a bounded initial height; the page remains the only scroller.
internal sealed class DetailSynopsis : StackPanel
{
    private readonly TextBlock heading = new() { FontSize = 20, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock prose = new() { FontSize = 15, LineHeight = 25, TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis, MaxLines = 7, IsTextSelectionEnabled = true };
    private readonly HyperlinkButton toggle = new() { Content = "展开全文", Padding = new(0, 4, 0, 4), HorizontalAlignment = HorizontalAlignment.Left, MinHeight = 32, Visibility = Visibility.Collapsed };
    private bool expanded;
    internal bool Expanded { get => expanded; set { expanded = value; prose.MaxLines = expanded ? 0 : 7; UpdateToggle(); } }
    public string Heading { get => heading.Text; set { heading.Text = value; heading.Visibility = string.IsNullOrEmpty(value) ? Visibility.Collapsed : Visibility.Visible; } }
    public string Text
    {
        get => prose.Text;
        set { if (prose.Text == value) return; expanded = false; prose.MaxLines = 7; prose.Text = value ?? ""; UpdateToggle(); }
    }
    public DetailSynopsis()
    {
        Spacing = 10; MaxWidth = 860; HorizontalAlignment = HorizontalAlignment.Left;
        Children.Add(heading); Children.Add(prose); Children.Add(toggle);
        prose.IsTextTrimmedChanged += (_, _) => UpdateToggle();
        toggle.Click += (_, _) =>
        {
            expanded = !expanded; prose.MaxLines = expanded ? 0 : 7; UpdateToggle();
            if (expanded) InteractionMotion.RevealContent(prose, .74f);
            else StartBringIntoView(new BringIntoViewOptions { AnimationDesired = InteractionMotion.Enabled });
        };
    }
    private void UpdateToggle() { toggle.Content = expanded ? "收起全文" : "展开全文"; toggle.Visibility = expanded || prose.IsTextTrimmed ? Visibility.Visible : Visibility.Collapsed; }
    internal void ExpandForFixture() => Expanded = true;
    internal object Evidence => new { expanded, trimmed = prose.IsTextTrimmed, maxLines = prose.MaxLines, width = ActualWidth, textLength = prose.Text.Length };
}
