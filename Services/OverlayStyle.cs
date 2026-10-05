namespace MiniPlayer.Services;

/// <summary>
/// Look of the OBS browser-source overlay (overlay.html), edited in the overlay editor and kept
/// in settings.json. Colors are "#RRGGBB" or "#AARRGGBB".
/// </summary>
public sealed class OverlayStyle
{
    /// <summary>Lines of text, top to bottom, each one shown or hidden.</summary>
    public List<OverlayItem> Items { get; set; } = DefaultItems();

    /// <summary>row (cover left), rowReverse (cover right), column (cover on top) or text (no cover).</summary>
    public string Layout { get; set; } = "row";
    /// <summary>left, center or right: where the card sits in the browser source and how text aligns.</summary>
    public string Align { get; set; } = "left";
    public string FontFamily { get; set; } = "Segoe UI";

    // Card
    public string Background { get; set; } = "#C7141414";
    /// <summary>Second color for a gradient background; null = solid.</summary>
    public string? Background2 { get; set; }
    public double BackgroundAngle { get; set; } = 135;
    public string BorderColor { get; set; } = "#33FFFFFF";
    public double BorderWidth { get; set; }
    public double Radius { get; set; } = 14;
    public double Padding { get; set; } = 12;
    public double Gap { get; set; } = 14;
    public double Width { get; set; } = 560;
    public double ShadowOpacity { get; set; } = 0.35;
    /// <summary>Blur of what is behind the card (only visible over other OBS sources).</summary>
    public double BackdropBlur { get; set; }

    // Cover
    public double CoverSize { get; set; } = 72;
    public double CoverRadius { get; set; } = 8;
    public bool CoverCircle { get; set; }
    public bool CoverSpin { get; set; }

    // Text
    public string Accent { get; set; } = "#60CDFF";
    /// <summary>Text of the label line; null = "Tocando agora" in the app language.</summary>
    public string? LabelText { get; set; }
    public double LabelSize { get; set; } = 12;
    public string? LabelColor { get; set; }
    public double TitleSize { get; set; } = 22;
    public string TitleColor { get; set; } = "#FFFFFF";
    public string TitleWeight { get; set; } = "semibold";
    public double ArtistSize { get; set; } = 16;
    public string ArtistColor { get; set; } = "#BFFFFFFF";
    public double SourceSize { get; set; } = 13;
    public string SourceColor { get; set; } = "#80FFFFFF";
    public double TimeSize { get; set; } = 12;
    public string TimeColor { get; set; } = "#99FFFFFF";
    /// <summary>Long titles scroll instead of ending in "…".</summary>
    public bool Marquee { get; set; }
    public bool TextShadow { get; set; }

    // Progress bar
    public double ProgressHeight { get; set; } = 4;
    public string TrackColor { get; set; } = "#33FFFFFF";
    /// <summary>Second color of the progress fill; null = accent only.</summary>
    public string? Accent2 { get; set; }

    /// <summary>On song change: fade, slide or none.</summary>
    public string Animation { get; set; } = "fade";

    public static List<OverlayItem> DefaultItems() =>
    [
        new("label", false), new("title", true), new("artist", true),
        new("source", false), new("progress", false), new("time", false),
    ];

    /// <summary>Missing/unknown items (older settings) are fixed up so every kind appears once.</summary>
    public void Normalize()
    {
        var known = DefaultItems().Select(i => i.Kind).ToList();
        Items = (Items ?? []).Where(i => known.Contains(i.Kind)).DistinctBy(i => i.Kind).ToList();
        foreach (var missing in DefaultItems().Where(d => Items.All(i => i.Kind != d.Kind)))
            Items.Add(missing);
    }

    public static OverlayStyle Preset(string name) => name switch
    {
        "minimal" => new()
        {
            Layout = "text", Background = "#00000000", ShadowOpacity = 0, Padding = 4, TextShadow = true,
            TitleSize = 26, ArtistSize = 18, Items = [new("label", false), new("title", true), new("artist", true),
                new("source", false), new("progress", false), new("time", false)],
        },
        "bar" => new()
        {
            Layout = "row", CoverSize = 56, CoverRadius = 6, Radius = 8, Width = 640, Background = "#E6101018",
            Accent = "#1ED760", Items = [new("title", true), new("artist", true), new("progress", true), new("time", true),
                new("label", false), new("source", false)],
        },
        "vinyl" => new()
        {
            Layout = "row", CoverCircle = true, CoverSpin = true, CoverSize = 88, Radius = 48, Padding = 10,
            Background = "#F2241A14", Background2 = "#F2140E0B", BorderColor = "#55FFB347", BorderWidth = 1.5,
            Accent = "#FF8A3D", Accent2 = "#FF5E62", LabelColor = "#FFB347", TitleColor = "#FFF3E6", ArtistColor = "#B3FFE2C7",
            Items = [new("label", true), new("title", true), new("artist", true), new("progress", true),
                new("source", false), new("time", false)],
        },
        "neon" => new()
        {
            Layout = "column", Align = "center", CoverSize = 140, CoverRadius = 14, Width = 360, Radius = 20, Padding = 16,
            Background = "#E61A0B2E", Background2 = "#E63B0F4F", BorderColor = "#FFFF2BD6", BorderWidth = 2,
            Accent = "#00F0FF", Accent2 = "#FF2BD6", LabelColor = "#00F0FF", ArtistColor = "#FFC9A8FF", Marquee = true,
            Animation = "slide",
            Items = [new("label", true), new("title", true), new("artist", true), new("progress", true), new("time", true),
                new("source", false)],
        },
        _ => new(),
    };
}

/// <summary>One line of the overlay: label, title, artist, source, progress or time.</summary>
public sealed class OverlayItem
{
    public OverlayItem() { }
    public OverlayItem(string kind, bool visible) { Kind = kind; Visible = visible; }

    public string Kind { get; set; } = "title";
    public bool Visible { get; set; } = true;
}
