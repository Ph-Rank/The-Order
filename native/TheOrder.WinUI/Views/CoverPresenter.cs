using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using TheOrder.Application;
using TheOrder.Presentation;

namespace TheOrder.WinUI.Views;

public interface IImageHost { IImagePipeline Images { get; } }
public sealed class CoverPresenter : Grid
{
    private readonly Image image = new() { Stretch = Stretch.Fill };
    private readonly Canvas imageCanvas = new();
    private readonly FontIcon placeholder = new() { Glyph = "\uE8F1", FontSize = 32 };
    private readonly ImageRequestSlot request = new();
    private bool loaded;
    private string? requestedKey;
    private int requestedEdge;
    private RasterPixels? displayedPixels;
    private RasterPixels? croppedPixels;
    private double croppedAspect;
    private CoverViewport cachedCrop = new(0, 0, 1, 1);
    private bool preserveEntireImage;
    private CompositionRoundedRectangleGeometry? imageGeometry;
    private CompositionGeometricClip? imageClip;
    public IImagePipeline? Pipeline {get;set;}
    internal string SourceStatus {get;private set;}="not-started";
    internal bool HasImage=>image.Source is not null;
    internal event Action? SourceStateChanged;
    internal void RetryFailed()
    {
        if (HasImage || SourceKey is null || SourceStatus == "loading") return;
        requestedKey = null; requestedEdge = 0; Start();
    }
    internal Task PendingImage { get; private set; } = Task.CompletedTask;
    public static readonly DependencyProperty SourceKeyProperty = DependencyProperty.Register(nameof(SourceKey), typeof(string), typeof(CoverPresenter), new PropertyMetadata(null, OnSourceChanged));
    public string? SourceKey { get => (string?)GetValue(SourceKeyProperty); set => SetValue(SourceKeyProperty, value); }
    public static readonly DependencyProperty ImageCornerRadiusProperty = DependencyProperty.Register(nameof(ImageCornerRadius), typeof(double), typeof(CoverPresenter), new PropertyMetadata(8d, OnCornerRadiusChanged));
    public double ImageCornerRadius { get => (double)GetValue(ImageCornerRadiusProperty); set => SetValue(ImageCornerRadiusProperty, value); }
    public int MinimumEdge { get; set; } = 16;
    // Posters fill their frame using a detail-aware source window. Portraits,
    // Markdown images and the selection preview can explicitly retain all art.
    public bool PreserveEntireImage { get => preserveEntireImage; set { preserveEntireImage = value; ApplyImageClip(); } }
    public CoverPresenter()
    {
        imageCanvas.Children.Add(image); Children.Add(placeholder); Children.Add(imageCanvas);
        Loaded += (_, _) => { loaded = true; Start(); };
        Unloaded += (_, _) => { loaded = false; Reset(); ReleaseImageClip(); };
        SizeChanged += (_, _) => { if (loaded) { ApplyImageClip(); Start(); } };
    }
    private static void OnSourceChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var presenter = (CoverPresenter)sender;
        // Invalidate the old generation immediately, including while layout is zero-sized.
        if ((string?)args.OldValue != (string?)args.NewValue) presenter.Reset();
        presenter.Start();
    }
    internal bool PerformanceSourceCurrent => image.Source is null || requestedKey == SourceKey;
    private static void OnCornerRadiusChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) => ((CoverPresenter)sender).ApplyImageClip();
    private void ApplyImageClip()
    {
        if (!loaded || ActualWidth <= 0 || ActualHeight <= 0) return;
        var width = ActualWidth;
        var height = ActualHeight;
        var offset = Vector2.Zero;
        // Canvas keeps the enlarged image from changing the card's desired size.
        // All poster clipping uses the fixed viewport, including its lower corners.
        if (displayedPixels is { } pixels)
        {
            if (PreserveEntireImage)
            {
                var scale = Math.Min(width / pixels.Width, height / pixels.Height);
                image.Width = pixels.Width * scale; image.Height = pixels.Height * scale;
                offset = new Vector2((float)((width - image.Width) / 2), (float)((height - image.Height) / 2));
                Canvas.SetLeft(image, offset.X); Canvas.SetTop(image, offset.Y);
                width = image.Width; height = image.Height;
            }
            else
            {
                var aspect = width / height;
                if (!ReferenceEquals(pixels, croppedPixels) || Math.Abs(aspect - croppedAspect) > .001)
                { cachedCrop = CoverCropping.Plan(pixels, aspect); croppedPixels = pixels; croppedAspect = aspect; }
                var crop = cachedCrop;
                var scale = Math.Max(width / pixels.Width, height / pixels.Height);
                image.Width = pixels.Width * scale; image.Height = pixels.Height * scale;
                Canvas.SetLeft(image, -crop.X * scale); Canvas.SetTop(image, -crop.Y * scale);
            }
        }
        if (!double.IsFinite(ImageCornerRadius) || ImageCornerRadius <= 0) { ReleaseImageClip(); return; }
        var visual = ElementCompositionPreview.GetElementVisual(this);
        imageGeometry ??= visual.Compositor.CreateRoundedRectangleGeometry();
        imageClip ??= visual.Compositor.CreateGeometricClip(imageGeometry);
        imageGeometry.Offset = offset;
        imageGeometry.Size = new Vector2((float)width, (float)height);
        imageGeometry.CornerRadius = new Vector2((float)Math.Min(ImageCornerRadius, Math.Min(width, height) / 2));
        visual.Clip = imageClip;
    }
    private void ReleaseImageClip()
    {
        if (imageClip is null) return;
        ElementCompositionPreview.GetElementVisual(this).Clip = null;
        imageClip.Dispose(); imageClip = null;
        imageGeometry?.Dispose(); imageGeometry = null;
    }
    private void Reset() { requestedKey = null; requestedEdge = 0; request.Clear(); image.Source = null; displayedPixels = null; croppedPixels = null; ApplyImageClip(); placeholder.Visibility = Visibility.Visible; SourceStatus=SourceKey is null?"no-source":"not-started"; ToolTipService.SetToolTip(this, SourceKey is null ? "来源尚未提供封面" : "正在准备封面"); SourceStateChanged?.Invoke(); }
    private void Start()
    {
        if (!loaded || SourceKey is null) { Reset(); return; }
        if (ActualWidth <= 0 || ActualHeight <= 0) return;
        DependencyObject? ancestor = this;
        while (ancestor is not null && ancestor is not IImageHost) ancestor = VisualTreeHelper.GetParent(ancestor);
        var pipeline=Pipeline??(ancestor as IImageHost)?.Images;
        if (pipeline is null) {SourceStatus="missing-pipeline";return;}
        var key = SourceKey;
        var edge = ThumbnailSizing.Edge(ActualWidth, ActualHeight, XamlRoot?.RasterizationScale ?? 1, MinimumEdge);
        if (requestedKey == key && requestedEdge == edge) return;
        if (requestedKey == key && displayedPixels is not null) request.Clear();
        else Reset();
        requestedKey = key; requestedEdge = edge;
        if(pipeline.TryGet(key,edge,out var pixels)&&pixels is not null)
        {displayedPixels=pixels;image.Source=DisplayBitmaps.Get(pipeline,pixels);ApplyImageClip();placeholder.Visibility=Visibility.Collapsed;SourceStatus="memory"; ToolTipService.SetToolTip(this, null); SourceStateChanged?.Invoke(); return;}
        var lease = request.Begin(key);
        SourceStatus="loading";
        SourceStateChanged?.Invoke();
        PendingImage = LoadAsync(pipeline, key, edge, lease.Generation, lease.Cancellation);
    }
    private async Task LoadAsync(IImagePipeline pipeline, string key, int edge, long version, CancellationToken cancellation)
    {
        try
        {
            var result = await pipeline.LoadAsync(key, edge, cancellation);
            if(request.IsCurrent(key,version))SourceStatus=result.Status;
            if (!loaded || !request.IsCurrent(key, version) || cancellation.IsCancellationRequested || SourceKey != key || result.Pixels is not { } pixels) return;
            displayedPixels = pixels; image.Source = DisplayBitmaps.Get(pipeline, pixels); ApplyImageClip(); placeholder.Visibility = Visibility.Collapsed;
        }
        catch (Exception e) { if (request.IsCurrent(key, version)) { SourceStatus=e.GetType().Name; if (displayedPixels is null) placeholder.Visibility = Visibility.Visible; } }
        finally
        {
            if (request.IsCurrent(key, version))
            {
                ToolTipService.SetToolTip(this, HasImage ? null : "封面暂时未能加载，可重试；其他资料保留。");
                SourceStateChanged?.Invoke();
            }
        }
    }
}
