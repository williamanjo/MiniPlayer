using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MiniPlayer.Localization;

namespace MiniPlayer.Views;

/// <summary>
/// Settings-style form generated in code (theme and overlay editors): section titles, cards with a
/// label and a control, and rows that only show when another option makes them relevant.
/// Every control calls <see cref="Changed"/> after an edit.
/// </summary>
public sealed class FormBuilder(Panel target, Style sectionStyle, Style cardStyle, Action changed)
{
    readonly List<(FrameworkElement Row, Func<bool> Visible)> _conditional = [];

    public Panel Target { get; set; } = target;

    /// <summary>Forgets the conditional rows (the form is being rebuilt).</summary>
    public void Clear() => _conditional.Clear();

    /// <summary>Re-evaluates conditional rows, then notifies the owner.</summary>
    public void Changed()
    {
        foreach (var (row, visible) in _conditional) row.Visibility = visible() ? Visibility.Visible : Visibility.Collapsed;
        changed();
    }

    public void Section(string key) =>
        Target.Children.Add(new TextBlock { Text = Loc.T(key), Style = sectionStyle });

    public Border Row(string labelKey, FrameworkElement control, Func<bool>? visible = null) =>
        Row(new TextBlock { Text = Loc.T(labelKey), VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 16, 0) }, control, visible);

    public Border Row(FrameworkElement label, FrameworkElement control, Func<bool>? visible = null)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(control, 1);
        control.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(label);
        grid.Children.Add(control);
        var card = new Border { Child = grid, Style = cardStyle };
        Target.Children.Add(card);
        if (visible is not null) _conditional.Add((card, visible));
        return card;
    }

    public FrameworkElement Text(Func<string?> get, Action<string?> set, double width = 260)
    {
        var box = new TextBox { Width = width, Text = get() ?? "" };
        box.TextChanged += (_, _) => { set(box.Text); Changed(); };
        return box;
    }

    public FrameworkElement Color(Func<string?> get, Action<string?> set, bool optional = false)
    {
        var field = new ColorField(get(), optional);
        field.Changed += v => { set(v); Changed(); };
        return field;
    }

    public FrameworkElement Slider(Func<double> get, Action<double> set, double min, double max, double step, string format)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        var value = new TextBlock { Width = 56, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        var slider = new Slider { Width = 200, Minimum = min, Maximum = max, TickFrequency = step, IsSnapToTickEnabled = true,
            Value = Math.Clamp(get(), min, max), VerticalAlignment = VerticalAlignment.Center };
        value.Text = string.Format(Loc.Instance.Culture, format, slider.Value);
        slider.ValueChanged += (_, e) =>
        {
            set(e.NewValue);
            value.Text = string.Format(Loc.Instance.Culture, format, e.NewValue);
            Changed();
        };
        panel.Children.Add(slider);
        panel.Children.Add(value);
        return panel;
    }

    public FrameworkElement Combo((string Value, string LabelKey)[] options, Func<string?> get, Action<string> set)
    {
        var box = new ComboBox { MinWidth = 200 };
        foreach (var (value, label) in options) box.Items.Add(new ComboBoxItem { Content = Loc.T(label), Tag = value });
        var current = get()?.ToLowerInvariant();
        box.SelectedItem = box.Items.OfType<ComboBoxItem>().FirstOrDefault(i => ((string)i.Tag).ToLowerInvariant() == current) ?? box.Items[0];
        box.SelectionChanged += (_, _) =>
        {
            if (box.SelectedItem is ComboBoxItem item) set((string)item.Tag);
            Changed();
        };
        return box;
    }

    public FrameworkElement Switch(Func<bool> get, Action<bool> set)
    {
        var box = new CheckBox { IsChecked = get() };
        box.Click += (_, _) => { set(box.IsChecked == true); Changed(); };
        return box;
    }

    /// <summary>Installed fonts; <paramref name="optional"/> adds a "(same font)" entry meaning null.</summary>
    public FrameworkElement FontCombo(Func<string?> get, Action<string?> set, bool optional, string fallback = "Segoe UI")
    {
        var box = new ComboBox { Width = 260, IsEditable = true };
        if (optional) box.Items.Add(new ComboBoxItem { Content = Loc.T("editor_same_font"), Tag = "" });
        foreach (var name in Fonts.SystemFontFamilies.Select(f => f.Source).OrderBy(n => n))
            box.Items.Add(new ComboBoxItem { Content = name, Tag = name, FontFamily = new FontFamily(name) });
        // first family of a fallback list ("Segoe UI Variable Text, Segoe UI")
        var current = get()?.Split(',')[0].Trim();
        box.Text = string.IsNullOrEmpty(current) && optional ? Loc.T("editor_same_font") : current ?? "";
        void Apply()
        {
            var text = (box.SelectedItem as ComboBoxItem)?.Tag as string ?? box.Text;
            set(string.IsNullOrWhiteSpace(text) || text == Loc.T("editor_same_font") ? (optional ? null : fallback) : text);
            Changed();
        }
        box.SelectionChanged += (_, _) => Apply();
        box.LostKeyboardFocus += (_, _) => Apply();
        return box;
    }
}
