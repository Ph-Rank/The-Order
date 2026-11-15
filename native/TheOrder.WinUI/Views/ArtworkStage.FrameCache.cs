using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using TheOrder.Application;
using TheOrder.Platform.Windows;
using TheOrder.WinUI.Materials;

namespace TheOrder.WinUI.Views;

public sealed partial class ArtworkStage
{
    // Bump when artwork sampling or glass rendering changes the composed pixels.
    private const int HeroFrameRenderVersion = 1;
    private const long FrameCacheBytes = 48L * 1024 * 1024;
    private const int MaximumFrames = 4;
    private readonly record struct FrameKey(string Artwork, int ImageEdge, string Geometry,
        int Width, int Height, double Scale, ElementTheme Theme, StageVariant Variant, StagePolarity Polarity, float Sigma);
    private sealed record CachedFrame(FrameKey Key, WriteableBitmap Bitmap, RasterPixels Artwork)
    {
        internal long Bytes => (long)Bitmap.PixelWidth * Bitmap.PixelHeight * 4 + Artwork.Bytes;
    }
    private readonly LinkedList<CachedFrame> frames = new();
    private Image? frameImage;
    private FrameKey? showingFrame;
    private FrameKey? pendingFrame;
    private int frameWidth, frameHeight, readAttempts;
    private byte[]? frameReadback;
    private long frameBytes, frameHits, diskFrameHits, sourceRequests;
    private FrameKey? attemptedDiskFrame;
    private CancellationTokenSource? diskLookup;
    private bool firstDrawQueued;
    private long firstDrawRevision;

    internal bool CacheRenderedFrame { get; set; }
    internal IsolatedHeroFrameCache? FrameDiskCache { get; set; }
    internal bool IsShowingCachedFrame => showingFrame.HasValue || indexedFrameShown;

    private FrameKey? CurrentFrameKey()
    {
        if (!CacheRenderedFrame || !NaturalLandscape || DetachedPlate || coverKey is null || pixelsKey != coverKey)
            return null;
        var scale = XamlRoot?.RasterizationScale ?? 1;
        var (width, height) = FramePixelSize((float)scale);
        return new(coverKey, loadedImageEdge, CurrentGeometry(), width, height, scale, ActualTheme, Variant, Polarity, AmbientSigma);
    }

    private (int Width, int Height) FramePixelSize(float scale) =>
        ((int)Math.Ceiling(ActualWidth * scale), (int)Math.Ceiling(ActualHeight * scale));

    private static string DiskLayout(FrameKey key)
    {
        var optics = OpticalProfile.Daily;
        return FormattableString.Invariant($"{HeroFrameRenderVersion}/{key.Geometry}/{key.Width}/{key.Height}/{key.Scale:R}/{key.Theme}/{key.Variant}/{key.Polarity}/{key.Sigma:R}/{optics.BevelWidth:R}/{optics.Depth:R}/{optics.Gloss:R}");
    }

    private bool TryLoadDiskFrame()
    {
        if (FrameDiskCache is not { } cache || originalLandscape is not { } artwork || CurrentFrameKey() is not { } key ||
            !IsolatedHeroFrameCache.Fits(key.Width, key.Height) || PlateRect() is not { Width: >= 64, Height: >= 64 }) return false;
        if (attemptedDiskFrame == key) return diskLookup is not null;

        CancelDiskLookup();
        attemptedDiskFrame = key;
        var lookup = diskLookup = new();
        _ = LoadDiskFrameAsync(cache, key, artwork, lookup);
        return true;
    }

    private async Task LoadDiskFrameAsync(IsolatedHeroFrameCache cache, FrameKey key, RasterPixels artwork,
        CancellationTokenSource lookup)
    {
        RasterPixels? image = null;
        try
        {
            // A slow disk must not hold up the native fallback.
            image = await cache.ReadAsync(DiskLayout(key), artwork, key.Width, key.Height, lookup.Token)
                .WaitAsync(TimeSpan.FromMilliseconds(150), lookup.Token);
        }
        catch (TimeoutException) { lookup.Cancel(); }
        catch (OperationCanceledException) when (lookup.IsCancellationRequested) { }
        catch (Exception error) { App.TraceVisualFailure(error); }
        if (!ReferenceEquals(diskLookup, lookup)) { lookup.Dispose(); return; }
        diskLookup = null;
        lookup.Dispose();

        if (!loaded || EffectiveMode() != "Glass" || DetachedPlate) return;
        try
        {
            if (image is not null && CurrentFrameKey() == key && ReferenceEquals(originalLandscape, artwork)
                && RememberFrame(key, image, artwork) && TryShowCachedFrame())
            {
                diskFrameHits++;
                PersistIndexedFrame(cache, key, artwork, image);
                return;
            }
        }
        catch (Exception error) { App.TraceVisualFailure(error); }
        Refresh();
    }

    private void CancelDiskLookup()
    {
        attemptedDiskFrame = null;
        var lookup = diskLookup;
        diskLookup = null;
        lookup?.Cancel();
    }

    private bool TryRestoreFrameArtwork(string key, int edge)
    {
        if (!CacheRenderedFrame) return false;
        foreach (var frame in frames)
        {
            if (frame.Key.Artwork != key || frame.Key.ImageEdge < edge) continue;
            AcceptPixels(key, frame.Key.ImageEdge, frame.Artwork);
            return true;
        }
        return false;
    }

    private bool TryShowCachedFrame()
    {
        if (CurrentFrameKey() is not { } key) return false;
        if (showingFrame == key)
        {
            SetPresentationReady(true);
            return true;
        }
        for (var node = frames.First; node is not null; node = node.Next)
        {
            if (node.Value.Key != key) continue;
            Release();
            if (retirementPending) return false;
            frames.Remove(node);
            frames.AddFirst(node);
            DisplayCachedBitmap(node.Value.Bitmap);
            showingFrame = key;
            frameHits++;
            FinishGlassFrame();
            return true;
        }
        return false;
    }

    private void DisplayCachedBitmap(WriteableBitmap bitmap)
    {
        frameImage ??= new Image { Stretch = Stretch.Fill, IsHitTestVisible = false };
        if (!artworkLayers.Children.Contains(frameImage)) artworkLayers.Children.Add(frameImage);
        frameImage.Source = bitmap;
        frameImage.Visibility = Visibility.Visible;
    }

    private void ClearCachedFrame()
    {
        if (frameImage is not null)
        {
            frameImage.Source = null;
            frameImage.Visibility = Visibility.Collapsed;
        }
        showingFrame = null;
    }

    // Let the readable title preview get a frame before a cold device/scene
    // setup. A warm frame can be shown during layout without scheduling a draw.
    private void QueueFirstGlassDraw()
    {
        if (firstDrawQueued) return;
        firstDrawQueued = true;
        CompositionTarget.Rendering += OnFirstGlassFrame;
    }

    private void OnFirstGlassFrame(object? sender, object args)
    {
        CompositionTarget.Rendering -= OnFirstGlassFrame;
        var revision = firstDrawRevision;
        if (!DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            if (revision != firstDrawRevision) return;
            firstDrawQueued = false;
            Refresh(allowColdDraw: true);
        })) firstDrawQueued = false;
    }

    private void CancelFirstGlassDraw()
    {
        if (!firstDrawQueued) return;
        CompositionTarget.Rendering -= OnFirstGlassFrame;
        firstDrawQueued = false;
        firstDrawRevision++;
    }

    private bool ShouldCacheFrame(FrameKey key, int width, int height) =>
        IsolatedHeroFrameCache.Fits(width, height) &&
        pendingFrame != key && !frames.Any(frame => frame.Key == key);

    private void QueueFrameReadback(FrameKey key, int width, int height)
    {
        if (!pendingFrame.HasValue) CompositionTarget.Rendering += ReadRenderedFrame;
        pendingFrame = key;
        frameWidth = width;
        frameHeight = height;
        readAttempts = 0;
        frameReadback = null;
    }

    private void ReadRenderedFrame(object? sender, object args)
    {
        if (pendingFrame is not { } key || CurrentFrameKey() != key || !HasCurrentPresentation
            || optical is not { } renderer || originalLandscape is not { } artwork)
        {
            CancelFrameReadback();
            return;
        }
        var retry = false;
        try
        {
            var buffer = frameReadback ??= GC.AllocateUninitializedArray<byte>(checked(frameWidth * frameHeight * 4));
            var result = renderer.ReadCachedFrame(buffer);
            if (result == 1 && ++readAttempts < 4)
            {
                retry = true;
                return;
            }
            if (result == 0)
            {
                var image = new RasterPixels(frameWidth, frameHeight, buffer);
                RememberFrame(key, image, artwork);
                if (FrameDiskCache is { } cache) PersistIndexedFrame(cache, key, artwork, image);
            }
        }
        catch (Exception error) { App.TraceVisualFailure(error); }
        finally
        {
            // The return above for a busy GPU intentionally leaves the one-shot
            // readback queued, with at most four frame opportunities.
            if (!retry) CancelFrameReadback();
        }
    }

    private bool RememberFrame(FrameKey key, RasterPixels image, RasterPixels artwork)
    {
        if (image.Bytes + artwork.Bytes > FrameCacheBytes) return false;
        var frame = new CachedFrame(key, DisplayBitmaps.Create(image), artwork);
        while (frames.Last is { } old && (frames.Count >= MaximumFrames || frameBytes + frame.Bytes > FrameCacheBytes))
        {
            frameBytes -= old.Value.Bytes;
            frames.RemoveLast();
        }
        frames.AddFirst(frame);
        frameBytes += frame.Bytes;
        return true;
    }

    private void CancelFrameReadback()
    {
        if (!pendingFrame.HasValue) return;
        CompositionTarget.Rendering -= ReadRenderedFrame;
        pendingFrame = null;
        frameReadback = null;
        optical?.DiscardCachedFrame();
    }
}
