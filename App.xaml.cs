using System.Windows;
using Microsoft.Toolkit.Uwp.Notifications;
using MiniPlayer.Localization;
using MiniPlayer.Services;
using MiniPlayer.ViewModels;
using MiniPlayer.Views;

namespace MiniPlayer;

public partial class App : Application
{
    Mutex? _mutex;
    TrayIcon? _tray;
    PlayerWindow? _player;
    TaskbarWindow? _overlay;
    SettingsWindow? _settingsWindow;

    public SettingsService Settings { get; private set; } = null!;
    public PlayerViewModel ViewModel { get; private set; } = null!;
    public ThemeService Themes { get; private set; } = null!;
    public UpdateService Updates { get; private set; } = null!;
    public VisualizerService Visualizer { get; private set; } = null!;
    public SleepTimerService SleepTimer { get; private set; } = null!;
    public HotkeyService Hotkeys { get; private set; } = null!;
    public CallMonitorService Calls { get; private set; } = null!;
    public DuckingService Ducking { get; private set; } = null!;
    public HistoryService History { get; private set; } = null!;
    public NowPlayingService NowPlaying { get; private set; } = null!;
    HistoryWindow? _historyWindow;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _mutex = new Mutex(true, "MiniPlayer.SingleInstance.6E1F3C2A", out var isFirst);
        if (!isFirst)
        {
            Shutdown();
            return;
        }

        Settings = SettingsService.Load();
        Loc.SetLanguage(Settings.Data.Language);
        Themes = new ThemeService(Settings);
        Themes.Initialize();
        Updates = new UpdateService();
        if (Updates.IsInstalled)
        {
            // Toast buttons: fires in this process, or starts the app when it is closed.
            ToastNotificationManagerCompat.OnActivated += args =>
                Dispatcher.BeginInvoke(() => OnToastActivated(args.Argument));
            AnnounceIfJustUpdated();
        }
        var media = new MediaService();
        ViewModel = new PlayerViewModel(media, Settings);

        Visualizer = new VisualizerService(() => media.CurrentAppId, () => ViewModel.IsPlaying)
        {
            Enabled = ViewModel.ShowVisualizer,
        };
        ViewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(PlayerViewModel.ShowVisualizer)) Visualizer.Enabled = ViewModel.ShowVisualizer;
        };

        SleepTimer = new SleepTimerService(media, Settings, () => ViewModel.TimeLeftInTrack, () => ViewModel.TrackKey);
        SleepTimer.Changed += () => ViewModel.SleepText = SleepTimer.RemainingText;

        Calls = new CallMonitorService(media, Settings, () => ViewModel.IsPlaying);
        Calls.CallChanged += caller => ViewModel.ShowStatus(caller is null
            ? Loc.T("call_ended")
            : Loc.F("call_started", caller));
        Ducking = new DuckingService(media, Settings, () => ViewModel.IsPlaying, () => SleepTimer.IsFading);

        History = new HistoryService(Settings);
        NowPlaying = new NowPlayingService(Settings);
        ViewModel.Ticked += () =>
        {
            History.Tick(ViewModel.Current);
            NowPlaying.Update(ViewModel.Current);
        };
        ViewModel.CoverUpdated += () =>
        {
            History.SetCover(ViewModel.CoverBytes, ViewModel.CoverHash);
            NowPlaying.SetCover(ViewModel.CoverBytes, ViewModel.CoverHash);
        };

        Hotkeys = new HotkeyService();
        Hotkeys.Pressed += OnHotkey;
        ApplyHotkeys();
        _tray = new TrayIcon(this);
        ViewModel.TrackChanged += () => _tray.SetText(ViewModel.FullText);

        SetMode(Settings.Data.Mode);

        Updates.UpdateAvailable += OnUpdateAvailable;
        // Started by a toast button: the click (OnToastActivated) checks and updates by itself.
        Updates.Start(checkNow: !(Updates.IsInstalled && ToastNotificationManagerCompat.WasCurrentProcessToastActivated()));

        try
        {
            await media.InitializeAsync();
        }
        catch (Exception ex)
        {
            ShowMessage(Loc.F("err_media", ex.Message),
                AppInfo.Name, MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// MessageBox in front of everything. The app usually has no active window (tray / toast
    /// click), and an ownerless MessageBox then opens behind other apps.
    /// </summary>
    static MessageBoxResult ShowMessage(string text, string caption, MessageBoxButton buttons, MessageBoxImage icon)
    {
        var owner = new Window
        {
            Width = 0,
            Height = 0,
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            Topmost = true,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
        };
        owner.Show();
        owner.Activate();
        try
        {
            return MessageBox.Show(owner, text, caption, buttons, icon);
        }
        finally
        {
            owner.Close();
        }
    }

    public void SetMode(PlayerMode mode)
    {
        Settings.Data.Mode = mode;
        Settings.Save();

        if (mode == PlayerMode.Taskbar)
        {
            _player?.Hide();
            _overlay ??= new TaskbarWindow(ViewModel, Settings);
            _overlay.Show();
        }
        else
        {
            _overlay?.Hide();
            _player ??= new PlayerWindow(ViewModel, Settings);
            _player.Show();
            _player.Activate();
        }
    }

    /// <summary>Move the overlay to the taskbar of another monitor (by default also switches to taskbar mode).</summary>
    public void SetTaskbarMonitor(string device, bool switchMode = true)
    {
        Settings.Data.TaskbarMonitor = device;
        Settings.Save();
        if (switchMode) SetMode(PlayerMode.Taskbar);
        _overlay?.Reposition();
    }

    /// <summary>Registers the saved global hotkeys (call after changing them).</summary>
    public void ApplyHotkeys() => Hotkeys.Apply(Settings.Data.HotkeyBindings());

    /// <summary>While a hotkey box records a combination, the old ones must not fire.</summary>
    public void SuspendHotkeys() => Hotkeys.Apply(new Dictionary<string, string>());

    void OnHotkey(HotkeyAction action)
    {
        switch (action)
        {
            case HotkeyAction.PlayPause: ViewModel.PlayPauseCommand.Execute(null); break;
            case HotkeyAction.Next: ViewModel.NextCommand.Execute(null); break;
            case HotkeyAction.Previous: ViewModel.PreviousCommand.Execute(null); break;
            case HotkeyAction.VolumeUp: ViewModel.ChangeVolume(120); break;
            case HotkeyAction.VolumeDown: ViewModel.ChangeVolume(-120); break;
            case HotkeyAction.Mute: ViewModel.ToggleMute(); break;
            case HotkeyAction.TogglePlayer: TogglePlayerVisibility(); break;
        }
    }

    /// <summary>Hotkey: hide/show whichever player the current mode uses.</summary>
    void TogglePlayerVisibility()
    {
        if (Settings.Data.Mode == PlayerMode.Floating) TogglePlayer();
        else if (_overlay is { IsVisible: true }) _overlay.Hide();
        else SetMode(PlayerMode.Taskbar);
    }

    public void ShowHistory(bool statistics = false)
    {
        if (_historyWindow is null)
        {
            _historyWindow = new HistoryWindow();
            _historyWindow.Closed += (_, _) => _historyWindow = null;
        }
        _historyWindow.ShowTab(statistics);
        _historyWindow.Show();
        if (_historyWindow.WindowState == WindowState.Minimized) _historyWindow.WindowState = WindowState.Normal;
        _historyWindow.Activate();
    }

    /// <summary>Switches the UI language live ("auto", "en", "pt-BR").</summary>
    public void SetLanguage(string code)
    {
        Settings.Data.Language = code;
        Settings.Save();
        Loc.SetLanguage(code);
        Themes.Load(); // built-in theme names are translated
        Themes.Apply(Themes.CurrentId);
        ViewModel.RefreshTexts();
    }

    public void ShowSettings(string? page = null)
    {
        if (_settingsWindow is null)
        {
            _settingsWindow = new SettingsWindow();
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        }
        if (page is not null) _settingsWindow.ShowPage(page);
        _settingsWindow.Show();
        if (_settingsWindow.WindowState == WindowState.Minimized) _settingsWindow.WindowState = WindowState.Normal;
        _settingsWindow.Activate();
    }

    public double PlayerZoom => _player?.Zoom ?? Settings.Data.FloatingScale;

    public void SetPlayerZoom(double zoom)
    {
        if (_player is not null)
        {
            _player.ZoomTo(zoom);
        }
        else
        {
            Settings.Data.FloatingScale = zoom;
            Settings.Save();
        }
    }

    public void ResetPlayerSize()
    {
        if (_player is not null)
        {
            _player.ResetSize();
            return;
        }
        Settings.Data.FloatingScale = 1;
        Settings.Data.FloatingWidth = Settings.Data.FloatingHeight = null;
        Settings.Save();
    }

    public void ResetOverlayPosition()
    {
        Settings.Data.TaskbarOffset = 0;
        Settings.Save();
        _overlay?.Reposition();
    }

    /// <summary>Tray left-click: show/hide the floating player.</summary>
    public void TogglePlayer()
    {
        if (Settings.Data.Mode == PlayerMode.Floating && _player is { IsVisible: true })
            _player.Hide();
        else
            SetMode(PlayerMode.Floating);
    }

    bool _updating;

    async void OnUpdateAvailable(string version, bool atStartup)
    {
        _tray?.ShowUpdateMenu(version);
        if (_updating) return; // already installing (e.g. toast button clicked)

        if (Settings.Data.AutoUpdate)
        {
            // "Manter atualizado": install right away when just opened; while running, only
            // download (no restart mid-song) and let Velopack apply it on the next start.
            if (atStartup)
            {
                await UpdateNowAsync();
                return;
            }
            try
            {
                await Updates.DownloadAsync();
                Notify(() => NotificationService.ShowUpdateDownloaded(version),
                    Loc.T("balloon_downloaded_title"), Loc.F("balloon_downloaded_text", version));
            }
            catch
            {
                // Offline: the next check retries.
            }
            return;
        }

        Notify(() => NotificationService.ShowUpdateAvailable(version, Updates.CurrentVersion),
            Loc.T("balloon_new_title"), Loc.F("balloon_new_text", version));
    }

    /// <summary>Toast first; tray balloon if toasts are unavailable.</summary>
    void Notify(Action toast, string title, string text)
    {
        try { toast(); }
        catch { _tray?.ShowBalloon(title, text); }
    }

    async void OnToastActivated(string argument)
    {
        ToastArguments.Parse(argument).TryGetValue(NotificationService.ActionKey, out var action);
        switch (action)
        {
            case NotificationService.ActionUpdate:
            case NotificationService.ActionRestart:
                _ = UpdateNowAsync();
                break;
            default:
                // Toast body. The app may have just been started by the click: check first.
                if ((Updates.AvailableVersion ?? await Updates.CheckAsync(manual: false)) is not null) InstallUpdate();
                else SetMode(Settings.Data.Mode);
                break;
        }
    }

    /// <summary>First run after an update: say so and drop the old "new version" toast.</summary>
    void AnnounceIfJustUpdated()
    {
        var current = Updates.CurrentVersion;
        if (Settings.Data.LastVersion is { } last && last != current)
        {
            NotificationService.ClearUpdate();
            try { NotificationService.ShowUpdated(current); }
            catch { /* notifications unavailable */ }
        }
        Settings.Data.LastVersion = current;
        Settings.Save();
    }

    /// <summary>Asks, then updates (tray item / balloon / toast body).</summary>
    public async void InstallUpdate()
    {
        if (_updating || Updates.AvailableVersion is not { } version) return;
        var answer = ShowMessage(
            Loc.F("update_ask", version, Updates.CurrentVersion),
            Loc.T("update_ask_title"), MessageBoxButton.YesNo, MessageBoxImage.Information);
        if (answer == MessageBoxResult.Yes) await UpdateNowAsync();
    }

    /// <summary>Downloads (checking first if needed) and restarts into the new version, no questions.</summary>
    public async Task UpdateNowAsync()
    {
        if (_updating) return;
        _updating = true;
        try
        {
            if (Updates.AvailableVersion is null && await Updates.CheckAsync(manual: true) is null)
            {
                _updating = false;
                return; // already up to date
            }
            NotificationService.ClearUpdate();
            await Updates.DownloadAndRestartAsync(
                percent => _tray?.SetText(Loc.F("update_downloading", percent)),
                () =>
                {
                    Settings.Save();
                    _tray?.Dispose();
                    _mutex?.ReleaseMutex();
                });
        }
        catch (Exception ex)
        {
            _updating = false;
            ShowMessage(Loc.F("update_failed", ex.Message), "MiniPlayer",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    public async void CheckUpdatesManually()
    {
        try
        {
            if (await Updates.CheckAsync(manual: true) is null)
                ShowMessage(Loc.F("update_latest", Updates.CurrentVersion), "MiniPlayer",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            else
                InstallUpdate();
        }
        catch (Exception ex)
        {
            ShowMessage(Loc.F("update_check_failed", ex.Message), "MiniPlayer",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    public void ExitApp()
    {
        Settings.Save();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        Hotkeys?.Dispose();
        SleepTimer?.Cancel(); // restores a faded volume
        Ducking?.Restore();
        History?.Finish(); // keep the track that was playing
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
