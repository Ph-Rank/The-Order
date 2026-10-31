using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Security.Cryptography;

namespace TheOrder.Application.Playground;

// Pure validation of user-selected snapshots. No path is opened or image decoded by a native codec.
public static class PlaygroundAttachmentRules
{
    public const int MaximumImageBytes = 4 * 1024 * 1024;
    public const int MaximumTotalImageBytes = 8 * 1024 * 1024;
    public static void Validate(ImmutableArray<PlaygroundAttachment> attachments)
    {
        if (attachments.IsDefault || attachments.Length > 4) throw Invalid();
        long textCharacters = 0, imageBytes = 0;
        foreach (var attachment in attachments)
        {
            if (attachment is null || string.IsNullOrWhiteSpace(attachment.Name) || attachment.Name.Length > 120 || attachment.Name is "." or ".."
                || attachment.Name.Any(c => char.IsControl(c) || c is '/' or '\\' or ':') || attachment.Text is null) throw Invalid();
            bool imageFields = attachment.MediaType is not null || attachment.ImageBase64 is not null || attachment.ImageWidth != 0 || attachment.ImageHeight != 0;
            if (!imageFields)
            {
                if (string.IsNullOrWhiteSpace(attachment.Text) || attachment.Text.Length > 12000 || (textCharacters += attachment.Text.Length) > 24000) throw Invalid();
                continue;
            }
            if (attachment.Text.Length != 0 || attachment.MediaType is not ("image/png" or "image/jpeg") || string.IsNullOrEmpty(attachment.ImageBase64)
                || attachment.ImageWidth is < 1 or > 8192 || attachment.ImageHeight is < 1 or > 8192
                || (long)attachment.ImageWidth * attachment.ImageHeight > 20_000_000) throw Invalid();
            var encoded = attachment.ImageBase64;
            if (encoded.Length % 4 != 0 || encoded.Length > 4 * ((MaximumImageBytes + 2) / 3)
                || encoded.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('+' or '/' or '='))) throw Invalid();
            int length = encoded.Length / 4 * 3 - (encoded.EndsWith("==", StringComparison.Ordinal) ? 2 : encoded.EndsWith('=') ? 1 : 0);
            if (length is < 1 or > MaximumImageBytes || (imageBytes += length) > MaximumTotalImageBytes) throw Invalid();
            var bytes = new byte[length];
            try
            {
                if (!Convert.TryFromBase64String(encoded, bytes, out int written) || written != length) throw Invalid();
                var (width, height) = attachment.MediaType == "image/png" ? PngDimensions(bytes) : JpegDimensions(bytes);
                if (width != attachment.ImageWidth || height != attachment.ImageHeight) throw Invalid();
            }
            finally { CryptographicOperations.ZeroMemory(bytes); }
        }
    }
    private static (int Width, int Height) PngDimensions(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 45 || !bytes[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })
            || BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(8, 4)) != 13 || !bytes.Slice(12, 4).SequenceEqual("IHDR"u8)
            || !bytes.Slice(bytes.Length - 8, 4).SequenceEqual("IEND"u8)) throw Invalid();
        uint width = BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(16, 4)), height = BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(20, 4));
        if (width is 0 or > 8192 || height is 0 or > 8192 || (long)width * height > 20_000_000
            || bytes[26] != 0 || bytes[27] != 0 || bytes[28] > 1) throw Invalid();
        return ((int)width, (int)height);
    }
    private static (int Width, int Height) JpegDimensions(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 12 || bytes[0] != 0xff || bytes[1] != 0xd8 || bytes[^2] != 0xff || bytes[^1] != 0xd9) throw Invalid();
        int position = 2;
        while (position < bytes.Length - 2)
        {
            if (bytes[position++] != 0xff) throw Invalid();
            while (position < bytes.Length && bytes[position] == 0xff) position++;
            if (position >= bytes.Length) throw Invalid(); byte marker = bytes[position++];
            if (marker is 0x00 or 0xda or 0xd9 or 0xd8) throw Invalid();
            if (marker == 0x01 || marker is >= 0xd0 and <= 0xd7) continue;
            if (position + 2 > bytes.Length) throw Invalid();
            int length = BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(position, 2));
            if (length < 2 || position + length > bytes.Length) throw Invalid();
            if (marker is 0xc0 or 0xc1 or 0xc2 or 0xc3 or 0xc5 or 0xc6 or 0xc7 or 0xc9 or 0xca or 0xcb or 0xcd or 0xce or 0xcf)
            {
                if (length < 8) throw Invalid();
                int height = BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(position + 3, 2));
                int width = BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(position + 5, 2));
                if (width is < 1 or > 8192 || height is < 1 or > 8192 || (long)width * height > 20_000_000) throw Invalid();
                return (width, height);
            }
            position += length;
        }
        throw Invalid();
    }
    private static ArgumentException Invalid() => new("附件需为有效文本或PNG/JPEG图片快照：最多4份；文本每份12000、合计24000字符；图片每张4MiB、合计8MiB，尺寸与编码必须有效。文件名不能包含路径或控制字符，超限不会截断。");
}
