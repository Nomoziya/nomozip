using System.Windows;
namespace ArchivePassword.Desktop;
public partial class ResultDirectoriesDialog : Window
{
    private readonly Action<string> open;
    public ResultDirectoriesDialog(IReadOnlyList<string> paths,Action<string> open)
    {
        InitializeComponent();this.open=open;DirectoriesBox.ItemsSource=paths;DirectoriesBox.SelectedIndex=0;
        WindowPlacement.FitWorkArea(this,SystemParameters.WorkArea);Loaded+=(_,_)=>DirectoriesBox.Focus();
    }
    private void OnOpen(object sender,RoutedEventArgs e) { if(DirectoriesBox.SelectedItem is string path) open(path); }
}
