using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using TheOrder.Application;
using TheOrder.Platform.Windows;
using Xunit;

namespace TheOrder.Tests;

public sealed class HeroFrameCacheTests
{
    private static readonly HeroFrameLayout Layout = new(1920, 1080, true, false, "10.0/20.0/64.0/80.0/128.0/96.0");

    [Fact]
    public async Task ReopenReadsExactPixelsAndLayoutBySourceWithoutOriginal()
    {
        using var profile = new TransactionProfile();
        using var other = new TransactionProfile();
        var image = new RasterPixels(8, 4, Enumerable.Range(0, 128).Select(value => (byte)value).ToArray());
        Assert.True(await new IsolatedHeroFrameCache(new(profile.Root)).WriteAsync("art/v1/dark/2", new(Layout, image)));

        var reopened = new IsolatedHeroFrameCache(new(profile.Root));
        var restored = await reopened.ReadAsync("art/v1/dark/2", 8, default);
        Assert.NotNull(restored);
        Assert.Equal(Layout, restored.Layout);
        Assert.Equal(image.Bgra, restored.Pixels.Bgra);
        Assert.Null(await reopened.ReadAsync("art/v2/dark/2", 8, default));
        Assert.Null(await reopened.ReadAsync("art/v1/light/2", 8, default));
        Assert.Null(await reopened.ReadAsync("art/v1/dark/2", 4, default));
        Assert.Null(await new IsolatedHeroFrameCache(new(other.Root)).ReadAsync("art/v1/dark/2", 8, default));
        Assert.False(Directory.Exists(Path.Combine(other.Root, IsolatedHeroFrameCache.DirectoryName)));
    }

    [Fact]
    public async Task PreviousPixelKeyedImageStillReadsForMigration()
    {
        using var profile = new TransactionProfile();
        var root = Path.Combine(profile.Root, IsolatedHeroFrameCache.DirectoryName);
        Directory.CreateDirectory(root);
        var pixels = new byte[64];
        var source = new RasterPixels(2, 2, new byte[16]);
        var sourceHash = Convert.ToHexString(SHA256.HashData(source.Bgra));
        const string layout = "legacy";
        var identity = FormattableString.Invariant($"{source.Width}/{source.Height}/{sourceHash}/{layout}");
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
        using (var file = File.Create(Path.Combine(root, key + ".frame")))
        {
            using (var header = new BinaryWriter(file, Encoding.UTF8, leaveOpen: true))
            {
                header.Write(0x31464F54);
                header.Write(4);
                header.Write(4);
                header.Write(SHA256.HashData(pixels));
            }
            using var compressed = new ZLibStream(file, CompressionLevel.Fastest);
            compressed.Write(pixels);
        }
        var cache = new IsolatedHeroFrameCache(new(profile.Root));
        var restored = await cache.ReadAsync(layout, source, 4, 4, default);
        Assert.NotNull(restored);
        Assert.Equal(pixels, restored.Bgra);
        Assert.Null(await cache.ReadAsync(layout, source with { Bgra = [1, .. source.Bgra.AsSpan(1)] }, 4, 4, default));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CorruptPixelsAndLayoutAreIgnoredAndAnInterruptedWriteIsRecovered(bool truncated)
    {
        using var profile = new TransactionProfile();
        var cache = new IsolatedHeroFrameCache(new(profile.Root));
        var image = new RasterPixels(64, 64, new byte[64 * 64 * 4]);
        Assert.True(await cache.WriteAsync("indexed", new(Layout, image)));

        var root = Path.Combine(profile.Root, IsolatedHeroFrameCache.DirectoryName);
        var path = Assert.Single(Directory.GetFiles(root, "*.frame"));
        var corrupt = File.ReadAllBytes(path);
        if (truncated) corrupt = corrupt[..(corrupt.Length / 2)];
        else corrupt[20] ^= 1; // A valid but incorrect placement flag must also fail the checksum.
        File.WriteAllBytes(path, corrupt);
        Assert.Null(await cache.ReadAsync("indexed", 64, default));

        var staging = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllBytes(staging, [1, 2, 3]);
        Assert.True(await cache.WriteAsync("indexed", new(Layout, image)));
        Assert.False(File.Exists(staging));
        Assert.Equal(image.Bgra, (await cache.ReadAsync("indexed", 64, default))!.Pixels.Bgra);
    }

    [Fact]
    public async Task BothTheFileCountAndCompressedSizeAreBounded()
    {
        using var profile = new TransactionProfile();
        var cache = new IsolatedHeroFrameCache(new(profile.Root));
        var root = Path.Combine(profile.Root, IsolatedHeroFrameCache.DirectoryName);
        var image = new RasterPixels(8, 8, new byte[256]);
        for (int i = 0; i <= IsolatedHeroFrameCache.MaximumFiles; i++)
        {
            Assert.True(await cache.WriteAsync(i.ToString(), new(Layout, image)));
            if (i == 0) File.SetLastWriteTimeUtc(Assert.Single(Directory.GetFiles(root, "*.frame")), DateTime.UtcNow.AddDays(-1));
        }
        Assert.Equal(IsolatedHeroFrameCache.MaximumFiles, Directory.GetFiles(root, "*.frame").Length);
        Assert.Null(await cache.ReadAsync("0", 8, default));

        var bytes = new byte[2048 * 2048 * 4];
        new Random(17).NextBytes(bytes);
        var large = new HeroFrameImage(Layout, new(2048, 2048, bytes));
        Assert.True(await cache.WriteAsync("large/1", large));
        Assert.True(await cache.WriteAsync("large/2", large));
        Assert.Null(await cache.ReadAsync("large/1", 2048, default));
        Assert.True(new DirectoryInfo(root).EnumerateFiles().Sum(file => file.Length) <= IsolatedHeroFrameCache.MaximumBytes);
        Assert.False(IsolatedHeroFrameCache.Fits(int.MaxValue, int.MaxValue));
    }
}
