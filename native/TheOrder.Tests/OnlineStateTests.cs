using System.Collections.Immutable;
using System.Net;
using TheOrder.Application;
using TheOrder.Domain;
using TheOrder.Infrastructure.Legacy;
using TheOrder.Infrastructure.Online;
using TheOrder.Platform.Windows;
using TheOrder.Presentation;
using Xunit;

namespace TheOrder.Tests;

public sealed class OnlineStateTests
{
    private static WorkMetadata Work(string id = "86", string title = "Synthetic") => new(id, title, "", [], [], null, null, null, "Description", null, null, [], [], [], [], [], null, null, TransactionDifferentialTests.Clock);
    private sealed class Catalog : IOnlineCatalog
    {
        public Func<string, string?, CancellationToken, Task<OnlinePage>> Search = (_, _, _) => Task.FromResult(new OnlinePage([], null));
        public Func<string, bool, CancellationToken, Task<WorkMetadata>> Detail = (id, _, _) => Task.FromResult(Work(id));
        public Task<OnlinePage> SearchAsync(string q, string? c, CancellationToken token) => Search(q, c, token);
        public Task<WorkMetadata> DetailsAsync(string id, bool refresh, CancellationToken c) => Detail(id, refresh, c);
        public Task TestConnectionAsync(CancellationToken c) => Task.CompletedTask;
        public void ClearCache() { }
    }
    private sealed class Lab : IAsyncDisposable
    {
        public readonly TransactionProfile Profile = new();
        public readonly Catalog Catalog = new();
        public readonly OfflineLibraryClient Offline;
        public readonly WorkspaceViewModel Workspace;
        public readonly OnlineViewModel Online;
        public readonly OnlineLibraryClient Library;
        private readonly HttpClient http = new(new OnlineTransportTests.Handler((_, _) => Task.FromResult(OnlineTransportTests.Json("{}"))));
        public Lab()
        {
            var config = new NativeConfiguration(Profile.Root); var rules = new LegacyClientTextRules();
            Offline = new(new IsolatedLibraryFactory(new NativeLibraryCodec(), config), rules);
            Workspace = new(Offline, new IsolatedUiPreferencesStore(config), config, rules);
            Library = new(Offline, Catalog); Online = new(Catalog, new OnlineTransportTests.Credential(), new RemoteCoverSource(http), Library, Workspace);
        }
        public async ValueTask DisposeAsync() { await Online.DisposeAsync(); await Workspace.DisposeAsync(); await Offline.DisposeAsync(); http.Dispose(); Profile.Dispose(); }
    }
    [Theory]
    [InlineData("#86")]
    [InlineData("https://www.kungal.com/galgame/86")]
    [InlineData("https://api.nextmoe.dev/v2/catalog/works/86")]
    public async Task ExplicitReferenceLoadsExactWorkWithoutTitleSearchOrLibraryMutation(string query)
    {
        await using var lab = new Lab(); await lab.Workspace.InitializeAsync();
        Assert.True((await lab.Library.AddAsync("77", default)).IsCommitted);
        var before = File.ReadAllBytes(lab.Profile.Main);
        int details = 0;
        lab.Catalog.Search = (_, _, _) => throw new InvalidOperationException("Must not search references as titles");
        lab.Catalog.Detail = (id, _, _) => { details++; Assert.Equal("86", id); return Task.FromResult(Work(id) with { Developers = ["Studio"] }); };
        lab.Online.Query = query; await lab.Online.SearchAsync(); await lab.Online.SearchAsync(true);
        var card = Assert.Single(lab.Online.Items); Assert.Equal("86", card.Id); Assert.Equal("#86", card.CatalogNumber);
        Assert.Equal("Studio", card.Subtitle); Assert.Equal(1, details); Assert.False(lab.Online.CanLoadMore);
        Assert.Equal(before, File.ReadAllBytes(lab.Profile.Main));
    }
    [Theory][InlineData("1984")][InlineData("#Synthetic")]
    public async Task NumericOrHashPrefixedGameTitleIsNotMisinterpretedAsAnId(string query)
    {
        await using var lab = new Lab();
        lab.Catalog.Detail = (_, _, _) => throw new InvalidOperationException("Numeric titles remain searchable");
        lab.Catalog.Search = (q, _, _) => { Assert.Equal(query, q); return Task.FromResult(new OnlinePage([Work("99", query)], null)); };
        lab.Online.Query = query; await lab.Online.SearchAsync(); Assert.Equal("99", Assert.Single(lab.Online.Items).Id);
    }
    [Theory]
    [InlineData("#0")]
    [InlineData("https://evil.test/galgame/86")]
    [InlineData("https://www.kungal.com/galgame/86?secret=example")]
    [InlineData("http://www.kungal.com/galgame/86")]
    public async Task InvalidReferenceDoesNotSendNetworkRequest(string query)
    {
        await using var lab = new Lab(); int requests = 0;
        lab.Catalog.Search = (_, _, _) => { requests++; return Task.FromResult(new OnlinePage([], null)); };
        lab.Catalog.Detail = (_, _, _) => { requests++; return Task.FromResult(Work()); };
        lab.Online.Query = query; await lab.Online.SearchAsync();
        Assert.Equal(0, requests); Assert.Empty(lab.Online.Items); Assert.Equal(OnlineState.Error, lab.Online.SearchState);
    }
    [Theory][InlineData(OnlineFailure.NotFound)][InlineData(OnlineFailure.Offline)]
    public async Task DirectLookupFailureIsNotPresentedAsAnEmptySearch(OnlineFailure failure)
    {
        await using var lab = new Lab(); lab.Catalog.Detail = (_, _, _) => throw new OnlineException(failure);
        lab.Online.Query = "#86"; await lab.Online.SearchAsync();
        Assert.Empty(lab.Online.Items); Assert.NotEqual(OnlineState.Empty, lab.Online.SearchState);
        Assert.Equal(new OnlineException(failure).Message, lab.Online.SearchMessage);
    }
    [Fact] public async Task LateDirectLookupCannotReplaceAChangedQuery()
    {
        await using var lab = new Lab(); var pending = new TaskCompletionSource<WorkMetadata>();
        lab.Catalog.Detail = (_, _, _) => pending.Task;
        lab.Catalog.Search = (_, _, _) => Task.FromResult(new OnlinePage([Work("2")], null));
        lab.Online.Query = "#86"; var old = lab.Online.SearchAsync();
        lab.Online.Query = "new title"; await lab.Online.SearchAsync();
        pending.SetResult(Work()); await old;
        Assert.Equal("2", Assert.Single(lab.Online.Items).Id); Assert.Equal(OnlineState.Loaded, lab.Online.SearchState);
    }
    [Fact] public async Task RapidQueryLateCompletionCannotReplaceNewQuery()
    {
        await using var lab = new Lab(); var old = new TaskCompletionSource<OnlinePage>();
        lab.Catalog.Search = (q, _, _) => q == "A" ? old.Task : Task.FromResult(new OnlinePage([Work("2")], null));
        lab.Online.Query = "A"; var first = lab.Online.SearchAsync(); lab.Online.Query = "AB"; await lab.Online.SearchAsync();
        old.SetResult(new([Work("1")], "cur_old")); await first;
        Assert.Equal("2", Assert.Single(lab.Online.Items).Id); Assert.Equal(OnlineState.Loaded, lab.Online.SearchState); Assert.False(lab.Online.CanLoadMore);
    }
    [Fact] public async Task EditingLoadedQueryReturnsToIdleBeforeNextRequest()
    {
        await using var lab = new Lab();
        lab.Catalog.Search = (_, _, _) => Task.FromResult(new OnlinePage([Work()], "cur_next"));
        lab.Online.Query = "old"; await lab.Online.SearchAsync(); Assert.Equal(OnlineState.Loaded, lab.Online.SearchState);
        lab.Online.Query = "new";
        Assert.Empty(lab.Online.Items); Assert.Equal(OnlineState.Idle, lab.Online.SearchState);
        Assert.False(lab.Online.SearchBusy); Assert.False(lab.Online.CanLoadMore);
    }
    [Fact] public async Task CredentialRemovalClearsPriorConnectionAndRemotePresentation()
    {
        await using var lab = new Lab();
        lab.Catalog.Search = (_, _, _) => Task.FromResult(new OnlinePage([Work()], "cur_next"));
        lab.Catalog.Detail = (_, _, _) => Task.FromResult(Work() with { CoverUrl = "https://images.example/cover.png" });
        lab.Online.Query = "old"; await lab.Online.SearchAsync(); await lab.Online.OpenDetailsAsync("86");
        await lab.Online.TestConnectionAsync(); Assert.Equal(OnlineState.Loaded, lab.Online.ConnectionState); Assert.NotNull(lab.Online.DetailCoverKey);
        await lab.Online.ClearCredentialAsync();
        Assert.False(lab.Online.HasCredential); Assert.Equal(OnlineState.Idle, lab.Online.ConnectionState);
        Assert.DoesNotContain("连接成功", lab.Online.ConnectionMessage); Assert.Null(lab.Online.Details); Assert.Null(lab.Online.DetailCoverKey);
        Assert.Equal(OnlineState.Idle, lab.Online.DetailState); Assert.Equal(OnlineState.Idle, lab.Online.SearchState);
        Assert.Empty(lab.Online.Items); Assert.False(lab.Online.CanLoadMore);
    }
    [Fact] public async Task OldPageTwoCannotReplaceNewPageOneAndOverlapIsSuppressed()
    {
        await using var lab = new Lab(); var old = new TaskCompletionSource<OnlinePage>(); int requests = 0;
        lab.Catalog.Search = (q, cursor, _) => { requests++; return cursor is not null ? old.Task : Task.FromResult(new OnlinePage([Work(q == "A" ? "1" : "2")], "cur_next")); };
        lab.Online.Query = "A"; await lab.Online.SearchAsync(); var more = lab.Online.SearchAsync(true); await lab.Online.SearchAsync(true); Assert.Equal(2, requests);
        lab.Online.Query = "B"; await lab.Online.SearchAsync(); old.SetResult(new([Work("3")], null)); await more;
        Assert.Equal("2", Assert.Single(lab.Online.Items).Id);
    }
    [Fact] public async Task DuplicateItemsAndCursorLoopsAreBounded()
    {
        await using var lab = new Lab(); lab.Catalog.Search = (_, _, _) => Task.FromResult(new OnlinePage([Work(), Work()], "cur_repeat"));
        lab.Online.Query = "A"; await lab.Online.SearchAsync(); await lab.Online.SearchAsync(true);
        Assert.Single(lab.Online.Items); Assert.False(lab.Online.CanLoadMore); Assert.Contains("重复", lab.Online.SearchMessage);
    }
    [Theory][InlineData(OnlineFailure.Offline, OnlineState.Offline)][InlineData(OnlineFailure.Unauthorized, OnlineState.Unauthorized)][InlineData(OnlineFailure.RateLimited, OnlineState.RateLimited)][InlineData(OnlineFailure.Server, OnlineState.Error)]
    public async Task ExplicitStatesRetryAndNeverDeleteLocalData(OnlineFailure failure, OnlineState expected)
    {
        await using var lab = new Lab(); await lab.Workspace.InitializeAsync(); await lab.Library.AddAsync("86", default); var before = File.ReadAllBytes(lab.Profile.Main);
        lab.Catalog.Search = (_, _, _) => throw new OnlineException(failure); lab.Online.Query = "A"; await lab.Online.SearchAsync(); Assert.Equal(expected, lab.Online.SearchState);
        lab.Catalog.Detail = (_, _, _) => throw new OnlineException(failure); var entry = Assert.Single((await lab.Offline.QueryAsync(new(), default)).Items);
        await Assert.ThrowsAsync<OnlineException>(() => lab.Library.RefreshAsync(entry.Id, default)); Assert.Equal(before, File.ReadAllBytes(lab.Profile.Main));
        lab.Catalog.Search = (_, _, _) => Task.FromResult(new OnlinePage([], null)); await lab.Online.SearchAsync(); Assert.Equal(OnlineState.Empty, lab.Online.SearchState);
    }
    [Fact] public async Task DiscoveryKeepsIdleAndCompletedSearchMessagesOnNavigation()
    {
        await using var lab = new Lab();
        lab.Online.Leave();
        Assert.Empty(lab.Online.SearchMessage);

        lab.Catalog.Search = (_, _, _) => Task.FromResult(new OnlinePage([Work()], null));
        lab.Online.Query = "sample";
        Assert.Empty(lab.Online.SearchMessage);
        await lab.Online.SearchAsync();
        var message = lab.Online.SearchMessage;
        lab.Online.Leave();

        Assert.Equal(OnlineState.Loaded, lab.Online.SearchState);
        Assert.Equal(message, lab.Online.SearchMessage);
        Assert.Single(lab.Online.Items);
    }
    [Fact] public async Task CancelLeaveAndCloseCancelNetworkWithoutError()
    {
        await using var lab = new Lab(); bool cancelled = false;
        lab.Catalog.Search = async (_, _, c) => { try { await Task.Delay(Timeout.Infinite, c); } catch (OperationCanceledException) { cancelled = true; throw; } return new([], null); };
        lab.Online.Query = "A"; var task = lab.Online.SearchAsync(); lab.Online.Leave(); await task;
        Assert.True(cancelled); Assert.Equal(OnlineState.Idle, lab.Online.SearchState); Assert.False(lab.Online.SearchBusy);
        task = lab.Online.SearchAsync(); await lab.Online.DisposeAsync(); await task;
    }
    [Fact] public async Task LateDetailsAndSourceCannotOverwriteNewSelection()
    {
        await using var lab = new Lab(); var old = new TaskCompletionSource<WorkMetadata>();
        lab.Catalog.Detail = (id, _, _) => id == "1" ? old.Task : Task.FromResult(Work("2") with { CoverUrl = "https://images.example/b.png" });
        var first = lab.Online.OpenDetailsAsync("1"); await lab.Online.OpenDetailsAsync("2"); var key = lab.Online.DetailCoverKey;
        old.SetResult(Work("1") with { CoverUrl = "https://images.example/a.png" }); await first;
        Assert.Equal("2", lab.Online.Details!.Id); Assert.Equal(key, lab.Online.DetailCoverKey);
    }
    [Fact] public async Task WishlistDuplicateRefreshPersonalStateRestartAndRemoval()
    {
        await using var lab = new Lab(); await lab.Workspace.InitializeAsync(); Assert.True((await lab.Library.AddAsync("86", default)).IsCommitted);
        await lab.Library.AddAsync("86", default); var item = Assert.Single((await lab.Offline.QueryAsync(new(View: "wishlist"), default)).Items); Assert.Equal(0, item.InstallationCount);
        var details = await lab.Offline.DetailsAsync(item.Id, default);
        await lab.Offline.SaveAsync(details, new("Personal title", "completed", "9", true, "Keep notes", "My category", "Personal tag"), default);
        lab.Catalog.Detail = (_, _, _) => Task.FromResult(Work("86", "New online title")); await lab.Library.RefreshAsync(item.Id, default);
        await lab.Offline.LoadAsync(default); var after = await lab.Offline.DetailsAsync(item.Id, default);
        Assert.Equal("Personal title", after.Personal.Name); Assert.Equal("Keep notes", after.Personal.Notes); Assert.Equal("New online title", after.MetadataTitle);
        await lab.Library.RemoveWishlistAsync(item.Id, default); Assert.Empty((await lab.Offline.QueryAsync(new(), default)).Items);
    }
    [Fact] public async Task NewPersonalRenameInvalidatesDelayedRefresh()
    {
        await using var lab = new Lab(); await lab.Workspace.InitializeAsync(); await lab.Library.AddAsync("86", default);
        var entry = Assert.Single((await lab.Offline.QueryAsync(new(), default)).Items); var waiting = new TaskCompletionSource<WorkMetadata>(); var entered = new TaskCompletionSource();
        lab.Catalog.Detail = (_, _, _) => { entered.SetResult(); return waiting.Task; };
        var refresh = lab.Library.RefreshAsync(entry.Id, default); await entered.Task;
        var details = await lab.Offline.DetailsAsync(entry.Id, default); await lab.Offline.SaveAsync(details, new("New user intent", "planned", "", false, "", "", ""), default);
        waiting.SetResult(Work("86", "Old response")); Assert.Equal(ClientOutcome.Conflict, (await refresh).Outcome);
        Assert.Equal("New user intent", (await lab.Offline.DetailsAsync(entry.Id, default)).Personal.Name);
    }
}
