using System.Windows;
using System.Windows.Media;
using MiniPlayer.Services;

namespace MiniPlayer.Views;

/// <summary>Audio bars drawn from <see cref="VisualizerService"/>.</summary>
public sealed class VisualizerControl : FrameworkElement
{
    public enum VisualizerStyle
    {
        /// <summary>Level history scrolling right to left (wide strips).</summary>
        Wave,
        /// <summary>Few bars bouncing around the current level (tiny equalizer icon).</summary>
        Equalizer,
        /// <summary>Bars pointing outward around the cover, following its shape.</summary>
        Ring,
    }

    /// <summary>Ring style: inner radius as a fraction of the control's half size (the cover's scale).</summary>
    public const double RingInner = 0.8;

    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(
        nameof(Fill), typeof(Brush), typeof(VisualizerControl),
        new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty BarCountProperty = DependencyProperty.Register(
        nameof(BarCount), typeof(int), typeof(VisualizerControl),
        new FrameworkPropertyMetadata(16, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StyleKindProperty = DependencyProperty.Register(
        nameof(StyleKind), typeof(VisualizerStyle), typeof(VisualizerControl),
        new FrameworkPropertyMetadata(VisualizerStyle.Wave, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ShapeRadiusProperty = DependencyProperty.Register(
        nameof(ShapeRadius), typeof(CornerRadius), typeof(VisualizerControl),
        new FrameworkPropertyMetadata(new CornerRadius(9999), FrameworkPropertyMetadataOptions.AffectsRender));

    public Brush Fill { get => (Brush)GetValue(FillProperty); set => SetValue(FillProperty, value); }
    /// <summary>Number of bars; 0 = as many as fit (about one per 7 px).</summary>
    public int BarCount { get => (int)GetValue(BarCountProperty); set => SetValue(BarCountProperty, value); }
    /// <summary>Ring style: corner radius of the shape the bars go around (the cover's).</summary>
    public CornerRadius ShapeRadius { get => (CornerRadius)GetValue(ShapeRadiusProperty); set => SetValue(ShapeRadiusProperty, value); }
    public VisualizerStyle StyleKind { get => (VisualizerStyle)GetValue(StyleKindProperty); set => SetValue(StyleKindProperty, value); }

    // Fixed per-bar character for the equalizer look.
    static readonly double[] Speeds = [7.1, 9.3, 5.7, 8.2, 6.4, 10.1, 7.7, 5.2];
    static readonly double[] Phases = [0.0, 1.7, 3.1, 0.9, 2.4, 4.0, 5.2, 2.9];

    VisualizerService? _service;

    public VisualizerControl()
    {
        IsHitTestVisible = false;
        Loaded += (_, _) =>
        {
            _service = (Application.Current as App)?.Visualizer;
            if (_service is not null) _service.Updated += OnUpdated;
        };
        Unloaded += (_, _) =>
        {
            if (_service is not null) _service.Updated -= OnUpdated;
            _service = null;
        };
    }

    void OnUpdated()
    {
        if (IsVisible) InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        var service = _service;
        var w = ActualWidth;
        var h = ActualHeight;
        var count = BarCount > 0 ? BarCount : Math.Clamp((int)(w / 7), 3, 48);
        if (service is null || w <= 0 || h <= 0) return;
        if (StyleKind == VisualizerStyle.Ring)
        {
            RenderRing(dc, service, w, h);
            return;
        }

        var gap = Math.Max(1, w / count * 0.25);
        var barWidth = (w - gap * (count - 1)) / count;
        var radius = Math.Min(barWidth / 2, 2);
        var t = Environment.TickCount64 / 1000.0;

        for (var i = 0; i < count; i++)
        {
            double value = StyleKind == VisualizerStyle.Wave
                // newest sample on the right, older ones scroll left
                ? service.History((count - 1 - i) * Math.Max(1, (VisualizerService.HistoryLength - 1) / count))
                : service.Level * (0.45 + 0.55 * (0.5 + 0.5 * Math.Sin(t * Speeds[i % Speeds.Length] + Phases[i % Phases.Length])));

            var barHeight = Math.Max(1.5, value * h);
            var x = i * (barWidth + gap);
            dc.DrawRoundedRectangle(Fill, null, new Rect(x, h - barHeight, barWidth, barHeight), radius, radius);
        }
    }

    void RenderRing(DrawingContext dc, VisualizerService service, double w, double h)
    {
        var center = new Point(w / 2, h / 2);
        var outer = Math.Min(w, h) / 2;
        // the cover is drawn at RingInner of this size: follow its outline (rounded square or circle)
        var half = outer * RingInner;
        var corner = Math.Min(half, ShapeRadius.TopLeft * RingInner);
        var gap = Math.Max(1.5, outer * 0.025);
        var room = outer - half - gap;
        if (room <= 1) return;

        var straight = 2 * (half - corner);
        var perimeter = 4 * straight + 2 * Math.PI * corner;
        var count = BarCount > 0 ? BarCount : Math.Clamp((int)(perimeter / 6), 16, 120);
        var barWidth = Math.Max(1.2, perimeter / count * 0.5);
        var radius = Math.Min(barWidth / 2, 2);
        var t = Environment.TickCount64 / 1000.0;
        var level = service.Level;

        for (var i = 0; i < count; i++)
        {
            var (point, angle) = Outline(center, half, corner, straight, perimeter * i / count);
            // each bar its own speed/phase (golden-ratio spread) so the ring ripples instead of pulsing
            var speed = 4.5 + (i * 0.618 % 1) * 6;
            var value = level * (0.3 + 0.7 * (0.5 + 0.5 * Math.Sin(t * speed + i * 2.39996)));
            var length = Math.Max(1.2, value * room);
            var transform = new TransformGroup();
            transform.Children.Add(new RotateTransform(angle));
            transform.Children.Add(new TranslateTransform(point.X, point.Y));
            dc.PushTransform(transform);
            // bar points outward (up before the rotation) from just outside the outline
            dc.DrawRoundedRectangle(Fill, null, new Rect(-barWidth / 2, -gap - length, barWidth, length), radius, radius);
            dc.Pop();
        }
    }

    /// <summary>
    /// Point at distance <paramref name="s"/> along a rounded square (clockwise from top center) and
    /// the outward normal as a rotation in degrees (0 = up, 90 = right).
    /// </summary>
    static (Point, double) Outline(Point c, double half, double r, double straight, double s)
    {
        var arc = Math.PI * r / 2;
        var side = half - r; // center to where the corner arc starts
        // corners clockwise from top-right: arc center and starting angle
        (double X, double Y)[] corners = [(side, -side), (side, side), (-side, side), (-side, -side)];

        // first half of the top edge
        if (s < straight / 2) return (new Point(c.X + s, c.Y - half), 0);
        s -= straight / 2;
        for (var k = 0; k < 4; k++)
        {
            var start = 90.0 * k;
            if (s < arc)
            {
                var deg = start + (arc > 0 ? s / arc * 90 : 0);
                var rad = deg * Math.PI / 180;
                return (new Point(c.X + corners[k].X + r * Math.Sin(rad), c.Y + corners[k].Y - r * Math.Cos(rad)), deg);
            }
            s -= arc;
            var len = k == 3 ? straight / 2 : straight;
            if (s < len || k == 3)
            {
                var edge = start + 90;
                return edge switch
                {
                    90 => (new Point(c.X + half, c.Y - side + s), 90),
                    180 => (new Point(c.X + side - s, c.Y + half), 180),
                    270 => (new Point(c.X - half, c.Y + side - s), 270),
                    _ => (new Point(c.X - side + s, c.Y - half), 0),
                };
            }
            s -= len;
        }
        return (new Point(c.X, c.Y - half), 0);
    }
}
