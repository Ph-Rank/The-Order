using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using TheOrder.Application;

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
public sealed class NextMoeSettingsViewModel(NextMoeAuthentication auth, PersonalSyncCoordinator sync, bool automaticSyncEnabled = true) : ObservableState, IDisposable, IAsyncDisposable
{
    public NextMoeAuthentication Access => auth;
    public NextMoeAuthStatus? Status => auth.CurrentStatus;
    // Called on the UI dispatcher after authentication changes, including failed background refreshes.
    public void RefreshAccessState() { if (disposed) return; if (Status is not null) CanRecover = false; Notify(); }
    public int AccessMode => (int)(Status?.Mode ?? NextMoeAccessMode.Anonymous);
    public string AccountName => Status?.Account?.Name ?? "尚未登录 NextMoe";
    public bool IsAccount => Status?.Mode == NextMoeAccessMode.Account;
    public bool HasSavedAccount => Status?.Account is not null;
    public string LoginAction => Status is { Mode: NextMoeAccessMode.Account } status
        ? status.NeedsLogin || !status.CanWriteFolders ? "重新授权 NextMoe" : "重新登录 NextMoe"
        : "登录 NextMoe";
    public string? Error { get; private set; }
    public ImmutableArray<NextMoeAssociationIssue> AssociationIssues { get; private set; } = [];

    public Task<NextMoeMergeResult> RepairCollectionMergeAsync(string entryId, string oldId, string newId, OnlineAuthSnapshot fence,
        CancellationToken cancellation) => sync.RepairCollectionMergeAsync(entryId, oldId, newId, fence, cancellation);
    public Task<ClientResult> ConfirmCatalogMergeAsync(CollectionBindingPreview preview, string oldId, OnlineAuthSnapshot fence,
        CancellationToken cancellation) => sync.ConfirmCatalogMergeAsync(preview, oldId, fence, cancellation);
    public bool CanRecover { get; private set; }
    public string Message { get; private set; } = "";
    public string LastSync { get; private set; } = "尚未同步";
    private int timeDirection, stateDirection, folderDirection;
    private bool readRatings;
    public int TimeDirection { get => timeDirection; set => SetSyncOption(ref timeDirection, value); }
    public int StateDirection { get => stateDirection; set => SetSyncOption(ref stateDirection, value); }
    public int FolderDirection { get => folderDirection; set => SetSyncOption(ref folderDirection, value); }
    public bool ReadRatings { get => readRatings; set => SetSyncOption(ref readRatings, value); }
    public bool Automatic => automaticSyncEnabled && Status is { Mode: NextMoeAccessMode.Account, NeedsLogin: false, CanWriteFolders: true };
    public bool CanCancel { get; private set; }
    public bool Busy { get; private set; }
    public bool CanOperate => !Busy && !paused;
    public bool CanImportRatings => CanOperate && Status is { Mode: NextMoeAccessMode.Account, Account: { }, NeedsLogin: false };
    public bool HasPreview => Preview is not null;
    public string PreviewSummary => Preview is null ? "" : $"本地 {Preview.LocalCount} 条，云端 {Preview.CloudCount} 条；{Preview.Rows.Length} 个项目，{Preview.Rows.Count(x => x.Conflict)} 个需核对。";
    public bool HasPending { get; private set; }
    public PersonalSyncPreview? Preview { get; private set; }
    public ObservableCollection<SyncRowView> Rows { get; } = [];
    public Func<Task>? IdentityChanged { get; set; }
    private readonly CancellationTokenSource lifetime = new();
    private CancellationTokenSource? operation;
    private bool disposed;
    private bool paused;
    private Task pending = Task.CompletedTask;
    private PersonalSyncSettings Settings => new((PersonalSyncDirection)TimeDirection, (PersonalSyncDirection)StateDirection,
        AllowedFolderDirection((PersonalSyncDirection)FolderDirection), ReadRatings, Automatic);

    private PersonalSyncDirection AllowedFolderDirection(PersonalSyncDirection requested)
    {
        if (Status?.CanWriteFolders == true) return requested;
        return Status?.CanReadFolders == true && requested is PersonalSyncDirection.Import or PersonalSyncDirection.Bidirectional
            ? PersonalSyncDirection.Import : PersonalSyncDirection.Off;
    }

    private void SetSyncOption<T>(ref T field, T value, [CallerMemberName] string? property = null)
    {
        if (!Set(ref field, value, property)) return;
        if (Preview is null) return;
        Preview = null;
        Rows.Clear();
        Changed(nameof(HasPreview));
        Changed(nameof(PreviewSummary));
    }
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
                TimeDirection = (int)current!.Settings.Playtime;
                StateDirection = (int)current.Settings.State;
                FolderDirection = (int)AllowedFolderDirection(current.Settings.Folders);
                ReadRatings = current.Settings.ReadRatings;
                LastSync = current.LastSuccess is null ? "尚未同步" : "最近成功同步：" + current.LastSuccess;
                HasPending = current.Pending.Length > 0 || current.FolderCreations?.Length > 0;
                if (HasPending) Message = "有上次未确定结果的任务，请先恢复检查。不会重新创建任务或重复累计时长。";
            }
            else { TimeDirection = StateDirection = FolderDirection = 0; ReadRatings = false; HasPending = false; LastSync = "尚未同步"; }
            return true; });
        }
        catch (OnlineException e) { CanRecover = e.Failure == OnlineFailure.CredentialStorageCorrupt; Error = Message = e.Message; }
        catch (OperationCanceledException) { }
        Notify();
    }
    public Task SelectModeAsync(int mode) => Run(async token => { await auth.SelectAsync((NextMoeAccessMode)mode, token); await AfterIdentity(); });
    public Task LoginAsync() => Run(async token =>
    {
        await auth.SignInAsync(token);
        await AfterIdentity();
        if (!HasPending && Status is { NeedsLogin: false })
            Message = "已连接 NextMoe。";
    });
    public Task LogoutAsync() => Run(async token => { await auth.SignOutAsync(token); Message = "已退出本机登录。"; await AfterIdentity(); });
    public Task RecoverLoginAsync() => Run(async token => { await auth.RecoverCorruptLoginAsync(token); Message = "已重置本机登录，可重新登录；书架、原 API Key 和同步记录保留。"; await AfterIdentity(); });
    private async Task AfterIdentity() { Preview = null; Rows.Clear(); await InitializeAsync(); if (IdentityChanged is { } callback) await callback(); }
    public Task PreviewAsync() => Run(async token =>
    {
        Preview = null; Rows.Clear(); Message = "正在读取所选数据并比较；此步骤不会写入云端或本地书架。"; Notify();
        var preview = await sync.PreviewAsync(Settings, token); Preview = preview;
        var defaults = PersonalSyncCoordinator.DefaultChoices(preview);
        foreach (var row in preview.Rows) Rows.Add(new(row, defaults[row.Id]));
        Message = "已完成比较。";
    });
    public Task ApplyAsync() => Run(async token =>
    {
        if (Preview is null) return;
        var choices = Rows.ToDictionary(x => x.Row.Id, x => (PersonalSyncChoice)x.Choice, StringComparer.Ordinal);
        var result = await sync.ApplyAsync(Preview, choices, false, null, token);
        Message = result.Message; Preview = null; Rows.Clear(); await InitializeAsync();
    });
    public Task RecoverPendingAsync() => Run(async token =>
    {
        try { await sync.RetryPendingAsync(token); }
        catch (OnlineException error) when (error.Failure == OnlineFailure.Conflict)
        { await sync.ReviewPendingAsync(token); }
        await InitializeAsync();
    });
    public async Task<ImportedPersonalFacts?> ImportedFactsAsync(string workId, CancellationToken cancellation)
    {
        if (!IsAccount) return null;
        var fence = await auth.AuthContext.CaptureAsync(cancellation);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellation, fence.Changed);
        var state = await sync.CurrentAsync(stop.Token);
        return auth.AuthContext.Accept(fence, () => { stop.Token.ThrowIfCancellationRequested(); return state.Imported.SingleOrDefault(x => x.WorkId == workId); });
    }
    public async Task<int?> ImportRatingsAsync()
    {
        if (!CanImportRatings) return null;
        int? count = null;
        await Run(async token =>
        {
            count = await sync.ImportRatingsAsync(token);
            Changed(nameof(LastSync));
        });
        return count;
    }

    public Task AutomaticAsync(bool force = false)
    {
        if (!Automatic || HasPending) return Task.CompletedTask;
        return Run(async token =>
        {
            PersonalSyncResult? result;
            try { result = await sync.AutomaticAsync(token, force); }
            catch (OnlineException error) when (error.Failure == OnlineFailure.Conflict)
            {
                await InitializeAsync();
                Error = HasPending ? "有同步任务需要恢复检查。" : "本轮同步校验未通过，稍后将重新核对。";
                return;
            }
            catch
            {
                if (!token.IsCancellationRequested) await InitializeAsync();
                throw;
            }
            if (result is null) return;
            AssociationIssues = result.AssociationIssues;
            Error = result.Failed == 0 ? result.AssociationWarning
                : $"有 {result.Failed} 项 NextMoe 记录同步失败，稍后将自动重试。"
                    + (result.AssociationWarning is { } warning ? " " + warning : "");
            Message = result.Message;
            await InitializeAsync();
        }, quiet: true);
    }
    private Task Run(Func<CancellationToken, Task> work, bool quiet = false)
    {
        if (Busy || disposed || paused) return Task.CompletedTask;
        return pending = RunCore(work, quiet);
    }
    private async Task RunCore(Func<CancellationToken, Task> work, bool quiet)
    {
        if (Busy || disposed) return;
        Busy = true;
        CanCancel = !quiet;
        if (!quiet) { Error = null; AssociationIssues = []; }
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        operation = stop;
        NotifyOperation();
        try { await work(stop.Token); }
        catch (OperationCanceledException) { if (!disposed) Message = "操作已取消。已稳定保存的结果保留；不确定的云端请求会在恢复检查中核对。"; }
        catch (OnlineException e) { Error = Message = e.Message; }
        catch (Exception) { Error = Message = "NextMoe 操作未完成，请稍后重试。"; }
        finally
        {
            if (ReferenceEquals(operation, stop)) operation = null;
            Busy = CanCancel = false;
            if (!disposed) { if (quiet) NotifyOperation(); else Notify(); }
        }
    }
    public void Cancel() => operation?.Cancel();
    public void PauseCommands(bool value) { paused = value; if (value) Cancel(); Notify(); }
    public async Task DrainAsync() { Cancel(); await pending; }
    private void NotifyOperation()
    {
        Changed(nameof(Busy));
        Changed(nameof(CanOperate));
        Changed(nameof(CanImportRatings));
        Changed(nameof(CanCancel));
        Changed(nameof(Error));
    }
    private void Notify()
    {
        foreach (var name in new[] { nameof(Status), nameof(AccessMode), nameof(AccountName), nameof(IsAccount), nameof(Message),
            nameof(LastSync), nameof(TimeDirection), nameof(StateDirection), nameof(FolderDirection), nameof(ReadRatings), nameof(Automatic),
            nameof(HasPreview), nameof(PreviewSummary), nameof(HasPending), nameof(CanRecover), nameof(HasSavedAccount), nameof(LoginAction) }) Changed(name);
        NotifyOperation();
    }
    public void Dispose() { if (disposed) return; disposed = true; lifetime.Cancel(); operation?.Cancel(); lifetime.Dispose(); }
    public async ValueTask DisposeAsync() { Dispose(); await pending.ConfigureAwait(false); }
}
