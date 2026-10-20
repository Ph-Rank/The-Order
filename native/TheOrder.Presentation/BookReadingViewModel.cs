using TheOrder.Application;

namespace TheOrder.Presentation;

// Owns only a view of provider links. User confirmations continue to belong to
// the existing identity store; no external address is requested or persisted here.
public sealed class BookReadingViewModel(IAniListReader aniListReader, IBangumiSubjectDetailsReader bangumiReader,
    IBookOutletSupplementReader? supplements = null)
    : ObservableState, IDisposable
{
    private CancellationTokenSource? request;
    private long generation;
    private bool disposed, complete;
    private string? key;
    private AniListDetails? aniList;
    private BangumiSubjectDetails? bangumi;
    private BookLinkSupplement? supplement;
    public BookSource? Source { get; private set; }
    public BookOutletLink[] Links { get; private set; } = [];
    public BookLinkReview[] Excluded { get; private set; } = [];
    public bool Expanded { get; private set; }
    public bool Busy => request is not null;
    public bool CanLoad => !disposed && Source is not null && !Busy;
    public string Message { get; private set; } = "";
    public long Version => generation;

    // The host supplies AniList only for this exact ID or an established root
    // bridge. A related volume never inherits the root's AniList identity.
    public void SetSource(BookSource? source, AniListDetails? details, BangumiSubjectDetails? subject)
    {
        if (disposed) return;
        if (source is not { IsValid: true }) { Deactivate(); return; }
        if (details is not null && (details.Media.Kind != AniListMediaKind.Manga || !AniListValidation.Details(details)
            || source.Identity.Provider == "AniList" && source.Identity.Id != details.Media.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)
            || source.Identity.Provider == "Bangumi" && source.Identity.Level is BookEntityLevel.Volume or BookEntityLevel.Publication
            || BookOutlets.Medium(source.Format) is { Length: > 0 } medium && medium != BookOutlets.Medium(details.Media.Format)))
            throw new InvalidDataException("阅读入口的作品类型或层级不一致。");
        if (subject is not null && (subject.Kind != BangumiRelationKind.Book || source.Identity.Provider != "Bangumi"
            || source.Identity.Id != subject.Id || !BangumiSubjectDetailsService.Valid(subject.Id, subject.Kind, subject)))
            throw new InvalidDataException("阅读入口的书籍编号不一致。");
        var next = source.Fingerprint + ":" + details?.Media.Id + ":" + subject?.Id;
        if (key == next)
        {
            bool changed = false;
            if (details is { HasBookLinkMetadata: true } && !ReferenceEquals(details, aniList) && (aniList is null || details.FetchedAtUtc >= aniList.FetchedAtUtc)) { aniList = details; changed = true; }
            if (subject?.BookFacts is { HasExternalLinkMetadata: true } && !ReferenceEquals(subject, bangumi) && (bangumi is null || subject.FetchedAtUtc >= bangumi.FetchedAtUtc)) { bangumi = subject; changed = true; }
            if (changed) { Rebuild(); Changed(null); }
            return;
        }
        Stop(); key = next; Source = source; aniList = details; bangumi = subject; supplement = null;
        Expanded = complete = false; Message = ""; Rebuild(); Changed(null);
    }
    public Task ExpandAsync(bool refresh = false)
    {
        if (disposed || Source is null) return Task.CompletedTask;
        Expanded = true; Changed(null);
        return Busy || complete && !refresh ? Task.CompletedTask : LoadAsync(refresh);
    }
    private async Task LoadAsync(bool refresh)
    {
        var owned = request = new(); var token = owned.Token; var version = ++generation;
        bool Accept() => !disposed && !token.IsCancellationRequested && generation == version && ReferenceEquals(request, owned);
        Message = "读取阅读与购买入口…"; Changed(null);
        var warnings = new List<string>(); bool failed = false;
        try
        {
            if (aniList is { } original && (refresh || !original.HasBookLinkMetadata))
            {
                try
                {
                    var result = await aniListReader.LoadAsync(original.Media.Id, AniListMediaKind.Manga, true, 1, token);
                    if (!Accept()) return;
                    if (result.Details.Media.Id != original.Media.Id || result.Details.Media.Kind != AniListMediaKind.Manga
                        || result.Details.Media.Format != original.Media.Format || !AniListValidation.Details(result.Details))
                        throw new InvalidDataException("作品类型发生变化，未使用新入口。");
                    aniList = result.Details; Rebuild();
                    if (result.Warning is { Length: > 0 } warning) warnings.Add(warning);
                    if (!aniList.HasBookLinkMetadata) { warnings.Add("尚未取得新的入口字段，可稍后刷新。"); failed = true; }
                    Changed(null);
                }
                catch (Exception error) when (error is AniListException or IOException or UnauthorizedAccessException or InvalidDataException)
                { if (!Accept()) return; failed = true; warnings.Add(error is InvalidDataException ? "作品类型或编号发生变化，保留原入口。" : "AniList 暂时无法更新，保留已有资料。"); }
            }
            if (!Accept()) return;
            if (bangumi is { } originalBg && (refresh || originalBg.BookFacts?.HasExternalLinkMetadata != true))
            {
                try
                {
                    var result = await bangumiReader.LoadAsync(originalBg.Id, BangumiRelationKind.Book, true, token);
                    if (!Accept()) return;
                    var updated = result.Details;
                    if (updated.Id != originalBg.Id || updated.Kind != BangumiRelationKind.Book
                        || BookSource.From(updated).Identity.Level != BookSource.From(originalBg).Identity.Level
                        || BookOutlets.Medium(updated.Platform ?? "") != BookOutlets.Medium(originalBg.Platform ?? "")
                        || !BookSource.From(updated).Isbns.SequenceEqual(BookSource.From(originalBg).Isbns))
                        throw new InvalidDataException("书籍身份发生变化，未使用新入口。");
                    bangumi = updated; Rebuild();
                    if (result.Warning is { Length: > 0 } warning) warnings.Add(warning);
                    Changed(null);
                }
                catch (Exception error) when (error is BangumiRelationsException or IOException or UnauthorizedAccessException or InvalidDataException)
                { if (!Accept()) return; failed = true; warnings.Add(error is InvalidDataException ? "卷册或出版身份发生变化，保留原入口。" : "Bangumi 暂时无法更新，保留已有资料。"); }
            }
            if (!Accept()) return;
            if (aniList?.Media is { Format: "MANGA" or "ONE_SHOT" } manga && (supplements ?? aniListReader as IBookOutletSupplementReader) is { } supplementReader)
            {
                try
                {
                    var result = await supplementReader.LoadBookLinksAsync(manga, refresh, token);
                    if (!Accept()) return;
                    if (!result.Value.Matches(manga)) throw new InvalidDataException();
                    supplement = result.Value; Rebuild();
                    if (result.Warning is { Length: > 0 } warning) { warnings.Add(warning); failed = true; }
                    Changed(null);
                }
                catch (Exception error) when (error is AniListException or IOException or UnauthorizedAccessException or InvalidDataException)
                { if (!Accept()) return; failed = true; warnings.Add("补充来源暂时无法更新，已有入口仍可使用。"); }
            }
            if (!Accept()) return;
            complete = !failed;
            Message = string.Join(" ", warnings.Distinct());
        }
        catch (OperationCanceledException) { if (Accept()) Message = "已取消读取，已有入口仍保留。"; }
        finally
        {
            if (ReferenceEquals(request, owned)) { request = null; owned.Dispose(); Changed(null); }
        }
    }
    private void Rebuild()
    {
        var values = new List<BookOutletSet>();
        if (aniList is not null) values.Add(BookOutlets.FromAniList(aniList));
        if (bangumi is not null) values.Add(BookOutlets.FromBangumi(bangumi));
        // A same-ID metadata refresh can still change a corroborating external
        // identifier. Recheck the assertion on every rebuild, not just arrival.
        if (supplement is not null && aniList is not null && supplement.Matches(aniList.Media)) values.Add(supplement.Outlets());
        Links = BookOutlets.Merge(values.SelectMany(x => x.Links)); Excluded = values.SelectMany(x => x.Excluded).ToArray();
    }
    public void Collapse() { Expanded = false; if (Busy) Cancel(); else Changed(null); }
    public void Cancel() { var busy = Busy; Stop(); if (busy) { complete = false; Message = "已取消读取，已有入口仍保留。"; } Changed(null); }
    private void Stop() { generation++; var old = request; request = null; old?.Cancel(); old?.Dispose(); }
    public void Deactivate() { Stop(); key = null; Source = null; aniList = null; bangumi = null; supplement = null; Links = []; Excluded = []; Expanded = complete = false; Message = ""; Changed(null); }
    public void Dispose() { if (disposed) return; Deactivate(); disposed = true; }
}
