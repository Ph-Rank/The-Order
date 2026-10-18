using System.Net;
using System.Text.Json;
using TheOrder.Application;

namespace TheOrder.Infrastructure.Online;

// Official anonymous catalog metadata only. Never request/cache previews or artwork.
public sealed class AppleCatalogProvider(HttpClient http, Func<DateTimeOffset>? clock = null) : IAppleCatalogProvider
{
    private static readonly SemaphoreSlim Serial = new(1);
    private static long lastStart;
    private static DateTimeOffset blockedUntil;
    private DateTimeOffset Now => (clock ?? (() => DateTimeOffset.UtcNow))();
    public async Task<AppleCatalogResult> SearchAsync(AppleCatalogQuery query, CancellationToken cancellation)
    {
        if (query is not { IsValid: true } || http.DefaultRequestHeaders.Authorization is not null || http.DefaultRequestHeaders.Contains("Cookie")) throw Invalid();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation); timeout.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            var discarded = 0;
            async Task<AppleCatalogResult> Read(string path, string evidence)
            {
                using var json = await GetAsync(new("https://itunes.apple.com/" + path), timeout.Token).ConfigureAwait(false);
                var result = Map(query, json.RootElement, evidence, Now);
                discarded = Math.Min(100, discarded + result.Discarded);
                return result with { Discarded = discarded };
            }
            var suffix = "&entity=album&country=" + query.Country + "&limit=8";
            if (query.ExplicitAlbumId is { } id)
                return await Read("lookup?id=" + id + suffix, "来源已有 Apple 专辑编号，仍需核对版本").ConfigureAwait(false);
            if (query.Barcode is { Length: >= 8 and <= 14 } barcode && barcode.All(char.IsAsciiDigit))
            {
                var exact = await Read("lookup?upc=" + barcode + suffix, "条形码检索，仍需核对标题与音乐人").ConfigureAwait(false);
                if (exact.Candidates.Length > 0) return exact;
            }
            var term = query.Title + (query.Artist is "" or "Various Artists" ? "" : " " + query.Artist);
            return await Read("search?media=music&lang=ja_jp&term=" + Uri.EscapeDataString(term) + suffix, "标题与音乐人检索，尚未确认对应").ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested) { throw new MusicBrainzException(MusicBrainzFailure.Timeout, "iTunes 商店查询超时，专辑资料保持。"); }
        catch (HttpRequestException) { throw new MusicBrainzException(MusicBrainzFailure.Offline, "暂时无法连接 iTunes 商店，专辑资料保持。"); }
        catch (IOException) { throw new MusicBrainzException(MusicBrainzFailure.Offline, "iTunes 商店响应未完整读取，专辑资料保持。"); }
        catch (JsonException) { throw Invalid(); }
    }
    private async Task<JsonDocument> GetAsync(Uri uri, CancellationToken token)
    {
        await Serial.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var elapsed = lastStart == 0 ? TimeSpan.FromDays(1) : System.Diagnostics.Stopwatch.GetElapsedTime(lastStart);
            var delay = TimeSpan.FromSeconds(3.2) - elapsed; var backoff = blockedUntil - DateTimeOffset.UtcNow;
            if (backoff > delay) delay = backoff;
            if (delay > TimeSpan.Zero) await Task.Delay(delay, token).ConfigureAwait(false);
            lastStart = System.Diagnostics.Stopwatch.GetTimestamp();
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.UserAgent.ParseAdd(MusicBrainzProvider.UserAgent); request.Headers.Accept.ParseAdd("application/json");
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            if (response.RequestMessage?.RequestUri is { } returned && returned != uri) throw Invalid();
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                var wait = response.Headers.RetryAfter?.Delta ?? response.Headers.RetryAfter?.Date - DateTimeOffset.UtcNow ?? TimeSpan.FromMinutes(1);
                blockedUntil = DateTimeOffset.UtcNow + (wait > TimeSpan.Zero ? wait : TimeSpan.FromSeconds(3.2));
                throw new MusicBrainzException(MusicBrainzFailure.RateLimited, "iTunes 商店请求较多，请稍后重试。");
            }
            if ((int)response.StatusCode >= 500) throw new MusicBrainzException(MusicBrainzFailure.Server, "iTunes 商店暂时不可用，请稍后重试。");
            if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > 1024 * 1024) throw Invalid();
            await using var input = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
            using var buffer = new MemoryStream(); var bytes = new byte[32768];
            while (true)
            {
                var count = await input.ReadAsync(bytes.AsMemory(0, Math.Min(bytes.Length, 1024 * 1024 + 1 - (int)buffer.Length)), token).ConfigureAwait(false);
                if (count == 0) break; buffer.Write(bytes, 0, count);
                if (buffer.Length > 1024 * 1024) throw Invalid();
            }
            return JsonDocument.Parse(buffer.ToArray(), new() { MaxDepth = 24 });
        }
        finally { Serial.Release(); }
    }
    public static AppleCatalogResult Map(AppleCatalogQuery query, JsonElement root, string evidence, DateTimeOffset now)
    {
        if (query is not { IsValid: true } || root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array
            || results.GetArrayLength() > 100 || !root.TryGetProperty("resultCount", out var total) || !total.TryGetInt32(out var count) || count != results.GetArrayLength()) throw Invalid();
        var candidates = new List<AppleAlbumCandidate>(); var dropped = 0;
        foreach (var row in results.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Object) { dropped++; continue; }
            string Text(string name) => row.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
            if (Text("wrapperType") != "collection" || Text("collectionType") != "Album" || !row.TryGetProperty("collectionId", out var number) || !number.TryGetInt64(out var id)
                || !row.TryGetProperty("trackCount", out var tracks) || !tracks.TryGetInt32(out var n)) { dropped++; continue; }
            var date = DateTimeOffset.TryParse(Text("releaseDate"), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var stamp) ? stamp.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) : null;
            var value = new AppleAlbumCandidate(id.ToString(System.Globalization.CultureInfo.InvariantCulture), query.Country, Text("collectionName"), Text("artistName"), Text("collectionViewUrl"), date, n, evidence);
            var expectedCountry = query.Country switch { "JP" => "JPN", "CN" => "CHN", _ => "USA" };
            if (!value.IsValid || Text("country") != expectedCountry || query.ExplicitAlbumId is { } expected && value.Id != expected
                || !AppleCatalogResult.HasTitleEvidence(query, value)) { dropped++; continue; }
            if (candidates.All(c => c.Id != value.Id) && candidates.Count < 8) candidates.Add(value);
        }
        return new(query, candidates.ToArray(), Math.Min(100, dropped), count >= 8, now);
    }
    private static MusicBrainzException Invalid() => new(MusicBrainzFailure.InvalidResponse, "iTunes 商店返回的目录资料无法核对，未保存关联。");
}
