using System.Security.Cryptography;
using System.Text.RegularExpressions;
using TheOrder.Application;

namespace TheOrder.Platform.Windows;

// Derived pixels only. Personal JPEGs live in library.json and are never owned/deleted here.
public sealed partial class IsolatedThumbnailCache(NativeConfiguration configuration) : IThumbnailDiskCache
{
    private const string DirectoryName = "native-thumbnails-v1";
    private const int Magic = 0x314F4854;
    private const int MaximumFiles = 4096;
    private readonly SemaphoreSlim queue = new(1, 1);

    internal Task<ImageCacheUsage> ManageDiskAsync(bool clear) =>
        ImageCacheStorage.WithDirectoryAsync(configuration, DirectoryName,
            name => name.EndsWith(".thumb", StringComparison.Ordinal) && CacheKey().IsMatch(name[..^6]),
            MaximumFiles + 16, queue, clear);

    public Task<RasterPixels?> ReadAsync(string key, CancellationToken cancellation) => Run<RasterPixels?>(root =>
    {
        try
        {
            using var file = IsolatedLibrarySource.OpenChecked(Path.Combine(root, key + ".thumb"));
            using var reader = new BinaryReader(file);
            if (file.Length < 44 || file.Length > (long)ArtworkSizing.MaximumEdge * ArtworkSizing.MaximumEdge * 4 + 44 || reader.ReadInt32() != Magic) return null;
            int width = reader.ReadInt32(), height = reader.ReadInt32();
            if (width < 1 || width > ArtworkSizing.MaximumEdge || height < 1 || height > ArtworkSizing.MaximumEdge || file.Length != 44L + (long)width * height * 4) return null;
            var hash = reader.ReadBytes(32); var pixels = reader.ReadBytes(width * height * 4);
            return CryptographicOperations.FixedTimeEquals(hash, SHA256.HashData(pixels)) ? new(width, height, pixels) : null;
        }
        catch (FileNotFoundException) { return null; }
    }, key, cancellation);
    public Task WriteAsync(string key, RasterPixels pixels, long maximumBytes, CancellationToken cancellation) => Run(root =>
    {
        if (maximumBytes <= 0 || pixels.Bytes + 44 > maximumBytes) return false;
        var files = new DirectoryInfo(root).EnumerateFiles("*.thumb").Take(MaximumFiles + 1).ToArray();
        if (files.Length > MaximumFiles) return false; // Cache flooding cannot trigger unbounded enumeration/deletion.
        long total = files.Sum(f => f.Length); string target = Path.Combine(root, key + ".thumb");
        foreach (var file in files.OrderBy(f => f.LastWriteTimeUtc))
        {
            if (total + pixels.Bytes + 44 <= maximumBytes) break;
            if (!CacheKey().IsMatch(Path.GetFileNameWithoutExtension(file.Name))) continue;
            using (IsolatedLibrarySource.OpenChecked(file.FullName)) { }
            long length = file.Length; File.Delete(file.FullName); total -= length;
        }
        try { using var prior = IsolatedLibrarySource.OpenChecked(target); } catch (FileNotFoundException) { }
        string temporary = Path.Combine(root, Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new BinaryWriter(file))
            { writer.Write(Magic); writer.Write(pixels.Width); writer.Write(pixels.Height); writer.Write(SHA256.HashData(pixels.Bgra)); writer.Write(pixels.Bgra); }
            File.Move(temporary, target, true); return true;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }, key, cancellation);
    private async Task<T> Run<T>(Func<string, T> action, string key, CancellationToken cancellation)
    {
        if (!CacheKey().IsMatch(key)) throw new InvalidDataException(UiText.Get("缩略图键无效。"));
        await queue.WaitAsync(cancellation).ConfigureAwait(false);
        try
        {
            return await Task.Run(() =>
            {
                var profile = NativeProfileAccess.Validate(configuration);
                var handles = IsolatedLibraryPersistence.PinDirectoryChain(profile);
                try
                {
                    NativeProfileAccess.Validate(configuration);
                    var cacheRoot = Path.Combine(profile, DirectoryName);
                    Directory.CreateDirectory(cacheRoot);
                    var cacheHandles = IsolatedLibraryPersistence.PinDirectoryChain(cacheRoot);
                    try { cancellation.ThrowIfCancellationRequested(); return action(cacheRoot); }
                    finally { foreach (var handle in cacheHandles) handle.Dispose(); }
                }
                finally { foreach (var handle in handles) handle.Dispose(); }
            }, cancellation).ConfigureAwait(false);
        }
        finally { queue.Release(); }
    }
    // Personal banners use a fixed namespace plus the same SHA-256 identity.
    // Admit that exact namespace; paths, arbitrary prefixes and non-hex IDs stay rejected.
    [GeneratedRegex("^(?:local-banner-)?[A-F0-9]{64}-[0-9]{2,4}$", RegexOptions.CultureInvariant)] private static partial Regex CacheKey();
}
