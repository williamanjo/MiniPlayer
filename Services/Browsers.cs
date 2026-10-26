namespace MiniPlayer.Services;

/// <summary>
/// The browsers the app knows, in one place: a fragment of the media session's AUMID, the process
/// that owns its audio (volume, visualizer, ducking) and the name shown in the player.
/// </summary>
public static class Browsers
{
    public sealed record Browser(string Id, string Process, string Name);

    // first match wins: keep the more specific ids first
    public static readonly Browser[] All =
    [
        new("msedge", "msedge", "Edge"),
        new("chrome", "chrome", "Chrome"),
        new("firefox", "firefox", "Firefox"),
        new("308046b0af4a39cb", "firefox", "Firefox"), // Firefox's AUMID is a hash of its install path
        new("brave", "brave", "Brave"),
        new("opera", "opera", "Opera"),
        new("vivaldi", "vivaldi", "Vivaldi"),
        new("librewolf", "librewolf", "LibreWolf"),
        new("arc", "arc", "Arc"),
        new("zen", "zen", "Zen"),
    ];

    /// <summary>The browser behind a media session AUMID, or null for other apps.</summary>
    public static Browser? Find(string? aumid) =>
        string.IsNullOrEmpty(aumid) ? null : All.FirstOrDefault(b => aumid.Contains(b.Id, StringComparison.OrdinalIgnoreCase));
}
