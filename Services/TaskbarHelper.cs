using System.Runtime.InteropServices;
using System.Text;

namespace MiniPlayer.Services;

/// <summary>Win32 helpers to locate the Windows taskbar and keep the overlay on top of it.</summary>
internal static class TaskbarHelper
{
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left, Top, Right, Bottom;
        public readonly int Width => Right - Left;
        public readonly int Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct MONITORINFOEX
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int X, Y; }

    /// <param name="Bounds">Taskbar rect, physical pixels.</param>
    /// <param name="TrayLeft">Left edge of the notification area (clock/icons), physical pixels.</param>
    /// <param name="IsHorizontal">False when the taskbar is docked left/right.</param>
    /// <param name="IsHidden">True while an auto-hide taskbar is slid off screen.</param>
    /// <param name="Device">Monitor device name, e.g. \\.\DISPLAY2.</param>
    /// <param name="Scale">Monitor DPI scale (1.0 = 96 DPI).</param>
    public sealed record TaskbarInfo(
        RECT Bounds, int TrayLeft, bool IsHorizontal, bool IsHidden, string Device, bool IsPrimary, double Scale)
    {
        public string Label
        {
            get
            {
                var digits = new string(Device.Reverse().TakeWhile(char.IsDigit).Reverse().ToArray());
                var name = digits.Length > 0 ? $"Tela {digits}" : Device;
                return IsPrimary ? $"{name} (principal)" : name;
            }
        }
    }

    static readonly IntPtr HWND_TOPMOST = new(-1);
    const uint SWP_NOSIZE = 0x0001, SWP_NOMOVE = 0x0002, SWP_NOACTIVATE = 0x0010;
    const int GWL_EXSTYLE = -20;
    const long WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000;
    const uint MONITOR_DEFAULTTONEAREST = 2;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr FindWindowEx(IntPtr parent, IntPtr childAfter, string? className, string? windowName);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

    [DllImport("user32.dll")]
    static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO info);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool GetMonitorInfoEx(IntPtr hMonitor, ref MONITORINFOEX info);

    [DllImport("shcore.dll")]
    static extern int GetDpiForMonitor(IntPtr hMonitor, int dpiType, out uint dpiX, out uint dpiY);

    delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int index, IntPtr value);

    [DllImport("user32.dll")]
    static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int GetClassName(IntPtr hWnd, StringBuilder name, int max);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetCursorPos(out POINT point);

    /// <summary>All taskbars (primary first, then left to right). One per monitor when "show on all displays" is on.</summary>
    public static List<TaskbarInfo> GetTaskbars()
    {
        var list = new List<TaskbarInfo>();
        var cls = new StringBuilder(64);
        EnumWindows((hwnd, _) =>
        {
            cls.Clear();
            GetClassName(hwnd, cls, cls.Capacity);
            var name = cls.ToString();
            if (name is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd"
                && Describe(hwnd, name == "Shell_TrayWnd") is { } info)
                list.Add(info);
            return true;
        }, IntPtr.Zero);
        return [.. list.OrderByDescending(t => t.IsPrimary).ThenBy(t => t.Bounds.Left)];
    }

    /// <summary>Taskbar on the given monitor, or the primary one if that monitor is gone.</summary>
    public static TaskbarInfo? GetTaskbar(string? device)
    {
        var all = GetTaskbars();
        return all.FirstOrDefault(t => t.Device == device) ?? all.FirstOrDefault();
    }

    static TaskbarInfo? Describe(IntPtr tray, bool isPrimary)
    {
        if (!GetWindowRect(tray, out var bounds) || bounds.Width <= 0 || bounds.Height <= 0) return null;

        var horizontal = bounds.Width > bounds.Height;

        var monitor = MonitorFromWindow(tray, MONITOR_DEFAULTTONEAREST);
        var info = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
        if (!GetMonitorInfoEx(monitor, ref info)) return null;

        var scale = GetDpiForMonitor(monitor, 0, out var dpiX, out _) == 0 ? dpiX / 96.0 : 1.0;

        // Primary: real notification area. Secondary taskbars only have a clock (no window for it),
        // so reserve roughly its width.
        int trayLeft;
        var notify = isPrimary ? FindWindowEx(tray, IntPtr.Zero, "TrayNotifyWnd", null) : IntPtr.Zero;
        if (notify != IntPtr.Zero && GetWindowRect(notify, out var n) && n.Width > 0)
            trayLeft = n.Left;
        else
            trayLeft = bounds.Right - (int)Math.Round((isPrimary ? 300 : 110) * scale);

        var m = info.rcMonitor;
        var visibleH = Math.Min(bounds.Bottom, m.Bottom) - Math.Max(bounds.Top, m.Top);
        var visibleW = Math.Min(bounds.Right, m.Right) - Math.Max(bounds.Left, m.Left);
        var hidden = horizontal ? visibleH < bounds.Height / 2 : visibleW < bounds.Width / 2;

        return new TaskbarInfo(bounds, trayLeft, horizontal, hidden, info.szDevice, isPrimary, scale);
    }

    /// <summary>True when the foreground window covers its whole monitor (fullscreen video, games...).</summary>
    public static bool IsForegroundFullscreen(IntPtr self)
    {
        var fg = GetForegroundWindow();
        if (fg == IntPtr.Zero || fg == self) return false;

        var cls = new StringBuilder(64);
        GetClassName(fg, cls, cls.Capacity);
        if (cls.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") return false;

        if (!GetWindowRect(fg, out var r)) return false;
        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(MonitorFromWindow(fg, MONITOR_DEFAULTTONEAREST), ref info)) return false;
        var m = info.rcMonitor;
        return r.Left <= m.Left && r.Top <= m.Top && r.Right >= m.Right && r.Bottom >= m.Bottom;
    }

    /// <summary>Work area (screen minus taskbar) of the monitor holding the window, physical pixels.</summary>
    public static RECT GetWorkArea(IntPtr hwnd)
    {
        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        GetMonitorInfo(MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST), ref info);
        return info.rcWork;
    }

    /// <summary>Hide from Alt+Tab and never steal focus from the app the user is in.</summary>
    public static void MakeToolWindow(IntPtr hwnd)
    {
        var style = (long)GetWindowLongPtr(hwnd, GWL_EXSTYLE);
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(style | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE));
    }

    /// <summary>Toggle WS_EX_NOACTIVATE (menus need an active window to close on outside clicks).</summary>
    public static void SetNoActivate(IntPtr hwnd, bool noActivate)
    {
        var style = (long)GetWindowLongPtr(hwnd, GWL_EXSTYLE);
        style = noActivate ? style | WS_EX_NOACTIVATE : style & ~WS_EX_NOACTIVATE;
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(style));
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool SetForegroundWindow(IntPtr hWnd);

    public static void Activate(IntPtr hwnd) => SetForegroundWindow(hwnd);

    public static void PlaceTopmost(IntPtr hwnd, int x, int y, int width, int height) =>
        SetWindowPos(hwnd, HWND_TOPMOST, x, y, width, height, SWP_NOACTIVATE);

    public static void BringTopmost(IntPtr hwnd) =>
        SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
}
