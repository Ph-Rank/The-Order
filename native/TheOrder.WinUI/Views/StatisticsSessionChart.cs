using System.Collections.Immutable;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using TheOrder.Application;
using Windows.Foundation;
using Windows.UI;

namespace TheOrder.WinUI.Views;

public sealed partial class StatisticsTrendChart
{
    private static readonly uint[] LightSessionColors = [0x5853CB, 0x007DAF, 0x008370, 0xBA511C, 0xB23B76, 0x697616, 0x884CA5, 0xBE3A49];
    private static readonly uint[] DarkSessionColors = [0x918DFF, 0x40B9FF, 0x3CCEB0, 0xF9A25E, 0xF386B7, 0xB9C955, 0xC998EC, 0xFF7D83];
    private readonly Dictionary<string, int> rememberedColors = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> sessionColors = new(StringComparer.Ordinal);
    private ImmutableArray<StatisticsSessionBucket> sessions = [];
    private Brush[] sessionBrushes = [];
    private Action<Border?>? showSessionMark;
    internal StatisticsSessionAxis? SessionAxis { get; private set; }

    internal void ShowSessionMark(Border? mark) => showSessionMark?.Invoke(mark);

    public void SetSessionPeriods(ImmutableArray<StatisticsSessionBucket> value)
    {
        if (sessions == value) return;
        sessions = value;
        SessionAxis = StatisticsSessionAxis.For(sessions);
        UpdateSessionPalette();
        Draw();
    }

    private void UpdateSessionPalette()
    {
        var palette = ActualTheme == ElementTheme.Dark ? DarkSessionColors : LightSessionColors;
        sessionBrushes = [.. palette.Select(BrushFor), BrushFor(ActualTheme == ElementTheme.Dark ? 0xA5A8B3u : 0x67707Fu)];
        var games = sessions.SelectMany(bucket => bucket.Ranges).GroupBy(range => range.GameId, StringComparer.Ordinal)
            .Select(group => (Id: group.Key, Weight: group.Sum(range => (long)range.EndSecond - range.StartSecond)))
            .OrderByDescending(game => game.Weight).ThenBy(game => game.Id, StringComparer.Ordinal);
        var colored = games.Take(palette.Length).ToArray();
        var unused = new SortedSet<int>(Enumerable.Range(0, palette.Length));
        sessionColors.Clear();
        foreach (var game in colored)
            if (rememberedColors.TryGetValue(game.Id, out int index) && unused.Remove(index)) sessionColors.Add(game.Id, index);
        foreach (var game in colored)
        {
            if (sessionColors.ContainsKey(game.Id)) continue;
            int index = unused.Min;
            unused.Remove(index);
            sessionColors.Add(game.Id, index);
            rememberedColors.TryAdd(game.Id, index);
        }
    }

    private static SolidColorBrush BrushFor(uint rgb) => new(Color.FromArgb(255, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb));
    private Brush SessionBrush(int color) => highContrast && Ink is { } ink ? ink : sessionBrushes[color];

    private void DrawSessionPeriods(double width, double height)
    {
        if (sessions.IsEmpty || sessionBrushes.Length == 0) return;
        const double left = 3, top = 12;
        double right = width - 46, bottom = height - 25;
        if (SessionAxis is not { } axis)
        {
            Position(new TextBlock
            {
                Text = "当前区间暂无带起止时间的游玩记录",
                Width = width, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap,
                Foreground = LabelBrush, FontSize = 13
            }, 0, (top + bottom) / 2 - 10);
            AutomationProperties.SetName(this, "游玩时段，当前区间暂无记录");
            return;
        }

        double TimeOffset(int second) => (bottom - top) * (second - axis.StartSecond) / (axis.EndSecond - axis.StartSecond);
        for (int second = axis.StartSecond; second < axis.EndSecond - axis.TickSeconds / 2; second += axis.TickSeconds)
            DrawClockTick(second);
        DrawClockTick(axis.EndSecond);

        void DrawClockTick(int second)
        {
            double y = top + TimeOffset(second);
            canvas.Children.Add(new Line
            {
                X1 = left, X2 = right, Y1 = y, Y2 = y,
                Stroke = highContrast ? LabelBrush : Gridline, StrokeThickness = 1,
                IsHitTestVisible = false
            });
            var label = AxisLabel(ClockText(second % StatisticsSessionAxis.SecondsPerDay));
            label.Measure(new Size(45, 30));
            Position(label, right + 7, y - label.DesiredSize.Height / 2);
        }

        var dateLabels = sessions.Select(bucket => AxisLabel(bucket.Label)).ToArray();
        foreach (var label in dateLabels) label.Measure(new Size(86, 30));
        int last = sessions.Length - 1;
        double plotWidth = right - left;
        double labelWidth = Math.Min(dateLabels.Max(label => label.DesiredSize.Width), last == 0 ? plotWidth : plotWidth / 2);
        double edge = Math.Max(labelWidth, plotWidth / sessions.Length) / 2;
        double slot = last == 0 ? plotWidth : (plotWidth - 2 * edge) / last;
        int stride = Math.Max(1, (int)Math.Ceiling((labelWidth + 7) / slot));
        var marks = new List<Border>();
        for (int i = 0; i < sessions.Length; i++)
        {
            var bucket = sessions[i];
            double x = left + edge + i * slot;
            if (i == 0 || i == last && i >= stride || i % stride == 0 && last - i >= stride)
            {
                var label = dateLabels[i];
                label.Width = labelWidth;
                label.TextAlignment = TextAlignment.Center;
                Position(label, x - labelWidth / 2, bottom + 6);
                if (!highContrast)
                    canvas.Children.Add(new Line
                    {
                        X1 = x, X2 = x, Y1 = top, Y2 = bottom,
                        Stroke = Gridline, StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 2, 3 },
                        IsHitTestVisible = false
                    });
            }

            if (bucket.Ranges.IsEmpty) continue;
            var column = new Canvas { Width = slot, Height = bottom - top };
            DrawSessionColumn(column, bucket, axis, TimeOffset);
            marks.AddRange(column.Children.OfType<Border>());
            AddSessionDateTarget(bucket, column, x - slot / 2, top);
        }

        if (marks.Count > 0) AddSessionHover(marks, bottom);
        AutomationProperties.SetName(this, "游玩时段，按本地时间显示，可用 Tab 查看各日期。");
    }

    private void DrawSessionColumn(Canvas column, StatisticsSessionBucket bucket, StatisticsSessionAxis axis, Func<int, double> timeOffset)
    {
        double width = Math.Min(38, column.Width * .72);
        var laneEnds = new List<double>();
        var pending = new List<(StatisticsSessionRange Range, double Top, double Bottom, int Lane)>();
        double componentEnd = 0;
        foreach (var range in bucket.Ranges.SelectMany(axis.Project).OrderBy(range => range.StartSecond))
        {
            double top = timeOffset(range.StartSecond);
            double bottom = Math.Min(column.Height, Math.Max(top + 1.25, timeOffset(range.EndSecond)));
            if (top >= componentEnd) DrawComponent();
            // Use the drawn height so nearby short sessions cannot cover each other's marks.
            int lane = laneEnds.FindIndex(end => end <= top);
            if (lane < 0) { lane = laneEnds.Count; laneEnds.Add(bottom); }
            else laneEnds[lane] = bottom;
            pending.Add((range, top, bottom, lane));
            componentEnd = Math.Max(componentEnd, bottom);
        }
        DrawComponent();

        void DrawComponent()
        {
            if (pending.Count == 0) return;
            double laneWidth = width / laneEnds.Count;
            double gap = laneEnds.Count > 1 ? Math.Min(.7, laneWidth * .18) : 0;
            foreach (var (range, top, bottom, lane) in pending)
            {
                double markWidth = laneWidth - gap, markHeight = bottom - top;
                int color = sessionColors.GetValueOrDefault(range.GameId, sessionBrushes.Length - 1);
                var mark = new Border
                {
                    Width = markWidth, Height = markHeight, Background = SessionBrush(color), Tag = range,
                    CornerRadius = new(Math.Min(2, Math.Min(markWidth, markHeight) * .28))
                };
                AutomationProperties.SetName(mark, RangeText(range));
                Canvas.SetLeft(mark, (column.Width - width) / 2 + lane * laneWidth + gap / 2);
                Canvas.SetTop(mark, top);
                column.Children.Add(mark);
            }
            pending.Clear();
            laneEnds.Clear();
        }
    }

    private void AddSessionHover(List<Border> marks, double bottom)
    {
        const double dimmedOpacity = .30;
        var time = AxisLabel("");
        time.TextWrapping = TextWrapping.Wrap;
        var name = CreateReadoutValue();
        name.MaxLines = 3;
        name.TextTrimming = TextTrimming.CharacterEllipsis;
        var readout = CreateReadout(time, name);
        readout.Visibility = Visibility.Collapsed;
        AutomationProperties.SetAutomationId(readout, "StatisticsSessionReadout");
        var outline = new Border
        {
            BorderBrush = highContrast ? Ink : LabelBrush, BorderThickness = new(1), CornerRadius = new(3),
            Opacity = highContrast ? 1 : .72, Visibility = Visibility.Collapsed, IsHitTestVisible = false
        };
        AutomationProperties.SetAutomationId(outline, "StatisticsSessionHighlight");
        canvas.Children.Add(outline);
        canvas.Children.Add(readout);

        Border? hovered = null;
        void Show(Border? mark)
        {
            if (ReferenceEquals(hovered, mark)) return;
            if (!highContrast)
            {
                if (hovered is null || mark is null)
                    foreach (var band in marks) band.Opacity = mark is null || ReferenceEquals(band, mark) ? 1 : dimmedOpacity;
                else
                {
                    hovered.Opacity = dimmedOpacity;
                    mark.Opacity = 1;
                }
            }
            hovered = mark;
            if (mark?.Tag is not StatisticsSessionRange range)
            {
                outline.Visibility = readout.Visibility = Visibility.Collapsed;
                return;
            }

            var bounds = mark.TransformToVisual(canvas).TransformBounds(new Rect(0, 0, mark.ActualWidth, mark.ActualHeight));
            outline.Width = bounds.Width + 4;
            outline.Height = bounds.Height + 4;
            Canvas.SetLeft(outline, bounds.Left - 2);
            Canvas.SetTop(outline, bounds.Top - 2);
            outline.Visibility = readout.Visibility = Visibility.Visible;
            time.Text = RangeTime(range);
            name.Text = range.GameName;
            double sideSpace = Math.Max(bounds.Left, ActualWidth - bounds.Right) - 18;
            readout.MaxWidth = Math.Clamp(sideSpace, 96, Math.Min(248, ActualWidth - 4));
            PlaceReadout(readout, bounds, bottom);
        }

        showSessionMark = Show;
        foreach (var mark in marks)
        {
            mark.PointerEntered += (_, args) => { if (args.Pointer.PointerDeviceType != PointerDeviceType.Touch) Show(mark); };
            mark.PointerExited += (_, _) => { if (ReferenceEquals(hovered, mark)) Show(null); };
        }
    }

    private void AddSessionDateTarget(StatisticsSessionBucket bucket, Canvas column, double left, double top)
    {
        string date = bucket.Start == bucket.End ? $"{bucket.Start:M月d日}" : $"{bucket.Start:M/d} – {bucket.End:M/d}";
        string[] preview = [.. bucket.Ranges.Take(4).Select(RangeText)];
        var highlight = new Border
        {
            Width = column.Width, Height = column.Height, BorderBrush = LabelBrush, BorderThickness = new(1),
            Opacity = 0, IsHitTestVisible = false
        };
        Position(highlight, left, top);
        var target = new Button
        {
            Content = column, Width = column.Width, Height = column.Height,
            MinWidth = 0, MinHeight = 0, Padding = new(0), Style = TargetStyle
        };
        AutomationProperties.SetName(target, date + "，" + string.Join("，", preview)
            + $"，共 {bucket.Ranges.Length} 个时段，打开详情");
        AutomationProperties.SetAutomationId(target, $"StatisticsSessions:{bucket.Start:yyyy-MM-dd}");
        target.GotFocus += (_, _) => highlight.Opacity = target.FocusState == FocusState.Keyboard ? 1 : 0;
        target.LostFocus += (_, _) => highlight.Opacity = 0;
        target.Click += (_, _) =>
        {
            ShowSessionMark(null);
            var list = new ListView
            {
                SelectionMode = ListViewSelectionMode.None, MaxHeight = 228,
                ItemsSource = bucket.Ranges.Select(RangeText).ToArray()
            };
            var heading = new TextBlock { Text = date, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
            new Flyout { Content = new StackPanel { Width = 286, Spacing = 6, Children = { heading, list } } }.ShowAt(target);
        };
        Position(target, left, top);
    }

    private static string RangeText(StatisticsSessionRange range) => $"{RangeTime(range)}  {range.GameName}";

    private static string RangeTime(StatisticsSessionRange range)
    {
        int dayOffset = range.StartSecond >= StatisticsSessionAxis.SecondsPerDay ? StatisticsSessionAxis.SecondsPerDay : 0;
        return $"{ClockText(range.StartSecond - dayOffset)}–{ClockText(range.EndSecond - dayOffset)}";
    }

    private static string ClockText(int seconds)
        => seconds % 60 == 0 ? $"{seconds / 3600:00}:{seconds / 60 % 60:00}" : $"{seconds / 3600:00}:{seconds / 60 % 60:00}:{seconds % 60:00}";
}
