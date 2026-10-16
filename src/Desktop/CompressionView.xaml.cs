using System.Windows;
using System.Windows.Controls;

namespace ArchivePassword.Desktop;
public partial class CompressionView : UserControl
{
    private bool narrow;
    public CompressionView()
    {
        InitializeComponent();
        CompressionViewport.SizeChanged += (_, _) => ArrangePanels(narrow);
        CompressionSourceTable.Loaded += (_, _) => FitSourceColumns();
        CompressionSourceTable.SizeChanged += (_, _) => FitSourceColumns();
    }

    private void FitSourceColumns()
    {
        if(CompressionSourceTable.ActualWidth<=0)return;
        CompressionSourceTable.Columns[1].Width=110;
        CompressionSourceTable.Columns[2].Width=120;
        CompressionSourceTable.Columns[0].Width=Math.Max(130,CompressionSourceTable.ActualWidth-110-120-18);
    }

    internal void ArrangePanels(bool isNarrow)
    {
        narrow = isNarrow;
        CompressionLayout.Height = narrow ? double.NaN : Math.Max(280, CompressionViewport.ActualHeight);
        CompressionViewport.VerticalScrollBarVisibility = narrow ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;
        SettingsScroll.VerticalScrollBarVisibility = narrow ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;
        ListRow.Height = narrow ? new GridLength(240) : new GridLength(1, GridUnitType.Star);
        SettingsColumn.Width = narrow ? new GridLength(0) : new GridLength(Math.Clamp(ActualWidth * .46,340,480));
        SettingsRow.Height = narrow ? GridLength.Auto : new GridLength(0);
        FooterSettingsColumn.Width=SettingsColumn.Width;
        Grid.SetColumn(CompressionActionsPanel,narrow?0:1);
        CompressionActionsPanel.BorderThickness=narrow?new Thickness(0,1,0,0):new Thickness(1,1,0,0);
        Grid.SetRow(SettingsPanel, narrow ? 1 : 0);
        Grid.SetColumn(SettingsPanel, narrow ? 0 : 2);
        SettingsPanel.Margin = narrow ? new Thickness(0, 16, 0, 0) : new Thickness(0);
    }
    internal void SetCompactToolbar(bool compact) => DropHint.Visibility = Visibility.Collapsed;
    private void OnBackFiles(object sender,RoutedEventArgs e) {if(DataContext is MainViewModel model)model.Page=4;}
}
