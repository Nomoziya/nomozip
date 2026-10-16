using System.Windows;
using System.Windows.Input;
using ArchivePassword.Infrastructure;

namespace ArchivePassword.Desktop;

public partial class FileHashDialog : Window
{
    internal Action? EmbeddedClose {get;set;}
    public FileHashViewModel Model { get; }
    public FileHashDialog(string? initial = null, Func<FileHashDialog, FileHashViewModel>? create = null, Window? dialogOwner = null)
    {
        InitializeComponent(); Model = create?.Invoke(this) ?? new FileHashViewModel(new FileHashCalculator(), new FileHashDialogs(dialogOwner ?? this)); DataContext = Model;
        void Arrange()
        {
            var compact = HashLayout.ActualWidth < 720 || HashLayout.ActualHeight < 500;
            HashHeading.Margin = new Thickness(compact ? 16 : 24,compact ? 16 : 24,compact ? 16 : 24,16);
            HashSubtitle.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
            if(compact && HashDetailsScroll.Content is not null) {HashDetailsScroll.Content=null;CompactDetailsScroll.Content=HashDetailsPanel;}
            else if(!compact && CompactDetailsScroll.Content is not null) {CompactDetailsScroll.Content=null;HashDetailsScroll.Content=HashDetailsPanel;}
            HashDetailsSurface.Visibility=compact?Visibility.Collapsed:Visibility.Visible;
            CompactHashDetails.Visibility=compact?Visibility.Visible:Visibility.Collapsed;
            HashDetailsColumn.Width=new GridLength(compact?0:Math.Clamp(HashLayout.ActualWidth*.38,300,400));
            if(compact && HashActions.Parent is System.Windows.Controls.StackPanel actionsParent) {actionsParent.Children.Remove(HashActions);HashLayout.Children.Add(HashActions);System.Windows.Controls.Grid.SetRow(HashActions,3);}
            else if(!compact && HashActions.Parent is System.Windows.Controls.Grid) {HashLayout.Children.Remove(HashActions);HashDetailsPanel.Children.Add(HashActions);}
            System.Windows.Controls.Grid.SetColumn(HashActions,0);System.Windows.Controls.Grid.SetColumnSpan(HashActions,2);
            HashActions.Margin=compact?new Thickness(16,8,16,12):new Thickness(0,24,0,0);
        }
        HashLayout.SizeChanged += (_, _) => Arrange();SizeChanged += (_, _) => Arrange();
        if (initial is not null) Model.AddFiles([initial]);
        Loaded += (_, _) => { Arrange(); HashAddButton.Focus(); };
        var closing = false;
        Closing += async (_, args) =>
        {
            if (!closing && Model.IsBusy) { args.Cancel = true; Model.Cancel(); await Model.WaitForIdleAsync(); closing = true; Close(); }
        };
        PreviewKeyDown += (_, args) => { if (args.Key == Key.Escape && Model.IsIdle) { args.Handled = true;OnClose(this,new RoutedEventArgs()); } };
    }
    private void OnClose(object sender, RoutedEventArgs args) {if(EmbeddedClose is not null) EmbeddedClose();else Close();}
}
