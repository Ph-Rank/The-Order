using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using TheOrder.Application;

namespace TheOrder.Platform.Windows;

// Disposable, anonymous public relations. This directory is not a library component
// and can never supply identification evidence or modify the user's library.
public sealed partial class IsolatedBangumiRelationsCache(NativeConfiguration configuration,
    Func<PersistenceStage, Task>? persistenceProbe = null) : IBangumiRelationsCache
{
    public const string DirectoryName = "native-bangumi-relations-v1";
    public const int MaximumFiles = 128, MaximumPayloadBytes = 1024 * 1024;
    public const long MaximumBytes = 32 * 1024 * 1024;
    private const int MaximumEnvelopeBytes = 1500000;
    private readonly ProfileMetadataCache storage = new(configuration, DirectoryName, MaximumFiles, MaximumBytes, persistenceProbe);
    private sealed record StoredIdentity(string LocalGameId, string NextMoeId, string SubjectId, string Fingerprint)
    {
        public string AccessScope { get; init; } = "anonymous";
        public BangumiRelationIdentity Current() => new(LocalGameId, "cache-validation", NextMoeId, SubjectId, Fingerprint) { AccessScope = AccessScope };
        public static StoredIdentity From(BangumiRelationIdentity value) => new(value.LocalGameId, value.NextMoeId, value.SubjectId, value.Fingerprint) { AccessScope = value.AccessScope };
    }
    private sealed record Envelope(int Version, Guid ProfileId, Guid StoreId, StoredIdentity Identity, string Hash, byte[] Payload);
    private sealed record Verified(Envelope Envelope, BangumiRelationsSnapshot Snapshot);

    public async Task<BangumiRelationsSnapshot?> ReadAsync(BangumiRelationIdentity identity, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        if (!BangumiRelationsService.ValidIdentity(identity)) return null;
        try
        {
            return await storage.InDirectoryAsync((root, profile) => Task.FromResult(
                ReadVerified(Path.Combine(root, identity.CacheKey + ".json"), profile) is { } found
                    && found.Envelope.Identity == StoredIdentity.From(identity)
                    && BangumiRelationsService.Valid(identity, found.Snapshot) ? found.Snapshot : null), false, cancellation).ConfigureAwait(false);
        }
        catch (Exception e) when (ProfileMetadataCache.IsReadFailure(e)) { return null; }
    }

    public async Task WriteAsync(BangumiRelationIdentity identity, BangumiRelationsSnapshot snapshot, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        if (!BangumiRelationsService.Valid(identity, snapshot)) return;
        var payload = JsonSerializer.SerializeToUtf8Bytes(snapshot, ProfileMetadataCache.JsonOptions);
        if (payload.Length > MaximumPayloadBytes) return;
        try
        {
            await storage.InDirectoryAsync(async (root, profile) =>
            {
                var envelope = new Envelope(BangumiRelationsService.SchemaVersion, profile.Manifest.ProfileId,
                    profile.Manifest.StoreId, StoredIdentity.From(identity), Convert.ToHexString(SHA256.HashData(payload)), payload);
                var bytes = JsonSerializer.SerializeToUtf8Bytes(envelope, ProfileMetadataCache.JsonOptions);
                if (bytes.Length > MaximumEnvelopeBytes) return false;
                string target = Path.Combine(root, identity.CacheKey + ".json");
                return await storage.ReplaceAsync(target, bytes,
                    () => ReadVerified(target, profile) is not null,
                    path => ReadVerified(path, profile) is not null,
                    cancellation.ThrowIfCancellationRequested, "关联缓存身份已改变，已保留原文件。", "关联缓存维护条件已改变。").ConfigureAwait(false);
            }, true, cancellation).ConfigureAwait(false);
        }
        catch (Exception e) when (e is NativeProfileException or DataIsolationException)
        { throw new IOException("关联缓存资料路径不可用。", e); }
    }

    private static Verified? ReadVerified(string path, NativeProfileStore profile)
    {
        if (!KeyPattern().IsMatch(Path.GetFileNameWithoutExtension(path)) || Path.GetExtension(path) != ".json") return null;
        try
        {
            using var file = IsolatedLibrarySource.OpenChecked(path);
            if (file.Length is < 1 or > MaximumEnvelopeBytes) return null;
            var value = JsonSerializer.Deserialize<Envelope>(file, ProfileMetadataCache.JsonOptions);
            if (value is null || value.Version != BangumiRelationsService.SchemaVersion || value.ProfileId != profile.Manifest.ProfileId
                || value.StoreId != profile.Manifest.StoreId || value.Identity is null || value.Payload is null
                || value.Identity.LocalGameId is null || value.Identity.NextMoeId is null || value.Identity.SubjectId is null || value.Identity.Fingerprint is null
                || value.Payload.Length is < 1 or > MaximumPayloadBytes || value.Hash is null
                || Convert.ToHexString(SHA256.HashData(value.Payload)) != value.Hash) return null;
            var identity = value.Identity.Current();
            if (!BangumiRelationsService.ValidIdentity(identity) || identity.CacheKey != Path.GetFileNameWithoutExtension(path)) return null;
            var snapshot = JsonSerializer.Deserialize<BangumiRelationsSnapshot>(value.Payload, ProfileMetadataCache.JsonOptions);
            return BangumiRelationsService.Valid(identity, snapshot) ? new(value, snapshot!) : null;
        }
        catch (Exception e) when (ProfileMetadataCache.IsReadFailure(e)) { return null; }
    }

    [GeneratedRegex("^[A-F0-9]{64}$", RegexOptions.CultureInvariant)] private static partial Regex KeyPattern();
}
