using TheOrder.Application;

namespace TheOrder.Presentation;

public sealed partial class AniListDetailsViewModel
{
    public MediaUserLinks? UserLinks { get; private set; }
    public AniListReference? PendingReference { get; private set; }
    public bool RelatedSelection { get; private set; }
    public bool CanAddRelated => CanLoad && IsBridge && Source is not null && Displayed is not null && reader is IMediaUserLinkReader;
    public bool CanSavePending => CanLoad && IsBridge && Source is not null && PendingReference is not null
        && Displayed is null && reader is IMediaUserLinkReader;
    public bool CanRestoreAutomatic => CanLoad && IsBridge && Source is not null && reader is IMediaUserLinkReader
        && (SavedUserTargetId is not null || RejectedIds.Length > 0);
    private async Task ReadUserLinksAsync(CancellationToken token)
    {
        if (Source is not null && reader is IMediaUserLinkReader links)
            UserLinks = await links.ReadUserLinksAsync(MediaIdentityContext.From(Source), token);
    }
    public Task SavePendingAsync() => !CanSavePending ? Task.CompletedTask : RunAsync(async token =>
    {
        var reference = PendingReference!;
        var target = new MediaIdentityReference(reference.MyAnimeList ? "MyAnimeList" : "AniList",
            reference.Kind == AniListMediaKind.Anime ? "Anime" : "Manga", reference.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));
        await SaveUserLinkAsync(target, MediaUserLinkState.Pending, reference.Url, "", token);
        State = AniListDetailState.Choosing;
        Message = "已保存待核验选择。取得资料后仍需你确认，不会自动合并。";
    });
    public Task AddRelatedAsync(string note = "") => !CanAddRelated ? Task.CompletedTask : RunAsync(async token =>
    {
        var media = Displayed!.Details.Media;
        await SaveUserLinkAsync(MediaIdentityDecisionRules.Target(media), MediaUserLinkState.Related, media.Title, media.Format, token, note);
        trustedBridge = false; unconfirmedPreview = false; RelatedSelection = true;
        State = AniListDetailState.Ready; SelectionReason = "相关作品 · 用户添加（独立条目）";
        Message = "已添加相关作品；原条目与此作品的身份及资料分别保留。";
    });
    private async Task SaveUserLinkAsync(MediaIdentityReference target, MediaUserLinkState state, string title, string format, CancellationToken token, string note = "")
    {
        var context = MediaIdentityContext.From(Source!);
        var link = new MediaUserLink(context, target, state, title, format, "RELATED", note, DateTimeOffset.UtcNow);
        var saved = await ((IMediaUserLinkReader)reader).ChangeUserLinkAsync(context, link, target, UserLinks?.Revision ?? 0, token);
        Require(token); UserLinks = saved;
    }
    public Task RemoveUserLinkAsync(MediaIdentityReference target) => !CanLoad || Source is null || reader is not IMediaUserLinkReader links ? Task.CompletedTask : RunAsync(async token =>
    {
        var saved = await links.ChangeUserLinkAsync(MediaIdentityContext.From(Source!), null, target, UserLinks?.Revision ?? 0, token);
        Require(token); UserLinks = saved;
        if (Media is { } media && MediaIdentityDecisionRules.Target(media) == target && RelatedSelection)
        { RelatedSelection = false; trustedBridge = false; unconfirmedPreview = true; State = AniListDetailState.Preview; SelectionReason = "条目预览 · 尚未确认"; }
        Message = "已移除这条用户关联，作品资料保留。";
    });
    public Task OpenUserLinkAsync(MediaUserLink link) => !CanLoad || Source is null || UserLinks?.Items.Contains(link) != true ? Task.CompletedTask : RunAsync(async token =>
    {
        if (link.Target.Provider == "MyAnimeList")
        {
            var reference = new AniListReference(int.Parse(link.Target.Id, System.Globalization.CultureInfo.InvariantCulture),
                link.Target.Resource == "Anime" ? AniListMediaKind.Anime : AniListMediaKind.Manga, true);
            PendingReference = reference; await MatchCoreAsync(reference.Url, true, token); return;
        }
        var kind = link.Target.Resource == "Anime" ? AniListMediaKind.Anime : AniListMediaKind.Manga;
        var id = int.Parse(link.Target.Id, System.Globalization.CultureInfo.InvariantCulture);
        PendingReference = new(id, kind);
        var result = await reader.LoadAsync(id, kind, false, 1, token); Require(token); Display(result);
        trustedBridge = false; RelatedSelection = link.State == MediaUserLinkState.Related; unconfirmedPreview = !RelatedSelection;
        State = RelatedSelection ? AniListDetailState.Ready : AniListDetailState.Preview;
        SelectionReason = RelatedSelection ? "相关作品 · 用户添加（独立条目）" : "待核验选择 · 请确认对应或添加相关作品";
        Message = result.Warning ?? (RelatedSelection ? "相关作品独立展示，原条目资料保持。" : string.Join(" · ", AniListMatching.Assess(Source!, result.Details.Media).Evidence));
    });
    public Task RestoreAutomaticAsync() => !CanRestoreAutomatic ? Task.CompletedTask : RunAsync(async token =>
    {
        await ((IMediaUserLinkReader)reader).RestoreAutomaticAsync(MediaIdentityContext.From(Source!), token); Require(token);
        SavedUserTargetId = null; RejectedIds = []; RelatedSelection = false; trustedBridge = unconfirmedPreview = false;
        Displayed = null; Media = null; PeoplePages = []; SelectionReason = "";
        await MatchCoreAsync(null, false, token);
        Message = "已撤销人工对应及排除决定，恢复原有自动选择规则。";
    });
}
