using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ArchivePassword.Desktop;

// Capture metadata only on the isolated runner; never record field or password values.
internal static class DesktopTypographyValidation
{
    private static readonly List<object> snapshots=[];
    private static readonly List<string> buttonIssues=[];
    private static readonly List<string> roleIssues=[];
    private static readonly (string Key,double Size,int Weight)[] roles=
    [ ("PageTitle",22,600),("SectionTitle",16,600),("Heading",16,600),
      ("Caption",12,400),("Secondary",13,400),("BodyText",14,400) ];

    internal static void Record(Window window,string path)
    {
        if(Environment.GetEnvironmentVariable("GITHUB_ACTIONS")!="true" || !path.Contains("ui-redesign",StringComparison.OrdinalIgnoreCase))return;
        var entries=new List<object>();
        foreach(var element in Descendants(window).OfType<FrameworkElement>().Where(value=>value.IsVisible))
        {
            if(element is not Control && element is not TextBlock)continue;
            var size=element is Control control?control.FontSize:((TextBlock)element).FontSize;
            var weight=element is Control controlWeight?controlWeight.FontWeight:((TextBlock)element).FontWeight;
            var family=element is Control controlFamily?controlFamily.FontFamily:((TextBlock)element).FontFamily;
            string? role=null;
            for(var style=element.Style;style is not null && role is null;style=style.BasedOn)
                foreach(var expected in roles)
                    if(ReferenceEquals(style,window.TryFindResource(expected.Key)))
                    {
                        role=expected.Key;
                        if(Math.Abs(size-expected.Size)>.1 || weight.ToOpenTypeWeight()!=expected.Weight)
                            roleIssues.Add($"{Path.GetFileName(path)}:{element.Name}:{expected.Key}:{size}/{weight}");
                        break;
                    }
            if(element is Button button)
            {
                var icon=button.Content is string glyph && glyph.Length>0 && glyph.All(value=>value is '•' or '…');
                if(!icon && (Math.Abs(size-14)>.1 || weight!=FontWeights.Normal || family.Source!="Segoe UI, Microsoft YaHei"))
                    buttonIssues.Add($"{Path.GetFileName(path)}:{element.Name}:{size}/{weight}/{family.Source}");
            }
            entries.Add(new {kind=element.GetType().Name,name=element.Name,role,fontSize=size,fontWeight=weight.ToOpenTypeWeight(),fontFamily=family.Source,
                width=element.ActualWidth,height=element.ActualHeight});
        }
        snapshots.Add(new {screenshot=Path.GetFileName(path),controls=entries});
    }

    internal static void Save(string folder,Action<string,bool> check)
    {
        if(Environment.GetEnvironmentVariable("GITHUB_ACTIONS")!="true")throw new InvalidOperationException("Isolated CI only.");
        check("all captured named typography roles retain their shared size and weight",snapshots.Count>0 && roleIssues.Count==0);
        check("text buttons retain a shared font family size and regular weight",snapshots.Count>0 && buttonIssues.Count==0);
        File.WriteAllText(Path.Combine(folder,"typography-audit.json"),JsonSerializer.Serialize(new {fieldValuesRecorded=false,snapshots,roleIssues,buttonIssues}));
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for(var index=0;index<VisualTreeHelper.GetChildrenCount(root);index++)
        {
            var child=VisualTreeHelper.GetChild(root,index);yield return child;
            foreach(var descendant in Descendants(child))yield return descendant;
        }
    }
}
