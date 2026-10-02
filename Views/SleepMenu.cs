using System.Windows;
using System.Windows.Controls;
using MiniPlayer.Services;

namespace MiniPlayer.Views;

/// <summary>"Timer para dormir" submenu, shared by the player and taskbar context menus.</summary>
internal static class SleepMenu
{
    public static readonly int[] Minutes = [15, 30, 45, 60, 90];

    public static void Fill(MenuItem menu)
    {
        var timer = ((App)Application.Current).SleepTimer;
        menu.Items.Clear();
        menu.Header = timer.IsActive ? $"Timer para dormir (🌙 {timer.RemainingText})" : "Timer para dormir";

        foreach (var minutes in Minutes)
        {
            var item = new MenuItem { Header = $"{minutes} minutos" };
            item.Click += (_, _) => timer.Start(TimeSpan.FromMinutes(minutes));
            menu.Items.Add(item);
        }
        var endOfTrack = new MenuItem { Header = "No fim desta música" };
        endOfTrack.Click += (_, _) => timer.StartEndOfTrack();
        menu.Items.Add(endOfTrack);

        if (!timer.IsActive) return;
        menu.Items.Add(new Separator());
        var cancel = new MenuItem { Header = $"Cancelar (faltam {timer.RemainingText})" };
        cancel.Click += (_, _) => timer.Cancel();
        menu.Items.Add(cancel);
    }
}
