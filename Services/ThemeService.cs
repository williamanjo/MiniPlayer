using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MiniPlayer.Localization;

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

    // ---- background
    /// <summary>Direction of <see cref="BackgroundGradient"/> in degrees (0 = left→right, 90 = top→bottom).</summary>
    public double BackgroundAngle { get; set; } = 45;
    /// <summary>Blur of the "cover" backdrop (0 = sharp … 80 = very soft).</summary>
    public double BackdropBlur { get; set; } = 30;
    /// <summary>Opacity of <see cref="BackgroundImage"/> (0..1).</summary>
    public double BackgroundImageOpacity { get; set; } = 1;

    // ---- text
    /// <summary>Font of the song title (null = <see cref="FontFamily"/>).</summary>
    public string? TitleFont { get; set; }
    public double TitleSize { get; set; } = 14;
    /// <summary>"normal", "semibold" or "bold".</summary>
    public string TitleWeight { get; set; } = "semibold";
    public double SubtitleSize { get; set; } = 12;
    /// <summary>Current lyric line (null = <see cref="Foreground"/>).</summary>
    public string? LyricsColor { get; set; }
    /// <summary>Previous/next lyric lines (null = <see cref="SecondaryForeground"/>).</summary>
    public string? LyricsDimColor { get; set; }

    // ---- buttons
    /// <summary>Control icons (null = <see cref="Foreground"/>).</summary>
    public string? IconColor { get; set; }
    /// <summary>"plain" or "circle" (filled circle behind play/pause).</summary>
    public string PlayButton { get; set; } = "plain";
    /// <summary>Circle color (null = <see cref="Accent"/>).</summary>
    public string? PlayButtonColor { get; set; }
    /// <summary>Play/pause icon on the circle (null = white).</summary>
    public string? PlayIconColor { get; set; }

    // ---- cover
    /// <summary>"rounded" or "circle" (vinyl look).</summary>
    public string CoverShape { get; set; } = "rounded";
    /// <summary>A circular cover spins while the music plays.</summary>
    public bool CoverSpin { get; set; }
    public string? CoverBorder { get; set; }
    public double CoverBorderThickness { get; set; }
    /// <summary>0 = no shadow under the cover.</summary>
    public double CoverShadowOpacity { get; set; }

    // ---- progress / visualizer
    /// <summary>Two or more colors for the progress bar (null = <see cref="Accent"/>).</summary>
    public string[]? ProgressGradient { get; set; }
    public double ProgressHeight { get; set; } = 4;
    /// <summary>Two or more colors for the audio bars, bottom → top (null = <see cref="Accent"/>).</summary>
    public string[]? VisualizerGradient { get; set; }
    public double VisualizerOpacity { get; set; } = 0.85;

    // ---- shadow
    public string ShadowColor { get; set; } = "#000000";
    public double ShadowBlur { get; set; } = 14;
    public double ShadowDepth { get; set; } = 2;

    [JsonIgnore] public string Id { get; set; } = "";
    [JsonIgnore] public string? Directory { get; set; }
    [JsonIgnore] public bool BuiltIn { get; set; }
}

public sealed class ThemeService
{
    public const string DefaultId = "builtin:escuro";

    public static readonly string UserDir = AppInfo.ThemesDir;

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

    /// <summary>Raised after a theme was applied (e.g. to start/stop the spinning vinyl cover).</summary>
    public event Action<ThemeDefinition>? Applied;

    public ThemeDefinition Current => Find(CurrentId);

    static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // keep accents readable
    };

    public static string ToJson(ThemeDefinition theme) => JsonSerializer.Serialize(theme, WriteOptions);

    /// <summary>Deep copy (the editor works on one), dropping the built-in flag.</summary>
    public static ThemeDefinition Copy(ThemeDefinition theme)
    {
        var copy = JsonSerializer.Deserialize<ThemeDefinition>(ToJson(theme), JsonOptions)!;
        copy.Directory = theme.Directory;
        return copy;
    }

    /// <summary>Saves a theme into the themes folder (overwrites its own file) and applies it.</summary>
    public string Save(ThemeDefinition theme, string? existingFile)
    {
        EnsureUserDir();
        var path = existingFile ?? UniquePath(theme.Name);
        File.WriteAllText(path, ToJson(theme));
        Load();
        var id = "file:" + path;
        Apply(id);
        return id;
    }

    /// <summary>Copies a theme file (and its background image, if any) into the themes folder.</summary>
    public string Import(string sourcePath)
    {
        var theme = JsonSerializer.Deserialize<ThemeDefinition>(File.ReadAllText(sourcePath), JsonOptions)
                    ?? throw new InvalidDataException("empty theme");
        EnsureUserDir();
        if (theme.BackgroundImage is { } image)
        {
            var src = Path.Combine(Path.GetDirectoryName(sourcePath)!, image);
            if (File.Exists(src)) File.Copy(src, Path.Combine(UserDir, Path.GetFileName(image)), overwrite: true);
            theme.BackgroundImage = Path.GetFileName(image);
        }
        var path = UniquePath(theme.Name);
        File.WriteAllText(path, ToJson(theme));
        Load();
        return "file:" + path;
    }

    /// <summary>Writes a theme to a file chosen by the user (copies its background image next to it).</summary>
    public static void Export(ThemeDefinition theme, string targetPath)
    {
        File.WriteAllText(targetPath, ToJson(theme));
        if (theme.BackgroundImage is { } image && theme.Directory is { } dir && File.Exists(Path.Combine(dir, image)))
            File.Copy(Path.Combine(dir, image), Path.Combine(Path.GetDirectoryName(targetPath)!, Path.GetFileName(image)), overwrite: true);
    }

    static string UniquePath(string name)
    {
        var safe = string.Concat((string.IsNullOrWhiteSpace(name) ? "tema" : name).Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '-' : c)).Trim();
        var path = Path.Combine(UserDir, safe + ".json");
        for (var i = 2; File.Exists(path); i++) path = Path.Combine(UserDir, $"{safe} ({i}).json");
        return path;
    }

    /// <summary>File behind a folder theme, or null for built-ins.</summary>
    public static string? FileOf(ThemeDefinition theme) => theme.Id.StartsWith("file:") ? theme.Id[5..] : null;

    public ThemeDefinition Find(string? id) =>
        Themes.FirstOrDefault(t => t.Id == id) ?? Themes.First(t => t.Id == DefaultId);

    public void Apply(string? id)
    {
        var theme = Find(id);
        var resources = BuildResources(theme);
        var app = Application.Current.Resources;
        foreach (var key in resources.Keys) app[key] = resources[key];

        CurrentId = theme.Id;
        Applied?.Invoke(theme);
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
            ? Gradient(stops, Color(theme.Background, fallback.Background), theme.BackgroundAngle)
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
            ? Frozen(new DropShadowEffect
            {
                BlurRadius = Math.Clamp(theme.ShadowBlur, 0, 60),
                ShadowDepth = Math.Clamp(theme.ShadowDepth, 0, 30),
                Color = Color(theme.ShadowColor, fallback.ShadowColor),
                Opacity = Math.Min(theme.ShadowOpacity, 1),
            })
            : null;

        // text
        r["ThemeTitleFont"] = Font(theme.TitleFont ?? theme.FontFamily, fallback.FontFamily);
        var titleSize = Math.Clamp(theme.TitleSize, 9, 32);
        r["ThemeTitleSize"] = titleSize;
        r["ThemeTitleSizeLarge"] = titleSize + 2;
        r["ThemeTitleSizeSmall"] = Math.Max(9, titleSize - 1);
        r["ThemeTitleWeight"] = theme.TitleWeight?.ToLowerInvariant() switch
        {
            "normal" or "regular" => FontWeights.Normal,
            "bold" => FontWeights.Bold,
            _ => FontWeights.SemiBold,
        };
        r["ThemeSubtitleSize"] = Math.Clamp(theme.SubtitleSize, 8, 24);
        r["ThemeLyrics"] = Brush(theme.LyricsColor ?? theme.Foreground, fallback.Foreground);
        r["ThemeLyricsDim"] = Brush(theme.LyricsDimColor ?? theme.SecondaryForeground, fallback.SecondaryForeground);

        // buttons
        r["ThemeIcon"] = Brush(theme.IconColor ?? theme.Foreground, fallback.Foreground);
        var circle = theme.PlayButton?.ToLowerInvariant() == "circle";
        r["ThemePlayBackground"] = circle ? Brush(theme.PlayButtonColor ?? theme.Accent, fallback.Accent) : Brushes.Transparent;
        r["ThemePlayForeground"] = circle
            ? Brush(theme.PlayIconColor ?? "#FFFFFF", "#FFFFFF")
            : Brush(theme.IconColor ?? theme.Foreground, fallback.Foreground);
        r["ThemePlayCorner"] = new CornerRadius(circle ? 20 : 5);
        r["ThemePlaySize"] = circle ? 40d : 44d;

        // cover
        var round = theme.CoverShape?.ToLowerInvariant() == "circle";
        if (round) r["ThemeCoverRadius"] = new CornerRadius(9999); // a circle, whatever the size
        r["ThemeCoverBorder"] = theme.CoverBorder is null ? Brushes.Transparent : Brush(theme.CoverBorder, "#00000000");
        r["ThemeCoverBorderThickness"] = new Thickness(Math.Clamp(theme.CoverBorderThickness, 0, 10));
        r["ThemeCoverShadow"] = theme.CoverShadowOpacity > 0
            ? Frozen(new DropShadowEffect { BlurRadius = 16, ShadowDepth = 3, Opacity = Math.Min(theme.CoverShadowOpacity, 1) })
            : null;

        // progress / visualizer
        r["ThemeProgress"] = theme.ProgressGradient is { Length: >= 2 } progress
            ? Gradient(progress, Color(theme.Accent, fallback.Accent), 0)
            : Brush(theme.Accent, fallback.Accent);
        r["ThemeProgressHeight"] = Math.Clamp(theme.ProgressHeight, 1, 12);
        r["ThemeVisualizer"] = theme.VisualizerGradient is { Length: >= 2 } bars
            ? Gradient(bars, Color(theme.Accent, fallback.Accent), 270) // bottom → top
            : Brush(theme.Accent, fallback.Accent);
        r["ThemeVisualizerOpacity"] = Math.Clamp(theme.VisualizerOpacity, 0.1, 1);
        r["ThemeBackdropEffect"] = theme.BackdropBlur > 0
            ? Frozen(new BlurEffect { Radius = Math.Clamp(theme.BackdropBlur, 0, 80), KernelType = KernelType.Gaussian })
            : null;
        r["ThemeImageOpacity"] = Math.Clamp(theme.BackgroundImageOpacity, 0, 1);

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

    public static void EnsureUserDir()
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

    /// <param name="angle">Degrees: 0 = left→right, 90 = top→bottom, 45 = diagonal.</param>
    static LinearGradientBrush Gradient(string[] colors, Color fallback, double angle = 45)
    {
        var rad = angle * Math.PI / 180;
        var (dx, dy) = (Math.Cos(rad) / 2, Math.Sin(rad) / 2);
        var brush = new LinearGradientBrush { StartPoint = new Point(0.5 - dx, 0.5 - dy), EndPoint = new Point(0.5 + dx, 0.5 + dy) };
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

    const string BuiltInAuthor = "williamanjo";

    static IEnumerable<ThemeDefinition> BuiltInThemes() =>
        BuiltInList().Select(t => { t.Author ??= BuiltInAuthor; return t; });

    static IEnumerable<ThemeDefinition> BuiltInList() =>
    [
        new() { Id = DefaultId, BuiltIn = true, Name = Loc.T("theme_dark") },
        new()
        {
            Id = "builtin:claro", BuiltIn = true, Name = Loc.T("theme_light"),
            Background = "#FFFAFAFA", Border = "#1F000000", Foreground = "#1B1B1B",
            SecondaryForeground = "#8A1B1B1B", Accent = "#0067C0", Track = "#26000000",
            Hover = "#14000000", ShadowOpacity = 0.25,
        },
        new()
        {
            Id = "builtin:vidro", BuiltIn = true, Name = Loc.T("theme_glass"),
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
            Id = "builtin:vinil", BuiltIn = true, Name = Loc.T("theme_vinyl"),
            BackgroundGradient = ["#F7241A14", "#F7140E0B"], BackgroundAngle = 90, Border = "#33FFB347",
            Accent = "#FF8A3D", SecondaryForeground = "#B3FFE2C7", Track = "#33FFB347", Hover = "#26FFB347",
            CornerRadius = 18, CoverShape = "circle", CoverSpin = true, CoverBorder = "#FF0B0B0B",
            CoverBorderThickness = 6, CoverShadowOpacity = 0.55, PlayButton = "circle", PlayIconColor = "#FF1A120C",
            ProgressGradient = ["#FFB347", "#FF5E62"], VisualizerGradient = ["#FF5E62", "#FFB347"],
            TitleWeight = "bold", ShadowOpacity = 0.6, ShadowBlur = 22,
        },
        new()
        {
            Id = "builtin:terminal", BuiltIn = true, Name = "Terminal",
            Background = "#FA0C0C0C", Border = "#FF33FF66", Foreground = "#33FF66",
            SecondaryForeground = "#FF1F9E40", Accent = "#33FF66", Track = "#331F9E40",
            Hover = "#2633FF66", CornerRadius = 0, CoverRadius = 0, ShadowOpacity = 0,
            FontFamily = "Cascadia Mono, Consolas",
        },
        new()
        {
            // editorial: cream paper, serif type, square cover with a hairline
            Id = "builtin:papel", BuiltIn = true, Name = Loc.T("theme_paper"),
            Background = "#FFF4EEE1", Border = "#FF2B2118", BorderThickness = 1, Foreground = "#2B2118",
            SecondaryForeground = "#995A4636", Accent = "#B3261E", Track = "#262B2118", Hover = "#142B2118",
            IconColor = "#2B2118", CornerRadius = 3, CoverRadius = 0, CoverBorder = "#FF2B2118", CoverBorderThickness = 1,
            FontFamily = "Georgia", TitleSize = 16, TitleWeight = "normal", ProgressHeight = 2,
            LyricsColor = "#B3261E", LyricsDimColor = "#802B2118", ShadowOpacity = 0.18, ShadowBlur = 10,
        },
        new()
        {
            // synthwave: purple to pink sunset, yellow circle play
            Id = "builtin:retrowave", BuiltIn = true, Name = "Retrowave",
            BackgroundGradient = ["#FA2B1055", "#FA7A1F6B", "#FAD53369"], BackgroundAngle = 90,
            Border = "#FFFF2BD6", BorderThickness = 1.5, Accent = "#FFD319", SecondaryForeground = "#FFFFB8E6",
            Track = "#40FFFFFF", Hover = "#33FF2BD6", IconColor = "#FFFFB8E6", CornerRadius = 6, CoverRadius = 4,
            CoverBorder = "#FFFF2BD6", CoverBorderThickness = 2, PlayButton = "circle", PlayIconColor = "#FF2B1055",
            FontFamily = "Bahnschrift", TitleWeight = "bold", ProgressGradient = ["#FF2BD6", "#FFD319"],
            VisualizerGradient = ["#FF2BD6", "#FFD319"], ShadowColor = "#FF2BD6", ShadowOpacity = 0.55, ShadowBlur = 24, ShadowDepth = 0,
        },
        new()
        {
            // soft pastel green, very round, round cover (no spin)
            Id = "builtin:matcha", BuiltIn = true, Name = "Matcha",
            BackgroundGradient = ["#FFEDF4E6", "#FFDCEBD2"], BackgroundAngle = 90, Border = "#00000000", BorderThickness = 0,
            Foreground = "#1E3A2B", SecondaryForeground = "#A61E3A2B", Accent = "#4C8B5D", Track = "#264C8B5D", Hover = "#1F4C8B5D",
            IconColor = "#2F5E3F", CornerRadius = 28, CoverShape = "circle", CoverShadowOpacity = 0.25,
            PlayButton = "circle", PlayIconColor = "#FFFFFF", FontFamily = "Segoe UI Variable Display, Segoe UI",
            TitleWeight = "semibold", ProgressHeight = 6, ShadowColor = "#2F5E3F", ShadowOpacity = 0.25, ShadowBlur = 26, ShadowDepth = 4,
        },
        new()
        {
            // deep glass: heavily blurred cover under a teal/violet tint
            Id = "builtin:aurora", BuiltIn = true, Name = "Aurora",
            Background = "#FF0B1026", Backdrop = "cover", BackdropBlur = 70, Overlay = "#B30B1026",
            Border = "#407CF5D0", Accent = "#7CF5D0", SecondaryForeground = "#CCC9D6FF", Track = "#33FFFFFF", Hover = "#26FFFFFF",
            CornerRadius = 20, CoverRadius = 16, CoverShadowOpacity = 0.6, TitleSize = 15,
            ProgressGradient = ["#7CF5D0", "#8B7CFF"], VisualizerGradient = ["#8B7CFF", "#7CF5D0"], VisualizerOpacity = 0.7,
            LyricsColor = "#7CF5D0", ShadowColor = "#8B7CFF", ShadowOpacity = 0.5, ShadowBlur = 30, ShadowDepth = 0,
        },
        new()
        {
            // neo-brutalism: flat yellow, thick black outlines, hard offset shadow
            Id = "builtin:brutal", BuiltIn = true, Name = Loc.T("theme_brutal"),
            Background = "#FFFFE600", Border = "#FF000000", BorderThickness = 3, Foreground = "#000000",
            SecondaryForeground = "#CC000000", Accent = "#000000", Track = "#33000000", Hover = "#26000000", IconColor = "#000000",
            CornerRadius = 0, CoverRadius = 0, CoverBorder = "#FF000000", CoverBorderThickness = 3,
            PlayButton = "circle", PlayButtonColor = "#000000", PlayIconColor = "#FFE600",
            FontFamily = "Segoe UI", TitleFont = "Segoe UI Black", TitleWeight = "bold", ProgressHeight = 6,
            LyricsColor = "#000000", LyricsDimColor = "#80000000",
            ShadowColor = "#000000", ShadowOpacity = 1, ShadowBlur = 0, ShadowDepth = 8,
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
