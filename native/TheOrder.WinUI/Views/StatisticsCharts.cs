using System.Collections.Immutable;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Shapes;
using TheOrder.Application;
using TheOrder.Presentation;
using Windows.Foundation;
using Windows.UI;
using Windows.UI.ViewManagement;
using Path = Microsoft.UI.Xaml.Shapes.Path;

namespace TheOrder.WinUI.Views;

// Play a finite reveal when fresh marks reach the viewport. Resizing and scrolling
// back to marks already seen keep the finished chart in place.
internal sealed class StatisticsChartMotion
{
    private readonly FrameworkElement chart;
    private Rect viewport;
    private bool pending;
    private Func<Storyboard>? prepare;
    private Storyboard? playing;

    internal StatisticsChartMotion(FrameworkElement chart)
    {
        this.chart = chart;
        chart.EffectiveViewportChanged += (_, args) => { viewport = args.EffectiveViewport; TryShow(); };
        chart.Unloaded += (_, _) =>
        {
            viewport = default;
            playing?.Stop();
            playing = null;
        };
    }

    internal void DataChanged()
    {
        pending = true;
        prepare = null;
    }

    internal void Prepared(Func<Storyboard>? reveal)
    {
        playing?.Stop();
        playing = null;
        prepare = pending ? reveal : null;
        TryShow();
    }

    private void TryShow()
    {
        if (prepare is not { } reveal || !chart.IsLoaded || viewport.Width <= 0
            || !(Math.Min(viewport.Bottom, chart.ActualHeight) - Math.Max(viewport.Top, 0) >= 32)) return;
        prepare = null;
        pending = false;
        if (!InteractionMotion.Enabled) return;
        try
        {
            playing = reveal();
            playing.Begin();
        }
        catch (Exception error) { App.TraceVisualFailure(error); }
    }

    internal static void Add(Storyboard sequence, DependencyObject target, string property,
        double from, double to, int milliseconds, bool dependent = false)
    {
        var animation = new DoubleAnimation
        {
            From = from, To = to, Duration = TimeSpan.FromMilliseconds(milliseconds), FillBehavior = FillBehavior.Stop,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, EnableDependentAnimation = dependent
        };
        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, property);
        sequence.Children.Add(animation);
    }
}

// Charts are vectors. The same time buckets drive the shapes and keyboard-accessible readouts.
public sealed partial class StatisticsTrendChart : Grid
{
    private readonly Canvas canvas = new();
    private readonly StatisticsChartMotion motion;
    private ImmutableArray<StatisticsBucket> data = [];
    private SolidColorBrush? observedAccent;
    private long accentToken;
    private GradientStop[] areaStops = [];
    private bool highContrast = new AccessibilitySettings().HighContrast;
    public static readonly DependencyProperty AccentProperty = BrushProperty(nameof(Accent));
    public static readonly DependencyProperty GridlineProperty = BrushProperty(nameof(Gridline));
    public static readonly DependencyProperty LabelBrushProperty = BrushProperty(nameof(LabelBrush));
    public static readonly DependencyProperty InkProperty = BrushProperty(nameof(Ink));
    public static readonly DependencyProperty ReadoutBackgroundProperty = BrushProperty(nameof(ReadoutBackground));
    public static readonly DependencyProperty ReadoutBorderProperty = BrushProperty(nameof(ReadoutBorder));

    public Brush? Accent { get => (Brush?)GetValue(AccentProperty); set => SetValue(AccentProperty, value); }
    public Brush? Gridline { get => (Brush?)GetValue(GridlineProperty); set => SetValue(GridlineProperty, value); }
    public Brush? LabelBrush { get => (Brush?)GetValue(LabelBrushProperty); set => SetValue(LabelBrushProperty, value); }
    public Brush? Ink { get => (Brush?)GetValue(InkProperty); set => SetValue(InkProperty, value); }
    public Brush? ReadoutBackground { get => (Brush?)GetValue(ReadoutBackgroundProperty); set => SetValue(ReadoutBackgroundProperty, value); }
    public Brush? ReadoutBorder { get => (Brush?)GetValue(ReadoutBorderProperty); set => SetValue(ReadoutBorderProperty, value); }
    public Style? TargetStyle { get; set; }
    public bool Bars { get; set; }
    public bool SessionPeriods { get; set; }

    private static DependencyProperty BrushProperty(string name) => DependencyProperty.Register(
        name, typeof(Brush), typeof(StatisticsTrendChart), new PropertyMetadata(null, (d, _) => ((StatisticsTrendChart)d).Draw()));

    public StatisticsTrendChart()
    {
        motion = new(this);
        Children.Add(canvas);
        SizeChanged += (_, _) => Draw();
        ActualThemeChanged += (_, _) =>
        {
            if (SessionPeriods) UpdateSessionPalette();
            if (Bars || SessionPeriods) Draw();
        };
        Loaded += (_, _) => ObserveAccent(Accent as SolidColorBrush);
        Unloaded += (_, _) =>
        {
            ObserveAccent(null);
            ShowSessionMark(null);
        };
        canvas.PointerMoved += (_, args) =>
        {
            if (args.Pointer.PointerDeviceType != Microsoft.UI.Input.PointerDeviceType.Touch)
                onSessionPointerMoved?.Invoke(args.GetCurrentPoint(canvas).Position);
        };
        canvas.PointerExited += (_, _) => onSessionPointerExited?.Invoke();
    }

    internal void SetHighContrast(bool value)
    {
        if (highContrast == value) return;
        highContrast = value;
        if (SessionPeriods) UpdateSessionPalette();
        Draw();
    }

    public void SetData(ImmutableArray<StatisticsBucket> value)
    {
        if (data.SequenceEqual(value)) return;
        data = value;
        motion.DataChanged();
        Draw();
    }

    private void ObserveAccent(SolidColorBrush? accent)
    {
        if (ReferenceEquals(accent, observedAccent)) return;
        observedAccent?.UnregisterPropertyChangedCallback(SolidColorBrush.ColorProperty, accentToken);
        observedAccent = accent;
        if (accent is not null)
            accentToken = accent.RegisterPropertyChangedCallback(SolidColorBrush.ColorProperty, (_, _) =>
            {
                if (Bars) Draw();
                else UpdateAreaColors();
            });
    }

    private void UpdateAreaColors()
    {
        if (Accent is not SolidColorBrush accent || areaStops.Length == 0) return;
        var color = accent.Color;
        areaStops[0].Color = Color.FromArgb(66, color.R, color.G, color.B);
        areaStops[1].Color = Color.FromArgb(0, color.R, color.G, color.B);
        areaStops[2].Color = areaStops[0].Color;
    }

    private void Draw()
    {
        motion.Prepared(null);
        showSessionMark = null;
        hitTestSessionPoint = null;
        onSessionPointerMoved = null;
        onSessionPointerExited = null;
        canvas.Children.Clear();
        canvas.Background = SessionPeriods ? new SolidColorBrush(Color.FromArgb(0, 0, 0, 0)) : null;
        areaStops = [];
        double width = ActualWidth, height = ActualHeight;
        if (!double.IsFinite(width) || width < 100 || height < 90) return;
        if (SessionPeriods) { DrawSessionPeriods(width, height); return; }
        if (data.IsEmpty) return;
        if (IsLoaded) ObserveAccent(Accent as SolidColorBrush);

        var scale = StatisticsChartScale.For(data.Select(x => x.Seconds));
        double range = scale.Maximum - scale.Minimum;
        bool empty = data.All(x => x.Seconds == 0);
        int intervals = (int)Math.Round(range / scale.Step);
        var ticks = empty ? new[] { 0d } : Enumerable.Range(0, intervals + 1)
            .Select(i => scale.Minimum + i * scale.Step).ToArray();

        // Measure the scale so a signed value or a larger unit never steals space from the plot.
        var tickLabels = ticks.Select(value => AxisLabel(TickText(value, scale))).ToArray();
        foreach (var label in tickLabels) label.Measure(new Size(88, 32));
        double axisWidth = tickLabels.Max(x => x.DesiredSize.Width);
        double xLabelWidth = data.Any(x => x.Label.Length > 5) ? 86 : 52;
        double left = Math.Max(axisWidth + 13, Bars ? 0 : xLabelWidth / 2);
        double right = width - (Bars ? 6 : xLabelWidth / 2);
        if (right - left < 48) return;
        double top = 12, bottom = height - 24;
        double slot = (right - left) / data.Length;
        double Y(double seconds) => bottom - (seconds - scale.Minimum) / range * (bottom - top);
        double X(int index) => Bars ? left + (index + .5) * slot : data.Length == 1
            ? (left + right) / 2 : left + index * (right - left) / (data.Length - 1);
        double zero = Y(0);
        var points = data.Select((bucket, i) => new Point(X(i), Y(bucket.Seconds))).ToArray();

        for (int i = 0; i < ticks.Length; i++)
        {
            var y = Y(ticks[i]);
            bool baseline = Math.Abs(y - zero) < .01;
            var guide = new Line
            {
                X1 = left, X2 = Bars && !baseline && !highContrast ? left + 4 : right, Y1 = y, Y2 = y,
                Stroke = baseline ? LabelBrush : Gridline, StrokeThickness = 1,
                Opacity = highContrast ? 1 : baseline ? .44 : .68
            };
            if (!Bars && !baseline && !highContrast) guide.StrokeDashArray = new DoubleCollection { 3, 4 };
            canvas.Children.Add(guide);
            var label = tickLabels[i];
            label.Width = axisWidth;
            label.TextAlignment = TextAlignment.Right;
            Position(label, 0, y - label.DesiredSize.Height / 2);
        }

        int peakIndex = 0;
        for (int i = 1; i < data.Length; i++)
            if (Math.Abs((double)data[i].Seconds) > Math.Abs((double)data[peakIndex].Seconds)) peakIndex = i;

        if (empty)
        {
            Position(new TextBlock
            {
                Text = "当前区间暂无游玩时长", Width = right - left, TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap, Foreground = LabelBrush, FontSize = 13
            }, left, (top + bottom) / 2 - 9);
        }
        else if (!Bars)
        {
            var series = new Canvas { Width = width, Height = height, IsHitTestVisible = false };
            // A curve that touches zero still has a centered stroke. Clip its paint on the
            // one-sided axis, while keeping the whole range when real data crosses zero.
            double clipTop = scale.Maximum == 0 ? zero : 0;
            double clipBottom = scale.Minimum == 0 ? zero : height;
            var plotClip = new RectangleGeometry { Rect = new Rect(0, clipTop, width, clipBottom - clipTop) };
            series.Clip = plotClip;
            canvas.Children.Add(series);
            if (points.Length > 1) DrawLine(series, points, zero, highContrast);
            var peak = new Grid { Width = 16, Height = 16 };
            if (!highContrast) peak.Children.Add(new Ellipse { Fill = Accent, Opacity = .18 });
            peak.Children.Add(new Ellipse { Width = 9, Height = 9, Fill = Accent, Stroke = ReadoutBackground, StrokeThickness = 1.5 });
            Canvas.SetLeft(peak, points[peakIndex].X - peak.Width / 2);
            Canvas.SetTop(peak, points[peakIndex].Y - peak.Height / 2);
            series.Children.Add(peak);
            if (!highContrast) motion.Prepared(() =>
            {
                var sweep = new ScaleTransform { CenterX = points[0].X };
                plotClip.Transform = sweep;
                var sequence = new Storyboard();
                StatisticsChartMotion.Add(sequence, sweep, nameof(ScaleTransform.ScaleX), .015, 1, 310, dependent: true);
                return sequence;
            });
        }
        else
        {
            DrawBars(points, zero, Math.Min(42, slot * .48), Math.Abs((double)data[peakIndex].Seconds));
        }

        int last = data.Length - 1;
        int labelStep = Math.Max(1, (int)Math.Ceiling(last * (xLabelWidth + 8) / (right - left)));
        for (int i = 0; i < data.Length; i++)
        {
            if (!Bars && i != 0 && i != last && (i % labelStep != 0 || i + labelStep > last)) continue;
            var label = AxisLabel(data[i].Label);
            label.Width = Bars ? slot : xLabelWidth;
            label.TextAlignment = TextAlignment.Center;
            Position(label, points[i].X - label.Width / 2, bottom + 6);
        }
        AddReadouts(points, left, top, right, bottom, highContrast);
        AutomationProperties.SetName(this, $"{(Bars ? "星期时长" : "游玩趋势")}，{data.Length} 个区间，可用 Tab 查看数值。");
    }

    private void DrawBars(Point[] points, double zero, double width, double peak)
    {
        var material = !highContrast && Accent is SolidColorBrush accent ? JellyBarMaterial(accent.Color) :
            ((Brush Body, Brush Depth, Brush Rim, Color Caustic, Brush Highlight)?)null;
        var marks = new List<(Border Shape, bool Positive)>();
        for (int i = 0; i < points.Length; i++)
        {
            long seconds = data[i].Seconds;
            if (seconds == 0) continue;
            var point = points[i];
            double height = Math.Abs(point.Y - zero);
            double radius = Math.Min(6, Math.Min(width * .22, height * .34));
            bool positive = seconds > 0;
            var bar = new Border
            {
                Width = width, Height = height, IsHitTestVisible = false,
                Opacity = highContrast ? 1 : .74 + .26 * Math.Abs((double)seconds) / peak
            };
            if (material is { } brushes && width >= 10 && height >= 5)
            {
                bar.Child = CreateJellyBar(width, height, radius, positive, brushes);
            }
            else
            {
                bar.Background = Accent;
                bar.CornerRadius = positive ? new(radius, radius, 0, 0) : new(0, 0, radius, radius);
            }
            Position(bar, point.X - width / 2, Math.Min(point.Y, zero));
            marks.Add((bar, positive));
        }
        if (!highContrast) motion.Prepared(() =>
        {
            var sequence = new Storyboard();
            foreach (var (bar, positive) in marks)
            {
                var growth = new ScaleTransform { CenterX = bar.Width / 2, CenterY = positive ? bar.Height : 0 };
                bar.RenderTransform = growth;
                StatisticsChartMotion.Add(sequence, growth, nameof(ScaleTransform.ScaleY), .06, 1, 280);
            }
            return sequence;
        });
    }

    private (Brush Body, Brush Depth, Brush Rim, Color Caustic, Brush Highlight) JellyBarMaterial(Color accent)
    {
        bool light = ActualTheme == ElementTheme.Light;
        var tint = Mix(accent, Color.FromArgb(255, 59, 220, 235), light ? .48 : .35);
        var shade = Mix(tint, Color.FromArgb(255, 24, 100, 148), light ? .28 : .38);
        var shine = Mix(tint, Color.FromArgb(255, 224, 255, 251), .75);
        var body = Gradient(new(0, .5), new(1, .5),
            (0, WithAlpha(shade, 207)), (.09, WithAlpha(tint, 199)), (.25, WithAlpha(tint, 146)),
            (.55, WithAlpha(tint, light ? (byte)87 : (byte)122)), (.78, WithAlpha(tint, 158)),
            (.94, WithAlpha(shade, 207)), (1, WithAlpha(tint, 174)));
        var depth = Gradient(new(.5, 0), new(.5, 1),
            (0, WithAlpha(tint, 88)), (.19, WithAlpha(tint, 0)),
            (.65, WithAlpha(shade, 0)), (1, WithAlpha(shade, 85)));
        var rim = Gradient(new(.08, 0), new(.92, 1),
            (0, WithAlpha(light ? tint : shine, 163)), (.32, WithAlpha(tint, 96)),
            (.67, WithAlpha(shade, 118)), (1, WithAlpha(tint, 164)));
        var highlight = Gradient(new(0, .5), new(1, .5),
            (0, WithAlpha(shine, 36)), (.28, WithAlpha(shine, light ? (byte)248 : (byte)230)),
            (.62, WithAlpha(shine, 124)), (1, WithAlpha(shine, 0)));
        var caustic = Mix(tint, Color.FromArgb(255, 101, 252, 245), light ? .66 : .58);
        return (body, depth, rim, caustic, highlight);

        static Color Mix(Color from, Color to, double weight)
        {
            byte Channel(byte a, byte b) => (byte)Math.Round(a + (b - a) * weight);
            return Color.FromArgb(255, Channel(from.R, to.R), Channel(from.G, to.G), Channel(from.B, to.B));
        }

        static LinearGradientBrush Gradient(Point start, Point end, params (double Offset, Color Color)[] stops)
        {
            var brush = new LinearGradientBrush { StartPoint = start, EndPoint = end };
            foreach (var (offset, color) in stops) brush.GradientStops.Add(new() { Offset = offset, Color = color });
            return brush;
        }
    }

    private static Color WithAlpha(Color color, byte alpha) => Color.FromArgb(alpha, color.R, color.G, color.B);

    private static Grid CreateJellyBar(double width, double height, double radius, bool positive,
        (Brush Body, Brush Depth, Brush Rim, Color Caustic, Brush Highlight) material)
    {
        // Keep a flat value edge and scale the reflections within the measured bar height.
        double foot = Math.Min(2, height * .14);
        var corners = new CornerRadius(radius, radius, foot, foot);
        double glowHeight = Math.Min(width * .34, height * .45) / height;
        var center = new Point(.71, 1 - glowHeight * .45);
        var light = material.Caustic;
        var caustic = new RadialGradientBrush
        {
            Center = center, GradientOrigin = center, RadiusX = .59, RadiusY = glowHeight,
            GradientStops =
            {
                new() { Offset = 0, Color = WithAlpha(light, 181) },
                new() { Offset = .34, Color = WithAlpha(light, 121) },
                new() { Offset = .72, Color = WithAlpha(light, 38) },
                new() { Offset = 1, Color = WithAlpha(light, 0) }
            }
        };
        double inset = Math.Min(1.9, height * .16);
        var gleam = new PathFigure { StartPoint = new(inset, inset + radius) };
        gleam.Segments.Add(new BezierSegment
        {
            Point1 = new(inset, inset + radius * .45), Point2 = new(inset + radius * .45, inset),
            Point3 = new(inset + radius, inset)
        });
        gleam.Segments.Add(new LineSegment { Point = new(width * .76, inset) });
        var surface = new Grid
        {
            Children =
            {
                new Border { CornerRadius = corners, Background = material.Body },
                new Border { CornerRadius = corners, Background = material.Depth },
                new Border { CornerRadius = corners, Background = caustic, BorderBrush = material.Rim, BorderThickness = new(.7) },
                new Path
                {
                    Data = new PathGeometry { Figures = { gleam } }, Stroke = material.Highlight,
                    StrokeThickness = Math.Min(.95, height * .12), StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round
                }
            }
        };
        if (!positive) surface.RenderTransform = new ScaleTransform { CenterY = height / 2, ScaleY = -1 };
        return surface;
    }

    private void DrawLine(Canvas series, Point[] points, double zero, bool highContrast)
    {
        // Tangents are in per-bucket units because the plotted dates are equally spaced.
        // The harmonic mean flattens extrema and keeps every segment within its two values.
        var tangents = new double[points.Length];
        tangents[0] = points[1].Y - points[0].Y;
        tangents[^1] = points[^1].Y - points[^2].Y;
        for (int i = 1; i < points.Length - 1; i++)
        {
            double incoming = points[i].Y - points[i - 1].Y;
            double outgoing = points[i + 1].Y - points[i].Y;
            if (incoming * outgoing > 0) tangents[i] = 2 * incoming * outgoing / (incoming + outgoing);
        }

        if (!highContrast && Accent is SolidColorBrush)
        {
            // Fade toward the actual zero, even when adjustments put part of the chart below it.
            var area = new PathFigure { StartPoint = new(points[0].X, zero), IsClosed = true };
            area.Segments.Add(new LineSegment { Point = points[0] });
            AppendCurve(area, points, tangents);
            area.Segments.Add(new LineSegment { Point = new(points[^1].X, zero) });
            var gradient = new LinearGradientBrush { StartPoint = new(0, 0), EndPoint = new(0, 1) };
            double top = Math.Min(zero, points.Min(x => x.Y)), bottom = Math.Max(zero, points.Max(x => x.Y));
            areaStops = [new() { Offset = 0 }, new() { Offset = (zero - top) / (bottom - top) }, new() { Offset = 1 }];
            UpdateAreaColors();
            // For one-sided data the transparent stop replaces the corresponding edge stop.
            foreach (var stop in areaStops)
                if (stop == areaStops[1] || stop.Offset != areaStops[1].Offset) gradient.GradientStops.Add(stop);
            var areaGeometry = new PathGeometry();
            areaGeometry.Figures.Add(area);
            series.Children.Add(new Path { Data = areaGeometry, Fill = gradient });
        }

        var curve = new PathFigure { StartPoint = points[0] };
        AppendCurve(curve, points, tangents);
        var geometry = new PathGeometry { Figures = { curve } };
        series.Children.Add(new Path
        {
            Data = geometry, Stroke = Accent, StrokeThickness = 2.5,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round
        });
    }

    private static void AppendCurve(PathFigure figure, Point[] points, double[] tangents)
    {
        for (int i = 1; i < points.Length; i++)
        {
            var start = points[i - 1];
            var end = points[i];
            double handle = (end.X - start.X) / 3;
            figure.Segments.Add(new BezierSegment
            {
                Point1 = new(start.X + handle, start.Y + tangents[i - 1] / 3),
                Point2 = new(end.X - handle, end.Y - tangents[i] / 3),
                Point3 = end
            });
        }
    }

    private void AddReadouts(Point[] points, double left, double top, double right, double bottom, bool highContrast)
    {
        var guide = Bars ? null : new Line
        {
            Stroke = LabelBrush, StrokeThickness = 1, Opacity = highContrast ? 1 : .55, IsHitTestVisible = false
        };
        var marker = Bars ? null : new Ellipse
        {
            Width = 9, Height = 9, Fill = Accent, Stroke = ReadoutBackground,
            StrokeThickness = 1.5, IsHitTestVisible = false
        };
        var column = Bars ? new Border
        {
            Width = (right - left) / points.Length * .88, Height = bottom - top, CornerRadius = new(5),
            Background = highContrast ? null : Ink, BorderBrush = highContrast ? LabelBrush : null,
            BorderThickness = new(highContrast ? 1 : 0), Opacity = highContrast ? 1 : .075, IsHitTestVisible = false
        } : null;
        if (column is not null)
        {
            Canvas.SetTop(column, top);
            canvas.Children.Insert(0, column);
        }
        var date = AxisLabel("");
        var value = CreateReadoutValue();
        Typography.SetNumeralAlignment(value, FontNumeralAlignment.Tabular);
        var readout = CreateReadout(date, value);

        int? focused = null, hovered = null;
        void Show(int? index)
        {
            var visibility = index.HasValue ? Visibility.Visible : Visibility.Collapsed;
            readout.Visibility = visibility;
            if (column is not null) column.Visibility = visibility;
            if (guide is not null && marker is not null) guide.Visibility = marker.Visibility = visibility;
            if (index is not { } i) return;
            var point = points[i];
            if (column is not null) Canvas.SetLeft(column, point.X - column.Width / 2);
            if (guide is not null && marker is not null)
            {
                guide.X1 = guide.X2 = point.X;
                guide.Y1 = top;
                guide.Y2 = bottom;
                Canvas.SetLeft(marker, point.X - marker.Width / 2);
                Canvas.SetTop(marker, point.Y - marker.Height / 2);
            }
            date.Text = BucketDate(data[i]);
            value.Text = StatisticsPage.Hours(data[i].Seconds);
            PlaceReadout(readout, new Rect(point.X, point.Y, 0, 0), bottom);
        }

        for (int i = 0; i < points.Length; i++)
        {
            int index = i;
            double start = i == 0 ? left : (points[i - 1].X + points[i].X) / 2;
            double end = i == points.Length - 1 ? right : (points[i].X + points[i + 1].X) / 2;
            var target = new Button
            {
                Style = TargetStyle, Width = end - start, Height = bottom - top,
                MinWidth = 0, MinHeight = 0, Padding = new(0)
            };
            AutomationProperties.SetName(target, $"{BucketDate(data[i])} · {StatisticsPage.Hours(data[i].Seconds)}");
            target.GotFocus += (_, _) =>
            {
                focused = target.FocusState == FocusState.Keyboard ? index : null;
                Show(index);
            };
            target.LostFocus += (_, _) => { focused = null; Show(hovered); };
            target.PointerEntered += (_, _) => { hovered = index; Show(index); };
            target.PointerExited += (_, _) => { hovered = null; Show(focused); };
            target.Click += (_, _) => Show(index);
            Position(target, start, top);
        }

        Show(null);
        if (guide is not null && marker is not null)
        {
            canvas.Children.Add(guide);
            canvas.Children.Add(marker);
        }
        canvas.Children.Add(readout);
    }

    private TextBlock CreateReadoutValue() => new()
    {
        Foreground = Ink, FontSize = 14, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap
    };

    private Border CreateReadout(TextBlock caption, TextBlock value) => new()
    {
        Background = ReadoutBackground, BorderBrush = highContrast ? Ink : (ReadoutBorder ?? Gridline), BorderThickness = new(1),
        CornerRadius = new(8), Padding = new(11, 7, 11, 8), MaxWidth = Math.Min(248, ActualWidth - 4),
        Child = new StackPanel { Spacing = 2, Children = { caption, value } }, IsHitTestVisible = false
    };

    private void PlaceReadout(Border readout, Rect anchor, double bottom)
    {
        readout.Measure(new Size(readout.MaxWidth, bottom));
        var size = readout.DesiredSize;
        double x = anchor.Right + size.Width + 16 < ActualWidth ? anchor.Right + 13 : anchor.Left - size.Width - 13;
        Canvas.SetLeft(readout, Math.Clamp(x, 2, Math.Max(2, ActualWidth - size.Width - 2)));
        Canvas.SetTop(readout, Math.Clamp(anchor.Y + (anchor.Height - size.Height) / 2, 0, Math.Max(0, bottom - size.Height)));
    }

    private string BucketDate(StatisticsBucket bucket) => Bars ? bucket.Label
        : bucket.Start == bucket.End ? bucket.Start.ToString("yyyy-MM-dd") : $"{bucket.Start:yyyy-MM-dd} — {bucket.End:yyyy-MM-dd}";

    private TextBlock AxisLabel(string text)
    {
        var label = new TextBlock { Text = text, FontSize = 12, Foreground = LabelBrush, TextTrimming = TextTrimming.CharacterEllipsis };
        Typography.SetNumeralAlignment(label, FontNumeralAlignment.Tabular);
        return label;
    }

    private static string TickText(double value, StatisticsChartScale scale)
    {
        double units = value / scale.Divisor;
        return (Math.Abs(units) >= 100_000 ? $"{units:0.#E+0}" : $"{units:0}") + scale.Unit;
    }

    private void Position(UIElement element, double x, double y)
    {
        Canvas.SetLeft(element, x);
        Canvas.SetTop(element, y);
        canvas.Children.Add(element);
    }
}

public sealed class StatisticsStatusChart : Grid
{
    private const double StartAngle = -Math.PI / 2;
    private readonly Canvas canvas = new() { IsHitTestVisible = false };
    private readonly Canvas joins = new() { IsHitTestVisible = false };
    private readonly Canvas surface = new() { IsHitTestVisible = false };
    private readonly Canvas highlights = new() { IsHitTestVisible = false };
    private readonly Dictionary<string, Canvas> highlightArcs = new(StringComparer.Ordinal);
    private readonly List<(string Key, double Start, double End)> hitArcs = [];
    private double ringCenter, ringRadius, ringHalfThickness;
    private ImmutableArray<StatisticsStatus> data = [];
    private string? active;
    private bool highContrast;
    public static readonly DependencyProperty TrackProperty = DependencyProperty.Register(
        nameof(Track), typeof(Brush), typeof(StatisticsStatusChart), new PropertyMetadata(null, (d, _) => ((StatisticsStatusChart)d).Draw()));
    public static readonly DependencyProperty LabelBrushProperty = DependencyProperty.Register(
        nameof(LabelBrush), typeof(Brush), typeof(StatisticsStatusChart), new PropertyMetadata(null, (d, _) => ((StatisticsStatusChart)d).UpdatePalette()));

    public Brush? Track { get => (Brush?)GetValue(TrackProperty); set => SetValue(TrackProperty, value); }
    public Brush? LabelBrush { get => (Brush?)GetValue(LabelBrushProperty); set => SetValue(LabelBrushProperty, value); }
    internal event EventHandler? PaletteChanged;
    internal event Action<string>? HoverChanged;
    internal event Action<string>? StatusInvoked;

    public StatisticsStatusChart()
    {
        // The whole ring shares one pointer surface; its paths only draw the slices.
        Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        Children.Add(canvas);
        SizeChanged += (_, _) => Draw();
        ActualThemeChanged += (_, _) => UpdatePalette();
        PointerEntered += OnStatusPointer;
        PointerMoved += OnStatusPointer;
        Tapped += (_, args) =>
        {
            if (StatusAt(args.GetPosition(this)) is not { } key) return;
            args.Handled = true;
            StatusInvoked?.Invoke(key);
        };
    }

    private void OnStatusPointer(object sender, PointerRoutedEventArgs args)
    {
        if (args.Pointer.PointerDeviceType != Microsoft.UI.Input.PointerDeviceType.Touch)
            PreviewAt(args.GetCurrentPoint(this).Position);
    }

    internal void PreviewAt(Point point)
    {
        if (StatusAt(point) is { } key) HoverChanged?.Invoke(key);
    }

    internal string? StatusAt(Point point)
    {
        double x = point.X - ringCenter, y = point.Y - ringCenter;
        double distance = Math.Sqrt(x * x + y * y);
        if (Math.Abs(distance - ringRadius) > ringHalfThickness) return null;

        double angle = Math.Atan2(y, x);
        if (angle < StartAngle) angle += Math.Tau;
        foreach (var arc in hitArcs)
            if (angle >= arc.Start && angle < arc.End) return arc.Key;
        return null;
    }

    internal void SetHighContrast(bool value)
    {
        if (highContrast == value) return;
        highContrast = value;
        UpdatePalette();
    }

    internal bool SetData(ImmutableArray<StatisticsStatus> value)
    {
        if (data.SequenceEqual(value)) return false;
        data = value;
        Draw();
        return true;
    }

    internal void Highlight(string? key)
    {
        if (active == key) return;
        active = key;
        bool animate = InteractionMotion.Enabled;
        foreach (var (status, highlight) in highlightArcs)
        {
            highlight.OpacityTransition = animate
                ? highlight.OpacityTransition ?? new() { Duration = TimeSpan.FromMilliseconds(125) }
                : null;
            highlight.Opacity = key == status ? 1 : 0;
        }
    }

    internal Brush? ColorFor(string key) => highContrast ? LabelBrush : new SolidColorBrush(StatusColor(key));

    private Color StatusColor(string key)
    {
        (int light, int dark) = key switch
        {
            "completed" => (0x2E87FF, 0x5A6AF3),
            "playing" => (0x0AC484, 0x3CC486),
            "wish" => (0xFFC225, 0xE6C049),
            "paused" => (0xFF812C, 0xEB8A4E),
            "dropped" => (0xFF4965, 0xDD5873),
            _ => (0xA56BFF, 0x6944C4)
        };
        int rgb = ActualTheme == ElementTheme.Light ? light : dark;
        return Color.FromArgb(255, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
    }

    private void UpdatePalette()
    {
        Draw();
        PaletteChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Draw()
    {
        highlightArcs.Clear();
        hitArcs.Clear();
        canvas.Children.Clear();
        joins.Children.Clear();
        surface.Children.Clear();
        highlights.Children.Clear();
        double size = Math.Min(ActualWidth, ActualHeight);
        if (!double.IsFinite(size) || size < 30) return;

        double thickness = highContrast ? 11 : 14;
        double center = ringCenter = size / 2;
        double radius = ringRadius = center - thickness / 2 - 3;
        ringHalfThickness = thickness / 2;
        var slices = data.Where(row => row.Count > 0).ToArray();
        int total = slices.Sum(row => row.Count);
        double angle = StartAngle;
        if (total == 0)
            canvas.Children.Add(new Path { Data = Arc(center, radius, angle, Math.Tau), Stroke = Track, StrokeThickness = thickness });

        for (int i = 0; i < slices.Length; i++)
        {
            var row = slices[i];
            double end = i == slices.Length - 1 ? StartAngle + Math.Tau : angle + (double)row.Count / total * Math.Tau;
            double sweep = end - angle;
            double gap = highContrast && slices.Length > 1 ? Math.Min(1.65 / radius, sweep * .12) : 0;
            hitArcs.Add((row.Key, angle + gap, end - gap));
            canvas.Children.Add(new Path
            {
                Data = Arc(center, radius, angle + gap, sweep - 2 * gap),
                Stroke = ColorFor(row.Key), StrokeThickness = thickness
            });
            if (!highContrast)
            {
                DrawHighlight(row.Key, center, radius + thickness / 2 - 2.1, angle, sweep);
                if (slices.Length > 1)
                {
                    var previous = slices[(i + slices.Length - 1) % slices.Length];
                    double previousSweep = (double)previous.Count / total * Math.Tau;
                    // Leave most of even a short slice in its own color.
                    double span = Math.Min(9 / radius, Math.Min(sweep, previousSweep) * .22);
                    if (span * radius >= 1)
                        joins.Children.Add(GradientArc(center, radius, thickness, angle - span, 2 * span,
                            StatusColor(previous.Key), StatusColor(row.Key)));
                }
            }
            angle = end;
        }

        if (total > 0 && !highContrast)
        {
            DrawSurface(center, radius, thickness);
            canvas.Children.Add(joins);
            canvas.Children.Add(surface);
            canvas.Children.Add(highlights);
        }
        AutomationProperties.SetName(this, "书架状态：" + string.Join("，", data.Select(x => $"{x.Name} {x.Count} 部")));
    }

    private void DrawHighlight(string key, double center, double radius, double angle, double sweep)
    {
        // A separate sheen keeps the color joins and the surface intact during inspection.
        const double thickness = 1.55;
        double feather = sweep >= Math.Tau ? 0 : Math.Min(8 / radius, sweep / 3);
        var sheen = Color.FromArgb(ActualTheme == ElementTheme.Light ? (byte)164 : (byte)207, 255, 255, 255);
        var highlight = new Canvas { Opacity = active == key ? 1 : 0 };
        highlight.Children.Add(new Path
        {
            Data = Arc(center, radius, angle + feather, sweep - 2 * feather),
            Stroke = new SolidColorBrush(sheen), StrokeThickness = thickness
        });
        if (feather > 0)
        {
            var transparent = Color.FromArgb(0, 255, 255, 255);
            highlight.Children.Add(GradientArc(center, radius, thickness, angle, feather, transparent, sheen));
            highlight.Children.Add(GradientArc(center, radius, thickness, angle + sweep - feather, feather, sheen, transparent));
        }
        highlightArcs.Add(key, highlight);
        highlights.Children.Add(highlight);
    }

    private static Path GradientArc(double center, double radius, double thickness, double angle, double sweep, Color from, Color to)
    {
        // Rotate the brush with the stroke so the color runs along the local tangent.
        const double top = -Math.PI / 2;
        double halfSweep = sweep / 2;
        var brush = new LinearGradientBrush
        {
            StartPoint = new(0, .5), EndPoint = new(1, .5),
            GradientStops = { new() { Offset = .11, Color = from }, new() { Offset = .89, Color = to } }
        };
        return new Path
        {
            Data = Arc(center, radius, top - halfSweep, sweep),
            Stroke = brush, StrokeThickness = thickness,
            RenderTransform = new RotateTransform { CenterX = center, CenterY = center, Angle = (angle + halfSweep - top) * 180 / Math.PI }
        };
    }

    private void DrawSurface(double center, double radius, double thickness)
    {
        bool light = ActualTheme == ElementTheme.Light;
        var shade = new LinearGradientBrush
        {
            StartPoint = new(.22, .02), EndPoint = new(.87, .91),
            GradientStops =
            {
                new() { Offset = .22, Color = Color.FromArgb(0, 29, 41, 90) },
                new() { Offset = 1, Color = Color.FromArgb(light ? (byte)9 : (byte)23, 29, 41, 90) }
            }
        };
        // Keep the finish subtle even when one status spans most of the ring.
        var coolLight = SoftGlow(new(.67, .11), .52, .44,
            Color.FromArgb(light ? (byte)25 : (byte)36, 170, 186, 255));
        var pearl = SoftGlow(new(.10, .68), .40, .44,
            Color.FromArgb(light ? (byte)25 : (byte)40, 255, 236, 220));
        foreach (var brush in new Brush[] { shade, coolLight, pearl })
        {
            surface.Children.Add(new Path
            {
                Data = Arc(center, radius, 0, Math.Tau), Stroke = brush, StrokeThickness = thickness
            });
        }

        var scatter = new Random(307);
        var specks = new GeometryGroup();
        var glint = SoftGlow(new(.5, .5), .5, .5,
            Color.FromArgb(light ? (byte)178 : (byte)226, 255, 248, 255));
        int count = Math.Clamp((int)(Math.Tau * radius / 20), 6, 22);
        for (int i = 0; i < count; i++)
        {
            double position = (i + .12 + scatter.NextDouble() * .76) / count * Math.Tau;
            var point = At(center, radius + (scatter.NextDouble() - .5) * (thickness - 5), position);
            double variation = scatter.NextDouble();
            if (i % 4 == 1)
            {
                double glowRadius = 1.35 + variation * .65;
                var dot = new Ellipse { Width = 2 * glowRadius, Height = 2 * glowRadius, Fill = glint };
                Canvas.SetLeft(dot, point.X - glowRadius);
                Canvas.SetTop(dot, point.Y - glowRadius);
                surface.Children.Add(dot);
            }
            else
            {
                double dotRadius = .33 + variation * .31;
                specks.Children.Add(new EllipseGeometry { Center = point, RadiusX = dotRadius, RadiusY = dotRadius });
            }
        }
        surface.Children.Add(new Path
        {
            Data = specks, Fill = new SolidColorBrush(Color.FromArgb(light ? (byte)71 : (byte)102, 247, 240, 255))
        });
    }

    private static RadialGradientBrush SoftGlow(Point center, double radiusX, double radiusY, Color tint)
    {
        return new RadialGradientBrush
        {
            Center = center, GradientOrigin = center, RadiusX = radiusX, RadiusY = radiusY,
            GradientStops =
            {
                new() { Offset = 0, Color = tint },
                new() { Offset = .28, Color = Color.FromArgb((byte)(tint.A * .80), tint.R, tint.G, tint.B) },
                new() { Offset = .65, Color = Color.FromArgb((byte)(tint.A * .25), tint.R, tint.G, tint.B) },
                new() { Offset = 1, Color = Color.FromArgb(0, tint.R, tint.G, tint.B) }
            }
        };
    }

    private static Point At(double center, double radius, double angle) =>
        new(center + Math.Cos(angle) * radius, center + Math.Sin(angle) * radius);

    private static Geometry Arc(double center, double radius, double start, double sweep)
    {
        if (sweep >= Math.Tau)
            return new EllipseGeometry { Center = new(center, center), RadiusX = radius, RadiusY = radius };
        var figure = new PathFigure { StartPoint = At(center, radius, start) };
        figure.Segments.Add(new ArcSegment
        {
            Point = At(center, radius, start + sweep), Size = new(radius, radius),
            IsLargeArc = sweep > Math.PI, SweepDirection = SweepDirection.Clockwise
        });
        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        return geometry;
    }
}
