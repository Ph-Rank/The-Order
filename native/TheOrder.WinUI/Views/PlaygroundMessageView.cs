using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using TheOrder.Application.Playground;
using TheOrder.Presentation;

namespace TheOrder.WinUI.Views;

internal sealed class PlaygroundMessageItem(PlaygroundMessage message, bool strict, string? browserDirectory,
    Func<PlaygroundSource, Task> openSource, Func<Guid, Task> retry) : INotifyPropertyChanged
{
    public PlaygroundMessage Message { get; private set; } = message;
    public bool Strict { get; private set; } = strict;
    public string? BrowserDirectory { get; } = browserDirectory;
    public Func<PlaygroundSource, Task> OpenSource { get; } = openSource;
    public Func<Guid, Task> Retry { get; } = retry;
    public event PropertyChangedEventHandler? PropertyChanged;
    public void Update(PlaygroundMessage value, bool strictMode)
    {
        if (Message == value && Strict == strictMode) return;
        Message = value; Strict = strictMode; PropertyChanged?.Invoke(this, new(nameof(Message)));
    }
}

// Public for the native DataTemplate; the item itself is an internal UI model.
// Only the active message changes during streaming. A completed message is
// parsed once, with no image registry and no browser-powered Markdown.
public sealed class PlaygroundMessageView : UserControl, IDisposable
{
    private readonly StackPanel root = new() { Spacing = 10, MaxWidth = 860, HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 16, 0, 22) };
    private readonly TextBlock heading = new() { FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, MaxLines = 1 };
    private readonly StackPanel body = new() { Spacing = 12 };
    private readonly StackPanel messageContent = new() { Spacing = 8 };
    private readonly TagFlowPanel fileLinks = new() { HorizontalSpacing = 6, VerticalSpacing = 6, Visibility = Visibility.Collapsed };
    private readonly Border contentSurface = new();
    private readonly Grid messageLine = new() { ColumnSpacing = 8 };
    private readonly Brush transparent = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
    private readonly StackPanel stream = new() { Spacing = 12 };
    private readonly TextBlock tail = new() { FontSize = 15.5, LineHeight = 27, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
    private readonly TextBlock phase = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock usage = new() { FontSize = 11, TextWrapping = TextWrapping.Wrap };
    private readonly Button copy = new() { Content = "复制", FontSize = 12, Padding = new Thickness(8, 4, 8, 4) };
    private readonly Button retry = new() { Content = "重试", FontSize = 12, Padding = new Thickness(8, 4, 8, 4), Visibility = Visibility.Collapsed };
    private readonly StackPanel sources = new() { HorizontalAlignment = HorizontalAlignment.Stretch, Visibility = Visibility.Collapsed, Spacing = 10, Margin = new Thickness(0, 2, 0, 6) };
    private readonly Button sourceToggle = new() { FontSize = 12, Padding = new Thickness(7, 3, 7, 3), Visibility = Visibility.Collapsed };
    private readonly StackPanel actions = new() { Orientation = Orientation.Horizontal, Spacing = 2 };
    private PlaygroundMessageItem? item;
    private PlaygroundMessage? previous;
    private PlaygroundCitations citations = new([]);
    private PlaygroundSearchSuggestionsView? suggestions;
    private string streamed = "";
    private int committed;
    private bool wasStrict, sourcesOpen;
    private bool? userLayout;
    private bool themePaintQueued;
    internal int StaticRenders { get; private set; }
    internal int TailUpdates { get; private set; }
    internal object Evidence => new { message = item?.Message.Id, StaticRenders, TailUpdates,
        suggestions = suggestions?.State, externalResourcesBlocked = suggestions?.BlockedRequests ?? 0,
        citationLinks = body.Children.OfType<PlaygroundMarkdownView>().Concat(stream.Children.OfType<PlaygroundMarkdownView>()).Sum(x => x.RenderedCitations),
        citationNumbers = citations.Entries.Where(x => x.Number is not null).Select(x => new { id = x.Source.Id, number = x.Number }).ToArray(),
        bodyWidth = body.ActualWidth, bodyHeight = body.ActualHeight, strict = item?.Strict == true,
        actualTheme = ActualTheme.ToString(), paletteTheme = PlaygroundAppearance.ThemeFor(this).ToString(),
        foregroundColor = PlaygroundAppearance.ColorOf(Foreground), bubbleColor = PlaygroundAppearance.ColorOf(contentSurface.Background) };
    public PlaygroundMessageView()
    {
        IsTabStop = false; HorizontalContentAlignment = HorizontalAlignment.Stretch; Content = root;
        messageContent.Children.Add(fileLinks); messageContent.Children.Add(body); contentSurface.Child = messageContent;
        messageLine.ColumnDefinitions.Add(new()); messageLine.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); messageLine.Children.Add(contentSurface);
        root.Children.Add(heading); root.Children.Add(messageLine); root.Children.Add(phase);
        copy.Content = PlaygroundAppearance.Icon("\uE8C8", 14); copy.Width = copy.Height = copy.MinWidth = copy.MinHeight = 28; copy.Padding = new Thickness(0);
        retry.Content = PlaygroundAppearance.IconLabel("\uE72C", "重试", 12);
        PlaygroundAppearance.Apply(copy, "PGIconButton"); PlaygroundAppearance.Apply(retry); PlaygroundAppearance.Apply(sourceToggle);
        actions.Children.Add(copy); actions.Children.Add(retry); actions.Children.Add(sourceToggle); root.Children.Add(actions); root.Children.Add(usage); root.Children.Add(sources);
        sourceToggle.Click += (_, _) => { sourcesOpen = !sourcesOpen; sources.Visibility = sourcesOpen ? Visibility.Visible : Visibility.Collapsed; UpdateSourceToggle(); };
        AutomationProperties.SetName(copy, "复制这条消息"); AutomationProperties.SetName(retry, "手动重试这轮回复");
        copy.Click += (_, _) => { if (item is not null) PlaygroundMarkdownView.Copy(item.Message.Text); };
        retry.Click += async (_, _) => { if (item is not null) { try { await item.Retry(item.Message.ConversationId); } catch { } } };
        DataContextChanged += (_, _) => Bind(DataContext as PlaygroundMessageItem);
        Loaded += (_, _) => { if (item is null) Bind(DataContext as PlaygroundMessageItem); QueuePaint(); };
        ActualThemeChanged += (_, _) => QueuePaint();
        Unloaded += (_, _) => { if (item is not null) item.PropertyChanged -= ItemChanged; item = null; previous = null; ClearSuggestions(); };
    }
    private void Bind(PlaygroundMessageItem? value)
    {
        if (item is not null) item.PropertyChanged -= ItemChanged;
        item = value; previous = null; streamed = ""; committed = 0; sourcesOpen = false; sources.Visibility = Visibility.Collapsed; body.Children.Clear(); stream.Children.Clear();
        ClearSuggestions();
        if (item is not null) { item.PropertyChanged += ItemChanged; Update(); }
    }
    private void ItemChanged(object? sender, PropertyChangedEventArgs e) => Update();
    private void Update()
    {
        if (item is null) return;
        var message = item.Message;
        bool generating = message.Status == PlaygroundMessageStatus.Generating;
        if (previous == message && wasStrict == item.Strict) return;
        bool changedMessage = previous?.Id != message.Id;
        bool strictChanged = wasStrict != item.Strict;
        bool sourcesChanged = previous?.Sources != message.Sources;
        if (changedMessage || sourcesChanged) citations = new(message.Sources);
        if (generating && (changedMessage || sourcesChanged || strictChanged))
        { body.Children.Clear(); stream.Children.Clear(); streamed = ""; committed = 0; }
        bool user = message.Role == "user";
        SetRoleLayout(user);
        if (changedMessage || previous?.Attachments != message.Attachments)
        {
            fileLinks.Children.Clear(); fileLinks.Visibility = message.Attachments.IsDefaultOrEmpty ? Visibility.Collapsed : Visibility.Visible;
            foreach (var file in message.Attachments)
            {
                var preview = PlaygroundView.AttachmentPreviewButton(file);
                preview.Click += async (_, _) => { try { await PlaygroundAttachmentPreview.ShowAsync(XamlRoot, PlaygroundAppearance.ThemeFor(this), file); } catch { } };
                fileLinks.Children.Add(preview);
            }
        }
        bool showBody = message.Text.Length > 0 || generating && !item.Strict;
        body.Visibility = showBody ? Visibility.Visible : Visibility.Collapsed;
        contentSurface.Visibility = showBody || !message.Attachments.IsDefaultOrEmpty ? Visibility.Visible : Visibility.Collapsed;
        heading.HorizontalAlignment = actions.HorizontalAlignment = user ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        contentSurface.HorizontalAlignment = user ? HorizontalAlignment.Right : HorizontalAlignment.Stretch;
        contentSurface.MaxWidth = user ? 720 : double.PositiveInfinity; contentSurface.CornerRadius = new CornerRadius(user ? 16 : 0);
        contentSurface.Padding = user ? new Thickness(16, 10, 16, 10) : new Thickness(0); Paint();
        string time = message.CreatedAt.ToLocalTime().ToString("HH:mm");
        heading.Text = user ? "你  ·  " + time : string.IsNullOrWhiteSpace(message.Model) ? time : message.Model + "  ·  " + time;
        ToolTipService.SetToolTip(heading, heading.Text);
        ToolTipService.SetToolTip(contentSurface, user ? "你的消息 · " + message.CreatedAt.ToLocalTime().ToString("MM-dd HH:mm") : null);
        AutomationProperties.SetName(this, (user ? "用户消息" : "助手回复") + "，" + StatusLabel(message.Status) + (user ? "，" + message.CreatedAt.ToLocalTime().ToString("HH:mm") : ""));
        phase.Text = generating
            ? item.Strict ? "正在准备回复…" : message.Activities.LastOrDefault() ?? "正在回复…"
            : message.Status == PlaygroundMessageStatus.Complete
                ? CompletionNotice(message)
                : StatusLabel(message.Status) + (string.IsNullOrWhiteSpace(message.Error) ? "；已接收内容未作为完整回复。" : " · " + message.Error);
        phase.Visibility = string.IsNullOrWhiteSpace(phase.Text) ? Visibility.Collapsed : Visibility.Visible;
        retry.Visibility = message.Role != "user" && message.Status is PlaygroundMessageStatus.Failed or PlaygroundMessageStatus.Cancelled or PlaygroundMessageStatus.Interrupted
            ? Visibility.Visible : Visibility.Collapsed;
        copy.IsEnabled = message.Text.Length > 0 && !(generating && item.Strict);
        if (generating && item.Strict)
        {
            if (changedMessage || strictChanged || body.Children.Count != 0) { body.Children.Clear(); stream.Children.Clear(); streamed = ""; committed = 0; }
        }
        else if (generating)
        {
            if (body.Children.Count == 0) { stream.Children.Add(tail); body.Children.Add(stream); }
            Stream(message.Text);
        }
        else if (changedMessage || previous?.Status == PlaygroundMessageStatus.Generating || previous?.Text != message.Text || strictChanged || sourcesChanged)
        {
            body.Children.Clear(); stream.Children.Clear(); streamed = ""; committed = 0;
            citations = new(message.Sources);
            if (message.Text.Length > 0) body.Children.Add(Markdown(message.Text));
            StaticRenders++;
        }
        if (!generating && !string.IsNullOrWhiteSpace(message.SearchSuggestionsHtml) && message.Retention == PlaygroundRetention.SessionOnly)
        {
            if (suggestions is null || previous?.SearchSuggestionsHtml != message.SearchSuggestionsHtml)
            {
                if (suggestions is not null) { root.Children.Remove(suggestions); suggestions.Dispose(); }
                suggestions = new PlaygroundSearchSuggestionsView(message.SearchSuggestionsHtml, item.BrowserDirectory, OpenSuggestionAsync);
                root.Children.Insert(Math.Min(2, root.Children.Count), suggestions);
            }
        }
        else if (suggestions is not null) { root.Children.Remove(suggestions); suggestions.Dispose(); suggestions = null; }
        if (message.Usage is null && message.Cost is null) { usage.Text = ""; usage.Visibility = Visibility.Collapsed; ToolTipService.SetToolTip(usage, null); }
        else
        {
            var cost = PlaygroundPricing.ForMessage(message);
            string tokens = Usage(message.Usage);
            usage.Text = (tokens.Length == 0 ? "" : tokens + " · ") + PlaygroundPricing.Format(cost);
            usage.Visibility = Visibility.Visible; ToolTipService.SetToolTip(usage, cost.Notice);
            AutomationProperties.SetName(usage, usage.Text);
        }
        if (user || changedMessage || sourcesChanged || previous?.Status != message.Status || previous?.Text != message.Text)
        {
            if (user)
            { sourcesOpen = false; sources.Children.Clear(); sources.Visibility = sourceToggle.Visibility = Visibility.Collapsed; }
            else if (message.Sources.IsEmpty || generating) { sources.Visibility = sourceToggle.Visibility = Visibility.Collapsed; }
            else
            {
                sourceToggle.Visibility = Visibility.Visible; UpdateSourceToggle();
                sources.Visibility = sourcesOpen ? Visibility.Visible : Visibility.Collapsed;
                var list = new StackPanel { Spacing = 10 };
                foreach (var group in citations.Entries.GroupBy(x => x.Source.Kind))
                {
                    list.Children.Add(new TextBlock { Text = SourceKind(group.Key), FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
                    foreach (var entry in group)
                    {
                        var source = entry.Source;
                        var row = new StackPanel { Spacing = 2 };
                        var link = new Button { Content = new TextBlock { Text = (entry.Number is { } number ? number + ". " : "") + source.Title, TextWrapping = TextWrapping.Wrap, MaxWidth = 740 },
                            HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(0) };
                        PlaygroundAppearance.Apply(link);
                        link.Click += async (_, _) => { try { if (item is not null) await item.OpenSource(source); } catch { } };
                        AutomationProperties.SetName(link, "打开来源：" + source.Title); row.Children.Add(link);
                        if (source.At is { } at) row.Children.Add(new TextBlock { Text = at.ToLocalTime().ToString("yyyy-MM-dd HH:mm"), FontSize = 12 });
                        if (!string.IsNullOrWhiteSpace(source.Quote)) row.Children.Add(new TextBlock { Text = source.Quote, FontSize = 13, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true });
                        list.Children.Add(row);
                    }
                }
                sources.Children.Clear(); sources.Children.Add(list);
            }
        }
        wasStrict = item.Strict; previous = message;
    }
    private void SetRoleLayout(bool user)
    {
        if (userLayout == user) return; userLayout = user;
        heading.Visibility = user ? Visibility.Collapsed : Visibility.Visible;
        root.Margin = user ? new Thickness(0, 8, 0, 16) : new Thickness(0, 16, 0, 22);
        root.Spacing = user ? 8 : 10;
        if (user)
        {
            root.Children.Remove(actions); Grid.SetColumn(actions, 1); messageLine.Children.Add(actions);
            actions.VerticalAlignment = VerticalAlignment.Center;
        }
        else
        {
            messageLine.Children.Remove(actions); root.Children.Remove(actions); Grid.SetColumn(actions, 0);
            int phaseAt = root.Children.IndexOf(phase); root.Children.Insert(Math.Min(phaseAt + 1, root.Children.Count), actions);
            actions.VerticalAlignment = VerticalAlignment.Top;
        }
    }
    private void Paint()
    {
        Foreground = PlaygroundAppearance.Brush(this, "PGTextBrush");
        heading.Foreground = phase.Foreground = usage.Foreground = PlaygroundAppearance.Brush(this, "PGSecondaryBrush");
        contentSurface.Background = item?.Message.Role == "user" ? PlaygroundAppearance.Brush(this, "PGUserBubbleBrush") : transparent;
    }
    private void QueuePaint()
    {
        if (themePaintQueued) return; themePaintQueued = true;
        DispatcherQueue.TryEnqueue(() => { themePaintQueued = false; if (IsLoaded) Paint(); });
    }
    private void UpdateSourceToggle()
    {
        sourceToggle.Content = PlaygroundAppearance.IconLabel("\uE71B", (sourcesOpen ? "收起来源 " : "信息来源 ") + (item?.Message.Sources.Length ?? 0), 12);
        AutomationProperties.SetName(sourceToggle, sourcesOpen ? "收起本次信息来源" : "展开本次信息来源");
    }
    internal void RefreshTheme() => Paint();
    private void Stream(string value)
    {
        if (value == streamed) return;
        if (!value.StartsWith(streamed, StringComparison.Ordinal)) { stream.Children.Clear(); stream.Children.Add(tail); committed = 0; citations = new(item?.Message.Sources ?? []); }
        int boundary = SafeBoundary(value, committed);
        if (boundary > committed)
        {
            stream.Children.Insert(Math.Max(0, stream.Children.Count - 1), Markdown(value[committed..boundary])); committed = boundary;
        }
        tail.Text = value[committed..]; streamed = value; TailUpdates++;
    }
    private PlaygroundMarkdownView Markdown(string text) => new(text, OpenLinkAsync, citations, item?.OpenSource);
    // Commit complete paragraphs only outside a fenced block. The unfinished
    // tail stays plain selectable text; it is never reparsed per token.
    private static int SafeBoundary(string value, int start)
    {
        bool fenced = false; char delimiter = '\0'; int boundary = start;
        for (int line = start; line < value.Length;)
        {
            int end = value.IndexOf('\n', line); if (end < 0) break;
            var text = value.AsSpan(line, end - line).Trim();
            if (text.StartsWith("```", StringComparison.Ordinal) || text.StartsWith("~~~", StringComparison.Ordinal))
            { if (!fenced) { fenced = true; delimiter = text[0]; } else if (text[0] == delimiter) fenced = false; }
            if (!fenced && text.Length == 0) boundary = end + 1;
            line = end + 1;
        }
        return boundary;
    }
    private Task OpenLinkAsync(string url)
    {
        if (item is null || !PlaygroundMarkdownView.Allowed(url)) return Task.CompletedTask;
        var source = item.Message.Sources.FirstOrDefault(s => s.Kind == PlaygroundSourceKind.Web && s.Url == url);
        // Arbitrary model-authored URLs are not promoted to verified citations.
        return source is null ? Task.CompletedTask : item.OpenSource(source);
    }
    private Task OpenSuggestionAsync(string url) => item is null || !PlaygroundMarkdownView.Allowed(url) ? Task.CompletedTask
        : item.OpenSource(new("google-search-suggestion", PlaygroundSourceKind.Web, "Google 搜索建议") { Url = url, Retention = PlaygroundRetention.SessionOnly });
    private static string Usage(PlaygroundUsage? usage) => usage is null ? "" : "输入 " + Number(usage.InputTokens) + " · 输出 " + Number(usage.OutputTokens)
        + (usage.Searches is { } searches ? " · 搜索 " + searches : "") + (usage.Fetches is { } fetches ? " · 读取网页 " + fetches : "");
    private static string Number(long? value) => value is { } number ? number.ToString("N0") : "未知";
    private static string CompletionNotice(PlaygroundMessage message)
    {
        // These are host-authored coverage notices, not model reasoning or
        // ordinary progress. Keep them visible after a successful reply.
        var notices = message.Activities.Where(x => x is "较早图片本轮未再次读取。" or "较早图片本轮未再次读取"
            or "本轮优先讨论手动附件，未执行独立 Google 公开检索。").Distinct().ToList();
        if (message.Retention == PlaygroundRetention.SessionOnly) notices.Insert(0, "公开检索原始答复 · 仅本次会话显示，不进入长期记忆。");
        return string.Join("\n", notices);
    }
    internal static string StatusLabel(PlaygroundMessageStatus value) => value switch
    { PlaygroundMessageStatus.Generating => "生成中", PlaygroundMessageStatus.Complete => "完成", PlaygroundMessageStatus.Cancelled => "已停止", PlaygroundMessageStatus.Interrupted => "已中断", _ => "未完成" };
    private static string SourceKind(PlaygroundSourceKind value) => value switch
    { PlaygroundSourceKind.LocalRecord => "本地记录", PlaygroundSourceKind.ChatMessage => "历史聊天", PlaygroundSourceKind.Memory => "偏好记忆", PlaygroundSourceKind.Attachment => "附件快照", _ => "网页来源" };
    private void ClearSuggestions() { if (suggestions is null) return; root.Children.Remove(suggestions); suggestions.Dispose(); suggestions = null; }
    public void Dispose() { if (item is not null) item.PropertyChanged -= ItemChanged; item = null; previous = null; ClearSuggestions(); }
}
