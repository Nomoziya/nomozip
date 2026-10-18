using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace ArchivePassword.Desktop;

// Exercise real input and rendered glyphs only on the disposable CI desktop.
internal static class DesktopButtonStateValidation
{
    internal static async Task RunAsync(MainWindow window, MainViewModel model, ThemeResources theme,
        string folder, Action<string, bool> check)
    {
        if (Environment.GetEnvironmentVariable("GITHUB_ACTIONS") != "true") throw new InvalidOperationException("Isolated CI only.");
        var left=window.Left;var top=window.Top;var opacity=window.Opacity;var page=model.Page;var section=model.SettingsSection;var mode=theme.Mode;
        var evidence=new List<object>();
        async Task Layout() { await window.Dispatcher.InvokeAsync(window.UpdateLayout,DispatcherPriority.ApplicationIdle);await Task.Delay(70); }
        async Task Move(Button button)
        {
            var point=button.PointToScreen(new Point(button.ActualWidth/2,button.ActualHeight/2));
            if(!SetCursorPos((int)point.X,(int)point.Y))throw new InvalidOperationException("CI cursor movement failed.");
            await Layout();
        }
        void Capture(Button button,string name)
        {
            var presenter=DesktopRedesignValidation.VisualDescendants(button).OfType<ContentPresenter>().First();
            var area=presenter.TransformToAncestor(button).TransformBounds(new Rect(presenter.RenderSize));
            var bitmap=new RenderTargetBitmap((int)Math.Ceiling(button.ActualWidth),(int)Math.Ceiling(button.ActualHeight),96,96,PixelFormats.Pbgra32);
            bitmap.Render(button);var pixels=new byte[bitmap.PixelWidth*bitmap.PixelHeight*4];bitmap.CopyPixels(pixels,bitmap.PixelWidth*4,0);
            var foreground=((SolidColorBrush)button.Foreground).Color;var ink=0;
            for(var y=Math.Max(0,(int)Math.Ceiling(area.Top));y<Math.Min(bitmap.PixelHeight,(int)Math.Floor(area.Bottom));y++)
                for(var x=Math.Max(0,(int)Math.Ceiling(area.Left));x<Math.Min(bitmap.PixelWidth,(int)Math.Floor(area.Right));x++)
                {
                    var at=(y*bitmap.PixelWidth+x)*4;var alpha=pixels[at+3];if(alpha<60)continue;
                    var distance=Math.Abs(pixels[at]*255.0/alpha-foreground.B)+Math.Abs(pixels[at+1]*255.0/alpha-foreground.G)+Math.Abs(pixels[at+2]*255.0/alpha-foreground.R);
                    if(distance<45)ink++;
                }
            check(name+" rendered foreground glyphs remain visible",ink>=8);
            evidence.Add(new {name,foregroundPixels=ink,button.IsMouseOver,button.IsPressed,button.IsKeyboardFocused,button.IsEnabled});
            DesktopAccessibilityValidation.Render(window,Path.Combine(folder,"button-state-"+name.Replace('/','-')+".png"),1,clientOnly:true);
        }
        async Task Exercise(Button button,string name)
        {
            button.BringIntoView();await Layout();
            check(name+" actual action is visible and enabled",button.IsVisible && button.IsEnabled && button.ActualWidth>20);
            if(!button.IsVisible || !button.IsEnabled)throw new InvalidOperationException("State validation action unavailable: "+name);
            SetCursorPos(0,0);await Layout();Capture(button,name+"/normal");
            await Move(button);check(name+" actual mouse hover reached action",button.IsMouseOver);Capture(button,name+"/hover");
            try
            {
                mouse_event(2,0,0,0,0);await Layout();check(name+" actual mouse press reached action",button.IsPressed);Capture(button,name+"/pressed");
            }
            finally
            {
                // Release outside the control so no import dialog or command is invoked.
                SetCursorPos(0,0);await Layout();mouse_event(4,0,0,0,0);await Layout();
            }
            button.Focus();await Layout();check(name+" keyboard focus is retained",button.IsKeyboardFocused);Capture(button,name+"/focus");
            var binding=BindingOperations.GetBindingBase(button,UIElement.IsEnabledProperty);var local=button.ReadLocalValue(UIElement.IsEnabledProperty);
            try {button.IsEnabled=false;await Layout();Capture(button,name+"/disabled");}
            finally
            {
                if(binding is not null)BindingOperations.SetBinding(button,UIElement.IsEnabledProperty,binding);
                else if(local==DependencyProperty.UnsetValue)button.ClearValue(UIElement.IsEnabledProperty);else button.SetValue(UIElement.IsEnabledProperty,local);
            }
        }
        try
        {
            window.Left=32;window.Top=32;window.Opacity=1;window.Activate();SetForegroundWindow(new WindowInteropHelper(window).Handle);
            foreach(var appearance in new[]{"light","dark","soft","contrast"})
            {
                theme.SetMode(appearance=="contrast"?"system":appearance);theme.Apply(appearance=="contrast");
                model.Page=5;await Layout();
                Button Hash(string name)=>DesktopRedesignValidation.VisualDescendants(window.ToolsHost).OfType<Button>().Single(button=>button.Name==name);
                await Exercise(Hash("HashImportButton"),appearance+"/hash-import");
                await Exercise(Hash("HashAddButton"),appearance+"/hash-add");
                await Exercise(Hash("HashCloseButton"),appearance+"/hash-close");
                model.Page=1;await Layout();await Exercise(window.VaultMoreAction,appearance+"/vault-more");
                model.Page=2;model.SettingsSection=1;await Layout();await Exercise(window.DefaultExtractionRulesAction,appearance+"/directory-rules");
            }
        }
        finally
        {
            mouse_event(4,0,0,0,0);SetCursorPos(0,0);theme.SetMode(mode);theme.Apply(false);model.Page=page;model.SettingsSection=section;
            window.Left=left;window.Top=top;window.Opacity=opacity;
            File.WriteAllText(Path.Combine(folder,"button-state-audit.json"),JsonSerializer.Serialize(new {actualMouseInput=true,fieldValuesRecorded=false,states=evidence}));
        }
    }
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x,int y);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint window);
    [DllImport("user32.dll")] private static extern void mouse_event(uint flags,uint dx,uint dy,uint data,nuint extraInfo);
}
