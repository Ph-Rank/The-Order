using System.Collections.Immutable;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using TheOrder.Application.Playground;

namespace TheOrder.WinUI.Views;

public sealed partial class PlaygroundView
{
    private static readonly PlaygroundProviderKind[] ProviderGroups =
        [PlaygroundProviderKind.OpenAI, PlaygroundProviderKind.Gemini, PlaygroundProviderKind.Claude, PlaygroundProviderKind.Grok, PlaygroundProviderKind.DeepSeek];
    private static readonly string[] OpenAiMethods = ["ChatGPT 登录", "API Key"];

    private static PlaygroundProviderKind ProviderGroup(PlaygroundProviderKind provider) =>
        provider == PlaygroundProviderKind.ChatGPT ? PlaygroundProviderKind.OpenAI : provider;
    private static string ProviderName(PlaygroundProviderKind provider) => ProviderGroup(provider) switch
    {
        PlaygroundProviderKind.OpenAI => "OpenAI",
        PlaygroundProviderKind.Gemini => "Google",
        PlaygroundProviderKind.Claude => "Anthropic",
        PlaygroundProviderKind.Grok => "xAI",
        PlaygroundProviderKind.DeepSeek => "DeepSeek",
        _ => provider.ToString()
    };
    private static PlaygroundProviderKind OpenAiMethodFor(PlaygroundConversationOptions options, PlaygroundSettings settings)
    {
        if (options.Provider == PlaygroundProviderKind.ChatGPT) return options.Provider;
        if (options.Provider == PlaygroundProviderKind.OpenAI && (!string.IsNullOrWhiteSpace(options.Model)
            || settings.Connections.Any(c => c.Provider == PlaygroundProviderKind.OpenAI))) return options.Provider;
        return settings.Connections.FirstOrDefault(c => ProviderGroup(c.Provider) == PlaygroundProviderKind.OpenAI)?.Provider
            ?? PlaygroundProviderKind.ChatGPT;
    }
    private static RadioButtons OpenAiMethodPicker(PlaygroundProviderKind provider, string automationId)
    {
        var picker = new RadioButtons { ItemsSource = OpenAiMethods, MaxColumns = 2,
            SelectedIndex = provider == PlaygroundProviderKind.OpenAI ? 1 : 0 };
        AutomationProperties.SetName(picker, "OpenAI 连接方式");
        AutomationProperties.SetAutomationId(picker, automationId);
        return picker;
    }
    private static PlaygroundProviderKind SelectedOpenAiMethod(RadioButtons picker) =>
        picker.SelectedIndex == 1 ? PlaygroundProviderKind.OpenAI : PlaygroundProviderKind.ChatGPT;

    private static PlaygroundConversationOptions WithConnection(PlaygroundConversationOptions options,
        PlaygroundProviderKind provider, PlaygroundConnection? connection)
    {
        string endpoint = connection?.Endpoint ?? Endpoint(provider);
        string? account = connection?.ChatGptAccountId;
        if (options.Provider == provider && SameEndpoint(options.Endpoint, endpoint)
            && (provider != PlaygroundProviderKind.ChatGPT || account is null || options.ChatGptAccountId == account)) return options;
        return options with { Provider = provider, Endpoint = endpoint, ChatGptAccountId = account,
            Model = connection?.Model ?? "", ReasoningEffort = null };
    }

    private sealed class ConnectionEdit(string endpoint, string model)
    {
        public string Endpoint { get; } = endpoint;
        public string Model { get; } = model;
        public string Secret { get; set; } = "";
        public bool Remove { get; set; }
    }

    private Task ShowConnectionAsync() => ShowConnectionForProviderAsync(null);

    private async Task ShowConnectionForProviderAsync(PlaygroundProviderKind? preferred)
    {
        var initial = service.Snapshot.Settings;
        var owner = CaptureOptionsOwner();
        var options = OptionsFor(owner.ConversationId);
        var group = ProviderGroup(preferred ?? options.Provider);
        var start = group == PlaygroundProviderKind.OpenAI
            ? preferred ?? OpenAiMethodFor(options, initial)
            : group;
        var edits = Enum.GetValues<PlaygroundProviderKind>().ToDictionary(kind => kind, kind =>
        {
            var existing = initial.Connections.FirstOrDefault(c => c.Provider == kind);
            return new ConnectionEdit(existing?.Endpoint ?? Endpoint(kind), existing?.Model ?? "");
        });
        var allow = new CheckBox { Content = "允许发送我主动提交的讨论内容", IsChecked = PlaygroundAuthorizationPolicy.CanSend(initial) };
        var library = new CheckBox { Content = "按需读取本地游玩记录、状态、评分和评价（只读）", IsChecked = initial.Permissions.Library };
        var history = new CheckBox { Content = "检索本资料库的历史对话", IsChecked = initial.Permissions.History };
        var memory = new CheckBox { Content = "使用已保存的偏好记忆", IsChecked = initial.Permissions.Memory };
        var automatic = new CheckBox { Content = "自动整理有证据的文娱偏好", IsChecked = initial.Permissions.AutoMemory };
        var providerChoice = new SelectedFirstComboBox { Header = "服务商", OptionsSource = ProviderGroups.Select(ProviderName).ToArray(),
            OptionIndex = Array.IndexOf(ProviderGroups, group), HorizontalAlignment = HorizontalAlignment.Stretch };
        var openAiMethod = OpenAiMethodPicker(group == PlaygroundProviderKind.OpenAI ? start : OpenAiMethodFor(options, initial),
            "PlaygroundOpenAiMethod");
        var key = new PasswordBox { Header = "API Key", MaxLength = 4096 };
        AutomationProperties.SetAutomationId(key, "PlaygroundApiKey");
        AutomationProperties.SetAutomationId(providerChoice, "PlaygroundCredentialProvider");

        var endpoint = Label("", 12);
        endpoint.Style = PlaygroundAppearance.Style("PGMetadataTextStyle");
        var clear = new Button { Content = "移除此连接的本机 Key", FontSize = 12, HorizontalAlignment = HorizontalAlignment.Left };
        var keyNote = Label("留空保留已保存的密钥。", 12);
        var apiBillingNote = Label("此方式按 OpenAI API 用量计费。", 12);
        apiBillingNote.Style = PlaygroundAppearance.Style("PGMetadataTextStyle");
        var keyPanel = new StackPanel { Spacing = 8, Children = { apiBillingNote, key, clear, endpoint, keyNote } };
        using var chatGpt = new ChatGptConnectionEditor(this, options.ChatGptAccountId
            ?? initial.Connections.FirstOrDefault(c => c.Provider == PlaygroundProviderKind.ChatGPT)?.ChatGptAccountId);
        var panel = new StackPanel { Spacing = 14, MinWidth = 280, MaxWidth = 620, Children = {
            providerChoice, openAiMethod, chatGpt.Panel, keyPanel,
            Divider(), Label("全局授权（建议全部勾选）", 16), allow, library, history, memory, automatic } };

        bool open = true;
        int checkGeneration = 0;
        var current = start;
        void CapturePassword()
        {
            if (current != PlaygroundProviderKind.ChatGPT)
                edits[current].Secret = key.Password;
        }
        async Task ShowProviderAsync(PlaygroundProviderKind kind)
        {
            if (!open || chatGpt.Busy) return;
            CapturePassword();
            current = kind;
            int check = ++checkGeneration;
            bool chatGptSelected = kind == PlaygroundProviderKind.ChatGPT;
            openAiMethod.Visibility = ProviderGroup(kind) == PlaygroundProviderKind.OpenAI ? Visibility.Visible : Visibility.Collapsed;
            chatGpt.Panel.Visibility = chatGptSelected ? Visibility.Visible : Visibility.Collapsed;
            keyPanel.Visibility = chatGptSelected ? Visibility.Collapsed : Visibility.Visible;
            apiBillingNote.Visibility = kind == PlaygroundProviderKind.OpenAI ? Visibility.Visible : Visibility.Collapsed;
            key.Password = edits[kind].Secret;
            key.PlaceholderText = "输入此服务商的 API Key";
            clear.Content = edits[kind].Remove ? "保存时移除 Key" : "移除此连接的本机 Key";
            clear.IsEnabled = false;
            endpoint.Text = Host(edits[kind].Endpoint);
            if (chatGptSelected)
            {
                await chatGpt.Reload();
                return;
            }
            try
            {
                bool exists = await service.HasCredentialAsync(kind, edits[kind].Endpoint, lifetime.Token);
                if (!open || check != checkGeneration) return;
                clear.IsEnabled = exists && !edits[kind].Remove;
                if (clear.IsEnabled) key.PlaceholderText = "已保存；留空保留现有 Key";
            }
            catch (OperationCanceledException) { }
            catch { if (open && check == checkGeneration) endpoint.Text = Host(edits[kind].Endpoint) + " · 凭据状态暂不可读"; }
        }
        providerChoice.OptionChanged += async (_, _) =>
        {
            if (providerChoice.OptionIndex < 0) return;
            group = ProviderGroups[providerChoice.OptionIndex];
            await ShowProviderAsync(group == PlaygroundProviderKind.OpenAI ? SelectedOpenAiMethod(openAiMethod) : group);
        };
        openAiMethod.SelectionChanged += async (_, _) =>
        {
            if (openAiMethod.SelectedIndex >= 0 && group == PlaygroundProviderKind.OpenAI)
                await ShowProviderAsync(SelectedOpenAiMethod(openAiMethod));
        };
        clear.Click += (_, _) =>
        {
            edits[current].Remove = true;
            key.Password = "";
            clear.Content = "保存时移除 Key";
            clear.IsEnabled = false;
        };
        ChatGptAccountInfo? welcome = null;
        try
        {
            await ShowProviderAsync(current);
            var scroll = new ScrollViewer { Content = panel, MaxHeight = Math.Max(180, Math.Min(620, (XamlRoot?.Size.Height ?? 760) - 220)), HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            if (await DialogAsync("连接与授权", scroll, "保存", dialog => chatGpt.BusyChanged = busy =>
                dialog.IsPrimaryButtonEnabled = providerChoice.IsEnabled = openAiMethod.IsEnabled = !busy) != ContentDialogResult.Primary) return;

            CapturePassword();
            key.Password = "";
            var connections = service.Snapshot.Settings.Connections.ToList();
            foreach (var pair in edits)
            {
                if (pair.Key == PlaygroundProviderKind.ChatGPT) continue;
                var edit = pair.Value;
                if (!edit.Remove && string.IsNullOrWhiteSpace(edit.Secret)) continue;
                string address = PlaygroundEndpointPolicy.Normalize(pair.Key, edit.Endpoint);
                if (!string.IsNullOrWhiteSpace(edit.Secret))
                    await service.SaveCredentialAsync(pair.Key, address, edit.Secret, lifetime.Token);
                else if (edit.Remove)
                    await service.ClearCredentialAsync(pair.Key, address, lifetime.Token);
                var index = connections.FindIndex(c => c.Provider == pair.Key && SameEndpoint(c.Endpoint, address));
                if (index < 0) connections.Add(new(pair.Key, address, pair.Key == options.Provider ? options.Model : edit.Model));
            }

            if (chatGpt.SelectedId is { } accountId)
            {
                int index = connections.FindIndex(c => c.Provider == PlaygroundProviderKind.ChatGPT);
                if (index < 0) connections.Add(new(PlaygroundProviderKind.ChatGPT, Endpoint(PlaygroundProviderKind.ChatGPT)) { ChatGptAccountId = accountId });
                else if (connections[index].ChatGptAccountId != accountId)
                    connections[index] = connections[index] with { ChatGptAccountId = accountId, Model = "" };
            }
            PlaygroundConnection? active = null;
            if (current == PlaygroundProviderKind.ChatGPT && chatGpt.SelectedId is not null)
                active = connections.First(c => c.Provider == PlaygroundProviderKind.ChatGPT);
            else if (current == PlaygroundProviderKind.OpenAI
                && await service.HasCredentialAsync(current, edits[current].Endpoint, lifetime.Token))
                active = connections.FirstOrDefault(c => c.Provider == current) ?? new(current, edits[current].Endpoint, edits[current].Model);
            else if (current == PlaygroundProviderKind.OpenAI && options.Provider != current)
                ShowError("保存 API Key 后即可切换到 OpenAI API。");

            if (active is not null)
            {
                connections.Remove(active);
                connections.Insert(0, active);
            }
            var next = service.Snapshot.Settings with { Enabled = true, DiscussionConsent = allow.IsChecked == true,
                Permissions = new(library.IsChecked == true, history.IsChecked == true, memory.IsChecked == true, automatic.IsChecked == true),
                Connections = connections.Select(c => c with { Consented = allow.IsChecked == true }).ToImmutableArray() };
            await service.ConfigureAsync(next, lifetime.Token);
            if (active is not null && IsCurrentOptionsOwner(owner))
            {
                Draft = WithConnection(OptionsFor(owner.ConversationId), active.Provider, active);
                await ApplyOptionsAsync();
                if (active.Provider == PlaygroundProviderKind.ChatGPT) welcome = chatGpt.PendingWelcome;
            }
            SyncOptions();
        }
        finally
        {
            open = false;
            checkGeneration++;
            key.Password = "";
            foreach (var edit in edits.Values) edit.Secret = "";
            chatGpt.Dispose();
            InvalidateModelCatalogues();
            await RefreshCredentialAsync();
            if (!disposed && welcome is { } account && await DialogAsync("正在使用 ChatGPT 额度",
                Label("此连接的对话将使用你授权的 ChatGPT 额度。你可以在 ChatGPT 用量设置中查看使用情况和调整额度。", 14), "知道了",
                dialog => dialog.CloseButtonText = "") == ContentDialogResult.Primary)
                await chatGptAccounts!.MarkWelcomedAsync(account.ClientId, lifetime.Token);
        }
    }
}
