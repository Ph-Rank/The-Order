using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using TheOrder.Application.Playground;

namespace TheOrder.WinUI.Views;

public sealed partial class PlaygroundView
{
    private readonly IChatGptAccounts? chatGptAccounts;
    private readonly TextBlock chatGptUsage = new() { Text = "使用 ChatGPT 额度", FontSize = 12, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock chatGptAccountLabel = new() { FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly HyperlinkButton chatGptManage = new() { Content = "管理 ChatGPT 用量", FontSize = 12, Padding = new Thickness(0) };
    private readonly StackPanel chatGptBadge = new() { Spacing = 4, Visibility = Visibility.Collapsed };
    private string? chatGptLabel;
    private void BuildChatGptUsage()
    {
        chatGptBadge.Children.Add(chatGptUsage); chatGptBadge.Children.Add(chatGptAccountLabel); chatGptBadge.Children.Add(chatGptManage);
        AutomationProperties.SetAutomationId(chatGptBadge, "PlaygroundChatGptUsage");
        AutomationProperties.SetAutomationId(chatGptManage, "PlaygroundChatGptManageUsage");
        chatGptManage.Click += async (_, _) => await Windows.System.Launcher.LaunchUriAsync(new Uri(ChatGptSignInPolicy.Usage));
        settings.Children.Add(chatGptBadge);
    }
    private async Task<bool> HasConversationCredential(PlaygroundConversationOptions options)
    {
        if (options.Provider != PlaygroundProviderKind.ChatGPT)
            return await service.HasCredentialAsync(options.Provider, options.Endpoint, lifetime.Token);
        if (chatGptAccounts is null) return false;
        var accounts = await chatGptAccounts.ReadAccountsAsync(lifetime.Token);
        var selectedAccount = accounts.Accounts.FirstOrDefault(a => a.ClientId == options.ChatGptAccountId);
        if (Draft.ChatGptAccountId == options.ChatGptAccountId) chatGptLabel = selectedAccount?.Label;
        return selectedAccount?.PlanEnabled == true;
    }
    private sealed class ChatGptConnectionEditor : IDisposable
    {
        private readonly PlaygroundView owner;
        private readonly IChatGptAccounts? accounts;
        private readonly SelectedFirstComboBox picker = new() { Header = "ChatGPT 账号", HorizontalAlignment = HorizontalAlignment.Stretch, DisplayMemberPath = "Label" };
        private readonly Button connect = new() { Content = "Continue with ChatGPT", HorizontalAlignment = HorizontalAlignment.Left };
        private readonly Button add = new() { Content = "添加账号" };
        private readonly Button grant = new() { Content = "授权使用 ChatGPT 额度" };
        private readonly Button signOut = new() { Content = "退出此账号" };
        private readonly Button cancel = new() { Content = "取消等待", Visibility = Visibility.Collapsed };
        private readonly TextBlock status = Label("", 13);
        private readonly TagFlowPanel actions;
        private ChatGptAccountSnapshot snapshot = new(null, []);
        private CancellationTokenSource? operation;
        private bool open = true, syncing, readable;
        public StackPanel Panel { get; } = new() { Spacing = 12 };
        public string? SelectedId { get; private set; }
        public bool Busy => operation is not null;
        public Action<bool>? BusyChanged { get; set; }
        public ChatGptAccountInfo? PendingWelcome => snapshot.Accounts.FirstOrDefault(a => a.ClientId == SelectedId && a.PlanEnabled && !a.Welcomed);
        public ChatGptConnectionEditor(PlaygroundView owner, string? selected)
        {
            this.owner = owner; accounts = owner.chatGptAccounts; SelectedId = selected;
            actions = new TagFlowPanel { HorizontalSpacing = 8, VerticalSpacing = 8, Children = { add, grant, signOut, cancel } };
            var usage = new HyperlinkButton { Content = "管理 ChatGPT 用量", Padding = new Thickness(0) };
            usage.Click += async (_, _) => await Windows.System.Launcher.LaunchUriAsync(new Uri(ChatGptSignInPolicy.Usage));
            Panel.Children.Add(picker); Panel.Children.Add(connect); Panel.Children.Add(actions); Panel.Children.Add(status); Panel.Children.Add(usage);
            AutomationProperties.SetAutomationId(picker, "PlaygroundChatGptAccounts");
            AutomationProperties.SetAutomationId(connect, "PlaygroundChatGptSignIn");
            AutomationProperties.SetAutomationId(add, "PlaygroundChatGptAddAccount");
            AutomationProperties.SetAutomationId(signOut, "PlaygroundChatGptSignOut");
            AutomationProperties.SetLiveSetting(status, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite);
            picker.OptionChanged += async (_, _) =>
            {
                if (syncing || Busy || picker.SelectedOption is not ChatGptAccountInfo choice) return;
                await Work(async token => { owner.service.Stop(); await accounts!.SelectAsync(choice.ClientId, token);
                    SelectedId = choice.ClientId; await Reload(); status.Text = choice.PlanEnabled ? "已选择 · 使用 ChatGPT 额度" : "请登录并授权使用额度。"; });
            };
            connect.Click += async (_, _) => await Login(SelectedId, false);
            add.Click += async (_, _) => await Login(null, false);
            grant.Click += async (_, _) => await Login(SelectedId, true);
            signOut.Click += async (_, _) => await Work(async token =>
            {
                owner.service.Stop(); bool revoked = await accounts!.SignOutAsync(SelectedId!, token); await Reload();
                status.Text = revoked ? "已退出此 ChatGPT 账号。" : "已在本机退出；远程撤销未能确认，可在 ChatGPT 设置中断开此应用。";
            });
            cancel.Click += (_, _) => operation?.Cancel();
            Update();
        }
        public async Task Reload()
        {
            if (!open) return;
            try
            {
                if (accounts is null)
                {
                    status.Text = "此运行环境暂不支持浏览器登录。";
                    return;
                }
                var next = await accounts.ReadAccountsAsync(operation?.Token ?? owner.lifetime.Token);
                if (!open) return;
                if (!readable) status.Text = "";
                snapshot = next;
                readable = true;
                SelectedId = snapshot.Accounts.Any(a => a.ClientId == SelectedId) ? SelectedId : snapshot.SelectedClientId;
                syncing = true;
                try
                {
                    picker.OptionsSource = snapshot.Accounts;
                    picker.OptionIndex = Array.FindIndex(snapshot.Accounts.ToArray(), a => a.ClientId == SelectedId);
                }
                finally { syncing = false; }
            }
            catch (OperationCanceledException) when (operation is null) { }
            catch when (operation is null)
            {
                readable = false;
                status.Text = "暂时无法读取本机保存的 ChatGPT 连接，请重新打开连接设置后重试。";
            }
            finally { if (open) Update(); }
        }
        private async Task Login(string? clientId, bool consent) => await Work(async token =>
        {
            owner.service.Stop(); status.Text = "请在浏览器中完成 ChatGPT 登录与授权…"; status.Visibility = Visibility.Visible;
            var account = await accounts!.SignInAsync(clientId, consent, token); SelectedId = account.ClientId;
            await Reload(); status.Text = account.PlanEnabled ? "已连接 · 使用 ChatGPT 额度" : "已登录，请点击「授权使用 ChatGPT 额度」。";
        });
        private async Task Work(Func<CancellationToken, Task> work)
        {
            if (Busy || !open || accounts is null) return;
            using var stop = CancellationTokenSource.CreateLinkedTokenSource(owner.lifetime.Token); operation = stop; BusyChanged?.Invoke(true); Update();
            try { await work(stop.Token); }
            catch (OperationCanceledException) { if (open) status.Text = "登录已取消，原有连接已保留。"; }
            catch (ChatGptAuthException e) { if (open) status.Text = e.Message; }
            catch { if (open) status.Text = "ChatGPT 连接未完成，原有资料已保留；请稍后重试。"; }
            finally { operation = null; if (open) { owner.InvalidateModelCatalogues(); BusyChanged?.Invoke(false); Update(); } }
        }
        private void Update()
        {
            var current = snapshot.Accounts.FirstOrDefault(a => a.ClientId == SelectedId);
            picker.Visibility = snapshot.Accounts.IsEmpty ? Visibility.Collapsed : Visibility.Visible;
            picker.IsEnabled = !Busy && readable;
            connect.Content = current?.Connected == true ? "重新登录 ChatGPT" : "Continue with ChatGPT";
            connect.IsEnabled = add.IsEnabled = grant.IsEnabled = signOut.IsEnabled = !Busy && readable;
            add.Visibility = snapshot.Accounts.IsEmpty ? Visibility.Collapsed : Visibility.Visible;
            grant.Visibility = current?.Connected == true ? Visibility.Visible : Visibility.Collapsed;
            grant.Content = current?.PlanEnabled == true ? "重新授权额度" : "授权使用 ChatGPT 额度";
            signOut.Visibility = current?.Connected == true ? Visibility.Visible : Visibility.Collapsed;
            cancel.Visibility = Busy ? Visibility.Visible : Visibility.Collapsed;
            actions.Visibility = actions.Children.Any(c => c.Visibility == Visibility.Visible) ? Visibility.Visible : Visibility.Collapsed;
            status.Visibility = status.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        }
        public void Dispose() { open = false; operation?.Cancel(); }
    }
}
