using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using TheOrder.Application;

namespace TheOrder.Platform.Windows;

public sealed record HeroFrameLayout(int ArtworkWidth, int ArtworkHeight, bool PlaceRight, bool LightPlate, string Geometry);
public sealed record HeroFrameImage(HeroFrameLayout Layout, RasterPixels Pixels);

// Only the rendered banner and glass. The original artwork and profile data have separate stores.
public sealed partial class IsolatedHeroFrameCache(NativeConfiguration configuration)
{
    internal const string DirectoryName = "native-hero-frames-v1";
    internal const int MaximumFiles = 16;
    internal const int MaximumBytes = 32 * 1024 * 1024;
    private const int MaximumFrameBytes = 24 * 1024 * 1024;
    private const int LegacyMagic = 0x31464F54;
    private const int Magic = 0x32464F54;
    private const int LegacyHeaderBytes = 44;
    private const int MaximumGeometryBytes = 128;

    private readonly SemaphoreSlim reader = new(1, 1);
    private readonly SemaphoreSlim writer = new(1, 1);
    private readonly ConditionalWeakTable<RasterPixels, string> sourceHashes = new();

    internal async Task<ImageCacheUsage> ManageDiskAsync(bool clear)
    {
        await reader.WaitAsync().ConfigureAwait(false);
        try
        {
            return await ImageCacheStorage.WithDirectoryAsync(configuration, DirectoryName,
                name => name.EndsWith(".frame", StringComparison.Ordinal) && FrameFile().IsMatch(name),
                MaximumFiles + 16, writer, clear).ConfigureAwait(false);
        }
        finally { reader.Release(); }
    }

    public static bool Fits(int width, int height) =>
        width > 0 && height > 0 && (long)width * height <= MaximumFrameBytes / 4;

    public Task<HeroFrameImage?> ReadAsync(string index, int width, CancellationToken cancellation) =>
        RunAsync<HeroFrameImage?>(reader, create: false, null, root =>
        {
            var name = IndexedFileName(index);
            return Task.FromResult(ReadIndexed(Path.Combine(root, name), name, width, cancellation));
        }, cancellation);

    public Task<bool> WriteAsync(string index, HeroFrameImage frame) =>
        Valid(frame.Layout) && Fits(frame.Pixels.Width, frame.Pixels.Height) && frame.Pixels.Bytes == (long)frame.Pixels.Width * frame.Pixels.Height * 4
        ? RunAsync(writer, create: true, false, root => WriteIndexedAsync(root, IndexedFileName(index), frame), CancellationToken.None)
        : Task.FromResult(false);

    // The previous format was keyed by original pixels. It is read after the
    // regular image load and rewritten under the source index on a legacy hit.
    public Task<RasterPixels?> ReadAsync(string layout, RasterPixels artwork, int width, int height,
        CancellationToken cancellation) => Fits(width, height)
        ? RunAsync<RasterPixels?>(reader, create: false, null, root =>
            Task.FromResult(ReadLegacy(Path.Combine(root, LegacyFileName(layout, artwork)), width, height, cancellation)), cancellation)
        : Task.FromResult<RasterPixels?>(null);

    private static string IndexedFileName(string index) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("hero-index-v2/" + index))) + ".frame";

    private string LegacyFileName(string layout, RasterPixels artwork)
    {
        var source = sourceHashes.GetValue(artwork, pixels => Convert.ToHexString(SHA256.HashData(pixels.Bgra)));
        var key = FormattableString.Invariant($"{artwork.Width}/{artwork.Height}/{source}/{layout}");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))) + ".frame";
    }

    private static bool Valid(HeroFrameLayout layout) =>
        layout.ArtworkWidth is > 0 and <= ArtworkSizing.MaximumEdge &&
        layout.ArtworkHeight is > 0 and <= ArtworkSizing.MaximumEdge &&
        layout.Geometry.Length is > 0 and <= MaximumGeometryBytes &&
        layout.Geometry.All(c => char.IsAsciiDigit(c) || c is '/' or '.' or '-');

    private static HeroFrameImage? ReadIndexed(string path, string name, int width, CancellationToken cancellation)
    {
        using var file = IsolatedLibrarySource.OpenChecked(path);
        if (file.Length <= LegacyHeaderBytes || file.Length > MaximumBytes) return null;
        using var header = new BinaryReader(file);
        if (header.ReadInt32() != Magic || header.ReadInt32() != width) return null;
        var height = header.ReadInt32();
        if (!Fits(width, height)) return null;
        var sourceWidth = header.ReadInt32();
        var sourceHeight = header.ReadInt32();
        var flags = header.ReadByte();
        var geometryLength = header.ReadByte();
        if (flags > 3 || geometryLength is 0 or > MaximumGeometryBytes) return null;
        var layout = new HeroFrameLayout(sourceWidth, sourceHeight, (flags & 1) != 0, (flags & 2) != 0,
            Encoding.ASCII.GetString(header.ReadBytes(geometryLength)));
        if (!Valid(layout)) return null;
        var hash = header.ReadBytes(32);
        var pixels = ReadPixels(file, width, height, cancellation);
        return pixels is not null && CryptographicOperations.FixedTimeEquals(hash, Checksum(name, layout, pixels))
            ? new(layout, pixels) : null;
    }

    private static RasterPixels? ReadLegacy(string path, int width, int height, CancellationToken cancellation)
    {
        using var file = IsolatedLibrarySource.OpenChecked(path);
        if (file.Length <= LegacyHeaderBytes || file.Length > MaximumBytes) return null;
        using var header = new BinaryReader(file);
        if (header.ReadInt32() != LegacyMagic || header.ReadInt32() != width || header.ReadInt32() != height) return null;
        var hash = header.ReadBytes(32);
        var pixels = ReadPixels(file, width, height, cancellation);
        return pixels is not null && CryptographicOperations.FixedTimeEquals(hash, SHA256.HashData(pixels.Bgra)) ? pixels : null;
    }

    private static RasterPixels? ReadPixels(Stream file, int width, int height, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        var pixels = GC.AllocateUninitializedArray<byte>(width * height * 4);
        using var image = new ZLibStream(file, CompressionMode.Decompress, leaveOpen: true);
        image.ReadExactly(pixels);
        cancellation.ThrowIfCancellationRequested();
        return image.ReadByte() == -1 ? new(width, height, pixels) : null;
    }

    private static byte[] Checksum(string name, HeroFrameLayout layout, RasterPixels image)
    {
        using var checksum = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var header = FormattableString.Invariant(
            $"{name}/{image.Width}/{image.Height}/{layout.ArtworkWidth}/{layout.ArtworkHeight}/{layout.PlaceRight}/{layout.LightPlate}/{layout.Geometry}");
        checksum.AppendData(Encoding.ASCII.GetBytes(header));
        checksum.AppendData(image.Bgra);
        return checksum.GetHashAndReset();
    }

    private async Task<bool> WriteIndexedAsync(string root, string name, HeroFrameImage frame)
    {
        var (layout, pixels) = frame;
        using var encoded = new MemoryStream();
        using (var header = new BinaryWriter(encoded, Encoding.UTF8, leaveOpen: true))
        {
            header.Write(Magic);
            header.Write(pixels.Width);
            header.Write(pixels.Height);
            header.Write(layout.ArtworkWidth);
            header.Write(layout.ArtworkHeight);
            header.Write((byte)((layout.PlaceRight ? 1 : 0) | (layout.LightPlate ? 2 : 0)));
            header.Write((byte)layout.Geometry.Length);
            header.Write(Encoding.ASCII.GetBytes(layout.Geometry));
            header.Write(Checksum(name, layout, pixels));
        }
        using (var image = new ZLibStream(encoded, CompressionLevel.Fastest, leaveOpen: true))
            image.Write(pixels.Bgra);
        if (encoded.Length > MaximumBytes) return false;

        var files = new DirectoryInfo(root).EnumerateFiles("*.frame*").Take(MaximumFiles + 2).ToArray();
        if (files.Length > MaximumFiles + 1 || files.Any(file => !FrameFile().IsMatch(file.Name))) return false;
        var target = Path.Combine(root, name);
        WindowsAtomicFile.CheckExisting(target);
        var entries = files.Where(file => file.Extension == ".frame").ToArray();
        var total = entries.Sum(file => file.Length);
        var count = entries.Length;
        // An interrupted process may leave one staging file. Recover only our own checked paths.
        foreach (var staged in files.Where(file => file.Extension == ".tmp"))
        {
            WindowsAtomicFile.CheckExisting(staged.FullName);
            File.Delete(staged.FullName);
        }
        var replacing = entries.Any(file => file.Name == name);
        foreach (var old in entries.OrderBy(file => file.LastWriteTimeUtc))
        {
            if (total + encoded.Length <= MaximumBytes && count + (replacing ? 0 : 1) <= MaximumFiles) break;
            if (old.Name == name) continue;
            WindowsAtomicFile.CheckExisting(old.FullName);
            total -= old.Length;
            count--;
            File.Delete(old.FullName);
        }
        if (total + encoded.Length > MaximumBytes || count + (replacing ? 0 : 1) > MaximumFiles) return false;
        NativeProfileAccess.Validate(configuration);
        await WindowsAtomicFile.WriteAsync(target, encoded.GetBuffer().AsMemory(0, (int)encoded.Length)).ConfigureAwait(false);
        return true;
    }

    private async Task<T> RunAsync<T>(SemaphoreSlim slot, bool create, T unavailable,
        Func<string, Task<T>> action, CancellationToken cancellation)
    {
        // Keep at most one read and one write in flight. Repaints can always fall back to rendering.
        if (!slot.Wait(0, cancellation)) return unavailable;
        try
        {
            return await Task.Run(async () =>
            {
                var profile = NativeProfileAccess.Validate(configuration);
                var held = IsolatedLibraryPersistence.PinDirectoryChain(profile);
                try
                {
                    NativeProfileAccess.Validate(configuration);
                    var root = Path.Combine(profile, DirectoryName);
                    if (create) Directory.CreateDirectory(root);
                    else if (!Directory.Exists(root)) return unavailable;
                    var pinned = IsolatedLibraryPersistence.PinDirectoryChain(root);
                    try
                    {
                        cancellation.ThrowIfCancellationRequested();
                        return await action(root).ConfigureAwait(false);
                    }
                    finally { foreach (var handle in pinned) handle.Dispose(); }
                }
                finally { foreach (var handle in held) handle.Dispose(); }
            }, cancellation).ConfigureAwait(false);
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException
            or DataIsolationException or NativeProfileException) { return unavailable; }
        finally { slot.Release(); }
    }

    [GeneratedRegex(@"\A[A-F0-9]{64}\.frame(?:\.[a-f0-9]{32}\.tmp)?\z", RegexOptions.CultureInvariant)]
    private static partial Regex FrameFile();
}
