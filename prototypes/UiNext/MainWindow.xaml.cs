using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Nomozip.DesignPreview;

public partial class MainWindow : Window
{
    private bool previewVisible = true;
    public ArchiveModel Model { get; } = new();
    public MainWindow()
    {
        InitializeComponent(); DataContext = Model;
        InputBindings.Add(new KeyBinding(new PreviewCommand(() => RequestPreview()), Key.P, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(new PreviewCommand(() => OnPrototypeAction(this, new RoutedEventArgs())), Key.E, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(new PreviewCommand(() => Search.Focus()), Key.F, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(new PreviewCommand(() => Model.AllSelected = true), Key.A, ModifierKeys.Control));
    }
    private void OnMinimize(object sender, RoutedEventArgs e) => SystemCommands.MinimizeWindow(this);
    private void OnMaximize(object sender, RoutedEventArgs e) { if (WindowState == WindowState.Maximized) SystemCommands.RestoreWindow(this); else SystemCommands.MaximizeWindow(this); }
    private void OnClose(object sender, RoutedEventArgs e) => Close();
    private void OnTheme(object sender, RoutedEventArgs e) { if (Application.Current is App app) app.ApplyTheme(!app.Dark); }
    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!IsInitialized) return;
        Model.SetCompact(ActualWidth < 1000); UpdatePreview();
    }
    private void UpdatePreview()
    {
        // At narrow widths the same preview becomes a bounded overlay, preserving file-name space.
        if (!IsInitialized) return;
        var overlay = ActualWidth < 960;
        var destination = overlay ? ArchiveCard : Workspace;
        if (PreviewPane.Parent is Panel parent && parent != destination)
        {
            parent.Children.Remove(PreviewPane); destination.Children.Add(PreviewPane);
        }
        PreviewColumn.Width = new GridLength(previewVisible && !overlay ? 292 : 0);
        PreviewPane.Visibility = previewVisible ? Visibility.Visible : Visibility.Collapsed;
        Grid.SetRow(PreviewPane, 0);
        Grid.SetRowSpan(PreviewPane, overlay ? 3 : 1);
        Grid.SetColumn(PreviewPane, overlay ? 0 : 1);
        PreviewPane.HorizontalAlignment = overlay ? HorizontalAlignment.Right : HorizontalAlignment.Stretch;
        PreviewPane.Width = overlay ? 292 : double.NaN;
        Panel.SetZIndex(PreviewPane, overlay ? 2 : 0);
    }
    private void OnTogglePreview(object sender, RoutedEventArgs e) { previewVisible = !previewVisible; UpdatePreview(); }
    private void OnClosePreview(object sender, RoutedEventArgs e) { previewVisible = false; UpdatePreview(); }
    private void OnRequestPreview(object sender, MouseButtonEventArgs e) => RequestPreview();
    public void RequestPreview() { Model.RequestPreview(); previewVisible = true; UpdatePreview(); }
    public void HidePreview() { previewVisible = false; UpdatePreview(); }
    private void OnEditPreview(object sender, RoutedEventArgs e) => PrototypeNotice("编辑文本");
    private void OnMore(object sender, RoutedEventArgs e) { if (sender is Button { ContextMenu: { } menu } button) { menu.PlacementTarget = button; menu.IsOpen = true; } }
    private void OnPrototypeAction(object sender, RoutedEventArgs e) => PrototypeNotice((sender as FrameworkElement)?.Tag as string ?? "解压所选");
    private void PrototypeNotice(string action) => MessageBox.Show(this,
        $"“{action}”将在方向确认后的正式界面中接入。\n\n当前为独立视觉原型，可体验搜索、勾选、文件预览、窗口缩放及浅深主题。不会解压、写回或读取密码库。", "nomozip · 设计预览", MessageBoxButton.OK, MessageBoxImage.Information);
    private sealed class PreviewCommand(Action action) : ICommand
    {
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => action();
        public event EventHandler? CanExecuteChanged { add { } remove { } }
    }
}
