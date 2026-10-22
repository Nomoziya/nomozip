using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Threading;
using ArchivePassword.Core;
using ArchivePassword.Infrastructure;

namespace ArchivePassword.Desktop;

internal static class DesktopCompressionRetryValidation
{
    internal static async Task RunAsync(MainWindow window,ThemeResources theme,string root,string data,string baseDirectory,
        Action<string,bool> check,Action<Window,string> capture)
    {
        data=Path.GetFullPath(Path.Combine(data,"compression-retry"));Directory.CreateDirectory(data);var prompts=0;
        Task Layout()=>window.Dispatcher.InvokeAsync(window.UpdateLayout,DispatcherPriority.ApplicationIdle).Task;
        void Invoke(Button button)=>((IInvokeProvider)UIElementAutomationPeer.CreatePeerForElement(button)!.GetPattern(PatternInterface.Invoke)).Invoke();
        async Task<MainViewModel> Model(string name)
        {
            var path=Path.Combine(data,name);Directory.CreateDirectory(path);
            var dialogs=new WindowDialogs(window,prepareDialog:dialog=>
            {
                dialog.Opacity=0;dialog.ShowActivated=false;
                dialog.Loaded+=(_,_)=>dialog.Dispatcher.BeginInvoke(()=>
                {prompts++;dialog.SecretBox.Password="Retry-test-123";dialog.ConfirmationBox.Password=dialog.SecretBox.Password;dialog.SavePassword.IsChecked=false;dialog.ContinueButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));});
            });
            var model=new MainViewModel(baseDirectory,path,new DpapiPasswordVault(Path.Combine(path,"synthetic.apv")),dialogs);
            await model.InitializeAsync();model.SelectedEngineVersion="26.04";model.ApplyEngineCommand.Execute(null);await model.WaitForIdleAsync();
            model.Page=3;model.CompressionFormat=1;model.CompressionOutput=Path.Combine(path,"output.zip");window.DataContext=model;await Layout();return model;
        }
        async Task Create(MainViewModel model)
        {model.Page=3;await Layout();Invoke(window.CompressionPage.CreateButton);await Layout();await model.WaitForIdleAsync();await Layout();}
        var model=await Model("single");var input=Path.Combine(data,"single-input.txt");await File.WriteAllTextAsync(input,"first");
        model.AddCompressionSources([input]);model.CompressionExcludedExtensions="tmp";File.Delete(input);await Create(model);
        var task=model.WorkspaceTasks.Single();
        check("retry starts from a real failed compression",task.Failure is not null && !task.Completed && task.CompressionRetry is {ExcludedExtensions:"tmp"});
        model.SelectedTask=task;model.EditCompressionRetryCommand.Execute(null);await Layout();
        capture(window,"next-retry-edit-loaded");
        check($"retry edit loads missing sources and original parameters without starting (page={model.Page}, sources={model.CompressionSources.Count}, sourceMatches={model.CompressionSources.FirstOrDefault()?.Path==input}, exists={File.Exists(input)}, format={model.CompressionFormat}, running={model.CompressionRunning})",model.Page==3 && model.CompressionSources.Count==1 && model.CompressionSources[0].Path==input && !File.Exists(input) && model.CompressionFormat==1 && !model.CompressionRunning);
        var originalOutput=task.Path;await File.WriteAllTextAsync(originalOutput,"existing output");await File.WriteAllTextAsync(input,"source changed before retry");
        var draft=Path.Combine(data,"draft.txt");await File.WriteAllTextAsync(draft,"draft");var draftOutput=Path.Combine(data,"draft.7z");
        model.CompressionSources.Clear();model.AddCompressionSources([draft]);model.CompressionFormat=0;model.CompressionOutput=draftOutput;model.CompressionExcludedExtensions="log";
        model.Page=0;model.SelectedTask=task;model.TaskDetailsExpanded=true;await Layout();window.TaskRetryDetailsButton.BringIntoView();await Layout();
        check("retry is available through task detail action",window.TaskRetryDetailsButton.IsVisible && window.TaskRetryDetailsButton.IsEnabled && window.TaskEditRetryDetailsButton.IsEnabled);
        foreach(var mode in new[]{"light","dark","contrast"})foreach(var width in new[]{1152d,800d})
        {
            theme.SetMode(mode=="dark"?"dark":"light");theme.Apply(mode=="contrast");window.MinWidth=width;window.MinHeight=width==800?600:768;window.Width=width;window.Height=width==800?600:768;await Layout();
            window.TaskStatusDetails.BringIntoView();await Layout();
            check($"retry detail error is fully laid out once {mode} {width}",DesktopReadabilityValidation.CompleteText(window.TaskStatusDetails)
                && DesktopReadabilityValidation.Descendants(window.TaskDetailsExpander).OfType<TextBlock>().Count(text=>text.Text==task.Name)==1);
            window.TaskRetryDetailsButton.BringIntoView();await Layout();
            var buttonBounds=window.TaskRetryDetailsButton.TransformToAncestor(window).TransformBounds(new Rect(window.TaskRetryDetailsButton.RenderSize));
            check($"retry detail actions remain reachable {mode} {width}",buttonBounds.Bottom<window.ActualHeight && buttonBounds.Right<window.ActualWidth && window.TaskEditRetryDetailsButton.IsVisible);
            capture(window,$"refined-retry-task-details-{mode}-{width}");
        }
        theme.SetMode("light");theme.Apply(false);window.MinWidth=1152;window.MinHeight=768;window.Width=1152;window.Height=768;await Layout();
        capture(window,"next-retry-task-details");Invoke(window.TaskRetryDetailsButton);await Layout();await model.WaitForIdleAsync();await Layout();
        check("retry reuses the failed row and preserves prior output",task.Completed && task.Failure is null && model.WorkspaceTasks.Count==1 && task.Path!=originalOutput && await File.ReadAllTextAsync(originalOutput)=="existing output");
        check("retry leaves compression draft inputs unchanged",model.CompressionSources.Single().Path==draft && model.CompressionOutput==draftOutput && model.CompressionFormat==0 && model.CompressionExcludedExtensions=="log" && !File.Exists(draftOutput));
        var client=new ArchiveEngineClient(Path.Combine(root,"artifacts/worker/ArchiveEngineWorker.exe"),Path.Combine(root,"engines/26.04/7z.dll"));
        var extracted=Path.Combine(data,"retry-extract");var result=await client.ExtractAsync(task.Path,extracted);
        check("retry rescans changed source bytes using original ZIP format",result.IsSuccess && task.Path.EndsWith(".zip") && await File.ReadAllTextAsync(Path.Combine(extracted,Path.GetFileName(input)))=="source changed before retry");

        model=await Model("bulk");var first=Path.Combine(data,"good.txt");var second=Path.Combine(data,"failed.txt");
        await File.WriteAllTextAsync(first,"preserve success");await File.WriteAllTextAsync(second,"missing");model.AddCompressionSources([first,second]);model.CompressionSeparate=true;File.Delete(second);await Create(model);
        var good=model.WorkspaceTasks.Single(row=>row.Completed);var bad=model.WorkspaceTasks.Single(row=>row.Failure is not null);var before=SHA256.HashData(await File.ReadAllBytesAsync(good.Path));
        await File.WriteAllTextAsync(second,"recovered");model.RetryFailedTasksCommand.Execute(null);await Layout();await model.WaitForIdleAsync();await Layout();
        var after=SHA256.HashData(await File.ReadAllBytesAsync(good.Path));
        check("retry-all only recreates failed separate outputs",model.WorkspaceTasks.Count==2 && model.WorkspaceTasks.All(row=>row.Completed) && before.SequenceEqual(after) && bad.CompressionRetry!.Sources.SequenceEqual([second]));

        model=await Model("encrypted");var encryptedInputs=new[]{Path.Combine(data,"secret-one.txt"),Path.Combine(data,"secret-two.txt")};
        foreach(var file in encryptedInputs)await File.WriteAllTextAsync(file,"before");model.AddCompressionSources(encryptedInputs);model.CompressionFormat=0;model.CompressionEncrypt=true;model.CompressionSeparate=true;
        foreach(var file in encryptedInputs)File.Delete(file);var initialPrompts=prompts;await Create(model);
        check("retry encrypted fixture fails both sources after one password prompt",prompts==initialPrompts+1 && model.WorkspaceTasks.Count==2 && model.WorkspaceTasks.All(row=>row.Failure is not null));
        foreach(var file in encryptedInputs)await File.WriteAllTextAsync(file,"recovered encrypted");
        model.RetryFailedTasksCommand.Execute(null);await Layout();await model.WaitForIdleAsync();await Layout();
        check("retry encrypted batch asks again once without retaining a password",prompts==initialPrompts+2 && model.WorkspaceTasks.All(row=>row.Completed) && (await client.TestAsync(model.WorkspaceTasks[0].Path,"Retry-test-123")).IsSuccess);

        model=await Model("cancel");var cancelInputs=Enumerable.Range(0,3).Select(index=>Path.Combine(data,$"cancel-{index}.txt")).ToArray();
        foreach(var file in cancelInputs)await File.WriteAllTextAsync(file,"before");model.AddCompressionSources(cancelInputs);model.CompressionSeparate=true;foreach(var file in cancelInputs)File.Delete(file);await Create(model);
        foreach(var file in cancelInputs)await File.WriteAllTextAsync(file,"after");var firstRetry=model.WorkspaceTasks[0];
        firstRetry.PropertyChanged+=(_,args)=>{if(args.PropertyName==nameof(ArchiveTaskRow.Running) && firstRetry.Running)model.CancelCommand.Execute(null);};
        model.RetryFailedTasksCommand.Execute(null);await Layout();await model.WaitForIdleAsync();await Layout();
        check("retry cancellation stops remaining failed tasks",firstRetry.Failure?.Status=="cancelled" && model.WorkspaceTasks.Skip(1).All(row=>row.Failure is {Status:not "cancelled"} && !row.Running) && model.WorkspaceTasks.All(row=>!File.Exists(row.Path)));
    }
}
