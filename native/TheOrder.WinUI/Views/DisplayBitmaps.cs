using System.Runtime.CompilerServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml.Media.Imaging;
using TheOrder.Application;
using TheOrder.Presentation;

namespace TheOrder.WinUI.Views;

internal static class DisplayBitmaps
{
    private static readonly ConditionalWeakTable<IImagePipeline, DecodedSurfaceCache<WriteableBitmap>> pipelines = new();
    private static DecodedSurfaceCache<WriteableBitmap> For(IImagePipeline pipeline) => pipelines.GetValue(pipeline, _ => new(Create));
    internal static WriteableBitmap Create(RasterPixels pixels)
    {
        var bitmap = new WriteableBitmap(pixels.Width, pixels.Height);
        using (var buffer = bitmap.PixelBuffer.AsStream()) buffer.Write(pixels.Bgra);
        bitmap.Invalidate();
        return bitmap;
    }
    internal static WriteableBitmap Get(IImagePipeline pipeline, RasterPixels pixels)
    {
        return For(pipeline).Get(pixels);
    }
    internal static object Evidence(IImagePipeline pipeline)
    {
        var metrics = For(pipeline).Metrics;
        return new { requests = metrics.Requests, created = metrics.Created, copiedBytes = metrics.CopiedBytes,
            ownership = "Weak decoded-buffer identity; bounded by source pipeline and realized views" };
    }
}
