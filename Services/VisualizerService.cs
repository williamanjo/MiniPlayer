using System.Windows.Threading;

namespace MiniPlayer.Services;

/// <summary>
/// Samples the playing app's audio peak level ~30 times a second for the visualizer.
/// Windows only exposes a peak per session (no spectrum), so bars are drawn from the level
/// history; an automatic gain keeps quiet tracks from looking flat.
/// </summary>
public sealed class VisualizerService
{
    public const int HistoryLength = 64;

    readonly Func<string?> _processName;
    readonly Func<bool> _isPlaying;
    readonly DispatcherTimer _sample = new() { Interval = TimeSpan.FromMilliseconds(33) };
    readonly DispatcherTimer _resolve = new() { Interval = TimeSpan.FromSeconds(2) };
    readonly float[] _history = new float[HistoryLength];
    List<AudioService.IAudioMeterInformation> _meters = [];
    string? _metersFor;
    int _head;
    float _gain = 0.2f;
    bool _enabled;

    public VisualizerService(Func<string?> processName, Func<bool> isPlaying)
    {
        _processName = processName;
        _isPlaying = isPlaying;
        _sample.Tick += (_, _) => Sample();
        _resolve.Tick += (_, _) => ResolveMeters(force: false);
    }

    /// <summary>Raised after each sample (UI thread).</summary>
    public event Action? Updated;

    /// <summary>Smoothed current level, 0..1.</summary>
    public float Level { get; private set; }

    /// <summary>Level <paramref name="age"/> samples ago (0 = newest), 0..1.</summary>
    public float History(int age) => _history[((_head - 1 - age) % HistoryLength + HistoryLength) % HistoryLength];

    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value) return;
            _enabled = value;
            if (value)
            {
                ResolveMeters(force: true);
                _sample.Start();
                _resolve.Start();
            }
            else
            {
                _sample.Stop();
                _resolve.Stop();
                Array.Clear(_history);
                Level = 0;
                Updated?.Invoke();
            }
        }
    }

    void ResolveMeters(bool force)
    {
        var process = AudioService.ProcessNameFor(_processName());
        if (!force && process == _metersFor && _meters.Count > 0) return;
        _metersFor = process;
        _meters = process is null ? [] : AudioService.Meters(process);
    }

    void Sample()
    {
        float raw = 0;
        if (_isPlaying() && _meters.Count > 0)
        {
            var peak = AudioService.Peak(_meters);
            if (peak is null) ResolveMeters(force: true); // session went away
            raw = peak ?? 0;
        }

        // Automatic gain: follow loud peaks fast, forget them slowly.
        _gain = Math.Max(Math.Max(raw, _gain * 0.995f), 0.05f);
        var normalized = Math.Clamp(raw / _gain, 0f, 1f);

        // Fast attack, slow release so bars fall smoothly.
        Level = normalized > Level ? normalized : Level * 0.82f;
        _history[_head] = Level;
        _head = (_head + 1) % HistoryLength;
        Updated?.Invoke();
    }
}
