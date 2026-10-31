using System.Collections.Immutable;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using TheOrder.Application.Playground;

namespace TheOrder.WinUI.Views;

public sealed partial class PlaygroundView
{
    private readonly ToggleSwitch searchMode = new() { OnContent = "", OffContent = "", MinWidth = 0 };
    private readonly ToggleSwitch spoilerMode = new() { OnContent = "", OffContent = "", MinWidth = 0 };
    private readonly ComboBox effort = new() { HorizontalAlignment = HorizontalAlignment.Stretch, MinWidth = 0 };
    private readonly CheckBox savedContext = new() { Content = "使用已授权的记录与记忆" };
    private readonly TextBlock connectionStatus = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock capabilitiesNotice = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
    // Retained solely for reading legacy explicit-progress records; the new switch uses library status.
    private readonly Button progress = new();
    private readonly TextBlock progressStatus = new();
    private readonly Border modelSurface = new() { CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(1) };
    private readonly Button currentModel = new() { HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(14, 12, 12, 12) };
    private readonly TextBlock currentModelTitle = new() { FontSize = 16, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock currentModelProvider = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap };
    private readonly Button systemPrompt = new() { HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(14, 12, 14, 12), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12) };
    private readonly TextBlock systemPromptPreview = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap, MaxLines = 3, TextTrimming = TextTrimming.CharacterEllipsis };
    private bool modelsLoading, contextConfirmation;
    private static readonly string[] ProviderNames = ["OpenAI", "Google", "Anthropic", "xAI", "DeepSeek"];
    private static string Endpoint(PlaygroundProviderKind kind) => PlaygroundEndpointPolicy.Default(kind);
    private static readonly Dictionary<string, PlaygroundProviderKind> EndpointProviders = Enum.GetValues<PlaygroundProviderKind>()
        .ToDictionary(kind => new Uri(PlaygroundEndpointPolicy.Default(kind)).IdnHost, StringComparer.OrdinalIgnoreCase);

    private void BuildSettings()
    {
        PlaygroundAppearance.Apply(currentModel); PlaygroundAppearance.Apply(systemPrompt);
        var modelRow = new Grid { ColumnSpacing = 10 };
        modelRow.ColumnDefinitions.Add(new()); modelRow.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        modelRow.Children.Add(new StackPanel { Spacing = 5, Children = { currentModelProvider, currentModelTitle } });
        var chevron = PlaygroundAppearance.Icon("\uE70D", 12); chevron.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(chevron, 1); modelRow.Children.Add(chevron); currentModel.Content = modelRow; modelSurface.Child = currentModel;
        currentModel.Click += async (_, _) => await RunAsync(ShowModelPickerAsync);
        AutomationProperties.SetAutomationId(currentModel, "PlaygroundChooseModel"); settings.Children.Add(modelSurface);

        systemPrompt.Content = new StackPanel { Spacing = 7, Children = { Label("对话预设", 14), systemPromptPreview } };
        systemPrompt.Click += async (_, _) => await RunAsync(ShowSystemPromptAsync);
        AutomationProperties.SetAutomationId(systemPrompt, "PlaygroundSystemInstructions"); AutomationProperties.SetName(systemPrompt, "编辑对话预设"); settings.Children.Add(systemPrompt);

        settings.Children.Add(Label("思考等级", 13)); PlaygroundAppearance.Apply(effort, "PGComboBox");
        effort.ItemsSource = new[] { "低", "中", "高" };
        effort.SelectionChanged += async (_, _) => { if (!synchronizing && effort.SelectedIndex >= 0) { Draft = Draft with { ReasoningEffort = new[] { "low", "medium", "high" }[effort.SelectedIndex] }; await RunAsync(ApplyOptionsAsync); } };
        AutomationProperties.SetAutomationId(effort, "PlaygroundReasoningEffort"); settings.Children.Add(effort);

        settings.Children.Add(Divider());
        foreach (var toggle in new[] { searchMode, spoilerMode }) PlaygroundAppearance.Apply(toggle, "PGToggleSwitch");
        searchMode.Toggled += async (_, _) => { if (!synchronizing) { Draft = Draft with { Search = searchMode.IsOn ? PlaygroundSearchMode.Smart : PlaygroundSearchMode.Off }; await RunAsync(ApplyOptionsAsync); } };
        spoilerMode.Toggled += async (_, _) => { if (!synchronizing) { Draft = Draft with { Spoiler = spoilerMode.IsOn ? PlaygroundSpoilerMode.LibraryStatus : PlaygroundSpoilerMode.Full }; await RunAsync(ApplyOptionsAsync); } };
        AutomationProperties.SetAutomationId(searchMode, "PlaygroundSearchPermission"); AutomationProperties.SetName(searchMode, "联网搜索");
        AutomationProperties.SetAutomationId(spoilerMode, "PlaygroundSpoilerProtection"); AutomationProperties.SetName(spoilerMode, "限制剧透");
        settings.Children.Add(SwitchRow("联网搜索", searchMode));
        ToolTipService.SetToolTip(searchMode, "开启仅允许模型按需联网，不强制搜索；关闭后禁止本轮联网搜索和网页读取。");
        var info = new Button { Content = PlaygroundAppearance.Icon("\uE946", 14), Width = 26, Height = 26, MinWidth = 26, MinHeight = 26, Padding = new Thickness(0), Opacity = .7 };
        PlaygroundAppearance.Apply(info, "PGIconButton"); AutomationProperties.SetName(info, "了解剧透限制的工作机制"); AutomationProperties.SetAutomationId(info, "PlaygroundSpoilerInfo");
        var infoBody = new StackPanel { Spacing = 10, MaxWidth = 300, Children = {
            Label("按书架状态限制剧透", 15),
            Label("开启后，依据已授权的书架记录，仅“玩过”的作品可讨论具体剧情；其他作品只作不揭露情节的一般讨论。", 13),
            Label("无法读取或确认记录时，按未玩处理。关闭后不限制剧透。", 13) } };
        var flyout = new Flyout { Content = infoBody, Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.LeftEdgeAlignedTop };
        var presenter = new Style(typeof(FlyoutPresenter)) { BasedOn = PlaygroundAppearance.Style("PGFlyoutPresenter") }; presenter.Setters.Add(new Setter(FrameworkElement.MaxWidthProperty, 350d));
        flyout.FlyoutPresenterStyle = presenter; info.Flyout = flyout; StyleContent(infoBody);
        settings.Children.Add(SwitchRow("限制剧透", spoilerMode, info));

        PlaygroundAppearance.Apply(savedContext, "PGCheckBox");
        savedContext.Content = new TextBlock { Text = "使用已授权的记录与记忆", FontSize = 13, TextWrapping = TextWrapping.Wrap, MaxWidth = 220 };
        savedContext.Checked += async (_, _) =>
        {
            if (synchronizing || contextConfirmation) return;
            var owner = CaptureOptionsOwner();
            if (Current?.Temporary == true)
            {
                contextConfirmation = true;
                try
                {
                    if (await DialogAsync("使用已授权记录？", Label("仅为这段临时会话使用已授权的本地记录；临时消息不会进入长期历史和偏好记忆。", 14), "本会话使用") != ContentDialogResult.Primary)
                    { if (IsCurrentOptionsOwner(owner)) { bool previous = synchronizing; synchronizing = true; savedContext.IsChecked = false; synchronizing = previous; } return; }
                }
                finally { contextConfirmation = false; }
            }
            if (!IsCurrentOptionsOwner(owner)) return;
            var options = OptionsFor(owner.ConversationId) with { UseSavedContext = true };
            StoreOptions(owner.ConversationId, options);
            await RunAsync(() => SaveOptionsAsync(owner.ConversationId, options));
        };
        savedContext.Unchecked += async (_, _) => { if (!synchronizing) { Draft = Draft with { UseSavedContext = false }; await RunAsync(ApplyOptionsAsync); } };
        settings.Children.Add(savedContext);
        BuildGenerationSettings();
        foreach (var text in new[] { currentModelProvider, systemPromptPreview }) text.Style = PlaygroundAppearance.Style("PGMetadataTextStyle");
    }
    private static Grid SwitchRow(string title, ToggleSwitch toggle, Button? info = null)
    {
        var row = new Grid { ColumnSpacing = 8 };
        row.ColumnDefinitions.Add(new()); row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var labels = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        labels.Children.Add(Label(title, 14)); if (info is not null) labels.Children.Add(info);
        row.Children.Add(labels); Grid.SetColumn(toggle, 1); row.Children.Add(toggle); return row;
    }
    private Border Divider()
    {
        var border = new Border { Height = 1, Background = PlaygroundAppearance.Brush(this, "PGBorderBrush"), Margin = new Thickness(0, 1, 0, 1) };
        visualDividers.Add(border); return border;
    }
    private void SyncOptions()
    {
        var options = Draft;
        if (options.Spoiler != PlaygroundSpoilerMode.Full && options.Spoiler != PlaygroundSpoilerMode.LibraryStatus || options.Search == PlaygroundSearchMode.Deep)
            Draft = options = options with { Spoiler = options.Spoiler == PlaygroundSpoilerMode.Full ? PlaygroundSpoilerMode.Full : PlaygroundSpoilerMode.LibraryStatus,
                Search = options.Search == PlaygroundSearchMode.Off ? PlaygroundSearchMode.Off : PlaygroundSearchMode.Smart };
        bool previous = synchronizing; synchronizing = true;
        try
        {
            searchMode.IsOn = options.Search != PlaygroundSearchMode.Off; spoilerMode.IsOn = options.Spoiler != PlaygroundSpoilerMode.Full;
            savedContext.IsChecked = options.UseSavedContext; SyncModelCatalogue();
            systemPromptPreview.Text = string.IsNullOrWhiteSpace(options.SystemInstructions) ? "设定 AI 的角色、语气和交流方式" : options.SystemInstructions;
        }
        finally { synchronizing = previous; }
        UpdateCapabilities(); UpdateProgressStatus();
    }
    private string? DefaultEffort(PlaygroundProviderKind kind, string address, string modelId)
    {
        try { var capabilities = service.Capabilities(kind, address, modelId); return capabilities.Verified && capabilities.ReasoningEfforts.Contains("high") ? "high" : null; }
        catch { return null; }
    }
    private void UpdateCapabilities()
    {
        PlaygroundCapabilities capabilities;
        try { capabilities = service.Capabilities(Draft.Provider, Draft.Endpoint, Draft.Model); }
        catch { capabilities = new(); }
        bool old = synchronizing; synchronizing = true;
        try
        {
            var allowed = new[] { "low", "medium", "high" }.Where(x => capabilities.Verified && capabilities.ReasoningEfforts.Contains(x)).ToArray();
            string? selectedEffort = Draft.ReasoningEffort is { } saved && allowed.Contains(saved) ? saved : allowed.Contains("high") ? "high" : allowed.FirstOrDefault();
            if (Draft.ReasoningEffort != selectedEffort) Draft = Draft with { ReasoningEffort = selectedEffort };
            effort.SelectedIndex = selectedEffort switch { "low" => 0, "medium" => 1, "high" => 2, _ => -1 };
            effort.PlaceholderText = "模型默认"; effort.IsEnabled = allowed.Length != 0;
            effort.Visibility = Visibility.Visible;
        }
        finally { synchronizing = old; }
        ToolTipService.SetToolTip(effort, capabilities.Notice ?? (effort.IsEnabled ? "调节本模型的思考投入；应用于后续消息。" : "当前模型的思考等级尚未核实，使用服务商默认值。"));
    }
    private void UpdateProgressStatus() { }
    private async Task RefreshCredentialAsync()
    {
        if (disposed || !initialized) return;
        var owner = CaptureOptionsOwner(); var options = OptionsFor(owner.ConversationId);
        int request = ++credentialGeneration; string binding = OptionsBinding(options);
        credentialAvailable = false; credentialBinding = binding; UpdateConnectionStatus(); UpdateActions();
        try
        {
            bool available = await service.HasCredentialAsync(options.Provider, options.Endpoint, lifetime.Token);
            if (!IsCurrentOptionsOwner(owner) || request != credentialGeneration || credentialBinding != binding || OptionsBinding(Draft) != binding) return;
            credentialAvailable = available; UpdateConnectionStatus(); UpdateActions(); SyncModelCatalogue();
            if (modelPickerOptions is null && available && CanLoadModels && !modelCatalogues.ContainsKey(binding) && !modelLoadAttempts.Contains(binding)) _ = LoadModelsAsync();
        }
        catch (OperationCanceledException) { }
        catch { if (IsCurrentOptionsOwner(owner) && request == credentialGeneration) connectionStatus.Text = "本机凭据无法读取，请在连接设置中重新保存。"; }
    }
    private void UpdateConnectionStatus()
    {
        connectionStatus.Text = !credentialAvailable ? "此连接尚未保存 API Key。"
            : !PlaygroundAuthorizationPolicy.CanSend(service.Snapshot.Settings) ? "尚未授予全局发送授权。" : "已配置 · " + Host(Draft.Endpoint);
        savedContext.IsEnabled = service.Snapshot.Settings.Permissions.Library || service.Snapshot.Settings.Permissions.History || service.Snapshot.Settings.Permissions.Memory;
        refreshModelPicker?.Invoke();
    }

    private async Task ShowProgressAsync()
    {
        if (Current is null) return;
        var selectedWorks = Draft.WorkProgress.ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal);
        IReadOnlyList<(string Key, string Title)> available = service.Snapshot.Settings.Permissions.Library ? ProgressWorks?.Invoke() ?? [] : [];
        var panel = new StackPanel { Spacing = 10, MinWidth = 280, MaxWidth = 600 };
        panel.Children.Add(Label("只记录你明确给出的进度，不根据游玩时长或“通关”状态推算线路。边界仅影响后续回复；无法确认安全的旧正文和偏好仍会省略。", 13));
        var rows = new StackPanel { Spacing = 10 };
        var editors = new Dictionary<string, TextBox>(StringComparer.Ordinal);
        void Add(string workKey, string text)
        {
            if (editors.ContainsKey(workKey)) return;
            var name = available.FirstOrDefault(x => x.Key == workKey).Title ?? "已保存的作品边界";
            var editor = new TextBox { Header = name, Text = text, MaxLength = 400, PlaceholderText = "例如：共通线第3章；其他线路未知", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap };
            editors[workKey] = editor; rows.Children.Add(editor);
        }
        foreach (var pair in selectedWorks) Add(pair.Key, pair.Value);
        if (available.Count > 0)
        {
            var choose = new ComboBox { Header = "选择需要讨论的作品", HorizontalAlignment = HorizontalAlignment.Stretch, DisplayMemberPath = "Title", ItemsSource = available.Select(x => new ProgressWork(x.Key, x.Title)).ToArray() };
            choose.SelectionChanged += (_, _) => { if (choose.SelectedItem is ProgressWork work) Add(work.Key, ""); }; panel.Children.Add(choose);
        }
        else panel.Children.Add(Label("尚未授权本地记录，或当前没有可供选择的库内作品；不会枚举整库。", 12));
        panel.Children.Add(rows); panel.Children.Add(Label("清空一项进度可移除该作品的明确边界，之后按未知处理。", 12));
        var scroll = new ScrollViewer { Content = panel, MaxHeight = Math.Max(180, Math.Min(540, (XamlRoot?.Size.Height ?? 760) - 220)), HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        if (await DialogAsync("按作品设置进度", scroll, "保存边界") != ContentDialogResult.Primary) return;
        Draft = Draft with { WorkProgress = editors.Where(x => !string.IsNullOrWhiteSpace(x.Value.Text)).ToImmutableDictionary(x => x.Key, x => x.Value.Text.Trim(), StringComparer.Ordinal) };
        await ApplyOptionsAsync(); UpdateProgressStatus();
    }
    private sealed record ProgressWork(string Key, string Title);
    private async Task ShowMemoriesAsync()
    {
        var query = new TextBox { PlaceholderText = "搜索记忆", MaxLength = 200 };
        var list = new ListView { MaxHeight = 180, SelectionMode = ListViewSelectionMode.Single, DisplayMemberPath = "Text" };
        var text = new TextBox { Header = "偏好内容", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 80, MaxHeight = 150, MaxLength = 2000, IsEnabled = false };
        var original = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12, IsTextSelectionEnabled = true };
        var disabled = new CheckBox { Content = "暂不使用这条记忆", IsEnabled = false };
        var status = Label("选择一条记忆查看来源。", 12);
        var save = new Button { Content = "保存修改", IsEnabled = false }; var forget = new Button { Content = "忘记这条偏好", IsEnabled = false }; var source = new Button { Content = "查看原始讨论", IsEnabled = false };
        PlaygroundMemory? current = null; bool sync = false, confirmForget = false, memoryOpen = true;
        int memorySearchGeneration = 0;
        var memorySearchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        async Task PopulateAsync()
        {
            string term = query.Text.Trim(); int generation = ++memorySearchGeneration;
            PlaygroundMemory[] found;
            try { found = string.IsNullOrWhiteSpace(term) ? service.Snapshot.Memories.OrderByDescending(x => x.UpdatedAt).Take(200).ToArray()
                : await Task.Run(() => service.SearchMemories(term).ToArray(), lifetime.Token); }
            catch (OperationCanceledException) { return; }
            catch { if (!disposed && memoryOpen && generation == memorySearchGeneration) status.Text = "记忆搜索未能完成，可以重试。"; return; }
            if (!memoryOpen || disposed || generation != memorySearchGeneration || query.Text.Trim() != term) return;
            var id = current?.Id; sync = true; list.ItemsSource = found;
            list.SelectedItem = id is { } value ? found.FirstOrDefault(x => x.Id == value) : null;
            sync = false; Select();
        }
        void Select()
        {
            if (sync) return; current = list.SelectedItem as PlaygroundMemory; sync = true;
            text.Text = current?.Text ?? ""; disabled.IsChecked = current?.Disabled ?? false;
            text.IsEnabled = disabled.IsEnabled = save.IsEnabled = forget.IsEnabled = source.IsEnabled = current is not null;
            original.Text = current is null ? "" : (current.Kind == "inferred" ? "系统推测" : "用户观点") + " · " + current.UpdatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm")
                + (string.IsNullOrWhiteSpace(current.OriginalQuote) ? "" : "\n来源原文：“" + current.OriginalQuote + "”");
            status.Text = current is null ? "当前没有可显示的记忆。" : "更改影响后续读取；已发送给服务商的信息无法撤回。";
            confirmForget = false; forget.Content = "忘记这条偏好"; sync = false;
        }
        query.TextChanged += (_, _) => { memorySearchGeneration++; memorySearchTimer.Stop(); memorySearchTimer.Start(); }; list.SelectionChanged += (_, _) => Select();
        memorySearchTimer.Tick += async (_, _) => { memorySearchTimer.Stop(); await PopulateAsync(); };
        disabled.Click += async (_, _) => { if (current is null || sync) return; await RunAsync(async () => { await service.SetMemoryDisabledAsync(current.Id, disabled.IsChecked == true, lifetime.Token); await PopulateAsync(); }); };
        save.Click += async (_, _) => { if (current is null || string.IsNullOrWhiteSpace(text.Text)) return; await RunAsync(async () => { await service.EditMemoryAsync(current.Id, text.Text.Trim(), lifetime.Token); status.Text = "已保存。新的明确表达优先于旧推测。"; await PopulateAsync(); }); };
        forget.Click += async (_, _) =>
        {
            if (current is null) return;
            if (!confirmForget) { confirmForget = true; forget.Content = "确认忘记"; status.Text = "将移除这条派生偏好并抑制从保留原话自动重新提取；原会话不会因此删除。"; return; }
            await RunAsync(async () => { await service.ForgetMemoryAsync(current.Id, lifetime.Token); current = null; await PopulateAsync(); });
        };
        var actions = new TagFlowPanel { HorizontalSpacing = 6, VerticalSpacing = 6 }; actions.Children.Add(save); actions.Children.Add(disabled); actions.Children.Add(forget); actions.Children.Add(source);
        var content = new StackPanel { Spacing = 10, MinWidth = 280, MaxWidth = 620, Children = { query, list, text, original, actions, status } };
        var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = "偏好记忆", Content = new ScrollViewer { Content = content, MaxHeight = Math.Max(220, Math.Min(610, (XamlRoot?.Size.Height ?? 760) - 180)), HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled },
            CloseButtonText = "关闭", RequestedTheme = PaletteTheme, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        PlaygroundAppearance.Scope(dialog); dialog.Style = PlaygroundAppearance.Style("PGDialog"); dialog.CloseButtonStyle = PlaygroundAppearance.Style("PGGhostButton"); StyleContent(content);
        dialog.Resources["ContentDialogMaxWidth"] = 760d;
        source.Click += async (_, _) =>
        {
            if (current is null) return; var origin = current; dialog.Hide();
            await OpenSourceAsync(new(origin.Id.ToString("N"), PlaygroundSourceKind.Memory, "记忆来源") { TargetId = origin.ConversationId.ToString(), MessageId = origin.MessageId.ToString(), WorkKey = origin.WorkKey, At = origin.CreatedAt });
        };
        await PopulateAsync();
        try { await dialog.ShowAsync(); }
        finally { memoryOpen = false; memorySearchGeneration++; memorySearchTimer.Stop(); }
    }
    private static bool SameEndpoint(string a, string b)
    {
        if (!Uri.TryCreate(a, UriKind.Absolute, out var uri) || !EndpointProviders.TryGetValue(uri.IdnHost, out var kind)) return false;
        try { return PlaygroundEndpointPolicy.Normalize(kind, a) == PlaygroundEndpointPolicy.Normalize(kind, b); }
        catch (ArgumentException) { return false; }
    }
    private static string Host(string value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) ? uri.Authority : "所选端点";
    private static int Number(double value, int fallback, int minimum, int maximum) => double.IsFinite(value) ? Math.Clamp((int)value, minimum, maximum) : fallback;
}
