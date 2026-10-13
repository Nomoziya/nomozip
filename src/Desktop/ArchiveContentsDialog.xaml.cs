using System.Windows;

namespace ArchivePassword.Desktop;

public partial class ArchiveContentsDialog : Window
{
    public ArchiveContentsDialog(ArchiveContentsViewModel model)
    {
        InitializeComponent(); DataContext = model;
        WindowPlacement.FitWorkArea(this, SystemParameters.WorkArea);
        Loaded += (_, _) => EntrySearch.Focus();
        var closing=false;
        Closing+=async (_,e)=>
        {
            if(!closing && model.IsWorking) {e.Cancel=true;model.Cancel();await model.WaitForIdleAsync();closing=true;Close();}
        };
    }
    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
