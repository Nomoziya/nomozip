using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

namespace ArchivePassword.Desktop;

public partial class MainWindow : Window
{
    private bool closingApproved;
    private CompletionNotification? completionNotification;
    internal bool NotifyExtractionComplete(int count)
    {
        completionNotification ??= new CompletionNotification(this, ReturnToExtractionResults);
        return completionNotification.Show(count);
    }
    internal void ReturnToExtractionResults()
    {
        if (DataContext is MainViewModel model) model.Page = 0;
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        if (!WindowPlacement.ValidationMode) Activate();
    }
    internal bool SuppressExitConfirmationForValidation { get; set; }
    public MainWindow()
    {
        InitializeComponent();
        Closed += (_, _) => completionNotification?.Dispose();
        void ArrangePanels()
        {
            var narrow = ActualWidth < 1000;
            var compact = ActualWidth < 800;
            SidebarColumn.Width = new GridLength(compact ? 120 : 176);
            SidebarContent.Margin = compact ? new Thickness(8, 12, 8, 12) : new Thickness(16, 24, 16, 24);
            MainContent.Margin = new Thickness(compact ? 12 : 24);
            ExtractionDropHint.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
            CompressionPage.SetCompactToolbar(compact);
            if (DataContext is MainViewModel model) model.IsNarrow = narrow;
            CompressionPage.ArrangePanels(narrow);
            ExtractionLayout.Height = narrow ? double.NaN : Math.Max(280, ExtractionViewport.ActualHeight);
            ExtractionViewport.VerticalScrollBarVisibility = narrow ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;
            SettingsScroll.VerticalScrollBarVisibility = narrow ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;
            ListRow.Height = narrow ? new GridLength(240) : new GridLength(1, GridUnitType.Star);
            SettingsColumn.Width = narrow ? new GridLength(0) : new GridLength(280);
            SettingsRow.Height = narrow ? GridLength.Auto : new GridLength(0);
            Grid.SetRow(SettingsPanel, narrow ? 1 : 0);
            Grid.SetColumn(SettingsPanel, narrow ? 0 : 2);
            SettingsPanel.Margin = narrow ? new Thickness(0, 16, 0, 0) : new Thickness(0);
        }
        SizeChanged += (_, _) => ArrangePanels();
        ExtractionViewport.SizeChanged += (_, _) => ArrangePanels();
        Loaded += (_, _) => ArrangePanels();
    }
    private void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths && DataContext is MainViewModel model)
        {
            if (model.Page == 3) model.AddCompressionSources(paths);
            else model.AddFiles(paths);
        }
    }
    private void OnQueueActions(object sender,RoutedEventArgs args)
    {
        if(sender is Button button && button.ContextMenu is {} menu) {menu.PlacementTarget=button;menu.IsOpen=true;}
    }
    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!closingApproved && !SuppressExitConfirmationForValidation && DataContext is MainViewModel { IsBusy: true } model)
        {
            e.Cancel = true;
            if (MessageBox.Show(this, "正在处理任务。取消任务并退出？", "退出", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                model.CancelCommand.Execute(null);
                await model.WaitForIdleAsync();
                closingApproved = true; Close();
            }
        }
    }
}
