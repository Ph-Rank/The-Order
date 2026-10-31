using System.Collections.Immutable;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using TheOrder.Application.Playground;

namespace TheOrder.WinUI.Views;

public sealed partial class PlaygroundView
{
    private sealed class ConnectionEdit(string endpoint, string model)
    {
        public string Endpoint { get; } = endpoint;
        public string Model { get; } = model;
        public string Secret { get; set; } = "";
        public bool Remove { get; set; }
    }
    private async Task ShowConnectionAsync()
    {
        var initial = service.Snapshot.Settings; var options = Draft;
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
        var providerChoice = new ComboBox { Header = "连接", ItemsSource = ProviderNames, SelectedIndex = (int)options.Provider, HorizontalAlignment = HorizontalAlignment.Stretch };
        var key = new PasswordBox { Header = "API Key", MaxLength = 4096 };
        AutomationProperties.SetAutomationId(key, "PlaygroundApiKey"); AutomationProperties.SetAutomationId(providerChoice, "PlaygroundCredentialProvider");
        var endpoint = Label("", 12); endpoint.Style = PlaygroundAppearance.Style("PGMetadataTextStyle");
        var clear = new Button { Content = "移除此连接的本机 Key", FontSize = 12, HorizontalAlignment = HorizontalAlignment.Left };
        var panel = new StackPanel { Spacing = 14, MinWidth = 280, MaxWidth = 620, Children = {
            providerChoice, key, clear, endpoint,
            Label("密钥按服务商分别保存在本机，不进入聊天和导出；留空保留已保存的密钥。", 12),
            Divider(), Label("全局授权（建议全部勾选）", 16),
            allow, library, history, memory, automatic } };
        bool syncing = false, open = true; int checkGeneration = 0; var current = options.Provider;
        void Capture() { if (!syncing) edits[current].Secret = key.Password; }
        async Task ShowProviderAsync(PlaygroundProviderKind kind)
        {
            Capture(); current = kind; int check = ++checkGeneration; syncing = true;
            key.Password = edits[kind].Secret; key.PlaceholderText = "输入此服务商的 API Key";
            clear.Content = edits[kind].Remove ? "保存时移除 Key" : "移除此连接的本机 Key"; clear.IsEnabled = false;
            endpoint.Text = Host(edits[kind].Endpoint); syncing = false;
            try
            {
                bool exists = await service.HasCredentialAsync(kind, edits[kind].Endpoint, lifetime.Token);
                if (!open || check != checkGeneration) return;
                clear.IsEnabled = exists && !edits[kind].Remove;
                key.PlaceholderText = exists && !edits[kind].Remove ? "已保存；留空保留现有 Key" : "输入此服务商的 API Key";
            }
            catch (OperationCanceledException) { }
            catch { if (open && check == checkGeneration) endpoint.Text = Host(edits[kind].Endpoint) + " · 凭据状态暂不可读"; }
        }
        providerChoice.SelectionChanged += async (_, _) => { if (!syncing && providerChoice.SelectedIndex >= 0) await ShowProviderAsync((PlaygroundProviderKind)providerChoice.SelectedIndex); };
        clear.Click += (_, _) => { edits[current].Remove = true; edits[current].Secret = ""; key.Password = ""; clear.Content = "保存时移除 Key"; clear.IsEnabled = false; };
        await ShowProviderAsync(current);
        try
        {
            var scroll = new ScrollViewer { Content = panel, MaxHeight = Math.Max(180, Math.Min(620, (XamlRoot?.Size.Height ?? 760) - 220)), HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            if (await DialogAsync("连接与授权", scroll, "保存") != ContentDialogResult.Primary) return;
            Capture(); key.Password = "";
            var connections = service.Snapshot.Settings.Connections.ToList();
            foreach (var pair in edits)
            {
                var edit = pair.Value; string address = PlaygroundEndpointPolicy.Normalize(pair.Key, edit.Endpoint);
                if (edit.Remove) await service.ClearCredentialAsync(pair.Key, address, lifetime.Token);
                if (!string.IsNullOrWhiteSpace(edit.Secret)) await service.SaveCredentialAsync(pair.Key, address, edit.Secret, lifetime.Token);
                if (!edit.Remove && string.IsNullOrWhiteSpace(edit.Secret)) continue;
                connections.RemoveAll(c => c.Provider == pair.Key && SameEndpoint(c.Endpoint, address));
                connections.Add(new(pair.Key, address, pair.Key == options.Provider ? options.Model : edit.Model, allow.IsChecked == true));
            }
            var next = service.Snapshot.Settings with { Enabled = true, DiscussionConsent = allow.IsChecked == true,
                Permissions = new(library.IsChecked == true, history.IsChecked == true, memory.IsChecked == true, automatic.IsChecked == true),
                Connections = connections.Select(c => c with { Consented = allow.IsChecked == true }).ToImmutableArray() };
            await service.ConfigureAsync(next, lifetime.Token); InvalidateModelCatalogues(); SyncOptions(); await RefreshCredentialAsync();
        }
        finally { open = false; checkGeneration++; key.Password = ""; foreach (var value in edits.Values) value.Secret = ""; }
    }
}
