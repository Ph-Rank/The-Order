using System.Collections.Immutable;
using TheOrder.Application;
using TheOrder.Infrastructure.Legacy;
using TheOrder.Platform.Windows;
using Xunit;
using static TheOrder.Tests.RecognitionBoundaryTests;

namespace TheOrder.Tests;

public sealed class DriveRootScanningTests
{
    // A virtual data drive. These tests never enumerate an actual drive root or private files.
    private sealed class Drive : IFilesystemIdentity, IScanFilesystem
    {
        private readonly Dictionary<string, PathObservation> nodes = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<DirectoryItem>> items = new(StringComparer.OrdinalIgnoreCase);
        private int sequence;
        internal readonly List<string> Enumerated = [];
        internal Action<string>? After;
        internal Drive() => Folder(@"E:\");
        internal void Folder(string path, bool link = false) => Add(path, true, link);
        internal void File(string path) => Add(path, false, false);
        private void Add(string path, bool directory, bool link)
        {
            var display = Path.TrimEndingDirectorySeparator(path);
            nodes[path] = new(path, display, @"\\?\" + display, new("virtual-volume", (++sequence).ToString()), directory, false, 0);
            if (directory) items[path] = [];
            var parent = Path.GetDirectoryName(display);
            if (parent is not null && items.TryGetValue(parent, out var children)) children.Add(new(path, Path.GetFileName(display), directory, link));
        }
        public PathPreflight Inspect(string path, bool requireDirectory = true) => nodes.TryGetValue(path, out var node)
            ? requireDirectory && !node.IsDirectory ? new(PreflightStatus.FileInsteadOfDirectory, null, "file") : new(PreflightStatus.Valid, node, "valid")
            : new(PreflightStatus.NotFound, null, "missing");
        public IEnumerable<DirectoryItem> Enumerate(string directory)
        {
            Enumerated.Add(directory);
            foreach (var item in items[directory]) yield return item;
            After?.Invoke(directory);
        }
        public bool IsForbiddenRoot(PathObservation root) => WindowsScanFilesystem.IsForbiddenPath(root.FinalPath, @"C:\Windows", [@"C:\Program Files", @"C:\ProgramData"]);
    }
    [Theory]
    [InlineData(@"E:\", false)][InlineData(@"\\?\E:\", false)][InlineData(@"C:\", true)]
    [InlineData(@"C:\Windows", true)][InlineData(@"C:\Windows\System32", true)]
    [InlineData(@"C:\Program Files", true)][InlineData(@"C:\ProgramData", true)]
    [InlineData(@"C:\Program Files\Steam\steamapps\common\Game", false)]
    [InlineData(@"\\server\share\", true)][InlineData(@"\\server\share\Game", false)]
    public void RootAuthorizationKeepsSystemAndShareBoundaries(string path, bool forbidden)
        => Assert.Equal(forbidden, WindowsScanFilesystem.IsForbiddenPath(path, @"C:\Windows", [@"C:\Program Files", @"C:\ProgramData"]));

    [Fact]
    public void VolumeMountAliasDoesNotPutLocalParentPathIntoGameTitle()
    {
        var folder = new PathObservation(@"C:\private-parent\Game", @"C:\private-parent\Game", @"\\?\E:\", new("volume", "root"), true, true, 0xA0000003);
        Assert.True(ScanDirectoryPolicy.IsDriveRoot(folder)); Assert.Equal("Game", ScanDirectoryPolicy.CandidateName(folder));
    }

    [Fact]
    public async Task DataDriveContainerFindsGamesButNeverEntersProtectedDirectoriesOrLinks()
    {
        var drive = new Drive(); drive.Folder(@"E:\中文游戏+"); drive.File(@"E:\中文游戏+\game.exe");
        drive.Folder(@"E:\Other"); drive.File(@"E:\Other\start.cmd");
        foreach (var name in new[] { "Windows", "System Volume Information", "$Recycle.Bin", "Recovery", "Program Files", "EFI" })
        { drive.Folder(@"E:\" + name); drive.File(@"E:\" + name + @"\do-not-read.exe"); }
        drive.Folder(@"E:\link", true); drive.File(@"E:\link\do-not-read.exe");
        drive.Folder(@"E:\empty.exe"); // A directory with an .exe suffix is not a launcher.
        var scan = await new BoundedScanner(drive, drive).ScanAsync(@"E:\", []);
        Assert.Equal(ScanStatus.Completed, scan.Status); Assert.False(scan.IsPartial); Assert.Equal(2, scan.Candidates.Length);
        Assert.Contains(scan.Candidates, c => c.Name == "中文游戏+"); Assert.Contains(scan.Candidates, c => c.Name == "Other");
        Assert.DoesNotContain(scan.Candidates.SelectMany(c => c.Executables), e => e.EndsWith("do-not-read.exe") || e.EndsWith("empty.exe"));
        Assert.DoesNotContain(@"E:\Recovery", drive.Enumerated); Assert.DoesNotContain(@"E:\link", drive.Enumerated);
        Assert.DoesNotContain(@"E:\Program Files", drive.Enumerated);
    }
    [Fact]
    public async Task GameAtDriveRootHasUsableNameAndKeepsDuplicateProtection()
    {
        var drive = new Drive(); drive.File(@"E:\game.exe"); drive.File(@"E:\setup.exe");
        drive.Folder(@"E:\Another"); drive.File(@"E:\Another\other.exe");
        var scan = await new BoundedScanner(drive, drive).ScanAsync(@"E:\", []);
        var candidate = Assert.Single(scan.Candidates); Assert.Contains("E:\\", candidate.Name); Assert.True(candidate.Preflight.CanAttach);
        Assert.Equal(@"E:\game.exe", Assert.Single(candidate.Executables)); Assert.Single(drive.Enumerated);
        var repeated = await new BoundedScanner(drive, drive).ScanAsync(@"E:\", [new("known", "old", "installation", @"E:\")]);
        Assert.False(Assert.Single(repeated.Candidates).Preflight.CanAttach);
    }
    [Fact]
    public async Task DataDriveStillHonorsLimitsCancellationAndRootReplacement()
    {
        var drive = new Drive(); drive.Folder(@"E:\One"); drive.File(@"E:\One\game.exe"); drive.Folder(@"E:\Two");
        var scanner = new BoundedScanner(drive, drive);
        Assert.Equal(ScanStatus.Failed, (await scanner.ScanAsync(@"E:\", [], new(MaxEntries: 1))).Status);
        Assert.Equal(ScanStatus.Cancelled, (await scanner.ScanAsync(@"E:\", [], cancellation: new CancellationToken(true))).Status);
        drive.Enumerated.Clear(); drive.After = p => { if (p == @"E:\") drive.Folder(@"E:\"); };
        var changed = await scanner.ScanAsync(@"E:\", []);
        Assert.Equal(ScanStatus.Failed, changed.Status); Assert.Empty(changed.Candidates); Assert.Single(drive.Enumerated);
    }
    [Fact]
    public async Task LaunchPickerAtDataDriveSkipsSystemFoldersAndKeepsOrdinaryGameSubfolders()
    {
        var drive = new Drive(); drive.File(@"E:\game.exe"); drive.Folder(@"E:\bin"); drive.File(@"E:\bin\game64.exe");
        drive.Folder(@"E:\Recovery"); drive.File(@"E:\Recovery\system.exe");
        var choices = await new LaunchFileDiscovery(drive, drive).ReadAsync(@"E:\");
        Assert.False(choices.Partial); Assert.Equal(2, choices.Choices.Length); Assert.Null(choices.AutomaticChoice);
        Assert.DoesNotContain(@"E:\Recovery", drive.Enumerated);
        var ordinary = drive.Inspect(@"E:\bin").Observation!;
        Assert.False(ScanDirectoryPolicy.SkipChild(ordinary, "Recovery"));
    }
    private sealed class Store : ILibrarySourcesStore
    {
        internal ImmutableArray<LibrarySource> Values = [];
        public Task<ImmutableArray<LibrarySource>> LoadAsync(CancellationToken cancellation) => Task.FromResult(Values);
        public Task SaveAsync(ImmutableArray<LibrarySource> sources, CancellationToken cancellation) { Values = sources; return Task.CompletedTask; }
    }
    private sealed class Factory(SessionMemoryDisk disk) : ITransactionalLibraryFactory
    { public Task<TransactionalLibrary> OpenAsync(CancellationToken cancellation) => Task.FromResult(TransactionalLibrary.Open(new NativeLibraryCodec(), new SessionMemoryPersistence(disk))); }
    private sealed class NoSteam : ISteamLibraryScanner
    { public Task<ScanResult> ScanAsync(PathObservation root, ImmutableArray<ExistingInstallation> existing, CancellationToken cancellation) => throw new InvalidOperationException("Not a Steam test"); }
    [Fact]
    public async Task RootGameImportsPersistsAndReimportsWithoutDuplicating()
    {
        var drive = new Drive(); drive.File(@"E:\game.exe"); var disk = new SessionMemoryDisk { Main = new NativeLibraryCodec().Encode([], Clock) };
        string gameId;
        await using (var client = new OfflineLibraryClient(new Factory(disk), new LegacyClientTextRules()))
        {
            await client.LoadAsync(default); var files = new OfflineFilesClient(client, drive, new(drive, drive), new WindowsRasterCodec());
            var scanned = await files.ScanAsync(@"E:\", null, default);
            var imported = await files.ImportAsync(scanned.Candidates, default);
            Assert.Equal(ClientOutcome.Committed, imported.Outcome);
            var game = Assert.Single(client.CurrentSnapshot!.Entries); gameId = game.Id;
            Assert.Equal(@"E:\", Assert.Single(game.Installations).Folder); Assert.False(string.IsNullOrWhiteSpace(game.Name));
        }
        await using var reopened = new OfflineLibraryClient(new Factory(disk), new LegacyClientTextRules()); await reopened.LoadAsync(default);
        var restored = Assert.Single(reopened.CurrentSnapshot!.Entries); Assert.Equal(gameId, restored.Id);
        var repeat = new OfflineFilesClient(reopened, drive, new(drive, drive), new WindowsRasterCodec());
        var candidates = await repeat.ScanAsync(@"E:\", null, default); Assert.False(Assert.Single(candidates.Candidates).Preflight.CanAttach);
        Assert.Equal(ClientOutcome.Committed, (await repeat.ImportAsync(candidates.Candidates, default)).Outcome);
        Assert.Equal(gameId, Assert.Single(reopened.CurrentSnapshot.Entries).Id);
    }
    [Fact]
    public async Task WholeDriveLibrarySourceRequiresExplicitScanInsteadOfBackgroundWatch()
    {
        var drive = new Drive(); drive.Folder(@"E:\One"); drive.File(@"E:\One\game.exe"); var store = new Store();
        await using var client = new OfflineLibraryClient(new Factory(new() { Main = new NativeLibraryCodec().Encode([], Clock) }), new LegacyClientTextRules());
        await client.LoadAsync(default);
        await using var service = new LibrarySourcesService(store, drive, drive, new(drive, drive), new NoSteam(), client);
        await service.AddAsync(@"E:\", LibrarySourceKind.Folder); var source = Assert.Single(service.Sources); Assert.False(source.Watch);
        await service.ScanAsync(true, watchedOnly: true); Assert.Empty(drive.Enumerated);
        await service.ScanAsync(true); Assert.Single(service.Candidates);
        await Assert.ThrowsAsync<InvalidDataException>(() => service.ConfigureAsync(source.Id, true)); Assert.False(Assert.Single(store.Values).Watch);
        LibrarySourcesService.Validate(store.Values);
        Assert.Throws<InvalidDataException>(() => LibrarySourcesService.Validate([source with { Watch = true }]));
    }
}
