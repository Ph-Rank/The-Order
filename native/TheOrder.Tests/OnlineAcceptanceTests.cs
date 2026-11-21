using System.Net;
using System.Security.Cryptography;
using System.Text;
using TheOrder.Application;
using TheOrder.Domain;
using TheOrder.Infrastructure.Legacy;
using TheOrder.Infrastructure.Online;
using TheOrder.Platform.Windows;
using TheOrder.Presentation;
using Xunit;
using static TheOrder.Tests.OnlineTransportTests;

namespace TheOrder.Tests;

public sealed class OnlineAcceptanceTests
{
    private static HttpResponseMessage Work(string id = "86") => Json("{\"object\":\"work\",\"id\":\"" + id + "\",\"display_name\":\"Synthetic\"}");
    private static HttpResponseMessage Merge(string id) => Json("{\"object\":\"work\",\"code\":\"ENTITY_MERGED\",\"current_id\":\"" + id + "\"}", 404);
    private sealed class Store : ICredentialProvider, ICredentialSettings
    {
        public string? Value = "synthetic-old";
        public int Clears;
        public Func<string?, Task> Before = _ => Task.CompletedTask;
        public Task<string?> ReadAsync(CancellationToken c) { c.ThrowIfCancellationRequested(); return Task.FromResult(Value); }
        public Task<bool> HasCredentialAsync(CancellationToken c) => Task.FromResult(Value is not null);
        public async Task SaveAsync(string secret, CancellationToken c) { await Before(secret); c.ThrowIfCancellationRequested(); Value = secret; }
        public async Task ClearAsync(CancellationToken c) { Clears++; await Before(null); c.ThrowIfCancellationRequested(); Value = null; }
    }
    private sealed class LateConnectionCatalog(IOnlineCatalog inner) : IOnlineCatalog
    {
        public TaskCompletionSource Connection = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task TestConnectionAsync(CancellationToken c) => Connection.Task; // Deliberately ignores cancellation.
        public Task<OnlinePage> SearchAsync(string q, string? cursor, CancellationToken c) => inner.SearchAsync(q, cursor, c);
        public Task<WorkMetadata> DetailsAsync(string id, bool refresh, CancellationToken c) => inner.DetailsAsync(id, refresh, c);
        public void ClearCache() => inner.ClearCache();
    }
    private sealed class Lab : IAsyncDisposable
    {
        public readonly TransactionProfile Profile = new();
        public readonly Store Store = new();
        public readonly AuthenticatedCredentials Credentials;
        public readonly CatalogTransport Transport;
        public readonly NextMoeCatalog Catalog;
        public readonly LateConnectionCatalog Late;
        public readonly OfflineLibraryClient Offline;
        public readonly OnlineLibraryClient Library;
        public readonly WorkspaceViewModel Workspace;
        public readonly OnlineViewModel Vm;
        public int Calls;
        public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Send = (_, _) => Task.FromResult(Work());
        private readonly HttpClient http;
        public Lab(bool lateConnection = false)
        {
            Credentials = new(Store, Store);
            http = new(new Handler((r, c) => { Calls++; return Send(r, c); }));
            Transport = new(http, Credentials, new("https://api.nextmoe.dev/v2/")); Catalog = new(Transport); Late = new(Catalog);
            var configuration = new NativeConfiguration(Profile.Root); var rules = new LegacyClientTextRules();
            Offline = new(new IsolatedLibraryFactory(new NativeLibraryCodec(), configuration), rules);
            Workspace = new(Offline, new IsolatedUiPreferencesStore(configuration), configuration, rules);
            Library = new(Offline, Catalog);
            Vm = new(lateConnection ? Late : Catalog, Credentials, new RemoteCoverSource(http), Library, Workspace);
        }
        public async ValueTask DisposeAsync() { Late.Connection.TrySetResult(); await Vm.DisposeAsync(); await Workspace.DisposeAsync(); await Offline.DisposeAsync(); http.Dispose(); Profile.Dispose(); }
    }
    [Theory][InlineData(false)][InlineData(true)]
    public async Task CredentialIntentSupersedesUncooperativeConnectionTest(bool save)
    {
        await using var lab = new Lab(true); await lab.Vm.CheckCredentialAsync();
        var old = lab.Vm.TestConnectionAsync(); Assert.Equal(OnlineState.Loading, lab.Vm.ConnectionState);
        if (save) Assert.True(await lab.Vm.SaveCredentialAsync("synthetic-new")); else await lab.Vm.ClearCredentialAsync();
        Assert.Equal(save ? "synthetic-new" : null, lab.Store.Value); Assert.Equal(save ? 0 : 1, lab.Store.Clears);
        lab.Late.Connection.SetResult(); await old;
        Assert.Equal(save, lab.Vm.HasCredential); Assert.Equal(OnlineState.Idle, lab.Vm.ConnectionState);
        Assert.DoesNotContain("连接成功", lab.Vm.ConnectionMessage); Assert.Equal(1, lab.Credentials.AuthContext.Generation);
    }
    [Theory][InlineData(false)][InlineData(true)]
    public async Task FailedCredentialMutationDoesNotClaimSuccess(bool save)
    {
        await using var lab = new Lab(); await lab.Vm.CheckCredentialAsync();
        lab.Store.Before = _ => throw new OnlineException(OnlineFailure.CredentialStorage);
        if (save) Assert.False(await lab.Vm.SaveCredentialAsync("retained-synthetic-input")); else await lab.Vm.ClearCredentialAsync();
        // The save result is the UI's permission to clear its PasswordBox; false retains the input.
        Assert.True(lab.Vm.HasCredential); Assert.Equal("synthetic-old", lab.Store.Value);
        Assert.Equal(OnlineState.Error, lab.Vm.ConnectionState); Assert.DoesNotContain("已移除", lab.Vm.CredentialMessage);
        Assert.DoesNotContain("retained-synthetic-input", lab.Vm.CredentialMessage);
    }
    [Theory][InlineData(false)][InlineData(true)]
    public async Task CredentialWritesKeepInvocationOrderAndLatestIntent(bool clearFirst)
    {
        await using var lab = new Lab(); var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int entered = 0; lab.Store.Before = _ => ++entered == 1 ? release.Task : Task.CompletedTask;
        Task first = clearFirst ? lab.Vm.ClearCredentialAsync() : lab.Vm.SaveCredentialAsync("synthetic-A");
        var second = lab.Vm.SaveCredentialAsync("synthetic-B");
        var request = lab.Catalog.DetailsAsync("86", false, default);
        Assert.Equal(0, lab.Calls); Assert.Equal(1, entered); Assert.False(second.IsCompleted);
        release.SetResult(); await first; Assert.True(await second); await request;
        Assert.Equal("synthetic-B", lab.Store.Value); Assert.True(lab.Vm.HasCredential);
        Assert.Equal(2, lab.Credentials.AuthContext.Generation); Assert.Equal(1, lab.Calls);
    }
    [Fact] public async Task AuthEpochInvalidatesCachedDetailsWithoutCallerClearCache()
    {
        await using var lab = new Lab(); await lab.Catalog.DetailsAsync("86", false, default);
        await lab.Credentials.SaveAsync("synthetic-new", default);
        await lab.Catalog.DetailsAsync("86", false, default); Assert.Equal(2, lab.Calls);
        await lab.Credentials.ClearAsync(default);
        Assert.Equal(OnlineFailure.NoCredential, (await Assert.ThrowsAsync<OnlineException>(() => lab.Catalog.DetailsAsync("86", false, default))).Failure);
        Assert.Equal(2, lab.Calls);
    }
    [Theory][InlineData("details")][InlineData("search")][InlineData("connection")]
    public async Task AuthChangeRejectsOldProductionResult(string operation)
    {
        await using var lab = new Lab(); var waiting = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        lab.Send = (_, _) => waiting.Task;
        Task old = operation switch { "details" => lab.Catalog.DetailsAsync("86", false, default), "search" => lab.Catalog.SearchAsync("q", null, default), _ => lab.Catalog.TestConnectionAsync(default) };
        await lab.Credentials.SaveAsync("synthetic-new", default);
        waiting.SetResult(operation == "details" ? Work() : Json("{\"object\":\"list\",\"items\":[]}"));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => old);
        lab.Send = (_, _) => Task.FromResult(Work()); await lab.Catalog.DetailsAsync("86", false, default); Assert.Equal(2, lab.Calls);
    }
    [Theory][InlineData("", "90", false)][InlineData("90", "90", true)][InlineData("90,91", "91", true)]
    [InlineData("90", "86", false)][InlineData("90", "99", false)][InlineData("90,91", "90", false)]
    public async Task OnlyValidatedMergeChainCanChangeIdentity(string chain, string final, bool valid)
    {
        await using var lab = new Lab(); var targets = chain.Split(',', StringSplitOptions.RemoveEmptyEntries); int step = 0;
        lab.Send = (_, _) => Task.FromResult(step++ < targets.Length ? Merge(targets[step - 1]) : Work(final));
        if (valid) { Assert.Equal(final, (await lab.Catalog.DetailsAsync("86", false, default)).Id); await lab.Catalog.DetailsAsync("86", false, default); Assert.Equal(targets.Length + 1, lab.Calls); }
        else
        {
            for (int attempt = 0; attempt < 2; attempt++) { step = 0; Assert.Equal(OnlineFailure.InvalidResponse, (await Assert.ThrowsAsync<OnlineException>(() => lab.Catalog.DetailsAsync("86", false, default))).Failure); }
            Assert.Equal(2 * (targets.Length + 1), lab.Calls);
        }
    }
    [Fact] public async Task WrongDetailIdentityCannotMutateWishlistOrRefresh()
    {
        await using var lab = new Lab(); await lab.Workspace.InitializeAsync(); await lab.Library.AddAsync("86", default);
        var entry = Assert.Single((await lab.Offline.QueryAsync(new(), default)).Items); var before = File.ReadAllBytes(lab.Profile.Main);
        lab.Send = (_, _) => Task.FromResult(Work("90"));
        Assert.Equal(OnlineFailure.InvalidResponse, (await Assert.ThrowsAsync<OnlineException>(() => lab.Library.RefreshAsync(entry.Id, default))).Failure);
        lab.Catalog.ClearCache(); await Assert.ThrowsAsync<OnlineException>(() => lab.Library.AddAsync("86", default));
        Assert.Equal(before, File.ReadAllBytes(lab.Profile.Main)); Assert.Single((await lab.Offline.QueryAsync(new(), default)).Items);
    }
    [Fact] public async Task ConfirmedCatalogMergeKeepsLocalEditionNameAndSupplementedSource()
    {
        await using var lab = new Lab();
        await lab.Workspace.InitializeAsync();
        lab.Send = (_, _) => Task.FromResult(Json("""
            {"object":"work","id":"86","display_name":"Local Edition","refs":[{"source":"vndb","external_id":"r102263"}]}
            """));
        Assert.True((await lab.Library.AddAsync("86", default)).IsCommitted);
        var library = lab.Offline.Required();
        var old = Assert.Single(library.Snapshot.Entries);
        var extras = await library.ExtrasAsync(old.Id);
        var vndb = old.Metadata! with { Id = "vndb:v20424", References = [new("vndb", "v20424", "https://vndb.org/v20424")], Description = "补充资料" };
        await library.ApplySourceAsync(extras.Token, new("vndb", "v20424", vndb, ["description"], false));
        await library.UpdateAsync(old.Id, new() { Notes = "保留笔记", Favorite = true });
        lab.Send = (request, _) => Task.FromResult(request.RequestUri!.AbsolutePath.EndsWith("/86", StringComparison.Ordinal)
            ? Merge("90")
            : Json("""{"object":"work","id":"90","display_name":"Canonical Work","refs":[{"source":"vndb","external_id":"r102263"},{"source":"vndb","external_id":"v20424"}]}"""));

        var declined = await lab.Library.RefreshAsync(old.Id, default, (entry, target, _) =>
        {
            Assert.Equal(old.Id, entry.Id);
            Assert.Equal("90", target.Id);
            return Task.FromResult(false);
        });
        Assert.Equal(ClientOutcome.Cancelled, declined.Outcome);
        Assert.Equal("86", Assert.Single(library.Snapshot.Entries).Metadata?.Id);

        var accepted = await lab.Library.RefreshAsync(old.Id, default, (_, _, _) => Task.FromResult(true));
        Assert.True(accepted.IsCommitted, accepted.Message);
        var current = Assert.Single(library.Snapshot.Entries);
        Assert.Equal(old.Id, current.Id);
        Assert.Equal("Local Edition", current.Name);
        Assert.True(current.Favorite);
        Assert.Equal("保留笔记", current.Notes);
        Assert.Equal("90", current.Metadata?.Id);
        Assert.Equal("补充资料", current.Metadata?.Description);
        Assert.Equal("v20424", CollectionUpdate.WorkIdFor(current, CollectionSite.Vndb));
    }

    [Fact] public async Task ConfirmingCatalogMergeDoesNotCombineSeparateShelfEntries()
    {
        await using var lab = new Lab();
        await lab.Workspace.InitializeAsync();
        lab.Send = (request, _) => Task.FromResult(Work(request.RequestUri!.AbsolutePath.Split('/')[^1]));
        await lab.Library.AddAsync("86", default);
        await lab.Library.AddAsync("90", default);
        var library = lab.Offline.Required();
        var old = library.Snapshot.Entries.Single(entry => entry.Metadata?.Id == "86");
        lab.Send = (request, _) => Task.FromResult(request.RequestUri!.AbsolutePath.EndsWith("/86", StringComparison.Ordinal) ? Merge("90") : Work("90"));

        var result = await lab.Library.RefreshAsync(old.Id, default, (_, _, _) => throw new InvalidOperationException());
        Assert.Equal(ClientOutcome.Conflict, result.Outcome);
        Assert.Equal(2, library.Snapshot.Entries.Length);
        Assert.Contains(library.Snapshot.Entries, entry => entry.Id == old.Id && entry.Metadata?.Id == "86");
    }

    [Theory]
    [InlineData(CollectionSite.Vndb, "https://vndb.org/v202", "v202", 84)]
    [InlineData(CollectionSite.Bangumi, "202", "202", 8)]
    [InlineData(CollectionSite.NextMoe, "#86", "90", 8)]
    public async Task ConfirmingAnExactSyncBindingPreservesCatalogueAndPersonalFields(
        CollectionSite site, string input, string id, int rating)
    {
        await using var lab = new Lab();
        await lab.Workspace.InitializeAsync();
        Assert.True((await lab.Library.AddAsync("86", default)).IsCommitted);
        var library = lab.Offline.Required();
        var old = Assert.Single(library.Snapshot.Entries);
        await library.UpdateAsync(old.Id, new() { Notes = "本地笔记", Rating = 8.4, Status = "completed" });
        lab.Send = (request, _) => Task.FromResult(request.RequestUri!.AbsolutePath.EndsWith("/86", StringComparison.Ordinal)
            ? Merge("90") : Work("90"));
        using var direct = new HttpClient(new Handler((request, _) => Task.FromResult(
            request.RequestUri!.Host == "api.vndb.org"
                ? Json("""{"results":[{"id":"v202","title":"Synthetic"}]}""")
                : Json("""{"id":202,"type":4,"name":"Synthetic"}"""))));
        var binder = new CollectionBindingClient(lab.Offline, lab.Catalog, new(lab.Offline, new DirectMetadataSources(direct)));

        var preview = await binder.ReadAsync(old.Id, site, input, allowAdult: false, default);
        Assert.Equal(id, preview.WorkId);
        Assert.Equal("Synthetic", preview.Title);
        Assert.Null(Assert.Single(library.Snapshot.Entries).CollectionBindings);
        Assert.Equal(new CollectionUpdate(site, id, null, rating), preview.UpdateFor(CollectionFields.Rating));

        var result = await binder.BindAsync(preview, default);
        Assert.True(result.IsCommitted, result.Message);
        var loaded = Assert.Single(new NativeLibraryCodec().Decode(await lab.Offline.ExportBackupAsync(default)));
        Assert.Equal(id, CollectionUpdate.WorkIdFor(loaded, site));
        Assert.Equal("86", loaded.Metadata?.Id);
        Assert.Equal("Synthetic", loaded.Name);
        Assert.Equal("本地笔记", loaded.Notes);
        Assert.Equal("completed", loaded.Status);
        Assert.Equal(8.4, loaded.PersonalRating);
    }

    [Fact]
    public async Task SyncBindingRejectsDuplicateIdsMismatchedResponsesAndStalePreviews()
    {
        await using var lab = new Lab();
        await lab.Workspace.InitializeAsync();
        lab.Send = (request, _) => Task.FromResult(Work(request.RequestUri!.AbsolutePath.Split('/')[^1]));
        await lab.Library.AddAsync("86", default);
        await lab.Library.AddAsync("90", default);
        var library = lab.Offline.Required();
        var entry = library.Snapshot.Entries.Single(item => item.Metadata?.Id == "86");
        using var direct = new HttpClient(new Handler((_, _) =>
            Task.FromResult(Json("""{"results":[{"id":"v203","title":"Wrong number"}]}"""))));
        var binder = new CollectionBindingClient(lab.Offline, lab.Catalog, new(lab.Offline, new DirectMetadataSources(direct)));

        await Assert.ThrowsAsync<InvalidDataException>(() => binder.ReadAsync(entry.Id, CollectionSite.Vndb, "v202", false, default));
        await Assert.ThrowsAsync<InvalidDataException>(() => binder.ReadAsync(entry.Id, CollectionSite.Vndb, "r202", false, default));
        var duplicate = await binder.ReadAsync(entry.Id, CollectionSite.NextMoe, "90", false, default);
        Assert.Equal(ClientOutcome.ValidationFailure, (await binder.BindAsync(duplicate, default)).Outcome);
        Assert.Null(library.Snapshot.Entries.Single(item => item.Id == entry.Id).CollectionBindings);

        var stale = await binder.ReadAsync(entry.Id, CollectionSite.NextMoe, "91", false, default);
        var newer = await binder.ReadAsync(entry.Id, CollectionSite.NextMoe, "92", false, default);
        Assert.True((await binder.BindAsync(newer, default)).IsCommitted);
        Assert.Equal(ClientOutcome.Conflict, (await binder.BindAsync(stale, default)).Outcome);
        Assert.Equal("92", CollectionUpdate.WorkIdFor(library.Snapshot.Entries.Single(item => item.Id == entry.Id), CollectionSite.NextMoe));
    }

    [Fact] public async Task VerifiedBackgroundMergePreservesMetadataLayersAndExistingBindings()
    {
        await using var lab = new Lab();
        await lab.Workspace.InitializeAsync();
        Assert.True((await lab.Library.AddAsync("86", default)).IsCommitted);
        var library = lab.Offline.Required();
        var origin = Assert.Single(library.Snapshot.Entries);
        await library.UpdateAsync(origin.Id, new() { Notes = "本地记录", Status = "completed", Rating = 8.5 });
        using var direct = new HttpClient(new Handler((_, _) => Task.FromResult(
            Json("""{"results":[{"id":"v20424","title":"Synthetic"}]}"""))));
        var bindings = new CollectionBindingClient(lab.Offline, lab.Catalog, new(lab.Offline, new DirectMetadataSources(direct)));
        var vndb = await bindings.ReadAsync(origin.Id, CollectionSite.Vndb, "v20424", false, default);
        Assert.True((await bindings.BindAsync(vndb, default)).IsCommitted);
        var layerOrigin = await library.ExtrasAsync(origin.Id);
        var layer = origin.Metadata! with { Id = "vndb:v20424", Description = "已确认的补充资料",
            References = [new("vndb", "v20424", "https://vndb.org/v20424")] };
        await library.ApplySourceAsync(layerOrigin.Token, new("vndb", "v20424", layer, ["description"], false));
        var oldBinding = await bindings.ReadAsync(origin.Id, CollectionSite.NextMoe, "86", false, default);
        Assert.True((await bindings.BindAsync(oldBinding, default)).IsCommitted);

        lab.Send = (request, _) => Task.FromResult(request.RequestUri!.AbsolutePath.EndsWith("/86", StringComparison.Ordinal) ? Merge("90") : Work("90"));
        var details = await library.ExtrasAsync(origin.Id);
        var work = await lab.Catalog.DetailsAsync("86", true, default);
        var receipt = await library.RepairCatalogAssociationAsync(details, "86", work, false, default);
        Assert.Equal("90", receipt.Value.Metadata!.Id);
        var committed = Assert.Single(new NativeLibraryCodec().Decode(await lab.Offline.ExportBackupAsync(default)));
        Assert.Equal(origin.Id, committed.Id);
        Assert.Equal("90", committed.CollectionBindings!.NextMoe);
        Assert.Equal("v20424", committed.CollectionBindings.Vndb);
        Assert.Equal("已确认的补充资料", committed.Metadata?.Description);
        Assert.Equal("本地记录", committed.Notes);
        Assert.Equal("completed", committed.Status);
        Assert.Equal(8.5, committed.PersonalRating);
    }

    private sealed class CancelAtEof(byte[] bytes, Action cancel) : MemoryStream(bytes)
    {
        public bool WasDisposed;
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken c = default)
        { int count = await base.ReadAsync(buffer, c); if (count == 0) cancel(); return count; }
        protected override void Dispose(bool disposing) { WasDisposed = true; base.Dispose(disposing); }
    }
    [Fact] public async Task EofCancellationRejectsResultAndDoesNotFillCache()
    {
        await using var lab = new Lab(); using var cancel = new CancellationTokenSource();
        var stream = new CancelAtEof(Encoding.UTF8.GetBytes("{\"object\":\"work\",\"id\":\"86\"}"), cancel.Cancel);
        lab.Send = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(stream) });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => lab.Catalog.DetailsAsync("86", false, cancel.Token)); Assert.True(stream.WasDisposed);
        lab.Send = (_, _) => Task.FromResult(Work()); await lab.Catalog.DetailsAsync("86", false, default); Assert.Equal(2, lab.Calls);
    }
    [Theory][InlineData("BodyComplete")][InlineData("Parsed")][InlineData("Mapped")][InlineData("BeforeCache")][InlineData("AfterCache")][InlineData("BeforeReturn")]
    public async Task CancelledAcceptanceStageNeverLeavesReusableCache(string stage)
    {
        await using var lab = new Lab(); using var cancel = new CancellationTokenSource();
        lab.Transport.AcceptanceProbe = current => { if (current.ToString() == stage) cancel.Cancel(); };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => lab.Catalog.DetailsAsync("86", false, cancel.Token));
        lab.Transport.AcceptanceProbe = null; await lab.Catalog.DetailsAsync("86", false, default); Assert.Equal(2, lab.Calls);
    }
    [Theory][InlineData("BodyComplete")][InlineData("Mapped")][InlineData("AfterCache")][InlineData("BeforeReturn")]
    public async Task OverallDeadlineIncludesMappingAndFinalReturn(string stage)
    {
        await using var lab = new Lab(); var elapsed = TimeSpan.FromSeconds(44.9);
        lab.Transport.OperationElapsed = () => elapsed;
        lab.Transport.AcceptanceProbe = current => { if (current.ToString() == stage) elapsed = TimeSpan.FromSeconds(45.1); };
        Assert.Equal(OnlineFailure.Timeout, (await Assert.ThrowsAsync<OnlineException>(() => lab.Catalog.DetailsAsync("86", false, default))).Failure);
        lab.Transport.AcceptanceProbe = null; elapsed = TimeSpan.Zero;
        await lab.Catalog.DetailsAsync("86", false, default); Assert.Equal(2, lab.Calls);
    }
    [Fact] public async Task RetryBoundaryCancellationDoesNotIssueAnotherRequest()
    {
        int calls = 0; using var cancel = new CancellationTokenSource();
        using var http = new HttpClient(new Handler((_, _) => { calls++; return Task.FromResult(Json("{}", 503)); }));
        var transport = new CatalogTransport(http, new Credential(), new("https://api.nextmoe.dev/v2/"), delay: (_, _) => { cancel.Cancel(); return Task.CompletedTask; });
        var catalog = new NextMoeCatalog(transport);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => catalog.DetailsAsync("86", false, cancel.Token)); Assert.Equal(1, calls);
    }
    [Fact] public async Task CancellationAfterMappingDoesNotPublishDetails()
    {
        await using var lab = new Lab(); lab.Transport.AcceptanceProbe = stage => { if (stage == OnlineAcceptanceStage.Mapped) lab.Vm.Leave(); };
        await lab.Vm.OpenDetailsAsync("86"); Assert.Null(lab.Vm.Details); Assert.Equal(OnlineState.Idle, lab.Vm.DetailState);
        lab.Transport.AcceptanceProbe = null; await lab.Vm.OpenDetailsAsync("86"); Assert.Equal(2, lab.Calls); Assert.Equal(OnlineState.Loaded, lab.Vm.DetailState);
    }
    [Fact] public async Task CancelledPaginationNeverAppendsResults()
    {
        await using var lab = new Lab();
        lab.Send = (r, _) => Task.FromResult(r.RequestUri!.AbsolutePath.EndsWith("search", StringComparison.Ordinal)
            ? Json("{\"object\":\"list\",\"items\":[{\"object\":\"search_result\",\"target_object\":\"work\",\"id\":\"86\"}],\"next_cursor\":\"cur_next\"}")
            : Json("{\"object\":\"list\",\"items\":[{\"object\":\"work\",\"id\":\"86\"}]}"));
        lab.Vm.Query = "q"; await lab.Vm.SearchAsync(); Assert.Single(lab.Vm.Items);
        lab.Transport.AcceptanceProbe = stage => { if (stage == OnlineAcceptanceStage.Mapped) lab.Vm.CancelSearch(); };
        await lab.Vm.SearchAsync(true); Assert.Single(lab.Vm.Items); Assert.False(lab.Vm.SearchBusy); Assert.DoesNotContain("失败", lab.Vm.SearchMessage);
    }
    [Theory][InlineData("missing")][InlineData("empty")][InlineData("oversized")][InlineData("random")][InlineData("tampered")][InlineData("invalid-plaintext")][InlineData("valid")]
    public async Task CredentialStatusAndReadAgreeAndNeverOverwriteCorruption(string kind)
    {
        using var profile = new TransactionProfile(); var path = Path.Combine(profile.Root, "native-online-key.dpapi");
        var store = new IsolatedCredentialStore(new(profile.Root));
        if (kind is "valid" or "tampered") { await store.SaveAsync("synthetic-test", default); if (kind == "tampered") { var b = File.ReadAllBytes(path); b[^1] ^= 0xff; File.WriteAllBytes(path, b); } }
        else if (kind == "invalid-plaintext") File.WriteAllBytes(path, ProtectedData.Protect([0xff], null, DataProtectionScope.CurrentUser));
        else if (kind != "missing") File.WriteAllBytes(path, kind == "empty" ? [] : kind == "oversized" ? new byte[16385] : [1, 2, 3, 4]);
        if (kind == "missing") { Assert.False(await store.HasCredentialAsync(default)); Assert.Null(await store.ReadAsync(default)); Assert.False(File.Exists(path)); }
        else if (kind == "valid") { Assert.True(await store.HasCredentialAsync(default)); Assert.Equal("synthetic-test", await store.ReadAsync(default)); }
        else
        {
            var before = File.ReadAllBytes(path);
            Assert.Equal(OnlineFailure.CredentialStorageCorrupt, (await Assert.ThrowsAsync<OnlineException>(() => store.HasCredentialAsync(default))).Failure);
            Assert.Equal(OnlineFailure.CredentialStorageCorrupt, (await Assert.ThrowsAsync<OnlineException>(() => store.ReadAsync(default))).Failure);
            Assert.Equal(before, File.ReadAllBytes(path));
        }
    }
    [Theory][InlineData("public", 1)][InlineData("private", 0)][InlineData("mixed", 0)][InlineData("mapped", 0)][InlineData("rebinding", 1)]
    public async Task ProductionHttpHandlerUsesOnlyOnceResolvedValidatedAddresses(string kind, int expectedConnections)
    {
        int resolutions = 0, connections = 0;
        var first = kind switch { "private" => new[] { IPAddress.Parse("10.1.2.3") }, "mixed" => [IPAddress.Parse("8.8.8.8"), IPAddress.Parse("10.1.2.3")], "mapped" => [IPAddress.Parse("::ffff:127.0.0.1")], _ => [IPAddress.Parse("8.8.8.8"), IPAddress.Parse("1.1.1.1")] };
        using var handler = RemoteCoverSource.CreateHandler((host, _) => { Assert.Equal("images.example", host); resolutions++; return Task.FromResult(resolutions == 1 ? first : [IPAddress.Parse("127.0.0.1")]); },
            (addresses, port, _) => { connections++; Assert.Same(first, addresses); Assert.Equal(443, port); throw new HttpRequestException("Synthetic connector stops before any socket or TLS."); });
        Assert.False(handler.AllowAutoRedirect); Assert.False(handler.UseProxy); Assert.False(handler.UseCookies);
        using var http = new HttpClient(handler);
        await Assert.ThrowsAsync<HttpRequestException>(() => http.GetAsync("https://images.example/a"));
        Assert.Equal(1, resolutions); Assert.Equal(expectedConnections, connections);
    }
    [Fact] public async Task ImageRedirectToPrivateIsNotFollowedOrAuthenticated()
    {
        int calls = 0; using var http = new HttpClient(new Handler((request, _) =>
        {
            calls++; Assert.Equal("images.example", request.RequestUri!.Host); Assert.Null(request.Headers.Authorization); Assert.False(request.Headers.Contains("Cookie"));
            var result = new HttpResponseMessage(HttpStatusCode.Redirect); result.Headers.Location = new("https://127.0.0.1/private"); return Task.FromResult(result);
        }));
        var source = new RemoteCoverSource(http); Assert.Null(await source.ReadAsync(source.Register("https://images.example/a", "r")!, default)); Assert.Equal(1, calls);
    }
    [Theory][InlineData(false)][InlineData(true)]
    public async Task OldSearchSuccessOrUnauthorizedCannotReplaceNewAuthenticatedSearch(bool oldUnauthorized)
    {
        await using var lab = new Lab(); var oldResponse = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        lab.Send = (_, _) => oldResponse.Task;
        lab.Vm.Query = "old"; var old = lab.Vm.SearchAsync();
        Assert.True(await lab.Vm.SaveCredentialAsync("synthetic-new"));
        lab.Send = (r, _) => Task.FromResult(r.RequestUri!.AbsolutePath.EndsWith("search", StringComparison.Ordinal)
            ? Json("{\"object\":\"list\",\"items\":[{\"object\":\"search_result\",\"target_object\":\"work\",\"id\":\"90\"}]}")
            : Json("{\"object\":\"list\",\"items\":[{\"object\":\"work\",\"id\":\"90\"}]}"));
        lab.Vm.Query = "new"; await lab.Vm.SearchAsync();
        oldResponse.SetResult(oldUnauthorized ? Json("{}", 401) : Json("{\"object\":\"list\",\"items\":[]}")); await old;
        Assert.Equal(OnlineState.Loaded, lab.Vm.SearchState); Assert.Equal("90", Assert.Single(lab.Vm.Items).Id);
    }
    [Fact] public async Task CorruptCredentialStatusHasIndependentPresentationState()
    {
        await using var lab = new Lab(); var path = Path.Combine(lab.Profile.Root, "native-online-key.dpapi"); File.WriteAllBytes(path, []);
        var store = new IsolatedCredentialStore(new(lab.Profile.Root)); var credentials = new AuthenticatedCredentials(store, store);
        using var images = new HttpClient();
        await using var vm = new OnlineViewModel(lab.Catalog, credentials, new RemoteCoverSource(images), lab.Library, lab.Workspace);
        await vm.CheckCredentialAsync(); Assert.Equal(OnlineState.CredentialCorrupt, vm.ConnectionState); Assert.Contains("损坏", vm.CredentialMessage);
        Assert.DoesNotContain("尚未保存", vm.CredentialMessage); Assert.Equal(0, new FileInfo(path).Length);
    }
    [Fact] public async Task FailedQueuedIntentDoesNotDropNewerSave()
    {
        await using var lab = new Lab(); var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lab.Store.Before = async value => { if (value == "synthetic-A") { await release.Task; throw new OnlineException(OnlineFailure.CredentialStorage); } };
        var first = lab.Vm.SaveCredentialAsync("synthetic-A"); var second = lab.Vm.SaveCredentialAsync("synthetic-B"); release.SetResult();
        Assert.False(await first); Assert.True(await second); Assert.Equal("synthetic-B", lab.Store.Value); Assert.True(lab.Vm.HasCredential);
    }
    [Theory][InlineData("Mapped")][InlineData("AfterCache")][InlineData("BeforeReturn")]
    public async Task AuthChangeAtAcceptanceRejectsAndRollsBackOldCache(string stage)
    {
        await using var lab = new Lab(); Task? mutation = null;
        lab.Transport.AcceptanceProbe = current => { if (current.ToString() == stage) mutation = lab.Credentials.SaveAsync("synthetic-new", default); };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => lab.Catalog.DetailsAsync("86", false, default));
        Assert.NotNull(mutation); await mutation; lab.Transport.AcceptanceProbe = null;
        await lab.Catalog.DetailsAsync("86", false, default); Assert.Equal(2, lab.Calls);
    }
    [Fact] public async Task CacheReadersCannotObserveProvisionalAdmission()
    {
        await using var lab = new Lab(); using var cancel = new CancellationTokenSource();
        using var readBudget = new RequestBudget(TimeSpan.FromSeconds(10), default);
        readBudget.Attach(await lab.Credentials.AuthContext.CaptureAsync(default));
        using var attempted = new ManualResetEventSlim(); using var read = new ManualResetEventSlim(); Task? reader = null;
        lab.Transport.AcceptanceProbe = stage =>
        {
            if (stage != OnlineAcceptanceStage.AfterCache) return;
            reader = Task.Run(() => { attempted.Set(); lab.Transport.ReadCurrent(readBudget, () => { read.Set(); return true; }); });
            Assert.True(attempted.Wait(TimeSpan.FromSeconds(2)));
            Assert.False(read.Wait(TimeSpan.FromMilliseconds(50))); // Reader is held until rollback leaves the acceptance lock.
            cancel.Cancel();
        };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => lab.Catalog.DetailsAsync("86", false, cancel.Token));
        Assert.NotNull(reader); await reader; Assert.True(read.IsSet);
        lab.Transport.AcceptanceProbe = null; await lab.Catalog.DetailsAsync("86", false, default); Assert.Equal(2, lab.Calls);
    }
}
