using TheOrder.Application;

namespace TheOrder.Presentation;

public sealed class BangumiSettingsViewModel(BangumiAuthorization authorization) : ObservableState, IDisposable
{
    private CancellationTokenSource? operation;
    private Task pending = Task.CompletedTask;
    private bool disposed;
    public BangumiAccessContext Access => authorization.Access;
    public bool Busy { get; private set; }
    public bool CanOperate => !disposed && !Busy;
    public bool HasAuthorization => authorization.HasSavedToken;
    public bool CanTest => CanOperate && HasAuthorization;
    public bool QuickLoginAvailable => authorization.QuickLoginAvailable;
    public bool CanQuickLogin => CanOperate && QuickLoginAvailable;
    public string ConnectionAction => HasAuthorization ? "重新连接 Bangumi" : "连接 Bangumi";
    public string QuickLoginNotice => QuickLoginAvailable ? "" : "快捷登录尚未开通，可先使用手动令牌授权。";
    private bool AuthorizationExpired => Access.Rejected || authorization.RequiresLogin;
    private bool AwaitingRenewal => AuthorizationExpired && authorization.UsesQuickLogin
        && authorization.QuickLoginAvailable && !authorization.ReauthorizationRequired;
    public bool NeedsReauthorization => AuthorizationExpired && !AwaitingRenewal;
    public string Status => AwaitingRenewal ? "等待自动续期 · 暂时使用匿名访问"
        : NeedsReauthorization ? "授权已失效，请重新连接 Bangumi。" : !HasAuthorization ? "未连接 · 使用匿名访问"
        : authorization.Account is { } account ? "已连接 · " + account.Name + "（@" + account.Username + "）" : "已保存授权";
    public string Message { get; private set; } = "";
    public Task InitializeAsync() => Run(token => authorization.InitializeAsync(token), "", "正在读取本机授权…");
    public Task SaveAsync(string token) => Run(async cancellation => { await authorization.SaveAsync(token, cancellation); },
        "授权已保存，返回作品并刷新关联即可查看。", "正在验证授权…");
    public Task SignInAsync() => Run(authorization.SignInAsync, "快捷登录成功，返回作品并刷新关联即可查看。", "请在系统浏览器中登录并同意授权…");
    public Task RefreshIfDueAsync() => CanOperate && QuickLoginAvailable && authorization.NeedsRefresh
        ? Run(authorization.RefreshAsync, "", "正在续期番组计划授权…") : Task.CompletedTask;
    public Task TestAsync() => Run(async token => { await authorization.TestAsync(token); }, "授权有效。", "正在验证授权…");
    public Task ClearAsync() => Run(token => authorization.ClearAsync(token), "已移除本机授权，恢复匿名访问。", "正在移除授权…");
    public void RefreshState() => Notify();
    public async Task DrainAsync() { Cancel(); await pending; }
    private Task Run(Func<CancellationToken, Task> action, string success, string progress)
        => CanOperate ? pending = RunCore(action, success, progress) : Task.CompletedTask;
    private async Task RunCore(Func<CancellationToken, Task> action, string success, string progress)
    {
        if (!CanOperate) return;
        using var request = new CancellationTokenSource(); operation = request;
        Busy = true; Message = progress; Notify();
        try { await action(request.Token); if (!disposed) Message = success; }
        catch (OperationCanceledException) { if (!disposed) Message = "已取消操作。"; }
        catch (Exception error) { if (!disposed) Message = error is BangumiAuthorizationException ? error.Message : "番组计划授权操作未完成，请重试。"; }
        finally { if (ReferenceEquals(operation, request)) operation = null; Busy = false; if (!disposed) Notify(); }
    }
    public void Cancel() => operation?.Cancel();
    public void Dispose() { if (disposed) return; disposed = true; operation?.Cancel(); }
    private void Notify()
    { foreach (var name in new[] { nameof(Busy), nameof(CanOperate), nameof(HasAuthorization), nameof(CanTest), nameof(CanQuickLogin), nameof(ConnectionAction), nameof(Status), nameof(NeedsReauthorization), nameof(Message) }) Changed(name); }
}
