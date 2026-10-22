using System.Windows;
namespace ArchivePassword.Desktop;
public partial class PasswordMatchDialog:Window
{
    public PasswordMatchDialog(PasswordMatchViewModel model){InitializeComponent();DataContext=model;Closed+=(_,_)=>DataContext=null;}
    private void OnClose(object sender,RoutedEventArgs args)=>Close();
}
