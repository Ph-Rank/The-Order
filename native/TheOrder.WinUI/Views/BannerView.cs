using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TheOrder.Application;
using TheOrder.Domain;

namespace TheOrder.WinUI.Views;

// Catalog works use the same actual CG foreground layer as Library details, never a title above the image.
public sealed class BannerView : UserControl
{
    private readonly IImagePipeline images;
    private readonly IRemoteCoverRegistry registry;
    private readonly ArtworkStage stage;
    private readonly BannerPresentation presentation;
    private readonly Grid layout = new();
    private readonly Grid frame = new() { Opacity = 0, IsHitTestVisible = false };
    private readonly Border plate = new() { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom };
    private readonly TextBlock title = new() { TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis,
        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, MaxLines = 1 };
    private readonly TextBlock previewTitle = new() { TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Bottom };
    private readonly CatalogFactsView facts = new();
    public BannerChoice? Choice { get; private set; }
    public Func<CatalogEntityRef, Task>? Browse { get => facts.Browse; set => facts.Browse = value; }
    public BannerView(IImagePipeline images, IRemoteCoverRegistry registry)
    {
        this.images = images; this.registry = registry;
        stage = new() { Images = images, Plate = plate, AdaptiveLandscape = true, NaturalLandscape = true, AmbientSigma = 0,
            DecodeEdge = 1200, StageCornerRadius = 8, Variant = StageVariant.Unrefracted };
        previewTitle.Style = (Style)Microsoft.UI.Xaml.Application.Current.Resources["WorkTitleStyle"];
        var preview = new Border { Style = (Style)Microsoft.UI.Xaml.Application.Current.Resources["SurfaceStyle"],
            Child = previewTitle, Padding = new Thickness(24, 18, 24, 18), IsHitTestVisible = false };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAccessibilityView(preview, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAccessibilityView(previewTitle, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
        presentation = new(frame, stage, preview);
        var text = new StackPanel { Spacing = 6 }; text.Children.Add(title); text.Children.Add(facts); plate.Child = text;
        frame.RowDefinitions.Add(new() { Height = GridLength.Auto }); frame.RowDefinitions.Add(new() { Height = GridLength.Auto });
        frame.Children.Add(stage); layout.Children.Add(plate); Canvas.SetZIndex(layout, 10); frame.Children.Add(layout);
        Content = new Grid { Children = { preview, frame } };
        stage.SourceChanged += () => UpdateFrame(ActualWidth);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(this, "CatalogHero");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetHeadingLevel(title, Microsoft.UI.Xaml.Automation.Peers.AutomationHeadingLevel.Level1);
        stage.ForegroundChanged += brush => {
            facts.SetTextForeground(brush);
            if (brush is null) title.ClearValue(TextBlock.ForegroundProperty); else title.Foreground = brush;
        };
        Loaded += (_, _) => UpdateFrame(ActualWidth);
        Unloaded += (_, _) => { if (!IsLoaded) ReleaseBeforeLeave(); };
        SizeChanged += (_, e) => UpdateFrame(e.NewSize.Width);
    }
    public void SetContent(WorkMetadata work) { Choice = null; stage.CoverKey = null; previewTitle.Text = title.Text = CatalogLanguage.Title(work); facts.SetWork(work, title.Text); UpdateFrame(ActualWidth); }
    public void SetPolicy(bool allowed, bool highContrast, MaterialMode mode) => stage.SetPolicy(allowed, highContrast, mode);
    public void ReleaseBeforeLeave() { stage.ReleaseBeforeLeave(); }
    public void SetPresentation(CatalogPresentationEntry cached)
    {
        presentation.SetPending(true);
        SetContent(cached.Work);
        Choice = cached.Banner; stage.CoverKey = Choice?.Key; UpdateFrame(ActualWidth);
        presentation.SetPending(false);
    }
    private void UpdateFrame(double width)
    {
        if (width < 1) return;
        var artwork = BannerFrameLayout.ArtworkFrame(frame, stage, Choice);
        var compact = artwork.Width < 560; var dense = artwork.Height is > 0 and < 340;
        HeroTitleLayout.Fit(title, plate, artwork.Width, compact, dense); facts.Compact = compact; facts.Dense = dense;
        BannerFrameLayout.Apply(frame, stage, layout, plate, Choice);
    }
    internal object Evidence => new { width = ActualWidth, height = ActualHeight, frameOpacity = frame.Opacity, presentationPending = presentation.Pending, revealQueued = presentation.Queued, aspect = ActualHeight > 0 ? ActualWidth / ActualHeight : 0,
        textScreened = Choice?.TextScreened, artworkSourceId = Choice?.SourceWorkId, artworkReference = Choice?.ReferenceTitle,
        title = title.Text, titleHeight = title.ActualHeight, titleWidth = title.ActualWidth, titleFontSize = title.FontSize, titleMaxLines = title.MaxLines, titleWrapping = title.TextWrapping.ToString(), plateWidth = plate.ActualWidth, zIndex = Canvas.GetZIndex(layout), facts = facts.Evidence, stage = stage.Evidence };
}
