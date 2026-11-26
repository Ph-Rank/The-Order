using System.Collections.Immutable;
using System.Globalization;
using Microsoft.UI.Input;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using TheOrder.Application;
using TheOrder.Presentation;
using Windows.Foundation;
using Windows.UI;

namespace TheOrder.WinUI.Views;

public sealed partial class StatisticsTrendChart
{
    private static readonly uint[] LightSessionColors =
    [
        0x4F52C8, 0x0079A8, 0x0D8A6A, 0xC85A17,
        0xB83B74, 0x6B7D12, 0x8C47AE, 0xC93B49,
        0x2A6FD6, 0x00868D, 0xA36B00, 0x9E4B5A
    ];

    private static readonly uint[] DarkSessionColors =
    [
        0x8F8CFF, 0x4CC2FF, 0x3CD4AE, 0xFBA465,
        0xF484B8, 0xBCD254, 0xCA97F2, 0xFF7C84,
        0x6CA8FF, 0x4ED6DC, 0xF2C55C, 0xE38E9D
    ];

    private StatisticsSessionTimelineData sessionTimeline = StatisticsSessionTimelineData.Empty;
    private Brush[] sessionBrushes = [];
    private Action<Border?>? showSessionMark;
    private Func<Point, Border?>? hitTestSessionPoint;
    private Action<Point>? onSessionPointerMoved;
    private Action? onSessionPointerExited;

    internal StatisticsSessionAxis? SessionAxis => sessionTimeline.Axis;

    internal void ShowSessionMark(Border? mark) => showSessionMark?.Invoke(mark);

    internal Border? HitTestSessionMark(Point chartLocalPoint) => hitTestSessionPoint?.Invoke(chartLocalPoint);

    public void SetSessionTimeline(StatisticsSessionTimelineData value)
    {
        if (ReferenceEquals(sessionTimeline, value)) return;
        sessionTimeline = value;
        UpdateSessionPalette();
        Draw();
    }

    internal static string FormatAxisRange(StatisticsSessionAxis axis)
    {
        string start = ClockText(axis.StartSecond % StatisticsSessionAxis.SecondsPerDay);
        string endClock = axis.EndSecond == StatisticsSessionAxis.SecondsPerDay
            ? "24:00"
            : ClockText(axis.EndSecond % StatisticsSessionAxis.SecondsPerDay);
        return axis.CrossesMidnight ? $"{start} – 次日 {endClock}" : $"{start} – {endClock}";
    }

    private void UpdateSessionPalette()
    {
        var palette = ActualTheme == ElementTheme.Dark ? DarkSessionColors : LightSessionColors;
        sessionBrushes = [.. palette.Select(BrushFor)];
    }

    private static SolidColorBrush BrushFor(uint rgb)
        => new(Color.FromArgb(255, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb));

    private Brush SessionBrush(int colorIndex)
    {
        if (highContrast && Ink is { } ink) return ink;
        if (sessionBrushes.Length == 0) UpdateSessionPalette();
        int idx = ((colorIndex % sessionBrushes.Length) + sessionBrushes.Length) % sessionBrushes.Length;
        return sessionBrushes[idx];
    }

    private sealed record SessionMarkEntry(
        Border Visual,
        StatisticsSessionSpan Span,
        Rect CanvasBounds,
        double CornerRadius,
        double RestOpacity,
        Border? ColumnTint,
        bool MultiDay,
        Action OpenBucketFlyout);

    private void DrawSessionPeriods(double width, double height)
    {
        hitTestSessionPoint = null;
        if (sessionBrushes.Length == 0) UpdateSessionPalette();
        var buckets = sessionTimeline.Buckets;
        if (buckets.IsEmpty) return;

        const double top = 12, rightPad = 8, bottomPad = 26;
        double bottom = height - bottomPad;
        if (sessionTimeline.Axis is not { } axis)
        {
            Position(new TextBlock
            {
                Text = "当前区间暂无带起止时间的游玩记录",
                Width = width,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                Foreground = LabelBrush,
                FontSize = 13
            }, 0, (top + bottom) / 2 - 10);
            AutomationProperties.SetName(this, "游玩时段，当前区间暂无记录");
            return;
        }

        var ticks = axis.GetTicks();
        var tickLabels = ticks.Select(second =>
        {
            bool isMidnight = axis.CrossesMidnight && second == StatisticsSessionAxis.SecondsPerDay;
            string text = !axis.CrossesMidnight && second == StatisticsSessionAxis.SecondsPerDay
                ? "24:00"
                : ClockText(second % StatisticsSessionAxis.SecondsPerDay);
            var label = AxisLabel(text);
            if (isMidnight)
            {
                label.FontWeight = FontWeights.SemiBold;
                label.Foreground = Ink;
            }
            label.Measure(new Size(64, 30));
            double measuredW = Math.Max(34, label.DesiredSize.Width);
            double measuredH = Math.Max(16, label.DesiredSize.Height);
            return (Second: second, IsMidnight: isMidnight, Label: label, Width: measuredW, Height: measuredH);
        }).ToArray();

        double axisWidth = tickLabels.Max(item => item.Width);
        double left = axisWidth + 12;
        double right = width - rightPad;
        double plotWidth = right - left;
        double plotHeight = bottom - top;
        if (plotWidth < 48 || plotHeight < 48) return;

        double TimeY(int second) => top + plotHeight * (second - axis.StartSecond) / axis.SpanSeconds;

        // Subtle nocturnal twilight wash below 00:00 so after-midnight hours read unmistakably at a glance.
        if (axis.CrossesMidnight && !highContrast)
        {
            double midnightY = Math.Clamp(TimeY(StatisticsSessionAxis.SecondsPerDay), top, bottom);
            if (bottom - midnightY >= 4)
            {
                bool light = ActualTheme == ElementTheme.Light;
                var twilight = new Rectangle
                {
                    Width = plotWidth,
                    Height = bottom - midnightY,
                    Fill = new SolidColorBrush(Color.FromArgb(light ? (byte)9 : (byte)15, 112, 130, 198)),
                    IsHitTestVisible = false
                };
                Canvas.SetLeft(twilight, left);
                Canvas.SetTop(twilight, midnightY);
                canvas.Children.Add(twilight);
            }
        }

        // Subtle outer plot hairlines so non-aligned 24h axis bounds remain clearly framed.
        foreach (double frameY in new[] { top, bottom })
        {
            canvas.Children.Add(new Line
            {
                X1 = left, X2 = right, Y1 = frameY, Y2 = frameY,
                Stroke = highContrast ? LabelBrush : Gridline, StrokeThickness = 1,
                Opacity = highContrast ? 0.85 : 0.45,
                IsHitTestVisible = false
            });
        }

        for (int t = 0; t < tickLabels.Length; t++)
        {
            var (second, isMidnight, label, _, labelHeight) = tickLabels[t];
            double y = TimeY(second);
            bool atBoundary = y <= top + 0.5 || y >= bottom - 0.5;
            var line = new Line
            {
                X1 = left, X2 = right, Y1 = y, Y2 = y,
                Stroke = isMidnight ? (highContrast ? Ink : LabelBrush) : (highContrast ? LabelBrush : Gridline),
                StrokeThickness = 1,
                Opacity = highContrast ? 1 : (isMidnight ? 0.48 : 0.62),
                IsHitTestVisible = false
            };
            if (isMidnight) line.StrokeDashArray = new DoubleCollection { 4, 3 };
            else if (!atBoundary && !highContrast) line.StrokeDashArray = new DoubleCollection { 2, 3 };
            canvas.Children.Add(line);

            label.Width = axisWidth;
            label.TextAlignment = TextAlignment.Right;
            double labelY = Math.Clamp(y - labelHeight / 2, 0, Math.Max(0, height - labelHeight));
            Position(label, 0, labelY);

            if (isMidnight)
            {
                var nextDayTag = new TextBlock
                {
                    Text = "次日",
                    FontSize = 10,
                    Foreground = LabelBrush,
                    Opacity = highContrast ? 1 : 0.82,
                    Width = axisWidth,
                    TextAlignment = TextAlignment.Right,
                    IsHitTestVisible = false
                };
                AutomationProperties.SetAutomationId(nextDayTag, "StatisticsSessionMidnightLabel");
                nextDayTag.Measure(new Size(axisWidth, 20));
                double tagH = Math.Max(13, nextDayTag.DesiredSize.Height);
                double nextTickTop = t + 1 < tickLabels.Length
                    ? TimeY(tickLabels[t + 1].Second) - tickLabels[t + 1].Height / 2
                    : bottom + 4;
                double tagY = labelY + labelHeight;
                if (tagY + tagH <= nextTickTop)
                    Position(nextDayTag, 0, tagY);
                else
                    Position(nextDayTag, 0, Math.Max(labelY + labelHeight - 1, nextTickTop - tagH));
            }
        }

        var dateLabels = buckets.Select(bucket => AxisLabel(bucket.Label)).ToArray();
        foreach (var label in dateLabels) label.Measure(new Size(86, 30));

        int last = buckets.Length - 1;
        double slot = plotWidth / buckets.Length;
        double maxLabelWidth = Math.Min(dateLabels.Max(label => label.DesiredSize.Width), last == 0 ? plotWidth : plotWidth / 2);
        int stride = Math.Max(1, (int)Math.Ceiling((maxLabelWidth + 10) / slot));

        var columnTintLayer = new Canvas { Width = width, Height = height, IsHitTestVisible = false };
        var columnTargetLayer = new Canvas { Width = width, Height = height };
        var marksLayer = new Canvas { Width = width, Height = height };
        canvas.Children.Add(columnTintLayer);
        canvas.Children.Add(columnTargetLayer);
        canvas.Children.Add(marksLayer);

        var markEntries = new List<SessionMarkEntry>();
        double lastShownLabelRight = double.NegativeInfinity;
        for (int i = 0; i < buckets.Length; i++)
        {
            var bucket = buckets[i];
            double colLeft = left + i * slot;
            double colCenter = colLeft + slot / 2;
            bool shouldShowLabel = i == 0 || i == last || (i % stride == 0 && last - i >= stride);
            if (shouldShowLabel)
            {
                var label = dateLabels[i];
                double lw = Math.Max(label.DesiredSize.Width, Math.Min(maxLabelWidth, slot));
                double lx = Math.Clamp(colCenter - lw / 2, left, Math.Max(left, right - lw));
                if (i == last && lx < lastShownLabelRight + 4 && i > 0)
                {
                    // Avoid colliding the rightmost label with the preceding label.
                }
                else
                {
                    label.Width = lw;
                    label.TextAlignment = TextAlignment.Center;
                    Position(label, lx, bottom + 6);
                    lastShownLabelRight = lx + lw;
                    if (!highContrast)
                    {
                        canvas.Children.Add(new Line
                        {
                            X1 = colCenter, X2 = colCenter, Y1 = top, Y2 = bottom,
                            Stroke = Gridline, StrokeThickness = 1, Opacity = 0.38,
                            StrokeDashArray = new DoubleCollection { 2, 3 },
                            IsHitTestVisible = false
                        });
                    }
                }
            }

            if (bucket.Sessions.IsEmpty) continue;

            bool multiDay = bucket.Start < bucket.End;
            var columnTint = new Border
            {
                Width = Math.Max(2, slot - 2),
                Height = plotHeight,
                CornerRadius = new(4),
                Background = Ink,
                Opacity = 0,
                IsHitTestVisible = false
            };
            Canvas.SetLeft(columnTint, colLeft + 1);
            Canvas.SetTop(columnTint, top);
            columnTintLayer.Children.Add(columnTint);

            var (targetButton, openFlyout) = CreateSessionColumnTarget(bucket, colLeft, top, slot, plotHeight, multiDay);
            columnTargetLayer.Children.Add(targetButton);

            if (multiDay)
                DrawMultiDayBucketMarks(marksLayer, bucket, colLeft, slot, top, plotHeight, axis, columnTint, openFlyout, markEntries);
            else
                DrawDailyBucketMarks(marksLayer, bucket, colLeft, slot, top, plotHeight, axis, columnTint, openFlyout, markEntries);
        }

        if (markEntries.Count > 0) AddSessionHover(markEntries, left, right, top, bottom, height);
        AutomationProperties.SetName(this, $"游玩时段，{FormatAxisRange(axis)}，按本地时间显示，可用 Tab 查看各日期。");
    }

    private void DrawDailyBucketMarks(
        Canvas marksLayer,
        StatisticsSessionBucket bucket,
        double colLeft,
        double slot,
        double top,
        double plotHeight,
        StatisticsSessionAxis axis,
        Border columnTint,
        Action openFlyout,
        List<SessionMarkEntry> entries)
    {
        double maxBarWidth = Math.Min(26, Math.Max(6, slot * 0.66));
        foreach (var mark in StatisticsSessionLayout.ArrangeDay(bucket.Sessions, axis, plotHeight))
        {
            double totalWidth = mark.LaneCount == 1 ? maxBarWidth : Math.Min(slot - 2, Math.Max(maxBarWidth, slot * 0.80));
            double laneWidth = totalWidth / mark.LaneCount;
            double gap = mark.LaneCount > 1 ? Math.Clamp(laneWidth * 0.16, 1.5, 3.0) : 0;
            double startX = colLeft + (slot - totalWidth) / 2;
            double width = Math.Max(2.5, laneWidth - gap);
            double x = startX + mark.Lane * laneWidth + gap / 2;
            AddMarkVisual(marksLayer, mark.Span, new Rect(x, top + mark.Top, width, mark.Height),
                restOpacity: 1.0, columnTint, multiDay: false, openFlyout, entries);
        }
    }

    private void DrawMultiDayBucketMarks(
        Canvas marksLayer,
        StatisticsSessionBucket bucket,
        double colLeft,
        double slot,
        double top,
        double plotHeight,
        StatisticsSessionAxis axis,
        Border columnTint,
        Action openFlyout,
        List<SessionMarkEntry> entries)
    {
        int bucketDays = Math.Max(1, bucket.End.DayNumber - bucket.Start.DayNumber + 1);
        double pad = Math.Min(4, slot * 0.08);
        double innerW = Math.Max(4, slot - 2 * pad);
        double basePillWidth = Math.Clamp(innerW / bucketDays * 0.88, 2.2, 8.0);
        double restOpacity = highContrast ? 1.0 : 0.88;

        foreach (var dayGroup in bucket.Sessions.GroupBy(span => span.CycleDate))
        {
            int dayOffset = Math.Clamp(dayGroup.Key.DayNumber - bucket.Start.DayNumber, 0, bucketDays - 1);
            double fraction = bucketDays == 1 ? 0.5 : (dayOffset + 0.5) / bucketDays;
            double dayCenterX = colLeft + pad + fraction * innerW;

            foreach (var mark in StatisticsSessionLayout.ArrangeDay(dayGroup, axis, plotHeight))
            {
                double width = mark.LaneCount == 1 ? basePillWidth :
                    Math.Max(3.0, Math.Min(basePillWidth * 0.78, innerW / Math.Max(mark.LaneCount, 2)));
                double gap = mark.LaneCount > 1 ? 1.2 : 0;
                double groupWidth = mark.LaneCount * width + (mark.LaneCount - 1) * gap;
                double groupLeft = Math.Clamp(dayCenterX - groupWidth / 2, colLeft + 1, Math.Max(colLeft + 1, colLeft + slot - groupWidth - 1));
                double x = groupLeft + mark.Lane * (width + gap);
                AddMarkVisual(marksLayer, mark.Span, new Rect(x, top + mark.Top, width, mark.Height),
                    restOpacity, columnTint, multiDay: true, openFlyout, entries);
            }
        }
    }

    private void AddMarkVisual(
        Canvas marksLayer,
        StatisticsSessionSpan span,
        Rect bounds,
        double restOpacity,
        Border columnTint,
        bool multiDay,
        Action openFlyout,
        List<SessionMarkEntry> entries)
    {
        double radius = Math.Min(3.0, Math.Min(bounds.Width, bounds.Height) * 0.35);
        var mark = new Border
        {
            Width = bounds.Width,
            Height = bounds.Height,
            UseLayoutRounding = false,
            Background = SessionBrush(span.ColorIndex),
            CornerRadius = new(radius),
            Opacity = restOpacity,
            Tag = span
        };
        AutomationProperties.SetName(mark, SessionAccessibleText(span, multiDay));
        Canvas.SetLeft(mark, bounds.X);
        Canvas.SetTop(mark, bounds.Y);
        mark.Tapped += (_, args) =>
        {
            args.Handled = true;
            openFlyout();
        };
        marksLayer.Children.Add(mark);
        entries.Add(new(mark, span, bounds, radius, restOpacity, columnTint, multiDay, openFlyout));
    }

    private (Button Target, Action OpenFlyout) CreateSessionColumnTarget(
        StatisticsSessionBucket bucket,
        double left,
        double top,
        double width,
        double height,
        bool multiDay)
    {
        string dateHeading = bucket.Start == bucket.End
            ? $"{bucket.Start:M月d日}"
            : $"{bucket.Start:M/d} – {bucket.End:M/d}";
        string[] distinctSessions =
        [
            .. bucket.Sessions
                .GroupBy(span => span.SessionKey, StringComparer.Ordinal)
                .Select(group => SessionAccessibleText(group.First(), multiDay))
        ];
        string[] preview = [.. distinctSessions.Take(4)];

        var focusFrame = new Border
        {
            Width = width,
            Height = height,
            BorderBrush = LabelBrush,
            BorderThickness = new(1),
            CornerRadius = new(4),
            Opacity = 0,
            IsHitTestVisible = false
        };
        Position(focusFrame, left, top);

        var target = new Button
        {
            Width = width,
            Height = height,
            MinWidth = 0,
            MinHeight = 0,
            Padding = new(0),
            Style = TargetStyle
        };
        Canvas.SetLeft(target, left);
        Canvas.SetTop(target, top);

        AutomationProperties.SetName(target, dateHeading + "，" + string.Join("，", preview)
            + $"，共 {distinctSessions.Length} 次会话，打开详情");
        AutomationProperties.SetAutomationId(target, $"StatisticsSessions:{bucket.Start:yyyy-MM-dd}");
        target.GotFocus += (_, _) => focusFrame.Opacity = target.FocusState == FocusState.Keyboard ? 1 : 0;
        target.LostFocus += (_, _) => focusFrame.Opacity = 0;

        void OpenFlyout()
        {
            ShowSessionMark(null);
            var list = new ListView
            {
                SelectionMode = ListViewSelectionMode.None,
                MaxHeight = 228,
                ItemsSource = distinctSessions
            };
            var heading = new TextBlock { Text = dateHeading, FontWeight = FontWeights.SemiBold };
            new Flyout { Content = new StackPanel { Width = 296, Spacing = 6, Children = { heading, list } } }.ShowAt(target);
        }

        target.Click += (_, _) => OpenFlyout();
        return (target, OpenFlyout);
    }

    private void AddSessionHover(
        List<SessionMarkEntry> marks,
        double plotLeft,
        double plotRight,
        double plotTop,
        double plotBottom,
        double chartHeight)
    {
        const double dimmedOpacity = 0.25;

        var highlightLayer = new Canvas { IsHitTestVisible = false };
        var primaryOutline = new Border
        {
            BorderBrush = Ink,
            BorderThickness = new(1.5),
            CornerRadius = new(4),
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false
        };
        AutomationProperties.SetAutomationId(primaryOutline, "StatisticsSessionHighlight");
        highlightLayer.Children.Add(primaryOutline);

        var swatch = new Border
        {
            Width = 8,
            Height = 8,
            CornerRadius = new(4),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new(0, 1, 0, 0)
        };
        var titleBlock = new TextBlock
        {
            Foreground = Ink,
            FontSize = 13.5,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            MaxLines = 2,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        var titleRow = new Grid { ColumnSpacing = 7 };
        titleRow.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        titleRow.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        titleRow.Children.Add(swatch);
        Grid.SetColumn(titleBlock, 1);
        titleRow.Children.Add(titleBlock);

        var timeBlock = AxisLabel("");
        timeBlock.TextWrapping = TextWrapping.Wrap;

        var readout = new Border
        {
            Background = ReadoutBackground,
            BorderBrush = highContrast ? Ink : (ReadoutBorder ?? Gridline),
            BorderThickness = new(1),
            CornerRadius = new(8),
            Padding = new(11, 8, 11, 9),
            Child = new StackPanel { Spacing = 3, Children = { titleRow, timeBlock } },
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false
        };
        AutomationProperties.SetAutomationId(readout, "StatisticsSessionReadout");

        canvas.Children.Add(highlightLayer);
        canvas.Children.Add(readout);

        var byVisual = marks.ToDictionary(entry => entry.Visual);
        var extraOutlines = new List<Border>();
        SessionMarkEntry? hovered = null;

        SessionMarkEntry? FindEntryAt(Point pt)
        {
            SessionMarkEntry? direct = null;
            double directScore = double.PositiveInfinity;
            foreach (var entry in marks)
            {
                if (!entry.CanvasBounds.Contains(pt)) continue;
                double cx = entry.CanvasBounds.X + entry.CanvasBounds.Width / 2;
                double cy = entry.CanvasBounds.Y + entry.CanvasBounds.Height / 2;
                double dx = pt.X - cx, dy = pt.Y - cy;
                double score = dx * dx + dy * dy + entry.CanvasBounds.Width * entry.CanvasBounds.Height * 0.01;
                if (score < directScore) { direct = entry; directScore = score; }
            }
            if (direct is not null) return direct;

            SessionMarkEntry? near = null;
            double nearDist = double.PositiveInfinity;
            foreach (var entry in marks)
            {
                double padX = entry.CanvasBounds.Width < 8 ? 1.5 : 0;
                double padY = entry.CanvasBounds.Height < 10 ? 2.5 : 0;
                if (padX == 0 && padY == 0) continue;
                var inflated = new Rect(
                    entry.CanvasBounds.X - padX,
                    entry.CanvasBounds.Y - padY,
                    entry.CanvasBounds.Width + padX * 2,
                    entry.CanvasBounds.Height + padY * 2);
                if (!inflated.Contains(pt)) continue;
                double cx = entry.CanvasBounds.X + entry.CanvasBounds.Width / 2;
                double cy = entry.CanvasBounds.Y + entry.CanvasBounds.Height / 2;
                double dist = (pt.X - cx) * (pt.X - cx) + (pt.Y - cy) * (pt.Y - cy);
                if (dist < nearDist) { near = entry; nearDist = dist; }
            }
            return near;
        }

        void ClearExtraOutlines()
        {
            foreach (var extra in extraOutlines) highlightLayer.Children.Remove(extra);
            extraOutlines.Clear();
        }

        void ShowEntry(SessionMarkEntry? active)
        {
            if (ReferenceEquals(hovered, active)) return;
            if (hovered?.ColumnTint is { } previousTint) previousTint.Opacity = 0;
            hovered = active;
            ClearExtraOutlines();

            if (active is null)
            {
                foreach (var entry in marks)
                {
                    entry.Visual.Opacity = entry.RestOpacity;
                    Canvas.SetZIndex(entry.Visual, 0);
                }
                primaryOutline.Visibility = readout.Visibility = Visibility.Collapsed;
                return;
            }

            string activeKey = active.Span.SessionKey;
            foreach (var entry in marks)
            {
                bool match = entry.Span.SessionKey == activeKey;
                if (!highContrast) entry.Visual.Opacity = match ? 1.0 : dimmedOpacity;
                Canvas.SetZIndex(entry.Visual, match ? 10 : 0);
                if (match && !ReferenceEquals(entry, active))
                {
                    var peerOutline = new Border
                    {
                        Width = entry.CanvasBounds.Width + 4,
                        Height = entry.CanvasBounds.Height + 4,
                        BorderBrush = Ink,
                        BorderThickness = new(1.5),
                        CornerRadius = new(Math.Min(4.5, entry.CornerRadius + 1.5)),
                        IsHitTestVisible = false
                    };
                    Canvas.SetLeft(peerOutline, entry.CanvasBounds.Left - 2);
                    Canvas.SetTop(peerOutline, entry.CanvasBounds.Top - 2);
                    highlightLayer.Children.Add(peerOutline);
                    extraOutlines.Add(peerOutline);
                }
            }

            if (active.ColumnTint is { } tint && !highContrast && !active.MultiDay) tint.Opacity = 0.045;

            var b = active.CanvasBounds;
            primaryOutline.Width = b.Width + 4;
            primaryOutline.Height = b.Height + 4;
            primaryOutline.CornerRadius = new(Math.Min(4.5, active.CornerRadius + 1.5));
            Canvas.SetLeft(primaryOutline, b.Left - 2);
            Canvas.SetTop(primaryOutline, b.Top - 2);
            primaryOutline.Visibility = Visibility.Visible;

            swatch.Background = SessionBrush(active.Span.ColorIndex);
            titleBlock.Text = active.Span.GameName;
            timeBlock.Text = FormatSessionRange(active.Span, active.MultiDay);
            readout.Visibility = Visibility.Visible;
            PlaceSessionReadout(readout, b, plotLeft, plotRight, plotTop, plotBottom, chartHeight);
        }

        showSessionMark = mark => ShowEntry(mark is not null && byVisual.TryGetValue(mark, out var entry) ? entry : null);
        hitTestSessionPoint = pt => FindEntryAt(pt)?.Visual;
        onSessionPointerMoved = pt => ShowEntry(FindEntryAt(pt));
        onSessionPointerExited = () => ShowEntry(null);

        foreach (var entry in marks)
        {
            entry.Visual.PointerEntered += (_, args) =>
            {
                if (args.Pointer.PointerDeviceType != PointerDeviceType.Touch)
                    ShowEntry(FindEntryAt(args.GetCurrentPoint(canvas).Position));
            };
        }
    }

    private void PlaceSessionReadout(
        Border readout,
        Rect anchor,
        double plotLeft,
        double plotRight,
        double plotTop,
        double plotBottom,
        double chartHeight)
    {
        const double gap = 10;
        const double edgePad = 4;
        double leftPlotSpace = anchor.Left - plotLeft - gap;
        double leftSpace = anchor.Left - edgePad - gap;
        double rightSpace = ActualWidth - anchor.Right - edgePad - gap;

        if (Math.Max(leftSpace, rightSpace) >= 136)
        {
            bool placeRight = rightSpace >= 220 || rightSpace >= leftPlotSpace;
            double availableWidth = placeRight ? rightSpace : leftSpace;
            readout.MaxWidth = Math.Clamp(availableWidth, 120, Math.Min(256, ActualWidth - edgePad * 2));
            readout.Measure(new Size(readout.MaxWidth, chartHeight));
            var size = readout.DesiredSize;
            double x = placeRight ? anchor.Right + gap : anchor.Left - size.Width - gap;
            double y = Math.Clamp(anchor.Top + (anchor.Height - size.Height) / 2, 2, Math.Max(2, plotBottom - size.Height));
            Canvas.SetLeft(readout, Math.Clamp(x, edgePad, Math.Max(edgePad, ActualWidth - size.Width - edgePad)));
            Canvas.SetTop(readout, y);
            return;
        }

        // Narrow-window centered block: place cleanly above or below so the block stays uncovered and the title stays readable.
        readout.MaxWidth = Math.Clamp(ActualWidth - edgePad * 2, 120, 256);
        readout.Measure(new Size(readout.MaxWidth, chartHeight));
        var measured = readout.DesiredSize;
        double aboveSpace = anchor.Top - gap;
        double belowSpace = chartHeight - anchor.Bottom - gap;
        double yPos;
        if (aboveSpace >= measured.Height) yPos = anchor.Top - measured.Height - gap;
        else if (belowSpace >= measured.Height) yPos = anchor.Bottom + gap;
        else yPos = aboveSpace >= belowSpace ? 2 : Math.Max(2, chartHeight - measured.Height - 2);

        double xPos = Math.Clamp(
            anchor.Left + (anchor.Width - measured.Width) / 2,
            edgePad,
            Math.Max(edgePad, ActualWidth - measured.Width - edgePad));
        Canvas.SetLeft(readout, xPos);
        Canvas.SetTop(readout, Math.Clamp(yPos, 2, Math.Max(2, chartHeight - measured.Height - 2)));
    }

    internal static string FormatSessionRange(StatisticsSessionSpan span, bool includeDate)
    {
        var start = span.ActualStartLocal;
        var end = span.ActualEndLocal;
        bool endsAtExactMidnight = end.TimeOfDay == TimeSpan.Zero && end.Date > start.Date;
        string startClock = FormatLocalClock(start);
        string endClock = endsAtExactMidnight ? "24:00" : FormatLocalClock(end);

        if (includeDate)
        {
            if (!span.CrossesMidnight)
                return $"{start:M/d} {startClock}–{endClock}";
            var endDisplayDate = endsAtExactMidnight ? end.AddDays(-1) : end;
            return $"{start:M/d} {startClock}–{endDisplayDate:M/d} {endClock}";
        }

        if (!span.CrossesMidnight)
            return $"{startClock}–{endClock}";

        int dayDiff = DateOnly.FromDateTime(end.AddTicks(-1)).DayNumber - DateOnly.FromDateTime(start).DayNumber;
        if (dayDiff == 1)
            return $"{startClock}–次日 {endClock}";

        var multiEndDate = endsAtExactMidnight ? end.AddDays(-1) : end;
        return $"{start:M/d} {startClock}–{multiEndDate:M/d} {endClock}";
    }

    private static string SessionAccessibleText(StatisticsSessionSpan span, bool multiDay)
        => $"{span.GameName}  {FormatSessionRange(span, multiDay)}";

    private static string FormatLocalClock(DateTime local)
        => local.Second == 0
            ? local.ToString("HH:mm", CultureInfo.InvariantCulture)
            : local.ToString("HH:mm:ss", CultureInfo.InvariantCulture);

    private static string ClockText(int seconds)
        => seconds % 60 == 0
            ? $"{seconds / 3600:00}:{seconds / 60 % 60:00}"
            : $"{seconds / 3600:00}:{seconds / 60 % 60:00}:{seconds % 60:00}";
}
