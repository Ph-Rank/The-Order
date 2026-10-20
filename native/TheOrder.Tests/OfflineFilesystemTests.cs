using System.Collections.Immutable;
using TheOrder.Application;
using TheOrder.Domain;
using TheOrder.Infrastructure.Legacy;
using TheOrder.Platform.Windows;
using Xunit;

namespace TheOrder.Tests;

public sealed class OfflineFilesystemTests : IDisposable
{
    private readonly TransactionProfile profile = new();
    private readonly WindowsFilesystemIdentity identity = new();
    private static readonly NativeLibraryCodec Codec = new();
    private const string Time = "2026-10-04T00:00:00.000Z";
    private string Game(string name, params string[] files)
    {
        var path = Path.Combine(profile.Root, "games", name); Directory.CreateDirectory(path);
        foreach (var file in files) File.WriteAllText(Path.Combine(path, file), "synthetic-not-executable"); return path;
    }
    private static LibraryEntry Entry(string id) => new(id, id, false, null, [], null, "planned", null, false, [], [], "", 0, 0, [], Time, null, null, null, new(false, null));
    private TransactionalLibrary Open(params LibraryEntry[] entries)
    {
        if (!File.Exists(profile.Main)) File.WriteAllBytes(profile.Main, Codec.Encode([.. entries], Time));
        return TransactionalLibrary.Open(Codec, new IsolatedLibraryPersistence(profile.Root));
    }
    private PreparedInstallation Prepared(string path, string id = "install", string? executable = null, string? working = null, ImmutableArray<string> args = default)
        => new(new(id, path, executable, args.IsDefault ? [] : args, working), identity.Inspect(path).Observation!, executable is null ? null : identity.Inspect(executable, false).Observation, working is null ? null : identity.Inspect(working).Observation, identity.Inspect(path).Observation!);
    [Fact] public async Task ScannerMatchesBoundedCandidateSemanticsAndDoesNotWrite()
    {
        var a = Game("A", "A.exe", "uninstall.exe", "setup.exe", "launcher.exe"); Game("B", "setup.exe"); Game("补丁", "patch.exe");
        Directory.CreateDirectory(Path.Combine(a, "child")); File.WriteAllText(Path.Combine(a, "child", "hidden.exe"), "synthetic");
        var scanner = new BoundedScanner(identity, new WindowsScanFilesystem());
        var result = await scanner.ScanAsync(Path.Combine(profile.Root, "games"), []);
        Assert.Equal(ScanStatus.Completed, result.Status); var candidate = Assert.Single(result.Candidates);
        Assert.Equal("A", candidate.Name); Assert.Equal(new[] { "A.exe", "launcher.exe" }, candidate.Executables.Select(Path.GetFileName));
        Assert.False(File.Exists(profile.Main));
    }
    [Theory][InlineData("depth")][InlineData("entries")][InlineData("directories")][InlineData("candidates")]
    public async Task ScannerEnforcesEveryBound(string bound)
    {
        Game("A", "game.exe"); Game("B", "game.exe");
        var limits = bound switch { "depth" => new ScanLimits(MaxDepth: 0), "entries" => new ScanLimits(MaxEntries: 1), "directories" => new ScanLimits(MaxDirectories: 1), _ => new ScanLimits(MaxCandidates: 1) };
        var result = await new BoundedScanner(identity, new WindowsScanFilesystem()).ScanAsync(Path.Combine(profile.Root, "games"), [], limits);
        Assert.Equal(bound == "depth" ? ScanStatus.NoCandidates : ScanStatus.Failed, result.Status);
    }
    [Fact] public async Task CancelledAndMissingRootsAreNotSuccessfulEmptyScans()
    {
        var root = Game("A", "game.exe"); var scanner = new BoundedScanner(identity, new WindowsScanFilesystem());
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        Assert.Equal(ScanStatus.Cancelled, (await scanner.ScanAsync(root, [], cancellation: cancel.Token)).Status);
        Assert.Equal(ScanStatus.Failed, (await scanner.ScanAsync(root + "-gone", [])).Status);
        // Data-drive roots are now valid. This control must never enumerate an actual data volume.
        var systemRoot = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows))!;
        Assert.Equal(ScanStatus.Failed, (await scanner.ScanAsync(systemRoot, [])).Status);
    }
    [Fact] public async Task BatchImportUsesSingleDurableTransactionThenSkipsDuplicates()
    {
        var inputs = Enumerable.Range(0, 100).Select(i => { var directory = Game("G" + i, "game.exe"); return new LocalImportItem("Game " + i, Prepared(directory, "i" + i, Path.Combine(directory, "game.exe")), [identity.Inspect(Path.Combine(directory, "game.exe"), false).Observation!]); }).ToImmutableArray();
        var persistence = new CountingPersistence(Codec.Encode([], Time)); await using var library = TransactionalLibrary.Open(Codec, persistence);
        var service = new InstallationPreflightService(identity);
        Assert.Equal(100, (await library.ImportLocalAsync(inputs, service)).Value.Added); Assert.Equal(1, persistence.Writes);
        var duplicate = await library.ImportLocalAsync(inputs, service); Assert.Equal(0, duplicate.Value.Added); Assert.Equal(100, duplicate.Value.Skipped);
        Assert.Equal(100, library.Snapshot.Entries.Length);
    }
    [Fact] public async Task InstallationDraftPersistsRawPathArgumentsAndWorkingDirectory()
    {
        var path = Game("A", "game.exe"); var raw = path.Replace('\\', '/') + "/.";
        await using var library = Open(Entry("a")); var details = await library.InstallationsAsync("a");
        var result = await library.SaveInstallationAsync(details.Token, Prepared(raw, executable: Path.Combine(path, "game.exe"), working: path, args: ["two words", "", "--flag=中"]), true, new(identity));
        Assert.Equal(raw, result.Value.Installations[0].Folder);
        var saved = Codec.Decode(File.ReadAllBytes(profile.Main))[0].Installations[0]; Assert.Equal(raw, saved.Folder); Assert.Equal(path, saved.WorkingDirectory); Assert.Equal(new[] { "two words", "", "--flag=中" }, saved.Arguments);
    }
    [Fact] public async Task MultipleInstallationsPreferredAndRemovalNeverDeleteFiles()
    {
        var a = Game("A", "game.exe"); var b = Game("B", "game.exe"); await using var library = Open(Entry("a")); var preflight = new InstallationPreflightService(identity);
        await library.SaveInstallationAsync((await library.InstallationsAsync("a")).Token, Prepared(a, "one", Path.Combine(a, "game.exe")), true, preflight);
        await library.SaveInstallationAsync((await library.InstallationsAsync("a")).Token, Prepared(b, "two", Path.Combine(b, "game.exe")), false, preflight);
        Assert.Equal("one", library.Snapshot.Entries[0].PreferredInstallationId);
        await library.PreferInstallationAsync((await library.InstallationsAsync("a")).Token, "two");
        await library.RemoveInstallationAsync((await library.InstallationsAsync("a")).Token, "two");
        Assert.Null(library.Snapshot.Entries[0].PreferredInstallationId); Assert.True(File.Exists(Path.Combine(b, "game.exe"))); Assert.Single(library.Snapshot.Entries[0].Installations);
    }
    [Fact] public async Task DuplicateAndOverlappingInstallationsAcrossGamesAreRejected()
    {
        var path = Game("A", "game.exe"); await using var library = Open(Entry("a"), Entry("b")); var preflight = new InstallationPreflightService(identity);
        await library.SaveInstallationAsync((await library.InstallationsAsync("a")).Token, Prepared(path), true, preflight);
        await Assert.ThrowsAsync<InvalidDataException>(() => library.SaveInstallationAsync((library.InstallationsAsync("b").GetAwaiter().GetResult()).Token, Prepared(path, "other"), true, preflight));
        var child = Directory.CreateDirectory(Path.Combine(path, "child")).FullName;
        await Assert.ThrowsAsync<InvalidDataException>(() => library.SaveInstallationAsync((library.InstallationsAsync("b").GetAwaiter().GetResult()).Token, Prepared(child, "child"), true, preflight));
    }
    [Fact] public async Task DisappearingOrReplacedPathRejectsSnapshotBeforeWriting()
    {
        var path = Game("A", "game.exe"); var prepared = Prepared(path); await using var library = Open(Entry("a")); var token = (await library.InstallationsAsync("a")).Token;
        var before = File.ReadAllBytes(profile.Main); Directory.Move(path, path + "-moved"); Directory.CreateDirectory(path);
        await Assert.ThrowsAsync<InvalidDataException>(() => library.SaveInstallationAsync(token, prepared, true, new(identity)));
        Assert.Equal(before, File.ReadAllBytes(profile.Main)); Assert.Empty(library.Snapshot.Entries[0].Installations);
    }
    [Fact] public async Task ExecutableOutsideDirectoryAndForgedObservationsAreRejected()
    {
        var a = Game("A", "game.exe"); var b = Game("B", "game.exe"); await using var library = Open(Entry("a")); var token = (await library.InstallationsAsync("a")).Token;
        await Assert.ThrowsAsync<InvalidDataException>(() => library.SaveInstallationAsync(token, Prepared(a, executable: Path.Combine(b, "game.exe")), true, new(identity)));
        var forged = Prepared(a) with { Value = new("i", b, null, []) };
        await Assert.ThrowsAsync<InvalidDataException>(() => library.SaveInstallationAsync(token, forged, true, new(identity)));
    }
    [Fact] public async Task ConcurrentArgsEditCannotBeOverwrittenByOldDraft()
    {
        var path = Game("A", "game.exe"); await using var library = Open(Entry("a")); var preflight = new InstallationPreflightService(identity);
        await library.SaveInstallationAsync((await library.InstallationsAsync("a")).Token, Prepared(path), true, preflight);
        var token = (await library.InstallationsAsync("a")).Token;
        await library.SaveInstallationAsync(token, Prepared(path, args: ["new"]), true, preflight);
        await Assert.ThrowsAsync<StaleLibraryRequestException>(() => library.SaveInstallationAsync(token, Prepared(path, args: ["stale"]), true, preflight));
    }
    [Theory][InlineData("access-denied")][InlineData("disappeared")][InlineData("symlink-loop")][InlineData("junction-loop")]
    public async Task ScanChildFailuresAndReparseLoopsAreBounded(string kind)
    {
        var root = Game("root"); var good = Path.Combine(root, "good"); Directory.CreateDirectory(good); File.WriteAllText(Path.Combine(good, "game.exe"), "synthetic");
        Directory.CreateDirectory(Path.Combine(root, "unavailable"));
        var fault = new FaultScanFilesystem(root, kind);
        var result = await new BoundedScanner(identity, fault).ScanAsync(root, []);
        Assert.Equal(ScanStatus.Completed, result.Status); Assert.Single(result.Candidates); Assert.InRange(result.Progress.Directories, 1, 3);
        Assert.Equal(kind.EndsWith("loop") ? 0 : 1, result.Warnings.Length);
    }
    private sealed class FaultScanFilesystem(string root, string kind) : IScanFilesystem
    {
        private readonly WindowsScanFilesystem inner = new();
        public bool IsForbiddenRoot(PathObservation observation) => inner.IsForbiddenRoot(observation);
        public IEnumerable<DirectoryItem> Enumerate(string path)
        {
            if (path == Path.Combine(root, "unavailable"))
                throw kind == "access-denied" ? new UnauthorizedAccessException("Synthetic denied enumeration") : new DirectoryNotFoundException("Synthetic disappeared after identity resolution");
            if (path != root) return inner.Enumerate(path);
            return Entries();
            IEnumerable<DirectoryItem> Entries()
            {
                yield return new(Path.Combine(root, "good"), "good", true, false);
                if (kind.EndsWith("loop")) yield return new(root, "loop", true, true);
                // The child fails between identity resolution and enumeration.
                else yield return new(Path.Combine(root, "unavailable"), "unavailable", true, false);
            }
        }
    }
    public void Dispose() => profile.Dispose();
    private sealed class CountingPersistence(byte[] bytes) : ILibraryPersistence
    {
        public int Writes { get; private set; }
        public byte[]? ReadMain() => bytes;
        public byte[]? ReadBackup() => null;
        public Task<PersistenceReceipt> SaveAsync(ReadOnlyMemory<byte> previous, ReadOnlyMemory<byte> candidate) { bytes = candidate.ToArray(); Writes++; return Task.FromResult(new PersistenceReceipt()); }
        public void Dispose() { }
    }
}
