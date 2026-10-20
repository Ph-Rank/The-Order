using System.Collections.Immutable;
using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using TheOrder.Application;
using TheOrder.Application.Migrations;
using TheOrder.Domain;
using TheOrder.Infrastructure.Legacy;
using TheOrder.Infrastructure.Migration;
using TheOrder.Platform.Windows;
using Xunit;

namespace TheOrder.Tests;

// All records, directories and ZIPs are synthetic. No game or private Profile is accessed.
public sealed class MigrationDatedHistoryTests
{
    private const string Now = "2026-10-20T12:00:00.000Z";
    private static readonly NativeLibraryCodec Codec = new();
    private static ImportSession Session(string id = "s1", string start = "2026-10-19T02:00:00Z", long seconds = 1800)
    { var at = DateTimeOffset.Parse(start, CultureInfo.InvariantCulture); return new(id, at, at.AddSeconds(seconds), seconds); }
    private static ImportGame Game() => new("dated-source", "Dated synthetic VN", "", [], [new("vndb", "v701")],
        [new("i", @"Q:\DatedMigration\Synthetic", @"Q:\DatedMigration\Synthetic\game.exe")], [], [], "playing", null, null, "", 7200, 100,
        [new(new(2026, 10, 18), 3600)], [Session()], DateTimeOffset.Parse("2026-10-20T09:00:00+09:00"), null, null, null, null, []);
    private static ImportDataset Data(ImportGame? game = null) => new("potatovn", "potatovn-export-json-v1", ImportHash.Text("dated synthetic ZIP"), 1, [game ?? Game()], []);
    private static LibraryEntry Target() => ManagerInterchangeTests.Entry("dated-target") with
    {
        Name = "Dated synthetic VN", TotalPlaySeconds = 900, PlayCount = 2, LastPlayedAt = "2026-10-18T01:10:00.000Z",
        Installations = [new("i", @"Q:\DatedMigration\Synthetic", @"Q:\DatedMigration\Synthetic\game.exe", [])], PreferredInstallationId = "i",
        Sessions = [new("11111111-1111-4111-8111-111111111111", "2026-10-18T01:00:00.000Z", "2026-10-18T01:10:00.000Z", 600)],
        ManualPlaytime = [new("22222222-2222-4222-8222-222222222222", "2026-10-18", 120, "local correction", Now)]
    };
    private static MigrationPlan Plan(ImportDataset data, LibraryEntry target, ImportHistoryChoice choice = ImportHistoryChoice.AddSourceTotal)
        => MigrationResolver.Preview(data, [target], Codec, reviews: [new(ImportHash.Key(data.Manager, data.Games[0].SourceId), ImportHash.Text(target.Id)[..20], true, choice)]);
    private static async Task<LibraryEntry> Commit(ImportDataset data, LibraryEntry target, ImportHistoryChoice choice = ImportHistoryChoice.AddSourceTotal)
    {
        var disk = new SessionMemoryDisk { Main = Codec.Encode([target], Now) };
        await using var library = TransactionalLibrary.Open(Codec, new SessionMemoryPersistence(disk), () => Now);
        await library.ApplyMigrationAsync(Plan(data, library.Snapshot.Entries[0], choice));
        return Assert.Single(Codec.Decode(disk.Main!));
    }
    private static void SameHistoryAndPersonalFields(LibraryEntry expected, LibraryEntry actual)
    {
        Assert.Equal(ImportHash.Of(expected with { MigrationOrigins = [] }), ImportHash.Of(actual with { MigrationOrigins = [] }));
        Assert.Equal(ImportHash.Of(expected.MigrationOrigins[0].HistoryDates), ImportHash.Of(actual.MigrationOrigins[0].HistoryDates));
    }
    private static LibraryEntry LegacyAddition(ImportDataset data, LibraryEntry target, LibraryEntry merged)
        => Codec.Decode(Codec.Encode([merged with { TotalPlaySeconds = target.TotalPlaySeconds + data.Games[0].TotalSeconds,
            Sessions = target.Sessions, ManualPlaytime = target.ManualPlaytime,
            MigrationOrigins = [.. merged.MigrationOrigins.Select(o => o with { HistoryDates = null })] }], Now))[0];

    [Fact]
    public async Task AdditionCarriesDailyDatesRealIntervalsAndLatestKnownInstantIntoCharts()
    {
        var original = Target(); var data = Data(); var plan = Plan(data, original); var row = Assert.Single(plan.Rows);
        Assert.Equal(7200, row.HistoryAdditionalSeconds); Assert.Equal(5400, row.HistoryNewlyDatedSeconds);
        var result = await Commit(data, original);
        Assert.Equal(8220, result.EffectivePlaySeconds); Assert.Equal(2, result.PlayCount);
        Assert.Contains(original.Sessions[0], result.Sessions); Assert.Contains(original.ManualPlaytime[0], result.ManualPlaytime);
        var imported = result.Sessions.Single(s => s.Id != original.Sessions[0].Id);
        Assert.Equal(Game().Sessions[0].Start, DateTimeOffset.Parse(imported.StartedAt)); Assert.Equal(1800, imported.Seconds);
        Assert.Equal("2026-10-20T00:00:00.000Z", result.LastPlayedAt);
        var stats = PlaytimeStatistics.Build([result], TimeZoneInfo.Utc);
        Assert.Equal(8220, stats.TotalSeconds); Assert.Equal(2100, stats.UnknownSeconds);
        Assert.Equal(4320, stats.Days.Single(d => d.Date == new DateOnly(2026, 10, 18)).Seconds);
        Assert.Equal(1800, stats.Days.Single(d => d.Date == new DateOnly(2026, 10, 19)).Seconds);
        var dashboard = StatisticsDashboardBuilder.Build(new([result], StatisticsPeriod.AllTime, new(2026, 10, 20), TimeZoneInfo.Utc));
        Assert.Equal(2, dashboard.ActiveDays); Assert.Equal(6120, dashboard.PeriodSeconds);
        Assert.Equal(original.AddedAt, result.AddedAt); Assert.Equal(original.Notes, result.Notes); Assert.Equal(original.PersonalRating, result.PersonalRating);
    }

    [Fact]
    public async Task ReopenAndSourceGrowthOnlyAddNewSecondsAndDatedDeltasOnce()
    {
        var first = await Commit(Data(), Target());
        Assert.Equal(ImportAction.AlreadyImported, MigrationResolver.Preview(Data(), [first], Codec).Rows[0].Action);
        var repeated = await Commit(Data(), first); SameHistoryAndPersonalFields(first, repeated);
        var updated = Game() with { TotalSeconds = 9000, Days = [new(new(2026, 10, 18), 4200), new(new(2026, 10, 20), 1200)] };
        var second = await Commit(Data(updated), repeated);
        Assert.Equal(10020, second.EffectivePlaySeconds); Assert.Equal(first.PlayCount, second.PlayCount);
        Assert.Equal(2, second.Sessions.Length); Assert.Equal(3, second.ManualPlaytime.Length);
        var stats = PlaytimeStatistics.Build([second], TimeZoneInfo.Utc); Assert.Equal(2100, stats.UnknownSeconds);
        Assert.Equal(10020, stats.TotalSeconds); Assert.Equal(7920, stats.Days.Sum(d => d.Seconds));
        SameHistoryAndPersonalFields(second, await Commit(Data(updated), second));
    }

    [Fact]
    public async Task ExistingNativeImportHasDatedReceiptsBeforeItsFirstExplicitAddition()
    {
        var data = Data(); var initial = MigrationResolver.Preview(data, [], Codec);
        var entry = Codec.Decode(Codec.Encode([MigrationProjection.Apply(initial.Operations[0], data, null, Now)], Now))[0];
        Assert.NotNull(entry.MigrationOrigins[0].HistoryDates);
        var changed = Game() with { TotalSeconds = 7800, Days = [new(new(2026, 10, 18), 4200)] };
        var next = await Commit(Data(changed), entry);
        Assert.Equal(7800, next.EffectivePlaySeconds); Assert.Single(next.Sessions); Assert.Single(next.ManualPlaytime);
        Assert.Equal(4200, next.ManualPlaytime[0].Seconds); Assert.Equal(100, next.PlayCount);
    }

    [Fact]
    public async Task ReimportOfAnUnchangedV6AdditionOffersDateRepairWithoutAddingAnyTime()
    {
        var data = Data(); var target = Target(); var legacy = LegacyAddition(data, target, await Commit(data, target));
        using var json = JsonDocument.Parse(Codec.Encode([legacy], Now)); Assert.Equal(6, json.RootElement.GetProperty("games")[0].GetProperty("nativeExtras").GetProperty("version").GetInt32());
        var plan = MigrationResolver.Preview(data, [legacy], Codec); var row = Assert.Single(plan.Rows);
        Assert.Equal(ImportAction.FillMissing, row.Action); Assert.Contains("history-dates", row.Fields);
        Assert.Equal(0, row.HistoryAdditionalSeconds); Assert.Equal(5400, row.HistoryNewlyDatedSeconds);
        var disk = new SessionMemoryDisk { Main = Codec.Encode([legacy], Now) };
        await using var lib = TransactionalLibrary.Open(Codec, new SessionMemoryPersistence(disk), () => Now);
        await lib.ApplyMigrationAsync(plan); var result = Assert.Single(Codec.Decode(disk.Main!));
        Assert.Equal(legacy.EffectivePlaySeconds, result.EffectivePlaySeconds); Assert.Equal(2, result.PlayCount);
        Assert.Equal(2100, PlaytimeStatistics.Build([result], TimeZoneInfo.Utc).UnknownSeconds);
        Assert.Equal(legacy.MigrationOrigins[0].Review, result.MigrationOrigins[0].Review);
        Assert.Empty(MigrationResolver.Preview(data, [result], Codec).Operations);
    }

    [Fact]
    public async Task ChangedSourceCannotSilentlyRepairAnOldReviewedSnapshot()
    {
        var data = Data(); var legacy = LegacyAddition(data, Target(), await Commit(data, Target()));
        var changed = Game() with { Days = [new(new(2026, 10, 17), 3600)] };
        var row = Assert.Single(MigrationResolver.Preview(Data(changed), [legacy], Codec).Rows);
        Assert.Equal(ImportMatch.Conflict, row.Match); Assert.DoesNotContain("history-dates", row.Fields);
        Assert.Single(row.ReviewCandidates);
    }

    [Fact]
    public async Task AProgressOnlyUpdateCanRepairOldDatesInTheSameReviewedImport()
    {
        var data = Data(); var target = Target(); var legacy = LegacyAddition(data, target, await Commit(data, target))
            with { Status = "planned", LastPlayedAt = target.LastPlayedAt };
        var changed = data with { Games = [data.Games[0] with { Status = "completed" }] };
        var plan = MigrationResolver.Preview(changed, [legacy], Codec);
        Assert.Contains("history-dates", plan.Rows[0].Fields); Assert.Contains("status", plan.Rows[0].Fields);
        var result = MigrationProjection.Apply(plan.Operations[0], changed, legacy, Now);
        Assert.Equal("completed", result.Status); Assert.Equal(legacy.EffectivePlaySeconds, result.EffectivePlaySeconds);
        Assert.Equal("2026-10-20T00:00:00.000Z", result.LastPlayedAt);
        Assert.Equal(2100, PlaytimeStatistics.Build([result], TimeZoneInfo.Utc).UnknownSeconds);
    }

    [Fact]
    public async Task FirstContributionDeduplicatesAnExactSharedSessionWithoutInventingLaunches()
    {
        var target = Target(); var old = target.Sessions[0];
        var source = Game() with { Sessions = [Session("duplicate", old.StartedAt, old.Seconds), Session()] };
        var row = Assert.Single(Plan(Data(source), target).Rows);
        Assert.Equal(600, row.HistoryDeduplicatedSeconds); Assert.Equal(6600, row.HistoryAdditionalSeconds);
        var result = await Commit(Data(source), target);
        Assert.Equal(7620, result.EffectivePlaySeconds); Assert.Equal(2, result.Sessions.Length); Assert.Equal(2, result.PlayCount);
        SameHistoryAndPersonalFields(result, await Commit(Data(source), result));
    }

    [Fact]
    public async Task PartialSessionOverlapIsReportedAndCannotCreateAFalseDatedActivity()
    {
        var source = Game() with { Days = [], Sessions = [Session("overlap", "2026-10-18T01:05:00Z", 600)], TotalSeconds = 1800 };
        var row = Assert.Single(Plan(Data(source), Target()).Rows);
        Assert.Contains("OVERLAPPING_SESSION_KEPT_UNDATED", row.HistoryWarnings);
        var result = await Commit(Data(source), Target()); Assert.Single(result.Sessions);
        Assert.Equal(2820, result.EffectivePlaySeconds); Assert.Equal(2100, PlaytimeStatistics.Build([result], TimeZoneInfo.Utc).UnknownSeconds);
        Assert.Equal("overlap", result.MigrationOrigins[0].HistoryDates!.Sessions[0].Disposition);
        SameHistoryAndPersonalFields(result, await Commit(Data(source), result));
    }

    [Theory] [InlineData(true)] [InlineData(false)]
    public async Task LocallyUndoneOrEditedDailyRecordsAreNeverRestoredByReimport(bool undo)
    {
        var first = await Commit(Data(), Target()); var imported = first.ManualPlaytime.Single(m => m.Note == MigrationHistoryDates.DailyNote);
        first = first with { ManualPlaytime = first.ManualPlaytime.Replace(imported, undo ? imported with { Undone = true } : imported with { Seconds = 3000, Note = "user edit" }) };
        var before = first.EffectivePlaySeconds;
        var source = Game() with { TotalSeconds = 8000, Days = [new(new(2026, 10, 18), 4200)] };
        var plan = Plan(Data(source), first); Assert.Contains("LOCAL_DAILY_RECORD_EDIT_PRESERVED", plan.Rows[0].HistoryWarnings);
        var result = await Commit(Data(source), first);
        Assert.Equal(before + 800, result.EffectivePlaySeconds); Assert.Equal(first.ManualPlaytime.ToArray(), result.ManualPlaytime.ToArray());
        Assert.Equal(0, Plan(Data(source), result).Rows[0].HistoryAdditionalSeconds);
    }

    [Fact]
    public async Task AMeasuredSessionExtensionMayAddOnlyItsNewIntervalAndDuration()
    {
        var first = await Commit(Data(), Target());
        var source = Game() with { TotalSeconds = 7800, Sessions = [Session(seconds: 2400)] };
        var result = await Commit(Data(source), first);
        Assert.Equal(8820, result.EffectivePlaySeconds); Assert.Equal(2, result.Sessions.Length);
        Assert.Equal(2400, result.Sessions.Single(s => s.Id != Target().Sessions[0].Id).Seconds);
        Assert.Equal(600, Plan(Data(source), first).Rows[0].HistoryNewlyDatedSeconds);
        SameHistoryAndPersonalFields(result, await Commit(Data(source), result));
    }

    [Fact]
    public async Task SourceSessionRewriteCannotOverwriteAnExistingInterval()
    {
        var first = await Commit(Data(), Target());
        var source = Game() with { TotalSeconds = 7800, Sessions = [Session(start: "2026-10-19T04:00:00Z")] };
        var plan = Plan(Data(source), first);
        Assert.Contains("SOURCE_SESSION_REVISED_OR_LOCAL_RECORD_EDITED", plan.Rows[0].HistoryWarnings);
        var result = await Commit(Data(source), first); Assert.Equal(first.Sessions.ToArray(), result.Sessions.ToArray());
        Assert.Equal(8820, result.EffectivePlaySeconds); Assert.Equal(2700, PlaytimeStatistics.Build([result], TimeZoneInfo.Utc).UnknownSeconds);
    }

    [Fact]
    public async Task RenumberedSourceSessionsWithTheSameInstantsCannotDuplicateKnownHistory()
    {
        var first = await Commit(Data(), Target());
        var source = Game() with { TotalSeconds = 7800, Sessions = [Session("renumbered"), Session("new", "2026-10-19T03:00:00Z", 600)] };
        var result = await Commit(Data(source), first);
        Assert.Equal(8820, result.EffectivePlaySeconds); Assert.Equal(3, result.Sessions.Length); Assert.Equal(2, result.PlayCount);
        Assert.Equal(2100, PlaytimeStatistics.Build([result], TimeZoneInfo.Utc).UnknownSeconds);
    }

    [Fact]
    public async Task DailyOnlyDatesCannotInventAClockTimeOrAPlayCount()
    {
        var source = Game() with { Sessions = [], LastPlayed = null }; var target = Target() with { LastPlayedAt = null };
        var result = await Commit(Data(source), target);
        Assert.Null(result.LastPlayedAt); Assert.Single(result.Sessions); Assert.Equal(target.PlayCount, result.PlayCount);
        Assert.Equal(3900, PlaytimeStatistics.Build([result], TimeZoneInfo.Utc).UnknownSeconds);
    }

    [Fact]
    public async Task LastPlayedUsesRealInstantsAndDoesNotMoveAnExistingLaterTimeBackwards()
    {
        var target = Target() with { LastPlayedAt = "2026-10-20T01:00:00Z" };
        var result = await Commit(Data(), target); Assert.Equal(DateTimeOffset.Parse(target.LastPlayedAt), DateTimeOffset.Parse(result.LastPlayedAt!));
        var source = Game() with { LastPlayed = null };
        result = await Commit(Data(source), Target()); Assert.Equal(NativeSession.Format(source.Sessions[0].End), result.LastPlayedAt);
    }

    [Fact]
    public async Task CrossMidnightRealSessionKeepsItsOffsetAndExistingStatisticsPrecision()
    {
        var source = Game() with { Days = [], Sessions = [Session(start: "2026-10-19T23:45:00+08:00", seconds: 1800)] };
        var result = await Commit(Data(source), Target()); var zone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai");
        var stats = PlaytimeStatistics.Build([result], zone);
        Assert.Equal(900, stats.Days.Single(d => d.Date == new DateOnly(2026, 10, 19)).Seconds);
        Assert.Equal(900, stats.Days.Single(d => d.Date == new DateOnly(2026, 10, 20)).Seconds);
        Assert.All(stats.Days.Where(d => d.Date >= new DateOnly(2026, 10, 19)), d => Assert.True(d.Estimated));
    }

    [Fact]
    public async Task DecreasedSourceTotalsCannotRemoveKnownDatesOrResetTheHighWaterMark()
    {
        var first = await Commit(Data(), Target());
        var source = Game() with { TotalSeconds = 6000, Days = [new(new(2026, 10, 18), 3000)] };
        var result = await Commit(Data(source), first); Assert.Equal(first.EffectivePlaySeconds, result.EffectivePlaySeconds);
        Assert.Equal(first.ManualPlaytime.ToArray(), result.ManualPlaytime.ToArray()); Assert.Equal(7200, result.MigrationOrigins[0].HistoryDates!.AccountedSeconds);
        Assert.Equal(800, Plan(Data(Game() with { TotalSeconds = 8000 }), result).Rows[0].HistoryAdditionalSeconds);
    }

    [Theory] [InlineData("keep")] [InlineData("replace")]
    public async Task OtherHistoryChoicesContinueToPreserveTheExistingDatedFacts(string kind)
    {
        var target = Target(); var result = await Commit(Data(), target, kind == "keep" ? ImportHistoryChoice.KeepExisting : ImportHistoryChoice.UseSourceTotal);
        Assert.Equal(target.Sessions.ToArray(), result.Sessions.ToArray()); Assert.Equal(target.ManualPlaytime.ToArray(), result.ManualPlaytime.ToArray());
        Assert.Null(result.MigrationOrigins[0].HistoryDates); Assert.Equal(target.PlayCount, result.PlayCount);
    }

    [Fact]
    public async Task DatedReceiptsNeedV7AndCannotBeSmuggledIntoAnOlderWritableSchema()
    {
        var result = await Commit(Data(), Target()); var tree = JsonNode.Parse(Codec.Encode([result], Now))!;
        Assert.Equal(7, (int)tree["games"]![0]!["nativeExtras"]!["version"]!);
        tree["games"]![0]!["nativeExtras"]!["version"] = 6;
        Assert.Equal(LibraryAccess.InvalidInput, Codec.Inspect(JsonSerializer.SerializeToUtf8Bytes(tree), Now).Access);
        tree["games"]![0]!["nativeExtras"]!["version"] = 7;
        tree["games"]![0]!["nativeExtras"]!["migrationOrigins"]![0]!["historyDates"]!["accountedSeconds"] = 1;
        Assert.Equal(LibraryAccess.InvalidInput, Codec.Inspect(JsonSerializer.SerializeToUtf8Bytes(tree), Now).Access);
    }

    [Theory] [InlineData("empty-key")] [InlineData("unbounded-key")] [InlineData("unzonable-proof")]
    public async Task InvalidSessionKeysAndUnzonedReceiptsAreRejectedBeforeTheyCanAffectDates(string kind)
    {
        if (kind != "unzonable-proof")
        {
            var source = Game() with { Sessions = [Session(kind == "empty-key" ? "" : new string('x', 201))] };
            Assert.Throws<InvalidDataException>(() => Plan(Data(source), Target())); return;
        }
        var result = await Commit(Data(), Target()); var tree = JsonNode.Parse(Codec.Encode([result], Now))!;
        tree["games"]![0]!["nativeExtras"]!["migrationOrigins"]![0]!["historyDates"]!["sessions"]![0]!["startedAt"] = "2026-10-19T02:00:00";
        Assert.Equal(LibraryAccess.InvalidInput, Codec.Inspect(JsonSerializer.SerializeToUtf8Bytes(tree), Now).Access);
    }

    [Fact]
    public async Task ConflictingDatedReceiptsCannotBeSilentlyDroppedDuringAnEntryMerge()
    {
        var result = await Commit(Data(), Target()); var origin = result.MigrationOrigins[0];
        var changed = origin with { HistoryDates = origin.HistoryDates! with { AccountedSeconds = 8000 } };
        Assert.Throws<InvalidDataException>(() => MigrationLineage.Merge([origin], [changed]));
        var legacy = origin with { HistoryDates = null };
        Assert.NotNull(MigrationLineage.Merge([legacy], [origin])[0].HistoryDates);
    }

    [Fact]
    public async Task FailedAtomicSaveCannotLeavePartialDatesOrSourceAccounting()
    {
        var disk = new SessionMemoryDisk { Main = Codec.Encode([Target()], Now), Fail = true };
        var before = disk.Main.ToArray(); await using var lib = TransactionalLibrary.Open(Codec, new SessionMemoryPersistence(disk), () => Now);
        await Assert.ThrowsAsync<IOException>(async () => await lib.ApplyMigrationAsync(Plan(Data(), Target())));
        Assert.Equal(before, disk.Main); Assert.Equal(ImportHash.Of(Target()), ImportHash.Of(lib.Snapshot.Entries[0])); Assert.Equal(0, disk.Writes);
    }

    [Theory]
    [InlineData("vnite-pouchdb9-mixed.zip")]
    [InlineData("reinamanager-sqlite32.zip")]
    [InlineData("playnite10-sdk-mixed.zip")]
    [InlineData("lunabox-duckdb-csv.zip")]
    public async Task IndependentKnownFormatFixturesRetainAllReliableSourceDatesDuringAddition(string filename)
    {
        var bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixtures", "migration", "interchange", filename));
        using var stream = new MemoryStream(bytes, false); var data = await new ManagerImportAdapter().ReadAsync(stream, ImportHash.Bytes(bytes));
        var source = Assert.Single(data.Games); var target = Target() with { Installations = [], PreferredInstallationId = null,
            Sessions = [], ManualPlaytime = [], TotalPlaySeconds = 60, LastPlayedAt = null };
        var result = await Commit(data, target);
        Assert.Equal(source.TotalSeconds + 60, result.EffectivePlaySeconds); Assert.Equal(source.Sessions.Length, result.Sessions.Length);
        Assert.Equal(source.Days.Sum(d => d.Seconds), result.ManualPlaytime.Sum(d => d.Seconds)); Assert.Equal(2, result.PlayCount);
        Assert.Equal(source.TotalSeconds - source.Sessions.Sum(s => s.Seconds) - source.Days.Sum(d => d.Seconds) + 60,
            PlaytimeStatistics.Build([result], TimeZoneInfo.Utc).UnknownSeconds);
        Assert.Empty(MigrationResolver.Preview(data, [result], Codec).Operations);
        Assert.Equal(ImportHash.Bytes(bytes), data.DocumentDigest);
    }

    [Fact]
    public async Task DatedAdditionAndItsBackupSurviveAnActualIsolatedProfileRestart()
    {
        var root = Path.Combine(Path.GetTempPath(), "the-order-dated-migration-" + Guid.NewGuid().ToString("N"));
        var request = new NativeProfileRequest(NativeProfileKind.Test, root);
        try
        {
            using (var profile = NativeProfileStore.Open(request, Codec))
            {
                using (var p = profile.CreateLibraryPersistence()) await p.SaveAsync(ReadOnlyMemory<byte>.Empty, Codec.Encode([Target()], Now));
                var config = profile.Configuration(false);
                await using var client = new OfflineLibraryClient(new IsolatedLibraryFactory(Codec, config), new LegacyClientTextRules());
                await client.LoadAsync(default); var store = new WindowsLibraryBackupStore(config, Codec);
                var service = new LibraryMigrationService(client, new Source(), store);
                var initial = await service.PreviewAsync("synthetic.zip", default); var row = initial.Rows[0];
                var reviewed = await service.ReviewAsync(initial, new(row.RecordKey, row.ReviewCandidates[0].TargetKey, true, ImportHistoryChoice.AddSourceTotal), default);
                Assert.True((await service.ApplyAsync(reviewed, default)).IsCommitted);
                var backup = Assert.Single((await store.ReadStateAsync(default)).Records);
                Assert.Equal(1020, Codec.Decode(await store.ReadSnapshotAsync(backup.Id, default))[0].EffectivePlaySeconds);
            }
            using (var profile = NativeProfileStore.Open(request, Codec))
            {
                await using var lib = TransactionalLibrary.Open(Codec, profile.CreateLibraryPersistence());
                var result = Assert.Single(lib.Snapshot.Entries); Assert.Equal(2100, PlaytimeStatistics.Build([result], TimeZoneInfo.Utc).UnknownSeconds);
                var repeat = await lib.PreviewActiveMigrationAsync(Data()); Assert.Equal(1, repeat.AlreadyImported); Assert.False(repeat.CanApply);
                Assert.NotNull(result.MigrationOrigins[0].HistoryDates);
            }
        }
        finally
        {
            var full = Path.GetFullPath(root); var temp = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()));
            if (!full.StartsWith(temp + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || !Path.GetFileName(full).StartsWith("the-order-dated-migration-", StringComparison.Ordinal)) throw new InvalidOperationException();
            if (Directory.Exists(full)) Directory.Delete(full, true);
        }
    }
    private sealed class Source : ILibraryMigrationSource
    { public Task<ImportDataset> ReadAsync(string selectedFile, CancellationToken cancellation) => Task.FromResult(Data()); }

    [Fact]
    public async Task PublicPotatoVnFormatKeepsMinuteBasedCalendarDatesDuringReviewedAddition()
    {
        var fixture = Path.Combine(AppContext.BaseDirectory, "fixtures", "migration", "potatovn", "public-1.8.0");
        using var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, true))
            foreach (var name in new[] { "data.galgames.json", "data.galgameSources.json", "data.categoryGroups.json" })
            { using var member = zip.CreateEntry(name).Open(); member.Write(File.ReadAllBytes(Path.Combine(fixture, name))); }
        var bytes = memory.ToArray(); using var input = new MemoryStream(bytes, false);
        var parsed = await new PotatoVnImportAdapter().ReadAsync(input, ImportHash.Bytes(bytes));
        var game = parsed.Games.First(g => !g.Days.IsEmpty && !g.NeedsReview);
        var target = Target() with { Name = game.Name, Metadata = null, Sessions = [], ManualPlaytime = [], TotalPlaySeconds = 60,
            Installations = [], PreferredInstallationId = null, LastPlayedAt = null };
        var data = parsed with { RawCount = 1, Games = [game] };
        var result = await Commit(data, target);
        Assert.Equal(game.TotalSeconds + 60, result.EffectivePlaySeconds); Assert.Empty(result.Sessions);
        Assert.Equal(game.Days.Sum(d => d.Seconds), result.ManualPlaytime.Sum(d => d.Seconds));
        Assert.Equal(game.Days.Select(d => d.Date), result.ManualPlaytime.Select(d => DateOnly.ParseExact(d.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture)));
        Assert.Equal(ImportHash.Bytes(bytes), parsed.DocumentDigest);
    }

    [Theory] [InlineData("zh-CN")] [InlineData("en-US")] [InlineData("ja-JP")]
    public async Task LegacyCultureDependentDayIdsDoNotCreateDuplicateDailyRowsAfterALanguageChange(string sourceCulture)
    {
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            var source = Game() with { Days = [new(new(2026, 1, 9), 3600)] }; var first = await Commit(Data(source), Target());
            var day = first.ManualPlaytime.Single(d => d.Note == MigrationHistoryDates.DailyNote);
            var oldId = ImportHash.GuidFor("potatovn:" + source.SourceId + ":day:" + source.Days[0].Date.ToString(CultureInfo.GetCultureInfo(sourceCulture)));
            first = first with { ManualPlaytime = first.ManualPlaytime.Replace(day, day with { Id = oldId }),
                MigrationOrigins = [.. first.MigrationOrigins.Select(o => o with { HistoryDates = null })] };
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            var changed = source with { Days = [new(source.Days[0].Date, 4200)], TotalSeconds = 7800 };
            var result = await Commit(Data(changed), first);
            Assert.Equal(2, result.ManualPlaytime.Length); Assert.Equal(4200, result.ManualPlaytime.Single(d => d.Id == oldId).Seconds);
            Assert.Equal(8820, result.EffectivePlaySeconds);
        }
        finally { CultureInfo.CurrentCulture = originalCulture; }
    }

    [Fact]
    public async Task LargeDatedHistoryRetainsEverySourceDateAndIntervalWithoutDuplicatingTheTotal()
    {
        const int count = 10000; var start = new DateTimeOffset(1998, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var source = Game() with { TotalSeconds = count * 90,
            Days = [.. Enumerable.Range(0, count).Select(n => new ImportDay(DateOnly.FromDateTime(start.AddDays(n).DateTime), 30))],
            Sessions = [.. Enumerable.Range(0, count).Select(n => new ImportSession("long-" + n, start.AddDays(n).AddHours(1), start.AddDays(n).AddHours(1).AddMinutes(1), 60))] };
        var target = Target() with { Sessions = [], ManualPlaytime = [], TotalPlaySeconds = 60 };
        var clock = System.Diagnostics.Stopwatch.StartNew(); var result = await Commit(Data(source), target);
        Assert.Equal(900060, result.EffectivePlaySeconds); Assert.Equal(count, result.Sessions.Length); Assert.Equal(count, result.ManualPlaytime.Length);
        var stats = PlaytimeStatistics.Build([result], TimeZoneInfo.Utc); Assert.Equal(60, stats.UnknownSeconds);
        Assert.Equal(count, stats.Days.Length); Assert.Equal(900000, stats.Days.Sum(d => d.Seconds));
        Assert.Empty(MigrationResolver.Preview(Data(source), [result], Codec).Operations);
        Console.WriteLine($"dated history benchmark: days={count}; sessions={count}; elapsedMs={clock.ElapsedMilliseconds}");
    }

    [Fact]
    public async Task AFullReceiptLedgerKeepsFurtherSharedSessionsBoundedInsteadOfFailingTheWholeImport()
    {
        var source = Game() with { TotalSeconds = 600000, Days = [], Sessions = [] };
        var first = await Commit(Data(source), Target() with { TotalPlaySeconds = 0, Sessions = [], ManualPlaytime = [] });
        var origin = first.MigrationOrigins[0]; var start = new DateTimeOffset(1998, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var receipts = Enumerable.Range(0, 10000).Select(n => new ImportedSessionCoverage(ImportHash.Text("old-" + n),
            NativeSession.Format(start.AddDays(n)), NativeSession.Format(start.AddDays(n).AddMinutes(1)), 60, null, "overlap")).ToImmutableArray();
        first = first with { TotalPlaySeconds = 600600, Sessions = Target().Sessions,
            MigrationOrigins = [origin with { HistoryDates = new(600000, [], receipts) }] };
        var shared = first.Sessions[0]; source = source with { TotalSeconds = 600600, Sessions = [Session("new-shared", shared.StartedAt, shared.Seconds)] };
        var plan = Plan(Data(source), first); Assert.Contains("SOURCE_DATES_EXCEED_UNALLOCATED_CONTRIBUTION", plan.Rows[0].HistoryWarnings);
        var result = await Commit(Data(source), first); Assert.Equal(601200, result.EffectivePlaySeconds);
        Assert.Equal(10000, result.MigrationOrigins[0].HistoryDates!.Sessions.Length); Assert.Single(result.Sessions);
    }
}
