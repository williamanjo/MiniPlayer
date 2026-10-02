using System.Windows.Threading;

namespace MiniPlayer.Services;

/// <summary>
/// Lowers the music while another app makes sound (a video, a notification, a voice message)
/// and brings it back after a short silence. Detection uses each app's audio peak meter.
/// Limitation: tabs of the same browser share one audio session, so a video in another tab
/// of the music's browser cannot be told apart from the music.
/// </summary>
public sealed class DuckingService
{
    const float LoudThreshold = 0.02f;
    static readonly TimeSpan RestoreAfter = TimeSpan.FromSeconds(1.5);

    readonly MediaService _media;
    readonly SettingsService _settings;
    readonly Func<bool> _isPlaying;
    readonly Func<bool> _otherFeatureOwnsVolume;
    readonly DispatcherTimer _sample = new() { Interval = TimeSpan.FromMilliseconds(100) };
    readonly DispatcherTimer _refresh = new() { Interval = TimeSpan.FromSeconds(2) };
    List<AudioService.AudioApp> _others = [];
    string? _musicProcess;
    string? _duckedProcess;
    float _originalVolume;
    DateTime _lastLoud;

    public DuckingService(MediaService media, SettingsService settings, Func<bool> isPlaying, Func<bool> otherFeatureOwnsVolume)
    {
        _media = media;
        _settings = settings;
        _isPlaying = isPlaying;
        _otherFeatureOwnsVolume = otherFeatureOwnsVolume;
        _sample.Tick += (_, _) => Sample();
        _refresh.Tick += (_, _) => Refresh();
        Apply();
    }

    /// <summary>Starts or stops monitoring after the setting changed.</summary>
    public void Apply()
    {
        if (_settings.Data.DuckOtherAudio)
        {
            Refresh();
            _sample.Start();
            _refresh.Start();
        }
        else
        {
            _sample.Stop();
            _refresh.Stop();
            Restore();
        }
    }

    void Refresh()
    {
        _musicProcess = AudioService.ProcessNameFor(_media.CurrentAppId);
        var self = (uint)Environment.ProcessId;
        _others = [.. AudioService.AllSessions().Where(a => a.ProcessName != _musicProcess && a.ProcessId != self)];
    }

    void Sample()
    {
        if (_otherFeatureOwnsVolume()) return; // e.g. sleep timer fading out

        var loud = false;
        foreach (var app in _others)
        {
            try
            {
                app.Meter.GetPeakValue(out var peak);
                if (peak > LoudThreshold) { loud = true; break; }
            }
            catch
            {
                // session ended; the next refresh drops it
            }
        }

        if (loud)
        {
            _lastLoud = DateTime.Now;
            if (_duckedProcess is null && _isPlaying() && _musicProcess is { } music && AudioService.Get(music) is { } current)
            {
                _duckedProcess = music;
                _originalVolume = current.Volume;
                AudioService.SetVolume(music, _originalVolume * (float)Math.Clamp(_settings.Data.DuckLevel, 0, 1));
            }
        }
        else if (_duckedProcess is not null && DateTime.Now - _lastLoud > RestoreAfter)
        {
            Restore();
        }
    }

    /// <summary>Puts the music volume back (also on exit / when turned off).</summary>
    public void Restore()
    {
        if (_duckedProcess is { } process) AudioService.SetVolume(process, _originalVolume);
        _duckedProcess = null;
    }
}
