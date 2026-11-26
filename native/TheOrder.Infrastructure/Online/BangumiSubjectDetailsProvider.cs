using System.Globalization;
using System.Text;
using System.Text.Json;
using TheOrder.Application;

namespace TheOrder.Infrastructure.Online;

// Uses the existing anonymous PublicArtworkNetwork pool. This endpoint supplies
// display facts only; it does not validate/alter a local game's metadata identity.
public sealed class BangumiSubjectDetailsProvider(HttpClient client, Func<DateTimeOffset>? clock = null,
    Func<TimeSpan, CancellationToken, Task>? delay = null, TimeSpan? operationTimeout = null,
    BangumiAccessContext? access = null) : IBangumiSubjectDetailsProvider, IBangumiEpisodesProvider
{
    private readonly BangumiAccessContext authorization = access ?? new();
    public const int MaximumResponseBytes = 2 * 1024 * 1024;
    private readonly SemaphoreSlim queue = new(1);
    private DateTimeOffset nextRequest, rateLimitedUntil;
    private DateTimeOffset Now => (clock ?? (() => DateTimeOffset.UtcNow))();
    public Task<BangumiSubjectDetails> FetchAsync(string subjectId, BangumiRelationKind expectedKind, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        if (!BangumiRelationIdentityResolver.ValidSubjectId(subjectId) || !Enum.IsDefined(expectedKind))
            throw new InvalidDataException("Bangumi 作品身份无效。");
        return ReadCoreAsync("https://api.bgm.tv/v0/subjects/" + subjectId,
            (root, stamp, authorized) => Map(subjectId, expectedKind, root, stamp, authorized), cancellation);
    }
    public Task<BangumiEpisodePage> FetchEpisodesAsync(string subjectId, int offset, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        if (!BangumiRelationIdentityResolver.ValidSubjectId(subjectId) || offset is < 0 or > BangumiEpisodesService.MaximumOffset || offset % BangumiEpisodesService.PageSize != 0)
            throw new InvalidDataException("Bangumi 章节身份或页码无效。");
        return ReadCoreAsync("https://api.bgm.tv/v0/episodes?subject_id=" + subjectId + "&limit="
            + BangumiEpisodesService.PageSize.ToString(CultureInfo.InvariantCulture) + "&offset=" + offset.ToString(CultureInfo.InvariantCulture),
            (root, stamp, _) => MapEpisodes(subjectId, offset, root, stamp), cancellation);
    }
    private async Task<T> ReadCoreAsync<T>(string url, Func<JsonElement, DateTimeOffset, bool, T> map, CancellationToken cancellation)
    {
        var duration = operationTimeout ?? TimeSpan.FromSeconds(25);
        var lease = authorization.Capture();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation, lease.Changed);
        timeout.CancelAfter(duration); var token = timeout.Token; var deadline = Now + duration; bool entered = false;
        try
        {
            await queue.WaitAsync(token).ConfigureAwait(false); entered = true;
            if (client.DefaultRequestHeaders.Authorization is not null || client.DefaultRequestHeaders.Contains("Cookie"))
                throw Invalid("匿名资料网络配置无效，未发送请求。");
            for (var retries = 0; ; retries++)
            {
                token.ThrowIfCancellationRequested();
                if (Now >= deadline) throw Timeout();
                if (rateLimitedUntil > Now && rateLimitedUntil >= deadline)
                    throw new BangumiRelationsException(BangumiRelationsFailure.RateLimited, "Bangumi 请求过于频繁，请稍后重试。", rateLimitedUntil - Now);
                var next = nextRequest > rateLimitedUntil ? nextRequest : rateLimitedUntil;
                if (next > Now) await WaitAsync(next - Now, token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                if (Now >= deadline) throw Timeout();
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.UserAgent.ParseAdd(BangumiAccountProvider.UserAgent);
                request.Headers.Accept.ParseAdd("application/json");
                lease.Authorize(request);
                nextRequest = Now.AddSeconds(2);
                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
                var status = (int)response.StatusCode;
                if (status == 200)
                {
                    var media = response.Content.Headers.ContentType?.MediaType;
                    if (media is null || !media.Equals("application/json", StringComparison.OrdinalIgnoreCase) && !media.EndsWith("+json", StringComparison.OrdinalIgnoreCase))
                        throw Invalid("Bangumi 未返回有效的 JSON 资料，请稍后重试。");
                    var bytes = await BangumiHttpResponse.ReadBodyAsync(response.Content, MaximumResponseBytes, TooLarge, token).ConfigureAwait(false);
                    token.ThrowIfCancellationRequested();
                    try
                    {
                        _ = new UTF8Encoding(false, true).GetCharCount(bytes);
                        using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 32 });
                        var result = map(document.RootElement, Now, lease.Authorized);
                        token.ThrowIfCancellationRequested();
                        if (Now >= deadline) throw Timeout();
                        return result;
                    }
                    catch (Exception e) when (e is JsonException or DecoderFallbackException)
                    { throw Invalid("Bangumi 返回的 JSON 资料损坏，请稍后重试。"); }
                }
                if (status == 401 && lease.Authorized)
                {
                    authorization.Reject(lease);
                    throw new BangumiRelationsException(BangumiRelationsFailure.Unauthorized, "番组计划授权已失效，请在设置中重新授权。");
                }
                if (status is 401 or 403 or 404)
                    throw new BangumiRelationsException(BangumiRelationsFailure.Unavailable, lease.Authorized
                        ? "番组计划未返回该条目，请在原站核对编号或访问权限。"
                        : "暂时无法匿名读取。可在设置中授权番组计划后重试，或到原站核对条目。");
                var retryAfter = BangumiHttpResponse.ReadRetryAfter(response, Now);
                var failure = status == 429 ? BangumiRelationsFailure.RateLimited : status >= 500 ? BangumiRelationsFailure.Server : BangumiRelationsFailure.InvalidResponse;
                var message = status == 429 ? "Bangumi 请求过于频繁，请稍后重试。"
                    : status >= 500 ? "Bangumi 服务暂时不可用，请稍后重试。" : "Bangumi 返回了无法读取的响应，请稍后重试。";
                var wait = TimeSpan.FromMilliseconds(500 * Math.Pow(2, retries));
                if (retryAfter > wait) wait = retryAfter.Value;
                if (status == 429) rateLimitedUntil = Now + (retryAfter ?? TimeSpan.FromSeconds(2));
                if (status != 429 && status is not (>= 500 and <= 599) || retries >= 2 || wait >= deadline - Now)
                    throw new BangumiRelationsException(failure, message, retryAfter);
                await WaitAsync(wait, token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested && !lease.Changed.IsCancellationRequested) { throw Timeout(); }
        catch (HttpRequestException) { throw new BangumiRelationsException(BangumiRelationsFailure.Offline, "无法连接 Bangumi，请检查网络后重试。"); }
        catch (IOException) { throw Invalid("Bangumi 响应未完整读取，请稍后重试。"); }
        finally { if (entered) queue.Release(); }
    }
    private Task WaitAsync(TimeSpan duration, CancellationToken cancellation) => (delay ?? Task.Delay)(duration, cancellation);
    public static BangumiSubjectDetails Map(string subjectId, BangumiRelationKind expectedKind, JsonElement root, DateTimeOffset fetchedAtUtc, bool authorized = false)
    {
        if (!BangumiRelationIdentityResolver.ValidSubjectId(subjectId) || !Enum.IsDefined(expectedKind)
            || root.ValueKind != JsonValueKind.Object || Integer(root, "id")?.ToString(CultureInfo.InvariantCulture) != subjectId
            || Integer(root, "type") != (int)expectedKind) throw Invalid("Bangumi 返回的作品编号或媒体类型与关联卡片不一致。");
        // The official Subject schema requires nsfw. Absence is not evidence of public content.
        if (!root.TryGetProperty("nsfw", out var nsfw) || nsfw.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) throw Invalid();
        if (nsfw.ValueKind == JsonValueKind.True && !authorized)
            throw new BangumiRelationsException(BangumiRelationsFailure.Unavailable, "该作品不在本次匿名公开读取范围内。可在原站查看。");
        var original = Text(root, "name", 2000, required: true);
        var chinese = Text(root, "name_cn", 2000, required: true);
        var summary = Text(root, "summary", BangumiSubjectDetailsService.MaximumSummaryLength, required: true, multiline: true);
        if (original.Length == 0 && chinese.Length == 0) throw Invalid();
        string? cover = null;
        if (root.TryGetProperty("images", out var images) && images.ValueKind == JsonValueKind.Object)
            foreach (var size in new[] { "large", "common", "medium", "small", "grid" })
                if (images.TryGetProperty(size, out var image) && image.ValueKind == JsonValueKind.String && BangumiRelationsService.ValidCoverUrl(image.GetString()))
                { cover = image.GetString(); break; }
        var date = Text(root, "date", 100);
        if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)) date = "";
        var platform = Text(root, "platform", 200);
        var eps = Integer(root, "eps"); if (eps is not (> 0 and <= 1000000)) eps = null;
        double? score = null; int? votes = null;
        if (root.TryGetProperty("rating", out var rating) && rating.ValueKind == JsonValueKind.Object
            && Integer(rating, "total") is > 0 and var count && rating.TryGetProperty("score", out var scoreValue)
            && scoreValue.ValueKind == JsonValueKind.Number && scoreValue.TryGetDouble(out var number) && double.IsFinite(number) && number is >= 0 and <= 10)
        { score = number; votes = count; }
        var identityLinks = AniListBridgeLinks.MapEvidence(root, expectedKind);
        var result = new BangumiSubjectDetails(subjectId, expectedKind, chinese.Length > 0 ? chinese : original, original, cover,
            summary, date.Length == 0 ? null : date, platform.Length == 0 ? null : platform, eps, score, votes, fetchedAtUtc)
        { MusicFacts = expectedKind == BangumiRelationKind.Music ? BangumiMusicFactsMapping.Map(root) : null,
            BookFacts = expectedKind == BangumiRelationKind.Book ? BangumiBookFactsMapping.Map(root) : null,
            ExternalLinks = identityLinks.Links, HasAmbiguousExternalIdentity = identityLinks.Ambiguous,
            ChineseTitle = chinese.Length > 0 ? chinese : null };
        if (!BangumiSubjectDetailsService.Valid(subjectId, expectedKind, result)) throw Invalid();
        return result;
    }
    public static BangumiEpisodePage MapEpisodes(string subjectId, int offset, JsonElement root, DateTimeOffset fetchedAtUtc)
    {
        if (!BangumiRelationIdentityResolver.ValidSubjectId(subjectId) || offset is < 0 or > BangumiEpisodesService.MaximumOffset
            || root.ValueKind != JsonValueKind.Object || Integer(root, "offset") != offset
            || Integer(root, "limit") != BangumiEpisodesService.PageSize || Integer(root, "total") is not (>= 0 and <= BangumiEpisodesService.MaximumTotal)
            || !root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array
            || data.GetArrayLength() > BangumiEpisodesService.PageSize) throw Invalid("Bangumi 章节分页资料无效。");
        var rows = new List<BangumiEpisode>();
        foreach (var row in data.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Object || Integer(row, "id") is not > 0
                || Integer(row, "subject_id")?.ToString(CultureInfo.InvariantCulture) != subjectId
                || Integer(row, "type") is not (>= 0 and <= 6) || !row.TryGetProperty("sort", out var sort)
                || sort.ValueKind != JsonValueKind.Number || !sort.TryGetDecimal(out var number))
                throw Invalid("Bangumi 返回了其他作品或无效的章节资料。");
            var original = Text(row, "name", 2000, required: true);
            var chinese = Text(row, "name_cn", 2000, required: true);
            var airdate = Text(row, "airdate", 100, required: true);
            rows.Add(new(Integer(row, "id")!.Value, subjectId, Integer(row, "type")!.Value, number,
                chinese.Length > 0 ? chinese : original, original, airdate.Length == 0 ? null : airdate,
                Text(row, "duration", 200, required: true), Text(row, "desc", 32768, required: true, multiline: true)));
        }
        var page = new BangumiEpisodePage(subjectId, offset, Integer(root, "total")!.Value, rows.ToArray(), fetchedAtUtc);
        if (!BangumiEpisodesService.Valid(subjectId, offset, page)) throw Invalid("Bangumi 章节身份或分页资料不一致。");
        return page;
    }
    private static int? Integer(JsonElement root, string key) => root.TryGetProperty(key, out var value)
        && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) ? number : null;
    private static string Text(JsonElement root, string key, int limit, bool required = false, bool multiline = false)
    {
        if (!root.TryGetProperty(key, out var value) || value.ValueKind == JsonValueKind.Null)
            return required ? throw Invalid() : "";
        if (value.ValueKind != JsonValueKind.String) throw Invalid();
        var text = value.GetString()!;
        if (text.Length > limit || text.Any(c => char.IsControl(c) && !(multiline && c is '\n' or '\r' or '\t'))) throw Invalid();
        return text.Trim();
    }
    private static BangumiRelationsException Invalid(string message = "Bangumi 返回的作品资料格式无效，请稍后重试。") => new(BangumiRelationsFailure.InvalidResponse, message);
    private static BangumiRelationsException Timeout() => new(BangumiRelationsFailure.Timeout, "Bangumi 作品读取超时，请稍后重试。");
    private static BangumiRelationsException TooLarge() => new(BangumiRelationsFailure.ResponseTooLarge, "Bangumi 作品响应超过大小限制，未显示资料。");
}
