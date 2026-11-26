using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using TheOrder.Application;
using TheOrder.Platform.Windows;
using TheOrder.Presentation;

using TheOrder.WinUI.Views;

namespace TheOrder.WinUI;

public sealed partial class MainWindow
{
    // Existing, explicitly marked Test-only peer. No production commands, data writes,
    // networking, external paths or process launch are exposed by these operations.
    private async Task StatisticsFixtureAsync(JsonElement request)
    {
        if (Program.Profile?.Manifest.ProfileKind != NativeProfileKind.Test || visualFixtureTimer is null || fixtureDirectory is null)
            throw new InvalidOperationException("Isolated Test required.");
        var action = request.GetProperty("value").GetString();
        if (action is "game-menu" or "dropdown")
        {
            await DropDownFixtureAsync(request, statistics: action == "game-menu");
            return;
        }
        if (action == "progress-readout")
        {
            await StatisticsProgressFixtureAsync(request);
            return;
        }
        string[]? sessionReadout = null;
        bool? markReadoutImmediate = null;
        if (action == "open") { await Navigate(WorkspacePage.Statistics); await statisticsPage.RefreshAsync(); }
        else if (action == "back") await GoBackAsync();
        else if (action is "maximize" or "restore")
        {
            if (AppWindow.Presenter is not Microsoft.UI.Windowing.OverlappedPresenter presenter)
                throw new InvalidOperationException("Overlapped Test window required.");
            if (action == "maximize") presenter.Maximize(); else presenter.Restore();
            await Task.Delay(350);
        }
        else
        {
            if (ViewModel.Page != WorkspacePage.Statistics) throw new InvalidOperationException("Statistics page required.");
            var controls = P1Elements(statisticsPage).OfType<FrameworkElement>().ToArray();
            if (action == "period")
            {
                if (!Enum.TryParse<StatisticsPeriod>(request.GetProperty("period").GetString(), out var period) || !Enum.IsDefined(period)) throw new InvalidDataException();
                var button = controls.OfType<ToggleButton>().Single(x => AutomationProperties.GetAutomationId(x) == "StatisticsPeriod:" + period);
                if (button.IsChecked != true)
                    ((IToggleProvider)new ToggleButtonAutomationPeer(button).GetPattern(PatternInterface.Toggle)).Toggle();
            }
            else if (action == "game")
            {
                var id = request.TryGetProperty("id", out var game) ? game.GetString() : null;
                if (!statisticsPage.ViewModel.GameOptions.Any(option => option.Id == id)) throw new InvalidDataException("Unknown statistics work.");
                await statisticsPage.ViewModel.SelectGameAsync(id);
            }
            else if (action == "detail")
            {
                var id = request.GetProperty("id").GetString();
                var button = controls.OfType<Button>().Single(x => AutomationProperties.GetAutomationId(x) == "StatisticsGame:" + id);
                ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
                for (int i = 0; i < 100 && ViewModel.Page != WorkspacePage.Details; i++) await Task.Delay(30);
                if (ViewModel.Page != WorkspacePage.Details) throw new InvalidOperationException("Statistics drilldown did not navigate.");
            }
            else if (action == "daily")
            {
                controls.OfType<Expander>().Single(x => AutomationProperties.GetAutomationId(x) == "StatisticsDailyDetails").IsExpanded = true;
                controls.OfType<ScrollViewer>().First().ChangeView(null, 10000, null, true);
            }
            else if (action == "top") controls.OfType<ScrollViewer>().First().ChangeView(null, 0, null, true);
            else if (action == "trend")
            {
                var outer = (ScrollViewer)statisticsPage.FindName("StatisticsScroll");
                var card = (FrameworkElement)statisticsPage.FindName("TrendCard");
                outer.ChangeView(null, card.TransformToVisual((UIElement)outer.Content).TransformPoint(new()).Y - 8, null, true);
            }
            else if (action == "mode")
            {
                var mode = request.GetProperty("mode").GetString() switch
                {
                    "sessions" => "StatisticsSessionsMode",
                    "duration" => "StatisticsDurationMode",
                    _ => throw new InvalidDataException("Invalid trend mode.")
                };
                var button = controls.OfType<ToggleButton>().Single(x => AutomationProperties.GetAutomationId(x) == mode);
                ((IToggleProvider)new ToggleButtonAutomationPeer(button).GetPattern(PatternInterface.Toggle)).Toggle();
            }
            else if (action == "session-readout")
            {
                var id = request.GetProperty("id").GetString();
                var button = controls.OfType<Button>().Single(x => AutomationProperties.GetAutomationId(x) == "StatisticsSessions:" + id);
                ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
                for (int i = 0; i < 50 && sessionReadout is null; i++)
                {
                    await Task.Delay(25);
                    sessionReadout = VisualTreeHelper.GetOpenPopupsForXamlRoot(statisticsPage.XamlRoot)
                        .SelectMany(popup => P1Elements(popup.Child)).OfType<ListView>()
                        .Select(list => list.ItemsSource).OfType<string[]>().FirstOrDefault();
                }
                if (sessionReadout is null) throw new InvalidOperationException("Session details did not open.");
            }
            else if (action == "mark-readout")
            {
                var chart = (StatisticsTrendChart)statisticsPage.FindName("SessionChart");
                double x = request.GetProperty("x").GetDouble(), y = request.GetProperty("y").GetDouble();
                var bounds = chart.TransformToVisual(null).TransformBounds(new(0, 0, chart.ActualWidth, chart.ActualHeight));
                if (chart.Visibility != Visibility.Visible || !double.IsFinite(x) || !double.IsFinite(y)
                    || x < bounds.Left || x > bounds.Right || y < bounds.Top || y > bounds.Bottom)
                    throw new InvalidDataException("Session chart point required.");
                var hit = chart.HitTestSessionMark(new Windows.Foundation.Point(x - bounds.Left, y - bounds.Top))
                    ?? VisualTreeHelper.FindElementsInHostCoordinates(new Windows.Foundation.Point(x, y), chart)
                        .OfType<Border>().FirstOrDefault(mark => mark.Tag is StatisticsSessionSpan);
                chart.ShowSessionMark(hit);
                var readout = P1Elements(chart).OfType<Border>()
                    .FirstOrDefault(element => AutomationProperties.GetAutomationId(element) == "StatisticsSessionReadout");
                markReadoutImmediate = (readout?.Visibility == Visibility.Visible) == (hit is not null);
            }
            else if (action == "breakdown")
            {
                var outer = (ScrollViewer)statisticsPage.FindName("StatisticsScroll");
                var breakdown = (FrameworkElement)statisticsPage.FindName("BreakdownGrid");
                outer.ChangeView(null, breakdown.TransformToVisual((UIElement)outer.Content).TransformPoint(new()).Y - 8, null, true);
            }
            else if (action == "weekday")
            {
                var outer = (ScrollViewer)statisticsPage.FindName("StatisticsScroll");
                var weekday = (FrameworkElement)statisticsPage.FindName("WeekdayCard");
                outer.ChangeView(null, weekday.TransformToVisual((UIElement)outer.Content).TransformPoint(new()).Y - 8, null, true);
            }
            else if (action == "refresh") await statisticsPage.RefreshAsync(true);
            else if (action != "inspect") throw new InvalidDataException("Unknown statistics operation.");
        }
        for (int i = 0; i < 200 && statisticsPage.ViewModel.Busy; i++) await Task.Delay(20);
        if (statisticsPage.ViewModel.Busy || statisticsPage.ViewModel.Error is not null) throw new InvalidOperationException("Statistics projection not ready.");
        statisticsPage.UpdateLayout();
        var data = statisticsPage.ViewModel.Data;
        var scroll = (ScrollViewer)statisticsPage.FindName("StatisticsScroll");
        var rankScroll = (ScrollViewer)statisticsPage.FindName("RankingScroll");
        var ranking = (ItemsRepeater)statisticsPage.FindName("RankingRows");
        var layout = P1Elements(statisticsPage).OfType<FrameworkElement>()
            .Where(x => x.Name is "StatisticsBody" or "SummaryGrid" or "LifetimeCard" or "PeriodCard" or "DaysCard" or "GamesCard"
                or "BreakdownGrid" or "RankingCard" or "ProgressCard" or "TrendCard" or "TrendChart" or "SessionChart"
                or "WeekdayChart" or "WeekdayCard")
            .Select(x =>
            {
                var bounds = x.TransformToVisual(statisticsPage).TransformBounds(new(0, 0, x.ActualWidth, x.ActualHeight));
                return new { x.Name, left = bounds.X, top = bounds.Y, width = bounds.Width, right = bounds.Right, bottom = bounds.Bottom,
                    desiredWidth = x.DesiredSize.Width, actualWidth = x.ActualWidth };
            }).ToArray();
        var trend = (FrameworkElement)statisticsPage.FindName("TrendChart");
        var weekdays = (FrameworkElement)statisticsPage.FindName("WeekdayChart");
        var sessions = (StatisticsTrendChart)statisticsPage.FindName("SessionChart");
        var sessionBorders = P1Elements(sessions).OfType<Border>().ToArray();
        var chartReadout = sessionBorders.FirstOrDefault(element => AutomationProperties.GetAutomationId(element) == "StatisticsSessionReadout");
        var chartHighlight = sessionBorders.FirstOrDefault(element => AutomationProperties.GetAutomationId(element) == "StatisticsSessionHighlight");
        var midnightLabel = P1Elements(sessions).OfType<TextBlock>()
            .FirstOrDefault(element => AutomationProperties.GetAutomationId(element) == "StatisticsSessionMidnightLabel");
        var sessionTargets = P1Elements(sessions).OfType<Button>()
            .Where(button => AutomationProperties.GetAutomationId(button).StartsWith("StatisticsSessions:", StringComparison.Ordinal)).ToArray();
        var timeLabelNodes = P1Elements(sessions).OfType<Canvas>().First().Children.OfType<TextBlock>()
            .Where(label => label.Text.Length == 5 && label.Text[2] == ':').ToArray();
        var readoutBounds = chartReadout is { Visibility: Visibility.Visible }
            ? chartReadout.TransformToVisual(null).TransformBounds(new(0, 0, chartReadout.ActualWidth, chartReadout.ActualHeight))
            : default;
        var sessionMode = new
        {
            visible = sessions.Visibility == Visibility.Visible,
            durationVisible = trend.Visibility == Visibility.Visible,
            legendPresent = statisticsPage.FindName("SessionLegend") is not null,
            midnightLabelVisible = midnightLabel?.Visibility == Visibility.Visible,
            axisOnLeft = timeLabelNodes.Length > 0 && timeLabelNodes.All(label => Canvas.GetLeft(label) < 60),
            hover = chartReadout is { Visibility: Visibility.Visible, Child: StackPanel panel }
                && panel.Children.Count == 2 && panel.Children[0] is Grid titleGrid && titleGrid.Children.Count == 2
                ? new
                {
                    title = ((TextBlock)titleGrid.Children[1]).Text,
                    time = ((TextBlock)panel.Children[1]).Text,
                    lineCount = panel.Children.Count,
                    highlight = chartHighlight?.Visibility == Visibility.Visible,
                    chartReadout.IsHitTestVisible,
                    x = Canvas.GetLeft(chartReadout), y = Canvas.GetTop(chartReadout),
                    w = chartReadout.ActualWidth, h = chartReadout.ActualHeight,
                    right = Canvas.GetLeft(chartReadout) + chartReadout.ActualWidth,
                    bottom = Canvas.GetTop(chartReadout) + chartReadout.ActualHeight,
                    chartWidth = sessions.ActualWidth, chartHeight = sessions.ActualHeight,
                    windowX = readoutBounds.X, windowY = readoutBounds.Y,
                    windowRight = readoutBounds.Right, windowBottom = readoutBounds.Bottom
                } : null,
            markReadoutImmediate,
            caption = ((TextBlock)statisticsPage.FindName("TrendInterval")).Text,
            axis = sessions.SessionAxis,
            timeLabels = timeLabelNodes.Select(label => label.Text).ToArray(),
            dates = sessionTargets.Select(button =>
            {
                var bounds = button.TransformToVisual(null).TransformBounds(new(0, 0, button.ActualWidth, button.ActualHeight));
                return new { id = AutomationProperties.GetAutomationId(button), name = AutomationProperties.GetName(button),
                    tooltip = ToolTipService.GetToolTip(button) is not null, x = bounds.X, y = bounds.Y, w = bounds.Width, h = bounds.Height };
            }).ToArray(),
            marks = sessions.Visibility == Visibility.Visible ? sessionBorders
                .Where(mark => mark.Tag is StatisticsSessionSpan)
                .Select(mark =>
                {
                    var bounds = mark.TransformToVisual(null).TransformBounds(new(0, 0, mark.ActualWidth, mark.ActualHeight));
                    var span = (StatisticsSessionSpan)mark.Tag;
                    bool hit = VisualTreeHelper.FindElementsInHostCoordinates(new Windows.Foundation.Point(
                        bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2), sessions).Contains(mark);
                    return new
                    {
                        dateId = $"StatisticsSessions:{span.CycleDate:yyyy-MM-dd}",
                        sessionKey = span.SessionKey,
                        text = AutomationProperties.GetName(mark),
                        span.GameId,
                        span.GameName,
                        span.ColorIndex,
                        span.CrossesMidnight,
                        cycleDate = span.CycleDate.ToString("yyyy-MM-dd"),
                        axisStart = span.AxisStartSecond,
                        axisEnd = span.AxisEndSecond,
                        zIndex = Canvas.GetZIndex(mark),
                        mark.Opacity,
                        tooltip = ToolTipService.GetToolTip(mark) is not null,
                        hit,
                        x = bounds.X, y = bounds.Y, w = bounds.Width, h = bounds.Height,
                        color = (mark.Background as SolidColorBrush)?.Color.ToString()
                    };
                }).ToArray() : [],
            readout = sessionReadout
        };
        var chartMotion = new
        {
            enabled = Views.InteractionMotion.Enabled,
            trendReveal = P1Elements(trend).OfType<Canvas>()
                .Any(x => x.Clip?.Transform is ScaleTransform),
            trendClip = P1Elements(trend).OfType<Canvas>().Select(x => x.Clip).OfType<RectangleGeometry>()
                .Select(x => new { x.Rect.Y, x.Rect.Height }).FirstOrDefault(),
            barsFromZero = P1Elements(weekdays).OfType<Border>()
                .Count(x => x.RenderTransform is ScaleTransform)
        };
        await File.WriteAllTextAsync(Path.Combine(fixtureDirectory, "statistics-evidence.json"), JsonSerializer.Serialize(new
        {
            page = ViewModel.Page.ToString(), period = statisticsPage.ViewModel.Period.ToString(), gameId = statisticsPage.ViewModel.GameId,
            backVisible = RailBack.Visibility == Visibility.Visible, selectedNavigation = (Navigation.SelectedItem as NavigationViewItem)?.Tag,
            data, pageWidth = statisticsPage.ActualWidth, viewportWidth = scroll.ViewportWidth, extentWidth = scroll.ExtentWidth,
            horizontalOffset = scroll.HorizontalOffset, verticalOffset = scroll.VerticalOffset, layout, chartMotion, sessionMode,
            notes = P1Elements(statisticsPage).OfType<TextBlock>().Select(text => text.Text)
                .Where(text => text.Contains("归属") || text.Contains("归日") || text.Contains("计入累计")).ToArray(),
            expanders = P1Elements(statisticsPage).OfType<Expander>()
                .Select(x => new { id = AutomationProperties.GetAutomationId(x), header = x.Header as string, x.IsExpanded }).ToArray(),
            dailyRows = ((StackPanel)statisticsPage.FindName("DailyRows")).Children.Count,
            ranking = new { rows = (ranking.ItemsSource as Array)?.Length ?? 0, rankScroll.VerticalOffset, rankScroll.ScrollableHeight,
                realized = P1Elements(ranking).OfType<Button>().Select(x => AutomationProperties.GetAutomationId(x)).ToArray(),
                bars = P1Elements(ranking).OfType<Views.StatisticsRankingBar>()
                    .Where(x => x.Row is not null && x.Children.FirstOrDefault() is FrameworkElement)
                    .Select(x => new { id = x.Row!.Game.Id, x.Row.Game.Seconds, width = ((FrameworkElement)x.Children[0]).ActualWidth }).ToArray(),
                shares = P1Elements(ranking).OfType<Button>().Where(button => button.Tag is StatisticsRankingRow && button.Content is Grid)
                    .Select(button =>
                    {
                        var row = (StatisticsRankingRow)button.Tag;
                        var share = ((Grid)button.Content).Children.OfType<TextBlock>().Single(text => Grid.GetColumn(text) == 2);
                        return new { id = row.Game.Id, row.Game.Seconds, text = share.Text, visible = share.Visibility == Visibility.Visible,
                            tooltip = ToolTipService.GetToolTip(button) as string, accessibleName = AutomationProperties.GetName(button) };
                    }).ToArray() },
            rasterizationScale = statisticsPage.XamlRoot.RasterizationScale,
            physicalInput = false, syntheticProfile = true, chartFramework = "WinUI native vectors"
        }));
    }

    private async Task StatisticsProgressFixtureAsync(JsonElement request)
    {
        if (ViewModel.Page != WorkspacePage.Statistics || statisticsPage.ViewModel.Data is not { } data)
            throw new InvalidOperationException("Statistics page required.");
        var chart = (StatisticsStatusChart)statisticsPage.FindName("StatusChart");
        var legend = (StackPanel)statisticsPage.FindName("StatusLegend");
        var buttons = legend.Children.OfType<Button>().ToArray();
        if (request.TryGetProperty("status", out var status))
        {
            var key = status.GetString();
            var button = buttons.Single(item => AutomationProperties.GetAutomationId(item) == "StatisticsStatus:" + key);
            if (request.TryGetProperty("focus", out var focus) && focus.GetBoolean())
            {
                if (!button.Focus(FocusState.Keyboard)) throw new InvalidOperationException("Status row could not receive focus.");
            }
            else
            {
                ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
            }
            await Task.Delay(120);
        }

        statisticsPage.UpdateLayout();
        string Text(string name) => ((TextBlock)statisticsPage.FindName(name)).Text;
        var bounds = chart.TransformToVisual(chart.XamlRoot.Content)
            .TransformBounds(new(0, 0, chart.ActualWidth, chart.ActualHeight));
        string? hit = null;
        if (request.TryGetProperty("point", out var point))
        {
            double x = point.GetProperty("x").GetDouble();
            double y = point.GetProperty("y").GetDouble();
            if (!double.IsFinite(x) || !double.IsFinite(y) || x < 0 || y < 0 || x > bounds.Width || y > bounds.Height)
                throw new InvalidDataException("Status chart point required.");
            hit = chart.StatusAt(new(x, y));
            if (request.TryGetProperty("preview", out var preview) && preview.GetBoolean()) chart.PreviewAt(new(x, y));
        }

        await File.WriteAllTextAsync(Path.Combine(fixtureDirectory!, "statistics-status-readout.json"), JsonSerializer.Serialize(new
        {
            label = Text("CompletionLabel"), count = Text("CompletionValue"), share = Text("CompletionShare"),
            summary = Text("ProgressSummary"), scope = statisticsPage.ViewModel.GameId,
            statuses = data.Statuses, hit, bounds, windowWidth = chart.XamlRoot.Size.Width,
            selected = (buttons.FirstOrDefault(item => !string.IsNullOrEmpty(AutomationProperties.GetItemStatus(item)))?.Tag as StatisticsStatus)?.Key,
            focused = (buttons.FirstOrDefault(item => item.FocusState == FocusState.Keyboard)?.Tag as StatisticsStatus)?.Key
        }));
    }

    private async Task DropDownFixtureAsync(JsonElement request, bool statistics)
    {
        if (statistics && ViewModel.Page != WorkspacePage.Statistics) throw new InvalidOperationException("Statistics page required.");
        var choice = statistics ? (SelectedFirstComboBox)statisticsPage.FindName("GameChoice") :
            P1Elements(Root).Concat(VisualTreeHelper.GetOpenPopupsForXamlRoot(Root.XamlRoot).SelectMany(popup => P1Elements(popup.Child)))
                .OfType<SelectedFirstComboBox>().Distinct()
                .Single(item => item.IsLoaded && AutomationProperties.GetAutomationId(item) == request.GetProperty("id").GetString());
        var button = choice.Trigger;
        string? Id(SelectedFirstComboBox.DropDownOption? item) => item is null ? null : statistics
            ? ((StatisticsGameOption)choice.PrimaryOptions[item.Index]).Id : item.Label;
        var flyout = (Flyout)button.Flyout;
        var list = (ListView)flyout.Content;
        var peer = new DropDownButtonAutomationPeer(button);
        var action = request.GetProperty("operation").GetString();
        if (action == "open")
        {
            button.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = false, VerticalAlignmentRatio = 0.25 });
            await Task.Delay(100);
            peer.Expand();
        }
        else if (action == "close") peer.Collapse();
        else if (action == "scroll")
        {
            var scroller = P1Elements(list).OfType<ScrollViewer>().First();
            var fraction = Math.Clamp(request.GetProperty("fraction").GetDouble(), 0, 1);
            scroller.ChangeView(null, scroller.ScrollableHeight * fraction, null, true);
        }
        else if (action == "choose")
        {
            var id = request.TryGetProperty("id", out var game) ? game.GetString() : null;
            var options = list.Items.OfType<SelectedFirstComboBox.DropDownOption>();
            var option = statistics ? options.Single(item => Id(item) == id)
                : options.Single(item => item.Index == request.GetProperty("index").GetInt32());
            list.ScrollIntoView(option);
            await Task.Delay(200);
            var item = (ListViewItem)list.ContainerFromItem(option);
            var itemPeer = FrameworkElementAutomationPeer.CreatePeerForElement(item);
            if (itemPeer?.GetPattern(PatternInterface.Invoke) is not IInvokeProvider invoke)
                throw new InvalidOperationException("Statistics work invocation unavailable.");
            invoke.Invoke();
        }
        else if (action != "inspect") throw new InvalidDataException("Unknown game menu operation.");

        await Task.Delay(220);
        if (statistics)
        {
            for (int i = 0; i < 200 && statisticsPage.ViewModel.Busy; i++) await Task.Delay(20);
            if (statisticsPage.ViewModel.Busy || statisticsPage.ViewModel.Error is not null)
                throw new InvalidOperationException("Statistics projection not ready.");
        }
        Root.UpdateLayout();

        var xamlRoot = button.XamlRoot;
        var menu = VisualTreeHelper.GetOpenPopupsForXamlRoot(xamlRoot)
            .SelectMany(popup => P1Elements(popup.Child)).OfType<FlyoutPresenter>()
            .FirstOrDefault(presenter => ReferenceEquals(presenter.Content, list));
        Windows.Foundation.Rect Bounds(FrameworkElement element) => element.TransformToVisual(xamlRoot.Content)
            .TransformBounds(new(0, 0, element.ActualWidth, element.ActualHeight));

        var buttonBorder = P1Elements(button).OfType<Grid>().First(grid => grid.Name == "RootGrid");
        var menuBorder = menu is null ? null : P1Elements(menu).OfType<Border>().First();
        var listScroll = menu is null ? null : P1Elements(list).OfType<ScrollViewer>().First();
        var bounds = Bounds(button);
        await File.WriteAllTextAsync(Path.Combine(fixtureDirectory!, statistics ? "statistics-game-menu.json" : "dropdown-evidence.json"), JsonSerializer.Serialize(new
        {
            page = ViewModel.Page.ToString(), scope = statisticsPage.ViewModel.GameId, expanded = flyout.IsOpen,
            title = ((TextBlock)button.Content).Text, index = choice.OptionIndex,
            window = new { xamlRoot.Size.Width, xamlRoot.Size.Height },
            button = new { bounds.Left, bounds.Right, bounds.Top, bounds.Bottom, bounds.Width,
                corner = buttonBorder.CornerRadius.TopLeft, stroke = buttonBorder.BorderThickness.Left },
            menu = menu is null ? null : new { bounds = Bounds(menu), corner = menuBorder!.CornerRadius.TopLeft,
                stroke = menuBorder.BorderThickness.Left, brush = (menu.Background as Microsoft.UI.Xaml.Media.SolidColorBrush)?.Color.ToString(), offset = listScroll!.VerticalOffset,
                scrollable = listScroll.ScrollableHeight, horizontal = listScroll.ScrollableWidth, items = list.Items.Count,
                selected = Id(list.SelectedItem as SelectedFirstComboBox.DropDownOption),
                labels = P1Elements(list).OfType<TextBlock>().Where(text => text.TextTrimming == TextTrimming.CharacterEllipsis && !string.IsNullOrEmpty(text.Text))
                    .Select(text => text.Text).ToArray(),
                first = Id(list.Items.OfType<SelectedFirstComboBox.DropDownOption>().FirstOrDefault()),
                realized = P1Elements(list).OfType<ListViewItem>()
                    .Select(container => container.Content).OfType<SelectedFirstComboBox.DropDownOption>().Select(Id).ToArray() }
        }));
    }
}
