using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Threading;
using ArchivePassword.Core;
using ArchivePassword.Infrastructure;

namespace ArchivePassword.Desktop;
internal static class DesktopEncodingValidation
{
    internal static async Task<bool> RunAsync(MainWindow owner,ThemeResources theme,string root,string baseDirectory)
    {
        var data=Path.Combine(root,"artifacts/encoding-ui",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(data);
        var checks=new Dictionary<string,bool>();void Check(string name,bool value)=>checks.Add(name,value);
        var zip=JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(root,"artifacts/encoding-fixtures.json"))).RootElement.GetProperty("zip").GetString()!;
        var expected="中文目录/资料.txt";FilenameEncodingDialog? modal=null;var mode="apply";
        try
        {
            var main=new MainViewModel(baseDirectory,data,new DpapiPasswordVault(Path.Combine(data,"passwords.apv")),new WindowDialogs(owner,
                prepareContents:dialog=>{dialog.ShowActivated=false;dialog.ShowInTaskbar=false;dialog.WindowStartupLocation=WindowStartupLocation.Manual;dialog.Left=-32000;dialog.Top=-32000;dialog.Loaded+=(_,_)=>dialog.Dispatcher.BeginInvoke(()=>{Check("actual browser uses applied encoding",((ArchiveContentsViewModel)dialog.DataContext) .Rows.Single(row=>!row.Entry.Directory).Path.Replace('\\','/')==expected);dialog.Close();});},
                prepareEncoding:dialog=>
                {
                    modal=dialog;dialog.ShowActivated=false;dialog.ShowInTaskbar=false;dialog.WindowStartupLocation=WindowStartupLocation.Manual;dialog.Left=-32000;dialog.Top=-32000;
                    dialog.Loaded+=(_,_)=>dialog.Dispatcher.BeginInvoke(async()=>
                    {
                        try {
                        await dialog.Model.WaitForIdleAsync();await Layout(dialog);
                        Check(mode+" modal owned and parent busy",dialog.Owner==owner && ((MainViewModel)owner.DataContext).IsBusy);
                        if(mode=="parent-cancel") {((MainViewModel)owner.DataContext).CancelCommand.Execute(null);return;}
                        if(mode=="cancel") {dialog.Model.Selected=dialog.Model.Choices.Single(value=>value.CodePage==932);Invoke((Button)dialog.FindName("CancelEncodingButton"));return;}
                        dialog.Model.Selected=dialog.Model.Choices.Single(value=>value.CodePage==936);
                        Check("choice change clears preview and prevents unreviewed apply",dialog.Model.Names.Count==0 && !dialog.Model.CanApply);
                        Invoke((Button)dialog.FindName("PreviewNamesButton"));await Layout(dialog);await dialog.Model.WaitForIdleAsync();await Layout(dialog);
                        Check("real ZIP name preview decoded correctly",dialog.Model.Names.Single().Path.Replace('\\','/')==expected && dialog.Model.CanApply);
                        var table=(DataGrid)dialog.FindName("EncodingNames");var peer=UIElementAutomationPeer.CreatePeerForElement(table);
                        Check("preview table accessible name",peer!.GetName()=="ZIP 文件名预览");
                        var screenshots=Path.Combine(root,"artifacts/encoding-preview");Directory.CreateDirectory(screenshots);
                        foreach(var (width,height) in new[] {(720,580),(540,480)}) foreach(var contrast in new[] {false,true})
                        {
                            theme.Apply(contrast);dialog.Width=width;dialog.Height=height;await Layout(dialog);
                            var suffix=$"{width}x{height}-{contrast}";
                            Check(suffix+" dropdown and action fully visible",Fits(dialog,(FrameworkElement)dialog.FindName("EncodingChoice")) && Fits(dialog,(FrameworkElement)dialog.FindName("ApplyEncodingButton")) && Fits(dialog,(FrameworkElement)dialog.FindName("PreviewNamesButton")));
                            var row=(DataGridRow)table.ItemContainerGenerator.ContainerFromIndex(0);var bounds=row.TransformToAncestor(table).TransformBounds(new Rect(row.RenderSize));
                            Check(suffix+" preview row fully visible",bounds.Top>=0 && bounds.Bottom<=table.ActualHeight && row.ActualHeight>20);
                            Check(suffix+" row exposes real decoded name",UIElementAutomationPeer.CreatePeerForElement(row)!.GetName().Replace('\\','/')==expected);
                            DesktopAccessibilityValidation.Render(dialog,Path.Combine(screenshots,suffix+".png"),1);
                        }
                        theme.Apply(false);Invoke((Button)dialog.FindName("ApplyEncodingButton"));
                        } catch(Exception error) {Check("modal exception "+error.GetType().Name+" "+error.Message+" "+error.StackTrace,false);dialog.Close();}
                    });
                }));
            owner.DataContext=main;await main.InitializeAsync();main.AddFiles([zip]);main.SelectedTask=main.Tasks.Single();await Layout(owner);
            var task=main.SelectedTask;task.Completed=true;task.VerificationResult=new EngineResult {Status="success"};Check("default task retains engine encoding",task.FilenameCodePage==0);
            Check("actual ZIP encoding entry available",main.FilenameEncodingCommand.CanExecute(null));
            await Entry(main,owner);Check("apply modifies only selected task",task.FilenameCodePage==936 && main.IsIdle && !task.Completed && task.VerificationResult is null);
            var another=Path.Combine(data,"another.zip");File.Copy(zip,another);main.AddFiles([another]);Check("other ZIP task retains default",main.Tasks.Last().FilenameCodePage==0);
            main.SelectedTask=task;await Layout(owner);mode="cancel";await Entry(main,owner);Check("cancel preserves applied codepage",task.FilenameCodePage==936);
            mode="parent-cancel";await Entry(main,owner);Check("parent cancellation closes modal and retains encoding",main.IsIdle && task.FilenameCodePage==936);
            main.Tasks.Remove(main.Tasks.Last());main.BrowseArchiveCommand.Execute(null);await main.WaitForIdleAsync();
            main.TestArchiveCommand.Execute(null);await main.WaitForIdleAsync();Check("actual standalone test succeeds with applied encoding",task.VerificationResult?.IsSuccess==true);
            main.OutputPath=Path.Combine(data,"output");main.AutoTry=false;main.ExtractCommand.Execute(null);await main.WaitForIdleAsync();
            Check("actual main extraction names and bytes agree with preview",task.Completed && File.ReadAllText(Path.Combine(main.OutputPath,expected))=="actual content");
            var reopened=new MainViewModel(baseDirectory,data,new DpapiPasswordVault(Path.Combine(data,"passwords.apv")),new WindowDialogs(owner));await reopened.InitializeAsync();reopened.AddFiles([zip]);
            Check("new queue does not inherit previous archive encoding",reopened.Tasks.Single().FilenameCodePage==0);
            var nonzip=Path.Combine(data,"sample.7z");File.WriteAllText(nonzip,"synthetic");main.AddFiles([nonzip]);main.SelectedTask=main.Tasks.Last();await Layout(owner);Check("non ZIP entry disabled",!main.FilenameEncodingCommand.CanExecute(null));
            var failure=new FilenameEncodingViewModel(zip,0,(_,_)=>Task.FromResult(new EngineResult {Code="filename_encoding_unsupported"}));failure.PreviewCommand.Execute(null);await failure.WaitForIdleAsync();
            Check("unsupported engine explicit error and apply unavailable",!failure.CanApply && failure.Status.Contains("不支持"));
            var gate=new TaskCompletionSource<EngineResult>();var receivedCancellation=false;
            var closingModel=new FilenameEncodingViewModel(zip,0,async(_,token)=>{using var registration=token.Register(()=>{receivedCancellation=true;gate.TrySetCanceled(token);});return await gate.Task;});
            modal=new FilenameEncodingDialog(closingModel) {Owner=owner,ShowActivated=false,ShowInTaskbar=false,WindowStartupLocation=WindowStartupLocation.Manual,Left=-32000,Top=-32000};modal.Show();await Layout(modal);modal.Close();await closingModel.WaitForIdleAsync();await Layout(owner);
            Check("closing busy preview cancels worker and waits for release",receivedCancellation && !modal.IsVisible && !closingModel.IsBusy);
        }
        catch(Exception error) {checks["exception "+error.GetType().Name+" "+error.Message]=false;}
        finally {if(modal is {IsVisible:true}) {modal.Model.Cancel();await modal.Model.WaitForIdleAsync();modal.Close();}theme.Apply(false);}
        await File.WriteAllTextAsync(Path.Combine(root,"artifacts/encoding-ui-validation.json"),JsonSerializer.Serialize(new {passed=checks.Count>0 && checks.Values.All(value=>value),total=checks.Count,checks,syntheticFiles=true},new JsonSerializerOptions {WriteIndented=true}));return checks.Count>0 && checks.Values.All(value=>value);
    }
    private static async Task Entry(MainViewModel main,MainWindow window)
    {
        var done=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        System.ComponentModel.PropertyChangedEventHandler changed=(_,args)=>{if(args.PropertyName==nameof(MainViewModel.IsIdle) && main.IsIdle) done.TrySetResult();};
        main.PropertyChanged+=changed;
        try
        {
            System.Windows.Input.CommandManager.InvalidateRequerySuggested();await Layout(window);
            window.TaskContextMenu.PlacementTarget=window.ArchiveTasks;window.TaskContextMenu.IsOpen=true;await Layout(window);
            var entry=window.TaskEncodingAction;
            if(!DesktopMenuValidation.Reachable(entry) || !entry.IsEnabled)throw new InvalidOperationException("Actual filename encoding menu entry is unavailable.");
            ((IInvokeProvider)UIElementAutomationPeer.CreatePeerForElement(entry)!.GetPattern(PatternInterface.Invoke)).Invoke();
            await done.Task.WaitAsync(TimeSpan.FromSeconds(30));
        }
        finally {window.TaskContextMenu.IsOpen=false;main.PropertyChanged-=changed;}
    }
    private static void Invoke(Button button)=>((IInvokeProvider)UIElementAutomationPeer.CreatePeerForElement(button)!.GetPattern(PatternInterface.Invoke)).Invoke();
    private static async Task Layout(Window window)=>await window.Dispatcher.InvokeAsync(window.UpdateLayout,DispatcherPriority.ApplicationIdle);
    private static bool Fits(Window window,FrameworkElement element) {var content=(FrameworkElement)window.Content;var bounds=element.TransformToAncestor(content).TransformBounds(new Rect(element.RenderSize));return element.IsVisible && bounds.Top>=0 && bounds.Left>=0 && bounds.Bottom<=content.ActualHeight+1 && bounds.Right<=content.ActualWidth+1;}
}
