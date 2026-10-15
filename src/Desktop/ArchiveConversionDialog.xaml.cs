using System.Windows;
using Microsoft.Win32;

namespace ArchivePassword.Desktop;
public partial class ArchiveConversionDialog : Window
{
    private readonly ArchiveConversionViewModel model;
    public ArchiveConversionDialog(ArchiveConversionViewModel model)
    {
        InitializeComponent();this.model=model;DataContext=model;
        Loaded+=(_,_)=>OutputBox.Focus();
        var closing=false;
        Closing+=async (_,e)=>
        {
            if(!closing && model.IsWorking) {e.Cancel=true;model.Cancel();await model.WaitForIdleAsync();closing=true;Close();}
        };
        Closed+=(_,_)=>{SecretBox.Clear();ConfirmBox.Clear();};
    }
    private void OnBrowse(object sender,RoutedEventArgs e)
    {
        var dialog=new SaveFileDialog {Title=UiText.Get("Conversion.Output"),FileName=model.Output,Filter=model.Format=="zip"?"ZIP|*.zip":"7z|*.7z",DefaultExt="."+model.Format,AddExtension=true,OverwritePrompt=false};
        if(dialog.ShowDialog(this)==true) model.Output=dialog.FileName;
    }
    private async void OnStart(object sender,RoutedEventArgs e)
    {
        try {await model.ConvertAsync(SecretBox.Password,ConfirmBox.Password);}
        finally {SecretBox.Clear();ConfirmBox.Clear();}
    }
    private void OnEncryptionOff(object sender,RoutedEventArgs e) {SecretBox?.Clear();ConfirmBox?.Clear();}
    private void OnCancel(object sender,RoutedEventArgs e)=>model.Cancel();
    private void OnClose(object sender,RoutedEventArgs e)=>Close();
}
