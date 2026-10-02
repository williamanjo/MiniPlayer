using Microsoft.Toolkit.Uwp.Notifications;

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
                .AddText("Nova versão do MiniPlayer")
                .AddText($"A versão {version} está disponível (você usa a {current}).")
                .AddButton(new ToastButton().SetContent("Atualizar agora").AddArgument(ActionKey, ActionUpdate))
                .AddButton(new ToastButtonDismiss("Depois")),
            UpdateTag);

    public static void ShowUpdateDownloaded(string version) =>
        Show(new ToastContentBuilder()
                .AddArgument(ActionKey, ActionOpen)
                .AddText("Atualização do MiniPlayer baixada")
                .AddText($"A versão {version} será instalada na próxima vez que o MiniPlayer abrir.")
                .AddButton(new ToastButton().SetContent("Reiniciar agora").AddArgument(ActionKey, ActionRestart))
                .AddButton(new ToastButtonDismiss("Depois")),
            UpdateTag);

    public static void ShowUpdated(string version) =>
        Show(new ToastContentBuilder()
                .AddArgument(ActionKey, ActionOpen)
                .AddText("MiniPlayer atualizado")
                .AddText($"Agora você está na versão {version}."),
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
