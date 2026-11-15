using System.Text.Json;
using System.Text.Json.Nodes;
using TheOrder.Application;
using TheOrder.Domain;
using TheOrder.Infrastructure.Online;
using TheOrder.Infrastructure.Legacy;
using TheOrder.Platform.Windows;
using TheOrder.Presentation;
using Xunit;
using static TheOrder.Tests.OnlineTransportTests;
using static TheOrder.Tests.RecognitionBoundaryTests;

namespace TheOrder.Tests;

public sealed class CatalogBrowseTests
{
    private static object Row(string id, string? limit, string rating = "all_ages") =>
        new { @object = "work", id, medium = "galgame", claim = new { site = "kungal", site_work_id = id, state = "live" }, display_name = "Synthetic " + id, content_limit = limit, content_rating = rating };
    private static System.Net.Http.HttpResponseMessage Page(params object[] rows) =>
        Json(JsonSerializer.Serialize(new { @object = "list", items = rows, next_cursor = "cur_more" }));
    private static NextMoeCatalog Catalog(HttpClient http) => new(new(http, new Credential(), new("https://api.nextmoe.dev/v2/")));

    [Theory][InlineData(false)][InlineData(true)]
    public async Task SearchUsesSiteDisplayClassificationInsteadOfAgeOrTitle(bool show)
    {
        using var http = new HttpClient(new Handler((request, _) =>
        {
            Assert.Equal("/v2/catalog/works", request.RequestUri!.AbsolutePath);
            Assert.Contains("q=Synthetic", request.RequestUri.Query); Assert.Contains("nsfw=true", request.RequestUri.Query);
            Assert.Equal(!show, request.RequestUri.Query.Contains("content_limit=sfw"));
            return Task.FromResult(Page(Row("1", "sfw", "r18"), Row("2", "nsfw"), Row("3", "sfw", "sensitive")));
        }));
        var result = await Catalog(http).SearchVisibleAsync("Synthetic", null, show, default);
        Assert.Equal(show ? new[] { "1", "2", "3" } : new[] { "1", "3" }, result.Items.Select(w => w.Id));
        Assert.Equal("r18", result.Items[0].ContentRating); Assert.Equal("cur_more", result.NextCursor);
    }
    [Theory][InlineData(null)][InlineData("")][InlineData("unknown")][InlineData(" SFW ")]
    public async Task MissingOrUnsupportedDisplayClassificationIsAnErrorWhenHidden(string? limit)
    {
        using var http = new HttpClient(new Handler((_, _) => Task.FromResult(Page(Row("1", limit)))));
        var catalog = Catalog(http);
        Assert.Equal(OnlineFailure.InvalidResponse, (await Assert.ThrowsAsync<OnlineException>(() => catalog.SearchVisibleAsync("Synthetic", null, false, default))).Failure);
        Assert.Single((await catalog.SearchVisibleAsync("Synthetic", null, true, default)).Items);
    }
    [Theory][InlineData(false)][InlineData(true)]
    public async Task TagPagesKeepFilterAndCursorIncludingEmptyVisiblePage(bool show)
    {
        using var http = new HttpClient(new Handler((request, _) =>
        {
            Assert.Contains("tag_id=54", request.RequestUri!.Query); Assert.Contains("cursor=cur_next", request.RequestUri.Query);
            Assert.Equal(!show, request.RequestUri.Query.Contains("content_limit=sfw"));
            return Task.FromResult(Page(Row("2", "nsfw")));
        }));
        var result = await Catalog(http).TagWorksVisibleAsync("54", "cur_next", show, default);
        Assert.Equal(show ? 1 : 0, result.Works.Length); Assert.Equal("cur_more", result.NextCursor);
    }
    [Theory][InlineData("../54", null)][InlineData("54", "")][InlineData("54", "other")]
    public async Task InvalidBrowseParametersNeverSendRequest(string id, string? cursor)
    {
        int calls = 0; using var http = new HttpClient(new Handler((_, _) => { calls++; return Task.FromResult(Page()); }));
        Assert.Equal(OnlineFailure.InvalidRequest, (await Assert.ThrowsAsync<OnlineException>(() => Catalog(http).TagWorksVisibleAsync(id, cursor, false, default))).Failure);
        Assert.Equal(0, calls);
    }
    [Theory][InlineData(false)][InlineData(true)]
    public async Task OldSearchOrTagResponseCannotReturnAfterSwitchOrSwitchBack(bool tag)
    {
        var released = new TaskCompletionSource<System.Net.Http.HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var http = new HttpClient(new Handler((_, _) => released.Task)); var catalog = Catalog(http);
        var policy = new CatalogBrowsePolicy(); policy.Set(true); var browsing = new CatalogBrowseClient(catalog, catalog, catalog, policy);
        Task old = tag ? browsing.EntityWorksAsync(CatalogEntityKind.Tag, "54", null, default) : browsing.SearchAsync("Synthetic", null, default);
        policy.Set(false); policy.Set(true); released.SetResult(Page(Row("2", "nsfw")));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await old);
    }
    [Fact]
    public async Task RecognitionSearchAndDirectWorkDetailsAreNotRestrictedByBrowseSetting()
    {
        using var http = new HttpClient(new Handler((request, _) =>
        {
            Assert.DoesNotContain("content_limit", request.RequestUri!.Query); Assert.Contains("nsfw=true", request.RequestUri.Query);
            if (request.RequestUri.AbsolutePath.EndsWith("/search"))
                return Task.FromResult(Json("{\"object\":\"list\",\"items\":[{\"object\":\"search_result\",\"target_object\":\"work\",\"id\":\"2\"}]}"));
            if (request.RequestUri.AbsolutePath.EndsWith("/2")) return Task.FromResult(Json(JsonSerializer.Serialize(Row("2", "nsfw", "r18"))));
            return Task.FromResult(Page(Row("2", "nsfw", "r18")));
        }));
        var catalog = Catalog(http); var browsing = new CatalogBrowseClient(catalog, catalog, catalog, new());
        Assert.Equal("2", Assert.Single((await catalog.SearchAsync("Synthetic", null, default)).Items).Id);
        Assert.Equal("2", (await browsing.DetailsAsync("2", false, default)).Id);
    }
    [Fact]
    public async Task ResetClearsDisplayedCardsAndRejectsLateSearchDespiteIgnoredCancellation()
    {
        var released = new TaskCompletionSource<System.Net.Http.HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        bool held = false;
        using var http = new HttpClient(new Handler((_, _) => held ? released.Task : Task.FromResult(Page(Row("2", "nsfw")))));
        var catalog = Catalog(http); var policy = new CatalogBrowsePolicy(); policy.Set(true);
        var client = new CatalogBrowseClient(catalog, catalog, catalog, policy);
        using var profile = new TransactionProfile(); var configuration = new NativeConfiguration(profile.Root); var rules = new LegacyClientTextRules();
        await using var offline = new OfflineLibraryClient(new IsolatedLibraryFactory(new NativeLibraryCodec(), configuration), rules);
        await using var workspace = new WorkspaceViewModel(offline, new IsolatedUiPreferencesStore(configuration), configuration, rules);
        await using var model = new OnlineViewModel(client, new Credential(), new Registry(), new(offline, catalog), workspace);
        model.ResetBrowsing();
        Assert.Empty(model.SearchMessage);
        model.Query = "Synthetic";
        await model.SearchAsync(); Assert.Single(model.Items);
        held = true; var old = model.SearchAsync(true); policy.Set(false); model.ResetBrowsing();
        released.SetResult(Page(Row("2", "nsfw"))); await old;
        Assert.Empty(model.Items); Assert.False(model.CanLoadMore); Assert.Null(model.Details);
        Assert.Equal(OnlineState.Idle, model.SearchState);
        Assert.Contains("请重新搜索", model.SearchMessage);
    }
    private sealed class Registry : IRemoteCoverRegistry { public string? Register(string? url, string revision) => url; }
}

[Collection("Native profile resource")]
public sealed class CatalogBrowsePreferenceTests
{
    [Theory][InlineData(false)][InlineData(true)]
    public async Task RoundTripIsOptionalAndDoesNotChangeShelfUiOrProfile(bool show)
    {
        using var f = new ProfileFixture(); string[] files = ["library.json", "native-ui.json", "native-profile.json"];
        byte[][] before;
        using (var profile = f.Open())
        {
            // The component and manifest names are obtained from the actual fixture.
            files = ["library.json", "native-ui.json", NativeProfileStore.ManifestName];
            before = files.Select(n => File.ReadAllBytes(Path.Combine(f.Root, n))).ToArray();
            await using var store = new CatalogBrowsePreferencesStore(profile.Configuration());
            Assert.False((await store.LoadAsync()).Preference.ShowNsfw);
            Assert.False(File.Exists(Path.Combine(f.Root, CatalogBrowsePreferencesStore.FileName)));
            await store.SaveAsync(new(show));
        }
        using var reopened = f.Open(); await using var next = new CatalogBrowsePreferencesStore(reopened.Configuration());
        Assert.Equal(show, (await next.LoadAsync()).Preference.ShowNsfw);
        for (int i = 0; i < files.Length; i++) Assert.Equal(before[i], File.ReadAllBytes(Path.Combine(f.Root, files[i])));
    }
    [Theory][InlineData("version")][InlineData("profile")][InlineData("store")][InlineData("schema")]
    public async Task UnsupportedOrForeignPreferenceStaysOffAndCannotBeOverwritten(string fault)
    {
        using var f = new ProfileFixture(); using var profile = f.Open(); await using var store = new CatalogBrowsePreferencesStore(profile.Configuration());
        await store.SaveAsync(new(true)); var path = Path.Combine(f.Root, CatalogBrowsePreferencesStore.FileName); var json = JsonNode.Parse(File.ReadAllBytes(path))!;
        if (fault == "version") json["Version"] = 2;
        else if (fault == "profile") json["ProfileId"] = Guid.NewGuid().ToString();
        else if (fault == "store") json["StoreId"] = Guid.NewGuid().ToString(); else json["Future"] = true;
        File.WriteAllText(path, json.ToJsonString()); var original = File.ReadAllBytes(path);
        var read = await store.LoadAsync(); Assert.False(read.Preference.ShowNsfw); Assert.True(read.ReadOnly);
        await Assert.ThrowsAsync<InvalidDataException>(() => store.SaveAsync(new(true))); Assert.Equal(original, File.ReadAllBytes(path));
    }
    [Fact]
    public async Task InterruptedAtomicSaveKeepsLastGoodSetting()
    {
        using var f = new ProfileFixture(); using var profile = f.Open(); await using var good = new CatalogBrowsePreferencesStore(profile.Configuration());
        await good.SaveAsync(new(false));
        await using var failed = new CatalogBrowsePreferencesStore(profile.Configuration(), stage => stage == PersistenceStage.BeforeReplace ? throw new IOException("synthetic") : Task.CompletedTask);
        await Assert.ThrowsAsync<IOException>(() => failed.SaveAsync(new(true)));
        Assert.False((await good.LoadAsync()).Preference.ShowNsfw);
    }
    [Fact]
    public async Task LoadFailureHidesContentAndPreservesOriginalPreference()
    {
        using var f = new ProfileFixture(); using var profile = f.Open();
        var path = Path.Combine(f.Root, CatalogBrowsePreferencesStore.FileName); File.WriteAllText(path, "{broken"); var before = File.ReadAllBytes(path);
        await using var store = new CatalogBrowsePreferencesStore(profile.Configuration()); var policy = new CatalogBrowsePolicy();
        await using var settings = new CatalogBrowseSettings(store, policy); await settings.InitializeAsync();
        Assert.False(settings.ShowNsfw); Assert.Equal(before, File.ReadAllBytes(path)); Assert.Contains("格式损坏", settings.Message);
    }
}
