using System.Drawing;
using System.Windows.Forms;

namespace MiniPlayer.Services;

/// <summary>Notification-area icon and its menu.</summary>
public sealed class TrayIcon : IDisposable
{
    readonly NotifyIcon _icon;
    readonly ToolStripMenuItem _taskbarItem;
    readonly ToolStripMenuItem _pinItem;
    readonly ToolStripMenuItem _updateItem;

    public TrayIcon(App app)
    {
        _taskbarItem = new ToolStripMenuItem("Modo barra de tarefas", null, (_, _) =>
            app.SetMode(app.Settings.Data.Mode == PlayerMode.Taskbar ? PlayerMode.Floating : PlayerMode.Taskbar));
        _pinItem = new ToolStripMenuItem("Fixar no topo", null, (_, _) =>
            app.ViewModel.PinOnTop = !app.ViewModel.PinOnTop);

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

        var sleepItem = new ToolStripMenuItem("Timer para dormir");
        sleepItem.DropDownItems.Add("-"); // placeholder so the arrow shows
        sleepItem.DropDownOpening += (_, _) =>
        {
            var timer = app.SleepTimer;
            sleepItem.DropDownItems.Clear();
            foreach (var minutes in new[] { 15, 30, 45, 60, 90 })
                sleepItem.DropDownItems.Add($"{minutes} minutos", null, (_, _) => timer.Start(TimeSpan.FromMinutes(minutes)));
            sleepItem.DropDownItems.Add("No fim desta música", null, (_, _) => timer.StartEndOfTrack());
            if (timer.IsActive)
            {
                sleepItem.DropDownItems.Add(new ToolStripSeparator());
                sleepItem.DropDownItems.Add($"Cancelar (faltam {timer.RemainingText})", null, (_, _) => timer.Cancel());
            }
        };

        var menu = new ContextMenuStrip();
        menu.Items.Add(new ToolStripMenuItem($"MiniPlayer v{app.Updates.CurrentVersion}") { Enabled = false });
        menu.Items.Add(_updateItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Mostrar player", null, (_, _) => app.SetMode(PlayerMode.Floating));
        menu.Items.Add(_taskbarItem);
        menu.Items.Add(themesItem);
        menu.Items.Add(_pinItem);
        menu.Items.Add(sleepItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Configurações…", null, (_, _) => app.ShowSettings())
        {
            Font = new Font(SystemFonts.MenuFont ?? Control.DefaultFont, FontStyle.Bold),
        });
        menu.Items.Add("Sair", null, (_, _) => app.ExitApp());
        menu.Opening += (_, _) =>
        {
            _taskbarItem.Checked = app.Settings.Data.Mode == PlayerMode.Taskbar;
            _pinItem.Checked = app.ViewModel.PinOnTop;
            sleepItem.Text = app.SleepTimer.IsActive ? $"Timer para dormir (🌙 {app.SleepTimer.RemainingText})" : "Timer para dormir";
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

    /// <summary>Shows the "⬆ Atualizar para vX" menu entry.</summary>
    public void ShowUpdateMenu(string version)
    {
        _updateItem.Text = $"⬆ Atualizar para v{version}";
        _updateItem.Visible = true;
    }

    /// <summary>Fallback when Windows toasts are unavailable; clicking it offers the update.</summary>
    public void ShowBalloon(string title, string text) =>
        _icon.ShowBalloonTip(10000, title, text, ToolTipIcon.Info);

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
