using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using TheOrder.Application;
using TheOrder.Platform.Windows;
using TheOrder.Presentation;

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
                controls.OfType<ComboBox>().Single(x => AutomationProperties.GetAutomationId(x) == "StatisticsPeriod").SelectedIndex = (int)period;
            }
            else if (action == "game")
            {
                var id = request.TryGetProperty("id", out var game) ? game.GetString() : null;
                var combo = controls.OfType<ComboBox>().Single(x => AutomationProperties.GetAutomationId(x) == "StatisticsGame");
                combo.SelectedItem = combo.Items.OfType<StatisticsGameOption>().Single(x => x.Id == id);
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
            else if (action == "refresh") await statisticsPage.RefreshAsync(true);
            else if (action != "inspect") throw new InvalidDataException("Unknown statistics operation.");
        }
        for (int i = 0; i < 200 && statisticsPage.ViewModel.Busy; i++) await Task.Delay(20);
        if (statisticsPage.ViewModel.Busy || statisticsPage.ViewModel.Error is not null) throw new InvalidOperationException("Statistics projection not ready.");
        statisticsPage.UpdateLayout();
        var data = statisticsPage.ViewModel.Data;
        var scroll = (ScrollViewer)statisticsPage.FindName("StatisticsScroll");
        var layout = P1Elements(statisticsPage).OfType<FrameworkElement>()
            .Where(x => x.Name is "StatisticsBody" or "SummaryGrid" or "LifetimeCard" or "PeriodCard" or "DaysCard" or "GamesCard"
                or "BreakdownGrid" or "RankingCard" or "ProgressCard" or "TrendChart" or "WeekdayChart")
            .Select(x =>
            {
                var bounds = x.TransformToVisual(statisticsPage).TransformBounds(new(0, 0, x.ActualWidth, x.ActualHeight));
                return new { x.Name, left = bounds.X, top = bounds.Y, width = bounds.Width, right = bounds.Right,
                    desiredWidth = x.DesiredSize.Width, actualWidth = x.ActualWidth };
            }).ToArray();
        await File.WriteAllTextAsync(Path.Combine(fixtureDirectory, "statistics-evidence.json"), JsonSerializer.Serialize(new
        {
            page = ViewModel.Page.ToString(), period = statisticsPage.ViewModel.Period.ToString(), gameId = statisticsPage.ViewModel.GameId,
            backVisible = RailBack.Visibility == Visibility.Visible, selectedNavigation = (Navigation.SelectedItem as NavigationViewItem)?.Tag,
            data, pageWidth = statisticsPage.ActualWidth, viewportWidth = scroll.ViewportWidth, extentWidth = scroll.ExtentWidth,
            horizontalOffset = scroll.HorizontalOffset, verticalOffset = scroll.VerticalOffset, layout,
            rasterizationScale = statisticsPage.XamlRoot.RasterizationScale,
            physicalInput = false, syntheticProfile = true, chartFramework = "WinUI native vectors"
        }));
    }
}
