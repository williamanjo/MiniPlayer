using System.IO;
using Microsoft.Toolkit.Uwp.Notifications;
using MiniPlayer.Localization;

namespace MiniPlayer.Services;

/// <summary>
/// Windows toast notifications. Unlike tray balloons they stay in the notification center after
/// the app closes, and clicking a button starts the app if needed (COM activation).
/// </summary>
public static class NotificationService
{
    const string UpdateTag = "update";

    // Toast arguments: action=update | restart | open
    public const string ActionKey = "action";
    public const string ActionUpdate = "update";
    public const string ActionRestart = "restart";
    public const string ActionOpen = "open";

    public static void ShowUpdateAvailable(string version, string current) =>
        Show(new ToastContentBuilder()
                .AddArgument(ActionKey, ActionOpen)
                .AddText(Loc.T("toast_new_title"))
                .AddText(Loc.F("toast_new_text", version, current))
                .AddButton(new ToastButton().SetContent(Loc.T("toast_update_now")).AddArgument(ActionKey, ActionUpdate))
                .AddButton(new ToastButtonDismiss(Loc.T("toast_later"))),
            UpdateTag);

    public static void ShowUpdateDownloaded(string version) =>
        Show(new ToastContentBuilder()
                .AddArgument(ActionKey, ActionOpen)
                .AddText(Loc.T("toast_downloaded_title"))
                .AddText(Loc.F("toast_downloaded_text", version))
                .AddButton(new ToastButton().SetContent(Loc.T("toast_restart_now")).AddArgument(ActionKey, ActionRestart))
                .AddButton(new ToastButtonDismiss(Loc.T("toast_later"))),
            UpdateTag);

    public static void ShowUpdated(string version) =>
        Show(new ToastContentBuilder()
                .AddArgument(ActionKey, ActionOpen)
                .AddText(Loc.T("toast_updated_title"))
                .AddText(Loc.F("toast_updated_text", version)),
            tag: null);

    /// <summary>Drops a stale "new version" toast from the notification center.</summary>
    public static void ClearUpdate()
    {
        try { ToastNotificationManagerCompat.History.Remove(UpdateTag); }
        catch { /* notifications unavailable */ }
    }

    /// <summary>Removes the toast registration (COM activator, app id) on uninstall.</summary>
    public static void Uninstall()
    {
        try { ToastNotificationManagerCompat.Uninstall(); }
        catch { /* best effort */ }
    }

    /// <summary>
    /// The toast library registers the app id with an Icon.png it extracts at each start, and that
    /// came out as the previous icon after an update (shell icon cache). The taskbar groups the
    /// app's windows under that id and shows this picture, so write it from app.ico ourselves.
    /// </summary>
    public static void WriteRegisteredIcon()
    {
        try
        {
            var apps = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ToastNotificationManagerCompat", "Apps");
            if (!Directory.Exists(apps)) return;
            var res = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/MiniPlayer;component/Assets/app.ico"));
            if (res is null) return;
            using var stream = res.Stream;
            using var icon = new System.Drawing.Icon(stream, 256, 256);
            using var bitmap = icon.ToBitmap();
            foreach (var dir in Directory.GetDirectories(apps, "*MiniPlayer*"))
            {
                var png = Path.Combine(dir, "Icon.png");
                bitmap.Save(png, System.Drawing.Imaging.ImageFormat.Png);
                ShellRefresh(png);
            }
        }
        catch
        {
            // cosmetic only
        }
    }

    [System.Runtime.InteropServices.DllImport("shell32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    static extern void SHChangeNotify(int eventId, uint flags, string? item1, IntPtr item2);

    /// <summary>Tells the shell a file's icon changed (it caches icons by path); null = all associations.</summary>
    public static void ShellRefresh(string? path)
    {
        const int SHCNE_UPDATEITEM = 0x2000, SHCNE_ASSOCCHANGED = 0x08000000;
        const uint SHCNF_PATHW = 0x0005;
        if (path is not null) SHChangeNotify(SHCNE_UPDATEITEM, SHCNF_PATHW, path, IntPtr.Zero);
        else SHChangeNotify(SHCNE_ASSOCCHANGED, 0, null, IntPtr.Zero);
    }

    static void Show(ToastContentBuilder builder, string? tag)
    {
        builder.Show(toast =>
        {
            if (tag is not null) toast.Tag = tag;
            toast.ExpirationTime = DateTimeOffset.Now.AddDays(3);
        });
    }
}
