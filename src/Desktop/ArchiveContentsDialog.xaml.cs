using System.Windows;

namespace ArchivePassword.Desktop;

public partial class ArchiveContentsDialog : Window
{
    public ArchiveContentsDialog(ArchiveContentsViewModel model)
    {
        InitializeComponent(); DataContext = model;
        WindowPlacement.FitWorkArea(this, SystemParameters.WorkArea);
        Loaded += (_, _) => EntrySearch.Focus();
    }
    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
