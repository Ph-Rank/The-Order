using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using TheOrder.Application;
using TheOrder.Application.Migrations;
using TheOrder.Domain;
using TheOrder.Infrastructure.Legacy;
using TheOrder.Platform.Windows;
using TheOrder.Presentation;
using Xunit;

namespace TheOrder.Tests;

// Synthetic identities and folders: these tests never read a private library or game directory.
public sealed class MigrationReviewTests
{
    private static readonly NativeLibraryCodec Codec = new();
    private const string Date = "2026-10-19T01:00:00.000Z";
    private static ImportGame Game() => new("source-1", "Synthetic game", "", [], [new("vndb", "v701"), new("steam", "8001")],
        [new("source-install", @"Q:\MigrationReview\game", @"Q:\MigrationReview\game\game.exe")],
        ["来源分类"], ["来源标签"], "playing", 8, true, "source notes", 7200, null, [], [], null, null, null, null, null, []);
    private static ImportDataset Data(ImportGame? game = null) => new("potatovn", "potatovn-export-json-v1", ImportHash.Text("synthetic archive"), 1, [game ?? Game()], []);
    private static LibraryEntry Target(ImportGame? game = null)
    {
        var g = game ?? Game();
        return (SessionTestRig.Entry("target", 0, 0) with { Name = "Personal title", Status = "planned",
            Installations = [new("install", g.Installations[0].Folder, g.Installations[0].Executable, [])], PreferredInstallationId = "install",
            Metadata = new("123", "Synthetic game", "", [], null, null, null, null, "", null, null, [], [], [], [],
                [.. g.Identities.Select(i => new WorkReference(i.Provider, i.Id, null))], null, null, Date) });
    }
    private static ImportReviewDecision Choice(MigrationPlan plan, ImportHistoryChoice history = ImportHistoryChoice.KeepExisting)
    { var row = Assert.Single(plan.Rows); return new(row.RecordKey, Assert.Single(row.ReviewCandidates).TargetKey, true, history); }
    private static MigrationPlan Review(ImportDataset data, LibraryEntry target, ImportHistoryChoice history = ImportHistoryChoice.KeepExisting)
    { var plan = MigrationResolver.Preview(data, [target], Codec); return MigrationResolver.Preview(data, [target], Codec, reviews: [Choice(plan, history)]); }
    private static async Task<LibraryEntry> Commit(ImportDataset data, LibraryEntry target, ImportHistoryChoice history = ImportHistoryChoice.KeepExisting)
    {
        var disk = new SessionMemoryDisk { Main = Codec.Encode([target], Date) };
        await using var lib = TransactionalLibrary.Open(Codec, new SessionMemoryPersistence(disk), () => Date);
        await lib.ApplyMigrationAsync(Review(data, target, history));
        return Assert.Single(Codec.Decode(disk.Main!));
    }

    [Theory] [InlineData("8002")] [InlineData("8003")]
    public void SharedExactSteamIdIsNotContradictedByAnAdditionalLink(string extra)
    {
        var target = Target(); target = target with { Metadata = target.Metadata! with { References = target.Metadata.References.Add(new("steam", extra, null)) } };
        var row = Assert.Single(MigrationResolver.Preview(Data(), [target], Codec).Rows);
        Assert.Equal(ImportMatch.Confirmed, row.Match); Assert.Equal(ImportAction.FillMissing, row.Action);
        Assert.DoesNotContain("EXTERNAL_ID_OR_EDITION_CONFLICT", row.Reasons);
    }
    [Theory] [InlineData("steam", "8002")] [InlineData("vndb", "v702")]
    public void TrulyDisjointProviderIdsStillRequireReview(string provider, string id)
    {
        var target = Target(); target = target with { Metadata = target.Metadata! with { References = [.. target.Metadata.References.Where(i => i.Source != provider), new(provider, id, null)] } };
        var row = Assert.Single(MigrationResolver.Preview(Data(), [target], Codec).Rows);
        Assert.Equal(ImportMatch.Conflict, row.Match); Assert.Single(row.ReviewCandidates);
    }
    [Theory] [InlineData(" QHD Edition")] [InlineData(" REMASTERED")] [InlineData(" 10th Anniversary")]
    [InlineData(" Demo")] [InlineData(" Trial version")]
    public void SharedWorkIdDoesNotEraseAnEditionDifference(string suffix)
    {
        var target = Target() with { Name = "Synthetic game" + suffix };
        var row = Assert.Single(MigrationResolver.Preview(Data(), [target], Codec).Rows);
        Assert.Equal(ImportMatch.Candidate, row.Match); Assert.Single(row.ReviewCandidates);
        Assert.Contains("TITLE_EDITION_DIFFERENCE_REQUIRES_REVIEW", row.Reasons);
    }
    [Fact]
    public async Task MissingSharedIdsCanBeExplicitlyConfirmedWithoutReplacingMetadataOrPersonalFields()
    {
        var g = Game() with { Identities = [] }; var target = Target() with { Notes = "local notes", PersonalRating = 3, Status = "completed" };
        var data = Data(g); var before = Assert.Single(MigrationResolver.Preview(data, [target], Codec).Rows);
        Assert.Equal("PATH_WITHOUT_CONFIRMED_IDENTITY", Assert.Single(before.Reasons));
        var result = await Commit(data, target);
        Assert.Equal(ImportHash.Of(target.Metadata), ImportHash.Of(result.Metadata)); Assert.Equal(target.Name, result.Name);
        Assert.Equal(ImportHash.Of(target.Installations), ImportHash.Of(result.Installations));
        Assert.Equal("local notes", result.Notes); Assert.Equal(3, result.PersonalRating); Assert.Equal("completed", result.Status);
        Assert.Equal(0, result.EffectivePlaySeconds); Assert.Contains("来源分类", result.Categories); Assert.Contains("来源标签", result.CustomTags);
        var review = Assert.Single(result.MigrationOrigins).Review!;
        Assert.True(review.IdentityAndVersionConfirmed); Assert.Equal("keep-existing", review.HistoryChoice);
        Assert.Equal(ImportHash.Of(g), review.SourceRecordDigest);
    }
    [Fact]
    public void TitleOnlyOffersCandidatesButCannotMergeUntilExplicitlyConfirmed()
    {
        var target = Target() with { Installations = [], PreferredInstallationId = null, Metadata = null, Name = Game().Name };
        var plan = MigrationResolver.Preview(Data(), [target], Codec);
        Assert.Equal(ImportMatch.Candidate, plan.Rows[0].Match); Assert.Empty(plan.Operations);
        var reviewed = MigrationResolver.Preview(Data(), [target], Codec, reviews: [Choice(plan)]);
        Assert.Single(reviewed.Operations); Assert.True(reviewed.Rows[0].HumanReviewed);
    }
    [Fact]
    public void CandidateListIsBoundedAndKeepsHomonymsSeparate()
    {
        var games = Enumerable.Range(0, 30).Select(i => Target() with { Id = "target-" + i, Name = Game().Name, Metadata = null, Installations = [], PreferredInstallationId = null }).ToImmutableArray();
        var plan = MigrationResolver.Preview(Data(), games, Codec);
        Assert.Equal(20, Assert.Single(plan.Rows).ReviewCandidates.Length); Assert.Empty(plan.Operations);
    }
    [Fact]
    public void HomonymReviewDoesNotExpandEveryInstallationIntoThePlanHashOrExportPrivateDetails()
    {
        var g = Game() with { Identities = [], Installations = [] };
        var targets = Enumerable.Range(0, 20).Select(n => Target() with { Id = "target-" + n, Name = g.Name,
            Installations = [.. Enumerable.Range(0, 100).Select(i => new Installation("i-" + i, $@"Q:\SyntheticPrivate\target-{n}-{i}", null, []))], PreferredInstallationId = null }).ToImmutableArray();
        var data = Data(g) with { Games = [.. Enumerable.Range(0, 250).Select(i => g with { SourceId = "source-" + i })], RawCount = 250 };
        var plan = MigrationResolver.Preview(data, targets, Codec);
        Assert.Equal(250, plan.Rows.Length); Assert.All(plan.Rows, r =>
        { Assert.Equal(20, r.ReviewCandidates.Length); Assert.All(r.ReviewCandidates, c => { Assert.Equal(6, c.Folders.Length); Assert.Equal(100, c.InstallationCount); }); });
        Assert.Equal(plan.Id, MigrationResolver.Preview(data, targets, Codec).Id);
        foreach (var report in new[] { MigrationReport.Json(plan, targets.Length), MigrationReport.Markdown(plan, targets.Length) })
        { Assert.DoesNotContain("SyntheticPrivate", report); Assert.DoesNotContain(g.Name, report); Assert.DoesNotContain(g.Notes, report); }
    }
    [Theory] [InlineData(false, 0)] [InlineData(true, 99)]
    public void CheckboxAndHistoryChoiceAreValidatedInTheCore(bool confirmed, int history)
    {
        var data = Data(Game() with { Identities = [] }); var target = Target(); var plan = MigrationResolver.Preview(data, [target], Codec);
        var choice = Choice(plan) with { IdentityAndVersionConfirmed = confirmed, History = (ImportHistoryChoice)history };
        Assert.Throws<InvalidDataException>(() => MigrationResolver.Preview(data, [target], Codec, reviews: [choice]));
    }
    [Theory] [InlineData("target")] [InlineData("record")]
    public void ArbitraryTargetOrSourceCannotBeSmuggledIntoAReview(string change)
    {
        var data = Data(Game() with { Identities = [] }); var target = Target(); var choice = Choice(MigrationResolver.Preview(data, [target], Codec));
        choice = change == "target" ? choice with { TargetKey = ImportHash.Text("unrelated")[..20] } : choice with { RecordKey = ImportHash.Text("absent")[..20] };
        Assert.Throws<InvalidDataException>(() => MigrationResolver.Preview(data, [target], Codec, reviews: [choice]));
    }
    [Theory] [InlineData("duplicate")] [InlineData("invalid-source")] [InlineData("parent-child")]
    public void HumanReviewCannotBypassCorruptSourceOrOverlappingDirectories(string kind)
    {
        var g = Game() with { Identities = [] }; var data = Data(g); var target = Target();
        var choice = new ImportReviewDecision(ImportHash.Key(data.Manager, g.SourceId), ImportHash.Text(target.Id)[..20], true);
        if (kind == "duplicate") data = data with { Games = [g, g], RawCount = 2 };
        if (kind == "invalid-source") data = data with { Games = [g with { NeedsReview = true }] };
        if (kind == "parent-child") target = target with { Installations = [target.Installations[0] with { Folder = @"Q:\MigrationReview" }] };
        Assert.Empty(MigrationResolver.Preview(data, [target], Codec).Rows[0].ReviewCandidates);
        Assert.Throws<InvalidDataException>(() => MigrationResolver.Preview(data, [target], Codec, reviews: [choice]));
    }
    [Fact]
    public async Task ChoosingSourceTotalPreservesDatedRecordsAndDoesNotInventActivityOrLaunches()
    {
        var g = Game() with { Identities = [] }; var target = Target() with { TotalPlaySeconds = 900, PlayCount = 2,
            Sessions = [new("11111111-1111-4111-8111-111111111111", "2026-10-18T01:00:00.000Z", "2026-10-18T01:10:00.000Z", 600)],
            ManualPlaytime = [new("22222222-2222-4222-8222-222222222222", "2026-10-18", 120, "dated correction", Date)] };
        var result = await Commit(Data(g), target, ImportHistoryChoice.UseSourceTotal);
        Assert.Equal(7200, result.EffectivePlaySeconds); Assert.Equal(target.Sessions.ToArray(), result.Sessions.ToArray());
        Assert.Equal(target.ManualPlaytime.ToArray(), result.ManualPlaytime.ToArray());
        Assert.Equal(2, result.PlayCount); Assert.Equal(target.LastPlayedAt, result.LastPlayedAt);
        var stats = StatisticsDashboardBuilder.Build(new([result], StatisticsPeriod.AllTime, new(2026, 10, 19), TimeZoneInfo.Utc));
        Assert.Equal(7200, stats.LifetimeSeconds); Assert.Equal(6480, stats.UnknownSeconds); Assert.Equal(720, stats.PeriodSeconds); Assert.Equal(1, stats.ActiveDays);
        var review = Assert.Single(result.MigrationOrigins).Review!; Assert.Equal(1020, review.BeforeSeconds); Assert.Equal(7200, review.AfterSeconds);
    }
    [Fact]
    public async Task SourceCumulativeMayReplaceOnlyUndatedRemainderWithoutAddingTheTotals()
    {
        var target = Target() with { TotalPlaySeconds = 10_000 };
        var result = await Commit(Data(), target, ImportHistoryChoice.UseSourceTotal);
        Assert.Equal(7200, result.EffectivePlaySeconds); Assert.Empty(result.Sessions); Assert.Empty(result.ManualPlaytime); Assert.Equal(0, result.PlayCount);
    }
    [Fact]
    public void ASourceTotalBelowKnownDatedHistoryCannotBeAdopted()
    {
        var g = Game() with { TotalSeconds = 100 }; var target = Target() with { TotalPlaySeconds = 600,
            Sessions = [new("11111111-1111-4111-8111-111111111111", "2026-10-18T01:00:00.000Z", "2026-10-18T01:10:00.000Z", 600)] };
        Assert.False(Assert.Single(MigrationResolver.Preview(Data(g), [target], Codec).Rows[0].ReviewCandidates).CanUseSourceTotal);
        Assert.Throws<InvalidDataException>(() => Review(Data(g), target, ImportHistoryChoice.UseSourceTotal));
    }
    [Fact]
    public async Task EmptyHistoryCanRetainTheSourcesRealDailyAggregates()
    {
        var g = Game() with { Identities = [], Days = [new(new(2026, 10, 18), 3600)] };
        var result = await Commit(Data(g), Target(), ImportHistoryChoice.UseSourceTotal);
        Assert.Equal(7200, result.EffectivePlaySeconds); Assert.Equal(3600, Assert.Single(result.ManualPlaytime).Seconds); Assert.Empty(result.Sessions); Assert.Equal(0, result.PlayCount);
    }
    [Theory] [InlineData(ImportHistoryChoice.KeepExisting)] [InlineData(ImportHistoryChoice.UseSourceTotal)]
    public async Task ReviewedAssociationSurvivesRestartAndRepeatedImportWithoutAddingTime(ImportHistoryChoice history)
    {
        var data = Data(Game() with { Identities = [] }); var result = await Commit(data, Target(), history);
        var reopened = Codec.Decode(Codec.Encode([result], Date));
        var repeat = MigrationResolver.Preview(data, reopened, Codec); Assert.Empty(repeat.Operations); Assert.Equal(ImportAction.AlreadyImported, repeat.Rows[0].Action);
        Assert.Empty(MigrationResolver.Preview(data, reopened, Codec).Operations);
        Assert.Equal(history == ImportHistoryChoice.UseSourceTotal ? 7200 : 0, reopened[0].EffectivePlaySeconds);
        var changed = data with { Games = [data.Games[0] with { TotalSeconds = 8000 }] };
        Assert.Equal(ImportMatch.Conflict, MigrationResolver.Preview(changed, reopened, Codec).Rows[0].Match);
    }
    [Fact]
    public async Task AManuallyAcceptedForeignIdentityIsNotPromotedToAutomaticCrossRecordEvidence()
    {
        var g = Game() with { Identities = [new("vndb", "v799")] }; var target = Target();
        var reviewed = await Commit(Data(g), target);
        Assert.DoesNotContain(new ImportedIdentity("vndb", "v799"), MigrationResolver.Identities(reviewed));
        Assert.Contains(new ImportedIdentity("vndb", "v701"), MigrationResolver.Identities(reviewed));
        var next = g with { SourceId = "other-record", Installations = [], Name = "Other name" };
        Assert.Equal(ImportMatch.Unmatched, MigrationResolver.Preview(Data(next), [reviewed], Codec).Rows[0].Match);
    }
    [Fact]
    public async Task ReviewExtensionIsVersionedAndMalformedProofIsRejected()
    {
        var result = await Commit(Data(Game() with { Identities = [] }), Target());
        var tree = JsonNode.Parse(Codec.Encode([result], Date))!; var extra = tree["games"]![0]!["nativeExtras"]!;
        Assert.Equal(5, (int)extra["version"]!); Assert.Equal(LibraryAccess.ValidWritable, Codec.Inspect(JsonSerializer.SerializeToUtf8Bytes(tree), Date).Access);
        extra["migrationOrigins"]![0]!["review"]!["targetKey"] = null;
        Assert.Equal(LibraryAccess.InvalidInput, Codec.Inspect(JsonSerializer.SerializeToUtf8Bytes(tree), Date).Access);
    }
    [Fact]
    public async Task ReviewingAChangedRecordPreservesItsOriginalProvenanceAndTracksTheNewDecision()
    {
        var g = Game() with { Identities = [] }; var first = await Commit(Data(g), Target());
        var changed = Data(g with { TotalSeconds = 8100, Status = "completed", Notes = "changed source note" });
        var second = await Commit(changed, first, ImportHistoryChoice.UseSourceTotal);
        var a = first.MigrationOrigins[0]; var b = second.MigrationOrigins[0];
        Assert.Equal(a.RecordDigest, b.RecordDigest); Assert.Equal(a.ImportedAt, b.ImportedAt);
        Assert.Equal(ImportHash.Of(a.Fields), ImportHash.Of(b.Fields)); Assert.Equal("completed", second.Status);
        Assert.Equal(ImportHash.Of(changed.Games[0]), b.Review!.SourceRecordDigest); Assert.Equal(8100, second.EffectivePlaySeconds);
        Assert.Equal(ImportAction.AlreadyImported, MigrationResolver.Preview(changed, [second], Codec).Rows[0].Action);
    }
    [Fact]
    public void TwoSourceRecordsCannotBothBeReviewedIntoTheSameTargetInOneBatch()
    {
        var a = Game() with { Installations = [], Identities = [] }; var b = a with { SourceId = "source-2" };
        var data = Data(a) with { Games = [a, b], RawCount = 2 }; var target = Target() with { Name = a.Name };
        var plan = MigrationResolver.Preview(data, [target], Codec);
        var decisions = plan.Rows.Select(r => new ImportReviewDecision(r.RecordKey, r.ReviewCandidates[0].TargetKey, true)).ToImmutableArray();
        Assert.Throws<InvalidDataException>(() => MigrationResolver.Preview(data, [target], Codec, reviews: decisions));
    }
    [Fact]
    public async Task ReviewCanBeRevokedAndSupersedesPriorPreviewConsentWithoutWriting()
    {
        var data = Data(Game() with { Identities = [] }); var target = Target(); var disk = new SessionMemoryDisk { Main = Codec.Encode([target], Date) };
        await using var lib = TransactionalLibrary.Open(Codec, new SessionMemoryPersistence(disk), () => Date);
        var original = disk.Main.ToArray(); var initial = await lib.PreviewActiveMigrationAsync(data);
        var decision = new ImportReviewDecision(initial.Rows[0].RecordKey, initial.Rows[0].ReviewCandidates[0].TargetKey, true);
        var reviewed = await lib.ReviewActiveMigrationAsync(initial, decision);
        Assert.True(reviewed.CanApply); Assert.Equal(original, disk.Main); Assert.Equal(0, disk.Writes);
        await Assert.ThrowsAsync<StaleLibraryRequestException>(() => lib.ApplyActiveMigrationAsync(initial, (_, _, _) => Task.CompletedTask));
        var revoked = await lib.ReviewActiveMigrationAsync(reviewed, new(decision.RecordKey, null));
        Assert.False(revoked.CanApply); Assert.Empty(revoked.Reviews);
        await Assert.ThrowsAsync<StaleLibraryRequestException>(() => lib.ApplyActiveMigrationAsync(reviewed, (_, _, _) => Task.CompletedTask));
        var final = await lib.ReviewActiveMigrationAsync(revoked, decision);
        bool backedUp = false;
        await lib.ApplyActiveMigrationAsync(final, (bytes, count, _) => { Assert.Equal(1, count); Assert.Equal(0, disk.Writes); Assert.Equal(target.Name, Codec.Decode(bytes)[0].Name); backedUp = true; return Task.CompletedTask; });
        Assert.True(backedUp); Assert.Equal(1, disk.Writes);
    }
    [Theory] [InlineData("backup")] [InlineData("write")] [InlineData("cancel")]
    public async Task ReviewedImportFailuresNeverPublishPartialData(string fault)
    {
        var data = Data(Game() with { Identities = [] }); var target = Target(); var disk = new SessionMemoryDisk { Main = Codec.Encode([target], Date), Fail = fault == "write" };
        await using var lib = TransactionalLibrary.Open(Codec, new SessionMemoryPersistence(disk), () => Date);
        var initial = await lib.PreviewActiveMigrationAsync(data);
        var reviewed = await lib.ReviewActiveMigrationAsync(initial, new(initial.Rows[0].RecordKey, initial.Rows[0].ReviewCandidates[0].TargetKey, true, ImportHistoryChoice.UseSourceTotal));
        var before = disk.Main.ToArray(); using var cancel = new CancellationTokenSource(); if (fault == "cancel") cancel.Cancel();
        await Assert.ThrowsAnyAsync<Exception>(() => lib.ApplyActiveMigrationAsync(reviewed, (_, _, _) => fault == "backup" ? throw new IOException("synthetic") : Task.CompletedTask, cancel.Token));
        Assert.Equal(before, disk.Main); Assert.Equal(0, disk.Writes); Assert.Empty(lib.Snapshot.Entries[0].MigrationOrigins);
    }
    [Fact]
    public async Task ReviewCancellationAndConcurrentPreviewCannotLeaveOldConsentWritable()
    {
        var data = Data(Game() with { Identities = [] }); var disk = new SessionMemoryDisk { Main = Codec.Encode([Target()], Date) };
        await using var lib = TransactionalLibrary.Open(Codec, new SessionMemoryPersistence(disk), () => Date);
        var first = await lib.PreviewActiveMigrationAsync(data);
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        var choice = new ImportReviewDecision(first.Rows[0].RecordKey, first.Rows[0].ReviewCandidates[0].TargetKey, true);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => lib.ReviewActiveMigrationAsync(first, choice, cancel.Token));
        var second = await lib.PreviewActiveMigrationAsync(data);
        await Assert.ThrowsAsync<StaleLibraryRequestException>(() => lib.ReviewActiveMigrationAsync(first, choice));
        await lib.ReviewActiveMigrationAsync(second, choice); Assert.Equal(0, disk.Writes);
    }
    [Fact]
    public async Task RunningSessionCannotBeApprovedThroughManualReview()
    {
        await using var rig = new SessionTestRig(); await rig.Start();
        var g = Game() with { Installations = [new("i", @"C:\session-fixture", @"C:\session-fixture\game.exe")], Identities = [] };
        var preview = await rig.Library.PreviewActiveMigrationAsync(Data(g)); var before = rig.Disk.Writes;
        Assert.Empty(preview.Rows[0].ReviewCandidates);
        await Assert.ThrowsAsync<InvalidDataException>(() => rig.Library.ReviewActiveMigrationAsync(preview, new(preview.Rows[0].RecordKey, ImportHash.Text("g")[..20], true)));
        Assert.Equal(before, rig.Disk.Writes);
    }
    private sealed class Source(ImportDataset data) : ILibraryMigrationSource
    { public Task<ImportDataset> ReadAsync(string path, CancellationToken cancellation) => Task.FromResult(data); }
    private sealed class Factory(SessionMemoryDisk disk) : ITransactionalLibraryFactory
    { public Task<TransactionalLibrary> OpenAsync(CancellationToken cancellation) => Task.FromResult(TransactionalLibrary.Open(Codec, new SessionMemoryPersistence(disk), () => Date)); }
    private sealed class NoBackup : ILibraryBackupStore
    {
        public Task<LibraryBackupWrite> SaveSnapshotAsync(ReadOnlyMemory<byte> document, int count, string reason, CancellationToken cancellation) => throw new NotSupportedException();
        public Task<byte[]> ReadSnapshotAsync(string id, CancellationToken cancellation) => throw new NotSupportedException();
        public Task<LibraryBackupState> ReadStateAsync(CancellationToken cancellation) => throw new NotSupportedException();
        public Task SaveSettingsAsync(LibraryBackupSettings settings, CancellationToken cancellation) => throw new NotSupportedException();
        public Task<byte[]> ReadExternalAsync(string path, CancellationToken cancellation) => throw new NotSupportedException();
        public Task<string?> WriteExternalAsync(string path, ReadOnlyMemory<byte> document, CancellationToken cancellation) => throw new NotSupportedException();
    }
    [Fact]
    public async Task ViewModelExposesReviewAndRevocationInsteadOfAnUnavoidableSkip()
    {
        var disk = new SessionMemoryDisk { Main = Codec.Encode([Target()], Date) };
        await using var client = new OfflineLibraryClient(new Factory(disk), new LegacyClientTextRules()); await client.LoadAsync(default);
        await using var vm = new LibraryMigrationViewModel(new LibraryMigrationService(client, new Source(Data(Game() with { Identities = [] })), new NoBackup()));
        await vm.PreviewAsync("synthetic.zip"); Assert.True(vm.Rows[0].CanReview); Assert.Equal("待核验", vm.Rows[0].Action); Assert.False(vm.CanApply);
        var row = vm.Preview!.Rows[0]; await vm.ReviewAsync(new(row.RecordKey, row.ReviewCandidates[0].TargetKey, true, ImportHistoryChoice.UseSourceTotal));
        Assert.True(vm.CanApply); Assert.Contains("已核验", vm.Rows[0].Action); Assert.Contains("采用来源", vm.Rows[0].Detail);
        await vm.ReviewAsync(new(row.RecordKey, null)); Assert.False(vm.CanApply); Assert.True(vm.Rows[0].CanReview);
        Assert.Equal(0, disk.Writes);
    }
    private sealed class DelayedReview(ILibraryMigrationService inner) : ILibraryMigrationService
    {
        public TaskCompletionSource<LibraryMigrationPreview> Done = new();
        public Task<LibraryMigrationPreview> PreviewAsync(string path, CancellationToken cancellation) => inner.PreviewAsync(path, cancellation);
        public Task<LibraryMigrationPreview> ReviewAsync(LibraryMigrationPreview preview, ImportReviewDecision decision, CancellationToken cancellation) => Done.Task;
        public Task<ClientResult> ApplyAsync(LibraryMigrationPreview preview, CancellationToken cancellation) => inner.ApplyAsync(preview, cancellation);
    }
    [Fact]
    public async Task LateReviewAfterCancellationCannotEnableTheOldPreview()
    {
        var disk = new SessionMemoryDisk { Main = Codec.Encode([Target()], Date) };
        await using var client = new OfflineLibraryClient(new Factory(disk), new LegacyClientTextRules()); await client.LoadAsync(default);
        var service = new LibraryMigrationService(client, new Source(Data(Game() with { Identities = [] })), new NoBackup());
        var delayed = new DelayedReview(service); await using var vm = new LibraryMigrationViewModel(delayed);
        await vm.PreviewAsync("synthetic.zip"); var preview = vm.Preview!; var row = preview.Rows[0];
        var choice = new ImportReviewDecision(row.RecordKey, row.ReviewCandidates[0].TargetKey, true);
        var run = vm.ReviewAsync(choice); vm.Cancel(); delayed.Done.SetResult(await service.ReviewAsync(preview, choice, default)); await run;
        Assert.False(vm.CanApply); Assert.Null(vm.Preview); Assert.Empty(vm.Rows); Assert.Equal(0, disk.Writes);
    }
    [Fact]
    public async Task ReviewedImportBacksUpAndReopensThroughARealIsolatedNativeProfile()
    {
        var root = Path.Combine(Path.GetTempPath(), "the-order-reviewed-migration-" + Guid.NewGuid().ToString("N"));
        var request = new NativeProfileRequest(NativeProfileKind.Test, root); var data = Data(Game() with { Identities = [] });
        try
        {
            using (var profile = NativeProfileStore.Open(request, Codec))
            {
                using (var persistence = profile.CreateLibraryPersistence()) await persistence.SaveAsync(ReadOnlyMemory<byte>.Empty, Codec.Encode([Target()], Date));
                var config = profile.Configuration(false);
                await using var client = new OfflineLibraryClient(new IsolatedLibraryFactory(Codec, config), new LegacyClientTextRules()); await client.LoadAsync(default);
                var store = new WindowsLibraryBackupStore(config, Codec); var service = new LibraryMigrationService(client, new Source(data), store);
                var initial = await service.PreviewAsync("synthetic.zip", default); var row = initial.Rows[0];
                var preview = await service.ReviewAsync(initial, new(row.RecordKey, row.ReviewCandidates[0].TargetKey, true, ImportHistoryChoice.UseSourceTotal), default);
                Assert.True((await service.ApplyAsync(preview, default)).IsCommitted);
                var backup = Assert.Single((await store.ReadStateAsync(default)).Records);
                var original = Assert.Single(Codec.Decode(await store.ReadSnapshotAsync(backup.Id, default)));
                Assert.Equal(0, original.EffectivePlaySeconds); Assert.Empty(original.MigrationOrigins);
            }
            using (var profile = NativeProfileStore.Open(request, Codec))
            {
                await using var library = TransactionalLibrary.Open(Codec, profile.CreateLibraryPersistence());
                var game = Assert.Single(library.Snapshot.Entries);
                Assert.Equal(7200, game.EffectivePlaySeconds); Assert.NotNull(game.MigrationOrigins[0].Review);
                var repeat = await library.PreviewActiveMigrationAsync(data); Assert.Equal(1, repeat.AlreadyImported); Assert.False(repeat.CanApply);
            }
        }
        finally
        {
            var full = Path.GetFullPath(root); var temp = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()));
            if (!full.StartsWith(temp + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || !Path.GetFileName(full).StartsWith("the-order-reviewed-migration-", StringComparison.Ordinal)) throw new InvalidOperationException();
            if (Directory.Exists(full)) Directory.Delete(full, true);
        }
    }
}
