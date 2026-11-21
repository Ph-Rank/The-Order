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
                controls.OfType<SelectedFirstComboBox>().Single(x => AutomationProperties.GetAutomationId(x) == "StatisticsPeriod").OptionIndex = (int)period;
            }
            else if (action == "game")
            {
                var id = request.TryGetProperty("id", out var game) ? game.GetString() : null;
                var combo = controls.OfType<SelectedFirstComboBox>().Single(x => AutomationProperties.GetAutomationId(x) == "StatisticsGame");
                combo.SelectedOption = combo.PrimaryOptions.OfType<StatisticsGameOption>().Single(x => x.Id == id);
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
            else if (action == "notes")
            {
                var notes = controls.OfType<Expander>().Single(x => x.Header as string == "统计说明");
                notes.IsExpanded = true;
                await Task.Delay(220);
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
                var hit = VisualTreeHelper.FindElementsInHostCoordinates(new Windows.Foundation.Point(x, y), chart)
                    .OfType<Border>().FirstOrDefault(mark => mark.Tag is StatisticsSessionRange);
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
        var sessionTargets = P1Elements(sessions).OfType<Button>()
            .Where(button => AutomationProperties.GetAutomationId(button).StartsWith("StatisticsSessions:", StringComparison.Ordinal)).ToArray();
        var sessionMode = new
        {
            visible = sessions.Visibility == Visibility.Visible,
            durationVisible = trend.Visibility == Visibility.Visible,
            legendPresent = statisticsPage.FindName("SessionLegend") is not null,
            hover = chartReadout is { Visibility: Visibility.Visible, Child: StackPanel panel }
                ? new
                {
                    time = ((TextBlock)panel.Children[0]).Text, title = ((TextBlock)panel.Children[1]).Text,
                    highlight = chartHighlight?.Visibility == Visibility.Visible, chartReadout.IsHitTestVisible,
                    x = Canvas.GetLeft(chartReadout), y = Canvas.GetTop(chartReadout), w = chartReadout.ActualWidth, h = chartReadout.ActualHeight,
                    right = Canvas.GetLeft(chartReadout) + chartReadout.ActualWidth, chartWidth = sessions.ActualWidth,
                    windowX = chartReadout.TransformToVisual(null).TransformPoint(new()).X
                } : null,
            markReadoutImmediate,
            caption = ((TextBlock)statisticsPage.FindName("TrendInterval")).Text,
            axis = sessions.SessionAxis,
            timeLabels = P1Elements(sessions).OfType<Canvas>().First().Children.OfType<TextBlock>()
                .Select(label => label.Text).Where(label => label.Length == 5 && label[2] == ':').ToArray(),
            dates = sessionTargets.Select(button =>
            {
                var bounds = button.TransformToVisual(null).TransformBounds(new(0, 0, button.ActualWidth, button.ActualHeight));
                return new { id = AutomationProperties.GetAutomationId(button), name = AutomationProperties.GetName(button),
                    tooltip = ToolTipService.GetToolTip(button) is not null, x = bounds.X, y = bounds.Y, w = bounds.Width, h = bounds.Height };
            }).ToArray(),
            marks = sessions.Visibility == Visibility.Visible ? sessionTargets.SelectMany(target => ((Canvas)target.Content).Children.OfType<Border>().Select(mark =>
            {
                var bounds = mark.TransformToVisual(null).TransformBounds(new(0, 0, mark.ActualWidth, mark.ActualHeight));
                var range = (StatisticsSessionRange)mark.Tag;
                bool hit = VisualTreeHelper.FindElementsInHostCoordinates(new Windows.Foundation.Point(
                    bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2), sessions).Contains(mark);
                return new { dateId = AutomationProperties.GetAutomationId(target), text = AutomationProperties.GetName(mark),
                    range.GameId, mark.Opacity, tooltip = ToolTipService.GetToolTip(mark) is not null,
                    hit, x = bounds.X, y = bounds.Y, w = bounds.Width, h = bounds.Height,
                    color = (mark.Background as SolidColorBrush)?.Color.ToString() };
            })).ToArray() : [],
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
            ranking = new { rows = (ranking.ItemsSource as Array)?.Length ?? 0, rankScroll.VerticalOffset, rankScroll.ScrollableHeight,
                realized = P1Elements(ranking).OfType<Button>().Select(x => AutomationProperties.GetAutomationId(x)).ToArray(),
                bars = P1Elements(ranking).OfType<Views.StatisticsRankingBar>()
                    .Where(x => x.Row is not null && x.Children.FirstOrDefault() is FrameworkElement)
                    .Select(x => new { id = x.Row!.Game.Id, x.Row.Game.Seconds, width = ((FrameworkElement)x.Children[0]).ActualWidth }).ToArray() },
            rasterizationScale = statisticsPage.XamlRoot.RasterizationScale,
            physicalInput = false, syntheticProfile = true, chartFramework = "WinUI native vectors"
        }));
    }
}
