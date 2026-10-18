using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TheOrder.Application;

namespace TheOrder.Platform.Windows;

// Rebuildable metadata uses the existing profile ownership, directory pins and
// atomic writer. Identity decisions remain in IsolatedMediaIdentityDecisions.
public sealed class IsolatedGameVideoCache(NativeConfiguration configuration) : IGameVideoCache
{
    public const string DirectoryName = "native-game-videos-v1";
    private readonly SemaphoreSlim gate = new(1);
    private sealed record Envelope(int Version, Guid ProfileId, Guid StoreId, string Key, string Hash, byte[] Payload);
    private static string Name(string key) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))) + ".json";
    private static bool Key(string key) => key.StartsWith("igdb-v1:", StringComparison.Ordinal) && key.Length == 72 && key[8..].All(char.IsAsciiHexDigit);
    public Task<IgdbResult?> ReadAsync(string key, CancellationToken cancellation) => Run(key, async (root, profile) =>
    { await Task.CompletedTask; return Read(Path.Combine(root, Name(key)), profile, key); }, cancellation);
    public async Task WriteAsync(string key, IgdbResult value, CancellationToken cancellation)
    {
        if (!GameVideos.Valid(value)) throw new InvalidDataException();
        await Run(key, async (root, profile) =>
        {
            if (Directory.EnumerateFiles(root, "*.json").Take(129).Count() > 128) throw new IOException("缓存维护边界已改变。");
            var payload = JsonSerializer.SerializeToUtf8Bytes(value); if (payload.Length > 262144) throw new IOException("游戏影像缓存超出边界。");
            var bytes = JsonSerializer.SerializeToUtf8Bytes(new Envelope(1, profile.Manifest.ProfileId, profile.Manifest.StoreId, key, Convert.ToHexString(SHA256.HashData(payload)), payload));
            var path = Path.Combine(root, Name(key));
            await WindowsAtomicFile.WriteAsync(path, bytes, stage => { if (stage is PersistenceStage.BeforeTempWrite or PersistenceStage.BeforeReplace) cancellation.ThrowIfCancellationRequested(); return Task.CompletedTask; }).ConfigureAwait(false);
            var files = new DirectoryInfo(root).EnumerateFiles("*.json").Take(129).ToArray();
            if (files.Length > 128) throw new IOException("缓存维护边界已改变。");
            foreach (var file in files.Where(x => x.FullName != path).OrderBy(x => x.LastWriteTimeUtc).ThenBy(x => x.Name, StringComparer.Ordinal).Take(Math.Max(0, files.Length - 64)))
                if (Read(file.FullName, profile, null) is not null) File.Delete(file.FullName);
            return value;
        }, cancellation).ConfigureAwait(false);
    }
    private static IgdbResult? Read(string path, NativeProfileStore profile, string? key)
    {
        try
        {
            using var stream = IsolatedLibrarySource.OpenChecked(path); if (stream.Length is < 1 or > 400000) return null;
            var envelope = JsonSerializer.Deserialize<Envelope>(stream);
            if (envelope is null || envelope.Version != 1 || envelope.ProfileId != profile.Manifest.ProfileId || envelope.StoreId != profile.Manifest.StoreId || !Key(envelope.Key)
                || key is not null && key != envelope.Key || Path.GetFileName(path) != Name(envelope.Key) || envelope.Payload is not { Length: > 0 and <= 262144 }
                || envelope.Hash != Convert.ToHexString(SHA256.HashData(envelope.Payload))) return null;
            var value = JsonSerializer.Deserialize<IgdbResult>(envelope.Payload); return GameVideos.Valid(value) ? value : null;
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException or InvalidDataException) { return null; }
    }
    private async Task<IgdbResult?> Run(string key, Func<string, NativeProfileStore, Task<IgdbResult?>> action, CancellationToken cancellation)
    {
        if (!Key(key) || configuration.Profile is not NativeProfileStore profile) return null;
        await gate.WaitAsync(cancellation).ConfigureAwait(false);
        try
        {
            profile.ValidateAccess();
            return await Task.Run(async () =>
            {
                var root = Path.Combine(profile.Root, DirectoryName); NativeProfilePaths.CheckChain(root);
                var held = IsolatedLibraryPersistence.PinDirectoryChain(profile.Root);
                try { Directory.CreateDirectory(root); var pinned = IsolatedLibraryPersistence.PinDirectoryChain(root);
                    try { profile.ValidateAccess(); return await action(root, profile).ConfigureAwait(false); }
                    finally { foreach (var handle in pinned) handle.Dispose(); }
                } finally { foreach (var handle in held) handle.Dispose(); }
            }, cancellation).ConfigureAwait(false);
        }
        finally { gate.Release(); }
    }
}
