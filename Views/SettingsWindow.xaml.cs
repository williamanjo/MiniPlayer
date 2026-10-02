using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MiniPlayer.Services;

namespace MiniPlayer.Views;

/// <summary>All options in one place; the right-click menus keep only the quick ones.</summary>
public partial class SettingsWindow : Window
{
    /// <summary>Row of the theme list.</summary>
    public sealed record ThemeItem(string Id, string Name, string Detail, Brush Background, Brush Accent);

    readonly App _app = (App)Application.Current;
    bool _loading = true; // ignore control events while filling the UI
    bool _updateReady;

    public SettingsWindow()
    {
        InitializeComponent();
        DataContext = _app.ViewModel;

        ModeFloating.IsChecked = _app.Settings.Data.Mode == PlayerMode.Floating;
        ModeTaskbar.IsChecked = _app.Settings.Data.Mode == PlayerMode.Taskbar;
        AutoStartBox.IsChecked = SettingsService.AutoStart;
        ZoomSlider.Value = Math.Round(_app.PlayerZoom * 100);
        ZoomText.Text = $"{ZoomSlider.Value:0}%";

        LoadScreens();
        LoadThemes(_app.Themes.CurrentId);

        var version = _app.Updates.CurrentVersion;
        VersionText.Text = $"Versão instalada: {version}";
        AboutVersion.Text = $"Versão {version}";
        KeepUpdatedBox.IsChecked = _app.Settings.Data.AutoUpdate;
        KeepUpdatedBox.IsEnabled = CheckButton.IsEnabled = _app.Updates.IsInstalled;
        NotInstalledHint.Visibility = _app.Updates.IsInstalled ? Visibility.Collapsed : Visibility.Visible;

        // The player is often "always on top"; while the user works here, stay above it.
        Activated += (_, _) => Topmost = true;
        Deactivated += (_, _) => Topmost = false;

        _app.Themes.ThemesChanged += OnThemesChanged;
        Closed += (_, _) => _app.Themes.ThemesChanged -= OnThemesChanged;

        ShowPage("General");
        _loading = false;
    }

    public void ShowPage(string tag) =>
        Nav.SelectedItem = Nav.Items.OfType<ListBoxItem>().FirstOrDefault(i => (string)i.Tag == tag);

    void OnNavChanged(object sender, SelectionChangedEventArgs e)
    {
        var tag = (Nav.SelectedItem as ListBoxItem)?.Tag as string ?? "General";
        PageGeneral.Visibility = Vis(tag == "General");
        PageAppearance.Visibility = Vis(tag == "Appearance");
        PageTaskbar.Visibility = Vis(tag == "Taskbar");
        PageUpdates.Visibility = Vis(tag == "Updates");
        PageAbout.Visibility = Vis(tag == "About");
    }

    static Visibility Vis(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;

    #region Geral

    void OnModeChecked(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _app.SetMode(ModeTaskbar.IsChecked == true ? PlayerMode.Taskbar : PlayerMode.Floating);
        Activate(); // the player window grabbed focus
    }

    void OnAutoStartClick(object sender, RoutedEventArgs e) =>
        SettingsService.AutoStart = AutoStartBox.IsChecked == true;

    #endregion

    #region Aparência

    void LoadThemes(string? select)
    {
        var themes = _app.Themes;
        ThemeList.ItemsSource = themes.Themes.Select(t =>
        {
            var res = ThemeService.BuildResources(t);
            var detail = t.BuiltIn ? "Embutido" : string.IsNullOrWhiteSpace(t.Author) ? "Personalizado" : $"por {t.Author}";
            return new ThemeItem(t.Id, t.Name, detail, (Brush)res["ThemeBackground"], (Brush)res["AccentBrush"]);
        }).ToList();
        ThemeList.SelectedItem = ThemeList.Items.OfType<ThemeItem>().FirstOrDefault(i => i.Id == select)
                                 ?? ThemeList.Items.OfType<ThemeItem>().FirstOrDefault();

        ThemeErrors.Text = string.Join("\n", themes.Errors.Select(e => "⚠ " + e));
        ThemeErrors.Visibility = Vis(themes.Errors.Count > 0);
    }

    void OnThemesChanged() =>
        Dispatcher.BeginInvoke(() => LoadThemes((ThemeList.SelectedItem as ThemeItem)?.Id ?? _app.Themes.CurrentId));

    /// <summary>Preview only: the theme's resources go on the preview element, not the app.</summary>
    void OnThemeSelected(object sender, SelectionChangedEventArgs e)
    {
        if (ThemeList.SelectedItem is not ThemeItem item) return;
        Preview.Resources = ThemeService.BuildResources(_app.Themes.Find(item.Id));
        RefreshThemeButtons();
    }

    void RefreshThemeButtons()
    {
        var inUse = (ThemeList.SelectedItem as ThemeItem)?.Id == _app.Themes.CurrentId;
        UseThemeButton.IsEnabled = !inUse;
        ThemeInUse.Visibility = Vis(inUse);
    }

    void OnUseTheme(object sender, RoutedEventArgs e)
    {
        if (ThemeList.SelectedItem is not ThemeItem item) return;
        _app.Themes.Apply(item.Id);
        RefreshThemeButtons();
    }

    void OnThemeDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e) => OnUseTheme(sender, e);

    void OnOpenThemes(object sender, RoutedEventArgs e) => ThemeService.OpenFolder();

    void OnReloadThemes(object sender, RoutedEventArgs e)
    {
        _app.Themes.Load();
        _app.Themes.Apply(_app.Themes.CurrentId);
    }

    void OnZoomChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (ZoomText is null) return; // fires during InitializeComponent
        ZoomText.Text = $"{e.NewValue:0}%";
        if (!_loading) _app.SetPlayerZoom(e.NewValue / 100);
    }

    void OnResetSize(object sender, RoutedEventArgs e)
    {
        _app.ResetPlayerSize();
        _loading = true;
        ZoomSlider.Value = 100;
        _loading = false;
    }

    #endregion

    #region Barra de tarefas

    void LoadScreens()
    {
        var taskbars = TaskbarHelper.GetTaskbars();
        ScreenBox.ItemsSource = taskbars;
        var current = TaskbarHelper.GetTaskbar(_app.Settings.Data.TaskbarMonitor)?.Device;
        ScreenBox.SelectedItem = taskbars.FirstOrDefault(t => t.Device == current);
        ScreenBox.IsEnabled = taskbars.Count > 1;
    }

    void OnScreenChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || ScreenBox.SelectedItem is not TaskbarHelper.TaskbarInfo taskbar) return;
        _app.SetTaskbarMonitor(taskbar.Device, switchMode: false);
    }

    void OnResetTaskbarPosition(object sender, RoutedEventArgs e) => _app.ResetOverlayPosition();

    #endregion

    #region Atualizações

    async void OnCheckUpdates(object sender, RoutedEventArgs e)
    {
        if (_updateReady)
        {
            await _app.UpdateNowAsync();
            return;
        }

        CheckButton.IsEnabled = false;
        UpdateStatus.Text = "Verificando…";
        try
        {
            var version = await _app.Updates.CheckAsync(manual: true);
            if (version is null)
            {
                UpdateStatus.Text = "Você está na versão mais recente.";
            }
            else
            {
                _updateReady = true;
                UpdateStatus.Text = $"Versão {version} disponível.";
                CheckButton.Content = "Atualizar agora";
            }
        }
        catch (Exception ex)
        {
            UpdateStatus.Text = "Não foi possível verificar: " + ex.Message.Split('\n')[0];
        }
        finally
        {
            CheckButton.IsEnabled = true;
        }
    }

    void OnKeepUpdatedClick(object sender, RoutedEventArgs e)
    {
        _app.Settings.Data.AutoUpdate = KeepUpdatedBox.IsChecked == true;
        _app.Settings.Save();
    }

    void OnOpenReleases(object sender, RoutedEventArgs e) => OpenUrl(UpdateService.RepoUrl + "/releases");

    #endregion

    #region Sobre

    void OnOpenRepo(object sender, RoutedEventArgs e) => OpenUrl(UpdateService.RepoUrl);

    void OnOpenSettingsFolder(object sender, RoutedEventArgs e) =>
        Process.Start("explorer.exe", Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MiniPlayer"));

    static void OpenUrl(string url) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

    #endregion
}
