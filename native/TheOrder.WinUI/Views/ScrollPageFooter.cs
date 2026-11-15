using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using TheOrder.Application;

namespace TheOrder.WinUI.Views;

// A tail inside the list, rather than a bottom event on the entire page. The
// same tail works in nested detail sections and in virtualized list footers.
public sealed class ScrollPageFooter : Grid
{
    private readonly LoadingMark progress = new() { Width = 16, Height = 16, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly Button retry = new() { HorizontalAlignment = HorizontalAlignment.Center };
    private readonly List<ScrollViewer> scrollers = [];
    private object? page, requested;
    private bool busy, requesting, queued;
    public Func<Task>? LoadNext { get; set; }

    public ScrollPageFooter()
    {
        MinHeight = 24;
        Visibility = Visibility.Collapsed;
        retry.Content = UiText.Get("重试此页");
        AutomationProperties.SetName(progress, UiText.Get("正在读取…"));
        Children.Add(progress);
        Children.Add(retry);
        retry.Click += (_, _) => StartNext();
        Loaded += (_, _) =>
        {
            FindScrollers();
            LayoutUpdated += OnLayoutUpdated;
            QueueCheck();
        };
        Unloaded += (_, _) =>
        {
            LayoutUpdated -= OnLayoutUpdated;
            UnhookScrollers();
        };
    }

    public void Update(object? next, bool loading = false, bool retryRequired = false)
    {
        if (!Equals(page, next)) requested = null;
        page = next;
        if (retryRequired) requested = page;
        busy = loading;
        UpdateVisual();
        QueueCheck();
    }

    private void UpdateVisual()
    {
        Visibility = page is null ? Visibility.Collapsed : Visibility.Visible;
        var loading = page is not null && (busy || requesting && Equals(requested, page));
        progress.Visibility = loading ? Visibility.Visible : Visibility.Collapsed;
        progress.IsActive = loading;
        retry.Visibility = page is not null && !loading && Equals(page, requested) ? Visibility.Visible : Visibility.Collapsed;
    }

    private void FindScrollers()
    {
        UnhookScrollers();
        for (var node = VisualTreeHelper.GetParent(this); node is not null; node = VisualTreeHelper.GetParent(node))
            if (node is ScrollViewer scroll)
            {
                scrollers.Add(scroll);
                scroll.ViewChanged += OnViewChanged;
            }
    }

    private void UnhookScrollers()
    {
        foreach (var scroll in scrollers) scroll.ViewChanged -= OnViewChanged;
        scrollers.Clear();
    }

    private void OnViewChanged(object? sender, ScrollViewerViewChangedEventArgs e) => QueueCheck();
    private void OnLayoutUpdated(object? sender, object e) => QueueCheck();

    private void QueueCheck()
    {
        if (!IsLoaded || queued || busy || requesting || page is null || Equals(requested, page) || LoadNext is null) return;
        queued = DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            queued = false;
            try
            {
                if (IsLoaded && !busy && !requesting && page is not null && !Equals(requested, page) && AtTail())
                    StartNext();
            }
            catch (Exception error) when (error is InvalidOperationException or ArgumentException or System.Runtime.InteropServices.COMException)
            {
                // A virtualized footer can be detached between measuring and the queued check.
                UnhookScrollers();
                App.TraceVisualFailure(error);
            }
        });
    }

    private bool AtTail()
    {
        if (scrollers.Count == 0) FindScrollers();
        if (scrollers.Count == 0 || ActualHeight <= 0) return false;
        for (DependencyObject? node = this; node is not null; node = VisualTreeHelper.GetParent(node))
            if (node is UIElement { Visibility: Visibility.Collapsed } or Control { IsEnabled: false }) return false;

        foreach (var scroll in scrollers)
        {
            if (scroll.ViewportHeight <= 0) return false;
            var top = TransformToVisual(scroll).TransformPoint(new(0, 0)).Y;
            if (top + ActualHeight < 0 || top > scroll.ViewportHeight + 12) return false;
        }
        return true;
    }

    private async void StartNext()
    {
        if (busy || requesting || page is null || LoadNext is null) return;
        requested = page;
        requesting = true;
        UpdateVisual();
        try { await LoadNext(); }
        catch (Exception error) { App.TraceVisualFailure(error); }
        finally
        {
            requesting = false;
            UpdateVisual();
            QueueCheck();
        }
    }
}
