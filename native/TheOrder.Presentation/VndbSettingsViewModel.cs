using TheOrder.Application;

namespace TheOrder.Presentation;

public sealed class VndbSettingsViewModel(VndbAuthorization authorization) : ObservableState
{
    public VndbAccount? ConnectedAccount => authorization.ConnectedAccount;
    public bool Connected => authorization.Account is not null;
    public bool Busy { get; private set; }
    public bool CanOperate => !Busy;
    public bool CanTest => CanOperate && Connected;
    public string Status => authorization.NeedsReauthorization ? "授权已失效，请更新令牌。"
        : authorization.Account is { } account ? "已连接 · " + account.Username : "未连接";
    public string Message { get; private set; } = "";
    private CancellationTokenSource? operation;
    private Task pending = Task.CompletedTask;

    public Task InitializeAsync() => Run(authorization.InitializeAsync, "", "正在读取本机授权…");
    public Task SaveAsync(string token) => Run(cancellation => authorization.SaveAsync(token, cancellation),
        "已保存授权。可从作品详情同步游玩状态与评分。", "正在验证授权…");
    public Task TestAsync() => Run(authorization.TestAsync, "授权有效。", "正在验证授权…");
    public Task ClearAsync() => Run(authorization.ClearAsync, "已断开 VNDB。", "正在移除授权…");
    public Task WriteAsync(CollectionUpdate update, VndbAccount account, CancellationToken cancellation)
        => authorization.WriteAsync(update, account, cancellation);
    public void Cancel() => operation?.Cancel();
    public async Task DrainAsync() { Cancel(); await pending; }

    public void RefreshState()
    {
        foreach (var property in new[] { nameof(Connected), nameof(Status), nameof(Message), nameof(Busy), nameof(CanOperate), nameof(CanTest) })
            Changed(property);
    }

    private Task Run(Func<CancellationToken, Task> action, string success, string progress)
        => CanOperate ? pending = RunCoreAsync(action, success, progress) : Task.CompletedTask;

    private async Task RunCoreAsync(Func<CancellationToken, Task> action, string success, string progress)
    {
        using var cancellation = new CancellationTokenSource();
        operation = cancellation;
        Busy = true;
        Message = progress;
        RefreshState();
        try { await action(cancellation.Token); Message = success; }
        catch (OperationCanceledException) { Message = "已取消操作。"; }
        catch (CollectionSyncException error) { Message = error.Message; }
        finally { operation = null; Busy = false; RefreshState(); }
    }
}
