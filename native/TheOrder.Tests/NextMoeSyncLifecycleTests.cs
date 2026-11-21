using TheOrder.Application;
using TheOrder.Presentation;
using Xunit;

namespace TheOrder.Tests;

public sealed class NextMoeSyncLifecycleTests
{
    [Fact] public async Task ReadOnlyAcceptanceBlocksEveryCloudMutationBeforeTransport()
    {
        var remote = new PersonalSyncTests.Remote(); var api = new ReadOnlyPersonalApi(remote);
        foreach (var kind in new[] { PersonalSyncKind.Playtime, PersonalSyncKind.State, PersonalSyncKind.Folder, PersonalSyncKind.Rating })
            await Assert.ThrowsAsync<OnlineException>(() => api.WriteAsync(kind, "10", [new("200", 3, "done", "all")], Guid.NewGuid().ToString("D"), default));
        await Assert.ThrowsAsync<OnlineException>(() => api.CreateFolderAsync("合成夹", Guid.NewGuid().ToString("D"), default));
        await Assert.ThrowsAsync<OnlineException>(() => api.DeleteFolderItemAsync("10", "200", default));
        Assert.Equal(0, remote.Writes); Assert.Equal(0, remote.Creates); Assert.Equal(0, remote.Deletes); Assert.Empty(remote.Times); Assert.Empty(remote.States); Assert.Empty(remote.Members["10"]);
    }
    [Fact] public async Task LateAccountFactsCannotAppearAfterAccountSwitch()
    {
        var f = new PersonalSyncTests.Fixture(); await f.Login(); f.Cloud.Ratings = [new("200", 9, "2026-10-11T00:00:00Z")]; await f.Apply(new(ReadRatings: true));
        using var vm = new NextMoeSettingsViewModel(f.Identity.Auth, f.Sync); await vm.InitializeAsync();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Storage.BeforeLoad = async _ => { started.SetResult(); await release.Task; };
        var read = vm.ImportedFactsAsync("200", default); await started.Task;
        f.Identity.Protocol.Account = "99"; await f.Identity.Auth.SignInAsync(default); release.SetResult();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => read); f.Storage.BeforeLoad = null; await vm.InitializeAsync();
        Assert.Equal("99", vm.Status!.Account!.Id); Assert.Null(await vm.ImportedFactsAsync("200", default));
    }
    [Fact] public async Task CorruptAuthRemainsUnavailableUntilExplicitRecoveryAndLegacyKeyStaysIntact()
    {
        var f = new NextMoeAuthTests.Fixture(); f.Keys.Value = "synthetic-existing-key";
        f.Storage.Record = new(99, NextMoeAccessMode.ApiKey, null, null, []);
        await Assert.ThrowsAsync<OnlineException>(() => f.Auth.StatusAsync(default)); Assert.False(f.Auth.CatalogAvailable);
        await Assert.ThrowsAsync<OnlineException>(() => f.Auth.ReadAsync(default));
        await f.Auth.RecoverCorruptLoginAsync(default); Assert.True(f.Auth.CatalogAvailable); Assert.Null(await f.Auth.ReadAsync(default)); Assert.Equal("synthetic-existing-key", f.Keys.Value);
        Assert.Equal(NextMoeAccessMode.Anonymous, f.Storage.Record!.Mode);
    }
    [Fact] public async Task BackupPauseStopsNewAutomaticReadsAndResumesAfterward()
    {
        var f = new PersonalSyncTests.Fixture(); await f.Login();
        using var vm = new NextMoeSettingsViewModel(f.Identity.Auth, f.Sync);
        await vm.InitializeAsync();
        f.Cloud.Times = [new("200", 20)];
        vm.PauseCommands(true);
        await vm.AutomaticAsync();
        Assert.Equal(0, f.Cloud.Reads);
        vm.PauseCommands(false);
        await vm.AutomaticAsync();
        Assert.Equal(20, (await f.State()).Imported.Single().Minutes);
    }
    [Fact] public async Task UnmapOnlyRemovesIndependentMappingAndNeverCloudMembersOrBookshelf()
    {
        var f = new PersonalSyncTests.Fixture(); await f.Login(); var before = f.Library.Entries; f.Cloud.Members["10"] = ["200"];
        await f.Sync.MapFolderAsync(f.Cloud.Folders.Single(), ["200"], default); await f.Sync.UnmapFolderAsync("10", default);
        Assert.Empty((await f.State()).Folders); Assert.Equal(before, f.Library.Entries); Assert.Equal("200", f.Cloud.Members["10"].Single()); Assert.Equal(0, f.Cloud.Deletes);
    }
    [Fact] public async Task ReauthorizationEnablesAutomaticSyncEvenWithAnOldAllOffState()
    {
        var f = new PersonalSyncTests.Fixture();
        f.Identity.Storage.Record = new(1, NextMoeAccessMode.Account, new("42", "合成用户"), "synthetic-old-refresh",
            [.. NextMoeOAuthIdentity.CatalogScopes]);
        await f.Apply(new());
        f.Cloud.Times = [new("200", 20)];
        f.Cloud.States = [new("200", "done", "all", "2026-10-11T10:10:00Z")];
        f.Cloud.Ratings = [new("200", 9, "2026-10-11T10:10:00Z")];

        using var vm = new NextMoeSettingsViewModel(f.Identity.Auth, f.Sync);
        await vm.InitializeAsync();
        Assert.Equal("重新授权 NextMoe", vm.LoginAction);
        Assert.False(vm.Automatic);
        var reads = f.Cloud.Reads;
        await vm.AutomaticAsync();
        Assert.Equal(reads, f.Cloud.Reads);

        await vm.LoginAsync();
        Assert.True(vm.Automatic);
        Assert.Equal("重新登录 NextMoe", vm.LoginAction);
        await vm.AutomaticAsync();
        var records = (await f.State()).Imported.Single();
        Assert.Equal(20, records.Minutes);
        Assert.Equal("all", records.Completion);
        Assert.Null(records.Rating);
        Assert.Equal(0, f.Cloud.RatingReads);
        Assert.Equal(1, await vm.ImportRatingsAsync());
        Assert.Equal(9, (await f.State()).Imported.Single().Rating);
        Assert.Equal(1, f.Cloud.RatingReads);
        f.Cloud.Ratings = [];
        Assert.Equal(0, await vm.ImportRatingsAsync());
        Assert.Null((await f.State()).Imported.Single().Rating);
        Assert.Equal(20, (await f.State()).Imported.Single().Minutes);
        Assert.Equal("completed", f.Library.Entries.Single().Status);
        Assert.Equal(8.5, f.Library.Entries.Single().PersonalRating);
        Assert.Equal(0, f.Cloud.Writes);

        await vm.LogoutAsync();
        Assert.False(vm.Automatic);
        Assert.Null(await vm.ImportedFactsAsync("200", default));
    }
    [Fact] public async Task AutomaticRemoteOnlyTimeIncreaseImportsWithoutAdditionOrLocalHistoryChange()
    {
        var f = new PersonalSyncTests.Fixture(); await f.Login(); var local = f.Library.Entries[0]; f.Cloud.Times = [new("200", 10)];
        await f.Apply(new(Playtime: PersonalSyncDirection.Bidirectional, Automatic: true)); f.Cloud.Times = [new("200", 20)];
        var result = await f.Sync.AutomaticAsync(default); Assert.True(result?.Completed == 1, result?.Message); Assert.Equal(20, (await f.State()).Imported.Single().Minutes);
        Assert.Equal(local, f.Library.Entries[0]); Assert.Equal(0, f.Cloud.Writes);
        int reads = f.Cloud.Reads; Assert.Null(await f.Sync.AutomaticAsync(default)); Assert.Equal(reads, f.Cloud.Reads);
    }
    [Fact] public async Task AutomaticRemoteOnlyStateChangeImportsAndDoesNotClearCompletion()
    {
        var f = new PersonalSyncTests.Fixture(); await f.Login(); f.Cloud.States = [new("200", "wish", null, "2026-10-11T00:00:00Z")];
        await f.Apply(new(State: PersonalSyncDirection.Bidirectional, Automatic: true));
        f.Cloud.States = [new("200", "done", "all", "2026-10-11T10:10:00Z")];
        await f.Sync.AutomaticAsync(default); Assert.Equal("completed", f.Library.Entries[0].Status); Assert.Equal("all", (await f.State()).Imported.Single().Completion); Assert.Equal(0, f.Cloud.Writes);
    }
    [Fact] public async Task AutomaticKeepsConflictingCloudStateAndStillImportsIndependentTime()
    {
        var f = new PersonalSyncTests.Fixture(); await f.Login();
        f.Cloud.States = [new("200", "wish", null, "2026-10-11T00:00:00Z")];
        await f.Apply(new(State: PersonalSyncDirection.Bidirectional, Automatic: true));
        f.Library.Entries = [f.Library.Entries[0] with { Status = "playing" }];
        f.Cloud.States = [new("200", "done", "all", "2026-10-11T10:10:00Z")];
        f.Cloud.Times = [new("200", 14)];

        var result = await f.Sync.AutomaticAsync(default);
        Assert.Equal(1, result?.Completed);
        Assert.Equal("done", f.Cloud.States.Single().State);
        Assert.Equal("playing", f.Library.Entries.Single().Status);
        Assert.Equal(14, (await f.State()).Imported.Single().Minutes);
        Assert.Equal(0, f.Cloud.Writes);
    }

    [Fact] public async Task AutomaticUploadsTimeButLeavesStateToConfirmedEdits()
    {
        var f = new PersonalSyncTests.Fixture(); await f.Login();
        f.Library.Entries = [f.Library.Entries[0] with { TotalPlaySeconds = 180, Status = "playing" }];

        var result = await f.Sync.AutomaticAsync(default);
        Assert.Equal(1, result?.Completed);
        Assert.Equal(3, f.Cloud.Times.Single().Minutes);
        Assert.Empty(f.Cloud.States);
        Assert.Equal(1, f.Cloud.Writes);

        f.Identity.Time.Now += TimeSpan.FromMinutes(6);
        await f.Sync.AutomaticAsync(default);
        Assert.Equal(1, f.Cloud.Writes);
    }
    [Fact] public async Task AutomaticFolderSyncKeepsKnownRemovalsWhenOtherChangesCommit()
    {
        var f = new PersonalSyncTests.Fixture(); await f.Login();
        f.Cloud.Members["10"] = ["200"];
        await f.Sync.MapFolderAsync(f.Cloud.Folders.Single(), ["200"], default);
        await f.Apply(new(Folders: PersonalSyncDirection.Bidirectional));
        f.Cloud.Members["10"] = [];
        f.Cloud.Times = [new("200", 14)];

        await f.Sync.AutomaticAsync(default);
        Assert.Contains("200", (await f.State()).Folders.Single().CloudBaseline);
        f.Identity.Time.Now += TimeSpan.FromMinutes(6);
        await f.Sync.AutomaticAsync(default);
        Assert.Empty(f.Cloud.Members["10"]);
        Assert.Contains("200", (await f.State()).Folders.Single().Members);
        Assert.Equal(0, f.Cloud.Writes);
        Assert.Equal(0, f.Cloud.Deletes);
    }

    [Fact] public async Task RealReturnedCursorIsReusedForAutomaticReadButCommitRechecksFullSnapshot()
    {
        var f = new PersonalSyncTests.Fixture(); await f.Login(); int phase = 0;
        f.Cloud.TimePages = cursor => phase == 0 ? cursor is null ? new([new("200", 10)], "cur_second", true) : new([new("201", 2)], null, false)
            : new([new("200", 20)], null, false);
        await f.Sync.AutomaticAsync(default); int readIndex = f.Cloud.TimeCursors.Count; phase = 1;
        f.Identity.Time.Now += TimeSpan.FromMinutes(6);
        await f.Sync.AutomaticAsync(default); Assert.Equal("cur_second", f.Cloud.TimeCursors[readIndex]); Assert.Null(f.Cloud.TimeCursors[readIndex + 1]);
        Assert.Equal(20, (await f.State()).Imported.Single().Minutes); Assert.Equal(0, f.Cloud.Writes);
    }
    [Fact] public async Task ExpiredUnknownJobCanBeReviewedWithoutResendingIt()
    {
        var f = new PersonalSyncTests.Fixture(); await f.Login(); f.Library.Entries = [f.Library.Entries[0] with { TotalPlaySeconds = 180 }]; f.Cloud.LoseResponse = true;
        await Assert.ThrowsAsync<OnlineException>(() => f.Apply(new(Playtime: PersonalSyncDirection.Upload))); Assert.Single((await f.State()).Pending);
        f.Identity.Time.Now += TimeSpan.FromHours(25); await Assert.ThrowsAsync<OnlineException>(() => f.Sync.RetryPendingAsync(default));
        var result = await f.Sync.ReviewPendingAsync(default); Assert.Equal(0, result.Completed); Assert.Empty((await f.State()).Pending); Assert.Equal(1, f.Cloud.Writes); Assert.Equal(3, f.Cloud.Times.Single().Minutes);
        Assert.Equal(180, f.Library.Entries.Single().TotalPlaySeconds);
    }
    [Fact] public async Task RemovedCloudFolderIsVisibleConflictAndKeepsLocalMembers()
    {
        var f = new PersonalSyncTests.Fixture(); await f.Login(); await f.Sync.MapFolderAsync(f.Cloud.Folders.Single(), ["200"], default); f.Cloud.Folders = [];
        var preview = await f.Sync.PreviewAsync(new(Folders: PersonalSyncDirection.Bidirectional), default); Assert.True(preview.Rows.Single().Conflict);
        Assert.Equal(PersonalSyncChoice.Skip, PersonalSyncCoordinator.DefaultChoices(preview).Single().Value); Assert.Equal("200", (await f.State()).Folders.Single().Members.Single()); Assert.Equal(0, f.Cloud.Deletes);
    }
    [Fact] public async Task RatingChangedAfterPreviewIsNotSilentlyImportedAsOldValue()
    {
        var f = new PersonalSyncTests.Fixture(); await f.Login(); f.Cloud.Ratings = [new("200", 9, "2026-10-11T00:00:00Z")];
        var preview = await f.Sync.PreviewAsync(new(ReadRatings: true), default); f.Cloud.Ratings = [new("200", 3, "2026-10-11T10:00:00Z")];
        await Assert.ThrowsAsync<OnlineException>(() => f.Sync.ApplyAsync(preview, PersonalSyncCoordinator.DefaultChoices(preview), false, null, default)); Assert.Empty(f.Storage.Data);
    }
}
