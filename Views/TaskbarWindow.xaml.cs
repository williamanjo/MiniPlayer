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
    const double MinBarWidth = 200, MaxBarWidth = 900;
    const double TrayGap = 8;

    readonly SettingsService _settings;
    readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    IntPtr _hwnd;
    double _grabFromRight;
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
        var width = (int)Math.Round(BarWidth() * scale);
        var right = taskbar.TrayLeft - (int)Math.Round((TrayGap + _settings.Data.TaskbarOffset) * scale);
        var x = Math.Max(taskbar.Bounds.Left, right - width);

        TaskbarHelper.PlaceTopmost(_hwnd, x, taskbar.Bounds.Top, width, taskbar.Bounds.Height);
    }

    PlayerViewModel ViewModel => (PlayerViewModel)DataContext;

    double BarWidth() => Math.Clamp(_settings.Data.TaskbarWidth, MinBarWidth, MaxBarWidth);

    #region Resize grips

    string? _grip;
    int _gripX;
    double _gripWidth, _gripOffset;

    void OnGripDown(object sender, MouseButtonEventArgs e)
    {
        var grip = (FrameworkElement)sender;
        TaskbarHelper.GetCursorPos(out var p);
        _grip = (string)grip.Tag;
        _gripX = p.X;
        _gripWidth = BarWidth();
        _gripOffset = _settings.Data.TaskbarOffset;
        grip.CaptureMouse();
        e.Handled = true; // no drag along the bar, no double-click
    }

    void OnGripMove(object sender, MouseEventArgs e)
    {
        if (_grip is null || !((UIElement)sender).IsMouseCaptured) return;
        TaskbarHelper.GetCursorPos(out var p);
        var dx = (p.X - _gripX) / _scale;
        if (_grip == "L")
        {
            // right edge stays: wider to the left
            _settings.Data.TaskbarWidth = Math.Clamp(_gripWidth - dx, MinBarWidth, MaxBarWidth);
        }
        else
        {
            // left edge stays: the right edge moves toward the tray, at most up to it
            var delta = Math.Min(Math.Clamp(_gripWidth + dx, MinBarWidth, MaxBarWidth) - _gripWidth, _gripOffset);
            _settings.Data.TaskbarWidth = _gripWidth + delta;
            _settings.Data.TaskbarOffset = _gripOffset - delta;
        }
        Reposition();
    }

    void OnGripUp(object sender, MouseButtonEventArgs e)
    {
        var grip = (UIElement)sender;
        if (!grip.IsMouseCaptured) return;
        grip.ReleaseMouseCapture();
        _grip = null;
        _settings.Save();
        e.Handled = true;
    }

    #endregion

    void OnWheel(object sender, MouseWheelEventArgs e)
    {
        ViewModel.Wheel(e.Delta, onTaskbar: true);
        e.Handled = true;
    }

    void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Middle) return;
        ViewModel.MiddleClick();
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
        // where the cursor grabbed the player, from its right edge (kept when it moves to another screen)
        if (TaskbarHelper.GetTaskbar(_settings.Data.TaskbarMonitor) is { } current)
            _grabFromRight = current.TrayLeft - (TrayGap + _settings.Data.TaskbarOffset) * current.Scale - p.X;
        ((UIElement)sender).CaptureMouse();
        e.Handled = true;
    }

    void OnDragMove(object sender, MouseEventArgs e)
    {
        if (!((UIElement)sender).IsMouseCaptured) return;
        TaskbarHelper.GetCursorPos(out var p);
        // dragged onto another screen's taskbar: the player moves there, under the cursor
        var under = TaskbarHelper.GetTaskbars().FirstOrDefault(t => t.IsHorizontal && !t.IsHidden
            && p.X >= t.Bounds.Left && p.X < t.Bounds.Right && p.Y >= t.Bounds.Top - 40 && p.Y < t.Bounds.Bottom + 40);
        var current = TaskbarHelper.GetTaskbar(_settings.Data.TaskbarMonitor);
        var target = under ?? current;
        if (target is null) return;
        if (target.Device != current?.Device) _settings.Data.TaskbarMonitor = target.Device;
        // the grabbed point follows the cursor (offset is measured leftwards from the tray, in DIPs)
        _settings.Data.TaskbarOffset = Math.Max(0, (target.TrayLeft - (p.X + _grabFromRight)) / target.Scale - TrayGap);
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
        SleepMenu.Fill(SleepTimerMenu);

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

    void OnOpenHistory(object sender, RoutedEventArgs e) => ((App)Application.Current).ShowHistory();

    void OnExit(object sender, RoutedEventArgs e) => ((App)Application.Current).ExitApp();
}
