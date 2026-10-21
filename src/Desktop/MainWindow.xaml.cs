using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

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
        InitializeWorkspace();
        var vaultTimer=new DispatcherTimer {Interval=TimeSpan.FromSeconds(1)};
        vaultTimer.Tick+=async(_,_)=>{if(DataContext is MainViewModel model)await model.CheckVaultIdleAsync();};
        Loaded+=(_,_)=>vaultTimer.Start();
        PreviewKeyDown+=(_,_)=>{if(DataContext is MainViewModel model)model.RecordVaultActivity();};
        PreviewMouseDown+=(_,_)=>{if(DataContext is MainViewModel model)model.RecordVaultActivity();};
        PreviewMouseMove+=(_,_)=>{if(DataContext is MainViewModel model)model.RecordVaultActivity();};
        PreviewMouseWheel+=(_,_)=>{if(DataContext is MainViewModel model)model.RecordVaultActivity();};
        Closed += async (_, _) => {vaultTimer.Stop();completionNotification?.Dispose();if(DataContext is MainViewModel model)await model.ReleaseVaultSessionAsync();};
        void ArrangePanels()
        {
            var narrow = ActualWidth < 1000;
            var compact = ActualWidth < 1050;
            System.Windows.Shell.WindowChrome.SetIsHitTestVisibleInChrome(ReferenceCompactMenu, true);
            System.Windows.Shell.WindowChrome.SetIsHitTestVisibleInChrome(ReferenceThemeAction, true);
            ReferenceSidebar.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
            ReferenceCompactMenu.Visibility = compact ? Visibility.Visible : Visibility.Collapsed;
            if (ArchivePreviewPane.Visibility == Visibility.Visible) PreviewColumn.Width = new GridLength(compact ? 300 : 350);
            BrandMark.Width = BrandMark.Height = 26;
            BrandMark.Margin = new Thickness(0, 0, 10, 0);
            SidebarColumn.Width = new GridLength(compact ? 0 : 186);
            foreach (var text in ((StackPanel)BrandMark.Parent).Children.OfType<TextBlock>()) text.Visibility=compact?Visibility.Collapsed:Visibility.Visible;
            SidebarPrivacyNote.Visibility=compact?Visibility.Collapsed:Visibility.Visible;
            SettingsNavigationText.Visibility=compact?Visibility.Collapsed:Visibility.Visible;
            System.Windows.Automation.AutomationProperties.SetName(SettingsNavigation,UiText.Get("T_df3d58c7d84b"));
            SettingsNavigation.ToolTip=UiText.Get("T_df3d58c7d84b");
            VaultDetailsColumn.Width=new GridLength(360);
            VaultDetails.Visibility=Visibility.Visible;
            SettingsCategoriesColumn.Width=new GridLength(180);
            SettingsCategoryList.Visibility=Visibility.Visible;
            SettingsCategory.Visibility=Visibility.Collapsed;
            Navigation.Tag = compact;
            SidebarContent.Margin = new Thickness(0, 17, 0, 8);
            ApplyWorkspaceMargin();
            TaskOutputColumn.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
            TaskActionsColumn.Width = UiText.LanguageCode=="en"?196:172;
            CompressionPage.SetCompactToolbar(compact);
            if (DataContext is MainViewModel model) model.IsNarrow = narrow;
            CompressionPage.ArrangePanels(narrow);
            TaskDetailsScroll.MaxHeight=Math.Clamp(ActualHeight-500,64,160);
            var shortWorkArea=ActualHeight<480;
            ExtractionLayout.Height = shortWorkArea?double.NaN:Math.Max(0, ExtractionViewport.ActualHeight);
            DefaultOutputPanel.Width = Math.Min(360, Math.Max(160, PreferencesScroll.ActualWidth-200));
            DefaultConflictColumn.Width = new GridLength(DefaultOutputPanel.Width);
            ExtractionViewport.VerticalScrollBarVisibility = shortWorkArea?ScrollBarVisibility.Auto:ScrollBarVisibility.Disabled;
            SettingsScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            ListRow.Height = new GridLength(1, GridUnitType.Star);
            SettingsColumn.Width = new GridLength(0);
            SettingsRow.Height = new GridLength(0);

        }
        SizeChanged += (_, _) => ArrangePanels();
        ExtractionViewport.SizeChanged += (_, _) => ArrangePanels();
        PreferencesScroll.SizeChanged += (_, _) => ArrangePanels();
        Loaded += (_, _) => ArrangePanels();
    }
    private async void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths && DataContext is MainViewModel model)
        {
            if (model.Page == 3) model.AddCompressionSources(paths);
            else if(model.Page==4)
            {
                var archives=paths.Where(path=>System.IO.File.Exists(path) && new[]{".zip",".7z",".rar",".tar",".gz",".xz",".001",".exe"}.Contains(System.IO.Path.GetExtension(path).ToLowerInvariant())).ToArray();
                if(archives.Length==1 && paths.Length==1) await model.OpenWorkspaceArchiveAsync(archives);
                else if(archives.Length==paths.Length) {model.AddFiles(paths);model.Page=0;}
                else {model.Page=3;model.AddCompressionSources(paths);}
            }
            else model.AddFiles(paths);
        }
    }
    private void OnQueueActions(object sender,RoutedEventArgs args)
    {
        if(sender is Button button && button.ContextMenu is {} menu) {menu.PlacementTarget=button;menu.IsOpen=true;}
    }
    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (embeddedModel is not null)
        {
            e.Cancel=true;await CloseArchiveAsync();
            if(embeddedModel is not null) return;
            _ = Dispatcher.BeginInvoke(Close);return;
        }
        if(embeddedHashes?.Model.IsBusy==true) {e.Cancel=true;embeddedHashes.Model.Cancel();await embeddedHashes.Model.WaitForIdleAsync();_ = Dispatcher.BeginInvoke(Close);return;}
        if (!closingApproved && !SuppressExitConfirmationForValidation && DataContext is MainViewModel { IsBusy: true } model)
        {
            e.Cancel = true;
            if (MessageBox.Show(this, UiText.Get("T_6b1b473569fa"), UiText.Get("T_498e1d59b4d7"), MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                model.CancelCommand.Execute(null);
                await model.WaitForIdleAsync();
                closingApproved = true; Close();
            }
        }
    }
}
