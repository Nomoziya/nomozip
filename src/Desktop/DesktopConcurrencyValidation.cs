using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Threading;
using ArchivePassword.Core;
using ArchivePassword.Infrastructure;

namespace ArchivePassword.Desktop;
internal static class DesktopConcurrencyValidation
{
    internal static async Task<bool> RunAsync(MainWindow window,ThemeResources theme,string root,string baseDirectory)
    {
        var data=Path.Combine(root,"artifacts/concurrency-ui",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(data);
        var preview=Path.Combine(root,"artifacts/concurrency-preview");Directory.CreateDirectory(preview);
        var checks=new Dictionary<string,bool>();void Check(string name,bool value)=>checks.Add(name,value);
        var worker=Path.Combine(baseDirectory,"ArchiveEngineWorker.exe");var modalCount=0;var modalPeak=0;var promptCount=0;var mode="manual";MainViewModel? current=null;Exception? promptError=null;
        var secrets=new Dictionary<string,string>();
        var dialogs=new WindowDialogs(window,dialog=>
        {
            promptCount++;modalCount++;modalPeak=Math.Max(modalPeak,modalCount);
            dialog.WindowStartupLocation=WindowStartupLocation.Manual;dialog.Left=-32000;dialog.Top=-32000;dialog.Opacity=0;dialog.ShowActivated=false;dialog.ShowInTaskbar=false;
            dialog.Closed+=(_,_)=>modalCount--;
            dialog.Loaded+=(_,_)=>dialog.Dispatcher.BeginInvoke(async()=>
            {
                try
                {
                    await Task.Delay(100);
                    if(mode=="cancel") {current!.CancelCommand.Execute(null);return;}
                    var description=((TextBlock)dialog.FindName("Description")).Text;var name=description.Split('\n')[0];
                    ((PasswordBox)dialog.FindName("SecretBox")).Password=secrets[name];
                    ((CheckBox)dialog.FindName("SavePassword")).IsChecked=mode=="manual";
                    ((TextBox)dialog.FindName("NameBox")).Text=name;
                    Invoke((Button)dialog.FindName("ContinueButton"));
                }
                catch(Exception error) {promptError=error;dialog.Close();}
            });
        });
        async Task<MainViewModel> Model(string directory,DpapiPasswordVault vault,string version)
        {
            var model=new MainViewModel(baseDirectory,directory,vault,dialogs);current=model;window.DataContext=model;await model.InitializeAsync();model.SelectedEngineVersion=version;model.ApplyEngineCommand.Execute(null);await model.WaitForIdleAsync();await Layout(window);return model;
        }
        async Task<(int Rows,int Workers)> Monitor(MainViewModel model,Action<int>? observe=null)
        {
            var peakRows=0;var peakWorkers=0;var end=DateTime.UtcNow.AddSeconds(90);
            while(model.IsBusy)
            {
                peakRows=Math.Max(peakRows,model.Tasks.Count(row=>row.Running));var count=OwnedWorkers(worker);peakWorkers=Math.Max(peakWorkers,count);observe?.Invoke(count);
                if(DateTime.UtcNow>end) {model.CancelCommand.Execute(null);throw new TimeoutException("isolated concurrency operation timed out");}
                await Task.Delay(5);
            }
            await model.WaitForIdleAsync();await Layout(window);return (peakRows,peakWorkers);
        }
        string Hash(string file) {using var stream=File.OpenRead(file);return Convert.ToHexString(SHA256.HashData(stream));}
        try
        {
            foreach(var version in new[] {"26.03","26.04"})
            {
                var folder=Path.Combine(data,version);Directory.CreateDirectory(folder);var vault=new DpapiPasswordVault(Path.Combine(folder,"passwords.apv"));
                await File.WriteAllTextAsync(Path.Combine(folder,"settings.json"),JsonSerializer.Serialize(new {Version=1,OutputPath=Path.Combine(folder,"fallback"),AutoTry=true,ConflictPolicy=0,EngineVersion=version}));
                var model=await Model(folder,vault,version);Check(version+" old settings default to sequential",model.QueueConcurrency==1);
                window.QueueConcurrencyChoice.SelectedIndex=1;await Layout(window);Check(version+" actual choice enables two tasks",model.QueueConcurrency==2);
                model.ConflictPolicy=2;model.OutputPath=Path.Combine(folder,"fallback");
                var inputs=new[] {"a.7z","b.7z","c.7z"}.Select(name=>Path.Combine(folder,name)).ToArray();foreach(var file in inputs) File.Copy(Path.Combine(root,"tests/fixtures/generated/large.7z"),file);
                model.AddFiles(inputs);var rows=model.Tasks.ToArray();for(var index=0;index<rows.Length;index++) rows[index].ShellOutputPath=Path.Combine(folder,"independent-"+index);
                var launches=new List<ArchiveTaskRow>();foreach(var row in rows) row.PropertyChanged+=(_,args)=>{if(args.PropertyName==nameof(ArchiveTaskRow.Running) && row.Running) launches.Add(row);};
                model.ExtractCommand.Execute(null);await Layout(window);Check(version+" choice and queue edits disabled during work",!window.QueueConcurrencyChoice.IsEnabled && !model.MoveTaskUpCommand.CanExecute(null));model.QueueConcurrency=1;Check(version+" active limit cannot change",model.QueueConcurrency==2);
                var peak=await Monitor(model);Check(version+" two real isolated workers overlap and stay bounded",peak.Rows==2 && peak.Workers==2);
                Check(version+" starts tasks in visible order",launches.SequenceEqual(rows));
                Check(version+" independent outputs match source bytes",rows.All(row=>row.Completed && Hash(Path.Combine(row.ResultOutputPath!,"large.bin"))==Hash(Path.Combine(root,"tests/fixtures/generated/large.bin"))));
                Check(version+" work releases all native workers",OwnedWorkers(worker)==0 && rows.All(row=>!row.Running));
                var reopened=await Model(folder,vault,version);Check(version+" selected limit survives settings reload",reopened.QueueConcurrency==2);model=reopened;model.OutputPath=Path.Combine(folder,"same-target");model.ConflictPolicy=2;model.AddFiles(inputs.Take(2));peak=await RunAndMonitor();
                Check(version+" common target runs one task at a time",peak.Rows==1 && peak.Workers==1 && model.Tasks.All(row=>row.Completed));
                Check(version+" common target keeps correct output",Hash(Path.Combine(model.OutputPath,"large.bin"))==Hash(Path.Combine(root,"tests/fixtures/generated/large.bin")));
                model.Tasks.Clear();model.AddFiles(inputs.Take(2));model.ApplyDirectoryRules(new(Layout:ExtractionLayout.ArchiveFolder),Path.Combine(folder,"per-archive"));peak=await RunAndMonitor();
                Check(version+" shared base directory rules stay serialized",peak.Rows==1 && model.Tasks.All(row=>row.Completed));
                Check(version+" each archive directory rule still resolves own output",model.Tasks.Select((row,index)=>row.ResultOutputPath==Path.GetFullPath(Path.Combine(model.OutputPath,index==0?"a":"b")) && Hash(Path.Combine(row.ResultOutputPath!,"large.bin"))==Hash(Path.Combine(root,"tests/fixtures/generated/large.bin"))).All(value=>value));
                model.ApplyDirectoryRules(new(),Path.Combine(folder,"fallback"));
                model.Tasks.Clear();model.AddFiles(inputs);rows=model.Tasks.ToArray();foreach(var (row,index) in rows.Select((row,index)=>(row,index))) row.ShellOutputPath=Path.Combine(folder,"cancel-"+index);
                launches.Clear();foreach(var row in rows) row.PropertyChanged+=(_,args)=>{if(args.PropertyName==nameof(ArchiveTaskRow.Running) && row.Running) launches.Add(row);};
                var cancelled=false;model.ExtractCommand.Execute(null);peak=await Monitor(model,count=>{if(count==2 && !cancelled){cancelled=true;model.CancelCommand.Execute(null);}});
                Check(version+" cancel interrupts actual simultaneous workers",cancelled && peak.Workers==2 && OwnedWorkers(worker)==0);
                Check(version+" cancel never starts waiting task",launches.SequenceEqual(rows.Take(2)) && !rows[2].Completed && !Directory.Exists(rows[2].ShellOutputPath));
                Check(version+" cancel cleans private staging directories",rows.All(row=>!row.Running) && !Directory.EnumerateDirectories(folder,".archive-password-*",SearchOption.AllDirectories).Any());
                model.Tasks.Clear();var bad=Path.Combine(folder,"missing.7z");File.Copy(inputs[0],bad);model.AddFiles([bad,inputs[0]]);rows=model.Tasks.ToArray();rows[0].ShellOutputPath=Path.Combine(folder,"failed");rows[1].ShellOutputPath=Path.Combine(folder,"survivor");File.Delete(bad);peak=await RunAndMonitor();
                Check(version+" one ordinary failure does not cancel independent task",!rows[0].Completed && rows[0].Failure is not null && rows[1].Completed);
                Check(version+" failure state and output stay separate",Hash(Path.Combine(rows[1].ResultOutputPath!,"large.bin"))==Hash(Path.Combine(root,"tests/fixtures/generated/large.bin")) && rows[1].Failure is null);
                var manualPaths=new List<string>();secrets.Clear();
                foreach(var index in new[] {0,1})
                {
                    var name="manual-"+index+".7z";var archive=Path.Combine(folder,name);var source=Path.Combine(folder,"source-"+index+".txt");File.WriteAllText(source,"distinct content "+index);var secret="synthetic-manual-"+index;secrets[name]=secret;
                    var engine=new ArchiveEngineClient(worker,Path.Combine(baseDirectory,"engines",version,"7z.dll"));var created=await engine.CreateAsync(new([new(source,"item.txt",false)],"7z",1,true),archive,secret);if(!created.IsSuccess) throw new IOException("synthetic encrypted archive creation failed");manualPaths.Add(archive);
                }
                model.Tasks.Clear();model.AddFiles(manualPaths);rows=model.Tasks.ToArray();for(var index=0;index<rows.Length;index++) rows[index].ShellOutputPath=Path.Combine(folder,"manual-output-"+index);model.AutoTry=false;mode="manual";promptCount=0;modalPeak=0;peak=await RunAndMonitor();
                Check(version+" password dialogs are serialized without nesting",promptCount==2 && modalPeak==1 && modalCount==0 && promptError is null);
                Check(version+" distinct manual passwords decrypt only their own jobs",rows.Select((row,index)=>row.Completed && File.ReadAllText(Path.Combine(row.ResultOutputPath!,"item.txt"))=="distinct content "+index).All(value=>value));
                var saved=await vault.ReadAsync();Check(version+" concurrent successful saves keep both records",saved.Count==2 && rows.All(row=>saved.Single(value=>value.Name==row.Name).LastSuccessAt is not null));
                Check(version+" archive associations point to own saved password",await vault.GetAssociatedPasswordAsync(rows[0].Path)==saved.Single(value=>value.Name==rows[0].Name).Id && await vault.GetAssociatedPasswordAsync(rows[1].Path)==saved.Single(value=>value.Name==rows[1].Name).Id);
                model.Tasks.Clear();model.AddFiles(manualPaths);rows=model.Tasks.ToArray();for(var index=0;index<rows.Length;index++) rows[index].ShellOutputPath=Path.Combine(folder,"prompt-cancel-"+index);mode="cancel";promptCount=0;modalPeak=0;peak=await RunAndMonitor();
                Check(version+" cancellation closes prompt and suppresses waiting prompt",promptCount==1 && modalPeak==1 && modalCount==0 && rows.All(row=>!row.Completed && !row.Running));
                Check(version+" cancelled prompts do not add passwords",(await vault.ReadAsync()).Count==saved.Count);
                var skipFolder=Path.Combine(folder,"skip-isolation");Directory.CreateDirectory(skipFolder);var skipVault=new DpapiPasswordVault(Path.Combine(skipFolder,"passwords.apv"));
                for(var index=0;index<12;index++) await skipVault.SaveAsync(new(Guid.NewGuid(),"wrong "+index,"wrong-synthetic-"+index,"",true,DateTimeOffset.UnixEpoch.AddSeconds(index)));
                var correctId=Guid.NewGuid();await skipVault.SaveAsync(new(correctId,"correct","测试密碼 ' $ & 空格","",true,DateTimeOffset.UnixEpoch.AddDays(1)));
                model=await Model(skipFolder,skipVault,version);model.QueueConcurrency=2;model.ConflictPolicy=2;model.OutputPath=Path.Combine(skipFolder,"fallback");
                var skipInputs=new[] {"skip-a.7z","skip-b.7z"}.Select(name=>Path.Combine(skipFolder,name)).ToArray();foreach(var file in skipInputs) File.Copy(Path.Combine(root,"tests/fixtures/generated/encrypted-header.7z"),file);
                model.AddFiles(skipInputs);rows=model.Tasks.ToArray();for(var index=0;index<rows.Length;index++) rows[index].ShellOutputPath=Path.Combine(skipFolder,"output-"+index);
                secrets.Clear();foreach(var row in rows) secrets[row.Name]="测试密碼 ' $ & 空格";mode="skip";promptCount=0;var skipped=false;var otherStillTrying=false;
                model.ExtractCommand.Execute(null);await Monitor(model,_=>
                {
                    if(!skipped && rows.All(row=>row.Status.StartsWith("正在尝试保存的密码")))
                    {model.SelectedTask=rows[0];if(model.CanSkipPasswords){skipped=true;model.SkipPasswordsCommand.Execute(null);otherStillTrying=rows[1].Running && rows[1].Status.StartsWith("正在尝试保存的密码");}}
                });
                Check(version+" skip acts only on selected running task",skipped && otherStillTrying && promptCount==1 && rows.All(row=>row.Completed));
                Check(version+" other task keeps automatic success association",await skipVault.GetAssociatedPasswordAsync(rows[1].Path)==correctId && await skipVault.GetAssociatedPasswordAsync(rows[0].Path) is null);
                foreach(var contrast in new[] {false,true}) foreach(var size in new[] {(1120.0,760.0),(800.0,600.0)})
                {
                    theme.Apply(contrast);window.Width=size.Item1;window.Height=size.Item2;await Layout(window);window.QueueConcurrencyChoice.BringIntoView();await Layout(window);var bounds=window.QueueConcurrencyChoice.TransformToAncestor(window).TransformBounds(new Rect(window.QueueConcurrencyChoice.RenderSize));
                    var peer=UIElementAutomationPeer.CreatePeerForElement(window.QueueConcurrencyChoice)!;Check(version+" limit choice accessible and visible "+contrast+" "+size.Item1,bounds.Left>=0 && bounds.Top>=0 && bounds.Right<=window.ActualWidth && bounds.Bottom<=window.ActualHeight && peer.IsKeyboardFocusable() && peer.GetName()=="同时处理的解压任务数" && window.QueueConcurrencyChoice.IsEnabled);
                    if(version=="26.04") {window.Opacity=1;DesktopAccessibilityValidation.Render(window,Path.Combine(preview,"main-"+contrast+"-"+size.Item1+".png"),1);window.Opacity=0;}
                }
                async Task<(int Rows,int Workers)> RunAndMonitor() {model.ExtractCommand.Execute(null);return await Monitor(model);}
            }
        }
        catch(Exception error) {Check("exception "+error.GetType().Name+" "+error.Message,false);}
        finally {theme.Apply(SystemParameters.HighContrast);window.Opacity=0;}
        var report=new {passed=checks.Count>0 && checks.Values.All(value=>value),total=checks.Count,checks,syntheticData=true,usesActualCommandsAndEngines=true};await File.WriteAllTextAsync(Path.Combine(root,"artifacts/concurrency-ui-validation.json"),JsonSerializer.Serialize(report,new JsonSerializerOptions {WriteIndented=true}));return report.passed;
    }
    private static int OwnedWorkers(string worker)
    {var count=0;foreach(var process in Process.GetProcessesByName("ArchiveEngineWorker")) {using(process) try {if(process.MainModule?.FileName.Equals(worker,StringComparison.OrdinalIgnoreCase)==true) count++;}catch(Exception error) when(error is System.ComponentModel.Win32Exception or InvalidOperationException) {}}return count;}
    private static void Invoke(UIElement element)=>((IInvokeProvider)UIElementAutomationPeer.CreatePeerForElement(element)!.GetPattern(PatternInterface.Invoke)).Invoke();
    private static async Task Layout(Window window)=>await window.Dispatcher.InvokeAsync(window.UpdateLayout,DispatcherPriority.ApplicationIdle);
}
