using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TheOrder.Application;

namespace TheOrder.WinUI.Views;

internal static class BannerFrameLayout
{
    internal static BannerFrame ArtworkFrame(Grid host, ArtworkStage stage, BannerChoice? choice)
    {
        var width = host.ActualWidth;
        if (width < 1) return default;
        var sourceWidth = choice?.Width > 0 ? choice.Width : stage.SourceWidth;
        var sourceHeight = choice?.Height > 0 ? choice.Height : stage.SourceHeight;
        // Keep a loading surface in the tree for legacy selections/local originals
        // whose dimensions are known only after their first decode.
        if (stage.CoverKey is not null && sourceWidth <= 0) { sourceWidth = 1280; sourceHeight = 720; }
        if (stage.CoverKey is null || stage.ImageUnavailable) sourceWidth = sourceHeight = 0;
        return BannerLayout.Plan(width, sourceWidth, sourceHeight, 0, 0);
    }
    internal static BannerFrame Apply(Grid host, ArtworkStage stage, Grid overlay, Border plate, BannerChoice? choice)
    {
        var frame = ArtworkFrame(host, stage, choice);
        var width = host.ActualWidth;
        if (width < 1) return default;
        bool compact = frame.Width < 560, dense = frame.Height < 340;
        double margin = compact ? 8 : dense ? 12 : 28;
        plate.Width = double.NaN;
        plate.Measure(new Windows.Foundation.Size(plate.MaxWidth, double.PositiveInfinity));
        // Try the full width before ever moving text below. This usually turns a
        // wrapped ratings/statistics row into one line on short official heroes.
        if (frame.Height > 0 && plate.DesiredSize.Height + 2 * margin > frame.Height)
        {
            plate.MaxWidth = plate.Width = Math.Max(1, frame.Width - 2 * margin);
            plate.Measure(new Windows.Foundation.Size(plate.MaxWidth, double.PositiveInfinity));
        }
        var placement = stage.SuggestPlacement(plate.DesiredSize.Width / Math.Max(1, frame.Width), plate.DesiredSize.Height / Math.Max(1, frame.Height));
        bool docked = frame.Height <= 0 || plate.DesiredSize.Height + 2 * margin > frame.Height + .5;
        frame = frame with { Docked = docked };
        Grid.SetRow(overlay, frame.Docked ? 1 : 0);
        // One information panel: overlay first, below only when measured text cannot fit.
        // No duplicated text or composition buffers, and no text clipped to the art.
        overlay.Padding = frame.Docked ? new Thickness(0) : new Thickness(margin);
        overlay.Margin = frame.Docked && frame.Height > 0 ? new Thickness(0, 12, 0, 0) : new Thickness(0);
        overlay.Width = frame.Docked ? double.NaN : frame.Width;
        overlay.HorizontalAlignment = frame.Docked ? HorizontalAlignment.Stretch : HorizontalAlignment.Center;
        plate.HorizontalAlignment = frame.Docked ? HorizontalAlignment.Stretch : placement.Right ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        if (frame.Docked) { plate.Width = double.NaN; plate.MaxWidth = width; }
        stage.DetachedPlate = frame.Docked;
        stage.Width = frame.Width;
        stage.Height = frame.Height;
        stage.HorizontalAlignment = HorizontalAlignment.Center;
        stage.Visibility = frame.Height > 0 ? Visibility.Visible : Visibility.Collapsed;
        return frame;
    }
}
