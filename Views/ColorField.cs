using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MiniPlayer.Localization;

namespace MiniPlayer.Views;

/// <summary>
/// Color input for the theme editor: a swatch (click = Windows color picker) and the hex text
/// (#RRGGBB or #AARRGGBB, so transparency can be typed). Optional fields accept empty = default.
/// </summary>
public sealed class ColorField : StackPanel
{
    readonly Border _swatch;
    readonly TextBox _text;
    readonly bool _optional;
    bool _updating;

    public ColorField(string? value, bool optional)
    {
        _optional = optional;
        Orientation = Orientation.Horizontal;

        // checkerboard behind the swatch makes transparency visible
        var checker = new DrawingBrush
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, 8, 8),
            ViewportUnits = BrushMappingMode.Absolute,
            Drawing = new GeometryDrawing(new SolidColorBrush(Color.FromRgb(200, 200, 200)), null,
                Geometry.Parse("M0,0 H4 V4 H0Z M4,4 H8 V8 H4Z")),
        };
        _swatch = new Border { Width = 34, Height = 26, CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x55, 0x80, 0x80, 0x80)), Cursor = Cursors.Hand };
        var swatchHost = new Grid { Margin = new Thickness(0, 0, 8, 0) };
        swatchHost.Children.Add(new Border { Width = 34, Height = 26, CornerRadius = new CornerRadius(4), Background = checker });
        swatchHost.Children.Add(_swatch);
        swatchHost.MouseLeftButtonUp += (_, _) => Pick();

        _text = new TextBox { Width = 110, VerticalContentAlignment = VerticalAlignment.Center, Text = value ?? "" };
        _text.TextChanged += (_, _) =>
        {
            if (_updating) return;
            Refresh();
            Changed?.Invoke(Value);
        };
        Children.Add(swatchHost);
        Children.Add(_text);
        if (optional)
            Children.Add(new TextBlock { Text = Loc.T("editor_default_hint"), Opacity = 0.6, FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) });
        Refresh();
    }

    /// <summary>Raised with the new value (null = optional field left empty).</summary>
    public event Action<string?>? Changed;

    public string? Value => string.IsNullOrWhiteSpace(_text.Text) ? (_optional ? null : "#00000000") : _text.Text.Trim();

    void Refresh()
    {
        _swatch.Background = TryParse(_text.Text) is { } c ? new SolidColorBrush(c) : Brushes.Transparent;
    }

    static Color? TryParse(string? text)
    {
        try { return string.IsNullOrWhiteSpace(text) ? null : (Color)ColorConverter.ConvertFromString(text.Trim()); }
        catch { return null; }
    }

    void Pick()
    {
        var current = TryParse(_text.Text) ?? Colors.White;
        using var dialog = new System.Windows.Forms.ColorDialog
        {
            FullOpen = true,
            Color = System.Drawing.Color.FromArgb(current.R, current.G, current.B),
        };
        if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
        // the Windows picker has no alpha: keep the transparency that was typed
        var picked = Color.FromArgb(current.A, dialog.Color.R, dialog.Color.G, dialog.Color.B);
        _updating = true;
        _text.Text = current.A == 255 ? $"#{picked.R:X2}{picked.G:X2}{picked.B:X2}" : $"#{picked.A:X2}{picked.R:X2}{picked.G:X2}{picked.B:X2}";
        _updating = false;
        Refresh();
        Changed?.Invoke(Value);
    }
}
