using System.Collections.Immutable;
using System.Text.Json.Nodes;
using TheOrder.Application;
using TheOrder.Domain;
using TheOrder.Infrastructure.Legacy;
using Xunit;
using static TheOrder.Tests.RecognitionBoundaryTests;

namespace TheOrder.Tests;
public sealed class P1LibraryExtrasTests
{
    private static readonly NativeLibraryCodec Codec = new();
    private static readonly DateOnly Day = new(2026, 10, 2);
    private static PlaySession Dated(long seconds = 3600) => new("legacy", "2026-10-02T00:00:00.000Z", "2026-10-02T01:00:00.000Z", seconds);
    private static ManualPlaytime Manual(long seconds) => new(Guid.NewGuid().ToString("D"), "2026-10-02", seconds, "人工", Clock);
    private static WorkMetadata Source(string source = "bangumi", string id = "42", string description = "from source") => Work(source + ":" + id) with
    { Description = description, Developers = ["源公司"], Tags = ["源标签"], CoverUrl = "https://example.com/cover.png", Ratings = [new(source, 8, 10, 1)], References = [new(source, id, source == "bangumi" ? "https://bgm.tv/subject/" + id : "https://vndb.org/" + id)] };
    private static TransactionalLibrary Open(SessionMemoryDisk disk) => TransactionalLibrary.Open(Codec, new SessionMemoryPersistence(disk));
    private static SessionMemoryDisk Disk(LibraryEntry e) => new() { Main = Codec.Encode([e], Clock) };
    [Fact] public void AllExtrasRoundtripWithoutChangingNativeOrLegacyFacts()
    {
        var original = Entry(folder: @"C:\synthetic\game") with { TotalPlaySeconds = 3600, PlayCount = 1, Sessions = [Dated()], ManualPlaytime = [Manual(-600)], GuidePaths = [@"C:\guide.pdf"],
            Metadata = Work(), MetadataMix = new(Work(), [new("bangumi", "42", Source(), ["description"], false)]),
            Installations = [new("i", @"C:\synthetic\game", @"C:\synthetic\game\game.exe", ["arg"]) { SaveFolder = @"C:\save", SteamAppId = "480", Compatibility = new(UseSteam: true, MuteInBackground: true) }] };
        var loaded = Assert.Single(Codec.Decode(Codec.Encode([original], Clock)));
        Assert.Equal(3000, loaded.EffectivePlaySeconds); Assert.Equal(original.TotalPlaySeconds, loaded.TotalPlaySeconds);
        Assert.Equal(original.Sessions.ToArray(), loaded.Sessions.ToArray()); Assert.Equal(original.ManualPlaytime.ToArray(), loaded.ManualPlaytime.ToArray()); Assert.Equal(original.GuidePaths.ToArray(), loaded.GuidePaths.ToArray());
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(original.Installations), System.Text.Json.JsonSerializer.Serialize(loaded.Installations)); Assert.Equal("from source", loaded.MetadataMix!.Layers[0].Work.Description);
    }
    [Fact] public void OldDocumentDoesNotGainAnEmptyExtension()
    { var document = Codec.Encode([Entry()], Clock); Assert.DoesNotContain("nativeExtras", System.Text.Encoding.UTF8.GetString(document)); Assert.Empty(Assert.Single(Codec.Decode(document)).ManualPlaytime); }
    [Fact] public void FutureExtensionIsReadOnlyAndCorruptExtensionsAreInvalid()
    {
        var json = JsonNode.Parse(Codec.Encode([Entry() with { ManualPlaytime = [Manual(60)] }], Clock))!;
        json["games"]![0]!["nativeExtras"]!["version"] = 8; // v7 persists dated import receipts; v8 is unknown.
        Assert.Equal(LibraryAccess.ValidReadOnly, Codec.Inspect(System.Text.Encoding.UTF8.GetBytes(json.ToJsonString()), Clock).Access);
        json["games"]![0]!["nativeExtras"]!["version"] = 1; json["games"]![0]!["nativeExtras"]!["manualPlaytime"]![0] = null;
        Assert.Equal(LibraryAccess.InvalidInput, Codec.Inspect(System.Text.Encoding.UTF8.GetBytes(json.ToJsonString()), Clock).Access);
    }
    [Fact] public async Task ManualCorrectionAndUndoKeepOriginalSessionAndProcessTotals()
    {
        var e = Entry() with { TotalPlaySeconds = 3600, PlayCount = 1, Sessions = [Dated()] }; var disk = Disk(e); await using var library = Open(disk);
        var d = await library.ExtrasAsync("a"); await library.AddManualTimeAsync(d.Token, Day, 1800, "correct", TimeZoneInfo.Utc, true);
        var changed = library.Snapshot.Entries[0]; Assert.Equal(1800, changed.EffectivePlaySeconds); Assert.Equal(3600, changed.TotalPlaySeconds); Assert.Equal(e.Sessions.ToArray(), changed.Sessions.ToArray()); Assert.Equal(1, changed.PlayCount);
        d = await library.ExtrasAsync("a"); await library.UndoManualTimeAsync(d.Token, changed.ManualPlaytime[0].Id);
        Assert.Equal(3600, library.Snapshot.Entries[0].EffectivePlaySeconds); Assert.True(library.Snapshot.Entries[0].ManualPlaytime[0].Undone);
    }
    [Fact] public async Task UndoDoesNotAllowNegativeDependentDay()
    {
        await using var library = Open(Disk(Entry())); var d = await library.ExtrasAsync("a"); await library.AddManualTimeAsync(d.Token, Day, 100, "", TimeZoneInfo.Utc);
        d = await library.ExtrasAsync("a"); var first = d.Entry.ManualPlaytime[0].Id; await library.AddManualTimeAsync(d.Token, Day, 50, "", TimeZoneInfo.Utc, true);
        d = await library.ExtrasAsync("a"); await Assert.ThrowsAsync<InvalidDataException>(() => library.UndoManualTimeAsync(d.Token, first));
        await library.UndoManualTimeAsync(d.Token, d.Entry.ManualPlaytime[1].Id); d = await library.ExtrasAsync("a"); await library.UndoManualTimeAsync(d.Token, first);
        Assert.Equal(0, library.Snapshot.Entries[0].EffectivePlaySeconds);
    }
    [Theory][InlineData(0)][InlineData(-1)][InlineData(31622401)]
    public async Task InvalidTimeNeverWrites(long seconds)
    { var disk = Disk(Entry()); await using var library = Open(disk); var d = await library.ExtrasAsync("a"); await Assert.ThrowsAsync<InvalidDataException>(() => library.AddManualTimeAsync(d.Token, Day, seconds, "", TimeZoneInfo.Utc)); Assert.Equal(0, disk.Writes); }
    [Fact] public async Task ActiveSessionCannotBeCorrected()
    { await using var rig = new SessionTestRig(); await rig.Start(); var d = await rig.Library.ExtrasAsync("g"); await Assert.ThrowsAsync<SessionConflictException>(() => rig.Library.AddManualTimeAsync(d.Token, Day, 60, "", TimeZoneInfo.Utc)); }
    [Fact] public async Task FailedWriteAndStaleGuideOrDeleteRestoreCannotOverwrite()
    {
        var disk = Disk(Entry()); await using var library = Open(disk); var d = await library.ExtrasAsync("a"); disk.Fail = true;
        await Assert.ThrowsAsync<IOException>(() => library.SaveGuideAsync(d.Token, @"C:\guide.pdf")); Assert.Empty(library.Snapshot.Entries[0].GuidePaths);
        disk.Fail = false; await library.SaveGuideAsync(d.Token, @"C:\guide.pdf"); await Assert.ThrowsAsync<StaleLibraryRequestException>(() => library.AddManualTimeAsync(d.Token, Day, 60, "", TimeZoneInfo.Utc));
        d = await library.ExtrasAsync("a"); await library.DeleteAsync("a"); await library.ImportAsync(Codec.Encode([Entry()], Clock));
        await Assert.ThrowsAsync<StaleLibraryRequestException>(() => library.SaveGuideAsync(d.Token, @"C:\old.pdf"));
    }
    [Fact] public async Task GuideRelocationIsAtomicAndNeverDeletesFiles()
    { await using var library = Open(Disk(Entry())); var d = await library.ExtrasAsync("a"); await library.SaveGuideAsync(d.Token, @"C:\old.pdf"); d = await library.ExtrasAsync("a"); await library.ReplaceGuideAsync(d.Token, @"C:\old.pdf", @"C:\new.md"); Assert.Equal(@"C:\new.md", Assert.Single(library.Snapshot.Entries[0].GuidePaths)); d = await library.ExtrasAsync("a"); await Assert.ThrowsAsync<InvalidDataException>(() => library.SaveGuideAsync(d.Token, @"C:\command.exe")); }
    [Fact] public async Task PortableReplaceRestoresExtrasAndInvalidatesOldRequests()
    {
        await using var library = Open(Disk(Entry())); var d = await library.ExtrasAsync("a"); var e = Entry() with { ManualPlaytime = [Manual(60)], GuidePaths = [@"C:\guide.txt"] };
        var preview = await library.PreviewRestoreAsync(Codec.Encode([e], Clock), LibraryRestoreMode.ReplaceLibrary); await library.RestoreAsync(preview, (_, _, _) => Task.CompletedTask);
        Assert.Equal(60, library.Snapshot.Entries[0].EffectivePlaySeconds); Assert.Single(library.Snapshot.Entries[0].GuidePaths);
        await Assert.ThrowsAsync<StaleLibraryRequestException>(() => library.AddManualTimeAsync(d.Token, Day, 60, "", TimeZoneInfo.Utc));
    }
    [Fact] public void CalendarSplitsConserveSecondsAndSeparateUndatedTotals()
    {
        var e = Entry() with { TotalPlaySeconds = 7300, Sessions = [new("cross", "2026-10-01T23:00:00Z", "2026-10-02T01:00:00Z", 7200)], ManualPlaytime = [Manual(-600)] };
        var report = PlaytimeStatistics.Build([e], TimeZoneInfo.Utc); Assert.Equal(6700, report.TotalSeconds); Assert.Equal(100, report.UnknownSeconds);
        Assert.Equal(new long[] { 3600, 3000 }, report.Days.Select(x => x.Seconds)); Assert.All(report.Days, x => Assert.True(x.Estimated));
        Assert.Equal(report.TotalSeconds, report.Days.Sum(x => x.Seconds) + report.UnknownSeconds);
    }
    [Fact] public void InvalidAndOverstatedHistoricalDatesDoNotInflateChart()
    { var e = Entry() with { TotalPlaySeconds = 60, Sessions = [Dated(), new("bad", "bad", "bad", 60), new("future", "9999-12-31T00:00:00Z", "9999-12-31T01:00:00Z", 3600)] }; var report = PlaytimeStatistics.Build([e], TimeZoneInfo.Utc); Assert.Equal(60, report.Days.Sum(x => x.Seconds)); Assert.Equal(0, report.UnknownSeconds); }
    [Fact] public async Task SelectedFieldsOnlyAndLayerWithdrawalRestoreOriginal()
    {
        var e = Entry() with { Metadata = Work() with { Description = "base", Tags = ["baseTag"] }, Notes = "personal", NameIsCustom = true };
        await using var library = Open(Disk(e)); var d = await library.ExtrasAsync("a"); await library.ApplySourceAsync(d.Token, new("bangumi", "42", Source(), ["description"], false));
        var changed = library.Snapshot.Entries[0]; Assert.Equal("from source", changed.Metadata!.Description); Assert.Equal("baseTag", Assert.Single(changed.Metadata.Tags)); Assert.Equal("personal", changed.Notes); Assert.Equal(e.Name, changed.Name);
        d = await library.ExtrasAsync("a"); await library.RemoveSourceAsync(d.Token, "bangumi"); Assert.Equal("base", library.Snapshot.Entries[0].Metadata!.Description); Assert.Null(library.Snapshot.Entries[0].MetadataMix);
    }
    [Fact] public async Task NewMetadataStillHonorsFieldSelectionAndCanFullyWithdraw()
    { await using var library = Open(Disk(Entry())); var d = await library.ExtrasAsync("a"); await library.ApplySourceAsync(d.Token, new("bangumi", "42", Source(), ["description"], false)); var metadata = library.Snapshot.Entries[0].Metadata!; Assert.Empty(metadata.Tags); Assert.Null(metadata.CoverUrl); Assert.Equal("from source", metadata.Description); d = await library.ExtrasAsync("a"); await library.RemoveSourceAsync(d.Token, "bangumi"); Assert.Null(library.Snapshot.Entries[0].Metadata); }
    [Fact] public async Task ProviderIdConflictIsRejectedAndPersonalEditsSurvive()
    { await using var library = Open(Disk(Entry() with { Metadata = Source() })); var d = await library.ExtrasAsync("a"); await library.UpdateAsync("a", new() { Notes = "later", Favorite = true }); await library.ApplySourceAsync(d.Token, new("bangumi", "42", Source(), ["description"], true)); Assert.Equal("later", library.Snapshot.Entries[0].Notes); d = await library.ExtrasAsync("a"); await Assert.ThrowsAsync<InvalidDataException>(() => library.ApplySourceAsync(d.Token, new("bangumi", "43", Source(id: "43"), ["tags"], true))); }
    [Fact] public async Task RefreshKeepsOverridesAndUndoUsesRefreshedBase()
    { await using var library = Open(Disk(Entry() with { Metadata = Work() with { Description = "old" } })); var d = await library.ExtrasAsync("a"); await library.ApplySourceAsync(d.Token, new("bangumi", "42", Source(), ["description"], false)); await library.LinkAsync("a", Work() with { Description = "new" }, true); Assert.Equal("from source", library.Snapshot.Entries[0].Metadata!.Description); d = await library.ExtrasAsync("a"); await library.RemoveSourceAsync(d.Token, "bangumi"); Assert.Equal("new", library.Snapshot.Entries[0].Metadata!.Description); }
    [Fact] public void FillOnlyAndProviderSpecificRatingsRespectEarlierData()
    { var basis = Work() with { Description = "base", Ratings = [new("bangumi", 7, 10, 4), new("kunGal", 8, 10, 1)] }; var mix = new MetadataMix(basis, [new("bangumi", "42", Source(), ["description", "ratings"], true)]); Assert.Equal("base", MetadataMixing.Compose(mix)!.Description); Assert.Equal(7, MetadataMixing.Compose(mix)!.Ratings[0].Score); Assert.Equal(8, MetadataMixing.Compose(mix with { Layers = [mix.Layers[0] with { FillOnly = false }] })!.Ratings.Last().Score); }
    [Fact] public void ManualTimeIsNotClaimedAsAnIndependentObservedCloudIncrement()
    {
        var local = Entry() with { Metadata = Work(), TotalPlaySeconds = 600, ManualPlaytime = [Manual(60)] };
        var checkpoint = new SyncWorkCheckpoint(local.Id, local.Metadata.Id, 600, 10, 10, local.Status, null, null, [], Clock);
        Assert.Null(PersonalSyncPlanning.IndependentSeconds(local, checkpoint));
        Assert.True(PersonalSyncPlanning.Time(local, 10, checkpoint, null, PersonalSyncDirection.Bidirectional).Conflict);
        Assert.True(PersonalSyncPlanning.Time(local with { ManualPlaytime = [Manual(-60)] }, 10, checkpoint, null, PersonalSyncDirection.Bidirectional).Conflict);
    }
}
