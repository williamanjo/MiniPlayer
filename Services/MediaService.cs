using System.IO;
using Windows.Foundation;
using Windows.Media;
using Windows.Media.Control;
using Windows.Storage.Streams;
using Session = Windows.Media.Control.GlobalSystemMediaTransportControlsSession;
using SessionManager = Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager;

namespace MiniPlayer.Services;

public sealed record MediaSnapshot(
    bool HasSession,
    string Title,
    string Artist,
    string Source,
    bool IsPlaying,
    TimeSpan Position,
    TimeSpan Duration,
    DateTimeOffset LastUpdated,
    bool CanPlayPause,
    bool CanNext,
    bool CanPrevious,
    bool CanShuffle,
    bool CanRepeat,
    bool CanSeek,
    bool Shuffle,
    MediaPlaybackAutoRepeatMode Repeat,
    IRandomAccessStreamReference? Thumbnail)
{
    public static readonly MediaSnapshot Empty = new(
        false, "", "", "", false, default, default, default,
        false, false, false, false, false, false, false, MediaPlaybackAutoRepeatMode.None, null);
}

/// <summary>One entry of the "which tab to control" list.</summary>
public sealed record MediaSessionInfo(object Key, string Title, string Artist, string Source, bool IsPlaying, bool IsCurrent);

/// <summary>
/// Reads what is playing through Windows' System Media Transport Controls (SMTC).
/// Browsers publish their Media Session (YouTube, Spotify Web, SoundCloud, Deezer...) there.
/// </summary>
public sealed class MediaService
{
    static readonly string[] BrowserIds =
        ["msedge", "chrome", "firefox", "308046b0af4a39cb", "brave", "opera", "vivaldi", "arc", "zen", "librewolf"];

    readonly object _lock = new();
    SessionManager? _manager;
    List<Session> _watched = [];
    Session? _current;
    Session? _pinned;

    /// <summary>Raised (on any thread) whenever the current session or its state changes.</summary>
    public event Action? Changed;

    /// <summary>True while the user picked a session by hand (automatic switching paused).</summary>
    public bool IsPinned => _pinned is not null;

    /// <summary>App id of the session being shown (e.g. "Chrome", "MSEdge").</summary>
    public string? CurrentAppId
    {
        get
        {
            try { return _current?.SourceAppUserModelId; }
            catch { return null; }
        }
    }

    /// <summary>All sessions that currently publish media, for the source picker.</summary>
    public async Task<List<MediaSessionInfo>> GetSessionsAsync()
    {
        Session[] sessions;
        lock (_lock) sessions = [.. _watched];

        var list = new List<MediaSessionInfo>();
        foreach (var s in sessions)
        {
            try
            {
                var props = await s.TryGetMediaPropertiesAsync();
                var playing = s.GetPlaybackInfo().PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
                list.Add(new MediaSessionInfo(s, props?.Title ?? "", props?.Artist ?? "",
                    FriendlySource(s.SourceAppUserModelId), playing, ReferenceEquals(s, _current)));
            }
            catch
            {
                // Session closed while listing.
            }
        }
        return list;
    }

    /// <summary>Show this session until it closes; null goes back to automatic.</summary>
    public void Pin(object? key)
    {
        lock (_lock) _pinned = key as Session;
        Repick();
    }

    public async Task InitializeAsync()
    {
        _manager = await SessionManager.RequestAsync();
        _manager.SessionsChanged += (_, _) => Rewatch();
        _manager.CurrentSessionChanged += (_, _) => Repick();
        Rewatch();
    }

    void Rewatch()
    {
        if (_manager is null) return;
        lock (_lock)
        {
            foreach (var s in _watched)
            {
                s.PlaybackInfoChanged -= OnPlaybackInfoChanged;
                s.MediaPropertiesChanged -= OnMediaPropertiesChanged;
                s.TimelinePropertiesChanged -= OnTimelineChanged;
            }
            _watched = [.. _manager.GetSessions()];
            if (_pinned is not null && !_watched.Any(w => ReferenceEquals(w, _pinned))) _pinned = null;
            foreach (var s in _watched)
            {
                s.PlaybackInfoChanged += OnPlaybackInfoChanged;
                s.MediaPropertiesChanged += OnMediaPropertiesChanged;
                s.TimelinePropertiesChanged += OnTimelineChanged;
            }
        }
        Repick();
    }

    void OnPlaybackInfoChanged(Session sender, PlaybackInfoChangedEventArgs args) => Repick();
    void OnMediaPropertiesChanged(Session sender, MediaPropertiesChangedEventArgs args) => Repick();
    void OnTimelineChanged(Session sender, TimelinePropertiesChangedEventArgs args) => Repick();

    /// <summary>
    /// Pinned session wins; otherwise playing browser &gt; playing other app &gt; paused browser, sticky on ties.
    /// </summary>
    void Repick()
    {
        lock (_lock) _current = _pinned ?? PickBest() ?? TryGetCurrent();
        Changed?.Invoke();
    }

    Session? PickBest()
    {
        Session? best = null;
        var bestScore = -1;
        foreach (var s in _watched)
        {
            var score = Score(s);
            if (score > bestScore)
            {
                best = s;
                bestScore = score;
            }
        }
        return best;
    }

    int Score(Session s)
    {
        try
        {
            var playing = s.GetPlaybackInfo().PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            return (playing ? 4 : 0) + (IsBrowser(s.SourceAppUserModelId) ? 2 : 0) + (ReferenceEquals(s, _current) ? 1 : 0);
        }
        catch
        {
            return -1;
        }
    }

    Session? TryGetCurrent()
    {
        try { return _manager?.GetCurrentSession(); }
        catch { return null; }
    }

    static bool IsBrowser(string aumid)
    {
        var id = aumid.ToLowerInvariant();
        return BrowserIds.Any(id.Contains);
    }

    static string FriendlySource(string aumid)
    {
        var id = aumid.ToLowerInvariant();
        if (id.Contains("msedge")) return "Edge";
        if (id.Contains("chrome")) return "Chrome";
        if (id.Contains("firefox") || id.Contains("308046b0af4a39cb")) return "Firefox";
        if (id.Contains("brave")) return "Brave";
        if (id.Contains("opera")) return "Opera";
        if (id.Contains("vivaldi")) return "Vivaldi";
        if (id.Contains("spotify")) return "Spotify";
        var name = aumid.Split('!')[0].Split('\\', '/').Last();
        return name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
    }

    public async Task<MediaSnapshot> GetSnapshotAsync()
    {
        var s = _current;
        if (s is null) return MediaSnapshot.Empty;
        try
        {
            var props = await s.TryGetMediaPropertiesAsync();
            var pb = s.GetPlaybackInfo();
            var tl = s.GetTimelineProperties();
            var c = pb.Controls;
            return new MediaSnapshot(
                HasSession: true,
                Title: props?.Title ?? "",
                Artist: props?.Artist ?? "",
                Source: FriendlySource(s.SourceAppUserModelId),
                IsPlaying: pb.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing,
                Position: tl.Position - tl.StartTime,
                Duration: tl.EndTime - tl.StartTime,
                LastUpdated: tl.LastUpdatedTime,
                CanPlayPause: c.IsPlayPauseToggleEnabled || c.IsPlayEnabled || c.IsPauseEnabled,
                CanNext: c.IsNextEnabled,
                CanPrevious: c.IsPreviousEnabled,
                CanShuffle: c.IsShuffleEnabled,
                CanRepeat: c.IsRepeatEnabled,
                CanSeek: c.IsPlaybackPositionEnabled,
                Shuffle: pb.IsShuffleActive ?? false,
                Repeat: pb.AutoRepeatMode ?? MediaPlaybackAutoRepeatMode.None,
                Thumbnail: props?.Thumbnail);
        }
        catch
        {
            return MediaSnapshot.Empty;
        }
    }

    /// <summary>Reads the album art / video thumbnail the site published (null if none).</summary>
    public static async Task<byte[]?> ReadThumbnailAsync(IRandomAccessStreamReference? thumbnail)
    {
        if (thumbnail is null) return null;
        try
        {
            using var source = await thumbnail.OpenReadAsync();
            using var stream = source.AsStreamForRead();
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer);
            return buffer.Length > 0 ? buffer.ToArray() : null;
        }
        catch
        {
            return null;
        }
    }

    public Task TogglePlayPauseAsync() => Run(s => s.TryTogglePlayPauseAsync());
    public Task NextAsync() => Run(s => s.TrySkipNextAsync());
    public Task PreviousAsync() => Run(s => s.TrySkipPreviousAsync());
    public Task SetShuffleAsync(bool on) => Run(s => s.TryChangeShuffleActiveAsync(on));
    public Task SetRepeatAsync(MediaPlaybackAutoRepeatMode mode) => Run(s => s.TryChangeAutoRepeatModeAsync(mode));
    public Task SeekAsync(TimeSpan position) => Run(s => s.TryChangePlaybackPositionAsync(position.Ticks));

    async Task Run(Func<Session, IAsyncOperation<bool>> op)
    {
        var s = _current;
        if (s is null) return;
        try { await op(s); }
        catch { /* session vanished */ }
    }
}
