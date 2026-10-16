using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ArchivePassword.Desktop;
public partial class CompressionView : UserControl
{
    private bool narrow;
    private bool updatingSourceSelection;
    public CompressionView()
    {
        InitializeComponent();
        CompressionViewport.SizeChanged += (_, _) => ArrangePanels(narrow);
        CompressionSourceTable.Loaded += (_, _) => FitSourceColumns();
        CompressionSourceTable.SizeChanged += (_, _) => FitSourceColumns();
        CompressionSourceTable.SelectionChanged+=(_,_)=>
        {
            if(DataContext is MainViewModel model)model.SelectCompressionSources(CompressionSourceTable.SelectedItems.OfType<CompressionSourceRow>());
            RefreshSelectionHeader();
        };
        ((System.Collections.Specialized.INotifyCollectionChanged)CompressionSourceTable.Items).CollectionChanged+=(_,_)=>RefreshSelectionHeader();
    }
    private void RefreshSelectionHeader()
    {
        var selected=CompressionSourceTable.SelectedItems.Count;updatingSourceSelection=true;
        try{SourceSelectAll.IsChecked=selected==0?false:selected==CompressionSourceTable.Items.Count?true:null;}
        finally{updatingSourceSelection=false;}
    }

    private void FitSourceColumns()
    {
        if(CompressionSourceTable.ActualWidth<=0)return;
        CompressionSourceTable.Columns[0].Width=40;
        CompressionSourceTable.Columns[2].Width=110;
        CompressionSourceTable.Columns[3].Width=120;
        CompressionSourceTable.Columns[1].Width=Math.Max(130,CompressionSourceTable.ActualWidth-40-110-120-18);
    }

    private void OnSourceSelectAll(object sender,RoutedEventArgs e)
    {if(updatingSourceSelection)return;if(SourceSelectAll.IsChecked==true)CompressionSourceTable.SelectAll();else CompressionSourceTable.UnselectAll();}
    private void OnSourceSelectionMouseDown(object sender,MouseButtonEventArgs e)
    {
        var element=e.OriginalSource as DependencyObject;
        while(element is not null && element is not CheckBox && element is not DataGridCell)element=VisualTreeHelper.GetParent(element);
        if(element is not CheckBox check)return;
        var parent=VisualTreeHelper.GetParent(check);while(parent is not null && parent is not DataGridRow)parent=VisualTreeHelper.GetParent(parent);
        if(parent is DataGridRow row){var selected=!row.IsSelected;check.Focus();row.IsSelected=selected;e.Handled=true;}
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
