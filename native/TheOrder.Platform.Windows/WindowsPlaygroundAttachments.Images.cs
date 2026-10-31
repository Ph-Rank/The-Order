using System.Buffers.Binary;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Security.Cryptography;
using global::Windows.Foundation;
using global::Windows.Graphics.Imaging;
using global::Windows.Storage.Streams;
using TheOrder.Application.Playground;

namespace TheOrder.Platform.Windows;

public sealed partial class WindowsPlaygroundAttachments
{
    private static async Task<(PlaygroundAttachment Attachment, int Bytes)> ReadImageAsync(string name, byte[] bytes, CancellationToken cancellation)
    {
        string format = ImageFormat(bytes);
        RejectAnimation(bytes, format);
        using var source = new InMemoryRandomAccessStream();
        await source.WriteAsync(bytes.AsBuffer()).AsTask(cancellation).ConfigureAwait(false); source.Seek(0);
        var decoder = await BitmapDecoder.CreateAsync(source).AsTask(cancellation).ConfigureAwait(false);
        if (decoder.PixelWidth < 1 || decoder.PixelHeight < 1 || (ulong)decoder.PixelWidth * decoder.PixelHeight > 20_000_000 || decoder.FrameCount != 1)
            Reject("仅支持 2000 万像素以内的静态图片；不读取动画或空图片。");
        double scale = Math.Min(1, Math.Min(4096d / Math.Max(decoder.PixelWidth, decoder.PixelHeight), Math.Sqrt(6_000_000d / ((double)decoder.PixelWidth * decoder.PixelHeight))));
        var transform = new BitmapTransform
        {
            ScaledWidth = Math.Max(1, (uint)Math.Floor(decoder.PixelWidth * scale)),
            ScaledHeight = Math.Max(1, (uint)Math.Floor(decoder.PixelHeight * scale)),
            InterpolationMode = BitmapInterpolationMode.Fant
        };
        using var bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, transform,
            ExifOrientationMode.RespectExifOrientation, ColorManagementMode.ColorManageToSRgb).AsTask(cancellation).ConfigureAwait(false);
        if (bitmap.PixelWidth < 1 || bitmap.PixelHeight < 1 || Math.Max(bitmap.PixelWidth, bitmap.PixelHeight) > 4096
            || (long)bitmap.PixelWidth * bitmap.PixelHeight > 6_000_000) Reject("图片缩放后的尺寸超出安全范围。");
        cancellation.ThrowIfCancellationRequested();

        // Encode decoded pixels into a fresh image. No source EXIF, GPS, comments,
        // profiles, thumbnails or filesystem metadata are copied to this snapshot.
        bool jpeg = format == "jpeg" || format == "webp" && decoder.BitmapAlphaMode == BitmapAlphaMode.Ignore;
        using var output = new InMemoryRandomAccessStream();
        BitmapEncoder encoder;
        if (jpeg)
        {
            var options = new BitmapPropertySet { ["ImageQuality"] = new BitmapTypedValue(0.88f, PropertyType.Single) };
            encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.JpegEncoderId, output, options).AsTask(cancellation).ConfigureAwait(false);
        }
        else encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, output).AsTask(cancellation).ConfigureAwait(false);
        encoder.IsThumbnailGenerated = false;
        encoder.SetSoftwareBitmap(bitmap);
        await encoder.FlushAsync().AsTask(cancellation).ConfigureAwait(false);
        cancellation.ThrowIfCancellationRequested();
        if (output.Size is < 1 or > MaximumImageBytes) Reject("图片重新编码后超过 4 MiB；请使用较小的图片，不会发送未经处理的原文件。");
        output.Seek(0); byte[] encoded = new byte[checked((int)output.Size)];
        try
        {
            using (var stream = output.AsStreamForRead()) await stream.ReadExactlyAsync(encoded, cancellation).ConfigureAwait(false);
            cancellation.ThrowIfCancellationRequested();
            return (new PlaygroundAttachment(name, "") { MediaType = jpeg ? "image/jpeg" : "image/png", ImageBase64 = Convert.ToBase64String(encoded),
                ImageWidth = bitmap.PixelWidth, ImageHeight = bitmap.PixelHeight }, encoded.Length);
        }
        finally { CryptographicOperations.ZeroMemory(encoded); }
    }

    private static string ImageFormat(byte[] bytes)
    {
        if (bytes.AsSpan().StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) return "png";
        if (bytes.Length >= 3 && bytes[0] == 0xff && bytes[1] == 0xd8 && bytes[2] == 0xff) return "jpeg";
        if (bytes.Length >= 12 && bytes.AsSpan(0, 4).SequenceEqual("RIFF"u8) && bytes.AsSpan(8, 4).SequenceEqual("WEBP"u8)) return "webp";
        Reject("图片内容必须是静态 PNG、JPEG 或 WebP；不能通过重命名发送 SVG、动画或其他格式。");
        return "";
    }

    private static void RejectAnimation(byte[] bytes, string format)
    {
        // Some native PNG decoders expose APNG's first frame as a static image.
        // Inspect container animation markers as well as decoder.FrameCount.
        if (format == "png")
        {
            for (int offset = 8; offset <= bytes.Length - 12;)
            {
                uint length = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset, 4));
                if (length > bytes.Length - offset - 12) Reject("PNG 图片结构不完整。");
                var kind = bytes.AsSpan(offset + 4, 4);
                if (kind.SequenceEqual("acTL"u8) || kind.SequenceEqual("fcTL"u8) || kind.SequenceEqual("fdAT"u8)) Reject("不支持动画图片；请先保存一张静态图片。");
                offset = checked(offset + (int)length + 12);
            }
        }
        else if (format == "webp")
        {
            for (int offset = 12; offset <= bytes.Length - 8;)
            {
                uint length = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset + 4, 4));
                if (length > bytes.Length - offset - 8) Reject("WebP 图片结构不完整。");
                var kind = bytes.AsSpan(offset, 4);
                if (kind.SequenceEqual("ANIM"u8) || kind.SequenceEqual("ANMF"u8)
                    || kind.SequenceEqual("VP8X"u8) && length > 0 && (bytes[offset + 8] & 2) != 0)
                    Reject("不支持动画图片；请先保存一张静态图片。");
                offset = checked(offset + 8 + (int)length + ((int)length & 1));
            }
        }
    }
}
