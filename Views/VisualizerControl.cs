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
    }

    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(
        nameof(Fill), typeof(Brush), typeof(VisualizerControl),
        new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty BarCountProperty = DependencyProperty.Register(
        nameof(BarCount), typeof(int), typeof(VisualizerControl),
        new FrameworkPropertyMetadata(16, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StyleKindProperty = DependencyProperty.Register(
        nameof(StyleKind), typeof(VisualizerStyle), typeof(VisualizerControl),
        new FrameworkPropertyMetadata(VisualizerStyle.Wave, FrameworkPropertyMetadataOptions.AffectsRender));

    public Brush Fill { get => (Brush)GetValue(FillProperty); set => SetValue(FillProperty, value); }
    /// <summary>Number of bars; 0 = as many as fit (about one per 7 px).</summary>
    public int BarCount { get => (int)GetValue(BarCountProperty); set => SetValue(BarCountProperty, value); }
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
}
