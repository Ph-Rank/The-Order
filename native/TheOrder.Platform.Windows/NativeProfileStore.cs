using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using TheOrder.Application;

namespace TheOrder.Platform.Windows;

public enum BootstrapPoint { BeforeRoot, DirectoryCreated, BeforeManifest, ManifestTempWritten, BeforeManifestReplace,
    LibraryWriting, LibraryCreated, SettingsWriting, BeforeReady, ReadyPublished, BeforeReopen }
public interface IBootstrapProbe { void Reach(BootstrapPoint point); }
public sealed record NativeProfileManifest(int Version, Guid ProfileId, Guid StoreId, NativeProfileKind ProfileKind,
    int StoreFormatVersion, int CreatedByWriterVersion, int MinimumWriterVersion, string[] RequiredCapabilities, string PhysicalRoot, string State);

// Application-owned profile lease. Library and companion adapters borrow, never independently open another writer.
public sealed class NativeProfileStore : INativeProfileStore, IDisposable
{
    public const string ManifestName = "native-profile.json";
    public const string BootstrapName = "native-bootstrap.json";
    public const int WriterVersion = 1;
    private static readonly string[] Capabilities = ["native-profile-v1", "native-session-v1"];
    private static readonly string[] Components = ["library.json", "native-ui.json", "native-online-key.dpapi", "native-recognition.json", "native-launch-positions.dpapi"];
    internal static readonly JsonSerializerOptions JsonOptions = new()
    { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, RespectRequiredConstructorParameters = true, Converters = { new JsonStringEnumConverter() } };
    private sealed record Component(int Version, Guid ProfileId, Guid StoreId, string Name, bool RestoredFromBackup, byte[] Data);
    private readonly NativeProfileRequest request;
    private readonly ILibraryCodec codec;
    private readonly IPersistenceProbe? persistenceProbe;
    private readonly List<IDisposable> held = [];
    private readonly object gate = new();
    private bool disposed, libraryOwned;
    public string Root { get; }
    public NativeProfileManifest Manifest { get; private set; } = null!;
    public NativeConfiguration Configuration(bool diagnostics = false) => new(Root, diagnostics, this);

    private NativeProfileStore(NativeProfileRequest request, ILibraryCodec codec, IPersistenceProbe? persistenceProbe)
    { this.request = request; this.codec = codec; this.persistenceProbe = persistenceProbe; Root = NativeProfilePaths.Validate(request); }

    public static NativeProfileStore Open(NativeProfileRequest request, ILibraryCodec codec, IBootstrapProbe? probe = null, IPersistenceProbe? persistenceProbe = null)
    {
        var store = new NativeProfileStore(request, codec, persistenceProbe);
        try { store.Initialize(probe); return store; }
        catch { store.Dispose(); throw; }
    }
    internal static LibraryDocument ReadMigrationTestPreview(NativeProfileRequest request, ILibraryCodec codec)
    {
        if (request.Kind != NativeProfileKind.Test || request.ExplicitProductionAuthorization) throw new NativeProfileException("MigrationTestOnly");
        using var store = new NativeProfileStore(request, codec, null);
        store.held.AddRange(IsolatedLibraryPersistence.PinDirectoryChain(store.Root));
        // No Initialize, lease creation, recovery, settings access, backup or filesystem write.
        store.Manifest = store.ParseManifest(store.ReadFile(ManifestName) ?? throw new NativeProfileException("MissingManifest"), "Ready", store.PhysicalIdentity());
        if (store.Manifest.ProfileKind != NativeProfileKind.Test) throw new NativeProfileException("MigrationTestOnly");
        var bytes = store.ReadCore("library.json", false);
        store.ValidatePayload("library.json", bytes);
        return codec.Inspect(bytes, "2000-01-01T00:00:00.000Z");
    }
    private void Initialize(IBootstrapProbe? probe)
    {
        probe?.Reach(BootstrapPoint.BeforeRoot);
        held.AddRange(NativeProfilePaths.CreateAndPinRoot(request));
        NativeProfilePaths.Validate(request);
        var lease = Path.Combine(Root, ".the-order-writer.lock"); WindowsAtomicFile.CheckExisting(lease);
        held.Add(new FileStream(lease, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None));
        probe?.Reach(BootstrapPoint.DirectoryCreated);
        var physical = PhysicalIdentity();
        var ready = ReadFile(ManifestName);
        if (ready is not null)
        {
            Manifest = ParseManifest(ready, "Ready", physical);
            var anchor = ParseManifest(ReadFile(BootstrapName) ?? throw new NativeProfileException("RecoveryRequired"), "Initializing", physical);
            SameIdentity(anchor, Manifest);
        }
        else
        {
            var anchorBytes = ReadFile(BootstrapName);
            if (anchorBytes is null)
            {
                if (request.ExpectedProfileId is not null || request.ExpectedStoreId is not null)
                    throw new NativeProfileException("ExpectedProfileMissing");
                // Only our interrupted temp files/lease may exist. Never adopt an arbitrary library.
                var files = Directory.EnumerateFileSystemEntries(Root).Take(1025).ToArray();
                if (files.Length > 1024) throw new NativeProfileException("UnmanagedProfileRequiresExplicitImport");
                foreach (var file in files)
                {
                    var name = Path.GetFileName(file);
                    if (name != ".the-order-writer.lock" && !IsBootstrapTemp(name)) throw new NativeProfileException("UnmanagedProfileRequiresExplicitImport");
                }
                var profileId = StableId("profile|" + physical);
                Manifest = new(1, profileId, StableId("store|" + physical), request.Kind, 1, 1, 1, [.. Capabilities], physical, "Initializing");
                probe?.Reach(BootstrapPoint.BeforeManifest);
                WriteFile(BootstrapName, Serialize(Manifest), stage =>
                {
                    if (stage == PersistenceStage.AfterTempWrite) probe?.Reach(BootstrapPoint.ManifestTempWritten);
                    if (stage == PersistenceStage.BeforeReplace) probe?.Reach(BootstrapPoint.BeforeManifestReplace);
                    return Task.CompletedTask;
                });
            }
            else Manifest = ParseManifest(anchorBytes, "Initializing", physical);
            var initial = new Dictionary<string, byte[]>(StringComparer.Ordinal)
            {
                ["library.json"] = codec.Encode([], "2000-01-01T00:00:00.000Z"),
                ["native-ui.json"] = IsolatedUiPreferencesStore.Encode(new()),
                ["native-online-key.dpapi"] = [], ["native-recognition.json"] = [], ["native-launch-positions.dpapi"] = []
            };
            // Inspect every existing companion before filling any hole. A lost Ready manifest
            // must not let a partial directory containing user data be treated as fresh.
            foreach (var (name, data) in initial)
                foreach (var file in new[] { name + ".bak", name })
                    if (ReadFile(file) is { } prior && !Unwrap(name, prior).AsSpan().SequenceEqual(data))
                        throw new NativeProfileException("PartialBootstrapHasUserData");
            foreach (var (name, data) in initial)
            {
                var encoded = Wrap(name, data);
                foreach (var file in new[] { name + ".bak", name })
                {
                    var prior = ReadFile(file);
                    if (prior is not null)
                    {
                        var decoded = Unwrap(name, prior);
                        if (!decoded.AsSpan().SequenceEqual(data)) throw new NativeProfileException("PartialBootstrapHasUserData");
                    }
                    else WriteFile(file, encoded, stage =>
                    {
                        if (stage == PersistenceStage.DuringTempWrite) probe?.Reach(name == "library.json" ? BootstrapPoint.LibraryWriting : BootstrapPoint.SettingsWriting);
                        return Task.CompletedTask;
                    });
                }
                if (name == "library.json") probe?.Reach(BootstrapPoint.LibraryCreated);
            }
            probe?.Reach(BootstrapPoint.BeforeReady);
            Manifest = Manifest with { State = "Ready" };
            WriteFile(ManifestName, Serialize(Manifest));
            probe?.Reach(BootstrapPoint.ReadyPublished);
        }
        // Pin immutable identity manifests against replacement for the whole writer lifetime.
        held.Add(IsolatedLibrarySource.OpenChecked(Path.Combine(Root, BootstrapName)));
        held.Add(IsolatedLibrarySource.OpenChecked(Path.Combine(Root, ManifestName)));
        probe?.Reach(BootstrapPoint.BeforeReopen);
        ValidateAccess();
        foreach (var name in Components)
        {
            EnsureBackupCompatible(name);
            try { var bytes = ReadCore(name, false); ValidatePayload(name, bytes); }
            catch (Exception e) when (e is IOException or InvalidDataException)
            {
                // Recovery is explicit; missing/corrupt main must have a valid same-store backup.
                ValidatePayload(name, ReadCore(name, true));
            }
        }
    }
    private static bool IsBootstrapTemp(string name)
    {
        if (!name.EndsWith(".tmp", StringComparison.Ordinal) || !name.StartsWith(BootstrapName + ".", StringComparison.Ordinal)) return false;
        return Guid.TryParseExact(name[(BootstrapName.Length + 1)..^4], "N", out _);
    }
    private string PhysicalIdentity()
    {
        var identity = new WindowsFilesystemIdentity().Inspect(Root).Observation?.Identity ?? throw new NativeProfileException("ProfileIdentityUnavailable");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity + "|" + Directory.GetCreationTimeUtc(Root).Ticks)));
    }
    private static Guid StableId(string text) => new(SHA256.HashData(Encoding.UTF8.GetBytes(text)).AsSpan(0, 16));
    private NativeProfileManifest ParseManifest(byte[] bytes, string state, string physical)
    {
        NativeProfileManifest value;
        try { value = Deserialize<NativeProfileManifest>(bytes); }
        catch (Exception e) when (e is JsonException or InvalidDataException) { throw new NativeProfileException("InvalidManifest"); }
        if (value.Version != 1 || value.StoreFormatVersion != 1 || value.MinimumWriterVersion is < 1 or > WriterVersion
            || value.CreatedByWriterVersion < 1 || value.RequiredCapabilities is null
            || !value.RequiredCapabilities.Order().SequenceEqual(Capabilities.Order())) throw new NativeProfileException("UnsupportedStoreVersionOrCapability");
        if (value.ProfileId == Guid.Empty || value.StoreId == Guid.Empty || value.ProfileKind != request.Kind || value.PhysicalRoot != physical
            || value.State != state || request.ExpectedProfileId is { } p && p != value.ProfileId || request.ExpectedStoreId is { } s && s != value.StoreId)
            throw new NativeProfileException("ProfileIdentityMismatch");
        return value;
    }
    private static void SameIdentity(NativeProfileManifest a, NativeProfileManifest b)
    {
        if (!Serialize(a with { State = b.State }).AsSpan().SequenceEqual(Serialize(b))) throw new NativeProfileException("ProfileIdentityMismatch");
    }
    public void ValidateAccess()
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            NativeProfilePaths.Validate(request);
            var current = ParseManifest(ReadFile(ManifestName) ?? throw new NativeProfileException("MissingManifest"), "Ready", Manifest.PhysicalRoot);
            SameIdentity(Manifest, current);
        }
    }
    private byte[] Wrap(string name, ReadOnlyMemory<byte> bytes, bool restored = false) => Serialize(new Component(1, Manifest.ProfileId, Manifest.StoreId, name, restored, bytes.ToArray()));
    private byte[] Unwrap(string name, byte[] bytes)
    {
        Component value;
        try { value = Deserialize<Component>(bytes); }
        catch (JsonException e)
        {
            // Valid JSON with unknown fields is a compatibility failure, never a backup trigger.
            try { using var parsed = JsonDocument.Parse(bytes); }
            catch (JsonException) { throw new InvalidDataException(UiText.Get("损坏的 native component。"), e); }
            throw new NativeProfileException("UnsupportedComponentSchema");
        }
        if (value.Version != 1 || value.ProfileId != Manifest.ProfileId || value.StoreId != Manifest.StoreId || value.Name != name)
            throw new NativeProfileException("ComponentIdentityOrVersionMismatch");
        return value.Data ?? throw new InvalidDataException("Missing component payload");
    }
    private byte[] ReadCore(string name, bool backup)
    {
        var bytes = ReadFile(name + (backup ? ".bak" : "")) ?? throw new FileNotFoundException("Native component missing");
        return Unwrap(name, bytes);
    }
    private void ValidatePayload(string name, byte[] bytes)
    {
        if (name == "library.json")
        {
            try
            {
                using var json = JsonDocument.Parse(bytes);
                if (json.RootElement.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Invalid library object");
                if (json.RootElement.TryGetProperty("format", out var format) && format.GetString() != "kun-gal-manager"
                    || json.RootElement.TryGetProperty("version", out var version) && (!version.TryGetInt32(out var number) || number != 1))
                    throw new NativeProfileException("UnsupportedLibraryFormat");
            }
            catch (JsonException e) { throw new InvalidDataException("Corrupt library payload", e); }
            var read = codec.Inspect(bytes, "2000-01-01T00:00:00.000Z");
            if (read.Access == LibraryAccess.ValidReadOnly) throw new NativeProfileException("UnsupportedLibrarySchema");
            if (read.Access == LibraryAccess.InvalidInput) throw new InvalidDataException("Native library invalid");
            RejectUnknownFields(bytes, read.WritableBytes!);
        }
        else if (name == "native-ui.json") IsolatedUiPreferencesStore.Decode(bytes);
        else if (name == "native-recognition.json" && bytes.Length != 0)
        {
            try
            {
                var value = JsonSerializer.Deserialize<ReviewDocument>(bytes, new JsonSerializerOptions(JsonOptions) { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                if (value is null || value.Version != 1 || value.Reviews.IsDefault) throw new NativeProfileException("UnsupportedReviewSchema");
            }
            catch (JsonException) { throw new NativeProfileException("UnsupportedReviewSchema"); }
        }
    }
    private sealed record ReviewDocument(int Version, System.Collections.Immutable.ImmutableArray<RecognitionReview> Reviews);
    private static void RejectUnknownFields(byte[] input, byte[] canonical)
    {
        using var a = JsonDocument.Parse(input); using var b = JsonDocument.Parse(canonical);
        Compare(a.RootElement, b.RootElement);
        static void Compare(JsonElement a, JsonElement b)
        {
            if (a.ValueKind == JsonValueKind.Object && b.ValueKind == JsonValueKind.Object)
            {
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (var p in a.EnumerateObject())
                {
                    if (!names.Add(p.Name)) throw new NativeProfileException("DuplicateLibraryField");
                    if (!b.TryGetProperty(p.Name, out var q)) throw new NativeProfileException("UnknownLibraryField");
                    Compare(p.Value, q);
                }
            }
            else if (a.ValueKind == JsonValueKind.Array && b.ValueKind == JsonValueKind.Array)
            {
                // Lossy normalization is not an accepted native bootstrap source.
                if (a.GetArrayLength() != b.GetArrayLength()) throw new NativeProfileException("NoncanonicalLibraryArray");
                // JsonElement's indexer walks variable-sized children from the array start.
                // Sequential enumerators keep complete validation linear for large history arrays.
                var left = a.EnumerateArray(); var right = b.EnumerateArray();
                while (left.MoveNext()) { right.MoveNext(); Compare(left.Current, right.Current); }
            }
        }
    }
    public byte[]? ReadComponent(string name)
    {
        lock (gate) { ValidateName(name); ValidateAccess(); var bytes = ReadCore(name, false); ValidatePayload(name, bytes); return bytes.Length == 0 ? null : bytes; }
    }
    public void WriteComponent(string name, ReadOnlyMemory<byte> bytes)
    {
        lock (gate)
        {
            ValidateName(name); ValidateAccess();
            if (name == "library.json") throw new NativeProfileException("LibraryRequiresTransaction");
            ValidatePayload(name, bytes.ToArray());
            var previous = ReadCore(name, false); ValidatePayload(name, previous);
            SaveCore(name, previous, bytes);
        }
    }
    public void Restore(string name)
    {
        lock (gate)
        {
            ValidateName(name); ValidateAccess();
            if (libraryOwned) throw new NativeProfileException("CloseLibraryBeforeRestore");
            // Never downgrade an incompatible main. Corruption and absence are explicit restore cases.
            try { ValidatePayload(name, ReadCore(name, false)); }
            catch (Exception e) when (e is IOException or InvalidDataException) { }
            var backup = ReadCore(name, true); ValidatePayload(name, backup);
            WriteFile(name, Wrap(name, backup, name == "library.json"), stage => Reach(PersistenceTarget.Main, stage));
            ValidatePayload(name, ReadCore(name, false));
        }
    }
    public void CheckpointBackups()
    {
        lock (gate)
        {
            ValidateAccess();
            foreach (var name in Components)
            {
                EnsureBackupCompatible(name);
                var bytes = ReadCore(name, false); ValidatePayload(name, bytes);
                WriteFile(name + ".bak", Wrap(name, bytes), stage => Reach(PersistenceTarget.Backup, stage));
            }
        }
    }
    public ILibraryPersistence CreateLibraryPersistence()
    {
        lock (gate)
        {
            ValidateAccess();
            if (libraryOwned) throw new NativeProfileException("LibraryWriterAlreadyOwned");
            EnsureBackupCompatible("library.json");
            byte[]? baseline;
            try { baseline = ReadFile("library.json"); }
            catch (InvalidDataException)
            {
                // A capacity-invalid main cannot supply a bounded stale-write baseline. Do not borrow
                // or implicitly replace it: validate recovery, then let explicit Restore establish one.
                ValidatePayload("library.json", ReadCore("library.json", true));
                throw new NativeProfileException("LibraryRecoveryRequired");
            }
            try { ValidatePayload("library.json", Unwrap("library.json", baseline ?? throw new FileNotFoundException())); }
            catch (Exception e) when (e is FileNotFoundException or InvalidDataException)
            {
                // Only proven invalid/absent input authorizes recovery, never an access failure.
                // Complete native recovery BEFORE handing a writable generation to Phase 2.
                var recovered = ReadCore("library.json", true); ValidatePayload("library.json", recovered);
                if (!SameBytes(baseline, ReadFile("library.json"))) throw new NativeProfileException("LibraryChangedOutsideWriter");
                var restored = Wrap("library.json", recovered, true);
                WriteFile("library.json", restored, stage => Reach(PersistenceTarget.Main, stage));
                baseline = ReadFile("library.json");
                if (!SameBytes(restored, baseline)) throw new NativeProfileException("LibraryChangedOutsideWriter");
                ValidatePayload("library.json", Unwrap("library.json", baseline!));
            }
            var borrower = new LibraryPersistence(this, baseline!); // validated envelope is both source and comparison token; no constructor I/O
            libraryOwned = true; // sole transfer point, under the same gate as restore/dispose/borrow
            return borrower;
        }
    }
    private static bool SameBytes(byte[]? a, byte[]? b) => a is null ? b is null : b is not null && a.AsSpan().SequenceEqual(b);
    private PersistenceReceipt SaveCore(string name, ReadOnlyMemory<byte> previous, ReadOnlyMemory<byte> candidate, bool restored = false)
    {
        EnsureBackupCompatible(name);
        Reach(PersistenceTarget.Backup, PersistenceStage.BeforeBackup).GetAwaiter().GetResult();
        var backup = WriteFile(name + ".bak", Wrap(name, previous), stage => Reach(PersistenceTarget.Backup, stage));
        var main = WriteFile(name, Wrap(name, candidate, restored), stage => Reach(PersistenceTarget.Main, stage));
        return new(main.Warning ?? backup.Warning);
    }
    private void EnsureBackupCompatible(string name)
    {
        if (ReadFile(name + ".bak") is not { } bytes) return;
        try { ValidatePayload(name, Unwrap(name, bytes)); }
        catch (InvalidDataException) { } // A valid main can replace a corrupt last-good copy.
        // Unknown versions/fields and foreign identities deliberately escape this catch.
    }
    private Task Reach(PersistenceTarget target, PersistenceStage stage) => persistenceProbe?.ReachAsync(target, stage) ?? Task.CompletedTask;
    private static void ValidateName(string name) { if (!Components.Contains(name, StringComparer.Ordinal)) throw new NativeProfileException("UnknownComponent"); }
    private byte[]? ReadFile(string name)
    {
        try
        {
            using var file = IsolatedLibrarySource.OpenChecked(Path.Combine(Root, name));
            if (file.Length > 48 * 1024 * 1024) throw new InvalidDataException("Native file too large");
            var bytes = new byte[(int)file.Length]; file.ReadExactly(bytes); return bytes;
        }
        catch (FileNotFoundException) { return null; }
    }
    private PersistenceReceipt WriteFile(string name, ReadOnlyMemory<byte> bytes, Func<PersistenceStage, Task>? probe = null)
        => WindowsAtomicFile.WriteAsync(Path.Combine(Root, name), bytes, probe).GetAwaiter().GetResult();
    private static byte[] Serialize<T>(T value) => JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions);
    private static T Deserialize<T>(byte[] bytes)
    {
        using var doc = JsonDocument.Parse(bytes); Unique(doc.RootElement);
        return JsonSerializer.Deserialize<T>(bytes, JsonOptions) ?? throw new InvalidDataException("Missing JSON value");
        static void Unique(JsonElement e)
        {
            if (e.ValueKind == JsonValueKind.Object)
            {
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (var p in e.EnumerateObject()) { if (!names.Add(p.Name)) throw new NativeProfileException("DuplicateJsonField"); Unique(p.Value); }
            }
            else if (e.ValueKind == JsonValueKind.Array) foreach (var item in e.EnumerateArray()) Unique(item);
        }
    }
    private sealed class LibraryPersistence(NativeProfileStore owner, byte[] baseline) : ILibraryPersistence, ILibraryRecoveryProvenance
    {
        private bool closed;
        private byte[] expectedMain = baseline;
        private bool fromBackup = Deserialize<Component>(baseline).RestoredFromBackup;
        public LibraryRecovery MainRecovery => fromBackup ? LibraryRecovery.Backup : LibraryRecovery.Main;
        public byte[]? ReadMain()
        {
            lock (owner.gate)
            {
                ObjectDisposedException.ThrowIf(closed, this);
                byte[]? current;
                try { owner.ValidateAccess(); current = owner.ReadFile("library.json"); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    // Not an IOException: Phase 2 must not interpret a temporary inaccessible main
                    // as permission to load an older backup. Factory/Open cleanup releases this lease.
                    throw new NativeProfileException("LibraryTemporarilyUnavailable");
                }
                if (!SameBytes(expectedMain, current)) throw new InvalidDataException("LibraryReadGenerationChanged");
                // Read data and provenance from the SAME validated envelope used by stale-write checks.
                return owner.Unwrap("library.json", expectedMain);
            }
        }
        public byte[]? ReadBackup()
        {
            lock (owner.gate)
            {
                ObjectDisposedException.ThrowIf(closed, this); owner.ValidateAccess();
                // A borrowed native writer is already bound to an authoritative main generation.
                // Recovery requires releasing it and acquiring a fresh generation through the owner.
                throw new InvalidDataException("LibraryReadGenerationChanged: reopen native profile writer for recovery");
            }
        }
        public Task<PersistenceReceipt> SaveAsync(ReadOnlyMemory<byte> previous, ReadOnlyMemory<byte> candidate)
        {
            lock (owner.gate)
            {
                ObjectDisposedException.ThrowIf(closed, this); owner.ValidateAccess(); owner.ValidatePayload("library.json", candidate.ToArray());
                var actualMain = owner.ReadFile("library.json");
                if (!SameBytes(actualMain, expectedMain))
                    throw new NativeProfileException("LibraryChangedOutsideWriter");
                var retainProvenance = fromBackup && owner.codec.Decode(candidate).Any(e => e.Sessions.Any(s => s.Native is { IsTerminal: false }));
                var receipt = owner.SaveCore("library.json", previous, candidate, retainProvenance);
                expectedMain = owner.Wrap("library.json", candidate, retainProvenance);
                fromBackup = retainProvenance;
                return Task.FromResult(receipt);
            }
        }
        public void Dispose() { lock (owner.gate) { if (closed) return; closed = true; owner.libraryOwned = false; } }
    }
    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            if (libraryOwned) throw new NativeProfileException("DisposeLibraryBeforeProfile");
            disposed = true; for (var i = held.Count - 1; i >= 0; i--) held[i].Dispose(); held.Clear();
        }
    }
}
