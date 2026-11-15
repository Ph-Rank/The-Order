using TheOrder.Application;
using TheOrder.Platform.Windows;
using Xunit;

namespace TheOrder.Tests;

[Collection("Native profile resource")]
public sealed class SettingsManagementTests
{
    [Fact]
    public async Task CloseChoiceDefaultsToTrayAndSurvivesReload()
    {
        using var profile = new SettingsProfile();
        var originalUi = profile.Store.ReadComponent("native-ui.json");
        var preference = new WindowsClosePreference(profile.Configuration);
        Assert.False(await preference.ReadAsync());

        await preference.SaveAsync(true);
        Assert.True(await new WindowsClosePreference(profile.Configuration).ReadAsync());
        await preference.SaveAsync(false);
        Assert.False(await new WindowsClosePreference(profile.Configuration).ReadAsync());
        Assert.Equal(originalUi, profile.Store.ReadComponent("native-ui.json"));
    }

    [Fact]
    public async Task InvalidCloseChoiceIsPreserved()
    {
        using var profile = new SettingsProfile();
        var path = Path.Combine(profile.Configuration.DataDirectory!, "native-window-close-v1.dat");
        await File.WriteAllTextAsync(path, "future format");
        var preference = new WindowsClosePreference(profile.Configuration);
        await Assert.ThrowsAsync<InvalidDataException>(preference.ReadAsync);
        await Assert.ThrowsAsync<InvalidDataException>(() => preference.SaveAsync(true));
        Assert.Equal("future format", await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task CacheMeasurementsAndClearsUseOnlyTheSelectedStore()
    {
        using var profile = new SettingsProfile();
        var root = profile.Configuration.DataDirectory!;
        var originalLibrary = profile.Store.ReadComponent("library.json");
        var artwork = new IsolatedArtworkCache(profile.Configuration);
        var thumbnails = new IsolatedThumbnailCache(profile.Configuration);
        var hero = new IsolatedHeroFrameCache(profile.Configuration);
        var storage = new ImageCacheStorage(artwork, thumbnails, hero);
        var artworkDirectory = Path.Combine(root, "native-artwork-v1");
        Assert.Equal(default, await storage.InspectAsync(ImageCacheKind.OnlineArtwork));
        Assert.False(Directory.Exists(artworkDirectory));

        var key = new string('A', 64);
        var bytes = new byte[] { 1, 2, 3, 4 };
        Assert.True(await artwork.WriteAsync(key, bytes, default));
        var pixels = new RasterPixels(2, 2, new byte[16]);
        await thumbnails.WriteAsync(key + "-64", pixels, 1024, default);
        Assert.True(await hero.WriteAsync("fixture", new(new(2, 2, false, false, "2/2"), pixels)));
        var unrelated = Path.Combine(artworkDirectory, "unrelated.art");
        await File.WriteAllTextAsync(unrelated, "other file");
        Directory.CreateDirectory(Path.Combine(artworkDirectory, new string('B', 64) + ".art"));

        Assert.Equal(new ImageCacheUsage(44, 0, 1), await storage.InspectAsync(ImageCacheKind.OnlineArtwork));
        Assert.Equal(60, (await storage.InspectAsync(ImageCacheKind.Thumbnails)).Bytes);
        var frameSize = (await storage.InspectAsync(ImageCacheKind.HeroFrames)).Bytes;
        Assert.True(frameSize > 0);

        Assert.Equal(new ImageCacheUsage(0, 44, 1), await storage.ClearAsync(ImageCacheKind.OnlineArtwork));
        Assert.Null(await artwork.ReadAsync(key, default));
        Assert.Equal("other file", await File.ReadAllTextAsync(unrelated));
        Assert.NotNull(await thumbnails.ReadAsync(key + "-64", default));
        Assert.NotNull(await hero.ReadAsync("fixture", 2, default));
        Assert.Equal(originalLibrary, profile.Store.ReadComponent("library.json"));

        Assert.Equal(new ImageCacheUsage(0, 60, 0), await storage.ClearAsync(ImageCacheKind.Thumbnails));
        Assert.Equal(new ImageCacheUsage(0, frameSize, 0), await storage.ClearAsync(ImageCacheKind.HeroFrames));
        Assert.Equal(default, await storage.ClearAsync(ImageCacheKind.HeroFrames));
    }

    [Fact]
    public async Task CacheCleanupReportsLockedFilesAndCanBeRetried()
    {
        using var profile = new SettingsProfile();
        var root = profile.Configuration.DataDirectory!;
        var artwork = new IsolatedArtworkCache(profile.Configuration);
        var storage = new ImageCacheStorage(artwork, new(profile.Configuration), new(profile.Configuration));
        var key = new string('A', 64);
        Assert.True(await artwork.WriteAsync(key, new byte[4], default));
        var path = Path.Combine(root, "native-artwork-v1", key + ".art");
        using (File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None))
            Assert.Equal(new ImageCacheUsage(44, 0, 1), await storage.ClearAsync(ImageCacheKind.OnlineArtwork));
        Assert.Equal(new ImageCacheUsage(0, 44, 0), await storage.ClearAsync(ImageCacheKind.OnlineArtwork));
    }

    private sealed class SettingsProfile : IDisposable
    {
        private readonly DirectoryInfo directory = Directory.CreateTempSubdirectory("the-order-settings-");
        internal SettingsProfile()
        {
            Store = NativeProfileStore.Open(new(NativeProfileKind.Test, directory.FullName), NativeProfileTests.Codec);
            Configuration = Store.Configuration(false);
        }
        internal NativeProfileStore Store { get; }
        internal NativeConfiguration Configuration { get; }
        public void Dispose()
        {
            Store.Dispose();
            directory.Delete(recursive: true);
        }
    }
}
