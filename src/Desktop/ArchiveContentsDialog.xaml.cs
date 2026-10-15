using System.Windows;

namespace ArchivePassword.Desktop;

public partial class ArchiveContentsDialog : Window
{
    public ArchiveContentsDialog(ArchiveContentsViewModel model)
    {
        InitializeComponent(); DataContext = model;
        void Arrange()
        {
            var compact=ActualHeight<420;ContentsArchiveName.Visibility=ContentsSummary.Visibility=compact?Visibility.Collapsed:Visibility.Visible;
            var narrow=ActualWidth<680;
            PreviewEntryButton.Content=UiText.Get(narrow?"Preview.CompactAction":"Preview.Action");
            ExtractSelectedButton.Content=UiText.Get(narrow?"Contents.CompactExtract":"T_a1fca7bde5d2");
            CancelContentsButton.Content=UiText.Get(narrow?"Contents.CompactCancel":"T_a7084fcb218d");
            OpenContentsResultButton.Content=UiText.Get(narrow?"Contents.CompactOpen":"T_a9a693eafd18");
            ContentsSearchLabel.Content=UiText.Get(narrow?"Contents.CompactSearch":"T_51cf0d31ab02");
            SelectVisibleButton.Content=UiText.Get(narrow?"Contents.CompactSelect":"T_08f316ab8225");
            ClearSelectionButton.Content=UiText.Get(narrow?"Contents.CompactClear":"T_a3c974892464");
            EditContentsButton.Content=UiText.Get(narrow?"Contents.CompactEdit":"T_b1c46471ae91");
            foreach(var button in new[]{SelectVisibleButton,ClearSelectionButton,EditContentsButton}) button.Padding=new Thickness(narrow?12:16,8,narrow?12:16,8);
            EntryTable.Columns[0].Header=UiText.Get(narrow?"Contents.CompactPick":"T_c11330b85234");EntryTable.Columns[0].Width=narrow?52:68;
            EntryTable.Columns[1].MinWidth=narrow?110:150;
            EntryTable.Columns[3].Header=UiText.Get(narrow?"Contents.CompactSize":"T_301a69c74dfc");EntryTable.Columns[3].Width=narrow?70:116;
            EntryTable.Columns[4].Width=narrow?92:96;
            foreach(var button in ContentsActions.Children.OfType<System.Windows.Controls.Button>()) button.Padding=new Thickness(narrow?12:16,8,narrow?12:16,8);
            if(compact) ContentsOutputPath.Visibility=Visibility.Collapsed;else ContentsOutputPath.ClearValue(VisibilityProperty);
        }
        SizeChanged+=(_,_)=>Arrange();Loaded += (_, _) => {Arrange();EntrySearch.Focus();};
        var closing=false;
        Closing+=async (_,e)=>
        {
            if(!closing && model.IsWorking) {e.Cancel=true;model.Cancel();await model.WaitForIdleAsync();closing=true;Close();}
        };
    }
    private void OnClose(object sender, RoutedEventArgs e) => Close();
    private void OnEditContents(object sender,RoutedEventArgs e)
    {EditContentsMenu.PlacementTarget=EditContentsButton;EditContentsMenu.IsOpen=true;}
}
