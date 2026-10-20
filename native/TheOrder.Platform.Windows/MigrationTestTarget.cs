using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using TheOrder.Application;
using TheOrder.Application.Migrations;
using TheOrder.Domain;

namespace TheOrder.Platform.Windows;

// Test-only gateway: no environment fallback, no Production switch, no profile cloning.
public static class MigrationTestTarget
{
    public const string MarkerName = ".the-order-migration-test.json";
    private sealed record Marker(int Schema, string Purpose, Guid ProfileId, Guid StoreId);
    private static readonly JsonSerializerOptions Options = new() { Converters = { new JsonStringEnumConverter() }, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    public static void Create(string explicitRoot, ILibraryCodec codec)
    {
        var root = NativeProfilePaths.Validate(new(NativeProfileKind.Test, explicitRoot));
        if (Directory.Exists(root) && Directory.EnumerateFileSystemEntries(root).Any()) throw new NativeProfileException("MigrationRequiresFreshTestDirectory");
        using var profile = NativeProfileStore.Open(new(NativeProfileKind.Test, root), codec);
        var marker = new Marker(1, "the-order-migration-synthetic-only-v1", profile.Manifest.ProfileId, profile.Manifest.StoreId);
        using var file = new FileStream(Path.Combine(root, MarkerName), FileMode.CreateNew, FileAccess.Write, FileShare.None);
        JsonSerializer.Serialize(file, marker, Options); file.Flush(true);
    }
    private static NativeProfileRequest Validate(string explicitRoot)
    {
        var root = NativeProfilePaths.Validate(new(NativeProfileKind.Test, explicitRoot));
        var marker = JsonSerializer.Deserialize<Marker>(ReadPlain(Path.Combine(root, MarkerName), 4096), Options) ?? throw new NativeProfileException("MissingMigrationMarker");
        var manifest = JsonSerializer.Deserialize<NativeProfileManifest>(ReadPlain(Path.Combine(root, NativeProfileStore.ManifestName), 16384), Options) ?? throw new NativeProfileException("MissingManifest");
        if (marker.Schema != 1 || marker.Purpose != "the-order-migration-synthetic-only-v1" || marker.ProfileId == Guid.Empty || marker.StoreId == Guid.Empty
            || manifest.ProfileKind != NativeProfileKind.Test || manifest.State != "Ready" || marker.ProfileId != manifest.ProfileId || marker.StoreId != manifest.StoreId)
            throw new NativeProfileException("MigrationTestOnly");
        return new(NativeProfileKind.Test, root, ExpectedProfileId: marker.ProfileId, ExpectedStoreId: marker.StoreId);
    }
    public static LibraryDocument Preview(string explicitRoot, ILibraryCodec codec)
        => NativeProfileStore.ReadMigrationTestPreview(Validate(explicitRoot), codec);
    public static MigrationPlan PreviewPlan(string explicitRoot, ImportDataset dataset, ILibraryCodec codec, CancellationToken cancellation = default)
    {
        var request = Validate(explicitRoot); var document = NativeProfileStore.ReadMigrationTestPreview(request, codec);
        if (document.Access != LibraryAccess.ValidWritable) throw new InvalidDataException("Migration target is not writable.");
        return MigrationResolver.Preview(dataset, document.Entries, codec, cancellation, Scope(request));
    }
    private static string Scope(NativeProfileRequest request) => ImportHash.Of(new { request.ExpectedProfileId, request.ExpectedStoreId });
    public static async Task<CommitResult<MigrationCommit>> ApplyAsync(string explicitRoot, ImportDataset dataset, string expectedPlanId,
        ILibraryCodec codec, CancellationToken cancellation = default, IPersistenceProbe? probe = null)
    {
        if (!MigrationLineage.Digest(expectedPlanId)) throw new InvalidDataException("EXPLICIT_COMMIT_PLAN_REQUIRED");
        cancellation.ThrowIfCancellationRequested();
        var request = Validate(explicitRoot);
        using var profile = NativeProfileStore.Open(request, codec, persistenceProbe: probe);
        await using var library = TransactionalLibrary.Open(codec, profile.CreateLibraryPersistence());
        var plan = MigrationResolver.Preview(dataset, library.Snapshot.Entries, codec, cancellation, Scope(request));
        if (plan.Id != expectedPlanId) throw new StaleLibraryRequestException();
        return await library.ApplyMigrationAsync(plan, cancellation).ConfigureAwait(false);
    }
    public static async Task<ImportDataset> ReadSourceAsync(string explicitFile, ILibraryMigrationAdapter adapter, CancellationToken cancellation = default)
    {
        var path = Path.GetFullPath(explicitFile);
        if (!path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) && !Path.GetFileName(path).Equals("data.galgames.json", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("ONLY_EXPLICIT_MANAGER_BACKUPS_SUPPORTED");
        var parent = Path.GetDirectoryName(path) ?? throw new InvalidDataException("SOURCE_DIRECTORY_REQUIRED");
        // Same lexical namespace and physical-link protections as Native Test profiles, without creating one.
        NativeProfilePaths.Validate(new(NativeProfileKind.Test, parent));
        var held = IsolatedLibraryPersistence.PinDirectoryChain(parent);
        try
        {
            await using var file = IsolatedLibrarySource.OpenChecked(path);
            if (file.Length > 512L * 1024 * 1024) throw new InvalidDataException("SOURCE_TOO_LARGE");
            var hash = Convert.ToHexStringLower(await SHA256.HashDataAsync(file, cancellation)); file.Position = 0;
            return await adapter.ReadAsync(file, hash, cancellation).ConfigureAwait(false);
        }
        finally { foreach (var pin in held) pin.Dispose(); }
    }
    internal static byte[] ReadPlain(string path, int maximum)
    {
        using var file = IsolatedLibrarySource.OpenChecked(path);
        if (file.Length > maximum) throw new InvalidDataException("MIGRATION_MARKER_SIZE_LIMIT");
        var bytes = new byte[(int)file.Length]; file.ReadExactly(bytes); return bytes;
    }
    public static ImmutableArray<LibraryEntry> ReadLibraryBackup(string explicitFile, ILibraryCodec codec)
    {
        var path = Path.GetFullPath(explicitFile);
        if (!path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("NATIVE_BACKUP_JSON_REQUIRED");
        var parent = Path.GetDirectoryName(path)!; NativeProfilePaths.Validate(new(NativeProfileKind.Test, parent));
        var held = IsolatedLibraryPersistence.PinDirectoryChain(parent);
        try { return codec.Decode(ReadPlain(path, 32 * 1024 * 1024)); }
        finally { foreach (var pin in held) pin.Dispose(); }
    }
}
