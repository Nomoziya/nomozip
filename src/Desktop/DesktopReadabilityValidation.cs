using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ArchivePassword.Desktop;

internal static class DesktopReadabilityValidation
{
    internal static bool CompleteText(TextBlock text)
    {
        if(!text.IsVisible || text.ActualWidth<=0)return false;
        var formatted=new FormattedText(text.Text,CultureInfo.CurrentCulture,text.FlowDirection,
            new Typeface(text.FontFamily,text.FontStyle,text.FontWeight,text.FontStretch),text.FontSize,text.Foreground,VisualTreeHelper.GetDpi(text).PixelsPerDip);
        if(text.TextWrapping==TextWrapping.NoWrap)return formatted.WidthIncludingTrailingWhitespace<=text.ActualWidth+1;
        formatted.MaxTextWidth=Math.Max(1,text.ActualWidth);
        if(!double.IsNaN(text.LineHeight))formatted.LineHeight=text.LineHeight;
        return formatted.Height<=text.ActualHeight+2;
    }
    internal static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for(var index=0;index<VisualTreeHelper.GetChildrenCount(root);index++)
        {var child=VisualTreeHelper.GetChild(root,index);yield return child;foreach(var nested in Descendants(child))yield return nested;}
    }
}
