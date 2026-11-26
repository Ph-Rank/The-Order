using System.Text.Json;
using TheOrder.Application;

namespace TheOrder.Platform.Windows;

// Public display data and user-confirmed edition choices live outside library.json. The existing
// profile identity/path pins/atomic writer protect this disposable cache exactly like relations.
public sealed class IsolatedMusicBrainzCache(NativeConfiguration configuration,
    Func<PersistenceStage, Task>? persistenceProbe = null) : IMusicBrainzCache, IMediaIdentityDecisionOwner
{
    private readonly IsolatedMediaIdentityDecisions decisions = new(configuration, persistenceProbe);
    public IMediaIdentityDecisions IdentityDecisions => decisions;
    public const string DirectoryName = "native-musicbrainz-v1";
    private const int MaximumFiles = 128;
    private const long MaximumBytes = 64L * 1024 * 1024;
    private readonly ProfileMetadataCache storage = new(configuration, DirectoryName, MaximumFiles, MaximumBytes, persistenceProbe);
    public async Task<MusicBrainzCacheValue?> ReadAsync(string key, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        if (key is null || key.Length > MediaCacheEnvelope.MaximumKeyLength) return null;
        try { return await storage.InDirectoryAsync((root, profile) => Task.FromResult(ReadVerified(Path.Combine(root, MediaCacheEnvelope.FileName(key)), profile, key)), false, cancellation).ConfigureAwait(false); }
        catch (Exception e) when (ProfileMetadataCache.IsReadFailure(e)) { return null; }
    }
    public async Task WriteAsync(MusicBrainzCacheValue value, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        if (!MusicBrainzValidation.Cache(value)) throw new InvalidDataException("音乐缓存格式无效。");
        if (value.Binding is null)
            await decisions.ArchiveLegacyAsync(MediaIdentityPurpose.MusicRelease, cancellation).ConfigureAwait(false);
        var payload = JsonSerializer.SerializeToUtf8Bytes(value, ProfileMetadataCache.JsonOptions);
        if (payload.Length > MediaCacheEnvelope.MaximumPayload) throw new IOException("音乐缓存超过大小限制。");
        try
        {
            var stored = await storage.InDirectoryAsync(async (root, profile) =>
            {
                var bytes = MediaCacheEnvelope.Encode(profile, value.Key, payload);
                if (bytes.Length > MediaCacheEnvelope.MaximumBytes) return false;
                var target = Path.Combine(root, MediaCacheEnvelope.FileName(value.Key));
                return await storage.ReplaceAsync(target, bytes,
                    () => ReadVerified(target, profile, value.Key) is not null,
                    path => ReadVerified(path, profile) is { } verified && (value.Binding is null || verified.Binding is null),
                    cancellation.ThrowIfCancellationRequested, "音乐缓存身份已改变，保留原文件。", "音乐缓存维护条件已改变。").ConfigureAwait(false);
            }, true, cancellation).ConfigureAwait(false);
            if (!stored) throw new IOException("音乐缓存不可写，未保存此发行版选择。");
        }
        catch (Exception e) when (e is NativeProfileException or DataIsolationException)
        { throw new IOException("音乐缓存资料路径不可用。", e); }
    }
    internal static MusicBrainzCacheValue? ReadVerified(string path, NativeProfileStore profile, string? expected = null) =>
        MediaCacheEnvelope.ReadVerified<MusicBrainzCacheValue>(path, profile, expected, static value => value.Key, MusicBrainzValidation.Cache);
}
