using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace TheOrder.WinUI.Views;

// Keep a readable preview in the measured space until the artwork, glass and
// foreground can be published together. No timer or idle rendering subscription.
internal sealed class BannerPresentation
{
    private readonly Grid frame;
    private readonly ArtworkStage stage;
    private readonly FrameworkElement preview;
    private bool pending, queued, shown;
    internal bool Pending => pending;
    internal bool Queued => queued;
    internal BannerPresentation(Grid frame, ArtworkStage stage, FrameworkElement preview)
    {
        this.frame = frame; this.stage = stage; this.preview = preview;
        Hide();
        frame.Loaded += (_, _) => TryReveal();
        frame.Unloaded += (_, _) => { if (!frame.IsLoaded) Hide(); };
        frame.LayoutUpdated += (_, _) => TryReveal();
        stage.PresentationChanged += () => { if (!stage.PresentationReady) Hide(); TryReveal(); };
    }
    internal void SetPending(bool value)
    {
        pending = value;
        if (pending) Hide(); else TryReveal();
    }
    private void Hide()
    {
        Unqueue();
        if (shown) InteractionMotion.StopReveal(frame);
        shown = false; frame.Opacity = 0; frame.IsHitTestVisible = false;
        preview.Visibility = Visibility.Visible;
    }
    private bool Ready => !pending && frame.IsLoaded && (stage.CoverKey is null || stage.HasCurrentPresentation);
    private void TryReveal()
    {
        if (shown || queued || !Ready) return;
        CompositionTarget.Rendering += Reveal; queued = true;
    }
    private void Reveal(object? sender, object args)
    {
        Unqueue();
        // Rendering follows layout. Recheck the current image and plate, rather
        // than revealing a draw for stale dimensions or a cancelled selection.
        if (!Ready) return;
        shown = true; frame.Opacity = 1; frame.IsHitTestVisible = true;
        preview.Visibility = Visibility.Collapsed;
        if (!stage.IsShowingCachedFrame) InteractionMotion.RevealContent(frame, .72f);
    }
    private void Unqueue()
    {
        if (!queued) return;
        CompositionTarget.Rendering -= Reveal; queued = false;
    }
}
