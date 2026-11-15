using TheOrder.Application;
using TheOrder.Domain;
using System.Text.Json;
using static TheOrder.Infrastructure.Online.RemoteWorkDto;

namespace TheOrder.Infrastructure.Online;

public sealed partial class NextMoeCatalog : IContentFilteredCatalog
{
    public Task<OnlinePage> SearchVisibleAsync(string query, string? cursor, bool showNsfw, CancellationToken cancellation)
    {
        var q = query.Trim();
        if (q.Length is < 1 or > 512) throw new OnlineException(OnlineFailure.InvalidRequest);
        return VisibleWorksAsync(new() { ["q"] = q, ["include"] = "ratings,companies,refs,titles,covers" }, cursor, showNsfw, cancellation);
    }
    public async Task<CatalogEntityWorks> TagWorksVisibleAsync(string id, string? cursor, bool showNsfw, CancellationToken cancellation)
    {
        if (!ValidId(id)) throw new OnlineException(OnlineFailure.InvalidRequest);
        var page = await VisibleWorksAsync(new() { ["tag_id"] = id, ["include"] = "titles,refs,covers" }, cursor, showNsfw, cancellation).ConfigureAwait(false);
        return new(page.Items, page.NextCursor);
    }
    private Task<OnlinePage> VisibleWorksAsync(Dictionary<string, string> parameters, string? cursor, bool showNsfw, CancellationToken cancellation)
    {
        if (cursor is not null && (!cursor.StartsWith("cur_", StringComparison.Ordinal) || cursor.Length > 512))
            throw new OnlineException(OnlineFailure.InvalidRequest);
        return transport.Operation<OnlinePage>(async scope =>
        {
            // The fact-rating gate is open: r18 is not the site's SFW/NSFW display classification.
            // The work collection supports content_limit on both title-search and tag lanes.
            var p = Parameters(); foreach (var pair in parameters) p[pair.Key] = pair.Value;
            p["limit"] = "20"; if (cursor is not null) p["cursor"] = cursor;
            if (!showNsfw) p["content_limit"] = "sfw";
            using var body = await transport.GetAsync("catalog/works", p, scope).ConfigureAwait(false);
            RequireList(body.RootElement, 20);
            var next = Text(body.RootElement, "next_cursor");
            if (next.Length > 0 && (!next.StartsWith("cur_", StringComparison.Ordinal) || next.Length > 512))
                throw new OnlineException(OnlineFailure.InvalidResponse);
            var works = new List<TheOrder.Domain.WorkMetadata>();
            foreach (var row in Rows(body.RootElement, "items"))
            {
                if (MapDiscoveryWork(row, showNsfw) is { } work && works.All(w => w.Id != work.Id)) works.Add(work);
            }
            scope.Checkpoint(OnlineAcceptanceStage.Mapped);
            return new([.. works], next.Length == 0 ? null : next);
        }, cancellation);
    }

    private TheOrder.Domain.WorkMetadata? MapDiscoveryWork(JsonElement row, bool showNsfw)
    {
        if (Text(row, "object") != "work" || !ValidId(Text(row, "id"))) throw new OnlineException(OnlineFailure.InvalidResponse);
        // Works can appear before a community-site claim. Use the editorial classification,
        // which is independent of the age rating and the cover's image classification.
        if (Text(row, "medium") != "galgame") return null;
        if (!showNsfw)
        {
            var limit = Field(row, "content_limit");
            if (limit.ValueKind != JsonValueKind.String || limit.GetString() is not ("sfw" or "nsfw"))
                throw new OnlineException(OnlineFailure.InvalidResponse);
            if (limit.GetString() == "nsfw") return null;
        }
        return new RemoteWorkDto(row).Map(includeExplicit && showNsfw, Now);
    }
}
