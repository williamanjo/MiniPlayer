using System.Windows;
using System.Windows.Controls;
using MiniPlayer.Localization;
using MiniPlayer.Services;

namespace MiniPlayer.Views;

public partial class ThemePreview : UserControl
{
    public ThemePreview() => InitializeComponent();

    /// <summary>Restyles the sample with a theme (only this control) and shows its author credit.</summary>
    public void Show(ThemeDefinition theme)
    {
        Resources = ThemeService.BuildResources(theme);
        var show = !string.IsNullOrWhiteSpace(theme.Author)
                   && ((App)Application.Current).Settings.Data.ShowThemeAuthor;
        Credit.Text = show ? Loc.F("theme_credit", theme.Author!.Trim()) : "";
        CoverCredit.Text = show ? Loc.F("theme_by", theme.Author!.Trim()) : "";
        Credit.Visibility = CoverCreditBadge.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
    }
}
