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
        void Arrange() {var compact=ActualHeight<420;EncodingArchiveName.Visibility=compact?Visibility.Collapsed:Visibility.Visible;EncodingHelp.Text=compact?"UTF-8 名称优先；不改变密码编码。":"仅用于旧 ZIP 名称乱码；UTF-8 标记和有效 Unicode 名称优先。密码编码保持原样。";}
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
