using System.Windows;

namespace ArchivePassword.Desktop;
public partial class ArchivePreviewDialog : Window
{
    internal Action? EmbeddedClose {get;set;}
    public ArchivePreviewDialog(ArchivePreviewViewModel model)
    {
        InitializeComponent();DataContext=model;
        Loaded+=(_,_)=>{if(model.IsText) PreviewText.Focus();else ClosePreviewButton.Focus();};
        Closed+=(_,_)=>{model.Clear();DataContext=null;};
    }
    private void OnClose(object sender,RoutedEventArgs e) {if(EmbeddedClose is not null) EmbeddedClose();else Close();}
}
