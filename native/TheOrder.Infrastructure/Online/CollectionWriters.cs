using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using TheOrder.Application;

namespace TheOrder.Infrastructure.Online;

public sealed class VndbAccountProvider(HttpClient client) : IVndbAccountProvider
{
    public async Task<VndbAccount> ValidateAsync(string token, CancellationToken cancellation)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.vndb.org/kana/authinfo");
        Authorize(request, token);
        return await CollectionRequest.SendAsync(client, request, async (response, deadline) =>
        {
            CollectionRequest.Check(response.StatusCode, HttpStatusCode.OK);
            var content = response.Content;
            try
            {
                await content.LoadIntoBufferAsync(16384, deadline).ConfigureAwait(false);
                var json = await content.ReadFromJsonAsync<JsonElement>(cancellationToken: deadline).ConfigureAwait(false);
                if (json.ValueKind != JsonValueKind.Object
                    || !json.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.String
                    || !json.TryGetProperty("username", out var user) || user.ValueKind != JsonValueKind.String
                    || !json.TryGetProperty("permissions", out var permissions) || permissions.ValueKind != JsonValueKind.Array)
                    throw new CollectionSyncException(CollectionSyncFailure.InvalidResponse);
                var account = new VndbAccount(id.GetString()!, user.GetString()!);
                if (!VndbAuthorization.ValidAccount(account)) throw new CollectionSyncException(CollectionSyncFailure.InvalidResponse);
                if (!permissions.EnumerateArray().Any(p => p.ValueKind == JsonValueKind.String && p.ValueEquals("listwrite")))
                    throw new CollectionSyncException(CollectionSyncFailure.MissingPermission);
                return account;
            }
            catch (Exception error) when (error is HttpRequestException or JsonException or NotSupportedException)
            { throw new CollectionSyncException(CollectionSyncFailure.InvalidResponse); }
        }, cancellation).ConfigureAwait(false);
    }

    public async Task WriteAsync(CollectionUpdate update, string token, CancellationToken cancellation)
    {
        if (update.Site != CollectionSite.Vndb || !CollectionUpdate.ValidVndbId(update.WorkId)
            || update.Status is < 1 or > 5 || update.Rating is < 10 or > 100 || update.Status is null && update.Rating is null)
            throw new CollectionSyncException(CollectionSyncFailure.Rejected);
        var fields = new Dictionary<string, object>();
        if (update.Status is { } status)
        {
            fields.Add("labels_set", new[] { status });
            fields.Add("labels_unset", Enumerable.Range(1, 5).Where(id => id != status).ToArray());
        }
        if (update.Rating is { } score) fields.Add("vote", score);
        using var request = new HttpRequestMessage(HttpMethod.Patch, $"https://api.vndb.org/kana/ulist/{update.WorkId}")
        { Content = JsonContent.Create(fields) };
        Authorize(request, token);
        await CollectionRequest.WriteAsync(client, request, cancellation).ConfigureAwait(false);
    }

    private static void Authorize(HttpRequestMessage request, string token)
        => request.Headers.Authorization = new AuthenticationHeaderValue("Token", VndbAuthorization.NormalizeToken(token));
}

public sealed class BangumiCollectionWriter(HttpClient client, BangumiAccessContext access) : IBangumiCollectionWriter
{
    public async Task WriteAsync(CollectionUpdate update, BangumiAccessSnapshot expected, CancellationToken cancellation)
    {
        if (update.Site != CollectionSite.Bangumi || !BangumiRelationIdentityResolver.ValidSubjectId(update.WorkId)
            || update.Status is < 1 or > 5 || update.Rating is < 1 or > 10 || update.Status is null && update.Rating is null)
            throw new CollectionSyncException(CollectionSyncFailure.Rejected);
        if (!ReferenceEquals(access.Capture(), expected) || !expected.Authorized)
            throw new CollectionSyncException(CollectionSyncFailure.Changed);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellation, expected.Changed);
        var fields = new Dictionary<string, int>();
        if (update.Status is { } status) fields.Add("type", status);
        if (update.Rating is { } score) fields.Add("rate", score);
        using var request = new HttpRequestMessage(HttpMethod.Post, $"https://api.bgm.tv/v0/users/-/collections/{update.WorkId}")
        { Content = new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(fields)) };
        // Bangumi compares the Content-Type header literally.
        request.Content.Headers.ContentType = new("application/json");
        try
        {
            expected.AuthorizeCollectionWrite(request);
            // Bangumi's server returns 202 after writing. Its public schema also documents 204.
            await CollectionRequest.WriteAsync(client, request, linked.Token, HttpStatusCode.Accepted).ConfigureAwait(false);
        }
        catch (CollectionSyncException error) when (error.Failure == CollectionSyncFailure.Unauthorized)
        { access.Reject(expected); throw; }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
        { throw new CollectionSyncException(CollectionSyncFailure.Changed); }
    }
}

internal static class CollectionRequest
{
    public static async Task WriteAsync(HttpClient client, HttpRequestMessage request, CancellationToken cancellation,
        HttpStatusCode? alternateSuccess = null)
        => await SendAsync(client, request, (response, _) =>
        {
            if (response.StatusCode != alternateSuccess) Check(response.StatusCode, HttpStatusCode.NoContent);
            return Task.FromResult(true);
        }, cancellation).ConfigureAwait(false);

    public static async Task<T> SendAsync<T>(HttpClient client, HttpRequestMessage request,
        Func<HttpResponseMessage, CancellationToken, Task<T>> read, CancellationToken cancellation)
    {
        if (client.DefaultRequestHeaders.Authorization is not null || client.DefaultRequestHeaders.Contains("Cookie"))
            throw new CollectionSyncException(CollectionSyncFailure.Rejected);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        deadline.CancelAfter(TimeSpan.FromSeconds(15));
        request.Headers.UserAgent.ParseAdd(BangumiAccountProvider.UserAgent);
        request.Headers.Accept.ParseAdd("application/json");
        try
        {
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false);
            return await read(response, deadline.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
        { throw new CollectionSyncException(CollectionSyncFailure.Timeout); }
        catch (HttpRequestException) { throw new CollectionSyncException(CollectionSyncFailure.Offline); }
        catch (IOException) { throw new CollectionSyncException(CollectionSyncFailure.Offline); }
    }

    public static void Check(HttpStatusCode received, HttpStatusCode expected)
    {
        if (received == expected) return;
        throw new CollectionSyncException(received switch
        {
            HttpStatusCode.Unauthorized => CollectionSyncFailure.Unauthorized,
            HttpStatusCode.Forbidden => CollectionSyncFailure.MissingPermission,
            HttpStatusCode.TooManyRequests => CollectionSyncFailure.RateLimited,
            HttpStatusCode.UnsupportedMediaType => CollectionSyncFailure.UnsupportedMediaType,
            HttpStatusCode.NotFound or HttpStatusCode.BadRequest => CollectionSyncFailure.Rejected,
            _ => CollectionSyncFailure.InvalidResponse
        }, (int)received);
    }
}
