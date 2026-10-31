using System.Collections.Immutable;
using System.Runtime.InteropServices.WindowsRuntime;
using global::Windows.Data.Pdf;
using global::Windows.Graphics.Imaging;
using global::Windows.Storage.Streams;
using TheOrder.Application;
using TheOrder.Domain;

namespace TheOrder.Platform.Windows;

public sealed class WindowsLocalReading(IFilesystemIdentity identity, ILocalDocumentParser parser) : ILocalReadingLibrary
{
    // Classification is explicit. A file name or the presence of a file in a game
    // folder never grants permission to classify/read it as a guide or a note.
    public Task<LocalReadingFiles> FindAsync(LibraryEntry entry, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        return Task.FromResult(new LocalReadingFiles(entry.GuidePaths.Select(path => new LocalReadingFile(path, Path.GetFileName(path), "guide")).ToImmutableArray(), []));
    }
    public Task<ILocalReadingDocument> OpenAsync(LocalReadingFile file, CancellationToken cancellation) => Task.Run(async () =>
    {
        string extension = Path.GetExtension(file.Path).ToLowerInvariant();
        if (!Path.IsPathFullyQualified(file.Path) || !LocalDocuments.Extensions.Contains(extension)) throw new InvalidDataException("不支持此本地文件类型。");
        string path = Path.GetFullPath(file.Path);
        var before = identity.Inspect(path, false);
        if (!before.IsValid || before.Observation!.ViaReparsePoint) throw new InvalidDataException("文件不存在、是链接或无法访问，请重新定位。");
        byte[] bytes; var held = IsolatedLibraryPersistence.PinDirectoryChain(Path.GetDirectoryName(path)!);
        try
        {
            using var stream = IsolatedLibrarySource.OpenChecked(path);
            if (stream.Length is < 1 or > 64 * 1024 * 1024) throw new InvalidDataException("文档为空或超过 64 MiB。");
            bytes = new byte[checked((int)stream.Length)]; await stream.ReadExactlyAsync(bytes, cancellation).ConfigureAwait(false);
            new InstallationPreflightService(identity).Revalidate(before.Observation);
        }
        finally { foreach (var lease in held) lease.Dispose(); }
        if (extension == ".pdf") return (ILocalReadingDocument)await PdfReading.OpenAsync(file.Name, bytes, cancellation).ConfigureAwait(false);
        if (extension is ".png" or ".jpg" or ".jpeg") return await ImageReading.OpenAsync(file.Name, bytes, cancellation).ConfigureAwait(false);
        return await TextReading.OpenAsync(file.Name, parser.Parse(extension, bytes, cancellation), extension is ".doc" or ".docx" or ".rtf" ? "只读阅读版 · 正文与表格" : extension is ".xls" or ".xlsx" ? "只读表格 · 公式显示文件中已保存的结果" : "只读", cancellation).ConfigureAwait(false);
    }, cancellation);
    private sealed class TextReading(string name, ImmutableArray<(ReadingPage Page, int Part)> pages, string notice) : ILocalReadingDocument
    {
        public string Name => name; public string Notice => notice; public int PageCount => pages.Length;
        public static async Task<TextReading> OpenAsync(string name, ImmutableArray<ReadingPage> source, string notice, CancellationToken cancellation)
        {
            var pages = ImmutableArray.CreateBuilder<(ReadingPage, int)>();
            foreach (var page in source)
            {
                var image = page.Blocks.FirstOrDefault(b => b.Image is not null)?.Image;
                if (image is null) pages.Add((page, 0));
                else { using var reading = await ImageReading.OpenAsync(name, image, cancellation).ConfigureAwait(false); for (int i = 0; i < reading.PageCount; i++) pages.Add((page, i)); }
                if (pages.Count > 2000) throw new InvalidDataException("文档超过 2000 个阅读分页。");
            }
            return new(name, pages.ToImmutable(), notice);
        }
        public async Task<ReadingPage> PageAsync(int index, CancellationToken cancellation)
        {
            if (index < 0 || index >= pages.Length) throw new ArgumentOutOfRangeException(nameof(index));
            var page = pages[index].Page; var picture = page.Blocks.FirstOrDefault(b => b.Image is not null);
            if (picture?.Image is { } bytes)
            {
                using var image = await ImageReading.OpenAsync(name, bytes, cancellation).ConfigureAwait(false);
                var rendered = await image.PageAsync(pages[index].Part, cancellation).ConfigureAwait(false);
                return rendered with { Label = page.Label, Blocks = page.Blocks.Where(b => b.Image is null).ToImmutableArray() };
            }
            return page;
        }
        public void Dispose() { }
    }
    private sealed class PdfReading(string name, InMemoryRandomAccessStream stream, PdfDocument document) : ILocalReadingDocument
    {
        public string Name => name; public string Notice => "只读 PDF"; public int PageCount => checked((int)document.PageCount);
        public static async Task<PdfReading> OpenAsync(string name, byte[] bytes, CancellationToken cancellation)
        {
            var stream = await MemoryAsync(bytes, cancellation).ConfigureAwait(false);
            try { var pdf = await PdfDocument.LoadFromStreamAsync(stream).AsTask(cancellation).ConfigureAwait(false); if (pdf.PageCount is < 1 or > 2000) throw new InvalidDataException("PDF 超过 2000 页或没有页面。"); return new(name, stream, pdf); }
            catch { stream.Dispose(); throw; }
        }
        public async Task<ReadingPage> PageAsync(int index, CancellationToken cancellation)
        {
            if (index < 0 || index >= PageCount) throw new ArgumentOutOfRangeException(nameof(index));
            using var page = document.GetPage((uint)index); using var output = new InMemoryRandomAccessStream();
            double scale = Math.Min(1440 / page.Size.Width, 2048 / page.Size.Height);
            await page.RenderToStreamAsync(output, new PdfPageRenderOptions { DestinationWidth = (uint)Math.Max(1, Math.Round(page.Size.Width * scale)), DestinationHeight = (uint)Math.Max(1, Math.Round(page.Size.Height * scale)) }).AsTask(cancellation).ConfigureAwait(false);
            output.Seek(0); var decoder = await BitmapDecoder.CreateAsync(output).AsTask(cancellation).ConfigureAwait(false);
            return await PixelsAsync(decoder, new BitmapTransform { ScaledWidth = (uint)Math.Max(1, Math.Round(page.Size.Width * scale)), ScaledHeight = (uint)Math.Max(1, Math.Round(page.Size.Height * scale)), InterpolationMode = BitmapInterpolationMode.Fant }, $"第 {index + 1} 页", cancellation).ConfigureAwait(false);
        }
        public void Dispose() => stream.Dispose();
    }
    private sealed class ImageReading(string name, InMemoryRandomAccessStream stream, BitmapDecoder decoder, uint width, uint height) : ILocalReadingDocument
    {
        public string Name => name; public string Notice => "只读图片"; public int PageCount => checked((int)((height + 2047) / 2048));
        public static async Task<ImageReading> OpenAsync(string name, byte[] bytes, CancellationToken cancellation)
        {
            var stream = await MemoryAsync(bytes, cancellation).ConfigureAwait(false);
            try
            {
                var decoder = await BitmapDecoder.CreateAsync(stream).AsTask(cancellation).ConfigureAwait(false);
                if (decoder.PixelWidth < 1 || decoder.PixelHeight < 1 || (ulong)decoder.PixelWidth * decoder.PixelHeight > 80_000_000 || decoder.FrameCount != 1) throw new InvalidDataException("仅支持 8000 万像素以内的静态图片。");
                double scale = Math.Min(1, 1440d / decoder.OrientedPixelWidth);
                return new(name, stream, decoder, Math.Max(1, (uint)(decoder.OrientedPixelWidth * scale)), Math.Max(1, (uint)(decoder.OrientedPixelHeight * scale)));
            }
            catch { stream.Dispose(); throw; }
        }
        public Task<ReadingPage> PageAsync(int index, CancellationToken cancellation)
        {
            if (index < 0 || index >= PageCount) throw new ArgumentOutOfRangeException(nameof(index));
            return PixelsAsync(decoder, new BitmapTransform { ScaledWidth = width, ScaledHeight = height, Bounds = new BitmapBounds { X = 0, Y = (uint)index * 2048, Width = width, Height = Math.Min(2048, height - (uint)index * 2048) }, InterpolationMode = BitmapInterpolationMode.Fant }, PageCount > 1 ? $"图片 · 第 {index + 1} 段" : "图片", cancellation);
        }
        public void Dispose() => stream.Dispose();
    }
    private static async Task<InMemoryRandomAccessStream> MemoryAsync(byte[] bytes, CancellationToken cancellation)
    { var stream = new InMemoryRandomAccessStream(); try { await stream.WriteAsync(bytes.AsBuffer()).AsTask(cancellation).ConfigureAwait(false); stream.Seek(0); return stream; } catch { stream.Dispose(); throw; } }
    private static async Task<ReadingPage> PixelsAsync(BitmapDecoder decoder, BitmapTransform transform, string label, CancellationToken cancellation)
    {
        using var bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Rgba8, BitmapAlphaMode.Straight, transform, ExifOrientationMode.RespectExifOrientation, ColorManagementMode.ColorManageToSRgb).AsTask(cancellation).ConfigureAwait(false);
        if ((long)bitmap.PixelWidth * bitmap.PixelHeight > 3_000_000) throw new InvalidDataException("页面图像超过阅读器限制。");
        var rgba = new byte[checked(bitmap.PixelWidth * bitmap.PixelHeight * 4)]; bitmap.CopyToBuffer(rgba.AsBuffer());
        return new(label, [], bitmap.PixelWidth, bitmap.PixelHeight, rgba);
    }
}
