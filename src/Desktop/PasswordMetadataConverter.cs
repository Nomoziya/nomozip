using System.Globalization;
using System.Windows.Data;
using ArchivePassword.Core;

namespace ArchivePassword.Desktop;

public sealed class PasswordMetadataConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is SavedPassword password
        ? string.Join(" · ", new[] { password.Group, password.Source, password.MatchText.Length > 0 ? "优先：" + password.MatchText : "" }.Where(text => text.Length > 0)) : "";
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
