using System.Windows;
using System.Windows.Controls;
using System.Windows.Shell;
using System.Windows.Input;
using System.Windows.Threading;

namespace ArchivePassword.Desktop;

internal static class DesktopFrameValidation
{
    internal static async Task RunAsync(MainWindow window,Action<string,bool> check)
    {
        if(Environment.GetEnvironmentVariable("GITHUB_ACTIONS")!="true")throw new InvalidOperationException("Isolated CI only.");
        var width=window.Width;var height=window.Height;var left=window.Left;var top=window.Top;
        async Task Layout()=>await window.Dispatcher.InvokeAsync(window.UpdateLayout,DispatcherPriority.ApplicationIdle);
        Button Action(string name)=>(Button)window.Template.FindName(name,window);
        window.ApplyTemplate();await Layout();
        check("integrated caption retains its draggable resize frame and three actions",WindowChrome.GetWindowChrome(window) is {CaptionHeight:>0,ResizeBorderThickness.Left:>0} && Action("CaptionMinimize").IsVisible && Action("CaptionMaximize").IsVisible && Action("CaptionClose").IsVisible);
        try
        {
            ((RoutedCommand)Action("CaptionMaximize").Command).Execute(null,Action("CaptionMaximize").CommandTarget);await Layout();
            check("caption maximize changes the actual window state",window.WindowState==WindowState.Maximized);
            var inset=(Border)window.Template.FindName("WindowInset",window);
            check("maximized client reserves the native resize frame inset",inset.Margin.Left>=SystemParameters.WindowResizeBorderThickness.Left);
            ((RoutedCommand)Action("CaptionMaximize").Command).Execute(null,Action("CaptionMaximize").CommandTarget);await Layout();
            check("caption restore returns to the normal window",window.WindowState==WindowState.Normal);
            ((RoutedCommand)Action("CaptionMinimize").Command).Execute(null,Action("CaptionMinimize").CommandTarget);await Layout();
            check("caption minimize changes the actual window state",window.WindowState==WindowState.Minimized);
        }
        finally {window.WindowState=WindowState.Normal;window.Width=width;window.Height=height;window.Left=left;window.Top=top;await Layout();}
        var dialog=new Window {Style=(Style)window.FindResource("AppWindow"),Owner=window,Width=320,Height=160,WindowStartupLocation=WindowStartupLocation.Manual,Left=-32000,Top=-32000,Opacity=0,ShowActivated=false};
        var closed=false;dialog.Closed+=(_,_)=>closed=true;dialog.Show();await Layout();dialog.ApplyTemplate();
        var close=(Button)dialog.Template.FindName("CaptionClose",dialog);((RoutedCommand)close.Command).Execute(null,close.CommandTarget);await Layout();
        check("integrated dialog close uses the real window close lifecycle",closed && !dialog.IsVisible);
    }
}
