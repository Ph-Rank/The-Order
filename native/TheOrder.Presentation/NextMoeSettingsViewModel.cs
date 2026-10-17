using System.Collections.ObjectModel;
using TheOrder.Application;
using TheOrder.Domain;

namespace TheOrder.Presentation;

public sealed class SyncRowView : ObservableState
{
    public PersonalSyncRow Row { get; }
    public string Title => Row.Title;
    public string Values => "本地：" + Row.LocalValue + "　云端：" + Row.CloudValue;
    public string Explanation => (Row.Conflict ? "需要选择 · " : "") + Row.Explanation;
    private int choice;
    public int Choice { get => choice; set { var requested = (PersonalSyncChoice)value;
        if (!Enum.IsDefined(requested) || requested == PersonalSyncChoice.Import && Row.Import is null
            || requested == PersonalSyncChoice.Upload && Row.Upload is null && !Row.DeleteCloud) requested = PersonalSyncChoice.Skip;
        if (choice != (int)requested) { choice = (int)requested; Changed(); } } }
    public SyncRowView(PersonalSyncRow row, PersonalSyncChoice selected) { Row = row; choice = (int)selected; }
}
public sealed class NextMoeSettingsViewModel(NextMoeAuthentication auth, PersonalSyncCoordinator sync, IPersonalSyncLibrary library) : ObservableState, IDisposable, IAsyncDisposable
{
    public NextMoeAuthentication Access => auth;
    public NextMoeAuthStatus? Status => auth.CurrentStatus;
    // Called on the UI dispatcher after authentication changes, including failed background refreshes.
    public void RefreshAccessState() { if (disposed) return; if (Status is not null) CanRecover = false; Notify(); }
    public int AccessMode => (int)(Status?.Mode ?? NextMoeAccessMode.Anonymous);
    public string AccountName => Status?.Account?.Name ?? "尚未登录 NextMoe";
    public bool IsAccount => Status?.Mode == NextMoeAccessMode.Account;
    public string AccessMessage => CanRecover ? "登录资料无法读取，可明确重置本机登录后重新授权。" : Status?.NeedsLogin == true ? "登录需要恢复，请重新登录。" : "";
    public bool CanRecover { get; private set; }
    public string Message { get; private set; } = "";
    public string LastSync { get; private set; } = "尚未同步";
    public int TimeDirection { get; set; }
    public int StateDirection { get; set; }
    private int folderDirection;
    public int FolderDirection { get => folderDirection; set { if (folderDirection == value) return; folderDirection = value; Changed(); } }
    public bool ReadRatings { get; set; }
    public bool Automatic { get; set; }
    public bool ConfirmCloudRemoval { get; set; }
    public bool Busy { get; private set; }
    public bool CanOperate => !Busy && !paused;
    public bool HasPreview => Preview is not null;
    public double Progress { get; private set; }
    public string PreviewSummary => Preview is null ? "" : $"本地 {Preview.LocalCount} 条，云端 {Preview.CloudCount} 条；{Preview.Rows.Length} 个项目，{Preview.Rows.Count(x => x.Conflict)} 个需核对。";
    public bool HasPending { get; private set; }
    public PersonalSyncPreview? Preview { get; private set; }
    public ObservableCollection<SyncRowView> Rows { get; } = [];
    public ObservableCollection<CloudFolder> Folders { get; } = [];
    public ObservableCollection<SyncFolderMapping> Mappings { get; } = [];
    public Func<Task>? IdentityChanged { get; set; }
    private readonly CancellationTokenSource lifetime = new();
    private CancellationTokenSource? operation;
    private bool disposed;
    private bool paused;
    private Task pending = Task.CompletedTask;
    private PersonalSyncSettings Settings => new((PersonalSyncDirection)TimeDirection, (PersonalSyncDirection)StateDirection, (PersonalSyncDirection)FolderDirection, ReadRatings, Automatic);
    public async Task InitializeAsync()
    {
        try
        {
            var fence = await auth.AuthContext.CaptureAsync(lifetime.Token);
            using var stop = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token, fence.Changed);
            var status = await auth.StatusAsync(stop.Token);
            var current = status.Mode == NextMoeAccessMode.Account ? await sync.CurrentAsync(stop.Token) : null;
            auth.AuthContext.Accept(fence, () => { stop.Token.ThrowIfCancellationRequested();
            if (current is not null && current.AccountId != status.Account?.Id) throw new OperationCanceledException();
            CanRecover = false;
            if (IsAccount)
            {
                TimeDirection = (int)current!.Settings.Playtime; StateDirection = (int)current.Settings.State; FolderDirection = (int)current.Settings.Folders;
                ReadRatings = current.Settings.ReadRatings; Automatic = current.Settings.Automatic;
                LastSync = current.LastSuccess is null ? "尚未同步" : "最近成功同步：" + current.LastSuccess;
                HasPending = current.Pending.Length > 0 || current.FolderCreations?.Length > 0;
                Mappings.Clear(); foreach (var mapping in current.Folders) Mappings.Add(mapping);
                if (HasPending) Message = "有上次未确定结果的任务，请先恢复检查。不会重新创建任务或重复累计时长。";
            }
            else { TimeDirection = StateDirection = FolderDirection = 0; ReadRatings = Automatic = false; HasPending = false; LastSync = "尚未同步"; Mappings.Clear(); }
            return true; });
        }
        catch (OnlineException e) { CanRecover = e.Failure == OnlineFailure.CredentialStorageCorrupt; Message = e.Message; }
        catch (OperationCanceledException) { }
        Notify();
    }
    public Task SelectModeAsync(int mode) => Run(async token => { await auth.SelectAsync((NextMoeAccessMode)mode, token); await AfterIdentity(); });
    public Task LoginAsync(bool folderPermissions = false) => Run(async token =>
    { var previousAccount = Status?.Account?.Id; var draft = Settings;
        await auth.SignInAsync(folderPermissions, folderPermissions && FolderDirection is 2 or 3, token); await AfterIdentity();
        if (folderPermissions && previousAccount == Status?.Account?.Id)
        { TimeDirection = (int)draft.Playtime; StateDirection = (int)draft.State; FolderDirection = (int)draft.Folders; ReadRatings = draft.ReadRatings; Automatic = draft.Automatic; } });
    public Task LogoutAsync() => Run(async token => { await auth.SignOutAsync(token); Message = "已退出本机登录。"; await AfterIdentity(); });
    public Task RecoverLoginAsync() => Run(async token => { await auth.RecoverCorruptLoginAsync(token); Message = "已重置本机登录，可重新登录；书架、原 API Key 和同步记录保留。"; await AfterIdentity(); });
    public async Task PauseAutomaticAsync() { Cancel(); await pending; await Run(async token => { await sync.PauseAutomaticAsync(token); await InitializeAsync(); Message = "自动同步已暂停。"; }); }
    public Task UnmapFolderAsync(string id) => Run(async token => { await sync.UnmapFolderAsync(id, token); Preview = null; Rows.Clear(); await ReloadMappingsAsync(token); Message = "已取消本机收藏夹关联，云端收藏夹和书架作品保留。"; });
    private async Task ReloadMappingsAsync(CancellationToken token) { var state = await sync.CurrentAsync(token); Mappings.Clear(); foreach (var map in state.Folders) Mappings.Add(map); }
    private async Task AfterIdentity() { Preview = null; Rows.Clear(); Folders.Clear(); await InitializeAsync(); if (IdentityChanged is { } callback) await callback(); }
    public Task PreviewAsync() => Run(async token =>
    {
        Preview = null; Rows.Clear(); Message = "正在读取所选数据并比较；此步骤不会写入云端或本地书架。"; Notify();
        var preview = await sync.PreviewAsync(Settings, token); Preview = preview;
        var defaults = PersonalSyncCoordinator.DefaultChoices(preview);
        foreach (var row in preview.Rows) Rows.Add(new(row, defaults[row.Id]));
        Folders.Clear(); foreach (var folder in preview.CloudFolders) Folders.Add(folder);
        ConfirmCloudRemoval = false; Message = "请逐项核对处理方式。冲突项默认跳过，确认后才同步。";
    });
    public Task ApplyAsync() => Run(async token =>
    {
        if (Preview is null) return;
        var choices = Rows.ToDictionary(x => x.Row.Id, x => (PersonalSyncChoice)x.Choice, StringComparer.Ordinal);
        var progress = new Progress<(int Completed, int Total)>(value => { Progress = value.Total == 0 ? 0 : 100.0 * value.Completed / value.Total; Changed(nameof(Progress)); });
        var result = await sync.ApplyAsync(Preview, choices, ConfirmCloudRemoval, progress, token);
        Message = result.Message; Preview = null; Rows.Clear(); await InitializeAsync();
    });
    public Task RetryAsync() => Run(async token => { var result = await sync.RetryPendingAsync(token); Message = result.Message; await InitializeAsync(); });
    public Task ReviewPendingAsync() => Run(async token => { var result = await sync.ReviewPendingAsync(token); Message = result.Message; await InitializeAsync(); });
    public async Task<string[]> MappedMembersAsync(string folderId) => (await sync.CurrentAsync(lifetime.Token)).Folders.SingleOrDefault(x => x.FolderId == folderId)?.Members ?? [];
    public async Task<ImportedPersonalFacts?> ImportedFactsAsync(string workId, CancellationToken cancellation)
    {
        if (!IsAccount) return null;
        var fence = await auth.AuthContext.CaptureAsync(cancellation);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellation, fence.Changed);
        var state = await sync.CurrentAsync(stop.Token);
        return auth.AuthContext.Accept(fence, () => { stop.Token.ThrowIfCancellationRequested(); return state.Imported.SingleOrDefault(x => x.WorkId == workId); });
    }
    public Task MapFolderAsync(CloudFolder folder, string[] workIds) => Run(async token =>
    { await sync.MapFolderAsync(folder, workIds, token); Preview = null; Rows.Clear(); await ReloadMappingsAsync(token); Message = "已保存独立收藏列表映射；请重新预览，尚未上传成员。"; });
    public Task CreateFolderAsync(string name, string[] workIds) => Run(async token =>
    { var folder = await sync.CreateMappedFolderAsync(name, workIds, token); await ReloadMappingsAsync(token); Message = "已创建私密收藏夹「" + folder.Name + "」；成员将在预览确认后上传。"; Preview = null; Rows.Clear(); });
    public Task<LibraryEntry[]> LocalWorksAsync(CancellationToken cancellation) => ReadWorks(cancellation);
    private async Task<LibraryEntry[]> ReadWorks(CancellationToken cancellation) => (await library.SnapshotAsync(cancellation)).Where(MetadataMixing.HasNextMoeIdentity)
        .GroupBy(x => x.Metadata!.Id).Where(x => x.Count() == 1).Select(x => x.Single()).ToArray();
    public async Task AutomaticAsync()
    {
        if (Busy || disposed || !IsAccount || HasPending) return;
        await Run(async token => { var result = await sync.AutomaticAsync(token); if (result is not null) { Message = result.Message; await InitializeAsync(); } }, quiet: true);
    }
    private Task Run(Func<CancellationToken, Task> work, bool quiet = false)
    {
        if (Busy || disposed || paused) return Task.CompletedTask;
        return pending = RunCore(work, quiet);
    }
    private async Task RunCore(Func<CancellationToken, Task> work, bool quiet)
    {
        if (Busy || disposed) return;
        Busy = true; if (!quiet) Progress = 0;
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); operation = stop; Notify();
        try { await work(stop.Token); }
        catch (OperationCanceledException) { if (!disposed) Message = "操作已取消。已稳定保存的结果保留；不确定的云端请求会在恢复检查中核对。"; }
        catch (OnlineException e) { Message = e.Message; }
        catch (Exception) { Message = "操作未完成，原记录保留。请重新预览或恢复检查，不会自动换账号或覆盖数据。"; }
        finally { if (ReferenceEquals(operation, stop)) operation = null; Busy = false; if (!disposed) Notify(); }
    }
    public void Cancel() => operation?.Cancel();
    public void PauseCommands(bool value) { paused = value; if (value) Cancel(); Changed(nameof(CanOperate)); }
    public async Task DrainAsync() { Cancel(); await pending; }
    private void Notify() { foreach (var name in new[] { nameof(Status), nameof(AccessMode), nameof(AccountName), nameof(IsAccount), nameof(AccessMessage), nameof(Message),
        nameof(LastSync), nameof(TimeDirection), nameof(StateDirection), nameof(FolderDirection), nameof(ReadRatings), nameof(Automatic), nameof(ConfirmCloudRemoval),
        nameof(Busy), nameof(CanOperate), nameof(HasPreview), nameof(PreviewSummary), nameof(HasPending), nameof(Progress), nameof(CanRecover) }) Changed(name); }
    public void Dispose() { if (disposed) return; disposed = true; lifetime.Cancel(); operation?.Cancel(); lifetime.Dispose(); }
    public async ValueTask DisposeAsync() { Dispose(); await pending.ConfigureAwait(false); }
}
