using System.Text.Json.Nodes;
using TheOrder.Application;
using TheOrder.Infrastructure.Online;
using Xunit;
using static TheOrder.Tests.OnlineTransportTests;

namespace TheOrder.Tests;

public sealed class CatalogDiscoveryTests
{
    private static JsonObject Work(string id, string? medium = "galgame", string? limit = "sfw", string? site = null) => new()
    {
        ["object"] = "work", ["id"] = id, ["medium"] = medium, ["content_limit"] = limit,
        ["display_name"] = "Synthetic " + id,
        ["claim"] = site is null ? null : new JsonObject { ["site"] = site, ["site_work_id"] = id, ["state"] = "live" },
        ["companies"] = new JsonArray(new JsonObject { ["display_name"] = "Studio", ["attribution_role"] = "developer" }),
        ["cover"] = new JsonObject { ["url"] = "https://images.example/" + id, ["sexual"] = "safe" }
    };
    private static HttpResponseMessage Page(params JsonObject[] works) => Json(new JsonObject
    { ["object"] = "list", ["items"] = new JsonArray(works.Select(w => (JsonNode)w).ToArray()), ["next_cursor"] = "cur_next" }.ToJsonString());
    private static NextMoeCatalog Catalog(HttpClient http) => new(new(http, new Credential(), new("https://api.nextmoe.dev/v2/")));

    [Theory][InlineData(false)][InlineData(true)]
    public async Task SharedCatalogSearchIncludesUnclaimedAndOtherCommunityGames(bool show)
    {
        using var http = new HttpClient(new Handler((request, _) =>
        {
            Assert.Equal("/v2/catalog/works", request.RequestUri!.AbsolutePath);
            Assert.Equal(!show, request.RequestUri.Query.Contains("content_limit=sfw"));
            return Task.FromResult(Page(Work("1"), Work("2", site: "kungal"), Work("3", site: "letmoe"), Work("4", limit: "nsfw"), Work("5", "anime"), Work("6", null)));
        }));
        var page = await Catalog(http).SearchVisibleAsync("Synthetic", null, show, default);
        Assert.Equal(show ? new[] { "1", "2", "3", "4" } : new[] { "1", "2", "3" }, page.Items.Select(w => w.Id));
        Assert.Equal("cur_next", page.NextCursor);
        var work = page.Items[0]; Assert.Equal(new[] { "Studio" }, work.Developers);
        Assert.Equal("https://images.example/1", work.CoverUrl);
    }
    [Fact] public async Task TagBrowsingUsesTheSameCatalogPopulation()
    {
        using var http = new HttpClient(new Handler((request, _) =>
        {
            Assert.Contains("tag_id=54", request.RequestUri!.Query);
            return Task.FromResult(Page(Work("1"), Work("2", "novel"), Work("3", limit: "nsfw")));
        }));
        var page = await Catalog(http).TagWorksVisibleAsync("54", null, false, default);
        Assert.Equal("1", Assert.Single(page.Works).Id); Assert.Equal("cur_next", page.NextCursor);
    }
    [Theory][InlineData(null)][InlineData("unknown")]
    public async Task UnclaimedWorkWithoutDisplayClassificationCannotBeAssumedSafe(string? limit)
    {
        using var http = new HttpClient(new Handler((_, _) => Task.FromResult(Page(Work("1", limit: limit)))));
        var error = await Assert.ThrowsAsync<OnlineException>(() => Catalog(http).SearchVisibleAsync("Synthetic", null, false, default));
        Assert.Equal(OnlineFailure.InvalidResponse, error.Failure);
    }
}
