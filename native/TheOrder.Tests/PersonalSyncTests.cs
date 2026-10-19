using System.Collections.Immutable;
using System.Text.Json;
using TheOrder.Application;
using TheOrder.Domain;
using Xunit;
using static TheOrder.Tests.RecognitionBoundaryTests;

namespace TheOrder.Tests;

public sealed class PersonalSyncTests
{
    internal sealed class Store : IPersonalSyncStore
    {
        public readonly Dictionary<string, byte[]> Data = new(StringComparer.Ordinal);
        public int Saves, FailAt;
        public Func<CancellationToken, Task>? BeforeLoad;
        public async Task<AccountSyncState?> LoadAsync(string account, string client, CancellationToken c)
        { if (BeforeLoad is { } callback) await callback(c); c.ThrowIfCancellationRequested(); return Data.TryGetValue(account + ":" + client, out var bytes) ? JsonSerializer.Deserialize<AccountSyncState>(bytes) : null; }
        public Task SaveAsync(AccountSyncState state, CancellationToken c)
        { c.ThrowIfCancellationRequested(); if (++Saves == FailAt) throw new OnlineException(OnlineFailure.CredentialStorage); Data[state.AccountId + ":" + state.ClientId] = JsonSerializer.SerializeToUtf8Bytes(state); return Task.CompletedTask; }
    }
    internal sealed class Local : IPersonalSyncLibrary
    {
        public ImmutableArray<LibraryEntry> Entries = [Entry("合成作品", Path.Combine(Path.GetTempPath(), "the-order-synthetic-install")) with {
            Metadata = Work(), Notes = "private synthetic note", PersonalRating = 8.5, Categories = ["个人分类"], CustomTags = ["个人标签"] }];
        public int Changes, Added;
        public Task<ImmutableArray<LibraryEntry>> SnapshotAsync(CancellationToken c) { c.ThrowIfCancellationRequested(); return Task.FromResult(Entries); }
        public Task ApplyStateAsync(LibraryEntry expected, string status, CancellationToken c)
        { c.ThrowIfCancellationRequested(); var actual = Entries.Single(x => x.Id == expected.Id); if (actual.Status != expected.Status || actual.Metadata?.Id != expected.Metadata?.Id) throw new StaleLibraryRequestException(); Entries = Entries.Replace(actual, actual with { Status = status }); Changes++; return Task.CompletedTask; }
        public Task<string> AddWishlistAsync(WorkMetadata work, CancellationToken c)
        { c.ThrowIfCancellationRequested(); var entry = Entry(work.Title) with { Id = "wishlist-" + work.Id, Metadata = work }; Entries = Entries.Add(entry); Added++; return Task.FromResult(entry.Id); }
    }
    internal sealed class Catalog : IOnlineCatalog
    {
        public bool Merged;
        public Task<WorkMetadata> DetailsAsync(string id, bool refresh, CancellationToken c) => Task.FromResult(Work(Merged ? "999" : id));
        public Task<OnlinePage> SearchAsync(string q, string? cursor, CancellationToken c) => Task.FromResult(new OnlinePage([], null));
        public Task TestConnectionAsync(CancellationToken c) => Task.CompletedTask;
        public void ClearCache() { }
    }
    internal sealed class Remote : INextMoePersonalApi
    {
        public ImmutableArray<CloudPlaytime> Times = [];
        public ImmutableArray<CloudWorkState> States = [];
        public ImmutableArray<CloudRating> Ratings = [];
        public ImmutableArray<CloudFolder> Folders = [new("10", "合成收藏夹", "42", 0, "2026-10-11T00:00:00Z")];
        public readonly Dictionary<string, ImmutableArray<string>> Members = new() { ["10"] = [] };
        public int Reads, Writes, Deletes, Creates;
        public bool LoseResponse, Partial;
        public Func<CancellationToken, Task>? BeforeWrite;
        public Func<string?, CloudPage<CloudPlaytime>>? TimePages;
        public readonly List<string?> TimeCursors = [];
        public readonly List<string> Keys = [];
        public readonly List<PersonalWrite[]> Bodies = [];
        private readonly Dictionary<string, ImmutableArray<PersonalWriteResult>> receipts = new(StringComparer.Ordinal);
        private readonly Dictionary<string, CloudFolder> creations = new(StringComparer.Ordinal);
        public Task<CloudPage<CloudPlaytime>> PlaytimesAsync(string? cursor, CancellationToken c) { Reads++; TimeCursors.Add(cursor); return Task.FromResult(TimePages?.Invoke(cursor) ?? new CloudPage<CloudPlaytime>(Times, null, false)); }
        public Task<CloudPage<CloudWorkState>> StatesAsync(string? cursor, CancellationToken c) { Reads++; return Task.FromResult(new CloudPage<CloudWorkState>(States, null, false)); }
        public Task<CloudPage<CloudRating>> RatingsAsync(string? cursor, CancellationToken c) { Reads++; return Task.FromResult(new CloudPage<CloudRating>(Ratings, null, false)); }
        public Task<CloudPage<CloudFolder>> FoldersAsync(string? cursor, CancellationToken c) { Reads++; return Task.FromResult(new CloudPage<CloudFolder>(Folders, null, false)); }
        public Task<CloudPage<string>> FolderItemsAsync(string id, string? cursor, CancellationToken c) { Reads++; return Task.FromResult(new CloudPage<string>(Members[id], null, false)); }
        public async Task<ImmutableArray<PersonalWriteResult>> WriteAsync(PersonalSyncKind kind, string? folder, ImmutableArray<PersonalWrite> items, string key, CancellationToken c)
        {
            if (BeforeWrite is { } callback) await callback(c); c.ThrowIfCancellationRequested(); Keys.Add(key); Bodies.Add([.. items]);
            if (receipts.TryGetValue(key, out var receipt)) return receipt;
            Writes++; var output = ImmutableArray.CreateBuilder<PersonalWriteResult>(); int index = 0;
            foreach (var item in items)
            {
                bool success = !Partial || index++ == 0;
                if (success)
                {
                    if (kind == PersonalSyncKind.Playtime) Times = [.. Times.Where(x => x.WorkId != item.WorkId), new(item.WorkId, item.Minutes!.Value)];
                    else if (kind == PersonalSyncKind.State) States = [.. States.Where(x => x.WorkId != item.WorkId), new(item.WorkId, item.State!, item.Completion, "2026-10-11T10:00:00Z")];
                    else if (kind == PersonalSyncKind.Folder) Members[folder!] = [.. Members[folder!].Append(item.WorkId).Distinct(StringComparer.Ordinal)];
                    else throw new InvalidOperationException("Ratings must not be written.");
                }
                output.Add(new(item.WorkId, success, success ? 200 : 422, success ? null : "synthetic-invalid"));
            }
            receipts[key] = output.ToImmutable();
            if (LoseResponse) throw new OnlineException(OnlineFailure.Timeout);
            return output.ToImmutable();
        }
        public Task<CloudFolder> CreateFolderAsync(string name, string key, CancellationToken c)
        {
            if (!creations.TryGetValue(key, out var folder)) { Creates++; folder = new((20 + Creates).ToString(), name, "42", 0, "2026-10-11T10:00:00Z"); creations[key] = folder; Folders = Folders.Add(folder); Members.Add(folder.Id, []); }
            if (LoseResponse) throw new OnlineException(OnlineFailure.Timeout); return Task.FromResult(folder);
        }
        public Task DeleteFolderItemAsync(string folder, string work, CancellationToken c) { c.ThrowIfCancellationRequested(); Deletes++; Members[folder] = [.. Members[folder].Where(x => x != work)]; return Task.CompletedTask; }
    }
    internal sealed class Fixture
    {
        public readonly NextMoeAuthTests.Fixture Identity = new(); public readonly Store Storage = new(); public readonly Local Library = new(); public readonly Remote Cloud = new(); public readonly Catalog Metadata = new();
        public readonly PersonalSyncCoordinator Sync;
        public Fixture() => Sync = new(Identity.Auth, Cloud, Storage, Library, Metadata, Identity.Time);
        public Task Login() => Identity.Auth.SignInAsync(true, true, default);
        public Task<AccountSyncState> State() => Sync.CurrentAsync(default);
        public async Task<PersonalSyncResult> Apply(PersonalSyncSettings settings)
        { var preview = await Sync.PreviewAsync(settings, default); return await Sync.ApplyAsync(preview, PersonalSyncCoordinator.DefaultChoices(preview), false, null, default); }
    }
    [Fact] public async Task LoginAloneAndOffPreviewNeverReadOrWritePersonalData()
    {
        var f = new Fixture(); await f.Login(); var state = await f.State(); Assert.Equal(new PersonalSyncSettings(), state.Settings);
        var preview = await f.Sync.PreviewAsync(new(), default); Assert.Empty(preview.Rows); Assert.Equal(0, f.Cloud.Reads); Assert.Equal(0, f.Cloud.Writes); Assert.Equal(0, f.Library.Changes);
        Assert.Null(await f.Sync.AutomaticAsync(default));
    }
    [Fact] public async Task ImportMinutesPreservesSessionsInstallationsAndPersonalRating()
    {
        var f = new Fixture(); await f.Login(); var before = f.Library.Entries.Single(); f.Cloud.Times = [new("200", 123)];
        var preview = await f.Sync.PreviewAsync(new(Playtime: PersonalSyncDirection.Import), default);
        Assert.Equal(0, f.Storage.Saves); Assert.Equal(0, f.Cloud.Writes);
        var result = await f.Sync.ApplyAsync(preview, PersonalSyncCoordinator.DefaultChoices(preview), false, null, default);
        Assert.Equal(1, result.Completed); Assert.Equal(before, f.Library.Entries.Single());
        Assert.Equal(123, (await f.State()).Imported.Single().Minutes); Assert.Empty(f.Library.Entries.Single().Sessions); Assert.Equal(0, f.Cloud.Writes);
        await f.Apply(new(Playtime: PersonalSyncDirection.Import)); Assert.Equal(123, (await f.State()).Imported.Single().Minutes);
    }
    [Fact] public async Task UploadUsesAbsoluteFloorMinutesAndRepeatedSyncDoesNotAdd()
    {
        var f = new Fixture(); await f.Login(); f.Library.Entries = [f.Library.Entries[0] with { TotalPlaySeconds = 125 }];
        await f.Apply(new(Playtime: PersonalSyncDirection.Upload)); Assert.Equal(2, f.Cloud.Times.Single().Minutes); Assert.Equal(5, (await f.State()).Works.Single().CarriedSeconds);
        await f.Apply(new(Playtime: PersonalSyncDirection.Upload)); Assert.Equal(1, f.Cloud.Writes); Assert.Equal(2, f.Cloud.Times.Single().Minutes);
        Assert.DoesNotContain("private synthetic note", JsonSerializer.Serialize(f.Cloud.Bodies)); Assert.DoesNotContain("Installations", JsonSerializer.Serialize(f.Cloud.Bodies));
    }
    [Fact] public async Task SmallerOrReducedCloudValuesNeverReplaceLargerLocalHistory()
    {
        var f = new Fixture(); await f.Login(); f.Library.Entries = [f.Library.Entries[0] with { TotalPlaySeconds = 600 }]; f.Cloud.Times = [new("200", 2)];
        await f.Apply(new(Playtime: PersonalSyncDirection.Import)); Assert.Equal(600, f.Library.Entries.Single().TotalPlaySeconds);
        f.Cloud.Times = [new("200", 1)]; var preview = await f.Sync.PreviewAsync(new(Playtime: PersonalSyncDirection.Bidirectional), default);
        Assert.True(preview.Rows.Single().Conflict); Assert.Equal(PersonalSyncChoice.Skip, PersonalSyncCoordinator.DefaultChoices(preview).Single().Value);
    }
    [Fact] public async Task TwoNonzeroInitialTotalsConflictRatherThanAdd()
    {
        var f = new Fixture(); await f.Login(); f.Library.Entries = [f.Library.Entries[0] with { TotalPlaySeconds = 600 }]; f.Cloud.Times = [new("200", 20)];
        var preview = await f.Sync.PreviewAsync(new(Playtime: PersonalSyncDirection.Bidirectional), default); Assert.True(preview.Rows.Single().Conflict);
        await f.Sync.ApplyAsync(preview, PersonalSyncCoordinator.DefaultChoices(preview), false, null, default); Assert.Equal(0, f.Cloud.Writes); Assert.Equal(20, f.Cloud.Times.Single().Minutes);
    }
    [Fact] public async Task StateImportKeepsInstallationAndUploadPreservesCompletion()
    {
        var f = new Fixture(); await f.Login(); f.Cloud.States = [new("200", "done", "main", "2026-10-11T01:00:00Z")]; var before = f.Library.Entries.Single();
        await f.Apply(new(State: PersonalSyncDirection.Import)); Assert.Equal("completed", f.Library.Entries.Single().Status); Assert.Equal(before.Installations, f.Library.Entries.Single().Installations);
        Assert.Equal(before.Notes, f.Library.Entries.Single().Notes); f.Library.Entries = [f.Library.Entries[0] with { Status = "paused" }];
        await f.Apply(new(State: PersonalSyncDirection.Upload)); Assert.Equal("on_hold", f.Cloud.States.Single().State); Assert.Equal("main", f.Cloud.States.Single().Completion);
    }
    [Theory] [InlineData("wish", "wish")] [InlineData("playing", "doing")] [InlineData("completed", "done")] [InlineData("paused", "on_hold")] [InlineData("dropped", "dropped")]
    public void StateVocabularyIsExplicit(string local, string cloud)
    { Assert.Equal(cloud, PersonalSyncPlanning.CloudStatus(local)); Assert.Equal(local, PersonalSyncPlanning.LocalStatus(cloud)); }
    [Fact] public async Task WishCompletionRemovalIsExplicitConflict()
    {
        var f = new Fixture(); await f.Login(); f.Cloud.States = [new("200", "done", "all", "2026-10-11T01:00:00Z")];
        f.Library.Entries = [f.Library.Entries[0] with { Status = "wish" }];
        var preview = await f.Sync.PreviewAsync(new(State: PersonalSyncDirection.Upload), default); Assert.True(preview.Rows.Single().Conflict);
        Assert.Null(preview.Rows.Single().Upload!.Completion); await f.Sync.ApplyAsync(preview, PersonalSyncCoordinator.DefaultChoices(preview), false, null, default);
        Assert.Equal("all", f.Cloud.States.Single().Completion); Assert.Equal(0, f.Cloud.Writes);
    }
    [Fact] public async Task RemoteOnlyStateChangeImportsInsteadOfRevertingToLocal()
    {
        var f = new Fixture(); await f.Login(); f.Library.Entries = [f.Library.Entries[0] with { Status = "playing" }];
        await f.Apply(new(State: PersonalSyncDirection.Upload)); f.Cloud.States = [new("200", "done", "one_route", "2026-10-11T02:00:00Z")];
        await f.Apply(new(State: PersonalSyncDirection.Bidirectional)); Assert.Equal("completed", f.Library.Entries.Single().Status); Assert.Equal(1, f.Cloud.Writes);
    }
    [Fact] public async Task SimultaneousChangesConflictAndStalePreviewCannotWrite()
    {
        var f = new Fixture(); await f.Login(); f.Library.Entries = [f.Library.Entries[0] with { Status = "playing" }]; await f.Apply(new(State: PersonalSyncDirection.Upload));
        f.Library.Entries = [f.Library.Entries[0] with { Status = "paused" }]; f.Cloud.States = [new("200", "done", "main", "2026-10-11T03:00:00Z")];
        var preview = await f.Sync.PreviewAsync(new(State: PersonalSyncDirection.Bidirectional), default); Assert.True(preview.Rows.Single().Conflict);
        var choice = new Dictionary<string, PersonalSyncChoice> { [preview.Rows.Single().Id] = PersonalSyncChoice.Import };
        f.Cloud.States = [new("200", "dropped", null, "2026-10-11T04:00:00Z")];
        await Assert.ThrowsAsync<OnlineException>(() => f.Sync.ApplyAsync(preview, choice, false, null, default)); Assert.Equal(1, f.Cloud.Writes); Assert.Equal("paused", f.Library.Entries.Single().Status);
    }
    [Fact] public async Task RatingIsIndependentReadOnlyAndPreservesDecimalsAndZero()
    {
        var f = new Fixture(); await f.Login(); f.Cloud.Ratings = [new("200", 9, "2026-10-11T01:00:00Z")];
        await f.Apply(new(ReadRatings: true)); Assert.Equal(8.5, f.Library.Entries.Single().PersonalRating); Assert.Equal(9, (await f.State()).Imported.Single().Rating);
        f.Library.Entries = [f.Library.Entries[0] with { PersonalRating = 0 }]; await f.Apply(new(ReadRatings: true)); Assert.Equal(0, f.Library.Entries.Single().PersonalRating); Assert.Equal(0, f.Cloud.Writes);
    }
    [Fact] public async Task FolderImportAddsConfirmedWishlistWithoutChangingLocalCategories()
    {
        var f = new Fixture(); await f.Login(); f.Cloud.Members["10"] = ["200", "201"];
        await f.Sync.MapFolderAsync(f.Cloud.Folders.Single(), [], default);
        var preview = await f.Sync.PreviewAsync(new(Folders: PersonalSyncDirection.Import), default); Assert.Equal(0, f.Library.Added);
        await f.Sync.ApplyAsync(preview, PersonalSyncCoordinator.DefaultChoices(preview), false, null, default);
        Assert.Equal(1, f.Library.Added); Assert.Empty(f.Library.Entries.Single(x => x.Metadata?.Id == "201").Installations);
        Assert.Equal("个人分类", f.Library.Entries.Single(x => x.Metadata?.Id == "200").Categories.Single());
        Assert.Equal(new[] { "200", "201" }, (await f.State()).Folders.Single().Members.Order().ToArray()); Assert.Equal(0, f.Cloud.Deletes);
    }
    [Fact] public async Task FolderDeletionsRequireChoicesAndNeverDeleteBookshelf()
    {
        var f = new Fixture(); await f.Login(); await f.Sync.MapFolderAsync(f.Cloud.Folders.Single(), ["200"], default);
        await f.Apply(new(Folders: PersonalSyncDirection.Upload)); Assert.Contains("200", f.Cloud.Members["10"]);
        f.Cloud.Members["10"] = []; var preview = await f.Sync.PreviewAsync(new(Folders: PersonalSyncDirection.Bidirectional), default);
        Assert.True(preview.Rows.Single().Conflict); Assert.True(preview.Rows.Single().DeleteLocalMembership);
        await f.Sync.ApplyAsync(preview, new Dictionary<string, PersonalSyncChoice> { [preview.Rows.Single().Id] = PersonalSyncChoice.Import }, false, null, default);
        Assert.Empty((await f.State()).Folders.Single().Members); Assert.Single(f.Library.Entries); Assert.Equal(0, f.Cloud.Deletes);
    }
    [Fact] public async Task CloudMemberDeletionNeedsDedicatedConfirmation()
    {
        var f = new Fixture(); await f.Login(); f.Cloud.Members["10"] = ["200"];
        await f.Sync.MapFolderAsync(f.Cloud.Folders.Single(), ["200"], default); await f.Apply(new(Folders: PersonalSyncDirection.Import));
        await f.Sync.MapFolderAsync(f.Cloud.Folders.Single(), [], default); var preview = await f.Sync.PreviewAsync(new(Folders: PersonalSyncDirection.Bidirectional), default);
        var choices = new Dictionary<string, PersonalSyncChoice> { [preview.Rows.Single().Id] = PersonalSyncChoice.Upload };
        await Assert.ThrowsAsync<OnlineException>(() => f.Sync.ApplyAsync(preview, choices, false, null, default)); Assert.Equal(0, f.Cloud.Deletes);
        await f.Sync.ApplyAsync(preview, choices, true, null, default); Assert.Equal(1, f.Cloud.Deletes); Assert.Single(f.Library.Entries);
    }
    [Fact] public async Task BatchPartialSuccessOnlyCheckpointsSuccessfulItems()
    {
        var f = new Fixture(); await f.Login(); f.Library.Entries = [f.Library.Entries[0] with { TotalPlaySeconds = 600 }, Entry("合成第二作") with { Id = "b", Metadata = Work("201"), TotalPlaySeconds = 1200 }];
        f.Cloud.Partial = true; var result = await f.Apply(new(Playtime: PersonalSyncDirection.Upload));
        Assert.Equal(1, result.Completed); Assert.Equal(1, result.Failed); Assert.Single((await f.State()).Works); Assert.Single(f.Cloud.Times); Assert.Null((await f.State()).LastSuccess);
    }
    [Fact] public async Task LostPostResponseSurvivesRestartAndRetriesSameIdempotencyKey()
    {
        var f = new Fixture(); await f.Login(); f.Library.Entries = [f.Library.Entries[0] with { TotalPlaySeconds = 600 }]; f.Cloud.LoseResponse = true;
        await Assert.ThrowsAsync<OnlineException>(() => f.Apply(new(Playtime: PersonalSyncDirection.Upload))); Assert.Single((await f.State()).Pending); Assert.Equal(10, f.Cloud.Times.Single().Minutes);
        f.Cloud.LoseResponse = false; var restarted = new PersonalSyncCoordinator(f.Identity.Auth, f.Cloud, f.Storage, f.Library, f.Metadata, f.Identity.Time);
        var result = await restarted.RetryPendingAsync(default); Assert.Equal(1, result.Completed); Assert.Empty((await f.State()).Pending);
        Assert.Equal(f.Cloud.Keys[0], f.Cloud.Keys[1]); Assert.Equal(1, f.Cloud.Writes); Assert.Equal(10, f.Cloud.Times.Single().Minutes);
    }
    [Fact] public async Task JournalWriteFailurePreventsRemoteWrite()
    {
        var f = new Fixture(); await f.Login(); f.Storage.FailAt = 2;
        await Assert.ThrowsAsync<OnlineException>(() => f.Apply(new(Playtime: PersonalSyncDirection.Upload))); Assert.Equal(0, f.Cloud.Writes); Assert.Equal(0, f.Library.Changes);
    }
    [Fact] public async Task ChangesDuringNetworkWaitRemainOutsideAcknowledgedCheckpoint()
    {
        var f = new Fixture(); await f.Login(); f.Library.Entries = [f.Library.Entries[0] with { TotalPlaySeconds = 600 }];
        f.Cloud.BeforeWrite = _ => { f.Library.Entries = [f.Library.Entries[0] with { TotalPlaySeconds = 1200 }]; return Task.CompletedTask; };
        await f.Apply(new(Playtime: PersonalSyncDirection.Upload));
        Assert.Equal(10, f.Cloud.Times.Single().Minutes); Assert.Equal(600, (await f.State()).Works.Single().LocalSeconds);
        Assert.Equal(1200, f.Library.Entries.Single().TotalPlaySeconds);
    }
    [Fact] public async Task AccountSwitchInvalidatesPreviewAndKeepsSeparateCheckpoints()
    {
        var f = new Fixture(); await f.Login(); var preview = await f.Sync.PreviewAsync(new(Playtime: PersonalSyncDirection.Upload), default);
        f.Identity.Protocol.Account = "99"; await f.Identity.Auth.SignInAsync(false, false, default);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Sync.ApplyAsync(preview, PersonalSyncCoordinator.DefaultChoices(preview), false, null, default));
        Assert.Equal("99", (await f.State()).AccountId); Assert.Empty((await f.State()).Works); Assert.Equal(0, f.Cloud.Writes);
    }
    [Fact] public async Task MergedOrAmbiguousIdentityCannotBecomeCloudWrite()
    {
        var f = new Fixture(); await f.Login(); f.Metadata.Merged = true;
        await Assert.ThrowsAsync<OnlineException>(() => f.Apply(new(Playtime: PersonalSyncDirection.Upload))); Assert.Equal(0, f.Cloud.Writes);
        f.Metadata.Merged = false; f.Library.Entries = f.Library.Entries.Add(f.Library.Entries[0] with { Id = "duplicate" });
        var preview = await f.Sync.PreviewAsync(new(Playtime: PersonalSyncDirection.Upload), default); Assert.All(preview.Rows, row => Assert.True(row.Conflict)); Assert.All(PersonalSyncCoordinator.DefaultChoices(preview), choice => Assert.Equal(PersonalSyncChoice.Skip, choice.Value));
    }
    [Fact] public async Task CancelPreviewApplyDoesNotChangeAnyData()
    {
        var f = new Fixture(); await f.Login(); var preview = await f.Sync.PreviewAsync(new(Playtime: PersonalSyncDirection.Upload), default);
        using var stop = new CancellationTokenSource(); stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Sync.ApplyAsync(preview, PersonalSyncCoordinator.DefaultChoices(preview), false, null, stop.Token)); Assert.Equal(0, f.Cloud.Writes); Assert.Equal(0, f.Storage.Saves);
    }
    [Fact] public async Task FolderCreateTimeoutReusesIntentAndDoesNotCreateDuplicateFolder()
    {
        var f = new Fixture(); await f.Login(); f.Cloud.LoseResponse = true;
        await Assert.ThrowsAsync<OnlineException>(() => f.Sync.CreateMappedFolderAsync("合成新收藏夹", ["200"], default));
        Assert.Single((await f.State()).FolderCreations!); f.Cloud.LoseResponse = false;
        await f.Sync.RetryPendingAsync(default); Assert.Equal(1, f.Cloud.Creates); Assert.Empty((await f.State()).FolderCreations!); Assert.Single((await f.State()).Folders);
    }
    [Fact] public async Task CursorIsOpaqueAndRepeatedCursorCannotLoop()
    {
        int reads = 0; var result = await PersonalSyncCoordinator.All<int>((cursor, _) => { reads++; return Task.FromResult(cursor is null
            ? new CloudPage<int>([1], "cur_opaque.abc", true) : new CloudPage<int>([2], null, false)); }, 1000, default);
        Assert.Equal(new[] { 1, 2 }, result.Items); Assert.Equal("cur_opaque.abc", result.Cursor); Assert.Equal(2, reads);
        await Assert.ThrowsAsync<OnlineException>(() => PersonalSyncCoordinator.All<int>((_, _) => Task.FromResult(new CloudPage<int>([1], "cur_same", true)), 1000, default));
    }
    [Fact] public void OnlyNewConfirmedSessionsPermitIncrementAndCarrySeconds()
    {
        var at = DateTimeOffset.Parse("2026-10-11T10:00:00Z"); var basis = new SyncWorkCheckpoint("a", "200", 60, 100, 100, "planned", null, null, [], at.ToString("o"), 5);
        var native = new NativeSession(1, "a", "i", at.AddSeconds(1), at.AddSeconds(1), at.AddSeconds(57), at.AddSeconds(57), 55 * NativeSession.TicksPerSecond,
            SessionLifecycle.Completed, SessionReason.ExitConfirmed, false, 1, Guid.NewGuid(), new(0, null), null);
        var local = Entry() with { Metadata = Work(), TotalPlaySeconds = 115, Sessions = [native.Project(Guid.NewGuid().ToString("D"))] };
        var row = PersonalSyncPlanning.Time(local, 100, basis, null, PersonalSyncDirection.Bidirectional);
        Assert.False(row.Conflict); Assert.Equal(101, row.Upload!.Minutes);
        var unproven = local with { Sessions = [local.Sessions[0] with { Native = null }] }; Assert.True(PersonalSyncPlanning.Time(unproven, 100, basis, null, PersonalSyncDirection.Bidirectional).Conflict);
        var imported = local with { Sessions = [local.Sessions[0] with { Native = native with { Reason = SessionReason.ImportedSnapshot } }] };
        Assert.Null(PersonalSyncPlanning.IndependentSeconds(imported, basis));
    }
}
