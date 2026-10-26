using System.Windows;
using System.Windows.Controls;

namespace MiniPlayer.Views;

/// <summary>
/// One settings row, like Windows 11 Settings: icon, title, hint, the control on the right
/// (<see cref="ContentControl.Content"/>) and optional sub-options below (<see cref="Details"/>).
/// Look in App.xaml.
/// </summary>
public sealed class SettingsCard : ContentControl
{
    public static readonly DependencyProperty IconProperty =
        DependencyProperty.Register(nameof(Icon), typeof(string), typeof(SettingsCard));
    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(SettingsCard));
    public static readonly DependencyProperty HintProperty =
        DependencyProperty.Register(nameof(Hint), typeof(string), typeof(SettingsCard));
    public static readonly DependencyProperty DetailsProperty =
        DependencyProperty.Register(nameof(Details), typeof(object), typeof(SettingsCard), new PropertyMetadata(null, OnDetailsChanged));

    // Details must be a logical child, or bindings inside it (ElementName=…) find nothing
    static void OnDetailsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var card = (SettingsCard)d;
        if (e.OldValue is not null) card.RemoveLogicalChild(e.OldValue);
        if (e.NewValue is not null) card.AddLogicalChild(e.NewValue);
    }

    protected override System.Collections.IEnumerator LogicalChildren =>
        new object?[] { Content, Details }.Where(c => c is not null).GetEnumerator();

    /// <summary>Segoe Fluent Icons glyph.</summary>
    public string? Icon { get => (string?)GetValue(IconProperty); set => SetValue(IconProperty, value); }
    public string? Title { get => (string?)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string? Hint { get => (string?)GetValue(HintProperty); set => SetValue(HintProperty, value); }
    public object? Details { get => GetValue(DetailsProperty); set => SetValue(DetailsProperty, value); }
}
