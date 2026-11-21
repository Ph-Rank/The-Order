using System.Net;
using System.Text;
using System.Text.Json;
using TheOrder.Application;
using TheOrder.Domain;
using TheOrder.Infrastructure.Online;
using Xunit;

namespace TheOrder.Tests;

public sealed class CollectionSyncTests
{
    private static readonly string Token = new('b', 32);
    private static readonly string OtherToken = new('c', 32);

    [Theory]
    [InlineData("playing", 1, 3)]
    [InlineData("completed", 2, 2)]
    [InlineData("paused", 3, 4)]
    [InlineData("dropped", 4, 5)]
    [InlineData("wish", 5, 1)]
    public void MapsTheFiveStatusesAndRatingScales(string state, int vndbLabel, int bangumiType)
    {
        var entry = Entry() with { Status = state, PersonalRating = 8.25 };
        Assert.Equal(new(CollectionSite.Vndb, "v103", vndbLabel, 83), CollectionUpdate.From(entry, CollectionSite.Vndb));
        Assert.Equal(new(CollectionSite.Bangumi, "102", bangumiType, 8), CollectionUpdate.From(entry, CollectionSite.Bangumi));
        Assert.Equal(new(CollectionSite.NextMoe, "101", bangumiType, 8), CollectionUpdate.From(entry, CollectionSite.NextMoe));
    }

    [Fact]
    public void UnsetFieldsAndInconsistentReferencesCannotOverwriteRemoteData()
    {
        var entry = Entry();
        Assert.Null(CollectionUpdate.From(entry with { Status = "planned", PersonalRating = 0 }, CollectionSite.Vndb));
        Assert.Null(CollectionUpdate.From(entry with { Metadata = entry.Metadata! with {
            References = [new("vndb", "v103", "https://vndb.org/v111")] } }, CollectionSite.Vndb));
        Assert.Null(CollectionUpdate.From(entry with { Metadata = entry.Metadata! with {
            References = [..entry.Metadata.References, new("vndb", "v110", null)] } }, CollectionSite.Vndb));
        Assert.Null(CollectionUpdate.From(entry with { Metadata = entry.Metadata! with {
            References = [new("bangumi", "102", "https://bgm.tv/subject/113")] } }, CollectionSite.Bangumi));
        var ratingOnly = CollectionUpdate.From(entry with { Status = "planned", PersonalRating = 6.4 }, CollectionSite.Vndb);
        Assert.Equal(new(CollectionSite.Vndb, "v103", null, 64), ratingOnly);
    }

    [Theory]
    [InlineData("v60562", "r139142", "r143963")]
    [InlineData("v26523", "r117658", "r130155")]
    [InlineData("v23740", "r100185", "r133601")]
    public void VndbUsesTheWorkIdWhenNextMoeAlsoListsIndividualReleases(string workId, string release1, string release2)
    {
        var entry = Entry();
        var metadata = entry.Metadata! with
        {
            References = [new("vndb", release1, "https://vndb.org/" + release1),
                new("vndb", workId, "https://vndb.org/" + workId), new("vndb", release2, null)]
        };
        Assert.Equal(workId, CollectionUpdate.WorkIdFor(entry with { Metadata = metadata }, CollectionSite.Vndb));
        Assert.Null(CollectionUpdate.WorkIdFor(entry with { Metadata = metadata with
        {
            References = [new("vndb", release1, "https://vndb.org/" + release1)]
        } }, CollectionSite.Vndb));
    }

    [Fact]
    public void VndbAcceptsExactWorkLinksAndExplicitSourceLayers()
    {
        var entry = Entry();
        var baseWork = entry.Metadata! with { References = [new("bangumi", "102", "https://bgm.tv/subject/102")] };
        var direct = baseWork with { Id = "vndb:v103", References = [new("vndb", "v103", "https://vndb.org/v103")] };
        var layer = new MetadataSourceLayer("vndb", "v103", direct, ["description"], false);
        Assert.Equal("v103", CollectionUpdate.WorkIdFor(entry with { Metadata = direct }, CollectionSite.Vndb));
        Assert.Equal("v103", CollectionUpdate.WorkIdFor(entry with { MetadataMix = new(null, [layer]) }, CollectionSite.Vndb));
        Assert.Equal("v103", CollectionUpdate.WorkIdFor(entry with { MetadataMix = new(baseWork, [layer]) }, CollectionSite.Vndb));
        Assert.Equal("v103", CollectionUpdate.WorkIdFor(entry with { Metadata = baseWork with
        {
            References = [new("vndb", "https://vndb.org/v103/", "https://vndb.org/v103/")]
        } }, CollectionSite.Vndb));
        Assert.Null(CollectionUpdate.WorkIdFor(entry with { MetadataMix = new(baseWork with
        {
            References = [new("vndb", "v104", null)]
        }, [layer]) }, CollectionSite.Vndb));
    }

    [Fact]
    public void BangumiWritesAnExplicitGameSourceAndRejectsConflictingCatalogNumber()
    {
        var entry = Entry();
        var catalog = entry.Metadata! with { References = [new("vndb", "v103", "https://vndb.org/v103")] };
        var direct = catalog with { Id = "bangumi:46874", References = [new("bangumi", "46874", "https://bgm.tv/subject/46874")] };
        var layer = new MetadataSourceLayer("bangumi", "46874", direct, ["description"], true);

        Assert.Equal("46874", CollectionUpdate.WorkIdFor(entry with { MetadataMix = new(catalog, [layer]) }, CollectionSite.Bangumi));
        Assert.Equal("46874", CollectionUpdate.WorkIdFor(entry with { Metadata = direct }, CollectionSite.Bangumi));
        Assert.Null(CollectionUpdate.WorkIdFor(entry with { MetadataMix = new(catalog with
        {
            References = [new("bangumi", "102", "https://bgm.tv/subject/102")]
        }, [layer]) }, CollectionSite.Bangumi));
    }

    [Theory]
    [InlineData(CollectionSite.Vndb, 84, 2)]
    [InlineData(CollectionSite.Bangumi, 8, 2)]
    [InlineData(CollectionSite.NextMoe, 8, 2)]
    public void ConfirmedEditsAndQueuedWritesRetainOnlyRequestedCurrentFields(CollectionSite site, int score, int status)
    {
        var entry = Entry() with { Status = "completed", PersonalRating = 8.4 };
        var rating = CollectionUpdate.From(entry, site, CollectionFields.Rating)!;
        Assert.Equal(score, rating.Rating);
        Assert.Null(rating.Status);
        var state = CollectionUpdate.From(entry, site, CollectionFields.Status)!;
        Assert.Equal(status, state.Status);
        Assert.Null(state.Rating);
        var both = CollectionUpdate.From(entry, site)!;
        Assert.Equal(rating, both.RetainCurrentFields(entry with { Status = "paused" }));
        Assert.Equal(state, both.RetainCurrentFields(entry with { PersonalRating = 7 }));
        Assert.Null(both.RetainCurrentFields(entry with { Metadata = null }));
        Assert.Null(rating.RetainCurrentFields(entry with { Status = "paused", PersonalRating = 0 }));
    }

    [Fact]
    public async Task VndbValidatesListWriteAndPreservesRemoteCustomLabelsAndOtherFields()
    {
        var store = new TokenStore();
        using var api = new RecordingHandler();
        api.Replies.Enqueue(Json("""{"id":"u21","username":"demo","permissions":["listread"]}"""));
        api.Replies.Enqueue(Json("""{"id":"u21","username":"demo","permissions":["listwrite"]}"""));
        api.Replies.Enqueue(new(HttpStatusCode.NoContent));
        api.Replies.Enqueue(new(HttpStatusCode.NoContent));
        using var http = new HttpClient(api);
        var auth = new VndbAuthorization(store, new VndbAccountProvider(http));
        var readOnly = await Assert.ThrowsAsync<CollectionSyncException>(() => auth.SaveAsync(Token, default));
        Assert.Equal(CollectionSyncFailure.MissingPermission, readOnly.Failure);
        Assert.Null(store.Value);

        await auth.SaveAsync(Token, default);
        var account = Assert.IsType<VndbAccount>(auth.ConnectedAccount);
        Assert.Equal("u21", account.Id);
        await auth.WriteAsync(new(CollectionSite.Vndb, "v103", 2, 84), account, default);
        await auth.WriteAsync(new(CollectionSite.Vndb, "v103", null, 90), account, default);
        Assert.All(api.Requests, r => Assert.Equal("Token " + Token, r.Authorization));
        Assert.Equal("https://api.vndb.org/kana/authinfo", api.Requests[0].Uri);
        Assert.Equal(HttpMethod.Patch, api.Requests[2].Method);
        Assert.Equal("https://api.vndb.org/kana/ulist/v103", api.Requests[2].Uri);
        using var update = JsonDocument.Parse(api.Requests[2].Body!);
        Assert.Equal(new[] { 2 }, Ints(update.RootElement.GetProperty("labels_set")));
        Assert.Equal(new[] { 1, 3, 4, 5 }, Ints(update.RootElement.GetProperty("labels_unset")));
        Assert.Equal(84, update.RootElement.GetProperty("vote").GetInt32());
        Assert.Equal(3, update.RootElement.EnumerateObject().Count());
        Assert.Equal("""{"vote":90}""", api.Requests[3].Body);
    }

    [Fact]
    public async Task ReplacingOrRevokingVndbAccessRejectsAnEarlierConfirmation()
    {
        var store = new TokenStore();
        using var api = new RecordingHandler();
        for (int i = 0; i < 2; i++)
            api.Replies.Enqueue(Json("""{"id":"u21","username":"demo","permissions":["listwrite"]}"""));
        api.Replies.Enqueue(Json("""{"error":"sensitive response"}""", HttpStatusCode.Unauthorized));
        using var http = new HttpClient(api);
        var auth = new VndbAuthorization(store, new VndbAccountProvider(http));
        await auth.SaveAsync(Token, default);
        var earlier = auth.ConnectedAccount!;
        await auth.SaveAsync(OtherToken, default);
        var update = new CollectionUpdate(CollectionSite.Vndb, "v103", 2, null);
        var changed = await Assert.ThrowsAsync<CollectionSyncException>(() => auth.WriteAsync(update, earlier, default));
        Assert.Equal(CollectionSyncFailure.Changed, changed.Failure);
        Assert.Equal(2, api.Requests.Count);
        var rejected = await Assert.ThrowsAsync<CollectionSyncException>(() => auth.WriteAsync(update, auth.ConnectedAccount!, default));
        Assert.Equal(CollectionSyncFailure.Unauthorized, rejected.Failure);
        Assert.DoesNotContain("sensitive response", rejected.Message);
        Assert.Null(auth.ConnectedAccount);
        Assert.NotNull(store.Value);
        await auth.ClearAsync(default);
        Assert.Null(store.Value);
    }

    [Theory]
    [InlineData(HttpStatusCode.Accepted)]
    [InlineData(HttpStatusCode.NoContent)]
    public async Task BangumiWriteUsesOnlyTheExactOwnCollectionAndRequestedFields(HttpStatusCode returnedStatus)
    {
        var access = new BangumiAccessContext();
        var auth = new BangumiAuthorization(new BangumiStore(), new BangumiAccountStub(), access);
        await auth.SaveAsync("synthetic_bangumi_token", default);
        using var api = new RecordingHandler();
        api.Replies.Enqueue(new(returnedStatus));
        using var http = new HttpClient(api);
        var writer = new BangumiCollectionWriter(http, access);
        var captured = access.Capture();
        await writer.WriteAsync(new(CollectionSite.Bangumi, "102", null, 8), captured, default);
        var r = Assert.Single(api.Requests);
        Assert.Equal("https://api.bgm.tv/v0/users/-/collections/102", r.Uri);
        Assert.Equal(HttpMethod.Post, r.Method);
        Assert.Equal("Bearer synthetic_bangumi_token", r.Authorization);
        Assert.Equal("application/json", r.ContentType);
        Assert.Equal("""{"rate":8}""", r.Body);
        await auth.ClearAsync(default);
        await Assert.ThrowsAsync<CollectionSyncException>(() => writer.WriteAsync(new(CollectionSite.Bangumi, "102", 3, null), captured, default));
        Assert.Single(api.Requests);
    }

    [Theory]
    [InlineData(HttpStatusCode.OK, CollectionSyncFailure.InvalidResponse)]
    [InlineData(HttpStatusCode.BadGateway, CollectionSyncFailure.InvalidResponse)]
    [InlineData(HttpStatusCode.UnsupportedMediaType, CollectionSyncFailure.UnsupportedMediaType)]
    public async Task BangumiReportsUnexpectedHttpStatusWithoutGuessingOrResending(HttpStatusCode returnedStatus, CollectionSyncFailure failure)
    {
        var access = new BangumiAccessContext();
        var auth = new BangumiAuthorization(new BangumiStore(), new BangumiAccountStub(), access);
        await auth.SaveAsync("synthetic_bangumi_token", default);
        using var api = new RecordingHandler();
        api.Replies.Enqueue(Json("""{"detail":"server-only information"}""", returnedStatus));
        using var http = new HttpClient(api);
        var writer = new BangumiCollectionWriter(http, access);

        var error = await Assert.ThrowsAsync<CollectionSyncException>(() => writer.WriteAsync(
            new(CollectionSite.Bangumi, "102", 2, 8), access.Capture(), default));
        Assert.Equal(failure, error.Failure);
        Assert.Contains($"HTTP {(int)returnedStatus}", error.Message);
        Assert.DoesNotContain("server-only", error.Message);
        Assert.Single(api.Requests);
    }

    [Fact]
    public async Task NextMoeReportsWhichLinkedWorkCannotBeWritten()
    {
        var access = new NextMoeAuthTests.Fixture();
        await access.SignIn();
        using var api = new RecordingHandler();
        api.Replies.Enqueue(Json("""{"code":"NOT_FOUND"}""", HttpStatusCode.NotFound));
        api.Replies.Enqueue(Json("""{"code":"NOT_FOUND","detail":"sensitive detail"}""", HttpStatusCode.NotFound));
        api.Replies.Enqueue(Json("""{"code":"NOT_FOUND"}""", HttpStatusCode.NotFound));
        using var http = new HttpClient(api);
        var writer = new NextMoePersonalApi(http, access.Auth, new());

        var result = await writer.WriteAsync(new(CollectionSite.NextMoe, "101", 2, null),
            await access.Auth.AuthContext.CaptureAsync(default), default);

        Assert.False(result.StateSynced || result.RatingSynced);
        Assert.Contains("#101", result.Error);
        Assert.DoesNotContain("sensitive detail", result.Error);
        Assert.Equal(new[] { HttpMethod.Get, HttpMethod.Put, HttpMethod.Get }, api.Requests.Select(request => request.Method));
        Assert.All(api.Requests.Take(2), request => Assert.EndsWith("/me/work-states/101", request.Uri));
        Assert.EndsWith("/catalog/works/101?fields=id,object", api.Requests[2].Uri);
    }

    [Fact]
    public async Task NextMoePlainPersonalNotFoundLooksUpCanonicalCatalogWorkWithoutWritingToIt()
    {
        var access = new NextMoeAuthTests.Fixture();
        await access.SignIn();
        using var api = new RecordingHandler();
        api.Replies.Enqueue(Json("""{"code":"NOT_FOUND"}""", HttpStatusCode.NotFound));
        api.Replies.Enqueue(Json("""{"code":"NOT_FOUND"}""", HttpStatusCode.NotFound));
        api.Replies.Enqueue(Json("""{"code":"ENTITY_MERGED","object":"work","current_id":"4"}""", HttpStatusCode.NotFound));
        using var http = new HttpClient(api);
        var writer = new NextMoePersonalApi(http, access.Auth, new());

        var result = await writer.WriteAsync(new(CollectionSite.NextMoe, "212890", 2, 8),
            await access.Auth.AuthContext.CaptureAsync(default), default);

        Assert.False(result.StateSynced || result.RatingSynced);
        Assert.Contains("#212890", result.Error);
        Assert.Contains("#4", result.Error);
        Assert.Equal("4", result.MergedInto);
        var write = Assert.Single(api.Requests, request => request.Method == HttpMethod.Put);
        Assert.EndsWith("/me/work-states/212890", write.Uri);
        Assert.EndsWith("/catalog/works/212890?fields=id,object", api.Requests[2].Uri);
    }

    [Theory]
    [InlineData("work")]
    [InlineData("character")]
    public async Task OnlyAWorkMergeReceiptCanOfferACanonicalCollectionTarget(string objectType)
    {
        var access = new NextMoeAuthTests.Fixture();
        await access.SignIn();
        using var api = new RecordingHandler();
        api.Replies.Enqueue(Json(JsonSerializer.Serialize(new { code = "ENTITY_MERGED", @object = objectType, current_id = "333" }),
            HttpStatusCode.NotFound));
        using var http = new HttpClient(api);
        var writer = new NextMoePersonalApi(http, access.Auth, new());

        var result = await writer.WriteAsync(new(CollectionSite.NextMoe, "101", 2, 8),
            await access.Auth.AuthContext.CaptureAsync(default), default);

        Assert.False(result.StateSynced || result.RatingSynced);
        Assert.NotNull(result.Error);
        Assert.Equal(objectType == "work", result.Error.Contains("#333", StringComparison.Ordinal));
        var request = Assert.Single(api.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.EndsWith("/me/work-states/101", request.Uri);
    }

    [Fact]
    public async Task NextMoeSyncPreservesCloudReviewAndCompletionWithoutSendingPlaytime()
    {
        var access = new NextMoeAuthTests.Fixture();
        await access.SignIn();
        using var api = new RecordingHandler();
        api.Replies.Enqueue(Json("""{"object":"work_state","work_id":"101","state":"doing","completion":"one_route"}"""));
        api.Replies.Enqueue(Json("""{"object":"work_state","work_id":"101","state":"done","completion":"one_route"}"""));
        api.Replies.Enqueue(Json("""{"object":"rating","work_id":"101","overall":7,"recommend":"yes","short_summary":"已写短评","aspect_scores":{"story":9},"spoiler_level":"none","short_summary_html":"<p>...</p>"}"""));
        api.Replies.Enqueue(Json("""{"object":"rating","work_id":"101","overall":8}"""));
        using var http = new HttpClient(api);
        var writer = new NextMoePersonalApi(http, access.Auth, new());
        var result = await writer.WriteAsync(new(CollectionSite.NextMoe, "101", 2, 8),
            await access.Auth.AuthContext.CaptureAsync(default), default);
        Assert.Null(result.Error);
        Assert.True(result.StateSynced && result.RatingSynced);
        Assert.Equal(new[] { HttpMethod.Get, HttpMethod.Put, HttpMethod.Get, HttpMethod.Put }, api.Requests.Select(r => r.Method));
        Assert.All(api.Requests, r => Assert.StartsWith("https://api.nextmoe.dev/v2/me/", r.Uri));
        Assert.Equal("""{"state":"done","completion":"one_route"}""", api.Requests[1].Body);
        using var rating = JsonDocument.Parse(api.Requests[3].Body!);
        Assert.Equal(8, rating.RootElement.GetProperty("overall").GetInt32());
        Assert.Equal("已写短评", rating.RootElement.GetProperty("short_summary").GetString());
        Assert.Equal(9, rating.RootElement.GetProperty("aspect_scores").GetProperty("story").GetInt32());
        Assert.False(rating.RootElement.TryGetProperty("short_summary_html", out _));
        Assert.False(rating.RootElement.TryGetProperty("playtime", out _));
    }

    [Fact]
    public async Task NextMoeReportsAPartialResultAndKeepsTheConfirmedAccount()
    {
        var access = new NextMoeAuthTests.Fixture();
        await access.SignIn();
        using var api = new RecordingHandler();
        api.Replies.Enqueue(Json("""{"object":"work_state","work_id":"101","state":"done"}"""));
        api.Replies.Enqueue(Json("""{"code":"NOT_FOUND"}""", HttpStatusCode.NotFound));
        api.Replies.Enqueue(Json("""{"code":"VALIDATION_FAILED"}""", HttpStatusCode.UnprocessableEntity));
        using var http = new HttpClient(api);
        var writer = new NextMoePersonalApi(http, access.Auth, new());
        var captured = await access.Auth.AuthContext.CaptureAsync(default);
        var update = new CollectionUpdate(CollectionSite.NextMoe, "101", 2, 8);
        var partial = await writer.WriteAsync(update, captured, default);
        Assert.True(partial.StateSynced);
        Assert.False(partial.RatingSynced);
        Assert.NotNull(partial.Error);
        await access.Auth.SignOutAsync(default);
        var stale = await writer.WriteAsync(update, captured, default);
        Assert.False(stale.StateSynced || stale.RatingSynced);
        Assert.NotNull(stale.Error);
        Assert.Equal(3, api.Requests.Count);
    }

    private static LibraryEntry Entry()
    {
        var metadata = new WorkMetadata("101", "合成作品", "合成作品", [], null, null, null,
            null, "", null, null, [], [], [], [], [new("vndb", "v103", "https://vndb.org/v103"), new("bangumi", "102", "https://bgm.tv/subject/102")],
            null, null, "");
        return new("entry", "合成作品", false, metadata, [], null, "playing", null, false, [], [], "", 0, 0, [], "", null, null, null, new(false, null));
    }

    private static int[] Ints(JsonElement value) => value.EnumerateArray().Select(i => i.GetInt32()).ToArray();
    private static HttpResponseMessage Json(string json, HttpStatusCode code = HttpStatusCode.OK) => new(code)
    { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public readonly Queue<HttpResponseMessage> Replies = new();
        public readonly List<(HttpMethod Method, string Uri, string? Authorization, string? Body, string? ContentType)> Requests = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            Requests.Add((request.Method, request.RequestUri!.AbsoluteUri, request.Headers.Authorization?.ToString(),
                request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellation),
                request.Content?.Headers.ContentType?.ToString()));
            return Replies.Dequeue();
        }
    }
    private sealed class TokenStore : IVndbCredentialStore
    {
        public VndbCredential? Value;
        public Task<VndbCredential?> ReadAsync(CancellationToken cancellation) => Task.FromResult(Value);
        public Task SaveAsync(VndbCredential? credential, CancellationToken cancellation) { Value = credential; return Task.CompletedTask; }
    }
    private sealed class BangumiStore : IBangumiCredentialStore
    {
        public Task<string?> ReadAsync(CancellationToken cancellation) => Task.FromResult<string?>(null);
        public Task SaveAsync(string token, CancellationToken cancellation) => Task.CompletedTask;
        public Task ClearAsync(CancellationToken cancellation) => Task.CompletedTask;
    }
    private sealed class BangumiAccountStub : IBangumiAccountProvider
    {
        public Task<BangumiAccount> ValidateAsync(string token, CancellationToken cancellation) => Task.FromResult(new BangumiAccount("21", "demo", "Demo"));
    }
}
