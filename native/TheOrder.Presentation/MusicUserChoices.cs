using TheOrder.Application;

namespace TheOrder.Presentation;

public sealed partial class MusicAlbumViewModel
{
    public MediaUserLinks? UserLinks { get; private set; }
    public MusicBrainzReference? PendingReference { get; private set; }
    private string? relatedReleaseId;
    public bool RelatedSelection => relatedReleaseId is not null && Displayed?.Album.Release.Id == relatedReleaseId;
    public bool CanAddRelated => CanLoad && Source is not null && Preview is not null && music is IMediaUserLinkReader;
    public bool CanSavePending => CanLoad && Source is not null && PendingReference is not null && Preview is null && music is IMediaUserLinkReader;
    public bool CanRestoreAutomatic => CanLoad && Source is not null && music is IMediaUserLinkReader && (SavedUserTargetId is not null || Match?.RejectedIds.Length > 0);
    public Task SavePendingAsync() => !CanSavePending ? Task.CompletedTask : Request(async (token, accepts) =>
    {
        var reference = PendingReference!;
        var target = new MediaIdentityReference("MusicBrainz", reference.Kind == MusicBrainzEntityKind.Release ? "Release" : "ReleaseGroup", reference.Id);
        await SaveUserLinkAsync(new(MediaIdentityContext.From(Source!), target, MediaUserLinkState.Pending, reference.Url, "", "RELATED", "", DateTimeOffset.UtcNow), token, accepts);
        if (accepts()) { State = MusicAlbumState.Choosing; Message = "已保存待核验选择；重试取得资料后仍需明确确认。"; }
    }, default);
    public Task AddRelatedAsync() => !CanAddRelated ? Task.CompletedTask : Request(async (token, accepts) =>
    {
        var selected = Preview!; var release = selected.Album.Release;
        var sameGroup = Album?.Album.Release is { } prior && prior.Id != release.Id && prior.ReleaseGroupId == release.ReleaseGroupId;
        await SaveUserLinkAsync(new(MediaIdentityContext.From(Source!), MediaIdentityDecisionRules.Target(release.Id), MediaUserLinkState.Related,
            release.Title, "Release", sameGroup ? "OTHER_EDITION" : "RELATED", "", DateTimeOffset.UtcNow), token, accepts);
        if (!accepts()) return;
        Album = selected; Preview = null; relatedReleaseId = release.Id; State = MusicAlbumState.Ready;
        SelectionReason = "相关发行版 · 独立资料"; Message = "已保存相关发行版，原来的发行版选择没有改变。";
    }, default);
    private async Task SaveUserLinkAsync(MediaUserLink value, CancellationToken token, Func<bool> accepts)
    {
        var saved = await ((IMediaUserLinkReader)music).ChangeUserLinkAsync(value.Source, value, value.Target, UserLinks?.Revision ?? 0, token);
        if (accepts()) UserLinks = saved;
    }
    public Task RemoveUserLinkAsync(MediaIdentityReference target) => !CanLoad || Source is null || music is not IMediaUserLinkReader links ? Task.CompletedTask : Request(async (token, accepts) =>
    {
        var saved = await links.ChangeUserLinkAsync(MediaIdentityContext.From(Source!), null, target, UserLinks?.Revision ?? 0, token);
        if (!accepts()) return; UserLinks = saved;
        if (RelatedSelection && Album?.Album.Release.Id == target.Id) { Preview = Album; Album = null; relatedReleaseId = null; State = MusicAlbumState.Preview; }
        Message = "已移除相关发行版，作品资料保留。";
    }, default);
    public Task OpenUserLinkAsync(MediaUserLink link) => !CanLoad || Source is null || UserLinks?.Items.Contains(link) != true ? Task.CompletedTask : Request(async (token, accepts) =>
    {
        if (link.Target.Resource == "ReleaseGroup")
        {
            State = MusicAlbumState.Matching; Notify();
            var editions = await music.EditionsAsync(link.Target.Id, token); if (!accepts()) return;
            if (!MusicBrainzValidation.Search(editions) || editions.Candidates.Any(x => x.ReleaseGroupId != link.Target.Id)) throw new InvalidDataException();
            Match = new(editions, null, null, false, false, null); Preview = null; relatedReleaseId = null; State = MusicAlbumState.Choosing;
            Message = "请选择具体发行版；专辑组不作为具体发行物确认。"; return;
        }
        State = MusicAlbumState.LoadingAlbum; Notify(); PendingReference = new(MusicBrainzEntityKind.Release, link.Target.Id);
        var result = await music.LoadAlbumAsync(link.Target.Id, false, token); if (!accepts()) return; Validate(link.Target.Id, result);
        var isRelated = link.State == MediaUserLinkState.Related;
        if (isRelated) { relatedReleaseId = link.Target.Id; Album = result; Preview = null; State = MusicAlbumState.Ready; SelectionReason = "相关发行版 · 独立资料"; }
        else
        {
            Preview = result; Match = new(new([result.Album.Release], 1, false, null, null), null, null, false, false, null);
            State = MusicAlbumState.Preview; SelectionReason = "待核验选择 · 尚未确认";
        }
        Message = result.Warning ?? "";
    }, default);
    public Task RestoreAutomaticAsync() => !CanRestoreAutomatic ? Task.CompletedTask : Request(async (token, accepts) =>
    {
        await ((IMediaUserLinkReader)music).RestoreAutomaticAsync(MediaIdentityContext.From(Source!), token); if (!accepts()) return;
        SavedUserTargetId = null; Album = Preview = null; relatedReleaseId = null;
        if (Match is not null) Match = Match with { ConfirmedReleaseId = null, AutomaticReleaseId = null, RejectedIds = [] };
        State = MusicAlbumState.Choosing; SelectionReason = ""; Message = "已恢复原有自动选择规则，可刷新重新匹配。";
    }, default);
}
