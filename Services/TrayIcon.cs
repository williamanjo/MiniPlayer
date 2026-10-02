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
    readonly ToolStripMenuItem _updateItem;

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

        var themesItem = new ToolStripMenuItem("Temas");
        themesItem.DropDownItems.Add("-"); // placeholder so the arrow shows
        themesItem.DropDownOpening += (_, _) =>
        {
            themesItem.DropDownItems.Clear();
            foreach (var theme in app.Themes.Themes)
            {
                var id = theme.Id;
                var label = string.IsNullOrWhiteSpace(theme.Author) || theme.BuiltIn ? theme.Name : $"{theme.Name} — {theme.Author}";
                // "&&": WinForms treats a single "&" as a mnemonic marker.
                themesItem.DropDownItems.Add(new ToolStripMenuItem(label.Replace("&", "&&"), null,
                    (_, _) => app.Themes.Apply(id)) { Checked = id == app.Themes.CurrentId });
            }
            foreach (var error in app.Themes.Errors)
                themesItem.DropDownItems.Add(new ToolStripMenuItem("⚠ " + error.Replace("&", "&&")) { Enabled = false });
            themesItem.DropDownItems.Add(new ToolStripSeparator());
            themesItem.DropDownItems.Add("Abrir pasta de temas", null, (_, _) => ThemeService.OpenFolder());
        };

        _updateItem = new ToolStripMenuItem("Atualizar", null, (_, _) => app.InstallUpdate())
        {
            Visible = false,
            Font = new Font(SystemFonts.MenuFont ?? Control.DefaultFont, FontStyle.Bold),
        };

        var menu = new ContextMenuStrip();
        menu.Items.Add(new ToolStripMenuItem($"MiniPlayer v{app.Updates.CurrentVersion}") { Enabled = false });
        menu.Items.Add(_updateItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Mostrar player", null, (_, _) => app.SetMode(PlayerMode.Floating));
        menu.Items.Add(_taskbarItem);
        menu.Items.Add(screensItem);
        menu.Items.Add(themesItem);
        menu.Items.Add(_autoHideItem);
        menu.Items.Add(_pinItem);
        menu.Items.Add(_autoStartItem);
        menu.Items.Add(new ToolStripSeparator());
        if (app.Updates.IsInstalled)
            menu.Items.Add("Verificar atualizações", null, (_, _) => app.CheckUpdatesManually());
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
        _icon.BalloonTipClicked += (_, _) => app.InstallUpdate();
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) app.TogglePlayer();
        };
    }

    /// <summary>Shows the "update available" toast and menu entry.</summary>
    public void ShowUpdate(string version)
    {
        _updateItem.Text = $"⬆ Atualizar para v{version}";
        _updateItem.Visible = true;
        _icon.ShowBalloonTip(10000, "Nova versão disponível",
            $"Versão {version} pronta para instalar. Clique aqui para atualizar.", ToolTipIcon.Info);
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
