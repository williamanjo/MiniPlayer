using System.Windows;
using System.Windows.Controls;
using MiniPlayer.Services;

namespace MiniPlayer.Views;

/// <summary>Fills a "Tema" submenu: built-in + folder themes, load errors, folder shortcuts.</summary>
internal static class ThemeMenu
{
    public static void Fill(MenuItem menu)
    {
        var themes = ((App)Application.Current).Themes;
        menu.Items.Clear();

        foreach (var theme in themes.Themes)
        {
            var id = theme.Id;
            var label = string.IsNullOrWhiteSpace(theme.Author) || theme.BuiltIn ? theme.Name : $"{theme.Name}  —  {theme.Author}";
            var item = new MenuItem
            {
                // TextBlock header: underscores in names must not become access keys.
                Header = new TextBlock { Text = label },
                IsCheckable = true,
                IsChecked = id == themes.CurrentId,
            };
            item.Click += (_, _) => themes.Apply(id);
            menu.Items.Add(item);
        }

        if (themes.Errors.Count > 0)
        {
            menu.Items.Add(new Separator());
            foreach (var error in themes.Errors)
                menu.Items.Add(new MenuItem { Header = new TextBlock { Text = "⚠ " + error }, IsEnabled = false });
        }

        menu.Items.Add(new Separator());
        var open = new MenuItem { Header = "Abrir pasta de temas" };
        open.Click += (_, _) => ThemeService.OpenFolder();
        menu.Items.Add(open);
        var reload = new MenuItem { Header = "Recarregar temas" };
        reload.Click += (_, _) =>
        {
            themes.Load();
            themes.Apply(themes.CurrentId);
        };
        menu.Items.Add(reload);
    }
}
