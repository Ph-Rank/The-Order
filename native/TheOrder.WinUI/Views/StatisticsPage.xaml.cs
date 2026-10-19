using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using TheOrder.Application;
using TheOrder.Presentation;

namespace TheOrder.WinUI.Views;

public sealed partial class StatisticsPage : Page, IDisposable
{
    private readonly Func<LibrarySnapshot?> snapshot;
    internal StatisticsViewModel ViewModel { get; } = new();
    public Func<string, Task>? OpenDetails { get; set; }
    public Func<Task>? OpenImport { get; set; }
    private bool initialized, rendering, disposed;
    private int summaryColumns, breakdownColumns, visibleDays = 30;
    private StatisticsPeriod previousPeriod;
    private string? previousGame;
    public StatisticsPage(Func<LibrarySnapshot?> snapshot)
    {
        this.snapshot = snapshot; InitializeComponent(); initialized = true;
        ViewModel.PropertyChanged += OnState;
        Loaded += async (_, _) => { ArrangeCards(); await RefreshAsync(); };
        Unloaded += (_, _) => ViewModel.Leave();
        ActualThemeChanged += (_, _) => { if (initialized) DrawLegend(); };
    }
    public Task RefreshAsync(bool force = false) => disposed ? Task.CompletedTask : ViewModel.RefreshAsync(snapshot(), force);
    public async Task ScopeAsync(string? id) { await RefreshAsync(); await ViewModel.SelectGameAsync(id); }
    private void OnState(object? sender, PropertyChangedEventArgs args)
    {
        if (disposed) return;
        StatisticsLoadingBar.Visibility = ViewModel.Busy ? Visibility.Visible : Visibility.Collapsed;
        StatisticsError.IsOpen = ViewModel.Error is not null; StatisticsError.Message = ViewModel.Error ?? "";
        if (ViewModel.Data is null) Dashboard.Visibility = Visibility.Collapsed;
        if (args.PropertyName == nameof(StatisticsViewModel.Period)) { rendering = true; PeriodChoice.SelectedIndex = (int)ViewModel.Period; rendering = false; }
        if (args.PropertyName == nameof(StatisticsViewModel.GameOptions))
        {
            rendering = true;
            try { GameChoice.ItemsSource = ViewModel.GameOptions; GameChoice.SelectedItem = ViewModel.GameOptions.First(x => x.Id == ViewModel.GameId); }
            finally { rendering = false; }
        }
        if (args.PropertyName == nameof(StatisticsViewModel.Data)) DrawData();
    }
    private void DrawData()
    {
        if (ViewModel.Data is not { } data) { Dashboard.Visibility = Visibility.Collapsed; EmptyShelf.Visibility = Visibility.Collapsed; return; }
        if (previousPeriod != ViewModel.Period || previousGame != ViewModel.GameId) visibleDays = 30;
        previousPeriod = ViewModel.Period; previousGame = ViewModel.GameId;
        bool empty = data.LibraryCount == 0;
        EmptyShelf.Visibility = empty ? Visibility.Visible : Visibility.Collapsed; Dashboard.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
        LifetimeValue.Text = Hours(data.LifetimeSeconds); LifetimeCaption.Text = $"{data.LibraryCount:N0} 部书架作品";
        PeriodValue.Text = Hours(data.PeriodSeconds); PeriodCaption.Text = $"{data.Start:yyyy/M/d} — {data.End:M/d}";
        DaysValue.Text = $"{data.ActiveDays:N0} 天"; DaysCaption.Text = data.ActiveDays > 0 ? $"活跃日均 {Hours(data.PeriodSeconds / data.ActiveDays)}" : "当前区间尚无活跃日期";
        GamesValue.Text = $"{data.PlayedGames:N0} 部";
        TrendCaption.Text = "本地日期 · 已结束记录与时长修正"; TrendInterval.Text = data.IntervalLabel;
        TrendChart.SetData(data.Trend);
        WeekdayChart.SetData([.. data.Weekdays.Select(x => new StatisticsBucket(data.Start, data.End, x.Name, x.Seconds))]);
        UndatedNotice.Text = $"另有 {Hours(data.UnknownSeconds)} 缺少可归属日期，仅计入累计。";
        UndatedNotice.Visibility = data.UnknownSeconds > 0 ? Visibility.Visible : Visibility.Collapsed;
        CompletionValue.Text = $"{data.CompletedGames} / {data.LibraryCount}";
        StatusChart.SetData(data.Statuses); DrawLegend(); DrawRanking();
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
    }
    private void DrawRanking()
    {
        RankingRows.Children.Clear(); if (ViewModel.Data is not { } data) return;
        var rows = data.Games.Take(8).ToArray();
        if (rows.Length == 0) { RankingRows.Children.Add(Note("当前区间暂无可归日的游玩时长。")); return; }
        double maximum = Math.Max(1, rows.Max(x => Math.Abs((double)x.Seconds)));
        for (int i = 0; i < rows.Length; i++)
        {
            var row = rows[i]; var label = new Grid { ColumnSpacing = 10 };
            label.ColumnDefinitions.Add(new() { Width = new(24) }); label.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); label.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            var number = Note((i + 1).ToString()); var name = new TextBlock { Text = row.Name, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
            var hours = Note(Hours(row.Seconds)); Grid.SetColumn(name, 1); Grid.SetColumn(hours, 2); label.Children.Add(number); label.Children.Add(name); label.Children.Add(hours);
            var panel = new StackPanel { Spacing = 8 }; panel.Children.Add(label);
            panel.Children.Add(new ProgressBar { Maximum = maximum, Value = Math.Abs((double)row.Seconds), Height = 4, IsTabStop = false });
            var button = new Button { Content = panel, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Padding = new(4, 9, 4, 9), Style = (Style)Microsoft.UI.Xaml.Application.Current.Resources["SubtleButtonStyle"] };
            ToolTipService.SetToolTip(button, $"{row.Name} · {Hours(row.Seconds)}"); AutomationProperties.SetName(button, $"{row.Name}，{Hours(row.Seconds)}，查看详情");
            AutomationProperties.SetAutomationId(button, "StatisticsGame:" + row.Id);
            button.Click += async (_, _) => { if (OpenDetails is not null) await OpenDetails(row.Id); };
            RankingRows.Children.Add(button);
        }
        if (data.Games.Length > rows.Length) RankingRows.Children.Add(Note($"显示时长前 {rows.Length} 部；可用顶部作品筛选查看其他游戏。"));
    }
    private void DrawLegend()
    {
        StatusLegend.Children.Clear(); if (ViewModel.Data is not { } data) return;
        for (int i = 0; i < data.Statuses.Length; i++)
        {
            var row = data.Statuses[i]; var grid = new Grid { ColumnSpacing = 10 };
            grid.ColumnDefinitions.Add(new() { Width = new(12) }); grid.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); grid.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            grid.Children.Add(new Border { Width = 8, Height = 8, CornerRadius = new(4), Background = StatusChart.ColorFor(i), VerticalAlignment = VerticalAlignment.Center });
            var label = Note(row.Name); Grid.SetColumn(label, 1); grid.Children.Add(label);
            var count = new TextBlock { Text = $"{row.Count:N0} 部", VerticalAlignment = VerticalAlignment.Center }; Grid.SetColumn(count, 2); grid.Children.Add(count);
            StatusLegend.Children.Add(grid);
        }
    }
    private void DrawDays()
    {
        DailyRows.Children.Clear(); if (ViewModel.Data is not { } data) return;
        if (data.Days.IsEmpty) DailyRows.Children.Add(Note("当前区间没有可显示的日期记录。"));
        foreach (var row in data.Days.Take(visibleDays))
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
        }
        MoreDays.Visibility = visibleDays < data.Days.Length ? Visibility.Visible : Visibility.Collapsed;
    }
    internal static string Hours(long seconds) => seconds == 0 ? "0 小时" : Math.Abs((double)seconds) >= 3600 ? $"{seconds / 3600d:0.#} 小时" : Math.Abs((double)seconds) >= 60 ? $"{seconds / 60d:0.#} 分钟" : $"{seconds} 秒";
    private static TextBlock Note(string text) => new() { Text = text, Style = (Style)Microsoft.UI.Xaml.Application.Current.Resources["MetadataTextStyle"], VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
    private void OnStatisticsSizeChanged(object sender, SizeChangedEventArgs e) => ArrangeCards();
    private void ArrangeCards()
    {
        if (!initialized) return;
        int columns = StatisticsBody.ActualWidth < 390 ? 1 : StatisticsBody.ActualWidth < 860 ? 2 : 4;
        if (columns != summaryColumns)
        {
            summaryColumns = columns; SummaryGrid.ColumnDefinitions.Clear(); SummaryGrid.RowDefinitions.Clear();
            for (int i = 0; i < columns; i++) SummaryGrid.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
            for (int i = 0; i < (4 + columns - 1) / columns; i++) SummaryGrid.RowDefinitions.Add(new() { Height = GridLength.Auto });
            for (int i = 0; i < SummaryGrid.Children.Count; i++) { var card = (FrameworkElement)SummaryGrid.Children[i]; Grid.SetColumn(card, i % columns); Grid.SetRow(card, i / columns); }
        }
        int pairColumns = StatisticsBody.ActualWidth < 780 ? 1 : 2;
        if (pairColumns != breakdownColumns)
        {
            breakdownColumns = pairColumns; BreakdownGrid.ColumnDefinitions.Clear(); BreakdownGrid.RowDefinitions.Clear();
            BreakdownGrid.ColumnDefinitions.Add(new() { Width = new(pairColumns == 1 ? 1 : 1.5, GridUnitType.Star) });
            if (pairColumns == 2) BreakdownGrid.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
            BreakdownGrid.RowDefinitions.Add(new() { Height = GridLength.Auto }); if (pairColumns == 1) BreakdownGrid.RowDefinitions.Add(new() { Height = GridLength.Auto });
            Grid.SetColumn(ProgressCard, pairColumns == 1 ? 0 : 1); Grid.SetRow(ProgressCard, pairColumns == 1 ? 1 : 0);
        }
    }
    private async void OnPeriodChanged(object sender, SelectionChangedEventArgs e) { if (initialized && !rendering && PeriodChoice.SelectedIndex is >= 0 and <= 3) await ViewModel.SelectPeriodAsync((StatisticsPeriod)PeriodChoice.SelectedIndex); }
    private async void OnGameChanged(object sender, SelectionChangedEventArgs e) { if (initialized && !rendering && GameChoice.SelectedItem is StatisticsGameOption game) await ViewModel.SelectGameAsync(game.Id); }
    private async void OnRefresh(object sender, RoutedEventArgs e) => await RefreshAsync(true);
    private async void OnImport(object sender, RoutedEventArgs e) { if (OpenImport is not null) await OpenImport(); }
    private void OnDailyExpanding(Expander sender, ExpanderExpandingEventArgs args) => DrawDays();
    private void OnMoreDays(object sender, RoutedEventArgs e) { visibleDays += 30; DrawDays(); }
    public void Dispose() { if (disposed) return; disposed = true; ViewModel.PropertyChanged -= OnState; ViewModel.Dispose(); }
}
