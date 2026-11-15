using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Markup;
using Windows.System;
using Windows.UI.Core;

namespace TheOrder.WinUI.Views;

[ContentProperty(Name = nameof(Options))]
public sealed class SelectedFirstComboBox : ComboBox
{
    public static readonly DependencyProperty OptionsSourceProperty = DependencyProperty.Register(
        nameof(OptionsSource), typeof(object), typeof(SelectedFirstComboBox),
        new PropertyMetadata(null, (target, _) => ((SelectedFirstComboBox)target).ChangeSource()));

    public static readonly DependencyProperty OptionIndexProperty = DependencyProperty.Register(
        nameof(OptionIndex), typeof(int), typeof(SelectedFirstComboBox),
        new PropertyMetadata(-1, (target, _) => ((SelectedFirstComboBox)target).ChangeSelection()));

    public ObservableCollection<object> Options { get; } = [];
    public object? OptionsSource { get => GetValue(OptionsSourceProperty); set => SetValue(OptionsSourceProperty, value); }
    public int OptionIndex { get => (int)GetValue(OptionIndexProperty); set => SetValue(OptionIndexProperty, value); }
    public object? SelectedOption
    {
        get => (uint)OptionIndex < (uint)master.Count ? master[OptionIndex] : null;
        set => OptionIndex = value is null ? -1 : master.IndexOf(value);
    }

    public event SelectionChangedEventHandler? OptionChanged;
    internal IReadOnlyList<object> PrimaryOptions => master;

    private List<object> master = [];
    private ObservableCollection<object> displayed = [];
    private INotifyCollectionChanged? observedSource;
    private object? lastOption;
    private int lastIndex = -1;
    private int first = -1;
    private bool popupActive, synchronizing, fromDisplay;

    public SelectedFirstComboBox()
    {
        DefaultStyleKey = typeof(ComboBox);
        ItemsSource = displayed;
        Options.CollectionChanged += (_, args) => { if (OptionsSource is null) RefreshOptions(args); };
        Loaded += (_, _) => ChangeSource();
        Unloaded += (_, _) => StopObservingSource();
        DropDownOpened += (_, _) => popupActive = true;
        DropDownClosed += (_, _) => { popupActive = false; UpdateDisplay(); };
        SelectionChanged += OnDisplaySelectionChanged;
        PreviewKeyDown += OnClosedKeyDown;
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
            // Some sources append pages while the menu is open. Keep the visible rows and focus in place.
            synchronizing = true;
            try
            {
                foreach (var item in added) { master.Add(item!); displayed.Add(item!); }
            }
            finally { synchronizing = false; }
            UpdateDisplay();
            NotifySelection();
            return;
        }

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
        UpdateDisplay(replaceItems: true);
        NotifySelection();
    }

    private void ChangeSelection()
    {
        if (synchronizing) return;
        if (!fromDisplay) UpdateDisplay();
        NotifySelection();
    }

    private void UpdateDisplay(bool replaceItems = false)
    {
        var selected = SelectedOption is null ? -1 : OptionIndex;
        var promote = popupActive && !replaceItems ? first : selected > 0 ? selected : -1;
        synchronizing = true;
        try
        {
            if (replaceItems || first != promote)
            {
                first = promote;
                displayed = first < 0 ? new(master) : new([master[first], .. master.Where((_, index) => index != first)]);
                ItemsSource = displayed;
            }
            SelectedIndex = selected < 0 || first < 0 ? selected : selected == first ? 0 : selected < first ? selected + 1 : selected;
        }
        finally { synchronizing = false; }
    }

    private void OnDisplaySelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (synchronizing) return;
        var index = SelectedIndex;
        fromDisplay = true;
        try { OptionIndex = index < 0 || first < 0 ? index : index == 0 ? first : index <= first ? index - 1 : index; }
        finally { fromDisplay = false; }
        if (!popupActive) DispatcherQueue.TryEnqueue(() => UpdateDisplay());
    }

    private void NotifySelection()
    {
        var selected = SelectedOption;
        var index = selected is null ? -1 : OptionIndex;
        if (index == lastIndex && Equals(selected, lastOption)) return;
        var previous = lastOption;
        lastOption = selected;
        lastIndex = index;
        OptionChanged?.Invoke(this, new(previous is null ? [] : [previous], selected is null ? [] : [selected]));
    }

    private void OnClosedKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (IsDropDownOpen || InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Menu).HasFlag(CoreVirtualKeyStates.Down)) return;
        var index = OptionIndex;
        switch (args.Key)
        {
            case VirtualKey.Up: SelectFrom(index - 1, -1); break;
            case VirtualKey.Down: SelectFrom(index + 1, 1); break;
            case VirtualKey.Home: SelectFrom(0, 1); break;
            case VirtualKey.End: SelectFrom(master.Count - 1, -1); break;
            default: return;
        }
        args.Handled = true;
    }

    protected override void OnPointerWheelChanged(PointerRoutedEventArgs args)
    {
        if (IsDropDownOpen || FocusState == FocusState.Unfocused || !IsEnabled)
        {
            base.OnPointerWheelChanged(args);
            return;
        }
        int step = args.GetCurrentPoint(this).Properties.MouseWheelDelta < 0 ? 1 : -1;
        SelectFrom(OptionIndex + step, step);
        args.Handled = true;
    }

    private void SelectFrom(int start, int step)
    {
        for (int index = start; (uint)index < (uint)master.Count; index += step)
        {
            if (master[index] is ComboBoxItem { IsEnabled: false }) continue;
            OptionIndex = index;
            return;
        }
    }
}
