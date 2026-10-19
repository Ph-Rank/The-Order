using System.Collections.Immutable;
using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using TheOrder.Application;
using TheOrder.Application.Migrations;
using TheOrder.Domain;
using TheOrder.Infrastructure.Legacy;
using TheOrder.Infrastructure.Migration;
using TheOrder.Platform.Windows;
using Xunit;
using Xunit.Abstractions;

namespace TheOrder.Tests;

public sealed class MigrationTests(ITestOutputHelper output)
{
    private static readonly NativeLibraryCodec Codec = new();
    private const string Date = "2026-10-19T01:00:00.000Z";
    private static string Id(int n) => $"{n:00000000}-1111-4111-8111-111111111111";
    private static Dictionary<string, object?> Source(int n = 1) => new()
    {
        ["Uuid"] = Id(n), ["Name"] = new { Value = "合成 " + n, IsLock = true }, ["OriginalName"] = new { Value = "Original " + n },
        ["Ids"] = new string?[] { n.ToString(), (n + 100).ToString() }, ["TotalPlayTime"] = 120,
        ["PlayedTime"] = new Dictionary<string, int>(), ["PlayType"] = 1, ["MyRate"] = 8,
        ["Tags"] = new { Value = new[] { "かな", "🌸" } }, ["Comment"] = "合成笔记", ["Path"] = @"Q:\MigrationSynthetic\game-" + n,
        ["ExePath"] = @"game.exe"
    };
    private static byte[] Archive(object games, object? sources = null, object? categories = null, object? status = null)
    {
        using var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, true))
        {
            Put("data.galgames.json", JsonSerializer.SerializeToUtf8Bytes(games));
            Put("data.dataStatus.json", JsonSerializer.SerializeToUtf8Bytes(status ?? new { GameLiteDbUpgrade = false }));
            if (sources is not null) Put("data.galgameSources.json", JsonSerializer.SerializeToUtf8Bytes(sources));
            if (categories is not null) Put("data.categoryGroups.json", JsonSerializer.SerializeToUtf8Bytes(categories));
            void Put(string name, byte[] bytes) { using var stream = zip.CreateEntry(name, CompressionLevel.Fastest).Open(); stream.Write(bytes); }
        }
        return memory.ToArray();
    }
    private static Task<ImportDataset> Parse(byte[] bytes, CancellationToken cancellation = default)
        => new PotatoVnImportAdapter().ReadAsync(new MemoryStream(bytes), ImportHash.Bytes(bytes), cancellation);
    private static async Task<ImportDataset> One(Dictionary<string, object?>? game = null) => await Parse(Archive(new[] { game ?? Source() }));
    private static MigrationPlan Plan(ImportDataset dataset, params LibraryEntry[] target) => MigrationResolver.Preview(dataset, [.. target], Codec);
    private static LibraryEntry Entry(string id = "existing") => new(id, "Target personal name", true, null, [], null, "planned", null, false, [], [], "", 0, 0, [], Date, null, null, null, new(false, null));
    private static LibraryEntry SameIdentity(ImportGame g) => Entry() with
    {
        Metadata = new("123", "Catalog title", "", [], null, null, null, null, "", null, null, [], [], [], [],
            [.. g.Identities.Select(i => new WorkReference(i.Provider, i.Id, null))], null, null, Date),
        Installations = [.. g.Installations.Select(i => new Installation("existing-install", i.Folder, i.Executable, []))]
    };
    [Fact]
    public async Task OfficialPublicLegacyFixtureIsReadWithExactMinuteAccounting()
    {
        var fixture = Path.Combine(AppContext.BaseDirectory, "fixtures", "migration", "potatovn", "public-1.8.0");
        using var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, true))
            foreach (var name in new[] { "data.galgames.json", "data.galgameSources.json", "data.categoryGroups.json" })
            { using var stream = zip.CreateEntry(name).Open(); stream.Write(File.ReadAllBytes(Path.Combine(fixture, name))); }
        var data = await Parse(memory.ToArray());
        Assert.Equal(5, data.RawCount); Assert.Equal(5, data.Games.Length); Assert.Equal(143580, data.Games.Sum(g => g.TotalSeconds));
        Assert.Equal(39300, data.Games.SelectMany(g => g.Days).Sum(d => d.Seconds));
        Assert.Equal(5, data.Games.Sum(g => g.Installations.Length));
        Assert.Contains(data.Games.SelectMany(g => g.Issues), i => i.Code == "INVALID_EXTERNAL_ID_IGNORED");
        Assert.DoesNotContain(data.Games.SelectMany(g => g.Identities), i => i.Id.StartsWith("ymgal", StringComparison.Ordinal));
        output.WriteLine("Pinned public upstream 1.8.0 synthetic fixture; not a private user export. 5 records, 143580 seconds, 39300 dated aggregate seconds.");
    }
    [Fact]
    public async Task EmptyOfficialContainerProducesZeroOperations()
    { var data = await Parse(Archive(System.Array.Empty<object>())); var plan = Plan(data); Assert.Empty(plan.Rows); Assert.Empty(plan.Operations); }
    [Fact]
    public async Task GenericEmptyJsonIsNotMisidentifiedAsPotatoVn()
    { await Assert.ThrowsAsync<InvalidDataException>(() => Parse("[]"u8.ToArray())); }
    [Fact]
    public async Task UnicodeOriginalAliasesNotesAndPersonalRatherThanCatalogRatingSurvive()
    {
        var s = Source(); s["Name"] = new { Value = "素晴日・かな🌸 \"quoted\"" }; s["OriginalName"] = new { Value = "素晴らしき日々" };
        s["ChineseName"] = new { Value = "美好的每一天" }; s["Rating"] = new { Value = 2.3 }; s["MyRate"] = 9;
        var data = await One(s); using var p = new MemoryPersistence(); await using var lib = TransactionalLibrary.Open(Codec, p, () => Date);
        await lib.ApplyMigrationAsync(Plan(data)); var e = Assert.Single(Codec.Decode(p.Main!));
        Assert.Equal("素晴日・かな🌸 \"quoted\"", e.Name); Assert.Equal(9, e.PersonalRating); Assert.Equal("合成笔记", e.Notes);
        var origin = Assert.Single(e.MigrationOrigins); Assert.Equal("素晴らしき日々", origin.OriginalTitle); Assert.Contains("美好的每一天", origin.Aliases);
        Assert.Null(e.Metadata); Assert.Contains(origin.Identities, x => x.Provider == "vndb" && x.Id == "v1");
    }
    [Theory]
    [InlineData(0, "planned")][InlineData(1, "playing")][InlineData(2, "completed")]
    [InlineData(3, "paused")][InlineData(4, "dropped")][InlineData(5, "wish")]
    public async Task SourceStatesUseExplicitDictionary(int state, string expected)
    { var s = Source(); s["PlayType"] = state; Assert.Equal(expected, Assert.Single((await One(s)).Games).Status); }
    [Fact]
    public async Task UnknownStateIsHeldForReview()
    { var s = Source(); s["PlayType"] = 50; Assert.Equal(ImportMatch.Conflict, Assert.Single(Plan(await One(s)).Rows).Match); }
    [Fact]
    public async Task DailyAndUndatedTimeNeverInventSessionsOrCounts()
    {
        var s = Source(); s["PlayedTime"] = new Dictionary<string, int> { ["2026/10/18"] = 60 };
        var d = await One(s); using var p = new MemoryPersistence(); await using var lib = TransactionalLibrary.Open(Codec, p, () => Date);
        await lib.ApplyMigrationAsync(Plan(d)); var e = Assert.Single(lib.Snapshot.Entries); var stats = PlaytimeStatistics.Build([e], TimeZoneInfo.Utc);
        Assert.Equal(7200, stats.TotalSeconds); Assert.Equal(3600, stats.UnknownSeconds); Assert.Equal(3600, Assert.Single(stats.Days).ManualSeconds);
        Assert.Empty(e.Sessions); Assert.Equal(0, e.PlayCount); Assert.Equal("daily-aggregate", Assert.Single(e.MigrationOrigins).TimePrecision);
    }
    [Fact]
    public async Task TotalOnlyIsEntirelyUndated()
    {
        var d = await One(); using var p = new MemoryPersistence(); await using var lib = TransactionalLibrary.Open(Codec, p, () => Date);
        await lib.ApplyMigrationAsync(Plan(d)); var stats = PlaytimeStatistics.Build(lib.Snapshot.Entries, TimeZoneInfo.Utc);
        Assert.Empty(stats.Days); Assert.Equal(7200, stats.UnknownSeconds); Assert.Equal(7200, stats.TotalSeconds);
    }
    [Fact]
    public async Task AmbiguousDatesAndUnzonedTimestampsAreNotGuessed()
    {
        var s = Source(); s["PlayedTime"] = new Dictionary<string, int> { ["01/02/2026"] = 60 }; s["LastPlayTime"] = "2026-02-01T22:30:00";
        var g = Assert.Single((await One(s)).Games); Assert.Empty(g.Days); Assert.Null(g.LastPlayed); Assert.Equal("2026-02-01T22:30:00", g.LocalLastPlayed);
        Assert.Contains(g.Issues, i => i.Code == "AMBIGUOUS_DATE_KEPT_UNDATED");
    }
    [Fact]
    public async Task OffsetDatesPreserveTheInstant()
    { var s = Source(); s["LastPlayTime"] = "2026-10-19T01:30:00+09:00"; Assert.Equal(new DateTimeOffset(2026, 10, 18, 16, 30, 0, TimeSpan.Zero), (await One(s)).Games[0].LastPlayed); }
    [Fact]
    public async Task ExplicitSourceZoneConvertsLocalTimeAndIsBoundIntoThePlan()
    {
        var s=Source();s["LastPlayTime"]="2026-10-19T09:00:00";var bytes=Archive(new[]{s});
        var a=await new PotatoVnImportAdapter(TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai")).ReadAsync(new MemoryStream(bytes),ImportHash.Bytes(bytes));
        var b=await new PotatoVnImportAdapter(TimeZoneInfo.Utc).ReadAsync(new MemoryStream(bytes),ImportHash.Bytes(bytes));
        Assert.Equal(new DateTimeOffset(2026,10,19,1,0,0,TimeSpan.Zero),a.Games[0].LastPlayed);Assert.NotEqual(Plan(a).Id,Plan(b).Id);
    }
    [Theory]
    [InlineData("2026-03-08T02:30:00")][InlineData("2026-11-01T01:30:00")]
    public async Task DstGapAndAmbiguousLocalTimesAreNotGuessed(string local)
    {
        var s=Source();s["LastPlayTime"]=local;var bytes=Archive(new[]{s});
        var d=await new PotatoVnImportAdapter(TimeZoneInfo.FindSystemTimeZoneById("America/New_York")).ReadAsync(new MemoryStream(bytes),ImportHash.Bytes(bytes));
        Assert.Null(d.Games[0].LastPlayed);Assert.Equal(local,d.Games[0].LocalLastPlayed);
    }
    [Fact]
    public async Task DatedTotalDisagreementBlocksTheRecord()
    { var s = Source(); s["TotalPlayTime"] = 1; s["PlayedTime"] = new Dictionary<string, int> { ["2026/10/18"] = 60 }; Assert.Equal(ImportMatch.Conflict, Assert.Single(Plan(await One(s)).Rows).Match); }
    [Fact]
    public async Task LongDailyHistoryIsNotLimitedToSmallMetadataObjectSize()
    {
        var s = Source(); s["TotalPlayTime"] = 400;
        s["PlayedTime"] = Enumerable.Range(0, 400).ToDictionary(i => new DateOnly(2024, 1, 1).AddDays(i).ToString("yyyy-MM-dd"), _ => 1);
        Assert.Equal(400, (await One(s)).Games[0].Days.Length);
    }
    [Fact]
    public async Task SameTitleAloneIsOnlyCandidate()
    { var d = await One(); var row=Assert.Single(Plan(d, Entry() with { Name = d.Games[0].Name }).Rows);Assert.Equal(ImportMatch.Candidate,row.Match);Assert.Single(row.CandidateKeys); }
    [Fact]
    public async Task SharedWorkIdWithDifferentInstallationIsNotAutomaticMerge()
    {
        var d = await One(); var e = SameIdentity(d.Games[0]) with { Installations = [new("i", @"Q:\OtherEdition", null, [])] };
        Assert.Equal(ImportMatch.Candidate, Assert.Single(Plan(d, e).Rows).Match);
    }
    [Fact]
    public async Task ParentChildInstallationCollisionIsNotASecondGame()
    {
        var d=await One();var e=Entry() with {Installations=[new("i",d.Games[0].Installations[0].Folder+@"\nested",null,[])]};
        Assert.Equal(ImportMatch.Conflict,Plan(d,e).Rows[0].Match);
        e=e with {Installations=[new("i",d.Games[0].Installations[0].Folder+"-other",null,[])]};Assert.Equal(ImportAction.Create,Plan(d,e).Rows[0].Action);
    }
    [Fact]
    public async Task PathWithoutIdentityAndCrossProviderContradictionsAreConflicts()
    {
        var d = await One(); var e = SameIdentity(d.Games[0]);
        Assert.Equal(ImportMatch.Conflict, Plan(d, e with { Metadata = null }).Rows[0].Match);
        e = e with { Metadata = e.Metadata! with { References = [new("vndb", "v1", null), new("bangumi", "999", null)] } };
        Assert.Equal(ImportMatch.Conflict, Plan(d, e).Rows[0].Match);
    }
    [Fact]
    public async Task DuplicateSourceRecordOrWorkIdNeverSilentlyWins()
    {
        var a = Source(); var b = Source(2); b["Ids"] = a["Ids"];
        Assert.All(Plan(await Parse(Archive(new[] { a, b }))).Rows, r => Assert.Equal(ImportMatch.Conflict, r.Match));
        Assert.All(Plan(await Parse(Archive(new[] { a, a }))).Rows, r => Assert.Equal(ImportMatch.Conflict, r.Match));
    }
    [Theory]
    [InlineData("notes")][InlineData("rating")][InlineData("time")]
    public async Task PersonalFieldConflictsStayInPreview(string field)
    {
        var d = await One(); var e = SameIdentity(d.Games[0]) with { Status = "completed" };
        e = field switch { "notes" => e with { Notes = "keep private note" }, "rating" => e with { PersonalRating = 3 }, _ => e with { TotalPlaySeconds = 8000 } };
        var row = Assert.Single(Plan(d, e).Rows); Assert.Equal(ImportMatch.Conflict, row.Match); Assert.Equal(ImportAction.Skip, row.Action);
    }
    [Fact]
    public async Task ExactIdAndPathFillOnlyMissingPreservesNameAndState()
    {
        var d = await One(); var e = SameIdentity(d.Games[0]) with { Status = "completed" };
        using var p = new MemoryPersistence(Codec.Encode([e], Date)); await using var lib = TransactionalLibrary.Open(Codec, p, () => Date);
        var plan = Plan(d, e); Assert.Equal(ImportMatch.Confirmed, plan.Rows[0].Match); await lib.ApplyMigrationAsync(plan);
        var next = Assert.Single(lib.Snapshot.Entries); Assert.Equal(e.Name, next.Name); Assert.Equal(e.Status, next.Status); Assert.Equal("合成笔记", next.Notes);
        Assert.Equal(ImportHash.Of(e.Metadata), ImportHash.Of(next.Metadata)); Assert.Equal(7200, next.EffectivePlaySeconds); Assert.Equal(0, next.PlayCount);
    }
    [Fact]
    public async Task ChangedSourceRecordAfterImportIsConflictAndUserEditsSurviveRepeat()
    {
        var d = await One(); using var p = new MemoryPersistence(); await using var lib = TransactionalLibrary.Open(Codec, p, () => Date);
        await lib.ApplyMigrationAsync(Plan(d)); var e = lib.Snapshot.Entries[0]; await lib.UpdateAsync(e.Id, new() { Notes = "later edit", Rating = 4d });
        var repeat = MigrationResolver.Preview(d, lib.Snapshot.Entries, Codec); Assert.Equal(ImportAction.AlreadyImported, repeat.Rows[0].Action);
        await lib.ApplyMigrationAsync(repeat); Assert.Equal("later edit", lib.Snapshot.Entries[0].Notes); Assert.Equal(4, lib.Snapshot.Entries[0].PersonalRating);
        var s = Source(); s["TotalPlayTime"] = 140;
        Assert.Equal(ImportMatch.Conflict, MigrationResolver.Preview(await One(s), lib.Snapshot.Entries, Codec).Rows[0].Match);
    }
    [Fact]
    public async Task RepeatDoesNotWriteOrAddTimeAndStalePlanCannotOverwrite()
    {
        var d = await One(); using var p = new MemoryPersistence(); await using var lib = TransactionalLibrary.Open(Codec, p, () => Date);
        var original = Plan(d); await lib.ApplyMigrationAsync(original); var main = p.Main!.ToArray(); var writes = p.Writes;
        var repeat = MigrationResolver.Preview(d, lib.Snapshot.Entries, Codec); await lib.ApplyMigrationAsync(repeat);
        Assert.Equal(main, p.Main); Assert.Equal(writes, p.Writes); Assert.Equal(7200, lib.Snapshot.Entries[0].EffectivePlaySeconds);
        await Assert.ThrowsAsync<StaleLibraryRequestException>(() => lib.ApplyMigrationAsync(original)); Assert.Equal(main, p.Main);
    }
    [Fact]
    public async Task AtomicFailureAndCancellationRetainWholeLibrary()
    {
        var d = await Parse(Archive(new[] { Source(), Source(2) })); using var p = new MemoryPersistence { Fail = true };
        await using var lib = TransactionalLibrary.Open(Codec, p, () => Date);
        await Assert.ThrowsAsync<IOException>(() => lib.ApplyMigrationAsync(Plan(d))); Assert.Empty(lib.Snapshot.Entries); Assert.Null(p.Main);
        p.Fail = false; using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => lib.ApplyMigrationAsync(Plan(d), cancelled.Token)); Assert.Empty(lib.Snapshot.Entries); Assert.Null(p.Main);
    }
    [Fact]
    public async Task MultiInstallationCurrentUuidAndCustomCategoryJoinUsesExplicitIdentity()
    {
        var s = Source(); s.Remove("Path"); s.Remove("ExePath");
        var sources = new[] { new { SourceType = 1, Galgames = new[]
        { new { Galgame = Id(1), EntryId = "one", Path = @"Q:\MigrationSynthetic\a", LocalConfig = new { ExePath = "one.exe" } },
          new { Galgame = Id(1), EntryId = "two", Path = @"Q:\MigrationSynthetic\b", LocalConfig = new { ExePath = "two.exe" } } } } };
        var cats = new[] { new { Type = 2, Name = "My group", Categories = new[] { new { Name = "收藏", GalgamesX = new[] { Id(1) } } } } };
        var data = await Parse(Archive(new[] { s }, sources, cats)); var g = Assert.Single(data.Games);
        Assert.Equal(2, g.Installations.Length); Assert.Contains("My group / 收藏", g.Categories); Assert.Null(g.Favorite); // Label names are not a reliable favorite flag.
    }
    [Theory]
    [InlineData(@"..\escape.exe")][InlineData(@"C:\Windows\system32\cmd.exe")][InlineData("script.cmd")]
    [InlineData("https://example.invalid/game.exe")][InlineData("powershell.exe")]
    public async Task UnsafeLaunchFieldsNeverBecomeExecutableCommands(string executable)
    {
        var s = Source(); s["ExePath"] = executable; s["ExeArguments"] = "-ExecutionPolicy Bypass";
        var g = Assert.Single((await One(s)).Games); Assert.Null(Assert.Single(g.Installations).Executable);
        Assert.Contains(g.Issues, i => i.Code == "UNSAFE_EXECUTABLE_NOT_IMPORTED");
        using var p = new MemoryPersistence(); await using var lib = TransactionalLibrary.Open(Codec, p, () => Date); await lib.ApplyMigrationAsync(Plan(await One(s)));
        Assert.Empty(lib.Snapshot.Entries[0].Installations[0].Arguments);
    }
    [Theory]
    [InlineData(@"Q:\games\..\outside")][InlineData(@"\\server\share\games")][InlineData(@"\\?\Q:\games")]
    [InlineData(@"%USERPROFILE%\game")][InlineData("https://example.invalid")]
    [InlineData(@"Q:\NUL")][InlineData(@"Q:\games\CON.txt")]
    public async Task UnsafeOrUnresolvedGamePathIsBlocked(string path)
    { var s = Source(); s["Path"] = path; var p = Plan(await One(s)); Assert.Equal(ImportMatch.Conflict, p.Rows[0].Match); Assert.Empty(p.Operations); }
    [Fact]
    public async Task CredentialsAndValuesNeverAppearInReports()
    {
        var s = Source(); s["Comment"] = "sensitive-note-never-print"; s["Token"] = "secret-token-never-print";
        var plan = Plan(await One(s)); var text = MigrationReport.Json(plan, 0) + MigrationReport.Markdown(plan, 0);
        Assert.DoesNotContain("sensitive-note-never-print", text); Assert.DoesNotContain("secret-token-never-print", text);
        Assert.DoesNotContain(@"Q:\", text); Assert.DoesNotContain("合成 1", text); Assert.DoesNotContain(Id(1), text);
    }
    [Theory]
    [InlineData("{broken")][InlineData("[{\"Name\":{},\"Name\":{}}]")][InlineData("{\"$type\":\"Some.Executable.Type\"}")]
    [InlineData("SQLite format 3\0private database")]
    public async Task MalformedTypedUnknownOrDatabaseInputIsNotExecutedOrAccepted(string json)
    { await Assert.ThrowsAnyAsync<Exception>(() => Parse(Encoding.UTF8.GetBytes(json))); }
    [Fact]
    public async Task UnknownExplicitVersionAndLiveDbFlagsAreRejected()
    {
        await Assert.ThrowsAsync<InvalidDataException>(() => Parse(Archive(new[] { Source() }, status: new { Version = 500 })));
        await Assert.ThrowsAsync<InvalidDataException>(() => Parse(Archive(new[] { Source() }, status: new { GameLiteDbUpgrade = true })));
    }
    [Theory]
    [InlineData("../escape.json")][InlineData("/absolute.json")][InlineData("C:/absolute.json")]
    public async Task ArchiveTraversalNeverExtractsAnything(string name)
    {
        using var stream = new MemoryStream(); using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true)) zip.CreateEntry(name);
        await Assert.ThrowsAsync<InvalidDataException>(() => Parse(stream.ToArray()));
    }
    [Fact]
    public async Task DuplicateArchiveEntrySymlinkAndOversizedFieldsAreRejected()
    {
        using var stream = new MemoryStream(); using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true))
        { zip.CreateEntry("data.galgames.json"); zip.CreateEntry("DATA.GALGAMES.JSON"); }
        await Assert.ThrowsAsync<InvalidDataException>(() => Parse(stream.ToArray()));
        using var link = new MemoryStream(); using (var zip = new ZipArchive(link, ZipArchiveMode.Create, true)) zip.CreateEntry("link").ExternalAttributes = unchecked((int)0xa1ff0000);
        await Assert.ThrowsAsync<InvalidDataException>(() => Parse(link.ToArray()));
        var s = Source(); s["Comment"] = new string('x', 100001);
        await Assert.ThrowsAsync<InvalidDataException>(() => One(s));
    }
    [Fact]
    public async Task TruncatedArchiveAndCancelledReadFailWithoutPartialData()
    {
        var bytes = Archive(new[] { Source() }); await Assert.ThrowsAnyAsync<Exception>(() => Parse(bytes[..^12]));
        using var ct = new CancellationTokenSource(); ct.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Parse(bytes, ct.Token));
    }
    [Fact]
    public async Task SourceDailyTotalsDoNotMasqueradeAsCompletedSessionsButGenericReliableSessionsWork()
    {
        var data = await One(); var g = data.Games[0] with { TotalSeconds = 7200, Installations = [], Sessions = [new("s", new(2026,10,18,23,30,0,TimeSpan.FromHours(8)),new(2026,10,19,0,30,0,TimeSpan.FromHours(8)),3600)] };
        data = data with { Games = [g] }; using var p = new MemoryPersistence(); await using var lib = TransactionalLibrary.Open(Codec,p,()=>Date);
        await lib.ApplyMigrationAsync(Plan(data)); var zone=TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai"); var stats=PlaytimeStatistics.Build(lib.Snapshot.Entries,zone);
        Assert.Equal(2,stats.Days.Length);Assert.All(stats.Days,d=>Assert.True(d.Estimated));Assert.Equal(3600,stats.Days.Sum(d=>d.Seconds));Assert.Equal(3600,stats.UnknownSeconds);
        Assert.Equal("sessions",lib.Snapshot.Entries[0].MigrationOrigins[0].TimePrecision);
    }
    [Fact]
    public async Task OverlappingSessionsAndInvalidTimeUnitsCannotProducePlan()
    {
        var data=await One();var start=new DateTimeOffset(2026,10,18,0,0,0,TimeSpan.Zero);
        var g=data.Games[0] with { Sessions=[new("1",start,start.AddHours(1),3600),new("2",start,start.AddHours(1),3600)] };
        Assert.Throws<InvalidDataException>(()=>Plan(data with {Games=[g]}));
        Assert.Throws<InvalidDataException>(()=>Plan(data with {Games=[data.Games[0] with {TotalSeconds=-1}]}));
    }
    [Fact]
    public async Task LineagePersistsThroughCodecAndFutureSchemasStayReadOnly()
    {
        var d=await One();using var p=new MemoryPersistence();await using var lib=TransactionalLibrary.Open(Codec,p,()=>Date);await lib.ApplyMigrationAsync(Plan(d));
        var tree=JsonNode.Parse(p.Main!)!;Assert.Equal(3,(int)tree["games"]![0]!["nativeExtras"]!["version"]!);
        Assert.NotEmpty(Assert.Single(Codec.Decode(p.Main!)).MigrationOrigins);
        tree["games"]![0]!["nativeExtras"]!["version"]=5;
        Assert.Equal(LibraryAccess.ValidReadOnly,Codec.Inspect(Encoding.UTF8.GetBytes(tree.ToJsonString()),Date).Access);
        var legacy=Codec.Encode([Entry() with {GuidePaths=[@"Q:\guide.txt"]}],Date);
        Assert.DoesNotContain("migrationOrigins",Encoding.UTF8.GetString(legacy));
    }
    [Fact]
    public async Task ConflictingProvenanceCannotBeDroppedByLaterMerge()
    {
        var d=await One();using var p=new MemoryPersistence();await using var lib=TransactionalLibrary.Open(Codec,p,()=>Date);await lib.ApplyMigrationAsync(Plan(d));
        var o=lib.Snapshot.Entries[0].MigrationOrigins[0];
        Assert.Throws<InvalidDataException>(()=>MigrationLineage.Merge([o],[o with {RecordDigest=new string('a',64)}]));
    }
    [Fact]
    public async Task DedicatedNativeTestProfilePreviewWriteRestartAndRepeat()
    {
        using var lab=new Lab();var target=Path.Combine(lab.Root,"target");MigrationTestTarget.Create(target,Codec);var d=await One();
        var before=lab.Hashes(target);var empty=MigrationTestTarget.Preview(target,Codec);Assert.Empty(empty.Entries);var plan=MigrationTestTarget.PreviewPlan(target,d,Codec);
        Assert.Equal(before,lab.Hashes(target));await MigrationTestTarget.ApplyAsync(target,d,plan.Id,Codec);
        var loaded=MigrationTestTarget.Preview(target,Codec);Assert.Single(loaded.Entries);Assert.Equal(7200,loaded.Entries[0].EffectivePlaySeconds);
        var bytes=File.ReadAllBytes(Path.Combine(target,"library.json"));var again=MigrationTestTarget.PreviewPlan(target,d,Codec);
        var receipt=await MigrationTestTarget.ApplyAsync(target,d,again.Id,Codec);Assert.Equal(0,receipt.Value.Created);Assert.Equal(bytes,File.ReadAllBytes(Path.Combine(target,"library.json")));
    }
    [Fact]
    public async Task EmptyButDifferentTargetAndNormalizedPolicyRequireAnotherPreview()
    {
        using var lab=new Lab();var a=Path.Combine(lab.Root,"a");var b=Path.Combine(lab.Root,"b");MigrationTestTarget.Create(a,Codec);MigrationTestTarget.Create(b,Codec);
        var d=await One();var plan=MigrationTestTarget.PreviewPlan(a,d,Codec);
        await Assert.ThrowsAsync<StaleLibraryRequestException>(()=>MigrationTestTarget.ApplyAsync(b,d,plan.Id,Codec));Assert.Empty(MigrationTestTarget.Preview(b,Codec).Entries);
        var changed=d with {Games=[d.Games[0] with {Notes="other normalization"}]};Assert.NotEqual(plan.Id,MigrationTestTarget.PreviewPlan(a,changed,Codec).Id);
    }
    [Fact]
    public async Task InvalidOneOfTwoRecordsIsReportedAndOnlyValidRecordCanBeCommitted()
    {
        var good=Source();var bad=Source(2);bad["MyRate"]=999;
        var d=await Parse(Archive(new[]{good,bad}));Assert.Equal(2,d.RawCount);Assert.Single(d.Games);Assert.Contains(d.Issues,i=>i.Code=="INVALID_SOURCE_RECORD");
        using var p=new MemoryPersistence();await using var lib=TransactionalLibrary.Open(Codec,p,()=>Date);await lib.ApplyMigrationAsync(Plan(d));Assert.Single(lib.Snapshot.Entries);
    }
    [Fact]
    public void ProductionAndUnmarkedExistingProfilesHaveNoWriteEntryPoint()
    {
        Assert.Throws<NativeProfileException>(()=>MigrationTestTarget.Create(NativeProfilePaths.ProductionRoot,Codec));
        Assert.Throws<NativeProfileException>(()=>MigrationTestTarget.Preview(NativeProfilePaths.ProductionRoot,Codec));
        using var lab=new Lab();var root=Path.Combine(lab.Root,"plain-test");using(var p=NativeProfileStore.Open(new(NativeProfileKind.Test,root),Codec)){}
        Assert.ThrowsAny<Exception>(()=>MigrationTestTarget.Preview(root,Codec));
        Assert.Throws<NativeProfileException>(()=>MigrationTestTarget.Create(root,Codec));
    }
    [Fact]
    public async Task ReadOnlySourceDenyConcurrentWriterAndSourceDigestDoesNotChange()
    {
        using var lab=new Lab();var source=Path.Combine(lab.Root,"source.pvnExport.zip");var bytes=Archive(new[]{Source()});File.WriteAllBytes(source,bytes);
        var before=ImportHash.Bytes(File.ReadAllBytes(source));var time=File.GetLastWriteTimeUtc(source);
        var d=await MigrationTestTarget.ReadSourceAsync(source,new PotatoVnImportAdapter());Assert.Equal(before,d.DocumentDigest);Assert.Equal(time,File.GetLastWriteTimeUtc(source));
        using(var writer=new FileStream(source,FileMode.Open,FileAccess.Write,FileShare.ReadWrite))
            await Assert.ThrowsAsync<IOException>(()=>MigrationTestTarget.ReadSourceAsync(source,new PotatoVnImportAdapter()));
        Assert.Equal(before,ImportHash.Bytes(File.ReadAllBytes(source)));
    }
    [Fact]
    public async Task HardlinkedSourceAndTamperedTargetIdentityAreRefused()
    {
        using var lab=new Lab();var source=Path.Combine(lab.Root,"original.pvnExport.zip");File.WriteAllBytes(source,Archive(new[]{Source()}));
        var linked=Path.Combine(lab.Root,"linked.pvnExport.zip");Assert.True(CreateHardLinkW(linked,source,IntPtr.Zero));
        await Assert.ThrowsAsync<DataIsolationException>(()=>MigrationTestTarget.ReadSourceAsync(linked,new PotatoVnImportAdapter()));
        var root=Path.Combine(lab.Root,"target");MigrationTestTarget.Create(root,Codec);var marker=Path.Combine(root,MigrationTestTarget.MarkerName);
        var json=JsonNode.Parse(File.ReadAllText(marker))!;json["ProfileId"]=Guid.NewGuid().ToString();File.WriteAllText(marker,json.ToJsonString());
        Assert.Throws<NativeProfileException>(()=>MigrationTestTarget.Preview(root,Codec));
    }
    [Theory]
    [InlineData(PersistenceStage.DuringTempWrite)][InlineData(PersistenceStage.BeforeReplace)]
    public async Task NativeAtomicImportFailureLeavesNoPartialGames(PersistenceStage stage)
    {
        using var lab=new Lab();var root=Path.Combine(lab.Root,"target");MigrationTestTarget.Create(root,Codec);var d=await Parse(Archive(new[]{Source(),Source(2)}));
        var before=File.ReadAllBytes(Path.Combine(root,"library.json"));var plan=MigrationTestTarget.PreviewPlan(root,d,Codec);
        await Assert.ThrowsAsync<IOException>(()=>MigrationTestTarget.ApplyAsync(root,d,plan.Id,Codec,probe:new Probe((t,s)=>{if(t==PersistenceTarget.Main&&s==stage)throw new IOException("synthetic");})));
        Assert.Equal(before,File.ReadAllBytes(Path.Combine(root,"library.json")));Assert.Empty(MigrationTestTarget.Preview(root,Codec).Entries);
        Assert.Empty(Directory.GetFiles(root,"*.tmp"));
    }
    [Fact]
    public async Task PostReplaceCancellationIsCommittedAndReopenIsConsistent()
    {
        using var lab=new Lab();var root=Path.Combine(lab.Root,"target");MigrationTestTarget.Create(root,Codec);var d=await One();var plan=MigrationTestTarget.PreviewPlan(root,d,Codec);
        using var ct=new CancellationTokenSource();var receipt=await MigrationTestTarget.ApplyAsync(root,d,plan.Id,Codec,ct.Token,new Probe((t,s)=>{if(t==PersistenceTarget.Main&&s==PersistenceStage.AfterReplace)ct.Cancel();}));
        Assert.True(receipt.CancellationAfterCommit);Assert.Single(MigrationTestTarget.Preview(root,Codec).Entries);
    }
    [Fact]
    public async Task TenThousandRecordParsingResolutionAndCodecRemainBounded()
    {
        var records=Enumerable.Range(1,10000).Select(i=>{var s=Source(i);s["Comment"]="";return s;}).ToArray();var bytes=Archive(records);var clock=Stopwatch.StartNew();
        var d=await Parse(bytes);var parsed=clock.ElapsedMilliseconds;var plan=Plan(d);var planned=clock.ElapsedMilliseconds;
        Assert.Equal(10000,d.Games.Length);Assert.Equal(10000,plan.Operations.Length);Assert.Equal(10000,plan.Rows.Count(r=>r.Action==ImportAction.Create));
        Assert.InRange(planned,0,60000);output.WriteLine(JsonSerializer.Serialize(new{count=10000,bytes=bytes.Length,parseMs=parsed,planAndCodecMs=planned-parsed,privateData=false}));
    }
    private sealed class MemoryPersistence(byte[]? initial=null):ILibraryPersistence
    {
        public byte[]? Main=initial;public int Writes;public bool Fail;
        public byte[]? ReadMain()=>Main;public byte[]? ReadBackup()=>null;
        public Task<PersistenceReceipt> SaveAsync(ReadOnlyMemory<byte> previous,ReadOnlyMemory<byte> candidate)
        {if(Fail)throw new IOException("synthetic failure");Main=candidate.ToArray();Writes++;return Task.FromResult(new PersistenceReceipt());}
        public void Dispose(){}
    }
    private sealed class Probe(Action<PersistenceTarget,PersistenceStage> action):IPersistenceProbe
    {public Task ReachAsync(PersistenceTarget target,PersistenceStage stage){action(target,stage);return Task.CompletedTask;}}
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]
    [return:MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLinkW(string newName,string existingName,IntPtr security);
    private sealed class Lab:IDisposable
    {
        public string Root{get;}=Path.Combine(Path.GetTempPath(),"the-order-migration-v1-"+Guid.NewGuid().ToString("N"));
        public Lab()=>Directory.CreateDirectory(Root);
        public string Hashes(string directory)=>string.Join("\n",Directory.GetFiles(directory).Order().Select(f=>Path.GetFileName(f)+":"+ImportHash.Bytes(File.ReadAllBytes(f))));
        public void Dispose(){var root=Path.GetFullPath(Root);if(!root.StartsWith(Path.GetFullPath(Path.GetTempPath()),StringComparison.OrdinalIgnoreCase)||!Path.GetFileName(root).StartsWith("the-order-migration-v1-",StringComparison.Ordinal))throw new InvalidOperationException();Directory.Delete(root,true);}
    }
}
