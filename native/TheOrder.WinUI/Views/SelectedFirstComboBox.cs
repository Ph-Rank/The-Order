using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Markup;
using Windows.System;
using Windows.UI.Core;

namespace TheOrder.WinUI.Views;

[ContentProperty(Name = nameof(Options))]
public sealed class SelectedFirstComboBox : ContentControl
{
    public static readonly DependencyProperty OptionsSourceProperty = DependencyProperty.Register(
        nameof(OptionsSource), typeof(object), typeof(SelectedFirstComboBox),
        new PropertyMetadata(null, (target, _) => ((SelectedFirstComboBox)target).ChangeSource()));
    public static readonly DependencyProperty OptionIndexProperty = DependencyProperty.Register(
        nameof(OptionIndex), typeof(int), typeof(SelectedFirstComboBox),
        new PropertyMetadata(-1, (target, _) => ((SelectedFirstComboBox)target).ChangeSelection()));
    public static readonly DependencyProperty HeaderProperty = DependencyProperty.Register(
        nameof(Header), typeof(object), typeof(SelectedFirstComboBox),
        new PropertyMetadata(null, (target, _) => ((SelectedFirstComboBox)target).UpdateHeader()));
    public static readonly DependencyProperty PlaceholderTextProperty = DependencyProperty.Register(
        nameof(PlaceholderText), typeof(string), typeof(SelectedFirstComboBox),
        new PropertyMetadata("", (target, _) => ((SelectedFirstComboBox)target).UpdateCaption()));
    public static readonly DependencyProperty DisplayMemberPathProperty = DependencyProperty.Register(
        nameof(DisplayMemberPath), typeof(string), typeof(SelectedFirstComboBox),
        new PropertyMetadata("", (target, _) => ((SelectedFirstComboBox)target).UpdateDisplayLabels()));

    public ObservableCollection<object> Options { get; } = [];
    public object? OptionsSource { get => GetValue(OptionsSourceProperty); set => SetValue(OptionsSourceProperty, value); }
    public int OptionIndex { get => (int)GetValue(OptionIndexProperty); set => SetValue(OptionIndexProperty, value); }
    public object? Header { get => GetValue(HeaderProperty); set => SetValue(HeaderProperty, value); }
    public string PlaceholderText { get => (string)GetValue(PlaceholderTextProperty); set => SetValue(PlaceholderTextProperty, value); }
    public string DisplayMemberPath { get => (string)GetValue(DisplayMemberPathProperty); set => SetValue(DisplayMemberPathProperty, value); }
    public bool IsDropDownOpen => flyout.IsOpen;
    public object? SelectedOption
    {
        get => (uint)OptionIndex < (uint)master.Count ? master[OptionIndex] : null;
        set => OptionIndex = value is null ? -1 : master.IndexOf(value);
    }

    public event SelectionChangedEventHandler? OptionChanged;
    public event EventHandler? DropDownOpened;
    internal IReadOnlyList<object> PrimaryOptions => master;
    internal DropDownButton Trigger { get; }

    private readonly ContentPresenter header = new() { Margin = new(0, 0, 0, 6), Visibility = Visibility.Collapsed };
    private readonly TextBlock caption = new() { TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly ListView list;
    private readonly Flyout flyout;
    private readonly ObservableCollection<DropDownOption> rows = [];
    private List<object> master = [];
    private INotifyCollectionChanged? observedSource;
    private object? lastOption;
    private int lastIndex = -1;
    private bool synchronizing;

    public SelectedFirstComboBox()
    {
        DefaultStyleKey = typeof(ContentControl);
        HorizontalAlignment = HorizontalAlignment.Left;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        IsTabStop = false;
        AutomationProperties.SetAccessibilityView(this, AccessibilityView.Raw);

        var resources = Microsoft.UI.Xaml.Application.Current.Resources;
        Trigger = new DropDownButton
        {
            Content = caption, HorizontalAlignment = HorizontalAlignment.Stretch,
            Style = (Style)resources["OrderDropDownTriggerStyle"]
        };
        list = new ListView
        {
            ItemsSource = rows, IsItemClickEnabled = true, SingleSelectionFollowsFocus = false,
            SelectionMode = ListViewSelectionMode.Single, ItemTemplate = (DataTemplate)resources["OrderDropDownItemTemplate"],
            ItemContainerStyle = (Style)resources["OrderDropDownItemStyle"]
        };
        ScrollViewer.SetHorizontalScrollMode(list, ScrollMode.Disabled);
        ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Disabled);
        ScrollViewer.SetVerticalScrollBarVisibility(list, ScrollBarVisibility.Auto);
        ScrollViewer.SetIsVerticalScrollChainingEnabled(list, false);
        list.ContainerContentChanging += (_, args) =>
        {
            if (args.Item is DropDownOption option) args.ItemContainer.IsEnabled = IsOptionEnabled(option.Index);
        };
        flyout = new Flyout
        {
            Content = list, Placement = FlyoutPlacementMode.BottomEdgeAlignedLeft, ShouldConstrainToRootBounds = true,
            FlyoutPresenterStyle = (Style)resources["OrderDropDownListStyle"]
        };
        list.ItemClick += (_, args) =>
        {
            if (args.ClickedItem is not DropDownOption option || !IsOptionEnabled(option.Index)) return;
            flyout.Hide();
            OptionIndex = option.Index;
        };

        Trigger.Flyout = flyout;
        Content = new StackPanel { Children = { header, Trigger } };

        Options.CollectionChanged += (_, args) => { if (OptionsSource is null) RefreshOptions(args); };
        Loaded += (_, _) => { ChangeSource(); UpdateCaption(); };
        Unloaded += (_, _) => { flyout.Hide(); StopObservingSource(); };
        Trigger.PreviewKeyDown += OnClosedKeyDown;
        Trigger.SizeChanged += (_, _) => { if (flyout.IsOpen) flyout.Hide(); };
        flyout.Opening += OnOpening;
        flyout.Opened += (_, _) =>
        {
            if (list.SelectedItem is { } selection) list.ScrollIntoView(selection, ScrollIntoViewAlignment.Leading);
            DropDownOpened?.Invoke(this, EventArgs.Empty);
        };
        RegisterPropertyChangedCallback(AutomationProperties.AutomationIdProperty, (_, _) => SyncAccessibility());
        RegisterPropertyChangedCallback(AutomationProperties.NameProperty, (_, _) => SyncAccessibility());
    }

    private void OnOpening(object? sender, object args)
    {
        if (Trigger.XamlRoot is not { } root) return;
        var bounds = Trigger.TransformToVisual(root.Content)
            .TransformBounds(new(0, 0, Trigger.ActualWidth, Trigger.ActualHeight));
        // The presenter adds a 1-DIP border and 4 DIPs of padding on each side.
        list.Width = Math.Max(0, bounds.Width - 10);
        double roomBelow = root.Size.Height - bounds.Bottom - 24;
        // Keep at least one usable row. The native flyout can flip in the last row of a short viewport.
        list.MaxHeight = Math.Clamp(roomBelow < 34 ? bounds.Top - 24 : roomBelow, 34, 320);
        PopulateRows();
    }

    private void ChangeSource()
    {
        StopObservingSource();
        if (IsLoaded && OptionsSource is INotifyCollectionChanged source)
        {
            observedSource = source;
            source.CollectionChanged += OnSourceItemsChanged;
        }
        RefreshOptions();
    }

    private void StopObservingSource()
    {
        if (observedSource is not null) observedSource.CollectionChanged -= OnSourceItemsChanged;
        observedSource = null;
    }

    private void OnSourceItemsChanged(object? sender, NotifyCollectionChangedEventArgs args) => RefreshOptions(args);

    private void RefreshOptions(NotifyCollectionChangedEventArgs? change = null)
    {
        if (change is { Action: NotifyCollectionChangedAction.Add, NewItems: { } added } && change.NewStartingIndex == master.Count)
        {
            // Guide sources can append while the list is open. Leave the visible rows and scroll position intact.
            foreach (var item in added)
            {
                int index = master.Count;
                master.Add(item!);
                if (flyout.IsOpen) rows.Add(CreateRow(index));
            }
        }
        else
        {
            var next = (OptionsSource as IEnumerable ?? Options).Cast<object>().ToList();
            if (master.SequenceEqual(next)) return;
            var selected = SelectedOption;
            master = next;
            if (selected is not null && !Equals(selected, SelectedOption))
            {
                synchronizing = true;
                try { OptionIndex = master.IndexOf(selected); }
                finally { synchronizing = false; }
            }
            if (flyout.IsOpen) PopulateRows();
        }
        ChangeSelection();
    }

    private void PopulateRows()
    {
        rows.Clear();
        int selected = SelectedOption is null ? -1 : OptionIndex;
        if (selected >= 0) rows.Add(CreateRow(selected));
        for (int index = 0; index < master.Count; index++)
            if (index != selected) rows.Add(CreateRow(index));
        list.SelectedIndex = selected >= 0 ? 0 : -1;
    }

    private DropDownOption CreateRow(int index)
    {
        var item = master[index];
        var label = Display(item);
        var tooltip = item is ComboBoxItem option ? ToolTipService.GetToolTip(option) : null;
        return new(index, label, tooltip ?? label);
    }

    private void UpdateHeader()
    {
        header.Content = Header;
        header.Visibility = Header is null or "" ? Visibility.Collapsed : Visibility.Visible;
        SyncAccessibility();
    }

    private void UpdateDisplayLabels()
    {
        UpdateCaption();
        if (flyout.IsOpen) PopulateRows();
    }

    private void UpdateCaption()
    {
        caption.Text = SelectedOption is { } item ? Display(item) : PlaceholderText;
        SyncAccessibility();
    }

    private void SyncAccessibility()
    {
        var id = AutomationProperties.GetAutomationId(this);
        AutomationProperties.SetAutomationId(Trigger, id);
        AutomationProperties.SetAutomationId(list, string.IsNullOrEmpty(id) ? "" : id + "Choices");
        var name = AutomationProperties.GetName(this);
        AutomationProperties.SetName(Trigger, string.IsNullOrEmpty(name) ? Header as string ?? caption.Text : name);
        AutomationProperties.SetItemStatus(Trigger, caption.Text);
    }

    private string Display(object item)
    {
        if (item is ComboBoxItem option) return option.Content?.ToString() ?? "";
        if (DisplayMemberPath.Length > 0 && item.GetType().GetProperty(DisplayMemberPath)?.GetValue(item) is { } published)
            return published.ToString() ?? "";
        return item.ToString() ?? "";
    }

    private void ChangeSelection()
    {
        if (synchronizing) return;
        UpdateCaption();
        var selected = SelectedOption;
        var index = selected is null ? -1 : OptionIndex;
        if (flyout.IsOpen) list.SelectedItem = rows.FirstOrDefault(row => row.Index == index);
        if (index == lastIndex && Equals(selected, lastOption)) return;
        var previous = lastOption;
        lastOption = selected;
        lastIndex = index;
        OptionChanged?.Invoke(this, new(previous is null ? [] : [previous], selected is null ? [] : [selected]));
    }

    private void OnClosedKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (flyout.IsOpen || args.Key is not (VirtualKey.Up or VirtualKey.Down or VirtualKey.Home or VirtualKey.End)) return;
        if (InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Menu).HasFlag(CoreVirtualKeyStates.Down))
        {
            if (args.Key != VirtualKey.Down) return;
            args.Handled = true;
            flyout.ShowAt(Trigger);
            return;
        }

        var index = OptionIndex;
        switch (args.Key)
        {
            case VirtualKey.Up: SelectFrom(index - 1, -1); break;
            case VirtualKey.Down: SelectFrom(index + 1, 1); break;
            case VirtualKey.Home: SelectFrom(0, 1); break;
            case VirtualKey.End: SelectFrom(master.Count - 1, -1); break;
        }
        args.Handled = true;
    }

    protected override void OnPointerWheelChanged(PointerRoutedEventArgs args)
    {
        if (!flyout.IsOpen && Trigger.FocusState != FocusState.Unfocused && IsEnabled)
        {
            var delta = args.GetCurrentPoint(Trigger).Properties.MouseWheelDelta;
            if (delta != 0)
            {
                int step = -Math.Sign(delta);
                SelectFrom(OptionIndex + step, step);
                args.Handled = true;
                return;
            }
        }
        base.OnPointerWheelChanged(args);
    }

    private bool IsOptionEnabled(int index) =>
        (uint)index < (uint)master.Count && master[index] is not ComboBoxItem { IsEnabled: false };

    private void SelectFrom(int start, int step)
    {
        for (int index = start; (uint)index < (uint)master.Count; index += step)
        {
            if (!IsOptionEnabled(index)) continue;
            OptionIndex = index;
            return;
        }
    }

    internal sealed record DropDownOption(int Index, string Label, object Tooltip);
}
