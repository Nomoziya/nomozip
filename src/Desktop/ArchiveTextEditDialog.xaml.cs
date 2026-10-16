using System.IO;
using System.Windows;
using System.Windows.Controls;
using ArchivePassword.Core;
namespace ArchivePassword.Desktop;
public partial class ArchiveTextEditDialog : Window
{
    private ArchiveTextEditSession? session;
    public string EditedText=>Editor.Text;
    public ArchiveTextEditDialog(ArchiveTextEditSession session)
    {
        InitializeComponent();this.session=session;
        FileNameLabel.Text=Path.GetFileName(session.Entry.Path);EncodingLabel.Text=UiText.Format("TextEdit.Encoding",session.EncodingName);
        Editor.Text=session.OriginalText;UpdateState();
        Loaded+=(_,_)=>Editor.Focus();Closed+=(_,_)=>{this.session=null;Editor.Clear();};
    }
    private void UpdateState()
    {
        if(session is null || WriteBackButton is null) return;
        var changed=Editor.Text!=session.OriginalText;var tooLarge=session.Encode(Editor.Text).Length>(long)ArchivePreviewPolicy.TextLimit;
        WriteBackButton.IsEnabled=changed && !tooLarge;
        StateLabel.Text=UiText.Get(tooLarge?"TextEdit.TooLarge":changed?"TextEdit.Modified":"TextEdit.Unchanged");
    }
    private void OnTextChanged(object sender,TextChangedEventArgs e)=>UpdateState();
    // Read the text before Closed clears the control.
    public string? AcceptedText {get;private set;}
    private void OnWriteBack(object sender,RoutedEventArgs e) {if(!WriteBackButton.IsEnabled)return;AcceptedText=Editor.Text;DialogResult=true;}
}
