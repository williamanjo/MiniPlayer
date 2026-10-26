using MiniPlayer.Services;
using Velopack;

namespace MiniPlayer;

public static class Program
{
    [STAThread]
    public static void Main()
    {
        // Must run first: handles install/update/uninstall hooks and exits early for them.
        VelopackApp.Build()
            .OnBeforeUninstallFastCallback(_ =>
            {
                AutoStartService.SetAsync(false).GetAwaiter().GetResult();
                NotificationService.Uninstall();
            })
            // An update downloaded in the background ("Manter atualizado") is applied here.
            .SetAutoApplyOnStartup(true)
            .Run();

        // Velopack's silent setup starts the app when it finishes; our installer opens it itself
        // after the user picked a theme, so this start does nothing. (Set by Installer/, inherited.)
        if (Environment.GetEnvironmentVariable("MINIPLAYER_FROM_INSTALLER") == "1") return;

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
