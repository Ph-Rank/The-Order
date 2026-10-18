using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TheOrder.Application;

// Provider identities stay typed: a recording or release-group MBID is never a release MBID.
public enum MusicBrainzEntityKind { ReleaseGroup, Release, Recording, Work, Artist }
public sealed record MusicBrainzReference(MusicBrainzEntityKind Kind, string Id)
{
    public string Url => "https://musicbrainz.org/" + (Kind switch
    { MusicBrainzEntityKind.ReleaseGroup => "release-group", MusicBrainzEntityKind.Release => "release",
        MusicBrainzEntityKind.Recording => "recording", MusicBrainzEntityKind.Work => "work", MusicBrainzEntityKind.Artist => "artist",
        _ => throw new InvalidDataException("MusicBrainz 资料类型无效。") }) + "/" + Id;
    public static bool ValidId(string? id) => id is not null && Guid.TryParseExact(id, "D", out var parsed)
        && parsed != Guid.Empty && id == parsed.ToString("D");
    public static bool TryParse(string? url, out MusicBrainzReference? reference)
    {
        reference = null;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https" || !uri.IsDefaultPort
            || uri.Host != "musicbrainz.org" || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0) return false;
        var parts = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2 || !ValidId(parts[1])) return false;
        MusicBrainzEntityKind? kind = parts[0] switch
        { "release-group" => MusicBrainzEntityKind.ReleaseGroup, "release" => MusicBrainzEntityKind.Release, "recording" => MusicBrainzEntityKind.Recording,
            "work" => MusicBrainzEntityKind.Work, "artist" => MusicBrainzEntityKind.Artist, _ => null };
        if (kind is null) return false;
        reference = new(kind.Value, parts[1]); return reference.Url == url;
    }
}
public sealed record BangumiMusicFacts(string[] CatalogNumbers, string[] Artists, string[] Labels, string? Barcode,
    int? DiscCount, MusicBrainzReference[] ExternalIds)
{
    // Declared subject aliases are retrieval evidence, not new external identities.
    public string[] TitleAliases { get; init; } = [];
    public static readonly BangumiMusicFacts Empty = new([], [], [], null, null, []);
    public bool IsValid => MusicBrainzValidation.Texts(CatalogNumbers, 32, 200) && MusicBrainzValidation.Texts(Artists, 64, 500)
        && MusicBrainzValidation.Texts(Labels, 32, 500) && (Barcode is null || Regex.IsMatch(Barcode, "^[0-9]{8,14}$"))
        && (DiscCount is null or > 0 and <= 50) && ExternalIds is { Length: <= 16 }
        && ExternalIds.All(x => x is not null && Enum.IsDefined(x.Kind) && MusicBrainzReference.ValidId(x.Id))
        && MusicBrainzValidation.Texts(TitleAliases, 16, 2000);
}
public sealed record MusicAlbumSource(string BangumiId, string Title, string? ReleaseDate, BangumiMusicFacts Facts)
{
    public string Fingerprint => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
    { BangumiId, Title, ReleaseDate, Facts.CatalogNumbers, Facts.Artists, Facts.Labels, Facts.Barcode, Facts.DiscCount, Facts.ExternalIds })));
    public bool IsValid => BangumiRelationIdentityResolver.ValidSubjectId(BangumiId) && MusicBrainzValidation.Text(Title, 2000, false)
        && (ReleaseDate is null || MusicBrainzValidation.Date(ReleaseDate, full: true)) && Facts is not null && Facts.IsValid;
    public static MusicAlbumSource From(BangumiSubjectDetails details)
    {
        if (details.Kind != BangumiRelationKind.Music || !BangumiSubjectDetailsService.Valid(details.Id, details.Kind, details))
            throw new InvalidDataException("音乐条目身份无效。");
        return new(details.Id, string.IsNullOrWhiteSpace(details.OriginalTitle) ? details.Title : details.OriginalTitle,
            details.ReleaseDate, details.MusicFacts ?? BangumiMusicFacts.Empty);
    }
}
public sealed record MusicBrainzLabel(string Name, string? CatalogNumber);
// The credited spelling and join phrase belong to this release/track/recording.
// Artist identities are navigation facts, never evidence of release identity.
public sealed record MusicBrainzArtistCredit(string Name, string? ArtistId, string JoinPhrase);
public sealed record MusicBrainzCredit(string Role, string Name, string? ArtistId);
public sealed record MusicBrainzWork(string Id, string Title, string? Type, string[] Languages, MusicBrainzCredit[] Credits)
{
    public string Url => new MusicBrainzReference(MusicBrainzEntityKind.Work, Id).Url;
}
public sealed record MusicBrainzTrack(int Position, string Number, string Title, string Artist, int? LengthMs,
    string? RecordingId, MusicBrainzCredit[] Credits)
{
    // Optional additions retain the original serialized track/constructor contract.
    public MusicBrainzWork[] Works { get; init; } = [];
    public MusicBrainzArtistCredit[] ArtistCredits { get; init; } = [];
}
public sealed record MusicBrainzDisc(int Position, string Title, string Format, MusicBrainzTrack[] Tracks);
public sealed record MusicBrainzReleaseEvent(string? Date, string? Country);
public sealed record MusicBrainzReleaseCandidate(string Id, string? ReleaseGroupId, string Title, string Artist,
    string? Date, string? Country, string Status, string? Barcode, MusicBrainzLabel[] Labels, int? DiscCount, int? TrackCount)
{
    public string? Disambiguation { get; init; }
    public string? Packaging { get; init; }
    public string? TextLanguage { get; init; }
    public string? TextScript { get; init; }
    public string[] MediaFormats { get; init; } = [];
    public MusicBrainzReleaseEvent[] ReleaseEvents { get; init; } = [];
    public MusicBrainzArtistCredit[] ArtistCredits { get; init; } = [];
}
public sealed record MusicBrainzSearchResult(MusicBrainzReleaseCandidate[] Candidates, int Total, bool Truncated,
    string? ExplicitReleaseId, string? Notice);
public sealed record MusicBrainzAlbum(MusicBrainzReleaseCandidate Release, string? GroupTitle, string? GroupType,
    string[] SecondaryTypes, MusicBrainzDisc[] Discs, MusicBrainzCredit[] Credits, string? CoverUrl,
    DateTimeOffset FetchedAtUtc, string? CoverWarning = null)
{
    public string Url => new MusicBrainzReference(MusicBrainzEntityKind.Release, Release.Id).Url;
}
public sealed record MusicBrainzCandidateAssessment(MusicBrainzReleaseCandidate Candidate, string[] Evidence, bool HasConflict, bool Strong)
{
    public bool HasIdentityConflict { get; init; }
}
public sealed record MusicBrainzMatchResult(MusicBrainzSearchResult Search, string? AutomaticReleaseId,
    string? ConfirmedReleaseId, bool FromCache, bool IsStale, string? Warning)
{
    public bool NeedsReview { get; init; }
    public string? ConfirmedReleaseGroupId { get; init; }
    public string[] RejectedIds { get; init; } = [];
}
public sealed record MusicBrainzAlbumResult(MusicBrainzAlbum Album, bool FromCache, bool IsStale, string? Warning);
public sealed record MusicBrainzBinding(string SourceFingerprint, string ReleaseId);
public sealed record MusicBrainzCacheValue(string Key, DateTimeOffset CachedAtUtc, MusicBrainzSearchResult? Search = null,
    MusicBrainzAlbum? Album = null, MusicBrainzBinding? Binding = null, bool Rejected = false)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public MusicOutletSnapshot? Outlets { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public AppleCatalogResult? Apple { get; init; }
}
public enum MusicBrainzFailure { Offline, Timeout, RateLimited, Server, Unavailable, InvalidResponse, ResponseTooLarge }
public sealed class MusicBrainzException(MusicBrainzFailure failure, string message) : Exception(message)
{ public MusicBrainzFailure Failure { get; } = failure; }
public interface IMusicBrainzProvider
{
    Task<MusicBrainzSearchResult> SearchAsync(MusicAlbumSource source, CancellationToken cancellation);
    Task<MusicBrainzSearchResult> EditionsAsync(string releaseGroupId, CancellationToken cancellation);
    Task<MusicBrainzAlbum> FetchAlbumAsync(string releaseId, CancellationToken cancellation);
}
public interface IMusicBrainzCache
{
    Task<MusicBrainzCacheValue?> ReadAsync(string key, CancellationToken cancellation);
    Task WriteAsync(MusicBrainzCacheValue value, CancellationToken cancellation);
}
public interface IMusicBrainzReader
{
    Task<MusicBrainzMatchResult> MatchAsync(MusicAlbumSource source, bool refresh, CancellationToken cancellation);
    Task<MusicBrainzAlbumResult> LoadAlbumAsync(string releaseId, bool refresh, CancellationToken cancellation);
    Task<MusicBrainzSearchResult> EditionsAsync(string releaseGroupId, CancellationToken cancellation);
    Task ConfirmAsync(MusicAlbumSource source, MusicBrainzAlbum album, CancellationToken cancellation);
    Task RejectAsync(MusicAlbumSource source, MusicBrainzReleaseCandidate release, CancellationToken cancellation)
        => Task.FromException(new NotSupportedException("当前资料读取器不支持修改关联。"));
    Task RevokeAsync(MusicAlbumSource source, string expectedReleaseId, CancellationToken cancellation)
        => Task.FromException(new NotSupportedException("当前资料读取器不支持修改关联。"));
}

public static class MusicBrainzMatching
{
    public static string Normalize(string value) => string.Concat(value.Normalize(NormalizationForm.FormKC)
        .Where(char.IsLetterOrDigit)).ToUpperInvariant();
    // A small, explicit catalogue range is common in Japanese multidisc albums. Never expand arbitrary ranges.
    public static string[] Catalogues(IEnumerable<string> values) => values.SelectMany(value =>
    {
        var match = Regex.Match(value.Trim().Normalize(NormalizationForm.FormKC), @"^([A-Za-z][A-Za-z0-9-]*?)(\d{2,6})\s*[~～–〜]\s*(\d{2,6})$", RegexOptions.CultureInvariant);
        if (match.Success && match.Groups[2].Length == match.Groups[3].Length
            && int.TryParse(match.Groups[2].Value, out var first) && int.TryParse(match.Groups[3].Value, out var last)
            && last >= first && last - first < 32)
            return Enumerable.Range(first, last - first + 1).Select(x => match.Groups[1].Value + x.ToString(new string('0', match.Groups[2].Length), CultureInfo.InvariantCulture));
        return new[] { value };
    }).Distinct(StringComparer.OrdinalIgnoreCase).Take(32).ToArray();
    public static MusicBrainzCandidateAssessment Assess(MusicAlbumSource source, MusicBrainzReleaseCandidate candidate)
    {
        var evidence = new List<string>(); bool conflict = false, identityConflict = false;
        var primaryTitle = Normalize(source.Title) == Normalize(candidate.Title);
        var title = primaryTitle || source.Facts.TitleAliases.Any(x => Normalize(x) == Normalize(candidate.Title));
        evidence.Add(primaryTitle ? "标题一致" : title ? "来源提供的完整标题别名一致" : "标题不同，请核对");
        var expected = Catalogues(source.Facts.CatalogNumbers).Select(Normalize).ToArray();
        var actual = candidate.Labels.Where(x => !string.IsNullOrWhiteSpace(x.CatalogNumber)).Select(x => Normalize(x.CatalogNumber!)).ToArray();
        bool catalogue = expected.Length > 0 && expected.All(actual.Contains);
        if (expected.Length > 0) { evidence.Add(catalogue ? "目录编号一致" : "目录编号未完全对应"); conflict |= actual.Length > 0 && !catalogue; }
        bool barcode = source.Facts.Barcode is not null && source.Facts.Barcode == candidate.Barcode;
        if (source.Facts.Barcode is not null) { evidence.Add(barcode ? "条码一致" : "条码未对应"); conflict |= candidate.Barcode is not null && !barcode; }
        bool date = source.ReleaseDate is not null && source.ReleaseDate == candidate.Date;
        if (source.ReleaseDate is not null) { evidence.Add(date ? "发行日期一致" : "发行日期未对应"); conflict |= candidate.Date is not null && !date; }
        if (source.Facts.DiscCount is { } discs && candidate.DiscCount is { } found)
        { evidence.Add(discs == found ? "碟数一致" : "碟数不同"); conflict |= discs != found; }
        var explicitRelease = source.Facts.ExternalIds.Any(x => x.Kind == MusicBrainzEntityKind.Release && x.Id == candidate.Id);
        var releaseIds = source.Facts.ExternalIds.Where(x => x.Kind == MusicBrainzEntityKind.Release).Select(x => x.Id).Distinct().ToArray();
        var groupIds = source.Facts.ExternalIds.Where(x => x.Kind == MusicBrainzEntityKind.ReleaseGroup).Select(x => x.Id).Distinct().ToArray();
        if (releaseIds.Length > 1 || groupIds.Length > 1)
        { conflict = identityConflict = true; evidence.Add("来源存在多个不同的发行版或专辑组编号"); }
        if (releaseIds.Length > 0 && !explicitRelease || groupIds.Length > 0 && !groupIds.Contains(candidate.ReleaseGroupId))
        { conflict = identityConflict = true; evidence.Add("外部编号未对应"); }
        var relatedEntity = source.Facts.ExternalIds.Any(x => x.Kind is MusicBrainzEntityKind.Recording or MusicBrainzEntityKind.Work or MusicBrainzEntityKind.Artist);
        if (relatedEntity) evidence.Add("录音、作品或音乐人编号不能证明专辑发行版身份，请核对具体发行版");
        if (explicitRelease) evidence.Insert(0, "来源提供了此发行版编号");
        return new(candidate, evidence.ToArray(), conflict, !conflict && !relatedEntity && title
            && (explicitRelease || candidate.Status == "Official" && date && (catalogue || barcode))) { HasIdentityConflict = identityConflict };
    }
    public static string? Automatic(MusicAlbumSource source, MusicBrainzSearchResult result)
    {
        if (result.Truncated) return null;
        var strong = result.Candidates.Select(x => Assess(source, x)).Where(x => x.Strong).ToArray();
        return strong.Length == 1 ? strong[0].Candidate.Id : null;
    }
}

public static class MusicBrainzValidation
{
    public static bool Text(string? text, int max, bool empty = true) => text is not null && text.Length <= max
        && (empty || !string.IsNullOrWhiteSpace(text)) && !text.Any(char.IsControl);
    public static bool Texts(string[]? values, int count, int max) => values is not null && values.Length <= count && values.All(x => Text(x, max, false));
    public static bool ArtistCredits(MusicBrainzArtistCredit[]? values) => values is { Length: <= 64 }
        && values.All(x => x is not null && Text(x.Name, 500, false) && Text(x.JoinPhrase, 100)
            && (x.ArtistId is null || MusicBrainzReference.ValidId(x.ArtistId)));
    public static bool Date(string? value, bool full = false) => value is not null && (full
        ? DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)
        : Date(value, true) || Regex.IsMatch(value, @"^[1-9]\d{3}$") || Regex.IsMatch(value, @"^[1-9]\d{3}-(0[1-9]|1[0-2])$"));
    public static bool Candidate(MusicBrainzReleaseCandidate? c) => c is not null && MusicBrainzReference.ValidId(c.Id)
        && (c.ReleaseGroupId is null || MusicBrainzReference.ValidId(c.ReleaseGroupId)) && Text(c.Title, 2000, false) && Text(c.Artist, 2000)
        && (c.Date is null || Date(c.Date)) && (c.Country is null || Text(c.Country, 100)) && Text(c.Status, 100)
        && (c.Barcode is null || Text(c.Barcode, 100)) && c.Labels is { Length: <= 64 }
        && c.Labels.All(x => x is not null && Text(x.Name, 500) && (x.CatalogNumber is null || Text(x.CatalogNumber, 200)))
        && c.DiscCount is null or >= 0 and <= 50 && c.TrackCount is null or >= 0 and <= 5000
        && (c.Disambiguation is null || Text(c.Disambiguation, 1000)) && (c.Packaging is null || Text(c.Packaging, 100))
        && (c.TextLanguage is null || Text(c.TextLanguage, 20)) && (c.TextScript is null || Text(c.TextScript, 20))
        && Texts(c.MediaFormats, 50, 100) && ArtistCredits(c.ArtistCredits) && c.ReleaseEvents is { Length: <= 256 }
        && c.ReleaseEvents.All(e => e is not null && (e.Date is null || Date(e.Date)) && (e.Country is null || Text(e.Country, 100)));
    public static bool Search(MusicBrainzSearchResult? s) => s is not null && s.Candidates is { Length: <= 25 }
        && s.Total >= s.Candidates.Length && s.Truncated == (s.Total > s.Candidates.Length) && s.Candidates.All(Candidate) && s.Candidates.Select(x => x.Id).Distinct().Count() == s.Candidates.Length
        && (s.ExplicitReleaseId is null || s.Candidates.Any(x => x.Id == s.ExplicitReleaseId)) && (s.Notice is null || Text(s.Notice, 2000));
    private static bool Credit(MusicBrainzCredit x) => x is not null && Text(x.Role, 200, false) && Text(x.Name, 500, false)
        && (x.ArtistId is null || MusicBrainzReference.ValidId(x.ArtistId));
    public static bool Work(MusicBrainzWork? work) => work is not null && MusicBrainzReference.ValidId(work.Id)
        && Text(work.Title, 2000, false) && (work.Type is null || Text(work.Type, 100)) && Texts(work.Languages, 16, 20)
        && work.Credits is { Length: <= 64 } && work.Credits.All(Credit);
    public static bool Album(MusicBrainzAlbum? a) => a is not null && Candidate(a.Release) && a.FetchedAtUtc != default
        && (a.GroupTitle is null || Text(a.GroupTitle, 2000)) && (a.GroupType is null || Text(a.GroupType, 100))
        && Texts(a.SecondaryTypes, 16, 100) && a.Discs is { Length: <= 50 }
        && a.Discs.All(d => d is not null && d.Position is > 0 and <= 50 && Text(d.Title, 2000) && Text(d.Format, 100)
            && d.Tracks is { Length: <= 1000 } && d.Tracks.All(t => t is not null && t.Position is > 0 and <= 1000
                && Text(t.Number, 100) && Text(t.Title, 2000, false) && Text(t.Artist, 2000) && (t.LengthMs is null or >= 0 and <= 86400000)
                && (t.RecordingId is null || MusicBrainzReference.ValidId(t.RecordingId)) && ArtistCredits(t.ArtistCredits)
                && t.Credits is { Length: <= 64 } && t.Credits.All(Credit)
                && t.Works is { Length: <= 16 } && t.Works.All(Work) && t.Works.Select(w => w.Id).Distinct().Count() == t.Works.Length)
            && d.Tracks.Select(t => t.Position).Distinct().Count() == d.Tracks.Length)
        && a.Discs.Select(d => d.Position).Distinct().Count() == a.Discs.Length && a.Discs.Sum(d => d.Tracks.Length) <= 5000
        && (a.Release.DiscCount is null || a.Release.DiscCount == a.Discs.Length)
        && (a.Release.TrackCount is null || a.Release.TrackCount == a.Discs.Sum(d => d.Tracks.Length))
        && a.Credits is { Length: <= 128 } && a.Credits.All(Credit)
        && (a.CoverUrl is null || CoverUri(a.CoverUrl, a.Release.Id)) && (a.CoverWarning is null || Text(a.CoverWarning, 2000));
    // CAA artwork can redirect only to the edition-addressed Internet Archive item.
    public static bool CoverUri(string? url, string? releaseId = null)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u) || u.Scheme != "https" || !u.IsDefaultPort || u.UserInfo.Length > 0
            || u.Query.Length > 0 || u.Fragment.Length > 0 || url!.Length > 2000 || u.AbsolutePath.Contains('%')) return false;
        if (u.Host == "coverartarchive.org")
        {
            var parts = u.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            return parts.Length == 3 && parts[0] == "release" && MusicBrainzReference.ValidId(parts[1])
                && (releaseId is null || parts[1] == releaseId) && Regex.IsMatch(parts[2], @"^\d+-(250|500|1200)\.jpg$");
        }
        if (ArchiveHost(u.Host))
        {
            var match = Regex.Match(u.AbsolutePath, @"^/(download|[0-9]+/items)/mbid-([a-f0-9-]{36})/[^/]+\.(jpg|png)$");
            return match.Success && MusicBrainzReference.ValidId(match.Groups[2].Value) && (releaseId is null || match.Groups[2].Value == releaseId);
        }
        return false;
    }
    public static bool ArchiveHost(string host) => host == "archive.org" || Regex.IsMatch(host, @"^(ia|dn)\d+\.(us|eu|ca)\.archive\.org$");
    public static bool Cache(MusicBrainzCacheValue? v)
    {
        if (v is null || v.CachedAtUtc == default || v.Key is null || v.Key.Length > 100) return false;
        if (v.Outlets is not null) return !v.Rejected && v.Search is null && v.Album is null && v.Binding is null && v.Apple is null
            && MusicOutlets.Valid(v.Outlets) && v.Outlets.CacheKey == v.Key && v.Outlets.FetchedAtUtc <= v.CachedAtUtc.AddMinutes(5);
        if (v.Apple is not null) return !v.Rejected && v.Search is null && v.Album is null && v.Binding is null
            && v.Apple.IsValid && v.Apple.Query.Key == v.Key && v.Apple.FetchedAtUtc <= v.CachedAtUtc.AddMinutes(5);
        if (v.Rejected && (v.Key.StartsWith("apple:", StringComparison.Ordinal) && Hash(v.Key[6..])
            || System.Text.RegularExpressions.Regex.IsMatch(v.Key, @"^outlets:(1|2):[a-f0-9-]{36}$") && MusicBrainzReference.ValidId(v.Key[10..])))
            return v.Search is null && v.Album is null && v.Binding is null;
        return true
        && (v.Rejected ? v.Search is null && v.Album is null && v.Binding is null &&
                (v.Key.StartsWith("release:", StringComparison.Ordinal) && MusicBrainzReference.ValidId(v.Key[8..])
                    || v.Key.StartsWith("group:", StringComparison.Ordinal) && MusicBrainzReference.ValidId(v.Key[6..])
                    || v.Key.StartsWith("search:", StringComparison.Ordinal) && Hash(v.Key[7..]))
            : v.Key.StartsWith("search:", StringComparison.Ordinal) && Hash(v.Key[7..]) && Search(v.Search) && v.Album is null && v.Binding is null
            || v.Key.StartsWith("group:", StringComparison.Ordinal) && MusicBrainzReference.ValidId(v.Key[6..]) && Search(v.Search)
                && v.Search!.Candidates.All(c => c.ReleaseGroupId == v.Key[6..]) && v.Album is null && v.Binding is null
            || v.Key.StartsWith("release:", StringComparison.Ordinal) && Album(v.Album) && v.Album!.Release.Id == v.Key[8..]
                && v.Album.FetchedAtUtc <= v.CachedAtUtc.AddMinutes(5) && v.Search is null && v.Binding is null
            || v.Key.StartsWith("binding:", StringComparison.Ordinal) && v.Binding is { } b && Hash(b.SourceFingerprint)
                && v.Key[8..] == b.SourceFingerprint && MusicBrainzReference.ValidId(b.ReleaseId) && v.Search is null && v.Album is null);
    }
    private static bool Hash(string value) => Regex.IsMatch(value, "^[A-F0-9]{64}$");
}
