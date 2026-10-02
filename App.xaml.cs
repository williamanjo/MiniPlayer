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
    public UpdateService Updates { get; private set; } = null!;

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
        Updates = new UpdateService();
        var media = new MediaService();
        ViewModel = new PlayerViewModel(media, Settings);
        _tray = new TrayIcon(this);
        ViewModel.TrackChanged += () => _tray.SetText(ViewModel.FullText);

        SetMode(Settings.Data.Mode);

        Updates.UpdateAvailable += version => _tray.ShowUpdate(version);
        Updates.Start();

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

    bool _updating;

    /// <summary>Asks, downloads the pending version and restarts into it.</summary>
    public async void InstallUpdate()
    {
        if (_updating || Updates.AvailableVersion is not { } version) return;
        var answer = MessageBox.Show(
            $"A versão {version} está disponível (você usa a {Updates.CurrentVersion}).\n\n" +
            "Baixar e instalar agora? O MiniPlayer reinicia sozinho.",
            "MiniPlayer — atualização", MessageBoxButton.YesNo, MessageBoxImage.Information);
        if (answer != MessageBoxResult.Yes) return;

        _updating = true;
        try
        {
            await Updates.DownloadAndRestartAsync(
                percent => _tray?.SetText($"Baixando atualização… {percent}%"),
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
            MessageBox.Show($"Não foi possível atualizar.\n\n{ex.Message}", "MiniPlayer",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    public async void CheckUpdatesManually()
    {
        try
        {
            if (await Updates.CheckAsync(manual: true) is null)
                MessageBox.Show($"Você já está na versão mais recente ({Updates.CurrentVersion}).", "MiniPlayer",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            else
                InstallUpdate();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Não foi possível verificar atualizações.\n\n{ex.Message}", "MiniPlayer",
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
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
