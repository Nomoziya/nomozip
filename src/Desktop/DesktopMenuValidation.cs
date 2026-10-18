using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace ArchivePassword.Desktop;

// Exercise the visible product entry instead of the retained, collapsed legacy buttons.
internal static class DesktopMenuValidation
{
    internal static async Task<ContextMenu> OpenAsync(Window window, Button button)
    {
        if(Environment.GetEnvironmentVariable("GITHUB_ACTIONS")!="true")throw new InvalidOperationException("Menu validation requires isolated CI.");
        if(!button.IsVisible)throw new InvalidOperationException("The menu entry is hidden.");
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await window.Dispatcher.InvokeAsync(window.UpdateLayout,DispatcherPriority.ApplicationIdle);
        var menu=button.ContextMenu??throw new InvalidOperationException("Missing product menu.");
        if(!menu.IsOpen)throw new InvalidOperationException("The actual menu did not open.");
        menu.UpdateLayout();return menu;
    }

    internal static bool Reachable(MenuItem item)=>item.IsVisible && item.Focusable && item.ActualWidth>0 && item.ActualHeight>0 && UIElementAutomationPeer.CreatePeerForElement(item)!.GetName().Length>0;

    internal static async Task InvokeAsync(Window window,Button button,ICommand command)
    {
        var menu=await OpenAsync(window,button);
        try
        {
            var item=menu.Items.OfType<MenuItem>().Single(value=>ReferenceEquals(value.Command,command));
            if(!Reachable(item) || !item.IsEnabled)throw new InvalidOperationException("The product menu action is not reachable or enabled.");
            ((IInvokeProvider)UIElementAutomationPeer.CreatePeerForElement(item)!.GetPattern(PatternInterface.Invoke)).Invoke();
            await window.Dispatcher.InvokeAsync(window.UpdateLayout,DispatcherPriority.ApplicationIdle);
        }
        finally {menu.IsOpen=false;}
    }
}
