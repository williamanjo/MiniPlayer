using System.Windows;
using System.Windows.Automation;
using System.Windows.Media.Imaging;
using MiniPlayer.Localization;

namespace MiniPlayer.Views;

/// <summary>Update progress over a dimmed screen. Methods may be called from any thread.</summary>
public partial class UpdateWindow : Window
{
    readonly string _version;
    bool _installing;

    public UpdateWindow(string version)
    {
        InitializeComponent();
        _version = version;
        // the art carries a tagline: Portuguese for pt-BR, English for everyone else
        var art = Loc.Instance.Culture.Name == "pt-BR" ? "pt" : "en";
        Hero.Source = new BitmapImage(new Uri($"pack://application:,,,/MiniPlayer;component/Assets/update-hero-{art}.jpg"));
        SetProgress(0);
    }

    public void SetProgress(int percent) => Dispatcher.BeginInvoke(() =>
    {
        if (_installing) return; // a late progress report must not undo "Installing…"
        Status.Text = Loc.F("update_win_downloading", _version);
        // Velopack may report nothing until the end (small delta packages): animate instead of a stuck 0%
        Bar.IsIndeterminate = percent <= 0;
        Detail.Text = percent > 0 ? $"{percent}%" : "";
        Bar.Value = percent;
        AutomationProperties.SetName(Bar, Status.Text);
    });

    // the dimmed screen blocks everything: a click outside the card or Esc gets it out of the way
    // (the download goes on and the app still restarts when it is done)
    protected override void OnMouseLeftButtonDown(System.Windows.Input.MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (!Card.IsMouseOver) Hide();
    }

    protected override void OnKeyDown(System.Windows.Input.KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == System.Windows.Input.Key.Escape) Hide();
    }

    /// <summary>Download done: Velopack now replaces the files and restarts the app.</summary>
    /// <remarks>Queued after any pending progress; the caller awaits a moment so it gets drawn.</remarks>
    public void SetInstalling() => Dispatcher.BeginInvoke(() =>
    {
        _installing = true;
        Status.Text = Loc.T("update_win_installing");
        Detail.Text = "";
        Bar.IsIndeterminate = false;
        Bar.Value = 100;
    });
}
