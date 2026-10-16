using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ArchivePassword.Desktop;

public partial class ArchiveContentsDialog : Window
{
    internal Func<Task>? EmbeddedClose {get;set;}
    public ArchiveContentsDialog(ArchiveContentsViewModel model)
    {
        InitializeComponent(); DataContext = model;
        void Arrange()
        {
            var compact=EmbeddedClose is null && ActualHeight<420;ContentsArchiveName.Visibility=ContentsSummary.Visibility=compact?Visibility.Collapsed:Visibility.Visible;
            var narrow=EntryTable.ActualWidth<680;
            PreviewEntryButton.Content=UiText.Get(narrow?"Preview.CompactAction":"Preview.Action");
            ExtractSelectedButton.Content=UiText.Get(narrow?"Contents.CompactExtract":"T_a1fca7bde5d2");
            CancelContentsButton.Content=UiText.Get(narrow?"Contents.CompactCancel":"T_a7084fcb218d");
            OpenContentsResultButton.Content=UiText.Get(narrow?"Contents.CompactOpen":"T_a9a693eafd18");
            ContentsSearchLabel.Content=UiText.Get(narrow?"Contents.CompactSearch":"T_51cf0d31ab02");
            SelectVisibleButton.Content=UiText.Get(narrow?"Contents.CompactSelect":"T_08f316ab8225");
            ClearSelectionButton.Content=UiText.Get(narrow?"Contents.CompactClear":"T_a3c974892464");
            EditContentsButton.Content=UiText.Get(narrow?"Drag.CompactActions":"Drag.Actions");
            foreach(var button in new[]{SelectVisibleButton,ClearSelectionButton,EditContentsButton}) button.Padding=new Thickness(narrow?12:16,8,narrow?12:16,8);
            EntryTable.Columns[0].Header=UiText.Get(narrow?"Contents.CompactPick":"T_c11330b85234");EntryTable.Columns[0].Width=narrow?52:68;
            EntryTable.Columns[1].MinWidth=narrow?110:150;
            EntryTable.Columns[3].Header=UiText.Get(narrow?"Contents.CompactSize":"T_301a69c74dfc");EntryTable.Columns[3].Width=narrow?70:116;
            EntryTable.Columns[4].Width=narrow?92:96;
            foreach(var button in ContentsActions.Children.OfType<System.Windows.Controls.Button>()) button.Padding=new Thickness(narrow?12:16,8,narrow?12:16,8);
            if(compact) ContentsOutputPath.Visibility=Visibility.Collapsed;else ContentsOutputPath.ClearValue(VisibilityProperty);
        }
        EntryTable.SizeChanged+=(_,_)=>Arrange();SizeChanged+=(_,_)=>Arrange();Loaded += (_, _) => {Arrange();EntrySearch.Focus();};
        var closing=false;
        Closing+=async (_,e)=>
        {
            if(!closing && model.IsWorking) {e.Cancel=true;model.Cancel();await model.WaitForIdleAsync();closing=true;Close();}
            else model.ClearDrag();
        };
        Point? dragStart=null;
        EntryTable.PreviewMouseLeftButtonDown+=(_,e)=>
        {
            var node=e.OriginalSource as DependencyObject;
            if(node is FrameworkContentElement content) node=content.Parent;
            while(node is not null && node is not DataGridCell) node=VisualTreeHelper.GetParent(node);
            dragStart=node is DataGridCell cell && cell.Column.DisplayIndex!=0?e.GetPosition(EntryTable):null;
        };
        EntryTable.PreviewMouseLeftButtonUp+=(_,_)=>dragStart=null;
        EntryTable.PreviewMouseMove+=async(_,e)=>
        {
            if(dragStart is not { } start || e.LeftButton!=MouseButtonState.Pressed || !model.CanDrag) return;
            var point=e.GetPosition(EntryTable);
            if(Math.Abs(point.X-start.X)<SystemParameters.MinimumHorizontalDragDistance && Math.Abs(point.Y-start.Y)<SystemParameters.MinimumVerticalDragDistance) return;
            dragStart=null;e.Handled=true;
            await model.DragAsync(paths=>Mouse.LeftButton==MouseButtonState.Pressed?ArchiveFileDrag.Copy(paths):null);
        };
    }
    private async void OnClose(object sender, RoutedEventArgs e) {if(EmbeddedClose is not null) await EmbeddedClose();else Close();}
    private void OnEditContents(object sender,RoutedEventArgs e)
    {EditContentsMenu.PlacementTarget=EditContentsButton;EditContentsMenu.IsOpen=true;}
}
