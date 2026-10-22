using System.IO;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using ArchivePassword.Core;

namespace ArchivePassword.Desktop;

internal static class DesktopNextUsabilityValidation
{
    internal static async Task RunAsync(MainWindow window,ThemeResources theme,string data,
        Action<string,bool> check,Action<Window,string> capture)
    {
        if(Environment.GetEnvironmentVariable("GITHUB_ACTIONS")!="true")throw new InvalidOperationException("Requires isolated CI.");
        var entries=new[]{"alpha/report.txt","alpha/deep/report.txt","beta/report.txt","alphabet/report.txt","root.txt"}
            .Select((path,index)=>new ArchiveEntry((uint)index,path,(ulong)(index+1),false,false)).ToArray();
        var model=new ArchiveContentsViewModel(Path.Combine(data,"search-scope.zip"),entries);
        var browser=new ArchiveContentsDialog(model){Owner=window,Opacity=0,ShowActivated=false,Width=1152,Height=768};
        Task Layout()=>browser.Dispatcher.InvokeAsync(browser.UpdateLayout,DispatcherPriority.ApplicationIdle).Task;
        bool Fits(FrameworkElement element)
        {
            var rect=element.TransformToAncestor(browser).TransformBounds(new Rect(element.RenderSize));
            return element.IsVisible && rect.Left>=0 && rect.Top>=0 && rect.Right<=browser.ActualWidth+1 && rect.Bottom<=browser.ActualHeight+1;
        }
        try
        {
            browser.Show();await Layout();model.Search="report";
            var selected=model.VisibleRows.Single(row=>row.Path=="beta/report.txt");selected.Selected=true;
            model.Navigate("alpha");model.Search="report";await Layout();
            check("next browser global search includes sibling folders",model.VisibleRows.Count==4);
            browser.SearchScopeBox.SelectedIndex=1;await Layout();
            check("next browser folder scope includes descendants and excludes common prefixes",model.VisibleRows.Count==2 && model.VisibleRows.All(row=>row.Path.StartsWith("alpha/")));
            check("next browser scoped search preserves hidden selection",model.SelectedEntries is [{Path:"beta/report.txt"}]);
            model.ShowSelectedOnly=true;await Layout();
            check("next browser selected-only restores global scope and reveals hidden selection",model.SearchScopeIndex==0 && model.VisibleRows.Count==1 && model.VisibleRows[0]==selected);
            model.ShowSelectedOnly=false;model.Search="report";model.CurrentRow=selected;await Layout();
            browser.EditContentsMenu.PlacementTarget=browser.EditContentsButton;browser.EditContentsMenu.IsOpen=true;await Layout();
            check("next browser locate action is enabled in the real menu",browser.LocateCurrentEntryAction.IsEnabled);
            ((IInvokeProvider)new MenuItemAutomationPeer(browser.LocateCurrentEntryAction).GetPattern(PatternInterface.Invoke)).Invoke();
            await model.WaitForIdleAsync();await Layout();browser.EditContentsMenu.IsOpen=false;
            check("next browser locate returns to directory and selects the same file",model.CurrentFolder=="beta" && model.Search=="" && model.CurrentRow==selected && browser.EntryTable.SelectedItem==selected);
            check("next browser locate realizes the result row",browser.EntryTable.ItemContainerGenerator.ContainerFromItem(selected) is DataGridRow);
            model.Search="report";model.SearchScopeIndex=0;model.GridView=true;await Layout();
            check("next browser grid separates leaf name and containing folder",model.VisibleRows.All(row=>row.FileName=="report.txt" && row.FileStem=="report" && row.Extension==".txt" && row.DirectoryLabel==row.ParentPath));
            check("next browser file-name labels are rendered without path prefixes",Descendants(browser.EntryTiles).OfType<TextBlock>().Any(text=>text.Text=="report")
                && !Descendants(browser.EntryTiles).OfType<TextBlock>().Any(text=>text.Text=="alpha/report.txt"));
            foreach(var mode in new[]{"light","dark","contrast"})foreach(var width in new[]{1152d,512d})
            {
                theme.SetMode(mode=="contrast"?"system":mode);theme.Apply(mode=="contrast");browser.MinWidth=width;browser.Width=width;browser.MinHeight=360;browser.Height=width==512?360:768;await Layout();
                check($"next browser scope controls fit {mode} {width}",Fits(browser.SearchScopeBox) && Fits(browser.SelectedOnlyBox));
                check($"next browser first grid card fits {mode} {width}",browser.EntryTiles.ItemContainerGenerator.ContainerFromIndex(0) is FrameworkElement card && Fits(card));
                capture(browser,$"next-browser-grid-{mode}-{width}");
            }
        }
        finally {browser.Close();theme.SetMode("light");theme.Apply(false);}
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for(var index=0;index<VisualTreeHelper.GetChildrenCount(root);index++)
        {var child=VisualTreeHelper.GetChild(root,index);yield return child;foreach(var nested in Descendants(child))yield return nested;}
    }
}
