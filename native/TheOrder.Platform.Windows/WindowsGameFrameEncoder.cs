using System.Runtime.InteropServices.WindowsRuntime;
using System.Security.Cryptography;
using TheOrder.Application.Playground;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace TheOrder.Platform.Windows;

internal static class WindowsGameFrameEncoder
{
    public static async Task<PlaygroundAttachment> EncodeAsync(byte[] rgba, int width, int height, CancellationToken cancellation)
    {
        using var output = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.JpegEncoderId, output,
            new BitmapPropertySet { ["ImageQuality"] = new(0.92f, global::Windows.Foundation.PropertyType.Single) }).AsTask(cancellation).ConfigureAwait(false);
        encoder.IsThumbnailGenerated = false;
        encoder.SetPixelData(BitmapPixelFormat.Rgba8, BitmapAlphaMode.Ignore, (uint)width, (uint)height, 96, 96, rgba);
        await encoder.FlushAsync().AsTask(cancellation).ConfigureAwait(false);
        if (output.Size is < 1 or > 4 * 1024 * 1024) throw new InvalidDataException();
        output.Seek(0); byte[] bytes = new byte[(int)output.Size];
        try
        {
            using (var stream = output.AsStreamForRead()) await stream.ReadExactlyAsync(bytes, cancellation).ConfigureAwait(false);
            return new("当前游戏画面.jpg", "") { IsGameFrame = true, MediaType = "image/jpeg", ImageBase64 = Convert.ToBase64String(bytes), ImageWidth = width, ImageHeight = height };
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
}
