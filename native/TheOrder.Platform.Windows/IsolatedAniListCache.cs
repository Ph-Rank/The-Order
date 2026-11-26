using System.Text.Json;
using TheOrder.Application;

namespace TheOrder.Platform.Windows;

// Disposable public metadata/automatic proofs. Manual v1 bindings are read only
// for migration into the separate Native Profile user-decision component.
public sealed class IsolatedAniListCache(NativeConfiguration configuration,
    Func<PersistenceStage, Task>? persistenceProbe = null) : IAniListCache, IMediaIdentityDecisionOwner
{
    private readonly IsolatedMediaIdentityDecisions decisions = new(configuration, persistenceProbe);
    public IMediaIdentityDecisions IdentityDecisions => decisions;
    public const string DirectoryName = "native-anilist-v1";
    private const int MaximumFiles = 128;
    private const long MaximumBytes = 64L * 1024 * 1024;
    private readonly ProfileMetadataCache storage = new(configuration, DirectoryName, MaximumFiles, MaximumBytes, persistenceProbe);
    public async Task<AniListCacheValue?> ReadAsync(string key, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        if (key is null || key.Length > MediaCacheEnvelope.MaximumKeyLength) return null;
        try { return await storage.InDirectoryAsync((root, profile) => Task.FromResult(ReadVerified(Path.Combine(root, MediaCacheEnvelope.FileName(key)), profile, key)), false, cancellation).ConfigureAwait(false); }
        catch (Exception e) when (ProfileMetadataCache.IsReadFailure(e)) { return null; }
    }
    public async Task WriteAsync(AniListCacheValue value, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        if (!AniListValidation.Cache(value)) throw new InvalidDataException("AniList缓存格式无效。");
        var archivedForEviction = value.Binding is null || value.Binding.AutomaticPolicyVersion > 0;
        if (archivedForEviction)
            await decisions.ArchiveLegacyAsync(MediaIdentityPurpose.AniListMedia, cancellation).ConfigureAwait(false);
        var payload = JsonSerializer.SerializeToUtf8Bytes(value, ProfileMetadataCache.JsonOptions);
        if (payload.Length > MediaCacheEnvelope.MaximumPayload) throw new IOException("AniList缓存超过大小限制。");
        try
        {
            var stored = await storage.InDirectoryAsync(async (root, profile) =>
            {
                var bytes = MediaCacheEnvelope.Encode(profile, value.Key, payload);
                if (bytes.Length > MediaCacheEnvelope.MaximumBytes) return false;
                var target = Path.Combine(root, MediaCacheEnvelope.FileName(value.Key));
                return await storage.ReplaceAsync(target, bytes,
                    () => ReadVerified(target, profile, value.Key) is not null,
                    path => ReadVerified(path, profile) is { } verified && (archivedForEviction || verified.Binding is null),
                    cancellation.ThrowIfCancellationRequested, "AniList缓存身份已改变，保留原文件。", "AniList缓存维护条件已改变。").ConfigureAwait(false);
            }, true, cancellation).ConfigureAwait(false);
            if (!stored) throw new IOException("AniList缓存不可写，未保存此作品关联。");
        }
        catch (Exception e) when (e is NativeProfileException or DataIsolationException)
        { throw new IOException("AniList缓存资料路径不可用。", e); }
    }
    internal static AniListCacheValue? ReadVerified(string path, NativeProfileStore profile, string? expected = null) =>
        MediaCacheEnvelope.ReadVerified<AniListCacheValue>(path, profile, expected, static value => value.Key, AniListValidation.Cache);
}
