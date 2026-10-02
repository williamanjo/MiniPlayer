using System.Windows.Threading;
using Microsoft.Win32;

namespace MiniPlayer.Services;

/// <param name="Key">Registry key name (identifies the app in settings).</param>
/// <param name="Name">Friendly name, e.g. "chrome.exe" or "MSTeams".</param>
public sealed record MicApp(string Key, string Name, bool InUse, DateTime? LastUsed);

/// <summary>
/// Pauses the music while an app uses the microphone (a call) and resumes it afterwards.
/// Windows records microphone use per app (the privacy "in use" indicator); polling that
/// works for Teams, Meet, Discord, Zoom… without integrating with each of them.
/// </summary>
public sealed class CallMonitorService
{
    const string MicKey = @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\microphone";
    static readonly TimeSpan ResumeDelay = TimeSpan.FromSeconds(2);

    readonly MediaService _media;
    readonly SettingsService _settings;
    readonly Func<bool> _isPlaying;
    readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    bool _pausedByCall;
    DateTime? _micFreeSince;

    public CallMonitorService(MediaService media, SettingsService settings, Func<bool> isPlaying)
    {
        _media = media;
        _settings = settings;
        _isPlaying = isPlaying;
        _timer.Tick += async (_, _) => await TickAsync();
        _timer.Start();
    }

    /// <summary>"call started" (app name) / "call ended" (null) while the music was paused for it.</summary>
    public event Action<string?>? CallChanged;

    /// <summary>Apps that have used the microphone, most recent first.</summary>
    public static List<MicApp> MicApps()
    {
        var apps = new List<MicApp>();
        using var root = Registry.CurrentUser.OpenSubKey(MicKey);
        if (root is null) return apps;

        foreach (var name in root.GetSubKeyNames().Where(n => n != "NonPackaged"))
        {
            using var key = root.OpenSubKey(name);
            if (Read(key, name, name.Split('_')[0]) is { } app) apps.Add(app);
        }
        using (var nonPackaged = root.OpenSubKey("NonPackaged"))
        {
            foreach (var name in nonPackaged?.GetSubKeyNames() ?? [])
            {
                using var key = nonPackaged!.OpenSubKey(name);
                // Desktop apps are stored by path with '#' instead of '\'.
                if (Read(key, name, name.Split('#').Last()) is { } app) apps.Add(app);
            }
        }
        return [.. apps.OrderByDescending(a => a.InUse).ThenByDescending(a => a.LastUsed)];
    }

    static MicApp? Read(RegistryKey? key, string id, string friendly)
    {
        if (key?.GetValue("LastUsedTimeStart") is not long start || start <= 0) return null; // never used
        var stop = key.GetValue("LastUsedTimeStop") as long? ?? 0;
        DateTime? last = null;
        try { last = DateTime.FromFileTime(Math.Max(start, stop)); }
        catch { /* bad value */ }
        return new MicApp(id, friendly, stop == 0, last);
    }

    async Task TickAsync()
    {
        if (!_settings.Data.PauseOnCall)
        {
            _pausedByCall = false;
            return;
        }

        var ignored = _settings.Data.CallIgnore;
        var caller = MicApps().FirstOrDefault(a => a.InUse && !ignored.Contains(a.Key));
        if (caller is not null)
        {
            _micFreeSince = null;
            // Pause once per call; if the user presses play during the call, respect it.
            if (!_pausedByCall && _isPlaying())
            {
                _pausedByCall = true;
                await _media.PauseAsync();
                CallChanged?.Invoke(caller.Name);
            }
            return;
        }

        if (!_pausedByCall) return;
        _micFreeSince ??= DateTime.Now;
        if (DateTime.Now - _micFreeSince < ResumeDelay) return; // short mic gaps are not the end of the call

        _pausedByCall = false;
        _micFreeSince = null;
        if (_settings.Data.ResumeAfterCall && !_isPlaying()) await _media.PlayAsync();
        CallChanged?.Invoke(null);
    }
}
