using System.Windows;
using System.Windows.Controls;
using MiniPlayer.Services;
using MiniPlayer.Localization;

namespace MiniPlayer.Views;

/// <summary>"Timer para dormir" submenu, shared by the player and taskbar context menus.</summary>
internal static class SleepMenu
{
    public static readonly int[] Minutes = [15, 30, 45, 60, 90];

    public static void Fill(MenuItem menu)
    {
        var timer = ((App)Application.Current).SleepTimer;
        menu.Items.Clear();
        menu.Header = timer.IsActive ? Loc.F("sleep_title_active", timer.RemainingText) : Loc.T("sleep_title");

        foreach (var minutes in Minutes)
        {
            var item = new MenuItem { Header = Loc.F("sleep_minutes", minutes) };
            item.Click += (_, _) => timer.Start(TimeSpan.FromMinutes(minutes));
            menu.Items.Add(item);
        }
        var endOfTrack = new MenuItem { Header = Loc.T("sleep_end_of_track") };
        endOfTrack.Click += (_, _) => timer.StartEndOfTrack();
        menu.Items.Add(endOfTrack);

        if (!timer.IsActive) return;
        menu.Items.Add(new Separator());
        var cancel = new MenuItem { Header = Loc.F("sleep_cancel", timer.RemainingText) };
        cancel.Click += (_, _) => timer.Cancel();
        menu.Items.Add(cancel);
    }
}
