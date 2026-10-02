using System.Windows.Threading;

namespace MiniPlayer.Services;

/// <summary>
/// "Timer para dormir": pauses after a delay or at the end of the current track, optionally
/// fading the app volume out first (and restoring it after the pause).
/// </summary>
public sealed class SleepTimerService
{
    static readonly TimeSpan FadeDuration = TimeSpan.FromSeconds(20);
    /// <summary>"End of track" without fade: silence this long before the end, so even a late pause is inaudible.</summary>
    static readonly TimeSpan EndOfTrackSilence = TimeSpan.FromSeconds(1.5);
    /// <summary>Pause this long before the track's end (the position is an estimate).</summary>
    static readonly TimeSpan EndOfTrackLead = TimeSpan.FromSeconds(0.3);

    readonly MediaService _media;
    readonly SettingsService _settings;
    readonly Func<TimeSpan?> _timeLeftInTrack;
    readonly Func<string> _trackKey;
    readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromMilliseconds(200) };

    DateTime? _endsAt;
    bool _endOfTrack;
    string? _startTrack;
    string? _fadeProcess;
    float? _fadeStartVolume;

    public SleepTimerService(MediaService media, SettingsService settings, Func<TimeSpan?> timeLeftInTrack, Func<string> trackKey)
    {
        _media = media;
        _settings = settings;
        _timeLeftInTrack = timeLeftInTrack;
        _trackKey = trackKey;
        _tick.Tick += async (_, _) => await TickAsync();
    }

    /// <summary>Raised when the timer starts, stops or its remaining time text changes.</summary>
    public event Action? Changed;

    public bool IsActive => _endsAt is not null || _endOfTrack;

    /// <summary>True while this timer is lowering the app volume (other features keep off it).</summary>
    public bool IsFading => _fadeProcess is not null;

    /// <summary>"23 min", "45 s", "fim da música" or null.</summary>
    public string? RemainingText
    {
        get
        {
            if (_endOfTrack) return "fim da música";
            if (_endsAt is not { } end) return null;
            var left = end - DateTime.Now;
            return left.TotalSeconds < 60 ? $"{Math.Max(0, (int)left.TotalSeconds)} s" : $"{(int)Math.Ceiling(left.TotalMinutes)} min";
        }
    }

    public void Start(TimeSpan duration)
    {
        Cancel();
        _endsAt = DateTime.Now + duration;
        _tick.Start();
        Changed?.Invoke();
    }

    public void StartEndOfTrack()
    {
        Cancel();
        _endOfTrack = true;
        _startTrack = _trackKey();
        _tick.Start();
        Changed?.Invoke();
    }

    public void Cancel()
    {
        RestoreVolume();
        _endsAt = null;
        _endOfTrack = false;
        _tick.Stop();
        Changed?.Invoke();
    }

    string? _lastText;

    async Task TickAsync()
    {
        TimeSpan left;
        if (_endOfTrack)
        {
            if (_trackKey() != _startTrack)
            {
                // The next track already started (position estimate was late): stop it right
                // away and rewind it, so it waits at 0:00 instead of having played a bit.
                await _media.PauseAsync();
                await _media.SeekAsync(TimeSpan.Zero);
                await Task.Delay(400);
                Cancel();
                return;
            }
            if (_timeLeftInTrack() is not { } trackLeft) return; // no timeline: wait for the track change
            left = trackLeft - EndOfTrackLead;
        }
        else
        {
            left = _endsAt!.Value - DateTime.Now;
        }

        if (left <= TimeSpan.Zero)
        {
            await _media.PauseAsync();
            await Task.Delay(400); // let the pause land before the volume comes back
            Cancel();
            return;
        }

        // Fade to silence: the long fade if enabled; at the end of a track always a short one,
        // so a pause that lands a moment late is still inaudible.
        var window = _settings.Data.SleepFade ? FadeDuration : _endOfTrack ? EndOfTrackSilence : TimeSpan.Zero;
        if (left < window) Fade(left, window);

        var text = RemainingText;
        if (text != _lastText)
        {
            _lastText = text;
            Changed?.Invoke();
        }
    }

    void Fade(TimeSpan left, TimeSpan window)
    {
        var process = AudioService.ProcessNameFor(_media.CurrentAppId);
        if (process is null) return;
        if (_fadeProcess != process)
        {
            RestoreVolume();
            _fadeProcess = process;
            _fadeStartVolume = AudioService.Get(process)?.Volume;
        }
        if (_fadeStartVolume is { } start)
            AudioService.SetVolume(process, start * (float)Math.Clamp(left / window, 0, 1));
    }

    void RestoreVolume()
    {
        if (_fadeProcess is { } process && _fadeStartVolume is { } volume) AudioService.SetVolume(process, volume);
        _fadeProcess = null;
        _fadeStartVolume = null;
    }
}
