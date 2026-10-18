using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using TheOrder.Platform.Windows;
using TheOrder.Application;
using TheOrder.Presentation;

namespace TheOrder.WinUI;

public sealed partial class MainWindow
{
    // Extends the existing marked Test controller. Invoke real XAML controls,
    // not view-model commands; no launch, auth, files or external URL invocation.
    private async Task RunAcgnControlFixtureAsync(JsonElement request)
    {
        if (Program.Profile?.Manifest.ProfileKind != NativeProfileKind.Test || visualFixtureTimer is null || fixtureDirectory is null)
            throw new InvalidOperationException("Isolated Test window required.");
        var id = request.GetProperty("id").GetString() ?? "";
        var mode = request.GetProperty("value").GetString() ?? "";
        bool launchSettings = id is "GlobalLocaleEnabled" or "GlobalMagpieEnabled" or "GlobalMuteEnabled" or "SaveGlobalLaunch" or "CancelGlobalLaunch" or "GlobalLocaleToolSection" or "GlobalMagpieToolSection";
        bool gameSettings = id is "GameLaunchOverrides" or "GameLocaleMode" or "GameMagpieMode" or "GameMuteMode" or "ResetGameLaunchOverrides" or "SaveInstallation" or "CancelInstallation";
        if (ViewModel.Page != (launchSettings ? WorkspacePage.Settings : gameSettings ? WorkspacePage.Installations : WorkspacePage.Details))
            throw new InvalidOperationException("Control does not belong to the active Test page.");
        var allowed = id is "GoBack" or "GameRelatedJump" or "AniListConfirm" or "AniListRetry" or "AniListCharacters" or "AniListStaff" or "AniListRelations"
            or "AniListJumpEpisodes" or "AniListJumpCharacters" or "AniListJumpStaff" or "AniListJumpRelations" or "AniListJumpPV" or "AniListJumpWatch"
            or "ContentLanguageOriginals" or "BangumiEpisodesExpander" or "WatchLinks" or "AnimePV"
            or "MusicAlbumEditions" or "MusicAlbumConfirm" or "MusicAlbumShowTracks"
            or "BookExpand" or "BookRelations" or "BookSearchSection" or "BookSearch" or "BookConfirm" or "BookBack"
            or "BangumiRelatedAnime" or "BangumiRelatedMusic" or "BangumiRelatedBook" or "BangumiRelatedGame";
        allowed |= new[] { "BangumiRelatedSubject", "NextMoeRelatedWork", "AniListCandidate-", "AniListRelation-", "MusicEdition-", "MusicDisc-",
            "MusicTrackDetails-", "MusicTrackRecording-", "MusicTrackWork-", "BookOpen-", "BookPreview-", "BookSaved-", "BangumiEpisodeDescription-", "WatchPlatform-", "MusicArtist-" }
            .Any(prefix => id.StartsWith(prefix, StringComparison.Ordinal));
        allowed |= id is "AniListAddRelated" or "AniListSavePending" or "AniListUserSelections" or "AniListRestoreAutomatic" or "AniListRetryCover"
            or "MusicAlbumAddRelated" or "MusicAlbumSavePending" or "MusicAlbumRestoreAutomatic" or "MusicAlbumRetryCover";
        allowed |= id.StartsWith("AniListUserLink-", StringComparison.Ordinal);
        allowed |= id is "MusicListeningExpand" or "MusicListeningLoad" or "MusicListeningCancel" or "MusicListeningDetails" or "MusicAlbumSources";
        allowed |= id is "BookReadingExpand" or "BookReadingRefresh" or "BookReadingCancel" or "BookReadingSources";
        allowed |= id is "GameVideosExpand" or "GameVideosRefresh" or "GameVideosCancel" or "GameVideosWithdraw" or "GameVideosReset" or "GamePV";
        allowed |= new[] { "GameVideosConfirm-", "GameVideosReject-", "GameVideoPlay-", "GameVideoDefault-", "GameVideoReject-" }.Any(prefix => id.StartsWith(prefix, StringComparison.Ordinal));
        allowed |= id.StartsWith("BookPlatform-", StringComparison.Ordinal);
        allowed |= launchSettings || gameSettings;
        if (!allowed || id.Length > 200 || mode is not ("inspect" or "focus" or "invoke" or "expand" or "collapse" or "toggle" or "select0" or "select1" or "select2")) throw new InvalidDataException("Unsupported bounded control.");
        if ((mode == "toggle" || mode.StartsWith("select", StringComparison.Ordinal)) && !(launchSettings || gameSettings)) throw new InvalidDataException("Only launch settings accept Test value changes.");
        var pending = new Stack<DependencyObject>(); pending.Push(Root); var matches = new List<FrameworkElement>(); int count = 0;
        while (pending.TryPop(out var node))
        {
            if (++count > 8192) throw new InvalidOperationException("Bounded Test tree exceeded.");
            if (node is FrameworkElement element && (element.Visibility != Visibility.Visible || !element.IsLoaded)) continue;
            if (node is FrameworkElement found && AutomationProperties.GetAutomationId(found) == id) matches.Add(found);
            for (int n = VisualTreeHelper.GetChildrenCount(node) - 1; n >= 0; n--) pending.Push(VisualTreeHelper.GetChild(node, n));
        }
        var target = matches.Count == 1 ? matches[0] : throw new InvalidOperationException($"Expected one active control, found {matches.Count}.");
        target.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = false, VerticalAlignmentRatio = .2 });
        await Task.Delay(100); target.UpdateLayout();
        var peer = FrameworkElementAutomationPeer.CreatePeerForElement(target) ?? (target is Button button ? new ButtonAutomationPeer(button) : null)
            ?? throw new InvalidOperationException("Native automation peer unavailable.");
        if (!peer.IsEnabled()) throw new InvalidOperationException("Control disabled.");
        var root = target.XamlRoot.Content;
        bool Hit()
        {
            var point = target.TransformToVisual(root).TransformPoint(new(Math.Min(16, target.ActualWidth / 2), Math.Min(16, target.ActualHeight / 2)));
            return target.ActualWidth > 0 && target.ActualHeight > 0 && VisualTreeHelper.FindElementsInHostCoordinates(point, root).Contains(target);
        }
        // A native scroll/restore may still be settling. Wait for the real hit
        // target, never invoke a covered control or retry an already sent click.
        var hit = Hit();
        for (int n = 0; mode == "invoke" && !hit && n < 25; n++)
        { await Task.Delay(80); target.UpdateLayout(); hit = Hit(); }
        var area = peer.GetBoundingRectangle();
        var before = new { id, mode, name = peer.GetName(), role = peer.GetAutomationControlType().ToString(), keyboardFocusable = peer.IsKeyboardFocusable(), offscreen = peer.IsOffscreen(),
            hit, area.X, area.Y, area.Width, area.Height, contentAcceptsPointer = (target as ButtonBase)?.Content is UIElement child ? child.IsHitTestVisible : (bool?)null };
        if (mode == "focus")
        { if (target is not Control control || !control.Focus(FocusState.Keyboard)) throw new InvalidOperationException("Control cannot receive keyboard focus."); }
        else if (mode == "invoke")
        {
            if (id.StartsWith("WatchPlatform-", StringComparison.Ordinal) || id.StartsWith("BookPlatform-", StringComparison.Ordinal)) throw new InvalidOperationException("External destinations are inspected, not launched by this fixture.");
            if (!hit || peer.GetPattern(PatternInterface.Invoke) is not IInvokeProvider invoke) throw new InvalidOperationException("Active hit target and native Invoke pattern required.");
            invoke.Invoke();
        }
        else if (mode is "expand" or "collapse")
        {
            if (peer.GetPattern(PatternInterface.ExpandCollapse) is not IExpandCollapseProvider expand) throw new InvalidOperationException("Native ExpandCollapse pattern required.");
            if (mode == "expand") expand.Expand(); else expand.Collapse();
        }
        else if (mode == "toggle")
        {
            if (peer.GetPattern(PatternInterface.Toggle) is not IToggleProvider toggle) throw new InvalidOperationException("Native Toggle pattern required.");
            toggle.Toggle();
        }
        else if (mode.StartsWith("select", StringComparison.Ordinal))
        {
            if (target is not ComboBox choice || choice.Items.Count != 3) throw new InvalidOperationException("Three-state native launch choice required.");
            // A bounded control selection, distinguished from physical keyboard input.
            choice.SelectedIndex = mode[^1] - '0';
        }
        await Task.Delay(150);
        await File.WriteAllTextAsync(Path.Combine(fixtureDirectory, "acgn-control-evidence.json"), JsonSerializer.Serialize(new
        { before, focused = target is Control focusable ? focusable.FocusState.ToString() : null, details = detailsPage.BangumiEvidence, page = ViewModel.Page.ToString(),
            globalLaunch = new { launchDefaults.Saved, launchDefaults.HasChanges, launchDefaults.Message },
            gameLaunch = files.Draft is { } draft ? new { draft.LocaleMode, draft.MagpieMode, draft.MuteMode, draft.HelpersSummary } : null,
            interaction = mode.StartsWith("select", StringComparison.Ordinal) ? "Bounded native ComboBox selection; no desktop input" : "Real WinUI automation peer; no desktop input", productionProfileAccess = false }));
        if (ViewModel.Page == WorkspacePage.Details && !detailsPage.HasExclusiveContentBody) throw new InvalidOperationException("Details bodies overlap after native control interaction.");
    }
}
