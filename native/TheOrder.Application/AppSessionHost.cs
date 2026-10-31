using TheOrder.Application;
using TheOrder.Domain;

namespace TheOrder.Application;

// Commands used by presentation; ownership remains at application composition, never a page.
public interface ILaunchCommands
{
    LaunchStatus Status { get; }
    Task<LaunchPreparation> PrepareAsync(string id, string? installation = null, CancellationToken cancellation = default);
    Task ConfirmPositionAsync(LaunchSnapshot snapshot, CancellationToken cancellation = default);
    Task<LaunchStatus> LaunchAsync(string id, string? installation = null, CancellationToken cancellation = default);
}

public sealed class DirectLaunchCommands(GameLaunchService service) : ILaunchCommands
{
    public LaunchStatus Status => service.Status;
    public Task<LaunchPreparation> PrepareAsync(string id, string? installation = null, CancellationToken cancellation = default) => service.PrepareAsync(id, installation, cancellation);
    public Task ConfirmPositionAsync(LaunchSnapshot snapshot, CancellationToken cancellation = default) => service.ConfirmPositionAsync(snapshot, cancellation);
    public Task<LaunchStatus> LaunchAsync(string id, string? installation = null, CancellationToken cancellation = default) => service.LaunchAsync(id, installation, cancellation);
}

public sealed class AppSessionHost(Func<Task<SessionEngine?>> create, Func<LibrarySnapshot?> readLibrary,
    Func<string, CancellationToken, Task>? prepareCompanion = null) : ILaunchCommands, IAsyncDisposable
{
    private readonly object gate = new();
    private Task? initialization, disposal;
    private Task<SessionEngine?>? creation;
    private Task<bool>? exit;
    private SessionEngine? engine;
    private volatile bool ready, closing, disposing;
    private string? error;
    public bool Ready => ready;
    public bool Closing => closing;
    public bool CanMutate => ready && !closing && !disposing && engine is not null;
    public string? Error => error ?? engine?.Snapshot.Error;
    public SessionEngineSnapshot? Session => engine?.Snapshot;
    public LibrarySnapshot? Library => ready ? readLibrary() : null;
    public LaunchStatus Status => engine?.LaunchStatus ?? new(LaunchOutcome.Ready, ProcessTrackingState.None, null, null, null, ready ? UiText.Get("当前资料不可启动游戏。") : UiText.Get("正在恢复上次会话…"));
    public Task InitializeAsync()
    {
        lock (gate)
        {
            if (disposing) return Task.FromException(new ObjectDisposedException(nameof(AppSessionHost)));
            // Creation belongs to the host lifetime; only recovery attempts may be replaced.
            // Concurrent initialization/exit callers share the same in-flight attempt.
            if (initialization is null || initialization.IsFaulted || initialization.IsCanceled)
                initialization = InitializeCoreAsync();
            return initialization;
        }
    }
    private async Task<SessionEngine?> CreateOnceAsync() => await create().ConfigureAwait(false);
    private async Task InitializeCoreAsync()
    {
        engine = await (creation ??= CreateOnceAsync()).ConfigureAwait(false);
        if (engine is not null) await engine.InitializeAsync().ConfigureAwait(false);
        lock (gate) { if (!disposing) { ready = true; error = null; } }
    }
    public async Task<LaunchPreparation> PrepareAsync(string id, string? installation = null, CancellationToken cancellation = default)
    {
        await InitializeAsync().ConfigureAwait(false);
        if (!CanMutate) return new(LaunchOutcome.Closed, null, UiText.Get("正在关闭或当前书架不可写。"));
        return await engine!.PrepareAsync(id, installation, cancellation).ConfigureAwait(false);
    }
    public async Task ConfirmPositionAsync(LaunchSnapshot snapshot, CancellationToken cancellation = default)
    {
        await InitializeAsync().ConfigureAwait(false);
        if (!CanMutate) throw new InvalidOperationException(UiText.Get("当前不能修改启动位置。"));
        await engine!.ConfirmPositionAsync(snapshot, cancellation).ConfigureAwait(false);
    }
    public async Task<LaunchStatus> LaunchAsync(string id, string? installation = null, CancellationToken cancellation = default)
    {
        await InitializeAsync().ConfigureAwait(false);
        if (CanMutate && prepareCompanion is not null)
        {
            // Availability is resolved before OS creation; no helper is loaded for an empty/failed query.
            // Exit still seals admission while the optional network request is pending.
            try { await prepareCompanion(id, cancellation).ConfigureAwait(false); }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { return Status with { Outcome = LaunchOutcome.Cancelled }; }
            catch (Exception error) when (error is not OutOfMemoryException) { }
        }
        Task<LaunchStatus> returnTask;
        lock (gate)
        {
            if (!CanMutate) return Status with { Outcome = LaunchOutcome.Closed };
            // Engine's launch gate is entered before exit may seal admission.
            returnTask = engine!.LaunchAsync(id, installation, cancellation);
        }
        return await returnTask.ConfigureAwait(false);
    }
    public Task PauseAsync() { lock (gate) return CanMutate ? engine!.PauseAsync() : Task.CompletedTask; }
    public Task ResumeAsync() { lock (gate) return CanMutate ? engine!.ResumeAsync() : Task.CompletedTask; }
    public Task CheckpointAsync() => engine?.CheckpointAsync() ?? Task.CompletedTask;
    public Task<bool> PrepareExitAsync(SessionReason reason = SessionReason.ManagerExit)
    {
        lock (gate)
        {
            if (disposing) return Task.FromResult(false);
            if (exit is { IsCompleted: false } || closing && exit is { IsCompletedSuccessfully: true, Result: true }) return exit;
            closing = true; error = null;
            return exit = ExitCoreAsync(reason);
        }
    }
    private async Task<bool> ExitCoreAsync(SessionReason reason)
    {
        try
        {
            await InitializeAsync().ConfigureAwait(false);
            if (engine is null || await engine.CheckpointForExitAsync(reason).ConfigureAwait(false)) return true;
            error = UiText.Get("游玩记录保存失败，退出已取消；游戏继续运行，请重试退出。");
        }
        catch (Exception) { if (engine is null) return true; error = UiText.Get("必要的记录尚未保存，退出已取消，请重试。"); }
        closing = false; return false;
    }
    // OS deadline is an observation limit, not cancellation of an already-started atomic write.
    public async Task<bool> PrepareSystemEndAsync(TimeSpan budget)
    {
        var work = PrepareExitAsync(SessionReason.SystemShutdown);
        try { return await work.WaitAsync(budget).ConfigureAwait(false); }
        catch (TimeoutException) { error = UiText.Get("系统结束会话的保存预算已用尽；仅已落盘的记录可保证保留。"); return false; }
    }
    public async Task CancelSystemEndAsync()
    {
        Task<bool>? work; lock (gate) work = exit;
        if (work is not null) await work.ConfigureAwait(false);
        if (engine is not null) await engine.CancelExitPreparationAsync().ConfigureAwait(false);
        lock (gate) { closing = false; exit = null; error = null; }
    }
    public ValueTask DisposeAsync() { lock (gate) { disposing = true; return new(disposal ??= DisposeCoreAsync()); } }
    private async Task DisposeCoreAsync()
    {
        closing = true;
        if (initialization is not null) try { await initialization.ConfigureAwait(false); } catch { }
        if (exit is not null) await exit.ConfigureAwait(false);
        if (engine is not null) await engine.DisposeAsync().ConfigureAwait(false);
        ready = false;
    }
}
