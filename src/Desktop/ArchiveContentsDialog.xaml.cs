using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ArchivePassword.Desktop;

public partial class ArchiveContentsDialog : Window
{
    internal Func<Task>? EmbeddedClose {get;set;}
    public ArchiveContentsDialog(ArchiveContentsViewModel model)
    {
        InitializeComponent(); DataContext = model;
        foreach(var item in EditContentsMenu.Items.OfType<MenuItem>())
            if(item.Icon is System.Windows.Shapes.Path icon)
                icon.SetBinding(System.Windows.Shapes.Shape.StrokeProperty,new System.Windows.Data.Binding(nameof(Control.Foreground)) {Source=item});
        void Arrange()
        {
            var narrow=Window.GetWindow(EntryTable)?.ActualWidth<960;
            if(EntryTable.ActualWidth>0)
            {
                var detailWidth=ArchivePassword.Core.UiText.LanguageCode=="en"?128d:88d;
                var showEncryption=!narrow && EntryTable.ActualWidth>=680;
                EntryTable.Columns[4].Visibility=narrow?Visibility.Collapsed:Visibility.Visible;
                EntryTable.Columns[5].Visibility=showEncryption?Visibility.Visible:Visibility.Collapsed;
                foreach(var (index,width) in new[]{(0,40d),(2,90d),(3,detailWidth),(4,144d),(5,detailWidth)})
                {EntryTable.Columns[index].MinWidth=width;EntryTable.Columns[index].Width=width;}
                EntryTable.Columns[1].MinWidth=130;
                EntryTable.Columns[1].Width=Math.Max(130,EntryTable.ActualWidth-40-90-detailWidth-18-(narrow?0:144)-(showEncryption?detailWidth:0));
            }
            EntrySearch.Width=double.NaN;
            ContentsArchiveName.MaxWidth=Math.Max(160,BrowserHeader.ActualWidth-370);
        }
        EntryTable.SizeChanged+=(_,_)=>Arrange();EntryTable.Loaded+=(_,_)=>Arrange();SizeChanged+=(_,_)=>Arrange();Loaded += (_, _) => {Arrange();EntrySearch.Focus();};
        model.PropertyChanged+=(_,args)=>{if(args.PropertyName==nameof(model.GridView)) {ListViewAction.IsChecked=!model.GridView;GridViewAction.IsChecked=model.GridView;}};
        Point? tileDragStart=null;
        EntryTiles.PreviewMouseLeftButtonDown+=(_,args)=>{tileDragStart=FindTile(args.OriginalSource as DependencyObject) is null?null:args.GetPosition(EntryTiles);};
        EntryTiles.PreviewMouseLeftButtonUp+=(_,_)=>tileDragStart=null;
        EntryTiles.PreviewMouseMove+=async(_,args)=>
        {
            if(tileDragStart is not {} start || args.LeftButton!=MouseButtonState.Pressed || !model.CanDrag)return;
            var point=args.GetPosition(EntryTiles);if(Math.Abs(point.X-start.X)<SystemParameters.MinimumHorizontalDragDistance && Math.Abs(point.Y-start.Y)<SystemParameters.MinimumVerticalDragDistance)return;
            tileDragStart=null;args.Handled=true;await model.DragAsync(paths=>Mouse.LeftButton==MouseButtonState.Pressed?ArchiveFileDrag.Copy(paths):null);
        };
        var closing=false;
        Closing+=async (_,e)=>
        {
            if(!closing && model.IsWorking) {e.Cancel=true;model.Cancel();await model.WaitForIdleAsync();closing=true;Close();}
            else model.ClearDrag();
        };
        Point? dragStart=null;
        EntryTable.PreviewMouseLeftButtonDown+=(_,e)=>
        {
            var node=e.OriginalSource as DependencyObject;
            if(node is FrameworkContentElement content) node=content.Parent;
            while(node is not null && node is not DataGridCell) node=VisualTreeHelper.GetParent(node);
            dragStart=node is DataGridCell cell && cell.Column.DisplayIndex!=0?e.GetPosition(EntryTable):null;
        };
        EntryTable.PreviewMouseLeftButtonUp+=(_,_)=>dragStart=null;
        EntryTable.PreviewMouseMove+=async(_,e)=>
        {
            if(dragStart is not { } start || e.LeftButton!=MouseButtonState.Pressed || !model.CanDrag) return;
            var point=e.GetPosition(EntryTable);
            if(Math.Abs(point.X-start.X)<SystemParameters.MinimumHorizontalDragDistance && Math.Abs(point.Y-start.Y)<SystemParameters.MinimumVerticalDragDistance) return;
            dragStart=null;e.Handled=true;
            await model.DragAsync(paths=>Mouse.LeftButton==MouseButtonState.Pressed?ArchiveFileDrag.Copy(paths):null);
        };
    }
    private static ListBoxItem? FindTile(DependencyObject? node)
    {
        if(node is FrameworkContentElement text)node=text.Parent;
        while(node is not null && node is not ListBoxItem){if(node is CheckBox)return null;node=VisualTreeHelper.GetParent(node);}
        return node as ListBoxItem;
    }
    private void OnViewMode(object sender,RoutedEventArgs args)
    {
        if(DataContext is not ArchiveContentsViewModel model)return;
        model.GridView=((FrameworkElement)sender).Tag as string=="grid";
        ListViewAction.IsChecked=!model.GridView;GridViewAction.IsChecked=model.GridView;
        if(model.GridView)EntryTiles.Focus();else EntryTable.Focus();
    }
    private async void OnClose(object sender, RoutedEventArgs e) {if(EmbeddedClose is not null) await EmbeddedClose();else Close();}
    private void OnShowTasks(object sender,RoutedEventArgs e)
    {
        if(Window.GetWindow(BrowserFooter)?.DataContext is MainViewModel model) model.Page=0;
    }
    private void OnEditContents(object sender,RoutedEventArgs e)
    {EditContentsMenu.PlacementTarget=EditContentsButton;EditContentsMenu.IsOpen=true;}
    private void OnOpenEntry(object sender,MouseButtonEventArgs e)
    {
        var node=e.OriginalSource as DependencyObject;
        if(node is FrameworkContentElement content)node=content.Parent;
        while(node is not null && node is not DataGridRow && node is not ListBoxItem)
        {if(node is CheckBox)return;node=VisualTreeHelper.GetParent(node);}
        if(node is DataGridRow or ListBoxItem && DataContext is ArchiveContentsViewModel model && model.OpenCurrentEntryCommand.CanExecute(null))
        {model.OpenCurrentEntryCommand.Execute(null);e.Handled=true;}
    }
}
