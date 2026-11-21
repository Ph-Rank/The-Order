using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TheOrder.Application;
using TheOrder.Domain;
using TheOrder.Infrastructure.Legacy;
using TheOrder.Platform.Windows;
using TheOrder.Presentation;
using Xunit;

namespace TheOrder.Tests;

public sealed class OfflineClientTests
{
    private static readonly LegacyLibraryCodec Codec = new();
    private static readonly LegacyClientTextRules TextRules = new();
    private const string Clock = TransactionDifferentialTests.Clock;
    private static JsonDocument Oracle() => JsonDocument.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixtures", "offline-ui-oracle.json")));
    private static byte[] Input(JsonDocument oracle) => Encoding.UTF8.GetBytes("{\"format\":\"kun-gal-manager\",\"version\":1,\"games\":" + oracle.RootElement.GetProperty("games").GetRawText() + "}");
    public static IEnumerable<object[]> Queries() { using var d = Oracle(); return d.RootElement.GetProperty("queries").EnumerateArray().Select(c => new object[] { c.GetProperty("name").GetString()! }).ToArray(); }
    public static IEnumerable<object[]> Batches() { using var d = Oracle(); return d.RootElement.GetProperty("batches").EnumerateArray().Select(c => new object[] { c.GetProperty("name").GetString()! }).ToArray(); }
    public static IEnumerable<object[]> Labels() { using var d = Oracle(); return d.RootElement.GetProperty("labels").EnumerateArray().Select(c => new object[] { c.GetProperty("input").GetString()! }).ToArray(); }
    [Theory, MemberData(nameof(Queries)), Trait("Category", "UiDifferential")]
    public async Task QueryOrderMatchesRealElectron(string name)
    {
        using var oracle = Oracle(); using var p = new TransactionProfile(Input(oracle)); await using var client = Client(p);
        await client.LoadAsync(default);
        var c = oracle.RootElement.GetProperty("queries").EnumerateArray().Single(c => c.GetProperty("name").GetString() == name); var f = c.GetProperty("filters");
        var query = new LibraryQuery(f.GetProperty("query").GetString()!, f.GetProperty("view").GetString()!, f.GetProperty("status").GetString()!, f.GetProperty("favorites").GetBoolean(),
            f.GetProperty("category").GetString()!, f.GetProperty("tag").GetString()!, f.GetProperty("minRating").GetString()!, c.GetProperty("sort").GetString()!);
        var actual = await client.QueryAsync(query, default);
        Assert.Equal(c.GetProperty("ids").EnumerateArray().Select(v => v.GetString()), actual.Items.Select(i => i.Id));
        Assert.Equal(Input(oracle), File.ReadAllBytes(p.Main));
    }
    [Theory, MemberData(nameof(Labels)), Trait("Category", "UiDifferential")]
    public void LabelInputMatchesElectronWithoutChangingCase(string input)
    {
        using var d = Oracle(); var c = d.RootElement.GetProperty("labels").EnumerateArray().Single(c => c.GetProperty("input").GetString() == input);
        Assert.Equal(c.GetProperty("result").EnumerateArray().Select(v => v.GetString()), TextRules.SplitLabels(input));
    }
    [Theory, MemberData(nameof(Batches)), Trait("Category", "UiDifferential")]
    public async Task BatchUsesOneTransactionAndMatchesElectron(string name)
    {
        using var d = Oracle(); var c = d.RootElement.GetProperty("batches").EnumerateArray().Single(c => c.GetProperty("name").GetString() == name);
        using var p = new TransactionProfile(Input(d)); var writes = 0;
        await using var library = p.Open(new Probe((target, stage) => { if (target == PersistenceTarget.Main && stage == PersistenceStage.AfterReplace) writes++; return Task.CompletedTask; }));
        var raw = c.GetProperty("patch");
        ImmutableArray<string> Strings(string property) => raw.TryGetProperty(property, out var v) ? [.. v.EnumerateArray().Select(i => i.GetString()!)] : [];
        var patch = new BulkPersonalPatch
        {
            Status = raw.TryGetProperty("status", out var status) ? new(true, status.GetString()!) : default,
            Favorite = raw.TryGetProperty("favorite", out var favorite) ? new(true, favorite.GetBoolean()) : default,
            AddCategories = Strings("addCategories"), RemoveCategories = Strings("removeCategories"), AddTags = Strings("addTags"), RemoveTags = Strings("removeTags")
        };
        var error = await Record.ExceptionAsync(() => library.UpdateBatchAsync([.. c.GetProperty("ids").EnumerateArray().Select(v => v.GetString()!)], patch));
        var accepted = c.GetProperty("accepted").GetBoolean(); Assert.Equal(accepted, error is null); Assert.Equal(accepted ? 1 : 0, writes);
        TransactionDifferentialTests.EqualEntries(c.GetProperty("after"), library.Snapshot.Entries);
    }
    [Fact]
    public void UiOracleUsesCurrentSourcesAndLockedElectron()
    {
        using var d = Oracle(); Assert.Equal(38, d.RootElement.GetProperty("queries").GetArrayLength()); Assert.Equal(8, d.RootElement.GetProperty("batches").GetArrayLength());
        foreach (var hash in d.RootElement.GetProperty("hashes").EnumerateObject())
        {
            var source = File.ReadAllText(Path.Combine(ReferenceSources.Repository(), hash.Name)).Replace("\r\n", "\n");
            Assert.Equal(hash.Value.GetString(), Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(source))));
        }
        using var package = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(ReferenceSources.Repository(), "package.json")));
        Assert.Equal(package.RootElement.GetProperty("devDependencies").GetProperty("electron").GetString(), d.RootElement.GetProperty("electron").GetString());
    }
    private static OfflineLibraryClient Client(TransactionProfile p, IPersistenceProbe? probe = null) => new(new Factory(() => p.Open(probe)), TextRules);
    private static PersonalInput Form(GameDetails d) => new(d.Personal.Name, d.Personal.Status, d.Personal.Rating?.ToString(System.Globalization.CultureInfo.CurrentCulture) ?? "", d.Personal.Favorite, d.Personal.Notes, string.Join(", ", d.Personal.Categories), string.Join(", ", d.Personal.Tags));
    private static Task<UnsavedChoice> Stay() => Task.FromResult(UnsavedChoice.Stay);

    [Fact]
    public async Task ConfirmedSingleAndBatchEditsPublishOnlyDurablyChangedFields()
    {
        using var oracle = Oracle();
        using var profile = new TransactionProfile(Input(oracle));
        await using var client = Client(profile);
        await using var vm = new WorkspaceViewModel(client, new MemoryPreferences(), new(profile.Root), TextRules);
        await vm.InitializeAsync();
        var notifications = new List<ImmutableArray<CollectionEdit>>();
        vm.CollectionEdited += notifications.Add;

        await vm.NavigateAsync(WorkspacePage.Details, "entry-0", Stay);
        vm.Draft!.Notes = "note only";
        Assert.True((await vm.SaveAsync()).IsCommitted);
        Assert.Empty(notifications);
        vm.Draft!.Rating = "8";
        Assert.True((await vm.SaveAsync()).IsCommitted);
        var single = Assert.Single(Assert.Single(notifications));
        Assert.Equal(CollectionFields.Rating, single.Fields);
        Assert.Equal(8, single.Entry.PersonalRating);

        await vm.NavigateAsync(WorkspacePage.Library, null, Stay);
        var ids = ImmutableArray.Create("entry-0", "entry-1");
        var rejected = await vm.ApplyBatchAsync(ids, "all", "keep", "", "", false, "11");
        Assert.Equal(ClientOutcome.ValidationFailure, rejected.Outcome);
        Assert.Single(notifications);
        var accepted = await vm.ApplyBatchAsync(ids, "all", "keep", "", "", false, "9");
        Assert.True(accepted.IsCommitted);
        Assert.Equal(2, notifications[1].Length);
        Assert.All(notifications[1], edit =>
        {
            Assert.Equal(CollectionFields.Rating, edit.Fields);
            Assert.Equal(9, edit.Entry.PersonalRating);
        });
        Assert.All(client.CurrentSnapshot!.Entries.Where(entry => ids.Contains(entry.Id)), entry => Assert.Equal(9, entry.PersonalRating));
        var count = notifications.Count;
        Assert.True((await vm.ApplyBatchAsync(ids, "all", "keep", "", "", false, "9")).IsCommitted);
        Assert.Equal(count, notifications.Count);
    }

    [Fact]
    public async Task DraftSaveCancelAndRestartPreserveAuthoritativeState()
    {
        using var d = Oracle(); using var p = new TransactionProfile(Input(d));
        await using (var client = Client(p))
        {
            await using var vm = new WorkspaceViewModel(client, new MemoryPreferences(), new(p.Root), TextRules);
            await vm.InitializeAsync(); await vm.NavigateAsync(WorkspacePage.Details, "entry-0", Stay);
            var original = File.ReadAllBytes(p.Main); vm.Draft!.Name = "personal"; vm.Draft.Notes = "not saved";
            Assert.True(vm.IsDirty); Assert.Equal(original, File.ReadAllBytes(p.Main));
            vm.CancelDraft(); Assert.False(vm.IsDirty); Assert.NotEqual("personal", vm.Draft!.Name);
            vm.Draft.Name = "  personal title  "; vm.Draft.Notes = "saved"; vm.Draft.Tags = "Case,case, Case"; vm.Draft.Categories = "周末，new"; vm.Draft.Rating = "0"; vm.Draft.Favorite = false;
            Assert.True((await vm.SaveAsync()).IsCommitted); Assert.False(vm.IsDirty);
            Assert.Equal("personal title", vm.Draft!.Name); Assert.Equal("Case, case", vm.Draft.Tags);
        }
        await using var reopened = Client(p); await reopened.LoadAsync(default);
        var after = await reopened.DetailsAsync("entry-0", default); Assert.Equal("personal title", after.Personal.Name); Assert.Equal(0, after.Personal.Rating); Assert.False(after.Personal.Favorite); Assert.Equal("saved", after.Personal.Notes);
    }
    [Theory]
    [InlineData(UnsavedChoice.Stay)]
    [InlineData(UnsavedChoice.Discard)]
    [InlineData(UnsavedChoice.Save)]
    public async Task NavigationAndCloseRespectDirtyChoice(UnsavedChoice choice)
    {
        using var d = Oracle(); using var p = new TransactionProfile(Input(d)); await using var client = Client(p);
        await using var vm = new WorkspaceViewModel(client, new MemoryPreferences(), new(p.Root), TextRules);
        await vm.InitializeAsync(); await vm.NavigateAsync(WorkspacePage.Details, "entry-0", Stay); vm.Draft!.Notes = "draft";
        Assert.False(await vm.ReloadAsync());
        Assert.Equal(choice != UnsavedChoice.Stay, await vm.NavigateAsync(WorkspacePage.Settings, null, () => Task.FromResult(choice)));
        Assert.Equal(choice == UnsavedChoice.Stay ? WorkspacePage.Details : WorkspacePage.Settings, vm.Page);
        Assert.Equal(choice == UnsavedChoice.Save, (await client.DetailsAsync("entry-0", default)).Personal.Notes == "draft");
        if (choice == UnsavedChoice.Stay) Assert.False(await vm.ResolveUnsavedAsync(Stay));
    }
    [Fact]
    public async Task ConflictingEditRetainsDraftAndUnrelatedEditDoesNotInvalidateIt()
    {
        using var d = Oracle(); using var p = new TransactionProfile(Input(d)); await using var client = Client(p); await client.LoadAsync(default);
        var a = await client.DetailsAsync("entry-0", default); var b = await client.DetailsAsync("entry-1", default);
        Assert.True((await client.SaveAsync(b, Form(b) with { Notes = "other entry" }, default)).IsCommitted);
        Assert.True((await client.SaveAsync(a, Form(a) with { Notes = "mine" }, default)).IsCommitted);
        Assert.Equal(ClientOutcome.Conflict, (await client.SaveAsync(a, Form(a) with { Notes = "late" }, default)).Outcome);
        Assert.Equal("mine", (await client.DetailsAsync("entry-0", default)).Personal.Notes);
    }
    [Fact]
    public async Task UnchangedTitleDoesNotBecomeCustomWhenOnlyNotesChange()
    {
        var e = new LibraryEntry("a", "Catalog", false, null, [], null, "planned", null, false, [], [], "", 0, 0, [], Clock, null, null, null, new(false, null));
        using var p = new TransactionProfile(Codec.Encode([e], Clock)); await using var client = Client(p); await client.LoadAsync(default);
        var details = await client.DetailsAsync("a", default);
        await client.SaveAsync(details, Form(details) with { Notes = "changed" }, default);
        Assert.False(Codec.Decode(File.ReadAllBytes(p.Main))[0].NameIsCustom);
    }
    [Fact]
    public async Task PersonalDraftTokenCannotWriteDeletedOrRestoredIdentity()
    {
        using var d = Oracle(); var bytes = Input(d); using var p = new TransactionProfile(bytes); await using var library = p.Open();
        var details = await library.DetailsAsync("entry-0");
        await library.DeleteAsync("entry-0");
        await Assert.ThrowsAsync<StaleLibraryRequestException>(() => library.SavePersonalAsync(details.Token, new() { Notes = "late" }));
        await library.ImportAsync(bytes);
        var before = File.ReadAllBytes(p.Main);
        await Assert.ThrowsAsync<StaleLibraryRequestException>(() => library.SavePersonalAsync(details.Token, new() { Notes = "late after restoration" }));
        Assert.Equal(before, File.ReadAllBytes(p.Main));
    }
    [Theory]
    [InlineData("warning", ClientOutcome.CommittedWithWarning)]
    [InlineData("io", ClientOutcome.IoFailure)]
    [InlineData("validation", ClientOutcome.ValidationFailure)]
    [InlineData("cancel", ClientOutcome.Cancelled)]
    public async Task ActualTransactionOutcomeIsPresentedWithoutFalseFailure(string mode, ClientOutcome expected)
    {
        using var d = Oracle(); using var p = new TransactionProfile(Input(d)); var writes = 0;
        var probe = new Probe((target, stage) =>
        {
            if (target == PersistenceTarget.Main && stage == PersistenceStage.AfterReplace) { writes++; if (mode == "warning") throw new IOException("after replace"); }
            if (target == PersistenceTarget.Main && stage == PersistenceStage.BeforeReplace && mode == "io") throw new IOException("before replace");
            return Task.CompletedTask;
        });
        await using var client = Client(p, probe); await client.LoadAsync(default); var details = await client.DetailsAsync("entry-0", default);
        var result = await client.SaveAsync(details, Form(details) with { Notes = "modified", Rating = mode == "validation" ? "11" : "0" }, new(mode == "cancel"));
        Assert.Equal(expected, result.Outcome); Assert.Equal(mode == "warning" ? 1 : 0, writes);
        Assert.Equal(mode == "warning", (await client.DetailsAsync("entry-0", default)).Personal.Notes == "modified");
        if (mode == "warning") { Assert.Contains("已经保存", result.Message); Assert.DoesNotContain("保存失败", result.Message); }
    }
    [Fact]
    public async Task ReadOnlyAndCorruptionCannotMasqueradeAsEditableEmptyLibrary()
    {
        var fake = new ControlledClient { CapabilityValue = new(true, false, "可查看但不能安全修改") };
        await using var vm = new WorkspaceViewModel(fake, new MemoryPreferences(), new("synthetic"), TextRules);
        await vm.InitializeAsync(); await vm.NavigateAsync(WorkspacePage.Details, "a", Stay); vm.Draft!.Notes = "draft";
        Assert.True(vm.IsReadOnly); Assert.False(vm.CanSave); Assert.Equal(ClientOutcome.ReadOnly, (await vm.SaveAsync()).Outcome); Assert.Equal(0, fake.SaveCalls);
        using var p = new TransactionProfile("{corrupt"u8.ToArray()); await using var client = Client(p);
        Assert.Equal(ClientOutcome.LoadFailure, (await client.LoadAsync(default)).Outcome); Assert.False(client.Capability.Loaded); Assert.False(client.Capability.CanEdit);
    }
    [Fact]
    public async Task LateSearchAndDetailsCannotReplaceNewSelection()
    {
        var oldSearch = new TaskCompletionSource<LibraryQueryResult>(); var newSearch = new TaskCompletionSource<LibraryQueryResult>();
        var started = new TaskCompletionSource();
        var fake = new ControlledClient(); await using var vm = new WorkspaceViewModel(fake, new MemoryPreferences(), new("synthetic"), TextRules);
        await vm.InitializeAsync();
        fake.Search = (q, _) => { if (q.Text == "old") { started.TrySetResult(); return oldSearch.Task; } return newSearch.Task; };
        var old = vm.SetQueryAsync(new("old")); await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var latest = vm.SetQueryAsync(new("latest")); newSearch.SetResult(Result("latest")); await latest; oldSearch.SetResult(Result("old")); await old;
        Assert.Equal("latest", vm.Items.Single().Id);
        var oldDetails = new TaskCompletionSource<GameDetails>(); var entered = new TaskCompletionSource();
        fake.Detail = (id, _) => { if (id == "old") { entered.TrySetResult(); return oldDetails.Task; } return Task.FromResult(Detail(id)); };
        var first = vm.NavigateAsync(WorkspacePage.Details, "old", Stay); await entered.Task;
        await vm.NavigateAsync(WorkspacePage.Details, "latest", Stay); oldDetails.SetResult(Detail("old")); await first;
        Assert.Equal("latest", vm.SelectedId); Assert.Equal("latest", vm.Draft!.Source.Id);
    }
    [Fact]
    public async Task WarningRemainsVisibleAfterProjectionRefreshAndIsNeverRetried()
    {
        var fake = new ControlledClient { SaveResult = new(ClientOutcome.CommittedWithWarning, "修改已经保存，但存在附加警告") };
        await using var vm = new WorkspaceViewModel(fake, new MemoryPreferences(), new("synthetic"), TextRules);
        await vm.InitializeAsync(); await vm.NavigateAsync(WorkspacePage.Details, "a", Stay); vm.Draft!.Notes = "changed";
        await vm.SaveAsync(); Assert.Equal(1, fake.SaveCalls); Assert.Equal(ClientOutcome.CommittedWithWarning, vm.Notice.Outcome); Assert.False(vm.IsDirty);
    }
    [Fact]
    public async Task UiPreferencesPersistSeparatelyAndTemporaryQueryIsNotSavedFilter()
    {
        using var d = Oracle(); using var p = new TransactionProfile(Input(d)); var original = File.ReadAllBytes(p.Main);
        var store = new IsolatedUiPreferencesStore(new(p.Root));
        await using (var client = Client(p))
        {
            await using var vm = new WorkspaceViewModel(client, store, new(p.Root), TextRules); await vm.InitializeAsync();
            await vm.SetQueryAsync(new("temporary", Sort: "rating")); Assert.Empty(vm.SavedFilters);
            await vm.ChangeAppearanceAsync(NativeTheme.Dark, "list"); await vm.SaveFilterAsync("周末");
            await vm.SetQueryAsync(new("different", Sort: "name")); Assert.Equal("temporary", vm.SavedFilters.Single().Query.Text);
        }
        var preferences = await store.LoadAsync(default);
        using (var saved = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(p.Root, "native-ui.json"))))
            Assert.False(saved.RootElement.GetProperty("Preferences").TryGetProperty("Filters", out _));
        Assert.Equal(NativeTheme.Dark, preferences.Theme); Assert.Equal("list", preferences.Layout); Assert.Equal("name", preferences.Sort); Assert.Single(preferences.Filters);
        Assert.Equal(original, File.ReadAllBytes(p.Main));
        File.WriteAllText(Path.Combine(p.Root, "native-ui.json"), "{broken");
        await using var again = Client(p); await using var reopened = new WorkspaceViewModel(again, store, new(p.Root), TextRules);
        await reopened.InitializeAsync(); await reopened.ChangeAppearanceAsync(NativeTheme.Light, "grid");
        Assert.Equal("{broken", File.ReadAllText(Path.Combine(p.Root, "native-ui.json"))); Assert.True(reopened.IsLoaded);
    }
    [Fact]
    public async Task SaveDuringCloseAwaitsCommitAndKeepsWarning()
    {
        var release = new TaskCompletionSource<ClientResult>(); var fake = new ControlledClient { SaveHandler = (_, _, _) => release.Task };
        await using var vm = new WorkspaceViewModel(fake, new MemoryPreferences(), new("synthetic"), TextRules);
        await vm.InitializeAsync(); await vm.NavigateAsync(WorkspacePage.Details, "a", Stay); vm.Draft!.Notes = "save";
        var save = vm.SaveAsync(); var close = vm.ResolveUnsavedAsync(Stay); Assert.False(close.IsCompleted); Assert.False(vm.CanEdit);
        release.SetResult(new(ClientOutcome.CommittedWithWarning, "已经保存，附加警告")); await save; Assert.True(await close); Assert.Equal(1, fake.SaveCalls);
    }

    private sealed class Factory(Func<TransactionalLibrary> open) : ITransactionalLibraryFactory { public Task<TransactionalLibrary> OpenAsync(CancellationToken cancellation) => Task.Run(open, cancellation); }
    [Fact]
    public async Task ThousandSelectedEntriesUseOneDurableReplacement()
    {
        using var d = Oracle(); var template = Codec.Decode(Input(d))[0];
        var entries = Enumerable.Range(0, 1000).Select(i => template with { Id = "batch-" + i, Metadata = null, Installations = [], PreferredInstallationId = null }).ToImmutableArray();
        using var p = new TransactionProfile(Codec.Encode(entries, Clock)); var writes = 0;
        await using var client = Client(p, new Probe((target, stage) => { if (target == PersistenceTarget.Main && stage == PersistenceStage.AfterReplace) writes++; return Task.CompletedTask; }));
        await client.LoadAsync(default);
        Assert.True((await client.BatchAsync([.. entries.Select(e => e.Id)], new() { Status = new(true, "completed"), Favorite = new(true, true), AddTags = ["batch"] }, default)).IsCommitted);
        Assert.Equal(1, writes);
        Assert.All(Codec.Decode(File.ReadAllBytes(p.Main)), e => { Assert.Equal("completed", e.Status); Assert.True(e.Favorite); Assert.Contains("batch", e.CustomTags); });
    }
    [Theory]
    [InlineData(ClientOutcome.Conflict)]
    [InlineData(ClientOutcome.ValidationFailure)]
    [InlineData(ClientOutcome.IoFailure)]
    [InlineData(ClientOutcome.Cancelled)]
    public async Task FailedSaveRetainsEditableDraft(ClientOutcome outcome)
    {
        var fake = new ControlledClient { SaveResult = new(outcome, "未提交") };
        await using var vm = new WorkspaceViewModel(fake, new MemoryPreferences(), new("synthetic"), TextRules);
        await vm.InitializeAsync(); await vm.NavigateAsync(WorkspacePage.Details, "a", Stay); vm.Draft!.Notes = "keep draft";
        Assert.Equal(outcome, (await vm.SaveAsync()).Outcome); Assert.True(vm.IsDirty); Assert.Equal("keep draft", vm.Draft.Notes); Assert.True(vm.CanSave);
        Assert.False(await vm.ResolveUnsavedAsync(Stay)); Assert.Equal(1, fake.SaveCalls);
    }
    [Fact]
    public async Task ClosingWaitsForBatchAndPreferenceQueueWithoutCancellingAcceptedWork()
    {
        var release = new TaskCompletionSource<ClientResult>();
        var fake = new ControlledClient { BatchHandler = (_, _, _) => release.Task };
        var store = new GatedPreferences();
        var vm = new WorkspaceViewModel(fake, store, new("synthetic"), TextRules); await vm.InitializeAsync();
        var batch = vm.BatchAsync(["a"], new() { Favorite = new(true, true) });
        var close = vm.ResolveUnsavedAsync(Stay); Assert.False(close.IsCompleted);
        release.SetResult(new(ClientOutcome.Committed, "已保存")); await batch; Assert.True(await close);
        var first = vm.ChangeAppearanceAsync(NativeTheme.Dark, "list"); await store.Entered.Task;
        var second = vm.ChangeAppearanceAsync(NativeTheme.Light, "grid");
        var dispose = vm.DisposeAsync().AsTask(); Assert.False(dispose.IsCompleted); Assert.Equal(1, store.Calls);
        store.Release.SetResult(); await Task.WhenAll(first, second, dispose);
        Assert.Equal(2, store.Calls); Assert.Equal(NativeTheme.Light, store.Value.Theme); Assert.False(store.SawCancellation);
    }
    private sealed class GatedPreferences : IUiPreferencesStore
    {
        public readonly TaskCompletionSource Entered = new(), Release = new();
        public int Calls; public bool SawCancellation; public ClientPreferences Value = new();
        public Task<ClientPreferences> LoadAsync(CancellationToken cancellation) => Task.FromResult(Value);
        public async Task SaveAsync(ClientPreferences preferences, CancellationToken cancellation)
        { Calls++; Entered.TrySetResult(); await Release.Task; SawCancellation |= cancellation.IsCancellationRequested; Value = preferences; }
    }
    private sealed class Probe(Func<PersistenceTarget, PersistenceStage, Task> callback) : IPersistenceProbe { public Task ReachAsync(PersistenceTarget target, PersistenceStage stage) => callback(target, stage); }
    private sealed class MemoryPreferences : IUiPreferencesStore
    {
        public ClientPreferences Value = new();
        public Task<ClientPreferences> LoadAsync(CancellationToken cancellation) => Task.FromResult(Value);
        public Task SaveAsync(ClientPreferences preferences, CancellationToken cancellation) { Value = preferences; return Task.CompletedTask; }
    }
    private static GameDetails Detail(string id) => new(id, new(id, "planned", null, false, "", [], []), "metadata", "original", "related", "none", "zero", "placeholder", new(Guid.Empty, id, Guid.Empty, new(id, "planned", null, false, "", [], [])));
    private static LibraryQueryResult Result(string id) => new([new(id, id, "planned", null, false, "", 0, 0)], 1, [], []);
    private sealed class ControlledClient : IOfflineLibraryClient
    {
        public LibraryCapability CapabilityValue = new(true, true, "isolated"); public LibraryCapability Capability => CapabilityValue;
        public Func<LibraryQuery, CancellationToken, Task<LibraryQueryResult>> Search = (_, _) => Task.FromResult(Result("a"));
        public Func<string, CancellationToken, Task<GameDetails>> Detail = (id, _) => Task.FromResult(OfflineClientTests.Detail(id));
        public ClientResult SaveResult = new(ClientOutcome.Committed, "已经保存");
        public Func<GameDetails, PersonalInput, CancellationToken, Task<ClientResult>>? SaveHandler;
        public Func<ImmutableArray<string>, BulkPersonalPatch, CancellationToken, Task<ClientResult>>? BatchHandler;
        public int SaveCalls;
        public Task<ClientResult> LoadAsync(CancellationToken cancellation) => Task.FromResult(new ClientResult(ClientOutcome.Success, Capability.Message));
        public Task<LibraryQueryResult> QueryAsync(LibraryQuery query, CancellationToken cancellation) => Search(query, cancellation);
        public Task<GameDetails> DetailsAsync(string id, CancellationToken cancellation) => Detail(id, cancellation);
        public Task<ClientResult> SaveAsync(GameDetails details, PersonalInput input, CancellationToken cancellation) { SaveCalls++; return SaveHandler?.Invoke(details, input, cancellation) ?? Task.FromResult(SaveResult); }
        public Task<ClientResult> BatchAsync(ImmutableArray<string> ids, BulkPersonalPatch patch, CancellationToken cancellation) => BatchHandler?.Invoke(ids, patch, cancellation) ?? Task.FromResult(SaveResult);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
