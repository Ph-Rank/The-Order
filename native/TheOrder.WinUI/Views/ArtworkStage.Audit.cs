using TheOrder.Application;
using Microsoft.UI.Xaml.Media;

namespace TheOrder.WinUI.Views;

public sealed partial class ArtworkStage
{
    // Invoked only by the isolated visual Test fixture, never by normal UI or Production.
    internal async Task<object> AuditLifecycleAsync()
    {
        if (!loaded || Effective != "Glass" || !HasCurrentPresentation || Images is null || coverKey is null)
            throw new InvalidOperationException("Audit requires a settled advanced stage");
        var savedImages = Images; var savedKey = coverKey; var savedMode = Mode;
        var savedAllowed = allowed; var savedContrast = highContrast;
        try
        {
            CoverKey = null;
            var invalidation = optical is null && panel.Visibility == Microsoft.UI.Xaml.Visibility.Collapsed && fallbackArt.Source is null;
            var invalidated = Evidence;
            CoverKey = savedKey;
            SetPolicy(false, false, MaterialMode.Standard);
            var safety = Effective == "Opaque" && optical is null && Plate?.Background is not AcrylicBrush;
            var blocked = Evidence;
            SetPolicy(true, true, MaterialMode.Advanced);
            var contrast = Effective == "HighContrast" && optical is null && fallbackArt.Visibility == Microsoft.UI.Xaml.Visibility.Collapsed;
            var highContrastEvidence = Evidence;
            Images = new MissingAuditImages();
            SetPolicy(true, false, MaterialMode.Auto);
            CoverKey = "isolated-missing-artwork";
            await Task.Delay(80);
            var missingImage = PresentationReady && HasCurrentPresentation && imageFailed && optical is null
                && Plate?.Background is SolidColorBrush { Color.A: > 0 };
            var missingImageEvidence = Evidence;
            // An intentionally cancellation-ignoring completion proves the consumer's lease guard,
            // rather than relying on cooperative decoders. No global cache/source is changed.
            var delayed = new DelayedAuditImages();
            Images = delayed;
            SetPolicy(true, false, MaterialMode.Advanced);
            CoverKey = "isolated-stage-audit";
            var pendingImage = !PresentationReady && !HasCurrentPresentation;
            ReleaseBeforeLeave();
            var createdAtLeave = RenderersCreated;
            delayed.Result.SetResult(new(new RasterPixels(2, 2, new byte[16]), "audit"));
            await Task.Delay(80);
            var lateCompletion = optical is null && pixelsKey is null && !attached && !PresentationReady && RenderersCreated == createdAtLeave;
            var released = Evidence;
            return new { passed = invalidation && safety && contrast && missingImage && pendingImage && lateCompletion, invalidation, safety, contrast, missingImage, pendingImage, lateCompletion, invalidated, blocked, highContrastEvidence, missingImageEvidence, released };
        }
        finally
        {
            Images = savedImages; CoverKey = savedKey;
            SetPolicy(savedAllowed, savedContrast, savedMode); Reattach();
        }
    }
    private sealed class DelayedAuditImages : IImagePipeline
    {
        internal readonly TaskCompletionSource<ImageResult> Result = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<ImageResult> LoadAsync(string? key, int edge, CancellationToken cancellation) => Result.Task;
        public ImageMetrics Metrics => new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private sealed class MissingAuditImages : IImagePipeline
    {
        public Task<ImageResult> LoadAsync(string? key, int edge, CancellationToken cancellation) => Task.FromResult(new ImageResult(null, "audit-missing"));
        public ImageMetrics Metrics => new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
