using System.Windows;

namespace ArchivePassword.Desktop;

public partial class CompressionContentsDialog : Window
{
    public CompressionContentsViewModel Model {get;}
    public CompressionContentsDialog(CompressionContentsViewModel model)
    {
        InitializeComponent();DataContext=Model=model;
        Loaded+=async(_,_)=>await Model.InitializeAsync();
        Closed+=(_,_)=>Model.Dispose();
    }
    private void OnClose(object sender,RoutedEventArgs e)=>Close();
}
