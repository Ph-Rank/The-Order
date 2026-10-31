using System.Collections.Immutable;
using System.Buffers.Binary;
using System.IO.Compression;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TheOrder.Application;
using TheOrder.Application.Playground;
using TheOrder.Infrastructure;
using TheOrder.Platform.Windows;
using Xunit;

namespace TheOrder.Tests;

// Real local reads and real existing parsers, using only these synthetic files.
// The parent runner places TEMP on the isolated D: output volume.
public sealed class PlaygroundAttachmentTests
{
    [Theory]
    [InlineData("utf8", ".txt")]
    [InlineData("utf16", ".md")]
    [InlineData("gb18030", ".txt")]
    public async Task SelectedTextPreservesUnicodeAndSourceAndReturnsOnlyBasename(string encodingName, string extension)
    {
        using var files = new Files();
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Encoding encoding = encodingName == "utf16" ? Encoding.Unicode : encodingName == "gb18030" ? Encoding.GetEncoding(54936) : Encoding.UTF8;
        const string text = "攻略 · 宁宁\r\n次の選択肢\n存档 01";
        string path = files.Write("资料" + extension, [.. encoding.GetPreamble(), .. encoding.GetBytes(text)]);
        byte[] before = SHA256.HashData(await File.ReadAllBytesAsync(path));

        var attachments = await Reader().ReadAsync([path]);

        var attachment = Assert.Single(attachments);
        Assert.Equal("资料" + extension, attachment.Name);
        Assert.Contains("攻略 · 宁宁", attachment.Text);
        Assert.Contains("次の選択肢\n存档 01", attachment.Text);
        Assert.DoesNotContain('\r', attachment.Text);
        Assert.DoesNotContain(files.Root, JsonSerializer.Serialize(attachments), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(before, SHA256.HashData(await File.ReadAllBytesAsync(path)));
    }

    [Fact]
    public async Task WordIncludesHeadingAndTableCellsButNeverDecodesImagesOrFollowsRelationships()
    {
        using var files = new Files();
        string path = files.Write("攻略.docx", Word("""
            <w:p><w:pPr><w:pStyle w:val="Heading1"/></w:pPr><w:r><w:t>共通路线</w:t></w:r></w:p>
            <w:p><w:r><w:t>先存档 01</w:t></w:r><a:blip r:embed="picture"/><a:blip r:embed="external"/></w:p>
            <w:tbl><w:tr><w:tc><w:p><w:r><w:t>选项 A</w:t></w:r></w:p></w:tc><w:tc><w:p><w:r><w:t>宁宁</w:t></w:r></w:p></w:tc></w:tr>
            <w:tr><w:tc><w:p><w:r><w:t>选项 B</w:t></w:r></w:p></w:tc><w:tc><w:p><w:r><w:t>普通 END</w:t></w:r></w:p></w:tc></w:tr></w:tbl>
            """));

        var attachment = Assert.Single(await Reader().ReadAsync([path]));

        Assert.Contains("共通路线\n先存档 01", attachment.Text);
        Assert.Contains("选项 A\t宁宁\n选项 B\t普通 END", attachment.Text);
        Assert.DoesNotContain("文档插图", attachment.Text);
        Assert.DoesNotContain("untrusted.invalid", attachment.Text);
        Assert.DoesNotContain("第 1 页", attachment.Text);
    }

    [Fact]
    public async Task WorkbookFlattensActualRowsAndFinalColumnWithCachedValues()
    {
        using var files = new Files();
        string path = files.Write("路线.xlsx", LocalReadingTests.ExcelFile());
        var attachment = Assert.Single(await Reader().ReadAsync([path]));
        Assert.Contains("攻略表", attachment.Text);
        Assert.Contains("最后一列", attachment.Text);
        Assert.Contains("只读说明", attachment.Text);
        Assert.Contains("\t2", attachment.Text);
        Assert.DoesNotContain("1+1", attachment.Text);
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("whitespace")]
    [InlineData("empty-word")]
    [InlineData("image-word")]
    public async Task EmptyOrImageOnlyFilesAreExplicitlyRejected(string kind)
    {
        using var files = new Files();
        byte[] bytes = kind switch
        {
            "empty" => [],
            "whitespace" => Encoding.UTF8.GetBytes(" \r\n\t\n"),
            "empty-word" => Word(""),
            _ => Word("<w:p><a:blip r:embed='picture'/></w:p>")
        };
        string path = files.Write("empty" + (kind.EndsWith("word", StringComparison.Ordinal) ? ".docx" : ".txt"), bytes);
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => Reader().ReadAsync([path]));
        Assert.Contains("没有可提取", error.Message);
        Assert.DoesNotContain("此文档没有可读取的正文。", error.Message);
        Assert.DoesNotContain(path, error.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CountByteAndCharacterLimitsRejectWithoutTruncation()
    {
        using var files = new Files();
        string oversized = files.Write("oversized.txt", new byte[2 * 1024 * 1024 + 1]);
        var sizeError = await Assert.ThrowsAsync<InvalidDataException>(() => Reader().ReadAsync([oversized]));
        Assert.Contains("2 MiB", sizeError.Message);
        string longText = files.Write("long.txt", Encoding.UTF8.GetBytes(new string('a', 12_001)));
        var textError = await Assert.ThrowsAsync<InvalidDataException>(() => Reader().ReadAsync([longText]));
        Assert.Contains("12000", textError.Message);
        string[] paths = Enumerable.Range(0, 4).Select(i => files.Write($"part{i}.txt", Encoding.UTF8.GetBytes(new string('a', 6000)))).ToArray();
        var exact = await Reader().ReadAsync(paths);
        Assert.Equal(4, exact.Length);
        Assert.Equal(24_000, exact.Sum(attachment => attachment.Text.Length));
        string extra = files.Write("extra.txt", Encoding.UTF8.GetBytes("a"));
        var countError = await Assert.ThrowsAsync<InvalidDataException>(() => Reader().ReadAsync([.. paths, extra]));
        Assert.Contains("4 份", countError.Message);
        await File.WriteAllTextAsync(paths[3], new string('a', 6001));
        var totalError = await Assert.ThrowsAsync<InvalidDataException>(() => Reader().ReadAsync(paths));
        Assert.Contains("24000", totalError.Message);
        Assert.DoesNotContain(files.Root, totalError.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(".pdf")]
    [InlineData(".svg")]
    [InlineData(".exe")]
    public async Task UnsupportedFormatsAreRejectedBeforeParsing(string extension)
    {
        using var files = new Files();
        string path = files.Write("unsupported" + extension, [65]);
        var parser = new ProbeParser((_, _, _) => throw new InvalidOperationException("must not parse"));
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => Reader(parser).ReadAsync([path]));
        Assert.Contains("PDF、SVG", error.Message);
        Assert.Equal(0, parser.Calls);
    }

    [Fact]
    public async Task PathsDuplicatesAndOverlongNamesAreRejectedWithoutLeakingLocations()
    {
        using var files = new Files();
        string path = files.Write("guide.txt", [65]);
        string longName = files.Write(new string('a', 121) + ".txt", [65]);
        foreach (string[] paths in new[] { new[] { "relative.txt" }, new[] { @"\\untrusted.invalid\share\guide.txt" }, new[] { path, path }, new[] { longName } })
        {
            var error = await Assert.ThrowsAsync<InvalidDataException>(() => Reader().ReadAsync(paths));
            Assert.DoesNotContain(files.Root, error.ToString(), StringComparison.OrdinalIgnoreCase);
            Assert.Null(error.InnerException);
        }
    }

    [Fact]
    public async Task CancelledReadsStayCancelledIncludingCancellationAfterParsing()
    {
        using var files = new Files();
        string path = files.Write("guide.txt", [65]);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Reader().ReadAsync([path], new CancellationToken(true)));
        using var cancellation = new CancellationTokenSource();
        var parser = new ProbeParser((_, _, _) => { cancellation.Cancel(); return [new("page", [new("paragraph", "must not return")])]; });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Reader(parser).ReadAsync([path], cancellation.Token));
        Assert.Equal(1, parser.Calls);
    }

    [Fact]
    public async Task ParserErrorsAndChangedIdentityRejectWithoutPathOrInnerException()
    {
        using var files = new Files();
        string path = files.Write("guide.txt", [65]);
        var parser = new ProbeParser((_, _, _) => throw new IOException("private source: " + path));
        var parseError = await Assert.ThrowsAsync<InvalidDataException>(() => Reader(parser).ReadAsync([path]));
        Assert.DoesNotContain(path, parseError.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Null(parseError.InnerException);
        var identity = new ChangedIdentity();
        var staleError = await Assert.ThrowsAsync<InvalidDataException>(() => new WindowsPlaygroundAttachments(identity, new LocalDocumentParser()).ReadAsync([path]));
        Assert.Equal(2, identity.Calls);
        Assert.DoesNotContain(path, staleError.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Null(staleError.InnerException);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeImagesAreActualReencodedSnapshotsWithoutMetadataOrOcr(bool jpeg)
    {
        using var files = new Files();
        byte[] original = await NativeImageAsync(jpeg, 32, 16);
        const string marker = "synthetic-private-comment-never-upload";
        if (jpeg)
        {
            byte[] comment = Encoding.ASCII.GetBytes(marker), segment = new byte[comment.Length + 4];
            segment[0] = 0xff; segment[1] = 0xfe; BinaryPrimitives.WriteUInt16BigEndian(segment.AsSpan(2), checked((ushort)(comment.Length + 2))); comment.CopyTo(segment, 4);
            original = [.. original.Take(2), .. segment, .. original.Skip(2)];
        }
        string path = files.Write(jpeg ? "photo.jpg" : "transparent.png", original);
        var parser = new ProbeParser((_, _, _) => throw new InvalidOperationException("images must not be parsed as text"));

        var attachments = await Reader(parser).ReadAsync([path]);

        PlaygroundAttachmentRules.Validate(attachments);
        var image = Assert.Single(attachments);
        Assert.Equal("", image.Text); Assert.Equal(32, image.ImageWidth); Assert.Equal(16, image.ImageHeight);
        Assert.Equal(jpeg ? "image/jpeg" : "image/png", image.MediaType); Assert.NotNull(image.ImageBase64);
        byte[] encoded = Convert.FromBase64String(image.ImageBase64!);
        Assert.DoesNotContain(marker, Encoding.ASCII.GetString(encoded));
        using var memory = new global::Windows.Storage.Streams.InMemoryRandomAccessStream();
        await memory.WriteAsync(encoded.AsBuffer()); memory.Seek(0);
        var decoded = await global::Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(memory);
        Assert.Equal((uint)32, decoded.PixelWidth); Assert.Equal((uint)16, decoded.PixelHeight);
        Assert.Equal(0, parser.Calls);
        Assert.Equal(SHA256.HashData(original), SHA256.HashData(await File.ReadAllBytesAsync(path)));
    }

    [Fact]
    public async Task LargeEdgeIsResizedAndOversizedOrAnimatedImagesAreRejected()
    {
        using var files = new Files();
        string wide = files.Write("wide.png", await NativeImageAsync(false, 5000, 8));
        var resized = Assert.Single(await Reader().ReadAsync([wide]));
        Assert.Equal(4096, resized.ImageWidth); Assert.InRange(resized.ImageHeight, 1, 8);
        Assert.True((long)resized.ImageWidth * resized.ImageHeight <= 6_000_000);
        PlaygroundAttachmentRules.Validate([resized]);

        string huge = files.Write("too-large.png", new byte[8 * 1024 * 1024 + 1]);
        Assert.Contains("8 MiB", (await Assert.ThrowsAsync<InvalidDataException>(() => Reader().ReadAsync([huge]))).Message);
        byte[] pixelBomb = await NativeImageAsync(false, 8, 8);
        BinaryPrimitives.WriteInt32BigEndian(pixelBomb.AsSpan(16), 5000); BinaryPrimitives.WriteInt32BigEndian(pixelBomb.AsSpan(20), 5000);
        BinaryPrimitives.WriteUInt32BigEndian(pixelBomb.AsSpan(29), Crc(pixelBomb.AsSpan(12, 17)));
        string dimensions = files.Write("too-many-pixels.png", pixelBomb);
        await Assert.ThrowsAsync<InvalidDataException>(() => Reader().ReadAsync([dimensions]));

        byte[] png = await NativeImageAsync(false, 8, 8), animation = new byte[20];
        BinaryPrimitives.WriteInt32BigEndian(animation, 8); "acTL"u8.CopyTo(animation.AsSpan(4));
        BinaryPrimitives.WriteInt32BigEndian(animation.AsSpan(8), 2); BinaryPrimitives.WriteUInt32BigEndian(animation.AsSpan(16), Crc(animation.AsSpan(4, 12)));
        string animated = files.Write("animated.png", [.. png.Take(33), .. animation, .. png.Skip(33)]);
        Assert.Contains("动画", (await Assert.ThrowsAsync<InvalidDataException>(() => Reader().ReadAsync([animated]))).Message);
        string disguised = files.Write("not-a-picture.png", Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg'/>"));
        Assert.Contains("不能通过重命名", (await Assert.ThrowsAsync<InvalidDataException>(() => Reader().ReadAsync([disguised]))).Message);
    }

    private static async Task<byte[]> NativeImageAsync(bool jpeg, uint width, uint height)
    {
        using var output = new global::Windows.Storage.Streams.InMemoryRandomAccessStream();
        var encoder = await global::Windows.Graphics.Imaging.BitmapEncoder.CreateAsync(jpeg ? global::Windows.Graphics.Imaging.BitmapEncoder.JpegEncoderId : global::Windows.Graphics.Imaging.BitmapEncoder.PngEncoderId, output);
        byte[] rgba = new byte[checked((int)(width * height * 4))];
        for (int offset = 0; offset < rgba.Length; offset += 4) { rgba[offset] = 40; rgba[offset + 1] = 90; rgba[offset + 2] = 120; rgba[offset + 3] = jpeg ? (byte)255 : (byte)192; }
        encoder.SetPixelData(global::Windows.Graphics.Imaging.BitmapPixelFormat.Rgba8, jpeg ? global::Windows.Graphics.Imaging.BitmapAlphaMode.Ignore : global::Windows.Graphics.Imaging.BitmapAlphaMode.Straight, width, height, 96, 96, rgba);
        await encoder.FlushAsync(); output.Seek(0);
        byte[] bytes = new byte[checked((int)output.Size)];
        using var stream = output.AsStreamForRead(); await stream.ReadExactlyAsync(bytes); return bytes;
    }

    private static uint Crc(ReadOnlySpan<byte> bytes)
    {
        uint crc = uint.MaxValue;
        foreach (byte value in bytes) { crc ^= value; for (int bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0 : 0xedb88320u); }
        return ~crc;
    }

    private static WindowsPlaygroundAttachments Reader(ILocalDocumentParser? parser = null) => new(new WindowsFilesystemIdentity(), parser ?? new LocalDocumentParser());

    private static byte[] Word(string body)
    {
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true))
        {
            using (var writer = new StreamWriter(zip.CreateEntry("word/document.xml").Open(), Encoding.UTF8))
                writer.Write("<w:document xmlns:w='http://schemas.openxmlformats.org/wordprocessingml/2006/main' xmlns:a='http://schemas.openxmlformats.org/drawingml/2006/main' xmlns:r='http://schemas.openxmlformats.org/officeDocument/2006/relationships'><w:body>" + body + "</w:body></w:document>");
            using (var writer = new StreamWriter(zip.CreateEntry("word/_rels/document.xml.rels").Open(), Encoding.UTF8))
                writer.Write("<Relationships><Relationship Id='picture' Target='media/not-an-image.png'/><Relationship Id='external' Target='https://untrusted.invalid/image' TargetMode='External'/></Relationships>");
            using var image = zip.CreateEntry("word/media/not-an-image.png").Open();
            image.Write([1, 2, 3]);
        }
        return output.ToArray();
    }

    private sealed class ProbeParser(Func<string, byte[], CancellationToken, ImmutableArray<ReadingPage>> parse) : ILocalDocumentParser
    {
        public int Calls { get; private set; }
        public ImmutableArray<ReadingPage> Parse(string extension, byte[] bytes, CancellationToken cancellation)
        { Calls++; return parse(extension, bytes, cancellation); }
    }

    private sealed class ChangedIdentity : IFilesystemIdentity
    {
        public int Calls { get; private set; }
        public PathPreflight Inspect(string path, bool requireDirectory = true)
        {
            var result = new WindowsFilesystemIdentity().Inspect(path, requireDirectory);
            Calls++;
            return Calls == 1 || result.Observation is null ? result : result with { Observation = result.Observation with { Identity = new("changed", "changed") } };
        }
    }

    private sealed class Files : IDisposable
    {
        private readonly string parent = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar);
        public string Root { get; }
        public Files() { Root = Path.Combine(parent, "TheOrder-playground-attachments-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(Root); }
        public string Write(string name, byte[] bytes) { string path = Path.Combine(Root, name); File.WriteAllBytes(path, bytes); return path; }
        public void Dispose()
        {
            if (!string.Equals(Path.GetDirectoryName(Path.GetFullPath(Root)), parent, StringComparison.OrdinalIgnoreCase)
                || !Path.GetFileName(Root).StartsWith("TheOrder-playground-attachments-", StringComparison.Ordinal))
                throw new InvalidOperationException("Unexpected fixture cleanup target.");
            Directory.Delete(Root, true);
        }
    }
}
