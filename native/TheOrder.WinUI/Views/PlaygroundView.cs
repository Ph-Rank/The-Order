using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.ComponentModel;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using TheOrder.Application.Playground;
using TheOrder.Presentation;
using Windows.System;
using Windows.UI.Core;

namespace TheOrder.WinUI.Views;

public sealed class PlaygroundConversationItem(PlaygroundConversation conversation) : INotifyPropertyChanged
{
    public PlaygroundConversation Conversation { get; private set; } = conversation;
    public Guid Id => Conversation.Id;
    public string Title => Conversation.Title;
    public string Summary => (Conversation.Temporary ? "临时 · " : "") + Conversation.UpdatedAt.ToLocalTime().ToString("MM-dd HH:mm");
    public string Tooltip => Title + "\n" + Summary;
    public event PropertyChangedEventHandler? PropertyChanged;
    public void Update(PlaygroundConversation value)
    {
        if (Conversation == value) return; Conversation = value;
        PropertyChanged?.Invoke(this, new(nameof(Title))); PropertyChanged?.Invoke(this, new(nameof(Summary))); PropertyChanged?.Invoke(this, new(nameof(Tooltip)));
    }
}

// The app owns the service lifetime. Navigating away disposes neither the
// service nor a request explicitly started by the user.
public sealed partial class PlaygroundView : UserControl, IDisposable
{
    private readonly PlaygroundService service;
    private readonly ResourceDictionary templates = new() { Source = new Uri("ms-appx:///Views/PlaygroundTemplates.xaml") };
    private readonly CancellationTokenSource lifetime = new();
    private readonly Grid layout = new();
    private readonly Border workbenchFrame = new() { Margin = new Thickness(12), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12) };
    private readonly Border sessionsHost = new() { BorderThickness = new Thickness(0, 0, 1, 0) };
    private readonly Border settingsHost = new() { BorderThickness = new Thickness(1, 0, 0, 0) };
    private readonly Grid reader = new();
    private readonly Grid sessions = new() { RowSpacing = 7, MinWidth = 190, Margin = new Thickness(12, 0, 12, 12) };
    private readonly StackPanel settings = new() { Spacing = 18, MinWidth = 220 };
    private readonly ScrollViewer settingsScroll;
    private readonly TextBox search = new() { PlaceholderText = "搜索会话", MaxLength = 200, Visibility = Visibility.Collapsed, Padding = new Thickness(10, 7, 10, 7) };
    private readonly ListView conversations = new() { SelectionMode = ListViewSelectionMode.Single, HorizontalContentAlignment = HorizontalAlignment.Stretch };
    private readonly ObservableCollection<PlaygroundConversationItem> conversationRows = [];
    private readonly ListView messages = new() { SelectionMode = ListViewSelectionMode.None, HorizontalContentAlignment = HorizontalAlignment.Stretch,
        Padding = new Thickness(28, 6, 28, 24), IsItemClickEnabled = false };
    private readonly ObservableCollection<PlaygroundMessageItem> messageRows = [];
    private readonly TextBlock title = new() { Text = "对话", FontSize = 18, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
        TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock subtitle = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap };
    private readonly Button sessionToggle = new() { Content = "会话", Padding = new Thickness(10, 5, 10, 5) };
    private readonly Button settingsToggle = new() { Content = "运行设置", Padding = new Thickness(10, 5, 10, 5) };
    private readonly Button memoryButton = new() { Content = "记忆", Padding = new Thickness(10, 5, 10, 5) };
    private readonly Button conversationMenu = new() { Content = "···", Padding = new Thickness(10, 5, 10, 5) };
    private readonly TextBlock sessionNote = new() { FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(8, 0, 8, 0) };
    private readonly Border inputSurface = new() { CornerRadius = new CornerRadius(20), BorderThickness = new Thickness(1), Padding = new Thickness(14, 12, 10, 10) };
    private readonly InfoBar notification = new() { IsOpen = false, IsClosable = true };
    private readonly StackPanel empty = new() { Spacing = 10, MaxWidth = 440, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock emptyTitle = new() { FontSize = 21, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock emptyDescription = new() { FontSize = 14, LineHeight = 24, TextWrapping = TextWrapping.Wrap };
    private readonly Button emptyConfigure = new() { Content = "配置服务商", HorizontalAlignment = HorizontalAlignment.Left };
    private readonly Button retryInitialization = new() { Content = "重试读取", Visibility = Visibility.Collapsed, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly Button restoreState = new() { Content = "恢复上一个有效版本", Visibility = Visibility.Collapsed, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly TextBox composer = new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 32, MaxHeight = 184, Padding = new Thickness(0), FontSize = 15.5,
        MaxLength = 16000, IsSpellCheckEnabled = false, UseSystemFocusVisuals = false };
    private readonly Button send = new() { Width = 32, Height = 32, MinWidth = 32, MinHeight = 32, CornerRadius = new CornerRadius(16), Padding = new Thickness(0) };
    private readonly Button goActive = new() { Content = "查看正在回复的会话", Visibility = Visibility.Collapsed, FontSize = 12 };
    private readonly TextBlock activity = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
    private readonly DispatcherTimer refreshTimer = new() { Interval = TimeSpan.FromMilliseconds(90) };
    private readonly DispatcherTimer searchTimer = new() { Interval = TimeSpan.FromMilliseconds(150) };
    private readonly Dictionary<Guid, PlaygroundConversationOptions> drafts = [];
    private readonly Dictionary<Guid, string> textDrafts = [];
    private readonly Dictionary<Guid, bool> activeSpoilers = [];
    private readonly List<Border> visualDividers = [];
    private bool? shownStop;
    private bool inputFocused;
    private PlaygroundConversationOptions emptyDraft = new();
    private ImmutableArray<PlaygroundConversation> lastConversations;
    private Guid? selected;
    private bool initialized, disposed, synchronizing, sending;
    private bool credentialAvailable;
    private string credentialBinding = "";
    private int operations, credentialGeneration, refreshes, navigationGeneration, searchGeneration;
    private ScrollViewer? messageScroll;
    private readonly PlaygroundScrollFollow messageFollow = new();
    private PlaygroundScrollTicket? queuedLatestScroll;
    private bool latestScrollQueued, realizeLatestQueued, pendingLatestRealization, messagePointerScrolling, messageManipulating;
    private long messageInputUntil;
    private int messageInputDirection;
    private double lastMessageOffset;
    private PendingSubmission? pendingSubmission;
    private PendingRetry? pendingRetry;
    private sealed record PendingSubmission(Guid ConversationId, string Text, ImmutableArray<AttachmentDraft> Files, HashSet<Guid> PreviousUsers, PlaygroundScrollIntent? ScrollIntent);
    private sealed record PendingRetry(Guid ConversationId, HashSet<Guid> PreviousAssistants, PlaygroundScrollIntent? ScrollIntent);
    private Task? initialize;
    private ElementTheme canonicalWindowTheme;
    internal ElementTheme PaletteTheme => canonicalWindowTheme is ElementTheme.Light or ElementTheme.Dark ? canonicalWindowTheme : ActualTheme;
    private bool themePaintQueued;
    private string lastPaintTheme = "none", lastPaintReason = "none", queuedPaintReason = "none";
    private readonly List<object> paintTrace = [];
    public Func<PlaygroundSource, Task>? OpenSource { get; set; }
    public Func<IReadOnlyList<(string Key, string Title)>>? ProgressWorks { get; set; }
    public string? BrowserDataDirectory { get; set; }
    public Func<Task>? RestorePreviousState { get; set; }
    public Guid? SelectedConversationId => selected;
    public void FollowWindowTheme(ElementTheme theme)
    {
        if (theme is not (ElementTheme.Light or ElementTheme.Dark)) throw new ArgumentOutOfRangeException(nameof(theme), "An actual window theme is required.");
        if (disposed) return;
        if (!DispatcherQueue.HasThreadAccess) { DispatcherQueue.TryEnqueue(() => FollowWindowTheme(theme)); return; }
        // The window owns the theme truth. A missed/early inherited theme event
        // must not leave C# brushes and native ThemeResources on opposite themes.
        canonicalWindowTheme = theme;
        if (RequestedTheme != theme) RequestedTheme = theme;
        PaintWorkspace("window-theme"); QueueThemePaint("window-theme");
    }
    public PlaygroundView(PlaygroundService service)
    {
        this.service = service; PlaygroundAppearance.Scope(this); IsTabStop = false; workbenchFrame.Child = layout; Content = workbenchFrame;
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
        AutomationProperties.SetAutomationId(this, "PlaygroundWorkspace");
        layout.RowDefinitions.Add(new());
        settingsScroll = new ScrollViewer { Content = settings, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalScrollMode = ScrollMode.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollMode = ScrollMode.Enabled,
            HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(0, 0, 4, 12), Margin = new Thickness(18, 0, 14, 16) };
        BuildPanels();
        BuildSessions(); BuildReader(); BuildSettings();
        PlaygroundAppearance.Scope(sessions); PlaygroundAppearance.Scope(settingsScroll);
        ActualThemeChanged += (_, _) => QueueThemePaint("actual-theme");
        PaintWorkspace("construct");
        service.Changed += ServiceChanged;
        refreshTimer.Tick += (_, _) => { refreshTimer.Stop(); if (!disposed) Refresh(); };
        searchTimer.Tick += async (_, _) => { searchTimer.Stop(); await SearchConversationsAsync(); };
        Loaded += async (_, _) => { QueueThemePaint("loaded"); await InitializeAsync(); QueueThemePaint("loaded-ready"); };
        SizeChanged += (_, _) => ArrangeWorkspace();
        PlaygroundAppearance.PolicyChanged += AppearanceChanged;
        AutomationProperties.SetLiveSetting(activity, AutomationLiveSetting.Polite);
    }
    public Task InitializeAsync() => initialize ??= InitializeCoreAsync();
    private async Task InitializeCoreAsync()
    {
        retryInitialization.Visibility = restoreState.Visibility = Visibility.Collapsed;
        try
        {
            await service.InitializeAsync(lifetime.Token); if (disposed) return;
            initialized = true; var first = service.Snapshot.Conversations.OrderByDescending(c => c.UpdatedAt).FirstOrDefault();
            selected = first?.Id; emptyDraft = DefaultOptions(); Refresh(forceConversations: true, forceOptions: true);
            await RefreshCredentialAsync();
        }
        catch (OperationCanceledException) { }
        catch
        {
            ShowError("对话资料未能安全读取；原始资料已保留，不会自动重置。");
            emptyTitle.Text = "对话资料需要恢复"; emptyDescription.Text = "可以重试读取，或在确认后恢复上一份有效版本。不会访问其他 Profile。";
            empty.Visibility = Visibility.Visible; emptyConfigure.Visibility = Visibility.Collapsed;
            retryInitialization.Visibility = Visibility.Visible; restoreState.Visibility = RestorePreviousState is null ? Visibility.Collapsed : Visibility.Visible;
            settingsScroll.IsHitTestVisible = sessions.IsHitTestVisible = false; memoryButton.IsEnabled = conversationMenu.IsEnabled = composer.IsEnabled = false;
        }
    }
    private void BuildSessions()
    {
        sessions.RowDefinitions.Add(new() { Height = GridLength.Auto }); sessions.RowDefinitions.Add(new() { Height = GridLength.Auto }); sessions.RowDefinitions.Add(new()); sessions.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var find = new Button { Content = PlaygroundAppearance.Icon("\uE721") }; PlaygroundAppearance.Apply(find, "PGIconButton"); PlaygroundAppearance.Tip(find, "搜索会话");
        find.Click += (_, _) => { search.Visibility = search.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible; if (search.Visibility == Visibility.Visible) search.Focus(FocusState.Programmatic); else { search.Text = ""; RefreshConversations(true); } };
        var newRow = new Grid { ColumnSpacing = 1 }; newRow.ColumnDefinitions.Add(new()); newRow.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); newRow.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var create = new Button { Content = PlaygroundAppearance.IconLabel("\uE710", "新建会话", 13), HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(8, 7, 8, 7), CornerRadius = new CornerRadius(7) }; PlaygroundAppearance.Apply(create);
        AutomationProperties.SetAutomationId(create, "PlaygroundNewConversation"); AutomationProperties.SetName(create, "新建对话");
        create.Click += async (_, _) => await RunAsync(() => CreateAsync(false));
        var newMenu = StyledMenu(); var temporary = new MenuFlyoutItem { Text = "新建临时会话" }; temporary.Click += async (_, _) => await RunAsync(() => CreateAsync(true)); newMenu.Items.Add(temporary);
        var newOptions = new Button { Content = PlaygroundAppearance.Icon("\uE712") }; PlaygroundAppearance.Apply(newOptions, "PGIconButton"); PlaygroundAppearance.Tip(newOptions, "临时会话与新建选项"); newOptions.Flyout = newMenu;
        newRow.Children.Add(create); Grid.SetColumn(find, 1); newRow.Children.Add(find); Grid.SetColumn(newOptions, 2); newRow.Children.Add(newOptions); sessions.Children.Add(newRow);
        PlaygroundAppearance.Apply(search, "PGTextBox");
        AutomationProperties.SetAutomationId(search, "PlaygroundConversationSearch"); AutomationProperties.SetName(search, "搜索历史对话");
        search.TextChanged += (_, _) => { if (!synchronizing) RefreshConversations(true); }; Grid.SetRow(search, 1); sessions.Children.Add(search);
        conversations.ItemsSource = conversationRows;
        conversations.ItemTemplate = (DataTemplate)templates["PlaygroundConversationTemplate"];
        conversations.SelectionChanged += async (_, _) => { if (!synchronizing && conversations.SelectedItem is PlaygroundConversationItem item) await RunAsync(() => SelectAsync(item.Id)); };
        conversations.ItemContainerStyle = PlaygroundAppearance.Style("PGConversationListViewItem");
        conversations.ContainerContentChanging += (_, e) => { if (!e.InRecycleQueue && e.Item is PlaygroundConversationItem item) e.ItemContainer.ContextFlyout = ConversationActions(item.Id); };
        AutomationProperties.SetAutomationId(conversations, "PlaygroundConversations"); AutomationProperties.SetName(conversations, "历史对话列表");
        Grid.SetRow(conversations, 2); sessions.Children.Add(conversations);
        var footer = new StackPanel { Spacing = 10 }; footer.Children.Add(sessionNote);
        var footerActions = new Grid { ColumnSpacing = 4, MinWidth = 0 }; footerActions.ColumnDefinitions.Add(new() { MinWidth = 0 }); footerActions.ColumnDefinitions.Add(new() { Width = new GridLength(34), MinWidth = 34 });
        var connect = new Button { Content = PlaygroundAppearance.IconLabel("\uE8D7", "连接与授权", 12), HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left, MinWidth = 0, Padding = new Thickness(6, 6, 6, 6) }; PlaygroundAppearance.Apply(connect); connect.Click += async (_, _) => await RunAsync(ShowConnectionAsync);
        AutomationProperties.SetAutomationId(connect, "PlaygroundConnection"); PlaygroundAppearance.Tip(connect, "连接与授权");
        memoryButton.Content = PlaygroundAppearance.Icon("\uE8EC"); PlaygroundAppearance.Apply(memoryButton, "PGIconButton"); memoryButton.Padding = new Thickness(0); PlaygroundAppearance.Tip(memoryButton, "偏好记忆");
        footerActions.Children.Add(connect); Grid.SetColumn(memoryButton, 1); footerActions.Children.Add(memoryButton); footer.Children.Add(footerActions); Grid.SetRow(footer, 3); sessions.Children.Add(footer);
    }
    private void BuildReader()
    {
        reader.RowDefinitions.Add(new() { Height = GridLength.Auto }); reader.RowDefinitions.Add(new() { Height = GridLength.Auto }); reader.RowDefinitions.Add(new()); reader.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var header = new Grid { MinHeight = 56, Padding = new Thickness(12, 8, 12, 8) }; header.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); header.ColumnDefinitions.Add(new()); header.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        header.Children.Add(titleLead); Grid.SetColumn(title, 1); header.Children.Add(title); var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Margin = new Thickness(8, 0, 0, 0) };
        sessionToggle.Content = PlaygroundAppearance.Icon("\uE700"); settingsToggle.Content = PlaygroundAppearance.Icon("\uE9E9"); conversationMenu.Content = PlaygroundAppearance.Icon("\uE712");
        foreach (var button in new[] { sessionToggle, settingsToggle, conversationMenu }) { PlaygroundAppearance.Apply(button, "PGIconButton"); button.Padding = new Thickness(0); }
        goActive.Content = new SymbolIcon(Symbol.Forward); PlaygroundAppearance.Apply(goActive, "PGIconButton"); PlaygroundAppearance.Tip(goActive, "查看正在回复的会话");
        PlaygroundAppearance.Tip(sessionToggle, "会话列表"); PlaygroundAppearance.Tip(settingsToggle, "运行设置"); PlaygroundAppearance.Tip(conversationMenu, "当前会话操作");
        title.RenderTransform = titleMotion;
        actions.Children.Add(goActive); actions.Children.Add(conversationMenu); actions.Children.Add(settingsTail); Grid.SetColumn(actions, 2); header.Children.Add(actions); reader.Children.Add(header);
        BuildPanelToggleLayer();
        AutomationProperties.SetAutomationId(settingsToggle, "PlaygroundRunSettings"); AutomationProperties.SetAutomationId(memoryButton, "PlaygroundMemories");
        AutomationProperties.SetAutomationId(sessionToggle, "PlaygroundSessionToggle");
        AutomationProperties.SetName(conversationMenu, "当前会话操作");
        sessionToggle.Click += (_, _) => ToggleSessions();
        settingsToggle.Click += (_, _) => ToggleSettings();
        memoryButton.Click += async (_, _) => await RunAsync(ShowMemoriesAsync);
        conversationMenu.Click += (_, _) => { if (selected is { } id) ConversationActions(id).ShowAt(conversationMenu); else _ = RunAsync(() => CreateAsync(true)); };
        Grid.SetRow(notification, 1); reader.Children.Add(notification);
        var reading = new Grid { MinWidth = 0, HorizontalAlignment = HorizontalAlignment.Stretch }; reading.Children.Add(messages);
        ScrollViewer.SetHorizontalScrollMode(messages, ScrollMode.Disabled); ScrollViewer.SetHorizontalScrollBarVisibility(messages, ScrollBarVisibility.Disabled);
        messages.MinWidth = composer.MinWidth = 0; messages.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        empty.Children.Add(emptyTitle); empty.Children.Add(emptyDescription); empty.Children.Add(emptyConfigure); empty.Children.Add(retryInitialization); empty.Children.Add(restoreState); reading.Children.Add(empty);
        emptyConfigure.Click += async (_, _) => await RunAsync(ShowConnectionAsync);
        retryInitialization.Click += async (_, _) => { initialize = null; await InitializeAsync(); };
        restoreState.Click += async (_, _) => await RestoreAsync();
        messages.ItemsSource = messageRows;
        messages.ItemContainerStyle = PlaygroundAppearance.Style("PGMessageListViewItem");
        messages.ItemTemplate = (DataTemplate)templates["PlaygroundMessageTemplate"];
        AutomationProperties.SetAutomationId(messages, "PlaygroundMessages"); AutomationProperties.SetName(messages, "对话消息"); Grid.SetRow(reading, 2); reader.Children.Add(reading);
        messages.Loaded += MessageListLoaded;
        messages.Unloaded += MessageListUnloaded;
        messages.LayoutUpdated += MessageListLayoutUpdated;
        messages.AddHandler(UIElement.PointerWheelChangedEvent, new PointerEventHandler(MessageWheelChanged), true);
        messages.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler(MessageKeyDown), true);
        messages.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(MessagePointerPressed), true);
        messages.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(MessagePointerEnded), true);
        messages.AddHandler(UIElement.PointerCanceledEvent, new PointerEventHandler(MessagePointerEnded), true);
        messages.AddHandler(UIElement.PointerCaptureLostEvent, new PointerEventHandler(MessagePointerEnded), true);
        var input = new Grid { Margin = new Thickness(24, 10, 24, 16), MinWidth = 0, MaxWidth = 916, HorizontalAlignment = HorizontalAlignment.Stretch }; input.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var inputContent = new Grid(); inputContent.RowDefinitions.Add(new() { Height = GridLength.Auto }); inputContent.RowDefinitions.Add(new() { Height = GridLength.Auto }); inputContent.RowDefinitions.Add(new() { Height = GridLength.Auto });
        inputSurface.Child = inputContent; input.Children.Add(inputSurface); inputContent.Children.Add(draftFiles);
        PlaygroundAppearance.Apply(composer, "PGTextBox");
        composer.UseSystemFocusVisuals = false;
        AutomationProperties.SetAutomationId(composer, "PlaygroundComposer"); AutomationProperties.SetName(composer, "输入讨论内容"); Grid.SetRow(composer, 1); composer.Margin = new Thickness(0, 0, 0, 4); inputContent.Children.Add(composer);
        composer.TextChanged += (_, _) => UpdateActions();
        composer.GotFocus += (_, _) => { inputFocused = true; PaintInputBorder(); }; composer.LostFocus += (_, _) => { inputFocused = false; PaintInputBorder(); };
        composer.KeyDown += async (_, e) => { if (e.Key == VirtualKey.Enter && InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control).HasFlag(CoreVirtualKeyStates.Down)) { e.Handled = true; await SendAsync(); } };
        var inputActions = new Grid(); inputActions.ColumnDefinitions.Add(new()); inputActions.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        PlaygroundAppearance.Apply(attach, "PGIconButton"); PlaygroundAppearance.Tip(attach, "添加文件"); AutomationProperties.SetAutomationId(attach, "PlaygroundAddFile");
        attach.HorizontalAlignment = HorizontalAlignment.Left; attach.Click += async (_, _) => await RunAsync(PickFilesAsync); inputActions.Children.Add(attach);
        PlaygroundAppearance.Apply(send, "PGPrimaryButton"); send.Content = PlaygroundAppearance.Icon("\uE74A", 16);
        AutomationProperties.SetAutomationId(send, "PlaygroundSend"); send.Click += async (_, _) => { if (service.IsGenerating && service.ActiveConversationId == selected) service.Stop(); else await SendAsync(); };
        send.VerticalAlignment = VerticalAlignment.Bottom; Grid.SetColumn(send, 1); inputActions.Children.Add(send); Grid.SetRow(inputActions, 2); inputContent.Children.Add(inputActions);
        goActive.Click += async (_, _) => { if (service.ActiveConversationId is { } id) await RunAsync(() => SelectAsync(id)); };
        Grid.SetRow(input, 3); reader.Children.Add(input);
        PlaygroundAppearance.Apply(emptyConfigure, "PGButton"); PlaygroundAppearance.Apply(retryInitialization, "PGButton"); PlaygroundAppearance.Apply(restoreState, "PGButton");
    }
    private void QueueThemePaint(string reason)
    {
        if (disposed) return; queuedPaintReason = reason; if (themePaintQueued) return; themePaintQueued = true;
        if (!DispatcherQueue.TryEnqueue(() =>
        {
            themePaintQueued = false; if (!disposed) PaintWorkspace("settled:" + queuedPaintReason);
        })) themePaintQueued = false;
    }
    private void PaintWorkspace(string reason = "direct")
    {
        lastPaintTheme = PaletteTheme.ToString(); lastPaintReason = reason;
        Foreground = PlaygroundAppearance.Brush(this, "PGTextBrush");
        Background = layout.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        workbenchFrame.Background = PlaygroundAppearance.Brush(this, "PGCanvasBrush");
        workbenchFrame.BorderBrush = PlaygroundAppearance.Brush(this, "PGBorderBrush");
        sessionsHost.Background = sessions.Background = PlaygroundAppearance.Brush(this, "PGSidebarBrush");
        settingsHost.Background = settings.Background = PlaygroundAppearance.Brush(this, "PGPanelBrush");
        sessionSplit.PaneBackground = sessionsHost.Background; settingsSplit.PaneBackground = settingsHost.Background;
        inputSurface.Background = PlaygroundAppearance.Brush(this, "PGInputBrush");
        PaintInputBorder();
        modelSurface.Background = PlaygroundAppearance.Brush(this, "PGSurfaceBrush"); modelSurface.BorderBrush = PlaygroundAppearance.Brush(this, "PGBorderBrush");
        sessionsHost.BorderBrush = settingsHost.BorderBrush = PlaygroundAppearance.Brush(this, "PGBorderBrush");
        foreach (var divider in visualDividers) divider.Background = PlaygroundAppearance.Brush(this, "PGBorderBrush");
        foreach (var text in new[] { subtitle, activity, emptyDescription, sessionNote, connectionStatus, capabilitiesNotice, progressStatus })
            text.Foreground = PlaygroundAppearance.Brush(this, "PGSecondaryBrush");
        foreach (var view in Descendants(this).OfType<PlaygroundMessageView>()) view.RefreshTheme();
        foreach (var view in Descendants(this).OfType<PlaygroundMarkdownView>()) view.RefreshTheme();
        paintTrace.Add(new { reason, actualTheme = ActualTheme.ToString(), canonicalWindowTheme = canonicalWindowTheme.ToString(), requestedTheme = RequestedTheme.ToString(), palette = PaletteTheme.ToString(), highContrast = PlaygroundAppearance.HighContrast,
            canvas = PlaygroundAppearance.ColorOf(workbenchFrame.Background), foreground = PlaygroundAppearance.ColorOf(Foreground), input = PlaygroundAppearance.ColorOf(inputSurface.Background) });
        if (paintTrace.Count > 8) paintTrace.RemoveAt(0);
    }
    private void PaintInputBorder() => inputSurface.BorderBrush = PlaygroundAppearance.Brush(this, inputFocused ? "PGComposerFocusBrush" : "PGComposerBorderBrush");
    private void AppearanceChanged() => DispatcherQueue.TryEnqueue(() => QueueThemePaint("policy"));
    private MenuFlyout StyledMenu()
    {
        var menu = new MenuFlyout(); var style = new Style(typeof(MenuFlyoutPresenter)) { BasedOn = PlaygroundAppearance.Style("PGMenuFlyoutPresenter") };
        style.Setters.Add(new Setter(Control.BackgroundProperty, PlaygroundAppearance.Brush(this, "PGPanelBrush")));
        style.Setters.Add(new Setter(Control.ForegroundProperty, PlaygroundAppearance.Brush(this, "PGTextBrush")));
        style.Setters.Add(new Setter(Control.BorderBrushProperty, PlaygroundAppearance.Brush(this, "PGBorderBrush")));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
        style.Setters.Add(new Setter(Control.CornerRadiusProperty, new CornerRadius(12)));
        menu.MenuFlyoutPresenterStyle = style; return menu;
    }
    private void ServiceChanged(PlaygroundChange change)
    {
        if (disposed) return;
        int credentialEpoch = change.Kind == "credentials" ? Interlocked.Increment(ref modelCredentialEpoch) : 0;
        DispatcherQueue.TryEnqueue(() => { if (disposed) return; if (credentialEpoch > handledModelCredentialEpoch) InvalidateModelCatalogues(); if (!refreshTimer.IsEnabled) refreshTimer.Start(); });
    }
    private void MessageListLoaded(object sender, RoutedEventArgs e)
    {
        DetachMessageScroll();
        messageScroll = Descendants(messages).OfType<ScrollViewer>().FirstOrDefault();
        if (messageScroll is null) return;
        lastMessageOffset = messageScroll.VerticalOffset;
        messageScroll.ViewChanged += MessageViewChanged;
        messageScroll.DirectManipulationStarted += MessageManipulationStarted;
        messageScroll.DirectManipulationCompleted += MessageManipulationCompleted;
        QueueFollowLatest(realizeLatest: true);
    }
    private void MessageListUnloaded(object sender, RoutedEventArgs e)
    {
        messageFollow.Invalidate(); queuedLatestScroll = null; ClearMessageScrollInput(); DetachMessageScroll();
    }
    private void DetachMessageScroll()
    {
        if (messageScroll is null) return;
        messageScroll.ViewChanged -= MessageViewChanged;
        messageScroll.DirectManipulationStarted -= MessageManipulationStarted;
        messageScroll.DirectManipulationCompleted -= MessageManipulationCompleted;
        messageScroll = null;
    }
    private void MessageListLayoutUpdated(object? sender, object e)
    {
        if (!disposed && messageScroll is { } scroll && messageFollow.ObserveLayout(scroll.ScrollableHeight, scroll.ViewportHeight))
            QueueFollowLatest();
    }
    private void PauseMessageFollow()
    {
        messageFollow.Suspend(); queuedLatestScroll = null; pendingLatestRealization = false;
    }
    private void ClearMessageScrollInput()
    {
        messageInputUntil = 0; messageInputDirection = 0; messagePointerScrolling = messageManipulating = false;
    }
    private void BeginMessageScrollInput(int direction)
    {
        PauseMessageFollow(); messageInputDirection = direction; messageInputUntil = Environment.TickCount64 + 1500;
    }
    private void MessageWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        int delta = e.GetCurrentPoint(messages).Properties.MouseWheelDelta;
        if (delta == 0) return;
        BeginMessageScrollInput(delta > 0 ? -1 : 1); QueueUserEndCheck();
    }
    private void MessageKeyDown(object sender, KeyRoutedEventArgs e)
    {
        int direction = e.Key switch
        {
            VirtualKey.Up or VirtualKey.PageUp or VirtualKey.Home => -1,
            VirtualKey.Down or VirtualKey.PageDown or VirtualKey.End => 1,
            VirtualKey.Space => InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift).HasFlag(CoreVirtualKeyStates.Down) ? -1 : 1,
            _ => 0
        };
        if (direction == 0) return;
        BeginMessageScrollInput(direction); QueueUserEndCheck();
    }
    private void MessagePointerPressed(object sender, PointerRoutedEventArgs e)
    {
        // Scrollbar drags have no wheel/key event. Text selection and clicking
        // normal message actions do not themselves change the reading choice.
        for (var node = e.OriginalSource as DependencyObject; node is not null && node != messages; node = VisualTreeHelper.GetParent(node))
            if (node is Microsoft.UI.Xaml.Controls.Primitives.ScrollBar)
            { messagePointerScrolling = true; BeginMessageScrollInput(0); return; }
    }
    private void MessagePointerEnded(object sender, PointerRoutedEventArgs e)
    {
        if (!messagePointerScrolling) return;
        messagePointerScrolling = false; ResumeMessageFollowAtEnd();
    }
    private void MessageManipulationStarted(object? sender, object e)
    {
        messageManipulating = true; BeginMessageScrollInput(0);
    }
    private void MessageManipulationCompleted(object? sender, object e)
    {
        messageManipulating = false; ResumeMessageFollowAtEnd();
    }
    private void MessageViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
    {
        if (messageScroll is not { } scroll) return;
        double offset = scroll.VerticalOffset;
        if (messagePointerScrolling || messageManipulating || Environment.TickCount64 <= messageInputUntil)
        {
            if (messagePointerScrolling || messageManipulating)
            {
                if (offset < lastMessageOffset - .5) messageInputDirection = -1;
                else if (offset > lastMessageOffset + .5) messageInputDirection = 1;
            }
            if (messageInputDirection < 0 && offset < lastMessageOffset - .5) PauseMessageFollow();
            else if (messageInputDirection > 0 && !messagePointerScrolling && !messageManipulating) ResumeMessageFollowAtEnd();
        }
        lastMessageOffset = offset;
    }
    private void QueueUserEndCheck()
    {
        var conversation = selected; long generation = messageFollow.Generation;
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            if (!disposed && selected == conversation && messageFollow.Generation == generation) ResumeMessageFollowAtEnd();
        });
    }
    private void ResumeMessageFollowAtEnd()
    {
        if (messageInputDirection <= 0 || messagePointerScrolling || messageManipulating || messageScroll is not { ViewportHeight: > 0 } scroll
            || scroll.ScrollableHeight - scroll.VerticalOffset > 80) return;
        bool wasFollowing = messageFollow.Following; messageFollow.ResumeAtEnd();
        if (!wasFollowing) QueueFollowLatest();
    }
    private void QueueFollowLatest(bool realizeLatest = false)
    {
        if (disposed || messageScroll is null || !messages.IsLoaded || !messageFollow.TryCapture(out var ticket)) return;
        queuedLatestScroll = ticket; realizeLatestQueued |= realizeLatest;
        if (latestScrollQueued) return; latestScrollQueued = true;
        if (!DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            latestScrollQueued = false; var target = queuedLatestScroll; bool realize = realizeLatestQueued;
            queuedLatestScroll = null; realizeLatestQueued = false;
            if (disposed || target is not { } current || selected != current.ConversationId || !messageFollow.IsCurrent(current)
                || messageScroll is not { } scroll || !messages.IsLoaded || messagePointerScrolling || messageManipulating) return;
            var row = messageRows.LastOrDefault(); if (row?.Message.Id != current.LastMessageId) return;
            // Realize a new virtualized tail once. Its final measured extent can
            // change later; LayoutUpdated corrects that only while following.
            if (realize) messages.ScrollIntoView(row, ScrollIntoViewAlignment.Default);
            if (messageFollow.IsCurrent(current) && selected == current.ConversationId)
                scroll.ChangeView(null, scroll.ScrollableHeight, null, true);
        })) { latestScrollQueued = false; queuedLatestScroll = null; realizeLatestQueued = false; }
    }
    private void Refresh(bool forceConversations = false, bool forceOptions = false)
    {
        if (disposed || !initialized) return; refreshes++;
        var current = Current;
        bool replacedSelection = selected is not null && current is null;
        if (replacedSelection) { navigationGeneration++; InvalidateOptionsNavigation(); selected = service.Snapshot.Conversations.OrderByDescending(x => x.UpdatedAt).FirstOrDefault()?.Id; current = Current; forceOptions = true; }
        bool selectionChanged = messageFollow.ConversationId != selected;
        messageFollow.Select(selected); if (selectionChanged) ClearMessageScrollInput();
        ConfirmSubmittedDraft(); ConfirmRetriedTurn(); RenderDraftFiles();
        settingsScroll.IsHitTestVisible = sessions.IsHitTestVisible = true; memoryButton.IsEnabled = conversationMenu.IsEnabled = composer.IsEnabled = true;
        RefreshConversations(forceConversations);
        title.Text = current?.Title ?? "对话"; ToolTipService.SetToolTip(title, current?.Title ?? "对话");
        sessionNote.Text = current?.Temporary == true ? "临时会话 · 关闭后不保留" : current is null ? "独立讨论空间" : "最近更新 " + current.UpdatedAt.ToLocalTime().ToString("MM-dd HH:mm");
        subtitle.Text = current?.Temporary == true ? "临时会话 · 不写入长期聊天、检索或记忆" : "围绕作品、经历与观点展开讨论";
        PlaygroundMessage[] desired = current is null ? [] : service.Snapshot.Messages.Where(x => x.ConversationId == current.Id).ToArray();
        var ids = desired.Select(x => x.Id).ToHashSet();
        bool rowsChanged = false;
        for (int i = messageRows.Count - 1; i >= 0; i--) if (!ids.Contains(messageRows[i].Message.Id)) { messageRows.RemoveAt(i); rowsChanged = true; }
        for (int i = 0; i < desired.Length; i++)
        {
            var message = desired[i]; var existing = messageRows.FirstOrDefault(x => x.Message.Id == message.Id);
            bool strict = message.Status == PlaygroundMessageStatus.Generating && activeSpoilers.GetValueOrDefault(message.ConversationId, current?.Options.Spoiler != PlaygroundSpoilerMode.Full);
            if (existing is null) { existing = new(message, strict, BrowserDataDirectory, OpenSourceAsync, RetryAsync); messageRows.Insert(Math.Min(i, messageRows.Count), existing); rowsChanged = true; }
            else { int at = messageRows.IndexOf(existing); if (at != i) { messageRows.Move(at, i); rowsChanged = true; } rowsChanged |= existing.Message != message || existing.Strict != strict; existing.Update(message, strict); }
        }
        bool latestChanged = messageFollow.SetLatest(desired.LastOrDefault()?.Id);
        if (rowsChanged || latestChanged || pendingLatestRealization) QueueFollowLatest(latestChanged || pendingLatestRealization);
        pendingLatestRealization = false;
        empty.Visibility = messageRows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        emptyTitle.Text = !credentialAvailable ? "先连接你的模型服务商"
            : !PlaygroundAuthorizationPolicy.CanSend(service.Snapshot.Settings) ? "先完成发送授权" : "从一个真实的问题开始";
        emptyDescription.Text = !credentialAvailable ? "API Key 仅保存在本机当前 Profile。记录、历史和记忆按你授权的范围使用。"
            : "可以聊最近的游玩、找回过去的观点，或核实作品资料。没有记录时会明确说明。";
        emptyConfigure.Visibility = !credentialAvailable || !PlaygroundAuthorizationPolicy.CanSend(service.Snapshot.Settings) ? Visibility.Visible : Visibility.Collapsed;
        if (forceOptions) SyncOptions();
        SyncGenerationSettings(); UpdateConnectionStatus(); UpdateActions(); ArrangeWorkspace();
        if (replacedSelection) _ = RefreshCredentialAsync();
    }
    private void RefreshConversations(bool force = false)
    {
        if (!initialized || disposed) return;
        var array = service.Snapshot.Conversations; if (!force && array == lastConversations) return; lastConversations = array;
        // Temporary conversations remain reachable in this window, while the
        // service deliberately excludes them from the durable search index.
        searchGeneration++; searchTimer.Stop();
        if (!string.IsNullOrWhiteSpace(search.Text)) { searchTimer.Start(); return; }
        ApplyConversationRows(array.OrderByDescending(x => x.UpdatedAt).Take(100).ToArray());
    }
    private async Task SearchConversationsAsync()
    {
        if (!initialized || disposed || string.IsNullOrWhiteSpace(search.Text)) return;
        string query = search.Text.Trim(); int generation = searchGeneration;
        try
        {
            var wanted = await Task.Run(() => service.SearchConversations(query).ToArray(), lifetime.Token);
            if (disposed || generation != searchGeneration || search.Text.Trim() != query) return;
            ApplyConversationRows(wanted);
        }
        catch (OperationCanceledException) { }
        catch { if (!disposed && generation == searchGeneration) ShowError("会话搜索未能完成；原有会话仍保留，可以重试。"); }
    }
    private void ApplyConversationRows(PlaygroundConversation[] wanted)
    {
        var ids = wanted.Select(x => x.Id).ToHashSet(); synchronizing = true;
        try
        {
            for (int i = conversationRows.Count - 1; i >= 0; i--) if (!ids.Contains(conversationRows[i].Id)) conversationRows.RemoveAt(i);
            for (int i = 0; i < wanted.Length; i++)
            {
                var existing = conversationRows.FirstOrDefault(x => x.Id == wanted[i].Id);
                if (existing is null) { existing = new(wanted[i]); conversationRows.Insert(Math.Min(i, conversationRows.Count), existing); }
                else { int at = conversationRows.IndexOf(existing); if (at != i) conversationRows.Move(at, i); existing.Update(wanted[i]); }
            }
            conversations.SelectedItem = conversationRows.FirstOrDefault(x => x.Id == selected);
        }
        finally { synchronizing = false; }
    }
    private PlaygroundConversation? Current => selected is { } id ? service.Snapshot.Conversations.FirstOrDefault(x => x.Id == id) : null;
    private PlaygroundConversationOptions Draft
    {
        get => OptionsFor(selected);
        set => StoreOptions(selected, value);
    }
    private PlaygroundConversationOptions DefaultOptions()
    {
        var connection = service.Snapshot.Settings.Connections.FirstOrDefault();
        return connection is null ? new() : new() { Provider = connection.Provider, Endpoint = connection.Endpoint, Model = connection.Model };
    }
    private async Task<Guid?> CreateAsync(bool temporary, bool preserveComposer = false)
    {
        if (!initialized) return null;
        int navigation = ++navigationGeneration;
        var preservedFiles = preserveComposer ? DraftFiles : ImmutableArray<AttachmentDraft>.Empty;
        SaveTextDraft(); var options = Draft with { UseSavedContext = !temporary,
            WorkProgress = temporary ? ImmutableDictionary<string, string>.Empty : Draft.WorkProgress };
        SaveOutgoingOptions(); InvalidateOptionsNavigation();
        var id = await service.CreateConversationAsync(temporary, lifetime.Token); await service.UpdateConversationAsync(id, options, lifetime.Token);
        drafts[id] = options; if (disposed || navigation != navigationGeneration) return null;
        selected = id; if (!preservedFiles.IsEmpty) { attachmentDrafts[id] = preservedFiles; unnamedAttachments = unnamedAttachments.Where(x => !preservedFiles.Any(p => p.Id == x.Id)).ToImmutableArray(); }
        if (!preserveComposer) composer.Text = ""; search.Text = "";
        Refresh(forceConversations: true, forceOptions: true); await RefreshCredentialAsync();
        if (!disposed && navigation == navigationGeneration && selected == id) composer.Focus(FocusState.Programmatic);
        return !disposed && navigation == navigationGeneration && selected == id ? id : null;
    }
    private async Task SelectAsync(Guid id, bool showLatest = true)
    {
        if (selected == id) { if (!showLatest) { PauseMessageFollow(); ClearMessageScrollInput(); } return; }
        if (!service.Snapshot.Conversations.Any(x => x.Id == id)) return;
        int navigation = ++navigationGeneration;
        SaveTextDraft(); SaveOutgoingOptions(); InvalidateOptionsNavigation();
        selected = id; messageFollow.Select(id); ClearMessageScrollInput(); if (!showLatest) PauseMessageFollow();
        composer.Text = textDrafts.GetValueOrDefault(id, ""); Refresh(forceConversations: true, forceOptions: true); await RefreshCredentialAsync();
        if (!disposed && navigation == navigationGeneration && selected == id && showLatest) QueueFollowLatest(realizeLatest: true);
    }
    private void SaveTextDraft() { if (selected is { } id) textDrafts[id] = composer.Text; }
    private async Task SendAsync()
    {
        if (disposed || !initialized || sending || pickingAttachments || service.IsGenerating || string.IsNullOrWhiteSpace(composer.Text) && DraftFiles.IsEmpty) return;
        sending = true; UpdateActions(); var scrollIntent = messageFollow.CaptureIntent();
        try
        {
            if (!credentialAvailable) { await ShowConnectionAsync(); return; }
            string text = composer.Text; var options = Draft; var files = DraftFiles;
            if (string.IsNullOrWhiteSpace(options.Model)) { ShowError("请先选择或填写模型 ID；输入内容已保留。"); return; }
            if (!PlaygroundAuthorizationPolicy.CanSend(service.Snapshot.Settings))
            { ShowError("请先在左侧“连接与授权”中启用全局发送授权；输入内容已保留。"); return; }
            if (Current is null && await CreateAsync(false, preserveComposer: true) is null) return;
            var current = Current ?? throw new InvalidOperationException();
            await SaveOptionsAsync(current.Id, options);
            activeSpoilers[current.Id] = options.Spoiler != PlaygroundSpoilerMode.Full;
            if (scrollIntent?.ConversationId != current.Id) scrollIntent = messageFollow.CaptureIntent();
            pendingSubmission = new(current.Id, text, files, service.Snapshot.Messages.Where(x => x.ConversationId == current.Id && x.Role == "user").Select(x => x.Id).ToHashSet(), scrollIntent);
            notification.IsOpen = false;
            var pending = service.SendAsync(current.Id, text, files.Select(x => x.Snapshot).ToImmutableArray(), CancellationToken.None); Refresh();
            await pending;
        }
        catch (OperationCanceledException) { }
        catch (PlaygroundProviderException error) { ShowError(error.Failure.Message); }
        catch { ShowError("这轮讨论未能完成，请检查连接、模型和授权。已接收的内容保持原状。可手动重试。"); }
        finally { sending = false; if (!disposed) { ConfirmSubmittedDraft(); pendingSubmission = null; Refresh(); } }
    }
    private void ConfirmSubmittedDraft()
    {
        if (pendingSubmission is not { } pending || !service.Snapshot.Messages.Any(x => x.ConversationId == pending.ConversationId && x.Role == "user"
            && !pending.PreviousUsers.Contains(x.Id) && x.Text == pending.Text.Trim() && x.Attachments.SequenceEqual(pending.Files.Select(f => f.Snapshot)))) return;
        // Clear only after the service accepted the message. Late admission must
        // never erase new typing or the draft of another selected conversation.
        if (pending.ScrollIntent is { } intent && intent.ConversationId == pending.ConversationId && messageFollow.AcceptOwnTurn(intent))
        { pendingLatestRealization = true; ClearMessageScrollInput(); }
        if (selected == pending.ConversationId && composer.Text == pending.Text) composer.Text = "";
        if (textDrafts.GetValueOrDefault(pending.ConversationId) == pending.Text) textDrafts.Remove(pending.ConversationId);
        var sentIds = pending.Files.Select(x => x.Id).ToHashSet();
        if (sentIds.Count != 0) StoreFiles(pending.ConversationId, FilesFor(pending.ConversationId).Where(x => !sentIds.Contains(x.Id)).ToImmutableArray());
        pendingSubmission = null;
    }
    private async Task RetryAsync(Guid conversation)
    {
        if (service.IsGenerating || sending) return;
        sending = true; UpdateActions();
        pendingRetry = new(conversation, service.Snapshot.Messages.Where(x => x.ConversationId == conversation && x.Role == "assistant").Select(x => x.Id).ToHashSet(), messageFollow.CaptureIntent());
        try { activeSpoilers[conversation] = Current?.Options.Spoiler != PlaygroundSpoilerMode.Full; var pending = service.RetryAsync(conversation, CancellationToken.None); Refresh(); await pending; }
        catch (OperationCanceledException) { }
        catch (PlaygroundProviderException error) { ShowError(error.Failure.Message); }
        catch { ShowError("重试未能完成；未自动重复发送，请检查配置后再试。"); }
        finally { sending = false; if (!disposed) { ConfirmRetriedTurn(); pendingRetry = null; Refresh(); } }
    }
    private void ConfirmRetriedTurn()
    {
        if (pendingRetry is not { } pending || !service.Snapshot.Messages.Any(x => x.ConversationId == pending.ConversationId
            && x.Role == "assistant" && !pending.PreviousAssistants.Contains(x.Id))) return;
        if (pending.ScrollIntent is { } intent && intent.ConversationId == pending.ConversationId && messageFollow.AcceptOwnTurn(intent))
        { pendingLatestRealization = true; ClearMessageScrollInput(); }
        pendingRetry = null;
    }
    private void UpdateActions()
    {
        if (disposed) return;
        bool own = service.IsGenerating && service.ActiveConversationId == selected;
        if (shownStop != own) { shownStop = own; send.Content = PlaygroundAppearance.Icon(own ? "\uE71A" : "\uE74A", 16); ToolTipService.SetToolTip(send, own ? "停止回复" : "发送消息"); }
        AutomationProperties.SetName(send, own ? "停止当前回复" : "发送消息");
        send.IsEnabled = initialized && (own || operations == 0 && !sending && !pickingAttachments && !service.IsGenerating && (!string.IsNullOrWhiteSpace(composer.Text) || !DraftFiles.IsEmpty));
        attach.IsEnabled = initialized && pickAttachments is not null && !pickingAttachments && operations == 0;
        generationSave.IsEnabled = initialized && generationDirty && !service.IsGenerating && operations == 0;
        foreach (var control in new[] { generationOutput, generationContext, generationTimeout }) control.IsEnabled = initialized && !service.IsGenerating && operations == 0;
        bool other = service.IsGenerating && !own;
        goActive.Visibility = other ? Visibility.Visible : Visibility.Collapsed;
        activity.Text = other ? "另一会话正在回复；本 Profile 同时只运行一轮。"
            : own ? service.Snapshot.Messages.LastOrDefault(x => x.ConversationId == selected && x.Status == PlaygroundMessageStatus.Generating)?.Activities.LastOrDefault() ?? "正在回复…"
            : !credentialAvailable ? "尚未配置此服务商的 API Key。"
            : !PlaygroundAuthorizationPolicy.CanSend(service.Snapshot.Settings) ? "尚未授予全局发送授权。"
            : Draft.Search == PlaygroundSearchMode.Off ? "联网搜索已关闭；对话仍由所选服务商 API 处理。" : "";
        activity.Visibility = string.IsNullOrWhiteSpace(activity.Text) ? Visibility.Collapsed : Visibility.Visible;
        AutomationProperties.SetHelpText(send, activity.Text);
    }
    private async Task OpenSourceAsync(PlaygroundSource source)
    {
        if (source.Kind == PlaygroundSourceKind.Attachment)
        {
            if (Guid.TryParse(source.MessageId, out var messageId) && int.TryParse(source.TargetId, out int index)
                && service.Snapshot.Messages.FirstOrDefault(x => x.Id == messageId) is { } message && index >= 0 && index < message.Attachments.Length)
            { await PlaygroundAttachmentPreview.ShowAsync(XamlRoot, PaletteTheme, message.Attachments[index]); return; }
            ShowError("这份附件快照已不存在，原文件不会被重新打开。"); return;
        }
        if (source.Kind is PlaygroundSourceKind.ChatMessage or PlaygroundSourceKind.Memory)
        {
            Guid? conversation = Guid.TryParse(source.TargetId, out var target) && service.Snapshot.Conversations.Any(x => x.Id == target) ? target : null;
            Guid? message = Guid.TryParse(source.MessageId, out var origin) ? origin : null;
            if (conversation is null && message is { } originMessageId) conversation = service.Snapshot.Messages.FirstOrDefault(x => x.Id == originMessageId)?.ConversationId;
            if (conversation is null && source.Kind == PlaygroundSourceKind.Memory && Guid.TryParse(source.TargetId ?? source.Id, out var memoryId)) conversation = service.Snapshot.Memories.FirstOrDefault(x => x.Id == memoryId)?.ConversationId;
            if (conversation is { } found)
            {
                await SelectAsync(found, showLatest: false); if (disposed || selected != found) return;
                if (message is { } selectedMessageId && messageRows.FirstOrDefault(x => x.Message.Id == selectedMessageId) is { } row) messages.ScrollIntoView(row, ScrollIntoViewAlignment.Leading);
                return;
            }
            ShowError("原始讨论已删除或当前不可读取，这条来源不会跳到其他 Profile。"); return;
        }
        if (OpenSource is not null) { await OpenSource(source); return; }
        if (source.Kind == PlaygroundSourceKind.Web && PlaygroundMarkdownView.Allowed(source.Url)) { await Windows.System.Launcher.LaunchUriAsync(new Uri(source.Url!)); return; }
        ShowError("此来源的位置暂时不可用；原始记录没有更改。");
    }
    private MenuFlyout ConversationActions(Guid id)
    {
        var menu = StyledMenu(); var rename = new MenuFlyoutItem { Text = "重命名" }; var delete = new MenuFlyoutItem { Text = "删除会话" };
        rename.Click += async (_, _) => await RunAsync(() => RenameAsync(id)); delete.Click += async (_, _) => await RunAsync(() => DeleteAsync(id));
        var createTemporary = new MenuFlyoutItem { Text = "新建临时会话" }; createTemporary.Click += async (_, _) => await RunAsync(() => CreateAsync(true));
        menu.Items.Add(rename); menu.Items.Add(delete); menu.Items.Add(new MenuFlyoutSeparator()); menu.Items.Add(createTemporary); return menu;
    }
    private async Task RenameAsync(Guid id)
    {
        var conversation = service.Snapshot.Conversations.FirstOrDefault(x => x.Id == id); if (conversation is null) return;
        var name = new TextBox { Text = conversation.Title, Header = "会话名称", MaxLength = 120, MinWidth = 280 };
        if (await DialogAsync("重命名会话", name, "保存") == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(name.Text)) await service.RenameConversationAsync(id, name.Text.Trim(), lifetime.Token);
        Refresh(forceConversations: true);
    }
    private async Task DeleteAsync(Guid id)
    {
        var content = new TextBlock { Text = "会话及其派生记忆、摘要和检索记录会从本机移除。已发送给服务商的信息及已有导出副本无法由此撤回。", MaxWidth = 460, TextWrapping = TextWrapping.Wrap };
        if (await DialogAsync("删除这段会话？", content, "删除") != ContentDialogResult.Primary) return;
        await service.DeleteConversationAsync(id, lifetime.Token); drafts.Remove(id); textDrafts.Remove(id); attachmentDrafts.Remove(id);
        if (selected == id) { navigationGeneration++; InvalidateOptionsNavigation(); selected = service.Snapshot.Conversations.OrderByDescending(x => x.UpdatedAt).FirstOrDefault()?.Id; composer.Text = selected is { } next ? textDrafts.GetValueOrDefault(next, "") : ""; }
        Refresh(forceConversations: true, forceOptions: true); await RefreshCredentialAsync();
    }
    private async Task RunAsync(Func<Task> operation)
    {
        if (disposed) return; operations++; UpdateActions();
        try { await operation(); }
        catch (OperationCanceledException) { }
        catch (PlaygroundProviderException error) { ShowError(error.Failure.Message); }
        catch { ShowError("操作未能完成，请检查所选内容与配置后重试。原有资料保持不变。"); }
        finally { operations--; if (!disposed) { Refresh(); UpdateActions(); } }
    }
    private async Task RestoreAsync()
    {
        if (initialized || RestorePreviousState is null) return;
        if (await DialogAsync("恢复上一份有效资料？", Label("会保留当前密文副本，再恢复同一 Profile 的上一个有效版本。较新格式不能降级覆盖；没有有效备份时不会自动新建空资料。", 14), "保留当前并恢复") != ContentDialogResult.Primary) return;
        restoreState.IsEnabled = retryInitialization.IsEnabled = false;
        try
        {
            await RestorePreviousState(); initialize = null; notification.IsOpen = false; await InitializeAsync();
        }
        catch (FileNotFoundException) { ShowError("没有上一个有效版本可恢复。当前资料已保留，请保留该 Profile 并检查备份。"); }
        catch (InvalidOperationException error) when (error.Message is "没有上一个有效版本可恢复。" or "较新格式不能降级恢复。") { ShowError(error.Message); }
        catch { ShowError("恢复未能完成；当前资料和已有备份均保留。请检查备份是否有效，不能降级覆盖较新格式。"); }
        finally { restoreState.IsEnabled = retryInitialization.IsEnabled = true; }
    }
    private void ShowError(string message) { if (disposed) return; notification.Message = message; notification.Severity = InfoBarSeverity.Warning; notification.IsOpen = true; }
    private async Task<ContentDialogResult> DialogAsync(string heading, object content, string primary = "保存")
    {
        var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = heading, Content = content, PrimaryButtonText = primary, CloseButtonText = "取消", DefaultButton = ContentDialogButton.None,
            RequestedTheme = PaletteTheme, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        PlaygroundAppearance.Scope(dialog); dialog.Style = PlaygroundAppearance.Style("PGDialog"); dialog.PrimaryButtonStyle = PlaygroundAppearance.Style("PGDialogPrimaryAction"); dialog.CloseButtonStyle = PlaygroundAppearance.Style("PGDialogAction"); StyleContent(content);
        dialog.Resources["ContentDialogMaxWidth"] = 760d;
        return await dialog.ShowAsync();
    }
    private Brush Brush(string key) => PlaygroundAppearance.Brush(this, key switch { "TextSecondaryBrush" => "PGSecondaryBrush", "DividerBrush" => "PGBorderBrush", _ => key });
    private static void StyleContent(object? value)
    {
        if (value is TextBox text) PlaygroundAppearance.Apply(text, "PGTextBox");
        else if (value is PasswordBox password) PlaygroundAppearance.Apply(password, "PGPasswordBox");
        else if (value is NumberBox number) PlaygroundAppearance.Apply(number, "PGNumberBox");
        else if (value is ComboBox combo) PlaygroundAppearance.Apply(combo, "PGComboBox");
        else if (value is Button button) PlaygroundAppearance.Apply(button, "PGButton");
        else if (value is CheckBox check) PlaygroundAppearance.Apply(check, "PGCheckBox");
        else if (value is ListView list) list.ItemContainerStyle = PlaygroundAppearance.Style("PGListViewItem");
        if (value is Panel panel) foreach (var child in panel.Children) StyleContent(child);
        else if (value is ContentControl content) StyleContent(content.Content);
        else if (value is Border border) StyleContent(border.Child);
        else if (value is ScrollViewer scroll) StyleContent(scroll.Content);
    }
    private static TextBlock Label(string text, double size = 13) => new() { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap };
    internal object Evidence => new { initialized, selected, width = ActualWidth, height = ActualHeight, theme = ActualTheme.ToString(),
        conversations = conversationRows.Count, messages = messageRows.Count, generating = service.IsGenerating, service.ActiveConversationId,
        sessionPanel = sessionSplit.IsPaneOpen, settingsPanel = settingsSplit.IsPaneOpen,
        sessionMode = sessionSplit.DisplayMode.ToString(), settingsMode = settingsSplit.DisplayMode.ToString(), sessionsPreferredOpen, settingsPreferredOpen,
        settingsWidth = settingsSplit.DisplayMode == SplitViewDisplayMode.Inline && settingsSplit.IsPaneOpen ? settingsSplit.OpenPaneLength : 0,
        readingWidth = reader.ActualWidth, workspaceWidth = WorkspaceWidth, workspaceHeight = WorkspaceHeight,
        header = new { sessions = HeaderBox(sessionToggle), settings = HeaderBox(settingsToggle), title = HeaderBox(title), leftLead = titleLead.Width,
            activeAnimations = headerAnimations.Count, firstConversationRowHeight = (conversations.ContainerFromIndex(0) as FrameworkElement)?.ActualHeight },
        frameWidth = workbenchFrame.ActualWidth, frameHeight = workbenchFrame.ActualHeight, frameInset = workbenchFrame.Margin.Left,
        frameMargin = new { workbenchFrame.Margin.Left, workbenchFrame.Margin.Top, workbenchFrame.Margin.Right, workbenchFrame.Margin.Bottom }, refreshes,
        motionEnabled = PanelMotionEnabled, sessionMotionGroups = splitMotion.TryGetValue(sessionSplit, out var sessionMotion) ? sessionMotion.Groups.Count : 0,
        settingsMotionGroups = splitMotion.TryGetValue(settingsSplit, out var settingsMotion) ? settingsMotion.Groups.Count : 0,
        sessionTransitions = sessionMotion?.Groups.Sum(x => x.Group.Transitions.Count) ?? 0,
        settingsTransitions = settingsMotion?.Groups.Sum(x => x.Group.Transitions.Count) ?? 0,
        lastPaintTheme, lastPaintReason, paletteTheme = PaletteTheme.ToString(), canonicalWindowTheme = canonicalWindowTheme.ToString(), requestedTheme = RequestedTheme.ToString(), highContrast = PlaygroundAppearance.HighContrast,
        canvasColor = PlaygroundAppearance.ColorOf(workbenchFrame.Background), layoutColor = PlaygroundAppearance.ColorOf(layout.Background),
        foregroundColor = PlaygroundAppearance.ColorOf(Foreground), readerForegroundColor = PlaygroundAppearance.ColorOf(title.Foreground),
        inputColor = PlaygroundAppearance.ColorOf(inputSurface.Background), palette = PlaygroundAppearance.PaletteEvidence, paintTrace = paintTrace.ToArray(),
        sourceKinds = messageRows.SelectMany(x => x.Message.Sources).Select(x => x.Kind.ToString()).Distinct().ToArray(),
        messageScroll = new { messageFollow.Following, messageFollow.Generation, messageFollow.ConversationId, messageFollow.LastMessageId,
            offset = messageScroll?.VerticalOffset, extent = messageScroll?.ScrollableHeight, viewport = messageScroll?.ViewportHeight, latestScrollQueued },
        rows = messageRows.Select(x => new { x.Message.Id, x.Message.Role, status = x.Message.Status.ToString(), characters = x.Message.Text.Length, x.Message.Retention }).ToArray() };
    public void Dispose()
    {
        if (disposed) return; disposed = true; CancelModelLoading(); service.Changed -= ServiceChanged; PlaygroundAppearance.PolicyChanged -= AppearanceChanged; refreshTimer.Stop(); searchTimer.Stop(); searchGeneration++;
        messageFollow.Invalidate(); queuedLatestScroll = null; DetachMessageScroll();
        lifetime.Cancel(); lifetime.Dispose(); DisposePanels();
        foreach (var view in Descendants(this).OfType<PlaygroundMessageView>()) view.Dispose();
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        { var child = VisualTreeHelper.GetChild(root, i); yield return child; foreach (var descendant in Descendants(child)) yield return descendant; }
    }
}
