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

    readonly SettingsService _settings;

    public PlayerWindow(PlayerViewModel viewModel, SettingsService settings)
    {
        InitializeComponent();
        DataContext = viewModel;
        _settings = settings;
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
    /// Height changes (lyrics on/off, wrapped lines) keep the bottom edge fixed, so a player
    /// docked above the taskbar grows upward and drops back down when it shrinks.
    /// </summary>
    void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!IsLoaded || !e.HeightChanged || e.PreviousSize.Height <= 0) return;
        Top += e.PreviousSize.Height - e.NewSize.Height;
        KeepInsideWorkArea();
    }

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
        _settings.Data.Left = Left;
        _settings.Data.Top = Top;
        _settings.Data.Bottom = Top + ActualHeight;
        _settings.Save();
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
        var menu = new ContextMenu { PlacementTarget = SourcePicker, Placement = PlacementMode.Bottom };
        _ = SessionMenu.FillAsync(menu, ViewModel);
        menu.IsOpen = true;
    }

    void OnTogglePin(object sender, RoutedEventArgs e) => ViewModel.PinOnTop = !ViewModel.PinOnTop;

    void OnToggleLyrics(object sender, RoutedEventArgs e) => ViewModel.ShowLyrics = !ViewModel.ShowLyrics;

    void OnToTaskbar(object sender, RoutedEventArgs e) => ((App)Application.Current).SetMode(PlayerMode.Taskbar);

    void OnClose(object sender, RoutedEventArgs e) => Hide();
}
