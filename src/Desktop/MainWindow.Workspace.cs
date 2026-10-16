using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace ArchivePassword.Desktop;

public partial class MainWindow
{
    private ArchiveContentsDialog? embeddedArchive;
    private ArchiveContentsViewModel? embeddedModel;
    private TaskCompletionSource? archiveClosed;
    private ArchivePreviewDialog? embeddedPreview;
    private ArchivePreviewViewModel? previewModel;
    private FileHashDialog? embeddedHashes;
    private MainViewModel? workspaceModel;
    internal Action<Window>? PrepareExtractionOptionsForValidation {get;set;}

    private void InitializeWorkspace()
    {
        // Keep one parameter form, moved into a modal confirmation surface on demand.
        ExtractionLayout.Children.Remove(SettingsPanel);
        DataContextChanged += (_, _) =>
        {
            if (workspaceModel is not null) workspaceModel.PropertyChanged -= WorkspaceChanged;
            workspaceModel = DataContext as MainViewModel;
            if (workspaceModel is not null) workspaceModel.PropertyChanged += WorkspaceChanged;
        };
        Closed += (_, _) => { ClosePreview(); embeddedHashes?.Close(); if (workspaceModel is not null) workspaceModel.PropertyChanged -= WorkspaceChanged; };
        InputBindings.Add(new KeyBinding(new RelayCommand(() => { if (embeddedModel?.PreviewCommand.CanExecute(null) == true) embeddedModel.PreviewCommand.Execute(null); }), Key.P, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(new RelayCommand(async () =>
        {
            if(workspaceModel?.Page==5 && embeddedHashes is not null)
            {if(embeddedHashes.Model.IsBusy) embeddedHashes.Model.Cancel();else workspaceModel.Page=4;}
            else if(workspaceModel?.Page==4) await CloseArchiveAsync();
        }), Key.Escape, ModifierKeys.None));
        InputBindings.Add(new KeyBinding(new RelayCommand(() => embeddedModel?.SelectVisibleCommand.Execute(null), () => embeddedModel?.CanSelect == true), Key.A, ModifierKeys.Control));
        foreach(var binding in InputBindings.OfType<KeyBinding>().Where(value=>value.Key==Key.E && value.Modifiers==ModifierKeys.Control).ToArray()) InputBindings.Remove(binding);
        InputBindings.Add(new KeyBinding(new RelayCommand(() =>
        {
            if(embeddedModel is not null) {if(embeddedModel.ExtractSelectedCommand.CanExecute(null))embeddedModel.ExtractSelectedCommand.Execute(null);}
            else OnPrepareExtraction(this,new RoutedEventArgs());
        }),Key.E,ModifierKeys.Control));
    }

    private void WorkspaceChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainViewModel.Page) || workspaceModel is null) return;
        ApplyWorkspaceMargin();
        if (workspaceModel.Page == 5 && embeddedHashes is null)
        {
            embeddedHashes = new FileHashDialog(dialogOwner: this);
            var content = (FrameworkElement)embeddedHashes.Content;
            embeddedHashes.Content = null; content.DataContext = embeddedHashes.Model;
            DisableDialogDefaults(content);
            ToolsHost.Content = content;
            embeddedHashes.EmbeddedClose = () => workspaceModel.Page = 4;
        }
        if (workspaceModel.Page != 4) ClosePreview();
    }

    internal async Task HostArchiveAsync(ArchiveContentsDialog dialog, ArchiveContentsViewModel model)
    {
        ClosePreview();
        embeddedArchive = dialog; embeddedModel = model; archiveClosed = new TaskCompletionSource();
        dialog.EmbeddedClose = CloseArchiveAsync;
        var content = (FrameworkElement)dialog.Content; dialog.Content = null; content.DataContext = model;
        DisableDialogDefaults(content);
        dialog.BrowserRoot.Children.Remove(dialog.BrowserHeader);
        dialog.BrowserHeader.DataContext = model;
        ArchiveHeaderHost.Content = dialog.BrowserHeader;
        ArchiveHost.Content = content; FilesEmptyState.Visibility = Visibility.Collapsed;
        FilesLandingHeader.Visibility = FilesLandingActions.Visibility = Visibility.Collapsed;
        ApplyWorkspaceMargin();
        if (DataContext is MainViewModel vm) vm.Page = 4;
        try { await archiveClosed.Task; }
        finally
        {
            ClosePreview(); ArchiveHost.Content = null; ArchiveHeaderHost.Content = null; dialog.EmbeddedClose = null; model.ClearDrag();
            embeddedModel = null; embeddedArchive = null; archiveClosed = null;
            FilesEmptyState.Visibility = Visibility.Visible;
            FilesLandingHeader.Visibility = FilesLandingActions.Visibility = Visibility.Visible;
            dialog.Close();
        }
    }

    private void ApplyWorkspaceMargin()
    {
        var page = (DataContext as MainViewModel)?.Page;
        MainContent.Margin = page is 0 or 1 or 2 or 3 or 4 or 5 ? new Thickness(0) : new Thickness(ActualWidth < 960 ? 12 : 24);
    }

    private void OnTaskRowAction(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: ArchiveTaskRow row } || DataContext is not MainViewModel vm) return;
        vm.SelectedTask = row;
        if(row.Running){var command=row.IsCompression?vm.CancelCommand:vm.ToggleQueuePauseCommand;if(command.CanExecute(null))command.Execute(null);return;}
        if(!vm.IsIdle)return;
        if (row.Completed)
        {
            if (vm.OpenOutputCommand.CanExecute(null)) vm.OpenOutputCommand.Execute(null);
        }
        else if(row.IsCompression)vm.Page=3;
        else if (vm.PrepareSelectedTaskExtractionCommand.CanExecute(null)) vm.PrepareSelectedTaskExtractionCommand.Execute(null);
    }

    private async Task CloseArchiveAsync()
    {
        var current = embeddedModel;
        if (current is null) return;
        if (current.IsWorking) { current.Cancel(); await current.WaitForIdleAsync(); }
        if (!current.ClearDrag()) return;
        archiveClosed?.TrySetResult();
    }

    internal bool HostPreview(ArchivePreviewDialog dialog, ArchivePreviewViewModel model)
    {
        if (embeddedArchive is null || ActualWidth < 1040) return false;
        ClosePreview(); embeddedPreview = dialog; previewModel = model;
        var content = (FrameworkElement)dialog.Content; dialog.Content = null; content.DataContext = model;
        DisableDialogDefaults(content);
        dialog.EmbeddedClose = ClosePreview; PreviewHost.Content = content;
        ArchivePreviewPane.Visibility = Visibility.Visible; PreviewColumn.Width = new GridLength(Math.Min(360, Math.Max(260, ActualWidth * .28)));
        dialog.ClosePreviewButton.Focus();
        return true;
    }
    private void ClosePreview()
    {
        PreviewHost.Content = null; ArchivePreviewPane.Visibility = Visibility.Collapsed; PreviewColumn.Width = new GridLength(0);
        previewModel?.Clear(); previewModel = null;
        if (embeddedPreview is not null) { embeddedPreview.EmbeddedClose = null; embeddedPreview.Close(); embeddedPreview = null; }
    }
    private static void DisableDialogDefaults(DependencyObject root)
    {
        if(root is Button button) {button.IsCancel=false;button.IsDefault=false;}
        foreach(var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>()) DisableDialogDefaults(child);
    }

    internal bool ConfirmExtractionOptions(int? count=null)
    {
        if (DataContext is not MainViewModel vm) return false;
        vm.PreparingExtraction = true;
        var accepted = false;
        var taskCount=count ?? vm.Tasks.Count(row=>!row.Completed);var batch=taskCount>1;
        var dialog = new Window { Owner = this, Style = (Style)FindResource("AppWindow"), Title = UiText.Get("Redesign.ExtractionOptions"), Width = batch?900:640, Height = batch?620:520, MinWidth = 520, MinHeight = 420, WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false, DataContext = vm };
        var container = new Grid { Margin = new Thickness(24) };
        container.RowDefinitions.Add(new RowDefinition {Height=GridLength.Auto});container.RowDefinitions.Add(new RowDefinition()); container.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var heading=new StackPanel {Margin=new Thickness(0,0,0,24)};
        heading.Children.Add(new TextBlock {Text=UiText.Get(batch?"Extraction.BatchTitle":"Redesign.ExtractionOptions"),FontSize=24,FontWeight=FontWeights.SemiBold});
        var scopeLabel=new TextBlock {Text=batch?UiText.Format("Extraction.BatchCount",taskCount):vm.SelectedTask?.Name ?? "",Margin=new Thickness(0,8,0,0),TextTrimming=TextTrimming.CharacterEllipsis};heading.Children.Add(scopeLabel);container.Children.Add(heading);
        System.Windows.Data.ListCollectionView? batchView=null;
        var body=new Grid();body.ColumnDefinitions.Add(new ColumnDefinition {Width=batch?new GridLength(260):new GridLength(0)});body.ColumnDefinitions.Add(new ColumnDefinition());Grid.SetRow(body,1);container.Children.Add(body);
        if(batch)
        {
            var list=new Grid {Margin=new Thickness(0,0,24,0)};list.RowDefinitions.Add(new RowDefinition {Height=GridLength.Auto});list.RowDefinitions.Add(new RowDefinition());
            var tools=new WrapPanel {Margin=new Thickness(0,0,0,12)};tools.Children.Add(new Button {Content=UiText.Get("T_309cb972a002"),Command=vm.AddFilesCommand,Margin=new Thickness(0,0,8,0)});tools.Children.Add(new Button {Content=UiText.Get("T_a194128b2939"),Command=vm.RemoveFileCommand});list.Children.Add(tools);
            var view=new System.Windows.Data.ListCollectionView(vm.Tasks) {Filter=value=>!((ArchiveTaskRow)value).Completed};
            batchView=view;
            var table=new DataGrid {ItemsSource=view,AutoGenerateColumns=false,IsReadOnly=true,RowHeight=48,HeadersVisibility=DataGridHeadersVisibility.Column,CanUserAddRows=false};
            table.Columns.Add(new DataGridTextColumn {Header=UiText.Get("T_d44e9b3d3b31"),Binding=new System.Windows.Data.Binding("Name"),Width=new DataGridLength(1,DataGridLengthUnitType.Star)});table.SelectionChanged+=(_,_)=>vm.SelectedTask=table.SelectedItem as ArchiveTaskRow;table.SelectedItem=vm.Tasks.FirstOrDefault(row=>!row.Completed);Grid.SetRow(table,1);list.Children.Add(table);body.Children.Add(list);
            table.Columns[0].MinWidth=160;
            ((DataGridTextColumn)table.Columns[0]).ElementStyle=(Style)FindResource("TableText");
            void FitBatchColumn(){if(table.ActualWidth>0)table.Columns[0].Width=Math.Max(160,table.ActualWidth-20);}
            table.Loaded+=(_,_)=>FitBatchColumn();table.SizeChanged+=(_,_)=>FitBatchColumn();
        }
        SettingsPanel.Visibility = Visibility.Visible; SettingsPanel.DataContext = vm; SettingsPanel.Margin = new Thickness(0); SettingsPanel.Padding = new Thickness(0); SettingsScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        Grid.SetColumn(SettingsPanel,1);body.Children.Add(SettingsPanel);
        var actions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0,16,0,0) };
        var start = new Button { Content = UiText.Get("Redesign.ConfirmExtraction"), Style = (Style)FindResource("PrimaryButton"), IsDefault = true, Margin = new Thickness(0,0,8,0) };
        var cancel = new Button { Content = UiText.Get("T_2cd0f3be8738"), IsCancel = true };
        if(batchView is not null)((System.Collections.Specialized.INotifyCollectionChanged)batchView).CollectionChanged+=(_,_)=>{scopeLabel.Text=UiText.Format("Extraction.BatchCount",batchView.Count);start.IsEnabled=batchView.Count>0;};
        start.Click += (_, _) => { accepted = true; dialog.DialogResult = true; };
        actions.Children.Add(start); actions.Children.Add(cancel); Grid.SetRow(actions,2); container.Children.Add(actions);
        var surface=new Border {Child=container};surface.SetResourceReference(Border.BackgroundProperty,"SurfaceBrush");dialog.Content=surface;
        try { PrepareExtractionOptionsForValidation?.Invoke(dialog);dialog.ShowDialog(); return accepted; }
        finally { body.Children.Remove(SettingsPanel);Grid.SetColumn(SettingsPanel,2);SettingsPanel.Visibility = Visibility.Collapsed; SettingsPanel.DataContext = null; vm.PreparingExtraction = false; }
    }
    internal void OnPrepareExtraction(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm && vm.ExtractCommand.CanExecute(null) && ConfirmExtractionOptions()) vm.ExtractCommand.Execute(null);
    }
    private void OnSettings(object sender, RoutedEventArgs e) { if (DataContext is MainViewModel vm) vm.Page = 2; }
    private void OnCompression(object sender, RoutedEventArgs e) { if (DataContext is MainViewModel vm) vm.Page = 3; }
    private void OnCreateFromFiles(object sender, RoutedEventArgs e)
    {
        if(DataContext is not MainViewModel vm || !vm.IsIdle)return;
        vm.Page=3;if(vm.AddCompressionFilesCommand.CanExecute(null))vm.AddCompressionFilesCommand.Execute(null);
    }
    private async void OnOpenArchive(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm && vm.IsIdle) await vm.OpenWorkspaceArchiveAsync(new WindowDialogs(this).SelectArchives());
    }
    private void OnAddBatch(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm || !vm.IsIdle) return;
        vm.AddFiles(new WindowDialogs(this).SelectArchives()); vm.Page = 0;
    }
    private async void OnCloseArchive(object sender, RoutedEventArgs e) => await CloseArchiveAsync();
}
