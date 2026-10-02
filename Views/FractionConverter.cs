using System.Globalization;
using System.Windows.Data;

namespace MiniPlayer.Views;

/// <summary>value × parameter, e.g. a height that is 32% of its container.</summary>
public sealed class FractionConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is double size && double.TryParse(parameter?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var fraction)
            ? size * fraction
            : 0d;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
