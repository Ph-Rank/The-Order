using System.Security.Cryptography;
using System.Text;

namespace TheOrder.Application;

public sealed record AppleCatalogQuery(MusicBrainzReference Release, string Title, string Artist, string? Barcode,
    string Country, string? ExplicitAlbumId = null)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public string Key => "apple:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(new
        { Release.Kind, Release.Id, Title, Artist, Barcode, Country, ExplicitAlbumId }))));
    public bool IsValid => Release is { Kind: MusicBrainzEntityKind.Release } && MusicOutlets.Reference(Release)
        && MusicBrainzValidation.Text(Title, 2000, false) && MusicBrainzValidation.Text(Artist, 2000)
        && (Barcode is null || MusicBrainzValidation.Text(Barcode, 100)) && MusicOutlets.Country(Country)
        && (ExplicitAlbumId is null || MusicOutlets.Digits(ExplicitAlbumId));
}
public sealed record AppleAlbumCandidate(string Id, string Country, string Title, string Artist, string Url,
    string? ReleaseDate, int TrackCount, string RetrievalEvidence)
{
    public MediaIdentityReference Target => new("iTunes", "Album:" + Country, Id);
    public bool IsValid => MusicOutlets.Digits(Id) && MusicOutlets.Country(Country)
        && MusicBrainzValidation.Text(Title, 2000, false) && MusicBrainzValidation.Text(Artist, 2000, false)
        && TrackCount is >= 1 and <= 5000 && (ReleaseDate is null || MusicBrainzValidation.Date(ReleaseDate))
        && MusicBrainzValidation.Text(RetrievalEvidence, 1000, false)
        && TryAlbumUrl(Url, Country, out var id) && id == Id;
    public static bool TryAlbumUrl(string url, string country, out string id)
    {
        id = "";
        if (!WatchLinks.SafeUrl(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Host is not ("music.apple.com" or "itunes.apple.com") || uri.Fragment.Length > 0) return false;
        var parts = uri.AbsolutePath.Trim('/').Split('/');
        if (MusicOutlets.HasQueryParameter(uri.Query, "i") || parts.Length < 3 || !parts[0].Equals(country, StringComparison.OrdinalIgnoreCase) || parts[1] != "album") return false;
        var number = parts[^1].StartsWith("id", StringComparison.Ordinal) ? parts[^1][2..] : parts[^1];
        if (!MusicOutlets.Digits(number)) return false;
        id = number; return true;
    }
    public static bool TryCatalogAlbumId(string url, out string id)
    {
        id = "";
        if (!WatchLinks.SafeUrl(url)) return false;
        var uri = new Uri(url); var parts = uri.AbsolutePath.Trim('/').Split('/');
        if (parts.Length >= 3 && parts[0].Length == 2 && parts[0].All(char.IsAsciiLetter)) return TryAlbumUrl(url, parts[0], out id);
        if (uri.Host == "itunes.apple.com" && uri.AbsolutePath == "/WebObjects/MZStore.woa/wa/viewAlbum")
        { var query = MusicOutlets.Query(uri.Query); if (!MusicOutlets.HasQueryParameter(uri.Query, "i") && query.TryGetValue("id", out var number) && MusicOutlets.Digits(number)) { id = number; return true; } }
        return false;
    }
    public string[] Differences(MusicOutletSnapshot source)
    {
        var rows = new List<string>();
        if (MusicBrainzMatching.Normalize(Title) != MusicBrainzMatching.Normalize(source.Title)) rows.Add("标题有差异");
        if (source.Artist.Length > 0 && source.Artist != "Various Artists" && MusicBrainzMatching.Normalize(Artist) != MusicBrainzMatching.Normalize(source.Artist)) rows.Add("音乐人署名有差异");
        if (source.TrackCount is { } n && n != TrackCount) rows.Add($"曲目数量不同：{n} / {TrackCount}");
        if (source.Date is { } date && ReleaseDate is { } other && date != other) rows.Add($"发行日期不同：{date} / {other}");
        return rows.ToArray();
    }
}
public sealed record AppleCatalogResult(AppleCatalogQuery Query, AppleAlbumCandidate[] Candidates, int Discarded,
    bool Limited, DateTimeOffset FetchedAtUtc, bool FromCache = false, bool IsStale = false, string? Warning = null)
{
    public bool IsValid => Query is { IsValid: true } && Candidates is { Length: <= 8 } && Candidates.All(c => c is { IsValid: true } && c.Country == Query.Country && HasTitleEvidence(Query, c))
        && Candidates.Select(c => c.Id).Distinct().Count() == Candidates.Length && Discarded is >= 0 and <= 100
        && FetchedAtUtc != default;
    public static bool HasTitleEvidence(AppleCatalogQuery query, AppleAlbumCandidate candidate)
    {
        if (query.ExplicitAlbumId == candidate.Id) return true;
        var left = MusicBrainzMatching.Normalize(query.Title); var right = MusicBrainzMatching.Normalize(candidate.Title);
        return left == right || Math.Min(left.Length, right.Length) >= 8 && (left.Contains(right, StringComparison.Ordinal) || right.Contains(left, StringComparison.Ordinal));
    }
}
public interface IAppleCatalogProvider
{
    Task<AppleCatalogResult> SearchAsync(AppleCatalogQuery query, CancellationToken cancellation);
}
