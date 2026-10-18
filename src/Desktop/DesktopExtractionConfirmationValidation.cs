using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace ArchivePassword.Desktop;

internal static class DesktopExtractionConfirmationValidation
{
    internal static void Inspect(MainWindow window,Func<Window,Task> inspect)
    {
        if(Environment.GetEnvironmentVariable("GITHUB_ACTIONS")!="true")throw new InvalidOperationException("Confirmation inspection requires isolated CI.");
        var previous=window.PrepareExtractionOptionsForValidation;
        window.PrepareExtractionOptionsForValidation=dialog=>
        {
            dialog.WindowStartupLocation=WindowStartupLocation.Manual;dialog.Left=dialog.Top=-32000;dialog.ShowActivated=false;dialog.Opacity=0;
            dialog.Loaded+=(_,_)=>dialog.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,new Action(async()=>
            {
                foreach(var expander in Descendants((DependencyObject)dialog.Content).OfType<Expander>())expander.IsExpanded=true;
                dialog.UpdateLayout();await inspect(dialog);dialog.Close();
            }));
        };
        try{window.ConfirmExtractionOptions();}finally{window.PrepareExtractionOptionsForValidation=previous;}
    }
    internal static Button StartButton(Window dialog)=>Descendants((DependencyObject)dialog.Content).OfType<Button>().Single(button=>button.IsDefault);
    internal static IDisposable Accept(MainWindow window)
    {
        if(Environment.GetEnvironmentVariable("GITHUB_ACTIONS")!="true") throw new InvalidOperationException("Confirmation automation requires isolated CI.");
        var previous=window.PrepareExtractionOptionsForValidation;
        window.PrepareExtractionOptionsForValidation=dialog=>
        {
            dialog.WindowStartupLocation=WindowStartupLocation.Manual;dialog.Left=dialog.Top=-32000;dialog.ShowActivated=false;dialog.Opacity=0;
            dialog.Loaded+=(_,_)=>dialog.Dispatcher.BeginInvoke(DispatcherPriority.Loaded,new Action(()=>
            {
                dialog.UpdateLayout();var start=Descendants((DependencyObject)dialog.Content).OfType<Button>().Single(button=>button.IsDefault);
                if(!start.IsVisible || !start.IsEnabled)throw new InvalidOperationException("Actual extraction confirmation is unavailable.");
                start.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }));
        };
        return new Restore(()=>window.PrepareExtractionOptionsForValidation=previous);
    }
    private sealed class Restore(Action action):IDisposable {public void Dispose()=>action();}
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        foreach(var child in LogicalTreeHelper.GetChildren(parent).OfType<DependencyObject>())
        {yield return child;foreach(var nested in Descendants(child))yield return nested;}
    }
}
