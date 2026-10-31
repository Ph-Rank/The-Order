using System.Collections.Immutable;
using TheOrder.Application;

namespace TheOrder.Presentation;

public sealed class WalkthroughViewModel(IWalkthroughReader reader,
    Func<long>? authGeneration = null, Func<bool>? sourceCurrent = null) : ObservableState, IDisposable
{
    private CancellationTokenSource? request;
    private long generation;
    private bool disposed;
    private readonly HashSet<string> cursors = new(StringComparer.Ordinal);
    public string? WorkId { get; private set; }
    public ImmutableArray<WalkthroughSummary> Items { get; private set; } = [];
    public WalkthroughDocument? Document { get; private set; }
    public string? SelectedId { get; private set; }
    public string? NextCursor { get; private set; }
    public bool Busy => request is not null;
    public bool IsSourceCurrent => sourceCurrent?.Invoke() != false;
    public string Message { get; private set; } = "读取当前作品的网络攻略，可按路线阅读或查看概览。";
    public long Version => generation;
    public void SetSource(string? workId)
    {
        if (disposed) return;
        CancelCore(); WorkId = CatalogReleaseFacts.Id(workId) ? workId : null;
        Items = []; Document = null; SelectedId = NextCursor = null; cursors.Clear();
        Message = WorkId is null ? "先为此作品关联 NextMoe 资料，再读取网络攻略。" : "读取当前作品的网络攻略，可按路线阅读或查看概览。";
        Notify();
    }
    public Task RefreshAsync() => LoadAsync(false);
    public Task NextAsync() => NextCursor is null ? Task.CompletedTask : LoadAsync(true);
    private async Task LoadAsync(bool append)
    {
        if (disposed || WorkId is not { } workId || Busy) return;
        long auth = authGeneration?.Invoke() ?? 0;
        var tokenSource = request = new(); var version = ++generation; string? cursor = append ? NextCursor : null;
        bool Current() => !disposed && version == generation && !tokenSource.IsCancellationRequested && WorkId == workId
            && auth == (authGeneration?.Invoke() ?? 0) && sourceCurrent?.Invoke() != false;
        if (!append) { Items = []; Document = null; SelectedId = NextCursor = null; cursors.Clear(); }
        Message = append ? "正在读取下一页攻略…" : "正在读取对应作品的攻略…"; Notify();
        try
        {
            // A known local work follows the direct-details contract. Discovery filtering is separate.
            var page = await reader.ListAsync(workId, cursor, true, tokenSource.Token);
            if (!Current()) return;
            if (page.Items.Length > 50 || page.Items.Any(g => g.WorkId != workId)
                || page.NextCursor is not null && (!CatalogReleaseFacts.Cursor(page.NextCursor) || page.NextCursor == cursor || cursors.Contains(page.NextCursor)))
                throw new InvalidDataException();
            var items = append ? Items.ToDictionary(g => g.Id, StringComparer.Ordinal) : [];
            foreach (var guide in page.Items)
            {
                if (items.TryGetValue(guide.Id, out var previous) && previous != guide) throw new InvalidDataException();
                items.TryAdd(guide.Id, guide);
            }
            if (items.Count > 500) throw new InvalidDataException();
            Items = items.Values.ToImmutableArray(); NextCursor = page.NextCursor;
            if (cursor is not null) cursors.Add(cursor);
            Message = Items.IsEmpty ? "此作品暂无可读取的网络攻略；这不代表其他版本或来源没有攻略。"
                : $"找到 {Items.Length} 份攻略，选择一份阅读。";
        }
        catch (OperationCanceledException) { if (Current()) Message = "读取已取消，可以重试。"; }
        catch (OnlineException error) { if (Current()) Message = Failure(error.Failure); }
        catch (InvalidDataException) { if (Current()) Message = "攻略列表结构不受支持，已有资料保持不变。"; }
        finally
        {
            if (ReferenceEquals(request, tokenSource)) request = null;
            tokenSource.Dispose(); if (!disposed) Notify();
        }
    }
    public async Task SelectAsync(string id)
    {
        if (disposed || WorkId is not { } workId || !Items.Any(g => g.Id == id)) return;
        CancelCore(); long auth = authGeneration?.Invoke() ?? 0;
        var tokenSource = request = new(); var version = ++generation;
        bool Current() => !disposed && version == generation && !tokenSource.IsCancellationRequested && WorkId == workId
            && auth == (authGeneration?.Invoke() ?? 0) && sourceCurrent?.Invoke() != false;
        SelectedId = id; Document = null; Message = "正在读取攻略步骤与路线图…"; Notify();
        try
        {
            var document = await reader.ReadAsync(id, workId, true, tokenSource.Token);
            if (!Current()) return;
            if (document.Summary.Id != id || document.Summary.WorkId != workId) throw new InvalidDataException();
            Document = document;
            Message = $"已载入 {document.Segments.Length} 条路线段、{document.Segments.Sum(s => s.Steps.Length)} 个步骤。";
        }
        catch (OperationCanceledException) { if (Current()) Message = "读取已取消，可以重试。"; }
        catch (OnlineException error) { if (Current()) Message = Failure(error.Failure); }
        catch (InvalidDataException) { if (Current()) Message = "攻略结构不受支持，未显示不完整路线。"; }
        finally
        {
            if (ReferenceEquals(request, tokenSource)) request = null;
            tokenSource.Dispose(); if (!disposed) Notify();
        }
    }
    public void Invalidate()
    {
        if (disposed) return; CancelCore(); Items = []; Document = null; SelectedId = NextCursor = null; cursors.Clear();
        Message = "作品或账号已变化，请重新读取攻略。"; Notify();
    }
    public void Cancel() { if (disposed) return; CancelCore(); Message = "读取已取消，可以重试。"; Notify(); }
    private void CancelCore() { generation++; request?.Cancel(); request = null; }
    private static string Failure(OnlineFailure failure) => failure switch
    {
        OnlineFailure.NotFound => "攻略当前不可见或已移除，请核对作品关联与资料源访问状态。",
        OnlineFailure.Offline or OnlineFailure.Timeout => "网络攻略读取失败，请检查网络后重试。",
        OnlineFailure.RateLimited or OnlineFailure.QuotaExceeded => "攻略服务暂时限流，请稍后重试。",
        OnlineFailure.InvalidResponse or OnlineFailure.ResponseTooLarge => "攻略数据过大或结构不受支持，未显示不完整路线。",
        _ => "攻略暂未能读取，请检查资料源访问状态后重试。"
    };
    private void Notify() => Changed("");
    public void Dispose() { if (disposed) return; disposed = true; CancelCore(); }
}
