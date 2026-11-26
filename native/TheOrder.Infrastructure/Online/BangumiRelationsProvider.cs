using System.Collections.Immutable;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using TheOrder.Application;

namespace TheOrder.Infrastructure.Online;

// Public reads, with optional independent Bangumi bearer authorization. The supplied
// network pool disables cookies/redirects; credentials are applied per exact request.
public sealed partial class BangumiRelationsProvider(HttpClient client, Func<DateTimeOffset>? clock = null,
    Func<TimeSpan, CancellationToken, Task>? delay = null, TimeSpan? operationTimeout = null,
    BangumiAccessContext? access = null) : IBangumiRelationsProvider, IRelatedSemanticReader, IBangumiBookRelationsProvider
{
    private readonly BangumiAccessContext authorization = access ?? new();
    public const int MaximumResponseBytes = 2 * 1024 * 1024, MaximumRows = 1000;
    private readonly SemaphoreSlim queue = new(1);
    private DateTimeOffset nextRequest, rateLimitedUntil;
    private DateTimeOffset Now => (clock ?? (() => DateTimeOffset.UtcNow))();
    private sealed class Budget(DateTimeOffset deadline, CancellationToken token, BangumiAccessSnapshot access)
    {
        public DateTimeOffset Deadline { get; } = deadline;
        public CancellationToken Token { get; } = token;
        public BangumiAccessSnapshot Access { get; } = access;
        public int Retries;
    }
    public Task<BangumiRelationsSnapshot> FetchAsync(string subjectId, CancellationToken cancellation)
        => FetchCoreAsync(subjectId, BangumiRelationKind.Game, cancellation);
    public Task<BangumiRelationsSnapshot> FetchBookAsync(string subjectId, CancellationToken cancellation)
        => FetchCoreAsync(subjectId, BangumiRelationKind.Book, cancellation);
    private async Task<BangumiRelationsSnapshot> FetchCoreAsync(string subjectId, BangumiRelationKind kind, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        if (!BangumiRelationIdentityResolver.ValidSubjectId(subjectId)) throw new InvalidDataException("Bangumi 编号无效。");
        var duration = operationTimeout ?? TimeSpan.FromSeconds(25);
        var lease = authorization.Capture();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation, lease.Changed);
        timeout.CancelAfter(duration); var budget = new Budget(Now + duration, timeout.Token, lease); bool entered = false;
        try
        {
            // Queueing, pacing, both requests, body reads and all retries share this budget.
            await queue.WaitAsync(budget.Token).ConfigureAwait(false); entered = true;
            if (client.DefaultRequestHeaders.Authorization is not null || client.DefaultRequestHeaders.Contains("Cookie"))
                throw Invalid("匿名资料网络配置无效，未发送请求。");
            using var source = await GetAsync(subjectId, false, budget).ConfigureAwait(false);
            if (kind == BangumiRelationKind.Game) ValidateSource(subjectId, source.RootElement, lease.Authorized);
            else _ = BangumiSubjectDetailsProvider.Map(subjectId, kind, source.RootElement, Now, lease.Authorized);
            budget.Token.ThrowIfCancellationRequested();
            using var relations = await GetAsync(subjectId, true, budget).ConfigureAwait(false);
            var result = MapRelations(subjectId, relations.RootElement, Now);
            budget.Token.ThrowIfCancellationRequested();
            if (Now >= budget.Deadline) throw Timeout();
            return result;
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested && !lease.Changed.IsCancellationRequested) { throw Timeout(); }
        catch (HttpRequestException) { throw new BangumiRelationsException(BangumiRelationsFailure.Offline, "无法连接 Bangumi，请检查网络后重试。"); }
        catch (IOException) { throw Invalid("Bangumi 响应未完整读取，请稍后重试。"); }
        finally { if (entered) queue.Release(); }
    }
    private async Task<JsonDocument> GetAsync(string subjectId, bool relations, Budget budget)
    {
        while (true)
        {
            budget.Token.ThrowIfCancellationRequested();
            if (Now >= budget.Deadline) throw Timeout();
            if (rateLimitedUntil > Now && rateLimitedUntil >= budget.Deadline)
                throw new BangumiRelationsException(BangumiRelationsFailure.RateLimited, "Bangumi 请求过于频繁，请稍后重试。", rateLimitedUntil - Now);
            var next = nextRequest > rateLimitedUntil ? nextRequest : rateLimitedUntil;
            if (next > Now) await WaitAsync(next - Now, budget.Token).ConfigureAwait(false);
            budget.Token.ThrowIfCancellationRequested();
            if (Now >= budget.Deadline) throw Timeout();
            using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.bgm.tv/v0/subjects/" + subjectId + (relations ? "/subjects" : ""));
            request.Headers.UserAgent.ParseAdd(BangumiAccountProvider.UserAgent);
            request.Headers.Accept.ParseAdd("application/json");
            budget.Access.Authorize(request);
            nextRequest = Now.AddSeconds(2);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, budget.Token).ConfigureAwait(false);
            int status = (int)response.StatusCode;
            if (response.IsSuccessStatusCode)
            {
                if (status != 200) throw Invalid("Bangumi 返回了无法识别的关联响应，请稍后重试。");
                var media = response.Content.Headers.ContentType?.MediaType;
                if (media is null || !media.Equals("application/json", StringComparison.OrdinalIgnoreCase) && !media.EndsWith("+json", StringComparison.OrdinalIgnoreCase))
                    throw Invalid("Bangumi 未返回有效的 JSON 资料，请稍后重试。");
                var bytes = await BangumiHttpResponse.ReadBodyAsync(response.Content, MaximumResponseBytes, TooLarge, budget.Token).ConfigureAwait(false);
                budget.Token.ThrowIfCancellationRequested();
                try
                {
                    _ = new UTF8Encoding(false, true).GetCharCount(bytes);
                    return JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 32 });
                }
                catch (Exception e) when (e is JsonException or DecoderFallbackException)
                { throw Invalid("Bangumi 返回的 JSON 资料损坏，请稍后重试。"); }
            }
            if (status == 401 && budget.Access.Authorized)
            {
                authorization.Reject(budget.Access);
                throw new BangumiRelationsException(BangumiRelationsFailure.Unauthorized, "番组计划授权已失效，请在设置中重新授权。");
            }
            if (status is 401 or 403 or 404)
                throw new BangumiRelationsException(BangumiRelationsFailure.Unavailable, budget.Access.Authorized
                    ? "番组计划未返回该条目，请在原站核对编号或访问权限。"
                    : "暂时无法匿名读取。可在设置中授权番组计划后重试，或到原站核对条目。");
            var retryAfter = BangumiHttpResponse.ReadRetryAfter(response, Now);
            bool retryable = status == 429 || status is >= 500 and <= 599;
            var failure = status == 429 ? BangumiRelationsFailure.RateLimited : status >= 500 ? BangumiRelationsFailure.Server : BangumiRelationsFailure.InvalidResponse;
            var message = status == 429 ? "Bangumi 请求过于频繁，请稍后重试。"
                : status >= 500 ? "Bangumi 服务暂时不可用，请稍后重试。" : "Bangumi 返回了无法读取的响应（HTTP " + status + "），请稍后重试。";
            var wait = TimeSpan.FromMilliseconds(500 * Math.Pow(2, budget.Retries));
            if (retryAfter > wait) wait = retryAfter.Value;
            if (status == 429) rateLimitedUntil = Now + (retryAfter ?? TimeSpan.FromSeconds(2));
            if (!retryable || budget.Retries >= 2 || wait >= budget.Deadline - Now)
                throw new BangumiRelationsException(failure, message, retryAfter);
            budget.Retries++;
            await WaitAsync(wait, budget.Token).ConfigureAwait(false);
        }
    }
    private Task WaitAsync(TimeSpan duration, CancellationToken cancellation) => (delay ?? Task.Delay)(duration, cancellation);
    public static void ValidateSource(string subjectId, JsonElement root, bool authorized = false)
    {
        if (root.ValueKind != JsonValueKind.Object || Id(root) != subjectId || Integer(root, "type") != 4)
            throw Invalid("Bangumi 返回的源编号不是指定的游戏，未读取关联作品。");
        if (root.TryGetProperty("nsfw", out var nsfw) && nsfw.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) throw Invalid();
        if (nsfw.ValueKind == JsonValueKind.True && !authorized)
            throw new BangumiRelationsException(BangumiRelationsFailure.Unavailable, "该 Bangumi 条目不在本次匿名公开读取范围内。");
    }
    public static BangumiRelationsSnapshot MapRelations(string subjectId, JsonElement root, DateTimeOffset fetchedAtUtc)
    {
        if (!BangumiRelationIdentityResolver.ValidSubjectId(subjectId) || root.ValueKind != JsonValueKind.Array || root.GetArrayLength() > MaximumRows) throw Invalid();
        var items = ImmutableArray.CreateBuilder<BangumiRelationItem>();
        var seen = new Dictionary<string, int>(StringComparer.Ordinal); int unsupported = 0; bool truncated = false;
        foreach (var row in root.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Object) throw Invalid();
            var id = Id(row); var type = Integer(row, "type");
            var original = Text(row, "name", 2000); var chinese = Text(row, "name_cn", 2000); var relation = Text(row, "relation", 200);
            if (type is < 1 or > 100 || original.Length == 0 && chinese.Length == 0) throw Invalid();
            if (seen.TryGetValue(id, out var prior)) { if (prior != type) throw Invalid(); continue; }
            seen.Add(id, type);
            if (id == subjectId) continue;
            if (type is not (1 or 2 or 3 or 4)) { unsupported++; continue; }
            if (items.Count == BangumiRelationsService.MaxItems) { truncated = true; continue; }
            string? cover = null;
            if (row.TryGetProperty("images", out var images) && images.ValueKind == JsonValueKind.Object)
            {
                foreach (var size in new[] { "medium", "common", "small", "large", "grid" })
                    if (images.TryGetProperty(size, out var image) && image.ValueKind == JsonValueKind.String && BangumiRelationsService.ValidCoverUrl(image.GetString()))
                    { cover = image.GetString(); break; }
            }
            items.Add(new(id, (BangumiRelationKind)type, chinese.Length > 0 ? chinese : original, original, relation, cover));
        }
        return new(subjectId, fetchedAtUtc, items.ToImmutable()) { UnsupportedCount = unsupported, Truncated = truncated };
    }
    private static string Id(JsonElement root)
    {
        if (!root.TryGetProperty("id", out var value) || value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out var number)) throw Invalid();
        var id = number.ToString(CultureInfo.InvariantCulture);
        return BangumiRelationIdentityResolver.ValidSubjectId(id) ? id : throw Invalid();
    }
    private static int Integer(JsonElement root, string key) => root.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.Number
        && value.TryGetInt32(out var number) ? number : throw Invalid();
    private static string Text(JsonElement root, string key, int maximum)
    {
        if (!root.TryGetProperty(key, out var value) || value.ValueKind != JsonValueKind.String) throw Invalid();
        var text = value.GetString()!;
        if (text.Length > maximum || text.Any(char.IsControl)) throw Invalid();
        return text.Trim();
    }
    private static BangumiRelationsException Invalid(string message = "Bangumi 返回的关联资料格式无效，请稍后重试。") => new(BangumiRelationsFailure.InvalidResponse, message);
    private static BangumiRelationsException Timeout() => new(BangumiRelationsFailure.Timeout, "Bangumi 关联读取超时，请稍后重试。");
    private static BangumiRelationsException TooLarge() => new(BangumiRelationsFailure.ResponseTooLarge, "Bangumi 关联响应超过大小限制，未更新资料。");
}
