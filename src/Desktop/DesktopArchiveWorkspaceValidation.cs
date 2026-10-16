using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using ArchivePassword.Core;
using ArchivePassword.Infrastructure;

namespace ArchivePassword.Desktop;

internal static class DesktopArchiveWorkspaceValidation
{
    internal static async Task RunAsync(MainWindow window,ThemeResources theme,string folder,Action<string,bool> check)
    {
        if(Environment.GetEnvironmentVariable("GITHUB_ACTIONS")!="true") throw new InvalidOperationException("Isolated CI only.");
        var data=Path.Combine(folder,"synthetic","archive-workspace");Directory.CreateDirectory(data);
        var archive=Path.Combine(data,"editable.zip");
        var original=Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes("原始文本\r\noriginal\r\n")).ToArray();
        using(var zip=ZipFile.Open(archive,ZipArchiveMode.Create))
        {
            using(var stream=zip.CreateEntry("说明.txt").Open())stream.Write(original);
            using(var writer=new StreamWriter(zip.CreateEntry("keep.txt").Open()))writer.Write("keep original");
        }
        var before=File.ReadAllBytes(archive);var client=new ArchiveEngineClient(Path.Combine(AppContext.BaseDirectory,"ArchiveEngineWorker.exe"),Path.Combine(AppContext.BaseDirectory,"engines","26.04","7z.dll"));
        var vault=new DpapiPasswordVault(Path.Combine(data,"vault.apv"));var candidates=new PasswordCandidateProvider(vault);
        Task<PasswordPromptResponse?> NoPassword(EngineResult _)=>Task.FromResult<PasswordPromptResponse?>(null);
        async Task Layout()=>await window.Dispatcher.InvokeAsync(window.UpdateLayout,DispatcherPriority.ApplicationIdle);
        async Task Until(Func<bool> predicate)
        {
            var deadline=DateTime.UtcNow.AddSeconds(40);
            while(!predicate()) {if(DateTime.UtcNow>deadline)throw new TimeoutException("Archive workspace transition timed out.");await Task.Delay(25);await Layout();}
        }
        var scenario="cancel";
        void Prepare(Window dialog,string name,Action finish)
        {
            dialog.WindowStartupLocation=WindowStartupLocation.Manual;dialog.Left=dialog.Top=-32000;dialog.Opacity=0;dialog.ShowActivated=false;
            dialog.Loaded+=(_,_)=>dialog.Dispatcher.BeginInvoke(() =>
            {
                foreach(var mode in new[]{"light","dark"})
                {
                    theme.SetMode(mode);dialog.UpdateLayout();dialog.Opacity=1;
                    DesktopAccessibilityValidation.Render(dialog,Path.Combine(folder,$"{name}-{scenario}-{mode}.png"),1,clientOnly:true);dialog.Opacity=0;
                }
                theme.SetMode("light");finish();
            });
        }
        var dialogs=new WindowDialogs(window,prepareTextEdit:dialog=>Prepare(dialog,"text-edit",()=>
        {
            check(scenario+" editor starts with decoded original text",dialog.Editor.Text=="原始文本\r\noriginal\r\n");
            check(scenario+" unchanged text cannot be written back",!dialog.WriteBackButton.IsEnabled);
            dialog.Editor.Text+="追加 changed\r\n";
            if(scenario=="cancel")dialog.Close();else dialog.WriteBackButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }),prepareEditConfirm:dialog=>Prepare(dialog,dialog.IsWriteBack?"write-back":"edit-copy",()=>
        {
            if(dialog.IsWriteBack && scenario=="discard-confirmation")dialog.Close();
            else dialog.ContinueButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }),prepareConflict:dialog=>Prepare(dialog,"file-conflict",()=>
        {
            dialog.ApplyAll.IsChecked=true;
            dialog.RenameButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            dialog.UpdateLayout();dialog.Opacity=1;DesktopAccessibilityValidation.Render(dialog,Path.Combine(folder,"file-conflict-rename.png"),1,clientOnly:true);dialog.Opacity=0;
            dialog.RenameButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }));
        var model=new MainViewModel(AppContext.BaseDirectory,Path.Combine(data,"model"),vault,dialogs);window.DataContext=model;await model.InitializeAsync();
        var opening=model.OpenWorkspaceArchiveAsync([archive]);await Until(()=>window.ArchiveHost.Content is FrameworkElement);
        var contents=(ArchiveContentsViewModel)((FrameworkElement)window.ArchiveHost.Content).DataContext;
        foreach(var name in new[]{"cancel","discard-confirmation","write"})
        {
            scenario=name;contents.CurrentRow=contents.Rows.Single(row=>row.Path=="说明.txt");
            check(name+" text editing available from actual archive workspace",contents.EditTextCommand.CanExecute(null));
            contents.EditTextCommand.Execute(null);await Until(()=>!contents.IsWorking);
            if(name!="write")check(name+" preserves the original archive bytes",before.SequenceEqual(File.ReadAllBytes(archive)));
            else
            {
                var output=Path.Combine(data,"written");var result=await client.ExtractAsync(archive,output);
                var bytes=File.ReadAllBytes(Path.Combine(output,"说明.txt"));
                check("writeback preserves UTF16 BOM and line endings",result.IsSuccess && bytes.SequenceEqual(Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes("原始文本\r\noriginal\r\n追加 changed\r\n"))));
                check("writeback preserves unrelated entry",File.ReadAllText(Path.Combine(output,"keep.txt"))=="keep original");
                check("writeback retains byte-identical original backup",Directory.GetFiles(data,"*.bak").Any(path=>File.ReadAllBytes(path).SequenceEqual(before)));
            }
            check(name+" temporary edit copies are removed",!Directory.EnumerateFiles(Path.Combine(data,"model","edit-temp"),"*",SearchOption.AllDirectories).Any());
        }
        window.CloseArchiveButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await opening;

        // Detect a change with the same names and lengths, not just a different inventory.
        var snapshot=(await client.ListAsync(archive)).Items;var entry=snapshot.Single(value=>value.Path=="说明.txt");
        var service=new ArchiveTextEditingService(client,client,candidates,Path.Combine(data,"temporary"));
        var start=await service.BeginAsync(archive,entry,false,NoPassword);
        using(var session=start.Session!)
        {
            var replacement=Path.Combine(data,"changed.txt");await File.WriteAllBytesAsync(replacement,Enumerable.Repeat((byte)'x',(int)entry.Size).ToArray());
            var changed=await new ArchiveEditingService(client,client,candidates).EditAsync(archive,snapshot,[entry.Index],[new(replacement,entry.Path,false)],false,NoPassword);
            var changedBytes=File.ReadAllBytes(archive);
            var rejected=await session.WriteBackAsync("stale edit",snapshot,false,NoPassword);
            check("concurrent archive changes reject stale edit without overwriting",changed.Engine.IsSuccess && rejected.Engine.Code=="selection_changed" && changedBytes.SequenceEqual(File.ReadAllBytes(archive)));
        }

        var outputRoot=Path.Combine(data,"renamed");Directory.CreateDirectory(outputRoot);
        foreach(var item in (await client.ListAsync(archive)).Items)File.WriteAllText(Path.Combine(outputRoot,item.Path),"existing original");
        scenario="rename";var prompts=0;
        var extracted=await new ExtractionService(client,vault,candidates).ExtractAsync(archive,outputRoot,false,NoPassword,_=>Task.FromResult(ExistingFilePolicy.Ask),
            resolveFileConflict:conflict=>{prompts++;return Task.FromResult(dialogs.ResolveFileConflict(conflict));});
        check("rename applies to subsequent conflicts only within this task",extracted.Engine.IsSuccess && prompts==1 && Directory.GetFiles(outputRoot).Length==4);
        check("rename preserves both existing files",File.ReadAllText(Path.Combine(outputRoot,"说明.txt"))=="existing original" && File.ReadAllText(Path.Combine(outputRoot,"keep.txt"))=="existing original");
        var cancelRoot=Path.Combine(data,"cancel-conflict");Directory.CreateDirectory(cancelRoot);File.WriteAllText(Path.Combine(cancelRoot,"keep.txt"),"keep");
        var cancelled=await new ExtractionService(client,vault,candidates).ExtractAsync(archive,cancelRoot,false,NoPassword,_=>Task.FromResult(ExistingFilePolicy.Ask),resolveFileConflict:_=>Task.FromResult(new ExistingFileDecision(ExistingFilePolicy.Ask)));
        check("cancel conflict commits no staged files",cancelled.Engine.Status=="cancelled" && Directory.GetFiles(cancelRoot).Length==1 && File.ReadAllText(Path.Combine(cancelRoot,"keep.txt"))=="keep");
        var collision=await new ExtractionService(client,vault,candidates).ExtractAsync(archive,cancelRoot,false,NoPassword,_=>Task.FromResult(ExistingFilePolicy.Ask),resolveFileConflict:_=>Task.FromResult(new ExistingFileDecision(ExistingFilePolicy.Rename,"说明.txt")));
        check("rename cannot overwrite another file in the same archive",!collision.Engine.IsSuccess && Directory.GetFiles(cancelRoot).Length==1);
        check("invalid rename paths are rejected",!ConflictFileNames.IsValid("../escaped.txt") && !ConflictFileNames.IsValid("CON.txt") && !ConflictFileNames.IsValid("tail. "));

        var checkpointRoot=Path.Combine(data,"checkpoint-output");Directory.CreateDirectory(checkpointRoot);
        foreach(var item in (await client.ListAsync(archive)).Items)File.WriteAllText(Path.Combine(checkpointRoot,item.Path),"existing checkpoint file");
        var store=new ExtractionCheckpointStore(Path.Combine(data,"checkpoints"));
        var dll=Path.Combine(AppContext.BaseDirectory,"engines","26.04","7z.dll");
        var binding=new CheckpointBinding("26.04",Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(dll))).ToLowerInvariant(),System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture==System.Runtime.InteropServices.Architecture.Arm64?"arm64":"x64",0);
        using var cancel=new CancellationTokenSource();
        var interrupted=await new ExtractionService(client,vault,candidates).ExtractAsync(archive,checkpointRoot,false,NoPassword,_=>Task.FromResult(ExistingFilePolicy.Rename),
            new ForwardProgress<TaskUpdate>(value=>{if(value.State=="checkpoint-commit")cancel.Cancel();}),cancel.Token,checkpoint:new(store,binding));
        var prior=store.ReadPending().Single();
        check("checkpoint durably records renamed destination",interrupted.Engine.Status=="cancelled" && prior.Version==2 && prior.Receipts.Any(value=>value.State=="delivered" && value.TargetPath is not null));
        var resumed=await new ExtractionService(client,vault,candidates).ExtractAsync(archive,checkpointRoot,false,NoPassword,_=>Task.FromResult(ExistingFilePolicy.Rename),checkpoint:new(store,binding,prior.Id));
        check("checkpoint resumes rename without duplicate delivery",resumed.Engine.IsSuccess && Directory.GetFiles(checkpointRoot).Length==4 && store.ReadPending().Count==0);
        check("checkpoint rename preserves existing originals",File.ReadAllText(Path.Combine(checkpointRoot,"keep.txt"))=="existing checkpoint file");
    }
}
