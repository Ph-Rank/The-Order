using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using TheOrder.Application.Playground;

namespace TheOrder.WinUI.Views;

public sealed partial class PlaygroundView
{
    private bool modelPickerOpen;
    private Func<object>? readModelPickerEvidence;
    private object? lastModelPickerEvidence;
    private async Task ShowModelPickerAsync()
    {
        if (modelPickerOpen) return;
        modelPickerOpen = true; var owner = CaptureOptionsOwner();
        modelPickerOptions = OptionsFor(owner.ConversationId);
        bool selectedModel = false, open = true; int credentialCheck = 0;
        PlaygroundConversationOptions? chosenOptions = null;
        var tabs = new TagFlowPanel { HorizontalSpacing = 6, VerticalSpacing = 6 };
        var filter = new TextBox { PlaceholderText = "搜索模型", MaxLength = 160 };
        PlaygroundAppearance.Apply(filter, "PGTextBox"); AutomationProperties.SetName(filter, "搜索可用模型");
        var rows = new StackPanel { Spacing = 6 };
        var rowCache = new Dictionary<string, Button>(StringComparer.Ordinal); int createdRows = 0, refreshes = 0;
        var status = Label("", 13); status.Style = PlaygroundAppearance.Style("PGMetadataTextStyle"); status.MinHeight = 20;
        var refresh = new Button { Content = PlaygroundAppearance.IconLabel("\uE72C", "刷新模型", 12), HorizontalAlignment = HorizontalAlignment.Left };
        PlaygroundAppearance.Apply(refresh);
        var manual = new TextBox { Header = "模型 ID", MaxLength = 160, Visibility = Visibility.Collapsed };
        PlaygroundAppearance.Apply(manual, "PGTextBox");
        var manualApply = new Button { Content = "使用此型号", Visibility = Visibility.Collapsed };
        PlaygroundAppearance.Apply(manualApply);
        var manualToggle = new Button { Content = "手动填写模型 ID" }; PlaygroundAppearance.Apply(manualToggle);
        var actions = new TagFlowPanel { HorizontalSpacing = 6, VerticalSpacing = 6, Children = { refresh, manualToggle } };
        // Keep the modal frame and its surrounding scrim stable across cold,
        // cached and empty catalogues. Only the results viewport scrolls.
        var body = new Grid { RowSpacing = 12, Width = Math.Clamp((XamlRoot?.Size.Width ?? 760) - 120, 280, 600), Height = Math.Clamp((XamlRoot?.Size.Height ?? 760) - 240, 210, 590) };
        foreach (var height in new[] { GridLength.Auto, GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto, GridLength.Auto, GridLength.Auto }) body.RowDefinitions.Add(new() { Height = height });
        var results = new Grid { RowSpacing = 4 }; results.RowDefinitions.Add(new() { Height = GridLength.Auto }); results.RowDefinitions.Add(new()); results.Children.Add(status);
        var listScroll = new ScrollViewer { Content = rows, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetRow(listScroll, 1); results.Children.Add(listScroll);
        var sections = new FrameworkElement[] { filter, tabs, results, actions, manual, manualApply };
        for (int i = 0; i < sections.Length; i++) { Grid.SetRow(sections[i], i); body.Children.Add(sections[i]); }
        var frameSizes = new List<object>();
        body.SizeChanged += (_, e) => { if (frameSizes.Count < 32 && e.NewSize.Width > 0 && e.NewSize.Height > 0) frameSizes.Add(new { width = e.NewSize.Width, height = e.NewSize.Height }); };
        var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = "选择模型", RequestedTheme = PaletteTheme, CloseButtonText = "取消", DefaultButton = ContentDialogButton.None,
            Content = body };
        PlaygroundAppearance.Scope(dialog); dialog.Style = PlaygroundAppearance.Style("PGDialog"); dialog.CloseButtonStyle = PlaygroundAppearance.Style("PGDialogAction"); dialog.Resources["ContentDialogMaxWidth"] = 700d;
        bool OwnsPicker() => open && IsCurrentOptionsOwner(owner) && modelPickerOptions is not null;
        void Choose(string id)
        {
            id = id.Trim(); if (id.Length == 0 || !OwnsPicker()) return;
            try
            {
                var preview = modelPickerOptions!; var wanted = preview.ReasoningEffort;
                var caps = service.Capabilities(preview.Provider, preview.Endpoint, id);
                chosenOptions = preview with { Model = id, ReasoningEffort = wanted is not null && caps.ReasoningEfforts.Contains(wanted) ? wanted : DefaultEffort(preview.Provider, preview.Endpoint, id) };
                StoreOptions(owner.ConversationId, chosenOptions);
                selectedModel = true; dialog.Hide();
            }
            catch (PlaygroundProviderException error) { status.Text = error.Failure.Message; status.Opacity = 1; }
            catch { status.Text = "模型 ID 无效，请检查后重试。"; status.Opacity = 1; }
        }
        void Populate()
        {
            if (!OwnsPicker()) return;
            var preview = modelPickerOptions!;
            foreach (var tab in tabs.Children.OfType<Button>())
            {
                bool current = tab.Tag is PlaygroundProviderKind kind && kind == preview.Provider;
                tab.Background = PlaygroundAppearance.Brush(this, current ? "PGSurfaceBrush" : "PGCanvasBrush");
                tab.BorderBrush = PlaygroundAppearance.Brush(this, current ? "PGPrimaryBrush" : "PGBorderBrush");
            }
            refreshes++; string term = filter.Text.Trim(); var wantedRows = new List<Button>();
            foreach (var item in CurrentModels.Where(m => term.Length == 0 || m.Name.Contains(term, StringComparison.OrdinalIgnoreCase) || m.Id.Contains(term, StringComparison.OrdinalIgnoreCase)))
            {
                string displayName = ModelDisplayName(item.Id, item.Name);
                var quotedAt = DateTimeOffset.UtcNow;
                string pricing = PlaygroundPricing.Describe(preview.Provider, item.Id, quotedAt);
                string key = ModelBinding + "|" + item.Id + "|" + displayName + "|" + pricing + "|" + (item.Id == preview.Model);
                if (rowCache.TryGetValue(key, out var cached)) { wantedRows.Add(cached); continue; }
                var quote = PlaygroundPricing.Quote(preview.Provider, item.Id, quotedAt);
                var text = new StackPanel { Spacing = 5, Children = { Label(displayName, 16), Label(pricing, 12) } };
                ((TextBlock)text.Children[1]).Style = PlaygroundAppearance.Style("PGMetadataTextStyle");
                var row = new Grid { ColumnSpacing = 12 }; row.ColumnDefinitions.Add(new()); row.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); row.Children.Add(text);
                if (item.Id == preview.Model) { var check = PlaygroundAppearance.Icon("\uE73E", 14); check.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(check, 1); row.Children.Add(check); }
                var button = new Button { Content = row, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(14, 12, 14, 12), CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1) };
                PlaygroundAppearance.Apply(button, "PGButton"); AutomationProperties.SetName(button, "使用 " + displayName + "，" + pricing);
                string details = "模型 ID：" + item.Id + (quote is null ? "" : "\n" + quote.Notice
                    + "\n核验日期：" + quote.VerifiedAt.ToString("yyyy-MM-dd") + "\n" + quote.SourceUrl);
                AutomationProperties.SetHelpText(button, details);
                ToolTipService.SetToolTip(button, new TextBlock { Text = details, TextWrapping = TextWrapping.Wrap, MaxWidth = 440 });
                button.Click += (_, _) => Choose(item.Id); rowCache.Add(key, button); wantedRows.Add(button); createdRows++;
            }
            for (int i = 0; i < wantedRows.Count; i++)
            {
                var button = wantedRows[i];
                if (i < rows.Children.Count && ReferenceEquals(rows.Children[i], button)) continue;
                if (rows.Children.Contains(button)) rows.Children.Remove(button);
                rows.Children.Insert(i, button);
            }
            while (rows.Children.Count > wantedRows.Count) rows.Children.RemoveAt(rows.Children.Count - 1);
            status.Text = ModelCatalogueStatus;
            if (status.Text.Length == 0 && rows.Children.Count == 0) status.Text = "没有匹配的型号。";
            status.Opacity = status.Text.Length == 0 ? 0 : 1;
            refresh.IsEnabled = CanLoadModels && !modelsLoading;
        }
        async Task CheckPickerCredentialAsync()
        {
            if (!OwnsPicker()) return;
            var preview = modelPickerOptions!; string binding = OptionsBinding(preview); int check = ++credentialCheck;
            modelPickerCredentialAvailable = false; modelPickerCredentialBinding = binding; modelPickerCheckingCredential = true; Populate();
            try
            {
                bool available = await service.HasCredentialAsync(preview.Provider, preview.Endpoint, lifetime.Token);
                if (!OwnsPicker() || check != credentialCheck || binding != ModelBinding) return;
                modelPickerCredentialAvailable = available; modelPickerCheckingCredential = false; Populate();
                if (available && CanLoadModels && !modelCatalogues.ContainsKey(binding) && !modelLoadAttempts.Contains(binding)) _ = LoadModelsAsync();
            }
            catch (OperationCanceledException) { }
            catch { if (OwnsPicker() && check == credentialCheck) { modelPickerCheckingCredential = false; status.Text = "本机凭据暂不可读，可稍后重试。"; status.Opacity = 1; } }
        }
        foreach (var kind in Enum.GetValues<PlaygroundProviderKind>())
        {
            var tab = new Button { Content = ProviderNames[(int)kind], Tag = kind, CornerRadius = new CornerRadius(16), BorderThickness = new Thickness(1), Padding = new Thickness(12, 7, 12, 7), FontSize = 13 };
            PlaygroundAppearance.Apply(tab, "PGButton"); tab.Click += async (_, _) =>
            {
                if (!OwnsPicker() || kind == modelPickerOptions!.Provider) return;
                CancelModelLoading(); var connection = service.Snapshot.Settings.Connections.FirstOrDefault(c => c.Provider == kind);
                modelPickerOptions = modelPickerOptions! with { Provider = kind, Endpoint = connection?.Endpoint ?? Endpoint(kind), Model = connection?.Model ?? "" };
                await CheckPickerCredentialAsync();
            }; tabs.Children.Add(tab);
        }
        filter.TextChanged += (_, _) => Populate(); refresh.Click += async (_, _) => await LoadModelsAsync();
        manualToggle.Click += (_, _) => { if (!OwnsPicker()) return; manual.Text = modelPickerOptions!.Model; manual.Visibility = manualApply.Visibility = manual.Visibility == Visibility.Collapsed ? Visibility.Visible : Visibility.Collapsed; };
        manualApply.Click += (_, _) => Choose(manual.Text);
        readModelPickerEvidence = () => new { provider = modelPickerOptions?.Provider.ToString(), width = body.ActualWidth, height = body.ActualHeight,
            viewportWidth = listScroll.ActualWidth, viewportHeight = listScroll.ActualHeight, visibleRows = rows.Children.Count, createdRows, refreshes, frameSizes = frameSizes.ToArray(), status = status.Text };
        refreshModelPicker = Populate; dismissModelPicker = () => dialog.Hide(); StyleContent(body); Populate();
        try { _ = CheckPickerCredentialAsync(); await dialog.ShowAsync(); }
        finally
        {
            lastModelPickerEvidence = readModelPickerEvidence?.Invoke(); readModelPickerEvidence = null;
            open = false; credentialCheck++; refreshModelPicker = null; dismissModelPicker = null; modelPickerOpen = false;
            modelPickerOptions = null; modelPickerCredentialAvailable = modelPickerCheckingCredential = false; modelPickerCredentialBinding = null; CancelModelLoading();
            var save = selectedModel && chosenOptions is not null ? SaveOptionsAsync(owner.ConversationId, chosenOptions) : Task.CompletedTask;
            if (IsCurrentOptionsOwner(owner)) { SyncOptions(); await RefreshCredentialAsync(); }
            await save;
        }
    }
    private async Task ShowSystemPromptAsync()
    {
        var owner = CaptureOptionsOwner();
        var editor = new TextBox { Text = OptionsFor(owner.ConversationId).SystemInstructions, MaxLength = 8000, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 220, MaxHeight = Math.Max(220, Math.Min(480, (XamlRoot?.Size.Height ?? 760) - 300)), PlaceholderText = "例如：像熟悉视觉小说的朋友一样交流，语气自然直接，回答简洁；避免反复概括我的偏好。" };
        AutomationProperties.SetName(editor, "编辑对话预设");
        var body = new StackPanel { Spacing = 12, MinWidth = 280, MaxWidth = 620, Children = { editor, Label("用于当前会话的回复方式。它不会扩大授权、开启联网或解除剧透限制。", 12) } };
        if (await DialogAsync("对话预设", body, "保存") != ContentDialogResult.Primary || !IsCurrentOptionsOwner(owner)) return;
        var options = OptionsFor(owner.ConversationId) with { SystemInstructions = editor.Text.Trim() };
        StoreOptions(owner.ConversationId, options); SyncOptions(); await SaveOptionsAsync(owner.ConversationId, options);
    }
}
