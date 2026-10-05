using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MiniPlayer.Services;
using MiniPlayer.Localization;

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
        _ = LoadAutoStartAsync();
        ZoomSlider.Value = Math.Round(_app.PlayerZoom * 100);
        ZoomText.Text = $"{ZoomSlider.Value:0}%";

        LoadScreens();
        LoadThemes(_app.Themes.CurrentId);
        LoadSleepTimer();
        LoadHotkeys();
        SelectByTag(WheelBox, _app.Settings.Data.WheelAction.ToString());
        SelectByTag(TaskbarWheelBox, _app.Settings.Data.TaskbarWheelAction.ToString());
        SelectByTag(MiddleClickBox, _app.Settings.Data.MiddleClickAction.ToString());
        SelectByTag(VisualizerPositionBox, _app.Settings.Data.VisualizerPosition.ToString());
        OnVisualizerBarsChanged(this, new RoutedPropertyChangedEventArgs<double>(0, _app.Settings.Data.VisualizerBars));
        LoadAutomation();
        LoadHistoryAndStream();
        SelectByTag(LanguageBox, string.IsNullOrEmpty(_app.Settings.Data.Language) ? "auto" : _app.Settings.Data.Language);

        var version = _app.Updates.CurrentVersion;
        VersionText.Text = Loc.F("settings_version_installed", version);
        AboutVersion.Text = Loc.F("about_version", version);
        KeepUpdatedBox.IsChecked = _app.Settings.Data.AutoUpdate;
        KeepUpdatedBox.IsEnabled = CheckButton.IsEnabled = _app.Updates.IsInstalled;
        NotInstalledHint.Visibility = _app.Updates.IsInstalled ? Visibility.Collapsed : Visibility.Visible;
        if (AppInfo.IsStore)
        {
            // The Store installs updates; nothing to check or configure here.
            NotInstalledHint.Text = Loc.T("updates_store");
            CheckButton.Visibility = Visibility.Collapsed;
            KeepUpdatedCard.Visibility = Visibility.Collapsed;
        }

        // The player is often "always on top"; while the user works here, stay above it.
        Activated += (_, _) => Topmost = true;
        Deactivated += (_, _) => Topmost = false;

        _app.Themes.ThemesChanged += OnThemesChanged;
        _app.SleepTimer.Changed += RefreshSleepTimer;
        Loc.Changed += RefreshCodeTexts;
        Closed += (_, _) =>
        {
            _app.Themes.ThemesChanged -= OnThemesChanged;
            _app.SleepTimer.Changed -= RefreshSleepTimer;
            Loc.Changed -= RefreshCodeTexts;
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
        TaskbarModeHint.Visibility = Vis(_app.Settings.Data.Mode == PlayerMode.Floating);
        PageControls.Visibility = Vis(tag == "Controls");
        PageAutomation.Visibility = Vis(tag == "Automation");
        PageHistory.Visibility = Vis(tag == "History");
        PageStream.Visibility = Vis(tag == "Stream");
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

    void OnLanguageChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loading && TagOf(LanguageBox) is { } code) _app.SetLanguage(code);
    }

    /// <summary>XAML texts follow the language by binding; these are set from code.</summary>
    void RefreshCodeTexts()
    {
        var wasLoading = _loading;
        _loading = true;
        var version = _app.Updates.CurrentVersion;
        VersionText.Text = Loc.F("settings_version_installed", version);
        AboutVersion.Text = Loc.F("about_version", version);
        SleepButtons.Children.Clear();
        LoadSleepTimer();
        LoadHotkeys();
        LoadMicApps();
        LoadThemes((ThemeList.SelectedItem as ThemeItem)?.Id ?? _app.Themes.CurrentId);
        UpdateTemplatePreview();
        if (_updateReady) CheckButton.Content = Loc.T("toast_update_now");
        _loading = wasLoading;
    }

    async Task LoadAutoStartAsync() => AutoStartBox.IsChecked = await AutoStartService.IsEnabledAsync();

    async void OnAutoStartClick(object sender, RoutedEventArgs e) =>
        // Show what Windows actually allowed (it can refuse in the Store build).
        AutoStartBox.IsChecked = await AutoStartService.SetAsync(AutoStartBox.IsChecked == true);

    void LoadSleepTimer()
    {
        foreach (var minutes in SleepMenu.Minutes)
        {
            var button = new Button { Content = $"{minutes} min", Padding = new Thickness(12, 5, 12, 5), Margin = new Thickness(0, 0, 8, 6) };
            button.Click += (_, _) => _app.SleepTimer.Start(TimeSpan.FromMinutes(minutes));
            SleepButtons.Children.Add(button);
        }
        var endOfTrack = new Button { Content = Loc.T("sleep_end_of_track"), Padding = new Thickness(12, 5, 12, 5), Margin = new Thickness(0, 0, 8, 6) };
        endOfTrack.Click += (_, _) => _app.SleepTimer.StartEndOfTrack();
        SleepButtons.Children.Add(endOfTrack);
        SleepFadeBox.IsChecked = _app.Settings.Data.SleepFade;
        RefreshSleepTimer();
    }

    void RefreshSleepTimer()
    {
        var timer = _app.SleepTimer;
        SleepStatus.Text = timer.IsActive ? Loc.F("sleep_status", timer.RemainingText) : Loc.T("sleep_none");
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
            status.Text = taken ? Loc.T("hotkey_taken") : "";
            status.ToolTip = taken ? Loc.T("hotkey_taken_tip") : null;
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

    void OnVisualizerBarsChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (VisualizerBarsText is null) return; // fires during InitializeComponent
        VisualizerBarsText.Text = e.NewValue < 1 ? Loc.T("viz_bars_auto") : $"{e.NewValue:0}";
    }

    void OnVisualizerPositionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loading && Enum.TryParse<VisualizerPosition>(TagOf(VisualizerPositionBox), out var position))
            _app.ViewModel.VisualizerPosition = position;
    }

    #endregion

    #region Automação

    void LoadAutomation()
    {
        var data = _app.Settings.Data;
        PauseOnCallBox.IsChecked = data.PauseOnCall;
        ResumeAfterCallBox.IsChecked = data.ResumeAfterCall;
        DuckBox.IsChecked = data.DuckOtherAudio;
        DuckLevelSlider.Value = Math.Round(data.DuckLevel * 100);
        DuckLevelText.Text = $"{DuckLevelSlider.Value:0}%";
        LoadMicApps();
    }

    void LoadMicApps()
    {
        MicAppsPanel.Children.Clear();
        var apps = CallMonitorService.MicApps();
        if (apps.Count == 0)
        {
            MicAppsPanel.Children.Add(new TextBlock { Text = Loc.T("mic_none"), Opacity = 0.7 });
            return;
        }
        foreach (var app in apps.Take(12))
        {
            var when = app.InUse ? Loc.T("mic_in_use") : app.LastUsed is { } t ? Loc.F("mic_used_at", t.ToString("g", Loc.Instance.Culture)) : "";
            var box = new CheckBox
            {
                Content = $"{app.Name}   ·   {when}",
                IsChecked = !_app.Settings.Data.CallIgnore.Contains(app.Key),
                Tag = app.Key,
                Margin = new Thickness(0, 2, 0, 2),
            };
            box.Click += (_, _) =>
            {
                var ignore = _app.Settings.Data.CallIgnore;
                ignore.Remove(app.Key);
                if (box.IsChecked != true) ignore.Add(app.Key);
                _app.Settings.Save();
            };
            MicAppsPanel.Children.Add(box);
        }
    }

    void OnRefreshMicApps(object sender, RoutedEventArgs e) => LoadMicApps();

    void OnAutomationClick(object sender, RoutedEventArgs e)
    {
        var data = _app.Settings.Data;
        data.PauseOnCall = PauseOnCallBox.IsChecked == true;
        data.ResumeAfterCall = ResumeAfterCallBox.IsChecked == true;
        data.DuckOtherAudio = DuckBox.IsChecked == true;
        _app.Settings.Save();
        _app.Ducking.Apply();
    }

    void OnDuckLevelChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (DuckLevelText is null) return; // during InitializeComponent
        DuckLevelText.Text = $"{e.NewValue:0}%";
        if (_loading) return;
        _app.Settings.Data.DuckLevel = e.NewValue / 100;
        _app.Settings.Save();
    }

    #endregion

    #region Histórico e transmissão

    void LoadHistoryAndStream()
    {
        var data = _app.Settings.Data;
        RecordHistoryBox.IsChecked = data.RecordHistory;
        NowPlayingBox.IsChecked = data.NowPlayingEnabled;
        NowPlayingFolderBox.Text = _app.NowPlaying.Folder;
        TemplateBox.Text = data.NowPlayingTemplate ?? NowPlayingService.DefaultTemplate;
        ClearWhenPausedBox.IsChecked = data.NowPlayingClearWhenPaused;
        UpdateTemplatePreview();
    }

    void OnRecordHistoryClick(object sender, RoutedEventArgs e)
    {
        _app.Settings.Data.RecordHistory = RecordHistoryBox.IsChecked == true;
        _app.Settings.Save();
    }

    void OnOpenHistory(object sender, RoutedEventArgs e) => _app.ShowHistory();

    void OnOpenStatistics(object sender, RoutedEventArgs e) => _app.ShowHistory(statistics: true);

    void OnNowPlayingChanged(object sender, RoutedEventArgs e)
    {
        if (_loading || TemplatePreview is null) return;
        var data = _app.Settings.Data;
        data.NowPlayingEnabled = NowPlayingBox.IsChecked == true;
        data.NowPlayingTemplate = string.IsNullOrWhiteSpace(TemplateBox.Text) ? NowPlayingService.DefaultTemplate : TemplateBox.Text;
        data.NowPlayingClearWhenPaused = ClearWhenPausedBox.IsChecked == true;
        _app.Settings.Save();
        _app.NowPlaying.Invalidate();
        UpdateTemplatePreview();
    }

    void OnTemplateChanged(object sender, TextChangedEventArgs e) => OnNowPlayingChanged(sender, e);

    void UpdateTemplatePreview()
    {
        var s = _app.ViewModel.Current;
        var example = s.HasSession
            ? NowPlayingService.Format(TemplateBox.Text, s.Title, s.Artist, s.Source)
            : NowPlayingService.Format(TemplateBox.Text, Loc.T("np_example_song"), Loc.T("np_example_artist"), "Chrome");
        TemplatePreview.Text = Loc.F("np_example", example);
    }

    void OnChooseNowPlayingFolder(object sender, RoutedEventArgs e)
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = Loc.T("np_folder_dialog"),
            UseDescriptionForTitle = true,
            SelectedPath = _app.NowPlaying.Folder,
        };
        if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
        _app.Settings.Data.NowPlayingFolder = dialog.SelectedPath;
        _app.Settings.Save();
        _app.NowPlaying.Invalidate();
        NowPlayingFolderBox.Text = _app.NowPlaying.Folder;
    }

    void OnOpenNowPlayingFolder(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(_app.NowPlaying.Folder);
        Process.Start("explorer.exe", _app.NowPlaying.Folder);
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
            var detail = t.BuiltIn ? Loc.T("theme_builtin") : string.IsNullOrWhiteSpace(t.Author) ? Loc.T("theme_custom") : Loc.F("theme_by", t.Author);
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

    ThemeDefinition SelectedTheme() => _app.Themes.Find((ThemeList.SelectedItem as ThemeItem)?.Id ?? _app.Themes.CurrentId);

    void OnCustomizeTheme(object sender, RoutedEventArgs e) =>
        new ThemeEditorWindow(SelectedTheme(), id => LoadThemes(id)) { Owner = this }.Show();

    void OnImportTheme(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = Loc.T("theme_file_filter") + "|*.json" };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var id = _app.Themes.Import(dialog.FileName);
            LoadThemes(id);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, Loc.F("theme_import_failed", ex.Message), Title, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    void OnExportTheme(object sender, RoutedEventArgs e)
    {
        var theme = SelectedTheme();
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = Loc.T("theme_file_filter") + "|*.json",
            FileName = string.Concat(theme.Name.Select(c => System.IO.Path.GetInvalidFileNameChars().Contains(c) ? '-' : c)) + ".json",
        };
        if (dialog.ShowDialog(this) != true) return;
        try { ThemeService.Export(theme, dialog.FileName); }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

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
        UpdateStatus.Text = Loc.T("updates_checking");
        try
        {
            var version = await _app.Updates.CheckAsync(manual: true);
            if (version is null)
            {
                UpdateStatus.Text = Loc.T("updates_latest");
            }
            else
            {
                _updateReady = true;
                UpdateStatus.Text = Loc.F("updates_available", version);
                CheckButton.Content = Loc.T("toast_update_now");
            }
        }
        catch (Exception ex)
        {
            UpdateStatus.Text = Loc.F("updates_check_failed", ex.Message.Split('\n')[0]);
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
        Process.Start("explorer.exe", AppInfo.DataDirOnDisk);

    static void OpenUrl(string url) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

    #endregion
}
