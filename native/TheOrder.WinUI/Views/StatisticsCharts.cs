using System.Collections.Immutable;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using TheOrder.Application;
using TheOrder.Presentation;
using Windows.Foundation;
using Windows.UI;
using Path = Microsoft.UI.Xaml.Shapes.Path;
using Colors = Microsoft.UI.Colors;

namespace TheOrder.WinUI.Views;

// Native vector charts: no browser, telemetry, image allocations or chart package.
// Every value also has a keyboard-focusable target and a readable data alternative.
public sealed class StatisticsTrendChart : Grid
{
    private readonly Canvas canvas = new();
    private ImmutableArray<StatisticsBucket> data = [];
    public static readonly DependencyProperty AccentProperty = BrushProperty(nameof(Accent));
    public static readonly DependencyProperty GridlineProperty = BrushProperty(nameof(Gridline));
    public static readonly DependencyProperty LabelBrushProperty = BrushProperty(nameof(LabelBrush));
    public Brush? Accent { get => (Brush?)GetValue(AccentProperty); set => SetValue(AccentProperty, value); }
    public Brush? Gridline { get => (Brush?)GetValue(GridlineProperty); set => SetValue(GridlineProperty, value); }
    public Brush? LabelBrush { get => (Brush?)GetValue(LabelBrushProperty); set => SetValue(LabelBrushProperty, value); }
    public bool Bars { get; set; }
    private static DependencyProperty BrushProperty(string name) => DependencyProperty.Register(name, typeof(Brush), typeof(StatisticsTrendChart), new PropertyMetadata(null, (d, _) => ((StatisticsTrendChart)d).Draw()));
    public StatisticsTrendChart() { Children.Add(canvas); SizeChanged += (_, _) => Draw(); }
    public void SetData(ImmutableArray<StatisticsBucket> value) { data = value; Draw(); }
    private void Draw()
    {
        canvas.Children.Clear(); double width = ActualWidth, height = ActualHeight;
        if (!double.IsFinite(width) || width < 90 || height < 90 || data.IsEmpty) return;
        double left = 48, top = 8, bottom = height - 46, right = width - 12;
        bool empty = data.All(x => x.Seconds == 0);
        var scale = StatisticsChartScale.For(data.Select(x => x.Seconds)); double high = scale.Maximum, low = scale.Minimum;
        double range = high - low;
        double Y(double value) => bottom - (value - low) / range * (bottom - top);
        double X(int i) => Bars ? left + (i + .5) * (right - left) / data.Length : data.Length == 1 ? (left + right) / 2 : left + i * (right - left) / (data.Length - 1);
        var caption = new TextBlock { FontSize = 12, Foreground = LabelBrush, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = width - left };
        Position(caption, left, height - 17);
        for (int row = 0; row <= (empty ? 0 : (int)Math.Round(range / scale.Step)); row++)
        {
            double value = low + scale.Step * row, y = Y(value);
            canvas.Children.Add(new Line { X1 = left, X2 = right, Y1 = y, Y2 = y, Stroke = Gridline, StrokeThickness = 1 });
            Position(new TextBlock { Text = $"{value / scale.Divisor:0.##}{scale.Unit}", Width = 44, TextTrimming = TextTrimming.CharacterEllipsis, Foreground = LabelBrush, FontSize = 11 }, 0, y - 7);
        }
        if (empty)
        {
            var message = new TextBlock { Text = "当前区间暂无可归日的游玩时长", Foreground = LabelBrush, TextWrapping = TextWrapping.Wrap, MaxWidth = right - left, FontSize = 14 };
            Position(message, left + 8, (top + bottom) / 2 - 14);
        }
        else if (!Bars)
        {
            var area = new PathFigure { StartPoint = new(X(0), Y(0)), IsClosed = true };
            var line = new Polyline { Stroke = Accent, StrokeThickness = 2.5 };
            for (int i = 0; i < data.Length; i++) { var point = new Point(X(i), Y(data[i].Seconds)); line.Points.Add(point); area.Segments.Add(new LineSegment { Point = point }); }
            area.Segments.Add(new LineSegment { Point = new(X(data.Length - 1), Y(0)) });
            var geometry = new PathGeometry(); geometry.Figures.Add(area);
            canvas.Children.Add(new Path { Data = geometry, Fill = Accent, Opacity = .09, IsHitTestVisible = false });
            canvas.Children.Add(line);
        }
        int labelStep = Math.Max(1, (int)Math.Ceiling(data.Length / Math.Max(2, (right - left) / 70)));
        for (int i = 0; i < data.Length; i++)
        {
            var row = data[i]; var x = X(i); var y = Y(row.Seconds);
            if (!empty && Bars && row.Seconds != 0)
                Position(new Border { Width = Math.Min(36, (right - left) / data.Length * .55), Height = Math.Abs(y - Y(0)), Background = Accent, CornerRadius = new(3) }, x - Math.Min(36, (right - left) / data.Length * .55) / 2, Math.Min(y, Y(0)));
            if (!Bars && !empty && row.Seconds != 0) Position(new Ellipse { Width = 6, Height = 6, Fill = Accent }, x - 3, y - 3);
            string text = (row.Start == row.End ? row.Start.ToString("yyyy-MM-dd") : $"{row.Start:yyyy-MM-dd} — {row.End:yyyy-MM-dd}") + $" · {StatisticsPage.Hours(row.Seconds)}";
            if (Bars) text = row.Label + $" · {StatisticsPage.Hours(row.Seconds)}";
            double targetWidth = Math.Min(24, (right - left) / Math.Max(1, data.Length));
            var target = new Button { Width = targetWidth, MinWidth = 0, MinHeight = 0, Height = Math.Max(24, Bars ? bottom - top : 24), Padding = new(0), Background = new SolidColorBrush(Colors.Transparent), BorderThickness = new(0), Style = (Style)Microsoft.UI.Xaml.Application.Current.Resources["SubtleButtonStyle"] };
            AutomationProperties.SetName(target, text); ToolTipService.SetToolTip(target, text);
            target.GotFocus += (_, _) => caption.Text = text; target.PointerEntered += (_, _) => caption.Text = text; target.Click += (_, _) => caption.Text = text;
            Position(target, x - targetWidth / 2, Bars ? top : Math.Clamp(y - 12, top, bottom - 12));
            if (i % labelStep == 0 || i == data.Length - 1)
            {
                // The final label is right-aligned so long dates cannot extend outside the card.
                double labelWidth = Math.Min(64, right - left);
                Position(new TextBlock { Text = row.Label, Width = labelWidth, Foreground = LabelBrush, FontSize = 11, TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis }, Math.Clamp(x - labelWidth / 2, left - 8, right - labelWidth + 8), bottom + 9);
            }
        }
        AutomationProperties.SetName(this, $"{(Bars ? "星期时长" : "游玩趋势")}，{data.Length} 个区间，可用 Tab 查看数值。");
    }
    private void Position(UIElement element, double x, double y) { Canvas.SetLeft(element, x); Canvas.SetTop(element, y); canvas.Children.Add(element); }
}

public sealed class StatisticsStatusChart : Grid
{
    private readonly Canvas canvas = new();
    private ImmutableArray<StatisticsStatus> data = [];
    public static readonly DependencyProperty TrackProperty = DependencyProperty.Register(nameof(Track), typeof(Brush), typeof(StatisticsStatusChart), new PropertyMetadata(null, Changed));
    public static readonly DependencyProperty LabelBrushProperty = DependencyProperty.Register(nameof(LabelBrush), typeof(Brush), typeof(StatisticsStatusChart), new PropertyMetadata(null, Changed));
    public Brush? Track { get => (Brush?)GetValue(TrackProperty); set => SetValue(TrackProperty, value); }
    public Brush? LabelBrush { get => (Brush?)GetValue(LabelBrushProperty); set => SetValue(LabelBrushProperty, value); }
    private static void Changed(DependencyObject value, DependencyPropertyChangedEventArgs _) => ((StatisticsStatusChart)value).Draw();
    public StatisticsStatusChart() { Children.Add(canvas); SizeChanged += (_, _) => Draw(); ActualThemeChanged += (_, _) => Draw(); }
    public void SetData(ImmutableArray<StatisticsStatus> value) { data = value; Draw(); }
    public Brush ColorFor(int index)
    {
        if (new Windows.UI.ViewManagement.AccessibilitySettings().HighContrast) return LabelBrush ?? new SolidColorBrush(Colors.White);
        Color[] colors = [Color.FromArgb(255, 128, 140, 157), Color.FromArgb(255, 66, 153, 225), Color.FromArgb(255, 24, 165, 139), Color.FromArgb(255, 213, 151, 46), Color.FromArgb(255, 198, 104, 127)];
        return new SolidColorBrush(colors[index % colors.Length]);
    }
    private void Draw()
    {
        canvas.Children.Clear(); double size = Math.Min(ActualWidth, ActualHeight); if (!double.IsFinite(size) || size < 30) return;
        double r = size / 2 - 11, center = size / 2;
        var track = new Ellipse { Width = r * 2, Height = r * 2, Stroke = Track, StrokeThickness = 15 };
        Canvas.SetLeft(track, 11); Canvas.SetTop(track, 11); canvas.Children.Add(track);
        int total = data.Sum(x => x.Count); double angle = -Math.PI / 2;
        for (int i = 0; i < data.Length && total > 0; i++)
        {
            var row = data[i]; if (row.Count == 0) continue;
            double sweep = (double)row.Count / total * Math.Tau;
            if (row.Count == total) { track.Stroke = ColorFor(i); break; }
            Point At(double value) => new(center + Math.Cos(value) * r, center + Math.Sin(value) * r);
            double gap = Math.Min(.025, sweep / 4);
            var figure = new PathFigure { StartPoint = At(angle + gap) };
            figure.Segments.Add(new ArcSegment { Point = At(angle + sweep - gap), Size = new(r, r), IsLargeArc = sweep - gap * 2 > Math.PI, SweepDirection = SweepDirection.Clockwise });
            var geometry = new PathGeometry(); geometry.Figures.Add(figure);
            canvas.Children.Add(new Path { Data = geometry, Stroke = ColorFor(i), StrokeThickness = 15, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round });
            angle += sweep;
        }
        AutomationProperties.SetName(this, "书架状态：" + string.Join("，", data.Select(x => $"{x.Name} {x.Count} 部")));
    }
}
