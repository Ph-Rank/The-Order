using System.Collections.Immutable;
using System.ComponentModel;
using TheOrder.Application;

namespace TheOrder.Presentation;

public enum WorkspacePage { Library, Details, Settings, Import, Installations, Online, Recognition, Statistics }
public enum UnsavedChoice { Stay, Save, Discard }
public enum PreferenceInitialization { Uninitialized, Loading, Ready, LoadFailed }
public enum RefreshOutcome { None, Success, Failed, Superseded }

// No JSON, file paths used for access, network, native API or repository implementation here.
// Await continuations return to the caller's UI context; Application owns background work.
public sealed class WorkspaceViewModel(IOfflineLibraryClient client, IUiPreferencesStore preferencesStore, NativeConfiguration configuration, IClientTextRules textRules,
    ICachedCoverRecommendationSource? cachedCovers = null) : ObservableState, IAsyncDisposable
{
    private readonly CancellationTokenSource lifetime = new();
    private CancellationTokenSource? searchCancellation, detailsCancellation;
    private long searchGeneration, detailsGeneration, draftRevision;
    private CancellationTokenSource? operationCancellation;
    private NativeTheme? pendingTheme;
    private string? pendingLayout, pendingSort;
    private Task<ClientResult>? saving;
    private Task preferenceSaves = Task.CompletedTask;
    private bool loading, searching, detailsLoading, queryReadyDuringReload, disposed;
    private ImmutableArray<GameSummary> items = [];
    private IReadOnlyList<GameSummary> itemView = Array.Empty<GameSummary>();
    private readonly Dictionary<string, string?> preparedCovers = new(StringComparer.Ordinal);
    public void RefreshPreparedCover(string workId, string? key)
    {
        if (disposed) return;
        if (preparedCovers.Count >= 128 && !preparedCovers.ContainsKey(workId)) preparedCovers.Remove(preparedCovers.Keys.First());
        preparedCovers[workId] = key;
        var next = items.Select(PreparedCover).ToImmutableArray();
        if (items.SequenceEqual(next)) return;
        items = next; itemView = items; Changed(nameof(Items));
    }
    private GameSummary PreparedCover(GameSummary item) => item.AutomaticCover && item.CatalogWorkId is { } id && preparedCovers.TryGetValue(id, out var key) ? item with { CoverKey = key } : item;
    private async Task<ImmutableArray<GameSummary>> RestorePreparedCoversAsync(ImmutableArray<GameSummary> rows, CancellationToken cancellation)
    {
        if (cachedCovers is null) return rows;
        var saved = new Dictionary<string, CachedCoverRecommendation>(StringComparer.Ordinal);
        foreach (var id in rows.Where(item => item.AutomaticCover && item.CatalogWorkId is not null)
            .Select(item => item.CatalogWorkId!).Distinct(StringComparer.Ordinal))
        {
            cancellation.ThrowIfCancellationRequested();
            // Already prepared in this process takes precedence over an older disk read.
            if (preparedCovers.ContainsKey(id)) continue;
            if (await cachedCovers.ReadCoverAsync(id, cancellation) is { } cover) saved[id] = cover;
        }
        cancellation.ThrowIfCancellationRequested();
        return rows.Select(item => item.AutomaticCover && item.CatalogWorkId is { } id && saved.TryGetValue(id, out var cover)
            ? item with { CoverKey = cover.Key } : item).ToImmutableArray();
    }
    private ImmutableArray<string> categories = [], tags = [];
    private PersonalDraft? draft;
    private ClientResult notice = new(ClientOutcome.Success, "");
    private ClientPreferences preferences = new();
    private LibraryQuery query = new();
    private LibrarySnapshot? displayedSessionLibrary;
    private Task? sessionLibraryRefresh;
    private WorkspacePage page;
    private string? selectedId;
    private int total;
    private bool externalBusy;
    public void SetExternalBusy(bool value) { externalBusy = value; StateChanged(); }
    public IReadOnlyList<GameSummary> Items => itemView;
    public IReadOnlyList<string> Categories => categories;
    public IReadOnlyList<string> Tags => tags;
    public ClientPreferences Preferences => preferences;
    public IReadOnlyList<SavedFilter> SavedFilters => preferences.Filters;
    public LibraryQuery Query => query;
    public string Layout => preferences.Layout;
    public NativeTheme Theme => preferences.Theme;
    public WorkspacePage Page => page;
    public string? SelectedId => selectedId;
    public PersonalDraft? Draft => draft;
    public bool IsDirty => draft?.IsDirty == true;
    public bool IsLoading => loading;
    public bool IsSearching => searching;
    public bool IsDetailsLoading => detailsLoading;
    public bool IsSaving { get; private set; }
    public PreferenceInitialization PreferenceState { get; private set; }
    public bool CanManageFilters => PreferenceState == PreferenceInitialization.Ready && IsLoaded && !disposed;
    public ClientResult? PersistenceResult { get; private set; }
    public RefreshOutcome LastRefresh { get; private set; }
    public long SelectionVersion { get; private set; }
    public int TotalCount => total;
    public bool CanEdit => client.Capability.CanEdit && !loading && !IsSaving && !externalBusy && !disposed;
    public bool CanEditDetails => CanEdit && draft is not null;
    public bool CanSave => CanEdit && IsDirty;
    public bool CanReload => !loading && !IsSaving && !externalBusy && !disposed;
    public bool IsReadOnly => client.Capability.Loaded && !client.Capability.CanEdit;
    public string CapabilityMessage => client.Capability.Message;
    public bool IsLoaded => client.Capability.Loaded;
    public bool IsEmpty => IsLoaded && !loading && !searching && items.IsEmpty;
    public string EmptyText => total == 0 ? UiText.Get("书架还没有作品。选择“导入目录”添加本地游戏。") : UiText.Get("没有符合当前条件的作品。试试清除筛选。");
    public string CountText => IsLoaded ? UiText.Format($"{items.Length:N0} / {total:N0} 部作品") : UiText.Get("尚未加载书架");
    public ClientResult Notice => notice;
    public bool HasNotice => notice.Message.Length != 0;
    public string DataDirectory => configuration.DataDirectory ?? UiText.Get("未指定；不会访问正式书架");
    public double LastQueryMilliseconds { get; private set; }
    public event Action? ThemeChanged;

    private void StateChanged()
    {
        foreach (var name in new[] { nameof(CanEdit), nameof(CanEditDetails), nameof(CanSave), nameof(CanReload), nameof(CanManageFilters), nameof(IsReadOnly), nameof(IsLoaded), nameof(CapabilityMessage), nameof(IsEmpty), nameof(EmptyText), nameof(CountText), nameof(IsDirty) }) Changed(name);
    }
    private void Report(ClientResult result) { notice = result; Changed(nameof(Notice)); Changed(nameof(HasNotice)); }
    public void ReportUnexpected() => Report(new(ClientOutcome.LoadFailure, UiText.Get("操作未能完成。当前草稿已保留，请检查开发日志或重新读取。")));
    // Selection intentionally clears on any query, layout, reload or projection replacement.
    // A matching ID never carries a draft/token into a different entry lifecycle.
    private void ClearSelection() { SelectionVersion++; Changed(nameof(SelectionVersion)); }
    private void AppearanceChanged()
    {
        Changed(nameof(Preferences)); Changed(nameof(SavedFilters)); Changed(nameof(Layout)); Changed(nameof(Theme));
        Changed(nameof(PreferenceState)); Changed(nameof(CanManageFilters)); ThemeChanged?.Invoke();
    }
    public async Task InitializeAsync()
    {
        if (PreferenceState != PreferenceInitialization.Uninitialized) return;
        PreferenceState = PreferenceInitialization.Loading; AppearanceChanged();
        await ReloadAsync();
        if (disposed) return;
        try
        {
            var loaded = await preferencesStore.LoadAsync(lifetime.Token);
            if (disposed) return;
            preferences = loaded with { Theme = pendingTheme ?? loaded.Theme, Layout = pendingLayout ?? loaded.Layout, Sort = pendingSort ?? loaded.Sort };
            PreferenceState = PreferenceInitialization.Ready;
            AppearanceChanged(); ClearSelection();
            if (pendingTheme.HasValue || pendingLayout is not null || pendingSort is not null) await QueuePreferencesSave();
        }
        catch (OperationCanceledException) { return; }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or DataIsolationException or ArgumentException)
        { PreferenceState = PreferenceInitialization.LoadFailed; Report(new(ClientOutcome.ValidationFailure, UiText.Get("界面偏好无法读取，旧文件未覆盖。外观更改仅本次有效；本次运行不再写入偏好。"))); }
        if (disposed) return;
        AppearanceChanged();
        await SetQueryAsync(query with { Sort = preferences.Sort });
    }
    public async Task<bool> ReloadAsync()
    {
        if (IsDirty || IsSaving || loading || disposed) return false;
        loading = true; queryReadyDuringReload = false; Changed(nameof(IsLoading)); StateChanged();
        ClearSelection();
        searchCancellation?.Cancel(); detailsCancellation?.Cancel(); ++searchGeneration; ++detailsGeneration;
        searching = false; detailsLoading = false; Changed(nameof(IsSearching)); Changed(nameof(IsDetailsLoading));
        try
        {
            var result = await client.LoadAsync(lifetime.Token);
            if (disposed) return false;
            Report(result); SetDraft(null);
            if (!client.Capability.Loaded) { items = []; itemView = Array.Empty<GameSummary>(); Changed(nameof(Items)); total = 0; return false; }
            queryReadyDuringReload = true;
            return await SearchCoreAsync(reportFailure: true) == RefreshOutcome.Success;
        }
        catch (OperationCanceledException) { return false; }
        finally { loading = false; queryReadyDuringReload = false; Changed(nameof(IsLoading)); StateChanged(); }
    }
    public async Task SetQueryAsync(LibraryQuery value)
    {
        if (disposed) return;
        ClearSelection();
        query = value; Changed(nameof(Query));
        if (preferences.Sort != value.Sort)
        {
            preferences = preferences with { Sort = value.Sort };
            if (PreferenceState != PreferenceInitialization.Ready) pendingSort = value.Sort;
            _ = QueuePreferencesSave();
        }
        if (!loading || queryReadyDuringReload) await SearchAsync(); // While opening, defer; once opened, latest query may supersede Reload's query.
    }
    public async Task SearchAsync() => await SearchCoreAsync(reportFailure: true);
    // Refresh the committed in-memory projection, never reload the application-owned writer
    // or replace a Details draft. The existing shell tick supplies the latest snapshot:
    // one query may be in flight; busy/superseded updates are retried on a later tick.
    public Task SynchronizeSessionLibraryAsync(LibrarySnapshot? snapshot)
    {
        if (snapshot is null || ReferenceEquals(snapshot, displayedSessionLibrary) || disposed || !IsLoaded
            || loading || searching || IsSaving || externalBusy || sessionLibraryRefresh is { IsCompleted: false })
            return Task.CompletedTask;
        return sessionLibraryRefresh = SynchronizeSessionLibraryCoreAsync(snapshot);
    }
    private async Task SynchronizeSessionLibraryCoreAsync(LibrarySnapshot snapshot)
    {
        if (await SearchCoreAsync(reportFailure: false, preserveSelection: true) == RefreshOutcome.Success)
            displayedSessionLibrary = snapshot;
    }
    private async Task<RefreshOutcome> SearchCoreAsync(bool reportFailure, bool preserveSelection = false)
    {
        if (!client.Capability.Loaded || disposed) return RefreshOutcome.Superseded;
        searchCancellation?.Cancel(); searchCancellation?.Dispose();
        searchCancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        var cancellation = searchCancellation.Token; var generation = ++searchGeneration; var requested = query;
        searching = true; Changed(nameof(IsSearching)); StateChanged();
        try
        {
            await Task.Delay(120, cancellation);
            var timer = System.Diagnostics.Stopwatch.StartNew();
            var result = await client.QueryAsync(requested, cancellation);
            if (disposed || generation != searchGeneration || cancellation.IsCancellationRequested) return RefreshOutcome.Superseded;
            var restored = await RestorePreparedCoversAsync(result.Items, cancellation);
            if (disposed || generation != searchGeneration || cancellation.IsCancellationRequested) return RefreshOutcome.Superseded;
            LastQueryMilliseconds = timer.Elapsed.TotalMilliseconds;
            items = restored.Select(PreparedCover).ToImmutableArray(); itemView = items; total = result.Total; categories = result.Categories; tags = result.Tags;
            if (!preserveSelection) ClearSelection();
            Changed(nameof(Items)); Changed(nameof(Categories)); Changed(nameof(Tags)); Changed(nameof(LastQueryMilliseconds));
            return RefreshOutcome.Success;
        }
        catch (OperationCanceledException) { return RefreshOutcome.Superseded; }
        catch (Exception)
        {
            if (generation != searchGeneration || disposed) return RefreshOutcome.Superseded;
            if (reportFailure) ReportUnexpected();
            return RefreshOutcome.Failed;
        }
        finally { if (generation == searchGeneration) { searching = false; Changed(nameof(IsSearching)); StateChanged(); } }
    }
    public async Task<bool> ResolveUnsavedAsync(Func<Task<UnsavedChoice>> choose)
    {
        if (saving is { IsCompleted: false }) await saving;
        if (!IsDirty) return true;
        return await choose() switch { UnsavedChoice.Discard => Discard(), UnsavedChoice.Save => (await SaveAsync()).IsCommitted, _ => false };
    }
    private bool Discard() { CancelDraft(); return true; }
    public void CancelDraft() { if (!disposed && !IsSaving && draft is not null) SetDraft(new(draft.Source)); }
    public async Task CancelAndReloadDetailsAsync()
    {
        CancelDraft();
        if (selectedId is { } id) await NavigateAsync(WorkspacePage.Details, id, () => Task.FromResult(UnsavedChoice.Stay));
    }
    public async Task<bool> NavigateAsync(WorkspacePage destination, string? id, Func<Task<UnsavedChoice>> choose)
    {
        if (disposed || !await ResolveUnsavedAsync(choose)) return false;
        detailsCancellation?.Cancel(); detailsCancellation?.Dispose();
        detailsCancellation = null;
        var generation = ++detailsGeneration;
        detailsLoading = false; Changed(nameof(IsDetailsLoading));
        if (destination != WorkspacePage.Details)
        {
            SetDraft(null); selectedId = null; Changed(nameof(SelectedId));
            page = destination; Changed(nameof(Page)); return true;
        }
        if (id is null || !IsLoaded) return false;
        detailsCancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        var cancellation = detailsCancellation.Token;
        var revision = draftRevision;
        var originalToken = draft?.Source.Id == id ? draft.Source.Token : null;
        detailsLoading = true; Changed(nameof(IsDetailsLoading));
        try
        {
            var details = await client.DetailsAsync(id, cancellation);
            if (disposed || generation != detailsGeneration || cancellation.IsCancellationRequested || revision != draftRevision) return false;
            if (originalToken is not null && (originalToken.Epoch != details.Token.Epoch || originalToken.Generation != details.Token.Generation))
            { Report(new(ClientOutcome.Conflict, UiText.Get("条目生命周期已变化，未覆盖当前草稿。请返回书架后重新打开。"))); return false; }
            selectedId = id; SetDraft(new(details)); page = WorkspacePage.Details;
            Changed(nameof(SelectedId)); Changed(nameof(Page)); return true;
        }
        catch (OperationCanceledException) { return false; }
        catch (Exception) { if (generation == detailsGeneration && !disposed) ReportUnexpected(); return false; }
        finally { if (generation == detailsGeneration) { detailsLoading = false; Changed(nameof(IsDetailsLoading)); } }
    }
    private void SetDraft(PersonalDraft? value)
    {
        draftRevision++;
        if (draft is not null) draft.PropertyChanged -= DraftChanged;
        draft = value;
        if (draft is not null) draft.PropertyChanged += DraftChanged;
        Changed(nameof(Draft)); StateChanged();
    }
    private void DraftChanged(object? sender, PropertyChangedEventArgs e)
    { if (e.PropertyName == nameof(PersonalDraft.IsDirty)) { draftRevision++; StateChanged(); } }
    public Task<ClientResult> SaveAsync()
    {
        if (saving is { IsCompleted: false }) return saving;
        if (!CanSave || draft is null) return Task.FromResult(new ClientResult(ClientOutcome.ReadOnly, IsReadOnly ? CapabilityMessage : UiText.Get("没有可保存的修改。")));
        var source = draft.Source; var input = draft.Input;
        return saving = RunMutationAsync(token => client.SaveAsync(source, input, token), source.Id);
    }
    // Includes preprocessing, admission, durable commit and refresh. Close drains this whole task.
    private async Task<ClientResult> RunMutationAsync(Func<CancellationToken, Task<ClientResult>> mutation, string? detailId = null, bool preserveSelection = false)
    {
        operationCancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        IsSaving = true; Changed(nameof(IsSaving)); StateChanged();
        PersistenceResult = null; LastRefresh = RefreshOutcome.None;
        // Invalidate pending details before accepting a save of the current draft snapshot.
        detailsCancellation?.Cancel(); ++detailsGeneration;
        detailsLoading = false; Changed(nameof(IsDetailsLoading));
        try
        {
            var result = await mutation(operationCancellation.Token);
            PersistenceResult = result;
            if (disposed) return result;
            Report(result);
            if (result.IsCommitted)
            {
                var detailFailed = false;
                if (detailId is not null)
                {
                    // Retire the committed draft even if subsequent projection reads fail.
                    SetDraft(null);
                    try { SetDraft(new(await client.DetailsAsync(detailId, lifetime.Token))); }
                    catch (Exception) { detailFailed = true; }
                }
                var queryRefresh = await SearchCoreAsync(reportFailure: false, preserveSelection: preserveSelection);
                LastRefresh = detailFailed ? RefreshOutcome.Failed : queryRefresh;
                if (LastRefresh == RefreshOutcome.Failed)
                    Report(new(ClientOutcome.CommittedWithWarning, result.Message + UiText.Get(" 界面刷新失败；修改已经保存，请重新读取，不要重复提交。")));
            }
            return result;
        }
        catch (OperationCanceledException) { var result = new ClientResult(ClientOutcome.Cancelled, detailId is null ? UiText.Get("批量修改已在提交前取消，未写入本次修改。") : UiText.Get("提交前已取消，草稿已保留。")); Report(result); return result; }
        catch (InvalidDataException e) { var result = new ClientResult(ClientOutcome.ValidationFailure, e.Message); Report(result); return result; }
        catch (Exception) { ReportUnexpected(); return notice; }
        finally
        {
            operationCancellation.Dispose(); operationCancellation = null;
            IsSaving = false; Changed(nameof(IsSaving)); Changed(nameof(PersistenceResult)); Changed(nameof(LastRefresh)); StateChanged();
        }
    }
    public void CancelPendingOperation() => operationCancellation?.Cancel();
    public Task<ClientResult> ExecuteFileMutationAsync(Func<CancellationToken, Task<ClientResult>> operation, string? detailId = null, bool preserveSelection = false)
    {
        if (saving is { IsCompleted: false }) return saving;
        if (!CanEdit || IsDirty) return Task.FromResult(new ClientResult(ClientOutcome.ReadOnly, UiText.Get("请先处理个人资料草稿，或等待当前操作完成。")));
        return saving = RunMutationAsync(operation, detailId, preserveSelection);
    }
    private Task<ClientResult> StartBatchAsync(Func<CancellationToken, Task<ClientResult>> mutation)
    {
        if (saving is { IsCompleted: false }) return saving;
        if (!CanEdit || IsDirty) return Task.FromResult(new ClientResult(ClientOutcome.ReadOnly, UiText.Get("当前不能批量编辑，请先处理详情草稿。")));
        return saving = RunMutationAsync(mutation);
    }
    public Task<ClientResult> BatchAsync(ImmutableArray<string> ids, BulkPersonalPatch patch) => StartBatchAsync(token => client.BatchAsync(ids, patch, token));
    public Task<ClientResult> ApplyBatchAsync(ImmutableArray<string> ids, string status, string favorite, string categoriesText, string tagsText, bool remove) => StartBatchAsync(async token =>
    {
        var labels = await Task.Run(() => (Categories: textRules.SplitLabels(categoriesText), Tags: textRules.SplitLabels(tagsText)), token);
        token.ThrowIfCancellationRequested();
        return await client.BatchAsync(ids, new()
        {
            Status = new(status != "all", status), Favorite = new(favorite != "keep", favorite == "yes"),
            AddCategories = remove ? [] : labels.Categories, RemoveCategories = remove ? labels.Categories : [],
            AddTags = remove ? [] : labels.Tags, RemoveTags = remove ? labels.Tags : []
        }, token);
    });
    public async Task ChangeAppearanceAsync(NativeTheme theme, string layout)
    {
        if (disposed) return;
        if (PreferenceState != PreferenceInitialization.Ready)
        {
            if (theme != preferences.Theme) pendingTheme = theme;
            if (layout != preferences.Layout) pendingLayout = layout;
        }
        if (layout != preferences.Layout) ClearSelection();
        preferences = preferences with { Theme = theme, Layout = layout };
        Changed(nameof(Theme)); Changed(nameof(Layout)); Changed(nameof(Preferences)); ThemeChanged?.Invoke();
        await QueuePreferencesSave();
    }
    public async Task SaveFilterAsync(string name)
    {
        if (!CanManageFilters) return;
        var next = preferences with { SavedFilters = preferences.Filters.Add(new(Guid.NewGuid().ToString("N"), name.Trim(), query)) };
        try { PreferenceValidation.Validate(next); preferences = next; Changed(nameof(SavedFilters)); await QueuePreferencesSave(); }
        catch (InvalidDataException e) { Report(new(ClientOutcome.ValidationFailure, e.Message)); }
    }
    public async Task RemoveFilterAsync(string id)
    { if (!CanManageFilters) return; preferences = preferences with { SavedFilters = [.. preferences.Filters.Where(f => f.Id != id)] }; Changed(nameof(SavedFilters)); await QueuePreferencesSave(); }
    private Task QueuePreferencesSave()
    {
        if (PreferenceState != PreferenceInitialization.Ready || disposed || !IsLoaded) return Task.CompletedTask;
        var captured = preferences;
        return preferenceSaves = PersistAfterAsync(preferenceSaves, captured);
    }
    private async Task PersistAfterAsync(Task previous, ClientPreferences captured)
    { await previous; await PersistAsync(captured); }
    private async Task PersistAsync(ClientPreferences captured)
    {
        try { await preferencesStore.SaveAsync(captured, lifetime.Token); }
        catch (OperationCanceledException) { }
        catch (Exception) { if (!disposed) Report(new(ClientOutcome.IoFailure, UiText.Get("界面偏好未能保存；书架数据不受影响。"))); }
    }
    public async ValueTask DisposeAsync()
    {
        if (disposed) return; disposed = true;
        searchCancellation?.Cancel(); detailsCancellation?.Cancel();
        if (saving is not null) await saving;
        if (sessionLibraryRefresh is not null) await sessionLibraryRefresh;
        displayedSessionLibrary = null;
        await preferenceSaves;
        lifetime.Cancel();
        if (draft is not null) draft.PropertyChanged -= DraftChanged;
        searchCancellation?.Dispose(); detailsCancellation?.Dispose(); lifetime.Dispose();
    }
}
