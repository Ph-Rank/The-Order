using System.Text.RegularExpressions;

namespace TheOrder.Application;

public enum MusicOutletKind { Listening, Purchase, Information }
public enum MusicOutletSort { DomesticFirst, SourceOrder, PlatformName }
// Entity is the exact MB Release/Recording which supplied this URL, not a group or Work.
public sealed record MusicOutletLink(MusicBrainzReference Entity, string Platform, string Url, MusicOutletKind Kind,
    string TargetLayer, string[] Regions, WatchLinkEvidence[] Sources)
{
    public string RelationshipType { get; init; } = "";
}
public sealed record MusicOutletSnapshot(MusicBrainzReference Entity, string Title, string Artist, string? GroupId,
    string[] Formats, int? TrackCount, string? Date, string? Barcode, MusicOutletLink[] Links, DateTimeOffset FetchedAtUtc)
{
    public string? ReleaseStatus { get; init; }
    public string CacheKey => $"outlets:{(int)Entity.Kind}:{Entity.Id}";
}
public sealed record MusicOutletResult(MusicOutletSnapshot Snapshot, bool FromCache, bool IsStale, string? Warning);
public sealed record MusicDigitalOutletResult(MusicOutletResult[] Editions, bool Incomplete, string? Warning);
public interface IMusicOutletProvider
{
    Task<MusicOutletSnapshot> FetchOutletsAsync(MusicBrainzReference reference, CancellationToken cancellation);
}
public interface IMusicListeningReader
{
    Task<MusicOutletResult> LoadOutletsAsync(MusicBrainzReference reference, bool refresh, CancellationToken cancellation);
    Task<MusicDigitalOutletResult> LoadDigitalOutletsAsync(MusicOutletSnapshot current, CancellationToken cancellation);
    Task<AppleCatalogResult> SearchAppleAsync(MusicOutletSnapshot current, string country, bool refresh, CancellationToken cancellation);
    Task<MediaUserLinks> ReadAppleChoicesAsync(MusicBrainzReference release, CancellationToken cancellation);
    Task<MediaUserLinks> ChangeAppleChoiceAsync(MusicBrainzReference release, AppleAlbumCandidate? candidate,
        MediaIdentityReference target, long revision, CancellationToken cancellation);
}
public static class MusicOutlets
{
    public static bool Reference(MusicBrainzReference? value) => value is not null && MusicBrainzReference.ValidId(value.Id)
        && value.Kind is MusicBrainzEntityKind.Release or MusicBrainzEntityKind.Recording;
    public static string[] StoreCountries => ["JP", "CN", "US"];
    public static bool Country(string? value) => value is "JP" or "CN" or "US";
    public static bool Valid(MusicOutletLink? link) => link is not null && Reference(link.Entity) && Enum.IsDefined(link.Kind)
        && MusicBrainzValidation.Text(link.Platform, 100, false) && WatchLinks.SafeUrl(link.Url)
        && MusicBrainzValidation.Text(link.RelationshipType, 200, false)
        && link.TargetLayer is "Album" or "Recording" or "Page"
        && MusicBrainzValidation.Texts(link.Regions, 16, 20) && link.Sources is { Length: > 0 and <= 8 }
        && link.Sources.All(s => s is not null && MusicBrainzValidation.Text(s.Source, 100, false) && WatchLinks.SafeUrl(s.Url)
            && MusicBrainzValidation.Text(s.Note, 1000) && (s.DataVersion is null || MusicBrainzValidation.Text(s.DataVersion, 100)))
        && (link.Kind != MusicOutletKind.Listening || TargetMatches(link.Entity, link.TargetLayer)
            && link.RelationshipType is "streaming" or "free streaming" or "paid streaming"
            && Platform(link.Url).Layer == link.TargetLayer && Platform(link.Url).Platform == link.Platform && link.Platform != new Uri(link.Url).Host)
        && (link.Kind != MusicOutletKind.Purchase || link.RelationshipType is "purchase for download" or "purchase for mail-order");
    public static bool Valid(MusicOutletSnapshot? value) => value is not null && Reference(value.Entity)
        && MusicBrainzValidation.Text(value.Title, 2000, false) && MusicBrainzValidation.Text(value.Artist, 2000)
        && (value.GroupId is null || value.Entity.Kind == MusicBrainzEntityKind.Release && MusicBrainzReference.ValidId(value.GroupId))
        && MusicBrainzValidation.Texts(value.Formats, 16, 100) && value.TrackCount is null or >= 0 and <= 5000
        && (value.Date is null || MusicBrainzValidation.Date(value.Date))
        && (value.Barcode is null || MusicBrainzValidation.Text(value.Barcode, 100)) && value.FetchedAtUtc != default
        && (value.ReleaseStatus is null || value.Entity.Kind == MusicBrainzEntityKind.Release && MusicBrainzValidation.Text(value.ReleaseStatus, 100))
        && value.Links is { Length: <= 64 } && value.Links.All(l => Valid(l) && l.Entity == value.Entity);
    private static bool TargetMatches(MusicBrainzReference entity, string layer) => entity.Kind == MusicBrainzEntityKind.Release ? layer == "Album" : layer == "Recording";
    public static MusicOutletSnapshot Copy(MusicOutletSnapshot value) => value with { Formats = value.Formats.ToArray(),
        Links = value.Links.Select(x => x with { Regions = x.Regions.ToArray(), Sources = x.Sources.ToArray() }).ToArray() };
    public static MusicOutletLink? FromRelationship(MusicBrainzReference entity, string type, string direction,
        bool ended, string url, DateTimeOffset stamp)
    {
        if (!Reference(entity) || direction != "forward" || ended || !WatchLinks.SafeUrl(url)) return null;
        var (platform, layer, canonical, regions) = Platform(url);
        var kind = type switch { "streaming" or "free streaming" or "paid streaming" => MusicOutletKind.Listening,
            "purchase for download" or "purchase for mail-order" => MusicOutletKind.Purchase, _ => MusicOutletKind.Information };
        // A domain alone never establishes playback, nor can an album URL stand for a recording.
        if (kind == MusicOutletKind.Listening && (platform == new Uri(url).Host || !TargetMatches(entity, layer))) kind = MusicOutletKind.Information;
        if (platform == "Apple Music" && kind == MusicOutletKind.Purchase) platform = "iTunes 商店";
        return new(entity, platform, canonical, kind, layer, regions,
            [new("MusicBrainz", entity.Url, type + " · 来源收录，当前地区与订阅权限未核验", stamp)]) { RelationshipType = type };
    }
    public static (string Platform, string Layer, string Url, string[] Regions) Platform(string url)
    {
        var u = new Uri(url); var host = u.Host; var path = u.AbsolutePath.Trim('/').Split('/');
        string layer = "Page"; string name = host; string[] regions = [];
        if (host == "open.spotify.com") { name = "Spotify"; if (path.Length == 3 && path[0].StartsWith("intl-")) path = path[1..];
            if (path is ["album" or "track", var id] && Regex.IsMatch(id, "^[A-Za-z0-9]{10,30}$")) layer = path[0] == "album" ? "Album" : "Recording"; }
        else if (host is "music.apple.com" or "itunes.apple.com") { name = host == "music.apple.com" ? "Apple Music" : "iTunes 商店";
            var query = Query(u.Query);
            if (path.Length >= 3 && path[0].Length == 2 && path[1] is "album" or "song" && Digits(path[^1].StartsWith("id", StringComparison.Ordinal) ? path[^1][2..] : path[^1]))
            { layer = path[1] == "song" || HasQueryParameter(u.Query, "i") ? "Recording" : "Album"; regions = [path[0].ToUpperInvariant()]; }
        }
        else if (host is "deezer.com" or "www.deezer.com") { name = "Deezer"; if (path.Length == 3 && path[0].Length == 2) path = path[1..];
            if (path is ["album" or "track", var id] && Digits(id)) layer = path[0] == "album" ? "Album" : "Recording"; }
        else if (host.EndsWith(".bandcamp.com", StringComparison.Ordinal)) { name = "Bandcamp";
            if (path is ["album" or "track", _]) layer = path[0] == "album" ? "Album" : "Recording"; }
        else if (host is "music.163.com" or "m.music.163.com")
        {
            name = "网易云音乐"; var local = u.Fragment.StartsWith("#/") ? u.Fragment[1..] : u.PathAndQuery;
            var parts = local.Split('?', 2); var q = Query(parts.Length == 2 ? parts[1] : "");
            if (parts[0].Trim('/') is "album" or "song" && q.TryGetValue("id", out var id) && Digits(id))
            { layer = parts[0].Contains("album") ? "Album" : "Recording"; return (name, layer, "https://music.163.com/" + (layer == "Album" ? "album" : "song") + "?id=" + id, []); }
        }
        else if (host is "y.qq.com" or "i.y.qq.com") { name = "QQ 音乐";
            if (path.Length >= 2 && path[^2] is "albumDetail" or "songDetail" && Regex.IsMatch(path[^1], "^[A-Za-z0-9]{1,64}$")) layer = path[^2] == "albumDetail" ? "Album" : "Recording";
            else if (path.Length >= 3 && path[^3] == "cover") layer = "Album";
            else if (path.Length >= 3 && path[^3] == "song") layer = "Recording";
            else if (path.LastOrDefault() == "album.html") layer = "Album";
            else if (path.LastOrDefault() == "songDetail.html") layer = "Recording";
        }
        else if (host is "listen.tidal.com" or "tidal.com") { name = "Tidal"; if (path.Length >= 2 && path[^2] is "album" or "track") layer = path[^2] == "album" ? "Album" : "Recording"; }
        else if (host is "open.qobuz.com" or "www.qobuz.com") { name = "Qobuz"; if (path.Contains("album")) layer = "Album"; else if (path.Contains("track")) layer = "Recording"; }
        var ignored = new[] { "si", "uo", "app", "at", "ct", "itsct", "itscg", "ls" };
        var queryParts = u.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries).Where(x => !x.StartsWith("utm_", StringComparison.OrdinalIgnoreCase) && !ignored.Contains(x.Split('=')[0]));
        var queryText = string.Join('&', queryParts); var canonical = u.GetLeftPart(UriPartial.Path) + (queryText.Length > 0 ? "?" + queryText : "") + u.Fragment;
        return (name, layer, canonical, regions);
    }
    internal static Dictionary<string,string> Query(string text) => text.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
        .Select(x => x.Split('=', 2)).Where(x => x.Length == 2).GroupBy(x => x[0], StringComparer.Ordinal).Where(x => x.Count() == 1).ToDictionary(x => x.Key, x => x.Single()[1], StringComparer.Ordinal);
    internal static bool HasQueryParameter(string text, string name) => text.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
        .Any(x => Uri.UnescapeDataString(x.Split('=')[0]).Equals(name, StringComparison.OrdinalIgnoreCase));
    public static bool Digits(string value) => value is { Length: > 0 and <= 20 } && value[0] is >= '1' and <= '9' && value.All(char.IsAsciiDigit);
    public static MusicOutletLink[] Merge(IEnumerable<MusicOutletLink> input, MusicOutletSort sort)
    {
        var rows = new Dictionary<string,MusicOutletLink>(StringComparer.Ordinal);
        foreach (var value in input.Take(256).Where(Valid))
        {
            var p = Platform(value.Url); var item = value with { Url = p.Url };
            var key = $"{(int)item.Entity.Kind}:{item.Entity.Id}:{(int)item.Kind}:{item.Url}";
            rows[key] = rows.TryGetValue(key, out var old) ? old with { Sources = old.Sources.Concat(item.Sources).Distinct().Take(8).ToArray(), Regions = old.Regions.Concat(item.Regions).Distinct().Take(16).ToArray() } : item;
        }
        return WatchLinks.OrderPlatforms(rows.Values, x => x.Platform, sort == MusicOutletSort.DomesticFirst ? ["网易云音乐", "QQ 音乐"] : [], sort == MusicOutletSort.PlatformName).ToArray();
    }
}
