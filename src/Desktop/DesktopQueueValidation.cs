using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ArchivePassword.Core;
using ArchivePassword.Infrastructure;

namespace ArchivePassword.Desktop;
internal static class DesktopQueueValidation
{
    internal static async Task<bool> RunAsync(MainWindow window,ThemeResources theme,string root,string baseDirectory)
    {
        using var confirmation=DesktopExtractionConfirmationValidation.Accept(window);
        var data=Path.GetFullPath(Path.Combine(root,"artifacts/queue-ui",Guid.NewGuid().ToString("N")));Directory.CreateDirectory(data);
        var checks=new Dictionary<string,bool>();void Check(string name,bool value)=>checks.Add(name,value);
        var preview=Path.Combine(root,"artifacts/queue-preview");Directory.CreateDirectory(preview);
        window.QueueActionsMenu.Opacity=0; // Popup placement may clamp an off-screen parent back to the screen.
        try
        {
            foreach(var version in new[] {"26.03","26.04"})
            {
                var folder=Path.Combine(data,version);Directory.CreateDirectory(folder);
                var vault=new DpapiPasswordVault(Path.Combine(folder,"passwords.apv"));await vault.SaveAsync(new(Guid.NewGuid(),"synthetic","测试密碼 ' $ & 空格","",true,DateTimeOffset.UtcNow));
                var model=new MainViewModel(baseDirectory,folder,vault,new WindowDialogs(window));window.DataContext=model;await model.InitializeAsync();
                model.SelectedEngineVersion=version;model.ApplyEngineCommand.Execute(null);await model.WaitForIdleAsync();await Layout(window);
                var first=Path.Combine(folder,"first.7z");var second=Path.Combine(folder,"second.zip");var bad=Path.Combine(folder,"repair.7z");
                File.Copy(Path.Combine(root,"tests/fixtures/generated/encrypted-header.7z"),first);File.Copy(Path.Combine(root,"tests/fixtures/generated/plain.zip"),second);File.WriteAllText(bad,"not an archive");
                model.AddFiles([first,second,bad]);var rows=model.Tasks.ToArray();model.OutputPath=Path.Combine(folder,"fallback");model.ConflictPolicy=1;
                for(var index=0;index<rows.Length;index++) rows[index].ShellOutputPath=Path.Combine(folder,"output-"+index);
                rows[1].FilenameCodePage=936;var status=rows[1].Status;var output=rows[1].ShellOutputPath;
                File.Delete(bad); // A moved/missing queued archive must fail without prompting or output.
                model.SelectedTask=rows[0];await OpenMenu(window);Check(version+" upper boundary disabled",!window.QueueMoveUpAction.IsEnabled);window.QueueActionsMenu.IsOpen=false;
                model.SelectedTask=rows[1];await OpenMenu(window);Invoke(window.QueueMoveUpAction);await Layout(window);
                Check(version+" actual menu moves selected task up",model.Tasks[0]==rows[1] && model.SelectedTask==rows[1]);
                Check(version+" reordering preserves per-task encoding output and state",rows[1].FilenameCodePage==936 && rows[1].ShellOutputPath==output && rows[1].Status==status && !rows[1].Completed);
                await OpenMenu(window);Invoke(window.QueueMoveDownAction);await Layout(window);Check(version+" actual menu moves task down",model.Tasks.SequenceEqual(rows));
                model.SelectedTask=rows[2];await OpenMenu(window);Check(version+" lower boundary disabled",!window.QueueMoveDownAction.IsEnabled);Invoke(window.QueueExtractSelectedAction);await Layout(window);await model.WaitForIdleAsync();await Layout(window);
                Check(version+" selected failure retains other pending tasks",!rows[2].Completed && rows[2].Failure is not null && rows.Take(2).All(row=>row.Status=="准备就绪" && !row.Completed));
                Check(version+" failed selection has no output",!Directory.Exists(rows[2].ShellOutputPath));
                File.Copy(first,bad,overwrite:true);await OpenMenu(window);Invoke(window.QueueExtractSelectedAction);await Layout(window);await model.WaitForIdleAsync();await Layout(window);
                Check(version+" failed task can retry independently",rows[2].Completed && rows[2].Failure is null && File.ReadAllBytes(Path.Combine(rows[2].ResultOutputPath!,"中文示例.txt")).SequenceEqual(File.ReadAllBytes(Path.Combine(root,"tests/fixtures/generated/中文示例.txt"))));
                Check(version+" retry does not extract other pending tasks",rows.Take(2).All(row=>!row.Completed && !Directory.Exists(row.ShellOutputPath)));
                await OpenMenu(window);Check(version+" completed selected task cannot be re-extracted implicitly",!window.QueueExtractSelectedAction.IsEnabled);Invoke(window.QueueRemoveCompletedAction);await Layout(window);
                Check(version+" removes only completed history and keeps output",model.Tasks.SequenceEqual(rows.Take(2)) && Directory.Exists(rows[2].ResultOutputPath) && rows[2].Completed);
                model.SelectedTask=rows[0];await OpenMenu(window);Check(version+" removal disabled when no completed tasks",!window.QueueRemoveCompletedAction.IsEnabled);window.QueueActionsMenu.IsOpen=false;
                model.ExtractSelectedTaskCommand.Execute(null);
                Check(version+" busy queue commands are disabled",model.IsBusy && !model.MoveTaskUpCommand.CanExecute(null) && !model.MoveTaskDownCommand.CanExecute(null) && !model.RemoveCompletedTasksCommand.CanExecute(null) && !model.ExtractSelectedTaskCommand.CanExecute(null));
                model.CancelCommand.Execute(null);await model.WaitForIdleAsync();await Layout(window);
                Check(version+" cancellation retains selected and remaining pending tasks",!rows[0].Completed && !rows[1].Completed && model.Tasks.SequenceEqual(rows.Take(2)) && !Directory.Exists(rows[1].ShellOutputPath));
                var order=new List<ArchiveTaskRow>();foreach(var row in rows.Take(2)) row.PropertyChanged+=(_,args)=>{if(args.PropertyName==nameof(ArchiveTaskRow.Running) && row.Running) order.Add(row);};
                model.SelectedTask=rows[1];await OpenMenu(window);Invoke(window.QueueMoveUpAction);await Layout(window);
                model.ExtractCommand.Execute(null);await model.WaitForIdleAsync();await Layout(window);
                Check(version+" full batch follows displayed order",order.SequenceEqual(new[] {rows[1],rows[0]}) && model.Tasks.All(row=>row.Completed));
                Check(version+" success uses each task target and keeps encoding",rows[1].ResultOutputPath==rows[1].ShellOutputPath && rows[0].ResultOutputPath==rows[0].ShellOutputPath && rows[1].FilenameCodePage==936);
                Check(version+" selected extraction does not mutate source archives",File.ReadAllBytes(first).SequenceEqual(File.ReadAllBytes(Path.Combine(root,"tests/fixtures/generated/encrypted-header.7z"))) && File.ReadAllBytes(second).SequenceEqual(File.ReadAllBytes(Path.Combine(root,"tests/fixtures/generated/plain.zip"))));
                Check(version+" finished batch releases busy state",model.IsIdle && model.Tasks.All(row=>!row.Running));
                foreach(var size in new[] {(1120.0,760.0),(800.0,600.0)})
                {
                    window.Width=size.Item1;window.Height=size.Item2;await Layout(window);window.QueueActionsAction.BringIntoView();await Layout(window);
                    var bounds=window.QueueActionsAction.TransformToAncestor(window).TransformBounds(new Rect(window.QueueActionsAction.RenderSize));Check(version+" queue entry reachable "+size.Item1,bounds.Left>=0 && bounds.Top>=0 && bounds.Right<=window.ActualWidth && bounds.Bottom<=window.ActualHeight && window.QueueActionsAction.IsEnabled);
                    await OpenMenu(window);Check(version+" menu binds live model and exposes keyboard actions "+size.Item1,window.QueueActionsMenu.DataContext==model && window.QueueRemoveCompletedAction.IsEnabled && window.QueueMoveUpAction.Focusable && window.QueueExtractSelectedAction.Focusable);
                    if(version=="26.04")
                    {
                        var hwnd=((HwndSource)PresentationSource.FromVisual(window.QueueActionsMenu)).Handle;SetWindowPos(hwnd,0,-32000,-32000,0,0,0x0015);window.QueueActionsMenu.Opacity=1;window.QueueActionsMenu.UpdateLayout();RenderMenu(window.QueueActionsMenu,Path.Combine(preview,"menu-"+size.Item1+".png"));window.QueueActionsMenu.Opacity=0;
                        window.QueueActionsMenu.IsOpen=false;window.Opacity=1;DesktopAccessibilityValidation.Render(window,Path.Combine(preview,"main-"+size.Item1+".png"),1);window.Opacity=0;
                    }
                    if(!window.QueueActionsMenu.IsOpen) await OpenMenu(window);
                    FocusManager.SetFocusedElement(window.QueueActionsMenu,window.QueueRemoveCompletedAction);
                    Check(version+" menu logical focus enters enabled action "+size.Item1,FocusManager.GetFocusedElement(window.QueueActionsMenu)==window.QueueRemoveCompletedAction);
                    var escape=new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(window.QueueActionsMenu),0,Key.Escape) {RoutedEvent=Keyboard.KeyDownEvent};window.QueueRemoveCompletedAction.RaiseEvent(escape);await Layout(window);
                    Check(version+" Escape route closes menu "+size.Item1,!window.QueueActionsMenu.IsOpen);
                    window.QueueActionsMenu.IsOpen=false;
                }
                theme.Apply(true);await OpenMenu(window);
                Check(version+" menu follows high contrast resources",window.QueueActionsMenu.Background==SystemColors.WindowBrush && window.QueueMoveUpAction.Foreground==SystemColors.WindowTextBrush);
                Check(version+" menu automation exposes concrete action names",new[] {window.QueueExtractSelectedAction,window.QueueMoveUpAction,window.QueueMoveDownAction,window.QueueRemoveCompletedAction}.All(item=>UIElementAutomationPeer.CreatePeerForElement(item)!.GetName()==item.Header.ToString()));
                window.QueueActionsMenu.IsOpen=false;theme.Apply(false);
                model.RemoveCompletedTasksCommand.Execute(null);Check(version+" completed history clears without deleting extracted files",model.Tasks.Count==0 && rows.Take(2).All(row=>Directory.Exists(row.ResultOutputPath)));
            }
        }
        catch(Exception error) {Check("exception "+error.GetType().Name+" "+error.Message,false);}
        finally {window.QueueActionsMenu.IsOpen=false;window.Opacity=0;theme.Apply(SystemParameters.HighContrast);}
        await File.WriteAllTextAsync(Path.Combine(root,"artifacts/queue-ui-validation.json"),JsonSerializer.Serialize(new {passed=checks.Count>0 && checks.Values.All(value=>value),total=checks.Count,checks,syntheticData=true,usesActualCommandsAndEngines=true},new JsonSerializerOptions {WriteIndented=true}));return checks.Count>0 && checks.Values.All(value=>value);
    }
    private static async Task OpenMenu(MainWindow window) {Invoke(window.QueueActionsAction);await Layout(window);}
    private static void Invoke(UIElement element)=>((IInvokeProvider)UIElementAutomationPeer.CreatePeerForElement(element)!.GetPattern(PatternInterface.Invoke)).Invoke();
    private static async Task Layout(Window window)=>await window.Dispatcher.InvokeAsync(window.UpdateLayout,DispatcherPriority.ApplicationIdle);
    private static void RenderMenu(ContextMenu menu,string path)
    {var bitmap=new RenderTargetBitmap((int)Math.Ceiling(menu.ActualWidth),(int)Math.Ceiling(menu.ActualHeight),96,96,PixelFormats.Pbgra32);bitmap.Render(menu);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var stream=File.Create(path);encoder.Save(stream);}
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint hwnd,nint after,int x,int y,int width,int height,uint flags);
}
