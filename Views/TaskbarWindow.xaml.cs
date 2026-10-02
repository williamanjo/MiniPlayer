using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using MiniPlayer.Services;
using MiniPlayer.ViewModels;

namespace MiniPlayer.Views;

/// <summary>
/// Compact player that sits on top of the Windows 11 taskbar, left of the notification area.
/// Windows 11 has no API to embed windows in the taskbar, so this is a topmost overlay
/// that re-asserts its position and z-order on a timer.
/// </summary>
public partial class TaskbarWindow : Window
{
    const double DesignWidth = 380;
    const double TrayGap = 8;

    readonly SettingsService _settings;
    readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    IntPtr _hwnd;
    int _dragStartX;
    double _dragStartOffset;
    double _scale = 1;

    public TaskbarWindow(PlayerViewModel viewModel, SettingsService settings)
    {
        InitializeComponent();
        DataContext = viewModel;
        _settings = settings;
        _timer.Tick += (_, _) => Reposition();
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible) { Reposition(); _timer.Start(); }
            else _timer.Stop();
        };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwnd = new WindowInteropHelper(this).Handle;
        TaskbarHelper.MakeToolWindow(_hwnd);
    }

    public void Reposition()
    {
        if (_hwnd == IntPtr.Zero) return;

        var taskbar = TaskbarHelper.GetTaskbar(_settings.Data.TaskbarMonitor);
        var suppressed = taskbar is null || !taskbar.IsHorizontal || taskbar.IsHidden
                         || TaskbarHelper.IsForegroundFullscreen(_hwnd)
                         || (ViewModel.ShouldAutoHide && !IsMouseOver && Root.ContextMenu is not { IsOpen: true });
        // Opacity 0 makes the layered window fully click-through without stopping the timer.
        Opacity = suppressed ? 0 : 1;
        IsHitTestVisible = !suppressed;
        if (taskbar is null || suppressed) return;

        // Use the target monitor's DPI: the window may still sit on another screen.
        var scale = _scale = taskbar.Scale;
        var width = (int)Math.Round(DesignWidth * scale);
        var right = taskbar.TrayLeft - (int)Math.Round((TrayGap + _settings.Data.TaskbarOffset) * scale);
        var x = Math.Max(taskbar.Bounds.Left, right - width);

        TaskbarHelper.PlaceTopmost(_hwnd, x, taskbar.Bounds.Top, width, taskbar.Bounds.Height);
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

    /// <summary>Double-click anywhere outside the buttons: back to the floating player.</summary>
    void OnRootMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2) return;
        e.Handled = true;
        ((App)Application.Current).SetMode(PlayerMode.Floating);
    }

    void OnDragStart(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            OnRootMouseDown(sender, e);
            return;
        }
        TaskbarHelper.GetCursorPos(out var p);
        _dragStartX = p.X;
        _dragStartOffset = _settings.Data.TaskbarOffset;
        ((UIElement)sender).CaptureMouse();
        e.Handled = true;
    }

    void OnDragMove(object sender, MouseEventArgs e)
    {
        if (!((UIElement)sender).IsMouseCaptured) return;
        TaskbarHelper.GetCursorPos(out var p);
        _settings.Data.TaskbarOffset = Math.Max(0, _dragStartOffset + (_dragStartX - p.X) / _scale);
        Reposition();
    }

    void OnDragEnd(object sender, MouseButtonEventArgs e)
    {
        var element = (UIElement)sender;
        if (!element.IsMouseCaptured) return;
        element.ReleaseMouseCapture();
        _settings.Save();
    }

    void OnSeek(object sender, MouseButtonEventArgs e)
    {
        var area = (FrameworkElement)sender;
        ((PlayerViewModel)DataContext).SeekTo(e.GetPosition(area).X / area.ActualWidth);
        e.Handled = true;
    }

    void OnMenuOpening(object sender, ContextMenuEventArgs e)
    {
        _ = SessionMenu.FillAsync(SourcesMenu, ViewModel);
        ThemeMenu.Fill(ThemesMenu);

        // A never-active window never gets deactivated, so the menu would never close on
        // outside clicks. Let the window activate while the menu is open.
        TaskbarHelper.SetNoActivate(_hwnd, false);
        TaskbarHelper.Activate(_hwnd);
    }

    void OnMenuClosed(object sender, RoutedEventArgs e) => TaskbarHelper.SetNoActivate(_hwnd, true);

    protected override void OnDeactivated(EventArgs e)
    {
        base.OnDeactivated(e);
        if (Root.ContextMenu is { IsOpen: true } menu) menu.IsOpen = false;
    }

    void OnFloating(object sender, RoutedEventArgs e) => ((App)Application.Current).SetMode(PlayerMode.Floating);

    void OnOpenSettings(object sender, RoutedEventArgs e) => ((App)Application.Current).ShowSettings("Taskbar");

    void OnExit(object sender, RoutedEventArgs e) => ((App)Application.Current).ExitApp();
}
