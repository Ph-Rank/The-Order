using System.Collections.Immutable;
using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using TheOrder.Application;
using TheOrder.Domain;
using TheOrder.Presentation;
using Windows.Foundation;

namespace TheOrder.WinUI.Views;

public sealed record StatisticsRankingRow(StatisticsGame Game, int Position, double ZeroFraction, double Fraction)
{
    public string Rank => Position.ToString("00");
    public string Duration => StatisticsPage.Hours(Game.Seconds);
    public string Tooltip => $"{Game.Name} · {Duration}";
    public string AccessibleName => $"{Game.Name}，{Duration}，查看详情";
    public string AutomationId => "StatisticsGame:" + Game.Id;
    public Windows.UI.Text.FontWeight NameWeight => Position == 1 ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal;
    public double BarOpacity => Position == 1 ? 1 : .68;
}

public sealed class StatisticsRankingBar : Panel
{
    public static readonly DependencyProperty RowProperty = DependencyProperty.Register(
        nameof(Row), typeof(StatisticsRankingRow), typeof(StatisticsRankingBar),
        new PropertyMetadata(null, (bar, _) => ((StatisticsRankingBar)bar).InvalidateArrange()));

    public StatisticsRankingRow? Row
    {
        get => (StatisticsRankingRow?)GetValue(RowProperty);
        set => SetValue(RowProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (var child in Children) child.Measure(new Size(0, availableSize.Height));
        return new Size(0, 0);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var bounds = new Rect(0, 0, 0, finalSize.Height);
        if (Row is { Fraction: not 0 } row)
        {
            double width = finalSize.Width;
            double minimum = Math.Min(12, width / 2);
            double zero = width * row.ZeroFraction;
            // Reserve space for a legible bar on each side of a shared zero.
            if (row.ZeroFraction is > 0 and < 1) zero = Math.Clamp(zero, minimum, width - minimum);
            bool negative = row.Fraction < 0;
            double available = negative ? zero : width - zero;
            double length = Math.Min(available, Math.Max(minimum, width * Math.Abs(row.Fraction)));
            bounds = new Rect(negative ? zero - length : zero, 0, length, finalSize.Height);
        }

        foreach (var child in Children) child.Arrange(bounds);
        return finalSize;
    }
}

public sealed partial class StatisticsPage : Page, IDisposable
{
    private readonly Func<LibrarySnapshot?> snapshot;
    internal StatisticsViewModel ViewModel { get; } = new();
    public Func<string, Task>? OpenDetails { get; set; }
    public Func<Task>? OpenImport { get; set; }
    internal Func<Task>? OpenStatus { get; set; }
    private bool initialized, rendering, disposed;
    private int summaryColumns, breakdownColumns, visibleDays = 30;
    private StatisticsPeriod previousPeriod;
    private string? previousGame, hoveredStatus, focusedStatus;
    private ImmutableArray<StatisticsGame> rankedGames = [];
    public StatisticsPage(Func<LibrarySnapshot?> snapshot)
    {
        this.snapshot = snapshot; InitializeComponent(); initialized = true;
        DailyPageFooter.LoadNext = () => { visibleDays += 30; DrawDays(append: true); return Task.CompletedTask; };
        ViewModel.PropertyChanged += OnState;
        Loaded += async (_, _) => { FitViewport(); ArrangeCards(); await RefreshAsync(); };
        Unloaded += (_, _) => ViewModel.Leave();
        StatusChart.PaletteChanged += (_, _) => DrawLegend();
        StatusChart.HoverChanged += HoverStatus;
    }
    internal void SetHighContrast(bool value)
    {
        TrendChart.SetHighContrast(value);
        WeekdayChart.SetHighContrast(value);
        StatusChart.SetHighContrast(value);
    }

    public Task RefreshAsync(bool force = false) => disposed ? Task.CompletedTask : ViewModel.RefreshAsync(snapshot(), force);
    public async Task ScopeAsync(string? id) { await RefreshAsync(); await ViewModel.SelectGameAsync(id); }
    private void OnState(object? sender, PropertyChangedEventArgs args)
    {
        if (disposed) return;
        StatisticsLoadingBar.Visibility = ViewModel.Busy ? Visibility.Visible : Visibility.Collapsed;
        StatisticsUnavailable.Visibility = ViewModel.Error is not null && ViewModel.Data is null ? Visibility.Visible : Visibility.Collapsed;
        if (ViewModel.Data is null) Dashboard.Visibility = Visibility.Collapsed;
        if (args.PropertyName == nameof(StatisticsViewModel.Period)) { rendering = true; PeriodChoice.OptionIndex = (int)ViewModel.Period; rendering = false; }
        if (args.PropertyName == nameof(StatisticsViewModel.GameOptions))
        {
            rendering = true;
            try { GameChoice.OptionsSource = ViewModel.GameOptions; GameChoice.SelectedOption = ViewModel.GameOptions.First(x => x.Id == ViewModel.GameId); }
            finally { rendering = false; }
        }
        if (args.PropertyName == nameof(StatisticsViewModel.Data)) DrawData();
    }
    private void DrawData()
    {
        if (ViewModel.Data is not { } data) { Dashboard.Visibility = Visibility.Collapsed; EmptyShelf.Visibility = Visibility.Collapsed; return; }
        var scopeChanged = previousPeriod != ViewModel.Period || previousGame != ViewModel.GameId;
        var reveal = Dashboard.Visibility != Visibility.Visible;
        if (scopeChanged) visibleDays = 30;
        previousPeriod = ViewModel.Period; previousGame = ViewModel.GameId;
        bool empty = data.LibraryCount == 0;
        EmptyShelf.Visibility = empty ? Visibility.Visible : Visibility.Collapsed; Dashboard.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
        SetDuration(LifetimeNumber, LifetimeUnit, data.LifetimeSeconds);
        LifetimeCaption.Text = $"{data.LibraryCount:N0} 部书架作品";
        SetDuration(PeriodNumber, PeriodUnit, data.PeriodSeconds);
        PeriodCaption.Text = data.Start.Year == data.End.Year
            ? $"{data.Start:yyyy/M/d} — {data.End:M/d}" : $"{data.Start:yyyy/M/d} — {data.End:yyyy/M/d}";
        DaysNumber.Text = $"{data.ActiveDays:N0}\u2009";
        DaysCaption.Text = data.ActiveDays > 0 ? $"活跃日均 {Hours(data.PeriodSeconds / data.ActiveDays)}" : "暂无活跃日期";
        GamesNumber.Text = $"{data.PlayedGames:N0}\u2009";
        TrendInterval.Text = data.IntervalLabel;
        TrendChart.SetData(data.Trend);
        WeekdayChart.SetData([.. data.Weekdays.Select(x => new StatisticsBucket(data.Start, data.End, x.Name, x.Seconds))]);
        UndatedNotice.Text = $"另有 {Hours(data.UnknownSeconds)} 缺少可归属日期，仅计入累计。";
        UndatedNotice.Visibility = data.UnknownSeconds > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (StatusChart.SetData(data.Statuses)) DrawLegend();
        ShowStatus();
        DrawRanking(data.Games, scopeChanged);
        DailyDetails.Header = $"逐日明细 · {data.Days.Length:N0} 条";
        if (DailyDetails.IsExpanded) DrawDays();
        var explanation = new List<string>();
        if (data.UnknownSeconds > 0) explanation.Add($"{Hours(data.UnknownSeconds)} 无可归属日期，保留在累计中，不进入时间趋势和排行。");
        if (data.Estimated) explanation.Add("跨日记录按经过时间分配；因未保存完整暂停区间，这些日期的分配为估算。");
        if (data.ManualSeconds != 0) explanation.Add($"当前区间包含净时长修正 {Hours(data.ManualSeconds)}，原始计时记录不变。");
        if (data.FutureSeconds != 0) explanation.Add($"另有 {Hours(data.FutureSeconds)} 记录日期在今天之后，暂不计入当前区间。");
        if (explanation.Count == 0) explanation.Add("当前区间的时长均可归属到具体日期。");
        ProvenanceText.Text = string.Join("\n", explanation);
        AutomationProperties.SetName(Dashboard, $"游玩统计，累计 {Hours(data.LifetimeSeconds)}，当前区间 {Hours(data.PeriodSeconds)}，{data.ActiveDays} 个活跃日，{data.PlayedGames} 部作品。");
        if (reveal && !empty) InteractionMotion.RevealContent(Dashboard, .65f);
    }
    private void DrawRanking(ImmutableArray<StatisticsGame> games, bool scopeChanged)
    {
        if (scopeChanged) RankingScroll.ChangeView(null, 0, null, true);
        if (rankedGames.SequenceEqual(games)) return;
        rankedGames = games;
        RankingEmpty.Visibility = games.IsEmpty ? Visibility.Visible : Visibility.Collapsed;
        RankingScroll.Visibility = games.IsEmpty ? Visibility.Collapsed : Visibility.Visible;
        double minimum = Math.Min(0, games.IsEmpty ? 0 : games[^1].Seconds);
        double maximum = Math.Max(0, games.IsEmpty ? 0 : games[0].Seconds);
        double span = Math.Max(1, maximum - minimum);
        RankingRows.ItemsSource = games.Select((game, index) => new StatisticsRankingRow(game, index + 1, -minimum / span, game.Seconds / span)).ToArray();
    }

    private async void OnRankingClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: StatisticsRankingRow row } && OpenDetails is { } open)
            await open(row.Game.Id);
    }

    private void DrawLegend()
    {
        hoveredStatus = focusedStatus = null;
        StatusLegend.Children.Clear();
        if (ViewModel.Data is not { } data) return;
        foreach (var row in data.Statuses)
        {
            var grid = new Grid { ColumnSpacing = 8 };
            grid.ColumnDefinitions.Add(new() { Width = new(8) });
            grid.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            grid.Children.Add(new Border
            {
                Width = 8, Height = 8, CornerRadius = new(4),
                Background = StatusChart.ColorFor(row.Key), VerticalAlignment = VerticalAlignment.Center
            });
            var label = Note(row.Name);
            Grid.SetColumn(label, 1);
            grid.Children.Add(label);
            var count = new TextBlock
            {
                Text = row.Count.ToString("N0"), FontSize = 13, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            };
            Typography.SetNumeralAlignment(count, FontNumeralAlignment.Tabular);
            AutomationProperties.SetName(count, $"{row.Count:N0} 部");
            Grid.SetColumn(count, 2);
            grid.Children.Add(count);
            var button = new Button
            {
                Content = grid, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch,
                MinHeight = 0, MinWidth = 0, Padding = new(4, 1, 4, 1),
                Style = (Style)Microsoft.UI.Xaml.Application.Current.Resources["SubtleButtonStyle"]
            };
            AutomationProperties.SetName(button, $"{row.Name}，{row.Count:N0} 部");
            AutomationProperties.SetAutomationId(button, "StatisticsStatus:" + row.Key);
            button.PointerEntered += (_, _) => HoverStatus(row.Key);
            button.PointerExited += (_, _) => HoverStatus(null);
            button.GotFocus += (_, _) =>
            {
                focusedStatus = button.FocusState == FocusState.Keyboard ? row.Key : null;
                if (focusedStatus is not null) hoveredStatus = null;
                ShowStatus();
            };
            button.LostFocus += (_, _) => { focusedStatus = null; ShowStatus(); };
            button.Click += (_, _) => HoverStatus(row.Key);
            StatusLegend.Children.Add(button);
        }
        ShowStatus();
    }

    private void HoverStatus(string? key)
    {
        hoveredStatus = key;
        ShowStatus();
    }

    private void ShowStatus()
    {
        if (ViewModel.Data is not { } data) return;
        var key = hoveredStatus ?? focusedStatus;
        var status = data.Statuses.FirstOrDefault(x => x.Key == key);
        StatusChart.Highlight(status?.Key);
        CompletionLabel.Text = status?.Name ?? GameProgress.Label("completed");
        CompletionValue.Text = $"{status?.Count ?? data.CompletedGames:N0} / {data.LibraryCount:N0}";
    }

    private void DrawDays(bool append = false)
    {
        if (ViewModel.Data is not { } data) return;
        if (!append) DailyRows.Children.Clear();
        var start = append ? DailyRows.Children.Count : 0;
        if (data.Days.IsEmpty) DailyRows.Children.Add(Note("当前区间没有可显示的日期记录。"));
        foreach (var row in data.Days.Take(visibleDays).Skip(start))
        {
            var grid = new Grid { ColumnSpacing = 12 };
            grid.ColumnDefinitions.Add(new() { Width = new(84) }); grid.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); grid.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            grid.Children.Add(Note(row.Date.ToString("yyyy-MM-dd")));
            var name = new TextBlock { Text = row.Name, TextWrapping = TextWrapping.Wrap }; Grid.SetColumn(name, 1); grid.Children.Add(name);
            var value = Note((row.Estimated ? "≈ " : "") + Hours(row.Seconds)); Grid.SetColumn(value, 2); grid.Children.Add(value);
            var button = new Button { Content = grid, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Padding = new(8), Style = (Style)Microsoft.UI.Xaml.Application.Current.Resources["SubtleButtonStyle"] };
            string explanation = $"{row.Date:yyyy-MM-dd} · {row.Name} · {Hours(row.Seconds)}" + (row.ManualSeconds != 0 ? $"（含修正 {Hours(row.ManualSeconds)}）" : "") + (row.Estimated ? " · 跨日估算" : "");
            ToolTipService.SetToolTip(button, explanation); AutomationProperties.SetName(button, explanation + "，查看作品详情");
            button.Click += async (_, _) => { if (OpenDetails is not null) await OpenDetails(row.GameId); };
            DailyRows.Children.Add(button);
            if (append) InteractionMotion.RevealContent(button, .72f);
        }
        DailyPageFooter.Update(visibleDays < data.Days.Length ? visibleDays : (int?)null);
    }
    private static (string Number, string Unit) Duration(long seconds)
    {
        double magnitude = Math.Abs((double)seconds);
        if (seconds == 0) return ("0", "小时");
        if (magnitude >= 3600) return ($"{seconds / 3600d:0.#}", "小时");
        if (magnitude >= 60) return ($"{seconds / 60d:0.#}", "分钟");
        return (seconds.ToString(), "秒");
    }

    internal static string Hours(long seconds)
    {
        var duration = Duration(seconds);
        return $"{duration.Number} {duration.Unit}";
    }

    private static void SetDuration(Run number, Run unit, long seconds)
    {
        var duration = Duration(seconds);
        number.Text = duration.Number + "\u2009";
        unit.Text = duration.Unit;
    }
    private static TextBlock Note(string text) => new() { Text = text, Style = (Style)Microsoft.UI.Xaml.Application.Current.Resources["MetadataTextStyle"], VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
    private void OnStatisticsSizeChanged(object sender, SizeChangedEventArgs e) => ArrangeCards();
    private void OnBreakdownSideSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (breakdownColumns == 2 && e.NewSize.Height > 0) RankingCard.Height = e.NewSize.Height;
    }
    private void OnStatisticsViewportSizeChanged(object sender, SizeChangedEventArgs e) => FitViewport();
    private void OnStatusSizeChanged(object sender, SizeChangedEventArgs e)
    {
        bool compact = e.NewSize.Width < 282;
        Grid.SetColumnSpan(StatusDial, compact ? 2 : 1);
        Grid.SetColumn(StatusLegend, compact ? 0 : 1);
        Grid.SetColumnSpan(StatusLegend, compact ? 2 : 1);
        Grid.SetRow(StatusLegend, compact ? 1 : 0);
        StatusContent.RowSpacing = compact ? 10 : 0;
    }
    private void FitViewport()
    {
        if (!initialized || ActualWidth <= 0) return;
        // Canvas charts have no intrinsic width. Give the scroll content a bounded
        // width so its desired size and arranged size agree at every window size.
        StatisticsBody.Width = Math.Min(StatisticsBody.MaxWidth,
            Math.Max(0, ActualWidth - StatisticsBody.Margin.Left - StatisticsBody.Margin.Right));
    }
    private void ArrangeCards()
    {
        if (!initialized) return;
        double width = StatisticsBody.ActualWidth;
        bool compactHeader = width < 610;
        StatisticsHeader.RowSpacing = compactHeader ? 10 : 0;
        Grid.SetRow(StatisticsFilters, compactHeader ? 1 : 0);
        Grid.SetColumn(StatisticsFilters, compactHeader ? 0 : 1);
        Grid.SetColumnSpan(StatisticsFilters, compactHeader ? 2 : 1);
        GameChoice.Width = Math.Clamp(width - 186, 116, 210);

        int columns = width < 320 ? 1 : width < 760 ? 2 : 4;
        if (columns != summaryColumns)
        {
            summaryColumns = columns; SummaryGrid.ColumnDefinitions.Clear(); SummaryGrid.RowDefinitions.Clear();
            for (int i = 0; i < columns; i++) SummaryGrid.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
            for (int i = 0; i < (4 + columns - 1) / columns; i++) SummaryGrid.RowDefinitions.Add(new() { Height = GridLength.Auto });
            for (int i = 0; i < SummaryGrid.Children.Count; i++)
            {
                var cell = (Border)SummaryGrid.Children[i];
                int column = i % columns, row = i / columns;
                Grid.SetColumn(cell, column);
                Grid.SetRow(cell, row);
                cell.BorderThickness = new(0, 0, column + 1 < columns ? 1 : 0, i + columns < SummaryGrid.Children.Count ? 1 : 0);
            }
        }
        int pairColumns = width < 780 ? 1 : 2;
        if (pairColumns != breakdownColumns)
        {
            breakdownColumns = pairColumns; BreakdownGrid.ColumnDefinitions.Clear(); BreakdownGrid.RowDefinitions.Clear();
            BreakdownGrid.ColumnDefinitions.Add(new() { Width = new(pairColumns == 1 ? 1 : 1.24, GridUnitType.Star) });
            if (pairColumns == 2) BreakdownGrid.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
            BreakdownGrid.RowDefinitions.Add(new() { Height = GridLength.Auto }); if (pairColumns == 1) BreakdownGrid.RowDefinitions.Add(new() { Height = GridLength.Auto });
            Grid.SetColumn(BreakdownSide, pairColumns == 1 ? 0 : 1);
            Grid.SetRow(BreakdownSide, pairColumns == 1 ? 1 : 0);
            RankingScroll.MaxHeight = pairColumns == 1 ? 420 : double.PositiveInfinity;
            RankingCard.Height = pairColumns == 2 && BreakdownSide.ActualHeight > 0 ? BreakdownSide.ActualHeight : double.NaN;
        }
    }
    private async void OnPeriodChanged(object sender, SelectionChangedEventArgs e) { if (initialized && !rendering && PeriodChoice.OptionIndex is >= 0 and <= 3) await ViewModel.SelectPeriodAsync((StatisticsPeriod)PeriodChoice.OptionIndex); }
    private async void OnGameChanged(object sender, SelectionChangedEventArgs e) { if (initialized && !rendering && GameChoice.SelectedOption is StatisticsGameOption game) await ViewModel.SelectGameAsync(game.Id); }
    private async void OnRefresh(object sender, RoutedEventArgs e) => await RefreshAsync(true);
    private async void OnImport(object sender, RoutedEventArgs e) { if (OpenImport is not null) await OpenImport(); }
    private async void OnOpenStatus(object sender, RoutedEventArgs e) { if (OpenStatus is not null) await OpenStatus(); }
    private void OnDailyExpanding(Expander sender, ExpanderExpandingEventArgs args) => DrawDays();
    public void Dispose() { if (disposed) return; disposed = true; ViewModel.PropertyChanged -= OnState; ViewModel.Dispose(); }
}
