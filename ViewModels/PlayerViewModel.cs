using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using MiniPlayer.Services;
using Windows.Media;
using MiniPlayer.Localization;

namespace MiniPlayer.ViewModels;

public sealed class PlayerViewModel : INotifyPropertyChanged
{
    // Segoe Fluent Icons / Segoe MDL2 Assets glyphs
    const string GlyphPlay = "", GlyphPause = "", GlyphRepeatAll = "", GlyphRepeatOne = "";

    readonly MediaService _media;
    readonly SettingsService _settings;
    readonly Dispatcher _dispatcher;
    MediaSnapshot _snap = MediaSnapshot.Empty;
    bool _refreshing, _refreshPending;
    // Cover loading: track it belongs to, generation (bumped per track), hash of the shown image.
    string? _coverTrack;
    int _coverGeneration;
    string? _coverHash;
    bool _coverIsHd; // the shown cover came from the HD lookup: browser thumbnails don't replace it
    DateTime _coverReadAt;
    // After a track change, poll the artwork this often for this long (browsers send it ~1-4 s late).
    static readonly TimeSpan CoverPollInterval = TimeSpan.FromMilliseconds(500);
    static readonly TimeSpan CoverPollWindow = TimeSpan.FromSeconds(6);
    string? _lyricsKey;
    IReadOnlyList<LyricLine>? _lyrics;
    DateTime _statusUntil;
    DateTime _activeAt = DateTime.Now;

    public PlayerViewModel(MediaService media, SettingsService settings)
    {
        _media = media;
        _settings = settings;
        _dispatcher = Application.Current.Dispatcher;

        PlayPauseCommand = new RelayCommand(() => _ = _media.TogglePlayPauseAsync(), () => _snap.CanPlayPause);
        NextCommand = new RelayCommand(() => _ = _media.NextAsync(), () => _snap.CanNext);
        PreviousCommand = new RelayCommand(() => _ = _media.PreviousAsync(), () => _snap.CanPrevious);
        ShuffleCommand = new RelayCommand(() => _ = _media.SetShuffleAsync(!_snap.Shuffle), () => _snap.CanShuffle);
        RepeatCommand = new RelayCommand(() => _ = _media.SetRepeatAsync(NextRepeat(_snap.Repeat)), () => _snap.CanRepeat);

        _media.Changed += () => _dispatcher.InvokeAsync(RefreshAsync);

        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        timer.Tick += (_, _) => UpdateProgress();
        timer.Start();

        UpdateOverlayTheme();
        SystemEvents.UserPreferenceChanged += (_, _) => _dispatcher.BeginInvoke(UpdateOverlayTheme);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    /// <summary>Raised when the displayed track text changes (used for the tray tooltip).</summary>
    public event Action? TrackChanged;
    /// <summary>Raised with every progress update (~4×/s), for history and "now playing".</summary>
    public event Action? Ticked;
    /// <summary>Raised when the shown artwork changes (<see cref="CoverBytes"/>, <see cref="CoverHash"/>).</summary>
    public event Action? CoverUpdated;

    /// <summary>Raw state of the current session.</summary>
    public MediaSnapshot Current => _snap;
    public byte[]? CoverBytes { get; private set; }
    public string? CoverHash => _coverHash;

    public RelayCommand PlayPauseCommand { get; }
    public RelayCommand NextCommand { get; }
    public RelayCommand PreviousCommand { get; }
    public RelayCommand ShuffleCommand { get; }
    public RelayCommand RepeatCommand { get; }

    string _title = Loc.T("vm_nothing");
    public string Title { get => _title; private set => Set(ref _title, value); }

    string _subtitle = Loc.T("vm_open_music");
    public string Subtitle { get => _subtitle; private set { Set(ref _subtitle, value); RaiseSubtitles(); } }

    public string FullText => _snap.HasSession ? $"{Title} — {Subtitle}" : Title;

    string? _status;
    /// <summary>Short-lived message (volume, mute) shown in place of the subtitle.</summary>
    string? Status { get => _status; set { _status = value; RaiseSubtitles(); } }

    /// <summary>Second line of the floating player.</summary>
    public string SubtitleDisplay => _status ?? _otherTabHint ?? Subtitle;

    // Browsers publish one media session (one tab). When that tab is paused while another tab of
    // the same browser keeps playing, the browser does not hand the session over; say so.
    string? _otherTabHint;
    List<AudioService.IAudioMeterInformation> _browserMeters = [];
    string? _browserMetersFor;
    DateTime _browserMetersAt, _otherTabSince;

    void CheckOtherTab()
    {
        string? hint = null;
        var process = _snap.HasSession && !_snap.IsPlaying ? AudioService.ProcessNameFor(_media.CurrentAppId) : null;
        if (process is not null)
        {
            if (process != _browserMetersFor || DateTime.Now - _browserMetersAt > TimeSpan.FromSeconds(5))
            {
                _browserMeters = AudioService.Meters(process);
                _browserMetersFor = process;
                _browserMetersAt = DateTime.Now;
            }
            if (AudioService.Peak(_browserMeters) > 0.02f)
            {
                if (_otherTabSince == default) _otherTabSince = DateTime.Now;
                // sustained sound, not the tail of the track that was just paused
                if (DateTime.Now - _otherTabSince > TimeSpan.FromSeconds(2)) hint = Loc.F("other_tab_playing", _snap.Source);
            }
            else
            {
                _otherTabSince = default;
            }
        }
        else
        {
            _otherTabSince = default;
        }

        if (hint == _otherTabHint) return;
        _otherTabHint = hint;
        RaiseSubtitles();
    }

    /// <summary>Second line of the taskbar overlay: status, else current lyric line, else artist.</summary>
    public string TaskbarSubtitle =>
        _status ?? _otherTabHint ?? (ShowLyrics && !string.IsNullOrEmpty(LyricCurrent) ? LyricCurrent : Subtitle);

    /// <summary>True while the user picked the source by hand.</summary>
    public bool IsPinned => _media.IsPinned;

    /// <summary>Taskbar overlay should get out of the way (nothing playing for a while).</summary>
    public bool ShouldAutoHide
    {
        get
        {
            if (!_settings.Data.AutoHideWhenIdle || _snap.IsPlaying) return false;
            var idle = DateTime.Now - _activeAt;
            return idle > (_snap.HasSession ? TimeSpan.FromSeconds(30) : TimeSpan.FromSeconds(3));
        }
    }

    public bool AutoHideWhenIdle
    {
        get => _settings.Data.AutoHideWhenIdle;
        set
        {
            if (_settings.Data.AutoHideWhenIdle == value) return;
            _settings.Data.AutoHideWhenIdle = value;
            _settings.Save();
            OnPropertyChanged();
        }
    }

    public bool ShowVisualizer
    {
        get => _settings.Data.ShowVisualizer;
        set
        {
            if (_settings.Data.ShowVisualizer == value) return;
            _settings.Data.ShowVisualizer = value;
            _settings.Save();
            OnPropertyChanged();
            RaiseVisualizerPlacement();
        }
    }

    public VisualizerPosition VisualizerPosition
    {
        get => _settings.Data.VisualizerPosition;
        set
        {
            if (_settings.Data.VisualizerPosition == value) return;
            _settings.Data.VisualizerPosition = value;
            _settings.Save();
            RaiseVisualizerPlacement();
        }
    }

    public int VisualizerBars
    {
        get => _settings.Data.VisualizerBars;
        set
        {
            value = Math.Clamp(value, 0, 160);
            if (_settings.Data.VisualizerBars == value) return;
            _settings.Data.VisualizerBars = value;
            _settings.Save();
            OnPropertyChanged();
        }
    }

    // Where the floating player shows the bars (each false when the visualizer is off).
    public bool VisualizerAtControls => ShowVisualizer && VisualizerPosition == VisualizerPosition.Controls;
    public bool VisualizerOnCover => ShowVisualizer && VisualizerPosition == VisualizerPosition.Cover;
    public bool VisualizerAtBottom => ShowVisualizer && VisualizerPosition == VisualizerPosition.Bottom;
    public bool VisualizerAroundCover => ShowVisualizer && VisualizerPosition == VisualizerPosition.AroundCover;
    /// <summary>The cover shrinks to leave room for the ring of bars around it.</summary>
    public double CoverScale => VisualizerAroundCover ? Views.VisualizerControl.RingInner : 1;

    void RaiseVisualizerPlacement()
    {
        OnPropertyChanged(nameof(VisualizerAtControls));
        OnPropertyChanged(nameof(VisualizerOnCover));
        OnPropertyChanged(nameof(VisualizerAtBottom));
        OnPropertyChanged(nameof(VisualizerAroundCover));
        OnPropertyChanged(nameof(CoverScale));
    }

    /// <summary>Shows a short message in place of the subtitle (e.g. "call: music paused").</summary>
    public void ShowStatus(string text, double seconds = 4)
    {
        Status = text;
        _statusUntil = DateTime.Now.AddSeconds(seconds);
    }

    public bool IsPlaying => _snap.IsPlaying;

    bool _coverSpin;
    /// <summary>Theme asks for a spinning (vinyl) cover.</summary>
    public bool CoverSpin
    {
        get => _coverSpin;
        set
        {
            if (_coverSpin == value) return;
            _coverSpin = value;
            OnPropertyChanged(nameof(CoverSpinning));
        }
    }

    /// <summary>The vinyl turns only while the music plays.</summary>
    public bool CoverSpinning => _coverSpin && _snap.IsPlaying;

    /// <summary>Identifies the current track (sleep timer "end of track").</summary>
    public string TrackKey => $"{_snap.Source}|{_snap.Title}|{_snap.Artist}";

    TimeSpan _position;
    /// <summary>Time until the current track ends; null without a timeline.</summary>
    public TimeSpan? TimeLeftInTrack => HasTimeline ? _snap.Duration - _position : null;

    string? _sleepText;
    /// <summary>Sleep timer badge ("23 min"); null when no timer runs.</summary>
    public string? SleepText
    {
        get => _sleepText;
        set
        {
            if (_sleepText == value) return;
            _sleepText = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasSleepTimer));
        }
    }
    public bool HasSleepTimer => _sleepText is not null;

    DateTime _lastWheelSkip;

    /// <summary>Mouse wheel over the player: volume or previous/next, per settings.</summary>
    public void Wheel(int delta, bool onTaskbar)
    {
        var action = onTaskbar ? _settings.Data.TaskbarWheelAction : _settings.Data.WheelAction;
        if (action == WheelAction.Volume)
        {
            ChangeVolume(delta);
            return;
        }
        // A wheel notch fires several events; one skip per gesture.
        if (DateTime.Now - _lastWheelSkip < TimeSpan.FromMilliseconds(450)) return;
        _lastWheelSkip = DateTime.Now;
        if (delta > 0) PreviousCommand.Execute(null);
        else NextCommand.Execute(null);
    }

    public void MiddleClick()
    {
        if (_settings.Data.MiddleClickAction == MiddleClickAction.PlayPause) PlayPauseCommand.Execute(null);
        else ToggleMute();
    }

    public bool ShowLyrics
    {
        get => _settings.Data.ShowLyrics;
        set
        {
            if (_settings.Data.ShowLyrics == value) return;
            _settings.Data.ShowLyrics = value;
            _settings.Save();
            OnPropertyChanged();
            RaiseSubtitles();
            _lyricsKey = null;
            _ = UpdateLyricsAsync(_snap);
        }
    }

    string _lyricPrevious = "", _lyricCurrent = "", _lyricNext = "";
    public string LyricPrevious { get => _lyricPrevious; private set => Set(ref _lyricPrevious, value); }
    public string LyricCurrent
    {
        get => _lyricCurrent;
        private set
        {
            if (_lyricCurrent == value) return;
            _lyricCurrent = value;
            OnPropertyChanged();
            RaiseSubtitles();
        }
    }
    public string LyricNext { get => _lyricNext; private set => Set(ref _lyricNext, value); }

    string _playGlyph = GlyphPlay;
    public string PlayGlyph { get => _playGlyph; private set => Set(ref _playGlyph, value); }

    ImageSource? _cover;
    /// <summary>Album art / video thumbnail of the current track.</summary>
    public ImageSource? Cover { get => _cover; private set { Set(ref _cover, value); OnPropertyChanged(nameof(HasCover)); } }
    public bool HasCover => _cover is not null;

    ImageSource? _coverBackdrop;
    /// <summary>Tiny copy of the cover; stretched over the panel it reads as a blur (theme backdrop "cover").</summary>
    public ImageSource? CoverBackdrop { get => _coverBackdrop; private set => Set(ref _coverBackdrop, value); }

    bool _shuffleOn;
    public bool ShuffleOn { get => _shuffleOn; private set => Set(ref _shuffleOn, value); }

    bool _repeatOn;
    public bool RepeatOn { get => _repeatOn; private set => Set(ref _repeatOn, value); }

    string _repeatGlyph = GlyphRepeatAll;
    public string RepeatGlyph { get => _repeatGlyph; private set => Set(ref _repeatGlyph, value); }

    bool _hasTimeline;
    public bool HasTimeline { get => _hasTimeline; private set => Set(ref _hasTimeline, value); }

    double _progress;
    public double Progress { get => _progress; private set => Set(ref _progress, value); }

    string _positionText = "";
    public string PositionText { get => _positionText; private set => Set(ref _positionText, value); }

    string _durationText = "";
    public string DurationText { get => _durationText; private set => Set(ref _durationText, value); }

    Brush _overlayForeground = Brushes.White;
    /// <summary>Text color for the taskbar overlay, following the Windows light/dark theme.</summary>
    public Brush OverlayForeground { get => _overlayForeground; private set => Set(ref _overlayForeground, value); }

    public bool PinOnTop
    {
        get => _settings.Data.PinOnTop;
        set
        {
            if (_settings.Data.PinOnTop == value) return;
            _settings.Data.PinOnTop = value;
            _settings.Save();
            OnPropertyChanged();
        }
    }

    /// <summary>Mouse wheel over the player: volume of the app that is playing (not the system volume).</summary>
    public void ChangeVolume(int wheelDelta)
    {
        var process = AudioService.ProcessNameFor(_media.CurrentAppId);
        ShowVolume(process is null ? null : AudioService.Change(process, wheelDelta / 120f * 0.05f));
    }

    public void ToggleMute()
    {
        var process = AudioService.ProcessNameFor(_media.CurrentAppId);
        ShowVolume(process is null ? null : AudioService.ToggleMute(process));
    }

    void ShowVolume((float Volume, bool Muted)? state)
    {
        Status = state switch
        {
            null => Loc.T("vm_volume_unavailable"),
            { Muted: true } => Loc.T("vm_muted"),
            var (v, _) => Loc.F("vm_volume", Math.Round(v * 100)),
        };
        _statusUntil = DateTime.Now.AddSeconds(1.5);
    }

    public Task<List<MediaSessionInfo>> GetSessionsAsync() => _media.GetSessionsAsync();

    /// <summary>Control a specific tab/app; null returns to automatic choice.</summary>
    public void PinSession(object? key) => _media.Pin(key);

    /// <summary>Re-renders texts after the UI language changed.</summary>
    public void RefreshTexts()
    {
        _lyricsKey = null;
        Apply(_snap);
    }

    public void SeekTo(double fraction)
    {
        if (!_snap.CanSeek || _snap.Duration <= TimeSpan.Zero) return;
        var target = TimeSpan.FromTicks((long)(_snap.Duration.Ticks * Math.Clamp(fraction, 0, 1)));
        _snap = _snap with { Position = target, LastUpdated = DateTimeOffset.Now };
        UpdateProgress();
        _ = _media.SeekAsync(target);
    }

    async Task RefreshAsync()
    {
        if (_refreshing)
        {
            _refreshPending = true;
            return;
        }
        _refreshing = true;
        try
        {
            do
            {
                _refreshPending = false;
                Apply(await _media.GetSnapshotAsync());
            } while (_refreshPending);
        }
        finally
        {
            _refreshing = false;
        }
    }

    void Apply(MediaSnapshot s)
    {
        if (s.IsPlaying || _snap.IsPlaying || s.HasSession != _snap.HasSession) _activeAt = DateTime.Now;
        _snap = s;
        if (s.HasSession)
        {
            Title = string.IsNullOrWhiteSpace(s.Title) ? Loc.T("vm_untitled") : s.Title;
            Subtitle = string.Join(" · ", new[] { s.Artist, s.Source }.Where(x => !string.IsNullOrWhiteSpace(x)));
        }
        else
        {
            Title = Loc.T("vm_nothing");
            Subtitle = Loc.T("vm_open_music");
        }
        PlayGlyph = s.IsPlaying ? GlyphPause : GlyphPlay;
        ShuffleOn = s.Shuffle;
        RepeatOn = s.Repeat != MediaPlaybackAutoRepeatMode.None;
        RepeatGlyph = s.Repeat == MediaPlaybackAutoRepeatMode.Track ? GlyphRepeatOne : GlyphRepeatAll;

        PlayPauseCommand.RaiseCanExecuteChanged();
        NextCommand.RaiseCanExecuteChanged();
        PreviousCommand.RaiseCanExecuteChanged();
        ShuffleCommand.RaiseCanExecuteChanged();
        RepeatCommand.RaiseCanExecuteChanged();

        OnPropertyChanged(nameof(FullText));
        OnPropertyChanged(nameof(IsPinned));
        OnPropertyChanged(nameof(IsPlaying));
        OnPropertyChanged(nameof(CoverSpinning));
        TrackChanged?.Invoke();
        UpdateProgress();
        _ = UpdateCoverAsync(s);
        _ = UpdateLyricsAsync(s);
    }

    async Task UpdateLyricsAsync(MediaSnapshot s)
    {
        var key = ShowLyrics && s.HasSession && !string.IsNullOrWhiteSpace(s.Title)
            ? $"{s.Title}|{s.Artist}|{(int)s.Duration.TotalSeconds}"
            : null;
        if (key == _lyricsKey) return;
        _lyricsKey = key;
        _lyrics = null;
        SetLyricLines("", key is null ? "" : Loc.T("vm_lyrics_searching"), "");
        if (key is null) return;

        var result = await LyricsService.GetAsync(s.Title, s.Artist, s.Duration);
        if (key != _lyricsKey) return; // track changed while loading
        _lyrics = result.Lines;
        SetLyricLines("", _lyrics is not null ? "♪" : result.Found ? Loc.T("vm_lyrics_unsynced") : Loc.T("vm_lyrics_not_found"), "");
        UpdateProgress();
    }

    void UpdateLyricLine(TimeSpan position)
    {
        if (_lyrics is not { Count: > 0 } lines) return;
        // Small lead so the line appears as it is sung, not after.
        var t = position + TimeSpan.FromMilliseconds(300);
        var i = -1;
        while (i + 1 < lines.Count && lines[i + 1].Time <= t) i++;
        SetLyricLines(
            i > 0 ? lines[i - 1].Text : "",
            i >= 0 ? lines[i].Text : "♪",
            i + 1 < lines.Count ? lines[i + 1].Text : "");
    }

    void SetLyricLines(string previous, string current, string next)
    {
        LyricPrevious = previous;
        LyricCurrent = current;
        LyricNext = next;
    }

    void RaiseSubtitles()
    {
        OnPropertyChanged(nameof(SubtitleDisplay));
        OnPropertyChanged(nameof(TaskbarSubtitle));
    }

    /// <summary>
    /// Browsers often publish a new title before its artwork: the first read can return the
    /// previous track's image, an empty stream or nothing. So on a track change we read now
    /// and retry a few times, and any later update of the same track re-reads too. Images are
    /// compared by content, so stale or identical art never flickers.
    /// </summary>
    async Task UpdateCoverAsync(MediaSnapshot s)
    {
        var track = s.HasSession ? $"{s.Source}|{s.Title}|{s.Artist}" : null;
        if (track == _coverTrack)
        {
            // Same track: artwork may have arrived late (throttled).
            if (track is not null && DateTime.Now - _coverReadAt > TimeSpan.FromSeconds(2))
                await LoadCoverAsync(_coverGeneration);
            return;
        }

        _coverTrack = track;
        var generation = ++_coverGeneration;
        _coverIsHd = false;
        if (track is null)
        {
            ShowCover(null, null);
            return;
        }
        if (_settings.Data.HdCovers) _ = LoadHdCoverAsync(generation, s.Title, s.Artist);

        var found = false;
        var until = DateTime.Now + CoverPollWindow;
        while (true)
        {
            found |= await LoadCoverAsync(generation);
            if (generation != _coverGeneration) return; // skipped again meanwhile
            if (DateTime.Now >= until) break;
            await Task.Delay(CoverPollInterval);
        }
        if (!found && !_coverIsHd) ShowCover(null, null); // track has no art
    }

    /// <returns>True when the session returned an image (new or unchanged).</returns>
    async Task<bool> LoadCoverAsync(int generation)
    {
        _coverReadAt = DateTime.Now;
        if (_coverIsHd) return true;
        // Fresh properties every time: an older thumbnail reference keeps returning the old image.
        var fresh = await _media.GetSnapshotAsync();
        if (fresh.Title != _snap.Title || fresh.Artist != _snap.Artist) return false; // track moved on
        var bytes = await MediaService.ReadThumbnailAsync(fresh.Thumbnail);
        if (generation != _coverGeneration || bytes is null || _coverIsHd) return false;
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(bytes));
        if (hash != _coverHash) ShowCover(bytes, hash);
        return true;
    }

    /// <summary>Browsers hand over ~120 px art; swap in the album's 1000 px cover when one matches.</summary>
    async Task LoadHdCoverAsync(int generation, string title, string artist)
    {
        var bytes = await CoverArtService.FindAsync(title, artist);
        if (bytes is null || generation != _coverGeneration) return; // no match, or the track changed
        _coverIsHd = true;
        ShowCover(bytes, Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(bytes)));
    }

    string? _themeAuthor;

    /// <summary>Author of the theme in use (set by the app when a theme is applied).</summary>
    public string? ThemeAuthor
    {
        get => _themeAuthor;
        set
        {
            _themeAuthor = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            RaiseThemeCredit(); // also on the same author: the language may have changed
        }
    }

    public bool ShowThemeAuthor
    {
        get => _settings.Data.ShowThemeAuthor;
        set
        {
            if (_settings.Data.ShowThemeAuthor == value) return;
            _settings.Data.ShowThemeAuthor = value;
            _settings.Save();
            OnPropertyChanged();
            RaiseThemeCredit();
        }
    }

    public bool HasThemeCredit => ShowThemeAuthor && _themeAuthor is not null;
    public string? ThemeCredit => HasThemeCredit ? Loc.F("theme_credit", _themeAuthor) : null;

    void RaiseThemeCredit()
    {
        OnPropertyChanged(nameof(HasThemeCredit));
        OnPropertyChanged(nameof(ThemeCredit));
    }

    public bool HdCovers
    {
        get => _settings.Data.HdCovers;
        set
        {
            if (_settings.Data.HdCovers == value) return;
            _settings.Data.HdCovers = value;
            _settings.Save();
            OnPropertyChanged();
            // reload the current track's cover with the new setting
            _coverTrack = null;
            _coverHash = null;
            _ = UpdateCoverAsync(_snap);
        }
    }

    void ShowCover(byte[]? bytes, string? hash)
    {
        _coverHash = hash;
        CoverBytes = bytes;
        CoverUpdated?.Invoke();
        Cover = bytes is null ? null : Decode(bytes, 800); // big players, 200% zoom, HiDPI
        CoverBackdrop = bytes is null ? null : Decode(bytes, 64); // the theme blurs it
    }

    /// <param name="maxHeight">Downscale bigger images to this; smaller ones keep their size.</param>
    static BitmapImage? Decode(byte[] bytes, int maxHeight)
    {
        try
        {
            var original = BitmapFrame.Create(new MemoryStream(bytes), BitmapCreateOptions.DelayCreation, BitmapCacheOption.None).PixelHeight;
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            if (original > maxHeight) image.DecodePixelHeight = maxHeight;
            image.StreamSource = new MemoryStream(bytes);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch
        {
            return null;
        }
    }

    int _ticks;

    void UpdateProgress()
    {
        Ticked?.Invoke();
        if (++_ticks % 4 == 0) CheckOtherTab(); // ~1 s
        if (_status is not null && DateTime.Now > _statusUntil) Status = null;
        if (_snap.IsPlaying) _activeAt = DateTime.Now;

        var s = _snap;
        HasTimeline = s.HasSession && s.Duration > TimeSpan.Zero;
        if (!HasTimeline)
        {
            Progress = 0;
            PositionText = DurationText = "";
            return;
        }

        var pos = s.Position;
        if (s.IsPlaying)
        {
            // SMTC only pushes the position on events; extrapolate between them.
            var elapsed = DateTimeOffset.Now - s.LastUpdated;
            if (elapsed > TimeSpan.Zero && elapsed < TimeSpan.FromHours(12)) pos += elapsed;
        }
        if (pos < TimeSpan.Zero) pos = TimeSpan.Zero;
        if (pos > s.Duration) pos = s.Duration;

        _position = pos;
        UpdateLyricLine(pos);
        Progress = pos.TotalSeconds / s.Duration.TotalSeconds;
        PositionText = Format(pos);
        DurationText = Format(s.Duration);
    }

    void UpdateOverlayTheme()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        var light = key?.GetValue("SystemUsesLightTheme") is int v && v == 1;
        OverlayForeground = light ? Brushes.Black : Brushes.White;
    }

    static MediaPlaybackAutoRepeatMode NextRepeat(MediaPlaybackAutoRepeatMode m) => m switch
    {
        MediaPlaybackAutoRepeatMode.None => MediaPlaybackAutoRepeatMode.List,
        MediaPlaybackAutoRepeatMode.List => MediaPlaybackAutoRepeatMode.Track,
        _ => MediaPlaybackAutoRepeatMode.None,
    };

    static string Format(TimeSpan t) =>
        t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");

    void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        OnPropertyChanged(name);
    }

    void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
