using System.Collections.ObjectModel;
using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using TheOrder.Application;
using TheOrder.Presentation;

namespace TheOrder.WinUI.Views;

public sealed partial class DiscoveryFeedView : UserControl
{
    private const double DaySide = 26;
    private const double BadgeRadius = 6;
    private const double MinimumCalendarCardWidth = 116;
    private const double MaximumCalendarCardWidth = 168;
    private const double CalendarListInsets = 28;

    private readonly DiscoveryFeedViewModel viewModel;
    private readonly Func<string, string, Task> openWork;
    private readonly Dictionary<DateOnly, ToggleButton> dayButtons = new();
    private int displayedResultRevision;
    private DiscoverySection displayedSection = DiscoverySection.Search;
    private (DateOnly Month, DateOnly Today, int Revision, int Items)? displayedDays;
    private (ReleaseCalendarWindow Window, DateOnly Month, DateOnly? Selected, int Revision, int Items, int Columns)? displayedBands;
    private DateOnly? selectedDay;
    private ObservableCollection<DiscoveryCalendarBand> calendarBands = [];

    public DiscoveryFeedView(DiscoveryFeedViewModel viewModel, Func<string, string, Task> openWork)
    {
        this.viewModel = viewModel;
        this.openWork = openWork;
        InitializeComponent();
        ConfigureRankingFilters();
        FeedPageFooter.LoadNext = () => viewModel.LoadAsync(true);
        RailHost.Content = null;
        DetachedHeaders.Children.Clear();
        Loaded += (_, _) => { viewModel.PropertyChanged += OnStateChanged; Render(); };
        Unloaded += (_, _) => viewModel.PropertyChanged -= OnStateChanged;
        FeedBody.SizeChanged += (_, _) =>
        {
            ArrangeRail();
            UpdateLoadingIndicator();
            RenderCalendarBands();
        };
        FeedHeader.SizeChanged += (_, _) => UpdateLoadingIndicator();
    }

    public void FocusResults() => FeedList.Focus(FocusState.Programmatic);

    private void OnStateChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => Render();

    private void Render()
    {
        var section = viewModel.Section;
        if (displayedSection != section)
        {
            displayedSection = section;
            displayedResultRevision = viewModel.ResultRevision;
            var templateKey = section switch
            {
                DiscoverySection.Calendar => "CalendarTemplate",
                DiscoverySection.Rankings => "RankingTemplate",
                DiscoverySection.News => "NewsTemplate",
                _ => null
            };
            FeedList.ItemsSource = null;
            FeedList.ItemTemplate = templateKey is null ? null : (DataTemplate)Resources[templateKey];
            FeedList.ItemContainerStyle = (Style)Resources[section == DiscoverySection.Calendar ? "CalendarRow" : "FeedRow"];
            FeedList.IsItemClickEnabled = section != DiscoverySection.Calendar;
            RankingHeader.Visibility = Visible(section == DiscoverySection.Rankings);
            FeedList.ItemsSource = section switch
            {
                DiscoverySection.Calendar => calendarBands,
                DiscoverySection.Rankings or DiscoverySection.News => viewModel.Items,
                _ => null
            };
            Heading.Text = UiText.Get(section switch
            {
                DiscoverySection.Calendar => "发售月历",
                DiscoverySection.Rankings => "评分榜",
                DiscoverySection.News => "Galgame 资讯",
                _ => ""
            });
            AutomationProperties.SetName(FeedList, Heading.Text);
            ArrangeRail();
            if (section != DiscoverySection.Search) InteractionMotion.EnterPage(FeedBody);
        }

        UpdateRankingFilterCaption();
        var firstLoad = viewModel.Busy && !viewModel.LoadingMore;
        UpdateLoadingIndicator();
        Status.Text = firstLoad ? "" : viewModel.Message;
        Status.Visibility = Visible(Status.Text.Length > 0);
        ReloadButton.IsEnabled = !viewModel.Busy;
        var reload = UiText.Get(viewModel.Failed && !viewModel.CanLoadMore ? "重试" : "刷新");
        AutomationProperties.SetName(ReloadButton, reload);
        ToolTipService.SetToolTip(ReloadButton, reload);
        if (section == DiscoverySection.Calendar) RenderCalendar();
        FeedPageFooter.Update(viewModel.PaginationCursor is { } cursor ? (section, cursor) : null,
            viewModel.Busy, viewModel.Failed);
        if (!viewModel.Busy && displayedResultRevision != viewModel.ResultRevision)
        {
            displayedResultRevision = viewModel.ResultRevision;
            if (viewModel.Items.Count > 0)
                InteractionMotion.RevealContent(FeedList.ItemsPanelRoot as FrameworkElement ?? FeedList);
        }
    }

    private void ArrangeRail()
    {
        var rail = displayedSection switch
        {
            DiscoverySection.Calendar => CalendarRail,
            DiscoverySection.News => NewsRail,
            _ => null
        };
        var wide = rail is not null && FeedBody.ActualWidth >= 720;
        var aside = wide ? rail : null;
        var header = wide ? null : rail;
        if (ReferenceEquals(RailHost.Content, aside) && ReferenceEquals(FeedHeader.Content, header)) return;

        RailHost.Content = null;
        FeedHeader.Content = null;
        RailColumn.Width = new GridLength(wide ? displayedSection == DiscoverySection.News ? 190 : 282 : 0);
        RailHost.Content = aside;
        FeedHeader.Content = header;
        FeedHeader.Visibility = Visible(header is not null);
        NewsRail.Orientation = wide ? Orientation.Vertical : Orientation.Horizontal;
    }

    private void UpdateLoadingIndicator()
    {
        var hasHeader = FeedHeader.Content is not null;
        var headerHeight = hasHeader ? FeedHeader.ActualHeight + FeedHeader.Margin.Top + FeedHeader.Margin.Bottom : 0;
        var loading = displayedSection != DiscoverySection.Search && viewModel.Busy && !viewModel.LoadingMore;
        var large = loading && viewModel.Items.Count == 0 && FeedBody.ActualHeight - headerHeight >= 160
            && (!hasHeader || FeedHeader.ActualHeight > 0);
        var compact = loading && !large;

        if (large) FeedLoadingState.Margin = new Thickness(0, headerHeight, 0, 0);
        FeedLoadingState.Visibility = FeedLoadingMark.Visibility = Visible(large);
        FeedLoadingMark.IsActive = large;
        FeedProgress.Visibility = Visible(compact);
        FeedProgress.IsActive = compact;
        ReloadIcon.Visibility = Visible(!compact);
    }

    private void RenderCalendar()
    {
        var window = viewModel.CalendarWindow;
        var isMonth = window == ReleaseCalendarWindow.Month;
        var year = window == ReleaseCalendarWindow.Year;
        var month = viewModel.Month;
        CalendarPeriod.Text = year ? UiText.Format($"{month.Year} 年")
            : UiText.Format($"{month.Year} 年 {month.Month} 月");
        MonthNavigator.Visibility = Visible(window != ReleaseCalendarWindow.Undated);
        CalendarMonthGrid.Visibility = Visible(isMonth);
        CalendarAllButton.Visibility = Visible(isMonth);
        TodayButton.Visibility = MonthNavigator.Visibility;
        PreviousButton.IsEnabled = viewModel.CanStep(-1);
        NextButton.IsEnabled = viewModel.CanStep(1);
        AutomationProperties.SetName(PreviousButton, UiText.Get(year ? "上一年" : "上个月"));
        AutomationProperties.SetName(NextButton, UiText.Get(year ? "下一年" : "下个月"));
        TodayButton.Content = UiText.Get(year ? "今年" : "本月");
        TodayButton.IsEnabled = month.Year != viewModel.Today.Year || !year && month.Month != viewModel.Today.Month;
        if (!isMonth || selectedDay is { } day && (day.Year != month.Year || day.Month != month.Month
            || !viewModel.Items.Any(item => item.ReleaseDay == day))) selectedDay = null;

        var key = (month, viewModel.Today, viewModel.ResultRevision, viewModel.Items.Count);
        if (isMonth && displayedDays != key)
        {
            displayedDays = key;
            BuildCalendarDays(month);
        }
        else if (!isMonth) displayedDays = null;

        RefreshDaySelection();
        RenderCalendarBands();
    }

    private void BuildCalendarDays(DateOnly month)
    {
        CalendarDays.Children.Clear();
        CalendarDays.ColumnDefinitions.Clear();
        CalendarDays.RowDefinitions.Clear();
        dayButtons.Clear();
        var counts = viewModel.Items.Where(item => item.ReleaseDay.HasValue)
            .GroupBy(item => item.ReleaseDay!.Value)
            .ToDictionary(group => group.Key, group => group.Count());
        var headings = new[] { "日", "一", "二", "三", "四", "五", "六" };
        CalendarDays.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        foreach (var heading in headings)
        {
            var column = CalendarDays.ColumnDefinitions.Count;
            CalendarDays.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var text = new TextBlock { Text = UiText.Get(heading), FontSize = 11, TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 0, BadgeRadius, 2) };
            text.Style = (Style)Microsoft.UI.Xaml.Application.Current.Resources["MetadataTextStyle"];
            Grid.SetColumn(text, column);
            CalendarDays.Children.Add(text);
        }

        var first = (int)month.DayOfWeek;
        var days = DateTime.DaysInMonth(month.Year, month.Month);
        var weeks = (first + days + 6) / 7;
        for (var week = 0; week < weeks; week++) CalendarDays.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (var index = 0; index < days; index++)
        {
            var day = month.AddDays(index);
            var count = counts.GetValueOrDefault(day);
            var cell = CreateCalendarDay(day, count);
            Grid.SetRow(cell, (first + index) / 7 + 1);
            Grid.SetColumn(cell, (first + index) % 7);
            CalendarDays.Children.Add(cell);
        }
    }

    private FrameworkElement CreateCalendarDay(DateOnly day, int count)
    {
        var layout = new Grid { Width = DaySide + BadgeRadius, Height = DaySide + BadgeRadius };
        var date = new TextBlock { Text = day.Day.ToString(CultureInfo.CurrentCulture),
            FontSize = 13, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var frame = new Border
        {
            Style = (Style)Resources[day == viewModel.Today ? "CalendarTodayFrame" : "CalendarDayFrame"],
            Margin = new Thickness(0, BadgeRadius, BadgeRadius, 0)
        };
        layout.Children.Add(frame);
        if (count == 0)
        {
            date.Style = (Style)Microsoft.UI.Xaml.Application.Current.Resources["MetadataTextStyle"];
            frame.Child = date;
            return layout;
        }

        var cell = new ToggleButton
        {
            Style = (Style)Resources["CalendarDayButton"],
            Content = date
        };
        var name = UiText.Format($"{day.Month}月{day.Day}日 · {count} 部");
        AutomationProperties.SetName(cell, name);
        ToolTipService.SetToolTip(cell, name);
        cell.Click += (_, _) => ToggleDay(day);
        dayButtons.Add(day, cell);
        frame.Child = cell;
        var badge = new Border
        {
            Style = (Style)Resources["CalendarReleaseBadge"],
            Width = 2 * BadgeRadius,
            Height = 2 * BadgeRadius,
            CornerRadius = new CornerRadius(BadgeRadius),
            // Center the badge on the stroke centerlines, including fractional strokes.
            Margin = new Thickness(0, frame.BorderThickness.Top / 2, frame.BorderThickness.Right / 2, 0),
            Child = new Viewbox
            {
                StretchDirection = StretchDirection.DownOnly,
                Child = new TextBlock
                {
                    Text = count.ToString(CultureInfo.CurrentCulture),
                    Style = (Style)Resources["CalendarReleaseBadgeText"]
                }
            }
        };
        ToolTipService.SetToolTip(badge, name);
        badge.Tapped += (_, _) =>
        {
            cell.Focus(FocusState.Pointer);
            ToggleDay(day);
        };
        layout.Children.Add(badge);
        return layout;
    }

    private void RefreshDaySelection()
    {
        foreach (var (day, button) in dayButtons) button.IsChecked = day == selectedDay;
        CalendarAllButton.IsEnabled = selectedDay is not null;
        CalendarCount.Text = viewModel.Busy && viewModel.Items.Count == 0 ? "" : selectedDay is { } selected
            ? UiText.Format($"{selected.Month}月{selected.Day}日 · {viewModel.Items.Count(item => item.ReleaseDay == selected)} 部")
            : UiText.Format($"已载入 {viewModel.Items.Count} 部");
    }

    private void RenderCalendarBands()
    {
        if (displayedSection != DiscoverySection.Calendar) return;
        // The shared list can still be measured at the previous tab's width.
        var width = FeedBody.ActualWidth - RailColumn.Width.Value - CalendarListInsets;
        if (width <= 0) return;
        var spacing = (double)Resources["CalendarCardSpacing"];
        var columns = Math.Clamp((int)Math.Floor((width + spacing) / (MinimumCalendarCardWidth + spacing)), 1, 7);
        var cardWidth = Math.Floor((width - (columns - 1) * spacing) / columns);
        ((CalendarCardMetrics)Resources["CalendarCardMetrics"]).Width = Math.Clamp(cardWidth, 1, MaximumCalendarCardWidth);

        var key = (viewModel.CalendarWindow, viewModel.Month, selectedDay, viewModel.ResultRevision, viewModel.Items.Count, columns);
        if (displayedBands == key) return;
        var updating = displayedBands is { } previous && previous.Window == key.CalendarWindow && previous.Month == key.Month
            && previous.Selected == key.selectedDay && previous.Columns == columns;
        displayedBands = key;

        var bands = viewModel.Items
            .Where(item => selectedDay is null || item.ReleaseDay == selectedDay)
            .GroupBy(item => (item.ReleaseDay, item.Leading))
            .OrderBy(group => group.Key.ReleaseDay is null)
            .ThenBy(group => group.Key.ReleaseDay)
            .SelectMany(group => group.Chunk(columns)
                .Select((items, index) => new DiscoveryCalendarBand(index == 0 ? group.Key.Leading : "", items)))
            .ToArray();
        if (!updating)
        {
            calendarBands = new(bands);
            FeedList.ItemsSource = calendarBands;
            if (bands.Length > 0) InteractionMotion.RevealContent(FeedList.ItemsPanelRoot as FrameworkElement ?? FeedList, .74f);
            return;
        }
        for (var index = 0; index < bands.Length; index++)
        {
            if (index == calendarBands.Count) calendarBands.Add(bands[index]);
            else if (calendarBands[index].Heading != bands[index].Heading || !calendarBands[index].Works.SequenceEqual(bands[index].Works))
                calendarBands[index] = bands[index];
        }
        while (calendarBands.Count > bands.Length) calendarBands.RemoveAt(calendarBands.Count - 1);
    }

    internal void ToggleDay(DateOnly day)
    {
        selectedDay = selectedDay == day ? null : day;
        RefreshDaySelection();
        RenderCalendarBands();
    }

    private void OnAllDays(object sender, RoutedEventArgs e)
    {
        selectedDay = null;
        RefreshDaySelection();
        RenderCalendarBands();
    }

    private static Visibility Visible(bool value) => value ? Visibility.Visible : Visibility.Collapsed;

    private async void OnCalendarWindow(object sender, SelectionChangedEventArgs e)
    {
        if (IsLoaded && CalendarWindowBox.OptionIndex >= 0)
            await viewModel.SelectCalendarWindowAsync((ReleaseCalendarWindow)CalendarWindowBox.OptionIndex);
    }

    private async void OnRankingSource(object sender, SelectionChangedEventArgs e)
    {
        if (IsLoaded && RankingSourceBox.SelectedOption is ComboBoxItem { Tag: string key }
            && Enum.TryParse<DiscoveryRankingSource>(key, out var source))
            await viewModel.SelectRankingSourceAsync(source);
    }

    private async void OnNewsLane(object sender, SelectionChangedEventArgs e)
    {
        if (IsLoaded && NewsLaneBox.OptionIndex >= 0)
            await viewModel.SelectNewsLaneAsync((DiscoveryNewsLane)NewsLaneBox.OptionIndex);
    }

    private async void OnNewsSource(object sender, SelectionChangedEventArgs e)
    {
        if (IsLoaded && NewsSourceBox.OptionIndex >= 0)
            await viewModel.SelectNewsSourceAsync((DiscoveryNewsSource)NewsSourceBox.OptionIndex);
    }

    private async void OnPrevious(object sender, RoutedEventArgs e) => await viewModel.StepAsync(-1);
    private async void OnNext(object sender, RoutedEventArgs e) => await viewModel.StepAsync(1);
    private async void OnToday(object sender, RoutedEventArgs e) => await viewModel.ReturnToCurrentMonthAsync();
    private async void OnReload(object sender, RoutedEventArgs e) => await viewModel.ReloadAsync();

    private async void OnCalendarWork(object sender, RoutedEventArgs e)
    {
        viewModel.InvalidateIfStale();
        if (sender is Button { Tag: string id } && viewModel.Items.FirstOrDefault(item => item.Id == id) is { } item)
            await openWork(item.Id, item.Title);
    }

    private async void OnItem(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is DiscoveryRow item) await OpenFeedItemAsync(item);
    }

    private async void OnArticleLink(object sender, RoutedEventArgs e)
    {
        if (sender is ExternalLinkButton { Tag: DiscoveryRow item }) await OpenFeedItemAsync(item);
    }

    private async Task OpenFeedItemAsync(DiscoveryRow item)
    {
        viewModel.InvalidateIfStale();
        if (!viewModel.Items.Contains(item)) return;
        if (!item.IsArticle) await openWork(item.Id, item.Title);
        else if (item.ArticleUri is { } uri)
        {
            try { if (!await Windows.System.Launcher.LaunchUriAsync(uri)) viewModel.ReportLinkFailure(); }
            catch (Exception) { viewModel.ReportLinkFailure(); }
        }
    }

    public sealed record DiscoveryCalendarBand(string Heading, DiscoveryRow[] Works)
    {
        public Visibility HeadingVisibility => Visible(Heading.Length != 0);
    }
}

public sealed class CalendarCardMetrics : ObservableState
{
    private double width = 124;
    public double Width
    {
        get => width;
        internal set
        {
            if (Set(ref width, value)) Changed(nameof(CoverHeight));
        }
    }

    public double CoverHeight => Math.Round(Width * 161 / 124);
}
