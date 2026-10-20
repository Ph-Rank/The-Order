using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using TheOrder.Application;
using TheOrder.Domain;
using TheOrder.WinUI.Views;
using Windows.Foundation;

namespace TheOrder.WinUI;

public sealed partial class MainWindow
{
    private static WorkMetadata ArtworkRegressionWork() => new("200", "True Colors -memories of the abyss-", "True Colors -memories of the abyss-",
        [], null, null, null, null, "", "2025-12-26", null, ["MELLOW"], [],
        [new("vndb", 6.9, 10, null), new("bangumi", 7.2, 10, null), new("erogamescape", 72, 100, null)],
        [], [], null, null, "2026-10-20T00:00:00Z");
    private async Task RunArtworkDetailsAsync(string label)
    {
        if (visualFixtureTimer is null || fixtureDirectory is null || Program.Profile?.Manifest.ProfileKind != NativeProfileKind.Test ||
            label.Length is 0 or > 60 || label.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-')) throw new InvalidOperationException("Bounded Test required.");
        PageContent.Content = detailsPage; await Task.Delay(120);
        var result = await detailsPage.AuditArtworkLayoutAsync(ArtworkRegressionWork(), new ArtworkRegressionImages());
        await File.WriteAllTextAsync(Path.Combine(fixtureDirectory, "details-" + label + ".json"), JsonSerializer.Serialize(result));
    }
    // Bounded, synthetic WinUI regression. No library, filesystem, account or network input.
    private async Task RunArtworkRegressionAsync()
    {
        if (visualFixtureTimer is null || fixtureDirectory is null || Program.Profile?.Manifest.ProfileKind != NativeProfileKind.Test)
            throw new InvalidOperationException("Isolated visual Test required.");
        var previous = PageContent.Content;
        var content = new StackPanel { Spacing = 18, Padding = new Thickness(20) };
        PageContent.Content = new ScrollViewer { Content = content };
        var work = ArtworkRegressionWork();
        var facts = new CatalogFactsView(); facts.SetWork(work, work.Title);
        content.Children.Add(facts);
        var samples = new List<object>(); int overlaps = 0;
        await Task.Delay(100); content.UpdateLayout();
        foreach (var compact in new[] { false, true })
        foreach (var dense in new[] { false, true })
        {
            facts.Compact = compact; facts.Dense = dense;
            // The final natural width is pixel-rounded by ancestors. Exercise that boundary,
            // as well as the full constrained width, rather than only integer-size panels.
            for (double width = 180; width <= 560; width += 13.25)
            {
                facts.Width = width; facts.HorizontalAlignment = HorizontalAlignment.Left;
                content.UpdateLayout(); await Task.Delay(1);
                var desired = facts.DesiredSize;
                foreach (var finalWidth in new[] { width, Math.Max(1, desired.Width - .2) })
                {
                    facts.Width = finalWidth; content.UpdateLayout(); await Task.Delay(1);
                    var leaves = ArtworkFactLeaves(facts).Where(t => t.ActualWidth > 0 && t.ActualHeight > 0).Select(t =>
                    {
                        var p = t.TransformToVisual(facts).TransformPoint(new Point());
                        return new { text = t.Text, x = p.X, y = p.Y, width = t.ActualWidth, height = t.ActualHeight };
                    }).ToArray();
                    int collisions = 0;
                    for (int a = 0; a < leaves.Length; a++) for (int b = a + 1; b < leaves.Length; b++)
                        if (Math.Min(leaves[a].x + leaves[a].width, leaves[b].x + leaves[b].width) - Math.Max(leaves[a].x, leaves[b].x) > .5 &&
                            Math.Min(leaves[a].y + leaves[a].height, leaves[b].y + leaves[b].height) - Math.Max(leaves[a].y, leaves[b].y) > .5) collisions++;
                    overlaps += collisions;
                    if (collisions > 0 && samples.Count < 12) samples.Add(new { compact, dense, width, finalWidth, desired, collisions, leaves });
                }
            }
        }
        content.Children.Remove(facts);
        // A text/link group can be arranged a fraction of a DIP narrower than its
        // natural measure at a rasterized ancestor. It must not introduce a new row
        // after its parent has already reserved a single row of height.
        var roundFlow = new TagFlowPanel { HorizontalSpacing = 6, VerticalSpacing = 2, UseLayoutRounding = false };
        roundFlow.Children.Add(new Border { Width = 84.375, Height = 18, UseLayoutRounding = false });
        roundFlow.Children.Add(new Border { Width = 66.25, Height = 18, UseLayoutRounding = false });
        var roundedSlot = new ArtworkRoundedSlot { Child = roundFlow }; content.Children.Add(roundedSlot);
        content.UpdateLayout(); await Task.Delay(10); content.UpdateLayout();
        var rounding = new { desiredHeight = roundFlow.DesiredSize.Height,
            secondY = roundFlow.Children[1].TransformToVisual(roundFlow).TransformPoint(new Point()).Y };
        bool roundingFits = rounding.secondY < .5;
        content.Children.Remove(roundedSlot);
        var frame = new Grid { Width = 880, HorizontalAlignment = HorizontalAlignment.Left };
        frame.RowDefinitions.Add(new() { Height = GridLength.Auto }); frame.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var pipeline = new ArtworkRegressionImages();
        var stage = new ArtworkStage { Images = pipeline, AdaptiveLandscape = true, NaturalLandscape = true, StageCornerRadius = 8, Variant = StageVariant.Unrefracted };
        var overlay = new Grid(); var title = new TextBlock { Text = work.Title, FontSize = 28, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
        facts = new CatalogFactsView(); facts.SetWork(work, work.Title);
        var text = new StackPanel { Spacing = 5 }; text.Children.Add(title); text.Children.Add(facts);
        text.Children.Add(new TextBlock { Text = "当前状态 · 未设置    未评分" });
        var plate = new Border { Child = text, MaxWidth = 620, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom };
        stage.Plate = plate; overlay.Children.Add(plate); frame.Children.Add(stage); frame.Children.Add(overlay); content.Children.Add(frame);
        BannerChoice? choice = null;
        void Layout()
        {
            if (frame.ActualWidth < 1) return;
            HeroTitleLayout.Fit(title, plate, frame.ActualWidth, false);
            BannerFrameLayout.Apply(frame, stage, overlay, plate, choice);
        }
        stage.SourceChanged += Layout; frame.SizeChanged += (_, _) => Layout();
        stage.SetPolicy(true, false, MaterialMode.Standard);
        choice = new("synthetic-scene", "", 1, "fixture") { Width = 1600, Height = 900 }; stage.CoverKey = choice.Key;
        await Task.Delay(180); Layout(); content.UpdateLayout();
        bool VisibleImage() => ArtworkImages(stage).Any(i => i.Source is not null && i.Visibility == Visibility.Visible);
        var normal = new { artworkVisible = VisibleImage(), state = stage.Evidence };
        stage.SetPolicy(false, false, MaterialMode.Standard); content.UpdateLayout(); await Task.Delay(80);
        var effectsOff = new { artworkVisible = VisibleImage(), state = stage.Evidence };
        stage.SetPolicy(true, false, MaterialMode.Opaque); content.UpdateLayout(); await Task.Delay(80);
        var opaque = new { artworkVisible = VisibleImage(), state = stage.Evidence };
        stage.SetPolicy(true, true, MaterialMode.Advanced); content.UpdateLayout(); await Task.Delay(80);
        var highContrast = new { artworkVisible = VisibleImage(), state = stage.Evidence };
        stage.SetPolicy(true, false, MaterialMode.Standard);
        choice = choice with { Key = "synthetic-missing" }; stage.CoverKey = choice.Key;
        await Task.Delay(150); Layout(); content.UpdateLayout();
        var missing = new { collapsed = stage.Visibility == Visibility.Collapsed && stage.Height == 0, stage.Height, state = stage.Evidence };
        stage.CoverKey = null; choice = choice with { Key = "synthetic-scene" }; stage.CoverKey = choice.Key;
        await Task.Delay(150); Layout(); content.UpdateLayout();
        var recovery = new { artworkVisible = VisibleImage(), state = stage.Evidence };
        var delayed = new ArtworkDelayedImages(pipeline);
        stage.Images = delayed; choice = choice with { Key = "synthetic-delayed" }; stage.CoverKey = choice.Key;
        choice = choice with { Key = "synthetic-scene" }; stage.CoverKey = choice.Key;
        delayed.Complete(); await Task.Delay(80); content.UpdateLayout();
        var afterLate = JsonSerializer.SerializeToElement(stage.Evidence);
        bool lateIgnored = afterLate.GetProperty("loadedCoverKey").GetString() == "synthetic-scene" && VisibleImage();
        stage.Images = new ArtworkUpgradeFailureImages(pipeline); stage.DecodeEdge = ArtworkSizing.MaximumEdge;
        stage.SetPolicy(true, false, MaterialMode.Opaque); await Task.Delay(80); Layout(); content.UpdateLayout();
        var upgrade = JsonSerializer.SerializeToElement(stage.Evidence);
        bool upgradeRetained = VisibleImage() && !upgrade.GetProperty("imageFailed").GetBoolean() && stage.Visibility == Visibility.Visible;
        stage.Images = pipeline; stage.DecodeEdge = 512; stage.SetPolicy(true, false, MaterialMode.Standard);
        var report = new { passed = overlaps == 0 && roundingFits && normal.artworkVisible && effectsOff.artworkVisible && opaque.artworkVisible && !highContrast.artworkVisible && missing.collapsed && recovery.artworkVisible && lateIgnored && upgradeRetained,
            overlaps, samples, rounding, roundingFits, normal, effectsOff, opaque, highContrast, missing, recovery, lateIgnored, upgradeRetained, afterLate, upgrade,
            synthetic = true, productionRead = false, networkUsed = false, previousPage = previous?.GetType().Name };
        await File.WriteAllTextAsync(Path.Combine(fixtureDirectory, "artwork-regression.json"), JsonSerializer.Serialize(report));
        // Retain this owned synthetic scene for optional window screenshots. Navigation replaces it.
    }

    private static IEnumerable<TextBlock> ArtworkFactLeaves(DependencyObject root)
    {
        if (root is TextBlock text) { yield return text; yield break; }
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in ArtworkFactLeaves(VisualTreeHelper.GetChild(root, i))) yield return child;
    }
    private static IEnumerable<Image> ArtworkImages(DependencyObject root)
    {
        if (root is Image image) yield return image;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in ArtworkImages(VisualTreeHelper.GetChild(root, i))) yield return child;
    }
    private sealed class ArtworkRegressionImages : IImagePipeline
    {
        private readonly RasterPixels scene = Scene();
        private int retryLoads;
        private int posterLoads;
        private static RasterPixels Scene()
        {
            const int width = 640, height = 360; var bytes = new byte[width * height * 4];
            for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
            { int p = (y * width + x) * 4; bytes[p] = (byte)(100 + x / 5); bytes[p + 1] = (byte)(60 + y / 3); bytes[p + 2] = (byte)(35 + x / 7); bytes[p + 3] = 255; }
            return new(width, height, bytes);
        }
        public Task<ImageResult> LoadAsync(string? key, int edge, CancellationToken cancellation) =>
            Task.FromResult(new ImageResult(key == "synthetic-scene" || key == "synthetic-retry" && ++retryLoads > 1 || key == "synthetic-poster-retry" && ++posterLoads > 1 ? scene : null, "synthetic-regression"));
        public ImageMetrics Metrics => new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private sealed class ArtworkRoundedSlot : Panel
    {
        internal UIElement Child { set => Children.Add(value); }
        protected override Size MeasureOverride(Size availableSize)
        {
            Children[0].Measure(new Size(500, double.PositiveInfinity)); return Children[0].DesiredSize;
        }
        protected override Size ArrangeOverride(Size finalSize)
        {
            var size = Children[0].DesiredSize;
            Children[0].Arrange(new Rect(0, 0, size.Width - .2, size.Height)); return finalSize;
        }
    }
    private sealed class ArtworkDelayedImages(IImagePipeline fallback) : IImagePipeline
    {
        private readonly TaskCompletionSource<ImageResult> pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<ImageResult> LoadAsync(string? key, int edge, CancellationToken cancellation) => key == "synthetic-delayed" ? pending.Task : fallback.LoadAsync(key, edge, cancellation);
        internal void Complete() => pending.SetResult(new(new RasterPixels(2, 2, new byte[16]), "deliberately-late"));
        public ImageMetrics Metrics => fallback.Metrics;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private sealed class ArtworkUpgradeFailureImages(IImagePipeline fallback) : IImagePipeline
    {
        public Task<ImageResult> LoadAsync(string? key, int edge, CancellationToken cancellation) =>
            edge >= ArtworkSizing.MaximumEdge ? Task.FromResult(new ImageResult(null, "unavailable")) : fallback.LoadAsync(key, edge, cancellation);
        public ImageMetrics Metrics => fallback.Metrics;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
