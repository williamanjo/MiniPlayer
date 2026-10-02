using System.Windows.Threading;

namespace MiniPlayer.Services;

/// <summary>
/// "Timer para dormir": pauses after a delay or at the end of the current track, optionally
/// fading the app volume out first (and restoring it after the pause).
/// </summary>
public sealed class SleepTimerService
{
    static readonly TimeSpan FadeDuration = TimeSpan.FromSeconds(20);

    readonly MediaService _media;
    readonly SettingsService _settings;
    readonly Func<TimeSpan?> _timeLeftInTrack;
    readonly Func<string> _trackKey;
    readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromMilliseconds(500) };

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
            // Pause just before the end; if there is no timeline, when the next track starts.
            var trackLeft = _timeLeftInTrack();
            if (_trackKey() != _startTrack) left = TimeSpan.Zero;
            else if (trackLeft is { } t) left = t - TimeSpan.FromSeconds(0.8);
            else return;
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

        if (_settings.Data.SleepFade && left < FadeDuration) Fade(left);

        var text = RemainingText;
        if (text != _lastText)
        {
            _lastText = text;
            Changed?.Invoke();
        }
    }

    void Fade(TimeSpan left)
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
            AudioService.SetVolume(process, start * (float)(left / FadeDuration));
    }

    void RestoreVolume()
    {
        if (_fadeProcess is { } process && _fadeStartVolume is { } volume) AudioService.SetVolume(process, volume);
        _fadeProcess = null;
        _fadeStartVolume = null;
    }
}
