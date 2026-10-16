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
            var layoutHeight = EmbeddedClose is null ? ActualHeight : HashLayout.ActualHeight;
            var compact = layoutHeight < 560;
            HashLayout.Margin = new Thickness(compact ? 12 : 16);
            HashHeading.Margin = new Thickness(0, 0, 0, compact ? 8 : 12);
            HashSubtitle.Visibility = HashSelectedPath.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
            var tiny=layoutHeight<420;
            if(tiny && HashDetailsPanel.Parent==HashLayout) {HashLayout.Children.Remove(HashDetailsPanel);HashDetailsPanel.Margin=new Thickness(0);CompactDetailsScroll.Content=HashDetailsPanel;CompactHashDetails.Visibility=Visibility.Visible;}
            else if(!tiny && CompactDetailsScroll.Content is not null) {CompactDetailsScroll.Content=null;HashDetailsPanel.Margin=new Thickness(0,12,0,0);HashLayout.Children.Add(HashDetailsPanel);CompactHashDetails.Visibility=Visibility.Collapsed;}

        }
        HashLayout.SizeChanged += (_, _) => Arrange();SizeChanged += (_, _) => Arrange();
        if (initial is not null) Model.AddFiles([initial]);
        Loaded += (_, _) => { Arrange(); HashAddButton.Focus(); };
        var closing = false;
        Closing += async (_, args) =>
        {
            if (!closing && Model.IsBusy) { args.Cancel = true; Model.Cancel(); await Model.WaitForIdleAsync(); closing = true; Close(); }
        };
        PreviewKeyDown += (_, args) => { if (args.Key == Key.Escape) { args.Handled = true; Close(); } };
    }
    private void OnClose(object sender, RoutedEventArgs args) {if(EmbeddedClose is not null) EmbeddedClose();else Close();}
}
