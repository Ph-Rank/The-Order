using System.Collections.Immutable;
using TheOrder.Application;
using TheOrder.Infrastructure.Legacy;
using TheOrder.Presentation;
using Xunit;

namespace TheOrder.Tests;

public sealed class PresentationRemediationTests
{
    private static TaskCompletionSource<T> Gate<T>() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static Task<UnsavedChoice> Stay() => Task.FromResult(UnsavedChoice.Stay);
    private static GameDetails Detail(string id, string notes = "") => new(id, new(id, "planned", null, false, notes, [], []), "", "", "", "", "", "", new(Guid.Empty, id, Guid.Empty, new(id, "planned", null, false, notes, [], [])));
    private static LibraryQueryResult Rows(string id = "a") => new([new(id, id, "planned", null, false, "", 0, 0)], 1, [], []);
    private static WorkspaceViewModel Vm(Client client, Preferences? preferences = null, IClientTextRules? text = null, ICachedCoverRecommendationSource? covers = null) => new(client, preferences ?? new(), new("synthetic"), text ?? new LegacyClientTextRules(), covers);

    [Fact] public async Task SingleEditorDraftLeavesTheShelfAndSelectionUntouchedAndCancelDoesNotWrite()
    {
        var client = new Client(); await using var vm = Vm(client); await vm.InitializeAsync();
        await vm.SetQueryAsync(new(Text: "a", Sort: "name"));
        var query = vm.Query; var selection = vm.SelectionVersion;
        var edit = Assert.IsType<PersonalDraft>(await vm.LoadPersonalEditAsync("a"));
        edit.Name = "cancelled name"; edit.Notes = "cancelled notes";
        Assert.Equal(WorkspacePage.Library, vm.Page); Assert.Null(vm.SelectedId); Assert.Null(vm.Draft);
        Assert.False(vm.IsDirty); Assert.Equal(query, vm.Query); Assert.Equal(selection, vm.SelectionVersion);
        Assert.Equal(0, client.Saves); Assert.Equal("a", vm.Items.Single().Name);
    }

    [Fact] public async Task SingleEditorSavesTheOriginalEntryTokenWithoutNavigating()
    {
        var client = new Client(); await using var vm = Vm(client); await vm.InitializeAsync();
        var edit = Assert.IsType<PersonalDraft>(await vm.LoadPersonalEditAsync("a"));
        edit.Name = "saved name"; edit.Categories = "分类";
        Assert.True((await vm.SavePersonalEditAsync(edit)).IsCommitted);
        Assert.Same(edit.Source, client.SavedSource); Assert.Equal(edit.Input, client.SavedInput);
        Assert.Equal(1, client.Saves); Assert.Equal(WorkspacePage.Library, vm.Page); Assert.Null(vm.Draft);
    }

    [Fact] public async Task LateSingleEditorReadAndSaveCannotFollowNavigationToAnotherPage()
    {
        var client = new Client(); await using var vm = Vm(client); await vm.InitializeAsync();
        var edit = Assert.IsType<PersonalDraft>(await vm.LoadPersonalEditAsync("a"));
        var waiting = Gate<GameDetails>(); client.Detail = (_, _) => waiting.Task;
        var pending = vm.LoadPersonalEditAsync("a");
        await vm.NavigateAsync(WorkspacePage.Settings, null, Stay);
        waiting.SetResult(Detail("a")); Assert.Null(await pending);
        Assert.Equal(ClientOutcome.Conflict, (await vm.SavePersonalEditAsync(edit)).Outcome);
        Assert.Equal(0, client.Saves);
    }

    [Fact] public async Task SingleEditorConflictKeepsTheLocalDraftAndDoesNotRefreshTheShelf()
    {
        var client = new Client { SaveResult = new(ClientOutcome.Conflict, "changed elsewhere") };
        await using var vm = Vm(client); await vm.InitializeAsync();
        var edit = Assert.IsType<PersonalDraft>(await vm.LoadPersonalEditAsync("a")); edit.Name = "my changes";
        var rows = vm.Items;
        Assert.Equal(ClientOutcome.Conflict, (await vm.SavePersonalEditAsync(edit)).Outcome);
        Assert.True(edit.IsDirty); Assert.Equal("my changes", edit.Name); Assert.Same(rows, vm.Items);
        Assert.Null(vm.Draft); Assert.False(vm.IsSaving); Assert.Equal(WorkspacePage.Library, vm.Page);
    }

    private static GameSummary Automatic(string id = "a", string workId = "200") => new(id, id, "planned", null, false, "", 0, 0)
        { CatalogWorkId = workId, AutomaticCover = true, CoverKey = "old-" + workId };
    private sealed class CachedCovers : ICachedCoverRecommendationSource
    {
        public Func<string, CancellationToken, Task<CachedCoverRecommendation?>> Read = (id, _) => Task.FromResult<CachedCoverRecommendation?>(new("saved-" + id));
        public readonly List<string> Calls = [];
        public Task<CachedCoverRecommendation?> ReadCoverAsync(string id, CancellationToken token) { Calls.Add(id); return Read(id, token); }
    }
    [Fact] public async Task FirstPublishedShelfWaitsForSavedCoverAndNeverPublishesTheOldOne()
    {
        var entered = Gate<bool>(); var saved = Gate<CachedCoverRecommendation?>();
        var covers = new CachedCovers { Read = (_, _) => { entered.TrySetResult(true); return saved.Task; } };
        var client = new Client { Search = (_, _) => Task.FromResult(new LibraryQueryResult([Automatic()], 1, [], [])) };
        await using var vm = Vm(client, covers: covers); var frames = new List<string?>();
        vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(vm.Items)) frames.AddRange(vm.Items.Select(i => i.CoverKey)); };
        var startup = vm.InitializeAsync(); await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Empty(vm.Items); Assert.Empty(frames);
        saved.SetResult(new("persisted-choice")); await startup;
        Assert.NotEmpty(frames); Assert.All(frames, key => Assert.Equal("persisted-choice", key));
    }
    [Fact] public async Task SavedCoverRestorationPreservesManualAndLocalChoicesAndMissingCache()
    {
        var covers = new CachedCovers { Read = (id, _) => Task.FromResult<CachedCoverRecommendation?>(id == "201" ? null : new("saved")) };
        var rows = ImmutableArray.Create(Automatic(), Automatic("manual") with { AutomaticCover = false, CoverKey = "manual" },
            Automatic("local") with { AutomaticCover = false, HasLocalCover = true, CoverKey = "local" }, Automatic("missing", "201"));
        var client = new Client { Search = (_, _) => Task.FromResult(new LibraryQueryResult(rows, 4, [], [])) };
        await using var vm = Vm(client, covers: covers); await vm.InitializeAsync();
        Assert.Equal(new[] { "saved", "manual", "local", "old-201" }, vm.Items.Select(i => i.CoverKey));
        Assert.Equal(new[] { "200", "201", "200", "201" }, covers.Calls); // Two startup projections, once per work in each.
    }
    [Fact] public async Task FirstShelfRestoresMoreThanTheTransientPreparedEventLimit()
    {
        var rows = Enumerable.Range(1, 260).Select(i => Automatic(i.ToString(), i.ToString())).ToImmutableArray();
        var client = new Client { Search = (_, _) => Task.FromResult(new LibraryQueryResult(rows, rows.Length, [], [])) };
        await using var vm = Vm(client, covers: new CachedCovers()); await vm.InitializeAsync();
        Assert.Equal(260, vm.Items.Count); Assert.All(vm.Items, item => Assert.Equal("saved-" + item.CatalogWorkId, item.CoverKey));
    }
    [Fact] public async Task ANewPreparedEventWinsOverAnInFlightSavedCoverRead()
    {
        var entered = Gate<bool>(); var saved = Gate<CachedCoverRecommendation?>();
        var covers = new CachedCovers { Read = (_, _) => { entered.TrySetResult(true); return saved.Task; } };
        var client = new Client { Search = (_, _) => Task.FromResult(new LibraryQueryResult([Automatic()], 1, [], [])) };
        await using var vm = Vm(client, covers: covers); var startup = vm.InitializeAsync(); await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        vm.RefreshPreparedCover("200", "latest"); saved.SetResult(new("older-saved")); await startup;
        Assert.Equal("latest", vm.Items.Single().CoverKey);
    }
    [Fact] public async Task CancelledSavedCoverReadCannotReplaceANewerQuery()
    {
        var covers = new CachedCovers(); var client = new Client();
        await using var vm = Vm(client, covers: covers); await vm.InitializeAsync();
        var entered = Gate<bool>(); var saved = Gate<CachedCoverRecommendation?>();
        client.Search = (q, _) => Task.FromResult(new LibraryQueryResult([Automatic(q.Text, q.Text == "old" ? "200" : "201")], 1, [], []));
        covers.Read = (id, _) => { if (id == "200") { entered.TrySetResult(true); return saved.Task; } return Task.FromResult<CachedCoverRecommendation?>(new("latest")); };
        var old = vm.SetQueryAsync(new("old")); await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await vm.SetQueryAsync(new("new")); saved.SetResult(new("stale")); await old;
        Assert.Equal("new", vm.Items.Single().Id); Assert.Equal("latest", vm.Items.Single().CoverKey); Assert.False(vm.IsSearching);
    }

    [Fact] public async Task RejectedAutomaticCoverClearsOldPixelsAcrossReloadWithoutTouchingPersonalChoices()
    {
        GameSummary automatic = new("a", "a", "planned", null, false, "", 0, 0) { CatalogWorkId = "200", AutomaticCover = true, CoverKey = "old-scan" };
        var client = new Client { Search = (_, _) => Task.FromResult(new LibraryQueryResult([automatic, automatic with { Id = "b", AutomaticCover = false, CoverKey = "personal" }], 2, [], [])) };
        await using var vm = Vm(client); await vm.InitializeAsync();
        vm.RefreshPreparedCover("200", "temporary"); vm.RefreshPreparedCover("200", null);
        Assert.Null(vm.Items.Single(i => i.Id == "a").CoverKey);
        Assert.Equal("personal", vm.Items.Single(i => i.Id == "b").CoverKey);
        await vm.ReloadAsync(); Assert.Null(vm.Items.Single(i => i.Id == "a").CoverKey);
        vm.RefreshPreparedCover("200", "verified"); Assert.Equal("verified", vm.Items.Single(i => i.Id == "a").CoverKey);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CancelDelayedDetailsHonorsNewDraftRevision(bool typeAgain)
    {
        var client = new Client(); await using var vm = Vm(client); await vm.InitializeAsync(); await vm.NavigateAsync(WorkspacePage.Details, "a", Stay);
        vm.Draft!.Notes = "discard";
        var completion = Gate<GameDetails>(); client.Detail = (_, _) => completion.Task;
        var cancel = vm.CancelAndReloadDetailsAsync(); Assert.True(vm.IsDetailsLoading);
        if (typeAgain) vm.Draft!.Notes = "new input";
        completion.SetResult(Detail("a", "authoritative")); await cancel;
        Assert.Equal(typeAgain ? "new input" : "authoritative", vm.Draft!.Notes);
        Assert.Equal(typeAgain, vm.IsDirty); Assert.False(vm.IsDetailsLoading); Assert.True(vm.CanEdit);
    }
    [Fact]
    public async Task CancelReloadFailurePreservesCoherentEditableDraft()
    {
        var client = new Client(); await using var vm = Vm(client); await vm.InitializeAsync(); await vm.NavigateAsync(WorkspacePage.Details, "a", Stay);
        vm.Draft!.Notes = "discard"; client.Detail = (_, _) => Task.FromException<GameDetails>(new IOException());
        await vm.CancelAndReloadDetailsAsync(); Assert.False(vm.IsDirty); Assert.True(vm.CanEdit); Assert.False(vm.IsDetailsLoading); Assert.Equal(ClientOutcome.LoadFailure, vm.Notice.Outcome);
    }
    [Fact]
    public async Task CancelReloadCannotReplaceAnotherGameOrLifecycle()
    {
        var client = new Client(); await using var vm = Vm(client); await vm.InitializeAsync(); await vm.NavigateAsync(WorkspacePage.Details, "a", Stay);
        var delayed = Gate<GameDetails>(); client.Detail = (id, _) => id == "a" ? delayed.Task : Task.FromResult(Detail(id));
        var cancel = vm.CancelAndReloadDetailsAsync(); await vm.NavigateAsync(WorkspacePage.Details, "b", Stay);
        delayed.SetResult(Detail("a")); await cancel; Assert.Equal("b", vm.Draft!.Source.Id);
        client.Detail = (_, _) => Task.FromResult(Detail("b") with { Token = Detail("b").Token with { Generation = Guid.NewGuid() } });
        await vm.CancelAndReloadDetailsAsync(); Assert.Equal(Guid.Empty, vm.Draft.Source.Token.Generation); Assert.Equal(ClientOutcome.Conflict, vm.Notice.Outcome);
    }
    [Fact]
    public async Task SaveInvalidatesPendingCancelRefresh()
    {
        var client = new Client(); await using var vm = Vm(client); await vm.InitializeAsync(); await vm.NavigateAsync(WorkspacePage.Details, "a", Stay);
        var delayed = Gate<GameDetails>(); client.Detail = (_, _) => delayed.Task;
        var cancel = vm.CancelAndReloadDetailsAsync(); vm.Draft!.Notes = "new input";
        client.Detail = (id, _) => Task.FromResult(Detail(id, "new input")); await vm.SaveAsync();
        delayed.SetResult(Detail("a", "stale")); await cancel;
        Assert.Equal("new input", vm.Draft!.Notes); Assert.False(vm.IsDirty);
    }
    [Fact]
    public async Task StartupAppliesSavedThemeBeforeOpeningLibraryAndReadsPreferencesOnce()
    {
        var saved = new ClientPreferences(NativeTheme.Light, "list", "rating");
        var preferenceReads = 0;
        var libraryReads = 0;
        var store = new Preferences { Load = _ => { preferenceReads++; return Task.FromResult(saved); } };
        var client = new Client { Load = _ => { libraryReads++; return Task.FromResult(new ClientResult(ClientOutcome.Success, "")); } };
        await using var vm = Vm(client, store);
        await vm.PrepareStartupThemeAsync();
        Assert.Equal(NativeTheme.Light, vm.Theme);
        Assert.Equal(0, libraryReads);

        await vm.InitializeAsync();
        Assert.Equal(1, preferenceReads);
        Assert.Equal(1, libraryReads);
        Assert.Equal(saved, vm.Preferences);
        Assert.Equal("rating", vm.Query.Sort);
        Assert.Empty(store.Writes);
    }
    [Fact]
    public async Task FailedStartupPreferenceReadIsReportedWithoutOverwritingPreferences()
    {
        var reads = 0;
        var store = new Preferences { Load = _ => { reads++; throw new InvalidDataException("corrupt"); } };
        await using var vm = Vm(new(), store);
        await vm.PrepareStartupThemeAsync();
        Assert.Equal(NativeTheme.System, vm.Theme);

        await vm.InitializeAsync();
        Assert.Equal(1, reads);
        Assert.Equal(PreferenceInitialization.LoadFailed, vm.PreferenceState);
        Assert.Equal(ClientOutcome.ValidationFailure, vm.Notice.Outcome);
        await vm.ChangeAppearanceAsync(NativeTheme.Light, vm.Layout);
        Assert.Empty(store.Writes);
    }
    [Theory]
    [InlineData("theme")]
    [InlineData("layout")]
    [InlineData("sort")]
    [InlineData("rapid")]
    public async Task EarlyPreferencesMergeOnlyUserOverrides(string operation)
    {
        var entered = Gate<bool>(); var loaded = Gate<ClientPreferences>();
        var old = new ClientPreferences(NativeTheme.Dark, "grid", "rating", [new("keep", "existing", new("old"))]);
        var store = new Preferences { Load = _ => { entered.SetResult(true); return loaded.Task; } };
        await using var vm = Vm(new(), store); var initialization = vm.InitializeAsync(); await entered.Task;
        Assert.Equal(PreferenceInitialization.Loading, vm.PreferenceState);
        if (operation is "theme" or "rapid") await vm.ChangeAppearanceAsync(NativeTheme.Light, vm.Layout);
        if (operation == "layout") await vm.ChangeAppearanceAsync(vm.Theme, "list");
        if (operation == "sort") await vm.SetQueryAsync(new(Sort: "name"));
        if (operation == "rapid") { await vm.ChangeAppearanceAsync(NativeTheme.Dark, "list"); await vm.ChangeAppearanceAsync(NativeTheme.Light, "grid"); }
        Assert.Empty(store.Writes); loaded.SetResult(old); await initialization;
        Assert.Equal(PreferenceInitialization.Ready, vm.PreferenceState); Assert.Equal("keep", vm.SavedFilters.Single().Id);
        Assert.Equal(operation is "theme" or "rapid" ? NativeTheme.Light : NativeTheme.Dark, vm.Theme);
        Assert.Equal(operation == "layout" ? "list" : "grid", vm.Layout);
        Assert.Equal(operation == "sort" ? "name" : "rating", vm.Query.Sort);
        Assert.Equal(vm.Preferences, store.Writes.Last());
    }
    [Fact]
    public async Task FailedPreferenceLoadNeverWritesEvenAfterIntentionalChanges()
    {
        var entered = Gate<bool>(); var loaded = Gate<ClientPreferences>();
        var store = new Preferences { Load = _ => { entered.SetResult(true); return loaded.Task; } };
        await using var vm = Vm(new(), store); var initialization = vm.InitializeAsync(); await entered.Task;
        await vm.ChangeAppearanceAsync(NativeTheme.Dark, "list"); loaded.SetException(new InvalidDataException("corrupt")); await initialization;
        Assert.Equal(PreferenceInitialization.LoadFailed, vm.PreferenceState); Assert.Empty(store.Writes);
        await vm.ChangeAppearanceAsync(NativeTheme.Light, "grid"); await vm.SetQueryAsync(new(Sort: "name")); await vm.SaveFilterAsync("not persisted");
        Assert.Equal(NativeTheme.Light, vm.Theme); Assert.Equal("name", vm.Query.Sort); Assert.Empty(store.Writes); Assert.False(vm.CanManageFilters);
    }
    [Theory]
    [InlineData(ClientOutcome.Committed, false)]
    [InlineData(ClientOutcome.CommittedWithWarning, false)]
    [InlineData(ClientOutcome.Committed, true)]
    [InlineData(ClientOutcome.CommittedWithWarning, true)]
    public async Task CommitAndRefreshFailureHaveSeparateOutcomes(ClientOutcome outcome, bool detailsFail)
    {
        var client = new Client { SaveResult = new(outcome, outcome == ClientOutcome.CommittedWithWarning ? "修改已经保存；commit warning" : "修改已经保存") };
        await using var vm = Vm(client); await vm.InitializeAsync(); await vm.NavigateAsync(WorkspacePage.Details, "a", Stay); vm.Draft!.Notes = "saved";
        client.Search = (_, _) => Task.FromException<LibraryQueryResult>(new IOException("refresh"));
        client.Detail = detailsFail ? (_, _) => Task.FromException<GameDetails>(new IOException()) : (id, _) => Task.FromResult(Detail(id, "saved"));
        var receipt = await vm.SaveAsync(); Assert.Equal(outcome, receipt.Outcome); Assert.Equal(outcome, vm.PersistenceResult!.Outcome);
        Assert.Equal(RefreshOutcome.Failed, vm.LastRefresh); Assert.Equal(ClientOutcome.CommittedWithWarning, vm.Notice.Outcome);
        Assert.Contains("已经保存", vm.Notice.Message); Assert.Contains("刷新失败", vm.Notice.Message);
        if (outcome == ClientOutcome.CommittedWithWarning) Assert.Contains("commit warning", vm.Notice.Message);
        Assert.False(vm.IsDirty); Assert.Equal(1, client.Saves); Assert.False(vm.CanSave);
        Assert.Equal(!detailsFail, vm.CanEditDetails);
        if (!detailsFail) Assert.Equal("saved", vm.Draft!.Notes);
    }
    [Fact]
    public async Task FailedCommitDoesNotRefreshOrRetireDraft()
    {
        var client = new Client { SaveResult = new(ClientOutcome.IoFailure, "没有保存") }; await using var vm = Vm(client);
        await vm.InitializeAsync(); await vm.NavigateAsync(WorkspacePage.Details, "a", Stay); vm.Draft!.Notes = "retain";
        client.Search = (_, _) => throw new Xunit.Sdk.XunitException("No refresh after failed commit");
        Assert.Equal(ClientOutcome.IoFailure, (await vm.SaveAsync()).Outcome); Assert.Equal("retain", vm.Draft.Notes); Assert.True(vm.IsDirty); Assert.Equal(RefreshOutcome.None, vm.LastRefresh);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConfirmedBatchPreprocessingIsTrackedThroughCloseOrCancellation(bool cancel)
    {
        using var text = new GatedText(); var client = new Client(); var vm = Vm(client, text: text); await vm.InitializeAsync();
        var batch = vm.ApplyBatchAsync(["a"], "all", "keep", "", "label", false); await text.Entered.Task;
        Assert.True(vm.IsSaving); var close = vm.ResolveUnsavedAsync(Stay); Assert.False(close.IsCompleted);
        if (cancel) vm.CancelPendingOperation();
        text.Release.Set(); var result = await batch; Assert.True(await close);
        Assert.Equal(cancel ? ClientOutcome.Cancelled : ClientOutcome.Committed, result.Outcome); Assert.Equal(cancel ? 0 : 1, client.Batches);
        if (cancel) { Assert.Contains("未写入本次修改", result.Message); Assert.DoesNotContain("草稿已保留", result.Message); }
        Assert.False(vm.IsSaving); await vm.DisposeAsync();
    }
    [Fact]
    public async Task ConfirmedBatchAdmissionDrainsAndPreservesWarning()
    {
        var entered = Gate<bool>(); var finished = Gate<ClientResult>(); var client = new Client { Batch = (_, _, _) => { entered.SetResult(true); return finished.Task; } };
        await using var vm = Vm(client); await vm.InitializeAsync(); var batch = vm.ApplyBatchAsync(["a"], "all", "keep", "", "label", false); await entered.Task;
        var close = vm.ResolveUnsavedAsync(Stay); Assert.False(close.IsCompleted);
        finished.SetResult(new(ClientOutcome.CommittedWithWarning, "已经保存，warning")); await batch; Assert.True(await close);
        Assert.Equal(ClientOutcome.CommittedWithWarning, vm.Notice.Outcome); Assert.Equal(1, client.Batches);
    }
    [Fact]
    public async Task BatchParseFailureIsVisibleAndNeverEscapesUiEntry()
    {
        using var text = new GatedText { Fail = true }; text.Release.Set(); var client = new Client(); await using var vm = Vm(client, text: text); await vm.InitializeAsync();
        var result = await vm.ApplyBatchAsync(["a"], "all", "keep", "", "bad", false);
        Assert.Equal(ClientOutcome.ValidationFailure, result.Outcome); Assert.Contains("invalid labels", vm.Notice.Message); Assert.Equal(0, client.Batches); Assert.False(vm.IsSaving);
    }
    [Theory]
    [InlineData("success")]
    [InlineData("failure")]
    [InlineData("cancel")]
    [InlineData("new-query")]
    public async Task ReloadOwnsAndTerminatesSupersededSearch(string mode)
    {
        var client = new Client(); await using var vm = Vm(client); await vm.InitializeAsync();
        var entered = Gate<bool>(); var delayed = Gate<LibraryQueryResult>();
        client.Search = (q, _) => { if (q.Text == "old") { entered.TrySetResult(true); return delayed.Task; } return Task.FromResult(Rows(q.Text)); };
        var search = vm.SetQueryAsync(new("old")); await entered.Task;
        var load = Gate<ClientResult>(); client.Load = _ => load.Task; var reload = vm.ReloadAsync(); Assert.False(vm.IsSearching);
        await vm.SetQueryAsync(new(mode == "new-query" ? "latest" : "next"));
        if (mode == "failure") { client.CapabilityValue = new(false, false, "error"); load.SetResult(new(ClientOutcome.LoadFailure, "error")); }
        else if (mode == "cancel") load.SetCanceled(); else load.SetResult(new(ClientOutcome.Success, ""));
        await reload; delayed.SetException(new IOException("old error")); await search;
        Assert.False(vm.IsSearching); Assert.False(vm.IsLoading);
        if (mode == "new-query") Assert.Equal("latest", vm.Items.Single().Id);
        if (mode == "failure") Assert.Equal("error", vm.Notice.Message);
    }
    [Theory]
    [InlineData("layout")]
    [InlineData("sort")]
    [InlineData("filter")]
    [InlineData("search")]
    [InlineData("reload")]
    public async Task ExistingLibraryActionsExplicitlyInvalidateSelection(string action)
    {
        await using var vm = Vm(new()); await vm.InitializeAsync(); var version = vm.SelectionVersion;
        switch (action)
        {
            case "layout": await vm.ChangeAppearanceAsync(vm.Theme, "list"); break;
            case "sort": await vm.SetQueryAsync(new(Sort: "name")); break;
            case "filter": await vm.SetQueryAsync(new(Favorites: true)); break;
            case "search": await vm.SetQueryAsync(new("hidden")); break;
            default: await vm.ReloadAsync(); break;
        }
        Assert.True(vm.SelectionVersion > version);
    }
    [Fact]
    public async Task QueryDuringReloadProjectionWinsAfterNewSnapshotWasOpened()
    {
        var client = new Client(); await using var vm = Vm(client); await vm.InitializeAsync();
        var entered = Gate<bool>(); var delayed = Gate<LibraryQueryResult>();
        client.Search = (q, _) => { if (q.Text == "") { entered.TrySetResult(true); return delayed.Task; } return Task.FromResult(Rows(q.Text)); };
        var reload = vm.ReloadAsync(); await entered.Task;
        await vm.SetQueryAsync(new("latest")); delayed.SetResult(Rows("old")); await reload;
        Assert.Equal("latest", vm.Items.Single().Id); Assert.False(vm.IsSearching); Assert.False(vm.IsLoading);
    }
    [Fact]
    public async Task ConfirmedBatchCommitWarningSurvivesQueryRefreshFailure()
    {
        var client = new Client { SaveResult = new(ClientOutcome.CommittedWithWarning, "已经保存，commit warning") };
        await using var vm = Vm(client); await vm.InitializeAsync();
        client.Search = (_, _) => Task.FromException<LibraryQueryResult>(new IOException());
        var result = await vm.ApplyBatchAsync(["a"], "all", "keep", "", "tag", false);
        Assert.True(result.IsCommitted); Assert.Equal(1, client.Batches); Assert.Contains("commit warning", vm.Notice.Message);
        Assert.Contains("刷新失败", vm.Notice.Message); Assert.Equal(RefreshOutcome.Failed, vm.LastRefresh);
    }
    private sealed class Preferences : IUiPreferencesStore
    {
        public Func<CancellationToken, Task<ClientPreferences>> Load = _ => Task.FromResult(new ClientPreferences());
        public readonly List<ClientPreferences> Writes = [];
        public Task<ClientPreferences> LoadAsync(CancellationToken token) => Load(token);
        public Task SaveAsync(ClientPreferences value, CancellationToken token) { Writes.Add(value); return Task.CompletedTask; }
    }
    private sealed class Client : IOfflineLibraryClient
    {
        public LibraryCapability CapabilityValue = new(true, true, ""); public LibraryCapability Capability => CapabilityValue;
        public Func<CancellationToken, Task<ClientResult>> Load = _ => Task.FromResult(new ClientResult(ClientOutcome.Success, ""));
        public Func<LibraryQuery, CancellationToken, Task<LibraryQueryResult>> Search = (_, _) => Task.FromResult(Rows());
        public Func<string, CancellationToken, Task<GameDetails>> Detail = (id, _) => Task.FromResult(PresentationRemediationTests.Detail(id));
        public Func<ImmutableArray<string>, BulkPersonalPatch, CancellationToken, Task<ClientResult>>? Batch;
        public ClientResult SaveResult = new(ClientOutcome.Committed, "已经保存"); public int Saves, Batches;
        public GameDetails? SavedSource; public PersonalInput? SavedInput;
        public Task<ClientResult> LoadAsync(CancellationToken t) => Load(t);
        public Task<LibraryQueryResult> QueryAsync(LibraryQuery q, CancellationToken t) => Search(q, t);
        public Task<GameDetails> DetailsAsync(string id, CancellationToken t) => Detail(id, t);
        public Task<ClientResult> SaveAsync(GameDetails d, PersonalInput p, CancellationToken t) { Saves++; SavedSource = d; SavedInput = p; return Task.FromResult(SaveResult); }
        public Task<ClientResult> BatchAsync(ImmutableArray<string> ids, BulkPersonalPatch patch, CancellationToken t) { Batches++; return Batch?.Invoke(ids, patch, t) ?? Task.FromResult(SaveResult); }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    // Deliberately stalls only the background parser, never a dispatcher or production operation.
    private sealed class GatedText : IClientTextRules, IDisposable
    {
        private readonly LegacyClientTextRules inner = new(); public readonly ManualResetEventSlim Release = new(); public readonly TaskCompletionSource<bool> Entered = Gate<bool>(); public bool Fail;
        public ImmutableArray<string> SplitLabels(string value) { Entered.TrySetResult(true); if (!Release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException(); if (Fail) throw new InvalidDataException("invalid labels"); return inner.SplitLabels(value); }
        public string NormalizeSearch(string v) => inner.NormalizeSearch(v); public ImmutableArray<string> QueryTerms(string v) => inner.QueryTerms(v);
        public int CompareNames(string a, string b) => inner.CompareNames(a, b); public int CompareIds(string a, string b) => inner.CompareIds(a, b);
        public void Dispose() => Release.Dispose();
    }
}
