using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using MiniPlayer.Localization;
using MiniPlayer.Services;

namespace MiniPlayer.Views;

/// <summary>
/// Editor for the OBS overlay (overlay.html): which lines show and in what order, layout, colors,
/// sizes and animation, with the real page rendered as a live preview.
/// </summary>
public partial class OverlayEditorWindow : Window
{
    readonly App _app = (App)Application.Current;
    OverlayStyle _style;
    readonly FormBuilder _form;
    readonly DispatcherTimer _previewDebounce = new() { Interval = TimeSpan.FromMilliseconds(120) };
    StackPanel _itemsPanel = null!;
    bool _browserReady;

    public OverlayEditorWindow()
    {
        InitializeComponent();
        _style = Clone(_app.Settings.Data.Overlay);
        _style.Normalize();

        _previewDebounce.Tick += (_, _) =>
        {
            _previewDebounce.Stop();
            RenderPreview();
        };
        _form = new FormBuilder(Form, (Style)Resources["SectionTitle"], (Style)Resources["Card"], () =>
        {
            _previewDebounce.Stop();
            _previewDebounce.Start();
        });
        Activated += (_, _) => Topmost = true;
        Deactivated += (_, _) => Topmost = false;

        BuildForm();
        _form.Changed();
        Loaded += async (_, _) => await InitBrowserAsync();
    }

    static OverlayStyle Clone(OverlayStyle s) =>
        JsonSerializer.Deserialize<OverlayStyle>(JsonSerializer.Serialize(s)) ?? new OverlayStyle();

    #region Form

    void BuildForm()
    {
        Form.Children.Clear();
        _form.Clear();
        _form.Target = Form;
        var s = _style;

        _form.Section("ov_sec_preset");
        var preset = new ComboBox { MinWidth = 200 };
        preset.Items.Add(new ComboBoxItem { Content = Loc.T("ov_preset_pick"), Tag = "", IsSelected = true });
        foreach (var (id, key) in new[] { ("card", "ov_preset_card"), ("bar", "ov_preset_bar"), ("minimal", "ov_preset_minimal"),
                     ("vinyl", "ov_preset_vinyl"), ("neon", "ov_preset_neon") })
            preset.Items.Add(new ComboBoxItem { Content = Loc.T(key), Tag = id });
        preset.SelectionChanged += (_, _) =>
        {
            if (preset.SelectedItem is not ComboBoxItem { Tag: string id } || id.Length == 0) return;
            _style = OverlayStyle.Preset(id);
            Dispatcher.BeginInvoke(() => { BuildForm(); _form.Changed(); }); // rebuild with the preset values
        };
        _form.Row("ov_preset", preset);

        _form.Section("ov_sec_items");
        _itemsPanel = new StackPanel();
        Form.Children.Add(_itemsPanel);
        BuildItems();

        _form.Section("ov_sec_layout");
        _form.Row("ov_layout", _form.Combo([("row", "ov_layout_row"), ("rowReverse", "ov_layout_row_reverse"),
            ("column", "ov_layout_column"), ("text", "ov_layout_text")], () => s.Layout, v => s.Layout = v));
        _form.Row("ov_align", _form.Combo([("left", "ov_align_left"), ("center", "ov_align_center"), ("right", "ov_align_right")],
            () => s.Align, v => s.Align = v));
        _form.Row("ov_width", _form.Slider(() => s.Width, v => s.Width = v, 200, 1200, 10, "{0:0} px"));
        _form.Row("ov_padding", _form.Slider(() => s.Padding, v => s.Padding = v, 0, 40, 1, "{0:0} px"));
        _form.Row("ov_gap", _form.Slider(() => s.Gap, v => s.Gap = v, 0, 40, 1, "{0:0} px"), () => s.Layout != "text");
        _form.Row("ov_animation", _form.Combo([("fade", "ov_anim_fade"), ("slide", "ov_anim_slide"), ("none", "ov_anim_none")],
            () => s.Animation, v => s.Animation = v));

        _form.Section("ov_sec_card");
        _form.Row("editor_bg_color", _form.Color(() => s.Background, v => s.Background = v ?? "#00000000"));
        _form.Row("editor_bg_gradient", _form.Switch(() => s.Background2 is not null, on => s.Background2 = on ? s.Accent : null));
        _form.Row("editor_color2", _form.Color(() => s.Background2, v => s.Background2 = v ?? s.Accent), () => s.Background2 is not null);
        _form.Row("editor_bg_angle", _form.Slider(() => s.BackgroundAngle, v => s.BackgroundAngle = v, 0, 360, 15, "{0:0}°"),
            () => s.Background2 is not null);
        _form.Row("editor_border", _form.Color(() => s.BorderColor, v => s.BorderColor = v ?? "#00000000"));
        _form.Row("editor_border_thickness", _form.Slider(() => s.BorderWidth, v => s.BorderWidth = v, 0, 8, 0.5, "{0:0.#} px"));
        _form.Row("editor_corner", _form.Slider(() => s.Radius, v => s.Radius = v, 0, 60, 1, "{0:0} px"));
        _form.Row("editor_shadow_opacity", _form.Slider(() => s.ShadowOpacity, v => s.ShadowOpacity = v, 0, 1, 0.05, "{0:P0}"));
        _form.Row("ov_backdrop_blur", _form.Slider(() => s.BackdropBlur, v => s.BackdropBlur = v, 0, 40, 1, "{0:0}"));

        _form.Section("editor_sec_cover");
        _form.Row("ov_cover_size", _form.Slider(() => s.CoverSize, v => s.CoverSize = v, 24, 300, 2, "{0:0} px"), () => s.Layout != "text");
        _form.Row("editor_cover_shape", _form.Combo([("rounded", "editor_shape_rounded"), ("circle", "editor_shape_circle")],
            () => s.CoverCircle ? "circle" : "rounded", v => s.CoverCircle = v == "circle"), () => s.Layout != "text");
        _form.Row("editor_cover_radius", _form.Slider(() => s.CoverRadius, v => s.CoverRadius = v, 0, 60, 1, "{0:0} px"),
            () => s.Layout != "text" && !s.CoverCircle);
        _form.Row("editor_cover_spin", _form.Switch(() => s.CoverSpin, v => s.CoverSpin = v), () => s.Layout != "text" && s.CoverCircle);
        _form.Row("editor_spin_speed", _form.Slider(() => s.SpinSeconds, v => s.SpinSeconds = v, 2, 60, 1, "{0:0} s"),
            () => s.Layout != "text" && s.CoverCircle && s.CoverSpin);
        _form.Row("editor_spin_direction", _form.Combo([("cw", "editor_spin_cw"), ("ccw", "editor_spin_ccw")],
            () => s.SpinReverse ? "ccw" : "cw", v => s.SpinReverse = v == "ccw"), () => s.Layout != "text" && s.CoverCircle && s.CoverSpin);

        _form.Section("editor_sec_text");
        _form.Row("editor_font", _form.FontCombo(() => s.FontFamily, v => s.FontFamily = v ?? "Segoe UI", optional: false));
        _form.Row("ov_text_shadow", _form.Switch(() => s.TextShadow, v => s.TextShadow = v));
        _form.Row("ov_marquee", _form.Switch(() => s.Marquee, v => s.Marquee = v), () => Visible("title"));
        _form.Row("editor_accent", _form.Color(() => s.Accent, v => s.Accent = v ?? "#60CDFF"));
        _form.Row("ov_label_text", _form.Text(() => s.LabelText, v => s.LabelText = string.IsNullOrWhiteSpace(v) ? null : v), () => Visible("label"));
        _form.Row("ov_label_size", _form.Slider(() => s.LabelSize, v => s.LabelSize = v, 8, 40, 1, "{0:0} px"), () => Visible("label"));
        _form.Row("ov_label_color", _form.Color(() => s.LabelColor, v => s.LabelColor = v, optional: true), () => Visible("label"));
        _form.Row("editor_title_size", _form.Slider(() => s.TitleSize, v => s.TitleSize = v, 10, 72, 1, "{0:0} px"), () => Visible("title"));
        _form.Row("ov_title_color", _form.Color(() => s.TitleColor, v => s.TitleColor = v ?? "#FFFFFF"), () => Visible("title"));
        _form.Row("editor_title_weight", _form.Combo([("normal", "editor_weight_normal"), ("semibold", "editor_weight_semibold"),
            ("bold", "editor_weight_bold")], () => s.TitleWeight, v => s.TitleWeight = v), () => Visible("title"));
        _form.Row("ov_artist_size", _form.Slider(() => s.ArtistSize, v => s.ArtistSize = v, 8, 56, 1, "{0:0} px"), () => Visible("artist"));
        _form.Row("ov_artist_color", _form.Color(() => s.ArtistColor, v => s.ArtistColor = v ?? "#BFFFFFFF"), () => Visible("artist"));
        _form.Row("ov_source_size", _form.Slider(() => s.SourceSize, v => s.SourceSize = v, 8, 40, 1, "{0:0} px"), () => Visible("source"));
        _form.Row("ov_source_color", _form.Color(() => s.SourceColor, v => s.SourceColor = v ?? "#80FFFFFF"), () => Visible("source"));

        _form.Section("ov_sec_progress");
        _form.Row("editor_progress_height", _form.Slider(() => s.ProgressHeight, v => s.ProgressHeight = v, 1, 20, 1, "{0:0} px"),
            () => Visible("progress"));
        _form.Row("editor_track", _form.Color(() => s.TrackColor, v => s.TrackColor = v ?? "#33FFFFFF"), () => Visible("progress"));
        _form.Row("editor_progress_gradient", _form.Switch(() => s.Accent2 is not null, on => s.Accent2 = on ? s.Accent : null),
            () => Visible("progress"));
        _form.Row("editor_color2", _form.Color(() => s.Accent2, v => s.Accent2 = v ?? s.Accent), () => Visible("progress") && s.Accent2 is not null);
        _form.Row("ov_time_size", _form.Slider(() => s.TimeSize, v => s.TimeSize = v, 8, 40, 1, "{0:0} px"), () => Visible("time"));
        _form.Row("ov_time_color", _form.Color(() => s.TimeColor, v => s.TimeColor = v ?? "#99FFFFFF"), () => Visible("time"));
    }

    bool Visible(string kind) => _style.Items.Any(i => i.Kind == kind && i.Visible);

    /// <summary>One row per line: show/hide switch and up/down buttons for the order.</summary>
    void BuildItems()
    {
        _itemsPanel.Children.Clear();
        var previous = _form.Target;
        _form.Target = _itemsPanel;
        var items = _style.Items;
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var index = i;
            var controls = new StackPanel { Orientation = Orientation.Horizontal };
            controls.Children.Add(Arrow("", index > 0, () => Move(index, -1)));
            controls.Children.Add(Arrow("", index < items.Count - 1, () => Move(index, 1)));
            var toggle = new CheckBox { IsChecked = item.Visible, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            toggle.Click += (_, _) => { item.Visible = toggle.IsChecked == true; _form.Changed(); };
            controls.Children.Add(toggle);
            _form.Row("ov_item_" + item.Kind, controls);
        }
        _form.Target = previous;
    }

    Button Arrow(string glyph, bool enabled, Action click)
    {
        var button = new Button
        {
            Content = glyph, FontFamily = (System.Windows.Media.FontFamily)Application.Current.Resources["IconFont"],
            Padding = new Thickness(8, 4, 8, 4), Margin = new Thickness(4, 0, 0, 0), IsEnabled = enabled,
        };
        button.Click += (_, _) => click();
        return button;
    }

    void Move(int index, int delta)
    {
        var items = _style.Items;
        (items[index], items[index + delta]) = (items[index + delta], items[index]);
        BuildItems();
        _form.Changed();
    }

    #endregion

    #region Preview

    async Task InitBrowserAsync()
    {
        try
        {
            // the app folder may be read-only (Store): keep the browser profile in local app data
            var dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MiniPlayer", "WebView2");
            var env = await CoreWebView2Environment.CreateAsync(null, dataDir);
            await Browser.EnsureCoreWebView2Async(env);
            Browser.CoreWebView2.Settings.AreDevToolsEnabled = false;
            Browser.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            _browserReady = true;
            RenderPreview();
        }
        catch (Exception ex)
        {
            Browser.Visibility = Visibility.Collapsed;
            BrowserError.Text = Loc.F("ov_preview_unavailable", ex.Message);
            BrowserError.Visibility = Visibility.Visible;
        }
    }

    void RenderPreview()
    {
        if (!_browserReady) return;
        var snap = _app.ViewModel.Current;
        var has = snap.HasSession && !string.IsNullOrEmpty(snap.Title);
        var cover = _app.ViewModel.CoverBytes is { } bytes ? "data:image/png;base64," + Convert.ToBase64String(bytes) : null;
        var data = new OverlayHtml.Data(true,
            has ? snap.Title : Loc.T("ov_sample_title"), has ? snap.Artist : Loc.T("ov_sample_artist"), has ? snap.Source : "Chrome",
            cover, !has || snap.IsPlaying, has ? snap.Position.TotalSeconds : 83,
            has && snap.Duration > TimeSpan.Zero ? snap.Duration.TotalSeconds : 225,
            (has && snap.LastUpdated != default ? snap.LastUpdated : DateTimeOffset.Now).ToUnixTimeMilliseconds());

        var background = (PreviewBackground.SelectedItem as ComboBoxItem)?.Tag as string ?? "checker";
        var css = background == "checker"
            ? "repeating-conic-gradient(#3a3a3a 0% 25%, #2a2a2a 0% 50%) 50% / 24px 24px"
            : background;
        var html = OverlayHtml.Build(_style, data)
            .Replace("html, body { margin: 0; background: transparent;", $"html, body {{ margin: 0; background: {css};");
        Browser.NavigateToString(html);
    }

    void OnPreviewBackgroundChanged(object sender, SelectionChangedEventArgs e) => RenderPreview();

    #endregion

    void OnSave(object sender, RoutedEventArgs e)
    {
        _app.Settings.Data.Overlay = _style;
        _app.Settings.Save();
        _app.NowPlaying.Invalidate();
        Close();
    }

    void OnReset(object sender, RoutedEventArgs e)
    {
        _style = new OverlayStyle();
        BuildForm();
        _form.Changed();
    }

    void OnCancel(object sender, RoutedEventArgs e) => Close();
}
