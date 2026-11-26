using System.Collections.Immutable;
using System.ComponentModel;
using Microsoft.UI.Input;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using TheOrder.Application;
using TheOrder.Domain;
using TheOrder.Presentation;
using Windows.Foundation;

namespace TheOrder.WinUI.Views;

public sealed record StatisticsRankingRow(StatisticsGame Game, int Position, double ZeroFraction, double Fraction, double? ShareFraction)
{
    public string Rank => Position.ToString("00");
    public string Duration => StatisticsPage.Hours(Game.Seconds);
    public string ShareText => ShareFraction switch
    {
        null => string.Empty,
        > 0 and < 0.01 => "<1%",
        { } share => $"{Math.Round(share * 100):0}%"
    };
    public Visibility ShareVisibility => ShareFraction.HasValue ? Visibility.Visible : Visibility.Collapsed;
    public string Tooltip => ShareFraction.HasValue
        ? $"{Game.Name} · {Duration}（{ShareText}）"
        : $"{Game.Name} · {Duration}";
    public string AccessibleName => ShareFraction.HasValue
        ? $"{Game.Name}，{Duration}，占比 {ShareText}，查看详情"
        : $"{Game.Name}，{Duration}，查看详情";
    public string AutomationId => "StatisticsGame:" + Game.Id;
    public Windows.UI.Text.FontWeight NameWeight => Position == 1 ? FontWeights.SemiBold : FontWeights.Normal;
    public Windows.UI.Text.FontWeight RankWeight => Position <= 3 ? FontWeights.SemiBold : FontWeights.Normal;
    public Windows.UI.Text.FontWeight DurationWeight => Position <= 3 ? FontWeights.SemiBold : FontWeights.Normal;
    public double RankOpacity => Position == 1 ? 1.0 : Position <= 3 ? 0.88 : 0.66;
    public double BarOpacity => Position == 1 ? 1.0 : Position == 2 ? 0.84 : Position == 3 ? 0.72 : 0.56;
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
            double zero = width * row.ZeroFraction;
            bool negative = row.Fraction < 0;
            double available = negative ? zero : width - zero;
            double length = Math.Min(available, width * Math.Abs(row.Fraction));
            bounds = new Rect(negative ? zero - length : zero, 0, length, finalSize.Height);
        }

        foreach (var child in Children) child.Arrange(bounds);
        return finalSize;
    }
}

public sealed partial class StatisticsPage : Page, IDisposable
{
    private readonly Func<LibrarySnapshot?> snapshot;
    private readonly ToggleButton[] periodSegments;
    internal StatisticsViewModel ViewModel { get; } = new();
    public Func<string, Task>? OpenDetails { get; set; }
    public Func<Task>? OpenImport { get; set; }
    internal Func<Task>? OpenStatus { get; set; }
    private bool initialized, disposed, syncingGameChoice, showingSessionPeriods, updatingTrendMode, updatingPeriodMode, highContrast;
    private int summaryMode, breakdownColumns, weekdayMode, visibleDays = 30;
    private StatisticsPeriod previousPeriod;
    private string? previousGame, hoveredStatus, focusedStatus, selectedStatus;
    private ImmutableArray<StatisticsGame> rankedGames = [];

    public StatisticsPage(Func<LibrarySnapshot?> snapshot)
    {
        this.snapshot = snapshot;
        InitializeComponent();
        periodSegments = [Period7Days, Period30Days, PeriodThisYear, PeriodAllTime];
        initialized = true;
        DailyPageFooter.LoadNext = () => { visibleDays += 30; DrawDays(append: true); return Task.CompletedTask; };
        ViewModel.PropertyChanged += OnState;
        Loaded += async (_, _) => { FitViewport(); ArrangeCards(); SyncPeriodSegments(); await RefreshAsync(); };
        Unloaded += (_, _) =>
        {
            hoveredStatus = focusedStatus = selectedStatus = null;
            UpdateStatusSelection();
            ViewModel.Leave();
        };
        StatusChart.PaletteChanged += (_, _) => UpdateStatusPalette();
        StatusChart.HoverChanged += HoverStatus;
        StatusChart.StatusInvoked += SelectStatus;
        ProgressCard.PointerExited += (_, args) =>
        {
            if (args.Pointer.PointerDeviceType != PointerDeviceType.Touch) HoverStatus(null);
        };
    }

    internal void SetHighContrast(bool value)
    {
        if (highContrast == value) return;
        highContrast = value;
        TrendChart.SetHighContrast(value);
        SessionChart.SetHighContrast(value);
        WeekdayChart.SetHighContrast(value);
        StatusChart.SetHighContrast(value);
        if (ViewModel.Data is { } data) DrawWeeklyInsights(data);
    }

    public Task RefreshAsync(bool force = false) => disposed ? Task.CompletedTask : ViewModel.RefreshAsync(snapshot(), force);
    public async Task ScopeAsync(string? id) { await RefreshAsync(); await ViewModel.SelectGameAsync(id); }

    private void OnState(object? sender, PropertyChangedEventArgs args)
    {
        if (disposed) return;
        StatisticsLoadingBar.Visibility = ViewModel.Busy ? Visibility.Visible : Visibility.Collapsed;
        StatisticsUnavailable.Visibility = ViewModel.Error is not null && ViewModel.Data is null ? Visibility.Visible : Visibility.Collapsed;
        if (ViewModel.Data is null) Dashboard.Visibility = Visibility.Collapsed;
        if (args.PropertyName == nameof(StatisticsViewModel.Period)) SyncPeriodSegments();
        if (args.PropertyName is nameof(StatisticsViewModel.GameOptions) or nameof(StatisticsViewModel.GameId))
        {
            syncingGameChoice = true;
            try
            {
                if (args.PropertyName == nameof(StatisticsViewModel.GameOptions))
                    GameChoice.OptionsSource = ViewModel.GameOptions.SetItem(0, new(null, UiText.Get("全部作品")));
                GameChoice.SelectedOption = GameChoice.PrimaryOptions.OfType<StatisticsGameOption>()
                    .FirstOrDefault(option => option.Id == ViewModel.GameId);
            }
            finally { syncingGameChoice = false; }
        }
        if (args.PropertyName == nameof(StatisticsViewModel.Data)) DrawData();
    }

    private void SyncPeriodSegments()
    {
        updatingPeriodMode = true;
        try
        {
            int active = (int)ViewModel.Period;
            for (int i = 0; i < periodSegments.Length; i++)
                periodSegments[i].IsChecked = i == active;
        }
        finally { updatingPeriodMode = false; }
    }

    private async void OnPeriodSegmentChanged(object sender, RoutedEventArgs e)
    {
        if (!initialized || updatingPeriodMode || sender is not ToggleButton button) return;
        if (button.Tag is not string tag || !int.TryParse(tag, out int index) || index is < 0 or > 3) return;
        var selected = (StatisticsPeriod)index;
        if (button.IsChecked != true)
        {
            // Keep segmented bar strictly single-selected (clicking the active segment keeps it checked).
            if (ViewModel.Period == selected) SyncPeriodSegments();
            return;
        }
        if (ViewModel.Period == selected) return;
        updatingPeriodMode = true;
        try
        {
            for (int i = 0; i < periodSegments.Length; i++)
                periodSegments[i].IsChecked = i == index;
        }
        finally { updatingPeriodMode = false; }
        await ViewModel.SelectPeriodAsync(selected);
    }

    private void DrawData()
    {
        if (ViewModel.Data is not { } data) { Dashboard.Visibility = Visibility.Collapsed; EmptyShelf.Visibility = Visibility.Collapsed; return; }
        var gameChanged = previousGame != ViewModel.GameId;
        var scopeChanged = previousPeriod != ViewModel.Period || gameChanged;
        if (gameChanged) hoveredStatus = focusedStatus = selectedStatus = null;
        var reveal = Dashboard.Visibility != Visibility.Visible;
        if (scopeChanged) visibleDays = 30;
        previousPeriod = ViewModel.Period; previousGame = ViewModel.GameId;
        bool empty = data.LibraryCount == 0;
        EmptyShelf.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        Dashboard.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;

        bool singleGame = ViewModel.GameId is not null;
        SetDuration(PeriodNumber, PeriodUnit, data.PeriodSeconds);
        PeriodCaption.Text = data.Start.Year == data.End.Year
            ? $"{data.Start:yyyy/M/d} — {data.End:M/d}" : $"{data.Start:yyyy/M/d} — {data.End:yyyy/M/d}";

        SetDuration(LifetimeNumber, LifetimeUnit, data.LifetimeSeconds);
        LifetimeCaption.Text = singleGame ? "当前作品累计" : $"{data.LibraryCount:N0} 部书架作品";

        DaysNumber.Text = $"{data.ActiveDays:N0}\u2009";
        DaysCaption.Text = data.ActiveDays > 0 ? $"活跃日均 {Hours(data.PeriodSeconds / data.ActiveDays)}" : "暂无活跃日期";

        GamesNumber.Text = $"{data.PlayedGames:N0}\u2009";
        if (singleGame)
        {
            var statusName = data.Statuses.FirstOrDefault(s => s.Count > 0)?.Name ?? GameProgress.Label("planned");
            GamesCaption.Text = $"状态 · {statusName}";
        }
        else if (data.LibraryCount > 0)
        {
            int ratio = (int)Math.Round(100.0 * data.PlayedGames / data.LibraryCount);
            GamesCaption.Text = data.PlayedGames > 0 ? $"占书架 {ratio}%" : "当前区间无游玩";
        }
        else
        {
            GamesCaption.Text = string.Empty;
        }

        TrendChart.SetData(data.Trend);
        SessionChart.SetSessionTimeline(data.SessionTimeline);
        UpdateTrendMode();

        WeekdayChart.SetData([.. data.Weekdays.Select(x => new StatisticsBucket(data.Start, data.End, x.Name, x.Seconds))]);
        DrawWeeklyInsights(data);

        if (StatusChart.SetData(data.Statuses)) DrawLegend();
        else if (gameChanged) UpdateStatusSelection();
        ShowStatus();

        DrawRanking(data.Games, scopeChanged);

        DailyDetails.Header = $"逐日明细 · {data.Days.Length:N0} 条";
        if (DailyDetails.IsExpanded) DrawDays();

        AutomationProperties.SetName(Dashboard, $"游玩统计，累计 {Hours(data.LifetimeSeconds)}，当前区间 {Hours(data.PeriodSeconds)}，{data.ActiveDays} 个活跃日，{data.PlayedGames} 部作品。");
        if (reveal && !empty) InteractionMotion.RevealContent(Dashboard, .65f);
    }

    private void OnTrendModeChanged(object sender, RoutedEventArgs e)
    {
        if (!initialized || updatingTrendMode) return;
        if (DurationTrendMode.IsChecked == true && ReferenceEquals(sender, DurationTrendMode)) showingSessionPeriods = false;
        if (SessionTrendMode.IsChecked == true && ReferenceEquals(sender, SessionTrendMode)) showingSessionPeriods = true;
        UpdateTrendMode();
    }

    private void UpdateTrendMode()
    {
        updatingTrendMode = true;
        try
        {
            DurationTrendMode.IsChecked = !showingSessionPeriods;
            SessionTrendMode.IsChecked = showingSessionPeriods;
            TrendChart.Visibility = showingSessionPeriods ? Visibility.Collapsed : Visibility.Visible;
            SessionChart.Visibility = showingSessionPeriods ? Visibility.Visible : Visibility.Collapsed;
            if (!showingSessionPeriods) SessionChart.ShowSessionMark(null);
            if (ViewModel.Data is not { } data) return;
            if (!showingSessionPeriods)
            {
                var peak = data.Trend.Where(b => b.Seconds > 0).OrderByDescending(b => b.Seconds).FirstOrDefault();
                TrendInterval.Text = peak is not null
                    ? $"{data.IntervalLabel} · 峰值 {peak.Label}（{Hours(peak.Seconds)}）"
                    : data.IntervalLabel;
            }
            else
            {
                TrendInterval.Text = data.SessionTimeline.Axis is { } axis
                    ? $"{StatisticsTrendChart.FormatAxisRange(axis)} · 本地时间"
                    : "会话起止 · 本地时间";
            }
            ToolTipService.SetToolTip(TrendInterval, null);
        }
        finally { updatingTrendMode = false; }
    }

    private void DrawWeeklyInsights(StatisticsDashboard data)
    {
        var positiveDays = data.Weekdays.Where(w => w.Seconds > 0).ToArray();
        if (positiveDays.Length == 0)
        {
            WeekdayHeadline.Text = "暂无游玩分布";
            WeekdayPeakDetail.Text = "当前区间没有可归属到星期的时长。";
            WeekdayBalancePanel.Visibility = Visibility.Collapsed;
            return;
        }

        var peak = positiveDays.OrderByDescending(w => w.Seconds).First();
        bool allNonNegative = data.Weekdays.All(w => w.Seconds >= 0);
        long total = data.Weekdays.Sum(w => w.Seconds);
        WeekdayHeadline.Text = $"{peak.Name}最常游玩";
        WeekdayPeakDetail.Text = allNonNegative && total > 0
            ? $"{Hours(peak.Seconds)} · 占区间 {Math.Round(100.0 * peak.Seconds / total):0}%"
            : Hours(peak.Seconds);

        long workdays = data.Weekdays.Take(5).Sum(w => w.Seconds);
        long weekend = data.Weekdays.Skip(5).Sum(w => w.Seconds);
        if (allNonNegative && workdays >= 0 && weekend >= 0 && workdays + weekend > 0)
        {
            long sum = workdays + weekend;
            int workdayPct = (int)Math.Round(100.0 * workdays / sum);
            int weekendPct = 100 - workdayPct;
            WorkdayShareText.Text = $"工作日 {workdayPct}%";
            WeekendShareText.Text = $"周末 {weekendPct}%";
            WorkdayBarCol.Width = new GridLength(Math.Max(0.01, workdays), GridUnitType.Star);
            WeekendBarCol.Width = new GridLength(Math.Max(0.01, weekend), GridUnitType.Star);
            bool weekendHigher = weekend > workdays;
            WorkdayBarFill.Opacity = highContrast ? 1.0 : (weekendHigher ? 0.38 : 0.92);
            WeekendBarFill.Opacity = highContrast ? 0.65 : (weekendHigher ? 0.92 : 0.38);
            WeekdaySplitDetail.Text = $"工作日 {Hours(workdays)} · 周末 {Hours(weekend)}";
            WeekdayBalancePanel.Visibility = Visibility.Visible;
        }
        else
        {
            WeekdayBalancePanel.Visibility = Visibility.Collapsed;
        }
    }

    private void DrawRanking(ImmutableArray<StatisticsGame> games, bool scopeChanged)
    {
        if (scopeChanged) RankingScroll.ChangeView(null, 0, null, true);
        if (rankedGames.SequenceEqual(games)) return;
        rankedGames = games;
        RankingEmpty.Visibility = games.IsEmpty ? Visibility.Visible : Visibility.Collapsed;
        RankingScroll.Visibility = games.IsEmpty ? Visibility.Collapsed : Visibility.Visible;
        RankingSummary.Text = games.IsEmpty ? string.Empty : $"{games.Length:N0} 部作品";

        double minimum = Math.Min(0, games.IsEmpty ? 0 : games[^1].Seconds);
        double maximum = Math.Max(0, games.IsEmpty ? 0 : games[0].Seconds);
        double span = Math.Max(1, maximum - minimum);
        bool showShares = games.Length > 1 && games.All(g => g.Seconds >= 0);
        long totalSeconds = showShares ? games.Sum(g => g.Seconds) : 0;

        RankingRows.ItemsSource = games.Select((game, index) => new StatisticsRankingRow(
            game,
            index + 1,
            -minimum / span,
            game.Seconds / span,
            showShares && totalSeconds > 0 ? (double)game.Seconds / totalSeconds : null)).ToArray();
    }

    private async void OnRankingClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: StatisticsRankingRow row } && OpenDetails is { } open)
            await open(row.Game.Id);
    }

    private void DrawLegend()
    {
        focusedStatus = null;
        StatusLegend.Children.Clear();
        if (ViewModel.Data is not { } data) return;
        int total = Math.Max(1, data.LibraryCount);

        foreach (var row in data.Statuses)
        {
            var grid = new Grid { ColumnSpacing = 8 };
            grid.ColumnDefinitions.Add(new() { Width = new(8) });
            grid.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new() { Width = new(34) });

            var dot = new Border
            {
                Width = 8, Height = 8, CornerRadius = new(4),
                Background = StatusChart.ColorFor(row.Key), VerticalAlignment = VerticalAlignment.Center
            };
            grid.Children.Add(dot);

            var label = new TextBlock
            {
                Text = UiText.Get(row.Name),
                Style = (Style)Resources["StatsPrimaryLabel"],
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            Grid.SetColumn(label, 1);
            grid.Children.Add(label);

            var count = new TextBlock
            {
                Text = row.Count.ToString("N0"),
                Style = (Style)Resources["StatsPrimaryLabel"],
                FontWeight = FontWeights.SemiBold
            };
            Typography.SetNumeralAlignment(count, FontNumeralAlignment.Tabular);
            AutomationProperties.SetName(count, $"{row.Count:N0} 部");
            Grid.SetColumn(count, 2);
            grid.Children.Add(count);

            int pct = (int)Math.Round(100.0 * row.Count / total);
            var share = new TextBlock
            {
                Text = $"{pct}%",
                Style = (Style)Resources["StatsTertiaryCaption"],
                HorizontalAlignment = HorizontalAlignment.Right
            };
            Typography.SetNumeralAlignment(share, FontNumeralAlignment.Tabular);
            Grid.SetColumn(share, 3);
            grid.Children.Add(share);

            if (row.Count == 0 && !highContrast) grid.Opacity = 0.52;

            var button = new Button
            {
                Content = grid, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch,
                MinHeight = 0, MinWidth = 0, Padding = new(8, 4, 8, 4), CornerRadius = new(6), Tag = row,
                Style = (Style)Microsoft.UI.Xaml.Application.Current.Resources["SubtleButtonStyle"]
            };
            AutomationProperties.SetName(button, $"{label.Text}，{row.Count:N0} 部，占 {pct}%");
            AutomationProperties.SetAutomationId(button, "StatisticsStatus:" + row.Key);
            button.PointerEntered += (_, args) =>
            {
                if (args.Pointer.PointerDeviceType != PointerDeviceType.Touch) HoverStatus(row.Key);
            };
            button.GotFocus += (_, _) =>
            {
                focusedStatus = button.FocusState == FocusState.Keyboard ? row.Key : null;
                if (focusedStatus is not null) hoveredStatus = null;
                ShowStatus();
            };
            button.LostFocus += (_, _) =>
            {
                if (focusedStatus != row.Key) return;
                focusedStatus = null;
                ShowStatus();
            };
            button.Click += (_, _) => SelectStatus(row.Key);
            StatusLegend.Children.Add(button);
        }
        UpdateStatusSelection();
        ShowStatus();
    }

    private void HoverStatus(string? key)
    {
        if (hoveredStatus == key) return;
        hoveredStatus = key;
        ShowStatus();
    }

    private void SelectStatus(string key)
    {
        selectedStatus = selectedStatus == key ? null : key;
        focusedStatus = null;
        UpdateStatusSelection();
        ShowStatus();
    }

    private void UpdateStatusPalette()
    {
        foreach (var button in StatusLegend.Children.OfType<Button>())
        {
            if (button.Tag is not StatisticsStatus row || button.Content is not Grid layout ||
                layout.Children.FirstOrDefault() is not Border dot) continue;
            dot.Background = StatusChart.ColorFor(row.Key);
            layout.Opacity = row.Count == 0 && !highContrast ? 0.52 : 1;
        }
    }

    private void UpdateStatusSelection()
    {
        foreach (var button in StatusLegend.Children.OfType<Button>())
            if (button.Tag is StatisticsStatus row)
                AutomationProperties.SetItemStatus(button, selectedStatus == row.Key ? UiText.Get("已选中") : "");
    }

    private void ShowStatus()
    {
        if (ViewModel.Data is not { } data) return;
        var key = hoveredStatus ?? focusedStatus ?? selectedStatus;
        var status = data.Statuses.FirstOrDefault(x => x.Key == key);
        StatusChart.Highlight(status?.Key);
        int count = status?.Count ?? data.CompletedGames;
        string label = UiText.Get(status?.Name ?? GameProgress.Label("completed"));
        CompletionLabel.Text = label;
        CompletionValue.Text = $"{count:N0} / {data.LibraryCount:N0}";
        int share = data.LibraryCount > 0 ? (int)Math.Round(100.0 * count / data.LibraryCount) : 0;
        CompletionShare.Text = data.LibraryCount > 0 ? $"{share}%" : string.Empty;
        ProgressSummary.Text = data.LibraryCount > 0 ? $"{label} {share}%" : string.Empty;
    }

    private void DrawDays(bool append = false)
    {
        if (ViewModel.Data is not { } data) return;
        if (!append) DailyRows.Children.Clear();
        var start = append ? DailyRows.Children.Count : 0;
        if (data.Days.IsEmpty) DailyRows.Children.Add(Note("当前区间没有可显示的日期记录。"));
        foreach (var row in data.Days.Take(visibleDays).Skip(start))
        {
            var grid = new Grid { ColumnSpacing = 14 };
            grid.ColumnDefinitions.Add(new() { Width = new(86) });
            grid.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new() { Width = GridLength.Auto });

            var date = Note(row.Date.ToString("yyyy-MM-dd"));
            Typography.SetNumeralAlignment(date, FontNumeralAlignment.Tabular);
            grid.Children.Add(date);

            var name = new TextBlock
            {
                Text = row.Name,
                Style = (Style)Resources["StatsPrimaryLabel"],
                FontSize = 14,
                TextWrapping = TextWrapping.Wrap
            };
            Grid.SetColumn(name, 1);
            grid.Children.Add(name);

            var value = new TextBlock
            {
                Text = (row.Estimated ? "≈ " : "") + Hours(row.Seconds),
                Style = (Style)Resources["StatsPrimaryLabel"],
                FontWeight = FontWeights.SemiBold
            };
            Typography.SetNumeralAlignment(value, FontNumeralAlignment.Tabular);
            Grid.SetColumn(value, 2);
            grid.Children.Add(value);

            var button = new Button
            {
                Content = grid, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Padding = new(10, 7, 10, 7), CornerRadius = new(6),
                Style = (Style)Microsoft.UI.Xaml.Application.Current.Resources["SubtleButtonStyle"]
            };
            string explanation = $"{row.Date:yyyy-MM-dd} · {row.Name} · {Hours(row.Seconds)}"
                + (row.ManualSeconds != 0 ? $"（含修正 {Hours(row.ManualSeconds)}）" : "")
                + (row.Estimated ? " · 跨日估算" : "");
            ToolTipService.SetToolTip(button, explanation);
            AutomationProperties.SetName(button, explanation + "，查看作品详情");
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

    private static TextBlock Note(string text) => new()
    {
        Text = text, Style = (Style)Microsoft.UI.Xaml.Application.Current.Resources["MetadataTextStyle"],
        VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap
    };

    private void OnStatisticsSizeChanged(object sender, SizeChangedEventArgs e) => ArrangeCards();
    private void OnStatisticsViewportSizeChanged(object sender, SizeChangedEventArgs e) => FitViewport();

    private void OnStatusSizeChanged(object sender, SizeChangedEventArgs e)
    {
        bool compact = e.NewSize.Width < 320;
        StatusDial.Width = StatusDial.Height = compact ? 142 : 156;
        Grid.SetColumnSpan(StatusDial, compact ? 2 : 1);
        Grid.SetColumn(StatusLegend, compact ? 0 : 1);
        Grid.SetColumnSpan(StatusLegend, compact ? 2 : 1);
        Grid.SetRow(StatusLegend, compact ? 1 : 0);
        StatusContent.RowSpacing = compact ? 14 : 0;
        StatusContent.ColumnSpacing = compact ? 0 : 24;
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
        if (width <= 0) return;

        bool compactHeader = width < 700;
        StatisticsHeader.RowSpacing = compactHeader ? 10 : 0;
        Grid.SetRow(StatisticsFilters, compactHeader ? 1 : 0);
        Grid.SetColumn(StatisticsFilters, compactHeader ? 0 : 1);
        Grid.SetColumnSpan(StatisticsFilters, compactHeader ? 2 : 1);
        GameChoice.Width = compactHeader
            ? (width < 500 ? Math.Clamp(width - 42, 140, 440) : Math.Clamp(width - 280, 148, 220))
            : Math.Clamp(width - 460, 152, 212);

        bool compactTrendHeader = width < 400;
        TrendHeader.RowSpacing = compactTrendHeader ? 8 : 0;
        Grid.SetRow(TrendModeTrack, compactTrendHeader ? 1 : 0);
        Grid.SetColumn(TrendModeTrack, compactTrendHeader ? 0 : 1);
        Grid.SetColumnSpan(TrendModeTrack, compactTrendHeader ? 2 : 1);
        TrendModeTrack.HorizontalAlignment = compactTrendHeader ? HorizontalAlignment.Left : HorizontalAlignment.Right;

        // Asymmetric editorial KPI band:
        // mode 3 (wide >= 760): 1.55* Hero | 1* | 1* | 1*
        // mode 2 (medium 400..759): Hero full-width on row 0; 3 companions on row 1
        // mode 1 (ultra-narrow < 400): Hero full-width on row 0; 2+1 / stacked cleanly
        int nextSummaryMode = width >= 760 ? 3 : width >= 400 ? 2 : 1;
        if (nextSummaryMode != summaryMode)
        {
            summaryMode = nextSummaryMode;
            SummaryGrid.ColumnDefinitions.Clear();
            SummaryGrid.RowDefinitions.Clear();
            PeriodValue.FontSize = summaryMode == 3 ? 40 : 34;
            PeriodValue.LineHeight = summaryMode == 3 ? 44 : 38;
            double companionSize = summaryMode == 3 ? 26 : 22;
            double companionLine = summaryMode == 3 ? 32 : 28;
            LifetimeValue.FontSize = DaysValue.FontSize = GamesValue.FontSize = companionSize;
            LifetimeValue.LineHeight = DaysValue.LineHeight = GamesValue.LineHeight = companionLine;

            if (summaryMode == 3)
            {
                SummaryGrid.ColumnDefinitions.Add(new() { Width = new(1.52, GridUnitType.Star) });
                SummaryGrid.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
                SummaryGrid.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
                SummaryGrid.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
                SummaryGrid.RowDefinitions.Add(new() { Height = GridLength.Auto });

                PlaceMetric(PeriodCard, 0, 0, 1, new(0), new(2, 12, 24, 6), new(0));
                PlaceMetric(LifetimeCard, 1, 0, 1, new(1, 0, 0, 0), new(22, 0, 16, 0), new(0, 12, 0, 6));
                PlaceMetric(DaysCard, 2, 0, 1, new(1, 0, 0, 0), new(22, 0, 16, 0), new(0, 12, 0, 6));
                PlaceMetric(GamesCard, 3, 0, 1, new(1, 0, 0, 0), new(22, 0, 4, 0), new(0, 12, 0, 6));
            }
            else if (summaryMode == 2)
            {
                for (int i = 0; i < 3; i++) SummaryGrid.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
                SummaryGrid.RowDefinitions.Add(new() { Height = GridLength.Auto });
                SummaryGrid.RowDefinitions.Add(new() { Height = GridLength.Auto });

                PlaceMetric(PeriodCard, 0, 0, 3, new(0, 0, 0, 1), new(2, 10, 4, 14), new(0));
                PlaceMetric(LifetimeCard, 0, 1, 1, new(0), new(2, 0, 10, 0), new(0, 12, 0, 6));
                PlaceMetric(DaysCard, 1, 1, 1, new(1, 0, 0, 0), new(12, 0, 10, 0), new(0, 12, 0, 6));
                PlaceMetric(GamesCard, 2, 1, 1, new(1, 0, 0, 0), new(12, 0, 2, 0), new(0, 12, 0, 6));
            }
            else
            {
                SummaryGrid.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
                SummaryGrid.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
                SummaryGrid.RowDefinitions.Add(new() { Height = GridLength.Auto });
                SummaryGrid.RowDefinitions.Add(new() { Height = GridLength.Auto });
                SummaryGrid.RowDefinitions.Add(new() { Height = GridLength.Auto });

                PlaceMetric(PeriodCard, 0, 0, 2, new(0, 0, 0, 1), new(2, 10, 4, 12), new(0));
                PlaceMetric(LifetimeCard, 0, 1, 1, new(0, 0, 1, 1), new(2, 10, 10, 10), new(0));
                PlaceMetric(DaysCard, 1, 1, 1, new(0, 0, 0, 1), new(12, 10, 2, 10), new(0));
                PlaceMetric(GamesCard, 0, 2, 2, new(0), new(2, 10, 2, 6), new(0));
            }
        }

        // Card padding responds gracefully on compact windows.
        var cardPad = width < 520 ? new Thickness(16, 16, 16, 18) : new Thickness(24, 20, 24, 22);
        TrendCard.Padding = RankingCard.Padding = ProgressCard.Padding = cardPad;

        // Band 3: Ranking + Shelf Progress paired split (>= 800) or single column (< 800)
        int pairColumns = width < 800 ? 1 : 2;
        if (pairColumns != breakdownColumns)
        {
            breakdownColumns = pairColumns;
            BreakdownGrid.ColumnDefinitions.Clear();
            BreakdownGrid.RowDefinitions.Clear();
            BreakdownGrid.ColumnDefinitions.Add(new() { Width = new(pairColumns == 1 ? 1 : 1.32, GridUnitType.Star) });
            if (pairColumns == 2) BreakdownGrid.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
            BreakdownGrid.RowDefinitions.Add(new() { Height = GridLength.Auto });
            if (pairColumns == 1) BreakdownGrid.RowDefinitions.Add(new() { Height = GridLength.Auto });
            Grid.SetColumn(RankingCard, 0);
            Grid.SetRow(RankingCard, 0);
            Grid.SetColumn(ProgressCard, pairColumns == 1 ? 0 : 1);
            Grid.SetRow(ProgressCard, pairColumns == 1 ? 1 : 0);
        }

        // Band 4: Weekly Rhythm asymmetric landscape rail (>= 720) vs stacked (< 720)
        int nextWeekdayMode = width >= 720 ? 2 : 1;
        if (nextWeekdayMode != weekdayMode)
        {
            weekdayMode = nextWeekdayMode;
            WeekdayLayout.ColumnDefinitions.Clear();
            WeekdayLayout.RowDefinitions.Clear();
            if (weekdayMode == 2)
            {
                WeekdayLayout.ColumnDefinitions.Add(new() { Width = new(232) });
                WeekdayLayout.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
                WeekdayLayout.RowDefinitions.Add(new() { Height = GridLength.Auto });
                Grid.SetColumn(WeekdayAside, 0);
                Grid.SetRow(WeekdayAside, 0);
                Grid.SetColumn(WeekdayChart, 1);
                Grid.SetRow(WeekdayChart, 0);
                WeekdayChart.Height = 188;
                WeekdayCard.Padding = new(4, 22, 4, 22);
            }
            else
            {
                WeekdayLayout.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
                WeekdayLayout.RowDefinitions.Add(new() { Height = GridLength.Auto });
                WeekdayLayout.RowDefinitions.Add(new() { Height = GridLength.Auto });
                Grid.SetColumn(WeekdayAside, 0);
                Grid.SetRow(WeekdayAside, 0);
                Grid.SetColumn(WeekdayChart, 0);
                Grid.SetRow(WeekdayChart, 1);
                WeekdayChart.Height = 166;
                WeekdayCard.Padding = new(2, 18, 2, 18);
            }
        }
    }

    private static void PlaceMetric(Border cell, int column, int row, int colSpan, Thickness border, Thickness padding, Thickness margin)
    {
        Grid.SetColumn(cell, column);
        Grid.SetRow(cell, row);
        Grid.SetColumnSpan(cell, colSpan);
        cell.BorderThickness = border;
        cell.Padding = padding;
        cell.Margin = margin;
    }

    private async void OnGameChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!syncingGameChoice && GameChoice.SelectedOption is StatisticsGameOption game)
            await ViewModel.SelectGameAsync(game.Id);
    }

    private async void OnRefresh(object sender, RoutedEventArgs e) => await RefreshAsync(true);
    private async void OnImport(object sender, RoutedEventArgs e) { if (OpenImport is not null) await OpenImport(); }
    private async void OnOpenStatus(object sender, RoutedEventArgs e) { if (OpenStatus is not null) await OpenStatus(); }
    private void OnDailyExpanding(Expander sender, ExpanderExpandingEventArgs args) => DrawDays();
    public void Dispose() { if (disposed) return; disposed = true; ViewModel.PropertyChanged -= OnState; ViewModel.Dispose(); }
}
