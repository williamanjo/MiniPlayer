using System.Windows;
using System.Windows.Media;

namespace MiniPlayer.Views;

/// <summary>
/// Clips an element to a rounded rectangle (a circle when the radius is huge), following its size.
/// Border.CornerRadius only shapes the border's own background, never its children.
/// </summary>
public static class RoundedClip
{
    public static readonly DependencyProperty CornerRadiusProperty = DependencyProperty.RegisterAttached(
        "CornerRadius", typeof(CornerRadius), typeof(RoundedClip), new PropertyMetadata(default(CornerRadius), OnChanged));

    public static CornerRadius GetCornerRadius(DependencyObject d) => (CornerRadius)d.GetValue(CornerRadiusProperty);
    public static void SetCornerRadius(DependencyObject d, CornerRadius value) => d.SetValue(CornerRadiusProperty, value);

    static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element) return;
        element.SizeChanged -= OnSizeChanged;
        element.SizeChanged += OnSizeChanged;
        Update(element);
    }

    static void OnSizeChanged(object sender, SizeChangedEventArgs e) => Update((FrameworkElement)sender);

    static void Update(FrameworkElement element)
    {
        var w = element.ActualWidth;
        var h = element.ActualHeight;
        // same rule as Border: a radius larger than half the side is capped (9999 = circle)
        var r = Math.Min(GetCornerRadius(element).TopLeft, Math.Min(w, h) / 2);
        element.Clip = new RectangleGeometry(new Rect(0, 0, w, h), r, r);
    }
}
