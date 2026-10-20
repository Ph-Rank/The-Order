using System.Collections.Immutable;
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

public sealed class ProgressPolicyTests
{
    private static readonly string[] Priority = ["completed", "playing", "wish", "paused", "dropped", "planned"];
    private static readonly NativeLibraryCodec Codec = new();
    private const string Date = "2026-10-19T00:00:00.000Z";
    private static async Task<ImportDataset> Source(int playType = 1)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new[] { new {
            Uuid = "77777777-1111-4111-8111-111111111111", Name = new { Value = "合成进度测试" },
            Ids = new[] { "123", "456" }, PlayType = playType, TotalPlayTime = 120, MyRate = 0,
            Path = @"Q:\ProgressSynthetic", ExePath = "game.exe", PlayedTime = new Dictionary<string, int> { ["2026/10/18"] = 60 }
        } });
        return await new PotatoVnImportAdapter().ReadAsync(new MemoryStream(bytes), ImportHash.Bytes(bytes));
    }
    private static LibraryEntry Target(ImportGame source, string status) => SessionTestRig.Entry("existing", 3600, 0) with
    {
        Name = "保留的个人名称", Status = status, Notes = "", PreferredInstallationId = "install", Installations = [new("install", source.Installations[0].Folder, source.Installations[0].Executable, [])],
        ManualPlaytime = [new("66666666-1111-4111-8111-111111111111", "2026-10-18", 3600, "原有记录", Date)],
        Metadata = new("123", "目录名", "", [], null, null, null, null, "", null, null, [], [], [], [],
            [new("vndb", "v123", null), new("bangumi", "456", null)], null, null, Date)
    };
    public static IEnumerable<object[]> StatusPairs()
    {
        foreach (var local in Priority)
            foreach (var source in Priority)
                yield return new object[] { local, source, Priority[Math.Min(Array.IndexOf(Priority, local), Array.IndexOf(Priority, source))] };
    }
    [Theory, MemberData(nameof(StatusPairs))]
    public async Task MigrationSelectsHigherProgressForEveryPairWithoutAddingTime(string local, string incoming, string expected)
    {
        var data = await Source(); data = data with { Games = [data.Games[0] with { Status = incoming }] };
        var entry = Target(data.Games[0], local); var disk = new SessionMemoryDisk { Main = Codec.Encode([entry], Date) };
        await using var library = TransactionalLibrary.Open(Codec, new SessionMemoryPersistence(disk));
        var plan = MigrationResolver.Preview(data, library.Snapshot.Entries, Codec);
        Assert.Equal(ImportMatch.Confirmed, plan.Rows[0].Match); Assert.Equal(expected, plan.Rows[0].StatusAfter);
        await library.ApplyMigrationAsync(plan);
        var result = Assert.Single(library.Snapshot.Entries);
        Assert.Equal(expected, result.Status); Assert.Equal(7200, result.EffectivePlaySeconds); Assert.Single(result.ManualPlaytime);
        Assert.Equal(entry.Name, result.Name); Assert.Equal(0, result.PlayCount); Assert.Empty(result.Sessions);
        var before = disk.Main!.ToArray(); var repeated = MigrationResolver.Preview(data, library.Snapshot.Entries, Codec);
        Assert.Equal(ImportAction.AlreadyImported, repeated.Rows[0].Action); await library.ApplyMigrationAsync(repeated); Assert.Equal(before, disk.Main);
    }
    [Fact]
    public async Task HigherProgressCanBeImportedWhileUnrelatedNotesRatingAndTimeConflictsStayUntouched()
    {
        var data = await Source(); data = data with { Games = [data.Games[0] with { Status = "completed", Notes = "来源笔记", Rating = 3 }] };
        var entry = Target(data.Games[0], "dropped") with { Notes = "本地笔记", PersonalRating = 9, TotalPlaySeconds = 1 };
        var disk = new SessionMemoryDisk { Main = Codec.Encode([entry], Date) }; await using var library = TransactionalLibrary.Open(Codec, new SessionMemoryPersistence(disk));
        var plan = MigrationResolver.Preview(data, [entry], Codec); Assert.Contains("status", plan.Rows[0].Fields);
        await library.ApplyMigrationAsync(plan); var result = Assert.Single(library.Snapshot.Entries);
        Assert.Equal("completed", result.Status); Assert.Equal(entry.Notes, result.Notes); Assert.Equal(9, result.PersonalRating);
        Assert.Equal(entry.EffectivePlaySeconds, result.EffectivePlaySeconds); Assert.Equal(entry.ManualPlaytime.ToArray(), result.ManualPlaytime.ToArray());
    }
    [Fact]
    public async Task LegacyImportReplayUpgradesProgressWithoutRestoringRemovedTagsOrDuplicatingLineageAndTime()
    {
        var data = await Source(5); var game = data.Games[0] with { Status = "planned", Categories = ["来源分类"], Tags = ["来源标签"] };
        var legacy = data with { Games = [game] }; var disk = new SessionMemoryDisk { Main = Codec.Encode([], Date) };
        await using (var initial = TransactionalLibrary.Open(Codec, new SessionMemoryPersistence(disk)))
            await initial.ApplyMigrationAsync(MigrationResolver.Preview(legacy, [], Codec));
        var edited = Codec.Decode(disk.Main!)[0] with { Status = "dropped", Notes = "后来写的笔记", Categories = [], CustomTags = [] };
        disk.Main = Codec.Encode([edited], Date);
        await using var current = TransactionalLibrary.Open(Codec, new SessionMemoryPersistence(disk));
        foreach (var progress in new[] { "wish", "completed" })
        {
            var fresh = data with { Games = [game with { Status = progress }] };
            var plan = MigrationResolver.Preview(fresh, current.Snapshot.Entries, Codec);
            Assert.Equal(new[] { "status" }, plan.Rows[0].Fields);
            await current.ApplyMigrationAsync(plan); var result = current.Snapshot.Entries[0];
            Assert.Equal(progress, result.Status); Assert.Equal(7200, result.EffectivePlaySeconds);
            Assert.Single(result.MigrationOrigins); Assert.Single(result.ManualPlaytime); Assert.Equal("后来写的笔记", result.Notes);
            Assert.Empty(result.Categories); Assert.Empty(result.CustomTags);
            Assert.Equal(ImportAction.AlreadyImported, MigrationResolver.Preview(fresh, current.Snapshot.Entries, Codec).Rows[0].Action);
        }
    }
    [Fact]
    public async Task ChangedPersonalSourceFactsAllowOnlyProgressWithReliableIdentityButChangedIdentityStillRequiresReview()
    {
        var data = await Source(); var disk = new SessionMemoryDisk { Main = Codec.Encode([], Date) };
        await using var library = TransactionalLibrary.Open(Codec, new SessionMemoryPersistence(disk));
        await library.ApplyMigrationAsync(MigrationResolver.Preview(data, [], Codec));
        var otherIdentity = data with { Games = [data.Games[0] with { Status = "completed", Identities = [new("vndb", "v999")] }] };
        Assert.Equal(ImportAction.Skip, MigrationResolver.Preview(otherIdentity, library.Snapshot.Entries, Codec).Rows[0].Action);
        var changed = data with { DocumentDigest = ImportHash.Text("new source snapshot"), Games = [data.Games[0] with { Status = "completed", Notes = "改变了其他来源字段", TotalSeconds = 8000 }] };
        var plan = MigrationResolver.Preview(changed, library.Snapshot.Entries, Codec); Assert.Equal(new[] { "status", "history-pending" }, plan.Rows[0].Fields);
        var originalOrigin = library.Snapshot.Entries[0].MigrationOrigins[0];
        await library.ApplyMigrationAsync(plan); var entry = library.Snapshot.Entries[0];
        Assert.Equal("completed", entry.Status); Assert.Equal(7200, entry.EffectivePlaySeconds); Assert.Equal("", entry.Notes);
        var origin = Assert.Single(entry.MigrationOrigins); Assert.Equal(originalOrigin.RecordDigest, origin.RecordDigest); Assert.Equal(originalOrigin.DocumentDigest, origin.DocumentDigest);
        Assert.Equal(changed.DocumentDigest, origin.ProgressUpdate!.DocumentDigest); Assert.Equal(ImportHash.Of(changed.Games[0]), origin.ProgressUpdate.RecordDigest);
        var pending = MigrationResolver.Preview(changed, library.Snapshot.Entries, Codec).Rows[0];
        Assert.Equal(ImportMatch.Conflict, pending.Match); Assert.Single(pending.ReviewCandidates);
    }
    [Fact]
    public async Task NewMigrationEntriesKeepSourceProgressButOrdinaryAddsUseUnset()
    {
        var data = await Source(); data = data with { Games = [data.Games[0] with { Status = "wish" }] };
        var disk = new SessionMemoryDisk { Main = Codec.Encode([], Date) }; await using var library = TransactionalLibrary.Open(Codec, new SessionMemoryPersistence(disk));
        await library.ApplyMigrationAsync(MigrationResolver.Preview(data, [], Codec)); Assert.Equal("wish", library.Snapshot.Entries[0].Status);
        var added = await library.AddAsync(Target(data.Games[0], "planned").Metadata!);
        Assert.Equal(GameProgress.Default, added.Value.Status); Assert.Equal("未设置", StatusNames.Display(added.Value.Status));
    }
    [Fact]
    public void WantToPlayIsDistinctAndPersistedWithLegacyReadableFallbackAndVersionFence()
    {
        var entry = SessionTestRig.Entry() with { Status = "wish" }; var bytes = Codec.Encode([entry], Date);
        var tree = JsonNode.Parse(bytes)!; var raw = tree["games"]![0]!;
        Assert.Equal("planned", (string)raw["status"]!); Assert.Equal(4, (int)raw["nativeExtras"]!["version"]!);
        Assert.Equal("wish", (string)raw["nativeExtras"]!["progressStatus"]!); Assert.Equal("wish", Codec.Decode(bytes)[0].Status);
        Assert.Equal("planned", new LegacyLibraryCodec().Decode(bytes)[0].Status);
        raw["nativeExtras"]!["version"] = 8;
        Assert.Equal(LibraryAccess.ValidReadOnly, Codec.Inspect(JsonSerializer.SerializeToUtf8Bytes(tree), Date).Access);
    }
    [Theory] [InlineData(3, "wish")] [InlineData(4, "planned")] [InlineData(4, "unknown")]
    public void InconsistentProgressExtensionIsRejectedWithoutSilentlyChangingStatus(int version, string progress)
    {
        var tree = JsonNode.Parse(Codec.Encode([SessionTestRig.Entry() with { Status = "wish" }], Date))!;
        tree["games"]![0]!["nativeExtras"]!["version"] = version;
        tree["games"]![0]!["nativeExtras"]!["progressStatus"] = progress;
        Assert.Equal(LibraryAccess.InvalidInput, Codec.Inspect(JsonSerializer.SerializeToUtf8Bytes(tree), Date).Access);
    }
    [Fact]
    public async Task ExplicitEditingCanLowerProgressAndOldUnsetDataIsNotRewrittenOnOpen()
    {
        var old = Codec.Encode([SessionTestRig.Entry() with { Status = "planned" }], Date);
        Assert.DoesNotContain("progressStatus", System.Text.Encoding.UTF8.GetString(old));
        var disk = new SessionMemoryDisk { Main = old }; await using var library = TransactionalLibrary.Open(Codec, new SessionMemoryPersistence(disk));
        Assert.Equal(0, disk.Writes); Assert.Equal("未设置", StatusNames.Display(library.Snapshot.Entries[0].Status));
        await library.UpdateAsync("g", new() { Status = new(true, "wish") }); Assert.Equal("wish", library.Snapshot.Entries[0].Status);
        await library.UpdateAsync("g", new() { Status = new(true, "planned") }); Assert.Equal("planned", library.Snapshot.Entries[0].Status);
        Assert.DoesNotContain("progressStatus", System.Text.Encoding.UTF8.GetString(disk.Main!));
    }
    [Fact]
    public async Task WantToPlaySurvivesRealNativeProfilePersistenceAndRestart()
    {
        var root = Path.Combine(Path.GetTempPath(), "the-order-progress-" + Guid.NewGuid().ToString("N"));
        var request = new NativeProfileRequest(NativeProfileKind.Test, root);
        try
        {
            using (var profile = NativeProfileStore.Open(request, Codec))
            {
                await using var library = TransactionalLibrary.Open(Codec, profile.CreateLibraryPersistence());
                var added = await library.AddAsync(Target((await Source()).Games[0], "planned").Metadata!);
                await library.UpdateAsync(added.Value.Id, new() { Status = new(true, "wish") });
            }
            using (var profile = NativeProfileStore.Open(request, Codec))
            {
                await using var library = TransactionalLibrary.Open(Codec, profile.CreateLibraryPersistence());
                Assert.Equal("wish", Assert.Single(library.Snapshot.Entries).Status);
                Assert.Equal(LibraryAccess.ValidWritable, library.Snapshot.Access);
                var backup = await library.ExportPortableAsync(); Assert.Equal("wish", Assert.Single(Codec.Decode(backup)).Status);
            }
        }
        finally
        {
            if (!Path.GetFullPath(root).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(root).StartsWith("the-order-progress-")) throw new InvalidOperationException();
            Directory.Delete(root, true);
        }
    }
    [Fact]
    public void StatisticsAndFiltersShareSixStatusesInUserPriorityOrder()
    {
        var entries = Priority.Select((s, i) => SessionTestRig.Entry(i.ToString(), 0, 0) with { Status = s }).ToImmutableArray();
        var stats = StatisticsDashboardBuilder.Build(new(entries, StatisticsPeriod.AllTime, new(2026, 10, 19), TimeZoneInfo.Utc));
        Assert.Equal(Priority, stats.Statuses.Select(s => s.Key)); Assert.All(stats.Statuses, s => Assert.Equal(1, s.Count));
        Assert.Equal(new[] { "玩过", "在玩", "想玩", "搁置", "放弃", "未设置" }, stats.Statuses.Select(s => s.Name));
        Assert.Equal(new[] { "all" }.Concat(Priority), PreferenceValidation.Statuses); Assert.Equal(0, stats.ActiveDays);
    }
    [Fact]
    public async Task UnsetNeverBecomesCloudWishOrDeletesCloudProgressAndImportedStateCannotRegress()
    {
        var entry = SessionTestRig.Entry() with { Status = "planned", Metadata = Target((await Source()).Games[0], "planned").Metadata };
        Assert.Null(PersonalSyncPlanning.CloudStatus("planned")); Assert.Equal("wish", PersonalSyncPlanning.LocalStatus("wish"));
        var empty = PersonalSyncPlanning.State(entry, null, null, PersonalSyncDirection.Upload);
        Assert.Null(empty.Upload); Assert.Equal(PersonalSyncChoice.Skip, empty.Recommended);
        var remote = new CloudWorkState("123", "wish", null, Date);
        var row = PersonalSyncPlanning.State(entry, remote, null, PersonalSyncDirection.Bidirectional);
        Assert.Null(row.Upload); Assert.Equal("wish", row.LocalStateToApply); Assert.Equal(PersonalSyncChoice.Import, row.Recommended); Assert.False(row.Conflict);
        var played = PersonalSyncPlanning.State(entry with { Status = "completed" }, remote, null, PersonalSyncDirection.Import);
        Assert.Equal("completed", played.LocalStateToApply);
    }
}
