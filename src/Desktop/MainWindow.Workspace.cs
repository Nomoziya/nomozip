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
        InputBindings.Add(new KeyBinding(new RelayCommand(() => embeddedModel?.FolderUpCommand.Execute(null),()=>workspaceModel?.Page==4 && embeddedModel?.FolderUpCommand.CanExecute(null)==true),Key.Left,ModifierKeys.Alt));
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
        ArchiveFooterHost.Visibility=workspaceModel.Page==4 && embeddedModel is not null?Visibility.Visible:Visibility.Collapsed;
        GlobalStatus.Visibility=workspaceModel.Page==4 && embeddedModel is not null?Visibility.Collapsed:Visibility.Visible;
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
        dialog.BrowserRoot.Children.Remove(dialog.BrowserFooter);
        dialog.BrowserFooter.DataContext=model;
        ArchiveFooterHost.Content=dialog.BrowserFooter;
        dialog.BrowserTasksAction.Visibility=Visibility.Visible;
        dialog.BrowserTaskActivity.SetBinding(TextBlock.TextProperty,new System.Windows.Data.Binding(nameof(MainViewModel.FileWorkspaceTaskSummary)) {Source=workspaceModel});
        dialog.BrowserTaskActivity.Visibility=Visibility.Visible;
        ArchiveFooterHost.Visibility=Visibility.Visible;GlobalStatus.Visibility=Visibility.Collapsed;
        ArchiveHost.Content = content; FilesEmptyState.Visibility = Visibility.Collapsed;
        FilesLandingHeader.Visibility = FilesLandingActions.Visibility = Visibility.Collapsed;
        ApplyWorkspaceMargin();
        if (DataContext is MainViewModel vm) vm.Page = 4;
        try { await archiveClosed.Task; }
        finally
        {
            ClosePreview(); ArchiveHost.Content = null; ArchiveHeaderHost.Content = null; ArchiveFooterHost.Content=null;ArchiveFooterHost.Visibility=Visibility.Collapsed;GlobalStatus.Visibility=Visibility.Visible; dialog.EmbeddedClose = null; model.ClearDrag();
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
        if(row.Failure is not null){if(row.IsCompression)vm.ShowCompressionTaskResult(row);else if(vm.ShowFailureCommand.CanExecute(null))vm.ShowFailureCommand.Execute(null);return;}
        if (row.Completed)
        {
            vm.OpenTaskOutput(row);
        }
        else if(row.IsCompression)vm.ShowCompressionTaskResult(row);
        else if (vm.PrepareSelectedTaskExtractionCommand.CanExecute(null)) vm.PrepareSelectedTaskExtractionCommand.Execute(null);
    }

    private void OnTaskMore(object sender,RoutedEventArgs e)
    {
        if(sender is not Button {DataContext:ArchiveTaskRow row} button || DataContext is not MainViewModel vm)return;
        vm.SelectedTask=row;TaskContextMenu.DataContext=vm;TaskContextMenu.PlacementTarget=button;TaskContextMenu.IsOpen=true;
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
        if(model.Picture is not null && embeddedModel?.Rows.FirstOrDefault(row=>row.Path==model.EntryPath) is {} previewRow)previewRow.PreviewThumbnail=model.Picture;
        var content = (FrameworkElement)dialog.Content; dialog.Content = null; content.DataContext = model;
        DisableDialogDefaults(content);
        dialog.EmbeddedClose = ClosePreview; PreviewHost.Content = content;
        ArchivePreviewPane.Visibility = Visibility.Visible; PreviewColumn.Width = new GridLength(ArchivePassword.Core.UiText.LanguageCode=="en"?260:Math.Min(360, Math.Max(260, ActualWidth * .28)));
        dialog.ClosePreviewButton.Focus();
        return true;
    }
    private void ClosePreview()
    {
        if(embeddedModel is not null)foreach(var row in embeddedModel.Rows)row.PreviewThumbnail=null;
        PreviewHost.Content = null; ArchivePreviewPane.Visibility = Visibility.Collapsed; PreviewColumn.Width = new GridLength(0);
        previewModel?.Clear(); previewModel = null;
        if (embeddedPreview is not null) { embeddedPreview.EmbeddedClose = null; embeddedPreview.Close(); embeddedPreview = null; }
    }
    private static void DisableDialogDefaults(DependencyObject root)
    {
        if(root is Button button) {button.IsCancel=false;button.IsDefault=false;}
        foreach(var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>()) DisableDialogDefaults(child);
    }

    internal bool ConfirmExtractionOptions(int? count=null,int? selectedItemCount=null)
    {
        if (DataContext is not MainViewModel vm) return false;
        vm.PreparingExtraction = true;
        var accepted = false;
        var taskCount=count ?? vm.Tasks.Count(row=>!row.Completed);var batch=taskCount>1;
        var titleKey=batch?"Extraction.BatchTitle":selectedItemCount is not null?"Extraction.SelectedTitle":"Redesign.ExtractionOptions";
        var dialog = new Window { Owner = this, Style = (Style)FindResource("AppWindow"), Title = UiText.Get(titleKey), Width = 560, Height = batch?760:720, MinWidth = 500, MinHeight = 500, WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false, DataContext = vm };
        var container = new Grid { Margin = new Thickness(24) };
        container.RowDefinitions.Add(new RowDefinition {Height=GridLength.Auto});container.RowDefinitions.Add(new RowDefinition()); container.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var titleRow=new DockPanel {Margin=new Thickness(0,0,0,16)};
        var icon=new System.Windows.Shapes.Path {Data=System.Windows.Media.Geometry.Parse("M18,2 L6,2 Q3,2 3,5 L3,29 Q3,32 6,32 L25,32 Q28,32 28,29 L28,12 L20,12 L20,2 Z M8,7 L15,7 M14,6 L14,24 M8,18 L14,24 L20,18"),StrokeThickness=1.8,Width=29,Height=34,Stretch=System.Windows.Media.Stretch.Uniform,Margin=new Thickness(0,0,16,0),VerticalAlignment=VerticalAlignment.Top};
        icon.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty,"AccentBrush");DockPanel.SetDock(icon,Dock.Left);titleRow.Children.Add(icon);
        var heading=new StackPanel();titleRow.Children.Add(heading);
        heading.Children.Add(new TextBlock {Text=UiText.Get(titleKey),Style=(Style)FindResource("PageTitle")});
        var scopeLabel=new TextBlock {Text=batch?UiText.Format("Extraction.BatchCount",taskCount):selectedItemCount is {} itemCount?UiText.Format("Extraction.SelectedCount",itemCount,vm.SelectedTask?.Name ?? ""):vm.SelectedTask?.Name ?? "",Margin=new Thickness(0,8,0,0),TextTrimming=TextTrimming.CharacterEllipsis};scopeLabel.SetResourceReference(TextBlock.ForegroundProperty,"SecondaryBrush");heading.Children.Add(scopeLabel);container.Children.Add(titleRow);
        System.Windows.Data.ListCollectionView? batchView=null;
        var body=new Grid();body.RowDefinitions.Add(new RowDefinition {Height=GridLength.Auto});body.RowDefinitions.Add(new RowDefinition());Grid.SetRow(body,1);container.Children.Add(body);
        if(batch)
        {
            body.Children.Add(ExtractionOptionsVisuals.BatchList(vm,out batchView));
        }
        var pathPreview=ExtractionOptionsVisuals.OutputPreview(vm,batch);ExtractionOutputPreview.Content=pathPreview.View;
        var restoreBatchLayout=ConfigureBatchOptions(batch);

        SettingsPanel.Visibility = Visibility.Visible; SettingsPanel.DataContext = vm; SettingsPanel.Margin = new Thickness(0); SettingsPanel.Padding = new Thickness(0); SettingsScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        Grid.SetColumn(SettingsPanel,0);Grid.SetRow(SettingsPanel,1);body.Children.Add(SettingsPanel);
        var actions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0,16,0,0) };
        var start = new Button { Content = UiText.Get("Redesign.ConfirmExtraction")+(batch?" ("+taskCount+")":""), Style = (Style)FindResource("PrimaryButton"), MinWidth=144,MinHeight=44,IsDefault = true, Margin = new Thickness(0,0,12,0) };
        var cancel = new Button { Content = UiText.Get("T_2cd0f3be8738"), MinWidth=100,MinHeight=44,IsCancel = true };
        if(batchView is not null)((System.Collections.Specialized.INotifyCollectionChanged)batchView).CollectionChanged+=(_,_)=>{scopeLabel.Text=UiText.Format("Extraction.BatchCount",batchView.Count);start.Content=UiText.Get("Redesign.ConfirmExtraction")+" ("+batchView.Count+")";start.IsEnabled=batchView.Count>0;};
        start.Click += (_, _) => { accepted = true; dialog.DialogResult = true; };
        actions.Children.Add(start); actions.Children.Add(cancel);var actionBorder=new Border {BorderThickness=new Thickness(0,1,0,0),Padding=new Thickness(0,4,0,0),Child=actions};actionBorder.SetResourceReference(Border.BorderBrushProperty,"DividerBrush");Grid.SetRow(actionBorder,2);container.Children.Add(actionBorder);
        var surface=new Border {Child=container};surface.SetResourceReference(Border.BackgroundProperty,"SurfaceBrush");dialog.Content=surface;
        try { PrepareExtractionOptionsForValidation?.Invoke(dialog);dialog.ShowDialog(); return accepted; }
        finally { restoreBatchLayout();pathPreview.Dispose();ExtractionOutputPreview.Content=null;body.Children.Remove(SettingsPanel);Grid.SetColumn(SettingsPanel,2);Grid.SetRow(SettingsPanel,0);SettingsPanel.Visibility = Visibility.Collapsed; SettingsPanel.DataContext = null; vm.PreparingExtraction = false; }
    }
    private Action ConfigureBatchOptions(bool batch)
    {
        if(!batch)return ()=>{};
        var advanced=(StackPanel)ExtractionAdvancedOptions.Content;
        var header=ExtractionAdvancedOptions.Header;var expanded=ExtractionAdvancedOptions.IsExpanded;var folderLabel=ExtractionFolderChoice.Content;
        var controls=new FrameworkElement[]{ExtractionConflictPanel,ExtractionAutoTryChoice,ExtractionRuleDescription,ExtractionRulesAction,ExtractionVaultSummary};
        var positions=controls.Select(control=>(Control:control,Parent:(Panel)control.Parent,Index:((Panel)control.Parent).Children.IndexOf(control))).ToArray();
        var extraContent=new StackPanel();var extra=new Expander {Header=UiText.Get("Redesign.Advanced"),Content=extraContent,Margin=new Thickness(0,4,0,0)};
        foreach(var item in positions){item.Parent.Children.Remove(item.Control);extraContent.Children.Add(item.Control);}
        advanced.Children.Add(extra);ExtractionAdvancedOptions.Header=UiText.Get("Extraction.RulesAndResume");ExtractionAdvancedOptions.IsExpanded=true;ExtractionFolderChoice.Content=UiText.Get("Extraction.BatchFolder");
        return ()=>
        {
            advanced.Children.Remove(extra);
            foreach(var item in positions.OrderBy(item=>item.Index)) {extraContent.Children.Remove(item.Control);item.Parent.Children.Insert(Math.Min(item.Index,item.Parent.Children.Count),item.Control);}
            ExtractionAdvancedOptions.Header=header;ExtractionAdvancedOptions.IsExpanded=expanded;ExtractionFolderChoice.Content=folderLabel;
        };
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
