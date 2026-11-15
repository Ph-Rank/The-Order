using Microsoft.UI.Xaml;
using TheOrder.Platform.Windows;

namespace TheOrder.WinUI.Views;

public sealed partial class SettingsPage
{
    internal WindowsWalkthroughSessions? GuideSettings { get; set; }
    internal GuideShortcutInput? GuideShortcut => GuideShortcutHost.Content as GuideShortcutInput;
    private bool savingGuideSettings, synchronizingGuideSettings;

    private void RefreshGuideSettings()
    {
        if (savingGuideSettings || GuideSettings is not { } settings) return;
        GuideShortcutHost.Content ??= new GuideShortcutInput(settings.VirtualKey, ChangeGuideShortcutAsync);
        SynchronizeGuideSettings(settings);
        GuideShortcut?.ShowStatus(settings.CanConfigure ? "" : settings.Message);
    }

    private async void OnGuideEnabledChanged(object sender, RoutedEventArgs e)
    {
        if (!synchronizingGuideSettings && GuideSettings is { } settings && GuideEnabledChoice.IsOn != settings.Enabled)
            await SaveGuideSettingsAsync(GuideEnabledChoice.IsOn, settings.VirtualKey);
    }

    private Task ChangeGuideShortcutAsync(int key) => GuideSettings is { } settings
        ? SaveGuideSettingsAsync(settings.Enabled, key) : Task.CompletedTask;

    private async Task SaveGuideSettingsAsync(bool enabled, int key)
    {
        if (savingGuideSettings || GuideSettings is not { CanConfigure: true } settings || GuideShortcut is not { } shortcut) return;
        savingGuideSettings = true;
        GuideEnabledChoice.IsEnabled = false;
        shortcut.ShowStatus("");
        try
        {
            await settings.ConfigureAsync(enabled, key);
        }
        catch (GuideHotkeyUnavailableException error) { shortcut.ShowStatus(error.Message); }
        catch { shortcut.ShowStatus("游戏内助手设置未保存，请重试。"); }
        finally
        {
            savingGuideSettings = false;
            SynchronizeGuideSettings(settings);
        }
    }

    private void SynchronizeGuideSettings(WindowsWalkthroughSessions settings)
    {
        synchronizingGuideSettings = true;
        try { GuideEnabledChoice.IsOn = settings.Enabled; }
        finally { synchronizingGuideSettings = false; }
        GuideEnabledChoice.IsEnabled = settings.CanConfigure;
        if (GuideShortcut is { } shortcut)
        {
            shortcut.IsEnabled = settings.CanConfigure;
            shortcut.ShowValue(settings.VirtualKey);
        }
    }
}
