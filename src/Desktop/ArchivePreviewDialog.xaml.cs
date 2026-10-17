using System.Windows;

namespace ArchivePassword.Desktop;
public partial class ArchivePreviewDialog : Window
{
    internal Action? EmbeddedClose {get;set;}
    public ArchivePreviewDialog(ArchivePreviewViewModel model)
    {
        InitializeComponent();DataContext=model;
        void ArrangeImage()
        {
            if(model.Picture is null || PreviewImage.ActualWidth<=0)return;
            var height=Math.Min(260,PreviewImage.ActualWidth*model.Picture.PixelHeight/model.Picture.PixelWidth);
            PreviewImage.Height=height;
            PreviewImage.Clip=new System.Windows.Media.RectangleGeometry(new Rect(0,0,PreviewImage.ActualWidth,height),4,4);
        }
        PreviewImage.SizeChanged+=(_,_)=>ArrangeImage();
        Loaded+=(_,_)=>ArrangeImage();
        Loaded+=(_,_)=>{if(model.IsText) PreviewText.Focus();else ClosePreviewButton.Focus();};
        Closed+=(_,_)=>{model.Clear();DataContext=null;};
    }
    private void OnClose(object sender,RoutedEventArgs e) {if(EmbeddedClose is not null) EmbeddedClose();else Close();}
}
