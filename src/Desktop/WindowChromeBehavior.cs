using System.Windows;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Shell;
using ArchivePassword.Core;

namespace ArchivePassword.Desktop;

internal static class WindowChromeBehavior
{
    public static readonly DependencyProperty EnabledProperty=DependencyProperty.RegisterAttached("Enabled",typeof(bool),typeof(WindowChromeBehavior),new PropertyMetadata(false,OnEnabled));
    public static readonly DependencyProperty ShowSystemButtonsProperty=DependencyProperty.RegisterAttached("ShowSystemButtons",typeof(bool),typeof(WindowChromeBehavior),new PropertyMetadata(false));
    public static bool GetEnabled(DependencyObject value)=>(bool)value.GetValue(EnabledProperty);
    public static void SetEnabled(DependencyObject value,bool enabled)=>value.SetValue(EnabledProperty,enabled);
    public static bool GetShowSystemButtons(DependencyObject value)=>(bool)value.GetValue(ShowSystemButtonsProperty);
    public static void SetShowSystemButtons(DependencyObject value,bool enabled)=>value.SetValue(ShowSystemButtonsProperty,enabled);
    private static void OnEnabled(DependencyObject value,DependencyPropertyChangedEventArgs args)
    {
        if(value is not Window window || args.NewValue is not true)return;
        window.Language=XmlLanguage.GetLanguage(System.Globalization.CultureInfo.CurrentCulture.IetfLanguageTag);
        SetShowSystemButtons(window,window is MainWindow);
        WindowChrome.SetWindowChrome(window,new WindowChrome {CaptionHeight=28,ResizeBorderThickness=SystemParameters.WindowResizeBorderThickness,GlassFrameThickness=new Thickness(0),CornerRadius=new CornerRadius(8),UseAeroCaptionButtons=false});
        window.CommandBindings.Add(new CommandBinding(SystemCommands.MinimizeWindowCommand,(_,_)=>SystemCommands.MinimizeWindow(window)));
        window.CommandBindings.Add(new CommandBinding(SystemCommands.MaximizeWindowCommand,(_,_)=>
        {
            if(window.WindowState==WindowState.Maximized)SystemCommands.RestoreWindow(window);else SystemCommands.MaximizeWindow(window);
        }));
        window.CommandBindings.Add(new CommandBinding(SystemCommands.CloseWindowCommand,(_,_)=>SystemCommands.CloseWindow(window)));
    }
}
