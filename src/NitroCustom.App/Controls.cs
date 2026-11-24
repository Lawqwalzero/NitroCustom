using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace NitroCustom.App;

/// <summary>Attached icon geometry for templates (profile rows, section headers): <c>local:Glyph.Data</c>.</summary>
public static class Glyph
{
    public static readonly DependencyProperty DataProperty = DependencyProperty.RegisterAttached(
        "Data", typeof(Geometry), typeof(Glyph), new FrameworkPropertyMetadata(null));

    public static Geometry? GetData(DependencyObject d) => (Geometry?)d.GetValue(DataProperty);
    public static void SetData(DependencyObject d, Geometry? value) => d.SetValue(DataProperty, value);
}

/// <summary>Circular 270° gauge: dark track, red-to-amber value arc, value in the middle.</summary>
public sealed class ArcGauge : FrameworkElement
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double?), typeof(ArcGauge), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnValueChanged));

    /// <summary>Animated value actually drawn (eases towards <see cref="Value"/>).</summary>
    private static readonly DependencyProperty ShownProperty = DependencyProperty.Register(
        "Shown", typeof(double), typeof(ArcGauge), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var g = (ArcGauge)d;
        if (e.NewValue is double v)
            g.BeginAnimation(ShownProperty, new System.Windows.Media.Animation.DoubleAnimation(v, TimeSpan.FromMilliseconds(700))
            {
                EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut },
            });
    }
    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
        nameof(Maximum), typeof(double), typeof(ArcGauge), new FrameworkPropertyMetadata(100.0, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty UnitProperty = DependencyProperty.Register(
        nameof(Unit), typeof(string), typeof(ArcGauge), new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsRender));

    public double? Value { get => (double?)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public double Maximum { get => (double)GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
    public string Unit { get => (string)GetValue(UnitProperty); set => SetValue(UnitProperty, value); }

    private static readonly Typeface Number = new(new FontFamily("Bahnschrift"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
    private static readonly Typeface Small = new(new FontFamily("Bahnschrift"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
    private static readonly Brush TextBrush = Frozen(new SolidColorBrush(Color.FromRgb(0xF2, 0xEC, 0xE8)));
    private static readonly Brush MutedBrush = Frozen(new SolidColorBrush(Color.FromRgb(0xB8, 0xA5, 0x9B)));
    private static readonly Pen RingPen = Frozen(new Pen(Frozen(new SolidColorBrush(Color.FromRgb(0x4A, 0x24, 0x14))), 1));
    private static readonly Brush DiscBrush = Frozen(new RadialGradientBrush(Color.FromRgb(0x22, 0x0E, 0x08), Color.FromRgb(0x10, 0x07, 0x05)));
    private static readonly Brush ValueBrush = Frozen(new LinearGradientBrush(Color.FromRgb(0xE3, 0x26, 0x2B), Color.FromRgb(0xFF, 0xB2, 0x1F), new Point(0, 1), new Point(1, 0)));
    private static readonly Brush TrackBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x2E, 0x15, 0x0C)));

    protected override Size MeasureOverride(Size availableSize) =>
        new(Math.Min(100, availableSize.Width), Math.Min(100, availableSize.Height));

    protected override void OnRender(DrawingContext dc)
    {
        var size = Math.Min(ActualWidth, ActualHeight);
        if (size < 20) return;
        var c = new Point(ActualWidth / 2, ActualHeight / 2);
        var outer = size / 2 - 1;
        var thickness = Math.Max(4, size * 0.08);
        var r = outer - 3 - thickness / 2;

        dc.DrawEllipse(DiscBrush, RingPen, c, outer, outer);
        DrawArc(dc, c, r, 135, 270, new Pen(TrackBrush, thickness));
        var shown = (double)GetValue(ShownProperty);
        if (Value is not null && Maximum > 0)
        {
            var sweep = 270 * Math.Clamp(shown / Maximum, 0, 1);
            DrawArc(dc, c, r, 135, sweep, new Pen(ValueBrush, thickness) { StartLineCap = PenLineCap.Flat, EndLineCap = PenLineCap.Flat });
        }

        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var text = Value is not null ? shown.ToString("0", CultureInfo.InvariantCulture) : "—";
        var number = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Number, size * 0.27, TextBrush, dpi);
        var unit = new FormattedText(Unit, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Small, size * 0.11, MutedBrush, dpi);
        var top = c.Y - (number.Height + unit.Height) / 2 + size * 0.02;
        dc.DrawText(number, new Point(c.X - number.Width / 2, top));
        dc.DrawText(unit, new Point(c.X - unit.Width / 2, top + number.Height - size * 0.03));
    }

    private static void DrawArc(DrawingContext dc, Point c, double r, double startDeg, double sweepDeg, Pen pen)
    {
        if (sweepDeg < 0.5) return;
        if (sweepDeg >= 359.9) sweepDeg = 359.9;
        static Point At(Point c, double r, double deg) =>
            new(c.X + r * Math.Cos(deg * Math.PI / 180), c.Y + r * Math.Sin(deg * Math.PI / 180));
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            g.BeginFigure(At(c, r, startDeg), false, false);
            g.ArcTo(At(c, r, startDeg + sweepDeg), new Size(r, r), 0, sweepDeg > 180, SweepDirection.Clockwise, true, false);
        }
        geometry.Freeze();
        dc.DrawGeometry(null, pen, geometry);
    }

    private static T Frozen<T>(T f) where T : Freezable { f.Freeze(); return f; }
}

/// <summary>Panel frame with cut top-left and bottom-right corners and ember corner accents.</summary>
public sealed class ChamferFrame : Decorator
{
    public double Cut { get; set; } = 16;
    public Thickness Padding { get; set; } = new(18);

    private static readonly Brush Fill = Frozen(new SolidColorBrush(Color.FromArgb(0xC8, 0x14, 0x09, 0x06)));
    private static readonly Pen Edge = Frozen(new Pen(Frozen(new SolidColorBrush(Color.FromArgb(0xB0, 0x8A, 0x3A, 0x1A))), 1));
    private static readonly Pen Accent = Frozen(new Pen(Frozen(new SolidColorBrush(Color.FromRgb(0xFF, 0x6A, 0x1F))), 2));

    protected override Size MeasureOverride(Size constraint)
    {
        var inner = new Size(Math.Max(0, constraint.Width - Padding.Left - Padding.Right), Math.Max(0, constraint.Height - Padding.Top - Padding.Bottom));
        Child?.Measure(inner);
        var d = Child?.DesiredSize ?? default;
        return new Size(d.Width + Padding.Left + Padding.Right, d.Height + Padding.Top + Padding.Bottom);
    }

    protected override Size ArrangeOverride(Size size)
    {
        Child?.Arrange(new Rect(Padding.Left, Padding.Top,
            Math.Max(0, size.Width - Padding.Left - Padding.Right), Math.Max(0, size.Height - Padding.Top - Padding.Bottom)));
        return size;
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight, k = Cut;
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(new Point(k, 0.5), true, true);
            c.PolyLineTo([new Point(w - 0.5, 0.5), new Point(w - 0.5, h - k), new Point(w - k, h - 0.5), new Point(0.5, h - 0.5), new Point(0.5, k)], true, false);
        }
        g.Freeze();
        dc.DrawGeometry(Fill, Edge, g);
        // Bright accents on the cut corners.
        dc.DrawLine(Accent, new Point(0.5, k + 10), new Point(0.5, k));
        dc.DrawLine(Accent, new Point(0.5, k), new Point(k, 0.5));
        dc.DrawLine(Accent, new Point(k, 0.5), new Point(k + 10, 0.5));
        dc.DrawLine(Accent, new Point(w - 0.5, h - k - 10), new Point(w - 0.5, h - k));
        dc.DrawLine(Accent, new Point(w - 0.5, h - k), new Point(w - k, h - 0.5));
        dc.DrawLine(Accent, new Point(w - k, h - 0.5), new Point(w - k - 10, h - 0.5));
    }

    private static T Frozen<T>(T f) where T : Freezable { f.Freeze(); return f; }
}

/// <summary>Thin horizontal bar with a label and percentage (CPU / RAM load).</summary>
public sealed class LoadBar : Control
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(LoadBar), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender,
            (d, e) => ((LoadBar)d).BeginAnimation(ShownProperty, new System.Windows.Media.Animation.DoubleAnimation((double)e.NewValue, TimeSpan.FromMilliseconds(600))
            {
                EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut },
            })));

    private static readonly DependencyProperty ShownProperty = DependencyProperty.Register(
        "Shown", typeof(double), typeof(LoadBar), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty CaptionProperty = DependencyProperty.Register(
        nameof(Caption), typeof(string), typeof(LoadBar), new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsRender));

    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public string Caption { get => (string)GetValue(CaptionProperty); set => SetValue(CaptionProperty, value); }

    private static readonly Typeface Face = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
    private static readonly Typeface Bold = new(new FontFamily("Bahnschrift"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

    protected override Size MeasureOverride(Size constraint) => new(100, 30);

    protected override void OnRender(DrawingContext dc)
    {
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var w = ActualWidth;
        var caption = new FormattedText(Caption, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Face, 12, new SolidColorBrush(Color.FromRgb(0xB8, 0xA5, 0x9B)), dpi);
        var value = new FormattedText($"{Value:0}%", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Bold, 15, new SolidColorBrush(Color.FromRgb(0xF2, 0xEC, 0xE8)), dpi);
        dc.DrawText(caption, new Point(0, 2));
        dc.DrawText(value, new Point(w - value.Width, 0));
        var y = 24;
        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x2E, 0x15, 0x0C)), null, new Rect(0, y, w, 3));
        var fill = new LinearGradientBrush(Color.FromRgb(0xE3, 0x26, 0x2B), Color.FromRgb(0xFF, 0x8A, 0x1F), 0);
        dc.DrawRectangle(fill, null, new Rect(0, y, w * Math.Clamp((double)GetValue(ShownProperty) / 100, 0, 1), 3));
    }
}
