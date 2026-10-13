using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ArchivePassword.Core;
using ArchivePassword.Infrastructure;

namespace ArchivePassword.Desktop;
internal static class DesktopSelectionValidation
{
    internal static async Task<bool> RunAsync(MainWindow window,ThemeResources theme,string root,string baseDirectory)
    {
        var data=Path.GetFullPath(Path.Combine(root,"artifacts/selection-ui",Guid.NewGuid().ToString("N")));Directory.CreateDirectory(data);
        var checks=new Dictionary<string,bool>();var geometry=new Dictionary<string,double[]>();void Check(string name,bool passed)=>checks[name]=passed;
        void Invoke(Button button)=>((IInvokeProvider)UIElementAutomationPeer.CreatePeerForElement(button)!.GetPattern(PatternInterface.Invoke)).Invoke();
        var archive=Path.Combine(data,"content.zip");using(var zip=ZipFile.Open(archive,ZipArchiveMode.Create)) {foreach(var name in new[] {"folder/","folder/a.txt","folder/sub/b.txt","outside.txt"}) {var entry=zip.CreateEntry(name);if(!name.EndsWith('/')) {using var writer=new StreamWriter(entry.Open());writer.Write(name);}}}
        var vault=new DpapiPasswordVault(Path.Combine(data,"passwords.apv"));await vault.SaveAsync(new(Guid.NewGuid(),"synthetic","unused","",true,DateTimeOffset.UtcNow));var before=File.ReadAllBytes(Path.Combine(data,"passwords.apv"));
        var mode=0;var dialogsShown=0;var opened=new List<string>();MainViewModel? model=null;
        try
        {
            var dialogs=new WindowDialogs(window,prepareDialog:password=>password.Loaded+=(_,_)=>password.Dispatcher.BeginInvoke(()=>
            {
                ((PasswordBox)password.FindName("SecretBox")).Password="password2";((CheckBox)password.FindName("SavePassword")).IsChecked=true;Invoke((Button)password.FindName("ContinueButton"));
            }),prepareContents:dialog=>
            {
                dialog.WindowStartupLocation=WindowStartupLocation.Manual;dialog.Left=-32000;dialog.Top=-32000;dialog.ShowActivated=false;
                dialog.Loaded+=(_,_)=>dialog.Dispatcher.BeginInvoke((Action)(async()=>
                {
                    dialogsShown++;
                    var view=(ArchiveContentsViewModel)dialog.DataContext;var table=(DataGrid)dialog.FindName("EntryTable");
                    async Task Layout()=>await dialog.Dispatcher.InvokeAsync(dialog.UpdateLayout,DispatcherPriority.ApplicationIdle);
                    try
                    {
                        if(mode==0)
                        {
                            foreach(var contrast in new[] {false,true})
                            foreach(var size in new[] {(900.0,600.0),(640.0,400.0)})
                            {
                                theme.Apply(contrast);dialog.Width=size.Item1;dialog.Height=size.Item2;await Layout();
                                var folder=view.Rows.Single(row=>row.Path.TrimEnd('/')=="folder");table.ScrollIntoView(folder);await Layout();
                                var row=(DataGridRow)table.ItemContainerGenerator.ContainerFromItem(folder);var check=Find<CheckBox>(row)!;
                                var peer=UIElementAutomationPeer.CreatePeerForElement(check)!;((IToggleProvider)peer.GetPattern(PatternInterface.Toggle)).Toggle();await Layout();
                                Check($"real checkbox works {size.Item1} {contrast}",folder.Selected && view.SelectedEntries.Count==3 && peer.IsKeyboardFocusable() && peer.GetName()==folder.Path);
                                var bounds=row.TransformToAncestor(table).TransformBounds(new Rect(row.RenderSize));geometry[$"{size.Item1} {contrast}"]=[bounds.Top,bounds.Bottom,table.ActualHeight];Check($"row fits content viewport {size.Item1} {contrast}",bounds.Top>=0 && bounds.Bottom<=table.ActualHeight);
                                view.Search="outside";await Layout();Check($"hidden selections retained {size.Item1} {contrast}",view.Rows.Count==1 && view.SelectedEntries.Count==3 && view.SelectionSummary.Contains("隐藏"));
                                view.Search="";Invoke((Button)dialog.FindName("ClearSelectionButton"));await Layout();Check($"clear disables extraction {size.Item1} {contrast}",!view.ExtractSelectedCommand.CanExecute(null));
                                DesktopAccessibilityValidation.Render(dialog,Path.Combine(root,$"artifacts/selection-{size.Item1}-{contrast}.png"),1.0);
                            }
                            var nested=view.Rows.Single(row=>row.Path.Replace('\\','/')=="folder/sub/b.txt");nested.Selected=true;await Layout();
                            Invoke((Button)dialog.FindName("ExtractSelectedButton"));await Layout();await view.WaitForIdleAsync();await Layout();
                            Check("actual selection writes only selected descendant",Directory.GetFiles(model!.OutputPath,"*",SearchOption.AllDirectories).Length==1 && File.ReadAllText(Path.Combine(model.OutputPath,"folder/sub/b.txt"))=="folder/sub/b.txt");
                            Check("partial extraction never marks whole archive completed",!model!.Tasks.Single().Completed && model.Tasks.Single().SelectedOutputPath==model.OutputPath && model.Tasks.Single().Status=="所选内容已解压");
                            view.OpenResultCommand.Execute(null);Check("selected result folder opened once",opened.Count==1 && opened[0]==model!.OutputPath);
                            Check("plaintext selection preserves password library",before.SequenceEqual(File.ReadAllBytes(Path.Combine(data,"passwords.apv"))));
                            using(var changed=ZipFile.Open(archive,ZipArchiveMode.Update)) {foreach(var entry in changed.Entries.ToArray()) entry.Delete();using var writer=new StreamWriter(changed.CreateEntry("replacement.txt").Open());writer.Write("changed");}
                            Invoke((Button)dialog.FindName("ExtractSelectedButton"));await Layout();await view.WaitForIdleAsync();await Layout();Check("stale browser asks to refresh instead of writing changed entries",view.OutputPath.Length==0 && view.OperationMessage.Contains("重新查看") && Directory.GetFiles(model!.OutputPath,"*",SearchOption.AllDirectories).Length==1);
                        }
                        else if(mode==1)
                        {
                            view.SelectVisibleCommand.Execute(null);view.ExtractSelectedCommand.Execute(null);await Layout();dialog.Close();await view.WaitForIdleAsync();
                            Check("closing during selected work cancels and waits",!view.IsWorking && !model!.Tasks.Single().Completed);
                        }
                        else
                        {
                            view.Rows.Last(row=>row.Entry.Encrypted).Selected=true;await Layout();Invoke((Button)dialog.FindName("ExtractSelectedButton"));await Layout();await view.WaitForIdleAsync();await Layout();
                            Check("actual nested manual password dialog completes only selection",view.OutputPath.Length>0 && Directory.GetFiles(view.OutputPath,"*",SearchOption.AllDirectories).Length==1 && !model!.Tasks.Single().Completed);
                            Check("nested manual password saved after actual selected output",(await vault.ReadAsync()).Any(record=>record.Secret=="password2" && record.LastSuccessAt is not null));
                        }
                    }
                    catch(Exception error) {checks["unexpected dialog "+error]=false;}
                    finally {dialog.Close();}
                }));
            });
            model=new MainViewModel(baseDirectory,data,vault,dialogs);model.ConfigureResultDirectoryValidation(paths=>opened.AddRange(paths));window.DataContext=model;await model.InitializeAsync();
            model.ApplyDirectoryRules(new(Layout:ExtractionLayout.Smart),Path.Combine(data,"output"));model.AddFiles([archive]);model.SelectedTask=model.Tasks.Single();model.BrowseArchiveCommand.Execute(null);await model.WaitForIdleAsync();
            await window.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
            mode=1;model.Tasks.Clear();model.AddFiles([Path.Combine(root,"tests/fixtures/generated/large.7z")]);model.SelectedTask=model.Tasks.Single();model.BrowseArchiveCommand.Execute(null);await model.WaitForIdleAsync();
            Check("both normal and cancel browser flows ran",dialogsShown==2);
            await window.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
            mode=2;model.AutoTry=false;model.OutputPath=Path.Combine(data,"encrypted-output");model.Tasks.Clear();model.AddFiles([Path.Combine(root,"tests/fixtures/generated/mixed-aes.zip")]);model.SelectedTask=model.Tasks.Single();model.BrowseArchiveCommand.Execute(null);await model.WaitForIdleAsync();
            Check("manual selection browser flow ran",dialogsShown==3);
            Check("browser returns idle after closing work",model.IsIdle);
            await PreviewSpike(root,data,baseDirectory,Check);
        }
        catch(Exception error) {checks["unexpected "+error]=false;}
        finally {theme.Apply(SystemParameters.HighContrast);}
        await File.WriteAllTextAsync(Path.Combine(root,"artifacts/selection-ui-validation.json"),JsonSerializer.Serialize(new {passed=checks.Values.All(value=>value),total=checks.Count,checks,geometry,syntheticData=true,previewPrototypeOnly=true,fullPreviewUi=false}));return checks.Values.All(value=>value);
    }
    private static T? Find<T>(DependencyObject parent) where T:DependencyObject
    {if(parent is T match) return match;for(var i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++) if(Find<T>(VisualTreeHelper.GetChild(parent,i)) is {} child) return child;return null;}
    private static async Task PreviewSpike(string root,string data,string baseDirectory,Action<string,bool> check)
    {
        var zipPath=Path.Combine(data,"preview-spike.zip");var pixel=BitmapSource.Create(2,2,96,96,PixelFormats.Bgra32,null,new byte[16],8);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(pixel));using var image=new MemoryStream();encoder.Save(image);
        using(var zip=ZipFile.Open(zipPath,ZipArchiveMode.Create)) {using(var writer=new StreamWriter(zip.CreateEntry("text.txt").Open())) writer.Write("只读文本示例");using(var output=zip.CreateEntry("image.png").Open()) output.Write(image.ToArray());using var extra=new StreamWriter(zip.CreateEntry("unselected.txt").Open());extra.Write("must not extract");}
        var engine=new ArchiveEngineClient(Path.Combine(baseDirectory,"ArchiveEngineWorker.exe"),Path.Combine(baseDirectory,"engines/26.04/7z.dll"));var listed=await engine.ListAsync(zipPath);var selected=listed.Items.Where(item=>item.Path is "text.txt" or "image.png").ToArray();
        var scratch=Path.GetFullPath(Path.Combine(data,"preview-temp-"+Guid.NewGuid().ToString("N")));Directory.CreateDirectory(scratch);
        try
        {
            var verified=await engine.TestSelectionAsync(zipPath,selected,null,new Dictionary<uint,string>());var extracted=await engine.ExtractSelectionAsync(zipPath,scratch,selected,null,new Dictionary<uint,string>());
            check("F26 prototype selected temporary output only",verified.IsSuccess && extracted.IsSuccess && Directory.GetFiles(scratch).Length==2);
            check("F26 prototype UTF8 text read",await File.ReadAllTextAsync(Path.Combine(scratch,"text.txt"))=="只读文本示例");
            using(var input=File.OpenRead(Path.Combine(scratch,"image.png"))) {var decoder=BitmapDecoder.Create(input,BitmapCreateOptions.None,BitmapCacheOption.OnLoad);check("F26 prototype builtin PNG decoder",decoder.Frames[0].PixelWidth==2 && decoder.Frames[0].PixelHeight==2);}
        }
        finally {if(!scratch.StartsWith(data+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)) throw new IOException("Invalid preview test scope.");Directory.Delete(scratch,true);}
        check("F26 prototype temporary directory removed without lock",!Directory.Exists(scratch));
    }
}
