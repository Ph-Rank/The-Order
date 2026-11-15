using System.Collections.Immutable;

namespace TheOrder.Application;

// Geometry only: neither recognized strings nor source image content are stored.
public readonly record struct ArtworkTextRegion(double X, double Y, double Width, double Height, double Confidence);
public sealed record ArtworkTextDetection(bool Available, ImmutableArray<ArtworkTextRegion> Regions, string Status);
public interface IArtworkTextDetector : IAsyncDisposable
{
    Task<ArtworkTextDetection> AnalyzeAsync(RasterPixels pixels, CancellationToken cancellation);
}
public static class ArtworkTextRules
{
    public static bool ContainsOverlayText(ArtworkTextDetection result) => ContainsOverlayText(result, allowTitleMark: false);

    internal static bool ContainsOverlayText(ArtworkTextDetection result, bool allowTitleMark)
    {
        var regions = result.Regions.Where(r => double.IsFinite(r.Confidence) && r.Confidence >= .5 &&
            r.Width >= .012 && r.Height >= .008 && r.Width * r.Height >= .00015).ToArray();
        if (regions.Length == 0 || regions.Length == 1 && regions[0] is { Y: >= .95, Width: <= .30, Height: <= .028 } stamp &&
            (stamp.X <= .03 || stamp.X + stamp.Width >= .95)) return false;
        return !allowTitleMark || !IsCompactTitleMark(regions);
    }

    private static bool IsCompactTitleMark(ArtworkTextRegion[] regions)
    {
        // Verified promotional art can have one prominent title and tightly grouped supporting text.
        // Small dialogue text, wide captions, and separate menu labels cannot form this group.
        if (regions.Length > 6) return false;
        var title = regions.MaxBy(r => r.Height);
        return title.Width is >= .14 and <= .58 && title.Height >= .045 &&
            regions.Max(r => r.Y + r.Height) - regions.Min(r => r.Y) <= .20 &&
            regions.All(r => r == title || r.Height <= title.Height * .67 &&
                r.X >= title.X - .04 && r.X + r.Width <= title.X + title.Width + .04);
    }
}
