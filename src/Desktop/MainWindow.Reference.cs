using System.Windows;
using System.Windows.Controls;

namespace ArchivePassword.Desktop;

public partial class MainWindow
{
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
