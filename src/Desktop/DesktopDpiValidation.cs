using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using ArchivePassword.Core;
using ArchivePassword.Infrastructure;

namespace ArchivePassword.Desktop;
internal static class DesktopDpiValidation
{
    internal static async Task<bool> RunAsync(MainWindow window,string root,string baseDirectory)
    {
        var data=Path.GetFullPath(Path.Combine(root,"artifacts/dpi-ui",Guid.NewGuid().ToString("N")));Directory.CreateDirectory(data);
        var checks=new Dictionary<string,bool>();var failures=new List<object>();var measured=new List<object>();
        void Check(string name,bool value)=>checks.Add(name,value);
        var windows=new List<Window>();
        try
        {
            var hwnd=new WindowInteropHelper(window).Handle;
            Check("actual main HWND is PerMonitorV2",AreDpiAwarenessContextsEqual(GetWindowDpiAwarenessContext(hwnd),-4));
            Check("WPF main visual DPI matches native HWND",Math.Abs(VisualTreeHelper.GetDpi(window).PixelsPerInchX-GetDpiForWindow(hwnd))<1);
            var baselineManifest=ReadManifest(Path.Combine(root,"artifacts/origin-candidate/ArchivePassword.exe"));
            var manifest=ReadManifest(Path.Combine(baseDirectory,"ArchivePassword.exe"));
            Check("built EXE embeds requested PerMonitorV2 manifest",manifest.Contains("PerMonitorV2,PerMonitor") && manifest.Contains("asInvoker"));
            await File.WriteAllTextAsync(Path.Combine(data,"baseline-manifest.xml"),baselineManifest);await File.WriteAllTextAsync(Path.Combine(data,"candidate-manifest.xml"),manifest);
            var work=new WindowPlacement.PixelBounds(-2560,-1440,0,0);
            foreach(var scale in new[] {1.0,1.25,1.5,2.0,2.5})
            {
                var bounds=new WindowPlacement.PixelBounds(-4000,100,-4000+(int)(1120*scale),100+(int)(760*scale));var fit=WindowPlacement.Clamp(bounds,work);
                Check("simulated negative-origin pixel clamp "+scale,fit.Left>=work.Left && fit.Top>=work.Top && fit.Right<=work.Right && fit.Bottom<=work.Bottom && fit.Width==Math.Min(bounds.Width,work.Width));
            }
            var centered=WindowPlacement.Center(new(-100,0,300,300),new(-2000,-800,-1000,0));Check("owner centering uses native pixel origin",centered.Left==-1700 && centered.Top==-550);
            window.Opacity=0;WindowPlacement.AttachForValidation(window);Check("actual main native fit succeeds",WindowPlacement.FitCurrent(window));await Layout(window);
            var model=new MainViewModel(baseDirectory,data,new DpapiPasswordVault(Path.Combine(data,"passwords.apv")),new WindowDialogs(window));window.DataContext=model;await model.InitializeAsync();model.LoadPreview();
            foreach(var page in new[] {0,3,1,2}) {model.Page=page;await Layout(window);Check("main page "+page+" controls remain reachable",Reachable(window,page.ToString(),failures));}
            var saved=new SavedPassword(Guid.NewGuid(),"synthetic","unused","notes",true,DateTimeOffset.UtcNow);
            var factories=new (string Name,Func<Window> Create)[] {
                ("password",()=>new PasswordDialog("test.zip",false,[],true)),
                ("password-editor",()=>new PasswordDialog("",false,[],true,saved,editing:true)),
                ("vault-backup",()=>new VaultBackupDialog(true,"test.nvault")),
                ("vault-import",()=>new VaultImportDialog([], [saved])),
                ("rules",()=>new ExtractionRulesDialog(new(),data,[])),
                ("contents",()=>new ArchiveContentsDialog(new("test.zip",[new(0,"folder/a.txt",12,false,false)]))),
                ("failure",()=>new ExtractionFailureDialog("test.zip",new EngineResult {Code="corrupt_archive",Failures=[new(0,"corrupt_archive","a.txt")]})),
                ("hashes",()=>new FileHashDialog(CreateHashFile(data))),
                ("encoding",()=>new FilenameEncodingDialog(new("test.zip",0,(_,_)=>Task.FromResult(new EngineResult {Status="success",Format="zip",Items=[new(0,"folder/a.txt",12,false,false)]})))),
                ("results",()=>new ResultDirectoriesDialog([data],_=>{}))};
            foreach(var (name,create) in factories)
            {
                var dialog=create();windows.Add(dialog);dialog.Owner=window;dialog.ShowActivated=false;dialog.ShowInTaskbar=false;dialog.Opacity=0;dialog.WindowStartupLocation=WindowStartupLocation.Manual;dialog.Left=-32000;dialog.Top=-32000;dialog.Show();await Layout(dialog);
                if(dialog is FilenameEncodingDialog encoding) {await encoding.Model.WaitForIdleAsync();await Layout(dialog);}
                Check(name+" isolated validation not repositioned automatically",dialog.Left < -10000);
                WindowPlacement.AttachForValidation(dialog);Check(name+" actual fit succeeds",WindowPlacement.FitCurrent(dialog,center:true));await Layout(dialog);
                var childHandle=new WindowInteropHelper(dialog).Handle;Check(name+" HWND is PerMonitorV2",AreDpiAwarenessContextsEqual(GetWindowDpiAwarenessContext(childHandle),-4));
                Check(name+" WPF and native DPI match",Math.Abs(VisualTreeHelper.GetDpi(dialog).PixelsPerInchX-GetDpiForWindow(childHandle))<1);
                GetWindowRect(childHandle,out var rectangle);measured.Add(new {name,dpi=GetDpiForWindow(childHandle),pixels=new[] {rectangle.Left,rectangle.Top,rectangle.Right,rectangle.Bottom}});
                Check(name+" current work-area controls reachable",Reachable(dialog,name+" current",failures));
                Check(name+" actual rectangle fits monitor work area",InsideWorkArea(childHandle));
                // Explicit layout simulation; no DPI messages or OS scale changes are forged.
                WindowPlacement.SetAutoFit(dialog,false);dialog.MinWidth=0;dialog.MinHeight=0;dialog.SizeToContent=SizeToContent.Manual;dialog.Width=512;dialog.Height=360;await Layout(dialog);
                Check(name+" 512x360 simulated work-area controls reachable",Reachable(dialog,name+" simulated",failures));
                if(name is "hashes" or "contents" or "encoding" or "vault-import") {var table=Descendants(dialog).OfType<DataGrid>().First();table.ScrollIntoView(table.Items[0]);await Layout(dialog);var row=table.ItemContainerGenerator.ContainerFromIndex(0) as DataGridRow;row?.BringIntoView();await Layout(dialog);var visible=row is not null && FullyVisible(row,dialog);Check(name+" compact table shows one complete row",visible);if(!visible) failures.Add(new {name,tableHeight=table.ActualHeight,row=row?.RenderSize.ToString()});}
                SetWindowPos(childHandle,0,-32000,-32000,0,0,0x0015);dialog.Opacity=1;await Layout(dialog);
                if(dialog is FileHashDialog hash)
                {
                    hash.CompactHashDetails.IsExpanded=true;await Layout(hash);Check("compact hash details can expand and remain reachable",Reachable(hash,"expanded hash details",failures));
                    hash.HashExpectedBox.Text=new string('a',64);Check("compact expected hash input still edits selected file",hash.Model.Selected?.ExpectedHash==new string('a',64));hash.CompactHashDetails.IsExpanded=false;await Layout(hash);
                }
                var screenshots=Path.Combine(root,"artifacts/dpi-preview");Directory.CreateDirectory(screenshots);DesktopAccessibilityValidation.Render(dialog,Path.Combine(screenshots,name+"-512x360.png"),1);
                dialog.Width=1000;dialog.Height=800;await Layout(dialog);
                if(dialog is FileHashDialog restoredHash) Check("hash details restore after enlarging window",ReferenceEquals(restoredHash.HashDetailsScroll.Content,restoredHash.HashDetailsPanel) && restoredHash.CompactHashDetails.Visibility==Visibility.Collapsed && restoredHash.HashExpectedBox.Text==new string('a',64) && Reachable(dialog,"restored hash",failures));
                if(dialog is ArchiveContentsDialog restoredContents) Check("contents summary restores after enlarging window",restoredContents.ContentsArchiveName.IsVisible && restoredContents.ContentsSummary.IsVisible);
                if(dialog is FilenameEncodingDialog restoredEncoding) Check("encoding help restores after enlarging window",restoredEncoding.EncodingArchiveName.IsVisible && restoredEncoding.EncodingHelp.Text.Length>40);
                dialog.Close();
            }
            // Exercise the production style path with an invisible owned window, not only a direct helper call.
            WindowPlacement.ValidationMode=false;
            var owned=new PasswordDialog("owned.zip",false,[],true) {Owner=window,Opacity=0,ShowActivated=false,ShowInTaskbar=false,WindowStartupLocation=WindowStartupLocation.CenterOwner};windows.Add(owned);owned.Show();await Layout(owned);WindowPlacement.ValidationMode=true;
            var ownedHandle=new WindowInteropHelper(owned).Handle;Check("normal style automatically fits owned window",InsideWorkArea(ownedHandle));
            Check("normal style retains owner and center mode",owned.Owner==window && owned.WindowStartupLocation==WindowStartupLocation.CenterOwner);
            owned.Close();
            Check("closed dialogs release placement hooks",windows.All(child=>!child.IsVisible));
            Check("app window rounding enabled",window.UseLayoutRounding && window.SnapsToDevicePixels);
        }
        catch(Exception error) {Check("exception "+error.GetType().Name+" "+error.Message,false);}
        finally {WindowPlacement.ValidationMode=true;foreach(var child in windows.Where(child=>child.IsVisible)) child.Close();WindowPlacement.SetAutoFit(window,false);window.Left=-32000;window.Top=-32000;}
        var count=GetSystemMetrics(80);
        await File.WriteAllTextAsync(Path.Combine(root,"artifacts/dpi-ui-validation.json"),JsonSerializer.Serialize(new {passed=checks.Count>0 && checks.Values.All(value=>value),total=checks.Count,checks,failures,measured,activeDisplayCount=count,physicalMixedDpiMovementTested=false,systemSettingsChanged=false,scope="Actual HWND PMv2/DPI and native work-area fitting; negative-origin geometry and compact layout simulation. No physical mixed-DPI acceptance."},new JsonSerializerOptions {WriteIndented=true}));return checks.Count>0 && checks.Values.All(value=>value);
    }
    private static bool Reachable(Window window,string name,List<object> failures)
    {
        var passed=true;
        foreach(var control in Descendants(window).OfType<Control>().Where(control=>control.TemplatedParent is null && control.IsVisible && control.Focusable && control is Button or TextBox or PasswordBox or ComboBox or CheckBox))
        {
            control.BringIntoView();window.UpdateLayout();var content=(FrameworkElement)window.Content;var bounds=control.TransformToAncestor(content).TransformBounds(new Rect(control.RenderSize));
            var fits=bounds.Width>0 && bounds.Height>0 && bounds.Left>=-1 && bounds.Top>=-1 && bounds.Right<=content.ActualWidth+1 && bounds.Bottom<=content.ActualHeight+1;
            passed &= fits;if(!fits) failures.Add(new {name,control=control.Name,bounds=bounds.ToString(),size=new[] {window.ActualWidth,window.ActualHeight}});
        }
        return passed;
    }
    private static string CreateHashFile(string data) {var path=Path.Combine(data,"hash.txt");File.WriteAllText(path,"abc");return path;}
    private static bool FullyVisible(FrameworkElement element,Window window)
    {
        if(element.ActualHeight<=0 || element.ActualWidth<=0) return false;
        for(var parent=VisualTreeHelper.GetParent(element);parent is not null && parent!=window;parent=VisualTreeHelper.GetParent(parent))
        {
            if(parent is FrameworkElement region && (region.ClipToBounds || region is ScrollContentPresenter))
            {var bounds=element.TransformToAncestor(region).TransformBounds(new Rect(element.RenderSize));if(bounds.Left < -1 || bounds.Top < -1 || bounds.Right > region.ActualWidth+1 || bounds.Bottom > region.ActualHeight+1) return false;}
        }
        return true;
    }
    private static bool InsideWorkArea(nint hwnd)
    {
        var info=new MonitorInfo {Size=Marshal.SizeOf<MonitorInfo>()};return GetMonitorInfoW(MonitorFromWindow(hwnd,2),ref info) && GetWindowRect(hwnd,out var rect) && rect.Left>=info.Work.Left && rect.Top>=info.Work.Top && rect.Right<=info.Work.Right && rect.Bottom<=info.Work.Bottom;
    }
    private static async Task Layout(Window window)=>await window.Dispatcher.InvokeAsync(window.UpdateLayout,DispatcherPriority.ApplicationIdle);
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent) {for(var index=0;index<VisualTreeHelper.GetChildrenCount(parent);index++) {var child=VisualTreeHelper.GetChild(parent,index);yield return child;foreach(var other in Descendants(child)) yield return other;}}
    private static string ReadManifest(string path)
    {
        if(!File.Exists(path)) return "baseline unavailable on clean CI";
        var module=LoadLibraryExW(path,0,2);if(module==0) throw new IOException("Cannot load manifest resource.");
        try {var resource=FindResourceW(module,1,24);var loaded=LoadResource(module,resource);var bytes=LockResource(loaded);return Marshal.PtrToStringUTF8(bytes,(int)SizeofResource(module,resource))!.TrimEnd('\0');}finally {FreeLibrary(module);}
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect {public int Left,Top,Right,Bottom;}
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo {public int Size;public NativeRect Monitor,Work;public uint Flags;}
    [DllImport("user32.dll")] private static extern nint MonitorFromWindow(nint hwnd,uint flags);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] [return:MarshalAs(UnmanagedType.Bool)] private static extern bool GetMonitorInfoW(nint monitor,ref MonitorInfo info);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint hwnd,nint after,int x,int y,int width,int height,uint flags);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern nint GetWindowDpiAwarenessContext(nint hwnd);
    [DllImport("user32.dll")] [return:MarshalAs(UnmanagedType.Bool)] private static extern bool AreDpiAwarenessContextsEqual(nint one,nint two);
    [DllImport("user32.dll")] [return:MarshalAs(UnmanagedType.Bool)] private static extern bool GetWindowRect(nint hwnd,out NativeRect rect);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] private static extern nint LoadLibraryExW(string path,nint file,uint flags);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] private static extern nint FindResourceW(nint module,nint name,nint type);
    [DllImport("kernel32.dll")] private static extern nint LoadResource(nint module,nint resource);
    [DllImport("kernel32.dll")] private static extern nint LockResource(nint resource);
    [DllImport("kernel32.dll")] private static extern uint SizeofResource(nint module,nint resource);
    [DllImport("kernel32.dll")] private static extern bool FreeLibrary(nint module);
}
