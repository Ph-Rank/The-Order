using TheOrder.Application;

namespace TheOrder.Presentation;

// Independent on-demand operation: a failed link lookup never clears the album or track page.
public sealed class MusicListeningViewModel(IMusicListeningReader reader) : ObservableState, IDisposable
{
    private CancellationTokenSource? operation;
    private long generation;
    private bool disposed, digitalAttempted;
    public MusicBrainzReference? Reference { get; private set; }
    public MusicOutletResult? Current { get; private set; }
    public MusicDigitalOutletResult? Digital { get; private set; }
    public AppleCatalogResult? Apple { get; private set; }
    public MediaUserLinks? Choices { get; private set; }
    public string Message { get; private set; } = "展开后读取已收录的入口。";
    // Routine success and bounded-search notes belong in source details. Actual
    // failures, offline fallback and empty results must remain visible.
    public bool NeedsAttention { get; private set; }
    public bool Busy => operation is not null;
    public bool CanLoad => !disposed && Reference is not null && !Busy;
    public bool CanFindDigital => CanLoad && Current?.Snapshot is { Entity.Kind: MusicBrainzEntityKind.Release, GroupId: not null };
    public bool CanFindApple => CanLoad && Current?.Snapshot.Entity.Kind == MusicBrainzEntityKind.Release;
    public void SetContext(MusicBrainzReference? reference)
    {
        if (disposed || Reference == reference) return;
        Stop(); Reference = MusicOutlets.Reference(reference) ? reference : null; Current = null; Digital = null; Apple = null; Choices = null; digitalAttempted = false;
        Message = "展开后读取已收录的入口；平台存在不代表当前地区或订阅可播放。"; NeedsAttention = false; Changed(null);
    }
    private bool HasDigitalContext => Current?.Snapshot is { Entity.Kind: MusicBrainzEntityKind.Release, GroupId: not null };
    // One operation owns both stages. A collapse, navigation or cancellation
    // cannot let a late first-stage result start a second request for a new page.
    public Task ExpandAsync(bool refresh = false) => !CanLoad || !refresh && Current is not null && (digitalAttempted || !HasDigitalContext)
        ? Task.CompletedTask : Run(async (reference, token, accepts) =>
    {
        if (refresh || Current is null) await ReadCurrentAsync(reference, refresh, token, accepts);
        if (!accepts() || !HasDigitalContext || !refresh && digitalAttempted) return;
        Message = "正在读取数字版入口…"; Changed(null);
        await ReadDigitalAsync(Current!.Snapshot, token, accepts);
    });
    public Task LoadAsync(bool refresh = false) => !CanLoad ? Task.CompletedTask : Run((reference, token, accepts) => ReadCurrentAsync(reference, refresh, token, accepts));
    private async Task ReadCurrentAsync(MusicBrainzReference reference, bool refresh, CancellationToken token, Func<bool> accepts)
    {
        var result = await reader.LoadOutletsAsync(reference, refresh, token); if (!accepts()) return;
        if (!MusicOutlets.Valid(result.Snapshot) || result.Snapshot.Entity != reference) throw new InvalidDataException();
        if (Current?.Snapshot.GroupId != result.Snapshot.GroupId) { Digital = null; digitalAttempted = false; }
        Current = result; Message = result.Warning ?? (result.Snapshot.Links.Length == 0 ? "MusicBrainz 暂未收录可核对的直达入口。" : "收听、购买及资料关系由 MusicBrainz 收录，当前可用性以平台为准。");
        NeedsAttention = result.Warning is not null;
        if (reference.Kind == MusicBrainzEntityKind.Release)
        {
            try { var values = await reader.ReadAppleChoicesAsync(reference, token); if (accepts()) Choices = values; }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { if (accepts()) { Message += " 已保存的商店页面暂时无法读取，原记录保留。"; NeedsAttention = true; } }
        }
    }
    public Task FindDigitalAsync() => !CanFindDigital ? Task.CompletedTask : Run((_, token, accepts) => ReadDigitalAsync(Current!.Snapshot, token, accepts));
    private async Task ReadDigitalAsync(MusicOutletSnapshot current, CancellationToken token, Func<bool> accepts)
    {
        var result = await reader.LoadDigitalOutletsAsync(current, token);
        if (!accepts()) return;
        if (result.Editions.Length > 2 || result.Editions.Any(x => !MusicOutlets.Valid(x.Snapshot) || x.Snapshot.Entity.Kind != MusicBrainzEntityKind.Release || x.Snapshot.ReleaseStatus != "Official"
            || x.Snapshot.Formats.Length == 0 || x.Snapshot.Formats.Any(f => f != "Digital Media") || x.Snapshot.GroupId != current.GroupId || x.Snapshot.Entity == current.Entity)) throw new InvalidDataException();
        Digital = result; digitalAttempted = true;
        Message = Current?.Warning ?? result.Warning ?? (result.Editions.Length == 0 ? "未找到其他数字发行版入口。" : "以下属于同组的其他数字版本，没有更换当前发行版。");
        NeedsAttention = result.Editions.Length == 0 || Current?.Warning is not null;
    }
    public Task FindAppleAsync(string country, bool refresh = false) => !CanFindApple || !MusicOutlets.Country(country) ? Task.CompletedTask : Run(async (reference, token, accepts) =>
    {
        var result = await reader.SearchAppleAsync(Current!.Snapshot, country, refresh, token); if (!accepts()) return;
        if (!result.IsValid || result.Query.Release != reference || result.Query.Country != country) throw new InvalidDataException();
        Apple = result; Message = result.Warning ?? (result.Candidates.Length == 0 ? "未找到可核对的 iTunes 商店专辑页面；这不代表没有正版资源。" : "请核对专辑、音乐人和版本后保存商店页面；未核验 Apple Music 订阅可听。");
    });
    public Task SaveAppleAsync(AppleAlbumCandidate candidate) => !CanLoad || Reference?.Kind != MusicBrainzEntityKind.Release
        || Apple?.Query.Release != Reference || Apple.Candidates.Contains(candidate) != true ? Task.CompletedTask : Run(async (reference, token, accepts) =>
    {
        var values = await reader.ChangeAppleChoiceAsync(reference, candidate, candidate.Target, Choices?.Revision ?? 0, token);
        if (accepts()) { Choices = values; Message = "已保存这个 iTunes 商店页面；这是独立关联，未更换 MusicBrainz 发行版。"; }
    });
    public Task RemoveAppleAsync(MediaUserLink choice) => !CanLoad || Reference?.Kind != MusicBrainzEntityKind.Release
        || Choices?.Items.Contains(choice) != true ? Task.CompletedTask : Run(async (reference, token, accepts) =>
    {
        var values = await reader.ChangeAppleChoiceAsync(reference, null, choice.Target, Choices!.Revision, token);
        if (accepts()) { Choices = values; Message = "已移除商店页面，当前发行版及其他资料保持。"; }
    });
    private async Task Run(Func<MusicBrainzReference,CancellationToken,Func<bool>,Task> action)
    {
        var reference = Reference!; Stop(); var version = generation; var cancel = new CancellationTokenSource(); operation = cancel;
        bool Accepts() => !disposed && Reference == reference && generation == version && !cancel.IsCancellationRequested;
        Message = "正在读取音乐入口…"; NeedsAttention = false; Changed(null);
        try { await action(reference, cancel.Token, Accepts); }
        catch (OperationCanceledException) { if (!disposed && generation == version) { Message = "已取消读取，已有资料保持。"; NeedsAttention = true; } }
        catch (Exception e) when (e is MusicBrainzException or IOException or UnauthorizedAccessException or InvalidDataException or NotSupportedException)
        { if (Accepts()) { Message = e is MusicBrainzException or NotSupportedException ? e.Message : "入口或本地选择暂时无法处理，原资料与选择保持。请重试。"; NeedsAttention = true; } }
        finally { if (ReferenceEquals(operation, cancel)) operation = null; cancel.Dispose(); if (!disposed) Changed(null); }
    }
    private void Stop() { generation++; var prior = operation; operation = null; prior?.Cancel(); }
    public void Cancel() { if (disposed) return; Stop(); Message = "已取消读取，已有资料保持。"; NeedsAttention = true; Changed(null); }
    public void Dispose() { if (disposed) return; disposed = true; Stop(); Reference = null; }
}
