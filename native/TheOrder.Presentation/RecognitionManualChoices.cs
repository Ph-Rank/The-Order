using TheOrder.Application;

namespace TheOrder.Presentation;

public sealed partial class RecognitionViewModel
{
    private MediaUserLinks? manualChoices;
    private long manualChoicesVersion;
    public MediaUserLink[] ManualPendingChoices => manualChoices?.Items ?? [];
    public bool CanSaveManualPending => !disposed && !busy && HasSelection && SelectedGameId is not null && CatalogWorkReference.TryParse(ManualId, out _);
    private async Task LoadManualChoicesAsync(string game, long version)
    {
        try
        {
            var result = await identityDecisions.ReadLinksAsync(MediaIdentityContext.ForLibraryEntry(game), lifetime.Token);
            if (SelectedGameId == game && version == manualChoicesVersion) { manualChoices = result; Changed(nameof(ManualPendingChoices)); }
        }
        catch (OperationCanceledException) { }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException)
        { if (SelectedGameId == game && version == manualChoicesVersion) { Message = "待核验选择暂时无法读取，原记录保留。"; Notify(); } }
    }
    public Task SaveManualPendingAsync() => !CanSaveManualPending || SelectedGameId is not { } game ? Task.CompletedTask : Track(async () =>
    {
        if (!CatalogWorkReference.TryParse(ManualId, out var id)) return;
        var version = ++manualChoicesVersion; var source = MediaIdentityContext.ForLibraryEntry(game);
        var link = new MediaUserLink(source, new("NextMoe", "Work", id), MediaUserLinkState.Pending,
            "待核验目录作品 · " + id, "GAME", "RELATED", "尚未读取完整作品，不用于自动识别或资料融合", DateTimeOffset.UtcNow);
        busy = true; Notify();
        try
        {
            var current = manualChoices ?? await identityDecisions.ReadLinksAsync(source, lifetime.Token);
            var saved = await identityDecisions.ChangeLinkAsync(source, link, link.Target, current.Revision, lifetime.Token);
            if (SelectedGameId == game && version == manualChoicesVersion) { manualChoices = saved; Message = "已保存待核验选择；读取成功后仍需明确确认关联。"; }
        }
        catch (OperationCanceledException) { }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or NotSupportedException)
        { if (SelectedGameId == game) Message = "待核验选择未能保存，原关联保留。"; }
        finally { busy = false; Notify(); Changed(nameof(ManualPendingChoices)); Changed(nameof(CanSaveManualPending)); }
    });
    public Task RemoveManualPendingAsync(MediaUserLink link) => busy || SelectedGameId is not { } game || manualChoices?.Items.Contains(link) != true
        ? Task.CompletedTask : Track(async () =>
    {
        busy = true; var version = ++manualChoicesVersion; Notify();
        try
        {
            var saved = await identityDecisions.ChangeLinkAsync(MediaIdentityContext.ForLibraryEntry(game), null, link.Target, manualChoices!.Revision, lifetime.Token);
            if (SelectedGameId == game && version == manualChoicesVersion) { manualChoices = saved; Message = "已移除待核验选择。"; }
        }
        catch (OperationCanceledException) { }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or NotSupportedException)
        { if (SelectedGameId == game) Message = "待核验选择未能移除，请重试。"; }
        finally { busy = false; Notify(); Changed(nameof(ManualPendingChoices)); }
    });
}
