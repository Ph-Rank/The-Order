using System.Globalization;
using System.Net;
using System.Text.Json;
using TheOrder.Application;

namespace TheOrder.Infrastructure.Online;

// Anonymous, fixed-origin public provider. Never uses the authenticated NextMoe transport.
public sealed partial class MusicBrainzProvider(HttpClient http, MusicBrainzRateCoordinator? coordinator = null,
    TimeSpan? operationTimeout = null, Func<DateTimeOffset>? clock = null) : IMusicBrainzProvider, IMusicBrainzEntityReader
{
    public static readonly string UserAgent = $"TheOrder/{AppReleaseIdentity.Version} (https://github.com/Ph-Rank/The-Order)";
    public const int MaximumResponseBytes = 8 * 1024 * 1024;
    private MusicBrainzRateCoordinator Rate => coordinator ?? MusicBrainzRateCoordinator.Shared;
    private DateTimeOffset Now => (clock ?? (() => DateTimeOffset.UtcNow))();
    private const string AlbumIncludes = "recordings+artist-credits+labels+release-groups+artist-rels+recording-level-rels+work-rels+work-level-rels+url-rels";

    public async Task<MusicBrainzSearchResult> SearchAsync(MusicAlbumSource source, CancellationToken cancellation)
    {
        if (!source.IsValid) throw new InvalidDataException("音乐匹配依据无效。");
        return await Operation(async token =>
        {
            var release = source.Facts.ExternalIds.Where(x => x.Kind == MusicBrainzEntityKind.Release).Distinct().ToArray();
            if (release.Length == 1)
            {
                try
                {
                    using var data = await GetJsonAsync("release/" + release[0].Id + "?fmt=json&inc=labels%2Bartist-credits%2Brelease-groups", token).ConfigureAwait(false);
                    var candidate = MapCandidate(data.RootElement);
                    if (candidate.Id != release[0].Id) throw Invalid();
                    return new MusicBrainzSearchResult([candidate], 1, false, candidate.Id, null);
                }
                catch (MusicBrainzException e) when (e.Failure == MusicBrainzFailure.Unavailable) { }
            }
            var groups = source.Facts.ExternalIds.Where(x => x.Kind == MusicBrainzEntityKind.ReleaseGroup).Distinct().ToArray();
            if (groups.Length == 1)
            {
                return await BrowseEditions(groups[0].Id, token).ConfigureAwait(false);
            }
            var catalogues = MusicBrainzMatching.Catalogues(source.Facts.CatalogNumbers).Take(8).ToArray();
            if (catalogues.Length > 0 || source.Facts.Barcode is not null)
            {
                var query = source.Facts.Barcode is { } code ? "barcode:" + Quote(code)
                    : string.Join(" OR ", catalogues.Select(x => "catno:" + Quote(x)));
                using var data = await SearchQuery(query, token).ConfigureAwait(false);
                var result = MapSearch(data.RootElement);
                if (result.Candidates.Length > 0) return result;
            }
            MusicBrainzSearchResult? best = null;
            foreach (var query in MusicBrainzSearchQueries.Retrieval(source))
            {
                using var titles = await SearchQuery(query, token).ConfigureAwait(false);
                var found = MapSearch(titles.RootElement);
                if (found.Candidates.Length == 0) continue;
                found = found with { Candidates = found.Candidates.OrderByDescending(x => MusicBrainzSearchQueries.EvidenceOrder(source, x)).ThenBy(x => x.Id, StringComparer.Ordinal).ToArray() };
                if (best is null || MusicBrainzSearchQueries.EvidenceOrder(source, found.Candidates[0]).CompareTo(MusicBrainzSearchQueries.EvidenceOrder(source, best.Candidates[0])) > 0) best = found;
                // A truncated/contradictory hit is not a useful stopping condition:
                // continue the bounded plan instead of repeating the old first-hit bug.
                if (!found.Truncated && found.Candidates.Any(x => !MusicBrainzMatching.Assess(source, x).HasConflict
                    && (source.Facts.Artists.Length == 0 || x.Artist.Length == 0
                        || MusicBrainzSearchQueries.EvidenceOrder(source, x).Artist || MusicBrainzMatching.Assess(source, x).Strong)))
                    return found with { Notice = "已通过来源标题、别名或标题片段与发行信息找到候选，请核对后确认。" };
            }
            return (best ?? new MusicBrainzSearchResult([], 0, false, null, null)) with
            { Notice = source.Facts.ExternalIds.Any(x => x.Kind == MusicBrainzEntityKind.Recording)
                ? "来源的编号属于单曲录音，不能直接绑定专辑。请确认发行版。" : "请核对目录编号、日期和曲目后选择发行版。" };
        }, cancellation).ConfigureAwait(false);
    }
    public Task<MusicBrainzSearchResult> EditionsAsync(string releaseGroupId, CancellationToken cancellation)
    {
        if (!MusicBrainzReference.ValidId(releaseGroupId)) throw new InvalidDataException("MusicBrainz 专辑组编号无效。");
        return Operation(token => BrowseEditions(releaseGroupId, token), cancellation);
    }
    private async Task<MusicBrainzSearchResult> BrowseEditions(string releaseGroupId, CancellationToken token)
    {
        using var data = await GetJsonAsync("release?fmt=json&limit=25&release-group=" + releaseGroupId + "&inc=labels%2Bartist-credits%2Brelease-groups", token).ConfigureAwait(false);
        var result = MapSearch(data.RootElement, browse: true);
        if (result.Candidates.Any(x => x.ReleaseGroupId != releaseGroupId)) throw Invalid();
        return result with { Notice = "请选择此专辑的具体发行版。" };
    }
    public async Task<MusicBrainzAlbum> FetchAlbumAsync(string releaseId, CancellationToken cancellation)
    {
        if (!MusicBrainzReference.ValidId(releaseId)) throw new InvalidDataException("MusicBrainz 发行版编号无效。");
        return await Operation(async token =>
        {
            using var data = await GetJsonAsync("release/" + releaseId + "?fmt=json&inc=" + Uri.EscapeDataString(AlbumIncludes), token).ConfigureAwait(false);
            var album = MapAlbum(releaseId, data.RootElement, Now);
            try { album = album with { CoverUrl = await ReadCoverAsync(releaseId, token).ConfigureAwait(false) }; }
            catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
            { album = album with { CoverWarning = "封面读取超时，已沿用 Bangumi 封面。" }; }
            catch (Exception e) when (e is MusicBrainzException or HttpRequestException or IOException or JsonException)
            { album = album with { CoverWarning = "专辑封面暂时不可用，已沿用 Bangumi 封面。" }; }
            return album;
        }, cancellation).ConfigureAwait(false);
    }
    private Task<JsonDocument> SearchQuery(string query, CancellationToken token)
        => GetJsonAsync("release/?fmt=json&limit=25&query=" + Uri.EscapeDataString(query), token);
    private static string Quote(string value) => "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    private async Task<T> Operation<T>(Func<CancellationToken, Task<T>> action, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        if (http.DefaultRequestHeaders.Authorization is not null || http.DefaultRequestHeaders.Contains("Cookie")) throw Invalid();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        deadline.CancelAfter(operationTimeout ?? TimeSpan.FromSeconds(35));
        try { return await action(deadline.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
        { throw new MusicBrainzException(MusicBrainzFailure.Timeout, "MusicBrainz 读取超时，可稍后重试。"); }
        catch (HttpRequestException) { throw new MusicBrainzException(MusicBrainzFailure.Offline, "无法连接 MusicBrainz，已保留原有音乐资料。"); }
        catch (JsonException) { throw Invalid(); }
        catch (IOException) { throw new MusicBrainzException(MusicBrainzFailure.Offline, "音乐响应未能完整读取，请重试。"); }
    }
    private async Task<JsonDocument> GetJsonAsync(string path, CancellationToken token)
    {
        var uri = new Uri("https://musicbrainz.org/ws/2/" + path);
        for (int attempt = 0; ; attempt++)
        {
            using var lease = await Rate.EnterAsync(token).ConfigureAwait(false);
            using var request = Request(uri);
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            if (response.RequestMessage?.RequestUri is { } returned && returned != uri) throw Invalid();
            if (response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable || (int)response.StatusCode is >= 500 and <= 599)
            {
                var wait = response.Headers.RetryAfter?.Delta ?? response.Headers.RetryAfter?.Date - Now ?? TimeSpan.FromSeconds(2 + attempt * 2);
                Rate.Backoff(wait < TimeSpan.FromSeconds(1.1) ? TimeSpan.FromSeconds(1.1) : wait);
                if (attempt < 2) continue;
                throw new MusicBrainzException(response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable
                    ? MusicBrainzFailure.RateLimited : MusicBrainzFailure.Server, "MusicBrainz 暂时繁忙，请稍后重试。");
            }
            if (response.StatusCode == HttpStatusCode.NotFound) throw new MusicBrainzException(MusicBrainzFailure.Unavailable, "MusicBrainz 未提供此条目的音乐资料。");
            if (!response.IsSuccessStatusCode || response.Content.Headers.ContentType?.MediaType != "application/json") throw Invalid();
            var bytes = await ReadBytes(response.Content, token).ConfigureAwait(false);
            return JsonDocument.Parse(bytes, new() { MaxDepth = 64 });
        }
    }
    private static HttpRequestMessage Request(Uri uri)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.UserAgent.ParseAdd(UserAgent); request.Headers.Accept.ParseAdd("application/json"); return request;
    }
    private static async Task<byte[]> ReadBytes(HttpContent content, CancellationToken token)
    {
        if (content.Headers.ContentLength > MaximumResponseBytes) throw TooLarge();
        using var output = new MemoryStream(); await using var input = await content.ReadAsStreamAsync(token).ConfigureAwait(false);
        var buffer = new byte[32768];
        while (true)
        {
            var count = await input.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, MaximumResponseBytes + 1 - (int)output.Length)), token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested(); if (count == 0) return output.ToArray();
            if (output.Length + count > MaximumResponseBytes) throw TooLarge(); output.Write(buffer, 0, count);
        }
    }
    private static MusicBrainzException Invalid() => new(MusicBrainzFailure.InvalidResponse, "MusicBrainz 返回的音乐身份或资料格式无效。");
    private static MusicBrainzException TooLarge() => new(MusicBrainzFailure.ResponseTooLarge, "音乐资料超过大小限制，未加载。可在原站查看。");
}
