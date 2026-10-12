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
    }

    internal void ArrangePanels(bool isNarrow)
    {
        narrow = isNarrow;
        CompressionLayout.Height = narrow ? double.NaN : Math.Max(280, CompressionViewport.ActualHeight);
        CompressionViewport.VerticalScrollBarVisibility = narrow ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;
        SettingsScroll.VerticalScrollBarVisibility = narrow ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;
        ListRow.Height = narrow ? new GridLength(240) : new GridLength(1, GridUnitType.Star);
        SettingsColumn.Width = narrow ? new GridLength(0) : new GridLength(280);
        SettingsRow.Height = narrow ? GridLength.Auto : new GridLength(0);
        Grid.SetRow(SettingsPanel, narrow ? 1 : 0);
        Grid.SetColumn(SettingsPanel, narrow ? 0 : 2);
        SettingsPanel.Margin = narrow ? new Thickness(0, 16, 0, 0) : new Thickness(0);
    }
}
