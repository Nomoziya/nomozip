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
        void Arrange() {var compact=ActualHeight<420;EncodingArchiveName.Visibility=compact?Visibility.Collapsed:Visibility.Visible;EncodingHelp.Text=compact?UiText.Get("T_6ddbd4ca96f6"):UiText.Get("T_65c10d80f1f3");}
        SizeChanged+=(_,_)=>Arrange();
        Loaded+=(_,_)=>{Arrange();EncodingChoice.Focus();Model.PreviewCommand.Execute(null);};
        Closing+=OnClosing;
    }
    private void OnApply(object sender,RoutedEventArgs args) {if(Model.CanApply) DialogResult=true;}
    private async void OnClosing(object? sender,CancelEventArgs args)
    {
        if(!Model.IsBusy) return;
        args.Cancel=true;if(closing) return;closing=true;Model.Cancel();await Model.WaitForIdleAsync();Close();
    }
}
