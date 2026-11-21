using System.Text.Json;
using TheOrder.Application;

namespace TheOrder.Infrastructure.Online;

public sealed partial class NextMoePersonalApi
{
    public async Task<NextMoeCollectionResult> WriteAsync(CollectionUpdate update, OnlineAuthSnapshot expected, CancellationToken cancellation)
    {
        if (update.Site != CollectionSite.NextMoe || update.Status is < 1 or > 5 || update.Rating is < 1 or > 10
            || update.Status is null && update.Rating is null)
            return new(false, false, new CollectionSyncException(CollectionSyncFailure.Rejected).Message);

        bool stateSynced = false;
        try
        {
            var id = ValidateId(update.WorkId);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellation, expected.Changed);
            if (update.Status is { } status)
            {
                var state = status switch { 1 => "wish", 2 => "done", 3 => "doing", 4 => "on_hold", _ => "dropped" };
                await SyncStateAsync(id, state, expected, linked.Token).ConfigureAwait(false);
                stateSynced = true;
            }
            if (update.Rating is { } rating)
                await SyncRatingAsync(id, rating, expected, linked.Token).ConfigureAwait(false);
            return new(stateSynced, update.Rating.HasValue, null);
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
        { return new(stateSynced, false, new CollectionSyncException(CollectionSyncFailure.Changed).Message); }
        catch (MergedWorkException error)
        { return new(stateSynced, false, MergeMessage(update.WorkId, error.CanonicalId), error.CanonicalId); }
        catch (OnlineException error)
        {
            if (error.Failure != OnlineFailure.NotFound) return new(stateSynced, false, error.Message);
            // Personal endpoints can return plain 404 for an ID that the catalog explicitly merged.
            try
            {
                using var ignored = await Request(HttpMethod.Get, $"catalog/works/{update.WorkId}?fields=id,object", "catalog:read",
                    null, null, cancellation, expected).ConfigureAwait(false);
            }
            catch (MergedWorkException merged) { return new(stateSynced, false, MergeMessage(update.WorkId, merged.CanonicalId), merged.CanonicalId); }
            catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
            { return new(stateSynced, false, new CollectionSyncException(CollectionSyncFailure.Changed).Message); }
            catch (OnlineException) { }
            return new(stateSynced, false, $"NextMoe 无法访问已关联的作品（#{update.WorkId}），请核对作品编号。");
        }
    }

    private static string MergeMessage(string oldId, string canonicalId)
        => $"NextMoe 已将关联条目 #{oldId} 合并至 #{canonicalId}。请核对合并后的作品。";

    private async Task SyncStateAsync(string id, string state, OnlineAuthSnapshot expected, CancellationToken cancellation)
    {
        string path = "me/work-states/" + id;
        using var original = await ReadOptionalAsync(path, expected, cancellation).ConfigureAwait(false);
        var payload = new Dictionary<string, string> { ["state"] = state };
        if (original is not null)
        {
            var root = original.RootElement;
            Kind(root, "work_state");
            if (Id(root, "work_id") != id) throw Invalid();
            var before = Text(root, "state", 32);
            var completion = OptionalText(root, "completion", 32);
            ValidateState(before, completion);
            if (state == before) return;
            if (state != "wish" && completion is not null) payload["completion"] = completion;
        }
        using var result = await Request(HttpMethod.Put, path, null, payload, null, cancellation, expected).ConfigureAwait(false);
        var item = result.RootElement;
        Kind(item, "work_state");
        if (Id(item, "work_id") != id || Text(item, "state", 32) != state) throw Invalid();
    }

    private async Task SyncRatingAsync(string id, int rating, OnlineAuthSnapshot expected, CancellationToken cancellation)
    {
        string path = "me/ratings/" + id;
        using var original = await ReadOptionalAsync(path, expected, cancellation).ConfigureAwait(false);
        var payload = new Dictionary<string, object> { ["overall"] = rating, ["recommend"] = "neutral" };
        if (original is not null)
        {
            var root = original.RootElement;
            Kind(root, "rating");
            if (Id(root, "work_id") != id) throw Invalid();
            if (Integer(root, "overall", 10) == rating) return;
            // Whole-resource PUT. Keep the review fields the user already put on this site.
            foreach (var name in new[] { "recommend", "aspect_scores", "spoiler_level", "short_summary" })
                if (root.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null)
                    payload[name] = value;
        }
        using var result = await Request(HttpMethod.Put, path, null, payload, null, cancellation, expected).ConfigureAwait(false);
        var item = result.RootElement;
        Kind(item, "rating");
        if (Id(item, "work_id") != id || Integer(item, "overall", 10) != rating) throw Invalid();
    }

    private async Task<JsonDocument?> ReadOptionalAsync(string path, OnlineAuthSnapshot expected, CancellationToken cancellation)
    {
        try { return await Request(HttpMethod.Get, path, null, null, null, cancellation, expected).ConfigureAwait(false); }
        catch (OnlineException error) when (error.Failure == OnlineFailure.NotFound) { return null; }
    }
}

public sealed class DisabledNextMoeCollectionWriter : INextMoeCollectionWriter
{
    public Task<NextMoeCollectionResult> WriteAsync(CollectionUpdate update, OnlineAuthSnapshot expected, CancellationToken cancellation)
        => Task.FromResult(new NextMoeCollectionResult(false, false, "此环境禁用 NextMoe 写入。"));
}
