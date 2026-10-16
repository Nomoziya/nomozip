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
        InputBindings.Add(new KeyBinding(new RelayCommand(async () => await CloseArchiveAsync()), Key.Escape, ModifierKeys.None));
        InputBindings.Add(new KeyBinding(new RelayCommand(() => embeddedModel?.SelectVisibleCommand.Execute(null), () => embeddedModel?.CanSelect == true), Key.A, ModifierKeys.Control));
    }

    private void WorkspaceChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainViewModel.Page) || workspaceModel is null) return;
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
        ArchiveHost.Content = content; FilesEmptyState.Visibility = Visibility.Collapsed; CloseArchiveButton.Visibility = Visibility.Visible;
        if (DataContext is MainViewModel vm) vm.Page = 4;
        try { await archiveClosed.Task; }
        finally
        {
            ClosePreview(); ArchiveHost.Content = null; dialog.EmbeddedClose = null; model.ClearDrag();
            embeddedModel = null; embeddedArchive = null; archiveClosed = null;
            FilesEmptyState.Visibility = Visibility.Visible; CloseArchiveButton.Visibility = Visibility.Collapsed;
            dialog.Close();
        }
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

    internal bool ConfirmExtractionOptions()
    {
        if (DataContext is not MainViewModel vm) return false;
        vm.PreparingExtraction = true;
        var accepted = false;
        var dialog = new Window { Owner = this, Style = (Style)FindResource("AppWindow"), Title = UiText.Get("Redesign.ExtractionOptions"), Width = 520, Height = 620, MinWidth = 420, MinHeight = 420, WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false, DataContext = vm };
        var container = new Grid { Margin = new Thickness(20) };
        container.RowDefinitions.Add(new RowDefinition()); container.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        SettingsPanel.Visibility = Visibility.Visible; SettingsPanel.DataContext = vm; SettingsPanel.Margin = new Thickness(0); SettingsPanel.Padding = new Thickness(0); SettingsScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        container.Children.Add(SettingsPanel);
        var actions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0,16,0,0) };
        var start = new Button { Content = UiText.Get("Redesign.ConfirmExtraction"), Style = (Style)FindResource("PrimaryButton"), IsDefault = true, Margin = new Thickness(0,0,8,0) };
        var cancel = new Button { Content = UiText.Get("T_2cd0f3be8738"), IsCancel = true };
        start.Click += (_, _) => { accepted = true; dialog.DialogResult = true; };
        actions.Children.Add(start); actions.Children.Add(cancel); Grid.SetRow(actions,1); container.Children.Add(actions); dialog.Content = container;
        try { PrepareExtractionOptionsForValidation?.Invoke(dialog);dialog.ShowDialog(); return accepted; }
        finally { container.Children.Remove(SettingsPanel); SettingsPanel.Visibility = Visibility.Collapsed; SettingsPanel.DataContext = null; vm.PreparingExtraction = false; }
    }
    internal void OnPrepareExtraction(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm && vm.ExtractCommand.CanExecute(null) && ConfirmExtractionOptions()) vm.ExtractCommand.Execute(null);
    }
    private void OnSettings(object sender, RoutedEventArgs e) { if (DataContext is MainViewModel vm) vm.Page = 2; }
    private void OnCompression(object sender, RoutedEventArgs e) { if (DataContext is MainViewModel vm) vm.Page = 3; }
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
