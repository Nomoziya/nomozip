using System.Windows;

namespace ArchivePassword.Desktop;

public partial class ArchiveContentsDialog : Window
{
    public ArchiveContentsDialog(ArchiveContentsViewModel model)
    {
        InitializeComponent(); DataContext = model;
        void Arrange() {var compact=ActualHeight<420;ContentsArchiveName.Visibility=ContentsSummary.Visibility=compact?Visibility.Collapsed:Visibility.Visible;}
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
