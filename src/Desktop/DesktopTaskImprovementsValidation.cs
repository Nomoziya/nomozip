using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using ArchivePassword.Core;
using ArchivePassword.Infrastructure;

namespace ArchivePassword.Desktop;

internal static class DesktopTaskImprovementsValidation
{
    internal static async Task<bool> RunAsync(MainWindow window, ThemeResources theme, string root, string baseDirectory)
    {
        if(Environment.GetEnvironmentVariable("GITHUB_ACTIONS")!="true")throw new InvalidOperationException("Requires isolated CI.");
        using var confirmation=DesktopExtractionConfirmationValidation.Accept(window);
        var language=UiText.LanguageCode;
        var folder=Path.Combine(root,"artifacts/task-improvements-preview",language);Directory.CreateDirectory(folder);
        var data=Path.Combine(root,"artifacts/task-improvements-ui",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(data);
        var checks=new Dictionary<string,bool>(); string? error=null; MainViewModel? model=null;
        void Check(string name,bool passed) {checks.Add(name,passed);if(!passed)throw new InvalidOperationException(name);}
        Task Layout()=>window.Dispatcher.InvokeAsync(window.UpdateLayout,DispatcherPriority.ApplicationIdle).Task;
        async Task Until(Func<bool> predicate)
        {var end=DateTime.UtcNow.AddSeconds(90);while(!predicate()){if(DateTime.UtcNow>end)throw new TimeoutException("Task improvements condition timed out.");await Task.Delay(5);}}
        void Invoke(Button button)=>((IInvokeProvider)UIElementAutomationPeer.CreatePeerForElement(button)!.GetPattern(PatternInterface.Invoke)).Invoke();
        async Task Menu(MenuItem item)
        {
            window.TaskContextMenu.DataContext=model;window.TaskContextMenu.PlacementTarget=window.ArchiveTasks;
            window.TaskContextMenu.IsOpen=true;await Layout();
            try {Check("reachable "+item.Name+" "+checks.Count,item.IsEnabled && DesktopMenuValidation.Reachable(item));((IInvokeProvider)UIElementAutomationPeer.CreatePeerForElement(item)!.GetPattern(PatternInterface.Invoke)).Invoke();await Layout();}
            finally {window.TaskContextMenu.IsOpen=false;}
        }
        void Capture(string name)
        {
            window.UpdateLayout();var client=(FrameworkElement)window.Content;
            Check(name+" exact client viewport",Math.Abs(client.ActualWidth-window.Width)<1 && Math.Abs(client.ActualHeight-window.Height)<1);
            window.Opacity=1;DesktopAccessibilityValidation.Render(window,Path.Combine(folder,name+".png"),1,clientOnly:true);window.Opacity=0;
        }
        void Size(double width,double height) { window.MinWidth=width;window.Width=width;window.Height=height; }
        bool Fits(FrameworkElement element)
        {var rect=element.TransformToAncestor(window).TransformBounds(new Rect(element.RenderSize));return element.IsVisible && rect.Left>=0 && rect.Top>=0 && rect.Right<=window.ActualWidth+1 && rect.Bottom<=window.ActualHeight+1;}
        var vault=new DpapiPasswordVault(Path.Combine(data,"passwords.apv"));
        var original=new SavedPassword(Guid.NewGuid(),language=="en"?"Archive passwords":"常用归档密码","synthetic-backup-only","",true,DateTimeOffset.UtcNow);
        var cancelExport=false; var failExport=false; var passwordPrompts=0;
        var backup=Path.Combine(data,"export.nvault");
        try
        {
            await vault.SaveAsync(original);
            var dialogs=new WindowDialogs(window,prepareDialog:dialog=>
            {
                passwordPrompts++;dialog.Loaded+=(_,_)=>dialog.Dispatcher.BeginInvoke(()=>
                {
                    ((PasswordBox)dialog.FindName("SecretBox")).Password="测试密碼 ' $ & 空格";
                    ((CheckBox)dialog.FindName("SavePassword")).IsChecked=false;
                    Invoke((Button)dialog.FindName("ContinueButton"));
                });
            },selectVaultBackup:_=>failExport?data:backup,prepareVaultBackup:dialog=>
            {
                dialog.Loaded+=(_,_)=>dialog.Dispatcher.BeginInvoke(()=>
                {
                    if(cancelExport){dialog.Close();return;}
                    ((PasswordBox)dialog.FindName("SecretBox")).Password="Synthetic backup passphrase 123";
                    ((PasswordBox)dialog.FindName("ConfirmationBox")).Password="Synthetic backup passphrase 123";
                    Invoke((Button)dialog.FindName("ContinueButton"));
                });
            });
            model=new MainViewModel(baseDirectory,data,vault,dialogs);window.DataContext=model;await model.InitializeAsync();model.ConflictPolicy=2;
            Check("existing vault without history receives reminder",model.VaultBackupReminderNeeded);
            model.Page=1;await Layout();Invoke(window.VaultBackupReminderAction);await Layout();await model.WaitForIdleAsync();
            Check("successful export records protected backup status",File.Exists(backup) && !model.VaultBackupReminderNeeded && File.Exists(Path.Combine(data,"vault-backup-history.apb")));
            await vault.SaveAsync(original with {Id=Guid.NewGuid(),Name=language=="en"?"Project backup passwords":"项目备份密码"});await model.InitializeAsync();
            Check("new record triggers reminder",model.VaultBackupReminderNeeded && model.VaultBackupSummary.Contains("1"));
            var history=await File.ReadAllBytesAsync(Path.Combine(data,"vault-backup-history.apb"));cancelExport=true;
            Invoke(window.VaultBackupReminderAction);await Layout();await model.WaitForIdleAsync();
            var afterCancelledExport=await File.ReadAllBytesAsync(Path.Combine(data,"vault-backup-history.apb"));
            Check("cancelled export preserves backup history",history.SequenceEqual(afterCancelledExport) && model.VaultBackupReminderNeeded);
            cancelExport=false;failExport=true;Invoke(window.VaultBackupReminderAction);await Layout();await model.WaitForIdleAsync();
            var afterFailedExport=await File.ReadAllBytesAsync(Path.Combine(data,"vault-backup-history.apb"));
            Check("failed export preserves backup history",history.SequenceEqual(afterFailedExport) && model.VaultBackupReminderNeeded);failExport=false;
            model.Banner="";model.SelectedPassword=model.PasswordRows.First();
            foreach(var mode in new[]{"light","dark"})foreach(var size in new[]{(1152d,768d),(800d,600d)})
            {
                theme.SetMode(mode);theme.Apply(false);Size(size.Item1,size.Item2);await Layout();
                Check($"backup reminder fits {mode} {size.Item1}",Fits(window.VaultBackupStatus) && Fits(window.VaultBackupReminderAction));
                Capture($"vault-{mode}-{size.Item1}");
                model.Page=2;model.SettingsSection=4;await Layout();window.CleanApplicationInstallersButton.BringIntoView();await Layout();
                Check($"cache cleanup fits {mode} {size.Item1}",Fits(window.CleanApplicationInstallersButton) && window.CleanApplicationInstallersButton.IsEnabled);
                Capture($"updates-{mode}-{size.Item1}");model.Page=1;
            }
            Size(1152,768);theme.SetMode("light");theme.Apply(false);
            var cache=Path.Combine(data,"application-updates");Directory.CreateDirectory(cache);
            var stale=Path.Combine(cache,new string('a',32)+".exe");await File.WriteAllBytesAsync(stale,new byte[100]);
            model.Page=2;model.SettingsSection=4;await Layout();Invoke(window.CleanApplicationInstallersButton);await Layout();await model.WaitForIdleAsync();
            Check("actual cleanup entry removes stale installer",!File.Exists(stale) && model.ApplicationUpdateMessage.Contains("1"));Capture("updates-cleaned");

            model.Page=0;model.AutoTry=false;model.QueueConcurrency=2;model.OutputPath=Path.Combine(data,"output");
            var fixture=Path.Combine(root,"tests/fixtures/generated/large-ui.7z");
            var inputs=Enumerable.Range(0,3).Select(index=>Path.Combine(data,$"task-{index}.7z")).ToArray();foreach(var input in inputs)File.Copy(fixture,input);
            model.AddFiles(inputs);var rows=model.Tasks.ToArray();for(var index=0;index<rows.Length;index++)rows[index].ShellOutputPath=Path.Combine(data,"output-"+index);
            await Layout();Invoke(window.ExtractAction);await Until(()=>rows.Take(2).All(row=>row.Running && row.Percent>0));
            model.SelectedTask=rows[0];await Menu(window.TaskPauseAction);await Until(()=>rows[0].Paused);
            var pausedPercent=rows[0].Percent;await Until(()=>rows[1].Completed);
            Check("single pause leaves sibling progressing",rows[0].Running && !rows[0].Completed && rows[0].Percent==pausedPercent && rows[1].Completed);
            model.ToggleQueuePauseCommand.Execute(null);Check("global pause disables local resume",!model.ToggleSelectedTaskPauseCommand.CanExecute(null));
            model.ToggleQueuePauseCommand.Execute(null);Check("global resume retains local pause",rows[0].PauseRequested);
            await Layout();Capture("single-task-paused");await Menu(window.TaskCancelAction);await model.WaitForIdleAsync().WaitAsync(TimeSpan.FromSeconds(90));
            Check("single cancel keeps other tasks and queued task successful",!rows[0].Completed && rows[0].Failure?.Status=="cancelled" && rows.Skip(1).All(row=>row.Completed));
            Check("single cancellation clears running and pause state",rows.All(row=>!row.Running && !row.PauseRequested));

            model.Tasks.Clear();model.AddFiles([inputs[0]]);var resumed=model.Tasks[0];resumed.ShellOutputPath=Path.Combine(data,"resumed");
            await Layout();Invoke(window.ExtractAction);await Until(()=>resumed.Running && resumed.Percent>0);model.SelectedTask=resumed;
            await Menu(window.TaskPauseAction);await Until(()=>resumed.Paused);await Menu(window.TaskPauseAction);await model.WaitForIdleAsync().WaitAsync(TimeSpan.FromSeconds(90));
            Check("actual single resume finishes original task",resumed.Completed && File.Exists(Path.Combine(resumed.ResultOutputPath!,"large.bin")));

            model.Tasks.Clear();var retry=Path.Combine(data,"retry.7z");
            File.WriteAllText(retry,"placeholder");model.AddFiles([retry]);File.Delete(retry);
            await Layout();Invoke(window.ExtractAction);await Layout();await model.WaitForIdleAsync().WaitAsync(TimeSpan.FromSeconds(30));
            var failed=model.Tasks.Single();model.SelectedTask=failed;
            Check("read failure exposes retry",failed.Failure is not null && model.RetrySelectedTaskCommand.CanExecute(null));
            File.Copy(Path.Combine(root,"tests/fixtures/generated/plain.7z"),retry);await Menu(window.TaskRetryFailedAction);await model.WaitForIdleAsync().WaitAsync(TimeSpan.FromSeconds(30));
            Check("retry failed processes only failed extraction",failed.Completed && !model.RetryFailedTasksCommand.CanExecute(null));
            // Simulate another known failure, then exercise the explicit manual-password path with a real encrypted archive.
            failed.Completed=false;failed.Failure=new EngineResult {Code="wrong_password"};
            File.Copy(Path.Combine(root,"tests/fixtures/generated/encrypted-header.7z"),retry,true);
            model.AutoTry=true;var beforePrompts=passwordPrompts;await Menu(window.TaskRetryPasswordAction);await model.WaitForIdleAsync().WaitAsync(TimeSpan.FromSeconds(30));
            Check("manual retry uses password prompt and completes",failed.Completed && passwordPrompts>beforePrompts);
            Check("unchecked manual password is not persisted",(await vault.ReadAsync()).Count==2);
            await Layout();Capture("retry-completed");
        }
        catch(Exception exception){error=exception.ToString();}
        finally
        {
            if(model?.IsBusy==true){model.CancelCommand.Execute(null);try{await model.WaitForIdleAsync().WaitAsync(TimeSpan.FromSeconds(20));}catch(TimeoutException){}}
        }
        var report=new {passed=error is null && checks.Values.All(value=>value),language,architecture=EngineArchitecture.Current,checks,error};
        await File.WriteAllTextAsync(Path.Combine(root,$"artifacts/task-improvements-ui-{language}-validation.json"),JsonSerializer.Serialize(report,new JsonSerializerOptions {WriteIndented=true}));
        return report.passed;
    }
}
