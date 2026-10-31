using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace TheOrder.WinUI.Views;

// Track batches are bounded before reaching this layout. Measure their real
// heights so scrolling cannot change an estimated extent or recycle its anchor.
internal sealed class StableTrackLayout : NonVirtualizingLayout
{
    protected override Size MeasureOverride(NonVirtualizingLayoutContext context, Size available)
    {
        double width=double.IsFinite(available.Width)?available.Width:800,height=0;
        foreach(var child in context.Children){child.Measure(new(width,double.PositiveInfinity));height+=child.DesiredSize.Height;}
        return new(width,height);
    }
    protected override Size ArrangeOverride(NonVirtualizingLayoutContext context, Size final)
    {
        double y=0;
        foreach(var child in context.Children){double height=child.DesiredSize.Height;child.Arrange(new Rect(0,y,final.Width,height));y+=height;}
        return new(final.Width,y);
    }
}
