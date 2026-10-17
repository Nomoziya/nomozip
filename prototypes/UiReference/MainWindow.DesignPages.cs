using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Nomozip.DesignPreview;

public partial class MainWindow
{
    private WorkspacePreview? workspaceView;
    private readonly Dictionary<Button, Border> workspaceIndicators = new();
    public WorkspacePreview WorkspaceView
    {
        get
        {
            if (workspaceView is not null) return workspaceView;
            DecorateWorkspaceNavigation();
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
        ShowTasks(false); WorkspaceView.ShowPage(page, english, state); SetWorkspaceNavigationLanguage(english); ExpandedWorkspace.Visibility = Visibility.Visible;
        FilesIndicator.Visibility = TasksIndicator.Visibility = Visibility.Collapsed;
        FilesNavigation.SetResourceReference(Control.BackgroundProperty, "Canvas"); FilesNavigation.SetResourceReference(Control.ForegroundProperty, "Text"); FilesNavLabel.SetResourceReference(TextBlock.ForegroundProperty, "Text");
        // Keep the adopted nav geometry; show the selected page using the same accent/selection pair.
        foreach (var button in Descendants<Button>(NavigationRail).Where(button => button.Tag is "vault" or "tools" or "settings"))
        {
            var selected = (string)button.Tag == page || page == "updates" && (string)button.Tag == "settings";
            button.SetResourceReference(Control.BackgroundProperty, selected ? "Selected" : "Canvas"); button.SetResourceReference(Control.ForegroundProperty, selected ? "NavText" : "Text");
            if (workspaceIndicators.TryGetValue(button, out var indicator)) indicator.Visibility = selected ? Visibility.Visible : Visibility.Collapsed;
        }
        if (page is "home" or "create") { FilesIndicator.Visibility = Visibility.Visible; FilesNavigation.SetResourceReference(Control.BackgroundProperty, "Selected"); FilesNavLabel.SetResourceReference(TextBlock.ForegroundProperty, "NavText"); }
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++) { var child = VisualTreeHelper.GetChild(parent, i); if (child is T target) yield return target; foreach (var nested in Descendants<T>(child)) yield return nested; }
    }
    private void ResetWorkspaceNavigation()
    {
        if (!IsInitialized || NavigationRail is null) return;
        foreach (var indicator in workspaceIndicators.Values) indicator.Visibility = Visibility.Collapsed;
        SetWorkspaceNavigationLanguage(false);
        foreach (var button in Descendants<Button>(NavigationRail).Where(button => button.Tag is "vault" or "tools" or "settings"))
        { button.SetResourceReference(Control.BackgroundProperty, "Canvas"); button.SetResourceReference(Control.ForegroundProperty, "Text"); }
    }
    private void DecorateWorkspaceNavigation()
    {
        foreach (var button in Descendants<Button>(NavigationRail).Where(button => button.Tag is "vault" or "tools" or "settings").ToArray())
        {
            if (button.Parent is not Panel parent) continue;
            foreach (var label in Descendants<TextBlock>(button)) label.SetBinding(TextBlock.ForegroundProperty, new System.Windows.Data.Binding("Foreground") { Source = button });
            var index = parent.Children.IndexOf(button); var dock = DockPanel.GetDock(button); parent.Children.RemoveAt(index);
            var grid = new Grid(); DockPanel.SetDock(grid, dock); grid.Children.Add(button);
            var indicator = new Border { Width = 4, Height = 28, CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(button.Margin.Left, 7, 0, 0), IsHitTestVisible = false, Visibility = Visibility.Collapsed };
            indicator.SetResourceReference(Border.BackgroundProperty, "Accent"); grid.Children.Add(indicator); workspaceIndicators[button] = indicator; parent.Children.Insert(index, grid);
        }
    }
    private void SetWorkspaceNavigationLanguage(bool english)
    {
        FilesNavLabel.Text = english ? "Files" : "文件"; TasksNavLabel.Text = english ? "Tasks" : "任务"; CompactNavigation.Content = english ? "Menu" : "菜单"; CompactNavigation.Width = english ? 76 : 56;
        foreach (var button in Descendants<Button>(NavigationRail).Where(button => button.Tag is "vault" or "tools" or "settings"))
            foreach (var label in Descendants<TextBlock>(button)) label.Text = (string)button.Tag switch { "vault" => english ? "Vault" : "密码库", "tools" => english ? "Tools" : "工具", _ => english ? "Settings" : "设置" };
    }
    public DialogPreview ShowDesignDialog(string scene, bool english = false) => new(this, scene, english);
    private void OnWorkspaceNavigation(object sender, RoutedEventArgs e)
    {
        var page = sender switch { FrameworkElement { Tag: string tag } => tag, _ => "home" }; ShowWorkspace(page, workspaceView?.English ?? false);
    }
    private void OnDesignDialog(object sender, RoutedEventArgs e) => ShowDesignDialog((string)((FrameworkElement)sender).Tag, workspaceView?.English ?? false).ShowDialog();
}
