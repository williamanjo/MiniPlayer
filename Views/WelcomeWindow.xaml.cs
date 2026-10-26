using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MiniPlayer.Localization;
using MiniPlayer.Services;

namespace MiniPlayer.Views;

/// <summary>First-run choices for the Store build: theme and start with Windows, or keep / clear existing settings.</summary>
public partial class WelcomeWindow : Window
{
    sealed record ThemeItem(string Id, string Name, ImageSource Image);

    // id → language key of the name (or the name itself, for proper names); same list as the installer
    static readonly (string Id, string Name)[] Themes =
    [
        ("escuro", "theme_dark"), ("claro", "theme_light"), ("vidro", "theme_glass"), ("neon", "Neon"),
        ("vinil", "theme_vinyl"), ("terminal", "Terminal"), ("papel", "theme_paper"), ("retrowave", "Retrowave"),
        ("matcha", "Matcha"), ("aurora", "Aurora"), ("brutal", "theme_brutal"),
    ];

    readonly App _app;
    readonly bool _hasOld;

    public WelcomeWindow(App app, bool hasOld)
    {
        InitializeComponent();
        _app = app;
        _hasOld = hasOld;
        var art = Loc.Instance.Culture.Name == "pt-BR" ? "pt" : "en";
        Hero.Source = new BitmapImage(new Uri($"pack://application:,,,/MiniPlayer;component/Assets/update-hero-{art}.jpg"));

        TitleText.Text = Loc.T(hasOld ? "welcome_back_title" : "welcome_new_title");
        LeadText.Text = Loc.T(hasOld ? "welcome_back_lead" : "welcome_new_lead");
        KeepText.Text = Loc.T("inst_keep");
        KeepHint.Text = Loc.T("inst_keep_hint");
        FreshText.Text = Loc.T("welcome_clear");
        FreshHint.Text = Loc.T("inst_fresh_hint");
        BackupText.Text = Loc.T("inst_backup");
        ThemeTitle.Text = Loc.T("inst_pick_theme");
        ThemeHint.Text = Loc.T("inst_pick_theme_hint");
        StartupText.Text = Loc.T("inst_startup");
        StartupHint.Text = Loc.T("inst_startup_hint");

        ThemeList.ItemsSource = Themes.Select(t => new ThemeItem(t.Id, Loc.T(t.Name),
            new BitmapImage(new Uri($"pack://application:,,,/MiniPlayer;component/Assets/theme-thumbs/{t.Id}.png")))).ToList();
        ThemeList.SelectedIndex = 0;

        if (hasOld)
        {
            ShowOldData();
            // existing settings: the switch starts as Windows has it now, not forced on
            Loaded += async (_, _) =>
            {
                try { StartupBox.IsChecked = await AutoStartService.IsEnabledAsync(); }
                catch { /* keep the default */ }
            };
        }
        Refresh();
    }

    void ShowOldData()
    {
        OldPanel.Visibility = Visibility.Visible;
        AddChip(Loc.F("inst_chip_theme", _app.Themes.Current.Name));
        var custom = _app.Themes.Themes.Count(t => !t.BuiltIn);
        if (custom > 0) AddChip(Loc.F("inst_chip_themes", custom));
        var songs = HistoryService.Load().Count;
        if (songs > 0) AddChip(Loc.F("inst_chip_history", songs.ToString("N0", Loc.Instance.Culture)));
    }

    void AddChip(string text) => Chips.Children.Add(new Border
    {
        Style = (Style)FindResource("Chip"),
        Child = new TextBlock { Text = text, FontSize = 12, Foreground = new SolidColorBrush(Color.FromRgb(0x4C, 0x1D, 0x95)) },
    });

    bool Clear => _hasOld && FreshChoice.IsChecked == true;

    void OnChoice(object sender, RoutedEventArgs e)
    {
        if (IsInitialized) Refresh();
    }

    void Refresh()
    {
        BackupBox.Visibility = Clear ? Visibility.Visible : Visibility.Collapsed;
        // keeping the settings keeps their theme
        ThemePanel.Visibility = _hasOld && !Clear ? Visibility.Collapsed : Visibility.Visible;
        GoButton.Content = Loc.T(Clear ? "welcome_clear_start" : _hasOld ? "welcome_continue" : "welcome_start");
        GoButton.Background = Clear
            ? new LinearGradientBrush(Color.FromRgb(0xE1, 0x1D, 0x48), Color.FromRgb(0xF4, 0x72, 0xB6), 0)
            : (Brush)FindResource("Grad");
    }

    async void OnGo(object sender, RoutedEventArgs e)
    {
        GoButton.IsEnabled = false;
        try
        {
            if (Clear)
            {
                if (BackupBox.IsChecked == true)
                {
                    var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                    BackupService.Export(_app.Settings, Path.Combine(docs, $"MiniPlayer-backup-{DateTime.Now:yyyy-MM-dd-HHmm}.zip"));
                }
                _app.History.Clear();
                if (Directory.Exists(AppInfo.ThemesDir))
                    foreach (var f in Directory.GetFiles(AppInfo.ThemesDir)) File.Delete(f);
                _app.Settings.Replace(new AppSettings());
                _app.Themes.Load();
            }
            if (ThemePanel.Visibility == Visibility.Visible && ThemeList.SelectedItem is ThemeItem theme)
                _app.Themes.Apply("builtin:" + theme.Id);
            try { await AutoStartService.SetAsync(StartupBox.IsChecked == true); }
            catch { /* Windows may refuse (turned off in Task Manager): the switch in Settings shows the real state */ }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, AppInfo.Name, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        Close();
    }

    void OnDrag(object sender, System.Windows.Input.MouseButtonEventArgs e) => DragMove();
}
