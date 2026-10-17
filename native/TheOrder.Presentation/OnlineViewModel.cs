using TheOrder.Application;
using TheOrder.Domain;

namespace TheOrder.Presentation;

public enum OnlineState { Idle, Loading, Loaded, Empty, Offline, Unauthorized, RateLimited, Error, Refreshing, CredentialCorrupt }
public sealed record OnlineCard(string Id, string Title, string? CoverKey, string Subtitle);
public sealed record CatalogAccessNotice(string Title, string Message);
public sealed class OnlineViewModel(IOnlineCatalog catalog, ICredentialSettings credentials, IRemoteCoverRegistry covers, OnlineLibraryClient library, WorkspaceViewModel workspace) : ObservableState, IAsyncDisposable
{
    private string query = "";
    private string? cursor;
    private readonly HashSet<string> usedCursors = [];
    private CancellationTokenSource? searchStop, detailStop, mutationStop, connectionStop;
    private readonly CancellationTokenSource lifetime = new();
    private long searchGeneration, detailGeneration;
    private readonly HashSet<Task> pending = [];
    private bool disposed;
    private int credentialOperations;
    private long credentialGeneration, connectionGeneration;
    public string Query { get => query; set { if (!Set(ref query, value)) return; CancelSearch(); Items = []; cursor = null; usedCursors.Clear(); SearchState = OnlineState.Idle; SearchMessage = UiText.Get("输入作品名后搜索。"); Notify(); } }
    public OnlineState SearchState { get; private set; }
    public OnlineState DetailState { get; private set; }
    public OnlineState ConnectionState { get; private set; }
    public string SearchMessage { get; private set; } = UiText.Get("输入作品名后搜索。");
    public string DetailMessage { get; private set; } = UiText.Get("选择作品查看详情。");
    public string CredentialMessage { get; private set; } = UiText.Get("仅用于隔离开发资料；尚未检查密钥。");
    public string ConnectionMessage { get; private set; } = UiText.Get("尚未测试连接。");
    public IReadOnlyList<OnlineCard> Items { get; private set; } = Array.Empty<OnlineCard>();
    public WorkMetadata? Details { get; private set; }
    public string? DetailCoverKey { get; private set; }
    public bool SearchBusy => SearchState is OnlineState.Loading or OnlineState.Refreshing;
    public bool DetailBusy => DetailState is OnlineState.Loading or OnlineState.Refreshing;
    public bool CanLoadMore => !SearchBusy && cursor is not null && Items.Count < 2000;
    public bool HasCredential { get; private set; }
    private bool credentialChecked;
    public bool CanAccessCatalog => credentials is NextMoeAuthentication next
        ? next.CatalogAvailable && (next.CurrentStatus?.Mode != NextMoeAccessMode.ApiKey || HasCredential) : HasCredential;
    public CatalogAccessNotice? CatalogReminder
    {
        get
        {
            if (CanAccessCatalog) return null;
            if (credentials is NextMoeAuthentication next)
            {
                if (next.CurrentStatus?.Mode == NextMoeAccessMode.Account)
                    return new(UiText.Get("NextMoe 登录需要恢复"), UiText.Get("请前往设置重新登录 NextMoe 后继续在线识别，本地书架仍可使用。"));
                if (!credentialChecked) return null;
                if (next.CurrentStatus?.Mode != NextMoeAccessMode.ApiKey)
                    return new(UiText.Get("NextMoe 访问状态暂不可用"), UiText.Get("请前往设置检查当前访问方式。本地书架仍可使用，不会自动更换账号或移除登录资料。"));
            }
            return credentialChecked ? new(UiText.Get("请检查当前 API Key"),
                UiText.Get("当前选择了自定义 API Key。请在设置中检查密钥，或改用 NextMoe 账号、匿名访问。")) : null;
        }
    }
    private long accessGeneration;
    public void RefreshAccessState()
    {
        if (disposed) return;
        if (accessGeneration != AuthGeneration && CanAccessCatalog && ConnectionState == OnlineState.Unauthorized)
        { ConnectionState = OnlineState.Idle; ConnectionMessage = UiText.Get("访问方式已更新，请测试当前连接。"); Changed(nameof(ConnectionState)); Changed(nameof(ConnectionMessage)); }
        accessGeneration = AuthGeneration;
        Changed(nameof(CanAccessCatalog)); Changed(nameof(CatalogReminder));
    }
    public long AuthGeneration => (credentials as IOnlineAuthContextSource)?.AuthContext.Generation ?? credentialGeneration;
    public bool SettingsBusy => credentialOperations > 0 || ConnectionState == OnlineState.Loading;
    public string DetailTitle => Details is { } d ? CatalogLanguage.Title(d) : UiText.Get("作品详情");
    public string DetailDescription => Details is { } d ? CatalogLanguage.Project(d).Description : "";
    public string DetailFacts => Details is { } work ? UiText.Format($"目录编号 {work.Id}\n{work.OriginalTitle}\n{string.Join(" · ", work.Developers)}\n{work.ReleaseDate}\n{string.Join(" · ", work.Tags)}\n{string.Join(" · ", work.Ratings.Select(r => $"{r.Source} {r.Score}/{r.Scale}"))}\n{string.Join(" · ", work.Characters.Select(c => c.Name))}") : "";
    private Task Track(Func<Task> action)
    {
        if (disposed) return Task.CompletedTask;
        var task = action(); lock (pending) pending.Add(task); _ = Observe(task); return task;
    }
    private async Task Observe(Task task) { try { await task; } catch { /* Event handlers display controlled errors; disposal must still drain. */ } finally { lock (pending) pending.Remove(task); } }
    public Task SearchAsync(bool more = false) => Track(() => SearchCore(more));
    private async Task SearchCore(bool more)
    {
        if (SearchBusy || more && !CanLoadMore) return;
        var requested = Query.Trim(); if (requested.Length == 0) return;
        var next = more ? cursor : null;
        searchStop?.Cancel(); searchStop?.Dispose(); searchStop = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        var cancellation = searchStop.Token; var generation = ++searchGeneration;
        var authGeneration = AuthGeneration;
        if (!more) { Items = []; cursor = null; usedCursors.Clear(); }
        SearchState = more ? OnlineState.Refreshing : OnlineState.Loading; SearchMessage = UiText.Get("正在搜索在线目录…"); Notify();
        try
        {
            var page = await catalog.SearchAsync(requested, next, cancellation);
            if (disposed || generation != searchGeneration || authGeneration != AuthGeneration || cancellation.IsCancellationRequested) return;
            var seen = Items.Select(i => i.Id).ToHashSet();
            Items = Items.Concat(page.Items.Where(w => seen.Add(w.Id)).Select(w => new OnlineCard(w.Id, CatalogLanguage.Title(w), covers.Register(w.CoverUrl, w.FetchedAt), string.Join(" · ", w.Developers)))).Take(2000).ToArray();
            if (next is not null) usedCursors.Add(next);
            cursor = page.NextCursor is { } candidate && !usedCursors.Contains(candidate) ? candidate : null;
            SearchState = Items.Count == 0 ? OnlineState.Empty : OnlineState.Loaded;
            SearchMessage = Items.Count == 0 ? UiText.Get("本次搜索没有结果。") : UiText.Format($"已加载 {Items.Count} 部作品。");
            if (page.NextCursor is not null && cursor is null) SearchMessage += UiText.Get(" 分页标记重复，已停止加载。");
            if (Items.Count >= 2000) { cursor = null; SearchMessage += UiText.Get(" 已达到结果上限，请缩小搜索范围。"); }
        }
        catch (OperationCanceledException) { if (generation == searchGeneration) { SearchState = OnlineState.Idle; SearchMessage = UiText.Get("搜索已取消。"); } }
        catch (OnlineException e) { if (generation == searchGeneration && authGeneration == AuthGeneration) { SearchState = State(e); SearchMessage = e.Message; } }
        catch (Exception) { if (generation == searchGeneration && authGeneration == AuthGeneration) { SearchState = OnlineState.Error; SearchMessage = UiText.Get("在线搜索未完成，请重试。"); } }
        finally { if (generation == searchGeneration) { if (authGeneration != AuthGeneration) { Items = []; cursor = null; usedCursors.Clear(); SearchState = OnlineState.Idle; SearchMessage = UiText.Get("凭据已改变，请重新搜索。"); } Notify(); } }
    }
    public void CancelSearch() { searchGeneration++; searchStop?.Cancel(); SearchState = Items.Count > 0 ? OnlineState.Loaded : OnlineState.Idle; SearchMessage = UiText.Get("搜索已取消。"); Notify(); }
    public void ResetBrowsing()
    {
        CancelSearch(); detailGeneration++; detailStop?.Cancel();
        Items = []; cursor = null; usedCursors.Clear(); Details = null; DetailCoverKey = null;
        SearchState = DetailState = OnlineState.Idle;
        SearchMessage = UiText.Get("内容设置已改变，请重新搜索。"); DetailMessage = UiText.Get("选择作品查看详情。"); Notify();
    }
    public Task OpenDetailsAsync(string id, bool refresh = false) => Track(() => DetailsCore(id, refresh));
    private async Task DetailsCore(string id, bool refresh)
    {
        detailStop?.Cancel(); detailStop?.Dispose(); detailStop = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        var cancellation = detailStop.Token; var generation = ++detailGeneration;
        var authGeneration = AuthGeneration;
        Details = null; DetailCoverKey = null; DetailState = refresh ? OnlineState.Refreshing : OnlineState.Loading; DetailMessage = UiText.Get("正在读取作品详情…"); Notify();
        try
        {
            var result = await catalog.DetailsAsync(id, refresh, cancellation);
            if (disposed || generation != detailGeneration || authGeneration != AuthGeneration || cancellation.IsCancellationRequested) return;
            Details = result; DetailCoverKey = covers.Register(result.CoverUrl, result.FetchedAt); DetailState = OnlineState.Loaded; DetailMessage = UiText.Get("在线作品 · 未自动添加到书架");
        }
        catch (OperationCanceledException) { if (generation == detailGeneration) { DetailState = OnlineState.Idle; DetailMessage = UiText.Get("详情读取已取消。"); } }
        catch (OnlineException e) { if (generation == detailGeneration && authGeneration == AuthGeneration) { DetailState = State(e); DetailMessage = e.Message; } }
        catch (Exception) { if (generation == detailGeneration && authGeneration == AuthGeneration) { DetailState = OnlineState.Error; DetailMessage = UiText.Get("作品详情未能读取，请重试。"); } }
        finally { if (generation == detailGeneration) { if (authGeneration != AuthGeneration) { Details = null; DetailCoverKey = null; DetailState = OnlineState.Idle; DetailMessage = UiText.Get("凭据已改变，请重新读取详情。"); } Notify(); } }
    }
    public void Leave() { CancelSearch(); detailGeneration++; detailStop?.Cancel(); mutationStop?.Cancel(); connectionGeneration++; connectionStop?.Cancel(); if (ConnectionState == OnlineState.Loading) { ConnectionState = OnlineState.Idle; ConnectionMessage = UiText.Get("连接测试已取消。"); } if (DetailBusy) { DetailState = OnlineState.Idle; DetailMessage = UiText.Get("详情读取已取消。"); } Notify(); }
    public Task AddWishlistAsync() => Details is { } work ? Track(() => Mutate(c => library.AddAsync(work.Id, c))) : Task.CompletedTask;
    public Task AddWishlistAsync(string workId) => Track(() => Mutate(c => library.AddAsync(workId, c)));
    public Task RefreshEntryAsync(string id) => Track(() => Mutate(c => library.RefreshAsync(id, c), id));
    public Task RemoveWishlistAsync(string id) => Track(() => Mutate(c => library.RemoveWishlistAsync(id, c)));
    private async Task Mutate(Func<CancellationToken, Task<ClientResult>> action, string? detailId = null)
    {
        if (mutationStop is not null) return;
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); mutationStop = stop;
        try {
        await workspace.ExecuteFileMutationAsync(async cancellation =>
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellation, stop.Token);
            try { return await action(linked.Token); }
            catch (OnlineException e) { return new(ClientOutcome.ValidationFailure, e.Message); }
        }, detailId);
        } finally { mutationStop = null; }
    }
    public Task CheckCredentialAsync() => Track(async () =>
    {
        var generation = credentialGeneration;
        try
        {
            var present = await credentials.HasCredentialAsync(lifetime.Token);
            if (disposed || generation != credentialGeneration) return;
            credentialChecked = true; HasCredential = present; CredentialMessage = present ? UiText.Get("已保存隔离开发密钥。") : UiText.Get("尚未保存隔离开发密钥。");
        }
        catch (OperationCanceledException) { }
        catch (OnlineException e) { if (generation == credentialGeneration) { CredentialMessage = e.Message; ConnectionState = State(e); ConnectionMessage = e.Message; } }
        catch (Exception) { if (generation == credentialGeneration) CredentialMessage = UiText.Get("凭据状态检查未完成，请重试。"); }
        finally { if (!disposed && generation == credentialGeneration) { credentialChecked = true; Notify(); } }
    });
    public Task<bool> SaveCredentialAsync(string secret)
    {
        if (disposed) return Task.FromResult(false);
        var task = CredentialMutation(secret); lock (pending) pending.Add(task); _ = Observe(task); return task;
    }
    public Task ClearCredentialAsync() => Track(async () => { await CredentialMutation(null); });
    private async Task<bool> CredentialMutation(string? secret)
    {
        var generation = ++credentialGeneration; credentialOperations++;
        Leave();
        Details = null; DetailCoverKey = null; DetailState = OnlineState.Idle; DetailMessage = UiText.Get("选择作品查看详情。");
        Items = []; cursor = null; usedCursors.Clear(); SearchState = OnlineState.Idle; SearchMessage = UiText.Get("输入作品名后搜索。");
        ConnectionState = OnlineState.Idle; ConnectionMessage = UiText.Get("凭据正在更新，旧连接测试已作废。");
        CredentialMessage = secret is null ? UiText.Get("正在移除隔离开发密钥…") : UiText.Get("正在保存隔离开发密钥…"); Notify();
        try
        {
            if (secret is null) await credentials.ClearAsync(lifetime.Token); else await credentials.SaveAsync(secret, lifetime.Token);
            if (disposed || generation != credentialGeneration) return false;
            credentialChecked = true; HasCredential = secret is not null;
            CredentialMessage = HasCredential ? UiText.Get("密钥已使用 Windows 账户保护保存。") : UiText.Get("隔离开发密钥已移除。");
            ConnectionState = OnlineState.Idle;
            ConnectionMessage = HasCredential ? UiText.Get("密钥已更改，请测试连接。") : UiText.Get("尚未保存密钥，无法测试连接。");
            return true;
        }
        catch (OperationCanceledException) { if (generation == credentialGeneration) CredentialMessage = UiText.Get("凭据操作已取消，请检查当前状态。"); return false; }
        catch (Exception e)
        {
            if (generation == credentialGeneration)
            {
                CredentialMessage = e is OnlineException online ? online.Message : UiText.Get("凭据操作失败，请重试。");
                ConnectionState = e is OnlineException failure ? State(failure) : OnlineState.Error;
                ConnectionMessage = UiText.Get("凭据操作未完成；未宣称密钥已保存或移除。");
            }
            return false;
        }
        finally { credentialOperations--; if (!disposed) Notify(); }
    }
    public Task TestConnectionAsync() => Track(async () =>
    {
        connectionStop?.Cancel();
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); connectionStop = stop;
        var generation = ++connectionGeneration; var credential = credentialGeneration;
        var auth = (credentials as IOnlineAuthContextSource)?.AuthContext;
        var epoch = auth?.Generation;
        bool Current() => !disposed && generation == connectionGeneration && credential == credentialGeneration && epoch == auth?.Generation && !stop.IsCancellationRequested;
        ConnectionState = OnlineState.Loading; ConnectionMessage = UiText.Get("正在验证目录访问权限…"); Notify();
        try { await catalog.TestConnectionAsync(stop.Token); if (Current()) { ConnectionState = OnlineState.Loaded; ConnectionMessage = credentials is NextMoeAuthentication ? "连接成功：当前访问方式可读取公开目录。" : UiText.Get("连接成功：目录接口有效，密钥已获读取授权。"); } }
        catch (OperationCanceledException) { if (!disposed && generation == connectionGeneration && credential == credentialGeneration) { ConnectionState = OnlineState.Idle; ConnectionMessage = UiText.Get("连接测试已取消。"); } }
        catch (Exception e) { if (Current()) { ConnectionState = e is OnlineException online ? State(online) : OnlineState.Error; ConnectionMessage = e is OnlineException failure ? failure.Message : UiText.Get("连接测试未完成，请重试。"); } }
        finally { if (ReferenceEquals(connectionStop, stop)) connectionStop = null; if (!disposed && generation == connectionGeneration) Notify(); }
    });
    private static OnlineState State(OnlineException e) => e.Failure switch { OnlineFailure.Offline => OnlineState.Offline,
        OnlineFailure.NoCredential or OnlineFailure.InvalidCredential or OnlineFailure.Unauthorized or OnlineFailure.Forbidden or OnlineFailure.LoginRequired => OnlineState.Unauthorized,
        OnlineFailure.RateLimited => OnlineState.RateLimited, OnlineFailure.CredentialStorageCorrupt => OnlineState.CredentialCorrupt, _ => OnlineState.Error };
    private void Notify() { foreach (var property in new[] { nameof(Items), nameof(SearchState), nameof(SearchMessage), nameof(SearchBusy), nameof(CanLoadMore), nameof(Details), nameof(DetailCoverKey), nameof(DetailState), nameof(DetailMessage), nameof(DetailTitle), nameof(DetailDescription), nameof(DetailFacts), nameof(DetailBusy), nameof(CredentialMessage), nameof(HasCredential), nameof(CanAccessCatalog), nameof(CatalogReminder), nameof(ConnectionMessage), nameof(ConnectionState), nameof(SettingsBusy) }) Changed(property); }
    public async ValueTask DisposeAsync()
    {
        if (disposed) return; disposed = true; Leave(); lifetime.Cancel(); workspace.CancelPendingOperation();
        Task[] tasks; lock (pending) tasks = pending.ToArray();
        await Task.WhenAll(tasks); searchStop?.Dispose(); detailStop?.Dispose(); lifetime.Dispose();
    }
}
