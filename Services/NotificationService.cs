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

    static void Show(ToastContentBuilder builder, string? tag)
    {
        builder.Show(toast =>
        {
            if (tag is not null) toast.Tag = tag;
            toast.ExpirationTime = DateTimeOffset.Now.AddDays(3);
        });
    }
}
