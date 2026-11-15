using System.Collections.Immutable;
using System.Net;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using TheOrder.Application;
using TheOrder.Domain;

namespace TheOrder.Infrastructure.Online;

// Independently associated Steam IDs only. When both game and demo are listed,
// the official store must confirm their exact parent relationship. No title search.
public sealed partial class SteamArtworkSource(HttpClient http, IImagePipeline images, IRemoteCoverRegistry registry) : IPublicArtworkSource
{
    public async Task<ArtworkSupplement> ReadAsync(WorkMetadata work, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        var ids = work.References.Where(r => r.Source == "steam" && AppId().IsMatch(r.Id)).Select(r => r.Id).Distinct().Take(3).ToArray();
        if (ids.Length is 0 or > 2) return new([], true);
        var target = ids.Length == 1 ? (Id: (string?)ids[0], Complete: true) : await ResolveGameAndDemoAsync(ids, cancellation).ConfigureAwait(false);
        if (target.Id is null) return new([], target.Complete);
        var art = new List<CatalogArt>(); var covers = new List<WorkCover>(); bool complete = true;
        foreach (var asset in new[] { "library_hero.jpg", "library_600x900.jpg" })
        {
            string url = $"https://shared.fastly.steamstatic.com/store_item_assets/steam/apps/{target.Id}/{asset}";
            using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellation); stop.CancelAfter(TimeSpan.FromSeconds(20));
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Head, url);
                using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, stop.Token).ConfigureAwait(false);
                stop.Token.ThrowIfCancellationRequested();
                if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone) continue;
                if (!response.IsSuccessStatusCode || response.Content.Headers.ContentType?.MediaType is not ("image/jpeg" or "image/png" or "image/webp")
                    || response.Content.Headers.ContentLength > 20 * 1024 * 1024) { complete = false; continue; }
                var key = registry.Register(url, work.FetchedAt);
                if (key is null || (await images.LoadAsync(key, ArtworkSizing.MaximumEdge, stop.Token).ConfigureAwait(false)).Pixels is not { } pixels)
                { complete = false; continue; }
                if (asset == "library_hero.jpg") art.Add(new(url, pixels.Width, pixels.Height, "steam-hero", "Steam " + target.Id));
                else covers.Add(new("steam:" + target.Id, url, pixels.Width, pixels.Height, null, null));
            }
            catch (OperationCanceledException) when (!cancellation.IsCancellationRequested) { complete = false; }
            catch (Exception e) when (e is HttpRequestException or IOException or OnlineException) { complete = false; }
        }
        return new([.. art], complete, [.. covers]);
    }
    private async Task<(string? Id, bool Complete)> ResolveGameAndDemoAsync(string[] ids, CancellationToken cancellation)
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellation); stop.CancelAfter(TimeSpan.FromSeconds(20));
        var rows = new List<(string Id, string Type, string Parent)>();
        try
        {
            foreach (var id in ids)
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, $"https://store.steampowered.com/api/appdetails?appids={id}&l=english&filters=basic");
                using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, stop.Token).ConfigureAwait(false);
                stop.Token.ThrowIfCancellationRequested();
                if (!response.IsSuccessStatusCode) return (null, false);
                var bytes = await CatalogTransport.ReadBoundedAsync(response.Content, 128 * 1024, stop.Token).ConfigureAwait(false);
                using var document = JsonDocument.Parse(bytes);
                if (document.RootElement.ValueKind != JsonValueKind.Object || !document.RootElement.TryGetProperty(id, out var entry) || entry.ValueKind != JsonValueKind.Object) return (null, false);
                if (!entry.TryGetProperty("success", out var success) || success.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return (null, false);
                if (!success.GetBoolean()) return (null, true);
                var data = RemoteWorkDto.Field(entry, "data"); var actualId = RemoteWorkDto.Field(data, "steam_appid");
                if (actualId.ValueKind != JsonValueKind.Number || !actualId.TryGetUInt64(out var number) || number.ToString(CultureInfo.InvariantCulture) != id) return (null, false);
                rows.Add((id, RemoteWorkDto.Text(data, "type"), RemoteWorkDto.Text(RemoteWorkDto.Field(data, "fullgame"), "appid")));
            }
            var games = rows.Where(r => r.Type == "game").ToArray();
            var demos = rows.Where(r => r.Type == "demo").ToArray();
            return games.Length == 1 && demos.Length == 1 && demos[0].Parent == games[0].Id ? (games[0].Id, true) : (null, true);
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested) { return (null, false); }
        catch (Exception e) when (e is HttpRequestException or IOException or JsonException or OnlineException) { return (null, false); }
    }
    internal static bool OriginalImageUri(Uri uri) => uri.Scheme == "https" && uri.IsDefaultPort && uri.UserInfo.Length == 0 && uri.Query.Length == 0 && uri.Fragment.Length == 0
        && uri.Host == "shared.fastly.steamstatic.com" && OriginalPath().IsMatch(uri.AbsolutePath);
    [GeneratedRegex("^[1-9][0-9]{0,9}$", RegexOptions.CultureInvariant)] private static partial Regex AppId();
    [GeneratedRegex("^/store_item_assets/steam/apps/[1-9][0-9]{0,9}/library_(hero|600x900)\\.jpg$", RegexOptions.CultureInvariant)] private static partial Regex OriginalPath();
}

public sealed class PublicArtworkSources(params IPublicArtworkSource[] sources) : IPublicArtworkSource
{
    public Task<ArtworkSupplement> ReadAsync(WorkMetadata work, CancellationToken cancellation)
        => ReadAsync(work, cancellation, promoOnly: false);

    public Task<ArtworkSupplement> ReadPromoAsync(WorkMetadata work, CancellationToken cancellation)
        => ReadAsync(work, cancellation, promoOnly: true);

    private async Task<ArtworkSupplement> ReadAsync(WorkMetadata work, CancellationToken cancellation, bool promoOnly)
    {
        var art = new List<CatalogArt>(); var covers = new List<WorkCover>(); bool complete = true;
        foreach (var source in sources)
        {
            var result = await (promoOnly ? source.ReadPromoAsync(work, cancellation) : source.ReadAsync(work, cancellation)).ConfigureAwait(false);
            complete &= result.Complete; art.AddRange(result.Art); if (!result.Covers.IsDefault) covers.AddRange(result.Covers);
        }
        return new([.. art.DistinctBy(a => a.Url).Take(24)], complete, [.. covers.DistinctBy(c => c.Url).Take(24)]);
    }
}
