namespace TheOrder.Application;

public sealed partial class AniListService
{
    public async Task<BookLinkSupplementResult> LoadBookLinksAsync(AniListMedia source, bool refresh, CancellationToken cancellation)
    {
        if (!AniListValidation.Media(source) || source.Kind != AniListMediaKind.Manga || source.Format is not ("MANGA" or "ONE_SHOT"))
            throw new InvalidDataException("仅可为当前漫画读取补充入口。");
        var empty = new BookLinkSupplement(source.Id, source.Format, source.NativeTitle.Length > 0 ? source.NativeTitle : source.Title,
            null, null, null, [], Now, null);
        if (bookSupplementProvider is null) return new(empty, null);
        var result = await LoadCacheAsync(empty.Key, refresh, FreshFor, async token =>
        {
            var value = await bookSupplementProvider.FetchAsync(source, token).ConfigureAwait(false);
            if (!value.Matches(source)) throw new AniListException(AniListFailure.InvalidResponse, "补充入口的跨站编号不一致。");
            return new AniListCacheValue(value.Key, value.FetchedAtUtc) { BookSupplement = value };
        }, cancellation, preservePreviousOnFailure: true).ConfigureAwait(false);
        var snapshot = result.Value.BookSupplement;
        if (snapshot is null || !snapshot.Matches(source)) throw new InvalidDataException("补充入口与当前漫画身份不一致。");
        return new(snapshot, result.Warning is null ? snapshot.Notice : "MangaDex 暂时无法更新，保留之前读取的入口。");
    }
}
