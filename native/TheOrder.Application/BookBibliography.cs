using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;

namespace TheOrder.Application;

// Only the levels evidenced by the provider are used. ISBN is a publication
// identifier, never a work/series identity. These records do not enter library.json.
public enum BookEntityLevel { Unknown, Series, Volume, Publication, Aggregate }
public sealed record BookFactValue(string Name, string[] Values);
public sealed record BookIdentity(string Provider, string Id, BookEntityLevel Level)
{
    public string Key => Provider + ":" + Id + ":" + Level;
    public bool IsValid => Enum.IsDefined(Level) && (Provider switch
    {
        "Bangumi" => BangumiRelationIdentityResolver.ValidSubjectId(Id),
        "AniList" => int.TryParse(Id, NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id > 0 && Id == id.ToString(CultureInfo.InvariantCulture),
        "MangaDex" => Level == BookEntityLevel.Series && Guid.TryParseExact(Id, "D", out var uuid) && uuid != Guid.Empty && Id == uuid.ToString("D"),
        "NDL" => BookValidation.NdlId(Id) && Level is BookEntityLevel.Publication or BookEntityLevel.Aggregate,
        _ => false
    });
}
public sealed record BangumiBookFacts(bool? IsSeries, int? Volumes, int? Chapters,
    string[] Isbns, string[] Authors, string[] Illustrators, string[] Publishers,
    string[] Magazines, string[] Imprints, string? Volume)
{
    public BookFactValue[] RawFacts { get; init; } = [];
    public BookExternalLink[] ExternalLinks { get; init; } = [];
    public bool HasExternalLinkMetadata { get; init; }
    public BookEntityLevel Level => IsSeries == true ? BookEntityLevel.Series
        : Volume is { Length: > 0 } ? BookEntityLevel.Volume
        : IsSeries == false && Isbns.Length > 0 ? BookEntityLevel.Publication : BookEntityLevel.Unknown;
    public bool IsValid => Volumes is null or >= 0 and <= 1000000 && Chapters is null or >= 0 and <= 1000000
        && BookValidation.Isbns(Isbns) && new[] { Authors, Illustrators, Publishers, Magazines, Imprints }.All(BookValidation.Texts)
        && AniListValidation.OptionalText(Volume, 200) && RawFacts is { Length: <= 64 }
        && RawFacts.All(x => x is not null && AniListValidation.Text(x.Name, 100, false) && BookValidation.Texts(x.Values))
        && ExternalLinks is { Length: <= 96 } && ExternalLinks.All(BookOutlets.ValidRaw);
}
public sealed record BookSource(BookIdentity Identity, string Title, string OriginalTitle,
    string Format, string[] Creators, string[] Isbns, string? Volume)
{
    public string Fingerprint => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { Identity, Title, OriginalTitle, Format, Creators, Isbns, Volume })));
    public bool IsValid => Identity is not null && Identity.IsValid && Identity.Provider is "Bangumi" or "AniList" && AniListValidation.Text(Title, 2000, false)
        && AniListValidation.Text(OriginalTitle, 2000) && AniListValidation.Text(Format, 200) && BookValidation.Texts(Creators)
        && BookValidation.Isbns(Isbns) && AniListValidation.OptionalText(Volume, 200);
    public static BookSource From(BangumiSubjectDetails book)
    {
        if (book.Kind != BangumiRelationKind.Book || !BangumiSubjectDetailsService.Valid(book.Id, book.Kind, book)) throw new InvalidDataException("书籍身份无效。");
        var f = book.BookFacts;
        return new(new("Bangumi", book.Id, f?.Level ?? BookEntityLevel.Unknown), book.Title, book.OriginalTitle,
            book.Platform ?? "", (f?.Authors ?? []).Concat(f?.Illustrators ?? []).Distinct().ToArray(), f?.Isbns ?? [], f?.Volume);
    }
    public static BookSource From(AniListDetails book)
    {
        if (book.Media.Kind != AniListMediaKind.Manga || !AniListValidation.Details(book)) throw new InvalidDataException("书籍身份无效。");
        var m = book.Media;
        // AniList NOVEL/MANGA is a medium/format, not a guaranteed series level.
        return new(new("AniList", m.Id.ToString(CultureInfo.InvariantCulture), BookEntityLevel.Unknown),
            m.Title, m.NativeTitle, m.Format, book.Staff.Select(x => x.Person.NativeName.Length > 0 ? x.Person.NativeName : x.Person.Name).Distinct().Take(64).ToArray(), [], null);
    }
}
public sealed record BookPublication(string Id, string Title, string BaseTitle, string? Volume,
    string[] Isbns, string[] SetIsbns, string[] CreatorStatements, string[] Publishers,
    string[] BibliographicSeries, string? PublicationDate, string? Edition, string[] Languages,
    bool IsAggregate, DateTimeOffset FetchedAtUtc)
{
    public BookIdentity Identity => new("NDL", Id, IsAggregate ? BookEntityLevel.Aggregate : BookEntityLevel.Publication);
    public string Url => "https://ndlsearch.ndl.go.jp/books/R100000002-I" + Id;
    public string License => "CC BY 4.0";
}
public sealed record BookSearchQuery(string Title, string? Isbn = null, string? Creator = null, int Start = 1)
{
    public string Key => "search:" + Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { Title, Isbn, Creator, Start })));
    public bool IsValid => AniListValidation.Text(Title, 200, Isbn is not null) && AniListValidation.OptionalText(Creator, 200)
        && (Isbn is null || BookIsbn.Normalize(Isbn) == Isbn) && Start is >= 1 and <= 91;
}
public sealed record BookSearchPage(BookPublication[] Publications, int Total, int? NextStart, DateTimeOffset FetchedAtUtc);
public sealed record BookSearchResult(BookSearchPage Page, bool FromCache, bool IsStale, string? Warning);
public sealed record BookPublicationResult(BookPublication Publication, bool FromCache, bool IsStale, string? Warning)
{
    // SRU supplies full bibliographic facts, but only an independent record read
    // can authorize a new user mapping. This flag is presentation-only, never persisted.
    public bool FromSearch { get; init; }
}
public sealed record BookPublicationBinding(BookIdentity SourceIdentity, string SourceFingerprint,
    string PublicationId, DateTimeOffset ConfirmedAtUtc, string Evidence)
{
    // A projection state, never a silent rewrite of the original confirmation.
    [System.Text.Json.Serialization.JsonIgnore] public bool NeedsReview { get; init; }
}
public sealed record BookCacheValue(string Key, DateTimeOffset CachedAtUtc, BookSearchPage? Search = null, BookPublication? Publication = null, bool Rejected = false);
public enum BookFailure { Offline, Timeout, RateLimited, Server, Unavailable, InvalidResponse, ResponseTooLarge }
public sealed class BookProviderException(BookFailure failure, string message) : Exception(message) { public BookFailure Failure { get; } = failure; }
public interface IBookBibliographyProvider
{
    Task<BookSearchPage> SearchAsync(BookSearchQuery query, CancellationToken cancellation);
    Task<BookPublication> FetchAsync(string id, CancellationToken cancellation);
}
public interface IBookBibliographyCache
{
    Task<BookCacheValue?> ReadAsync(string key, CancellationToken cancellation);
    Task WriteAsync(BookCacheValue value, CancellationToken cancellation);
}
// Separate persistent user choices; never part of disposable remote-cache eviction.
public interface IBookPublicationBindings
{
    Task<BookPublicationBinding[]> ReadAsync(BookIdentity source, CancellationToken cancellation);
    Task WriteAsync(BookIdentity source, BookPublicationBinding[] bindings, CancellationToken cancellation);
}
public interface IBookBibliographyReader
{
    Task<BookSearchResult> SearchAsync(BookSearchQuery query, bool refresh, CancellationToken cancellation);
    Task<BookPublicationResult> LoadAsync(string id, bool refresh, CancellationToken cancellation);
    Task<BookPublicationBinding[]> BindingsAsync(BookSource source, CancellationToken cancellation);
    Task ConfirmAsync(BookSource source, BookPublication publication, CancellationToken cancellation);
    Task RemoveAsync(BookSource source, string publicationId, CancellationToken cancellation);
}
public interface IBangumiBookRelationsProvider { Task<BangumiRelationsSnapshot> FetchBookAsync(string id, CancellationToken cancellation); }
public interface IBangumiBookRelationsReader { Task<BangumiRelationsLoadResult> LoadAsync(string id, bool refresh, CancellationToken cancellation); }
public static class BookIsbn
{
    public static string? Normalize(string? text)
    {
        if (text is null || text.Length > 40 || text.Any(c => !(char.IsAsciiDigit(c) || c is 'X' or 'x' or '-' or ' '))) return null;
        var s = text.Replace("-", "").Replace(" ", "").ToUpperInvariant();
        if (s.Length == 10 && s[..9].All(char.IsAsciiDigit) && (char.IsAsciiDigit(s[9]) || s[9] == 'X')
            && s.Select((c, i) => (10 - i) * (c == 'X' ? 10 : c - '0')).Sum() % 11 == 0)
        { var prefix = "978" + s[..9]; return prefix + ((10 - prefix.Select((c, i) => (c - '0') * (i % 2 == 0 ? 1 : 3)).Sum() % 10) % 10); }
        return s.Length == 13 && s.All(char.IsAsciiDigit) && s.StartsWith("97", StringComparison.Ordinal)
            && s[2] is '8' or '9' && s.Select((c, i) => (c - '0') * (i % 2 == 0 ? 1 : 3)).Sum() % 10 == 0 ? s : null;
    }
}
public static class BookValidation
{
    public static bool NdlId(string? id) => id is { Length: 9 or 12 } && id.All(char.IsAsciiDigit);
    public static bool Texts(string[]? values) => values is { Length: <= 64 } && values.All(x => AniListValidation.Text(x, 2000, false));
    public static bool Isbns(string[]? values) => values is { Length: <= 64 } && values.All(x => x is not null && BookIsbn.Normalize(x) == x);
    public static bool Publication(BookPublication? p) => p is not null && NdlId(p.Id) && AniListValidation.Text(p.Title, 2000, false)
        && AniListValidation.Text(p.BaseTitle, 2000, false) && AniListValidation.OptionalText(p.Volume, 200)
        && Isbns(p.Isbns) && Isbns(p.SetIsbns) && new[] { p.CreatorStatements, p.Publishers, p.BibliographicSeries, p.Languages }.All(Texts)
        && AniListValidation.OptionalText(p.PublicationDate, 200) && AniListValidation.OptionalText(p.Edition, 200) && p.FetchedAtUtc != default;
    public static bool Page(BookSearchPage? p) => p is not null && p.Publications is { Length: <= 20 } && p.Publications.All(Publication)
        && p.Publications.Select(x => x.Id).Distinct().Count() == p.Publications.Length && p.Total >= p.Publications.Length && p.Total <= 10000000
        && (p.NextStart is null or >= 1 and <= 91) && p.FetchedAtUtc != default;
    public static bool Cache(BookCacheValue? v) => v is not null && AniListValidation.Text(v.Key, 100, false) && v.CachedAtUtc != default
        && (v.Rejected ? v.Search is null && v.Publication is null
            : v.Search is not null && v.Publication is null && Page(v.Search) || v.Search is null && Publication(v.Publication));
    public static bool Binding(BookPublicationBinding? b) => b is not null && b.SourceIdentity is not null && b.SourceIdentity.IsValid && b.SourceIdentity.Provider != "NDL"
        && b.SourceFingerprint is { Length: 64 } && b.SourceFingerprint.All(char.IsAsciiHexDigit) && NdlId(b.PublicationId)
        && b.ConfirmedAtUtc != default && AniListValidation.Text(b.Evidence, 1000, false);
}
