namespace TheOrder.Application;

// A provider assertion scoped to one AniList manga, not a user-confirmed mapping.
// No chapters, covers, account state or publication identity is imported.
public sealed record BookLinkSupplement(int AniListId, string Format, string Query, string? MangaDexId,
    string? Title, int? MalId, BookExternalLink[] Links, DateTimeOffset FetchedAtUtc, string? Notice)
{
    public string Key => $"booklinks:md-v1:{Format}:{AniListId}";
    public bool IsValid => AniListId > 0 && Format is "MANGA" or "ONE_SHOT" && AniListValidation.Text(Query, 2000, false)
        && FetchedAtUtc != default && AniListValidation.OptionalText(Notice, 500) && Links is { Length: <= 32 } && Links.All(BookOutlets.ValidRaw)
        && (MangaDexId is null ? Title is null && MalId is null && Links.Length == 0
            : new BookIdentity("MangaDex", MangaDexId, BookEntityLevel.Series).IsValid && AniListValidation.Text(Title, 2000, false) && MalId is null or > 0);
    public bool Matches(AniListMedia source) => IsValid && source.Kind == AniListMediaKind.Manga && source.Id == AniListId && source.Format == Format
        && (MalId is null || source.MalId is null || MalId == source.MalId);
    public BookOutletSet Outlets()
    {
        if (MangaDexId is null) return new([], []);
        var parsed = BookOutlets.From(new("MangaDex", MangaDexId, BookEntityLevel.Series), Format, "https://mangadex.org/title/" + MangaDexId, Links, FetchedAtUtc);
        return parsed with { Links = parsed.Links.Select(link => link with { Sources = link.Sources.Select(proof => proof with
            { CrossReference = $"来源声明 AniList:{AniListId}；标题一致" + (MalId is { } mal ? $"；MyAnimeList:{mal}" : "") }).ToArray() }).ToArray() };
    }
}
public interface IBookOutletSupplementProvider
{
    Task<BookLinkSupplement> FetchAsync(AniListMedia source, CancellationToken cancellation);
}
public sealed record BookLinkSupplementResult(BookLinkSupplement Value, string? Warning);
public interface IBookOutletSupplementReader
{
    Task<BookLinkSupplementResult> LoadBookLinksAsync(AniListMedia source, bool refresh, CancellationToken cancellation);
}
