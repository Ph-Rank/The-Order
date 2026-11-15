using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using TheOrder.Application;
using TheOrder.Platform.Windows;
using TheOrder.WinUI.Materials;

namespace TheOrder.WinUI.Views;

public sealed partial class ArtworkStage
{
    private sealed record IndexedFrame(HeroFrameLayout Layout, WriteableBitmap Bitmap);
    private string? indexedAttempt;
    private CancellationTokenSource? indexedLookup;
    private IndexedFrame? indexedFrame;
    private bool indexedFrameShown;
    private long indexedFrameHits;

    private string IndexedFrameKey(string artwork, int width, double scale)
    {
        var optics = OpticalProfile.Daily;
        return FormattableString.Invariant(
            $"{HeroFrameRenderVersion}/{artwork}/{width}/{scale:R}/{ActualTheme}/{Variant}/{AmbientSigma:R}/{optics.BevelWidth:R}/{optics.Depth:R}/{optics.Gloss:R}");
    }

    private bool IndexedFrameFits()
    {
        if (indexedFrame is not { } frame || coverKey is null) return false;
        var scale = XamlRoot?.RasterizationScale ?? 1;
        var (width, height) = FramePixelSize((float)scale);
        return indexedAttempt == IndexedFrameKey(coverKey, width, scale)
            && frame.Bitmap.PixelWidth == width && frame.Bitmap.PixelHeight == height
            && frame.Layout.Geometry == CurrentGeometry();
    }

    private bool TryRestoreIndexedFrame(bool afterLayout)
    {
        if (FrameDiskCache is not { } cache || !CacheRenderedFrame || !NaturalLandscape || coverKey is null) return false;
        var scale = XamlRoot?.RasterizationScale ?? 1;
        var (width, _) = FramePixelSize((float)scale);
        if (width < 1) return false;
        var key = IndexedFrameKey(coverKey, width, scale);
        if (indexedAttempt != key)
        {
            CancelIndexedFrame();
            indexedAttempt = key;
            var lease = indexedLookup = new();
            _ = ReadIndexedFrameAsync(cache, key, width, lease);
            return true;
        }
        if (indexedLookup is not null) return true;
        if (indexedFrame is not { } frame) return false;
        if (!IndexedFrameFits())
        {
            if (!afterLayout) { QueueFirstGlassDraw(); return true; }
            DiscardIndexedFrame();
            return false;
        }

        if (!indexedFrameShown)
        {
            CancelFirstGlassDraw();
            // The composed bitmap already contains the final original and glass.
            // Cancel an old source request before it can replace this presentation.
            request.Clear();
            requestedImageKey = null;
            Polarity = frame.Layout.LightPlate ? StagePolarity.Lift : StagePolarity.Deepen;
            DisplayCachedBitmap(frame.Bitmap);
            indexedFrameShown = true;
            frameHits++;
            diskFrameHits++;
            indexedFrameHits++;
            ApplyLayers();
            FinishGlassFrame();
        }
        else SetPresentationReady(true);
        return true;
    }

    private async Task ReadIndexedFrameAsync(IsolatedHeroFrameCache cache, string key, int width, CancellationTokenSource lease)
    {
        HeroFrameImage? image = null;
        try
        {
            image = await cache.ReadAsync(key, width, lease.Token).WaitAsync(TimeSpan.FromMilliseconds(150), lease.Token);
        }
        catch (TimeoutException) { lease.Cancel(); }
        catch (OperationCanceledException) when (lease.IsCancellationRequested) { }
        catch (Exception error) { App.TraceVisualFailure(error); }
        if (!ReferenceEquals(indexedLookup, lease)) { lease.Dispose(); return; }
        indexedLookup = null;
        lease.Dispose();
        if (!loaded || EffectiveMode() != "Glass" || DetachedPlate || optical is not null) return;

        var scale = XamlRoot?.RasterizationScale ?? 1;
        var (currentWidth, _) = FramePixelSize((float)scale);
        if (image is not null && coverKey is not null && key == IndexedFrameKey(coverKey, currentWidth, scale))
        {
            try
            {
                indexedFrame = new(image.Layout, DisplayBitmaps.Create(image.Pixels));
                // Layout can now recover dimensions for personal or legacy banners
                // and the original side of the glass without decoding the source.
                SourceChanged?.Invoke();
            }
            catch (Exception error) { DiscardIndexedFrame(); App.TraceVisualFailure(error); }
        }
        Refresh();
    }

    private void PersistIndexedFrame(IsolatedHeroFrameCache cache, FrameKey key, RasterPixels artwork, RasterPixels image)
    {
        if (key.ImageEdge < RequiredImageEdge()) return;
        var layout = new HeroFrameLayout(artwork.Width, artwork.Height, Plate?.HorizontalAlignment == HorizontalAlignment.Right,
            key.Polarity == StagePolarity.Lift, key.Geometry);
        _ = WriteIndexedFrameAsync(cache, IndexedFrameKey(key.Artwork, key.Width, key.Scale), new(layout, image));
    }

    private static async Task WriteIndexedFrameAsync(IsolatedHeroFrameCache cache, string key, HeroFrameImage image)
    {
        try { await cache.WriteAsync(key, image).ConfigureAwait(false); }
        catch (Exception error) { App.TraceVisualFailure(error); }
    }

    private void DiscardIndexedFrame()
    {
        indexedFrame = null;
        if (!indexedFrameShown) return;
        indexedFrameShown = false;
        ClearCachedFrame();
        SetPresentationReady(false);
    }

    private void CancelIndexedFrame()
    {
        indexedAttempt = null;
        var lookup = indexedLookup;
        indexedLookup = null;
        lookup?.Cancel();
        DiscardIndexedFrame();
    }
}
