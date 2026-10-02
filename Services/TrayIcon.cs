using System.Drawing;
using System.Windows.Forms;

namespace MiniPlayer.Services;

/// <summary>Notification-area icon and its menu.</summary>
public sealed class TrayIcon : IDisposable
{
    readonly NotifyIcon _icon;
    readonly ToolStripMenuItem _taskbarItem;
    readonly ToolStripMenuItem _pinItem;
    readonly ToolStripMenuItem _autoStartItem;
    readonly ToolStripMenuItem _autoHideItem;

    public TrayIcon(App app)
    {
        _taskbarItem = new ToolStripMenuItem("Modo barra de tarefas", null, (_, _) =>
            app.SetMode(app.Settings.Data.Mode == PlayerMode.Taskbar ? PlayerMode.Floating : PlayerMode.Taskbar));
        _pinItem = new ToolStripMenuItem("Fixar no topo", null, (_, _) =>
            app.ViewModel.PinOnTop = !app.ViewModel.PinOnTop);
        _autoStartItem = new ToolStripMenuItem("Iniciar com o Windows", null, (_, _) =>
            SettingsService.AutoStart = !SettingsService.AutoStart);

        _autoHideItem = new ToolStripMenuItem("Ocultar barra quando nada toca", null, (_, _) =>
            app.ViewModel.AutoHideWhenIdle = !app.ViewModel.AutoHideWhenIdle);

        var screensItem = new ToolStripMenuItem("Barra de tarefas da tela");
        screensItem.DropDownItems.Add("-"); // placeholder so the arrow shows
        screensItem.DropDownOpening += (_, _) =>
        {
            screensItem.DropDownItems.Clear();
            var current = app.Settings.Data.Mode == PlayerMode.Taskbar
                ? TaskbarHelper.GetTaskbar(app.Settings.Data.TaskbarMonitor)?.Device
                : null;
            foreach (var taskbar in TaskbarHelper.GetTaskbars())
            {
                var device = taskbar.Device;
                screensItem.DropDownItems.Add(new ToolStripMenuItem(taskbar.Label, null,
                    (_, _) => app.SetTaskbarMonitor(device)) { Checked = device == current });
            }
        };

        var menu = new ContextMenuStrip();
        menu.Items.Add("Mostrar player", null, (_, _) => app.SetMode(PlayerMode.Floating));
        menu.Items.Add(_taskbarItem);
        menu.Items.Add(screensItem);
        menu.Items.Add(_autoHideItem);
        menu.Items.Add(_pinItem);
        menu.Items.Add(_autoStartItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Sair", null, (_, _) => app.ExitApp());
        menu.Opening += (_, _) =>
        {
            _taskbarItem.Checked = app.Settings.Data.Mode == PlayerMode.Taskbar;
            _pinItem.Checked = app.ViewModel.PinOnTop;
            _autoHideItem.Checked = app.ViewModel.AutoHideWhenIdle;
            _autoStartItem.Checked = SettingsService.AutoStart;
        };

        _icon = new NotifyIcon
        {
            Icon = LoadIcon(),
            Text = "MiniPlayer",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) app.TogglePlayer();
        };
    }

    public void SetText(string text) =>
        _icon.Text = text.Length > 120 ? text[..117] + "..." : text;

    static Icon LoadIcon()
    {
        try
        {
            if (Environment.ProcessPath is { } path && Icon.ExtractAssociatedIcon(path) is { } icon)
                return icon;
        }
        catch
        {
            // fall through
        }
        return SystemIcons.Application;
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
