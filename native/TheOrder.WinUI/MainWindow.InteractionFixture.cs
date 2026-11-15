using System.Diagnostics;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using TheOrder.Application;
using TheOrder.Domain;
using TheOrder.Presentation;
using TheOrder.WinUI.Views;

namespace TheOrder.WinUI;

public sealed partial class MainWindow
{
    // Actual controls, using the existing isolated Test controller. No production
    // control channel, arbitrary paths, credentials or launch actions.
    private async Task InteractionFixtureAsync(JsonElement request)
    {
        if (Program.Profile?.Manifest.ProfileKind != NativeProfileKind.Test || fixtureDirectory is null || !startupReady)
            throw new InvalidOperationException("Ready isolated Test required");
        if (p1Dialog is not null || ViewModel.IsDirty) throw new InvalidOperationException("Clean workspace required");
        var action = request.GetProperty("value").GetString();
        var frames = new List<double>(); var clock = Stopwatch.StartNew(); double previous = 0;
        EventHandler<object> sample = (_, _) => { var now = clock.Elapsed.TotalMilliseconds; if (previous > 0) frames.Add(now - previous); previous = now; };
        CompositionTarget.Rendering += sample;
        int operations = 0; var swapsBefore = pageSwapCount;
        var railBefore = railAnimationCount;
        object? verified = null;
        try
        {
            if (action == "navigation")
            {
                foreach (var page in new[] { WorkspacePage.Library, WorkspacePage.Settings, WorkspacePage.Library,
                    WorkspacePage.Statistics, WorkspacePage.Library, WorkspacePage.Import, WorkspacePage.Library,
                    WorkspacePage.Online, WorkspacePage.Library })
                {
                    await Navigate(page); await Task.Delay(200); operations++;
                    var before = pageSwapCount; await Navigate(page);
                    if (pageSwapCount != before) throw new InvalidOperationException("Same page replaced twice");
                }
            }
            else if (action is "pagination" or "pagination-retry")
            {
                await Navigate(WorkspacePage.Online);
                var tabs = P1Elements(onlinePage).OfType<SelectorBar>().Single();
                SelectorBarItem Tab(string id) => tabs.Items.Single(item => AutomationProperties.GetAutomationId(item) == id);
                tabs.SelectedItem = Tab("DiscoverySearchTab");
                var retrying = action == "pagination-retry";
                var submitted = retrying ? "motion-retry" : "motion-scroll";
                online.Query = submitted;
                await online.SearchAsync();
                await Task.Delay(650);
                var grid = P1Elements(onlinePage).OfType<GridView>().Single(view => AutomationProperties.GetAutomationId(view) == "OnlineResults");
                var scroll = P1Elements(grid).OfType<ScrollViewer>().First();
                if (online.Items.Count != 20 || !online.CanLoadMore || scroll.ScrollableHeight <= 0 || scroll.VerticalOffset > 1)
                    throw new InvalidOperationException("The first search page did not stay at the top with its next page pending.");
                var input = P1Elements(onlinePage).OfType<TextBox>().Single(box => AutomationProperties.GetAutomationId(box) == "OnlineQuery");
                input.Text = "unsent search draft";
                if (online.Query != submitted) throw new InvalidOperationException("Editing the search box changed the submitted query.");
                scroll.ChangeView(null, scroll.ScrollableHeight, null, true);
                if (retrying)
                {
                    for (var n = 0; online.SearchState != OnlineState.Error && n < 60; n++) await Task.Delay(60);
                    var retry = P1Elements(grid.Footer as ScrollPageFooter).OfType<Button>().Single();
                    if (online.SearchState != OnlineState.Error || online.Items.Count != 20 || retry.Visibility != Visibility.Visible
                        || FrameworkElementAutomationPeer.CreatePeerForElement(retry)?.GetPattern(PatternInterface.Invoke) is not IInvokeProvider invoke)
                        throw new InvalidOperationException("The previous page was not retained and retryable after a failed continuation.");
                    invoke.Invoke();
                }
                for (var n = 0; online.Items.Count < 24 && n < 60; n++) await Task.Delay(60);
                if (online.Items.Count != 24 || online.CanLoadMore || online.Items.Select(item => item.Id).Distinct().Count() != 24)
                    throw new InvalidOperationException("The results grid did not continue the submitted query at its tail.");

                tabs.SelectedItem = Tab("DiscoveryNewsTab");
                var news = P1Elements(onlinePage).OfType<ListView>().FirstOrDefault(view => AutomationProperties.GetAutomationId(view) == "DiscoveryFeedList");
                for (var n = 0; (news is null || news.Items.Count < 4) && n < 60; n++)
                {
                    await Task.Delay(60);
                    news = P1Elements(onlinePage).OfType<ListView>().FirstOrDefault(view => AutomationProperties.GetAutomationId(view) == "DiscoveryFeedList");
                }
                if (news?.Items.Count != 4) throw new InvalidOperationException("A short news page did not automatically fill the viewport.");
                operations = 2; verified = new { searchFirst = 20, searchAfterScroll = 24, newsAfterFill = 4, retrying };
            }
            else if (action == "discovery-loading")
            {
                (operations, verified) = await CheckDiscoveryLoadingAsync();
            }
            else if (action == "status-center")
            {
                (operations, verified) = await CheckStatusCenterAsync();
            }
            else if (action == "page-spacing")
            {
                (operations, verified) = await CheckPageSpacingAsync();
            }
            else if (action == "rail")
            {
                await Navigate(WorkspacePage.Library);
                await Task.Delay(250);
                var animations = railAnimationCount;
                var motionEnabled = InteractionMotion.Enabled;
                foreach (var (page, item) in new[] { (WorkspacePage.Online, OnlineNavigation), (WorkspacePage.Statistics, StatisticsNavigation),
                    (WorkspacePage.Online, OnlineNavigation), (WorkspacePage.Settings, SettingsNavigation), (WorkspacePage.Online, OnlineNavigation) })
                {
                    var before = railAnimationCount;
                    await Navigate(page);
                    await Task.Delay(245);
                    if (!ReferenceEquals(drawnRailTarget, item) || RailIndicator.Visibility != Visibility.Visible || !float.IsFinite(railPosition)
                        || railAnimationCount != before + (motionEnabled ? 1 : 0))
                        throw new InvalidOperationException("Selection indicator lost its target or skipped its transition.");
                    operations++;
                }
                verified = new { motionEnabled, transitions = railAnimationCount - animations };
            }
            else if (action == "accent-markers")
            {
                await Navigate(WorkspacePage.Online);
                var tabs = P1Elements(onlinePage).OfType<SelectorBar>().Single();
                var calendar = tabs.Items.Single(item => AutomationProperties.GetAutomationId(item) == "DiscoveryCalendarTab");
                tabs.SelectedItem = calendar;
                await Task.Delay(350);
                var underline = P1Elements(calendar).OfType<Microsoft.UI.Xaml.Shapes.Rectangle>()
                    .Single(element => element.Name == "PART_SelectionVisual");
                if (RailIndicator.Visibility != Visibility.Visible || RailIndicator.Background is not SolidColorBrush rail
                    || underline.Fill is not SolidColorBrush tab || rail.Color != tab.Color || rail.Opacity != tab.Opacity
                    || rail.Color.A == 0)
                    throw new InvalidOperationException("Navigation and calendar indicators do not share the current accent.");
                verified = new { theme = Root.ActualTheme.ToString(), rail = rail.Color.ToString(),
                    calendar = tab.Color.ToString(), sameBrush = ReferenceEquals(rail, tab), accentSource };
                operations = 1;
            }
            else if (action == "settings-categories")
            {
                await Navigate(WorkspacePage.Settings);
                settingsPage.RevealSettingsTest("settings-top");
                await Task.Delay(220);
                T Named<T>(string name) where T : FrameworkElement =>
                    settingsPage.FindName(name) as T ?? throw new InvalidOperationException("Missing settings element: " + name);
                var overview = Named<ScrollViewer>("SettingsOverviewScroll");
                var detail = Named<Grid>("SettingsDetail");
                var back = Named<Button>("SettingsBack");
                var title = Named<TextBlock>("SettingsCategoryTitle");
                var list = Named<StackPanel>("SettingsCategories");
                string[] categories = ["Appearance", "Launch", "Library", "Sources", "Backup", "Updates", "Help"];
                string[] primaryControls = ["ThemeChoice", "LaunchDefaultsSection", "ManageLibrarySources",
                    "NextMoeSection", "ImageCacheSection", "UpdateSection", "ArticleScroll"];
                var panels = categories.Select(name => Named<FrameworkElement>(name + "Panel")).ToArray();
                var entries = categories.Select(name => Named<Button>(name + "Category")).ToArray();
                var groups = list.Children.OfType<StackPanel>().ToArray();
                if (groups.Length != 2 || !P1Elements(groups[0]).OfType<Button>().SequenceEqual(entries.Take(3))
                    || !P1Elements(groups[1]).OfType<Button>().SequenceEqual(entries.Skip(3)))
                    throw new InvalidOperationException("Settings should list Help after Updates in the second group.");
                if (P1Elements(Named<Grid>("SettingsHeading")).OfType<Button>()
                    .Any(button => AutomationProperties.GetAutomationId(button) == "OpenUserGuide"))
                    throw new InvalidOperationException("The help entry still appears in the settings heading.");

                var page = Named<Grid>("SettingsOverviewColumn");
                var minRowHeight = entries.Min(entry => entry.ActualHeight);
                var rowWidth = entries[0].ActualWidth;
                var origins = entries.Select(entry => entry.TransformToVisual(list).TransformPoint(new())).ToArray();
                if (page.ActualWidth > page.MaxWidth + .5 || minRowHeight < 57 || rowWidth <= 0
                    || origins.Where((point, index) => Math.Abs(point.X - origins[0].X) > .5
                        || Math.Abs(entries[index].ActualWidth - rowWidth) > .5
                        || Math.Abs(entries[index].ActualHeight - minRowHeight) > .5
                        || (index > 0 && point.Y < origins[index - 1].Y + entries[index - 1].ActualHeight - .5)).Any()
                    || origins[^1].Y + entries[^1].ActualHeight > list.ActualHeight + .5
                    || origins[0].X + rowWidth > list.ActualWidth + .5)
                    throw new InvalidOperationException("Settings entries do not fit their reading column.");

                foreach (var entry in entries)
                {
                    var row = (Grid)entry.Content;
                    var label = P1Elements(row.Children[1]).OfType<TextBlock>().First();
                    var iconBrushes = row.Children[0] is FontIcon glyph
                        ? new[] { glyph.Foreground }
                        : P1Elements(row.Children[0]).OfType<Microsoft.UI.Xaml.Shapes.Path>().Select(path => path.Stroke).ToArray();
                    if (label.Foreground is not SolidColorBrush textBrush || iconBrushes.Length == 0
                        || iconBrushes.Any(brush => brush is not SolidColorBrush iconBrush || iconBrush.Color != textBrush.Color))
                        throw new InvalidOperationException("Settings category icons do not match the neutral text color.");
                }
                var narrowScroll = overview.ScrollableHeight > 8;
                var originalMute = launchDefaults.BackgroundMuteEnabled;
                var maxScrollDrift = 0d;

                static void ClickButton(Button button) =>
                    ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();

                for (int index = 0; index < entries.Length; index++)
                {
                    var entry = entries[index];
                    entry.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = false });
                    await Task.Delay(65);
                    var offset = overview.VerticalOffset;
                    ClickButton(entry);
                    await Task.Delay(220);
                    var primary = P1Elements(panels[index]).OfType<FrameworkElement>().Single(x =>
                        x.Name == primaryControls[index] || AutomationProperties.GetAutomationId(x) == primaryControls[index]);
                    if (detail.Visibility != Visibility.Visible || overview.Visibility != Visibility.Collapsed
                        || title.Text != AutomationProperties.GetName(entry) || panels.Count(x => x.Visibility == Visibility.Visible) != 1
                        || panels[index].Visibility != Visibility.Visible || primary.ActualWidth <= 0)
                        throw new InvalidOperationException("Settings category did not open its own controls: " + entry.Name);
                    if (categories[index] == "Appearance" &&
                        !P1Elements(panels[index]).Contains(Named<ComboBox>("CloseActionChoice")))
                        throw new InvalidOperationException("Window closing preferences are missing from General.");

                    if (categories[index] == "Launch")
                    {
                        var mute = P1Elements(panels[index]).OfType<ToggleSwitch>()
                            .Single(toggle => AutomationProperties.GetAutomationId(toggle) == "GlobalMuteEnabled");
                        mute.IsOn = !originalMute;
                        if (!launchDefaults.HasChanges) throw new InvalidOperationException("Changing a launch setting did not create a draft.");
                    }

                    if (index % 2 == 0) ClickButton(back);
                    else await GoBackAsync();
                    await Task.Delay(220);
                    if (overview.Visibility != Visibility.Visible || detail.Visibility != Visibility.Collapsed
                        || !ReferenceEquals(FocusManager.GetFocusedElement(settingsPage.XamlRoot), entry))
                        throw new InvalidOperationException("Settings back did not restore the selected category.");
                    maxScrollDrift = Math.Max(maxScrollDrift, Math.Abs(overview.VerticalOffset - offset));
                    operations += 2;
                }
                if (maxScrollDrift > 2) throw new InvalidOperationException("Settings overview scrolled away from the selected entry.");
                settingsPage.RevealSources();
                await Task.Delay(220);
                var sources = Array.IndexOf(categories, "Sources");
                if (panels[sources].Visibility != Visibility.Visible || title.Text != AutomationProperties.GetName(entries[sources]))
                    throw new InvalidOperationException("The library source shortcut did not open its settings category.");
                await GoBackAsync();
                await Task.Delay(220);
                settingsPage.RevealSettingsTest("settings-launch");
                await Task.Delay(220);
                if (!launchDefaults.HasChanges || launchDefaults.BackgroundMuteEnabled == originalMute)
                    throw new InvalidOperationException("Category navigation lost the unsaved launch settings.");
                launchDefaults.Cancel();
                settingsPage.RevealGameAssistant();
                await Task.Delay(220);
                var viewport = Named<ScrollViewer>("SettingsScroll");
                var assistant = Named<Border>("GameAssistantSection");
                var assistantY = assistant.TransformToVisual(viewport).TransformPoint(new()).Y;
                if (panels[1].Visibility != Visibility.Visible || settingsPage.GuideShortcut is null
                    || assistantY < -.5 || assistantY + assistant.ActualHeight > viewport.ActualHeight + .5)
                    throw new InvalidOperationException("The game assistant shortcut did not reveal its controls in Play.");
                await GoBackAsync();
                verified = new { groups = groups.Length, rowWidth, minRowHeight, narrowScroll, maxScrollDrift, categories = entries.Length,
                    helpInCategories = true, neutralIcons = true, sourceShortcut = true, assistantShortcut = true, launchDraft = true };
            }
            else if (action == "user-guide")
            {
                await Navigate(WorkspacePage.Settings);
                settingsPage.RevealSettingsTest("settings-top");
                await Task.Delay(220);
                T Named<T>(FrameworkElement scope, string name) where T : FrameworkElement =>
                    scope.FindName(name) as T ?? throw new InvalidOperationException("Missing guide control: " + name);
                static void Click(Button button) =>
                    ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();

                var entry = Named<Button>(settingsPage, "HelpCategory");
                var guide = Named<UserGuideView>(settingsPage, "HelpPanel");
                var overview = Named<ScrollViewer>(settingsPage, "SettingsOverviewScroll");
                var settingsScroll = Named<ScrollViewer>(settingsPage, "SettingsScroll");
                entry.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = false });
                await Task.Delay(65);
                Click(entry);
                await Task.Delay(240);
                var rail = Named<Border>(guide, "ChapterRail");
                var list = Named<ListView>(guide, "ChapterList");
                var picker = Named<SelectedFirstComboBox>(guide, "CompactChapters");
                var title = Named<TextBlock>(guide, "ArticleTitle");
                var article = Named<MarkdownView>(guide, "ArticleBody");
                var scroll = Named<ScrollViewer>(guide, "ArticleScroll");
                bool compact = guide.ActualWidth < UserGuideView.CompactWidth;
                if (guide.Visibility != Visibility.Visible || overview.Visibility != Visibility.Collapsed
                    || settingsScroll.Visibility != Visibility.Collapsed
                    || rail.Visibility != (compact ? Visibility.Collapsed : Visibility.Visible)
                    || picker.Visibility != (compact ? Visibility.Visible : Visibility.Collapsed)
                    || title.Visibility != (compact ? Visibility.Collapsed : Visibility.Visible)
                    || scroll.ViewportWidth <= 0 || scroll.ViewportHeight <= 0
                    || list.Items.Count != 12 || picker.PrimaryOptions.Count != 12
                    || list.ItemsSource is not Microsoft.UI.Xaml.Data.ICollectionView { CollectionGroups.Count: 3 })
                    throw new InvalidOperationException("The guide did not open with its article and adaptive chapter directory.");

                var toWindow = guide.TransformToVisual(null);
                double readerY = guide.ActualHeight - scroll.ActualHeight / 2;
                double dividerX = compact ? 0 : rail.ActualWidth;
                bool HitsViewport(ScrollViewer viewport, double x) =>
                    VisualTreeHelper.FindElementsInHostCoordinates(
                        toWindow.TransformPoint(new Windows.Foundation.Point(x, readerY)), guide).Contains(viewport);

                if (!HitsViewport(scroll, dividerX + 8) || !HitsViewport(scroll, guide.ActualWidth - 8))
                    throw new InvalidOperationException("The article viewport did not cover its side insets.");
                if (!compact)
                {
                    var directory = P1Elements(list).OfType<ScrollViewer>().First();
                    if (!HitsViewport(directory, 8) || !HitsViewport(directory, dividerX - 8))
                        throw new InvalidOperationException("The chapter viewport did not cover its side insets.");
                }

                void Select(int index)
                {
                    if (compact) picker.OptionIndex = index;
                    else list.SelectedIndex = index;
                }

                Select(1);
                await Task.Delay(200);
                scroll.ChangeView(null, scroll.ScrollableHeight, null, true);
                await Task.Delay(80);
                var scrollAtEnd = scroll.VerticalOffset;
                if (scrollAtEnd < 1) throw new InvalidOperationException("The longer help article did not scroll.");
                Select(0);
                await Task.Delay(200);
                if (scroll.VerticalOffset > .5) throw new InvalidOperationException("The next chapter did not start at the top.");

                var figureNames = new[] { "shelf", "import", "details", null, "discovery", "game-tools",
                    "statistics", "chat", null, "backup", null, null };
                int figures = 0;
                for (int i = 0; i < list.Items.Count; i++)
                {
                    Select(i);
                    if (list.Items[i] is not UserGuideChapter chapter || title.Text != chapter.Title
                        || !ReferenceEquals(picker.SelectedOption, chapter)
                        || article.Children.Count == 0 || string.IsNullOrWhiteSpace(article.Text))
                        throw new InvalidOperationException("Could not read guide chapter " + (i + 1));

                    if (figureNames[i] is not { } name) continue;
                    var figure = article.Children.OfType<Border>().Select(border => border.Child).OfType<Image>()
                        .SingleOrDefault(image => AutomationProperties.GetAutomationId(image) == "UserGuideFigure:" + name);
                    if (figure is null || string.IsNullOrWhiteSpace(AutomationProperties.GetName(figure)))
                        throw new InvalidOperationException("Missing accessible guide figure: " + name);
                    for (int retry = 0; retry < 30 && figure.Source is null; retry++) await Task.Delay(40);
                    if (figure.Source is not Microsoft.UI.Xaml.Media.Imaging.BitmapImage { PixelWidth: > 0 }
                        || figure.ActualWidth > scroll.ViewportWidth + 1
                        || figure.ActualWidth > figure.MaxWidth + 1)
                        throw new InvalidOperationException("Could not display guide figure: " + name);
                    figures++;
                }

                Select(0);
                await Task.Delay(240);
                var links = P1Elements(article).OfType<RichTextBlock>()
                    .SelectMany(text => text.Blocks.OfType<Microsoft.UI.Xaml.Documents.Paragraph>())
                    .SelectMany(paragraph => paragraph.Inlines.OfType<Microsoft.UI.Xaml.Documents.Hyperlink>())
                    .ToArray();
                if (request.TryGetProperty("captureGuide", out var captureGuide) && captureGuide.ValueKind == JsonValueKind.True)
                {
                    string layout = compact ? "compact" : "wide";
                    await CaptureRound3Async("guide-start-" + layout);
                    if (article.Children.OfType<Border>().FirstOrDefault(border => border.Child is Image) is { } figure)
                    {
                        var y = figure.TransformToVisual(scroll).TransformPoint(new Windows.Foundation.Point(0, 0)).Y;
                        scroll.ChangeView(null, scroll.VerticalOffset + y - 22, null, true);
                        await Task.Delay(160);
                        await CaptureRound3Async("guide-shelf-" + layout);
                    }
                    if (article.Children.OfType<ScrollViewer>().FirstOrDefault() is { } table)
                    {
                        var y = table.TransformToVisual(scroll).TransformPoint(new Windows.Foundation.Point(0, 0)).Y;
                        scroll.ChangeView(null, scroll.VerticalOffset + y - 50, null, true);
                        await Task.Delay(160);
                        await CaptureRound3Async("guide-links-" + layout);
                    }
                }
                if (links.Length != 5 || article.ResolveArticleLink?.Invoke("#攻略与游戏内助手") is not { } jump)
                    throw new InvalidOperationException($"Expected five guide links, found {links.Length}.");
                jump();
                if (title.Text != "攻略与游戏内助手" || list.SelectedIndex != 5 || picker.OptionIndex != 5)
                    throw new InvalidOperationException("Guide link did not select its chapter.");

                Click(Named<Button>(settingsPage, "SettingsBack"));
                await Task.Delay(240);
                if (overview.Visibility != Visibility.Visible || guide.Visibility != Visibility.Collapsed
                    || !ReferenceEquals(FocusManager.GetFocusedElement(settingsPage.XamlRoot), entry))
                    throw new InvalidOperationException("Returning from help did not restore the settings entry.");

                Click(entry);
                Select(1);
                await Task.Delay(240);
                operations = list.Items.Count + figures + 6;
                verified = new { chapters = list.Items.Count, figures, links = links.Length, groups = 3, compact, width = guide.ActualWidth,
                    viewportWidth = scroll.ViewportWidth, viewportHeight = scroll.ViewportHeight, scrollAtEnd,
                    theme = Root.ActualTheme.ToString(), dpi = Root.XamlRoot.RasterizationScale };
            }
            else if (action is "disclosures" or "rapid")
            {
                await Navigate(WorkspacePage.Settings);
                settingsPage.RevealSettingsTest("settings-launch");
                await Task.Delay(200);
                var groups = P1Elements(settingsPage).OfType<SmoothExpander>()
                    .Where(x => x.Name is "GlobalLocaleToolSection" or "GlobalMagpieToolSection").ToArray();
                if (groups.Length != 2) throw new InvalidOperationException("Launch settings disclosures unavailable");
                foreach (var group in groups)
                {
                    int rounds = action == "rapid" ? 5 : 2;
                    for (int i = 0; i < rounds; i++)
                    {
                        group.IsExpanded = true; await Task.Delay(action == "rapid" ? 25 : 400);
                        group.IsExpanded = false; await Task.Delay(action == "rapid" ? 25 : 250); operations += 2;
                    }
                    await Task.Delay(400);
                    if (group.IsExpanded || group.ContentShown) throw new InvalidOperationException("Collapsed content remained visible");
                }
            }
            else if (action == "settings-reflow")
            {
                await Navigate(WorkspacePage.Settings);
                settingsPage.RevealSettingsTest("settings-collapse-all");
                settingsPage.RevealSettingsTest("settings-launch");
                await Task.Delay(300);
                var scroll = P1Elements(settingsPage).OfType<ScrollViewer>().Single(x => x.Name == "SettingsScroll");
                var group = P1Elements(settingsPage).OfType<SmoothExpander>().Single(x => x.Name == "GlobalLocaleToolSection");
                var next = P1Elements(settingsPage).OfType<SmoothExpander>().Single(x => x.Name == "GlobalMagpieToolSection");
                var motionEnabled = InteractionMotion.Enabled;
                var transitions = new List<object>();
                foreach (var expand in new[] { true, false })
                {
                    if (!expand)
                    {
                        if (scroll.ScrollableHeight < 8) throw new InvalidOperationException("The settings fixture needs a shorter window.");
                        scroll.ChangeView(null, scroll.ScrollableHeight, null, true);
                        await Task.Delay(100);
                    }

                    var geometry = new List<(double Height, double Offset, double Gap)>();
                    void ReadFrame() => geometry.Add((group.ActualHeight, scroll.VerticalOffset,
                        next.TransformToVisual(group).TransformPoint(new(0, 0)).Y - group.ActualHeight));
                    EventHandler<object> record = (_, _) => ReadFrame();
                    ReadFrame();
                    CompositionTarget.Rendering += record;
                    try { group.IsExpanded = expand; await Task.Delay(330); }
                    finally { CompositionTarget.Rendering -= record; }
                    ReadFrame();

                    var before = geometry[0];
                    var after = geometry[^1];
                    var min = Math.Min(before.Height, after.Height);
                    var max = Math.Max(before.Height, after.Height);
                    var intermediate = geometry.Select(x => x.Height).Where(x => x > min + 2 && x < max - 2)
                        .Select(x => Math.Round(x)).Distinct().Count();
                    var gapDrift = geometry.Max(x => Math.Abs(x.Gap - before.Gap));
                    var heightStep = geometry.Zip(geometry.Skip(1), (a, b) => Math.Abs(a.Height - b.Height)).Max();
                    var offsetStep = geometry.Zip(geometry.Skip(1), (a, b) => Math.Abs(a.Offset - b.Offset)).Max();
                    var offsetChange = Math.Abs(before.Offset - after.Offset);
                    var motionPass = motionEnabled
                        ? intermediate >= 3 && heightStep < (max - min) * .6
                            && (offsetChange < 8 || offsetStep < offsetChange * .8)
                        : intermediate == 0;
                    var passed = max - min > 80 && gapDrift < 2 && group.ContentShown == expand && motionPass;
                    transitions.Add(new { expand, before = new { before.Height, before.Offset, before.Gap },
                        after = new { after.Height, after.Offset, after.Gap }, intermediate, heightStep, offsetStep, gapDrift, passed });
                    await File.WriteAllTextAsync(Path.Combine(fixtureDirectory, "settings-reflow.json"),
                        JsonSerializer.Serialize(new { motionEnabled, transitions }));
                    if (!passed) throw new InvalidOperationException("The settings panel and following section did not reflow together.");
                    operations++;
                }
                verified = new { motionEnabled, transitions };
            }
            else if (action == "retained-content")
            {
                var markdown = P1Elements(PageContent).OfType<MarkdownView>().FirstOrDefault();
                if (markdown is null) throw new InvalidOperationException("Detail Markdown required");
                var before = markdown.RenderCount;
                markdown.Text = markdown.Text; markdown.Visibility = Visibility.Collapsed; markdown.Visibility = Visibility.Visible;
                await Task.Delay(250); operations = 3;
                if (markdown.RenderCount != before) throw new InvalidOperationException("Unchanged Markdown rebuilt");
            }
            else throw new InvalidDataException("Unknown interaction check");
        }
        finally { CompositionTarget.Rendering -= sample; }
        var intervals = frames.Order().ToArray();
        await File.AppendAllTextAsync(Path.Combine(fixtureDirectory, "interaction-evidence.jsonl"), JsonSerializer.Serialize(new {
            action, operations, elapsedMs=clock.Elapsed.TotalMilliseconds, pageSwaps=pageSwapCount-swapsBefore,
            railAnimations = railAnimationCount - railBefore, verified,
            renderedFrames=frames.Count, medianIntervalMs=intervals.Length == 0 ? 0 : intervals[intervals.Length/2],
            longestIntervalMs=intervals.LastOrDefault(), gapsOver50Ms=frames.Count(x=>x>50),
            samePageReplacementPrevented=true, nativeTemplatesRetained=true,
            measurement="WinUI Rendering callback intervals; not system PresentMode or an end-to-end frame-rate claim",
            isolatedTest=true, productionAccess=false, passed=true })+"\n");
    }

    private async Task<(int Operations, object Verified)> CheckPageSpacingAsync()
    {
        var positions = new Dictionary<string, double>();

        async Task Check(string label, FrameworkElement heading, bool back = false, double sectionInset = 0)
        {
            await Task.Delay(180);
            var pageTop = Top(PageContent);
            var headingTop = Top(heading);
            var backVisible = PageNavigationToolbar.Visibility == Visibility.Visible && RailBack.ActualHeight >= 32;
            if (!heading.IsLoaded || heading.ActualHeight <= 0 || backVisible != back
                || Math.Abs(pageTop - (back ? 68 : 24)) > 2
                || (back && Math.Abs(Top(RailBack) - 24) > 2)
                || headingTop < pageTop - 1 || headingTop > pageTop + sectionInset + 1)
                throw new InvalidOperationException($"{label} has an unexpected top inset (page={pageTop:F1}, heading={headingTop:F1}, back={backVisible}).");
            positions.Add(label, Math.Round(headingTop, 1));
        }

        double Top(FrameworkElement element) => element.TransformToVisual(WorkspaceSurface)
            .TransformPoint(new Windows.Foundation.Point(0, 0)).Y;

        static T Named<T>(FrameworkElement page, string name) where T : FrameworkElement =>
            page.FindName(name) as T ?? throw new InvalidOperationException("Missing page element: " + name);

        await Navigate(WorkspacePage.Library);
        await Check("library", Named<StackPanel>(libraryPage, "HeaderIdentity"), sectionInset: 8);
        var game = ViewModel.Items.First();
        await Navigate(WorkspacePage.Details, game.Id);
        await Check("details", Named<Grid>(detailsPage, "TopHeaderBar"), back: true);
        await detailsPage.ManageInstallations!();
        await Check("installations", (FrameworkElement)((Grid)installationsPage.Content).Children[0], back: true);
        await GoBackAsync();
        if (ViewModel.Page != WorkspacePage.Details || ViewModel.SelectedId != game.Id)
            throw new InvalidOperationException("Back did not return to the selected work.");
        await GoBackAsync();
        if (ViewModel.Page != WorkspacePage.Library || RailBack.Visibility != Visibility.Collapsed)
            throw new InvalidOperationException("Back did not return to the top-level library.");

        await Navigate(WorkspacePage.Import);
        await Check("import", (FrameworkElement)((Grid)importPage.Content).Children[0], back: true);
        await Navigate(WorkspacePage.Statistics);
        await Check("statistics", Named<Grid>(statisticsPage, "StatisticsHeader"));
        await Navigate(WorkspacePage.Playground);
        await Check("playground", (FrameworkElement)((PlaygroundView)PageContent.Content).Content);

        await Navigate(WorkspacePage.Settings);
        settingsPage.RevealSettingsTest("settings-top");
        var settingsOverview = Named<Grid>(settingsPage, "SettingsOverviewColumn");
        await Check("settings", (FrameworkElement)settingsOverview.Children[0]);
        settingsPage.RevealSettingsTest("settings-appearance");
        await Check("settingsDetail", Named<Button>(settingsPage, "SettingsBack"));
        settingsPage.TryGoBack();
        var help = Named<Button>(settingsPage, "HelpCategory");
        ((IInvokeProvider)new ButtonAutomationPeer(help).GetPattern(PatternInterface.Invoke)).Invoke();
        await Check("help", Named<Button>(settingsPage, "SettingsBack"));
        settingsPage.TryGoBack();

        await Navigate(WorkspacePage.Online);
        var tabs = Named<SelectorBar>(onlinePage, "DiscoveryTabs");
        tabs.SelectedItem = tabs.Items[0];
        await Check("discovery", tabs);
        var search = Named<StackPanel>(onlinePage, "SearchHeader");
        var searchArea = Named<Grid>(onlinePage, "SearchResults");
        await Check("search", search, sectionInset: tabs.ActualHeight + 154);
        var searchOrigin = search.TransformToVisual(searchArea).TransformPoint(new Windows.Foundation.Point(0, 0));
        var searchGap = Math.Clamp((searchArea.ActualHeight - search.ActualHeight - 16) / 4, 0, 144);
        if (Math.Abs(searchOrigin.Y - searchGap) > 2
            || Math.Abs(searchOrigin.X + search.ActualWidth / 2 - searchArea.ActualWidth / 2) > 2)
            throw new InvalidOperationException($"Search landing does not fit the available space (inset={searchOrigin.Y:F1}, expected={searchGap:F1}).");
        for (var index = 1; index < tabs.Items.Count; index++)
        {
            tabs.SelectedItem = tabs.Items[index];
            var feed = (DiscoveryFeedView)Named<ContentControl>(onlinePage, "DiscoveryFeedHost").Content;
            await Check(AutomationProperties.GetAutomationId(tabs.Items[index]), Named<TextBlock>(feed, "Heading"),
                sectionInset: tabs.ActualHeight + 12);
        }

        await Navigate(WorkspacePage.Status);
        var title = P1Elements(statusPage).OfType<TextBlock>().First(text => AutomationProperties.GetHeadingLevel(text) == AutomationHeadingLevel.Level1);
        await Check("status", title);
        var railBottom = Top(StatusNavigation) + StatusNavigation.ActualHeight;
        if (StatusNavigation.ActualHeight < 36 || railBottom > WorkspaceSurface.ActualHeight + 1
            || !ReferenceEquals(Navigation.SelectedItem, StatusNavigation))
            throw new InvalidOperationException("The status entry did not fit in the navigation rail.");

        return (positions.Count, new { positions, theme = Root.ActualTheme.ToString(), dpi = Root.XamlRoot.RasterizationScale,
            width = WorkspaceSurface.ActualWidth, height = WorkspaceSurface.ActualHeight, backRoundTrip = true, statusRailVisible = true });
    }

    private async Task<(int Operations, object Verified)> CheckStatusCenterAsync()
    {
        if (scenario is null || fixtureDirectory is null) throw new InvalidOperationException("Isolated scan fixture required.");
        await Navigate(WorkspacePage.Status);
        await Navigate(WorkspacePage.Library);
        if (TitleBarHasExtraContent)
            throw new InvalidOperationException("The title bar has extra content before receiving status messages.");
        await updates.CheckAsync();
        if (updates.Latest?.Version is not ("1.8.0" or "1.9.0-beta.1") || !statusPage.HasNotice("app-update"))
            throw new InvalidOperationException("The synthetic available update did not reach Status.");
        if (!await ViewModel.ReloadAsync())
            throw new InvalidOperationException("The synthetic library could not be refreshed.");
        statusPage.Post(new("合成通知", "普通警告", InfoBarSeverity.Warning));
        statusPage.SetTask("status-fixture", "合成任务", "正在处理");
        if (statusPage.HasUnreadAttention || StatusBadge.Visibility != Visibility.Collapsed)
            throw new InvalidOperationException("A refresh, optional update, passive warning, or background task marked the rail.");
        statusPage.ClearTask("status-fixture");
        statusPage.SetNotice("status-fixture", new("合成操作", "需要核对", RequiresAction: true));
        if (!statusPage.HasUnreadAttention || StatusBadge.Visibility != Visibility.Visible)
            throw new InvalidOperationException("A required action did not mark the rail.");
        await Navigate(WorkspacePage.Status);
        await Task.Delay(180);
        if (statusPage.HasUnreadAttention || StatusBadge.Visibility != Visibility.Collapsed)
            throw new InvalidOperationException("Opening Status did not acknowledge the required action.");
        if (!P1Elements(statusPage).OfType<InfoBar>().Any(bar => bar.Title == "书架" && bar.Severity == InfoBarSeverity.Success))
            throw new InvalidOperationException("Status did not retain the library refresh message.");
        statusPage.SetNotice("status-fixture", null);

        await Navigate(WorkspacePage.Library);
        ViewModel.ReportUnexpected();
        ShowLifecycleError("合成验证：应用错误。");
        if (StatusBadge.Visibility != Visibility.Visible || !statusPage.HasUnreadAttention
            || P1Elements(Root).OfType<InfoBar>().Any(bar => bar.IsOpen))
            throw new InvalidOperationException("A global notice appeared outside Status or did not mark the rail.");

        await Navigate(WorkspacePage.Status);
        await Task.Delay(180);
        if (!ReferenceEquals(Navigation.SelectedItem, StatusNavigation) || statusPage.ActualWidth <= 0
            || statusPage.HasUnreadAttention || StatusBadge.Visibility != Visibility.Collapsed
            || !P1Elements(statusPage).OfType<InfoBar>().Any(bar => bar.IsOpen
                && bar.Title == "书架" && bar.Severity == InfoBarSeverity.Error && bar.ActualHeight > 0))
            throw new InvalidOperationException("Status did not display and acknowledge the notice.");

        InvokeStatusAction("ClearStatusMessages");
        await Task.Delay(120);
        if (!statusPage.HasNotice("app-update")
            || P1Elements(statusPage).OfType<InfoBar>().Any(bar => bar.Title is "书架" or "程序运行"))
            throw new InvalidOperationException("Clearing message history affected a current reminder or kept an old error.");

        InvokeStatusAction("StatusOpenUpdates");
        await Task.Delay(180);
        if (ViewModel.Page != WorkspacePage.Settings ||
            !P1Elements(settingsPage).OfType<TextBlock>().Any(label =>
                label.Name == "SettingsCategoryTitle" && label.Text == "更新与反馈"))
            throw new InvalidOperationException("The reminder did not open Update and feedback.");

        await Navigate(WorkspacePage.Import);
        scenario.DelayScan();
        var scan = files.ScanAsync(fixtureDirectory);
        try
        {
            await Navigate(WorkspacePage.Status);
            await Task.Delay(160);
            if (scenario.Status != "scan-held" || !statusPage.HasTask("import-scan") || !files.IsScanning)
                throw new InvalidOperationException("The scan did not remain visible after leaving Import.");
            InvokeStatusAction("CancelStatusScan");
            await scan.WaitAsync(TimeSpan.FromSeconds(3));
            if (statusPage.HasTask("import-scan") || files.Stage != ImportStage.Cancelled)
                throw new InvalidOperationException("Cancelling in Status did not stop the scan.");
        }
        finally { files.CancelScan(); }

        ViewModel.ReportUnexpected();
        ShowLifecycleError("合成验证：应用错误。");
        await Task.Delay(200);
        if (TitleBarHasExtraContent)
            throw new InvalidOperationException("A status message added content to the title bar.");
        return (18, new
        {
            notice = true, noTopBanner = true, unread = true, updateLink = true, clearHistory = true, cancelScan = true,
            quietNotices = true, requiredAction = true, titleBarEmpty = true,
            theme = statusPage.ActualTheme.ToString(), width = statusPage.ActualWidth
        });

        void InvokeStatusAction(string id)
        {
            var button = P1Elements(statusPage).OfType<Button>().Single(value => AutomationProperties.GetAutomationId(value) == id);
            if (FrameworkElementAutomationPeer.CreatePeerForElement(button)?.GetPattern(PatternInterface.Invoke) is not IInvokeProvider invoke)
                throw new InvalidOperationException("Status action is not accessible.");
            invoke.Invoke();
        }
    }

    private async Task<(int Operations, object Verified)> CheckDiscoveryLoadingAsync()
    {
        if (onlineFixture is null) throw new InvalidOperationException("Synthetic catalog required.");
        await Navigate(WorkspacePage.Online);
        var tabs = Named<SelectorBar>(onlinePage, "DiscoveryTabs");
        var input = Named<TextBox>(onlinePage, "OnlineQueryBox");
        var searchBody = Named<Grid>(onlinePage, "SearchResultBody");
        var searchMark = Named<LoadingMark>(onlinePage, "SearchLoadingMark");
        var searchSmall = Named<LoadingMark>(onlinePage, "SearchInlineMark");
        var searchButton = Named<Button>(onlinePage, "SearchButton");
        var searchGrid = Named<GridView>(onlinePage, "ResultsGrid");
        var layouts = new List<object>();
        var feedSources = new Dictionary<string, object>();
        var feedTabs = new[] { ("Calendar", 1), ("Rankings", 4), ("News", 4) };

        SelectTab("Search");
        await UntilAsync(() => input.IsLoaded && searchBody.ActualHeight >= 160, "search layout");
        input.Text = "motion-scroll";
        await UntilAsync(() => searchButton.IsEnabled && searchButton.Visibility == Visibility.Visible, "search button");
        Invoke(searchButton);
        await UntilAsync(() => Ready(searchMark), "search emblem");
        layouts.Add(CheckLargeEmblem("search", searchBody, searchMark, searchSmall));
        Invoke(P1Elements(onlinePage).OfType<Button>().Single(button => AutomationProperties.GetAutomationId(button) == "OnlineCancel"));
        await UntilAsync(() => !online.SearchBusy && !searchMark.IsActive, "cancel search");
        if (online.Items.Count != 0 || searchSmall.IsActive || !online.SearchMessage.Contains(UiText.Get("搜索已取消。"), StringComparison.Ordinal))
            throw new InvalidOperationException("Cancelling search did not clear its loading state.");

        await UntilAsync(() => searchButton.IsEnabled && searchButton.Visibility == Visibility.Visible, "retry search button");
        Invoke(searchButton);
        await UntilAsync(() => online.SearchBusy, "search retry");

        foreach (var (name, count) in feedTabs)
        {
            SelectTab(name);
            if (name == "Calendar" && !online.SearchBusy)
                throw new InvalidOperationException("Leaving the search tab canceled its pending request.");
            var feed = (DiscoveryFeedView)Named<ContentControl>(onlinePage, "DiscoveryFeedHost").Content;
            var body = Named<Grid>(feed, "FeedBody");
            var list = Named<ListView>(feed, "FeedList");
            var header = Named<ContentControl>(feed, "FeedHeader");
            var mark = Named<LoadingMark>(feed, "FeedLoadingMark");
            var small = Named<LoadingMark>(feed, "FeedProgress");
            var reload = Named<Button>(feed, "ReloadButton");
            await UntilAsync(() => Ready(mark) || Ready(small), name + " emblem");
            // The marker can become active before WinUI lays out the new section's filter rail.
            await Task.Delay(100);
            var headerHeight = header.Content is null ? 0 : header.ActualHeight + header.Margin.Top + header.Margin.Bottom;
            var railWidth = body.ColumnDefinitions[0].ActualWidth;
            if (body.ActualHeight - headerHeight >= 160)
                layouts.Add(CheckLargeEmblem(name, body, mark, small, railWidth, headerHeight));
            else if (!mark.IsActive && Ready(small))
                layouts.Add(new { section = name, size = small.ActualWidth, height = body.ActualHeight, top = headerHeight });
            else throw new InvalidOperationException(name + " did not use a compact emblem in the constrained results area.");
            await UntilAsync(() => !mark.IsActive && list.Items.Count >= count && reload.IsEnabled, name + " results");

            Invoke(reload);
            await UntilAsync(() => Ready(small), name + " refresh emblem");
            if (mark.IsActive || small.ActualWidth != 16 || list.Items.Count < count)
                throw new InvalidOperationException(name + " refresh displaced existing content.");
            await UntilAsync(() => reload.IsEnabled && !small.IsActive, name + " refresh completion");
            await UntilAsync(() => list.Items.Count >= count && reload.IsEnabled, name + " refreshed results");
            feedSources.Add(name, list.ItemsSource);
        }
        var results = online.Items;
        var searchSource = searchGrid.ItemsSource;
        CheckSearch();
        foreach (var (name, count) in feedTabs)
        {
            SelectTab(name);
            var feed = (DiscoveryFeedView)Named<ContentControl>(onlinePage, "DiscoveryFeedHost").Content;
            var list = Named<ListView>(feed, "FeedList");
            if (!ReferenceEquals(feedSources[name], list.ItemsSource) || list.Items.Count < count
                || Named<LoadingMark>(feed, "FeedLoadingMark").IsActive || Named<LoadingMark>(feed, "FeedProgress").IsActive)
                throw new InvalidOperationException(name + " rebuilt or reloaded its existing results on return.");
        }
        CheckSearch();
        return (13, new { layouts, canceledSearch = true, compactRefresh = true, retainedTabs = 4,
            searchFinishedInBackground = true, theme = Root.ActualTheme.ToString(), dpi = Root.XamlRoot.RasterizationScale });

        void SelectTab(string name) => tabs.SelectedItem = tabs.Items.Single(
            item => AutomationProperties.GetAutomationId(item) == $"Discovery{name}Tab");

        void CheckSearch()
        {
            SelectTab("Search");
            if (results.Count < 20 || online.SearchBusy || !ReferenceEquals(results, online.Items) ||
                !ReferenceEquals(searchSource, searchGrid.ItemsSource) || input.Text != "motion-scroll" ||
                searchMark.IsActive || searchSmall.IsActive || searchGrid.Visibility != Visibility.Visible)
                throw new InvalidOperationException("Returning to search did not retain its query and completed results.");
        }

        static T Named<T>(FrameworkElement scope, string name) where T : FrameworkElement =>
            scope.FindName(name) as T ?? throw new InvalidOperationException("Missing discovery element: " + name);

        static void Invoke(Button button)
        {
            if (FrameworkElementAutomationPeer.CreatePeerForElement(button)?.GetPattern(PatternInterface.Invoke) is not IInvokeProvider invoke)
                throw new InvalidOperationException("Missing button action: " + button.Name);
            try { invoke.Invoke(); }
            catch (System.Runtime.InteropServices.COMException error)
            { throw new InvalidOperationException("Could not invoke " + AutomationProperties.GetAutomationId(button), error); }
        }

        static bool Ready(LoadingMark mark) => mark.IsActive && mark.ActualWidth > 0 &&
            Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementChildVisual(mark) is Microsoft.UI.Composition.SpriteVisual { IsVisible: true };

        static object CheckLargeEmblem(string section, Grid body, LoadingMark mark, LoadingMark small, double left = 0, double top = 0)
        {
            var content = (StackPanel)VisualTreeHelper.GetParent(mark);
            var origin = content.TransformToVisual(body).TransformPoint(new(0, 0));
            var center = mark.TransformToVisual(body).TransformPoint(new(mark.ActualWidth / 2, mark.ActualHeight / 2));
            var freeHeight = body.ActualHeight - top - content.ActualHeight;
            if (!Ready(mark) || small.IsActive || mark.ActualWidth != 108 || mark.ActualHeight != 108
                || Math.Abs(center.X - (body.ActualWidth + left) / 2) > 2
                || Math.Abs(origin.Y - top - freeHeight / 4) > 2
                || origin.Y < top || origin.Y + content.ActualHeight > body.ActualHeight)
                throw new InvalidOperationException($"{section} emblem size or placement differs ({center.X:F1}, {center.Y:F1}; {body.ActualWidth:F1}×{body.ActualHeight:F1}; {left:F1}, {top:F1}).");
            return new { section, size = mark.ActualWidth, width = body.ActualWidth, height = body.ActualHeight,
                left, top, centerX = center.X, centerY = center.Y };
        }

        static async Task UntilAsync(Func<bool> done, string state)
        {
            for (var attempt = 0; attempt < 160; attempt++)
            {
                if (done()) return;
                await Task.Delay(25);
            }
            throw new InvalidOperationException("Discovery timed out: " + state);
        }
    }
}
