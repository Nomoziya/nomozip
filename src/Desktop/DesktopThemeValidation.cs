using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ArchivePassword.Infrastructure;
using ArchivePassword.Core;

namespace ArchivePassword.Desktop;
internal static class DesktopThemeValidation
{
    internal static async Task<bool> RunAsync(MainWindow window,ThemeResources theme,string root,string baseDirectory,string profile)
    {
        var checks=new Dictionary<string,bool>();void Check(string name,bool value)=>checks.Add(name,value);
        string Hash(string path)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
        Color ColorOf(string key)=>((SolidColorBrush)Application.Current.Resources[key]).Color;
        bool DarkSurface()=>ColorOf("SurfaceBrush")==Color.FromRgb(31,41,55);
        var language=UiText.LanguageCode;var samples=Path.Combine(root,"artifacts/theme-controls-preview");Directory.CreateDirectory(samples);
        try
        {
            Check("startup reads saved dark mode before window display",theme.Mode=="dark" && AppearancePreferenceStore.Read(profile).Mode=="dark" && (theme.IsHighContrast || theme.IsDark));
            Check("both languages actual encrypted tasks and dialogs in dark and contrast modes",await DesktopLocalizationValidation.RunAsync(window,theme,root,baseDirectory,profile,"theme"));
            var model=(MainViewModel)window.DataContext;model.Page=2;window.Width=800;window.Height=760;await Layout(window);window.AppearanceChoice.BringIntoView();await Layout(window);
            window.SaveAppearanceAction.BringIntoView();await Layout(window);window.Opacity=1;DesktopAccessibilityValidation.Render(window,Path.Combine(samples,language+"-settings-dark.png"),1,clientOnly:true);window.Opacity=0;
            Check("theme selection and save have translated accessible labels",UIElementAutomationPeer.CreatePeerForElement(window.AppearanceChoice)!.GetName()==UiText.Get("Appearance.Label") && new ButtonAutomationPeer(window.SaveAppearanceAction).GetName()==UiText.Get("Appearance.Save"));
            Check("theme controls keyboard focusable and stored mode selected",new ButtonAutomationPeer(window.SaveAppearanceAction).IsKeyboardFocusable() && model.SelectedAppearance.Code=="dark");
            Check("record based dropdown displays its label rather than record type",Descendants(window.AppearanceChoice).OfType<TextBlock>().Any(v=>v.Text==model.SelectedAppearance.Label));
            var preference=Path.Combine(profile,AppearancePreferenceStore.FileName);var settings=Path.Combine(profile,"settings.json");var vault=Path.Combine(profile,"passwords.apv");var languageFile=Path.Combine(profile,LanguagePreferenceStore.FileName);
            var original=new[]{settings,vault,languageFile}.ToDictionary(p=>p,Hash);
            async Task Save(string mode)
            {
                window.AppearanceChoice.SelectedItem=model.AppearanceOptions.Single(v=>v.Code==mode);await Layout(window);
                ((IInvokeProvider)new ButtonAutomationPeer(window.SaveAppearanceAction).GetPattern(PatternInterface.Invoke)).Invoke();
                var until=DateTime.UtcNow.AddSeconds(5);while((!File.Exists(preference) || AppearancePreferenceStore.Read(profile).Mode!=mode) && DateTime.UtcNow<until)await Task.Delay(10);
                await Layout(window);
            }
            await Save("light");Check("actual UI light save changes current resources immediately",theme.Mode=="light" && !theme.IsDark && ColorOf("SurfaceBrush")==Colors.White && model.AppearanceMessage==UiText.Get("Appearance.Saved"));
            await Save("system");theme.Apply(false,true);Check("system choice follows simulated dark preference",theme.Mode=="system" && DarkSurface());
            theme.Apply(false,false);Check("system choice follows simulated light preference",!theme.IsDark && ColorOf("SurfaceBrush")==Colors.White);
            var events=0;void Changed()=>events++;theme.Changed+=Changed;
            SendMessage(new WindowInteropHelper(window).Handle,0x001A,IntPtr.Zero,IntPtr.Zero);await Layout(window);
            theme.Changed-=Changed;Check("own HWND setting change reaches real system preference reader",events>0 && theme.IsDark==(!SystemParameters.HighContrast && ThemeResources.ReadSystemDark()));
            await Save("dark");theme.Apply(false,false);Check("explicit dark choice ignores light system preference",theme.Mode=="dark" && DarkSurface());
            theme.Apply(true,true);Check("system high contrast overrides chosen dark colors",theme.Mode=="dark" && !theme.IsDark && ColorOf("SurfaceBrush")==SystemColors.WindowColor && ColorOf("TextBrush")==SystemColors.WindowTextColor && ColorOf("AccentBrush")==SystemColors.HighlightColor);
            theme.Apply(false,false);Check("leaving contrast mode restores chosen dark theme",theme.Mode=="dark" && DarkSurface());
            foreach(var pair in new[]{("TextBrush","SurfaceBrush",4.5),("SecondaryBrush","SurfaceBrush",4.5),("TextBrush","CanvasBrush",4.5),("OnAccentBrush","AccentBrush",4.5),("SelectionTextBrush","SelectionBrush",4.5),("ErrorBrush","SurfaceBrush",4.5),("BorderBrush","SurfaceBrush",3.0),("FocusBrush","SurfaceBrush",3.0)})
                Check("dark contrast "+pair.Item1+" on "+pair.Item2,Contrast(ColorOf(pair.Item1),ColorOf(pair.Item2))>=pair.Item3);
            var field=new TextBox{Text="Example text",Width=260};var secret=new PasswordBox{Password="synthetic only",Width=260};
            var choices=new ComboBox{ItemsSource=new[]{"First choice","Second choice"},SelectedIndex=0,Width=260};
            var edit=new ComboBox{IsEditable=true,ItemsSource=new[]{"C:\\example","D:\\example"},Text="C:\\new directory",Width=260};
            var check=new CheckBox{Content="Selected option",IsChecked=true};var progress=new ProgressBar{Value=45,Height=8,Width=260};var horizontal=new ScrollBar{Orientation=Orientation.Horizontal,Minimum=0,Maximum=100,Value=40,ViewportSize=30,Width=260};
            var stack=new StackPanel{Margin=new Thickness(20)};stack.SetResourceReference(Panel.BackgroundProperty,"CanvasBrush");foreach(var child in new UIElement[]{field,secret,choices,edit,check,progress,horizontal})stack.Children.Add(child);
            var dialog=new Window{Style=(Style)Application.Current.Resources["AppWindow"],Content=stack,Width=380,Height=430,Owner=window,WindowStartupLocation=WindowStartupLocation.Manual,Left=-32000,Top=-32000,Opacity=0,ShowActivated=false,ShowInTaskbar=false};dialog.Show();await Layout(dialog);
            Check("existing and newly opened windows share dark resources",window.Background is SolidColorBrush windowBrush && windowBrush.Color==ColorOf("CanvasBrush") && dialog.Background is SolidColorBrush dialogBrush && dialogBrush.Color==windowBrush.Color);
            Check("text and password caret remain visible",((SolidColorBrush)field.CaretBrush).Color==ColorOf("TextBrush") && ((SolidColorBrush)secret.CaretBrush).Color==ColorOf("TextBrush"));
            var horizontalThumb=Descendants(horizontal).OfType<Thumb>().Single();Check("horizontal thumb has themed accessible track",UIElementAutomationPeer.CreatePeerForElement(horizontalThumb)!.GetName()==UiText.Get("Appearance.HorizontalScroll") && horizontalThumb.ActualWidth>=28);
            var expand=(IExpandCollapseProvider)UIElementAutomationPeer.CreatePeerForElement(choices)!.GetPattern(PatternInterface.ExpandCollapse);expand.Expand();await Layout(dialog);var popup=(Popup)choices.Template.FindName("PART_Popup",choices);var popupBorder=(Border)popup.Child;
            Check("dropdown real popup opens with dark surface and contrast border",popup.IsOpen && ((SolidColorBrush)popupBorder.Background).Color==ColorOf("SurfaceBrush") && ((SolidColorBrush)popupBorder.BorderBrush).Color==ColorOf("BorderBrush"));
            var bitmap=new RenderTargetBitmap((int)Math.Ceiling(popupBorder.ActualWidth),(int)Math.Ceiling(popupBorder.ActualHeight),96,96,PixelFormats.Pbgra32);bitmap.Render(popupBorder);
            var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var file=File.Create(Path.Combine(samples,language+"-dark-dropdown.png")))encoder.Save(file);
            var owner=(ComboBoxAutomationPeer)UIElementAutomationPeer.CreatePeerForElement(choices)!;var selection=(ISelectionProvider)owner.GetPattern(PatternInterface.Selection);var item=new ListBoxItemAutomationPeer(choices.Items[1],owner);((ISelectionItemProvider)item.GetPattern(PatternInterface.SelectionItem)!).Select();
            Check("dropdown retains actual UIA selection behavior",choices.SelectedIndex==1 && selection.GetSelection().Length==1);expand.Collapse();await Layout(dialog);Check("dropdown native automation closes popup",!popup.IsOpen && expand.ExpandCollapseState==ExpandCollapseState.Collapsed);
            edit.ApplyTemplate();var editable=(TextBox)edit.Template.FindName("PART_EditableTextBox",edit);editable.Text="D:\\typed directory";await Layout(dialog);
            Check("editable directory dropdown retains typed path",edit.Text=="D:\\typed directory" && ((SolidColorBrush)editable.Foreground).Color==ColorOf("TextBrush"));
            ((IToggleProvider)new CheckBoxAutomationPeer(check).GetPattern(PatternInterface.Toggle)).Toggle();await Layout(dialog);Check("checkbox native automation semantics retained",check.IsChecked==false && ((SolidColorBrush)((Border)check.Template.FindName("Box",check)).Background).Color==ColorOf("SurfaceBrush"));
            dialog.Opacity=1;DesktopAccessibilityValidation.Render(dialog,Path.Combine(samples,language+"-dark-controls.png"),1,clientOnly:true);dialog.Opacity=0;
            choices.IsEnabled=false;field.IsEnabled=false;check.IsEnabled=false;await Layout(dialog);dialog.Opacity=1;DesktopAccessibilityValidation.Render(dialog,Path.Combine(samples,language+"-dark-disabled.png"),1,clientOnly:true);dialog.Opacity=0;
            await Save("light");Check("already open dialog updates when user saves light mode",((SolidColorBrush)dialog.Background).Color==ColorOf("CanvasBrush") && ((SolidColorBrush)secret.Background).Color==Colors.White);dialog.Close();await Save("dark");
            var savedPreference=await File.ReadAllBytesAsync(preference);try
            {
                await File.WriteAllTextAsync(preference,"{\"Version\":99,\"Mode\":\"future\"}");var hash=Hash(preference);
                ((IInvokeProvider)new ButtonAutomationPeer(window.SaveAppearanceAction).GetPattern(PatternInterface.Invoke)).Invoke();await Layout(window);
                var until=DateTime.UtcNow.AddSeconds(5);while(model.AppearanceMessage!=UiText.Get("Appearance.Failed") && DateTime.UtcNow<until)await Task.Delay(10);
                Check("unknown preference UI save shows localized error and keeps current theme",model.AppearanceMessage==UiText.Get("Appearance.Failed") && theme.Mode=="dark" && DarkSurface());
                Check("unknown preference UI save preserves original file",Hash(preference)==hash);
            }
            finally {await File.WriteAllBytesAsync(preference,savedPreference);}
            Check("theme saves preserve settings language and encrypted vault bytes",original.All(pair=>Hash(pair.Key)==pair.Value));
            Check("final saved dark choice ready for independent process restart",AppearancePreferenceStore.Read(profile).Mode=="dark");
        }
        catch(Exception error){checks["exception "+error]=false;}
        var result=new{passed=checks.Count>0 && checks.Values.All(v=>v),total=checks.Count,checks,language,syntheticData=true,changedActualWindowsTheme=false,actualVisibleNarratorTested=false};
        await File.WriteAllTextAsync(Path.Combine(root,"artifacts/theme-"+language+"-settings-validation.json"),JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true}));return result.passed;
    }
    private static double Contrast(Color first,Color second)
    {
        double L(Color c){double F(byte b){var v=b/255.0;return v<=0.04045?v/12.92:Math.Pow((v+0.055)/1.055,2.4);}return .2126*F(c.R)+.7152*F(c.G)+.0722*F(c.B);}
        var a=L(first);var b=L(second);return (Math.Max(a,b)+.05)/(Math.Min(a,b)+.05);
    }
    private static async Task Layout(Window window)=>await window.Dispatcher.InvokeAsync(window.UpdateLayout,DispatcherPriority.ApplicationIdle);
    private static IEnumerable<DependencyObject> Descendants(DependencyObject target)
    {yield return target;for(var index=0;index<VisualTreeHelper.GetChildrenCount(target);index++)foreach(var child in Descendants(VisualTreeHelper.GetChild(target,index)))yield return child;}
    [DllImport("user32.dll",EntryPoint="SendMessageW")]private static extern IntPtr SendMessage(IntPtr hwnd,int message,IntPtr wParam,IntPtr lParam);
}
