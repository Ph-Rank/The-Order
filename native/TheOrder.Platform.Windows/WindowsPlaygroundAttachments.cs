using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using TheOrder.Application;
using TheOrder.Application.Playground;

namespace TheOrder.Platform.Windows;

// This boundary accepts only files explicitly selected by the user. It returns
// immutable text snapshots, never filesystem permissions, paths or Office objects.
public sealed partial class WindowsPlaygroundAttachments(IFilesystemIdentity identity, ILocalDocumentParser parser)
{
    private const int MaximumFiles = 4;
    private const int MaximumBytes = 2 * 1024 * 1024;
    private const int MaximumImageSourceBytes = 8 * 1024 * 1024;
    private const int MaximumImageBytes = 4 * 1024 * 1024;
    private const int MaximumTotalImageBytes = 8 * 1024 * 1024;
    private const int MaximumCharacters = 12_000;
    private const int MaximumTotalCharacters = 24_000;
    private const string EmptyDocumentPlaceholder = "此文档没有可读取的正文。";
    private static readonly ImmutableHashSet<string> Extensions =
        ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase, ".txt", ".md", ".rtf", ".doc", ".docx", ".xls", ".xlsx");
    private static readonly ImmutableHashSet<string> ImageExtensions =
        ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase, ".png", ".jpg", ".jpeg", ".webp");

    public async Task<ImmutableArray<PlaygroundAttachment>> ReadAsync(IReadOnlyList<string> paths, CancellationToken cancellation = default)
    {
        try
        {
            cancellation.ThrowIfCancellationRequested();
            if (paths is null || paths.Count > MaximumFiles) Reject("每条消息最多添加 4 份文件资料。");
            var selected = paths!.ToArray();
            return await Task.Run(() => ReadSelectedAsync(selected, cancellation), cancellation).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            throw new OperationCanceledException(cancellation);
        }
        catch (AttachmentRejectedException error)
        {
            throw new InvalidDataException(error.Message);
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            // Native/parser exceptions may carry FileName, XML base URI or a full
            // source path. Do not retain them as InnerException or log them here.
            throw new InvalidDataException("无法只读提取这份文件；请检查文件是否完整、可访问，并使用支持的正文或表格格式。");
        }
    }

    private async Task<ImmutableArray<PlaygroundAttachment>> ReadSelectedAsync(string[] selected, CancellationToken cancellation)
    {
        var attachments = ImmutableArray.CreateBuilder<PlaygroundAttachment>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int total = 0, imageBytes = 0;
        foreach (string selectedPath in selected)
        {
            cancellation.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(selectedPath) || !Path.IsPathFullyQualified(selectedPath)
                || selectedPath.StartsWith(@"\\", StringComparison.Ordinal))
                Reject("请手动选择本机磁盘中的文件；不读取相对路径、网络位置或设备路径。");
            string path = Path.GetFullPath(selectedPath);
            string extension = Path.GetExtension(path);
            bool isImage = ImageExtensions.Contains(extension);
            if (!Extensions.Contains(extension) && !isImage) Reject("可添加 TXT、MD、RTF、Word、Excel，或静态 PNG、JPEG、WebP 图片；暂不读取 PDF、SVG 或动画。");
            string name = Path.GetFileName(path);
            if (string.IsNullOrWhiteSpace(name) || name.Length > 120 || name is "." or ".."
                || name.Any(c => char.IsControl(c) || c is '/' or '\\' or ':'))
                Reject("文件名需要不超过 120 个字符，且不能包含路径或控制字符；请先重命名后添加。");
            if (new DriveInfo(Path.GetPathRoot(path)!).DriveType == DriveType.Network)
                Reject("请先把文件保存到本机磁盘；不直接读取网络映射盘。");
            if (!seen.Add(path)) Reject("同一份文件无需重复添加。");

            var before = identity.Inspect(path, false);
            var observation = before.Observation;
            if (!before.IsValid || observation is null || observation.IsDirectory || observation.ViaReparsePoint)
                Reject("文件不存在、是链接或无法访问，请重新选择本机文件。");
            byte[]? bytes = null;
            try
            {
                var held = IsolatedLibraryPersistence.PinDirectoryChain(Path.GetDirectoryName(path)!);
                try
                {
                    using var stream = IsolatedLibrarySource.OpenChecked(path);
                    if (stream.Length < 1) Reject("文件为空，没有可提取的正文或表格。");
                    if (stream.Length > (isImage ? MaximumImageSourceBytes : MaximumBytes))
                        Reject(isImage ? "每张源图片不能超过 8 MiB。" : "每份文档不能超过 2 MiB；不会自动截断或读取部分内容。");
                    bytes = new byte[checked((int)stream.Length)];
                    await stream.ReadExactlyAsync(bytes, cancellation).ConfigureAwait(false);
                    cancellation.ThrowIfCancellationRequested();
                    new InstallationPreflightService(identity).Revalidate(observation);
                }
                finally { foreach (var lease in held) lease.Dispose(); }

                if (isImage)
                {
                    var image = await ReadImageAsync(name, bytes!, cancellation).ConfigureAwait(false);
                    if (imageBytes + image.Bytes > MaximumTotalImageBytes) Reject("同一消息的图片编码后合计不能超过 8 MiB；请减少图片。");
                    imageBytes += image.Bytes; attachments.Add(image.Attachment);
                    continue;
                }
                var pages = parser.Parse(extension, bytes!, cancellation);
                cancellation.ThrowIfCancellationRequested();
                string text = Flatten(pages, cancellation);
                if (total + text.Length > MaximumTotalCharacters)
                    Reject("文件资料合计不能超过 24000 个字符；请减少文件或选取较短的资料，不会自动截断。");
                total += text.Length;
                attachments.Add(new(name, text));
            }
            finally { if (bytes is not null) CryptographicOperations.ZeroMemory(bytes); }
        }
        cancellation.ThrowIfCancellationRequested();
        return attachments.ToImmutable();
    }

    private static string Flatten(ImmutableArray<ReadingPage> pages, CancellationToken cancellation)
    {
        // The reader's synthetic empty-state sentence is UI, not source text.
        if (pages.Length == 1 && pages[0].Blocks.Length == 1
            && pages[0].Blocks[0] is { Kind: "paragraph", Text: EmptyDocumentPlaceholder })
            Reject("没有可提取的正文或表格；仅包含图片的文件不会被当作已阅读的文字资料。");
        var result = new StringBuilder();
        bool blankLine = false;
        void Append(string value)
        {
            cancellation.ThrowIfCancellationRequested();
            if (value.Length > MaximumCharacters - result.Length)
                Reject("单份文件提取后不能超过 12000 个字符；请使用较短的资料，不会自动截断。");
            result.Append(value);
        }
        void NewLine()
        {
            if (result.Length != 0) Append(blankLine ? "\n\n" : "\n");
            blankLine = false;
        }
        foreach (var page in pages)
        {
            cancellation.ThrowIfCancellationRequested();
            foreach (var block in page.Blocks)
            {
                cancellation.ThrowIfCancellationRequested();
                if (block.Kind == "space") { blankLine = result.Length != 0; continue; }
                if (block.Kind is "paragraph" or "heading")
                {
                    if (string.IsNullOrWhiteSpace(block.Text)) continue;
                    NewLine(); Append(block.Text.TrimEnd());
                }
                else if (block.Kind == "table" && !block.Rows.IsDefaultOrEmpty)
                {
                    var rows = block.Rows.Where(row => row.Any(cell => !string.IsNullOrWhiteSpace(cell)));
                    bool labelWritten = false;
                    foreach (var row in rows)
                    {
                        cancellation.ThrowIfCancellationRequested();
                        if (!labelWritten && !string.IsNullOrWhiteSpace(block.Text)) { NewLine(); Append(block.Text.TrimEnd()); }
                        labelWritten = true; NewLine();
                        for (int column = 0; column < row.Length; column++)
                        {
                            if (column != 0) Append("\t");
                            Append(row[column]);
                        }
                    }
                }
                // Images and reading-page labels are never represented as text.
            }
        }
        string text = result.ToString().Trim();
        if (text.Length == 0) Reject("没有可提取的正文或表格；仅包含图片的文件不会被当作已阅读的文字资料。");
        return text;
    }

    [DoesNotReturn]
    private static void Reject(string message) => throw new AttachmentRejectedException(message);
    private sealed class AttachmentRejectedException(string message) : Exception(message);
}
