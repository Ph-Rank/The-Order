using System.Text.Json;
using System.Text.Json.Serialization;
using TheOrder.Application;

namespace TheOrder.Platform.Windows;

// Optional settings borrow the profile lease. This queue owns only one settings file.
internal sealed class ProfilePreferencesFile(NativeConfiguration configuration, string fileName, Type owner,
    string profileRequiredMessage, Func<PersistenceStage, Task>? probe) : IAsyncDisposable
{
    public const int MaximumBytes = 4096;
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true
    };

    private readonly SemaphoreSlim queue = new(1, 1);
    private bool disposed;

    public Task<TRead> LoadAsync<TRead>(TRead fallback, Func<byte[], NativeProfileStore, TRead> decode,
        CancellationToken cancellation) => RunAsync((target, profile) =>
        Task.FromResult(ReadBytes(target) is { } bytes ? decode(bytes, profile) : fallback), cancellation);

    public Task SaveAsync<TEnvelope, TRead>(Func<NativeProfileStore, TEnvelope> encode,
        Func<byte[], NativeProfileStore, TRead> decode, Func<TRead, (string? Warning, bool ReadOnly)> status,
        CancellationToken cancellation) => RunAsync(async (target, profile) =>
    {
        if (ReadBytes(target) is { } previous)
        {
            var (warning, readOnly) = status(decode(previous, profile));
            if (readOnly) throw new InvalidDataException(warning);
            if (warning is null)
                await WindowsAtomicFile.WriteAsync(target + ".bak", previous).ConfigureAwait(false);
        }

        var bytes = JsonSerializer.SerializeToUtf8Bytes(encode(profile));
        // Once admitted, the atomic write completes even if its caller cancels.
        await WindowsAtomicFile.WriteAsync(target, bytes, probe).ConfigureAwait(false);
        return true;
    }, cancellation);

    public static HashSet<string> ReadUniqueNames(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object) throw new JsonException();
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in root.EnumerateObject())
            if (!names.Add(property.Name)) throw new JsonException();
        return names;
    }

    public static bool IsVersionOne(JsonElement root) =>
        root.TryGetProperty("Version", out var version) && version.TryGetInt32(out var number) && number == 1;

    private static byte[]? ReadBytes(string target)
    {
        try
        {
            using var file = IsolatedLibrarySource.OpenChecked(target);
            if (file.Length > MaximumBytes) return new byte[MaximumBytes + 1];
            var bytes = new byte[checked((int)file.Length)];
            file.ReadExactly(bytes);
            return bytes;
        }
        catch (FileNotFoundException) { return null; }
    }

    private async Task<T> RunAsync<T>(Func<string, NativeProfileStore, Task<T>> action, CancellationToken cancellation)
    {
        await queue.WaitAsync(cancellation).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, owner);
            if (configuration.Profile is not NativeProfileStore profile) throw new InvalidOperationException(profileRequiredMessage);
            return await Task.Run(async () =>
            {
                var root = NativeProfileAccess.Validate(configuration);
                var held = IsolatedLibraryPersistence.PinDirectoryChain(root);
                try
                {
                    profile.ValidateAccess();
                    return await action(Path.Combine(profile.Root, fileName), profile).ConfigureAwait(false);
                }
                finally { foreach (var handle in held) handle.Dispose(); }
            }, CancellationToken.None).ConfigureAwait(false);
        }
        finally { queue.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        await queue.WaitAsync().ConfigureAwait(false);
        try { disposed = true; }
        finally { queue.Release(); }
    }
}
