using System.Windows;
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

    public SettingsService Settings { get; private set; } = null!;
    public PlayerViewModel ViewModel { get; private set; } = null!;
    public ThemeService Themes { get; private set; } = null!;

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
        Themes = new ThemeService(Settings);
        Themes.Initialize();
        var media = new MediaService();
        ViewModel = new PlayerViewModel(media, Settings);
        _tray = new TrayIcon(this);
        ViewModel.TrackChanged += () => _tray.SetText(ViewModel.FullText);

        SetMode(Settings.Data.Mode);

        try
        {
            await media.InitializeAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Não foi possível acessar os controles de mídia do Windows.\n\n{ex.Message}",
                "MiniPlayer", MessageBoxButton.OK, MessageBoxImage.Error);
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

    /// <summary>Move the overlay to the taskbar of another monitor (switches to taskbar mode).</summary>
    public void SetTaskbarMonitor(string device)
    {
        Settings.Data.TaskbarMonitor = device;
        SetMode(PlayerMode.Taskbar);
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

    public void ExitApp()
    {
        Settings.Save();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
