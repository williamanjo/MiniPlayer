using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32;

namespace MiniPlayer.Services;

public enum PlayerMode { Floating, Taskbar }

public sealed class AppSettings
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public PlayerMode Mode { get; set; } = PlayerMode.Floating;
    public bool PinOnTop { get; set; } = true;
    public double? Left { get; set; }
    public double? Top { get; set; }
    /// <summary>Bottom edge of the floating player; it grows upward from here.</summary>
    public double? Bottom { get; set; }
    /// <summary>Distance (DIP) between the overlay and the notification area.</summary>
    public double TaskbarOffset { get; set; }
    /// <summary>Monitor whose taskbar hosts the overlay (e.g. \\.\DISPLAY2); null = primary.</summary>
    public string? TaskbarMonitor { get; set; }
    /// <summary>Taskbar overlay disappears when nothing plays (3 s) or after a long pause (30 s).</summary>
    public bool AutoHideWhenIdle { get; set; } = true;
    /// <summary>Fetch synced lyrics from lrclib.net (sends title/artist).</summary>
    public bool ShowLyrics { get; set; }
}

public sealed class SettingsService
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string RunValue = "MiniPlayer";

    static readonly string Dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MiniPlayer");
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

    public static bool AutoStart
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(RunValue) is string;
        }
        set
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (value)
                key.SetValue(RunValue, $"\"{Environment.ProcessPath}\"");
            else
                key.DeleteValue(RunValue, throwOnMissingValue: false);
        }
    }
}
