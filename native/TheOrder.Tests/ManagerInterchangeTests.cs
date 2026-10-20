using System.Collections.Immutable;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using TheOrder.Application;
using TheOrder.Application.Migrations;
using TheOrder.Domain;
using TheOrder.Infrastructure.Legacy;
using TheOrder.Infrastructure.Migration;
using Xunit;

namespace TheOrder.Tests;

public sealed class ManagerInterchangeTests
{
    internal const string At = "2026-10-19T01:00:00.000Z";
    internal static LibraryEntry Entry(string id = "fixture-1") => SessionTestRig.Entry(id, 7200, 3) with
    {
        Name = "跨管理器合成样本・かな 🌸", Status = "playing", PersonalRating = 8, Favorite = true, Notes = "合成笔记\n引号\"、逗号, 与 <标签>。",
        Categories = ["合成分类"], CustomTags = ["自定义标签"], AddedAt = At, LastPlayedAt = At,
        Installations = [new("install", @"Q:\TheOrderInterop\Synthetic", @"Q:\TheOrderInterop\Synthetic\game.exe", [])], PreferredInstallationId = "install",
        Metadata = new("1", "Synthetic VN", "Synthetic original", ["Synthetic alias"], null, null, null, null, "", null, null, [], [], [], [], [new("vndb", "v701", null)], null, null, At),
        Sessions = [new("11111111-1111-4111-8111-111111111111", At, "2026-10-19T01:30:00.000Z", 1800)]
    };
    internal static async Task<ImportDataset> Read(ManagerExportPackage package)
    { using var input = new MemoryStream(package.Archive.ToArray()); return await new ManagerImportAdapter().ReadAsync(input, package.Sha256); }

    [System.Runtime.InteropServices.DllImport("leveldb.dll")]
    private static extern int leveldb_minor_version();
    [Fact]
    public void VniteNativeEngineMatchesThePinnedSnappyCapableBuild()
    {
        Assert.Equal(23, leveldb_minor_version());
        using var manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "leveldb-build.json")));
        var digest = ImportHash.Bytes(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "leveldb.dll")));
        Assert.Equal(manifest.RootElement.GetProperty("sha256").GetString(), digest);
    }

    [Theory]
    [InlineData("vnite-pouchdb9-mixed.zip", "vnite", 2, 7200, 1800, 600)]
    [InlineData("vnite-pouchdb9-snappy.zip", "vnite", 2, 7200, 1800, 600)]
    [InlineData("playnite10-sdk-mixed.zip", "playnite", 3, 12345, 0, 0)]
    [InlineData("reinamanager-sqlite32.zip", "reinamanager", 1, 5400, 1800, 0)]
    [InlineData("lunabox-duckdb-csv.zip", "lunabox", 1, 1800, 1800, 0)]
    public async Task IndependentForeignWritersProduceReadableKnownFormats(string filename, string manager, int raw, long total, long sessions, long days)
    {
        var bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixtures", "migration", "interchange", filename));
        var digest = ImportHash.Bytes(bytes); using var stream = new MemoryStream(bytes, false);
        var data = await new ManagerImportAdapter().ReadAsync(stream, digest);
        Assert.Equal(manager, data.Manager); Assert.Equal(raw, data.RawCount); var game = Assert.Single(data.Games);
        Assert.Equal(total, game.TotalSeconds); Assert.Equal(sessions, game.Sessions.Sum(s => s.Seconds)); Assert.Equal(days, game.Days.Sum(s => s.Seconds));
        Assert.Equal(digest, ImportHash.Bytes(bytes)); Assert.Equal(raw - 1, data.Exclusions.Length);
        Assert.False(game.NeedsReview); Assert.Contains(new ImportedIdentity("vndb", "v701"), game.Identities);
        if (manager == "vnite") Assert.Equal("Foreign VN winning revision", game.Name);
        if (manager == "playnite") { Assert.Equal(8.3, game.Rating); Assert.Equal(7, game.PlayCount); Assert.Contains("second line", game.Notes); }
        // DuckDB emits TIMESTAMPTZ in the export connection's zone, retaining the instant.
        if (manager == "lunabox") Assert.Equal(new DateTime(2026, 1, 2, 14, 45, 0, DateTimeKind.Utc), game.Sessions[0].Start.UtcDateTime);
    }

    [Theory] [InlineData("potatovn")] [InlineData("vnite")] [InlineData("playnite")] [InlineData("reinamanager")] [InlineData("lunabox")]
    public async Task AThousandSyntheticGamesRetainDistinctIdentitiesAndIdempotentPlans(string manager)
    {
        var template = Entry();
        var entries = Enumerable.Range(0, 1000).Select(i => template with { Id = "large-" + i, Name = "Large synthetic VN " + i,
            Installations = [], PreferredInstallationId = null, Metadata = template.Metadata! with { References = [new("vndb", "v" + (100000 + i), null)] } }).ToImmutableArray();
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var data = await Read(await new ManagerArchiveWriter().CreateAsync(manager, entries, CancellationToken.None));
        Assert.Equal(1000, data.Games.Length); Assert.Equal(1000, data.Games.SelectMany(g => g.Identities).Distinct().Count());
        var codec = new NativeLibraryCodec(); var plan = MigrationResolver.Preview(data, [], codec);
        Assert.Equal(1000, plan.Operations.Length);
        var projected = plan.Operations.Select(o => MigrationProjection.Apply(o, data, null, At)).ToImmutableArray();
        var reopened = codec.Decode(codec.Encode(projected, At));
        Assert.Empty(MigrationResolver.Preview(data, reopened, codec).Operations);
        Console.WriteLine($"interchange benchmark manager={manager} games=1000 elapsedMs={clock.ElapsedMilliseconds}");
    }

    [Theory] [InlineData("potatovn")] [InlineData("vnite")] [InlineData("playnite")] [InlineData("reinamanager")] [InlineData("lunabox")]
    public async Task CompatibleExportsCannotInventVisualNovelEvidenceForAnUnidentifiedEntry(string manager)
    {
        var unknown = Entry() with { Metadata = null, CustomTags = [], Categories = [], Name = "Unidentified ordinary fixture" };
        var data = await Read(await new ManagerArchiveWriter().CreateAsync(manager, [unknown], CancellationToken.None));
        Assert.Equal(1, data.RawCount); Assert.Empty(data.Games); Assert.Single(data.Exclusions);
    }

    [Fact]
    public async Task CorruptAndUnknownNativeDatabaseVersionsFailWithoutAWrite()
    {
        using var bytes = new MemoryStream();
        using (var zip = new ZipArchive(bytes, ZipArchiveMode.Create, true))
        { using (var v = zip.CreateEntry("library/database.json").Open()) v.Write(Encoding.UTF8.GetBytes("{\"Version\":999}")); zip.CreateEntry("library/games.db"); }
        bytes.Position = 0; await Assert.ThrowsAsync<InvalidDataException>(() => new ManagerImportAdapter().ReadAsync(bytes, new string('a', 64)));
        using var c = new MemoryStream();
        using (var zip = new ZipArchive(c, ZipArchiveMode.Create, true))
        { using (var v = zip.CreateEntry("library/database.json").Open()) v.Write(Encoding.UTF8.GetBytes("{\"Version\":4}")); zip.CreateEntry("library/games.db"); }
        c.Position = 0; await Assert.ThrowsAsync<InvalidDataException>(() => new ManagerImportAdapter().ReadAsync(c, new string('a', 64)));
    }

    [Fact]
    public async Task ReaderTransportRejectsOversizeCancelledOrMalformedInvocations()
    {
        Assert.Equal(64, TheOrder.Platform.Windows.WindowsIsolatedMigrationSource.Run(["--manager-backup-reader"], new ManagerImportAdapter()));
        using var bytes = new MemoryStream(new byte[1025]);
        await Assert.ThrowsAsync<InvalidDataException>(() => TheOrder.Platform.Windows.WindowsIsolatedMigrationSource.ReadBounded(bytes, 1024, default));
        using var cancel = new CancellationTokenSource(); cancel.Cancel(); bytes.Position = 0;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => TheOrder.Platform.Windows.WindowsIsolatedMigrationSource.ReadBounded(bytes, 2048, cancel.Token));
    }
    [Theory] [InlineData("potatovn")] [InlineData("vnite")] [InlineData("playnite")] [InlineData("reinamanager")] [InlineData("lunabox")]
    public async Task NativeArchiveRoundTripRetainsIdentityPersonalRecordsAndKnownPrecision(string manager)
    {
        var before = Entry(); var package = await new ManagerArchiveWriter().CreateAsync(manager, [before], CancellationToken.None);
        Assert.True(package.Archive.Length > 100); Assert.Equal(64, package.Sha256.Length);
        var data = await Read(package); var game = Assert.Single(data.Games);
        Assert.Equal(manager, data.Manager); Assert.Equal(before.Name, game.Name); Assert.Equal("playing", game.Status);
        Assert.Equal(8, game.Rating); Assert.Equal(before.Notes, game.Notes); Assert.Contains(manager == "potatovn" ? "从 The Order 导入 / 合成分类" : "合成分类", game.Categories);
        Assert.Contains(new ImportedIdentity("vndb", "v701"), game.Identities);
        var install = Assert.Single(game.Installations); Assert.Equal(before.Installations[0].Folder, install.Folder); Assert.Equal(before.Installations[0].Executable, install.Executable);
        Assert.Equal(manager == "lunabox" ? 1800 : 7200, game.TotalSeconds);
        Assert.False(game.NeedsReview); Assert.DoesNotContain(data.Issues, i => i.Code == "INVALID_SOURCE_RECORD");
        if (manager == "lunabox") Assert.Contains(package.Notices, n => n.Field == "time");
        var codec = new NativeLibraryCodec(); var plan = MigrationResolver.Preview(data, [], codec);
        var projected = MigrationProjection.Apply(Assert.Single(plan.Operations), data, null, At);
        var reopened = codec.Decode(codec.Encode([projected], At));
        Assert.Equal(ImportAction.AlreadyImported, MigrationResolver.Preview(data, reopened, codec).Rows[0].Action);
        using var zip = new ZipArchive(new MemoryStream(package.Archive.ToArray())); Assert.NotNull(zip.GetEntry("the-order-supplement.json"));
        if (manager == "vnite") Assert.DoesNotContain(zip.Entries, e => e.FullName.EndsWith("/LOG", StringComparison.Ordinal) || e.FullName.EndsWith("/LOCK", StringComparison.Ordinal));
    }
    [Theory] [InlineData("potatovn")] [InlineData("vnite")] [InlineData("playnite")] [InlineData("reinamanager")] [InlineData("lunabox")]
    public async Task EmptyLibrariesAreValidBackupsRatherThanUnrecognizedFiles(string manager)
    {
        var package = await new ManagerArchiveWriter().CreateAsync(manager, [], CancellationToken.None);
        var data = await Read(package); Assert.Equal(0, data.RawCount); Assert.Empty(data.Games); Assert.Equal(manager, data.Manager);
    }
    [Fact]
    public async Task PlayniteMixedLibraryExcludesOrdinaryGamesAndTitleOnlyHomonyms()
    {
        var package = await new ManagerArchiveWriter().CreateAsync("playnite", [Entry(), Entry("ordinary") with { Name = "Synthetic action title", Metadata = null, CustomTags = [], Categories = [] }], CancellationToken.None);
        // Make the second record a real non-VN genre declaration in the native LiteDB.
        using var temp = new InterchangeTemp();
        using var source = new ZipArchive(new MemoryStream(package.Archive.ToArray()));
        var gameDb = source.Entries.Single(e => e.FullName.Replace('\\', '/') == "library/games.db");
        using (var stream = gameDb.Open()) using (var file = File.Create(Path.Combine(temp.Root, "games.db"))) stream.CopyTo(file);
        using (var db = new LiteDB.LiteDatabase(Path.Combine(temp.Root, "games.db")))
        {
            var collection = db.GetCollection("Game"); var game = collection.FindAll().Single(d => d["Name"].AsString == "Synthetic action title");
            game["GenreIds"] = new LiteDB.BsonArray(); collection.Update(game);
        }
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true))
            foreach (var e in source.Entries)
            { using var target = zip.CreateEntry(e.FullName).Open(); if (e == gameDb) target.Write(File.ReadAllBytes(Path.Combine(temp.Root, "games.db"))); else { using var input = e.Open(); input.CopyTo(target); } }
        using var bytes = new MemoryStream(output.ToArray()); var data = await new ManagerImportAdapter().ReadAsync(bytes, ImportHash.Bytes(output.ToArray()));
        Assert.Equal(2, data.RawCount); Assert.Single(data.Games); Assert.Single(data.Issues, i => i.Code == "NON_GALGAME_OR_UNVERIFIED_IDENTITY_EXCLUDED");
    }
    [Fact]
    public async Task LunasInputSqlIsNeverExecutedOrUsedToReadExternalFiles()
    {
        var package = await new ManagerArchiveWriter().CreateAsync("lunabox", [Entry()], CancellationToken.None);
        using var input = new ZipArchive(new MemoryStream(package.Archive.ToArray())); using var bytes = new MemoryStream();
        using (var output = new ZipArchive(bytes, ZipArchiveMode.Create, true))
            foreach (var entry in input.Entries)
            { using var destination = output.CreateEntry(entry.FullName).Open(); if (entry.FullName.EndsWith(".sql")) destination.Write(Encoding.UTF8.GetBytes("INSTALL httpfs; COPY games FROM 'https://invalid.example/private';")); else { using var stream = entry.Open(); stream.CopyTo(destination); } }
        using var archive = new MemoryStream(bytes.ToArray()); var data = await new ManagerImportAdapter().ReadAsync(archive, ImportHash.Bytes(bytes.ToArray())); Assert.Single(data.Games);
    }
    [Theory] [InlineData("../outside")] [InlineData("/absolute")] [InlineData("C:/outside")] [InlineData("dup\\path")]
    public async Task ArchiveTraversalAndNormalizedDuplicateNamesAreRejected(string name)
    {
        using var bytes = new MemoryStream();
        using (var zip = new ZipArchive(bytes, ZipArchiveMode.Create, true))
        { zip.CreateEntry(name); if (name == "dup\\path") zip.CreateEntry("dup/path"); }
        bytes.Position = 0; await Assert.ThrowsAsync<InvalidDataException>(() => new ManagerImportAdapter().ReadAsync(bytes, new string('a', 64)));
    }
    [Fact]
    public void TypedOrDuplicateMetadataCannotActivateObjectsOrSilentlyReplaceFields()
    {
        Assert.Throws<InvalidDataException>(() => ImportJson.Parse("{\"_type\":\"System.Diagnostics.Process\",\"Name\":\"x\"}"));
        Assert.Throws<InvalidDataException>(() => ImportJson.Parse("{\"a\":1,\"a\":2}"));
        Assert.Throws<InvalidDataException>(() => ImportJson.Parse("{\"a\":{\"$type\":\"x\"}}"));
    }
    [Fact]
    public void CsvHandlesQuotedNewlinesAndRejectsMalformedOrOversizedShapes()
    {
        var rows = InterchangeCsv.Read("id,name\r\n\"1\",\"hello,\n\"\"世界\"\"\"\r\n", CancellationToken.None);
        Assert.Equal("hello,\n\"世界\"", Assert.Single(rows)["name"]);
        Assert.Throws<InvalidDataException>(() => InterchangeCsv.Read("id,id\n1,2", CancellationToken.None));
        Assert.Throws<InvalidDataException>(() => InterchangeCsv.Read("id,name\n1,\"bad", CancellationToken.None));
        Assert.Throws<InvalidDataException>(() => InterchangeCsv.Read("id,name\n1,2,3", CancellationToken.None));
    }
}
