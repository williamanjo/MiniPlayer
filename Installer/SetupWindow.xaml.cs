using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace MiniPlayerSetup;

public partial class SetupWindow : Window
{
    // Same places the app uses (installer build): data in %APPDATA%, program in %LOCALAPPDATA%.
    static readonly string DataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MiniPlayer");
    static readonly string SettingsPath = Path.Combine(DataDir, "settings.json");
    static readonly string AppExe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MiniPlayer", "current", "MiniPlayer.exe");
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string RunValue = "MiniPlayer"; // = AutoStartService.RunValue in the app

    // a class, not a record: records need IsExternalInit, which .NET Framework lacks
    sealed class ThemeItem(string id, string name, ImageSource image)
    {
        public string Id { get; } = id;
        public string Name { get; } = name;
        public ImageSource Image { get; } = image;
    }

    readonly bool _hasOld = File.Exists(SettingsPath);
    bool _busy;

    public SetupWindow()
    {
        InitializeComponent();
        var art = Lang.Code == "pt-BR" ? "pt" : "en";
        Hero.Source = new BitmapImage(new Uri($"pack://application:,,,/Assets/update-hero-{art}.jpg"));
        FillTexts();
        if (_hasOld) ShowOldData();
        FillThemes();
        UpdateInstallButton();
    }

    void FillTexts()
    {
        SetupTitle.Text = Lang.T("inst_title");
        SetupLead.Text = Lang.T("inst_lead");
        StartupText.Text = Lang.T("inst_startup");
        StartupHint.Text = Lang.T("inst_startup_hint");
        UpdateText.Text = Lang.T("inst_update");
        UpdateHint.Text = Lang.T("inst_update_hint");
        OldTitle.Text = Lang.T("inst_old_title");
        KeepText.Text = Lang.T("inst_keep");
        KeepHint.Text = Lang.T("inst_keep_hint");
        FreshText.Text = Lang.T("inst_fresh");
        FreshHint.Text = Lang.T("inst_fresh_hint");
        BackupText.Text = Lang.T("inst_backup");
        CancelButton.Content = Lang.T("inst_cancel");
        ProgressTitle.Text = Lang.T("inst_installing");
        DoneTitle.Text = Lang.T("inst_done_title");
        DoneLead.Text = Lang.T("inst_done_lead");
        ThemeTitle.Text = Lang.T("inst_pick_theme");
        ThemeHint.Text = Lang.T("inst_pick_theme_hint");
        OpenText.Text = Lang.T("inst_open_now");
        FinishButton.Content = Lang.T("inst_finish");
    }

    // ---------- previous install ----------

    void ShowOldData()
    {
        OldPanel.Visibility = Visibility.Visible;
        try
        {
            var settings = ReadSettings();
            if (settings.TryGetValue("Theme", out var t) && t is string theme && ThemeName(theme) is { } name)
                AddChip(Lang.F("inst_chip_theme", name));
            var themesDir = Path.Combine(DataDir, "themes");
            var custom = Directory.Exists(themesDir)
                ? Directory.GetFiles(themesDir, "*.json").Count(f => !Path.GetFileName(f).StartsWith("exemplo", StringComparison.OrdinalIgnoreCase))
                : 0;
            if (custom > 0) AddChip(Lang.F("inst_chip_themes", custom));
            var history = Path.Combine(DataDir, "history.jsonl");
            if (File.Exists(history))
            {
                var songs = File.ReadLines(history).Count(l => l.Trim().Length > 0);
                if (songs > 0) AddChip(Lang.F("inst_chip_history", songs.ToString("N0")));
            }
        }
        catch
        {
            // a damaged settings file still counts as previous data; the chips are only a hint
        }
    }

    void AddChip(string text) => Chips.Children.Add(new Border
    {
        Style = (Style)FindResource("Chip"),
        Child = new TextBlock { Text = text, FontSize = 12, Foreground = new SolidColorBrush(Color.FromRgb(0x4C, 0x1D, 0x95)) },
    });

    static string? ThemeName(string id)
    {
        if (id.StartsWith("file:")) return Path.GetFileNameWithoutExtension(id.Substring(5));
        if (!id.StartsWith("builtin:")) return null;
        var key = id.Substring(8);
        return Themes.FirstOrDefault(t => t.Id == key).Name is { } n ? Lang.T(n) : key;
    }

    bool StartFresh => _hasOld && FreshChoice.IsChecked == true;

    void OnChoice(object sender, RoutedEventArgs e)
    {
        if (!IsInitialized) return;
        BackupBox.Visibility = StartFresh ? Visibility.Visible : Visibility.Collapsed;
        UpdateInstallButton();
    }

    void UpdateInstallButton()
    {
        InstallButton.Content = Lang.T(StartFresh ? "inst_erase_install" : "inst_install");
        InstallButton.Background = StartFresh
            ? new LinearGradientBrush(Color.FromRgb(0xE1, 0x1D, 0x48), Color.FromRgb(0xF4, 0x72, 0xB6), 0)
            : (Brush)FindResource("Grad");
    }

    // ---------- themes (picked on the last page) ----------

    // id → language key of the name (or the name itself, for proper names)
    static readonly (string Id, string Name)[] Themes =
    [
        ("escuro", "theme_dark"), ("claro", "theme_light"), ("vidro", "theme_glass"), ("neon", "Neon"),
        ("vinil", "theme_vinyl"), ("terminal", "Terminal"), ("papel", "theme_paper"), ("retrowave", "Retrowave"),
        ("matcha", "Matcha"), ("aurora", "Aurora"), ("brutal", "theme_brutal"),
    ];

    void FillThemes()
    {
        ThemeList.ItemsSource = Themes.Select(t => new ThemeItem(t.Id, Lang.T(t.Name),
            new BitmapImage(new Uri($"pack://application:,,,/Themes/{t.Id}.png")))).ToList();
        ThemeList.SelectedIndex = 0;
    }

    // ---------- install ----------

    async void OnInstall(object sender, RoutedEventArgs e)
    {
        _busy = true;
        CloseButton.IsEnabled = false;
        PageSetup.Visibility = Visibility.Collapsed;
        PageProgress.Visibility = Visibility.Visible;

        var fresh = StartFresh;
        var backup = fresh && BackupBox.IsChecked == true;
        var stepBackup = backup ? AddStep("inst_step_backup") : null;
        var stepFiles = AddStep("inst_step_files");
        var stepShortcuts = AddStep("inst_step_shortcuts");
        var stepChoices = AddStep("inst_step_choices");
        try
        {
            if (fresh)
            {
                SetStep(stepBackup, StepState.Now);
                StopRunningApp();
                await Task.Run(() =>
                {
                    if (backup) Backup();
                    if (Directory.Exists(DataDir)) Directory.Delete(DataDir, recursive: true);
                });
                SetStep(stepBackup, StepState.Done);
            }

            SetStep(stepFiles, StepState.Now);
            var code = await Task.Run(RunVelopackSetup);
            if (code != 0) throw new Exception($"Setup exit code {code}");
            SetStep(stepFiles, StepState.Done);
            SetStep(stepShortcuts, StepState.Done); // Velopack creates them during the install

            SetStep(stepChoices, StepState.Now);
            var update = UpdateBox.IsChecked == true;
            var startup = StartupBox.IsChecked == true;
            await Task.Run(() =>
            {
                EditSettings(s => s["AutoUpdate"] = update);
                SetStartup(startup);
            });
            SetStep(stepChoices, StepState.Done);
            await Task.Delay(400);

            // keeping the old settings keeps the old theme too
            ThemePanel.Visibility = _hasOld && !fresh ? Visibility.Collapsed : Visibility.Visible;
            PageProgress.Visibility = Visibility.Collapsed;
            PageDone.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, Lang.F("inst_failed", ex.Message), Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            Steps.Children.Clear();
            PageProgress.Visibility = Visibility.Collapsed;
            PageSetup.Visibility = Visibility.Visible;
        }
        finally
        {
            _busy = false;
            CloseButton.IsEnabled = true;
        }
    }

    /// <summary>
    /// Runs the embedded Velopack Setup.exe silently. The app it starts at the end sees
    /// MINIPLAYER_FROM_INSTALLER and closes at once: this window opens it after the theme is picked.
    /// (An environment variable, not "-- args": Velopack 1.2.161's Setup panics on those.)
    /// </summary>
    static int RunVelopackSetup()
    {
        var dir = Path.Combine(Path.GetTempPath(), "MiniPlayerSetup");
        Directory.CreateDirectory(dir);
        var exe = Path.Combine(dir, "Setup.exe");
        using (var res = typeof(SetupWindow).Assembly.GetManifestResourceStream("VelopackSetup.exe")!)
        using (var file = File.Create(exe))
            res.CopyTo(file);
        try
        {
            var info = new ProcessStartInfo(exe, "--silent") { UseShellExecute = false, CreateNoWindow = true };
            info.EnvironmentVariables["MINIPLAYER_FROM_INSTALLER"] = "1"; // Setup passes its environment on to the app
            using var p = Process.Start(info)!;
            p.WaitForExit();
            return p.ExitCode;
        }
        finally
        {
            try { File.Delete(exe); } catch { /* temp file */ }
        }
    }

    /// <summary>Same layout as the app's backup (BackupService), so Settings → About → Import reads it.</summary>
    static void Backup()
    {
        var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var zip = Path.Combine(docs, $"MiniPlayer-backup-{DateTime.Now:yyyy-MM-dd-HHmm}.zip");
        using var archive = ZipFile.Open(zip, ZipArchiveMode.Create);
        if (File.Exists(SettingsPath)) archive.CreateEntryFromFile(SettingsPath, "settings.json");
        var themes = Path.Combine(DataDir, "themes");
        if (Directory.Exists(themes))
            foreach (var f in Directory.GetFiles(themes)) archive.CreateEntryFromFile(f, "themes/" + Path.GetFileName(f));
    }

    /// <summary>A running MiniPlayer would write its settings back after they were deleted.</summary>
    static void StopRunningApp()
    {
        foreach (var p in Process.GetProcessesByName("MiniPlayer"))
        {
            try { p.Kill(); p.WaitForExit(3000); } catch { /* already gone */ }
            p.Dispose();
        }
    }

    static void SetStartup(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled) key.SetValue(RunValue, $"\"{AppExe}\"");
        else key.DeleteValue(RunValue, throwOnMissingValue: false);
    }

    // ---------- settings.json ----------

    static Dictionary<string, object> ReadSettings() =>
        File.Exists(SettingsPath)
            ? new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(SettingsPath)) ?? []
            : [];

    /// <summary>Changes a few values and keeps everything else the app saved.</summary>
    static void EditSettings(Action<Dictionary<string, object>> change)
    {
        Dictionary<string, object> s;
        try { s = ReadSettings(); }
        catch { s = []; } // unreadable: the app would fall back to defaults anyway
        change(s);
        Directory.CreateDirectory(DataDir);
        var tmp = SettingsPath + ".tmp";
        File.WriteAllText(tmp, new JavaScriptSerializer().Serialize(s), new UTF8Encoding(false));
        if (File.Exists(SettingsPath)) File.Delete(SettingsPath);
        File.Move(tmp, SettingsPath);
    }

    // ---------- steps ----------

    enum StepState { Waiting, Now, Done }

    TextBlock AddStep(string key)
    {
        var t = new TextBlock { Tag = Lang.T(key), FontSize = 13, Margin = new Thickness(0, 0, 0, 6) };
        Steps.Children.Add(t);
        SetStep(t, StepState.Waiting);
        return t;
    }

    static void SetStep(TextBlock? t, StepState state)
    {
        if (t is null) return;
        var (mark, color, weight) = state switch
        {
            StepState.Done => ("✓  ", Color.FromRgb(0x16, 0xA3, 0x4A), FontWeights.Normal),
            StepState.Now => ("●  ", Color.FromRgb(0x7C, 0x3A, 0xED), FontWeights.SemiBold),
            _ => ("○  ", Color.FromRgb(0xB9, 0xAE, 0xDB), FontWeights.Normal),
        };
        t.Inlines.Clear();
        t.Inlines.Add(new System.Windows.Documents.Run(mark) { Foreground = new SolidColorBrush(color) });
        t.Inlines.Add(new System.Windows.Documents.Run((string)t.Tag)
        {
            Foreground = new SolidColorBrush(state == StepState.Waiting ? Color.FromRgb(0x5B, 0x60, 0x70) : Color.FromRgb(0x16, 0x14, 0x1E)),
            FontWeight = weight,
        });
    }

    // ---------- finish / window ----------

    void OnFinish(object sender, RoutedEventArgs e)
    {
        try
        {
            if (ThemePanel.Visibility == Visibility.Visible && ThemeList.SelectedItem is ThemeItem theme)
                EditSettings(s => s["Theme"] = "builtin:" + theme.Id);
            if (OpenBox.IsChecked == true && File.Exists(AppExe)) Process.Start(AppExe);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        Close();
    }

    void OnClose(object sender, RoutedEventArgs e)
    {
        if (!_busy) Close();
    }

    void OnDrag(object sender, System.Windows.Input.MouseButtonEventArgs e) => DragMove();
}
