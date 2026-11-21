using System.Collections.Immutable;
using System.Net.Http.Headers;
using System.Text.Json;
using TheOrder.Application;

namespace TheOrder.Infrastructure.Online;

public sealed partial class NextMoePersonalApi(HttpClient client, INextMoeAccess auth, NextMoeRateCoordinator rate) : INextMoePersonalApi, INextMoeCollectionWriter
{
    public Task<CloudPage<CloudPlaytime>> PlaytimesAsync(string? cursor, CancellationToken cancellation)
        => Page("me/playtimes", cursor, null, x => { Kind(x, "playtime"); return new CloudPlaytime(Id(x, "work_id"), Integer(x, "minutes", long.MaxValue / 60)); }, cancellation);
    public Task<CloudPage<CloudWorkState>> StatesAsync(string? cursor, CancellationToken cancellation)
        => Page("me/work-states", cursor, null, x => { Kind(x, "work_state"); var state = Text(x, "state", 32);
            var completion = OptionalText(x, "completion", 32); ValidateState(state, completion); return new CloudWorkState(Id(x, "work_id"), state, completion, Text(x, "updated_at", 40)); }, cancellation);
    public Task<CloudPage<CloudRating>> RatingsAsync(string? cursor, CancellationToken cancellation)
        => Page("me/ratings", cursor, null, x => { Kind(x, "rating"); var rating = (int)Integer(x, "overall", 10);
            if (rating < 1) throw Invalid(); return new CloudRating(Id(x, "work_id"), rating, Text(x, "updated_at", 40)); }, cancellation);
    public Task<CloudPage<CloudFolder>> FoldersAsync(string? cursor, CancellationToken cancellation)
        => Page("me/folders", cursor, "folder:read", Folder, cancellation);
    public Task<CloudPage<string>> FolderItemsAsync(string folderId, string? cursor, CancellationToken cancellation)
        => Page("me/folders/" + ValidateId(folderId) + "/items", cursor, "folder:read", x =>
        { Kind(x, "folder_item"); if (Id(x, "folder_id") != folderId) throw Invalid(); return Id(x, "work_id"); }, cancellation);
    private async Task<CloudPage<T>> Page<T>(string path, string? cursor, string? scope, Func<JsonElement, T> map, CancellationToken cancellation)
    {
        if (cursor is not null && !ValidCursor(cursor)) throw new OnlineException(OnlineFailure.InvalidRequest);
        using var body = await Request(HttpMethod.Get, path + "?limit=100" + (cursor is null ? "" : "&cursor=" + Uri.EscapeDataString(cursor)), scope, null, null, cancellation).ConfigureAwait(false);
        var root = body.RootElement; Kind(root, "list");
        if (!root.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array || items.GetArrayLength() > 100) throw Invalid();
        var next = OptionalText(root, "next_cursor", 512); if (next is not null && !ValidCursor(next)) throw Invalid();
        try { return new([.. items.EnumerateArray().Select(map)], next, next is not null); }
        catch (OnlineException e) when (e.Failure == OnlineFailure.InvalidRequest) { throw Invalid(); }
    }
    public async Task<ImmutableArray<PersonalWriteResult>> WriteAsync(PersonalSyncKind kind, string? folderId, ImmutableArray<PersonalWrite> items, string idempotencyKey, CancellationToken cancellation)
    {
        if (items.Length is < 1 or > 100 || items.Select(x => x.WorkId).Distinct(StringComparer.Ordinal).Count() != items.Length
            || !Guid.TryParseExact(idempotencyKey, "D", out _)) throw new OnlineException(OnlineFailure.InvalidRequest);
        string path; string? scope = null;
        var entries = new List<Dictionary<string, object?>>();
        foreach (var item in items)
        {
            var entry = new Dictionary<string, object?> { ["work_id"] = ValidateId(item.WorkId) };
            switch (kind)
            {
                case PersonalSyncKind.Playtime:
                    if (item.Minutes is not (>= 0 and <= 60000)) throw new OnlineException(OnlineFailure.InvalidRequest);
                    entry["minutes"] = item.Minutes; break;
                case PersonalSyncKind.State:
                    ValidateState(item.State!, item.Completion); entry["state"] = item.State;
                    if (item.Completion is not null) entry["completion"] = item.Completion; break;
                case PersonalSyncKind.Folder: break;
                default: throw new OnlineException(OnlineFailure.Forbidden); // Ratings are structurally read-only.
            }
            entries.Add(entry);
        }
        path = kind switch { PersonalSyncKind.Playtime => "me/playtimes", PersonalSyncKind.State => "me/work-states",
            _ => "me/folders/" + ValidateId(folderId!) + "/items" };
        if (kind == PersonalSyncKind.Folder) scope = "folder:write";
        using var result = await Request(HttpMethod.Post, path, scope, new { items = entries }, idempotencyKey, cancellation).ConfigureAwait(false);
        var root = result.RootElement;
        if (!root.TryGetProperty("items", out var results) || results.ValueKind != JsonValueKind.Array || results.GetArrayLength() != items.Length) throw Invalid();
        var output = ImmutableArray.CreateBuilder<PersonalWriteResult>(); int index = 0;
        foreach (var row in results.EnumerateArray())
        {
            int status = (int)Integer(row, "status", 599); var requested = items[index++]; bool success = status == 200;
            if (!success && status is < 400 or > 599) throw Invalid();
            if (success)
            {
                if (Id(row, "work_id") != requested.WorkId) throw Invalid();
                Kind(row, kind switch { PersonalSyncKind.Playtime => "playtime", PersonalSyncKind.State => "work_state", _ => "folder_item" });
                if (kind == PersonalSyncKind.Playtime && Integer(row, "minutes", long.MaxValue / 60) != requested.Minutes) throw Invalid();
                if (kind == PersonalSyncKind.State && (Text(row, "state", 32) != requested.State || OptionalText(row, "completion", 32) != requested.Completion)) throw Invalid();
                if (kind == PersonalSyncKind.Folder && Id(row, "folder_id") != folderId) throw Invalid();
            }
            var error = row.TryGetProperty("problem", out var problem) ? OptionalText(problem, "code", 100) : null;
            if (!success && string.IsNullOrEmpty(error)) throw Invalid();
            output.Add(new(requested.WorkId, success, status, error));
        }
        return output.ToImmutable();
    }
    public async Task<CloudFolder> CreateFolderAsync(string name, string idempotencyKey, CancellationToken cancellation)
    {
        if (name.Length is < 1 or > 100 || !Guid.TryParseExact(idempotencyKey, "D", out _)) throw new OnlineException(OnlineFailure.InvalidRequest);
        using var response = await Request(HttpMethod.Post, "me/folders", "folder:write", new { name, visibility = "private" }, idempotencyKey, cancellation).ConfigureAwait(false);
        return Folder(response.RootElement);
    }
    public async Task DeleteFolderItemAsync(string folderId, string workId, CancellationToken cancellation)
    {
        using var response = await Request(HttpMethod.Delete, "me/folders/" + ValidateId(folderId) + "/items/" + ValidateId(workId), "folder:write", null, null, cancellation).ConfigureAwait(false);
    }
    private async Task<JsonDocument> Request(HttpMethod method, string path, string? scope, object? payload, string? idempotencyKey, CancellationToken cancellation,
        OnlineAuthSnapshot? expected = null)
    {
        for (int attempt = 0; attempt < 2; attempt++)
        {
        var snapshot = expected ?? await auth.AuthContext.CaptureAsync(cancellation).ConfigureAwait(false);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation, snapshot.Changed); timeout.CancelAfter(TimeSpan.FromSeconds(45));
        try
        {
            var token = await auth.UserTokenAsync(scope, timeout.Token).ConfigureAwait(false);
            await rate.WaitAsync(snapshot.Generation, NextMoeAccessMode.Account, timeout.Token).ConfigureAwait(false);
            snapshot.Changed.ThrowIfCancellationRequested();
            using var request = new HttpRequestMessage(method, new Uri("https://api.nextmoe.dev/v2/" + path));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Accept.Add(new("application/json"));
            if (payload is not null) request.Content = new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(payload));
            if (request.Content is not null) request.Content.Headers.ContentType = new("application/json");
            if (idempotencyKey is not null) request.Headers.Add("Idempotency-Key", idempotencyKey);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            if ((int)response.StatusCode is >= 300 and < 400) throw new OnlineException(OnlineFailure.RedirectBlocked);
            var bytes = await CatalogTransport.ReadBoundedAsync(response.Content, CatalogTransport.MaximumBytes, timeout.Token).ConfigureAwait(false);
            JsonDocument body;
            try { body = JsonDocument.Parse(bytes.Length == 0 && (int)response.StatusCode == 204 ? "{}"u8.ToArray() : bytes); }
            catch (JsonException) { throw Invalid(); }
            if (body.RootElement.ValueKind != JsonValueKind.Object) { body.Dispose(); throw Invalid(); }
            string? code;
            try { code = OptionalText(body.RootElement, "code", 100); }
            catch { body.Dispose(); throw; }
            rate.Observe(snapshot.Generation, response.Headers, code);
            if ((int)response.StatusCode == 401 && attempt == 0)
            { body.Dispose(); if (await auth.RefreshRejectedAsync(token, timeout.Token).ConfigureAwait(false)) continue; throw new OnlineException(OnlineFailure.LoginRequired); }
            if (expected.HasValue && (int)response.StatusCode == 404 && code == "ENTITY_MERGED")
            {
                using (body)
                {
                    if (Text(body.RootElement, "object", 32) != "work") throw Invalid();
                    throw new MergedWorkException(Id(body.RootElement, "current_id"));
                }
            }
            if (!response.IsSuccessStatusCode)
            {
                body.Dispose(); throw new OnlineException((int)response.StatusCode switch { 401 => OnlineFailure.LoginRequired,
                    403 => code == "SCOPE_REQUIRED" ? OnlineFailure.ScopeRequired : OnlineFailure.Forbidden, 404 => OnlineFailure.NotFound,
                    409 or 412 => OnlineFailure.Conflict, 429 => code == "QUOTA_EXCEEDED" ? OnlineFailure.QuotaExceeded : OnlineFailure.RateLimited,
                    422 => OnlineFailure.InvalidRequest, >= 500 => OnlineFailure.Server, _ => OnlineFailure.InvalidResponse });
            }
            try { return auth.AuthContext.Accept(snapshot, () => { timeout.Token.ThrowIfCancellationRequested(); return body; }); }
            catch { body.Dispose(); throw; }
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested && !snapshot.Changed.IsCancellationRequested) { throw new OnlineException(OnlineFailure.Timeout); }
        catch (HttpRequestException) { throw new OnlineException(OnlineFailure.Offline); }
        }
        throw new OnlineException(OnlineFailure.LoginRequired);
    }
    internal static bool ValidCursor(string value) => value.Length is > 4 and <= 512 && value.StartsWith("cur_", StringComparison.Ordinal)
        && value[4..].All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '~' or '-');
    private static CloudFolder Folder(JsonElement x) { Kind(x, "folder"); return new(Id(x, "id"), Text(x, "name", 100), Id(x, "owner_uid"), (int)Integer(x, "item_count", 10000), Text(x, "updated_at", 40)); }
    private static void ValidateState(string state, string? completion)
    {
        if (state is not ("wish" or "doing" or "done" or "on_hold" or "dropped") || completion is not (null or "one_route" or "main" or "all")
            || state == "wish" && completion is not null) throw new OnlineException(OnlineFailure.InvalidRequest);
    }
    private static void Kind(JsonElement x, string value) { if (Text(x, "object", 32) != value) throw Invalid(); }
    private static string ValidateId(string value) { if (value is null || value.Length is < 1 or > 20 || !value.All(char.IsAsciiDigit)) throw new OnlineException(OnlineFailure.InvalidRequest); return value; }
    private static string Id(JsonElement x, string name)
    { var value = Text(x, name, 20); if (value.Length == 0 || !value.All(char.IsAsciiDigit)) throw Invalid(); return value; }
    private static long Integer(JsonElement x, string name, long maximum)
    { if (x.ValueKind != JsonValueKind.Object || !x.TryGetProperty(name, out var field) || field.ValueKind != JsonValueKind.Number || !field.TryGetInt64(out var value) || value < 0 || value > maximum) throw Invalid(); return value; }
    private static string Text(JsonElement x, string name, int maximum) => OptionalText(x, name, maximum) ?? throw Invalid();
    private static string? OptionalText(JsonElement x, string name, int maximum)
    {
        if (x.ValueKind != JsonValueKind.Object) throw Invalid();
        if (!x.TryGetProperty(name, out var field) || field.ValueKind == JsonValueKind.Null) return null;
        if (field.ValueKind != JsonValueKind.String) throw Invalid(); var value = field.GetString()!;
        if (value.Length > maximum || value.Any(char.IsControl)) throw Invalid(); return value;
    }
    private static OnlineException Invalid() => new(OnlineFailure.InvalidResponse);
}
