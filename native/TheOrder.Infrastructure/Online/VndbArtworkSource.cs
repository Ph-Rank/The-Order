using System.Collections.Immutable;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using TheOrder.Application;
using TheOrder.Domain;

namespace TheOrder.Infrastructure.Online;

// Optional public artwork supplement. Uses its own unauthenticated client so the
// NextMoe key can never leave the catalog transport. Identity facts stay in KunGal.
public sealed partial class VndbArtworkSource(HttpClient http, Func<Stream>? openHints = null) : IPublicArtworkSource
{
    private readonly SemaphoreSlim requests = new(1);
    private DateTimeOffset nextRequest;
    private readonly Lazy<Dictionary<string, string[]>> hints = new(() =>
    {
        try { using var stream = openHints?.Invoke(); return stream is null ? [] : JsonSerializer.Deserialize<Dictionary<string, string[]>>(stream) ?? []; }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { return []; }
    });
    public Task<ArtworkSupplement> ReadAsync(WorkMetadata work, CancellationToken cancellation)
    {
        if (ArtworkSources.VndbWorkId(work) is not { } id) return Task.FromResult(new ArtworkSupplement([], true));
        return QueryAsync("vn", new
        {
            filters = new[] { "id", "=", id },
            fields = "image.id,image.url,image.dims,image.sexual,screenshots.id,screenshots.url,screenshots.dims,screenshots.sexual,screenshots.violence",
            results = 1
        }, data => Parse(data, id, hints.Value.GetValueOrDefault(id) ?? []), cancellation);
    }

    private const int MaximumReleaseRows = 32;

    public Task<ArtworkSupplement> ReadPromoAsync(WorkMetadata work, CancellationToken cancellation)
    {
        if (ArtworkSources.VndbWorkId(work) is not { } id) return Task.FromResult(new ArtworkSupplement([], true));
        return QueryAsync("release", new
        {
            filters = new object[] { "and", new object[] { "vn", "=", new[] { "id", "=", id } },
                new object[] { "official", "=", 1 }, new object[] { "patch", "!=", 1 },
                new[] { "rtype", "=", "complete" }, new[] { "image", "=", "dig" } },
            fields = "official,patch,vns.id,vns.rtype,images.id,images.url,images.dims,images.sexual,images.violence,images.type,images.vn,images.photo",
            sort = "released",
            reverse = true,
            results = MaximumReleaseRows
        }, data => ParsePromos(data, id), cancellation);
    }

    private async Task<ArtworkSupplement> QueryAsync(string endpoint, object query, Func<JsonElement, ArtworkSupplement> parse, CancellationToken cancellation)
    {
        await requests.WaitAsync(cancellation).ConfigureAwait(false);
        try
        {
            var delay = nextRequest - DateTimeOffset.UtcNow;
            if (delay > TimeSpan.Zero) await Task.Delay(delay, cancellation).ConfigureAwait(false);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation); timeout.CancelAfter(TimeSpan.FromSeconds(20));
            nextRequest = DateTimeOffset.UtcNow.AddMilliseconds(1600);
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.vndb.org/kana/" + endpoint)
            {
                Content = JsonContent.Create(query)
            };
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) { if ((int)response.StatusCode == 429) nextRequest = DateTimeOffset.UtcNow.AddMinutes(1); return new([], false); }
            var bytes = await CatalogTransport.ReadBoundedAsync(response.Content, 512 * 1024, timeout.Token).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(bytes);
            return parse(doc.RootElement);
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested) { return new([], false); }
        catch (Exception e) when (e is HttpRequestException or IOException or JsonException or OnlineException) { return new([], false); }
        finally { requests.Release(); }
    }
    public static ArtworkSupplement Parse(JsonElement data, string id, string[] preferred)
    {
        var rows = RemoteWorkDto.Rows(data, "results").Take(2).ToArray();
        if (rows.Length != 1 || RemoteWorkDto.Text(rows[0], "id") != id) return new([], false);
        var art = new List<CatalogArt>();
        foreach (var image in RemoteWorkDto.Rows(rows[0], "screenshots").Take(64))
        {
            var dims = RemoteWorkDto.Field(image, "dims");
            var sexual = RemoteWorkDto.Field(image, "sexual"); var violence = RemoteWorkDto.Field(image, "violence");
            if (!SafeRating(sexual) || !SafeRating(violence) || !Dimensions(dims, out var width, out var height) || width < 640 || height < 180) continue;
            var url = RemoteWorkDto.Text(image, "url"); var screenshotId = RemoteWorkDto.Text(image, "id");
            if (!ScreenshotId().IsMatch(screenshotId) || !ImageUri(url, screenshotId, "sf")) continue;
            // A historical hint is used only if this exact work still lists the image as safe.
            art.Add(new(url, width, height, preferred.Contains(screenshotId) ? "vndb-curated" : "vndb-screenshot", "VNDB " + id));
        }
        var cover = RemoteWorkDto.Field(rows[0], "image");
        var coverUrl = RemoteWorkDto.Text(cover, "url"); var coverId = RemoteWorkDto.Text(cover, "id");
        ImmutableArray<WorkCover> covers = [];
        if (CoverId().IsMatch(coverId) && ImageUri(coverUrl, coverId, "cv") && SafeRating(RemoteWorkDto.Field(cover, "sexual")) && Dimensions(RemoteWorkDto.Field(cover, "dims"), out var cw, out var ch))
            covers = [new("vndb:" + coverId, coverUrl, cw, ch, null, "safe")];
        return new([.. art.DistinctBy(a => a.Url)], true, covers);
    }
    private static ArtworkSupplement ParsePromos(JsonElement data, string id)
    {
        if (RemoteWorkDto.Field(data, "results").ValueKind != JsonValueKind.Array) return new([], false);
        var art = new List<CatalogArt>();
        foreach (var release in RemoteWorkDto.Rows(data, "results").Take(MaximumReleaseRows))
        {
            var releaseId = RemoteWorkDto.Text(release, "id");
            if (!ReleaseId().IsMatch(releaseId) || RemoteWorkDto.Field(release, "official").ValueKind != JsonValueKind.True ||
                RemoteWorkDto.Field(release, "patch").ValueKind != JsonValueKind.False) continue;
            var works = RemoteWorkDto.Rows(release, "vns").Take(64).ToArray();
            if (!works.Any(v => RemoteWorkDto.Text(v, "id") == id && RemoteWorkDto.Text(v, "rtype") == "complete")) continue;
            foreach (var image in RemoteWorkDto.Rows(release, "images").Take(32))
            {
                if (RemoteWorkDto.Text(image, "type") != "dig" || RemoteWorkDto.Field(image, "photo").ValueKind != JsonValueKind.False) continue;
                var imageWork = RemoteWorkDto.Field(image, "vn");
                // An unlabelled image can only be attributed to a single-work release.
                if (!(imageWork.ValueKind == JsonValueKind.String && imageWork.GetString() == id ||
                    imageWork.ValueKind == JsonValueKind.Null && works.Length == 1)) continue;
                if (!SafeRating(RemoteWorkDto.Field(image, "sexual")) || !SafeRating(RemoteWorkDto.Field(image, "violence")) ||
                    !Dimensions(RemoteWorkDto.Field(image, "dims"), out var width, out var height) ||
                    width < 640 || height < 180 || width / (double)height is < 1.35 or > 4.5) continue;
                var url = RemoteWorkDto.Text(image, "url"); var imageId = RemoteWorkDto.Text(image, "id");
                if (CoverId().IsMatch(imageId) && ImageUri(url, imageId, "cv"))
                    art.Add(new(url, width, height, ArtworkSources.VndbPromoKind, "VNDB " + releaseId));
            }
        }
        return new([.. art.DistinctBy(a => a.Url).OrderByDescending(a => Math.Min(a.Width, a.Height * 3.3)).Take(8)], true);
    }

    private static bool SafeRating(JsonElement value) => value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var n) && double.IsFinite(n) && n is >= 0 and < 1;
    private static bool Dimensions(JsonElement dims, out int width, out int height)
    {
        width = height = 0;
        return dims.ValueKind == JsonValueKind.Array && dims.GetArrayLength() == 2 && dims[0].ValueKind == JsonValueKind.Number && dims[1].ValueKind == JsonValueKind.Number &&
            dims[0].TryGetInt32(out width) && dims[1].TryGetInt32(out height) && width is > 0 and <= 65535 && height is > 0 and <= 65535 && (long)width * height <= 40_000_000;
    }
    private static bool ImageUri(string url, string id, string directory)
    {
        var number = id[2..];
        return Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == "https" && uri.Host == "t.vndb.org" && uri.IsDefaultPort && uri.UserInfo.Length == 0 && uri.Query.Length == 0 && uri.Fragment.Length == 0 &&
            uri.AbsolutePath == "/" + directory + "/" + number.PadLeft(2, '0')[^2..] + "/" + number + ".jpg";
    }
    internal static bool OriginalImageUri(Uri uri)
    {
        var match = OriginalImagePath().Match(uri.AbsolutePath);
        return match.Success && ImageUri(uri.AbsoluteUri, match.Groups[1].Value + match.Groups[2].Value, match.Groups[1].Value);
    }
    [GeneratedRegex("^/(cv|sf)/[0-9]{2}/([0-9]{1,12})\\.jpg$", RegexOptions.CultureInvariant)] private static partial Regex OriginalImagePath();
    [GeneratedRegex("^r[0-9]{1,12}$", RegexOptions.CultureInvariant)] private static partial Regex ReleaseId();
    [GeneratedRegex("^sf[0-9]{1,12}$", RegexOptions.CultureInvariant)] private static partial Regex ScreenshotId();
    [GeneratedRegex("^cv[0-9]{1,12}$", RegexOptions.CultureInvariant)] private static partial Regex CoverId();
}
