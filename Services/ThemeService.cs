using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace MiniPlayer.Services;

/// <summary>
/// A theme is plain JSON (colors, sizes, font, backdrop). JSON instead of XAML on purpose:
/// themes come from other people, and loose XAML can instantiate arbitrary types.
/// </summary>
public sealed class ThemeDefinition
{
    public string Name { get; set; } = "Sem nome";
    public string? Author { get; set; }

    /// <summary>Panel color, or ignored when <see cref="BackgroundGradient"/> is set.</summary>
    public string Background { get; set; } = "#F21F1F1F";
    /// <summary>Two or more colors, painted diagonally (top-left → bottom-right).</summary>
    public string[]? BackgroundGradient { get; set; }
    public string Border { get; set; } = "#33FFFFFF";
    public double BorderThickness { get; set; } = 1;
    public double CornerRadius { get; set; } = 10;

    public string Foreground { get; set; } = "#FFFFFF";
    public string SecondaryForeground { get; set; } = "#B3FFFFFF";
    public string Accent { get; set; } = "#60CDFF";
    public string Track { get; set; } = "#40808080";
    public string Hover { get; set; } = "#33808080";

    public double CoverRadius { get; set; } = 6;
    public string FontFamily { get; set; } = "Segoe UI Variable Text, Segoe UI";
    /// <summary>0 disables the drop shadow.</summary>
    public double ShadowOpacity { get; set; } = 0.45;

    /// <summary>"none", "cover" (blurred album art) or "image" (<see cref="BackgroundImage"/>).</summary>
    public string Backdrop { get; set; } = "none";
    /// <summary>Image file next to the theme JSON (png/jpg), used when Backdrop is "image".</summary>
    public string? BackgroundImage { get; set; }
    /// <summary>Tint drawn over the backdrop so text stays readable.</summary>
    public string Overlay { get; set; } = "#00000000";

    [JsonIgnore] public string Id { get; set; } = "";
    [JsonIgnore] public string? Directory { get; set; }
    [JsonIgnore] public bool BuiltIn { get; set; }
}

public sealed class ThemeService
{
    public const string DefaultId = "builtin:escuro";

    public static readonly string UserDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MiniPlayer", "themes");

    /// <summary>Portable mode: a "themes" folder next to the exe is read too.</summary>
    static readonly string? PortableDir = Environment.ProcessPath is { } exe
        ? Path.Combine(Path.GetDirectoryName(exe)!, "themes")
        : null;

    static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    readonly SettingsService _settings;
    readonly DispatcherTimer _reloadDebounce;
    FileSystemWatcher? _watcher;

    public ThemeService(SettingsService settings)
    {
        _settings = settings;
        _reloadDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _reloadDebounce.Tick += (_, _) =>
        {
            _reloadDebounce.Stop();
            Load();
            Apply(_settings.Data.Theme);
        };
    }

    public IReadOnlyList<ThemeDefinition> Themes { get; private set; } = [];
    /// <summary>Theme files that failed to load ("file: reason").</summary>
    public IReadOnlyList<string> Errors { get; private set; } = [];
    public string CurrentId { get; private set; } = DefaultId;

    public void Initialize()
    {
        EnsureUserDir();
        Load();
        Apply(_settings.Data.Theme);
        Watch();
    }

    public void Load()
    {
        var themes = new List<ThemeDefinition>(BuiltInThemes());
        var errors = new List<string>();
        foreach (var dir in new[] { UserDir, PortableDir }.OfType<string>().Where(System.IO.Directory.Exists))
        {
            foreach (var file in System.IO.Directory.EnumerateFiles(dir, "*.json").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    var theme = JsonSerializer.Deserialize<ThemeDefinition>(File.ReadAllText(file), JsonOptions)
                                ?? throw new InvalidDataException("arquivo vazio");
                    theme.Id = "file:" + file;
                    theme.Directory = dir;
                    themes.Add(theme);
                }
                catch (Exception ex)
                {
                    errors.Add($"{Path.GetFileName(file)}: {ex.Message.Split('\n')[0]}");
                }
            }
        }
        Themes = themes;
        Errors = errors;
        ThemesChanged?.Invoke();
    }

    /// <summary>Raised after the theme list is (re)loaded, e.g. a file changed in the folder.</summary>
    public event Action? ThemesChanged;

    public ThemeDefinition Find(string? id) =>
        Themes.FirstOrDefault(t => t.Id == id) ?? Themes.First(t => t.Id == DefaultId);

    public void Apply(string? id)
    {
        var theme = Find(id);
        var resources = BuildResources(theme);
        var app = Application.Current.Resources;
        foreach (var key in resources.Keys) app[key] = resources[key];

        CurrentId = theme.Id;
        if (_settings.Data.Theme != theme.Id)
        {
            _settings.Data.Theme = theme.Id;
            _settings.Save();
        }
    }

    /// <summary>
    /// The theme tokens as resources. Applied app-wide by <see cref="Apply"/>, or set on a single
    /// element (settings preview) so only that element is restyled.
    /// </summary>
    public static ResourceDictionary BuildResources(ThemeDefinition theme)
    {
        var fallback = new ThemeDefinition();
        var r = new ResourceDictionary();

        r["ThemeBackground"] = theme.BackgroundGradient is { Length: >= 2 } stops
            ? Gradient(stops, Color(theme.Background, fallback.Background))
            : Brush(theme.Background, fallback.Background);
        r["ThemeBorder"] = Brush(theme.Border, fallback.Border);
        r["ThemeBorderThickness"] = new Thickness(Math.Clamp(theme.BorderThickness, 0, 8));
        r["ThemeCornerRadius"] = new CornerRadius(Math.Clamp(theme.CornerRadius, 0, 40));
        r["ThemeCoverRadius"] = new CornerRadius(Math.Clamp(theme.CoverRadius, 0, 42));
        r["ThemeForeground"] = Brush(theme.Foreground, fallback.Foreground);
        r["ThemeSecondary"] = Brush(theme.SecondaryForeground, fallback.SecondaryForeground);
        r["AccentBrush"] = Brush(theme.Accent, fallback.Accent);
        r["ThemeTrack"] = Brush(theme.Track, fallback.Track);
        r["ThemeHover"] = Brush(theme.Hover, fallback.Hover);
        r["ThemeFont"] = Font(theme.FontFamily, fallback.FontFamily);
        r["ThemeShadow"] = theme.ShadowOpacity > 0
            ? Frozen(new DropShadowEffect { BlurRadius = 14, ShadowDepth = 2, Opacity = Math.Min(theme.ShadowOpacity, 1) })
            : null;

        var backdrop = theme.Backdrop?.ToLowerInvariant();
        r["ThemeCoverBackdropVisibility"] = backdrop == "cover" ? Visibility.Visible : Visibility.Collapsed;
        r["ThemeImageBrush"] = backdrop == "image" ? (object?)LoadImage(theme) ?? Brushes.Transparent : Brushes.Transparent;
        r["ThemeOverlay"] = Brush(theme.Overlay, fallback.Overlay);
        return r;
    }

    public static void OpenFolder()
    {
        EnsureUserDir();
        System.Diagnostics.Process.Start("explorer.exe", UserDir);
    }

    void Watch()
    {
        _watcher = new FileSystemWatcher(UserDir)
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
            IncludeSubdirectories = false,
            EnableRaisingEvents = true,
        };
        // Editors fire several events per save; reload once things settle.
        FileSystemEventHandler restart = (_, _) => Application.Current.Dispatcher.BeginInvoke(() =>
        {
            _reloadDebounce.Stop();
            _reloadDebounce.Start();
        });
        _watcher.Changed += restart;
        _watcher.Created += restart;
        _watcher.Deleted += restart;
        _watcher.Renamed += (s, e) => restart(s, e);
    }

    static void EnsureUserDir()
    {
        if (System.IO.Directory.Exists(UserDir)) return;
        System.IO.Directory.CreateDirectory(UserDir);
        File.WriteAllText(Path.Combine(UserDir, "exemplo-oceano.json"), ExampleTheme);
    }

    /// <summary>Background image, restricted to the theme's own folder.</summary>
    static ImageBrush? LoadImage(ThemeDefinition theme)
    {
        if (theme.Directory is null || string.IsNullOrWhiteSpace(theme.BackgroundImage)) return null;
        try
        {
            var root = Path.GetFullPath(theme.Directory) + Path.DirectorySeparatorChar;
            var path = Path.GetFullPath(Path.Combine(root, theme.BackgroundImage));
            if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !File.Exists(path)) return null;

            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = 800;
            image.UriSource = new Uri(path);
            image.EndInit();
            image.Freeze();
            return Frozen(new ImageBrush(image) { Stretch = Stretch.UniformToFill });
        }
        catch
        {
            return null;
        }
    }

    static Color Color(string? value, string fallback)
    {
        try { return (Color)ColorConverter.ConvertFromString(value ?? fallback); }
        catch { return (Color)ColorConverter.ConvertFromString(fallback); }
    }

    static SolidColorBrush Brush(string? value, string fallback) => Frozen(new SolidColorBrush(Color(value, fallback)));

    static LinearGradientBrush Gradient(string[] colors, Color fallback)
    {
        var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
        for (var i = 0; i < colors.Length; i++)
            brush.GradientStops.Add(new GradientStop(Color(colors[i], fallback.ToString()), i / (double)(colors.Length - 1)));
        return Frozen(brush);
    }

    static FontFamily Font(string? value, string fallback) =>
        new(string.IsNullOrWhiteSpace(value) ? fallback : $"{value}, {fallback}");

    static T Frozen<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }

    static IEnumerable<ThemeDefinition> BuiltInThemes() =>
    [
        new() { Id = DefaultId, BuiltIn = true, Name = "Escuro" },
        new()
        {
            Id = "builtin:claro", BuiltIn = true, Name = "Claro",
            Background = "#FFFAFAFA", Border = "#1F000000", Foreground = "#1B1B1B",
            SecondaryForeground = "#8A1B1B1B", Accent = "#0067C0", Track = "#26000000",
            Hover = "#14000000", ShadowOpacity = 0.25,
        },
        new()
        {
            Id = "builtin:vidro", BuiltIn = true, Name = "Vidro (capa)",
            Background = "#FF202020", Border = "#40FFFFFF", Backdrop = "cover", Overlay = "#99101010",
            Accent = "#1ED760", SecondaryForeground = "#CCFFFFFF", Track = "#40FFFFFF",
            Hover = "#26FFFFFF", CornerRadius = 14, CoverRadius = 10,
        },
        new()
        {
            Id = "builtin:neon", BuiltIn = true, Name = "Neon",
            BackgroundGradient = ["#F21A0B2E", "#F23B0F4F", "#F2120C3A"], Border = "#FFFF2BD6",
            BorderThickness = 1.5, Accent = "#00F0FF", SecondaryForeground = "#FFC9A8FF",
            Track = "#4DFF2BD6", Hover = "#33FF2BD6", CornerRadius = 16, CoverRadius = 12, ShadowOpacity = 0.7,
        },
        new()
        {
            Id = "builtin:terminal", BuiltIn = true, Name = "Terminal",
            Background = "#FA0C0C0C", Border = "#FF33FF66", Foreground = "#33FF66",
            SecondaryForeground = "#FF1F9E40", Accent = "#33FF66", Track = "#331F9E40",
            Hover = "#2633FF66", CornerRadius = 0, CoverRadius = 0, ShadowOpacity = 0,
            FontFamily = "Cascadia Mono, Consolas",
        },
    ];

    const string ExampleTheme = """
        // Tema de exemplo do MiniPlayer. Copie este arquivo, mude o "name" e as cores.
        // Salvar o arquivo recarrega o tema na hora. Escolha em: bandeja → Temas.
        // Cores: "#RRGGBB" ou "#AARRGGBB" (AA = transparência) ou nomes ("White").
        {
          "name": "Oceano (exemplo)",
          "author": "MiniPlayer",

          // Fundo: cor única ou degradê diagonal (2+ cores)
          "background": "#F20B2540",
          "backgroundGradient": ["#F2082032", "#F20E4D64", "#F2137C8B"],
          "border": "#5590E0EF",
          "borderThickness": 1,
          "cornerRadius": 12,

          // Textos e destaques
          "foreground": "#FFFFFF",
          "secondaryForeground": "#B3D6F5FF",
          "accent": "#7DF9FF",
          "track": "#4090E0EF",
          "hover": "#2690E0EF",

          "coverRadius": 8,
          "fontFamily": "Segoe UI Variable Text",
          "shadowOpacity": 0.5,

          // "none" | "cover" (capa desfocada) | "image" (backgroundImage, arquivo nesta pasta)
          "backdrop": "none",
          "backgroundImage": null,
          // Película por cima do fundo de capa/imagem p/ o texto continuar legível
          "overlay": "#00000000"
        }
        """;
}
