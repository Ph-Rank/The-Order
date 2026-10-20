using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using TheOrder.Application;
using Windows.Graphics.Imaging;
using Windows.Storage;

namespace TheOrder.WinUI;
public sealed partial class MainWindow
{
    private Task p1FixtureDialog = Task.CompletedTask;
    // Extends the pre-existing bounded Test fixture. No arbitrary paths, processes,
    // authentication, or production commands are accepted by these actions.
    private async Task RunP1FixtureAsync(JsonElement request)
    {
        if (visualFixtureTimer is null || fixtureDirectory is null || Program.Profile?.Manifest.ProfileKind != NativeProfileKind.Test) throw new InvalidOperationException();
        var action = request.GetProperty("action").GetString();
        if (action == "p1PagesInspect")
        {
            if (p1Dialog is not null || ViewModel.IsDirty) throw new InvalidOperationException("Clean Test workspace required.");
            var value = request.GetProperty("value").GetString();
            if (value == "scan-demo")
            {
                var parent = Directory.GetParent(Program.Profile.Root) ?? throw new InvalidOperationException();
                if (parent.Name != "argon-pages-20261014") throw new InvalidOperationException("Bounded visual fixture root required.");
                var scanRoot = Path.Combine(parent.FullName, "synthetic-new-games");
                await Navigate(TheOrder.Presentation.WorkspacePage.Import);
                await files.ScanAsync(scanRoot);
            }
            else if (value == "online-demo")
            {
                await Navigate(TheOrder.Presentation.WorkspacePage.Online);
                online.Query = "CLANNAD";
                await online.SearchAsync();
            }
            else if (value == "library-select-first")
            {
                if (ViewModel.Page != TheOrder.Presentation.WorkspacePage.Library) throw new InvalidOperationException();
                libraryPage.SetSelectionModeTest(true);
                var list = P1Elements(libraryPage).OfType<ListViewBase>().Single(l => l.Name == (ViewModel.Layout == "list" ? "GameList" : "GameGrid"));
                if (list.Items.Count == 0) throw new InvalidOperationException("Test library must contain a row.");
                list.SelectedItems.Add(list.Items[0]);
            }
            else if (value is "settings-api" or "settings-api-edit" or "settings-api-close" or "settings-token" or "settings-token-close")
            {
                if (ViewModel.Page != TheOrder.Presentation.WorkspacePage.Settings) throw new InvalidOperationException();
                settingsPage.RevealSettingsTest(value);
                var controls = P1Elements(settingsPage).OfType<FrameworkElement>().ToArray();
                if (value is "settings-api" or "settings-api-edit" or "settings-api-close")
                {
                    controls.OfType<Expander>().Single(e => e.Name == "ApiKeyPanel").IsExpanded = value != "settings-api-close";
                    settingsPage.UpdateLayout();
                    controls = P1Elements(settingsPage).OfType<FrameworkElement>().ToArray();
                }
                if (value is "settings-token" or "settings-token-close" || value == "settings-api-edit"
                    && controls.OfType<StackPanel>().Single(b => b.Name == "CredentialEditor").Visibility != Visibility.Visible)
                {
                    // Existing editor-open action only: no token input, network authorization or persistence.
                    var button = value == "settings-api-edit" ? controls.OfType<Button>().Single(b => b.Name == "ChangeCredentialButton")
                        : controls.OfType<Button>().Single(b => AutomationProperties.GetAutomationId(b) == (value == "settings-token-close" ? "BangumiCloseEditor" : "BangumiAuthorize"));
                    var peer = FrameworkElementAutomationPeer.CreatePeerForElement(button) ?? new ButtonAutomationPeer(button);
                    if (peer.GetPattern(PatternInterface.Invoke) is not IInvokeProvider invoke) throw new InvalidOperationException();
                    invoke.Invoke();
                }
            }
            else if (value is "settings-top" or "settings-online" or "settings-bangumi" or "settings-backups" or "settings-update"
                or "settings-appearance" or "settings-launch" or "settings-library" or "settings-sync" or "settings-collapse-all")
            {
                if (ViewModel.Page != TheOrder.Presentation.WorkspacePage.Settings) throw new InvalidOperationException();
                settingsPage.RevealSettingsTest(value);
            }
            else if (value == "clear-notice") NoticeBar.IsOpen = false;
            else throw new InvalidDataException("Unknown bounded page inspection.");
            await Task.Delay(200);
        }
        if (action == "p1DetailsInspect")
        {
            // Read-only inspection of the isolated Test page. Invoke existing edit controls
            // without desktop input; this action cannot save a draft or launch a game.
            if (ViewModel.Page != TheOrder.Presentation.WorkspacePage.Details || p1Dialog is not null || ViewModel.IsDirty)
                throw new InvalidOperationException("A clean Test details page is required.");
            var value = request.GetProperty("value").GetString();
            var controls = P1Elements(detailsPage).OfType<FrameworkElement>().ToArray();
            if (value == "clear-notice") NoticeBar.IsOpen = false;
            else if (value == "long-notice")
            {
                // Fixed synthetic text exercises wrapping in the actual control, without
                // changing LaunchViewModel, bindings' source, installations or persistence.
                controls.OfType<TextBlock>().Single(t => AutomationProperties.GetAutomationId(t) == "LaunchMessage").Text =
                    "尚未选定启动程序。请从这部作品的游戏文件夹中选择正确的 exe 或 cmd；如果路径已经移动，请在启动设置中更新位置并重新确认，然后再启动游戏。";
            }
            else if (value is "edit" or "finish")
            {
                var button = controls.OfType<Button>().Single(b => value == "edit"
                    ? AutomationProperties.GetAutomationId(b) == "EditGame"
                    : AutomationProperties.GetName(b) == "结束编辑");
                if (!button.IsEnabled) throw new InvalidOperationException("Action disabled.");
                var peer = FrameworkElementAutomationPeer.CreatePeerForElement(button) ?? new ButtonAutomationPeer(button);
                if (peer.GetPattern(PatternInterface.Invoke) is not IInvokeProvider invoke) throw new InvalidOperationException();
                invoke.Invoke(); await Task.Delay(200);
            }
            else if (value is "top" or "tags" or "characters" or "review" or "review-manual")
            {
                var scroll = controls.OfType<ScrollViewer>().Single(s => s.Name == "DetailsScroll");
                var y = 0d;
                if (value != "top")
                {
                    FrameworkElement heading = value == "review-manual" ? controls.Single(t => t.Name == "ManualActions")
                        : value == "review" ? controls.Single(t => t.Name == "ReviewSection")
                        : controls.OfType<TextBlock>().Single(t => t.Text == (value == "tags" ? "标签" : "角色"));
                    y = heading.TransformToVisual((UIElement)scroll.Content).TransformPoint(new Windows.Foundation.Point()).Y - 12;
                }
                scroll.ChangeView(null, Math.Max(0, y), null, true);
            }
            else throw new InvalidDataException("Unknown read-only details inspection.");
            await Task.Delay(200);
        }
        if (action is "p1ContextMenu" or "p1LaunchSettings" or "p1Recognize")
        {
            var id = request.GetProperty("id").GetString() ?? throw new InvalidDataException();
            if (extras.Snapshot?.Entries.Any(e => e.Id == id) != true || p1Dialog is not null) throw new InvalidDataException();
            if (action == "p1LaunchSettings") await OpenLaunchSettingsAsync(id);
            else
            {
                await Navigate(TheOrder.Presentation.WorkspacePage.Library);
                if (action == "p1ContextMenu") await libraryPage.ShowCardMenuFixtureAsync(id);
                else { recognition.UseOnline = false; recognition.IncludePe = false; await recognition.StartAsync([id], RecognitionMode.Reidentify); }
            }
        }
        if (action == "p1ContextClose") libraryPage.CloseCardMenuFixture();
        if (action == "p1ContextInvoke")
        {
            var text = request.GetProperty("value").GetString();
            if (text is not ("打开详情" or "选择" or "启动设置（LE / Magpie）")) throw new InvalidDataException();
            var item = libraryPage.ContextItemFixture(text); if (!item.IsEnabled) throw new InvalidOperationException();
            var peer = FrameworkElementAutomationPeer.CreatePeerForElement(item) ?? new MenuFlyoutItemAutomationPeer(item);
            if (peer.GetPattern(PatternInterface.Invoke) is not IInvokeProvider invoke) throw new InvalidOperationException();
            invoke.Invoke(); await Task.Delay(600);
        }
        if (action == "p1DismissDiscovery") { if (libraryPage.DismissDiscovery is { } dismiss) await dismiss(); }
        if (action == "p1ReviewDiscovery") { files.ReviewDiscovered(sources.Candidates); await Navigate(TheOrder.Presentation.WorkspacePage.Import); }
        if (action == "p1IgnoreCandidate")
        {
            if (!files.IsSourceDiscovery) throw new InvalidDataException();
            var index = request.GetProperty("value").GetInt32(); if (index < 0 || index >= files.Candidates.Count) throw new InvalidDataException();
            await IgnoreDiscoveredAsync([files.Candidates[index]]);
        }
        if (action == "p1SourceToggle")
        {
            var toggle = P1Elements(p1Dialog).OfType<ToggleSwitch>().Single(t => AutomationProperties.GetAutomationId(t).StartsWith("ShowUnconfirmedSteam:", StringComparison.Ordinal));
            toggle.IsOn = request.GetProperty("value").GetBoolean(); await Task.Delay(100); await p1Operation;
        }
        if (action == "p1Installation")
        {
            var id = request.GetProperty("id").GetString() ?? throw new InvalidDataException();
            if (extras.Snapshot?.Entries.Any(e => e.Id == id) != true) throw new InvalidDataException();
            await installationNavigation.OpenAsync(id, ResolveUnsavedAsync,
                () => Navigate(TheOrder.Presentation.WorkspacePage.Installations));
            files.Edit(files.Installations!.Rows.Single(r => r.Installation.Id == request.GetProperty("value").GetString()));
            await Task.Delay(200);
        }
        if (action == "p1CancelInstallation") { files.CancelDraft(); }
        if (action == "p1ImportSelect")
        {
            if (ViewModel.Page != TheOrder.Presentation.WorkspacePage.Import || p1Dialog is not null) throw new InvalidOperationException();
            var list = P1Elements(importPage).OfType<ListView>().Single(l => AutomationProperties.GetAutomationId(l) == "ScanCandidates");
            var index = request.GetProperty("value").GetInt32();
            if (index < 0 || index >= files.Candidates.Count || !files.Candidates[index].Preflight.CanAttach) throw new InvalidDataException();
            list.SelectedItems.Add(files.Candidates[index]);
        }
        if (action == "p1ImportInvoke")
        {
            if (ViewModel.Page != TheOrder.Presentation.WorkspacePage.Import || p1Dialog is not null) throw new InvalidOperationException();
            var id = request.GetProperty("value").GetString();
            if (id is not ("SelectAllImportGames" or "ClearImportSelection" or "IgnoreSelectedCandidates" or "IgnoreAllCandidates" or "ViewImportedLibrary")) throw new InvalidDataException();
            var button = P1Elements(importPage).OfType<Button>().Single(b => AutomationProperties.GetAutomationId(b) == id);
            if (!button.IsEnabled) throw new InvalidOperationException("Action disabled.");
            var peer = FrameworkElementAutomationPeer.CreatePeerForElement(button) ?? new ButtonAutomationPeer(button);
            if (peer.GetPattern(PatternInterface.Invoke) is not IInvokeProvider invoke) throw new InvalidOperationException();
            invoke.Invoke(); await Task.Delay(250);
        }
        if (action == "p1ScrollInstallation")
        {
            if (files.Draft is null || p1Dialog is not null) throw new InvalidOperationException();
            var id = request.GetProperty("value").GetString();
            if (id is not ("SaveFolderPath" or "SaveInstallation" or "LaunchHelpers")) throw new InvalidDataException();
            installationsPage.ExpandSectionForEvidence(id!);
            var viewElements = P1Elements(installationsPage).OfType<FrameworkElement>().ToArray();
            var target = viewElements.Single(e => AutomationProperties.GetAutomationId(e) == id);
            var scroll = viewElements.OfType<ScrollViewer>().Single(s => s.Content is StackPanel);
            var point = target.TransformToVisual((UIElement)scroll.Content).TransformPoint(new Windows.Foundation.Point());
            scroll.ChangeView(null, Math.Max(0, point.Y - 12), null, true); await Task.Delay(200);
        }
        if (action == "p1Close") { p1Dialog?.Hide(); await p1FixtureDialog; return; }
        if (action == "p1Dialog")
        {
            if (p1Dialog is not null || !p1FixtureDialog.IsCompleted) throw new InvalidOperationException("Dialog already open.");
            var id = request.TryGetProperty("id", out var key) ? key.GetString() : null;
            p1FixtureDialog = request.GetProperty("value").GetString() switch
            {
                "sources" => OpenSourcesAsync(), "statistics" => OpenStatisticsAsync(id),
                "game" when id is not null && extras.Snapshot?.Entries.Any(x => x.Id == id) == true => OpenGameToolsAsync(id),
                _ => throw new InvalidDataException("Unknown bounded P1 view.")
            };
            for (int i = 0; i < 50 && p1Dialog?.IsLoaded != true; i++) { if (p1FixtureDialog.IsCompleted) { await p1FixtureDialog; break; } await Task.Delay(100); }
            if (p1Dialog?.IsLoaded != true) throw new InvalidOperationException("Dialog failed to load.");
        }
        if (action == "p1Tab")
        {
            var pivot = P1Elements(p1Dialog).OfType<Pivot>().Single(); var index = request.GetProperty("value").GetInt32();
            if (index < 0 || index >= pivot.Items.Count) throw new InvalidDataException(); pivot.SelectedIndex = index; await Task.Delay(350);
        }
        if (action == "p1Statistics")
        {
            if (p1Dialog?.Title as string != "游玩统计") throw new InvalidDataException();
            var combos = P1Elements(p1Dialog).OfType<ComboBox>().ToArray();
            var index = request.GetProperty("value").GetInt32(); if (index is < 0 or > 1) throw new InvalidDataException();
            combos.Single(c => c.Header as string == "统计区间").SelectedIndex = index;
            if (request.TryGetProperty("id", out var id))
            {
                var choose = combos.Single(c => c.Header as string == "作品");
                var item = choose.Items.OfType<ComboBoxItem>().Single(c => c.Tag as string == id.GetString()); choose.SelectedItem = item;
            }
            await Task.Delay(200);
        }
        if (action == "p1Fill")
        {
            var controls = P1Elements(p1Dialog).ToArray();
            switch (request.GetProperty("value").GetString())
            {
                case "manual":
                    var hours = request.GetProperty("hours").GetDouble(); var days = request.GetProperty("daysAgo").GetInt32();
                    if (!double.IsFinite(hours) || hours is < 0 or > 8784 || days is < 0 or > 30) throw new InvalidDataException();
                    controls.OfType<CalendarDatePicker>().Single().Date = DateTimeOffset.Now.AddDays(-days);
                    controls.OfType<NumberBox>().Single().Value = hours;
                    controls.OfType<ComboBox>().Single(c => c.Header as string == "方式").SelectedIndex = request.GetProperty("replace").GetBoolean() ? 1 : 0;
                    break;
                case "source":
                    var source = request.GetProperty("source").GetString() ?? "";
                    var number = DirectMetadataIdentity.Normalize(source, request.GetProperty("number").GetString() ?? "");
                    controls.OfType<ComboBox>().Single(c => c.Header as string == "资料源").SelectedIndex = source == "bangumi" ? 0 : 1;
                    controls.OfType<TextBox>().Single(c => c.Header as string == "作品编号或作品链接").Text = number;
                    break;
                default: throw new InvalidDataException();
            }
        }
        if (action == "p1Invoke")
        {
            var id = request.GetProperty("value").GetString() ?? "";
            if (id is not ("SaveManualPlaytime" or "PreviewMetadataSource" or "ApplyMetadataSource" or "ScanLibrarySources" or "IgnoreAllSourceCandidates")
                && !id.StartsWith("RestoreIgnored:", StringComparison.Ordinal)
                && !id.StartsWith("UndoManual:", StringComparison.Ordinal) && id is not ("RemoveSource:bangumi" or "RemoveSource:vndb")) throw new InvalidDataException();
            var button = P1Elements(p1Dialog).OfType<Button>().Single(b => AutomationProperties.GetAutomationId(b) == id);
            if (!button.IsEnabled) throw new InvalidOperationException("Action disabled.");
            var peer = FrameworkElementAutomationPeer.CreatePeerForElement(button) ?? new ButtonAutomationPeer(button);
            if (peer.GetPattern(PatternInterface.Invoke) is not IInvokeProvider invoke) throw new InvalidOperationException();
            invoke.Invoke(); await Task.Delay(200); await p1Operation;
        }
        FrameworkElement? fixtureView = p1Dialog is not null ? p1Dialog : libraryPage.ContextPresenterFixture() is { } presenter ? presenter
            : ViewModel.Page == TheOrder.Presentation.WorkspacePage.Installations ? installationsPage : ViewModel.Page == TheOrder.Presentation.WorkspacePage.Import ? importPage
            : ViewModel.Page == TheOrder.Presentation.WorkspacePage.Details ? detailsPage : PageContent.Content as FrameworkElement ?? libraryPage;
        var elements = P1Elements(fixtureView).OfType<FrameworkElement>().Take(2000).Select(x => new
        {
            type = x.GetType().Name, id = AutomationProperties.GetAutomationId(x), visible = x.Visibility.ToString(),
            width = x.ActualWidth, height = x.ActualHeight,
            isOn = x is ToggleSwitch toggle ? (bool?)toggle.IsOn : null,
            isEnabled = x is Control control ? (bool?)control.IsEnabled : null,
            position = x.TransformToVisual(fixtureView!).TransformPoint(new Windows.Foundation.Point()),
            text = x switch { TextBlock t => t.Text, ContentControl c when c.Content is string s => s, _ => "" }
        }).ToArray();
        await File.WriteAllTextAsync(Path.Combine(fixtureDirectory, "p1-evidence.json"), JsonSerializer.Serialize(new
        {
            dialog = p1Dialog?.Title, loaded = p1Dialog?.IsLoaded, elements,
            entries = extras.Snapshot?.Entries.Length, candidates = sources.Candidates.Length, roots = sources.Sources.Length,
            notifications = libraryPage.NotificationEvidenceFixture(), ignored = sources.Sources.Sum(s => s.IgnoredCandidates.Length),
            sourceSettings = sources.Sources.Select(s => new { s.Id, kind = s.Kind.ToString(), s.ShowUnconfirmedSteam, ignored = s.IgnoredCandidates.Length }),
            sourceDiscovery = files.IsSourceDiscovery, importCandidates = files.Candidates.Count, importSelection = files.SelectedImportCount,
            installationDraft = files.Draft is null ? null : new { files.Draft.SaveFolder, files.Draft.MuteInBackground, files.Draft.UseSteam },
            stats = PlaytimeStatistics.Build(extras.Snapshot?.Entries ?? [], TimeZoneInfo.Local),
            recentShellReady, recentShellFailure,
            testingOnly = true, productionAccess = false
        }));
        if (action != "p1Capture") return;
        var name = request.GetProperty("value").GetString() ?? "";
        if (fixtureView is null || name.Length is 0 or > 80 || name.Any(x => !char.IsAsciiLetterOrDigit(x) && x != '-')) throw new InvalidDataException();
        var path = Path.Combine(fixtureDirectory, name + ".png"); if (File.Exists(path)) throw new InvalidOperationException("Evidence overwrite forbidden.");
        var bitmap = new RenderTargetBitmap(); await bitmap.RenderAsync(fixtureView);
        var pixels = (await bitmap.GetPixelsAsync()).ToArray(); await File.WriteAllBytesAsync(path, []);
        var file = await StorageFile.GetFileFromPathAsync(path); using var stream = await file.OpenAsync(FileAccessMode.ReadWrite);
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, pixels); await encoder.FlushAsync();
    }
    private static IEnumerable<DependencyObject> P1Elements(DependencyObject? root)
    {
        if (root is null) yield break;
        var pending = new Queue<DependencyObject>(); pending.Enqueue(root); int count = 0;
        while (pending.TryDequeue(out var item) && count++ < 2000)
        { yield return item; for (int i = 0; i < VisualTreeHelper.GetChildrenCount(item); i++) pending.Enqueue(VisualTreeHelper.GetChild(item, i)); }
    }
}
