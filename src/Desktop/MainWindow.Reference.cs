using System.Windows;
using System.Windows.Controls;

namespace ArchivePassword.Desktop;

public partial class MainWindow
{
    private void OnReferenceTheme(object sender, RoutedEventArgs args)
    {
        if (DataContext is not MainViewModel model || !model.SaveAppearanceCommand.CanExecute(null)) return;
        var canvas = (System.Windows.Media.SolidColorBrush)FindResource("CanvasBrush");
        var dark = canvas.Color.R + canvas.Color.G + canvas.Color.B < 384;
        model.SelectedAppearance = model.AppearanceOptions.Single(option => option.Code == (dark ? "light" : "dark"));
        model.SaveAppearanceCommand.Execute(null);
    }
    private void OnReferenceMenu(object sender, RoutedEventArgs args)
    {
        ReferenceCompactMenu.ContextMenu.PlacementTarget = ReferenceCompactMenu;
        ReferenceCompactMenu.ContextMenu.IsOpen = true;
    }
    private void OnReferencePage(object sender, RoutedEventArgs args)
    {
        if (sender is MenuItem { Tag: string page } && DataContext is MainViewModel model && int.TryParse(page, out var index)) model.Page = index;
    }
}
