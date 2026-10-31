using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using TheOrder.Application.Playground;

namespace TheOrder.WinUI.Views;

public sealed partial class PlaygroundView
{
    private readonly Slider generationOutput = new() { Minimum = 128, Maximum = 65536, StepFrequency = 128, SmallChange = 128, LargeChange = 4096, HorizontalAlignment = HorizontalAlignment.Stretch, IsThumbToolTipEnabled = false };
    private readonly Slider generationContext = new() { Minimum = 2000, Maximum = 1000000, StepFrequency = 1000, SmallChange = 1000, LargeChange = 32000, HorizontalAlignment = HorizontalAlignment.Stretch, IsThumbToolTipEnabled = false };
    private readonly Slider generationTimeout = new() { Minimum = 10, Maximum = 1800, StepFrequency = 10, SmallChange = 10, LargeChange = 60, HorizontalAlignment = HorizontalAlignment.Stretch, IsThumbToolTipEnabled = false };
    private readonly Button generationSave = new() { Content = "保存生成设置", HorizontalAlignment = HorizontalAlignment.Left };
    private readonly TextBlock generationUsage = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap };
    private bool generationDirty, generationSync;
    private void BuildGenerationSettings()
    {
        settings.Children.Add(Divider());
        settings.Children.Add(new TextBlock { Text = "生成与用量", Style = PlaygroundAppearance.Style("PGMetadataTextStyle") });
        AddBudgetSlider("最大输出", "tokens", generationOutput);
        AddBudgetSlider("上下文", "字符", generationContext);
        AddBudgetSlider("等待上限", "秒", generationTimeout);
        AutomationProperties.SetAutomationId(generationOutput, "PlaygroundMaximumOutput");
        AutomationProperties.SetAutomationId(generationContext, "PlaygroundMaximumContext");
        AutomationProperties.SetAutomationId(generationTimeout, "PlaygroundTimeout");
        AutomationProperties.SetAutomationId(generationSave, "PlaygroundSaveGeneration");
        PlaygroundAppearance.Apply(generationSave); generationSave.Click += async (_, _) => await RunAsync(SaveGenerationSettingsAsync);
        settings.Children.Add(generationSave); settings.Children.Add(generationUsage);
        var notice = Label("各会话通用。估算费用不等于最终账单。", 12);
        notice.Style = generationUsage.Style = PlaygroundAppearance.Style("PGMetadataTextStyle"); settings.Children.Add(notice);
        ToolTipService.SetToolTip(generationContext, "字符数不等于 token 数，包含工具说明及可读续接内容；超出时停止发送，不自动扩大。");
        ToolTipService.SetToolTip(generationTimeout, "停止前服务商可能已处理请求；生成期间不能更改这些设置。");
    }
    private void AddBudgetSlider(string title, string unit, Slider slider)
    {
        var value = new TextBlock { FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
        var badge = new Border { Child = value, Padding = new Thickness(8, 4, 8, 4), CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1) };
        var header = new Grid { ColumnSpacing = 8 }; header.ColumnDefinitions.Add(new()); header.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var label = Label(title, 13); label.VerticalAlignment = VerticalAlignment.Center; header.Children.Add(label); Grid.SetColumn(badge, 1); header.Children.Add(badge);
        slider.Header = header; PlaygroundAppearance.Apply(slider, "PGSlider");
        void SyncValue() { value.Text = slider.Value.ToString("N0") + " " + unit; }
        slider.ValueChanged += (_, _) => { SyncValue(); if (!generationSync) { generationDirty = true; UpdateActions(); } };
        AutomationProperties.SetName(slider, title + "，" + unit);
        ToolTipService.SetToolTip(badge, slider.Minimum.ToString("N0") + " – " + slider.Maximum.ToString("N0") + " " + unit);
        SyncValue(); settings.Children.Add(slider);
    }
    private void SyncGenerationSettings()
    {
        if (!generationDirty)
        {
            generationSync = true;
            try { var value = service.Snapshot.Settings; generationOutput.Value = value.MaximumOutputTokens; generationContext.Value = value.MaximumContextCharacters; generationTimeout.Value = value.TimeoutSeconds; }
            finally { generationSync = false; }
        }
        var reply = service.Snapshot.Messages.LastOrDefault(m => m.ConversationId == selected && m.Role == "assistant");
        if (reply is null) { generationUsage.Text = "尚无本会话的返回用量。"; ToolTipService.SetToolTip(generationUsage, null); }
        else
        {
            // A model switch changes future requests, never the price identity
            // of the latest reply. Its own saved request costs take precedence.
            var usage = reply.Usage; var cost = PlaygroundPricing.ForMessage(reply);
            generationUsage.Text = "最近回复：输入 " + (usage?.InputTokens?.ToString("N0") ?? "未知")
                + " · 输出 " + (usage?.OutputTokens?.ToString("N0") ?? "未知")
                + (usage?.Searches is { } searches ? " · 搜索 " + searches : "")
                + (usage?.Fetches is { } fetches ? " · 读取网页 " + fetches : "")
                + "\n" + PlaygroundPricing.Format(cost);
            ToolTipService.SetToolTip(generationUsage, cost.Notice);
            AutomationProperties.SetName(generationUsage, generationUsage.Text);
        }
    }
    private async Task SaveGenerationSettingsAsync()
    {
        if (!generationDirty || service.IsGenerating) return;
        var current = service.Snapshot.Settings;
        await service.ConfigureAsync(current with
        {
            MaximumOutputTokens = Number(generationOutput.Value, current.MaximumOutputTokens, 128, 65536),
            MaximumContextCharacters = Number(generationContext.Value, current.MaximumContextCharacters, 2000, 1000000),
            TimeoutSeconds = Number(generationTimeout.Value, current.TimeoutSeconds, 10, 1800)
        }, lifetime.Token);
        generationDirty = false; SyncGenerationSettings();
        notification.Message = "生成设置已保存，后续请求将使用新值。"; notification.Severity = InfoBarSeverity.Informational; notification.IsOpen = true;
    }
}
