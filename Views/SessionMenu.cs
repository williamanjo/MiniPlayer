using System.Windows.Controls;
using MiniPlayer.ViewModels;
using MiniPlayer.Localization;

namespace MiniPlayer.Views;

/// <summary>Fills a menu with every tab/app publishing media, so the user can pick which one to control.</summary>
internal static class SessionMenu
{
    public static async Task FillAsync(ItemsControl menu, PlayerViewModel vm)
    {
        menu.Items.Clear();
        menu.Items.Add(new MenuItem { Header = Loc.T("sessions_loading"), IsEnabled = false });

        var sessions = await vm.GetSessionsAsync();
        menu.Items.Clear();

        var auto = new MenuItem { Header = Loc.T("sessions_auto"), IsCheckable = true, IsChecked = !vm.IsPinned };
        auto.Click += (_, _) => vm.PinSession(null);
        menu.Items.Add(auto);
        menu.Items.Add(new Separator());

        if (sessions.Count == 0)
        {
            menu.Items.Add(new MenuItem { Header = Loc.T("sessions_none"), IsEnabled = false });
            return;
        }

        foreach (var s in sessions)
        {
            var title = string.IsNullOrWhiteSpace(s.Title) ? Loc.T("vm_untitled") : s.Title;
            var detail = string.Join(" · ", new[] { s.Artist, s.Source }.Where(x => !string.IsNullOrWhiteSpace(x)));
            var item = new MenuItem
            {
                // TextBlock header: underscores in titles must not become access keys.
                Header = new TextBlock { Text = $"{(s.IsPlaying ? "▶ " : "")}{title}  —  {detail}", MaxWidth = 420, TextTrimming = System.Windows.TextTrimming.CharacterEllipsis },
                IsCheckable = true,
                IsChecked = vm.IsPinned && s.IsCurrent,
            };
            var key = s.Key;
            item.Click += (_, _) => vm.PinSession(key);
            menu.Items.Add(item);
        }
        menu.Items.Add(new Separator());
        menu.Items.Add(new MenuItem
        {
            Header = new TextBlock { Text = Loc.T("sessions_one_per_browser"), MaxWidth = 420, TextWrapping = System.Windows.TextWrapping.Wrap },
            IsEnabled = false,
        });
    }
}
