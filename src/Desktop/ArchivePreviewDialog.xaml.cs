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
        model.PropertyChanged+=(_,args)=>{if(args.PropertyName==nameof(model.Picture))PreviewImage.Dispatcher.BeginInvoke(ArrangeImage);};
        model.FindMatchRequested+=(index,length)=>PreviewText.Dispatcher.BeginInvoke(()=>
        {if(model.IsText && index+length<=PreviewText.Text.Length){PreviewText.Select(index,length);PreviewText.ScrollToLine(PreviewText.GetLineIndexFromCharacterIndex(index));}},System.Windows.Threading.DispatcherPriority.Loaded);
        PreviewLayout.PreviewKeyDown+=(_,args)=>
        {
            if(args.Key==System.Windows.Input.Key.F && System.Windows.Input.Keyboard.Modifiers==System.Windows.Input.ModifierKeys.Control && model.IsText){OnFind(this,new RoutedEventArgs());args.Handled=true;}
            else if(args.Key==System.Windows.Input.Key.Enter && PreviewFindText.IsKeyboardFocusWithin)
            {if(model.FindNextCommand.CanExecute(null))model.FindNextCommand.Execute(null);args.Handled=true;}
            else if(args.Key==System.Windows.Input.Key.Escape && model.ShowFind){OnCloseFind(this,new RoutedEventArgs());args.Handled=true;}
        };
        Loaded+=(_,_)=>ArrangeImage();
        Loaded+=(_,_)=>{if(model.IsText) PreviewText.Focus();else ClosePreviewButton.Focus();};
        Closed+=(_,_)=>{model.Clear();DataContext=null;};
    }
    internal void ConfigureEmbeddedImageLayout()
    {
        PreviewLayout.Margin = new Thickness(0);
        if (DataContext is not ArchivePreviewViewModel { IsImage: true,HasNavigation:false }) return;
        PreviewLayout.RowDefinitions[0].Height = new GridLength(94);
        PreviewLayout.RowDefinitions[2].Height = new GridLength(0);
        PreviewLayout.RowDefinitions[3].Height = new GridLength(64);
        PreviewLayout.Children.Remove(PreviewStatus);
        PreviewStatus.Margin = new Thickness(0,26,0,20); PreviewImageStack.Children.Add(PreviewStatus);
        PreviewActions.Margin = new Thickness(0); PreviewActions.Height = 44; PreviewActions.VerticalAlignment = VerticalAlignment.Top;
        OpenPreviewButton.Height = ClosePreviewButton.Height = 44;
    }
    private void OnClose(object sender,RoutedEventArgs e) {if(EmbeddedClose is not null) EmbeddedClose();else Close();}
    private void OnFind(object sender,RoutedEventArgs e)
    {
        if(DataContext is not ArchivePreviewViewModel model)return;
        model.FindExpanded=true;PreviewFindText.Dispatcher.BeginInvoke(()=>{PreviewFindText.Focus();PreviewFindText.SelectAll();});
    }
    private void OnCloseFind(object sender,RoutedEventArgs e)
    {if(DataContext is ArchivePreviewViewModel model)model.FindExpanded=false;ShowFindButton.Focus();}
}
