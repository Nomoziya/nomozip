using System.ComponentModel;
using System.Windows;

namespace ArchivePassword.Desktop;
public partial class FilenameEncodingDialog : Window
{
    private bool closing;
    public FilenameEncodingViewModel Model {get;}
    public FilenameEncodingDialog(FilenameEncodingViewModel model)
    {
        InitializeComponent();Model=model;DataContext=model;
        Loaded+=(_,_)=>{EncodingChoice.Focus();Model.PreviewCommand.Execute(null);};
        Closing+=OnClosing;
    }
    private void OnApply(object sender,RoutedEventArgs args) {if(Model.CanApply) DialogResult=true;}
    private async void OnClosing(object? sender,CancelEventArgs args)
    {
        if(!Model.IsBusy) return;
        args.Cancel=true;if(closing) return;closing=true;Model.Cancel();await Model.WaitForIdleAsync();Close();
    }
}
