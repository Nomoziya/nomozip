using System.IO;
using System.Windows;
using ArchivePassword.Core;
namespace ArchivePassword.Desktop;
public partial class ArchiveEditConfirmDialog : Window
{
    public bool IsWriteBack {get;}
    public ArchiveEditConfirmDialog(ArchiveEntry entry,bool writeBack,string encoding,long? editedSize=null)
    {
        InitializeComponent();IsWriteBack=writeBack;
        if(writeBack)ActionIcon.Data=System.Windows.Media.Geometry.Parse("M14,28 L14,14 M8,20 L14,14 L20,20");
        ChangedState.Visibility=writeBack?Visibility.Visible:Visibility.Collapsed;
        Title=Heading.Text=UiText.Get(writeBack?"TextEdit.WriteBackTitle":"TextEdit.Title");
        FileNameLabel.Text=Path.GetFileName(entry.Path);FileNameLabel.ToolTip=entry.Path;
        Details.Text=writeBack?UiText.Format("TextEdit.ChangedDetails",editedSize,entry.Size,encoding):UiText.Format("TextEdit.Details",entry.Size,encoding);
        Explanation.Text=UiText.Get(writeBack?"TextEdit.WriteBackExplanation":"TextEdit.Explanation");
        ContinueButton.Content=UiText.Get(writeBack?"TextEdit.WriteBack":"TextEdit.EditCopy");
        CancelButton.Content=UiText.Get(writeBack?"TextEdit.Discard":"TextEdit.Cancel");
        Loaded+=(_,_)=>ContinueButton.Focus();
    }
    private void OnContinue(object sender,RoutedEventArgs e)=>DialogResult=true;
}
