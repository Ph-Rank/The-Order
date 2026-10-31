using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using TheOrder.Application.Playground;

namespace TheOrder.WinUI.Views;

public sealed partial class PlaygroundView
{
    private readonly SplitView sessionSplit = new() { PanePlacement = SplitViewPanePlacement.Left, OpenPaneLength = 220,
        DisplayMode = SplitViewDisplayMode.Inline, IsPaneOpen = true };
    private readonly SplitView settingsSplit = new() { PanePlacement = SplitViewPanePlacement.Right, OpenPaneLength = 288,
        DisplayMode = SplitViewDisplayMode.Inline, IsPaneOpen = true };
    private bool sessionsPreferredOpen = true, settingsPreferredOpen = true;
    private bool arrangingPanels;
    private PlaygroundPanelPriority panelPriority = PlaygroundPanelPriority.Sessions;
    private PlaygroundPanelLayout panelLayout;
    private double arrangedWidth = -1, arrangedHeight = -1;
    private long sessionPaneCallback, settingsPaneCallback;
    private readonly Dictionary<SplitView, SplitMotion> splitMotion = [];
    private bool fixtureReducedMotion;
    private readonly Border titleLead = new() { Width = 44 };
    private readonly Border settingsTail = new() { Width = 34 };
    private readonly TranslateTransform sessionToggleMotion = new(), titleMotion = new();
    private readonly Dictionary<TranslateTransform, Storyboard> headerAnimations = [];
    private bool panelHeaderReady;
    private bool? headerSessionOpen, headerSessionInline;
    private double headerPaneLength = -1, headerLead = 44;
    private bool PanelMotionEnabled => !fixtureReducedMotion && InteractionMotion.Enabled;
    private sealed record SplitMotion(FrameworkElement Root, List<(VisualStateGroup Group, VisualTransition[] Transitions)> Groups, bool Enabled);

    private void BuildPanels()
    {
        sessionSplit.Background = settingsSplit.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        var sessionsPane = new Grid(); sessionsPane.RowDefinitions.Add(new() { Height = GridLength.Auto }); sessionsPane.RowDefinitions.Add(new());
        sessionsPane.Children.Add(PaneHeader("会话", 16)); Grid.SetRow(sessions, 1); sessionsPane.Children.Add(sessions); sessionsHost.Child = sessionsPane;
        var settingsPane = new Grid(); settingsPane.RowDefinitions.Add(new() { Height = GridLength.Auto }); settingsPane.RowDefinitions.Add(new());
        settingsPane.Children.Add(PaneHeader("运行设置", 14)); Grid.SetRow(settingsScroll, 1); settingsPane.Children.Add(settingsScroll); settingsHost.Child = settingsPane;
        sessionSplit.Pane = sessionsHost; sessionSplit.Content = settingsSplit;
        settingsSplit.Pane = settingsHost; settingsSplit.Content = reader;
        sessionSplit.MinWidth = settingsSplit.MinWidth = reader.MinWidth = 0;
        layout.Children.Add(sessionSplit);
        sessionPaneCallback = sessionSplit.RegisterPropertyChangedCallback(SplitView.IsPaneOpenProperty, (_, _) => PaneChanged(true));
        settingsPaneCallback = settingsSplit.RegisterPropertyChangedCallback(SplitView.IsPaneOpenProperty, (_, _) => PaneChanged(false));
        sessionSplit.Loaded += (_, _) => ArrangeWorkspace(); settingsSplit.Loaded += (_, _) => ArrangeWorkspace();
    }
    private static Grid PaneHeader(string text, double size)
    {
        var header = new Grid { MinHeight = 56, Padding = new Thickness(18, 8, 12, 8), ColumnSpacing = 8 };
        header.ColumnDefinitions.Add(new()); header.ColumnDefinitions.Add(new() { Width = new GridLength(34) });
        header.Children.Add(new TextBlock { Text = text, FontSize = size, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        return header;
    }
    private void BuildPanelToggleLayer()
    {
        // One real button per side stays mounted: focus and pointer state are
        // preserved while RenderTransform moves the left button with the pane.
        var layer = new Grid(); Canvas.SetZIndex(layer, 10);
        sessionToggle.HorizontalAlignment = HorizontalAlignment.Left; settingsToggle.HorizontalAlignment = HorizontalAlignment.Right;
        foreach (var button in new[] { sessionToggle, settingsToggle }) { button.VerticalAlignment = VerticalAlignment.Top; button.Margin = new Thickness(12, 11, 12, 0); layer.Children.Add(button); }
        sessionToggle.RenderTransform = sessionToggleMotion; layout.Children.Add(layer); panelHeaderReady = true; SyncPanelHeader(false, true);
    }
    private double WorkspaceWidth => Math.Max(0, ActualWidth - workbenchFrame.Margin.Left - workbenchFrame.Margin.Right - 2);
    private double WorkspaceHeight => Math.Max(0, ActualHeight - workbenchFrame.Margin.Top - workbenchFrame.Margin.Bottom - 2);
    private void ArrangeWorkspace()
    {
        if (disposed || arrangingPanels) return;
        var width = WorkspaceWidth; var height = WorkspaceHeight;
        bool changed = width != arrangedWidth || height != arrangedHeight;
        arrangedWidth = width; arrangedHeight = height;
        panelLayout = PlaygroundPanelLayoutPolicy.Calculate(width, sessionsPreferredOpen, settingsPreferredOpen, panelPriority);
        arrangingPanels = true;
        try
        {
            ConfigureSplitMotion(sessionSplit); ConfigureSplitMotion(settingsSplit);
            // The panes always participate in layout. An opened settings pane
            // must never cover the last lines of a reply or the composer.
            sessionSplit.DisplayMode = settingsSplit.DisplayMode = SplitViewDisplayMode.Inline;
            sessionSplit.OpenPaneLength = panelLayout.SessionsPaneWidth;
            settingsSplit.OpenPaneLength = panelLayout.SettingsPaneWidth;
            sessionSplit.MaxWidth = width;
            settingsSplit.MaxWidth = width - (panelLayout.SessionsOpen ? panelLayout.SessionsPaneWidth : 0);
            reader.MaxWidth = panelLayout.ReaderWidth;
            sessions.MinWidth = Math.Min(190, Math.Max(0, panelLayout.SessionsPaneWidth
                - sessions.Margin.Left - sessions.Margin.Right - sessionsHost.BorderThickness.Left - sessionsHost.BorderThickness.Right));
            settings.MinWidth = Math.Min(220, Math.Max(0, panelLayout.SettingsPaneWidth
                - settingsScroll.Margin.Left - settingsScroll.Margin.Right - settingsScroll.Padding.Left - settingsScroll.Padding.Right
                - settingsHost.BorderThickness.Left - settingsHost.BorderThickness.Right));
            sessionSplit.IsPaneOpen = panelLayout.SessionsOpen;
            settingsSplit.IsPaneOpen = panelLayout.SettingsOpen;
            FinishReducedMotion(sessionSplit); FinishReducedMotion(settingsSplit);
        }
        finally { arrangingPanels = false; }
        SyncPanelHeader(!changed, changed);
        composer.MaxHeight = Math.Clamp(height * .24, 96, 184);
        PlaygroundAppearance.Tip(sessionToggle, sessionSplit.IsPaneOpen ? "收起会话栏" : "展开会话栏");
        PlaygroundAppearance.Tip(settingsToggle, settingsSplit.IsPaneOpen ? "收起运行设置" : "展开运行设置");
        if (changed) QueueThemePaint("layout");
    }
    private void PaneChanged(bool left)
    {
        if (disposed || arrangingPanels) return;
        var split = left ? sessionSplit : settingsSplit;
        if (left) sessionsPreferredOpen = split.IsPaneOpen; else settingsPreferredOpen = split.IsPaneOpen;
        if (split.IsPaneOpen) panelPriority = left ? PlaygroundPanelPriority.Sessions : PlaygroundPanelPriority.Settings;
        ArrangeWorkspace();
    }
    private void ToggleSessions()
        => SetPanelOpen(true, !sessionSplit.IsPaneOpen);
    private void ToggleSettings()
        => SetPanelOpen(false, !settingsSplit.IsPaneOpen);
    private void SetPanelOpen(bool left, bool open)
    {
        if (left)
        {
            sessionsPreferredOpen = open;
            if (open) panelPriority = PlaygroundPanelPriority.Sessions;
        }
        else
        {
            settingsPreferredOpen = open;
            if (open) panelPriority = PlaygroundPanelPriority.Settings;
        }
        ArrangeWorkspace();
    }

    // Test receipts expose only measured geometry, never message text or paths.
    internal object PanelLayoutEvidence
    {
        get
        {
            var readerBounds = PanelBounds(reader);
            var messageBounds = PanelBounds(messages);
            var inputBounds = PanelBounds(inputSurface);
            var rendered = Descendants(messages).OfType<PlaygroundMessageView>().Select(PanelBounds).ToArray();
            return new { priority = panelPriority.ToString(), planned = panelLayout,
                reader = readerBounds, messages = messageBounds, input = inputBounds,
                sessions = PanelBounds(sessionsHost), settings = PanelBounds(settingsHost),
                readerLimit = reader.MaxWidth, settingsLimit = settingsSplit.MaxWidth,
                messageHorizontalScroll = ScrollViewer.GetHorizontalScrollMode(messages).ToString(),
                messageHorizontalScrollBar = ScrollViewer.GetHorizontalScrollBarVisibility(messages).ToString(),
                messagesWithinReader = readerBounds.Contains(messageBounds), inputWithinReader = readerBounds.Contains(inputBounds),
                renderedMessagesWithinReader = rendered.Length == 0 ? (bool?)null : rendered.All(readerBounds.Contains),
                realizedMessageBounds = rendered };
        }
    }
    private readonly record struct PanelRectangle(bool Measured, double Left, double Top, double Width, double Height)
    {
        public double Right => Left + Width;
        public bool Contains(PanelRectangle other) => Measured && other.Measured
            && other.Left >= Left - 1 && other.Right <= Right + 1;
    }
    private PanelRectangle PanelBounds(FrameworkElement element)
    {
        try
        {
            var point = element.TransformToVisual(this).TransformPoint(new(0, 0));
            return new(element.IsLoaded && element.ActualWidth > 0, point.X, point.Y, element.ActualWidth, element.ActualHeight);
        }
        catch { return new(false, 0, 0, 0, 0); }
    }

    private void SyncPanelHeader(bool animate, bool force = false)
    {
        if (!panelHeaderReady) return;
        bool open = sessionSplit.IsPaneOpen, inline = sessionSplit.DisplayMode == SplitViewDisplayMode.Inline;
        bool changed = headerSessionOpen != open || headerSessionInline != inline || headerPaneLength != sessionSplit.OpenPaneLength;
        bool canAnimate = animate && !force && headerSessionOpen is not null && PanelMotionEnabled && IsLoaded;
        settingsTail.Visibility = settingsSplit.IsPaneOpen ? Visibility.Collapsed : Visibility.Visible;
        if (!changed && !force && PanelMotionEnabled) return;
        double lead = inline && open ? 0 : 44;
        double compensatedTitle = titleMotion.X + headerLead - lead;
        titleLead.Width = lead; headerLead = lead;
        // These match this WinUI SplitView template's independent transform
        // transitions. Layout is committed once; there is no per-frame measure.
        double seconds = inline ? open ? .2 : .1 : open ? .35 : .12;
        AnimateHeaderTransform(sessionToggleMotion, open ? Math.Max(0, sessionSplit.OpenPaneLength - 58) : 0, canAnimate, seconds, inline);
        AnimateHeaderTransform(titleMotion, 0, canAnimate && inline, inline && open ? .19999 : seconds, inline, compensatedTitle);
        headerSessionOpen = open; headerSessionInline = inline; headerPaneLength = sessionSplit.OpenPaneLength;
    }
    private void AnimateHeaderTransform(TranslateTransform transform, double target, bool animate, double seconds, bool inline, double? start = null)
    {
        double from = start ?? transform.X;
        if (headerAnimations.Remove(transform, out var previous)) previous.Stop();
        transform.X = target;
        if (!animate || Math.Abs(from - target) < .01) return;
        var frames = new DoubleAnimationUsingKeyFrames();
        frames.KeyFrames.Add(new DiscreteDoubleKeyFrame { KeyTime = KeyTime.FromTimeSpan(TimeSpan.Zero), Value = from });
        frames.KeyFrames.Add(new SplineDoubleKeyFrame { KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromSeconds(seconds)), Value = target,
            KeySpline = new KeySpline { ControlPoint1 = inline ? new(0, .35) : new(.1, .9), ControlPoint2 = inline ? new(.15, 1) : new(.2, 1) } });
        Storyboard.SetTarget(frames, transform); Storyboard.SetTargetProperty(frames, "X");
        var storyboard = new Storyboard(); storyboard.Children.Add(frames); headerAnimations[transform] = storyboard;
        storyboard.Completed += (_, _) => { if (headerAnimations.TryGetValue(transform, out var current) && ReferenceEquals(current, storyboard)) { headerAnimations.Remove(transform); storyboard.Stop(); } };
        storyboard.Begin();
    }
    private object HeaderBox(FrameworkElement element)
    {
        try
        {
            var point = element.TransformToVisual(this).TransformPoint(new(0, 0));
            return new { x = point.X, y = point.Y, width = element.ActualWidth, height = element.ActualHeight };
        }
        catch { return new { unavailable = true }; }
    }

    private void ConfigureSplitMotion(SplitView split)
    {
        split.ApplyTemplate();
        var root = VisualTreeHelper.GetChildrenCount(split) > 0 ? VisualTreeHelper.GetChild(split, 0) as FrameworkElement : null;
        if (root is null) return;
        bool enabled = PanelMotionEnabled;
        if (!splitMotion.TryGetValue(split, out var state) || !ReferenceEquals(state.Root, root))
        {
            var groups = VisualStateManager.GetVisualStateGroups(root).Select(g => (Group: g, Transitions: g.Transitions.ToArray())).ToList();
            state = new(root, groups, true); splitMotion[split] = state;
        }
        if (state.Enabled == enabled) return;
        foreach (var item in state.Groups)
        {
            item.Group.Transitions.Clear();
            if (enabled) foreach (var transition in item.Transitions) item.Group.Transitions.Add(transition);
        }
        splitMotion[split] = state with { Enabled = enabled };
    }
    private void FinishReducedMotion(SplitView split)
    {
        if (PanelMotionEnabled || !splitMotion.TryGetValue(split, out var state)) return;
        foreach (var item in state.Groups)
            try { item.Group.CurrentState?.Storyboard?.SkipToFill(); } catch { /* An idle state has no running storyboard. */ }
    }
    private void DisposePanels()
    {
        sessionSplit.UnregisterPropertyChangedCallback(SplitView.IsPaneOpenProperty, sessionPaneCallback);
        settingsSplit.UnregisterPropertyChangedCallback(SplitView.IsPaneOpenProperty, settingsPaneCallback);
        foreach (var storyboard in headerAnimations.Values) storyboard.Stop(); headerAnimations.Clear();
        splitMotion.Clear();
    }
}
