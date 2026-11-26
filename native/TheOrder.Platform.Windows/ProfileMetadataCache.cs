using System.Text.Json;
using System.Text.Json.Serialization;
using TheOrder.Application;

namespace TheOrder.Platform.Windows;

// Public metadata caches share directory ownership and replacement rules.
// Each cache supplies its own envelope, identity checks and eviction policy.
internal sealed class ProfileMetadataCache(NativeConfiguration configuration, string directoryName,
    int maximumFiles, long maximumBytes, Func<PersistenceStage, Task>? probe)
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true
    };

    private readonly SemaphoreSlim serial = new(1);

    public async Task<T?> InDirectoryAsync<T>(Func<string, NativeProfileStore, Task<T>> action, bool create,
        CancellationToken cancellation)
    {
        // Path-only test configurations have no profile/store identity to bind.
        if (configuration.Profile is not NativeProfileStore profile) return default;
        await serial.WaitAsync(cancellation).ConfigureAwait(false);
        try
        {
            return await Task.Run(async () =>
            {
                var parent = NativeProfileAccess.Validate(configuration);
                var held = IsolatedLibraryPersistence.PinDirectoryChain(parent);
                try
                {
                    NativeProfileAccess.Validate(configuration);
                    var root = Path.Combine(parent, directoryName);
                    NativeProfilePaths.CheckChain(root);
                    if (!create && !Directory.Exists(root)) return default;
                    if (create) Directory.CreateDirectory(root);
                    var pinned = IsolatedLibraryPersistence.PinDirectoryChain(root);
                    try
                    {
                        NativeProfilePaths.CheckChain(root);
                        cancellation.ThrowIfCancellationRequested();
                        return await action(root, profile).ConfigureAwait(false);
                    }
                    finally { foreach (var handle in pinned) handle.Dispose(); }
                }
                finally { foreach (var handle in held) handle.Dispose(); }
            }, cancellation).ConfigureAwait(false);
        }
        finally { serial.Release(); }
    }

    public async Task<bool> ReplaceAsync(string target, byte[] bytes, Func<bool> canReplace,
        Func<string, bool> canEvict, Action requireCommit, string targetChanged, string evictionChanged)
    {
        // Count all files. Foreign or unreadable envelopes and a flooded directory are preserved.
        var files = new DirectoryInfo(Path.GetDirectoryName(target)!).EnumerateFiles().Take(maximumFiles + 1).ToArray();
        if (files.Length > maximumFiles) return false;
        var prior = files.SingleOrDefault(file => SamePath(file.FullName));
        if (prior is not null && !canReplace()) return false;
        long total = files.Sum(file => file.Length) - (prior?.Length ?? 0) + bytes.Length;
        int count = files.Length + (prior is null ? 1 : 0);

        var remove = new List<string>();
        foreach (var file in files.OrderBy(file => file.LastWriteTimeUtc))
        {
            if (total <= maximumBytes && count <= maximumFiles) break;
            if (SamePath(file.FullName) || !canEvict(file.FullName)) continue;
            remove.Add(file.FullName);
            total -= file.Length;
            count--;
        }
        if (total > maximumBytes || count > maximumFiles) return false;

        requireCommit();
        await WindowsAtomicFile.WriteAsync(target, bytes, async stage =>
        {
            if (probe is not null) await probe(stage).ConfigureAwait(false);
            if (stage is PersistenceStage.BeforeTempWrite or PersistenceStage.BeforeReplace) requireCommit();
            if (stage != PersistenceStage.BeforeReplace) return;
            if (File.Exists(target) && !canReplace()) throw new IOException(targetChanged);
            foreach (var path in remove)
            {
                if (!canEvict(path)) throw new IOException(evictionChanged);
                File.Delete(path);
            }
        }).ConfigureAwait(false);
        return true;

        bool SamePath(string path) => string.Equals(path, target, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsReadFailure(Exception error) => error is IOException or UnauthorizedAccessException or JsonException
        or InvalidDataException or NativeProfileException or DataIsolationException or ArgumentException;
}
