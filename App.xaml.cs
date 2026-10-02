using System.Windows;
using Microsoft.Toolkit.Uwp.Notifications;
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
        if (Updates.IsInstalled)
        {
            // Toast buttons: fires in this process, or starts the app when it is closed.
            ToastNotificationManagerCompat.OnActivated += args =>
                Dispatcher.BeginInvoke(() => OnToastActivated(args.Argument));
            AnnounceIfJustUpdated();
        }
        var media = new MediaService();
        ViewModel = new PlayerViewModel(media, Settings);
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
            ShowMessage($"Não foi possível acessar os controles de mídia do Windows.\n\n{ex.Message}",
                "MiniPlayer", MessageBoxButton.OK, MessageBoxImage.Error);
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
                    "Atualização baixada", $"A versão {version} será instalada na próxima abertura.");
            }
            catch
            {
                // Offline: the next check retries.
            }
            return;
        }

        Notify(() => NotificationService.ShowUpdateAvailable(version, Updates.CurrentVersion),
            "Nova versão disponível", $"Versão {version} pronta para instalar. Clique aqui para atualizar.");
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
            $"A versão {version} está disponível (você usa a {Updates.CurrentVersion}).\n\n" +
            "Baixar e instalar agora? O MiniPlayer reinicia sozinho.",
            "MiniPlayer — atualização", MessageBoxButton.YesNo, MessageBoxImage.Information);
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
            ShowMessage($"Não foi possível atualizar.\n\n{ex.Message}", "MiniPlayer",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    public async void CheckUpdatesManually()
    {
        try
        {
            if (await Updates.CheckAsync(manual: true) is null)
                ShowMessage($"Você já está na versão mais recente ({Updates.CurrentVersion}).", "MiniPlayer",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            else
                InstallUpdate();
        }
        catch (Exception ex)
        {
            ShowMessage($"Não foi possível verificar atualizações.\n\n{ex.Message}", "MiniPlayer",
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
