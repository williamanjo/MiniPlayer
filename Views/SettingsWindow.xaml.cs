using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
        LoadSleepTimer();
        LoadHotkeys();
        SelectByTag(WheelBox, _app.Settings.Data.WheelAction.ToString());
        SelectByTag(TaskbarWheelBox, _app.Settings.Data.TaskbarWheelAction.ToString());
        SelectByTag(MiddleClickBox, _app.Settings.Data.MiddleClickAction.ToString());

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
        _app.SleepTimer.Changed += RefreshSleepTimer;
        Closed += (_, _) =>
        {
            _app.Themes.ThemesChanged -= OnThemesChanged;
            _app.SleepTimer.Changed -= RefreshSleepTimer;
        };

        ShowPage("General");
        _loading = false;
    }

    public void ShowPage(string tag) =>
        Nav.SelectedItem = Nav.Items.OfType<ListBoxItem>().FirstOrDefault(i => (string)i.Tag == tag);

    void OnNavChanged(object sender, SelectionChangedEventArgs e)
    {
        var tag = (Nav.SelectedItem as ListBoxItem)?.Tag as string ?? "General";
        PageGeneral.Visibility = Vis(tag == "General");
        PageControls.Visibility = Vis(tag == "Controls");
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

    void LoadSleepTimer()
    {
        foreach (var minutes in SleepMenu.Minutes)
        {
            var button = new Button { Content = $"{minutes} min", Padding = new Thickness(12, 5, 12, 5), Margin = new Thickness(0, 0, 8, 6) };
            button.Click += (_, _) => _app.SleepTimer.Start(TimeSpan.FromMinutes(minutes));
            SleepButtons.Children.Add(button);
        }
        var endOfTrack = new Button { Content = "No fim desta música", Padding = new Thickness(12, 5, 12, 5), Margin = new Thickness(0, 0, 8, 6) };
        endOfTrack.Click += (_, _) => _app.SleepTimer.StartEndOfTrack();
        SleepButtons.Children.Add(endOfTrack);
        SleepFadeBox.IsChecked = _app.Settings.Data.SleepFade;
        RefreshSleepTimer();
    }

    void RefreshSleepTimer()
    {
        var timer = _app.SleepTimer;
        SleepStatus.Text = timer.IsActive ? $"🌙 Pausa em {timer.RemainingText}" : "Nenhum timer ativo";
        SleepCancel.Visibility = Vis(timer.IsActive);
    }

    void OnSleepCancel(object sender, RoutedEventArgs e) => _app.SleepTimer.Cancel();

    void OnSleepFadeClick(object sender, RoutedEventArgs e)
    {
        _app.Settings.Data.SleepFade = SleepFadeBox.IsChecked == true;
        _app.Settings.Save();
    }

    #endregion

    #region Atalhos e controles

    readonly Dictionary<HotkeyAction, (TextBox Box, TextBlock Status)> _hotkeyRows = [];

    void LoadHotkeys()
    {
        HotkeyRows.Children.Clear();
        _hotkeyRows.Clear();
        var bindings = _app.Settings.Data.HotkeyBindings();
        foreach (var action in Enum.GetValues<HotkeyAction>())
        {
            var row = new Grid { Margin = new Thickness(0, 4, 0, 4) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var label = new TextBlock { Text = HotkeyService.Label(action), VerticalAlignment = VerticalAlignment.Center };
            var box = new TextBox
            {
                IsReadOnly = true,
                IsReadOnlyCaretVisible = false,
                Text = Hotkey.Parse(bindings.GetValueOrDefault(action.ToString()))?.ToString() ?? "",
                Tag = action,
                Cursor = Cursors.Hand,
            };
            box.PreviewKeyDown += OnHotkeyKeyDown;
            box.GotKeyboardFocus += (_, _) => _app.SuspendHotkeys(); // let the combination reach the box
            box.LostKeyboardFocus += (_, _) => { _app.ApplyHotkeys(); RefreshHotkeyStatus(); };
            var status = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0), Foreground = Brushes.IndianRed };

            Grid.SetColumn(box, 1);
            Grid.SetColumn(status, 2);
            row.Children.Add(label);
            row.Children.Add(box);
            row.Children.Add(status);
            HotkeyRows.Children.Add(row);
            _hotkeyRows[action] = (box, status);
        }
        RefreshHotkeyStatus();
    }

    void RefreshHotkeyStatus()
    {
        foreach (var (action, (_, status)) in _hotkeyRows)
        {
            var taken = _app.Hotkeys.Failed.Contains(action);
            status.Text = taken ? "⚠ em uso" : "";
            status.ToolTip = taken ? "Outro programa já usa essa combinação. Escolha outra." : null;
        }
    }

    /// <summary>Records the pressed combination into the hotkey box.</summary>
    void OnHotkeyKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        var box = (TextBox)sender;
        var action = (HotkeyAction)box.Tag;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var modifiers = Keyboard.Modifiers;

        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift
            or Key.LWin or Key.RWin or Key.ImeProcessed) return; // wait for the actual key

        if (modifiers == ModifierKeys.None)
        {
            if (key is Key.Back or Key.Delete) SaveHotkey(action, box, null); // remove
            else if (key == Key.Escape) Keyboard.ClearFocus();
            return; // a global hotkey needs a modifier
        }
        SaveHotkey(action, box, new Hotkey(modifiers, key));
    }

    void SaveHotkey(HotkeyAction action, TextBox box, Hotkey? hotkey)
    {
        _app.Settings.Data.Hotkeys[action.ToString()] = hotkey?.Serialize() ?? "";
        _app.Settings.Save();
        box.Text = hotkey?.ToString() ?? "";
    }

    void OnResetHotkeys(object sender, RoutedEventArgs e)
    {
        _app.Settings.Data.Hotkeys.Clear();
        _app.Settings.Save();
        _app.ApplyHotkeys();
        LoadHotkeys();
    }

    static void SelectByTag(ComboBox box, string tag) =>
        box.SelectedItem = box.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == tag);

    static string? TagOf(ComboBox box) => (box.SelectedItem as ComboBoxItem)?.Tag as string;

    void OnMouseOptionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        var data = _app.Settings.Data;
        if (Enum.TryParse<WheelAction>(TagOf(WheelBox), out var wheel)) data.WheelAction = wheel;
        if (Enum.TryParse<WheelAction>(TagOf(TaskbarWheelBox), out var taskbarWheel)) data.TaskbarWheelAction = taskbarWheel;
        if (Enum.TryParse<MiddleClickAction>(TagOf(MiddleClickBox), out var middle)) data.MiddleClickAction = middle;
        _app.Settings.Save();
    }

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
