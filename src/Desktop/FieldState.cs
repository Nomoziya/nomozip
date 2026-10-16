using System.Windows;

namespace ArchivePassword.Desktop;

public static class FieldState
{
    public static readonly DependencyProperty HasErrorProperty = DependencyProperty.RegisterAttached(
        "HasError", typeof(bool), typeof(FieldState), new FrameworkPropertyMetadata(false));
    public static bool GetHasError(DependencyObject field) => (bool)field.GetValue(HasErrorProperty);
    public static void SetHasError(DependencyObject field, bool value) => field.SetValue(HasErrorProperty, value);
}
