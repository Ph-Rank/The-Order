using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using TheOrder.Platform.Windows;
using Windows.System;
using Windows.UI.Core;

namespace TheOrder.WinUI.Views;

public sealed class GuideShortcutInput : UserControl
{
    private readonly StackPanel root = new() { Spacing = 8 };
    private readonly Button record = new() { MinWidth = 148, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
    private bool recording;
    public int Value { get; private set; }
    public GuideShortcutInput(int value)
    {
        Value = value; Content = root;
        AutomationProperties.SetAutomationId(this, "GuideShortcutInput");
        root.Children.Add(new TextBlock { Text = "显示与隐藏快捷键" });
        var row = new TagFlowPanel { HorizontalSpacing = 8, VerticalSpacing = 6 };
        AutomationProperties.SetAutomationId(record, "RecordGuideShortcut");
        AutomationProperties.SetName(record, "录入攻略快捷键");
        ToolTipService.SetToolTip(record, "点击后按下单键或组合键；Esc 取消。默认 J。");
        record.Click += (_, _) => BeginRecording();
        record.PreviewKeyDown += (_, e) =>
        {
            if (!recording) return;
            if (e.Key is VirtualKey.Escape or VirtualKey.Tab) { Cancel(); e.Handled = e.Key == VirtualKey.Escape; return; }
            e.Handled = true;
            if (e.Key is VirtualKey.Control or VirtualKey.Shift or VirtualKey.Menu
                or VirtualKey.LeftControl or VirtualKey.RightControl or VirtualKey.LeftShift or VirtualKey.RightShift
                or VirtualKey.LeftMenu or VirtualKey.RightMenu or VirtualKey.LeftWindows or VirtualKey.RightWindows) return;
            if (Down(VirtualKey.LeftWindows) || Down(VirtualKey.RightWindows)) { Reject(); return; }
            Record((int)e.Key, (Down(VirtualKey.Menu) ? 1 : 0) | (Down(VirtualKey.Control) ? 2 : 0) | (Down(VirtualKey.Shift) ? 4 : 0));
        };
        record.ProcessKeyboardAccelerators += (_, e) => { if (recording) e.Handled = true; };
        record.LostFocus += (_, _) => Cancel();
        row.Children.Add(record);
        var reset = new Button { Content = "恢复 J 默认" };
        AutomationProperties.SetAutomationId(reset, "ResetGuideShortcut");
        reset.Click += (_, _) => { Value = WindowsGuideHotkeys.DefaultKey; Cancel(); };
        row.Children.Add(reset); root.Children.Add(row); root.Children.Add(status); Refresh();
    }
    private static bool Down(VirtualKey key) => InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down);
    private void Refresh() => record.Content = recording ? "请按快捷键…" : WindowsGuideHotkeys.Label(Value);
    private void Cancel() { recording = false; status.Visibility = Visibility.Collapsed; Refresh(); }
    private void Reject() { status.Text = "请选择字母、数字、F1–F11 或符号键，可搭配 Ctrl、Shift、Alt。"; status.Visibility = Visibility.Visible; }
    internal void BeginRecording() { recording = true; status.Visibility = Visibility.Collapsed; record.Focus(FocusState.Programmatic); Refresh(); }
    internal bool Record(int key, int modifiers)
    {
        int next = key | (modifiers << 16);
        if (!WindowsGuideHotkeys.IsSupported(next)) { Reject(); return false; }
        Value = next; Cancel(); return true;
    }
    internal object Evidence => new { value = Value, label = WindowsGuideHotkeys.Label(Value), recording, error = status.Visibility == Visibility.Visible };
}
