using Microsoft.UI.Xaml.Controls;
using TheOrder.Application;
using TheOrder.Presentation;
using System.ComponentModel;

namespace TheOrder.WinUI.Views;

public sealed partial class SettingsPage : Page
{
    public Func<Task>? OpenLibrarySources { get; set; }
    private async void OnLibrarySources(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) { try { if (OpenLibrarySources is not null) await OpenLibrarySources(); } catch { ViewModel.ReportUnexpected(); } }
    public WorkspaceViewModel ViewModel { get; }
    public OnlineViewModel Online { get; }
    public RecognitionViewModel Recognition { get; }
    public CatalogBrowseSettings Browsing { get; }
    public LanguageSettings Languages { get; }
    public ContentLanguageSettings ContentLanguages { get; }
    public GlobalLaunchSettings LaunchDefaults { get; }
    public Func<Task<string?>>? ChooseLaunchHelper { get; set; }
    public AppUpdateViewModel Updates { get; }
    public NextMoeSettingsViewModel NextMoe { get; }
    public LibraryBackupViewModel Backups { get; }
    public LibraryMigrationViewModel Migration { get; }
    public BangumiSettingsViewModel Bangumi { get; }
    public Func<VerifiedAppUpdate, Task<bool>>? InstallUpdate { get; set; }
    public string AppVersion => "The Order " + AppReleaseIdentity.Version;
    private bool editingCredential;
    private bool synchronizing;
    private bool synchronizingUpdateChannel;
    public Func<Task>? CredentialSaved { get; set; }
    public Func<MaterialMode, Task>? ChangeMaterialMode { get; set; }
    // MainWindow owns persisted intent; system policy owns effective state.
    internal void ShowMaterialState(MaterialMode mode, bool effectsAllowed, bool highContrast,
        bool controlledAdvanced, bool readOnly, string? notice)
    {
        synchronizing = true;
        MaterialChoice.SelectedIndex = mode == MaterialMode.Opaque ? (int)MaterialMode.Standard : (int)mode;
        synchronizing = false;
        MaterialChoice.IsEnabled = !readOnly;
        MaterialError.Text = notice ?? "";
        MaterialError.Visibility = string.IsNullOrEmpty(notice) ? Microsoft.UI.Xaml.Visibility.Collapsed : Microsoft.UI.Xaml.Visibility.Visible;
    }
    internal void SetMaterialSaving(bool saving) => MaterialChoice.IsEnabled = !saving;
    private async void OnMaterialMode(object sender, SelectionChangedEventArgs e)
    {
        if (synchronizing || MaterialChoice.SelectedIndex < 0) return;
        if (ChangeMaterialMode is { } change) await change((MaterialMode)MaterialChoice.SelectedIndex);
    }
    public SettingsPage(WorkspaceViewModel viewModel, OnlineViewModel online, RecognitionViewModel recognition, CatalogBrowseSettings browsing, LanguageSettings languages, ContentLanguageSettings contentLanguages, AppUpdateViewModel updates, NextMoeSettingsViewModel nextMoe, LibraryBackupViewModel backups, BangumiSettingsViewModel bangumi, GlobalLaunchSettings launchDefaults, LibraryMigrationViewModel migration)
    {
        ViewModel = viewModel; Online = online; Recognition = recognition; Browsing = browsing; Languages = languages; ContentLanguages = contentLanguages; Updates = updates; NextMoe = nextMoe; Backups = backups; Bangumi = bangumi; LaunchDefaults = launchDefaults; Migration = migration; InitializeComponent();
        Loaded += (_, _) => { Migration.PropertyChanged += OnMigrationState; ViewModel.PropertyChanged += OnMigrationState; SynchronizeMigration(); };
        Unloaded += (_, _) => { Migration.PropertyChanged -= OnMigrationState; ViewModel.PropertyChanged -= OnMigrationState; CancelMigrationDialog(); Migration.Clear(); };
        Loaded += (_, _) => Bangumi.RefreshState();
        Unloaded += (_, _) => { Bangumi.Cancel(); BangumiToken.Password = ""; BangumiEditor.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed; };
        Loaded += (_, _) => { Backups.PropertyChanged += OnBackupState; SynchronizeBackups(); };
        Unloaded += (_, _) => { Backups.PropertyChanged -= OnBackupState; };
        Loaded += (_, _) => { NextMoe.PropertyChanged += OnNextMoeState; SynchronizeNextMoe(); };
        Unloaded += (_, _) => { NextMoe.PropertyChanged -= OnNextMoeState; };
        Loaded += (_, _) => { Updates.PropertyChanged += OnUpdateState; SynchronizeUpdate(); };
        Unloaded += (_, _) => Updates.PropertyChanged -= OnUpdateState;
        Loaded += (_, _) => { Languages.PropertyChanged += OnLanguageState; SynchronizeLanguage(); };
        Unloaded += (_, _) => Languages.PropertyChanged -= OnLanguageState;
        Loaded += (_, _) => { ContentLanguages.PropertyChanged += OnContentLanguageState; SynchronizeContentLanguage(); };
        Unloaded += (_, _) => ContentLanguages.PropertyChanged -= OnContentLanguageState;
        Loaded += async (_, _) => { Online.PropertyChanged += OnCredentialState; await Online.CheckCredentialAsync(); if (IsLoaded) SynchronizeCredential(); };
        Loaded += (_, _) => { ViewModel.PropertyChanged += OnState; Synchronize(); };
        Loaded += (_, _) => { Browsing.PropertyChanged += OnBrowseState; SynchronizeBrowsing(); };
        Unloaded += (_, _) => Browsing.PropertyChanged -= OnBrowseState;
        Unloaded += (_, _) => { ViewModel.PropertyChanged -= OnState; Online.PropertyChanged -= OnCredentialState; ApiCredential.Password = ""; editingCredential = false; };
    }
    private void OnUpdateState(object? sender, PropertyChangedEventArgs e) => SynchronizeUpdate();
    private void SynchronizeUpdate()
    {
        synchronizingUpdateChannel = true;
        UpdateChannelChoice.SelectedIndex = (int)Updates.SelectedChannel;
        synchronizingUpdateChannel = false;
        var visible = Microsoft.UI.Xaml.Visibility.Visible; var collapsed = Microsoft.UI.Xaml.Visibility.Collapsed;
        UpdateNotes.Visibility = Updates.HasUpdate && Updates.Notes.Length != 0 ? visible : collapsed;
        UpdateHighlights.Visibility = Updates.HasUpdate ? visible : collapsed;
        InstallUpdateButton.Visibility = Updates.HasUpdate ? visible : collapsed;
        UpdateInstallNotice.Visibility = Updates.HasUpdate ? visible : collapsed;
        UpdateProgress.Visibility = Updates.IsBusy ? visible : collapsed;
        CancelUpdateButton.Visibility = Updates.CanCancel ? visible : collapsed;
    }
    private async void OnCheckUpdate(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) => await Updates.CheckAsync();
    private async void OnUpdateChannel(object sender, SelectionChangedEventArgs e)
    { if (!synchronizingUpdateChannel && UpdateChannelChoice.SelectedIndex >= 0) await Updates.ChangeChannelAsync((AppUpdateChannel)UpdateChannelChoice.SelectedIndex); }
    private async void OnInstallUpdate(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (InstallUpdate is not { } install || !Updates.CanInstall) return;
        if (Updates.IsChannelChange)
        {
            var dialog = new ContentDialog { XamlRoot = XamlRoot, RequestedTheme = ActualTheme, Title = Updates.InstallText,
                Content = "将切换到 " + Updates.Latest!.Version + "。退出保存后会先备份当前资料，再替换程序。当前书架、游玩记录和个人资料保留。正式版可能不提供 Beta 的跨媒体与辅助启动功能，但其资料和配置会保留。",
                PrimaryButtonText = "确认切换", CloseButtonText = "取消", DefaultButton = ContentDialogButton.Close };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        }
        await Updates.InstallAsync(install);
    }
    private void OnCancelUpdate(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) => Updates.Cancel();
    internal void ChooseUpdateChannelTest(int index) => UpdateChannelChoice.SelectedIndex = index;
    internal void ShowUpdatesTest() => RevealSettingsGroup(UpdatesGroup);
    private void OnBrowseState(object? sender, PropertyChangedEventArgs e) => SynchronizeBrowsing();
    private void OnLanguageState(object? sender, PropertyChangedEventArgs e) => SynchronizeLanguage();
    private void OnContentLanguageState(object? sender, PropertyChangedEventArgs e) => SynchronizeContentLanguage();
    private void SynchronizeContentLanguage()
    {
        synchronizing = true;
        ContentLanguageChoice.SelectedIndex = Array.IndexOf(ContentLanguage.Choices, ContentLanguages.Preference.Preferred);
        ContentFallbackChoice.SelectedIndex = Array.IndexOf(ContentLanguage.Choices, ContentLanguages.Preference.Fallback);
        synchronizing = false;
    }
    private async void OnContentLanguage(object sender, SelectionChangedEventArgs e)
    {
        if (!synchronizing && ContentLanguageChoice.SelectedIndex >= 0 && ContentFallbackChoice.SelectedIndex >= 0)
            await ContentLanguages.ChangeAsync(new(ContentLanguage.Choices[ContentLanguageChoice.SelectedIndex], ContentLanguage.Choices[ContentFallbackChoice.SelectedIndex]));
    }
    private void SynchronizeLanguage() { synchronizing = true; LanguageOptions.Visibility = Languages.SelectionAvailable ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed; LanguageChoice.SelectedIndex = Array.IndexOf(UiText.Choices, Languages.Selected); LanguageChoice.IsEnabled = Languages.CanChange; synchronizing = false; }
    private async void OnLanguage(object sender, SelectionChangedEventArgs e) { if (!synchronizing && LanguageChoice.SelectedIndex >= 0) await Languages.ChangeAsync(UiText.Choices[LanguageChoice.SelectedIndex]); }
    private void SynchronizeBrowsing() { synchronizing = true; NsfwSwitch.IsOn = Browsing.ShowNsfw; NsfwSwitch.IsEnabled = Browsing.CanChange; synchronizing = false; }
    private async void OnNsfw(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) { if (!synchronizing) await Browsing.ChangeAsync(NsfwSwitch.IsOn); }
    private async void OnSaveCredential(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        var secret = ApiCredential.Password;
        if (await Online.SaveCredentialAsync(secret))
        {
            if (ApiCredential.Password == secret) { ApiCredential.Password = ""; editingCredential = false; SynchronizeCredential(); }
            if (CredentialSaved is { } saved) await saved();
            await NextMoe.InitializeAsync();
        }
    }
    private void OnCredentialState(object? sender, PropertyChangedEventArgs e)
    { if (e.PropertyName == nameof(OnlineViewModel.HasCredential)) SynchronizeCredential(); }
    private void SynchronizeCredential()
    {
        CredentialEditor.Visibility = !Online.HasCredential || editingCredential ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
        ChangeCredentialButton.Visibility = Online.HasCredential && !editingCredential ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
        RemoveCredentialButton.Visibility = Online.HasCredential ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
        CancelCredentialButton.Visibility = Online.HasCredential ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
        ResizeSettingsControls();
    }
    private void OnChangeCredential(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) { editingCredential = true; SynchronizeCredential(); ApiCredential.Focus(Microsoft.UI.Xaml.FocusState.Programmatic); }
    private void OnCancelCredential(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) { ApiCredential.Password = ""; editingCredential = false; SynchronizeCredential(); ChangeCredentialButton.Focus(Microsoft.UI.Xaml.FocusState.Programmatic); }
    private async void OnMissing(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) => await Recognition.StartUnidentifiedAsync(retryPending: true);
    private void OnCancelRecognition(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) => Recognition.CancelAll();
    private async void OnClearCredential(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) { ApiCredential.Password = ""; await Online.ClearCredentialAsync(); await NextMoe.InitializeAsync(); }
    private async void OnTestConnection(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) => await Online.TestConnectionAsync();
    private void OnState(object? sender, PropertyChangedEventArgs e) { if (e.PropertyName == nameof(ViewModel.Theme)) Synchronize(); }
    private void Synchronize() { synchronizing = true; ThemeChoice.SelectedIndex = (int)ViewModel.Theme; synchronizing = false; }
    private async void OnAppearance(object sender, SelectionChangedEventArgs e)
    {
        if (synchronizing || ThemeChoice.SelectedIndex < 0) return;
        await ViewModel.ChangeAppearanceAsync((NativeTheme)ThemeChoice.SelectedIndex, ViewModel.Layout);
    }
    private void OnApiKeyPanelExpanding(Expander sender, ExpanderExpandingEventArgs args)
    {
        ResizeSettingsControls();
        DispatcherQueue.TryEnqueue(ResizeSettingsControls);
    }
    private void OnSettingsSectionSizeChanged(object sender, Microsoft.UI.Xaml.SizeChangedEventArgs e) => ResizeSettingsControls();
    private void OnSettingsControlLoaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) => ResizeSettingsControls();
    private void OnSettingsSizeChanged(object sender, Microsoft.UI.Xaml.SizeChangedEventArgs e)
    {
        var narrow = e.NewSize.Width < 641;
        SettingsColumn.Padding = narrow ? new Microsoft.UI.Xaml.Thickness(16, 12, 16, 28) : new Microsoft.UI.Xaml.Thickness(24, 12, 24, 32);
        // Leave a scroll-bar gutter; a bounded centered column keeps settings legible
        // on wide windows without forcing narrower windows to scroll horizontally.
        SettingsColumn.Width = Math.Min(880, Math.Max(0, e.NewSize.Width - 12));
        Grid.SetRow(SettingsVersion, narrow ? 1 : 0);
        Grid.SetColumn(SettingsVersion, narrow ? 0 : 1);
        SettingsVersion.Margin = new Microsoft.UI.Xaml.Thickness(0, narrow ? 6 : 0, 0, 0);
        foreach (var group in TopSettingsGroups)
            group.Padding = narrow ? new Microsoft.UI.Xaml.Thickness(16) : new Microsoft.UI.Xaml.Thickness(24, 20, 24, 20);
        ResizeSettingsControls();
    }
    private void ResizeSettingsControls()
    {
        var columnWidth = !double.IsNaN(SettingsColumn.Width) && SettingsColumn.Width > 0
            ? SettingsColumn.Width
            : SettingsColumn.ActualWidth > 0
                ? SettingsColumn.ActualWidth
                : SettingsRoot.ActualWidth > 0
                    ? Math.Min(880, SettingsRoot.ActualWidth - 12)
                    : 0;
        if (columnWidth <= 0) return;
        foreach (var control in SettingsControls().Distinct())
        {
            var width = Math.Min(control.MaxWidth, Math.Max(0, columnWidth - HorizontalInset(control, SettingsColumn)));
            if (double.IsNaN(control.Width) || Math.Abs(control.Width - width) > .1)
                control.Width = width;
        }
    }
    private IEnumerable<Microsoft.UI.Xaml.FrameworkElement> SettingsControls()
    {
        foreach (var control in Descendants(SettingsColumn))
            if (control is ComboBox or PasswordBox) yield return control;
        yield return ApiCredential;
        yield return BangumiToken;
    }
    private double HorizontalInset(Microsoft.UI.Xaml.DependencyObject control, Microsoft.UI.Xaml.DependencyObject root)
    {
        double inset = Inside(control, SyncPreviewRows) ? 32 : 0;
        for (var current = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(control); current is not null; )
        {
            if (current is Microsoft.UI.Xaml.FrameworkElement fe) inset += fe.Margin.Left + fe.Margin.Right;
            if (current is Border b) inset += b.Padding.Left + b.Padding.Right + b.BorderThickness.Left + b.BorderThickness.Right;
            else if (current is StackPanel sp) inset += sp.Padding.Left + sp.Padding.Right;
            else if (current is Grid g) inset += g.Padding.Left + g.Padding.Right + g.BorderThickness.Left + g.BorderThickness.Right;
            if (ReferenceEquals(current, root)) break;
            var parent = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(current);
            if (parent is null && SettingsExpanders.FirstOrDefault(e => ReferenceEquals(current, e.Content)) is { } expander)
            {
                var expanderPadding = expander.Padding.Left + expander.Padding.Right;
                var expanderBorder = expander.BorderThickness.Left + expander.BorderThickness.Right;
                inset += (expanderPadding > 0 ? expanderPadding : 32) + (expanderBorder > 0 ? expanderBorder : 2);
                parent = expander;
            }
            current = parent;
        }
        return inset;
    }
    private static bool Inside(Microsoft.UI.Xaml.DependencyObject control, Microsoft.UI.Xaml.DependencyObject parent)
    { for (var current = control; current is not null; current = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(current)) if (ReferenceEquals(current, parent)) return true; return false; }
    private static bool IsVisible(Microsoft.UI.Xaml.DependencyObject control)
    { if (control is Microsoft.UI.Xaml.FrameworkElement { IsLoaded: false }) return false;
      for (var current = control; current is not null; current = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(current))
        if (current is Microsoft.UI.Xaml.FrameworkElement { Visibility: Microsoft.UI.Xaml.Visibility.Collapsed }) return false; return true; }
    private static IEnumerable<Microsoft.UI.Xaml.FrameworkElement> Descendants(Microsoft.UI.Xaml.DependencyObject root)
    {
        for (int index = 0; index < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(root); index++)
        { var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(root, index); if (child is Microsoft.UI.Xaml.FrameworkElement element) yield return element;
            foreach (var descendant in Descendants(child)) yield return descendant; }
    }
    public Microsoft.UI.Xaml.Visibility Visible(bool value) => value ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
    public Microsoft.UI.Xaml.Visibility VisibleText(string? message) => Visible(!string.IsNullOrWhiteSpace(message));
    public string OperationalMessage(string message) => message.Contains("SFW / NSFW", StringComparison.Ordinal) ? "" : message;
    public Microsoft.UI.Xaml.Visibility VisibleOperationalMessage(string message) => VisibleText(OperationalMessage(message));
    public Microsoft.UI.Xaml.Visibility VisibleConnectionMessage(string message)
        => message == UiText.Get("尚未测试连接。") ? Microsoft.UI.Xaml.Visibility.Collapsed : VisibleText(message);
    internal object LayoutEvidence() => new { active = IsLoaded, columnWidth = SettingsColumn.ActualWidth, rootWidth = SettingsRoot.ActualWidth,
        columnTop = SettingsColumn.TransformToVisual(SettingsRoot).TransformPoint(new Windows.Foundation.Point()).Y,
        versionRow = Grid.GetRow(SettingsVersion),
        headers = TopSettingsGroups.Select(g => {
            var header = (Microsoft.UI.Xaml.FrameworkElement)g.Header;
            return new { name = g.Name, height = header.ActualHeight, width = header.ActualWidth,
                texts = Descendants(header).OfType<TextBlock>().Select(t => {
                    var point = t.TransformToVisual(header).TransformPoint(new Windows.Foundation.Point());
                    return new { text = t.Text, x = point.X, y = point.Y, width = t.ActualWidth, height = t.ActualHeight,
                        fits = point.X >= -.5 && point.Y >= -.5 && point.X + t.ActualWidth <= header.ActualWidth + .5 && point.Y + t.ActualHeight <= header.ActualHeight + .5 };
                }).ToArray() };
        }).ToArray(),
        groups = TopSettingsGroups.Select(g => new { name = g.Name, expanded = g.IsExpanded, width = g.ActualWidth, height = g.ActualHeight,
            x = g.TransformToVisual(SettingsColumn).TransformPoint(new Windows.Foundation.Point()).X,
            y = g.TransformToVisual(SettingsColumn).TransformPoint(new Windows.Foundation.Point()).Y }).ToArray(),
        actionCaptions = Descendants(SettingsColumn).OfType<Button>().Where(IsVisible).Select(b => b.Content as string).Where(s => s is not null).ToArray(),
        controls = Descendants(SettingsColumn).Where(x => x is ComboBox or PasswordBox).Select(x => {
            var point = x.TransformToVisual(SettingsColumn).TransformPoint(new Windows.Foundation.Point());
            return new { name = x.Name, visible = IsLoaded && IsVisible(x), alignment = x.HorizontalAlignment.ToString(), x = point.X, width = x.ActualWidth, fits = point.X + x.ActualWidth <= SettingsColumn.ActualWidth + .5 }; }).ToArray(),
        headings = Descendants(SettingsColumn).OfType<TextBlock>().Where(x => x.Text.Length > 0 && IsVisible(x)).Select(x => x.Text).ToArray() };
}
