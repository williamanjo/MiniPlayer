using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using MiniPlayer.Localization;
using MiniPlayer.Services;

namespace MiniPlayer.Views;

/// <summary>
/// Visual theme editor: every theme property as a control (color, slider, choice, switch),
/// with a live preview. Saves into the themes folder as a JSON theme, like hand-made ones.
/// </summary>
public partial class ThemeEditorWindow : Window
{
    readonly App _app = (App)Application.Current;
    readonly ThemeDefinition _theme;
    readonly string? _file; // the theme's own file when editing a folder theme; null = new file
    readonly Action<string>? _saved;
    readonly string _originalName;
    readonly FormBuilder _form;
    readonly DispatcherTimer _previewDebounce = new() { Interval = TimeSpan.FromMilliseconds(80) };

    public ThemeEditorWindow(ThemeDefinition source, Action<string>? saved)
    {
        InitializeComponent();
        DataContext = _app.ViewModel; // preview shows the current track
        _saved = saved;
        _theme = ThemeService.Copy(source);
        if (source.BuiltIn)
        {
            _theme.Name = Loc.F("editor_copy_suffix", source.Name);
            _theme.Directory = ThemeService.UserDir;
        }
        else
        {
            _file = ThemeService.FileOf(source);
        }
        _originalName = source.Name;
        // built-ins are always saved as a new file, so "save" already is "save as new"
        SaveAsNewButton.Visibility = _file is null ? Visibility.Collapsed : Visibility.Visible;

        _previewDebounce.Tick += (_, _) =>
        {
            _previewDebounce.Stop();
            Preview.Show(_theme);
        };
        _form = new FormBuilder(Form, (Style)Resources["SectionTitle"], (Style)Resources["Card"], () =>
        {
            _previewDebounce.Stop();
            _previewDebounce.Start();
        });
        Activated += (_, _) => Topmost = true;
        Deactivated += (_, _) => Topmost = false;

        BuildForm();
        Changed();
    }

    #region Form

    void BuildForm()
    {
        var t = _theme;

        Section("editor_sec_general");
        Row("editor_name", Text(() => t.Name, v => t.Name = string.IsNullOrWhiteSpace(v) ? "Tema" : v));
        Row("editor_author", Text(() => t.Author, v => t.Author = string.IsNullOrWhiteSpace(v) ? null : v));
        Row("editor_author_position", Combo([("bottom", "editor_pos_bottom"), ("bottomLeft", "editor_pos_bottom_left"),
                ("bottomRight", "editor_pos_bottom_right"), ("top", "editor_pos_top"), ("topLeft", "editor_pos_top_left"), ("topRight", "editor_pos_top_right"), ("cover", "editor_pos_cover"),
                ("none", "editor_pos_none")],
            () => t.AuthorPosition, v => t.AuthorPosition = v), () => t.Author is not null);

        Section("editor_sec_background");
        Row("editor_bg_color", Color(() => t.BackgroundGradient is { Length: >= 2 } bg ? bg[0] : t.Background, v =>
        {
            t.Background = v ?? "#FF1F1F1F";
            if (t.BackgroundGradient is { Length: >= 2 } g) g[0] = t.Background;
        }));
        Row("editor_bg_gradient", Switch(() => t.BackgroundGradient is { Length: >= 2 },
            on => t.BackgroundGradient = on ? [t.Background, t.Accent] : null));
        Row("editor_color2", Color(() => t.BackgroundGradient?[1], v => { if (t.BackgroundGradient is { Length: >= 2 } g) g[1] = v ?? t.Accent; }),
            () => t.BackgroundGradient is { Length: >= 2 });
        Row("editor_bg_angle", Slider(() => t.BackgroundAngle, v => t.BackgroundAngle = v, 0, 360, 15, "{0:0}°"),
            () => t.BackgroundGradient is { Length: >= 2 });
        Row("editor_border", Color(() => t.Border, v => t.Border = v ?? "#00000000"));
        Row("editor_border_thickness", Slider(() => t.BorderThickness, v => t.BorderThickness = v, 0, 6, 0.5, "{0:0.#} px"));
        Row("editor_corner", Slider(() => t.CornerRadius, v => t.CornerRadius = v, 0, 30, 1, "{0:0} px"));
        Row("editor_backdrop", Combo([("none", "editor_backdrop_none"), ("cover", "editor_backdrop_cover"), ("image", "editor_backdrop_image")],
            () => t.Backdrop, v => t.Backdrop = v));
        Row("editor_backdrop_blur", Slider(() => t.BackdropBlur, v => t.BackdropBlur = v, 0, 80, 5, "{0:0}"), () => t.Backdrop == "cover");
        Row("editor_image", ImagePicker(), () => t.Backdrop == "image");
        Row("editor_image_opacity", Slider(() => t.BackgroundImageOpacity, v => t.BackgroundImageOpacity = v, 0, 1, 0.05, "{0:P0}"),
            () => t.Backdrop == "image");
        Row("editor_overlay", Color(() => t.Overlay, v => t.Overlay = v ?? "#00000000"), () => t.Backdrop != "none");
        Row("editor_shadow_opacity", Slider(() => t.ShadowOpacity, v => t.ShadowOpacity = v, 0, 1, 0.05, "{0:P0}"));
        Row("editor_shadow_blur", Slider(() => t.ShadowBlur, v => t.ShadowBlur = v, 0, 60, 1, "{0:0}"), () => t.ShadowOpacity > 0);
        Row("editor_shadow_depth", Slider(() => t.ShadowDepth, v => t.ShadowDepth = v, 0, 30, 1, "{0:0} px"), () => t.ShadowOpacity > 0);
        Row("editor_shadow_color", Color(() => t.ShadowColor, v => t.ShadowColor = v ?? "#000000"), () => t.ShadowOpacity > 0);

        Section("editor_sec_text");
        Row("editor_font", FontCombo(() => t.FontFamily, v => t.FontFamily = v ?? "Segoe UI", optional: false));
        Row("editor_title_font", FontCombo(() => t.TitleFont, v => t.TitleFont = v, optional: true));
        Row("editor_title_size", Slider(() => t.TitleSize, v => t.TitleSize = v, 10, 28, 1, "{0:0}"));
        Row("editor_title_weight", Combo([("normal", "editor_weight_normal"), ("semibold", "editor_weight_semibold"), ("bold", "editor_weight_bold")],
            () => t.TitleWeight, v => t.TitleWeight = v));
        Row("editor_subtitle_size", Slider(() => t.SubtitleSize, v => t.SubtitleSize = v, 9, 20, 1, "{0:0}"));
        Row("editor_fg", Color(() => t.Foreground, v => t.Foreground = v ?? "#FFFFFF"));
        Row("editor_fg2", Color(() => t.SecondaryForeground, v => t.SecondaryForeground = v ?? "#B3FFFFFF"));
        Row("editor_lyrics", Color(() => t.LyricsColor, v => t.LyricsColor = v, optional: true));
        Row("editor_lyrics_dim", Color(() => t.LyricsDimColor, v => t.LyricsDimColor = v, optional: true));

        Section("editor_sec_buttons");
        Row("editor_accent", Color(() => t.Accent, v => t.Accent = v ?? "#60CDFF"));
        Row("editor_icon", Color(() => t.IconColor, v => t.IconColor = v, optional: true));
        Row("editor_play", Combo([("plain", "editor_play_plain"), ("circle", "editor_play_circle")], () => t.PlayButton, v => t.PlayButton = v));
        Row("editor_play_color", Color(() => t.PlayButtonColor, v => t.PlayButtonColor = v, optional: true), () => t.PlayButton == "circle");
        Row("editor_play_icon", Color(() => t.PlayIconColor, v => t.PlayIconColor = v, optional: true), () => t.PlayButton == "circle");
        Row("editor_hover", Color(() => t.Hover, v => t.Hover = v ?? "#33808080"));
        Row("editor_track", Color(() => t.Track, v => t.Track = v ?? "#40808080"));

        Section("editor_sec_cover");
        Row("editor_cover_shape", Combo([("rounded", "editor_shape_rounded"), ("circle", "editor_shape_circle")], () => t.CoverShape, v => t.CoverShape = v));
        Row("editor_cover_spin", Switch(() => t.CoverSpin, v => t.CoverSpin = v), () => t.CoverShape == "circle");
        Row("editor_cover_radius", Slider(() => t.CoverRadius, v => t.CoverRadius = v, 0, 42, 1, "{0:0} px"), () => t.CoverShape != "circle");
        Row("editor_cover_border", Color(() => t.CoverBorder, v => t.CoverBorder = v, optional: true));
        Row("editor_cover_border_thickness", Slider(() => t.CoverBorderThickness, v => t.CoverBorderThickness = v, 0, 10, 0.5, "{0:0.#} px"),
            () => t.CoverBorder is not null);
        Row("editor_cover_shadow", Slider(() => t.CoverShadowOpacity, v => t.CoverShadowOpacity = v, 0, 1, 0.05, "{0:P0}"));

        Section("editor_sec_progress");
        Row("editor_progress_height", Slider(() => t.ProgressHeight, v => t.ProgressHeight = v, 1, 10, 1, "{0:0} px"));
        Row("editor_progress_gradient", Switch(() => t.ProgressGradient is { Length: >= 2 },
            on => t.ProgressGradient = on ? [t.Accent, t.Accent] : null));
        Row("editor_color1", Color(() => t.ProgressGradient?[0], v => { if (t.ProgressGradient is { Length: >= 2 } g) g[0] = v ?? t.Accent; }),
            () => t.ProgressGradient is { Length: >= 2 });
        Row("editor_color2", Color(() => t.ProgressGradient?[1], v => { if (t.ProgressGradient is { Length: >= 2 } g) g[1] = v ?? t.Accent; }),
            () => t.ProgressGradient is { Length: >= 2 });
        Row("editor_viz_gradient", Switch(() => t.VisualizerGradient is { Length: >= 2 },
            on => t.VisualizerGradient = on ? [t.Accent, t.Accent] : null));
        Row("editor_color1", Color(() => t.VisualizerGradient?[0], v => { if (t.VisualizerGradient is { Length: >= 2 } g) g[0] = v ?? t.Accent; }),
            () => t.VisualizerGradient is { Length: >= 2 });
        Row("editor_color2", Color(() => t.VisualizerGradient?[1], v => { if (t.VisualizerGradient is { Length: >= 2 } g) g[1] = v ?? t.Accent; }),
            () => t.VisualizerGradient is { Length: >= 2 });
        Row("editor_viz_opacity", Slider(() => t.VisualizerOpacity, v => t.VisualizerOpacity = v, 0.1, 1, 0.05, "{0:P0}"));
    }

    // Form rows come from the shared builder (also used by the overlay editor).
    void Changed() => _form.Changed();
    void Section(string key) => _form.Section(key);
    void Row(string labelKey, FrameworkElement control, Func<bool>? visible = null) => _form.Row(labelKey, control, visible);
    FrameworkElement Text(Func<string?> get, Action<string?> set) => _form.Text(get, set);
    FrameworkElement Color(Func<string?> get, Action<string?> set, bool optional = false) => _form.Color(get, set, optional);
    FrameworkElement Slider(Func<double> get, Action<double> set, double min, double max, double step, string format) =>
        _form.Slider(get, set, min, max, step, format);
    FrameworkElement Combo((string Value, string LabelKey)[] options, Func<string?> get, Action<string> set) => _form.Combo(options, get, set);
    FrameworkElement Switch(Func<bool> get, Action<bool> set) => _form.Switch(get, set);
    FrameworkElement FontCombo(Func<string?> get, Action<string?> set, bool optional) => _form.FontCombo(get, set, optional);

    FrameworkElement ImagePicker()
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        var name = new TextBlock { Text = _theme.BackgroundImage ?? "", Opacity = 0.7, VerticalAlignment = VerticalAlignment.Center,
            MaxWidth = 160, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 10, 0) };
        var button = new Button { Content = Loc.T("editor_image_choose"), Padding = new Thickness(12, 4, 12, 4) };
        button.Click += (_, _) =>
        {
            var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "Imagens|*.png;*.jpg;*.jpeg;*.bmp;*.webp" };
            if (dialog.ShowDialog(this) != true) return;
            // images live next to the theme JSON (themes may only use files from their own folder)
            ThemeService.EnsureUserDir();
            var target = Path.Combine(ThemeService.UserDir, Path.GetFileName(dialog.FileName));
            if (!string.Equals(Path.GetFullPath(dialog.FileName), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase))
                File.Copy(dialog.FileName, target, overwrite: true);
            _theme.BackgroundImage = Path.GetFileName(target);
            _theme.Directory = ThemeService.UserDir;
            name.Text = _theme.BackgroundImage;
            Changed();
        };
        panel.Children.Add(name);
        panel.Children.Add(button);
        return panel;
    }

    #endregion

    void OnSave(object sender, RoutedEventArgs e) => SaveTo(_file);

    /// <summary>Keeps the original theme and writes a new file (renamed if the name was not changed).</summary>
    void OnSaveAsNew(object sender, RoutedEventArgs e)
    {
        if (_theme.Name == _originalName) _theme.Name = Loc.F("editor_copy_suffix", _theme.Name);
        SaveTo(null);
    }

    void SaveTo(string? file)
    {
        var id = _app.Themes.Save(_theme, file);
        _saved?.Invoke(id);
        Close();
    }

    void OnCancel(object sender, RoutedEventArgs e) => Close();
}
