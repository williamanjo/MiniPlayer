using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MiniPlayer.Services;
using MiniPlayer.Localization;

namespace MiniPlayer.Views;

/// <summary>Listening history (searchable, grouped by day) and statistics.</summary>
public partial class HistoryWindow : Window
{
    public sealed record Item(PlayRecord Record, string Title, string Subtitle, string Time, string Day, ImageSource? Cover)
    {
        public bool HasCover => Cover is not null;
    }

    public sealed record Rank(string Name, string Detail, double Fraction);

    const int MaxItems = 1000;

    readonly App _app = (App)Application.Current;
    readonly Dictionary<string, ImageSource?> _covers = [];
    List<PlayRecord> _records = [];

    public HistoryWindow()
    {
        InitializeComponent();
        _app.History.Changed += OnHistoryChanged;
        Loc.Changed += OnHistoryChanged;
        Closed += (_, _) =>
        {
            _app.History.Changed -= OnHistoryChanged;
            Loc.Changed -= OnHistoryChanged;
        };
        // While open, keep above the (often always-on-top) player.
        Activated += (_, _) => Topmost = true;
        Deactivated += (_, _) => Topmost = false;
        Reload();
    }

    public void ShowTab(bool statistics) => Tabs.SelectedIndex = statistics ? 1 : 0;

    void OnHistoryChanged() => Dispatcher.BeginInvoke(Reload);

    void Reload()
    {
        _records = HistoryService.Load();
        ApplyFilter();
        UpdateStatistics();
    }

    #region Histórico

    void OnSearchChanged(object sender, TextChangedEventArgs e)
    {
        SearchHint.Visibility = string.IsNullOrEmpty(SearchBox.Text) ? Visibility.Visible : Visibility.Collapsed;
        ApplyFilter();
    }

    void ApplyFilter()
    {
        var query = SearchBox.Text.Trim();
        var matches = string.IsNullOrEmpty(query)
            ? _records
            : _records.Where(r => r.Title.Contains(query, StringComparison.CurrentCultureIgnoreCase)
                               || r.Artist.Contains(query, StringComparison.CurrentCultureIgnoreCase)).ToList();

        var items = matches.Take(MaxItems).Select(r => new Item(
            r,
            r.Title,
            string.Join(" · ", new[] { r.Artist, r.Source }.Where(x => !string.IsNullOrWhiteSpace(x))),
            r.Start.ToString("HH:mm"),
            DayLabel(r.Start),
            LoadCover(r.Cover))).ToList();

        var view = new ListCollectionView(items);
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(Item.Day)));
        HistoryList.ItemsSource = view;

        EmptyText.Visibility = _records.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        CountText.Text = _records.Count == 0 ? ""
            : string.IsNullOrEmpty(query) ? Loc.F("history_count", _records.Count)
            : Loc.F("history_count_filtered", matches.Count, _records.Count);
    }

    static string DayLabel(DateTime start)
    {
        var day = start.Date;
        if (day == DateTime.Today) return Loc.T("period_today");
        if (day == DateTime.Today.AddDays(-1)) return Loc.T("day_yesterday");
        var text = day.ToString(Loc.T("day_format"), Loc.Instance.Culture);
        return char.ToUpper(text[0]) + text[1..];
    }

    ImageSource? LoadCover(string? file)
    {
        if (file is null) return null;
        if (_covers.TryGetValue(file, out var cached)) return cached;
        ImageSource? image = null;
        var path = Path.Combine(HistoryService.CoversDir, file);
        if (File.Exists(path))
        {
            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.DecodePixelHeight = 88;
                bitmap.UriSource = new Uri(path);
                bitmap.EndInit();
                bitmap.Freeze();
                image = bitmap;
            }
            catch
            {
                // damaged thumbnail
            }
        }
        return _covers[file] = image;
    }

    PlayRecord? Selected => (HistoryList.SelectedItem as Item)?.Record;

    void OnCopy(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } r) return;
        try { Clipboard.SetText(string.IsNullOrWhiteSpace(r.Artist) ? r.Title : $"{r.Artist} — {r.Title}"); }
        catch { /* clipboard busy */ }
    }

    void OnSearchWeb(object sender, RoutedEventArgs e) =>
        SearchWeb((string)((FrameworkElement)sender).Tag);

    void OnItemDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (Selected is not null) SearchWeb("youtube");
    }

    void SearchWeb(string site)
    {
        if (Selected is not { } r) return;
        var q = Uri.EscapeDataString($"{r.Artist} {r.Title}".Trim());
        var url = site switch
        {
            "ytmusic" => $"https://music.youtube.com/search?q={q}",
            "spotify" => $"https://open.spotify.com/search/{q}",
            "deezer" => $"https://www.deezer.com/search/{q}",
            "google" => $"https://www.google.com/search?q={q}",
            _ => $"https://www.youtube.com/results?search_query={q}",
        };
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    void OnClear(object sender, RoutedEventArgs e)
    {
        if (_records.Count == 0) return;
        var answer = MessageBox.Show(this, Loc.F("history_clear_confirm", _records.Count),
            AppInfo.Name, MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (answer == MessageBoxResult.Yes) _app.History.Clear();
    }

    #endregion

    #region Estatísticas

    void OnPeriodChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TotalTime is not null) UpdateStatistics();
    }

    void UpdateStatistics()
    {
        var days = int.Parse((string)((PeriodBox.SelectedItem as ComboBoxItem)?.Tag ?? "7"));
        var since = days == 0 ? DateTime.MinValue : DateTime.Today.AddDays(1 - days);
        var records = _records.Where(r => r.Start >= since).ToList();

        TotalTime.Text = Duration(records.Sum(r => r.ListenedSec));
        TotalPlays.Text = records.Count.ToString();
        TotalArtists.Text = records.Select(r => r.Artist).Where(a => !string.IsNullOrWhiteSpace(a))
            .Distinct(StringComparer.CurrentCultureIgnoreCase).Count().ToString();

        TopArtists.ItemsSource = Ranking(
            records.Where(r => !string.IsNullOrWhiteSpace(r.Artist)).GroupBy(r => r.Artist, StringComparer.CurrentCultureIgnoreCase),
            g => g.Sum(r => r.ListenedSec),
            g => g.Count() == 1
                ? Loc.F("stats_artist_one", Duration(g.Sum(r => r.ListenedSec)))
                : Loc.F("stats_artist_many", g.Count(), Duration(g.Sum(r => r.ListenedSec))));

        TopTracks.ItemsSource = Ranking(
            records.GroupBy(r => $"{r.Title}\u001f{r.Artist}", StringComparer.CurrentCultureIgnoreCase),
            g => g.Count(),
            g => $"{g.Count()}×",
            g => string.IsNullOrWhiteSpace(g.First().Artist) ? g.First().Title : $"{g.First().Title} — {g.First().Artist}");

        TopSources.ItemsSource = Ranking(
            records.GroupBy(r => r.Source),
            g => g.Sum(r => r.ListenedSec),
            g => Duration(g.Sum(r => r.ListenedSec)));
    }

    static List<Rank> Ranking<T>(IEnumerable<IGrouping<string, T>> groups, Func<IGrouping<string, T>, double> score,
        Func<IGrouping<string, T>, string> detail, Func<IGrouping<string, T>, string>? name = null)
    {
        var top = groups.OrderByDescending(score).Take(8).ToList();
        var max = top.Count > 0 ? Math.Max(1, score(top[0])) : 1;
        return [.. top.Select(g => new Rank(name?.Invoke(g) ?? g.Key, detail(g), score(g) / max))];
    }

    static string Duration(int seconds)
    {
        var t = TimeSpan.FromSeconds(seconds);
        return t.TotalHours >= 1 ? $"{(int)t.TotalHours} h {t.Minutes} min" : $"{t.Minutes} min";
    }

    #endregion
}
