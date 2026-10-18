using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Threading;
using ArchivePassword.Core;
using ArchivePassword.Infrastructure;
using Microsoft.Win32.SafeHandles;

namespace ArchivePassword.Desktop;
internal static class DesktopPauseValidation
{
    internal static async Task<bool> RunAsync(MainWindow window,ThemeResources theme,string root,string baseDirectory)
    {
        using var confirmation=DesktopExtractionConfirmationValidation.Accept(window);
        var data=Path.Combine(root,"artifacts/pause-ui",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(data);var preview=Path.Combine(root,"artifacts/pause-preview");Directory.CreateDirectory(preview);
        var checks=new Dictionary<string,bool>();void Check(string name,bool value)=>checks.Add(name,value);MainViewModel? model=null;
        var worker=Path.Combine(baseDirectory,"ArchiveEngineWorker.exe");
        var fixture=Path.Combine(root,"tests/fixtures/generated/large-ui.7z");if(!File.Exists(fixture)) fixture=Path.Combine(root,"tests/fixtures/generated/large.7z");
        var sourceLength=new FileInfo(Path.Combine(root,"tests/fixtures/generated/large.bin")).Length;
        var sizeSamples=0;long maximumLiveLength=0;
        bool HasPartialExtraction(ArchiveTaskRow row)=>Directory.Exists(row.ShellOutputPath) && Directory.GetDirectories(row.ShellOutputPath,".archive-password-*")
            .SelectMany(path=>Directory.GetFiles(path,"*",SearchOption.AllDirectories)).Any(path=>
            {
                // Directory-entry sizes can lag behind an open writer on NTFS.
                // Query the live handle, keeping the original partial-file criterion.
                try
                {
                    var length=LiveLength(path);sizeSamples++;maximumLiveLength=Math.Max(maximumLiveLength,length);
                    return length>0 && length<sourceLength;
                }
                catch(IOException error) when((error.HResult&0xffff) is 2 or 3){return false;}
            });
        async Task Wait(string phase,Func<bool> condition,bool layout=true)
        {var deadline=DateTime.UtcNow.AddSeconds(30);while(!condition()){if(DateTime.UtcNow>deadline) throw new TimeoutException("pause UI condition timed out: "+JsonSerializer.Serialize(new {phase,workers=WorkerIds(),sizeSamples,maximumLiveLength,sourceLength,page=model?.Page,busy=model?.IsBusy,concurrency=model?.QueueConcurrency,tasks=model?.Tasks.Select(row=>new {row.Name,row.Running,row.Completed,row.Status,row.Percent})}));await Task.Delay(5);}if(layout) await Layout(window);}
        int[] WorkerIds() {var ids=new List<int>();foreach(var process in Process.GetProcessesByName("ArchiveEngineWorker")){using(process) try {if(process.MainModule?.FileName.Equals(worker,StringComparison.OrdinalIgnoreCase)==true) ids.Add(process.Id);}catch(Exception error) when(error is System.ComponentModel.Win32Exception or InvalidOperationException){}}return ids.Order().ToArray();}
        string Hash(string file) {using var stream=File.OpenRead(file);return Convert.ToHexString(SHA256.HashData(stream));}
        try
        {
            foreach(var version in new[] {"26.03","26.04"})
            foreach(var cancel in new[] {false,true})
            {
                var folder=Path.Combine(data,version+"-"+cancel);Directory.CreateDirectory(folder);var vaultPath=Path.Combine(folder,"passwords.apv");var vault=new DpapiPasswordVault(vaultPath);await vault.SaveAsync(new(Guid.NewGuid(),"unused synthetic","unused-secret","",true,DateTimeOffset.UtcNow));var vaultBefore=File.ReadAllBytes(vaultPath);
                model=new MainViewModel(baseDirectory,folder,vault,new WindowDialogs(window));window.DataContext=model;await model.InitializeAsync();model.SelectedEngineVersion=version;model.ApplyEngineCommand.Execute(null);await model.WaitForIdleAsync();await Layout(window);
                Check(version+" idle pause action stays disabled "+cancel,!window.QueuePauseAction.IsEnabled && !model.ToggleQueuePauseCommand.CanExecute(null));model.QueueConcurrency=2;model.ConflictPolicy=2;model.OutputPath=Path.Combine(folder,"fallback");
                var paths=new[] {"a.7z","b.7z","waiting.7z"}.Select(name=>Path.Combine(folder,name)).ToArray();foreach(var path in paths) File.Copy(fixture,path);model.AddFiles(paths);var rows=model.Tasks.ToArray();for(var index=0;index<rows.Length;index++) rows[index].ShellOutputPath=Path.Combine(folder,"output-"+index);
                var launches=new List<ArchiveTaskRow>();foreach(var row in rows) row.PropertyChanged+=(_,args)=>{if(args.PropertyName==nameof(ArchiveTaskRow.Running) && row.Running) launches.Add(row);};
                sizeSamples=0;maximumLiveLength=0;
                await Layout(window);Invoke(window.ExtractAction);
                // Verification also launches workers. Wait for actual partial extraction files,
                // otherwise pausing between phases can legitimately leave only managed waiters.
                // Start observing immediately: an idle-priority layout wait can run after
                // every fast native task has finished, losing the pause boundary entirely.
                await Wait("two active extraction workers with partial output",()=>WorkerIds().Length==2 && rows.Take(2).All(row=>row.Running && HasPartialExtraction(row)),layout:false);model.ToggleQueuePauseCommand.Execute(null);
                await Wait("two native pause acknowledgements",()=>rows.Take(2).All(row=>row.Status=="已暂停"));var ids=WorkerIds();var percents=rows.Select(row=>row.Percent).ToArray();await Task.Delay(200);await Layout(window);
                Check(version+" actual pause entry changes to continue "+cancel,window.QueuePauseAction.IsVisible && window.QueuePauseAction.IsEnabled && model.QueuePauseCaption==UiText.Get("T_c4bf814baf3d"));
                Check(version+" two acknowledged workers remain paused "+cancel,ids.Length==2 && WorkerIds().SequenceEqual(ids) && rows.Take(2).All(row=>row.Status=="已暂停" && !row.IsIndeterminate));
                Check(version+" progress stable and waiting task not launched "+cancel,rows.Select(row=>row.Percent).SequenceEqual(percents) && launches.SequenceEqual(rows.Take(2)) && !rows[2].Running && !Directory.Exists(rows[2].ShellOutputPath));
                Check(version+" paused task saves no password record "+cancel,File.ReadAllBytes(vaultPath).SequenceEqual(vaultBefore));
                Check(version+" mutation and skip remain disabled while paused "+cancel,!model.MoveTaskUpCommand.CanExecute(null) && !model.RemoveCompletedTasksCommand.CanExecute(null) && !model.SkipPasswordsCommand.CanExecute(null) && model.CancelCommand.CanExecute(null));
                if(!cancel)
                {
                    foreach(var contrast in new[] {false,true}) foreach(var size in new[] {(1120.0,760.0),(800.0,600.0)})
                    {
                        theme.Apply(contrast);window.Width=size.Item1;window.Height=size.Item2;await Layout(window);var button=window.QueuePauseAction;var bounds=button.TransformToAncestor(window).TransformBounds(new Rect(button.RenderSize));var peer=UIElementAutomationPeer.CreatePeerForElement(button)!;
                        Check(version+" continue visible focusable and named "+contrast+" "+size.Item1,bounds.Left>=0 && bounds.Top>=0 && bounds.Right<=window.ActualWidth && bounds.Bottom<=window.ActualHeight && peer.IsKeyboardFocusable() && peer.GetName()==UiText.Get("T_c4bf814baf3d"));
                        if(version=="26.04") {window.Opacity=1;DesktopAccessibilityValidation.Render(window,Path.Combine(preview,"paused-"+contrast+"-"+size.Item1+".png"),1);window.Opacity=0;}
                    }
                    theme.Apply(false);Invoke(window.QueuePauseAction);await Layout(window);await model.WaitForIdleAsync().WaitAsync(TimeSpan.FromSeconds(30));await Layout(window);
                    Check(version+" actual continue finishes original queue",rows.All(row=>row.Completed && !row.Running) && launches.SequenceEqual(rows));
                    Check(version+" resumed output has correct source bytes",rows.All(row=>Hash(Path.Combine(row.ResultOutputPath!,"large.bin"))==Hash(Path.Combine(root,"tests/fixtures/generated/large.bin"))));
                }
                else
                {
                    Check(version+" cancel entry clearly targets queue",window.QueueCancelAction.Content?.ToString()=="取消队列" && window.QueueCancelAction.IsEnabled);
                    Invoke(window.QueueCancelAction);await Layout(window);await model.WaitForIdleAsync().WaitAsync(TimeSpan.FromSeconds(5));await Layout(window);
                    Check(version+" paused queue cancellation drains native workers",model.IsIdle && WorkerIds().Length==0 && rows.All(row=>!row.Running));
                    Check(version+" cancellation keeps queued task pending",launches.SequenceEqual(rows.Take(2)) && !rows[2].Completed && rows[2].Status=="准备就绪");
                    Check(version+" cancelled pause leaves no successful output",rows.All(row=>!row.Completed) && !Directory.EnumerateDirectories(folder,".archive-password-*",SearchOption.AllDirectories).Any());
                }
                Check(version+" pause resets after ending "+cancel,!model.CanPauseQueue && model.QueuePauseCaption==UiText.Get("T_e90e4d4b3a66") && !window.QueuePauseAction.IsEnabled && WorkerIds().Length==0);
                Check(version+" source archives unchanged "+cancel,paths.All(path=>Hash(path)==Hash(fixture))) ;
                // Reports retain the assertions; successful cases need no GiB-sized outputs.
                var cleanup=Path.GetFullPath(folder);var scope=Path.GetFullPath(data).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
                if(!cleanup.StartsWith(scope,StringComparison.OrdinalIgnoreCase)) throw new IOException("Pause validation cleanup escaped its owned directory.");
                Directory.Delete(cleanup,recursive:true);
            }
        }
        catch(Exception error) {Check("exception "+error.GetType().Name+" "+error.Message,false);}
        finally {if(model?.IsBusy==true){model.CancelCommand.Execute(null);await model.WaitForIdleAsync().WaitAsync(TimeSpan.FromSeconds(5));}theme.Apply(SystemParameters.HighContrast);window.Opacity=0;}
        var report=new {passed=checks.Count>0 && checks.Values.All(value=>value),total=checks.Count,checks,syntheticData=true,usesActualCommandsAndEngines=true};await File.WriteAllTextAsync(Path.Combine(root,"artifacts/pause-ui-validation.json"),JsonSerializer.Serialize(report,new JsonSerializerOptions {WriteIndented=true}));return report.passed;
    }
    private static void Invoke(UIElement element)=>((IInvokeProvider)UIElementAutomationPeer.CreatePeerForElement(element)!.GetPattern(PatternInterface.Invoke)).Invoke();
    internal static long LiveLength(string path)
    {
        // FILE_READ_ATTRIBUTES is compatible with the worker's exclusive data
        // writer; GENERIC_READ is not. This handle never reads file contents.
        using var handle=CreateFileW(path,0x80,7,IntPtr.Zero,3,0x80,IntPtr.Zero);
        if(handle.IsInvalid) throw new IOException("Cannot query live file size ("+Marshal.GetLastWin32Error()+").",Marshal.GetHRForLastWin32Error());
        if(!GetFileSizeEx(handle,out var length)) throw new IOException("Cannot query live file size ("+Marshal.GetLastWin32Error()+").",Marshal.GetHRForLastWin32Error());
        return length;
    }
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true,ExactSpelling=true)]
    private static extern SafeFileHandle CreateFileW(string path,uint access,uint share,IntPtr security,uint disposition,uint flags,IntPtr template);
    [DllImport("kernel32.dll",SetLastError=true)]
    [return:MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileSizeEx(SafeFileHandle handle,out long size);
    private static async Task Layout(Window window)=>await window.Dispatcher.InvokeAsync(window.UpdateLayout,DispatcherPriority.ApplicationIdle);
}
