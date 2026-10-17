using System.ComponentModel;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using TheOrder.Application;
using TheOrder.Presentation;
using Windows.System;

namespace TheOrder.WinUI.Views;

// Revision 2 Library. Normal browsing: SelectionMode None + item click opens Details. Selection mode
// (选择, Ctrl/Shift+click, Ctrl+A) switches to Multiple. Context menu / Ctrl+Enter launch through the
// window's QuickLaunch, which keeps the existing readiness rules.
public sealed partial class LibraryPage : Page, IImageHost
{
    public WorkspaceViewModel ViewModel { get; }
    public IImagePipeline Images { get; }
    public Func<string, Task>? OpenDetails { get; set; }
    public Func<Task<bool>>? ResolveUnsaved { get; set; }
    public Func<Task>? OpenImport { get; set; }
    public Func<GameSummary, Task>? QuickLaunch { get; set; }
    public Func<ImmutableArray<string>, Task>? RecognizeSelected { get; set; }
    public Func<ImmutableArray<string>, Task>? RemoveSelected { get; set; }
    public Func<Task>? OpenSettings { get; set; }
    public Func<string, Task>? OpenLaunchSettings { get; set; }
    private RecognitionViewModel? recognition;
    internal void ShowRecognition(RecognitionViewModel model)
    {
        recognition = model;
        var p = model.Progress;
        RecognitionStatus.Visibility = model.HasRecognitionWork ? Visibility.Visible : Visibility.Collapsed;
        RecognitionCounts.Text = model.ProgressText; RecognitionMessage.Text = model.Message;
        RecognitionBar.Maximum = Math.Max(1, p.Total); RecognitionBar.Value = p.Processed;
        CancelRecognition.Visibility = model.HasRecognitionWork ? Visibility.Visible : Visibility.Collapsed;
        UpdateNoticeRegion();
    }
    private void OnCancelRecognition(object sender, RoutedEventArgs e) => recognition?.CancelAll();
    internal void ShowCatalogReminder(CatalogAccessNotice? notice)
    { ApiReminder.Title = notice?.Title ?? ""; ApiReminder.Message = notice?.Message ?? ""; ApiReminder.IsOpen = notice is not null; UpdateNoticeRegion(); }
    private async void OnApiSettings(object sender, RoutedEventArgs e) { if (OpenSettings is { } open) await open(); }
    private void UpdateNoticeRegion()
    {
        if (NoticePanel is null) return;
        DiscoveryNotice.Visibility = Visible(DiscoveryNotice.IsOpen);
        ApiReminder.Visibility = Visible(ApiReminder.IsOpen);
        var any = DiscoveryNotice.IsOpen || ApiReminder.IsOpen || RecognitionStatus.Visibility == Visibility.Visible;
        NoticePanel.Visibility = Visible(any);
        var narrow = ActualWidth is > 0 and < 641;
        NoticePanel.Margin = any ? (narrow ? new Thickness(16, 12, 16, 0) : new Thickness(24, 12, 24, 0)) : new Thickness(0);
    }
    private bool synchronizing = true, selectionMode, keyboardOpen, restorePending;
    private string? scrollKey, lastOpenedId;
    private long returnRestorationVersion;
    private readonly bool diagnostics;
    private readonly Stopwatch display = Stopwatch.StartNew();
    private readonly ConditionalWeakTable<object, object> seenContainers = new();
    private int createdContainers;
    private double firstDisplay;
    private int maxRealized;
    private string measuredLayout = "";
    private double modeStarted;
    private ListViewBase Active => ViewModel.Layout == "grid" ? GameGrid : GameList;

    public LibraryPage(WorkspaceViewModel viewModel, bool diagnostics, IImagePipeline images)
    {
        ViewModel = viewModel; Images = images; this.diagnostics = diagnostics; InitializeComponent();
        BuildContinueBanner();
        DiscoveryNotice.RegisterPropertyChangedCallback(InfoBar.IsOpenProperty, (_, _) => UpdateNoticeRegion());
        ApiReminder.RegisterPropertyChangedCallback(InfoBar.IsOpenProperty, (_, _) => UpdateNoticeRegion());
        UpdateNoticeRegion();
        foreach (var list in new ListViewBase[] { GameGrid, GameList })
        {
            list.AddHandler(PointerPressedEvent, new PointerEventHandler(OnListPointerPressed), true);
        }
        CategoryFilter.KeyDown += OnFilterKey; TagFilter.KeyDown += OnFilterKey;
        Loaded += OnLoaded; Unloaded += OnUnloaded;
        Synchronize();
    }
    public Visibility Visible(bool value) => value ? Visibility.Visible : Visibility.Collapsed;
    private void OnFilterKey(object sender, KeyRoutedEventArgs e) { if (e.Key == VirtualKey.Enter) { e.Handled = true; FilterFlyout.Hide(); FocusResults(); } }
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ViewModel.PropertyChanged += OnState; Synchronize();
        if (diagnostics) { DiagnosticsText.Visibility = Visibility.Visible; LayoutUpdated += OnLayout; }
        if (restorePending) _ = RestoreReturnAsync();
    }
    private void OnUnloaded(object sender, RoutedEventArgs e) { ViewModel.PropertyChanged -= OnState; LayoutUpdated -= OnLayout; }
    private void OnState(object? sender, PropertyChangedEventArgs e)
    {
        // Normal browsing (SelectionMode None) holds no selection; SelectedItems may only be touched in selection mode.
        if (e.PropertyName == nameof(WorkspaceViewModel.SelectionVersion) && selectionMode) { GameGrid.SelectedItems.Clear(); GameList.SelectedItems.Clear(); }
        if (e.PropertyName is nameof(WorkspaceViewModel.Items) or nameof(WorkspaceViewModel.Layout) or nameof(WorkspaceViewModel.Query))
        {
            // Explicit query/reload changes already clear SelectionVersion. A live Session
            // projection only updates records: retain selection by ID across ItemsSource rebinding.
            var selected = selectionMode ? Active.SelectedItems.OfType<GameSummary>().Select(i => i.Id).ToHashSet(StringComparer.Ordinal) : [];
            Synchronize();
            if (selectionMode && selected.Count > 0)
                foreach (var item in ViewModel.Items.Where(i => selected.Contains(i.Id)))
                    if (!Active.SelectedItems.Contains(item)) Active.SelectedItems.Add(item);
        }
        if (e.PropertyName is nameof(WorkspaceViewModel.IsEmpty) or nameof(WorkspaceViewModel.EmptyText) or nameof(WorkspaceViewModel.Items)) UpdateEmptyActions();
        UpdateSelectionChrome();
    }

    public int SearchLength => LibrarySearch.Text.Length;
    public string SearchFocus => LibrarySearch.FocusState.ToString();
    public void OpenSearch() => SearchFlyout.ShowAt(SearchButton);
    private void OnSearchOpened(object? sender, object e) => LibrarySearch.Focus(FocusState.Keyboard);
    private async void OnSearchChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (synchronizing || args.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;
        CancelReturnRestorationForSearch();
        try { await ViewModel.SetQueryAsync(ViewModel.Query with { Text = sender.Text }); }
        catch (Exception) { ViewModel.ReportUnexpected(); }
    }
    private void OnSearchSubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    { SearchFlyout.Hide(); FocusResults(); }
    // ---- Query chrome ----------------------------------------------------------------------------------
    private static string Value(ComboBox box, string fallback = "all") => (box.SelectedItem as ComboBoxItem)?.Tag as string ?? fallback;
    private static void Select(ComboBox box, string value) => box.SelectedItem = box.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (i.Tag as string ?? "") == value);
    private static readonly Dictionary<string, string> sortNames = new() { ["added"] = UiText.Get("最近添加"), ["recent"] = UiText.Get("最近游玩"), ["name"] = UiText.Get("名称"), ["rating"] = UiText.Get("我的评分"), ["playtime"] = UiText.Get("游玩时长") };
    private void Synchronize()
    {
        synchronizing = true;
        var q = ViewModel.Query;
        if (LibrarySearch.Text != q.Text) LibrarySearch.Text = q.Text;
        if (CategoryFilter.Text != q.Category) CategoryFilter.Text = q.Category;
        if (TagFilter.Text != q.Tag) TagFilter.Text = q.Tag;
        FavoriteFilter.IsChecked = q.Favorites;
        Select(ViewChoice, q.View); Select(StatusChoice, q.Status); Select(MinimumChoice, q.MinRating);
        foreach (var item in SortMenu.Items.OfType<RadioMenuFlyoutItem>()) item.IsChecked = (item.Tag as string) == q.Sort;
        SortLabel.Text = sortNames.GetValueOrDefault(q.Sort, UiText.Get("最近添加"));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(SortButton, UiText.Get("作品排序：") + SortLabel.Text);
        var grid = ViewModel.Layout == "grid";
        if (measuredLayout != ViewModel.Layout)
        { measuredLayout = ViewModel.Layout; firstDisplay = 0; maxRealized = 0; modeStarted = measuredLayout == "grid" && createdContainers == 0 ? 0 : display.Elapsed.TotalMilliseconds; }
        GameGrid.Visibility = Visible(grid); GameList.Visibility = Visible(!grid); ListHeader.Visibility = Visible(!grid);
        if (grid) { GameList.ItemsSource = null; if (!ReferenceEquals(GameGrid.ItemsSource, ViewModel.Items)) GameGrid.ItemsSource = ViewModel.Items; }
        else { GameGrid.ItemsSource = null; if (!ReferenceEquals(GameList.ItemsSource, ViewModel.Items)) GameList.ItemsSource = ViewModel.Items; }
        synchronizing = false;
        UpdateBrowsingChrome();
        UpdateContinue();
        UpdateSelectionChrome();
        UpdateEmptyActions();
    }
    private void UpdateBrowsingChrome()
    {
        GridModeButton.IsChecked = ViewModel.Layout == "grid";
        ListModeButton.IsChecked = ViewModel.Layout == "list";
        var query = ViewModel.Query;
        var chips = new List<(string Label, Func<LibraryQuery, LibraryQuery> Remove)>();
        if (query.View != "all") chips.Add(((ViewChoice.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? query.View, q => q with { View = "all" }));
        if (query.Status != "all") chips.Add((StatusNames.Display(query.Status), q => q with { Status = "all" }));
        if (query.Favorites) chips.Add((UiText.Get("只看收藏"), q => q with { Favorites = false }));
        if (!string.IsNullOrWhiteSpace(query.Category)) chips.Add((UiText.Get("分类：") + query.Category, q => q with { Category = "" }));
        if (!string.IsNullOrWhiteSpace(query.Tag)) chips.Add((UiText.Get("标签：") + query.Tag, q => q with { Tag = "" }));
        if (!string.IsNullOrWhiteSpace(query.MinRating)) chips.Add(((MinimumChoice.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? query.MinRating, q => q with { MinRating = "" }));
        FilterChips.Children.Clear();
        foreach (var (label, remove) in chips)
        {
            var chip = new Button { CornerRadius = new CornerRadius(14), Padding = new Thickness(10, 3, 8, 4), MinHeight = 28 };
            var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            content.Children.Add(new TextBlock { Text = label, Style = (Style)Microsoft.UI.Xaml.Application.Current.Resources["CaptionTextBlockStyle"], VerticalAlignment = VerticalAlignment.Center });
            content.Children.Add(new FontIcon { Glyph = "", FontSize = 10, VerticalAlignment = VerticalAlignment.Center });
            chip.Content = content;
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(chip, UiText.Get("移除条件：") + label);
            chip.Click += async (_, _) => { try { await ViewModel.SetQueryAsync(remove(ViewModel.Query)); } catch (Exception) { ViewModel.ReportUnexpected(); } };
            FilterChips.Children.Add(chip);
        }
        ActiveFilterRegion.Visibility = Visible(chips.Count > 0);
        FilterBadge.Value = chips.Count; FilterBadge.Visibility = Visible(chips.Count > 0);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(FilterButton, chips.Count == 0 ? UiText.Get("展开作品筛选") : UiText.Format($"展开作品筛选，当前 {chips.Count} 项条件"));
    }
    private void UpdateEmptyActions()
    {
        EmptyImport.Visibility = Visible(ViewModel.IsEmpty && ViewModel.TotalCount == 0);
        EmptyClear.Visibility = Visible(ViewModel.IsEmpty && ViewModel.TotalCount > 0);
    }
    private Task ApplyQuery() => synchronizing ? Task.CompletedTask : ViewModel.SetQueryAsync(ViewModel.Query with
    {
        View = Value(ViewChoice), Status = Value(StatusChoice), Favorites = FavoriteFilter.IsChecked == true,
        Category = CategoryFilter.Text, Tag = TagFilter.Text, MinRating = Value(MinimumChoice, "")
    });
    private async void OnFilterText(object sender, TextChangedEventArgs e) => await ApplyQuery();
    private async void OnFilter(object sender, SelectionChangedEventArgs e) => await ApplyQuery();
    private async void OnFavoriteFilter(object sender, RoutedEventArgs e) => await ApplyQuery();
    private async void OnSortItem(object sender, RoutedEventArgs e)
    {
        if (sender is RadioMenuFlyoutItem { Tag: string sort } && sort != ViewModel.Query.Sort) await ViewModel.SetQueryAsync(ViewModel.Query with { Sort = sort });
    }
    private async void OnHeaderSort(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string sort } && sort != ViewModel.Query.Sort) await ViewModel.SetQueryAsync(ViewModel.Query with { Sort = sort });
    }
    private async void OnGrid(object sender, RoutedEventArgs e) { await ViewModel.ChangeAppearanceAsync(ViewModel.Theme, "grid"); UpdateBrowsingChrome(); }
    private async void OnList(object sender, RoutedEventArgs e) { await ViewModel.ChangeAppearanceAsync(ViewModel.Theme, "list"); UpdateBrowsingChrome(); }
    private async void OnReload(object sender, RoutedEventArgs e) { if (ResolveUnsaved is null || await ResolveUnsaved()) await ViewModel.ReloadAsync(); }
    private async void OnImport(object sender, RoutedEventArgs e) { try { if (OpenImport is not null) await OpenImport(); } catch (Exception) { ViewModel.ReportUnexpected(); } }
    private async void OnClear(object sender, RoutedEventArgs e) { SavedChoice.SelectedItem = null; await ViewModel.SetQueryAsync(new(Sort: ViewModel.Query.Sort)); }
    private async void OnSavedFilter(object sender, SelectionChangedEventArgs e) { if (SavedChoice.SelectedItem is SavedFilter saved) await ViewModel.SetQueryAsync(saved.Query); }
    private void OnFinishFilter(object sender, RoutedEventArgs e) => FilterFlyout.Hide();
    private async void OnSaveFilter(object sender, RoutedEventArgs e)
    {
        FilterFlyout.Hide();
        var name = new TextBox { Header = UiText.Get("名称"), PlaceholderText = UiText.Get("例如：周末想玩的作品"), MaxLength = 80 };
        var dialog = new ContentDialog { XamlRoot = XamlRoot, RequestedTheme = ActualTheme, Title = UiText.Get("保存当前筛选"), Content = name, PrimaryButtonText = UiText.Get("保存"), CloseButtonText = UiText.Get("取消"), DefaultButton = ContentDialogButton.Primary };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary) await ViewModel.SaveFilterAsync(name.Text);
    }
    private void OnFilterOpening(object? sender, object e) => ConstrainFilterHeight();
    private void ConstrainFilterHeight()
    {
        if (XamlRoot?.Content is not UIElement root) return;
        var anchor = FilterButton.TransformToVisual(root).TransformPoint(new Windows.Foundation.Point());
        var below = XamlRoot.Size.Height - anchor.Y - FilterButton.ActualHeight - 36;
        var above = anchor.Y - 36;
        var useAbove = below < 180 && above > below;
        FilterFlyout.Placement = useAbove ? FlyoutPlacementMode.TopEdgeAlignedRight : FlyoutPlacementMode.BottomEdgeAlignedRight;
        // A finite constraint is required: MaxHeight alone leaves the star row
        // measured as content in a popup and can push the fixed footer offscreen.
        FilterPanel.Height = FilterPanel.MaxHeight = Math.Clamp(useAbove ? above : below, 128, 520);
    }
    internal void ShowVisualFilter() => FilterFlyout.ShowAt(FilterButton);
    internal void HideVisualFilter() => FilterFlyout.Hide();

    // ---- Responsive layout -------------------------------------------------------------------------------
    public Func<Task>? OpenPendingReview { get; set; }
    internal void ShowPendingReviews(int count)
    {
        PendingReviewButton.Content = UiText.Format($"待核对 {count}");
        PendingReviewButton.Visibility = Visible(count > 0);
    }
    private async void OnPendingReview(object sender, RoutedEventArgs e)
    { try { if (OpenPendingReview is not null) await OpenPendingReview(); } catch { ViewModel.ReportUnexpected(); } }
    private void OnLibrarySizeChanged(object sender, SizeChangedEventArgs e)
    {
        var compact = e.NewSize.Width < 900; var narrow = e.NewSize.Width < 641;
        HeaderRow.Padding = narrow ? new Thickness(16, 12, 12, 8) : new Thickness(32, 16, 24, 8);
        // Keep the page title whole when the compact command row consumes the available width.
        // The second Auto row is empty at desktop widths, preserving the normal layout.
        HeaderRow.RowSpacing = narrow ? 8 : 0;
        Grid.SetColumnSpan(HeaderIdentity, narrow ? 2 : 1);
        Grid.SetRow(HeaderCommands, narrow ? 1 : 0);
        Grid.SetColumn(HeaderCommands, narrow ? 0 : 1);
        Grid.SetColumnSpan(HeaderCommands, narrow ? 2 : 1);
        HeaderCommands.HorizontalAlignment = narrow ? HorizontalAlignment.Left : HorizontalAlignment.Right;
        ActiveFilterRegion.Padding = narrow ? new Thickness(16, 0, 12, 8) : new Thickness(32, 0, 24, 8);
        SelectionContext.Margin = narrow ? new Thickness(8, 0, 8, 8) : new Thickness(24, 0, 24, 8);
        SelectionContext.RowSpacing = narrow ? 6 : 0;
        Grid.SetRow(ContextActions, narrow ? 1 : 0);
        Grid.SetColumn(ContextActions, narrow ? 0 : 1);
        Grid.SetColumnSpan(ContextActions, narrow ? 3 : 1);
        ContextActions.Margin = narrow ? new Thickness(0) : new Thickness(12, 0, 0, 0);
        SearchLabel.Visibility = FilterLabel.Visibility = SelectLabel.Visibility = ImportLabel.Visibility = Visible(!compact);
        SortLabel.Visibility = Visible(!narrow);
        SelectButton.Visibility = ImportButton.Visibility = Visible(!compact);
        MoreSelect.Visibility = MoreImport.Visibility = Visible(compact);
        SelectionHint.Visibility = Visible(!compact);
        GameGrid.Padding = narrow ? new Thickness(6, 0, 6, 16) : new Thickness(18, 0, 18, 24);
        GameList.Padding = narrow ? new Thickness(8, 0, 8, 16) : new Thickness(24, 0, 24, 24);
        ListHeader.Margin = new Thickness(GameList.Padding.Left, 0, GameList.Padding.Right, 0);
        UpdateNoticeRegion();
        UpdateListHeaderColumns(ListHeader.ActualWidth);
        if (FilterFlyout.IsOpen) ConstrainFilterHeight();
    }
    private void OnListHeaderSizeChanged(object sender, SizeChangedEventArgs e) => UpdateListHeaderColumns(e.NewSize.Width);
    private void UpdateListHeaderColumns(double headerWidth)
    {
        if (ListHeader is null || ListHeader.ColumnDefinitions.Count < 6 || headerWidth <= 0) return;
        var innerWidth = headerWidth - ListHeader.Padding.Left - ListHeader.Padding.Right;
        var compact = innerWidth < 560;
        ListHeader.ColumnSpacing = compact ? 0 : 16;
        if (HeaderNameButton is not null) HeaderNameButton.Margin = compact ? new Thickness(16, 0, 0, 0) : new Thickness(0);
        ListHeader.ColumnDefinitions[2].Width = compact ? new GridLength(0) : new GridLength(84);
        ListHeader.ColumnDefinitions[3].Width = compact ? new GridLength(0) : new GridLength(68);
        ListHeader.ColumnDefinitions[4].Width = compact ? new GridLength(0) : new GridLength(88);
        ListHeader.ColumnDefinitions[5].Width = compact ? new GridLength(0) : new GridLength(104);
        HeaderStatus.Visibility = HeaderRatingButton.Visibility = HeaderPlayTimeButton.Visibility = HeaderLastPlayedButton.Visibility = Visible(!compact);
    }
    private void OnRowSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is not Grid row || row.ColumnDefinitions.Count < 6) return;
        var compact = e.NewSize.Width < 560;
        row.ColumnSpacing = compact ? 0 : 16;
        row.ColumnDefinitions[2].Width = compact ? new GridLength(0) : new GridLength(84);
        row.ColumnDefinitions[3].Width = compact ? new GridLength(0) : new GridLength(68);
        row.ColumnDefinitions[4].Width = compact ? new GridLength(0) : new GridLength(88);
        row.ColumnDefinitions[5].Width = compact ? new GridLength(0) : new GridLength(104);
        for (int i = 2; i < row.Children.Count; i++)
            if (row.Children[i] is FrameworkElement child && Grid.GetColumn(child) >= 2)
                child.Visibility = Visible(!compact);
        if (row.Children[1] is Panel titles)
        {
            titles.Margin = compact ? new Thickness(16, 0, 0, 0) : new Thickness(0);
            if (titles.Children.Count > 2 && titles.Children[2] is FrameworkElement narrowMeta)
                narrowMeta.Visibility = Visible(compact);
        }
    }
    private ItemsWrapGrid? wrapGrid;
    private bool metricsQueued;
    private void OnWrapGridLoaded(object sender, RoutedEventArgs e) { wrapGrid = sender as ItemsWrapGrid; QueueGridMetrics(); }
    private void OnGridSizeChanged(object sender, SizeChangedEventArgs e) => QueueGridMetrics();
    // Item size is applied after the current layout pass, once per burst of size changes.
    private void QueueGridMetrics()
    {
        if (metricsQueued) return;
        metricsQueued = DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => { metricsQueued = false; ApplyGridMetrics(); });
    }
    // Adaptive columns: n = floor((W + 12) / (156 + 12)) (128 below 641); items stretch to fill the row.
    private void ApplyGridMetrics()
    {
        var panel = GameGrid.ItemsPanelRoot as ItemsWrapGrid ?? wrapGrid;
        if (panel is null || GameGrid.ActualWidth <= 0) return;
        var width = GameGrid.ActualWidth - GameGrid.Padding.Left - GameGrid.Padding.Right - 2;
        var minimum = ActualWidth < 641 ? 128 : 156;
        var columns = Math.Max(1, (int)Math.Floor((width + 12) / (minimum + 12)));
        var itemWidth = Math.Floor(width / columns);
        var itemHeight = Math.Round((itemWidth - 16) * 4 / 3 + 81);
        // Initial values are NaN, so compare with "not within" rather than "greater than".
        if (!(Math.Abs(panel.ItemWidth - itemWidth) <= .5)) panel.ItemWidth = itemWidth;
        if (!(Math.Abs(panel.ItemHeight - itemHeight) <= .5)) panel.ItemHeight = itemHeight;
    }
    // ---- Interaction model ---------------------------------------------------------------------------------
    private static bool IsDown(VirtualKey key) => InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
    private async void OnItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not GameSummary item) return;
        var modifiers = keyboardOpen ? (IsDown(VirtualKey.Control) || IsDown(VirtualKey.Shift)) : pressModifiers != VirtualKeyModifiers.None;
        pressModifiers = VirtualKeyModifiers.None;
        if (modifiers)
        {
            // Ctrl/Shift+click enters selection mode with this item; applied after the click completes so the
            // switch to Multiple cannot toggle the same item straight back off.
            var list = (ListViewBase)sender;
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            {
                if (!ReferenceEquals(list, Active)) return;
                SetSelectionMode(true); if (!list.SelectedItems.Contains(item)) list.SelectedItems.Add(item);
                (list.ContainerFromItem(item) as Control)?.Focus(FocusState.Pointer);
            });
            return;
        }
        await OpenItem(item);
    }
    // The pointer event carries the modifiers of this very click; the thread key state can lag behind it.
    private VirtualKeyModifiers pressModifiers;
    private int selectionAnchor = -1;
    private void OnListPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        keyboardOpen = false;
        pressModifiers = e.KeyModifiers & (VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift);
        // Shift+click in selection mode selects the range from the anchor (the last item toggled without Shift).
        if (!selectionMode || pressModifiers != VirtualKeyModifiers.Shift || sender is not ListViewBase list || selectionAnchor < 0) return;
        var node = e.OriginalSource as DependencyObject;
        while (node is not null and not SelectorItem) node = VisualTreeHelper.GetParent(node);
        if (node is not SelectorItem container || list.IndexFromContainer(container) is var index && index < 0) return;
        var anchor = selectionAnchor;
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            if (!selectionMode || !ReferenceEquals(list, Active) || anchor >= list.Items.Count || index >= list.Items.Count) return;
            var first = Math.Min(anchor, index);
            list.SelectRange(new Microsoft.UI.Xaml.Data.ItemIndexRange(first, (uint)(Math.Max(anchor, index) - first + 1)));
        });
    }
    private async Task OpenItem(GameSummary item)
    {
        if (OpenDetails is null) return;
        try
        {
            // Capture the anchor before leaving; query, sort and view already live in WorkspaceViewModel.
            scrollKey = ListViewPersistenceHelper.GetRelativeScrollPosition(Active, value => ((GameSummary)value).Id);
            lastOpenedId = item.Id; restorePending = true;
            await OpenDetails(item.Id);
        }
        catch (Exception) { ViewModel.ReportUnexpected(); }
    }
    internal bool CancelReturnRestorationForSearch()
    {
        var pending = restorePending;
        restorePending = false;
        returnRestorationVersion++;
        return pending;
    }
    internal void RestoreCancelledReturnIntent(bool pending) => restorePending |= pending;
    private async Task RestoreReturnAsync()
    {
        restorePending = false;
        var version = returnRestorationVersion;
        var key = scrollKey; var id = lastOpenedId; var keyboard = keyboardOpen;
        try
        {
            // Wait one layout pass so the re-bound list has realized its panel.
            await Task.Yield();
            if (version != returnRestorationVersion || !IsLoaded) return;
            var target = id is null ? null : ViewModel.Items.FirstOrDefault(i => i.Id == id);
            if (key is not null && Active.ItemsPanelRoot is not null)
                await ListViewPersistenceHelper.SetRelativeScrollPositionAsync(Active, key,
                    value => AsyncInfo.Run(_ => Task.FromResult<object>(ViewModel.Items.FirstOrDefault(i => i.Id == value)!)));
            if (version != returnRestorationVersion || !IsLoaded || target is null) return;
            if (Active.ContainerFromItem(target) is not Control container) { Active.ScrollIntoView(target); await Task.Yield(); container = (Active.ContainerFromItem(target) as Control)!; }
            if (version == returnRestorationVersion && IsLoaded)
                container?.Focus(keyboard ? FocusState.Keyboard : FocusState.Programmatic);
        }
        catch (Exception) { /* Restoration is best effort; the query state itself is never lost. */ }
    }
    private async void OnItemsPreviewKey(object sender, KeyRoutedEventArgs e)
    {
        var control = IsDown(VirtualKey.Control);
        if (e.Key == VirtualKey.Enter && control)
        {
            e.Handled = true;
            if (FocusedItem() is { } item && QuickLaunch is not null) await QuickLaunch(item);
            return;
        }
        if (e.Key == VirtualKey.A && control && !selectionMode) { e.Handled = true; SetSelectionMode(true); SelectAll(); return; }
        if (e.Key is VirtualKey.Enter or VirtualKey.Space) { keyboardOpen = true; if (selectionMode && e.Key == VirtualKey.Enter) e.Handled = true; }
        if (e.Key == VirtualKey.Application || e.Key == VirtualKey.F10 && IsDown(VirtualKey.Shift))
        {
            keyboardOpen = true;
            if (FocusedItem() is { } item && Active.ContainerFromItem(item) is FrameworkElement target)
            { e.Handled = true; ShowCardMenu(item, target); }
        }
    }
    private void OnPageKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape && selectionMode) { e.Handled = true; SetSelectionMode(false); FocusResults(); }
    }
    private GameSummary? FocusedItem() => XamlRoot is null ? null
        : FocusManager.GetFocusedElement(XamlRoot) is SelectorItem container ? Active.ItemFromContainer(container) as GameSummary : null;
    private void SetSelectionMode(bool value)
    {
        if (selectionMode == value) return;
        selectionMode = value;
        foreach (var list in new ListViewBase[] { GameGrid, GameList })
        { list.SelectionMode = value ? ListViewSelectionMode.Multiple : ListViewSelectionMode.None; list.IsItemClickEnabled = !value; }
        SelectButton.IsChecked = value;
        UpdateContinue();
        UpdateSelectionChrome();
    }
    private void SelectAll() { if (Active is GridView grid) grid.SelectAll(); else if (Active is ListView list) list.SelectAll(); }
    private void OnSelectToggle(object sender, RoutedEventArgs e) => SetSelectionMode(SelectButton.IsChecked == true);
    private void OnMoreSelect(object sender, RoutedEventArgs e) => SetSelectionMode(true);
    private void OnSelectAll(object sender, RoutedEventArgs e) => SelectAll();
    private void OnSelectionDone(object sender, RoutedEventArgs e) { SetSelectionMode(false); FocusResults(); }
    private void OnItemSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ReferenceEquals(sender, Active) && e.AddedItems.Count == 1 && pressModifiers != VirtualKeyModifiers.Shift) selectionAnchor = Active.Items.IndexOf(e.AddedItems[0]);
        if (!selectionMode) selectionAnchor = -1;
        // Clearing the last selected item by hand ends selection mode (not layout switches or batch resets).
        if (!synchronizing && selectionMode && ReferenceEquals(sender, Active) && Active.SelectedItems.Count == 0 && e.RemovedItems.Count > 0 && e.AddedItems.Count == 0)
            DispatcherQueue.TryEnqueue(() => { if (selectionMode && Active.SelectedItems.Count == 0) SetSelectionMode(false); });
        UpdateSelectionChrome();
    }
    private void UpdateSelectionChrome()
    {
        if (SelectionCountText is null || GameGrid is null || GameList is null) return;
        var count = Active.SelectedItems.Count;
        SelectionContext.Visibility = Visible(selectionMode);
        HeaderCommands.Opacity = selectionMode ? .5 : 1; HeaderCommands.IsHitTestVisible = !selectionMode;
        SelectionCountText.Text = count == 0 ? UiText.Get("选择作品") : UiText.Format($"已选择 {count} 项");
        RecognizeSelectedButton.IsEnabled = count > 0;
        BatchEditButton.IsEnabled = count > 0 && ViewModel.CanEdit;
        RemoveSelectedButton.IsEnabled = count > 0 && ViewModel.CanEdit;
        ListHeader.Padding = new Thickness(selectionMode ? 52 : 12, 0, 12, 0);
        UpdateListHeaderColumns(ListHeader.ActualWidth);
    }
    private void OnCardContextRequested(UIElement sender, ContextRequestedEventArgs e)
    {
        if (sender is not FrameworkElement target || target.Tag is not string id
            || ViewModel.Items.FirstOrDefault(i => i.Id == id) is not { } item) return;
        e.Handled = true;
        ShowCardMenu(item, target, e.TryGetPosition(target, out var point) ? point : null);
    }
    private MenuFlyout? activeCardMenu;
    private void ShowCardMenu(GameSummary item, FrameworkElement target, Windows.Foundation.Point? position = null)
    {
        activeCardMenu?.Hide();
        var menu = new MenuFlyout();
        menu.MenuFlyoutPresenterStyle = new Style(typeof(MenuFlyoutPresenter));
        menu.MenuFlyoutPresenterStyle.Setters.Add(new Setter(FrameworkElement.RequestedThemeProperty, ActualTheme));
        MenuFlyoutItem Entry(string text, string glyph, Func<Task> action, bool enabled = true, string? accelerator = null)
        {
            var entry = new MenuFlyoutItem { Text = text, Icon = new FontIcon { Glyph = glyph }, IsEnabled = enabled, KeyboardAcceleratorTextOverride = accelerator ?? "" };
            entry.Click += async (_, _) => { try { await action(); } catch (Exception) { ViewModel.ReportUnexpected(); } };
            return entry;
        }
        if (!selectionMode)
        {
            menu.Items.Add(Entry(UiText.Get("打开详情"), "", () => OpenItem(item), accelerator: "Enter"));
            menu.Items.Add(Entry(UiText.Get("启动游戏"), "", () => QuickLaunch?.Invoke(item) ?? Task.CompletedTask, item.InstallationCount > 0, "Ctrl+Enter"));
            menu.Items.Add(Entry("启动设置（LE / Magpie）…", "", () => OpenLaunchSettings?.Invoke(item.Id) ?? Task.CompletedTask, item.InstallationCount > 0 && ViewModel.CanEdit));
            menu.Items.Add(new MenuFlyoutSeparator());
            menu.Items.Add(Entry(UiText.Get("选择"), "", () => { SetSelectionMode(true); Active.SelectedItems.Add(item); return Task.CompletedTask; }));
            menu.Items.Add(Entry("从书架移除", "", () => RemoveSelected?.Invoke([item.Id]) ?? Task.CompletedTask, ViewModel.CanEdit));
        }
        else
        {
            if (!Active.SelectedItems.Contains(item)) Active.SelectedItems.Add(item);
            menu.Items.Add(Entry(UiText.Get("识别所选"), "", RecognizeSelection));
            menu.Items.Add(Entry(UiText.Get("批量编辑"), "", BatchEdit, ViewModel.CanEdit));
            menu.Items.Add(Entry("移除所选作品", "", RemoveSelection, ViewModel.CanEdit));
            menu.Items.Add(new MenuFlyoutSeparator());
            menu.Items.Add(Entry(UiText.Get("完成选择"), "", () => { SetSelectionMode(false); return Task.CompletedTask; }, accelerator: "Esc"));
        }
        activeCardMenu = menu;
        menu.Closed += (_, _) => { if (ReferenceEquals(activeCardMenu, menu)) activeCardMenu = null; };
        if (position is { } at) menu.ShowAt(target, new FlyoutShowOptions { Position = at }); else menu.ShowAt(target);
    }
    private async void OnRecognize(object sender, RoutedEventArgs e) => await RecognizeSelection();
    private async void OnRemoveSelected(object sender, RoutedEventArgs e) => await RemoveSelection();
    private async Task RemoveSelection()
    {
        try
        {
            var ids = Active.SelectedItems.OfType<GameSummary>().Select(g => g.Id).ToImmutableArray();
            if (!ids.IsEmpty && RemoveSelected is { } remove) await remove(ids);
        }
        catch { ViewModel.ReportUnexpected(); }
    }
    private async Task RecognizeSelection()
    {
        try { var selected = Active.SelectedItems.OfType<GameSummary>().Select(g => g.Id).ToImmutableArray(); if (!selected.IsEmpty && RecognizeSelected is not null) await RecognizeSelected(selected); }
        catch (Exception) { ViewModel.ReportUnexpected(); }
    }
    private async void OnBatch(object sender, RoutedEventArgs e) => await BatchEdit();
    private async Task BatchEdit()
    {
        try
        {
            var ids = Active.SelectedItems.Cast<GameSummary>().Select(i => i.Id).ToImmutableArray();
            if (ids.Length is < 1 or > 1000)
            { await new ContentDialog { XamlRoot = XamlRoot, RequestedTheme = ActualTheme, Title = UiText.Get("请选择作品"), Content = UiText.Get("批量修改支持 1 至 1000 个条目。"), CloseButtonText = UiText.Get("知道了") }.ShowAsync(); return; }
            var status = new ComboBox { Header = UiText.Get("游玩状态"), ItemsSource = new[] { UiText.Get("保持不变"), UiText.Get("未开始"), UiText.Get("在玩"), UiText.Get("已完成"), UiText.Get("搁置"), UiText.Get("放弃") }, SelectedIndex = 0 };
            var favorite = new ComboBox { Header = UiText.Get("收藏"), ItemsSource = new[] { UiText.Get("保持不变"), UiText.Get("收藏"), UiText.Get("取消收藏") }, SelectedIndex = 0 };
            var categories = new TextBox { Header = UiText.Get("分类（逗号或换行分隔）") }; var tags = new TextBox { Header = UiText.Get("个人标签（逗号或换行分隔）") };
            var remove = new CheckBox { Content = UiText.Get("移除这些分类和标签（默认追加）") };
            var panel = new StackPanel { Spacing = 12 }; foreach (var element in new UIElement[] { status, favorite, categories, tags, remove }) panel.Children.Add(element);
            var dialog = new ContentDialog { XamlRoot = XamlRoot, RequestedTheme = ActualTheme, Title = UiText.Format($"批量修改 {ids.Length} 部作品"), Content = panel, PrimaryButtonText = UiText.Get("保存一次修改"), CloseButtonText = UiText.Get("取消"), DefaultButton = ContentDialogButton.Primary };
            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
                await ViewModel.ApplyBatchAsync(ids, PreferenceValidation.Statuses[status.SelectedIndex], new[] { "keep", "yes", "no" }[favorite.SelectedIndex], categories.Text, tags.Text, remove.IsChecked == true);
        }
        catch (Exception) { ViewModel.ReportUnexpected(); }
    }
    internal void FocusResults()
    {
        if (Active.Items.Count == 0) { Active.Focus(FocusState.Programmatic); return; }
        if (Active.ContainerFromIndex(0) is Control first) first.Focus(FocusState.Keyboard); else Active.Focus(FocusState.Keyboard);
    }

    // A small portrait cover preserves the selected artwork instead of magnifying it into a banner.
    private readonly CoverPresenter continueCover = new() { Width = 84, Height = 112, ImageCornerRadius = 6, VerticalAlignment = VerticalAlignment.Center };
    private readonly Border continueHost = new() { Margin = new Thickness(6, 4, 6, 14) };
    private readonly TextBlock continueTitle = new() { TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.Wrap, MaxLines = 2 };
    private readonly TextBlock continueMeta = new() { TextWrapping = TextWrapping.Wrap };
    private GameSummary? continueGame;
    private void BuildContinueBanner()
    {
        var resources = Microsoft.UI.Xaml.Application.Current.Resources;
        continueTitle.Style = (Style)resources["SubtitleTextBlockStyle"];
        continueMeta.Style = (Style)resources["CaptionTextBlockStyle"]; continueMeta.Opacity = .86;
        var caption = new TextBlock { Text = UiText.Get("继续游玩"), Style = (Style)resources["CaptionTextBlockStyle"], Opacity = .86 };
        var play = new Button { Style = (Style)resources["AccentButtonStyle"] };
        var playContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        playContent.Children.Add(new FontIcon { Glyph = "", FontSize = 14 }); playContent.Children.Add(new TextBlock { Text = UiText.Get("继续游戏") });
        play.Content = playContent;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(play, "ContinuePlay");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(play, UiText.Get("继续游戏"));
        play.Click += async (_, _) => { if (continueGame is { } game && QuickLaunch is not null) await QuickLaunch(game); };
        var details = new Button { Content = UiText.Get("详情") };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(details, "ContinueDetails");
        details.Click += async (_, _) => { if (continueGame is { } game) await OpenItem(game); };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 10, 0, 0) };
        actions.Children.Add(play); actions.Children.Add(details);
        var body = new StackPanel { Spacing = 2 };
        body.Children.Add(caption); body.Children.Add(continueTitle); body.Children.Add(continueMeta); body.Children.Add(actions);
        continueCover.Pipeline = Images;
        var layout = new Grid { ColumnSpacing = 20 };
        layout.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); layout.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        layout.Children.Add(continueCover); Grid.SetColumn(body, 1); body.VerticalAlignment = VerticalAlignment.Center; layout.Children.Add(body);
        continueHost.Style = (Style)Resources["ContinueCardStyle"]; continueHost.Child = layout;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(continueHost, "ContinueBanner");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(continueHost, UiText.Get("继续游玩"));
    }
    // Shown only for the unfiltered library outside selection mode; the game is the most recently played local
    // entry (existing LastPlayedAt). The banner is detached when hidden so its renderer is released.
    private void UpdateContinue()
    {
        var q = ViewModel.Query;
        GameSummary? candidate = null;
        if (!selectionMode && q with { Sort = "" } == new LibraryQuery(Sort: ""))
        {
            DateTimeOffset best = default;
            foreach (var item in ViewModel.Items)
                if (item.InstallationCount > 0 && item.LastPlayedAt is { } value && DateTimeOffset.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out var at) && (candidate is null || at > best))
                { candidate = item; best = at; }
        }
        continueGame = candidate;
        var grid = ViewModel.Layout == "grid";
        object? gridHeader = candidate is not null && grid ? continueHost : null, listHeader = candidate is not null && !grid ? continueHost : null;
        if (candidate is null) continueCover.SourceKey = null;
        if (!ReferenceEquals(GameGrid.Header, gridHeader)) { if (gridHeader is null) GameGrid.Header = null; }
        if (!ReferenceEquals(GameList.Header, listHeader)) { if (listHeader is null) GameList.Header = null; }
        if (gridHeader is not null && !ReferenceEquals(GameGrid.Header, gridHeader)) GameGrid.Header = gridHeader;
        if (listHeader is not null && !ReferenceEquals(GameList.Header, listHeader)) GameList.Header = listHeader;
        if (candidate is null) return;
        continueTitle.Text = candidate.Name;
        continueMeta.Text = UiText.Format($"上次游玩 {candidate.LastPlayedText} · 共 {LibraryFormat.PlayTime(candidate.PlaySeconds)}");
        continueHost.Margin = grid ? new Thickness(6, 4, 6, 14) : new Thickness(0, 4, 0, 14);
        continueCover.SourceKey = candidate.CoverKey;
    }
    internal void SetStagePolicy(bool effectsAllowed, bool highContrast, MaterialMode mode) { } // Theme resources handle the ordinary card.
    internal void ReleaseStageBeforeLeave() => continueCover.SourceKey = null;
    internal object StageEvidence => new { placement = UiText.Get("Library 继续游玩"), visible = continueGame is not null, game = continueGame?.Id, selectionMode, layout = "cover-and-text", material = "none", cover = continueCover.SourceStatus };
    internal Task<object> AuditStageAsync() => Task.FromResult(StageEvidence);

    // ---- Test and diagnostics ------------------------------------------------------------------------------
    internal void ScrollTest(double fraction)
    {
        if (FindScroll(Active) is { } scroll) scroll.ChangeView(null, scroll.ScrollableHeight * Math.Clamp(fraction, 0, 1), null, true);
    }
    internal void SetSelectionModeTest(bool value) => SetSelectionMode(value);
    internal object VisualLayoutEvidence() => new { width = ActualWidth, viewportWidth = Active.ActualWidth, viewportHeight = Active.ActualHeight,
        selection = Active.SelectedItems.Count, selectionMode, selectionVisible = SelectionContext.Visibility.ToString(),
        filterHeight = FilterPanel.ActualHeight, footerHeight = FilterFooter.ActualHeight,
        footerY = FilterFooter.ActualHeight == 0 ? 0 : FilterFooter.TransformToVisual(FilterPanel).TransformPoint(new Windows.Foundation.Point()).Y,
        itemWidth = (GameGrid.ItemsPanelRoot as ItemsWrapGrid ?? wrapGrid) is { ItemWidth: var w } && double.IsFinite(w) ? w : (double?)null,
        panel = GameGrid.ItemsPanelRoot?.GetType().Name, itemCount = Active.Items.Count, lastOpened = lastOpenedId, restorePending,
        focused = FocusedItem()?.Id, continueVisible = continueGame is not null };
    private void OnContainerChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (!diagnostics || args.InRecycleQueue) return;
        if (!seenContainers.TryGetValue(args.ItemContainer, out _)) { seenContainers.Add(args.ItemContainer, new object()); createdContainers++; }
        if (firstDisplay == 0) firstDisplay = display.Elapsed.TotalMilliseconds - modeStarted;
    }
    private void OnLayout(object? sender, object e)
    {
        var realized = Active.ItemsPanelRoot?.Children.Count ?? 0; maxRealized = Math.Max(maxRealized, realized);
        var value = $"{ViewModel.Layout} | items={ViewModel.Items.Count} | realized={realized} | max={maxRealized} | containers={createdContainers} | first={firstDisplay:F0}ms | query={ViewModel.LastQueryMilliseconds:F1}ms";
        if (DiagnosticsText.Text != value) DiagnosticsText.Text = value;
    }
    internal object RuntimeViewport() => new
    {
        realizedContainerCount = Active.ItemsPanelRoot?.Children.Count ?? 0, maxRealized,
        firstContainerMs = firstDisplay, viewportWidth = Active.ActualWidth, viewportHeight = Active.ActualHeight,
        selectedCount = Active.SelectedItems.Count,
        scrollOffset = FindScroll(Active)?.VerticalOffset ?? 0
    };
    private static ScrollViewer? FindScroll(DependencyObject root)
    {
        if (root is ScrollViewer scroll) return scroll;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            if (FindScroll(VisualTreeHelper.GetChild(root, i)) is { } found) return found;
        return null;
    }
    internal async Task<object> PerformanceScrollAsync(double fraction, int selection)
    {
        if (FindScroll(Active) is { } scroll) scroll.ChangeView(null, scroll.ScrollableHeight * Math.Clamp(fraction, 0, 1), null, true);
        // Normal browsing has no selection; the probe keeps exercising selection through selection mode.
        if (ViewModel.Items.Count > 0) { SetSelectionMode(true); Active.SelectedItems.Clear(); Active.SelectedItems.Add(ViewModel.Items[selection % ViewModel.Items.Count]); }
        await Task.Delay(80);
        var result = RuntimeViewport();
        SetSelectionMode(false);
        return result;
    }
    internal bool PerformanceHasVisibleBitmap() => VisibleBitmap(Active, Active);
    private static bool VisibleBitmap(DependencyObject root, FrameworkElement viewport)
    {
        if (root is Image image && image.Source is not null && image.Visibility == Visibility.Visible && image.ActualWidth > 0 && image.ActualHeight > 0)
        {
            var bounds = image.TransformToVisual(viewport).TransformBounds(new Windows.Foundation.Rect(0, 0, image.ActualWidth, image.ActualHeight));
            if (bounds.X < viewport.ActualWidth && bounds.Y < viewport.ActualHeight && bounds.X + bounds.Width > 0 && bounds.Y + bounds.Height > 0) return true;
        }
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            if (VisibleBitmap(VisualTreeHelper.GetChild(root, i), viewport)) return true;
        return false;
    }
}

// x:Bind helpers for item templates (static; no per-item allocations beyond the strings shown).
public static class LibraryFormat
{
    public static Visibility Show(bool value) => value ? Visibility.Visible : Visibility.Collapsed;
    public static Visibility ShowText(string? value) => !string.IsNullOrWhiteSpace(value) ? Visibility.Visible : Visibility.Collapsed;
    public static Visibility ShowRowSubtitle(string labels, int installations, bool favorite) => ShowText(RowSubtitle(labels, installations, favorite));
    public static Visibility Wishlist(int installations) => installations == 0 ? Visibility.Visible : Visibility.Collapsed;
    public static string Rating(double? rating) => rating is null ? "—" : $"{rating:0.#}";
    public static string CardMeta(string status, double? rating) => rating is null ? status : UiText.Format($"{status} · {rating:0.#} 分");
    public static string PlayTime(long seconds) => seconds <= 0 ? "—" : seconds < 3600 ? UiText.Format($"{Math.Max(1, seconds / 60)} 分钟") : UiText.Format($"{seconds / 3600d:0.#} 小时");
    public static string RowSubtitle(string labels, int installations, bool favorite) =>
        string.Join(" · ", new[] { favorite ? UiText.Get("收藏") : "", installations == 0 ? UiText.Get("愿望单") : "", labels }.Where(s => s.Length > 0));
    public static string CompactRowMeta(string status, double? rating, long seconds, string lastPlayed) =>
        string.Join(" · ", new[] { status, rating is null ? "" : UiText.Format($"{rating:0.#} 分"), seconds <= 0 ? "" : PlayTime(seconds), lastPlayed }.Where(s => s.Length > 0));
    public static string CardName(string name, string status, string lastPlayed) => UiText.Format($"{name}，{status}，最近游玩：{lastPlayed}");
}
