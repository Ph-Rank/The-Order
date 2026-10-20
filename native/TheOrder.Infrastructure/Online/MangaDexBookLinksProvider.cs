using System.Globalization;
using System.Text;
using System.Text.Json;
using TheOrder.Application;

namespace TheOrder.Infrastructure.Online;

// Bounded public metadata only. No MangaDex reader/feed/image endpoints, arbitrary
// URLs, login, proxy, reverse index or title-only identity selection.
public sealed class MangaDexBookLinksProvider(HttpClient client, AniListRateCoordinator? scheduler = null,
    Func<DateTimeOffset>? clock = null, TimeSpan? operationTimeout = null) : IBookOutletSupplementProvider
{
    private static readonly AniListRateCoordinator Shared = new();
    public async Task<BookLinkSupplement> FetchAsync(AniListMedia source, CancellationToken cancellation)
    {
        if (!AniListValidation.Media(source) || source.Kind != AniListMediaKind.Manga || source.Format is not ("MANGA" or "ONE_SHOT")) throw new InvalidDataException("仅查询已定位漫画的补充入口。");
        if (client.DefaultRequestHeaders.Authorization is not null || client.DefaultRequestHeaders.Contains("Cookie")) throw Invalid();
        var title = source.NativeTitle.Length > 0 ? source.NativeTitle : source.Title;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(operationTimeout ?? TimeSpan.FromSeconds(20)); var token = timeout.Token;
        var rate = scheduler ?? Shared;
        try
        {
            using var lease = await rate.EnterAsync(token).ConfigureAwait(false);
            var uri = new Uri("https://api.mangadex.org/manga?limit=10&order%5Brelevance%5D=desc&title=" + Uri.EscapeDataString(title));
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.UserAgent.ParseAdd("TheOrder/1.2.0 (https://github.com/Ph-Rank/The-Order)"); request.Headers.Accept.ParseAdd("application/json");
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            if (response.RequestMessage?.RequestUri is { } actual && actual != uri) throw Invalid();
            var retry = response.Headers.RetryAfter?.Delta ?? response.Headers.RetryAfter?.Date - (clock ?? (() => DateTimeOffset.UtcNow))();
            if ((int)response.StatusCode == 429)
            {
                rate.Observe(null, 0, null, retry ?? TimeSpan.FromMinutes(1));
                throw new AniListException(AniListFailure.RateLimited, "MangaDex 正在限流，已有入口仍可使用。");
            }
            if ((int)response.StatusCode != 200) throw new AniListException((int)response.StatusCode is >= 500 or 403 ? AniListFailure.Server : AniListFailure.InvalidResponse, "MangaDex 暂时无法读取，已有入口仍可使用。");
            if (response.Content.Headers.ContentType?.MediaType != "application/json") throw Invalid();
            var bytes = await CatalogTransport.ReadBoundedAsync(response.Content, 2 * 1024 * 1024, token).ConfigureAwait(false);
            _ = new UTF8Encoding(false, true).GetCharCount(bytes);
            using var json = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 32 });
            token.ThrowIfCancellationRequested();
            return Map(json.RootElement, source, title, (clock ?? (() => DateTimeOffset.UtcNow))());
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested) { throw new AniListException(AniListFailure.Timeout, "MangaDex 读取超时，已有入口仍可使用。"); }
        catch (HttpRequestException) { throw new AniListException(AniListFailure.Offline, "MangaDex 连接失败，已有入口仍可使用。"); }
        catch (Exception e) when (e is JsonException or DecoderFallbackException or IOException or OnlineException) { throw Invalid(); }
    }
    public static BookLinkSupplement Map(JsonElement root, AniListMedia source, string query, DateTimeOffset stamp)
    {
        if (!AniListValidation.Media(source) || source.Kind != AniListMediaKind.Manga || source.Format is not ("MANGA" or "ONE_SHOT")) throw new InvalidDataException();
        BookLinkSupplement Empty(string? notice = null) => new(source.Id, source.Format, query, null, null, null, [], stamp, notice);
        if (Str(root, "result") != "ok" || !root.TryGetProperty("data", out var rows) || rows.ValueKind != JsonValueKind.Array
            || rows.GetArrayLength() > 10 || Number(root, "offset") != 0 || Number(root, "total") is not { } total || total < rows.GetArrayLength()) throw Invalid();
        // Truncated searches can never prove a unique cross-reference. Do not
        // fetch successive pages or auto-adopt the first high-scoring title.
        if (total != rows.GetArrayLength()) return Empty("补充来源候选未完整读取，未采用其中的入口。");
        var id = source.Id.ToString(CultureInfo.InvariantCulture);
        var matches = rows.EnumerateArray().Where(row => Str(Node(Node(row, "attributes"), "links"), "al") == id).ToArray();
        if (matches.Length == 0) return Empty();
        if (matches.Length != 1) return Empty("补充来源的跨站编号有冲突，未采用其中的入口。");
        var selected = matches[0]; var mdId = Str(selected, "id"); var attrs = Node(selected, "attributes");
        if (Str(selected, "type") != "manga" || !new BookIdentity("MangaDex", mdId, BookEntityLevel.Series).IsValid) throw Invalid();
        var titles = Values(Node(attrs, "title")).Concat(Rows(attrs, "altTitles").SelectMany(Values)).Take(160).ToArray();
        static string Normalize(string text) => string.Concat(text.Normalize(NormalizationForm.FormKC).Where(char.IsLetterOrDigit)).ToUpperInvariant();
        var sourceNames = new[] { source.Title, source.NativeTitle, source.EnglishTitle }.Concat(source.Synonyms).Where(x => x.Length > 0).Select(Normalize).ToHashSet(StringComparer.Ordinal);
        if (!titles.Any(t => sourceNames.Contains(Normalize(t)))) return Empty("补充来源编号相同但标题有差异，未采用其中的入口。");
        var links = Node(attrs, "links"); var malText = Str(links, "mal"); int? mal = null;
        if (malText.Length > 0)
        {
            if (!int.TryParse(malText, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) || parsed <= 0 || parsed.ToString(CultureInfo.InvariantCulture) != malText) return Empty("补充来源编号格式有歧义，未采用其中的入口。");
            mal = parsed;
            if (source.MalId is { } expected && expected != mal) return Empty("补充来源的外部编号互相冲突，未采用其中的入口。");
        }
        var output = new List<BookExternalLink>();
        foreach (var (values, prefix) in new[] { (links, ""), (Node(attrs, "officialLinks"), "officialLinks:") })
        {
            if (output.Count >= 32) break;
            if (values.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null) continue;
            if (values.ValueKind != JsonValueKind.Object || values.EnumerateObject().Count() > 32) throw Invalid();
            foreach (var property in values.EnumerateObject())
            {
                // Retain supplied full URLs only. In particular bw:series/...
                // and identifier-only catalog keys are never expanded/guessed.
                if (property.Value.ValueKind != JsonValueKind.String) continue;
                var url = property.Value.GetString(); if (url?.StartsWith("https://", StringComparison.Ordinal) != true && url?.StartsWith("http://", StringComparison.Ordinal) != true) continue;
                var raw = new BookExternalLink(output.Count + 1, "MangaDex · " + property.Name, url, prefix + property.Name);
                if (BookOutlets.ValidRaw(raw)) output.Add(raw); if (output.Count == 32) break;
            }
        }
        var result = new BookLinkSupplement(source.Id, source.Format, query, mdId, titles.FirstOrDefault(t => AniListValidation.Text(t, 2000, false)), mal, output.Take(32).ToArray(), stamp, null);
        if (!result.IsValid) throw Invalid(); return result;
    }
    private static JsonElement Node(JsonElement row, string key) => row.ValueKind == JsonValueKind.Object && row.TryGetProperty(key, out var value) ? value : default;
    private static string Str(JsonElement row, string key) => Node(row, key) is { ValueKind: JsonValueKind.String } v ? v.GetString()! : "";
    private static int? Number(JsonElement row, string key) => Node(row, key) is { ValueKind: JsonValueKind.Number } v && v.TryGetInt32(out var n) ? n : null;
    private static IEnumerable<string> Values(JsonElement node) => node.ValueKind == JsonValueKind.Object ? node.EnumerateObject().Where(p => p.Value.ValueKind == JsonValueKind.String).Select(p => p.Value.GetString()!).Where(x => x.Length <= 2000) : [];
    private static IEnumerable<JsonElement> Rows(JsonElement row, string key) => Node(row, key) is { ValueKind: JsonValueKind.Array } v ? v.EnumerateArray().Take(128) : [];
    private static AniListException Invalid() => new(AniListFailure.InvalidResponse, "MangaDex 补充入口格式无法确认，未采用新资料。");
}
