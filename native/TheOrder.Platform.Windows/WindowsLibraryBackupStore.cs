using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using TheOrder.Application;

namespace TheOrder.Platform.Windows;

// Optional companion under the existing profile lease. Portable exports contain library
// records only: never credentials, OAuth tokens, caches or executable/game contents.
public sealed class WindowsLibraryBackupStore(NativeConfiguration configuration, ILibraryCodec codec,
    TimeProvider? timeProvider = null, Func<PersistenceStage, Task>? probe = null) : ILibraryBackupStore
{
    public const string DirectoryName = "native-library-backups-v1";
    public const int Retention = 10;
    public const int MaximumDocumentBytes = 32 * 1024 * 1024;
    private const int MaximumEnvelopeBytes = 45 * 1024 * 1024;
    private const long MaximumDirectoryBytes = 512L * 1024 * 1024;
    private readonly SemaphoreSlim queue = new(1, 1);
    private sealed record Preferences(int Version, Guid ProfileId, Guid StoreId, bool Automatic);
    private sealed record Snapshot(int Version, Guid ProfileId, Guid StoreId, string Id, DateTimeOffset CreatedAt,
        string Reason, int Entries, string Digest, string Checksum, byte[] Document);
    private static readonly JsonSerializerOptions Options = new()
    { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, RespectRequiredConstructorParameters = true };
    private static string SnapshotFile(string id)
    {
        if (!Guid.TryParseExact(id, "N", out _)) throw new InvalidDataException("备份编号无效。");
        return id + ".snapshot.json";
    }
    private async Task<T> Run<T>(Func<string, NativeProfileStore, Task<T>> action, CancellationToken cancellation)
    {
        await queue.WaitAsync(cancellation).ConfigureAwait(false);
        try
        {
            if (configuration.Profile is not NativeProfileStore profile) throw new DataIsolationException("Native profile required for backups");
            return await Task.Run(async () =>
            {
                cancellation.ThrowIfCancellationRequested();
                var root = NativeProfileAccess.Validate(configuration);
                var held = IsolatedLibraryPersistence.PinDirectoryChain(root);
                try
                {
                    var directory = Path.Combine(root, DirectoryName);
                    NativeProfilePaths.CheckChain(directory); Directory.CreateDirectory(directory);
                    held.AddRange(IsolatedLibraryPersistence.PinDirectoryChain(directory));
                    profile.ValidateAccess(); NativeProfilePaths.CheckChain(directory);
                    return await action(directory, profile).ConfigureAwait(false);
                }
                finally { foreach (var handle in held) handle.Dispose(); }
            }, cancellation).ConfigureAwait(false);
        }
        finally { queue.Release(); }
    }
    private static byte[] ReadBounded(string path, int maximum)
    {
        using var stream = IsolatedLibrarySource.OpenChecked(path);
        if (stream.Length < 1 || stream.Length > maximum) throw new InvalidDataException("备份文件过大或为空，原文件保留。");
        var bytes = new byte[checked((int)stream.Length)]; stream.ReadExactly(bytes); return bytes;
    }
    private static T Decode<T>(byte[] bytes)
    {
        try
        {
            using var json = JsonDocument.Parse(bytes);
            if (json.RootElement.ValueKind != JsonValueKind.Object) throw new JsonException();
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var p in json.RootElement.EnumerateObject()) if (!names.Add(p.Name)) throw new JsonException();
            return JsonSerializer.Deserialize<T>(bytes, Options) ?? throw new JsonException();
        }
        catch (JsonException e) { throw new InvalidDataException("备份文件损坏或格式不受支持，原文件保留。", e); }
    }
    private static void Identity(int version, Guid profileId, Guid storeId, NativeProfileStore profile)
    {
        if (version != 1 || profileId != profile.Manifest.ProfileId || storeId != profile.Manifest.StoreId)
            throw new InvalidDataException("备份目录格式不受支持或不属于当前资料，原文件保留。");
    }
    private Preferences ReadPreferences(string root, NativeProfileStore profile)
    {
        try
        {
            var p = Decode<Preferences>(ReadBounded(Path.Combine(root, "settings.json"), 4096));
            Identity(p.Version, p.ProfileId, p.StoreId, profile); return p;
        }
        catch (FileNotFoundException) { return new(1, profile.Manifest.ProfileId, profile.Manifest.StoreId, true); }
    }
    private Snapshot ReadSnapshot(string root, string id, NativeProfileStore profile)
    {
        var value = Decode<Snapshot>(ReadBounded(Path.Combine(root, SnapshotFile(id)), MaximumEnvelopeBytes));
        Identity(value.Version, value.ProfileId, value.StoreId, profile);
        if (value.Id != id || value.Document is null || value.Document.Length is < 1 or > MaximumDocumentBytes || value.Reason is not ("自动" or "恢复前" or "导入前")
            || value.CreatedAt.Offset != TimeSpan.Zero || value.CreatedAt.Year < 2000
            || value.Checksum != Convert.ToHexString(SHA256.HashData(value.Document))
            || codec.Decode(value.Document).Length != value.Entries
            || value.Digest != LibraryBackupService.ContentDigest(codec, value.Document))
            throw new InvalidDataException("备份校验失败，原文件保留。");
        return value;
    }
    private ImmutableArray<LibraryBackupRecord> Records(string root, NativeProfileStore profile)
    {
        var files = new DirectoryInfo(root).EnumerateFiles("*.snapshot.json").Take(101).ToArray();
        if (files.Length > 100) throw new InvalidDataException("备份目录中的文件过多，已停止自动维护。");
        return [.. files.Select(f =>
        {
            var value = ReadSnapshot(root, f.Name[..^".snapshot.json".Length], profile);
            return Record(value);
        }).OrderByDescending(s => s.CreatedAt).ThenByDescending(s => s.Id, StringComparer.Ordinal)];
    }
    private static LibraryBackupRecord Record(Snapshot value) => new(value.Id, value.CreatedAt, value.Reason, value.Entries, value.Digest);
    public Task<LibraryBackupState> ReadStateAsync(CancellationToken cancellation) => Run((root, profile) =>
        Task.FromResult(new LibraryBackupState(new(ReadPreferences(root, profile).Automatic), Records(root, profile))), cancellation);
    public Task<byte[]> ReadSnapshotAsync(string id, CancellationToken cancellation) => Run((root, profile) =>
        Task.FromResult(ReadSnapshot(root, id, profile).Document), cancellation);
    public Task SaveSettingsAsync(LibraryBackupSettings settings, CancellationToken cancellation) => Run(async (root, profile) =>
    {
        _ = ReadPreferences(root, profile);
        cancellation.ThrowIfCancellationRequested();
        var p = new Preferences(1, profile.Manifest.ProfileId, profile.Manifest.StoreId, settings.Automatic);
        await WindowsAtomicFile.WriteAsync(Path.Combine(root, "settings.json"), JsonSerializer.SerializeToUtf8Bytes(p, Options), probe).ConfigureAwait(false);
        return true;
    }, cancellation);
    public Task<LibraryBackupWrite> SaveSnapshotAsync(ReadOnlyMemory<byte> document, int entries, string reason, CancellationToken cancellation)
    {
        var bytes = document.ToArray();
        if (bytes.Length is < 1 or > MaximumDocumentBytes || reason is not ("自动" or "恢复前" or "导入前")) throw new InvalidDataException("备份内容无效。");
        return Run(async (root, profile) =>
        {
            _ = ReadPreferences(root, profile); _ = Records(root, profile);
            if (codec.Decode(bytes).Length != entries) throw new InvalidDataException("备份条目数不一致。");
            var value = new Snapshot(1, profile.Manifest.ProfileId, profile.Manifest.StoreId, Guid.NewGuid().ToString("N"),
                (timeProvider ?? TimeProvider.System).GetUtcNow().ToUniversalTime(), reason, entries,
                LibraryBackupService.ContentDigest(codec, bytes), Convert.ToHexString(SHA256.HashData(bytes)), bytes);
            var encoded = JsonSerializer.SerializeToUtf8Bytes(value, Options);
            var existing = new DirectoryInfo(root).EnumerateFiles().Take(257).ToArray();
            if (existing.Length > 256 || existing.Sum(f => f.Length) + encoded.Length > MaximumDirectoryBytes)
                throw new InvalidDataException("自动备份空间已满，现有备份已保留。");
            cancellation.ThrowIfCancellationRequested();
            var receipt = await WindowsAtomicFile.WriteAsync(Path.Combine(root, SnapshotFile(value.Id)), encoded, probe).ConfigureAwait(false);
            _ = ReadSnapshot(root, value.Id, profile); // Never prune until the completed new snapshot passes read-back validation.
            string? warning = receipt.Warning;
            foreach (var previous in Records(root, profile).Where(r => r.Id != value.Id).Skip(Retention - 1))
            {
                try { _ = ReadSnapshot(root, previous.Id, profile); File.Delete(Path.Combine(root, SnapshotFile(previous.Id))); }
                catch (IOException) { warning = "备份已保存，旧备份清理未完成。"; }
                catch (UnauthorizedAccessException) { warning = "备份已保存，旧备份清理未完成。"; }
            }
            return new LibraryBackupWrite(Record(value), warning);
        }, cancellation);
    }
    private string ExternalPath(string path)
    {
        if (!Path.IsPathFullyQualified(path) || Path.GetExtension(path).ToLowerInvariant() != ".json")
            throw new InvalidDataException("请选择 JSON 书架备份文件。");
        var target = Path.GetFullPath(path); var root = NativeProfileAccess.Validate(configuration);
        if (target.StartsWith(root.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("请将备份保存到资料目录以外的位置；自动备份请从列表恢复。");
        NativeProfilePaths.CheckChain(Path.GetDirectoryName(target)!); return target;
    }
    public Task<byte[]> ReadExternalAsync(string path, CancellationToken cancellation) => Task.Run(() =>
    { cancellation.ThrowIfCancellationRequested(); return ReadBounded(ExternalPath(path), MaximumDocumentBytes); }, cancellation);
    public Task<string?> WriteExternalAsync(string path, ReadOnlyMemory<byte> document, CancellationToken cancellation) => Task.Run(async () =>
    {
        var target = ExternalPath(path); var bytes = document.ToArray();
        if (bytes.Length is < 1 or > MaximumDocumentBytes) throw new InvalidDataException("书架备份过大或为空。");
        _ = codec.Decode(bytes);
        var held = IsolatedLibraryPersistence.PinDirectoryChain(Path.GetDirectoryName(target)!);
        try
        {
            cancellation.ThrowIfCancellationRequested();
            var receipt = await WindowsAtomicFile.WriteAsync(target, bytes, probe).ConfigureAwait(false);
            if (!ReadBounded(target, MaximumDocumentBytes).AsSpan().SequenceEqual(bytes)) throw new InvalidDataException("备份写入后校验失败。");
            return receipt.Warning;
        }
        finally { foreach (var handle in held) handle.Dispose(); }
    }, cancellation);
}
