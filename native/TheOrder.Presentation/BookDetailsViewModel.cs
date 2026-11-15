using TheOrder.Application;

namespace TheOrder.Presentation;

public enum BookDetailsState { Idle, Ready, Loading, Cancelled, Failed }

// This exploration state never mutates a library entry. Its source is the current
// media identity, not the title of the media that originally opened the host.
public sealed class BookDetailsViewModel(IBangumiSubjectDetailsReader subjects,
    IBookBibliographyReader books, IBangumiBookRelationsReader relations) : ObservableState, IDisposable
{
    private readonly Stack<Frame> history = new();
    private CancellationTokenSource? request;
    private string? rootFingerprint;
    private BangumiSubjectDetails? rootBangumi;
    private AniListDetails? rootAniList;
    private long generation;
    private bool active, disposed, navigating;
    public BookSource? Source { get; private set; }
    public BangumiSubjectDetails? Bangumi { get; private set; }
    public AniListDetails? AniList { get; private set; }
    public BangumiRelationsLoadResult? Relations { get; private set; }
    public BookSearchResult? Search { get; private set; }
    public BookSearchQuery? Query { get; private set; }
    public BookPublication[] Candidates { get; private set; } = [];
    public BookPublicationResult? Preview { get; private set; }
    public BookPublicationBinding[] Bindings { get; private set; } = [];
    public bool BindingsLoaded { get; private set; }
    public bool Expanded { get; private set; }
    public string Incoming { get; private set; } = "";
    public string Message { get; private set; } = "";
    public BookDetailsState State { get; private set; }
    public bool IsBusy => request is not null;
    public bool CanLoad => active && !disposed && Source is not null && Expanded && !IsBusy;
    public int HistoryDepth => history.Count;
    public bool CanBack => history.Count > 0 || navigating;
    public long Version => generation;
    public string DefaultTitle => Source?.OriginalTitle is { Length: > 0 } original ? original : Source?.Title ?? "";
    public string DefaultIsbn => Source?.Isbns.FirstOrDefault() ?? "";
    // An aggregate count is not a navigable chapter directory. Use only the
    // current, trusted book source; never borrow a count from a rejected preview.
    public string ChapterDirectoryNotice => (Bangumi?.BookFacts?.Chapters ?? AniList?.Media.Chapters) is > 0 and var count
        ? $"当前仅收录 {count} 章的总数，逐章目录暂不可用。"
        : Source?.Format is "MANGA" or "ONE_SHOT" or "漫画" ? "逐章目录暂不可用，可继续浏览系列与单行本。" : "";
    public bool PreviewIsBound => Preview is { } p && Bindings.Any(x => x.PublicationId == p.Publication.Id && !x.NeedsReview);
    public string ConfirmationBlock => Preview is not { } p ? "请先预览一条出版记录。"
        : p.IsStale ? "当前显示旧资料，请刷新出版记录后再确认。"
        : p.Publication.IsAggregate ? "这是集合记录，不能确认为独立出版物。"
        : Source is { Identity.Level: BookEntityLevel.Volume or BookEntityLevel.Publication, Isbns.Length: > 0 } s
            && !s.Isbns.Intersect(p.Publication.Isbns, StringComparer.Ordinal).Any() ? "此单卷的 ISBN 与出版记录不同，不能确认。"
        : "";
    public bool CanConfirm => CanLoad && Preview is not null && !PreviewIsBound && ConfirmationBlock.Length == 0;

    public void SetSource(BangumiSubjectDetails? bangumi, AniListDetails? aniList, bool bridgeTrusted)
    {
        if (disposed) return;
        var bg = bridgeTrusted && bangumi?.Kind == BangumiRelationKind.Book ? bangumi : null;
        BookSource? source = bg is not null ? BookSource.From(bg)
            : aniList?.Media.Kind == AniListMediaKind.Manga ? BookSource.From(aniList) : null;
        var fingerprint = source?.Fingerprint;
        rootBangumi = bg; rootAniList = bg is null ? aniList : null;
        if (active && rootFingerprint == fingerprint)
        {
            // Descriptive facts can change without changing publication identity.
            // Keep current exploration and pending requests, but refresh visible
            // root facts. A root saved in history is refreshed when restored.
            if (Source?.Fingerprint == fingerprint)
            { Bangumi = rootBangumi; AniList = rootAniList; Notify(); }
            return;
        }
        Stop(); history.Clear(); rootFingerprint = fingerprint; active = source is not null;
        Source = source; Bangumi = bg; AniList = bg is null ? aniList : null;
        Expanded = false; ResetResults(); Incoming = ""; Message = ""; State = source is null ? BookDetailsState.Idle : BookDetailsState.Ready;
        Notify();
    }
    public Task ExpandAsync()
    {
        if (!active || disposed || Source is null) return Task.CompletedTask;
        Expanded = true; Notify();
        return BindingsLoaded || IsBusy ? Task.CompletedTask : ReloadBindingsAsync();
    }
    public void Collapse() { if (!Expanded) return; Expanded = false; Cancel(); }
    public Task ReloadBindingsAsync() => RunAsync("读取已确认的出版记录…", async (token, accept) =>
    {
        var saved = await books.BindingsAsync(Source!, token);
        if (!accept()) return;
        AcceptBindings(saved);
        Message = saved.Any(x => x.NeedsReview) ? "部分已关联版本需要重新核对。" : "";
    });
    public Task LoadRelationsAsync(bool refresh = false)
    {
        if (Source?.Identity.Provider != "Bangumi" || Relations is not null && !refresh) return Task.CompletedTask;
        var id = Source.Identity.Id;
        return RunAsync("读取 Bangumi 书籍关系…", async (token, accept) =>
        {
            var value = await relations.LoadAsync(id, refresh, token);
            if (!accept()) return;
            if (value.Snapshot.SubjectId != id) throw new InvalidDataException();
            Relations = value;
            Message = value.Snapshot.Items.All(x => x.Kind != BangumiRelationKind.Book)
                ? "未找到书籍关联。" : "";
            Message = (Message + CacheNotice(value.FromCache, value.IsStale, value.Warning)).Trim(' ', '·');
        });
    }
    public Task OpenRelatedAsync(string id)
    {
        var item = Relations?.Snapshot.Items.FirstOrDefault(x => x.Id == id && x.Kind == BangumiRelationKind.Book);
        if (!CanLoad || item is null) return Task.CompletedTask;
        if (history.Count >= 20) { Message = "已达到 20 层浏览记录，请先返回。"; Notify(); return Task.CompletedTask; }
        var previous = Capture(); navigating = true;
        return RunAsync("读取所选书籍…", async (token, accept) =>
        {
            var result = await subjects.LoadAsync(item.Id, BangumiRelationKind.Book, false, token);
            if (!accept()) return;
            var source = BookSource.From(result.Details);
            if (result.Details.Id != item.Id) throw new InvalidDataException();
            // Only the explicit outgoing tankobon relation establishes a volume.
            if (item.Relation == "单行本") source = source with { Identity = source.Identity with { Level = BookEntityLevel.Volume } };
            history.Push(previous); navigating = false; Source = source; Bangumi = result.Details; AniList = null; ResetResults();
            Incoming = $"《{previous.Source.Title}》 → 《{source.Title}》 · Bangumi 原标签：{item.Relation}";
            Message = CacheNotice(result.FromCache, result.IsStale, result.Warning).Trim(' ', '·');
            // Read only the tiny saved-choice mapping, never recursively fetch relations.
            var saved = await books.BindingsAsync(source, token);
            if (accept()) AcceptBindings(saved);
        }, navigation: true);
    }
    public bool GoBack()
    {
        if (navigating) { Cancel(); return true; }
        if (history.Count == 0) return false;
        Stop(); Restore(history.Pop());
        if (Source?.Fingerprint == rootFingerprint) { Bangumi = rootBangumi; AniList = rootAniList; }
        Expanded = true; State = BookDetailsState.Ready; Notify(); return true;
    }
    public Task SearchAsync(string? title = null, string? isbn = null, bool refresh = false)
    {
        if (!CanLoad) return Task.CompletedTask;
        var text = (title ?? DefaultTitle).Trim(); var rawIsbn = (isbn ?? DefaultIsbn).Trim();
        var normalized = rawIsbn.Length == 0 ? null : BookIsbn.Normalize(rawIsbn);
        var query = new BookSearchQuery(text, normalized);
        if (rawIsbn.Length > 0 && normalized is null || !query.IsValid)
        { Message = "请输入不超过 200 字的原标题，或有效的 ISBN。"; State = BookDetailsState.Failed; Notify(); return Task.CompletedTask; }
        return SearchPageAsync(query, false, refresh);
    }
    public Task NextAsync()
    {
        if (Query is not { } query || Search?.Page.NextStart is not { } next || next <= query.Start || !(query with { Start = next }).IsValid) return Task.CompletedTask;
        return SearchPageAsync(query with { Start = next }, true, false);
    }
    private Task SearchPageAsync(BookSearchQuery query, bool append, bool refresh) => RunAsync("检索 NDL 出版候选…", async (token, accept) =>
    {
        var value = await books.SearchAsync(query, refresh, token);
        if (!accept()) return;
        if (!BookValidation.Page(value.Page)) throw new InvalidDataException();
        Query = query; Search = value; Preview = null;
        // Publication IDs remain independent even when their titles or ISBNs match.
        Candidates = (append ? Candidates.Concat(value.Page.Publications) : value.Page.Publications).DistinctBy(x => x.Id).Take(100).ToArray();
        Message = value.Page.Publications.Length == 0 ? "NDL 本次检索没有返回候选。可清空 ISBN，改用原标题检索。"
            : "";
        Message = (Message + CacheNotice(value.FromCache, value.IsStale, value.Warning)).Trim(' ', '·');
    });
    public Task PreviewAsync(string id, bool refresh = false)
    {
        if (!CanLoad) return Task.CompletedTask;
        if (!Candidates.Any(x => x.Id == id) && !Bindings.Any(x => x.PublicationId == id)) return Task.CompletedTask;
        // Browsing a candidate does not need a second network round trip. The
        // service still independently loads and verifies its ID/ISBN on Confirm.
        if (!refresh && Search is { } search && Candidates.FirstOrDefault(x => x.Id == id) is { } candidate)
        {
            Preview = new(candidate, search.FromCache, search.IsStale, search.Warning) { FromSearch = true };
            State = BookDetailsState.Ready;
            Message = candidate.IsAggregate ? "这是集合记录，仅供参考，不能确认绑定。"
                : "确认时独立核验版本和 ISBN。";
            Message += CacheNotice(search.FromCache, search.IsStale, search.Warning); Notify();
            return Task.CompletedTask;
        }
        return RunAsync("读取独立出版记录…", async (token, accept) =>
        {
            var result = await books.LoadAsync(id, refresh, token);
            if (!accept()) return;
            if (!BookValidation.Publication(result.Publication) || result.Publication.Id != id) throw new InvalidDataException();
            Preview = result; Message = result.Publication.IsAggregate ? "集合记录不可关联。" : "";
            Message = (Message + CacheNotice(result.FromCache, result.IsStale, result.Warning)).Trim(' ', '·');
        });
    }
    public Task ConfirmAsync()
    {
        if (!CanConfirm) return Task.CompletedTask;
        var source = Source!; var publication = Preview!.Publication;
        return RunAsync("核验并保存出版关联…", async (token, accept) =>
        {
            await books.ConfirmAsync(source, publication, token);
            if (!accept()) return;
            // Only persisted read-back can create the UI's confirmed state.
            BindingsLoaded = false;
            var saved = await books.BindingsAsync(source, token);
            if (!accept()) return;
            AcceptBindings(saved);
            Message = Bindings.Any(x => x.PublicationId == publication.Id) ? "已保存这条独立出版记录的关联。" : "尚未读取到确认记录，请重新读取已保存记录。";
        });
    }
    public Task ForgetAsync(string id)
    {
        if (!Bindings.Any(x => x.PublicationId == id)) return Task.CompletedTask;
        var source = Source!;
        return RunAsync("解除出版记录关联…", async (token, accept) =>
        {
            await books.RemoveAsync(source, id, token);
            if (!accept()) return;
            BindingsLoaded = false;
            var saved = await books.BindingsAsync(source, token);
            if (!accept()) return;
            AcceptBindings(saved); Message = Bindings.Any(x => x.PublicationId == id) ? "仍读取到原关联，请重新读取已保存记录。" : "已解除这条出版记录的关联。";
        });
    }
    public void Cancel()
    {
        var wasBusy = IsBusy; Stop();
        if (wasBusy) { State = BookDetailsState.Cancelled; Message = "已取消当前操作；已显示的资料仍保留。保存操作如已完成，可重新读取确认记录。"; }
        Notify();
    }
    public void Deactivate() { Stop(); active = false; rootFingerprint = null; Expanded = false; Notify(); }
    public void Dispose() { if (disposed) return; Deactivate(); disposed = true; history.Clear(); }
    private async Task RunAsync(string message, Func<CancellationToken, Func<bool>, Task> action, bool navigation = false)
    {
        if (!CanLoad) { if (navigation) navigating = false; return; }
        var stop = new CancellationTokenSource(); request = stop; var version = ++generation;
        // generation protects A→B→A as well as source changes made inside navigation.
        bool Accept() => active && !disposed && Expanded && version == generation && ReferenceEquals(request, stop) && !stop.IsCancellationRequested;
        State = BookDetailsState.Loading; Message = message; Notify();
        try { await action(stop.Token, Accept); if (Accept()) State = BookDetailsState.Ready; }
        catch (OperationCanceledException) { if (Accept()) { State = BookDetailsState.Cancelled; Message = "读取已取消。"; } }
        catch (Exception error)
        {
            if (Accept())
            {
                if (error is BookProviderException { Failure: BookFailure.Unavailable or BookFailure.InvalidResponse or BookFailure.ResponseTooLarge })
                { Preview = null; Search = null; Candidates = []; }
                if (error is BangumiRelationsException { Failure: BangumiRelationsFailure.Unavailable or BangumiRelationsFailure.InvalidResponse or BangumiRelationsFailure.Unauthorized }) Relations = null;
                State = BookDetailsState.Failed; Message = error is BookProviderException or BangumiRelationsException ? error.Message : "本次操作未完成，已保留现有资料。请重试或重新读取已保存记录。";
            }
        }
        finally
        {
            if (ReferenceEquals(request, stop)) { request = null; navigating = false; Notify(); }
            stop.Dispose();
        }
    }
    private void AcceptBindings(BookPublicationBinding[] saved)
    {
        if (saved.Any(x => !BookValidation.Binding(x) || x.SourceIdentity.Provider != Source!.Identity.Provider || x.SourceIdentity.Id != Source.Identity.Id
            || !x.NeedsReview && (x.SourceIdentity != Source.Identity || x.SourceFingerprint != Source.Fingerprint))) throw new InvalidDataException();
        Bindings = saved; BindingsLoaded = true;
    }
    private void Stop() { generation++; var old = request; request = null; navigating = false; old?.Cancel(); }
    private void ResetResults() { Relations = null; Search = null; Query = null; Candidates = []; Preview = null; Bindings = []; BindingsLoaded = false; }
    private void Notify() => Changed(string.Empty);
    private static string CacheNotice(bool cached, bool stale, string? warning) => (stale ? " · 暂时显示上次资料" : "") + (string.IsNullOrWhiteSpace(warning) ? "" : " · " + warning);
    private Frame Capture() => new(Source!, Bangumi, AniList, Relations, Search, Query, Candidates, Preview, Bindings, BindingsLoaded, Incoming, Message);
    private void Restore(Frame value)
    {
        Source = value.Source; Bangumi = value.Bangumi; AniList = value.AniList; Relations = value.Relations; Search = value.Search; Query = value.Query;
        Candidates = value.Candidates; Preview = value.Preview; Bindings = value.Bindings; BindingsLoaded = value.BindingsLoaded; Incoming = value.Incoming; Message = value.Message;
    }
    private sealed record Frame(BookSource Source, BangumiSubjectDetails? Bangumi, AniListDetails? AniList,
        BangumiRelationsLoadResult? Relations, BookSearchResult? Search, BookSearchQuery? Query, BookPublication[] Candidates,
        BookPublicationResult? Preview, BookPublicationBinding[] Bindings, bool BindingsLoaded, string Incoming, string Message);
}
