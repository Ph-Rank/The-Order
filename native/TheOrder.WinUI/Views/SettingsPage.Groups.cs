using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace TheOrder.WinUI.Views;

public sealed partial class SettingsPage
{
    private IEnumerable<Expander> TopSettingsGroups =>
        [AppearanceGroup, LaunchGroup, SourcesGroup, LibraryGroup, BackupGroup, UpdatesGroup];

    private IEnumerable<Expander> SettingsExpanders =>
        TopSettingsGroups.Concat([ApiKeyPanel, SyncSettingsGroup, GlobalLocaleToolSection, GlobalMagpieToolSection]);

    private void OnSettingsGroupExpanding(Expander sender, ExpanderExpandingEventArgs args)
    {
        ResizeSettingsControls();
        DispatcherQueue.TryEnqueue(ResizeSettingsControls);
    }

    internal void RevealSources() => RevealSettingsGroup(SourcesGroup, NextMoeSection);

    private void RevealSettingsGroup(Expander group, FrameworkElement? anchor = null)
    {
        group.IsExpanded = true;
        DispatcherQueue.TryEnqueue(() =>
        {
            if (!IsLoaded) return;
            ResizeSettingsControls();
            (anchor ?? group).StartBringIntoView(new BringIntoViewOptions { VerticalAlignmentRatio = 0 });
        });
    }

    // The existing isolated Test controller uses the same expand/scroll behavior.
    internal void RevealSettingsTest(string value)
    {
        if (value == "settings-collapse-all")
        {
            foreach (var group in SettingsExpanders) group.IsExpanded = false;
            SettingsScroll.ChangeView(null, 0, null, true);
            return;
        }
        switch (value)
        {
            case "settings-top": SettingsScroll.ChangeView(null, 0, null, true); break;
            case "settings-appearance": RevealSettingsGroup(AppearanceGroup); break;
            case "settings-launch": RevealSettingsGroup(LaunchGroup); break;
            case "settings-library": RevealSettingsGroup(LibraryGroup); break;
            case "settings-backups": RevealSettingsGroup(BackupGroup); break;
            case "settings-update": RevealSettingsGroup(UpdatesGroup); break;
            case "settings-bangumi": RevealSettingsGroup(SourcesGroup, BangumiSection); break;
            case "settings-sync":
                SourcesGroup.IsExpanded = true;
                RevealSettingsGroup(SyncSettingsGroup);
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
