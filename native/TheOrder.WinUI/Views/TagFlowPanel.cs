using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace TheOrder.WinUI.Views;

// Small, nonvirtualized metadata chips. Images and library cards keep their existing virtualization.
public sealed class TagFlowPanel : Panel
{
    // XAML ancestors can round the natural width to a physical pixel. Do not add
    // a line at arrange solely because that slot differs by a fraction of a DIP.
    private const double WrapTolerance = .5;
    public double HorizontalSpacing { get; set; }
    public double VerticalSpacing { get; set; }
    public double MaximumItemWidth { get; set; } = 260;
    // Text and links center within a chip row; independent multiline fact groups
    // instead align at their top so a tall engine list cannot lower a date.
    public bool CenterInlineItems { get; set; } = true;
    protected override Size MeasureOverride(Size availableSize)
    {
        double limit = double.IsPositiveInfinity(availableSize.Width) ? double.MaxValue : availableSize.Width;
        double x = 0, y = 0, row = 0, width = 0;
        foreach (var child in Children)
        {
            child.Measure(new Size(Math.Min(limit, MaximumItemWidth), availableSize.Height));
            var size = child.DesiredSize;
            if (size.Width <= 0 || size.Height <= 0) continue;
            var gap = x > 0 ? HorizontalSpacing : 0;
            if (x > 0 && x + gap + size.Width > limit + WrapTolerance) { width = Math.Max(width, x); y += row + VerticalSpacing; x = row = gap = 0; }
            x += gap + size.Width; row = Math.Max(row, size.Height);
        }
        return new Size(Math.Max(width, x), y + row);
    }
    protected override Size ArrangeOverride(Size finalSize)
    {
        double x = 0, y = 0, row = 0;
        var items = new List<(UIElement Child, double X, Size Size)>();
        void ArrangeRow()
        {
            foreach (var item in items)
            {
                // Inline links and plain text share a centerline. Image/card rows stay top aligned.
                var offset = CenterInlineItems && (item.Child is TextBlock or HyperlinkButton) ? (row - item.Size.Height) / 2 : 0;
                item.Child.Arrange(new Rect(item.X, y + offset, Math.Min(item.Size.Width, finalSize.Width), item.Size.Height));
            }
            items.Clear();
        }
        foreach (var child in Children)
        {
            var size = child.DesiredSize;
            if (size.Width <= 0 || size.Height <= 0) continue;
            var gap = x > 0 ? HorizontalSpacing : 0;
            if (x > 0 && x + gap + size.Width > finalSize.Width + WrapTolerance) { ArrangeRow(); y += row + VerticalSpacing; x = row = gap = 0; }
            x += gap;
            items.Add((child, x, size));
            x += size.Width; row = Math.Max(row, size.Height);
        }
        ArrangeRow();
        return finalSize;
    }
}
