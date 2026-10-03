using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32;

namespace MiniPlayer.Services;

public enum PlayerMode { Floating, Taskbar }

/// <summary>What the mouse wheel does over the player.</summary>
public enum WheelAction { Volume, Track }

/// <summary>Where the audio bars go in the floating player.</summary>
public enum VisualizerPosition { Controls, Cover, Bottom }

/// <summary>What the middle mouse button does over the player.</summary>
public enum MiddleClickAction { Mute, PlayPause }

public sealed class AppSettings
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public PlayerMode Mode { get; set; } = PlayerMode.Floating;
    public bool PinOnTop { get; set; } = true;
    /// <summary>UI language: null/"auto" = Windows language, "en" or "pt-BR".</summary>
    public string? Language { get; set; }
    public double? Left { get; set; }
    public double? Top { get; set; }
    /// <summary>Bottom edge of the floating player; it grows upward from here.</summary>
    public double? Bottom { get; set; }
    /// <summary>Floating player zoom (1 = 100%).</summary>
    public double FloatingScale { get; set; } = 1;
    /// <summary>Floating player content size (before zoom); null = default.</summary>
    public double? FloatingWidth { get; set; }
    public double? FloatingHeight { get; set; }
    /// <summary>Distance (DIP) between the overlay and the notification area.</summary>
    public double TaskbarOffset { get; set; }
    /// <summary>Monitor whose taskbar hosts the overlay (e.g. \\.\DISPLAY2); null = primary.</summary>
    public string? TaskbarMonitor { get; set; }
    /// <summary>Taskbar overlay disappears when nothing plays (3 s) or after a long pause (30 s).</summary>
    public bool AutoHideWhenIdle { get; set; } = true;
    /// <summary>Fetch synced lyrics from lrclib.net (sends title/artist).</summary>
    public bool ShowLyrics { get; set; }
    /// <summary>Theme id: "builtin:..." or "file:&lt;path&gt;".</summary>
    public string? Theme { get; set; }
    /// <summary>"Manter atualizado": install updates on start, pre-download them while running.</summary>
    public bool AutoUpdate { get; set; }
    /// <summary>Version that ran last time; a change means an update was just applied.</summary>
    public string? LastVersion { get; set; }

    /// <summary>Audio bars on the cover / taskbar.</summary>
    public bool ShowVisualizer { get; set; } = true;

    /// <summary>Global hotkeys: action name → "Ctrl+Alt+Right" ("" = off). Missing = default.</summary>
    public Dictionary<string, string> Hotkeys { get; set; } = [];

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public WheelAction WheelAction { get; set; } = WheelAction.Volume;
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public WheelAction TaskbarWheelAction { get; set; } = WheelAction.Volume;
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public MiddleClickAction MiddleClickAction { get; set; } = MiddleClickAction.Mute;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public VisualizerPosition VisualizerPosition { get; set; } = VisualizerPosition.Controls;

    /// <summary>Pause while an app uses the microphone (calls).</summary>
    public bool PauseOnCall { get; set; }
    public bool ResumeAfterCall { get; set; } = true;
    /// <summary>Microphone apps (registry key names) that do not count as a call.</summary>
    public List<string> CallIgnore { get; set; } = [];

    /// <summary>Lower the music while another app makes sound.</summary>
    public bool DuckOtherAudio { get; set; }
    /// <summary>Music volume while ducked, as a fraction of the normal volume.</summary>
    public double DuckLevel { get; set; } = 0.3;

    /// <summary>Keep a history of listened tracks (local file only).</summary>
    public bool RecordHistory { get; set; } = true;

    /// <summary>Write "now playing" files for OBS.</summary>
    public bool NowPlayingEnabled { get; set; }
    public string? NowPlayingFolder { get; set; }
    public string? NowPlayingTemplate { get; set; } = NowPlayingService.DefaultTemplate;
    public bool NowPlayingClearWhenPaused { get; set; } = true;

    /// <summary>Sleep timer lowers the volume during the last seconds before pausing.</summary>
    public bool SleepFade { get; set; } = true;

    /// <summary>Saved hotkeys merged over the defaults.</summary>
    public Dictionary<string, string> HotkeyBindings()
    {
        var result = HotkeyService.Defaults.ToDictionary(d => d.Key.ToString(), d => d.Value);
        foreach (var (action, combo) in Hotkeys) result[action] = combo;
        return result;
    }
}

public sealed class SettingsService
{

    static readonly string Dir = AppInfo.DataDir;
    static readonly string FilePath = Path.Combine(Dir, "settings.json");
    static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public AppSettings Data { get; private set; } = new();

    public static SettingsService Load()
    {
        var service = new SettingsService();
        try
        {
            if (File.Exists(FilePath))
                service.Data = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), JsonOptions) ?? new();
        }
        catch
        {
            // Corrupt file: fall back to defaults.
        }
        return service;
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(Data, JsonOptions));
        }
        catch
        {
            // Settings are best effort.
        }
    }
}
