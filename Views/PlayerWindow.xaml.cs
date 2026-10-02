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
    const double MinScale = 0.7, MaxScale = 2.0;
    static readonly double[] ScalePresets = [0.75, 1, 1.25, 1.5, 2];

    readonly SettingsService _settings;

    public PlayerWindow(PlayerViewModel viewModel, SettingsService settings)
    {
        InitializeComponent();
        DataContext = viewModel;
        _settings = settings;
        ApplyScale(settings.Data.FloatingScale);
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
        if (!IsLoaded || e.PreviousSize.Height <= 0) return;
        if (e.HeightChanged) Top += e.PreviousSize.Height - e.NewSize.Height;
        KeepInsideWorkArea();
    }

    double Scale => RootScale.ScaleX;

    void ApplyScale(double scale)
    {
        scale = Math.Round(Math.Clamp(scale, MinScale, MaxScale), 2);
        RootScale.ScaleX = RootScale.ScaleY = scale;
    }

    /// <summary>Zoom keeping the right edge in place (the player usually sits bottom-right).</summary>
    void ZoomTo(double scale)
    {
        var right = Left + ActualWidth;
        ApplyScale(scale);
        UpdateLayout();
        Left = right - ActualWidth;
        KeepInsideWorkArea();
        SaveScaleAndPosition();
    }

    void SaveScaleAndPosition()
    {
        _settings.Data.FloatingScale = Scale;
        _settings.Data.Left = Left;
        _settings.Data.Top = Top;
        _settings.Data.Bottom = Top + ActualHeight;
        _settings.Save();
    }

    // Resize grips. Cursor tracked in screen space: the window itself moves while dragging the left side.
    string? _gripSide;
    double _gripStartX, _gripStartWidth, _gripStartRight, _gripBaseWidth;

    double CursorX()
    {
        TaskbarHelper.GetCursorPos(out var p);
        return p.X / VisualTreeHelper.GetDpi(this).DpiScaleX;
    }

    void OnGripDown(object sender, MouseButtonEventArgs e)
    {
        var grip = (FrameworkElement)sender;
        _gripSide = (string)grip.Tag;
        _gripStartX = CursorX();
        _gripStartWidth = ActualWidth;
        _gripStartRight = Left + ActualWidth;
        _gripBaseWidth = ActualWidth / Scale; // width at 100%
        grip.CaptureMouse();
        e.Handled = true; // no window drag
    }

    void OnGripMove(object sender, MouseEventArgs e)
    {
        if (_gripSide is null || !((UIElement)sender).IsMouseCaptured) return;
        var dx = CursorX() - _gripStartX;
        var width = _gripStartWidth + (_gripSide == "L" ? -dx : dx);
        ApplyScale(width / _gripBaseWidth);
        UpdateLayout();
        if (_gripSide == "L") Left = _gripStartRight - ActualWidth;
    }

    void OnGripUp(object sender, MouseButtonEventArgs e)
    {
        var grip = (UIElement)sender;
        if (!grip.IsMouseCaptured) return;
        grip.ReleaseMouseCapture();
        _gripSide = null;
        KeepInsideWorkArea();
        SaveScaleAndPosition();
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
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            ZoomTo(Scale + (e.Delta > 0 ? 0.1 : -0.1));
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
        var menu = new ContextMenu { PlacementTarget = SourcePicker, Placement = PlacementMode.Bottom };
        _ = SessionMenu.FillAsync(menu, ViewModel);
        menu.IsOpen = true;
    }

    void OnMenuOpened(object sender, RoutedEventArgs e)
    {
        ThemeMenu.Fill(ThemesMenu);

        SizeMenu.Items.Clear();
        foreach (var preset in ScalePresets)
        {
            var item = new MenuItem
            {
                Header = $"{preset * 100:0}%",
                IsCheckable = true,
                IsChecked = Math.Abs(Scale - preset) < 0.01,
            };
            item.Click += (_, _) => ZoomTo(preset);
            SizeMenu.Items.Add(item);
        }
        SizeMenu.Items.Add(new Separator());
        SizeMenu.Items.Add(new MenuItem { Header = "Arraste as bordas ou use Ctrl + roda do mouse", IsEnabled = false });
    }

    void OnTogglePin(object sender, RoutedEventArgs e) => ViewModel.PinOnTop = !ViewModel.PinOnTop;

    void OnToggleLyrics(object sender, RoutedEventArgs e) => ViewModel.ShowLyrics = !ViewModel.ShowLyrics;

    void OnToTaskbar(object sender, RoutedEventArgs e) => ((App)Application.Current).SetMode(PlayerMode.Taskbar);

    void OnClose(object sender, RoutedEventArgs e) => Hide();
}
