using TheOrder.Application;

namespace TheOrder.Platform.Windows;

public enum ImageCacheKind { OnlineArtwork, Thumbnails, HeroFrames }

public readonly record struct ImageCacheUsage(long Bytes, long RemovedBytes, int SkippedFiles);

public sealed class ImageCacheStorage(IsolatedArtworkCache artwork, IsolatedThumbnailCache thumbnails, IsolatedHeroFrameCache heroFrames)
{
    public Task<ImageCacheUsage> InspectAsync(ImageCacheKind kind) => ManageAsync(kind, false);
    public Task<ImageCacheUsage> ClearAsync(ImageCacheKind kind) => ManageAsync(kind, true);

    private Task<ImageCacheUsage> ManageAsync(ImageCacheKind kind, bool clear) => kind switch
    {
        ImageCacheKind.OnlineArtwork => artwork.ManageDiskAsync(clear),
        ImageCacheKind.Thumbnails => thumbnails.ManageDiskAsync(clear),
        ImageCacheKind.HeroFrames => heroFrames.ManageDiskAsync(clear),
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    // Each store supplies the directory, name format and write lock it uses for normal cache operations.
    internal static async Task<ImageCacheUsage> WithDirectoryAsync(NativeConfiguration configuration, string directory,
        Func<string, bool> ownsFile, int maximumEntries, SemaphoreSlim gate, bool clear)
    {
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            return await Task.Run(() =>
            {
                var profile = NativeProfileAccess.Validate(configuration);
                var root = Path.Combine(profile, directory);
                var held = IsolatedLibraryPersistence.PinDirectoryChain(profile);
                try
                {
                    NativeProfileAccess.Validate(configuration);
                    FileAttributes attributes;
                    try { attributes = File.GetAttributes(root); }
                    catch (FileNotFoundException) { return default(ImageCacheUsage); }
                    catch (DirectoryNotFoundException) { return default(ImageCacheUsage); }
                    if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != FileAttributes.Directory)
                        throw new DataIsolationException("Cache directory must be an ordinary directory.");
                    held.AddRange(IsolatedLibraryPersistence.PinDirectoryChain(root));
                    NativeProfilePaths.CheckChain(root);
                    return InspectDirectory(root, ownsFile, maximumEntries, clear);
                }
                finally { foreach (var handle in held) handle.Dispose(); }
            }).ConfigureAwait(false);
        }
        finally { gate.Release(); }
    }

    private static ImageCacheUsage InspectDirectory(string root, Func<string, bool> ownsFile, int maximumEntries, bool clear)
    {
        var entries = new DirectoryInfo(root)
            .EnumerateFileSystemInfos("*", new EnumerationOptions { AttributesToSkip = 0, IgnoreInaccessible = false })
            .Take(maximumEntries + 1).ToArray();
        if (entries.Length > maximumEntries) throw new IOException("Cache directory exceeds the inspection limit.");

        long bytes = 0, removedBytes = 0;
        int skipped = 0;
        foreach (var entry in entries)
        {
            if (!ownsFile(entry.Name)) continue;
            if (entry is not FileInfo file || (entry.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                skipped++;
                continue;
            }

            long length = 0;
            try
            {
                length = file.Length;
                if (!clear)
                {
                    bytes += length;
                    continue;
                }
                // Validate that it is a single, ordinary file under the pinned cache directory.
                using (var checkedFile = IsolatedLibrarySource.OpenChecked(file.FullName))
                    length = checkedFile.Length;
                file.Delete();
                removedBytes += length;
            }
            catch (FileNotFoundException) { }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or DataIsolationException)
            {
                bytes += length;
                skipped++;
            }
        }
        return new(bytes, removedBytes, skipped);
    }
}
