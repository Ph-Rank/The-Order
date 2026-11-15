using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using TheOrder.Application;
using TheOrder.Domain;
using TheOrder.Presentation;

namespace TheOrder.WinUI.Views;
public sealed partial class OnlinePage : Page, IImageHost
{
    public OnlineViewModel ViewModel { get; }
    public IImagePipeline Images { get; }
    private readonly CatalogBrowserView browser;
    private readonly DiscoveryFeedViewModel discovery;
    private DiscoveryFeedView? discoveryView;
    private ItemsWrapGrid? wrap;
    private IReadOnlyList<OnlineCard>? displayedResults;
    private ObservableCollection<OnlineCard> resultItems = [];
    private bool? searchLanding;
    public bool IsBrowsing => BrowserHost.Visibility == Visibility.Visible;
    public event Action? NavigationChanged;
    public OnlinePage(OnlineViewModel viewModel, DiscoveryFeedViewModel discovery, IImagePipeline images, IOnlineCatalog catalog, IEntityCatalog entities, IRemoteCoverRegistry registry, CatalogPresentationService presentations, Func<LibrarySnapshot?> readLibrary)
    {
        ViewModel = viewModel; this.discovery = discovery; Images = images; InitializeComponent();
        browser = new(catalog, entities, images, registry, () => viewModel.AuthGeneration, presentations, readLibrary)
        { ReturnToSearch = ReturnToDiscoveryAsync, AddWishlist = id => viewModel.AddWishlistAsync(id) };
        BrowserHost.Content = browser;
        SearchPageFooter.LoadNext = () => ViewModel.SearchAsync(true);
        SearchButton.IsEnabled = !string.IsNullOrWhiteSpace(OnlineQueryBox.Text);
        DiscoveryTabs.SelectedItem = SearchTab;
        Loaded += async (_, _) =>
        {
            ViewModel.PropertyChanged += OnSearchState;
            UpdateResults(reveal: false);
            UpdateSearchLayout();
            if (IsBrowsing) await browser.ResumeAsync();
            else await SelectDiscoverySectionAsync();
        };
        Unloaded += (_, _) => { ViewModel.PropertyChanged -= OnSearchState; Cancel(); };
        SizeChanged += (_, _) => UpdateSearchLayout();
        SearchResultBody.SizeChanged += (_, _) => UpdateSearchIndicator();
    }
    private void OnSearchState(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(OnlineViewModel.SearchMessage)) UpdateSearchIndicator();
        if (e.PropertyName is not (nameof(OnlineViewModel.Items) or nameof(OnlineViewModel.SearchBusy))) return;
        UpdateSearchLayout();
        UpdateResults(reveal: true);
    }
    private void UpdateResults(bool reveal)
    {
        var next = ViewModel.Items;
        if (ReferenceEquals(displayedResults, next)) return;
        displayedResults = next;
        // A subsequent page keeps the existing containers and scroll position.
        if (resultItems.Count > 0 && next.Count >= resultItems.Count && resultItems.SequenceEqual(next.Take(resultItems.Count)))
        {
            foreach (var item in next.Skip(resultItems.Count)) resultItems.Add(item);
            return;
        }
        resultItems = new(next);
        ResultsGrid.ItemsSource = resultItems;
        if (reveal && next.Count > 0) InteractionMotion.RevealContent(ResultsGrid);
    }
    private void UpdateSearchLayout()
    {
        var hasResults = ViewModel.Items.Count > 0;
        var landing = !hasResults && ViewModel.SearchState == OnlineState.Idle;
        var narrow = ActualWidth is > 0 and < 641;
        var inset = narrow ? 16 : 24;
        OnlineRoot.Padding = new Thickness(inset, 0, inset, inset);
        if (ActualWidth > inset * 2)
            SearchHeader.Width = Math.Min(SearchHeader.MaxWidth, ActualWidth - inset * 2);
        if (searchLanding != landing)
        {
            InteractionMotion.SetReposition(SearchHeader, searchLanding.HasValue);
            SearchLandingSpacer.Height = landing ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
            searchLanding = landing;
        }
        DiscoveryTitle.Visibility = landing ? Visibility.Visible : Visibility.Collapsed;
        SearchStatus.TextAlignment = landing ? TextAlignment.Center : TextAlignment.Left;
        ResultsGrid.Visibility = hasResults || ViewModel.PaginationCursor is not null ? Visibility.Visible : Visibility.Collapsed;
        SearchPageFooter.Update(ViewModel.PaginationCursor, ViewModel.SearchBusy);
        UpdateSearchIndicator();
        ResizeCards();
    }
    private void UpdateSearchIndicator()
    {
        var large = ViewModel.SearchLoading && ViewModel.Items.Count == 0 && SearchResultBody.ActualHeight >= 160;
        var inline = ViewModel.SearchLoading && !large;
        SearchLoadingState.Visibility = SearchLoadingMark.Visibility = BusyVisibility(large);
        SearchLoadingMark.IsActive = large;
        SearchInlineMark.Visibility = BusyVisibility(inline);
        SearchInlineMark.IsActive = inline;
        SearchGlyph.Visibility = IdleVisibility(inline);
        SearchStatus.Visibility = BusyVisibility(!large && !string.IsNullOrWhiteSpace(ViewModel.SearchMessage));
    }
    public void FocusSearch()
    {
        ResetBrowsing();
        DiscoveryTabs.SelectedItem = SearchTab;
        OnlineQueryBox.Focus(FocusState.Keyboard);
    }
    public void Cancel() { browser.Cancel(); discovery.Cancel(); }
    internal void InvalidateDiscoveryIfStale() => discovery.InvalidateIfStale();
    internal void ConfigureMusic(IMusicBrainzReader music, IMusicBrainzEntityReader? entities = null) => browser.ConfigureMusic(music, entities);
    internal void ConfigureAniList(IAniListReader reader, IWatchLinkPreferencesStore? watchPreferences = null, IBangumiWatchLinksReader? watchReader = null) => browser.ConfigureAniList(reader, watchPreferences, watchReader);
    internal void ConfigureReleases(ICatalogReleaseReader reader, Func<long> auth) => browser.ConfigureReleases(reader, auth);
    internal void ConfigureEpisodes(IBangumiEpisodesReader reader) => browser.ConfigureEpisodes(reader);
    internal void ConfigureGameVideos(GameVideoReader reader, IMediaIdentityDecisions decisions) => browser.ConfigureGameVideos(reader, decisions);
    internal void SuspendGameVideos() => browser.SuspendGameVideos();
    internal void ConfigureBooks(IBookBibliographyReader books, IBangumiBookRelationsReader relations) => browser.ConfigureBooks(books, relations);
    internal void ConfigureRelatedWorks(IBangumiRelationsReader reader, IBangumiSubjectDetailsReader subjects,
        IRelatedSemanticReader semantics, IRelatedGameIdentityReader? identities, Func<string>? accessScope = null) => browser.ConfigureRelatedWorks(reader, subjects, semantics, identities, accessScope);
    internal void InvalidateBangumiAccess() => browser.InvalidateBangumiAccess();
    internal void DisposeRelated() => browser.DisposeRelated();
    internal void ResetBrowsing()
    {
        browser.Reset();
        discovery.InvalidateIfStale();
        BrowserHost.Visibility = Visibility.Collapsed;
        if (DiscoveryHome.Visibility != Visibility.Visible) InteractionMotion.EnterPage(DiscoveryHome);
        NavigationChanged?.Invoke();
    }
    internal void RefreshPrepared(string id) => browser.RefreshPrepared(id);
    internal void SetStagePolicy(bool allowed, bool contrast, MaterialMode mode) => browser.SetStagePolicy(allowed, contrast, mode);
    internal object? StageEvidence => browser.HeroEvidence;
    public async Task<bool> GoBackAsync() { if (!IsBrowsing) return false; if (!await browser.GoBackAsync()) await ReturnToDiscoveryAsync(); return true; }
    private async Task ReturnToDiscoveryAsync()
    {
        ResetBrowsing();
        if (discovery.Section == DiscoverySection.Search) ResultsGrid.Focus(FocusState.Programmatic);
        else
        {
            discoveryView?.FocusResults();
            await discovery.EnsureLoadedAsync();
        }
    }

    private async void OnDiscoverySection(SelectorBar sender, SelectorBarSelectionChangedEventArgs e)
    {
        if (IsLoaded && !IsBrowsing) await SelectDiscoverySectionAsync();
    }

    private Task SelectDiscoverySectionAsync()
    {
        var selected = DiscoveryTabs.SelectedItem;
        var section = selected == CalendarTab ? DiscoverySection.Calendar : selected == RankingsTab ? DiscoverySection.Rankings
            : selected == NewsTab ? DiscoverySection.News : DiscoverySection.Search;
        var search = section == DiscoverySection.Search;
        if (!search && discoveryView is null)
        {
            discoveryView = new(discovery, OpenWorkAsync);
            DiscoveryFeedHost.Content = discoveryView;
        }
        if (search && SearchResults.Visibility != Visibility.Visible) InteractionMotion.EnterPage(SearchResults);
        else if (!search) { SearchResults.Visibility = Visibility.Collapsed; InteractionMotion.StopPage(SearchResults); }
        DiscoveryFeedHost.Visibility = search ? Visibility.Collapsed : Visibility.Visible;
        return discovery.SelectAsync(section);
    }
    public Visibility BusyVisibility(bool busy) => busy ? Visibility.Visible : Visibility.Collapsed;
    public Visibility IdleVisibility(bool busy) => busy ? Visibility.Collapsed : Visibility.Visible;
    private void OnQueryTextChanged(object sender, TextChangedEventArgs e)
    {
        if (SearchButton is not null && sender is TextBox input) SearchButton.IsEnabled = !string.IsNullOrWhiteSpace(input.Text);
    }
    private void OnSearchFocusChanged(object sender, RoutedEventArgs e) =>
        SearchFocusOutline.Visibility = OnlineQueryBox.FocusState == FocusState.Unfocused ? Visibility.Collapsed : Visibility.Visible;
    private void OnSearchSurfaceTapped(object sender, TappedRoutedEventArgs e) => OnlineQueryBox.Focus(FocusState.Pointer);
    private Task SubmitSearchAsync()
    {
        if (string.IsNullOrWhiteSpace(OnlineQueryBox.Text)) return Task.CompletedTask;
        ViewModel.Query = OnlineQueryBox.Text;
        return ViewModel.SearchAsync();
    }
    private async void OnSearch(object sender, RoutedEventArgs e) => await SubmitSearchAsync();
    private async void OnQueryKeyDown(object sender, KeyRoutedEventArgs e) { if (e.Key == Windows.System.VirtualKey.Enter) { e.Handled = true; await SubmitSearchAsync(); } }
    private void OnCancel(object sender, RoutedEventArgs e) => ViewModel.CancelSearch();
    private void OnCopyNumber(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string id }) return;
        try
        {
            var package = new Windows.ApplicationModel.DataTransfer.DataPackage(); package.SetText(id);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
            ViewModel.ReportNumberCopied(true);
        }
        catch (Exception) { ViewModel.ReportNumberCopied(false); }
    }
    private async void OnWork(object sender, ItemClickEventArgs e)
    { if (e.ClickedItem is OnlineCard card) await OpenWorkAsync(card.Id, card.Title); }
    private Task OpenWorkAsync(string id, string title)
    {
        DiscoveryHome.Visibility = Visibility.Collapsed;
        BrowserHost.Visibility = Visibility.Visible;
        NavigationChanged?.Invoke();
        return browser.OpenAsync(new(CatalogEntityKind.Work, id, title), false);
    }
    private void OnWrapLoaded(object sender, RoutedEventArgs e) { wrap = sender as ItemsWrapGrid; ResizeCards(); }
    private void OnResultsSizeChanged(object sender, SizeChangedEventArgs e) => ResizeCards();
    private void ResizeCards()
    {
        if (wrap is null || ResultsGrid.ActualWidth <= 0) return;
        var width = Math.Max(140, ResultsGrid.ActualWidth - 4);
        var minCard = ActualWidth is > 0 and < 641 ? 136 : 168;
        var columns = Math.Max(1, (int)Math.Floor(width / minCard));
        var card = Math.Floor(width / columns);
        wrap.ItemWidth = card;
        wrap.ItemHeight = Math.Round((card - 16) * 4d / 3 + 106);
    }
}
