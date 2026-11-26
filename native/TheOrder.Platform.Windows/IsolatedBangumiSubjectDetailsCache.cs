using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TheOrder.Application;

namespace TheOrder.Platform.Windows;

// Rebuildable metadata beside the existing Native Profile caches, never a library
// component. A token's one-way scope separates access epochs without storing secrets.
public sealed class IsolatedBangumiSubjectDetailsCache(NativeConfiguration configuration,
    Func<PersistenceStage, Task>? persistenceProbe = null) : IBangumiSubjectDetailsCache
{
    public const string DirectoryName = "native-bangumi-subjects-v1";
    public const int MaximumFiles = 128, MaximumPayloadBytes = 1024 * 1024;
    public const long MaximumBytes = 32 * 1024 * 1024;
    private const int MaximumEnvelopeBytes = 1500000;
    private readonly ProfileMetadataCache storage = new(configuration, DirectoryName, MaximumFiles, MaximumBytes, persistenceProbe);
    private sealed record Envelope(int Version, Guid ProfileId, Guid StoreId, string Scope,
        string SubjectId, BangumiRelationKind Kind, string Hash, byte[] Payload);
    private static bool Identity(string scope, string id, BangumiRelationKind kind)
        => BangumiAccessContext.ValidScope(scope) && BangumiRelationIdentityResolver.ValidSubjectId(id) && Enum.IsDefined(kind);
    private static string Key(string scope, string id, BangumiRelationKind kind)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(scope + ":" + id + ":" + (int)kind)));

    public async Task<BangumiSubjectDetails?> ReadAsync(string scope, string subjectId, BangumiRelationKind kind, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        if (!Identity(scope, subjectId, kind)) return null;
        try { return await storage.InDirectoryAsync((root, profile) => Task.FromResult(ReadVerified(Path.Combine(root, Key(scope, subjectId, kind) + ".json"), profile, scope, subjectId, kind)), false, cancellation).ConfigureAwait(false); }
        catch (Exception e) when (ProfileMetadataCache.IsReadFailure(e)) { return null; }
    }

    public async Task WriteAsync(string scope, BangumiSubjectDetails value, Func<bool> canCommit, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        if (!Identity(scope, value.Id, value.Kind) || !BangumiSubjectDetailsService.Valid(value.Id, value.Kind, value)) throw new InvalidDataException("Bangumi 作品缓存无效。");
        var payload = JsonSerializer.SerializeToUtf8Bytes(value, ProfileMetadataCache.JsonOptions);
        if (payload.Length > MaximumPayloadBytes) throw new IOException("作品缓存超过限制。");
        try
        {
            var stored = await storage.InDirectoryAsync(async (root, profile) =>
            {
                var bytes = JsonSerializer.SerializeToUtf8Bytes(new Envelope(1, profile.Manifest.ProfileId, profile.Manifest.StoreId,
                    scope, value.Id, value.Kind, Convert.ToHexString(SHA256.HashData(payload)), payload), ProfileMetadataCache.JsonOptions);
                if (bytes.Length > MaximumEnvelopeBytes) return false;
                var target = Path.Combine(root, Key(scope, value.Id, value.Kind) + ".json");
                return await storage.ReplaceAsync(target, bytes,
                    () => ReadVerified(target, profile, scope, value.Id, value.Kind) is not null,
                    path => ReadVerified(path, profile) is not null,
                    RequireCommit, "作品缓存身份已改变，已保留原文件。", "作品缓存维护条件已改变。").ConfigureAwait(false);
            }, true, cancellation).ConfigureAwait(false);
            if (!stored) throw new IOException("作品离线缓存不可写。");
        }
        catch (Exception e) when (e is NativeProfileException or DataIsolationException)
        { throw new IOException("作品缓存路径不可用。", e); }
        void RequireCommit() { cancellation.ThrowIfCancellationRequested(); if (!canCommit()) throw new OperationCanceledException("作品缓存请求已更新。"); }
    }

    public async Task RemoveAsync(string scope, string subjectId, BangumiRelationKind kind, Func<bool> canCommit, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        if (!Identity(scope, subjectId, kind)) return;
        try
        {
            await storage.InDirectoryAsync((root, profile) =>
            {
                var path = Path.Combine(root, Key(scope, subjectId, kind) + ".json");
                cancellation.ThrowIfCancellationRequested();
                if (canCommit() && ReadVerified(path, profile, scope, subjectId, kind) is not null) File.Delete(path);
                return Task.FromResult(true);
            }, false, cancellation).ConfigureAwait(false);
        }
        catch (Exception e) when (e is NativeProfileException or DataIsolationException)
        { throw new IOException("作品缓存路径不可用。", e); }
    }

    private static BangumiSubjectDetails? ReadVerified(string path, NativeProfileStore profile, string? scope = null, string? subject = null, BangumiRelationKind? kind = null)
    {
        try
        {
            using var stream = IsolatedLibrarySource.OpenChecked(path);
            if (stream.Length is < 1 or > MaximumEnvelopeBytes) return null;
            var envelope = JsonSerializer.Deserialize<Envelope>(stream, ProfileMetadataCache.JsonOptions);
            if (envelope is null || envelope.Version != 1 || envelope.ProfileId != profile.Manifest.ProfileId || envelope.StoreId != profile.Manifest.StoreId
                || !Identity(envelope.Scope, envelope.SubjectId, envelope.Kind) || scope is not null && scope != envelope.Scope
                || subject is not null && subject != envelope.SubjectId || kind is not null && kind != envelope.Kind
                || Path.GetFileName(path) != Key(envelope.Scope, envelope.SubjectId, envelope.Kind) + ".json"
                || envelope.Payload is not { Length: > 0 and <= MaximumPayloadBytes }
                || envelope.Hash != Convert.ToHexString(SHA256.HashData(envelope.Payload))) return null;
            var value = JsonSerializer.Deserialize<BangumiSubjectDetails>(envelope.Payload, ProfileMetadataCache.JsonOptions);
            return BangumiSubjectDetailsService.Valid(envelope.SubjectId, envelope.Kind, value) ? value : null;
        }
        catch (Exception e) when (ProfileMetadataCache.IsReadFailure(e)) { return null; }
    }
}
