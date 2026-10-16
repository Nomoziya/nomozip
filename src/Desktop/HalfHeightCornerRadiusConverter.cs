using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace ArchivePassword.Desktop;

// WPF scales oversized corner radii in both axes. Half the actual height keeps
// the straight edges of a capsule instead of producing an ellipse.
public sealed class HalfHeightCornerRadiusConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => new CornerRadius(value is double height && double.IsFinite(height) ? Math.Max(0, height / 2) : 0);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
