using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

namespace TheOrder.WinUI.Views;

// Each task keeps its view and scroll position; the dialog itself never scrolls.
public sealed class GameToolsView : Grid
{
    private readonly TagFlowPanel navigation = new() { HorizontalSpacing = 6, VerticalSpacing = 6 };
    private readonly Grid pages = new() { Margin = new Thickness(0, 16, 0, 0) };
    private readonly List<(string Key, string Label, FrameworkElement View, ToggleButton Button)> entries = [];
    private string? selected;
    public string? SelectedKey => selected;
    public event Action<string>? PageSelected;
    public GameToolsView()
    {
        AutomationProperties.SetAutomationId(this, "GameToolsWorkspace");
        RowDefinitions.Add(new() { Height = GridLength.Auto }); RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) });
        Children.Add(new Border { Child = navigation, Padding = new Thickness(0, 0, 0, 12),
            BorderThickness = new Thickness(0, 0, 0, 1), BorderBrush = (Brush)Microsoft.UI.Xaml.Application.Current.Resources["DividerBrush"] });
        SetRow(pages, 1); Children.Add(pages);
    }
    public void AddPage(string key, string label, FrameworkElement content, bool scroll = true)
    {
        FrameworkElement view = scroll ? new ScrollViewer { Content = content, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalContentAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(0, 0, 12, 16) } : content;
        if (view is ScrollViewer scrolling)
        { content.HorizontalAlignment = HorizontalAlignment.Left; scrolling.SizeChanged += (_, e) => content.Width = Math.Min(760, Math.Max(0, e.NewSize.Width - 12)); }
        view.Visibility = Visibility.Collapsed;
        var button = new ToggleButton { Content = label, FontSize = 14, Padding = new Thickness(12, 7, 12, 7), MinHeight = 34 };
        AutomationProperties.SetAutomationId(button, "GameToolsTab-" + key);
        button.Click += (_, _) => Select(key);
        navigation.Children.Add(button);
        entries.Add((key, label, view, button)); pages.Children.Add(view);
        if (selected is null) Select(key);
    }
    public void Select(string key)
    {
        var index = entries.FindIndex(e => e.Key == key); if (index < 0) throw new ArgumentOutOfRangeException(nameof(key));
        if (selected == key) { if (entries[index].Button is { } current) current.IsChecked = true; return; }
        selected = key;
        foreach (var entry in entries)
        {
            entry.View.Visibility = entry.Key == key ? Visibility.Visible : Visibility.Collapsed;
            if (entry.Button is { } button) button.IsChecked = entry.Key == key;
        }
        InteractionMotion.Reveal(entries[index].View);
        PageSelected?.Invoke(key);
    }
    internal void SelectIndex(int index) { if (index < 0 || index >= entries.Count) throw new ArgumentOutOfRangeException(nameof(index)); Select(entries[index].Key); }
    internal object Evidence => new { selected, width = ActualWidth, height = ActualHeight, navigationHeight = navigation.ActualHeight,
        contentHeight = pages.ActualHeight, directTabs = entries.Select(e => new { key=e.Key, visible=e.Button.Visibility == Visibility.Visible,
            width=e.Button.ActualWidth, height=e.Button.ActualHeight, selected=e.Button.IsChecked }).ToArray(),
        pages = entries.Select(e => new { key=e.Key, visible=e.View.Visibility == Visibility.Visible, width=e.View.ActualWidth, height=e.View.ActualHeight }).ToArray() };
}
