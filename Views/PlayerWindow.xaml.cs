using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using MiniPlayer.Services;
using MiniPlayer.ViewModels;

namespace MiniPlayer.Views;

/// <summary>Floating mini player.</summary>
public partial class PlayerWindow : Window
{
    /// <summary>Distance (DIP) from a screen edge at which the player snaps to it.</summary>
    const double SnapDistance = 24;
    const double MinZoom = 0.7, MaxZoom = 2.0;

    // Content size (before zoom), Root grid units.
    const double DefaultWidth = 384, DefaultHeight = 129, LyricsHeight = 200;
    const double MinContentWidth = 200, MinContentHeight = 56, MaxContentSize = 900;
    // Body padding inside Root (14 + 10 horizontally, 10 + 10 vertically).
    const double PadX = 24, PadY = 20;

    enum LayoutMode { Mini, Standard, Tall }

    readonly SettingsService _settings;
    LayoutMode _mode = LayoutMode.Standard;
    double? _heightBeforeLyrics;

    public PlayerWindow(PlayerViewModel viewModel, SettingsService settings)
    {
        InitializeComponent();
        DataContext = viewModel;
        _settings = settings;

        foreach (var part in new FrameworkElement[] { MiniTransport, StdTransport, TallTransport, StdWindowButtons, TallWindowButtons, StdLyrics, TallLyrics })
            part.Tag = Visibility.Visible;

        ApplyZoom(settings.Data.FloatingScale);
        SetContentSize(
            settings.Data.FloatingWidth ?? DefaultWidth,
            settings.Data.FloatingHeight ?? (viewModel.ShowLyrics ? LyricsHeight : DefaultHeight));

        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PlayerViewModel.ShowLyrics)) OnLyricsToggled();
        };
        Loaded += (_, _) => RestorePosition();
        SizeChanged += OnSizeChanged;
    }

    void RestorePosition()
    {
        var bottom = _settings.Data.Bottom
                     ?? (_settings.Data.Top is double top ? top + ActualHeight : (double?)null);
        if (_settings.Data.Left is double left && bottom is double b
            && left + 40 >= SystemParameters.VirtualScreenLeft
            && b - 40 >= SystemParameters.VirtualScreenTop
            && left + 40 <= SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth
            && b <= SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight + 40)
        {
            Left = left;
            Top = b - ActualHeight;
        }
        else
        {
            // Default: docked bottom-right, just above the taskbar.
            var area = SystemParameters.WorkArea;
            Left = area.Right - ActualWidth;
            Top = area.Bottom - ActualHeight;
        }
        KeepInsideWorkArea();
    }

    /// <summary>
    /// Size changes not driven by a grip (lyrics on/off, zoom) keep the bottom edge fixed, so a
    /// player docked above the taskbar grows upward and drops back down when it shrinks.
    /// </summary>
    void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!IsLoaded || _grip is not null || e.PreviousSize.Height <= 0) return;
        if (e.HeightChanged) Top += e.PreviousSize.Height - e.NewSize.Height;
        KeepInsideWorkArea();
    }

    #region Responsive layout

    void SetContentSize(double width, double height)
    {
        Root.Width = Math.Round(Math.Clamp(width, MinContentWidth, MaxContentSize));
        Root.Height = Math.Round(Math.Clamp(height, MinContentHeight, MaxContentSize));
        ApplyResponsiveLayout();
    }

    /// <summary>
    /// Picks a layout for the available space: squashed = one-row mini, tall and narrow =
    /// portrait card, otherwise the standard layout. Optional parts drop out as width shrinks.
    /// </summary>
    void ApplyResponsiveLayout()
    {
        var w = Root.Width - PadX;
        var h = Root.Height - PadY;
        var lyrics = ViewModel.ShowLyrics;

        _mode = h < 108 ? LayoutMode.Mini
              : h >= 290 && h >= w * 0.9 ? LayoutMode.Tall
              : LayoutMode.Standard;

        MiniLayout.Visibility = Vis(_mode == LayoutMode.Mini);
        StandardLayout.Visibility = Vis(_mode == LayoutMode.Standard);
        TallLayout.Visibility = Vis(_mode == LayoutMode.Tall);

        switch (_mode)
        {
            case LayoutMode.Mini:
                MiniCover.Width = MiniCover.Height = Math.Max(24, h);
                MiniCover.Visibility = Vis(w >= 300);
                MiniText.Visibility = Vis(w >= 230);
                MiniTransport.Tag = Vis(w >= 470);
                break;

            case LayoutMode.Standard:
                StdTransport.Tag = Vis(w >= 300);
                StdWindowButtons.Tag = Vis(w >= 310);
                StdCover.Visibility = Vis(w >= 270);
                // Without lyrics the spare height goes to the cover.
                StdCover.Width = StdCover.Height = lyrics ? 84 : Math.Clamp(h - 34, 84, Math.Max(84, Math.Min(w * 0.42, 260)));
                break;

            case LayoutMode.Tall:
                TallTransport.Tag = Vis(w >= 250);
                TallWindowButtons.Tag = Vis(w >= 150);
                // ~146 for buttons, title, progress and controls; ~70 for lyrics.
                TallCover.Width = TallCover.Height = Math.Clamp(Math.Min(w, h - 146 - (lyrics ? 70 : 0)), 60, Math.Max(60, w));
                break;
        }
    }

    /// <summary>Previous/next lyric lines only when there is room for them.</summary>
    void OnLyricsSizeChanged(object sender, SizeChangedEventArgs e) =>
        ((FrameworkElement)sender).Tag = Vis(e.NewSize.Height >= 68);

    /// <summary>Lyrics on: grow (upward) if there is no room; off: give that height back.</summary>
    void OnLyricsToggled()
    {
        if (ViewModel.ShowLyrics)
        {
            if (_mode == LayoutMode.Standard && Root.Height < LyricsHeight)
            {
                _heightBeforeLyrics = Root.Height;
                Root.Height = LyricsHeight;
            }
        }
        else
        {
            if (_heightBeforeLyrics is double previous && Math.Abs(Root.Height - LyricsHeight) < 1)
                Root.Height = previous;
            _heightBeforeLyrics = null;
        }
        ApplyResponsiveLayout();
        SaveState();
    }

    static Visibility Vis(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;

    #endregion

    #region Zoom

    public double Zoom => RootScale.ScaleX;

    void ApplyZoom(double zoom)
    {
        zoom = Math.Round(Math.Clamp(zoom, MinZoom, MaxZoom), 2);
        RootScale.ScaleX = RootScale.ScaleY = zoom;
    }

    /// <summary>Zoom keeping the right edge in place (the player usually sits bottom-right).</summary>
    public void ZoomTo(double zoom)
    {
        var right = Left + ActualWidth;
        ApplyZoom(zoom);
        UpdateLayout();
        Left = right - ActualWidth;
        KeepInsideWorkArea();
        SaveState();
    }

    void OnResetSize(object sender, RoutedEventArgs e) => ResetSize();

    public void ResetSize()
    {
        var right = Left + ActualWidth;
        _heightBeforeLyrics = null;
        ApplyZoom(1);
        SetContentSize(DefaultWidth, ViewModel.ShowLyrics ? LyricsHeight : DefaultHeight);
        UpdateLayout();
        Left = right - ActualWidth;
        KeepInsideWorkArea();
        SaveState();
    }

    #endregion

    void SaveState()
    {
        _settings.Data.FloatingScale = Zoom;
        _settings.Data.FloatingWidth = Root.Width;
        _settings.Data.FloatingHeight = Root.Height;
        if (IsLoaded)
        {
            _settings.Data.Left = Left;
            _settings.Data.Top = Top;
            _settings.Data.Bottom = Top + ActualHeight;
        }
        _settings.Save();
    }

    #region Resize grips

    // Cursor tracked in screen space: the window itself moves while dragging the left/top sides.
    string? _grip;
    Point _gripStart;
    double _gripWidth, _gripHeight, _gripRight, _gripBottom;

    Point CursorDip()
    {
        TaskbarHelper.GetCursorPos(out var p);
        var dpi = VisualTreeHelper.GetDpi(this);
        return new Point(p.X / dpi.DpiScaleX, p.Y / dpi.DpiScaleY);
    }

    void OnGripDown(object sender, MouseButtonEventArgs e)
    {
        var grip = (FrameworkElement)sender;
        _grip = (string)grip.Tag;
        _gripStart = CursorDip();
        _gripWidth = Root.Width;
        _gripHeight = Root.Height;
        _gripRight = Left + ActualWidth;
        _gripBottom = Top + ActualHeight;
        grip.CaptureMouse();
        e.Handled = true; // no window drag
    }

    void OnGripMove(object sender, MouseEventArgs e)
    {
        if (_grip is null || !((UIElement)sender).IsMouseCaptured) return;
        var cursor = CursorDip();
        var dx = (cursor.X - _gripStart.X) / Zoom;
        var dy = (cursor.Y - _gripStart.Y) / Zoom;

        var width = _grip.Contains('L') ? _gripWidth - dx : _grip.Contains('R') ? _gripWidth + dx : _gripWidth;
        var height = _grip.Contains('T') ? _gripHeight - dy : _grip.Contains('B') ? _gripHeight + dy : _gripHeight;
        SetContentSize(width, height);
        UpdateLayout();

        // Opposite edge stays put.
        if (_grip.Contains('L')) Left = _gripRight - ActualWidth;
        if (_grip.Contains('T')) Top = _gripBottom - ActualHeight;
    }

    void OnGripUp(object sender, MouseButtonEventArgs e)
    {
        var grip = (UIElement)sender;
        if (!grip.IsMouseCaptured) return;
        grip.ReleaseMouseCapture();
        _grip = null;
        _heightBeforeLyrics = null; // the user picked this size
        KeepInsideWorkArea();
        SaveState();
    }

    #endregion

    Rect WorkArea()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return SystemParameters.WorkArea;
        var r = TaskbarHelper.GetWorkArea(hwnd);
        var dpi = VisualTreeHelper.GetDpi(this);
        return new Rect(r.Left / dpi.DpiScaleX, r.Top / dpi.DpiScaleY, r.Width / dpi.DpiScaleX, r.Height / dpi.DpiScaleY);
    }

    void KeepInsideWorkArea()
    {
        var a = WorkArea();
        Left = Math.Max(a.Left, Math.Min(Left, a.Right - ActualWidth));
        Top = Math.Max(a.Top, Math.Min(Top, a.Bottom - ActualHeight));
    }

    /// <summary>Dropped near a screen edge: stick to it.</summary>
    void SnapToEdges()
    {
        var a = WorkArea();
        if (Math.Abs(Left - a.Left) < SnapDistance) Left = a.Left;
        if (Math.Abs(Left + ActualWidth - a.Right) < SnapDistance) Left = a.Right - ActualWidth;
        if (Math.Abs(Top - a.Top) < SnapDistance) Top = a.Top;
        if (Math.Abs(Top + ActualHeight - a.Bottom) < SnapDistance) Top = a.Bottom - ActualHeight;
        KeepInsideWorkArea();
    }

    void OnDragArea(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed) return;
        DragMove();
        SnapToEdges();
        SaveState();
    }

    void OnSeek(object sender, MouseButtonEventArgs e)
    {
        var area = (FrameworkElement)sender;
        ((PlayerViewModel)DataContext).SeekTo(e.GetPosition(area).X / area.ActualWidth);
        e.Handled = true;
    }

    PlayerViewModel ViewModel => (PlayerViewModel)DataContext;

    void OnWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            ZoomTo(Zoom + (e.Delta > 0 ? 0.1 : -0.1));
            e.Handled = true;
            return;
        }
        ViewModel.ChangeVolume(e.Delta);
        e.Handled = true;
    }

    void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Middle) return;
        ViewModel.ToggleMute();
        e.Handled = true;
    }

    void OnPickSource(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true; // no window drag
        var menu = new ContextMenu { PlacementTarget = (UIElement)sender, Placement = PlacementMode.Bottom };
        _ = SessionMenu.FillAsync(menu, ViewModel);
        menu.IsOpen = true;
    }

    void OnMenuOpened(object sender, RoutedEventArgs e) => ThemeMenu.Fill(ThemesMenu);

    void OnOpenSettings(object sender, RoutedEventArgs e) => ((App)Application.Current).ShowSettings();

    void OnTogglePin(object sender, RoutedEventArgs e) => ViewModel.PinOnTop = !ViewModel.PinOnTop;

    void OnToggleLyrics(object sender, RoutedEventArgs e) => ViewModel.ShowLyrics = !ViewModel.ShowLyrics;

    void OnToTaskbar(object sender, RoutedEventArgs e) => ((App)Application.Current).SetMode(PlayerMode.Taskbar);

    void OnClose(object sender, RoutedEventArgs e) => Hide();
}
