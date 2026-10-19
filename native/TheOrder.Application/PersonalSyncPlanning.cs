using TheOrder.Domain;

namespace TheOrder.Application;

// Pure, inspectable three-way decisions. Scores/titles are never identity inputs.
public static class PersonalSyncPlanning
{
    public static string? CloudStatus(string local) => local switch { "planned" => null, "wish" => "wish", "playing" => "doing", "completed" => "done", "paused" => "on_hold", "dropped" => "dropped", _ => throw new OnlineException(OnlineFailure.InvalidRequest) };
    public static string LocalStatus(string cloud) => cloud switch { "wish" => "wish", "doing" => "playing", "done" => "completed", "on_hold" => "paused", "dropped" => "dropped", _ => throw new OnlineException(OnlineFailure.InvalidResponse) };
    public static PersonalSyncRow Time(LibraryEntry local, long? remoteMinutes, SyncWorkCheckpoint? basis,
        ImportedPersonalFacts? imported, PersonalSyncDirection direction)
    {
        var work = local.Metadata?.Id ?? throw new OnlineException(OnlineFailure.InvalidRequest);
        long localMinutes = local.EffectivePlaySeconds / 60;
        long floor = Math.Max(localMinutes, imported?.Minutes ?? 0);
        bool conflict = false; string explanation = "累计分钟，不相加；云端导入独立保存，本地会话保留。";
        long upload = floor;
        if (basis is not null)
        {
            bool localChanged = local.EffectivePlaySeconds != basis.LocalSeconds;
            bool remoteChanged = remoteMinutes != basis.CloudMinutes;
            if (local.EffectivePlaySeconds < basis.LocalSeconds || (remoteMinutes ?? 0) < (basis.CloudMinutes ?? 0))
            { conflict = true; explanation = "一侧累计值减少，需要人工核对；不会覆盖较大的已有值。"; }
            else if (localChanged && remoteChanged && floor != remoteMinutes)
            { conflict = true; explanation = "本地和云端都发生变化，无法证明两侧新增时长互不重叠。"; }
            else if (localChanged && !remoteChanged && basis.AgreedMinutes is { } agreed)
            {
                if (IndependentSeconds(local, basis) is { } seconds)
                { upload = checked(agreed + (seconds + basis.CarriedSeconds) / 60); explanation = "云端基线未变，仅追加基线后已稳定保存的新会话；不足一分钟的秒数保留到下次。"; }
                else { conflict = true; explanation = "本地增加值不能完全由基线后的新会话证明，需要选择导入或上传累计值。"; }
            }
        }
        else if (direction == PersonalSyncDirection.Bidirectional && floor != remoteMinutes && floor > 0 && remoteMinutes > 0)
        { conflict = true; explanation = "首次同步两侧都有不同累计值；先确认保留哪一侧，不能直接相加。"; }
        if (upload < (remoteMinutes ?? 0))
        { conflict |= direction == PersonalSyncDirection.Upload || direction == PersonalSyncDirection.Bidirectional && floor > 0 && (basis is null || local.EffectivePlaySeconds != basis.LocalSeconds);
            explanation = "云端累计值较大，只可导入，不会用较小的本地值覆盖。"; }
        if (upload > 60000) { conflict = true; explanation = "累计值超过官方单次写入上限 60,000 分钟；已保留原记录，不截断上传。"; }
        var write = upload >= (remoteMinutes ?? 0) && upload <= 60000 ? new PersonalWrite(work, upload) : null;
        var read = remoteMinutes is { } minutes ? new ImportedPersonalFacts(work, Math.Max(minutes, imported?.Minutes ?? 0), imported?.Rating,
            imported?.State, imported?.Completion, DateTimeOffset.UtcNow.ToString("o")) : null;
        var recommended = remoteMinutes is not null && (write is null || basis is not null && local.EffectivePlaySeconds == basis.LocalSeconds && remoteMinutes != basis.CloudMinutes
            || basis is null && floor == 0) ? PersonalSyncChoice.Import : PersonalSyncChoice.Upload;
        return new("time:" + local.Id, PersonalSyncKind.Playtime, work, local.Id, local.Name, $"{floor} 分钟（本地 {localMinutes}）",
            remoteMinutes is null ? "尚无记录" : $"{remoteMinutes} 分钟", conflict, explanation, write, read, Recommended: recommended);
    }
    public static long? IndependentSeconds(LibraryEntry local, SyncWorkCheckpoint basis)
    {
        if (!DateTimeOffset.TryParse(basis.UpdatedAt, out var at)) return null;
        var known = basis.SessionIds.ToHashSet(StringComparer.Ordinal);
        var added = local.Sessions.Where(x => !known.Contains(x.Id)).ToArray();
        if (added.Any(x => x.Native?.Lifecycle != SessionLifecycle.Completed || x.Native.Reason is not (SessionReason.ExitConfirmed or SessionReason.RecoveredProcessEnded)
            || !DateTimeOffset.TryParse(x.StartedAt, out var start) || start < at
            || !DateTimeOffset.TryParse(x.EndedAt, out var end) || end < at)) return null;
        long seconds = 0;
        foreach (var session in added) seconds = checked(seconds + session.Seconds);
        return local.EffectivePlaySeconds - basis.LocalSeconds == seconds ? seconds : null;
    }
    public static PersonalSyncRow State(LibraryEntry local, CloudWorkState? cloud, SyncWorkCheckpoint? basis, PersonalSyncDirection direction)
    {
        var work = local.Metadata?.Id ?? throw new OnlineException(OnlineFailure.InvalidRequest);
        var state = CloudStatus(local.Status);
        bool different = state != cloud?.State;
        bool conflict = different && (basis is null ? direction == PersonalSyncDirection.Bidirectional && cloud is not null
            : local.Status != basis.LocalState && (cloud?.State != basis.CloudState || cloud?.Completion != basis.Completion));
        var completion = state is "wish" or null ? null : cloud?.Completion;
        var explanation = "安装信息不参与状态转换；不推断通关程度，上传保留已有通关程度。";
        if (state == "wish" && cloud?.Completion is not null && direction != PersonalSyncDirection.Import
            && (direction == PersonalSyncDirection.Upload || basis is null || local.Status != basis.LocalState))
        { conflict = true; explanation = "改为想玩会清除云端通关程度，必须明确选择并确认。"; }
        var facts = cloud is null ? null : new ImportedPersonalFacts(work, State: cloud.State, Completion: cloud.Completion, FetchedAt: cloud.UpdatedAt);
        var importedStatus = cloud is null ? null : GameProgress.Higher(local.Status, LocalStatus(cloud.State));
        if (state is null) explanation = "未设置不上传，也不会清除云端记录。导入时按进度优先级保留较高状态。";
        else if (cloud is not null && importedStatus != LocalStatus(cloud.State)) explanation += " 导入不会降低当前进度。";
        return new("state:" + local.Id, PersonalSyncKind.State, work, local.Id, local.Name, StatusNames.Display(local.Status),
            cloud is null ? "尚无记录" : StatusNames.Display(LocalStatus(cloud.State)) + (cloud.Completion is null ? "" : " · " + cloud.Completion),
            conflict, explanation, state is null ? null : new(work, State: state, Completion: completion), facts, importedStatus,
            Recommended: state is null ? cloud is null ? PersonalSyncChoice.Skip : PersonalSyncChoice.Import : cloud is not null && basis is not null && local.Status == basis.LocalState && (cloud.State != basis.CloudState || cloud.Completion != basis.Completion)
                ? PersonalSyncChoice.Import : PersonalSyncChoice.Upload);
    }
    public static bool SameTime(CloudPlaytime? a, CloudPlaytime? b) => a?.Minutes == b?.Minutes;
    public static bool SameState(CloudWorkState? a, CloudWorkState? b) => a?.State == b?.State && a?.Completion == b?.Completion && a?.UpdatedAt == b?.UpdatedAt;
}
