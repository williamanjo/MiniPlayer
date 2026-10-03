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

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
