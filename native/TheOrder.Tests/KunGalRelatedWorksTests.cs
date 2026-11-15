using System.Text.Json;
using System.Text.Json.Nodes;
using TheOrder.Application;
using TheOrder.Domain;
using TheOrder.Infrastructure.Online;
using Xunit;
using static TheOrder.Tests.OnlineTransportTests;

namespace TheOrder.Tests;

public sealed class KunGalRelatedWorksTests
{
    private static JsonObject Row(string id, string? medium = "galgame", string? site = "kungal", string? state = "live", string? page = null) =>
        new() { ["object"] = "work", ["id"] = id, ["display_name"] = "Synthetic " + id, ["medium"] = medium, ["content_limit"] = "sfw",
            ["cover"] = new JsonObject { ["url"] = "https://images.example/" + id },
            ["claim"] = site is null ? null : new JsonObject { ["site"] = site, ["state"] = state, ["site_work_id"] = page ?? id } };
    private static WorkMetadata Map(params JsonObject[] rows)
    {
        var root = Row("900"); root["relations"] = new JsonArray(rows.Select(row => (JsonNode)new JsonObject
            { ["relation_type"] = "alternative_version", ["phrase"] = "不同演绎", ["work"] = row }).ToArray());
        using var body = JsonDocument.Parse(root.ToJsonString()); return new RemoteWorkDto(body.RootElement).Map(true, DateTimeOffset.UnixEpoch);
    }
    [Fact]
    public void OneReliableIdentityKeepsAllDistinctSourceRelationEvidence()
    {
        var root = Row("900");
        root["relations"] = new JsonArray(
            new JsonObject { ["relation_type"] = "sequel_of", ["phrase"] = "有续作", ["work"] = Row("3407") },
            new JsonObject { ["relation_type"] = "side_story_of", ["phrase"] = "有外传", ["work"] = Row("3407") });
        using var body = JsonDocument.Parse(root.ToJsonString());
        var work = new RemoteWorkDto(body.RootElement).Map(true, DateTimeOffset.UnixEpoch);
        var related = Assert.Single(work.Presentation!.RelatedWorks);
        Assert.Equal("sequel_of", related.Relation); Assert.Equal("有续作", related.Subtitle);
        Assert.Equal(new[] { new CatalogRelationEvidence("sequel_of", "有续作"), new CatalogRelationEvidence("side_story_of", "有外传") }, related.RelationEvidence);
        var card = Assert.Single(RelatedWorksProjection.Build(CatalogRelatedWorks.Visible(work.Presentation), []));
        Assert.Contains("sequel_of", card.Evidence); Assert.Contains("side_story_of", card.Evidence);
        Assert.Equal("有续作 / 有外传", card.Label); Assert.Equal(40, card.Priority);
    }
    [Fact]
    public void OlderDisplayCacheRefreshesForEvidenceWithoutHidingKnownCards()
    {
        var work = Map(Row("1"));
        var old = work.Presentation! with { RelatedSchema = 1 };
        Assert.True(CatalogRelatedWorks.NeedsRefresh(old));
        Assert.Single(CatalogRelatedWorks.Visible(old));
        Assert.False(CatalogRelatedWorks.NeedsRefresh(work.Presentation));
    }
    [Theory]
    [InlineData("anime", "kungal", "live", "1")]
    [InlineData("manga", "kungal", "live", "1")]
    [InlineData("novel", "kungal", "live", "1")]
    [InlineData(null, "kungal", "live", "1")]
    [InlineData("galgame", null, "live", "1")]
    [InlineData("galgame", "bangumi", "live", "1")]
    [InlineData("galgame", "kungal", "removed", "1")]
    [InlineData("galgame", "kungal", null, "1")]
    [InlineData("galgame", "kungal", "live", "../1")]
    [InlineData("galgame", "kungal", "live", "")]
    public void NonGalgameOrUnverifiedKunGalPageNeverBecomesRelatedCardOrVersionParent(string? medium, string? site, string? state, string page)
    {
        var work = Map(Row("1", medium, site, state, page));
        Assert.Empty(work.Presentation!.RelatedWorks); Assert.Null(CatalogReferences.Preferred(work));
        Assert.Equal("900", work.Id); Assert.Equal(CatalogRelatedWorks.Schema, work.Presentation.RelatedSchema);
    }
    [Theory][InlineData("live")][InlineData("draft")]
    public void SitePageIdentityAndInternalCatalogIdentityRemainSeparate(string state)
    {
        var work = Map(Row("21", state: state, page: "301")); var related = Assert.Single(work.Presentation!.RelatedWorks);
        Assert.Equal("21", related.Id); Assert.Equal("301", related.KunGalGalgameId);
        Assert.Equal("21", CatalogReferences.Preferred(work)?.Id); Assert.True(CatalogRelatedWorks.Eligible(related));
    }
    [Fact]
    public void LegacyDisplayWithoutMediaAndSiteProofHidesOldCardsWithoutChangingUnderlyingFacts()
    {
        var original = new CatalogEntityRef(CatalogEntityKind.Work, "1", "Synthetic old cached card", ImageUrl: "https://images.example/legacy");
        var p = new CatalogPresentation("kungal", [], [], [], [], [], [], [], [original]) { LanguageSchema = CatalogLanguage.DisplaySchema };
        var work = RecognitionBoundaryTests.Work("900") with { Presentation = p };
        Assert.Empty(CatalogLanguage.Project(work).Presentation!.RelatedWorks); Assert.Single(work.Presentation!.RelatedWorks);
        Assert.True(CatalogRelatedWorks.NeedsRefresh(p));
    }
    [Theory]
    [InlineData(CatalogEntityKind.Character)] [InlineData(CatalogEntityKind.CreditName)] [InlineData(CatalogEntityKind.Company)]
    [InlineData(CatalogEntityKind.Engine)] [InlineData(CatalogEntityKind.Series)] [InlineData(CatalogEntityKind.Tag)]
    public async Task EveryEntityWorkListFiltersOtherMediaAndUnclaimedRecords(CatalogEntityKind kind)
    {
        var rows = new[] { Row("1"), Row("2", "anime"), Row("3", site: null), Row("4", state: "draft") };
        var nested = kind is CatalogEntityKind.Character or CatalogEntityKind.CreditName;
        var calls = 0;
        using var http = new HttpClient(new Handler((_, _) => { calls++; return Task.FromResult(Json(JsonSerializer.Serialize(new
        { @object = "list", next_cursor = "cur_next", items = rows.Select(row => nested ? new JsonObject { ["work"] = row } : row).ToArray() }))); }));
        var catalog = new NextMoeCatalog(new(http, new Credential(), new("https://api.nextmoe.dev/v2/")));
        var result = await catalog.EntityWorksAsync(kind, "99", null, default);
        Assert.Equal(new[] { "1", "4" }, result.Works.Select(w => w.Id)); Assert.Equal("cur_next", result.NextCursor);
        Assert.Equal(1, calls); // Supplied covers do not cause a redundant artwork request.
    }
    [Theory]
    [InlineData(CatalogEntityKind.Character)] [InlineData(CatalogEntityKind.CreditName)] [InlineData(CatalogEntityKind.Company)]
    [InlineData(CatalogEntityKind.Engine)] [InlineData(CatalogEntityKind.Series)] [InlineData(CatalogEntityKind.Tag)]
    public async Task MissingEntityCoversUseOneBatchForEligibleExactIdsAndPreserveThePage(CatalogEntityKind kind)
    {
        var missing = Row("1"); missing.Remove("cover");
        var anime = Row("2", "anime"); anime.Remove("cover");
        var unclaimed = Row("3", site: null); unclaimed.Remove("cover");
        var nested = kind is CatalogEntityKind.Character or CatalogEntityKind.CreditName;
        var rows = new[] { missing, anime, unclaimed, Row("4", state: "draft") };
        var calls = new List<Uri>();
        using var http = new HttpClient(new Handler((request, _) =>
        {
            calls.Add(request.RequestUri!);
            if (calls.Count == 1) return Task.FromResult(Json(JsonSerializer.Serialize(new
            { @object = "list", next_cursor = "cur_next", items = rows.Select(row => nested ? new JsonObject { ["work"] = row } : row).ToArray() })));
            Assert.Equal("/v2/catalog/works", request.RequestUri!.AbsolutePath);
            var query = request.RequestUri.Query.TrimStart('?').Split('&').Select(s => s.Split('=', 2))
                .ToDictionary(p => p[0], p => Uri.UnescapeDataString(p[1]));
            Assert.Equal("1", query["ids"]); Assert.Equal("covers", query["include"]); Assert.Equal("100", query["limit"]);
            var cover = Row("1"); cover["display_name"] = "A batch title must not replace the original title";
            return Task.FromResult(Json(JsonSerializer.Serialize(new { @object = "list", items = new[] { cover, Row("99") } })));
        }));
        var catalog = new NextMoeCatalog(new(http, new Credential(), new("https://api.nextmoe.dev/v2/")));
        var result = await catalog.EntityWorksAsync(kind, "99", "cur_previous", default);
        Assert.Equal(2, calls.Count); Assert.Contains("cursor=cur_previous", calls[0].Query);
        Assert.Equal(new[] { "1", "4" }, result.Works.Select(w => w.Id)); Assert.Equal("cur_next", result.NextCursor);
        Assert.Equal("Synthetic 1", result.Works[0].Title); Assert.Equal("https://images.example/1", result.Works[0].CoverUrl);
        Assert.Equal("https://images.example/4", result.Works[1].CoverUrl);
    }
    [Theory]
    [InlineData("99", "galgame", "kungal", "live")]
    [InlineData("1", "anime", "kungal", "live")]
    [InlineData("1", "galgame", null, "live")]
    [InlineData("1", "galgame", "kungal", "removed")]
    public async Task CoverBatchNeverBorrowsFromAnotherIdentityOrUnverifiedPage(string id, string medium, string? site, string state)
    {
        var missing = Row("1"); missing.Remove("cover"); var calls = 0;
        using var http = new HttpClient(new Handler((_, _) => Task.FromResult(Json(JsonSerializer.Serialize(new
        { @object = "list", items = new[] { ++calls == 1 ? new JsonObject { ["work"] = missing } : Row(id, medium, site, state) } })))));
        var catalog = new NextMoeCatalog(new(http, new Credential(), new("https://api.nextmoe.dev/v2/")));
        var result = await catalog.EntityWorksAsync(CatalogEntityKind.CreditName, "99", null, default);
        Assert.Equal(2, calls); Assert.Null(Assert.Single(result.Works).CoverUrl);
    }
    [Theory][InlineData(false)][InlineData(true)]
    public async Task CancelOrCredentialReplacementDuringTheCoverBatchRejectsTheWholeStalePage(bool replaceCredential)
    {
        var missing = Row("1"); missing.Remove("cover"); var calls = 0;
        using var cancellation = new CancellationTokenSource();
        var store = new Credential(); var credentials = new AuthenticatedCredentials(store, store);
        using var http = new HttpClient(new Handler(async (_, _) =>
        {
            if (++calls == 2)
            {
                if (replaceCredential) await credentials.SaveAsync("new-synthetic-only", default);
                else cancellation.Cancel();
            }
            return Json(JsonSerializer.Serialize(new { @object = "list", items = new[]
                { calls == 1 ? new JsonObject { ["work"] = missing } : Row("1") } }));
        }));
        var catalog = new NextMoeCatalog(new(http, credentials, new("https://api.nextmoe.dev/v2/")));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => catalog.EntityWorksAsync(CatalogEntityKind.Character, "99", null, cancellation.Token));
        Assert.Equal(2, calls);
    }
    [Theory][InlineData(true)][InlineData(false)]
    public async Task DiscoverAndTagLanesIncludeUnclaimedCatalogGamesWithoutChangingMediaFilter(bool search)
    {
        using var http = new HttpClient(new Handler((_, _) => Task.FromResult(Json(JsonSerializer.Serialize(new
        { @object = "list", next_cursor = "cur_next", items = new[] { Row("1"), Row("2", "anime"), Row("3", site: null) } })))));
        var catalog = new NextMoeCatalog(new(http, new Credential(), new("https://api.nextmoe.dev/v2/")));
        var works = search ? (await catalog.SearchVisibleAsync("Synthetic", null, false, default)).Items :
            (await catalog.TagWorksVisibleAsync("99", null, false, default)).Works;
        Assert.Equal(new[] { "1", "3" }, works.Select(w => w.Id));
    }
}
