using System.Globalization;
using System.Windows.Data;
using ArchivePassword.Core;

namespace ArchivePassword.Desktop;

public sealed class PasswordLastUsedConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is DateTimeOffset timestamp
            ? Equals(parameter, "Full")
                ? timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.CurrentCulture)
                : Format(timestamp, DateTimeOffset.Now)
            : UiText.Get("T_9bd11dda28fc");

    internal static string Format(DateTimeOffset timestamp, DateTimeOffset now)
    {
        var local = timestamp.ToLocalTime();
        var today = now.ToLocalTime().Date;
        if (local.Date == today) return UiText.Format("Vault.UsedToday", local.ToString("HH:mm", CultureInfo.CurrentCulture));
        if (local.Date == today.AddDays(-1)) return UiText.Get("Vault.UsedYesterday");
        return local.ToString(local.Year == today.Year
            ? UiText.LanguageCode == "en" ? "MMM d" : "M月d日"
            : "yyyy-MM-dd", CultureInfo.CurrentCulture);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
