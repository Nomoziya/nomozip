using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Threading;
using ArchivePassword.Core;
using ArchivePassword.Infrastructure;

namespace ArchivePassword.Desktop;
internal static class DesktopOriginValidation
{
    internal static async Task<bool> RunAsync(MainWindow window,string root,string baseDirectory)
    {
        var data=Path.GetFullPath(Path.Combine(root,"artifacts/origin-ui",Guid.NewGuid().ToString("N")));Directory.CreateDirectory(data);
        var checks=new Dictionary<string,bool>();void Check(string name,bool value)=>checks.Add(name,value);
        var archive=Path.Combine(data,"marked.zip");
        using(var zip=ZipFile.Open(archive,ZipArchiveMode.Create)) foreach(var name in new[] {"folder/中文.txt","b.txt"}) {using var writer=new StreamWriter(zip.CreateEntry(name).Open());writer.Write("actual content");}
        var mark=Encoding.UTF8.GetBytes("[ZoneTransfer]\r\nZoneId=3\r\nHostUrl=https://example.invalid/archive.zip\r\n");File.WriteAllBytes(archive+":Zone.Identifier",mark);var beforeArchive=File.ReadAllBytes(archive);
        var vaultPath=Path.Combine(data,"passwords.apv");var vault=new DpapiPasswordVault(vaultPath);await vault.SaveAsync(new(Guid.NewGuid(),"synthetic unused","unused-secret","",true,DateTimeOffset.UtcNow));var beforeVault=File.ReadAllBytes(vaultPath);
        var mode=0;
        try
        {
            var model=new MainViewModel(baseDirectory,data,vault,new WindowDialogs(window,prepareContents:dialog=>
            {
                dialog.ShowActivated=false;dialog.ShowInTaskbar=false;dialog.WindowStartupLocation=WindowStartupLocation.Manual;dialog.Left=-32000;dialog.Top=-32000;
                dialog.Loaded+=(_,_)=>dialog.Dispatcher.BeginInvoke(async()=>
                {
                    try
                    {
                        var view=(ArchiveContentsViewModel)dialog.DataContext;
                        if(mode==0) {Check("actual readonly browser lists without output",view.Rows.Count==2 && !Directory.Exists(((MainViewModel)window.DataContext).OutputPath));dialog.Close();return;}
                        view.Rows.Single(row=>row.Path=="b.txt").Selected=true;await Layout(dialog);Invoke((Button)dialog.FindName("ExtractSelectedButton"));await Layout(dialog);await view.WaitForIdleAsync();await Layout(dialog);
                        Check("actual browser selected extraction completes",view.OutputPath.Length>0 && view.OperationMessage.Contains("已解压"));
                        Check("actual selected file preserves exact origin",File.ReadAllBytes(Path.Combine(view.OutputPath,"b.txt")+":Zone.Identifier").SequenceEqual(mark));
                        Check("unselected files not output or marked",Directory.GetFiles(view.OutputPath,"*",SearchOption.AllDirectories).Length==1 && !Directory.Exists(Path.Combine(view.OutputPath,"folder")));
                        dialog.Close();
                    }
                    catch(Exception error) {Check("contents exception "+error.GetType().Name+" "+error.Message,false);dialog.Close();}
                });
            }));
            window.DataContext=model;await model.InitializeAsync();model.AddFiles([archive]);model.SelectedTask=model.Tasks.Single();model.SelectedTask.FilenameCodePage=936;
            model.OutputPath=Path.Combine(data,"readonly");model.AutoTry=false;model.BrowseArchiveCommand.Execute(null);await model.WaitForIdleAsync();await Layout(window);
            model.TestArchiveCommand.Execute(null);await model.WaitForIdleAsync();await Layout(window);Check("actual readonly testing creates no output",model.SelectedTask.VerificationResult?.IsSuccess==true && !Directory.Exists(model.OutputPath));
            Check("readonly operations preserve archive origin and vault",File.ReadAllBytes(archive).SequenceEqual(beforeArchive) && File.ReadAllBytes(archive+":Zone.Identifier").SequenceEqual(mark) && File.ReadAllBytes(vaultPath).SequenceEqual(beforeVault));
            model.OutputPath=Path.Combine(data,"selected");mode=1;model.BrowseArchiveCommand.Execute(null);await model.WaitForIdleAsync();await Layout(window);Check("partial extraction keeps whole task incomplete",!model.Tasks.Single().Completed && model.IsIdle);
            model.OutputPath=Path.Combine(data,"whole");model.ExtractCommand.Execute(null);await model.WaitForIdleAsync();await Layout(window);
            Check("actual main extraction completed",model.Tasks.Single().Completed && model.IsIdle);
            Check("whole output Unicode path preserved",File.ReadAllText(Path.Combine(model.OutputPath,"folder/中文.txt"))=="actual content");
            Check("whole main output carries exact source origin",File.ReadAllBytes(Path.Combine(model.OutputPath,"folder/中文.txt")+":Zone.Identifier").SequenceEqual(mark) && File.ReadAllBytes(Path.Combine(model.OutputPath,"b.txt")+":Zone.Identifier").SequenceEqual(mark));
            var local=Path.Combine(data,"local.zip");File.Copy(archive,local);File.Delete(local+":Zone.Identifier");model.Tasks.Clear();model.AddFiles([archive,local]);model.ApplyDirectoryRules(new(Layout:ExtractionLayout.ArchiveFolder),Path.Combine(data,"batch"));
            model.ExtractCommand.Execute(null);await model.WaitForIdleAsync();await Layout(window);Check("mixed marked and local batch all complete",model.Tasks.Count==2 && model.Tasks.All(task=>task.Completed));
            var markedTask=model.Tasks.Single(task=>task.Path==archive);var localTask=model.Tasks.Single(task=>task.Path==local);
            Check("batch marker stays specific to each archive",File.ReadAllBytes(Path.Combine(markedTask.ResultOutputPath!,"b.txt")+":Zone.Identifier").SequenceEqual(mark) && !File.Exists(Path.Combine(localTask.ResultOutputPath!,"b.txt")+":Zone.Identifier"));
            model.Tasks.Clear();model.AddFiles([archive]);model.SelectedTask=model.Tasks.Single();model.ApplyDirectoryRules(new(),Path.Combine(data,"blocked"));
            using(var locked=new FileStream(archive+":Zone.Identifier",FileMode.Open,FileAccess.ReadWrite,FileShare.None))
            {
                model.ExtractCommand.Execute(null);await model.WaitForIdleAsync();await Layout(window);
                Check("actual origin read error shown without false completion",!model.SelectedTask.Completed && model.SelectedTask.Failure?.Code=="origin_read_failed" && model.Banner.Contains("无法读取"));
                Check("failed metadata read creates no output or password writes",!Directory.Exists(model.OutputPath) && File.ReadAllBytes(vaultPath).SequenceEqual(beforeVault));
                Check("error UI does not expose original HostUrl",!model.Banner.Contains("example.invalid") && !model.Footer.Contains("example.invalid"));
                window.Width=800;window.Height=600;await Layout(window);var extract=window.FindName("ExtractAction") as Button;
                // Existing main primary control name is resolved by command if the layout changes.
                extract ??= Descendants(window).OfType<Button>().Single(button=>ReferenceEquals(button.Command,model.ExtractCommand));
                var bounds=extract.TransformToAncestor(window).TransformBounds(new Rect(extract.RenderSize));Check("error banner leaves primary action reachable at minimum size",extract.IsVisible && bounds.Top>=0 && bounds.Bottom<=window.ActualHeight);
            }
            Check("source archive and exact stream remain unchanged",File.ReadAllBytes(archive).SequenceEqual(beforeArchive) && File.ReadAllBytes(archive+":Zone.Identifier").SequenceEqual(mark));
            Check("all controlled tasks release parent busy state",model.IsIdle);
        }
        catch(Exception error) {Check("exception "+error.GetType().Name+" "+error.Message,false);}
        await File.WriteAllTextAsync(Path.Combine(root,"artifacts/origin-ui-validation.json"),JsonSerializer.Serialize(new {passed=checks.Count>0 && checks.Values.All(value=>value),total=checks.Count,checks,actualStreams=true,syntheticFiles=true},new JsonSerializerOptions {WriteIndented=true}));return checks.Count>0 && checks.Values.All(value=>value);
    }
    private static void Invoke(Button button)=>((IInvokeProvider)UIElementAutomationPeer.CreatePeerForElement(button)!.GetPattern(PatternInterface.Invoke)).Invoke();
    private static async Task Layout(Window window)=>await window.Dispatcher.InvokeAsync(window.UpdateLayout,DispatcherPriority.ApplicationIdle);
    private static IEnumerable<System.Windows.DependencyObject> Descendants(System.Windows.DependencyObject parent) {for(var index=0;index<System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent);index++) {var child=System.Windows.Media.VisualTreeHelper.GetChild(parent,index);yield return child;foreach(var descendant in Descendants(child)) yield return descendant;}}
}
