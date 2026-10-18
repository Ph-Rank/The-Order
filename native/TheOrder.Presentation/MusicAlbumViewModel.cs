using TheOrder.Application;

namespace TheOrder.Presentation;

public enum MusicAlbumState { Idle, LoadingSource, Matching, LoadingAlbum, Choosing, Preview, Ready, Cancelled, Failed }
public sealed partial class MusicAlbumViewModel(IBangumiSubjectDetailsReader subjects, IMusicBrainzReader music) : ObservableState, IDisposable
{
    private CancellationTokenSource? operation;
    private long generation;
    private bool active, disposed;
    public BangumiRelationItem? Item { get; private set; }
    public BangumiSubjectDetailsResult? SourceDetails { get; private set; }
    public MusicAlbumSource? Source { get; private set; }
    public MusicBrainzMatchResult? Match { get; private set; }
    // A manual search or edition list is disposable; the last saved user target
    // must remain available even if the next preview fails or is removed.
    public string? SavedUserTargetId { get; private set; }
    public MusicBrainzAlbumResult? Album { get; private set; }
    public MusicBrainzAlbumResult? Preview { get; private set; }
    public MusicAlbumState State { get; private set; }
    public string Message { get; private set; } = "";
    public string SelectionReason { get; private set; } = "";
    public bool IsBusy => operation is not null || State is MusicAlbumState.LoadingSource or MusicAlbumState.Matching or MusicAlbumState.LoadingAlbum;
    public bool CanLoad => active && !disposed && !IsBusy && Item is not null;
    public bool CanConfirm => CanLoad && State == MusicAlbumState.Preview && Source is not null && Preview is not null
        && !MusicBrainzMatching.Assess(Source, Preview.Album.Release).HasIdentityConflict;
    public bool CanReject => CanLoad && Source is not null && Displayed is not null && State is MusicAlbumState.Ready or MusicAlbumState.Preview;
    public bool CanRevoke => CanLoad && Source is not null && SavedUserTargetId is not null;
    public bool CanCancel => active && !disposed && IsBusy;
    public MusicBrainzAlbumResult? Displayed => Preview ?? Album;
    public Task OpenAsync(BangumiRelationItem item, CancellationToken cancellation = default)
    {
        if (disposed) return Task.CompletedTask;
        Stop(); active = true; Item = item; SourceDetails = null; Source = null; Match = null; SavedUserTargetId = null; Album = Preview = null; SelectionReason = "";
        UserLinks = null; PendingReference = null; relatedReleaseId = null;
        State = MusicAlbumState.Idle;
        if (item.Kind != BangumiRelationKind.Music || !BangumiRelationIdentityResolver.ValidSubjectId(item.Id))
        { State = MusicAlbumState.Failed; Message = "音乐条目身份无效，请返回重试。"; Notify(); return Task.CompletedTask; }
        return ReadAsync(false, false, cancellation);
    }
    public Task RefreshAsync(CancellationToken cancellation = default) => ReadAsync(true, false, cancellation);
    public Task SearchAsync(string query, CancellationToken cancellation = default)
    {
        if (!CanLoad || Source is not { } bridge || string.IsNullOrWhiteSpace(query)) return Task.CompletedTask;
        query = query.Trim();
        if (!MusicBrainzValidation.Text(query, 2000, false)) return Task.CompletedTask;
        MusicAlbumSource search;
        PendingReference = null;
        if (MusicBrainzReference.TryParse(query, out var reference))
        {
            if (reference!.Kind is not (MusicBrainzEntityKind.Release or MusicBrainzEntityKind.ReleaseGroup))
            { Message = "这里请选择 MusicBrainz 发行版或专辑组链接；录音、作品与音乐人是独立资料。"; Notify(); return Task.CompletedTask; }
            PendingReference = reference; search = bridge with { Facts = BangumiMusicFacts.Empty with { ExternalIds = [reference] } };
        }
        else
        {
            if (Uri.TryCreate(query, UriKind.Absolute, out _)) { Message = "请填写专辑名，或 MusicBrainz 发行版／专辑组的链接。"; Notify(); return Task.CompletedTask; }
            search = bridge with { Title = query, Facts = BangumiMusicFacts.Empty };
        }
        return Request(async (token, accepts) =>
        {
            State = MusicAlbumState.Matching; Message = "正在查找音乐发行版…"; Preview = null; Notify();
            var found = await music.MatchAsync(search, true, token);
            if (!accepts()) return;
            if (!MusicBrainzValidation.Search(found.Search)) throw new InvalidDataException();
            // Manual retrieval never changes the original Bangumi facts or bypasses confirmation.
            Match = found with { AutomaticReleaseId = null, ConfirmedReleaseId = null }; State = MusicAlbumState.Choosing;
            Message = found.Search.Candidates.Length == 0 ? "未找到发行版，可以调整名称或粘贴 MusicBrainz 条目链接。"
                : "请选择发行版，核对曲目后确认。";
        }, cancellation);
    }
    public Task ChooseEditionAsync(CancellationToken cancellation = default)
    {
        if (!CanLoad) return Task.CompletedTask;
        var group = (Preview ?? Album)?.Album.Release.ReleaseGroupId;
        if (group is null) return ReadAsync(true, true, cancellation);
        return Request(async (token, accepts) =>
        {
            State = MusicAlbumState.Matching; Message = "正在读取此专辑的其他发行版…"; Notify();
            var result = await music.EditionsAsync(group, token);
            if (!accepts()) return;
            if (!MusicBrainzValidation.Search(result) || result.Candidates.Any(x => x.ReleaseGroupId != group)) throw new InvalidDataException();
            Match = new(result, null, null, false, false, null); Preview = null; State = MusicAlbumState.Choosing;
            Message = "不同发行版可能有不同曲目或译名。请先预览，再确认。";
            if (result.Truncated) Message += " 当前显示前 25 项。";
            if (result.Notice is not null) Message += " " + result.Notice;
        }, cancellation);
    }
    private async Task ReadAsync(bool refresh, bool choose, CancellationToken cancellation)
    {
        if (!CanLoad || Item is not { } item) return;
        await Request(async (token, accepts) =>
        {
            State = MusicAlbumState.LoadingSource; Message = "正在读取音乐条目的发行信息…"; Notify();
            var details = await subjects.LoadAsync(item.Id, item.Kind, refresh, token);
            if (!accepts()) return;
            if (!BangumiSubjectDetailsService.Valid(item.Id, item.Kind, details.Details)) throw new InvalidDataException();
            SourceDetails = details; Source = MusicAlbumSource.From(details.Details);
            if (music is IMediaUserLinkReader links) { UserLinks = await links.ReadUserLinksAsync(MediaIdentityContext.From(Source), token); if (!accepts()) return; }
            State = MusicAlbumState.Matching; Message = "正在查找 MusicBrainz 发行版…"; Notify();
            var match = await music.MatchAsync(Source, refresh, token);
            if (!accepts()) return;
            if (!MusicBrainzValidation.Search(match.Search)) throw new InvalidDataException();
            Match = match; SavedUserTargetId = match.ConfirmedReleaseId; Preview = null;
            var id = match.ConfirmedReleaseId ?? match.AutomaticReleaseId;
            if (!choose && id is not null)
            {
                State = MusicAlbumState.LoadingAlbum; Message = "正在读取专辑与曲目…"; Notify();
                var album = await music.LoadAlbumAsync(id, refresh, token);
                if (!accepts()) return;
                Validate(id, album);
                // Search documents are not a substitute for the final release identity and facts.
                var assessment = MusicBrainzMatching.Assess(Source, album.Album.Release);
                if (match.NeedsReview || match.ConfirmedReleaseGroupId is { } originalGroup && album.Album.Release.ReleaseGroupId != originalGroup)
                {
                    Album = null; Preview = album; State = MusicAlbumState.Preview;
                    Match = match with { Search = new([album.Album.Release], 1, false, null, null) };
                    Message = "已保存的发行版仍保留，但条目层级或所属专辑组已变化，请重新核对。";
                    return;
                }
                if (!assessment.HasIdentityConflict && (match.ConfirmedReleaseId is not null || assessment.Strong))
                {
                    Album = album; State = MusicAlbumState.Ready;
                    SelectionReason = match.ConfirmedReleaseId is not null ? "使用已确认的发行版" : "目录编号与发行信息匹配";
                    Message = Warnings(assessment.HasConflict ? "所选发行版的日期、目录编号或其他发行信息与来源不同，请核对版本。" : null,
                        details.Warning, match.Warning, album.Warning, album.Album.CoverWarning); return;
                }
                if (assessment.HasIdentityConflict)
                {
                    Album = null; State = MusicAlbumState.Choosing;
                    Message = "发行版与当前来源的明确证据存在冲突，请重新查找并核对；原有关联记录已保留。";
                    return;
                }
            }
            State = MusicAlbumState.Choosing;
            Message = match.Search.Candidates.Length == 0 ? "MusicBrainz 暂未找到对应发行版，仍可查看 Bangumi 资料。"
                : "请选择具体发行版。可以先查看曲目，再确认使用。";
            if (match.Search.Notice is { } notice) Message += " " + notice;
            if (match.Search.Truncated) Message += " 当前仅列出前 25 项，请在原站进一步核对。";
        }, cancellation);
    }
    public async Task PreviewAsync(string id, CancellationToken cancellation = default)
    {
        if (!CanLoad || Match?.Search.Candidates.Any(x => x.Id == id) != true) return;
        await Request(async (token, accepts) =>
        {
            PendingReference = new(MusicBrainzEntityKind.Release, id);
            Preview = null; State = MusicAlbumState.LoadingAlbum; Message = "正在读取所选发行版的曲目…"; Notify();
            var value = await music.LoadAlbumAsync(id, false, token);
            if (!accepts()) return; Validate(id, value);
            Preview = value; State = MusicAlbumState.Preview;
            Message = "这是发行版预览。确认后将记住此选择。";
            if (Source is not null)
            {
                var assessment = MusicBrainzMatching.Assess(Source, value.Album.Release);
                if (assessment.HasIdentityConflict)
                    Message = "此版本与来源编号不同，不能确认为同一发行版；可以添加相关发行版，资料分别保留。";
                else if (assessment.HasConflict)
                    Message = "此发行版的日期、目录编号或其他发行信息与来源不同。请核对版本和曲目后确认。";
            }
            Message = Warnings(Message, value.Warning, value.Album.CoverWarning);
        }, cancellation);
    }
    public async Task ConfirmAsync(CancellationToken cancellation = default)
    {
        if (!CanConfirm || Source is not { } source || Preview is not { } preview
            || Match?.Search.Candidates.Any(x => x.Id == preview.Album.Release.Id) != true) return;
        await Request(async (token, accepts) =>
        {
            State = MusicAlbumState.LoadingAlbum; Message = "正在保存发行版选择…"; Notify();
            await music.ConfirmAsync(source, preview.Album, token);
            if (!accepts()) return;
            relatedReleaseId = null; SavedUserTargetId = preview.Album.Release.Id;
            Match = Match! with { ConfirmedReleaseId = SavedUserTargetId, AutomaticReleaseId = null,
                ConfirmedReleaseGroupId = preview.Album.Release.ReleaseGroupId, NeedsReview = false,
                RejectedIds = Match!.RejectedIds.Where(id => id != SavedUserTargetId).ToArray() };
            Album = preview; Preview = null; State = MusicAlbumState.Ready; SelectionReason = "你确认的发行版";
            Message = Warnings(MusicBrainzMatching.Assess(source, preview.Album.Release).HasConflict
                ? "所选发行版的日期、目录编号或其他发行信息与来源不同，请核对版本。" : null, preview.Warning, preview.Album.CoverWarning);
        }, cancellation);
    }
    public void BackToEditions()
    {
        if (!CanLoad || Match is null) return;
        Preview = null; State = MusicAlbumState.Choosing; Message = "请选择具体发行版。"; Notify();
    }
    public async Task RejectAsync(CancellationToken cancellation = default)
    {
        if (!CanReject || Source is not { } source || Displayed?.Album.Release is not { } release) return;
        await Request(async (token, accepts) =>
        {
            State = MusicAlbumState.LoadingAlbum; Message = "正在取消关联并排除此发行版…"; Notify();
            await music.RejectAsync(source, release, token);
            if (!accepts()) return;
            if (SavedUserTargetId == release.Id) SavedUserTargetId = null;
            Album = Preview = null; SelectionReason = "";
            var match = await music.MatchAsync(source, true, token);
            if (!accepts()) return;
            if (!MusicBrainzValidation.Search(match.Search)) throw new InvalidDataException();
            Match = match; SavedUserTargetId = match.ConfirmedReleaseId;
            State = MusicAlbumState.Choosing;
            Message = "已取消此发行版关联并排除自动匹配。仍可手动核对后重新确认。";
        }, cancellation);
    }
    public async Task RevokeAsync(CancellationToken cancellation = default)
    {
        if (!CanRevoke || Source is not { } source || SavedUserTargetId is not { } id) return;
        await Request(async (token, accepts) =>
        {
            State = MusicAlbumState.LoadingAlbum; Message = "正在解除已保存的发行版关联…"; Notify();
            await music.RevokeAsync(source, id, token); if (!accepts()) return;
            SavedUserTargetId = null;
            Album = Preview = null; SelectionReason = "";
            Match = Match! with { ConfirmedReleaseId = null, AutomaticReleaseId = null, RejectedIds = Match.RejectedIds.Append(id).Distinct().ToArray() };
            State = MusicAlbumState.Choosing; Message = "已解除保存的发行版并排除自动匹配。可按需查找其他发行版。";
        }, cancellation);
    }
    private async Task Request(Func<CancellationToken, Func<bool>, Task> action, CancellationToken cancellation)
    {
        Stop(); var version = generation; var item = Item;
        using var request = CancellationTokenSource.CreateLinkedTokenSource(cancellation); operation = request;
        bool Owns() => !disposed && active && generation == version && Item == item;
        bool Accept() => Owns() && !request.IsCancellationRequested;
        try { await action(request.Token, Accept); }
        catch (OperationCanceledException) { if (Owns()) { State = MusicAlbumState.Cancelled; Message = "已取消读取，可重试或返回关联列表。"; } }
        catch (Exception e)
        {
            if (!Accept()) return;
            if (e is BangumiRelationsException { Failure: BangumiRelationsFailure.InvalidResponse or BangumiRelationsFailure.Unavailable })
            { Source = null; SourceDetails = null; Match = null; Album = Preview = null; }
            if (e is MusicBrainzException { Failure: MusicBrainzFailure.InvalidResponse or MusicBrainzFailure.Unavailable or MusicBrainzFailure.ResponseTooLarge })
                Album = Preview = null;
            State = MusicAlbumState.Failed;
            Message = e is MusicBrainzException or BangumiRelationsException ? e.Message
                : e is IOException ? "发行版选择未能保存，可以重试。" : "音乐资料暂时不可用，已保留 Bangumi 资料。";
        }
        finally
        {
            if (ReferenceEquals(operation, request)) operation = null;
            if (Owns())
            {
                if (IsBusy) { State = MusicAlbumState.Cancelled; Message = "已取消读取，可重试。"; }
                Notify();
            }
        }
    }
    private static void Validate(string id, MusicBrainzAlbumResult result)
    { if (!MusicBrainzValidation.Album(result.Album) || result.Album.Release.Id != id) throw new MusicBrainzException(MusicBrainzFailure.InvalidResponse, "专辑身份不一致，未显示资料。"); }
    private static string Warnings(params string?[] values) => string.Join(" ", values.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct());
    public void Cancel()
    {
        if (!CanCancel) return; Stop(); State = MusicAlbumState.Cancelled; Message = "已取消读取，可重试或返回关联列表。"; Notify();
    }
    public void Deactivate() { Stop(); active = false; Notify(); }
    private void Stop() { generation++; operation?.Cancel(); operation = null; }
    private void Notify() => Changed(string.Empty);
    public void Dispose() { if (disposed) return; Deactivate(); disposed = true; }
}
