using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Nomozip.DesignPreview;

public partial class MainWindow
{
    private WorkspacePreview? workspaceView;
    public WorkspacePreview WorkspaceView
    {
        get
        {
            if (workspaceView is not null) return workspaceView;
            workspaceView = new WorkspacePreview(Model);
            workspaceView.Navigate += page => ShowWorkspace(page, workspaceView.English);
            workspaceView.OpenDialog += scene => ShowDesignDialog(scene, workspaceView.English).ShowDialog();
            workspaceView.OpenArchive += () => { ShowTasks(false); Model.Navigate(); ShowPreview(); };
            ExpandedWorkspace.Content = workspaceView;
            return workspaceView;
        }
    }
    public void ShowWorkspace(string page, bool english = false, string state = "typical")
    {
        ShowTasks(false); WorkspaceView.ShowPage(page, english, state); ExpandedWorkspace.Visibility = Visibility.Visible;
        FilesIndicator.Visibility = TasksIndicator.Visibility = Visibility.Collapsed;
        FilesNavigation.SetResourceReference(Control.BackgroundProperty, "Canvas"); FilesNavigation.SetResourceReference(Control.ForegroundProperty, "Text"); FilesNavLabel.SetResourceReference(TextBlock.ForegroundProperty, "Text");
        // Keep the adopted nav geometry; show the selected page using the same accent/selection pair.
        foreach (var button in Descendants<Button>(NavigationRail).Where(button => button.Tag is "vault" or "tools" or "settings"))
        {
            var selected = (string)button.Tag == page || page == "updates" && (string)button.Tag == "settings";
            button.SetResourceReference(Control.BackgroundProperty, selected ? "Selected" : "Canvas"); button.SetResourceReference(Control.ForegroundProperty, selected ? "NavText" : "Text");
        }
        if (page is "home" or "create") { FilesIndicator.Visibility = Visibility.Visible; FilesNavigation.SetResourceReference(Control.BackgroundProperty, "Selected"); FilesNavLabel.SetResourceReference(TextBlock.ForegroundProperty, "NavText"); }
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++) { var child = VisualTreeHelper.GetChild(parent, i); if (child is T target) yield return target; foreach (var nested in Descendants<T>(child)) yield return nested; }
    }
    public DialogPreview ShowDesignDialog(string scene, bool english = false) => new(this, scene, english);
    private void OnWorkspaceNavigation(object sender, RoutedEventArgs e)
    {
        var page = sender switch { FrameworkElement { Tag: string tag } => tag, _ => "home" }; ShowWorkspace(page, workspaceView?.English ?? false);
    }
    private void OnDesignDialog(object sender, RoutedEventArgs e) => ShowDesignDialog((string)((FrameworkElement)sender).Tag, workspaceView?.English ?? false).ShowDialog();
}
