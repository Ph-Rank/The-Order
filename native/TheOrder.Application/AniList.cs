using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TheOrder.Application;

public enum AniListMediaKind { Anime, Manga }
public sealed record AniListReference(int Id, AniListMediaKind Kind, bool MyAnimeList = false)
{
    public string Url => (MyAnimeList ? "https://myanimelist.net/" : "https://anilist.co/") + (Kind == AniListMediaKind.Anime ? "anime/" : "manga/") + Id;
    public static bool TryParse(string? value, out AniListReference? result)
    {
        result = null;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var u) || u.Scheme != "https" || !u.IsDefaultPort || u.UserInfo.Length > 0
            || u.Query.Length > 0 || u.Fragment.Length > 0 || u.Host is not ("anilist.co" or "myanimelist.net") || value != u.AbsoluteUri) return false;
        var parts = u.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length is < 2 or > 3 || parts[0] is not ("anime" or "manga") || !int.TryParse(parts[1], out var id)
            || id <= 0 || parts[1] != id.ToString(CultureInfo.InvariantCulture)) return false;
        result = new(id, parts[0] == "anime" ? AniListMediaKind.Anime : AniListMediaKind.Manga, u.Host == "myanimelist.net"); return true;
    }
}
public sealed record AniListSource(string BangumiId, AniListMediaKind Kind, string Title, string OriginalTitle,
    string? Date, string? Platform, int? Episodes, string[] ExternalLinks)
{
    public BookEntityLevel? BookLevel { get; init; }
    public bool HasAmbiguousExternalIdentity { get; init; }
    public string Fingerprint => Convert.ToHexString(SHA256.HashData(HasAmbiguousExternalIdentity
        ? JsonSerializer.SerializeToUtf8Bytes(new { BangumiId, Kind, Title, OriginalTitle, Date, Platform, Episodes, ExternalLinks, BookLevel, HasAmbiguousExternalIdentity })
        : Kind == AniListMediaKind.Anime
        ? JsonSerializer.SerializeToUtf8Bytes(new { BangumiId, Kind, Title, OriginalTitle, Date, Platform, Episodes, ExternalLinks })
        : JsonSerializer.SerializeToUtf8Bytes(new { BangumiId, Kind, Title, OriginalTitle, Date, Platform, Episodes, ExternalLinks, BookLevel })));
    public string? ExpectedFormat => Kind == AniListMediaKind.Anime ? Platform?.ToUpperInvariant() switch
        { "TV" => "TV", "剧场版" or "MOVIE" => "MOVIE", "OVA" => "OVA", "WEB" or "ONA" => "ONA", _ => null }
        : Platform?.Contains("小说", StringComparison.Ordinal) == true ? "NOVEL" : Platform?.Contains("漫画", StringComparison.Ordinal) == true ? "MANGA" : null;
    public bool IsValid => BangumiRelationIdentityResolver.ValidSubjectId(BangumiId) && Enum.IsDefined(Kind)
        && AniListValidation.Text(Title, 2000, false) && AniListValidation.Text(OriginalTitle, 2000)
        && (Date is null || AniListValidation.Date(Date)) && AniListValidation.OptionalText(Platform, 200)
        && (Episodes is null or > 0 and <= 1000000) && ExternalLinks is { Length: <= 16 }
        && ExternalLinks.All(x => AniListReference.TryParse(x, out var r) && r!.Kind == Kind);
    public static AniListSource From(BangumiSubjectDetails value)
    {
        if (value.Kind is not (BangumiRelationKind.Anime or BangumiRelationKind.Book) || !BangumiSubjectDetailsService.Valid(value.Id, value.Kind, value))
            throw new InvalidDataException("这类作品不使用 AniList 资料。");
        return new(value.Id, value.Kind == BangumiRelationKind.Anime ? AniListMediaKind.Anime : AniListMediaKind.Manga,
            value.Title, value.OriginalTitle, value.ReleaseDate, value.Platform, value.EpisodeCount, value.ExternalLinks)
            { BookLevel = value.Kind == BangumiRelationKind.Book ? value.BookFacts?.Level ?? BookEntityLevel.Unknown : null,
                HasAmbiguousExternalIdentity = value.HasAmbiguousExternalIdentity };
    }
}
public sealed record AniListMedia(int Id, int? MalId, AniListMediaKind Kind, string Title, string NativeTitle,
    string EnglishTitle, string Format, string Status, string? CoverUrl, string? StartDate, string? EndDate,
    int? Episodes, int? Chapters, int? Volumes, string[] Synonyms)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RomajiTitle { get; init; }
    public string Url => new AniListReference(Id, Kind).Url;
    public string TypeName => Kind == AniListMediaKind.Anime ? "动画" : Format.Length == 0 ? "书籍" : Format == "NOVEL" ? "轻小说 / 小说" : "漫画";
}
public sealed record AniListPerson(int Id, string Name, string NativeName, string? ImageUrl);
public sealed record AniListVoiceRole(AniListPerson Person, string? RoleNotes, string? DubGroup);
public sealed record AniListCharacter(AniListPerson Person, string Role, AniListPerson[] VoiceActors)
{
    public AniListVoiceRole[] VoiceRoles { get; init; } = [];
}
public sealed record AniListStaff(AniListPerson Person, string Role);
public sealed record AniListRelation(AniListMedia Media, string Relation);
public sealed record AniListDetails(AniListMedia Media, string Description, string[] Studios, string[] Genres,
    string[] Tags, AniListCharacter[] Characters, AniListStaff[] Staff, AniListRelation[] Relations,
    int PeoplePage, bool MoreCharacters, bool MoreStaff, DateTimeOffset FetchedAtUtc)
{
    // Optional additions keep existing profile caches readable.
    public AniListWatchLink[] WatchLinks { get; init; } = [];
    public AniListTrailer? Trailer { get; init; }
    public bool HasPlaybackMetadata { get; init; }
}
public sealed record AniListBinding(string SourceFingerprint, int MediaId, AniListMediaKind Kind, string Evidence)
{
    // Legacy user confirmations remain valid. Old automatic choices must be
    // re-evaluated because their candidate search did not record completeness.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int AutomaticPolicyVersion { get; init; }
    [JsonIgnore] public bool NeedsReview { get; init; }
    [JsonIgnore] public string? ConfirmedFormat { get; init; }
    [JsonIgnore] public bool IsUserDecision { get; init; }
}
public sealed record AniListCacheValue(string Key, DateTimeOffset CachedAtUtc, AniListMedia[]? Search = null,
    AniListDetails? Details = null, AniListBinding? Binding = null, bool Rejected = false)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool SearchComplete { get; init; }
}
public sealed record AniListSearchCandidates(AniListMedia[] Items, bool Complete);
public sealed record AniListMatch(AniListMedia[] Candidates, AniListBinding? Binding, string? Notice = null)
{
    public int[] RejectedIds { get; init; } = [];
}
public sealed record AniListResult(AniListDetails Details, bool FromCache, bool IsStale, string? Warning);
public enum AniListFailure { Offline, Timeout, RateLimited, Server, Unavailable, InvalidResponse, ResponseTooLarge }
public sealed class AniListException(AniListFailure failure, string message) : Exception(message)
{ public AniListFailure Failure { get; } = failure; }
public interface IAniListProvider
{
    Task<AniListMedia[]> SearchAsync(AniListSource source, string? query, CancellationToken cancellation);
    // Existing adapters remain usable, but a bounded array is not proof that
    // all candidates were examined. Only an adapter with pagination evidence
    // may opt into automatic association.
    async Task<AniListSearchCandidates> SearchCandidatesAsync(AniListSource source, string? query, CancellationToken cancellation)
        => new(await SearchAsync(source, query, cancellation).ConfigureAwait(false), false);
    Task<AniListMedia> ResolveAsync(AniListReference reference, CancellationToken cancellation);
    Task<AniListDetails> FetchAsync(int id, AniListMediaKind kind, int peoplePage, CancellationToken cancellation);
}
public interface IAniListCache
{
    Task<AniListCacheValue?> ReadAsync(string key, CancellationToken cancellation);
    Task WriteAsync(AniListCacheValue value, CancellationToken cancellation);
}
public interface IAniListReader
{
    Task<AniListMatch> MatchAsync(AniListSource source, string? query, bool refresh, CancellationToken cancellation);
    Task<AniListMatch> FindCandidatesAsync(AniListSource source, bool refresh, CancellationToken cancellation);
    Task<AniListResult> LoadAsync(int id, AniListMediaKind kind, bool refresh, int peoplePage, CancellationToken cancellation);
    Task ConfirmAsync(AniListSource source, AniListDetails details, CancellationToken cancellation);
    Task RejectAsync(AniListSource source, AniListMedia media, CancellationToken cancellation)
        => Task.FromException(new NotSupportedException("当前资料读取器不支持修改关联。"));
    Task RevokeAsync(AniListSource source, int expectedMediaId, CancellationToken cancellation)
        => Task.FromException(new NotSupportedException("当前资料读取器不支持修改关联。"));
}
public sealed record AniListAssessment(bool Conflict, string[] Evidence);
public static class AniListMatching
{
    // Manual intent can acknowledge title/date discrepancies. Media, format,
    // publication level and explicit identity boundaries still have meaning.
    public static bool ManualCompatible(AniListSource source, AniListMedia media)
    {
        if (!source.IsValid || !AniListValidation.Media(media) || source.Kind != media.Kind || source.HasAmbiguousExternalIdentity) return false;
        if (source.Kind == AniListMediaKind.Manga && source.BookLevel is BookEntityLevel.Volume or BookEntityLevel.Publication) return false;
        if (source.ExpectedFormat is { } format && format != media.Format && !(format == "MANGA" && media.Format == "ONE_SHOT")) return false;
        return source.ExternalLinks.All(x => AniListReference.TryParse(x, out var r) && r!.Kind == media.Kind
            && (r.MyAnimeList ? media.MalId == r.Id : media.Id == r.Id));
    }
    private static string Normalize(string value) => string.Concat(value.Normalize(NormalizationForm.FormKC).Where(char.IsLetterOrDigit)).ToUpperInvariant();
    // A title alone is never identity evidence. Automatic association requires
    // one compatible identity with exact multilingual title, format and full
    // premiere date (allowing the small timezone difference between providers).
    public static AniListMedia? AutomaticCandidate(AniListSource source, IEnumerable<AniListMedia> candidates)
    {
        var winners = candidates.Where(x => StrongEvidence(source, x)).DistinctBy(x => x.Id).Take(2).ToArray();
        return winners.Length == 1 ? winners[0] : null;
    }
    public static bool StrongEvidence(AniListSource source, AniListMedia media)
    {
        // A Book date can refer to serialization, a collected volume, reprint or
        // edition. The anime premiere heuristic is not a book identity rule.
        if (source.Kind != AniListMediaKind.Anime) return false;
        if (!source.IsValid || !AniListValidation.Media(media) || Assess(source, media).Conflict
            || source.ExpectedFormat is not { } format || format != media.Format
            || source.Date is not { Length: 10 } date || media.StartDate is not { Length: 10 } start
            || !DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)
            || !DateOnly.TryParseExact(start, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var other)
            || Math.Abs(day.DayNumber - other.DayNumber) > 3) return false;
        // TV counts can differ by one extra episode. This relaxation requires
        // the exact same premiere day; broader count/date conflicts abstain.
        if (source.Episodes is { } eps && media.Episodes is { } count && eps != count
            && !(format == "TV" && day == other && Math.Abs(eps - count) == 1)) return false;
        var titles = new[] { media.Title, media.NativeTitle, media.EnglishTitle }.Concat(media.Synonyms)
            .Where(x => !string.IsNullOrWhiteSpace(x)).Select(Normalize).ToHashSet(StringComparer.Ordinal);
        return new[] { source.Title, source.OriginalTitle }.Where(x => !string.IsNullOrWhiteSpace(x)).Select(Normalize).Any(titles.Contains);
    }
    public static AniListAssessment Assess(AniListSource source, AniListMedia media)
    {
        var evidence = new List<string>(); bool conflict = source.Kind != media.Kind;
        evidence.Add(conflict ? "媒体类型不同" : "媒体类型一致");
        if (source.HasAmbiguousExternalIdentity)
        { conflict = true; evidence.Add("来源外部编号不完整或有歧义，请先核对来源记录"); }
        if (source.Kind == AniListMediaKind.Manga && source.BookLevel is BookEntityLevel.Volume or BookEntityLevel.Publication && media.Volumes is > 1)
        { conflict = true; evidence.Add("单卷 / 出版记录不能关联到多卷条目"); }
        var titles = new[] { media.Title, media.NativeTitle, media.EnglishTitle }.Concat(media.Synonyms).Where(x => x.Length > 0).Select(Normalize).ToArray();
        evidence.Add(new[] { source.Title, source.OriginalTitle }.Where(x => x.Length > 0).Select(Normalize).Any(titles.Contains) ? "标题有对应项" : "请核对不同语言的标题");
        if (source.ExpectedFormat is { } expected)
        {
            bool same = expected == media.Format || expected == "MANGA" && media.Format == "ONE_SHOT";
            conflict |= !same; evidence.Add(same ? "发行形式一致" : $"发行形式不同：{source.Platform} / {media.Format}");
        }
        if (source.Date is { Length: >= 4 } date && media.StartDate is { Length: >= 4 } start)
        {
            var same = date[..4] == start[..4];
            if (source.Kind == AniListMediaKind.Anime)
            { conflict |= !same; evidence.Add(same ? "发行年份一致" : $"发行年份不同：{date} / {start}"); }
            // Book dates can describe serialization or a collected publication.
            // Keep both values as a warning, rather than rejecting a deliberate
            // user choice. Book automatic matching remains disabled above.
            else evidence.Add(same ? "记录年份一致（仅供参考）"
                : $"日期不同：Bangumi {date} / AniList {start}（出版与连载口径可能不同，请核对）");
        }
        if (source.Episodes is { } eps && source.Kind == AniListMediaKind.Anime && media.Episodes is { } actual)
            evidence.Add(eps == actual ? "集数一致" : $"集数口径不同：Bangumi {eps} / AniList {actual}");
        var refs = source.ExternalLinks.Select(x => { AniListReference.TryParse(x, out var r); return r; }).Where(r => r is not null).ToArray();
        if (refs.Length > 0)
        {
            bool agrees = refs.All(r => r!.Kind == media.Kind && (r.MyAnimeList ? media.MalId == r.Id : media.Id == r.Id));
            conflict |= !agrees; evidence.Add(agrees ? "原站明确编号对应" : "原站编号与候选不一致");
        }
        return new(conflict, evidence.ToArray());
    }
}
public static class AniListValidation
{
    public static bool Text(string? value, int max, bool empty = true) => value is not null && value.Length <= max
        && (empty || !string.IsNullOrWhiteSpace(value)) && !value.Any(c => char.IsControl(c) && c is not ('\n' or '\r' or '\t'));
    public static bool OptionalText(string? value, int max) => value is null || Text(value, max);
    public static bool Date(string value) => value.Length is 4 or 7 or 10 && DateTime.TryParse(value + (value.Length == 4 ? "-01-01" : value.Length == 7 ? "-01" : ""), CultureInfo.InvariantCulture, DateTimeStyles.None, out _);
    public static bool Image(string? url) => url is { Length: <= 2048 } && Uri.TryCreate(url, UriKind.Absolute, out var u)
        && u.Scheme == "https" && u.IsDefaultPort && u.UserInfo.Length == 0 && u.Query.Length == 0 && u.Fragment.Length == 0
        && u.Host == "s4.anilist.co" && u.AbsolutePath.StartsWith("/file/anilistcdn/", StringComparison.Ordinal)
        && url == u.AbsoluteUri && !url.Contains("/../", StringComparison.Ordinal);
    public static bool Media(AniListMedia? m) => MediaCore(m, requireFormat: true);
    public static bool RelationMedia(AniListMedia? m) => MediaCore(m, requireFormat: false);
    private static bool MediaCore(AniListMedia? m, bool requireFormat) => m is not null && m.Id > 0 && Enum.IsDefined(m.Kind) && (m.MalId is null or > 0)
        && Text(m.Title, 2000, false) && Text(m.NativeTitle, 2000) && Text(m.EnglishTitle, 2000) && OptionalText(m.RomajiTitle, 2000)
        && (Text(m.Format, 64, false) || !requireFormat && m.Format == "")
        && Text(m.Status, 64) && (m.CoverUrl is null || Image(m.CoverUrl)) && (m.StartDate is null || Date(m.StartDate))
        && (m.EndDate is null || Date(m.EndDate)) && (m.Episodes is null or > 0 and <= 1000000)
        && (m.Chapters is null or > 0 and <= 1000000) && (m.Volumes is null or > 0 and <= 1000000) && Texts(m.Synonyms, 30, 2000);
    public static bool Person(AniListPerson? p) => p is not null && p.Id > 0 && Text(p.Name, 1000, false) && Text(p.NativeName, 1000) && (p.ImageUrl is null || Image(p.ImageUrl));
    public static bool Texts(string[]? values, int max, int length) => values is not null && values.Length <= max && values.All(x => Text(x, length, false));
    public static bool Details(AniListDetails? d) => d is not null && Media(d.Media) && Text(d.Description, 32768)
        && Texts(d.Studios, 40, 1000) && Texts(d.Genres, 50, 200) && Texts(d.Tags, 100, 200)
        && d.Characters is { Length: <= 24 } && d.Characters.All(x => x is not null && Person(x.Person) && Text(x.Role, 100) && x.VoiceActors is { Length: <= 8 } && x.VoiceActors.All(Person)
            && x.VoiceRoles is { Length: <= 8 } && x.VoiceRoles.All(r => r is not null && Person(r.Person) && OptionalText(r.RoleNotes, 1000) && OptionalText(r.DubGroup, 500)))
        && d.Staff is { Length: <= 32 } && d.Staff.All(x => x is not null && Person(x.Person) && Text(x.Role, 300))
        && d.Relations is { Length: <= 100 } && d.Relations.All(x => x is not null && RelationMedia(x.Media) && Text(x.Relation, 100, false))
        && d.WatchLinks is { Length: <= 24 } && d.WatchLinks.All(AniListPlayback.ValidWatchLink)
        && (d.Trailer is null || AniListPlayback.ValidTrailer(d.Trailer))
        && d.PeoplePage is > 0 and <= 10 && d.FetchedAtUtc != default;
    public static bool Cache(AniListCacheValue? v) => v is not null && Text(v.Key, 100, false) && v.CachedAtUtc != default
        && (v.Rejected ? v.Search is null && v.Details is null && v.Binding is null
            : (v.Search is not null ? 1 : 0) + (v.Details is not null ? 1 : 0) + (v.Binding is not null ? 1 : 0) == 1)
        && (v.Search is null || v.Search.Length <= 20 && v.Search.All(Media)) && (v.Details is null || Details(v.Details))
        && (v.Binding is null || v.Binding.SourceFingerprint is { Length: 64 } && v.Binding.SourceFingerprint.All(Uri.IsHexDigit)
            && v.Binding.MediaId > 0 && Enum.IsDefined(v.Binding.Kind) && Text(v.Binding.Evidence, 1000, false)
            && v.Binding.AutomaticPolicyVersion is >= 0 and <= 1)
        && (v.Binding is null || v.Key == "binding:" + v.Binding.SourceFingerprint)
        && (v.Details is null || v.Key == $"media:{(int)v.Details.Media.Kind}:{v.Details.Media.Id}:{v.Details.PeoplePage}");
}
