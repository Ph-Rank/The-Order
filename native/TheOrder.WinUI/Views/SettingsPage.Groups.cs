using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace TheOrder.WinUI.Views;

public sealed partial class SettingsPage
{
    private sealed record SettingsCategory(Button Entry, FrameworkElement Panel);

    private readonly SettingsCategory[] categories;
    private SettingsCategory? currentCategory;

    private IEnumerable<Expander> SettingsExpanders =>
        [ApiKeyPanel, SyncSettingsGroup, GlobalLocaleToolSection, GlobalMagpieToolSection, UpdateNotes];

    private void OnCategoryClick(object sender, RoutedEventArgs e) => OpenCategory((Button)sender);

    private void OnSettingsBack(object sender, RoutedEventArgs e) => TryGoBack();

    internal bool TryGoBack()
    {
        if (currentCategory is not { } previous) return false;
        currentCategory = null;
        SettingsScroll.ChangeView(null, 0, null, true);
        previous.Panel.Visibility = Visibility.Collapsed;
        SettingsDetail.Visibility = Visibility.Collapsed;
        InteractionMotion.StopPage(SettingsDetail);
        InteractionMotion.EnterPage(SettingsOverviewScroll);
        DispatcherQueue.TryEnqueue(() =>
        {
            if (IsLoaded && currentCategory is null) previous.Entry.Focus(FocusState.Programmatic);
        });
        return true;
    }

    internal void RevealSources() => OpenCategory(SourcesCategory, NextMoeSection);
    internal void RevealBangumi() => OpenCategory(SourcesCategory, BangumiSection);
    internal void RevealUpdates() => OpenCategory(UpdatesCategory);
    internal void RevealGameAssistant() => OpenCategory(LaunchCategory, GameAssistantSection);

    private void ResizeDetailHeading()
    {
        bool showHelp = currentCategory?.Entry == HelpCategory;
        SettingsDetailHeading.Width = showHelp ? HelpPanel.Width : SettingsColumn.Width;
        var side = (showHelp ? HelpPanel.Width < UserGuideView.CompactWidth : SettingsRoot.ActualWidth < 520) ? 16 : 24;
        SettingsDetailHeading.Padding = new Thickness(side, 0, side, 12);
    }

    private void OpenCategory(Button entry, FrameworkElement? anchor = null)
    {
        var category = categories.First(item => item.Entry == entry);
        if (currentCategory != category)
        {
            if (currentCategory is { } previous) previous.Panel.Visibility = Visibility.Collapsed;
            bool showHelp = entry == HelpCategory;
            if (showHelp) HelpPanel.EnsureContent();
            currentCategory = category;
            SettingsScroll.Visibility = showHelp ? Visibility.Collapsed : Visibility.Visible;
            ResizeDetailHeading();
            category.Panel.Visibility = Visibility.Visible;
            SettingsCategoryTitle.Text = AutomationProperties.GetName(entry);
            SettingsOverviewScroll.Visibility = Visibility.Collapsed;
            InteractionMotion.StopPage(SettingsOverviewScroll);
            InteractionMotion.EnterPage(SettingsDetail);
            SettingsScroll.ChangeView(null, 0, null, true);
            ResizeSettingsControls();
            if (entry == LaunchCategory) RefreshGuideSettings();
            if (entry == BackupCategory) _ = RefreshImageCacheAsync();
        }

        DispatcherQueue.TryEnqueue(() =>
        {
            if (!IsLoaded || currentCategory != category) return;
            ResizeSettingsControls();
            if (anchor is null) SettingsBack.Focus(FocusState.Programmatic);
            else anchor.StartBringIntoView(new BringIntoViewOptions { VerticalAlignmentRatio = 0, AnimationDesired = false });
        });
    }

    // These isolated Test entry points use the same category navigation as the settings buttons.
    internal void RevealSettingsTest(string value)
    {
        switch (value)
        {
            case "settings-collapse-all":
                foreach (var group in SettingsExpanders) group.IsExpanded = false;
                goto case "settings-top";
            case "settings-top":
                TryGoBack();
                SettingsOverviewScroll.ChangeView(null, 0, null, true);
                break;
            case "settings-appearance": OpenCategory(AppearanceCategory); break;
            case "settings-application": OpenCategory(AppearanceCategory, WindowAndTraySection); break;
            case "settings-guide": RevealGameAssistant(); break;
            case "settings-launch": OpenCategory(LaunchCategory); break;
            case "settings-library": OpenCategory(LibraryCategory); break;
            case "settings-backups": OpenCategory(BackupCategory); break;
            case "settings-update": RevealUpdates(); break;
            case "settings-bangumi": RevealBangumi(); break;
            case "settings-sync":
                SyncSettingsGroup.IsExpanded = true;
                OpenCategory(SourcesCategory, SyncSettingsGroup);
                break;
            case "settings-online":
            case "settings-api":
            case "settings-api-edit":
            case "settings-api-close":
            case "settings-token":
            case "settings-token-close": RevealSources(); break;
            default: throw new InvalidDataException("Unknown bounded settings section.");
        }
        UpdateLayout();
    }
}
