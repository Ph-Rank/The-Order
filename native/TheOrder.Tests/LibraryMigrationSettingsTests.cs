using System.Collections.Immutable;
using System.IO.Compression;
using System.Text.Json;
using TheOrder.Application;
using TheOrder.Application.Migrations;
using TheOrder.Domain;
using TheOrder.Infrastructure.Legacy;
using TheOrder.Infrastructure.Migration;
using TheOrder.Platform.Windows;
using TheOrder.Presentation;
using Xunit;

namespace TheOrder.Tests;

public sealed class LibraryMigrationSettingsTests
{
    private static readonly NativeLibraryCodec Codec = new();
    private static Task<ImportDataset> Data() => Read(JsonSerializer.SerializeToUtf8Bytes(new[] { new {
        Uuid = "99999999-1111-4111-8111-111111111111", Name = new { Value = "合成 🌸 かな" },
        Ids = new[] { "123", "456" }, TotalPlayTime = 120, PlayType = 1, MyRate = 8,
        Comment = "个人笔记", PlayedTime = new Dictionary<string, int> { ["2026/10/18"] = 60 },
        Path = @"Q:\NotARealMigrationGame", ExePath = "game.exe"
    } }));
    private static Task<ImportDataset> Read(byte[] bytes) => new PotatoVnImportAdapter().ReadAsync(new MemoryStream(bytes), ImportHash.Bytes(bytes));
    private sealed class Source(ImportDataset data) : ILibraryMigrationSource
    {
        public Func<CancellationToken, Task<ImportDataset>>? Pending;
        public Task<ImportDataset> ReadAsync(string selectedFile, CancellationToken cancellation) => Pending?.Invoke(cancellation) ?? Task.FromResult(data);
    }
    private sealed class Store : ILibraryBackupStore
    {
        public byte[] Saved = []; public int Saves; public bool FailSave, FailRead, Tamper;
        public Action<CancellationToken>? OnSave;
        public Task<LibraryBackupWrite> SaveSnapshotAsync(ReadOnlyMemory<byte> document, int count, string reason, CancellationToken cancellation)
        {
            Assert.Equal("导入前", reason); if (FailSave) throw new IOException("synthetic backup failure");
            Saves++; Saved = document.ToArray(); OnSave?.Invoke(cancellation);
            return Task.FromResult(new LibraryBackupWrite(new("synthetic", DateTimeOffset.UtcNow, reason, count, ImportHash.Bytes(Saved))));
        }
        public Task<byte[]> ReadSnapshotAsync(string id, CancellationToken cancellation)
        { cancellation.ThrowIfCancellationRequested(); if (FailRead) throw new IOException("synthetic backup read failure"); return Task.FromResult(Tamper ? "broken"u8.ToArray() : Saved); }
        public Task<LibraryBackupState> ReadStateAsync(CancellationToken cancellation) => Task.FromResult(new LibraryBackupState(new(false), []));
        public Task SaveSettingsAsync(LibraryBackupSettings settings, CancellationToken cancellation) => Task.CompletedTask;
        public Task<byte[]> ReadExternalAsync(string path, CancellationToken cancellation) => throw new NotSupportedException();
        public Task<string?> WriteExternalAsync(string path, ReadOnlyMemory<byte> document, CancellationToken cancellation) => throw new NotSupportedException();
    }
    private sealed class Factory(SessionMemoryDisk disk) : ITransactionalLibraryFactory
    { public Task<TransactionalLibrary> OpenAsync(CancellationToken cancellation) => Task.FromResult(TransactionalLibrary.Open(Codec, new SessionMemoryPersistence(disk))); }
    private static OfflineLibraryClient Client(SessionMemoryDisk disk) => new(new Factory(disk), new LegacyClientTextRules());

    [Fact]
    public async Task PreviewDoesNotWriteAndCommitBacksUpTheSameWriterThenRepeatsWithoutIo()
    {
        var disk = new SessionMemoryDisk { Main = Codec.Encode([], SessionTestRig.Date) };
        await using var client = Client(disk); await client.LoadAsync(default);
        var store = new Store(); var service = new LibraryMigrationService(client, new Source(await Data()), store);
        var original = disk.Main.ToArray(); var preview = await service.PreviewAsync("selected.zip", default);
        Assert.Equal(1, preview.Added); Assert.Equal(0, disk.Writes); Assert.Equal(0, store.Saves);
        Assert.True((await service.ApplyAsync(preview, default)).IsCommitted);
        Assert.Equal(LibraryBackupService.ContentDigest(Codec, original), LibraryBackupService.ContentDigest(Codec, store.Saved));
        Assert.Equal(1, disk.Writes); Assert.Equal(1, store.Saves);
        var imported = Assert.Single(client.CurrentSnapshot!.Entries);
        Assert.Equal(7200, imported.EffectivePlaySeconds); Assert.Empty(imported.Sessions); Assert.Equal(0, imported.PlayCount);
        Assert.Equal("个人笔记", imported.Notes); Assert.Single(imported.MigrationOrigins);
        var repeat = await service.PreviewAsync("selected.zip", default); Assert.Equal(1, repeat.AlreadyImported); Assert.False(repeat.CanApply);
        var after = disk.Main!.ToArray(); await service.ApplyAsync(repeat, default);
        Assert.Equal(after, disk.Main); Assert.Equal(1, disk.Writes); Assert.Equal(1, store.Saves);
    }
    [Theory]
    [InlineData(true, false, false)] [InlineData(false, true, false)] [InlineData(false, false, true)]
    public async Task BackupFailureOrMismatchedReadbackNeverChangesLibrary(bool save, bool read, bool tamper)
    {
        var disk = new SessionMemoryDisk { Main = Codec.Encode([], SessionTestRig.Date) };
        await using var client = Client(disk); await client.LoadAsync(default);
        var store = new Store { FailSave = save, FailRead = read, Tamper = tamper };
        var service = new LibraryMigrationService(client, new Source(await Data()), store); var original = disk.Main.ToArray();
        Assert.False((await service.ApplyAsync(await service.PreviewAsync("selected.zip", default), default)).IsCommitted);
        Assert.Equal(original, disk.Main); Assert.Equal(0, disk.Writes); Assert.Empty(client.CurrentSnapshot!.Entries);
    }
    [Fact]
    public async Task PrimaryWriteFailureKeepsVerifiedBackupAndUnchangedCurrentState()
    {
        var disk = new SessionMemoryDisk { Main = Codec.Encode([], SessionTestRig.Date), Fail = true };
        await using var client = Client(disk); await client.LoadAsync(default); var store = new Store();
        var service = new LibraryMigrationService(client, new Source(await Data()), store); var before = disk.Main.ToArray();
        Assert.False((await service.ApplyAsync(await service.PreviewAsync("selected.zip", default), default)).IsCommitted);
        Assert.Equal(before, disk.Main);
        Assert.Equal(LibraryBackupService.ContentDigest(Codec, before), LibraryBackupService.ContentDigest(Codec, store.Saved));
        Assert.Empty(client.CurrentSnapshot!.Entries);
    }
    [Fact]
    public async Task ConsentCannotCrossWriterOrSurviveAnInterveningEdit()
    {
        var disk = new SessionMemoryDisk { Main = Codec.Encode([], SessionTestRig.Date) };
        await using var client = Client(disk); await client.LoadAsync(default); var store = new Store(); var data = await Data();
        var service = new LibraryMigrationService(client, new Source(data), store);
        var preview = await service.PreviewAsync("selected.zip", default);
        await client.LoadAsync(default); // Same bytes, new writer: old consent must not become valid.
        Assert.Equal(ClientOutcome.Conflict, (await service.ApplyAsync(preview, default)).Outcome);
        var current = await service.PreviewAsync("selected.zip", default);
        await client.Required().ApplyMigrationAsync(MigrationResolver.Preview(data, [], Codec));
        Assert.Equal(ClientOutcome.Conflict, (await service.ApplyAsync(current, default)).Outcome); Assert.Equal(0, store.Saves);
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task CancellationBeforeDurablePhaseDoesNotWrite(bool duringBackup)
    {
        var disk = new SessionMemoryDisk { Main = Codec.Encode([], SessionTestRig.Date) };
        await using var client = Client(disk); await client.LoadAsync(default); var store = new Store();
        var service = new LibraryMigrationService(client, new Source(await Data()), store);
        var preview = await service.PreviewAsync("selected.zip", default); using var cancel = new CancellationTokenSource();
        if (duringBackup) store.OnSave = _ => cancel.Cancel(); else cancel.Cancel();
        Assert.Equal(ClientOutcome.Cancelled, (await service.ApplyAsync(preview, cancel.Token)).Outcome);
        Assert.Equal(0, disk.Writes); Assert.Empty(client.CurrentSnapshot!.Entries);
    }
    [Fact]
    public async Task CancellationDuringAtomicWriteReportsCommittedAndPublishesMemory()
    {
        using var cancel = new CancellationTokenSource();
        var disk = new SessionMemoryDisk { Main = Codec.Encode([], SessionTestRig.Date), BeforeSave = () => { cancel.Cancel(); return Task.CompletedTask; } };
        await using var client = Client(disk); await client.LoadAsync(default);
        var service = new LibraryMigrationService(client, new Source(await Data()), new Store());
        var result = await service.ApplyAsync(await service.PreviewAsync("selected.zip", default), cancel.Token);
        Assert.Equal(ClientOutcome.CommittedWithWarning, result.Outcome); Assert.Single(client.CurrentSnapshot!.Entries); Assert.Equal(1, disk.Writes);
    }
    [Fact]
    public async Task ConcurrentConfirmationCannotApplySamePreviewTwice()
    {
        var disk = new SessionMemoryDisk { Main = Codec.Encode([], SessionTestRig.Date) };
        await using var client = Client(disk); await client.LoadAsync(default); var store = new Store();
        var service = new LibraryMigrationService(client, new Source(await Data()), store); var preview = await service.PreviewAsync("selected.zip", default);
        var results = await Task.WhenAll(service.ApplyAsync(preview, default), service.ApplyAsync(preview, default));
        Assert.Single(results, x => x.IsCommitted); Assert.Single(results, x => x.Outcome == ClientOutcome.Conflict);
        Assert.Equal(1, store.Saves); Assert.Equal(1, disk.Writes);
    }
    [Fact]
    public async Task RunningGamePreventsMigrationBeforeAnyBackupOrWrite()
    {
        await using var rig = new SessionTestRig(); await rig.Start(); var before = rig.Disk.Writes; bool backup = false;
        var preview = await rig.Library.PreviewActiveMigrationAsync(await Data());
        await Assert.ThrowsAsync<SessionConflictException>(() => rig.Library.ApplyActiveMigrationAsync(preview, (_, _, _) => { backup = true; return Task.CompletedTask; }));
        Assert.False(backup); Assert.Equal(before, rig.Disk.Writes);
    }
    [Fact]
    public async Task LatePreviewAfterPageCancellationCannotEnableConfirmation()
    {
        var disk = new SessionMemoryDisk { Main = Codec.Encode([], SessionTestRig.Date) };
        await using var client = Client(disk); await client.LoadAsync(default);
        var data = await Data(); var done = new TaskCompletionSource<ImportDataset>(); var source = new Source(data) { Pending = _ => done.Task };
        await using var vm = new LibraryMigrationViewModel(new LibraryMigrationService(client, source, new Store()));
        var pending = vm.PreviewAsync("selected.zip"); Assert.True(vm.IsBusy); Assert.False(vm.CanSelect);
        vm.Cancel(); done.SetResult(data); await pending;
        Assert.Null(vm.Preview); Assert.False(vm.CanApply); Assert.Contains("取消", vm.Message); Assert.Equal(0, disk.Writes);
        source.Pending = null; await vm.PreviewAsync("selected.zip"); Assert.True(vm.CanApply);
        vm.Clear(); Assert.False(vm.CanApply); Assert.Null(vm.Preview); Assert.Equal(0, disk.Writes);
    }
    [Fact]
    public async Task ViewModelShowsConflictsAndDuplicatesWithoutSilentlySelectingThem()
    {
        var data = await Data(); data = data with { Games = [data.Games[0], data.Games[0]], RawCount = 2 };
        await using var client = Client(new() { Main = Codec.Encode([], SessionTestRig.Date) }); await client.LoadAsync(default);
        await using var vm = new LibraryMigrationViewModel(new LibraryMigrationService(client, new Source(data), new Store()));
        await vm.PreviewAsync("selected.zip"); Assert.False(vm.CanApply); Assert.Equal(2, vm.Rows.Length);
        Assert.All(vm.Rows, r => Assert.Contains("冲突", r.Action));
    }
    [Fact]
    public async Task ViewModelCannotRelabelDurableCommitAsCancellation()
    {
        await using var client = Client(new() { Main = Codec.Encode([], SessionTestRig.Date) }); await client.LoadAsync(default);
        await using var vm = new LibraryMigrationViewModel(new LibraryMigrationService(client, new Source(await Data()), new Store()));
        await vm.PreviewAsync("selected.zip");
        await vm.ApplyAsync((_, _) => { vm.Cancel(); return Task.FromResult(new ClientResult(ClientOutcome.CommittedWithWarning, "已保存，请勿重复提交。")); });
        Assert.Contains("已保存", vm.Message); Assert.False(vm.CanApply);
    }
    [Fact]
    public async Task SelectedPublicFormatZipImportsThroughOpenNativeWriterWithoutTestMarkerAndReopens()
    {
        var root = Path.Combine(Path.GetTempPath(), "the-order-settings-migration-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root); var selected = Path.Combine(root, "官方格式.zip"); var target = Path.Combine(root, "TestProfile");
        try
        {
            using (var zip = ZipFile.Open(selected, ZipArchiveMode.Create))
                foreach (var name in new[] { "data.galgames.json", "data.galgameSources.json", "data.categoryGroups.json" })
                    zip.CreateEntryFromFile(Path.Combine(AppContext.BaseDirectory, "fixtures", "migration", "potatovn", "public-1.8.0", name), name);
            var original = File.ReadAllBytes(selected);
            var configRequest = new NativeProfileRequest(NativeProfileKind.Test, target);
            using (var profile = NativeProfileStore.Open(configRequest, Codec))
            {
                var config = profile.Configuration(false);
                await using var client = new OfflineLibraryClient(new IsolatedLibraryFactory(Codec, config), new LegacyClientTextRules());
                await client.LoadAsync(default);
                var store = new WindowsLibraryBackupStore(config, Codec); var service = new LibraryMigrationService(client, new WindowsLibraryMigrationSource(new PotatoVnImportAdapter()), store);
                var preview = await service.PreviewAsync(selected, default); Assert.Equal(5, preview.Added);
                Assert.True((await service.ApplyAsync(preview, default)).IsCommitted);
                var backup = Assert.Single((await store.ReadStateAsync(default)).Records); Assert.Equal("导入前", backup.Reason);
                Assert.Empty(Codec.Decode(await store.ReadSnapshotAsync(backup.Id, default)));
                var repeat = await service.PreviewAsync(selected, default); Assert.Equal(5, repeat.AlreadyImported);
                Assert.Equal(143580, client.CurrentSnapshot!.Entries.Sum(e => e.EffectivePlaySeconds));
            }
            using (var profile = NativeProfileStore.Open(configRequest, Codec))
            {
                await using var lib = TransactionalLibrary.Open(Codec, profile.CreateLibraryPersistence());
                Assert.Equal(5, lib.Snapshot.Entries.Length); Assert.Equal(143580, lib.Snapshot.Entries.Sum(e => e.EffectivePlaySeconds));
            }
            Assert.False(File.Exists(Path.Combine(target, MigrationTestTarget.MarkerName))); Assert.Equal(original, File.ReadAllBytes(selected));
        }
        finally
        {
            if (!Path.GetFullPath(root).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(root).StartsWith("the-order-settings-migration-")) throw new InvalidOperationException();
            Directory.Delete(root, true);
        }
    }
    [Theory]
    [InlineData("unknown.zip", "not zip")] [InlineData("not-export.zip", "[]")] [InlineData("source.json", "[]")]
    public async Task PickerSourceRejectsNonArchiveInputsWithoutProbingGamePaths(string name, string contents)
    {
        var root = Path.Combine(Path.GetTempPath(), "the-order-settings-input-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, name); await File.WriteAllTextAsync(path, contents);
            await Assert.ThrowsAsync<InvalidDataException>(() => new WindowsLibraryMigrationSource(new PotatoVnImportAdapter()).ReadAsync(path, default));
            Assert.Equal(contents, await File.ReadAllTextAsync(path));
        }
        finally { Directory.Delete(root, true); }
    }
}
