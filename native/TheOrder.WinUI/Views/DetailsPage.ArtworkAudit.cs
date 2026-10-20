using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using TheOrder.Application;
using TheOrder.Domain;
using Windows.Foundation;

namespace TheOrder.WinUI.Views;

public sealed partial class DetailsPage
{
    // The window's isolated Test controller supplies only synthetic artwork/metadata.
    // Exercise the real XAML and retry handler without opening or editing a library item.
    internal async Task<object> AuditArtworkLayoutAsync(WorkMetadata work, IImagePipeline pipeline)
    {
        if (Program.Profile?.Manifest.ProfileKind != NativeProfileKind.Test || Environment.GetEnvironmentVariable("THE_ORDER_NATIVE_VISUAL_TEST") != "1")
            throw new InvalidOperationException("Visual Test required.");
        HeroStage.Images = pipeline; HeroFallbackCover.Pipeline = pipeline;
        HeroTitle.Text = work.Title; HeroFacts.SetWork(work, work.Title);
        HeroStatusText.Text = "当前状态 · 未设置"; HeroRatingText.Text = "未评分";
        HeroPlayText.Text = "累计 0 小时 0 分钟 · 1 次 · 1 条历史记录"; LastPlayedText.Text = "最近游玩 · 今天 15:33";
        HeroFallbackCover.SourceKey = "synthetic-scene";
        HeroStage.SetPolicy(false, false, MaterialMode.Standard);
        heroPresentation.SetPending(false);
        var choice = new BannerChoice("synthetic-scene", "", 1, "fixture", work.Id) { Width = 1600, Height = 900 };
        SetHeroArtwork(choice); await Settle();
        var effectsOff = Snapshot();
        SetHeroArtwork(choice with { Key = "synthetic-retry" }); await Settle();
        var missing = Snapshot();
        OnRetryHeroArtwork(this, new RoutedEventArgs()); await Settle();
        var retry = Snapshot();
        SetHeroArtwork(null); await Settle();
        var noBanner = Snapshot();
        HeroFallbackCover.SourceKey = "synthetic-poster-retry"; await Settle();
        var failedPoster = Snapshot();
        OnRetryHeroArtwork(this, new RoutedEventArgs()); await Settle();
        var posterRetry = Snapshot();
        HeroFallbackCover.SourceKey = "synthetic-scene";
        SetHeroArtwork(choice); await Settle();
        return new { effectsOff, missing, retry, noBanner, failedPoster, posterRetry, final = StageEvidence };

        async Task Settle() { DetailsRoot.UpdateLayout(); await Task.Delay(120); UpdateHeroFrame(HeroFrame.ActualWidth); DetailsRoot.UpdateLayout(); await Task.Delay(20); }
        object Snapshot()
        {
            var texts = Leaves(HeroPlate).Where(t => t.ActualWidth > 0 && t.ActualHeight > 0 && VisibleToPlate(t)).Select(t =>
            {
                var p = t.TransformToVisual(HeroPlate).TransformPoint(new Point());
                return new { text = t.Text, x = p.X, y = p.Y, width = t.ActualWidth, height = t.ActualHeight };
            }).ToArray();
            int overlaps = 0;
            for (int a = 0; a < texts.Length; a++) for (int b = a + 1; b < texts.Length; b++)
                if (Math.Min(texts[a].x + texts[a].width, texts[b].x + texts[b].width) - Math.Max(texts[a].x, texts[b].x) > .5 &&
                    Math.Min(texts[a].y + texts[a].height, texts[b].y + texts[b].height) - Math.Max(texts[a].y, texts[b].y) > .5) overlaps++;
            bool inside = texts.All(t => t.x >= -.5 && t.y >= -.5 && t.x + t.width <= HeroPlate.ActualWidth + .5 && t.y + t.height <= HeroPlate.ActualHeight + .5);
            return new { overlaps, inside, texts, state = StageEvidence, failureVisible = HeroArtworkFailure.Visibility == Visibility.Visible };
        }
        bool VisibleToPlate(DependencyObject? element)
        {
            while (element is not null && !ReferenceEquals(element, HeroPlate))
            {
                if (element is UIElement { Visibility: Visibility.Collapsed }) return false;
                element = VisualTreeHelper.GetParent(element);
            }
            return element is not null;
        }
    }
    private static IEnumerable<TextBlock> Leaves(DependencyObject node)
    {
        if (node is TextBlock text) { yield return text; yield break; }
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
            foreach (var child in Leaves(VisualTreeHelper.GetChild(node, i))) yield return child;
    }
}
