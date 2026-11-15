using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace TheOrder.WinUI.Views;

internal sealed record StatusAction(string Label, Func<Task> Invoke, string? AutomationId = null);
internal sealed record StatusNotice(string Title, string Message, InfoBarSeverity Severity = InfoBarSeverity.Informational,
    StatusAction? Action = null, StatusAction? SecondaryAction = null, bool RequiresAction = false)
{
    public bool NeedsAttention => Severity == InfoBarSeverity.Error || RequiresAction;
}

public sealed partial class StatusPage : Page
{
    private const int HistoryLimit = 60;
    private readonly List<MessageRow> history = [];
    private readonly Dictionary<string, MessageRow> active = [];
    private readonly Dictionary<string, TaskRow> running = [];
    private bool viewing;
    internal bool HasUnreadAttention { get; private set; }
    internal event Action? UnreadAttentionChanged;

    public StatusPage() => InitializeComponent();

    internal void SetViewing(bool value)
    {
        if (viewing == value) return;
        viewing = value;
        if (!viewing) return;
        foreach (var row in history) row.Unread = false;
        foreach (var row in active.Values) row.Unread = false;
        RefreshMessages();
    }

    internal void Post(StatusNotice notice)
    {
        if (string.IsNullOrWhiteSpace(notice.Message) || history.Count > 0 && history[0].Unread && history[0].Notice == notice) return;
        var row = AddNotice(notice);
        history.Insert(0, row);
        if (history.Count > HistoryLimit)
        {
            Messages.Children.Remove(history[^1].View);
            history.RemoveAt(history.Count - 1);
        }
        RefreshMessages();
    }

    internal bool HasNotice(string key) => active.ContainsKey(key);
    internal string? NoticeTitle(string key) => active.GetValueOrDefault(key)?.Notice.Title;
    internal bool HasTask(string key) => running.ContainsKey(key);

    internal void SetNotice(string key, StatusNotice? notice)
    {
        if (active.TryGetValue(key, out var previous))
        {
            if (previous.Notice == notice) return;
            active.Remove(key);
            Messages.Children.Remove(previous.View);
        }
        else if (notice is null) return;

        if (notice is not null) active.Add(key, AddNotice(notice));
        RefreshMessages();
    }

    internal void ClearHistory()
    {
        foreach (var row in history) Messages.Children.Remove(row.View);
        history.Clear();
        RefreshMessages();
    }

    internal void SetTask(string key, string title, string detail, double? percent = null, StatusAction? cancel = null)
    {
        if (!running.TryGetValue(key, out var row))
        {
            row = new TaskRow(title);
            running.Add(key, row);
            Tasks.Children.Add(row.View);
            EmptyTasks.Visibility = Visibility.Collapsed;
        }
        row.Update(detail, percent, cancel, CreateAction);
    }

    internal void ClearTask(string key)
    {
        if (!running.Remove(key, out var row)) return;
        Tasks.Children.Remove(row.View);
        EmptyTasks.Visibility = running.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private MessageRow AddNotice(StatusNotice notice)
    {
        var bar = new InfoBar { IsOpen = true, IsClosable = false, Title = notice.Title, Message = notice.Message, Severity = notice.Severity };
        if (notice.Action is { } primary)
        {
            var actions = new TagFlowPanel { HorizontalSpacing = 8, VerticalSpacing = 6 };
            actions.Children.Add(CreateAction(primary));
            if (notice.SecondaryAction is { } secondary) actions.Children.Add(CreateAction(secondary));
            bar.Content = actions;
        }
        Messages.Children.Insert(0, bar);
        return new(notice, bar, !viewing);
    }

    private void RefreshMessages()
    {
        EmptyMessages.Visibility = Messages.Children.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ClearMessages.Visibility = history.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        bool unread = history.Any(IsUnreadAttention) || active.Values.Any(IsUnreadAttention);
        if (HasUnreadAttention == unread) return;
        HasUnreadAttention = unread;
        UnreadAttentionChanged?.Invoke();

        static bool IsUnreadAttention(MessageRow row) => row.Unread && row.Notice.NeedsAttention;
    }

    private Button CreateAction(StatusAction action)
    {
        var button = new Button { Content = action.Label };
        if (action.AutomationId is { } id) AutomationProperties.SetAutomationId(button, id);
        button.Click += async (_, _) =>
        {
            button.IsEnabled = false;
            try { await action.Invoke(); }
            catch (OperationCanceledException) { }
            catch { Post(new("操作未完成", "请重试。", InfoBarSeverity.Error)); }
            finally { button.IsEnabled = true; }
        };
        return button;
    }

    private void OnClearMessages(object sender, RoutedEventArgs e) => ClearHistory();

    private sealed class MessageRow(StatusNotice notice, InfoBar view, bool unread)
    {
        public StatusNotice Notice { get; } = notice;
        public InfoBar View { get; } = view;
        public bool Unread { get; set; } = unread;
    }

    private sealed class TaskRow
    {
        private readonly TextBlock detail = new() { TextWrapping = TextWrapping.Wrap };
        private readonly ProgressBar progress = new() { Maximum = 100, Height = 4 };
        private readonly ContentControl action = new() { Visibility = Visibility.Collapsed, IsTabStop = false };
        private StatusAction? currentAction;
        public StackPanel View { get; } = new() { Spacing = 8 };

        public TaskRow(string title)
        {
            var heading = new TextBlock { Text = title, TextWrapping = TextWrapping.Wrap, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
            detail.Style = (Style)Microsoft.UI.Xaml.Application.Current.Resources["SecondaryTextStyle"];
            AutomationProperties.SetName(progress, title);
            View.Children.Add(heading);
            View.Children.Add(detail);
            View.Children.Add(progress);
            View.Children.Add(action);
        }

        public void Update(string text, double? percent, StatusAction? cancel, Func<StatusAction, Button> createAction)
        {
            if (detail.Text != text) detail.Text = text;
            detail.Visibility = text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
            progress.IsIndeterminate = percent is null;
            if (percent is { } value) progress.Value = Math.Clamp(value, 0, 100);
            if (currentAction == cancel) return;
            currentAction = cancel;
            action.Content = cancel is null ? null : createAction(cancel);
            action.Visibility = cancel is null ? Visibility.Collapsed : Visibility.Visible;
        }
    }
}
