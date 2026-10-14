using System.IO;
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
internal static class DesktopArchiveEditingValidation
{
    internal static async Task<bool> RunAsync(MainWindow window,ThemeResources theme,string root,string baseDirectory)
    {
        var data=Path.Combine(root,"artifacts/eu-"+Guid.NewGuid().ToString("N")[..8]);Directory.CreateDirectory(data);
        var screenshots=Path.Combine(root,"artifacts/archive-editing-preview");Directory.CreateDirectory(screenshots);
        var checks=new Dictionary<string,bool>();void Check(string name,bool value)=>checks[name]=value;
        var original=Path.Combine(data,"original.txt");await File.WriteAllTextAsync(original,"original");
        var added=Path.Combine(data,"added.txt");await File.WriteAllTextAsync(added,"added 中文");
        var replacement=Path.Combine(data,"replacement.txt");await File.WriteAllTextAsync(replacement,"replacement");
        var folder=Path.Combine(data,"folder");Directory.CreateDirectory(Path.Combine(folder,"empty"));await File.WriteAllTextAsync(Path.Combine(folder,"child.txt"),"child");
        var vaultPath=Path.Combine(data,"passwords.apv");var vault=new DpapiPasswordVault(vaultPath);await vault.SaveAsync(new(Guid.NewGuid(),"fixture","fixture-password","",true,DateTimeOffset.UtcNow));var vaultBefore=File.ReadAllBytes(vaultPath);
        bool approved=true;int shown=0;MainViewModel? model=null;
        try
        {
            foreach(var version in new[]{"26.04","26.03"})
            {
                int manualPrompts=0;
                var client=new ArchiveEngineClient(Path.Combine(baseDirectory,"ArchiveEngineWorker.exe"),Path.Combine(baseDirectory,"engines",version,"7z.dll"));
                var archive=Path.Combine(data,version+".7z");await client.CreateAsync(new([new(original,"keep.txt",false)],"7z",5,true),archive,"fixture-password");
                var bytes=File.ReadAllBytes(archive);
                var dialogs=new WindowDialogs(window,prepareDialog:password=>
                {
                    password.WindowStartupLocation=WindowStartupLocation.Manual;password.Left=-32000;password.Top=-32000;password.Opacity=0;password.ShowActivated=false;password.ShowInTaskbar=false;
                    password.Loaded+=(_,_)=>password.Dispatcher.BeginInvoke(()=>
                    {
                        Check(version+" actual manual verification prompt "+(++manualPrompts),password.Title=="输入校验密码" && password.SavePassword.Visibility==Visibility.Collapsed && password.SecretBox.IsVisible);
                        password.SecretBox.Password="fixture-password";
                        ((IInvokeProvider)UIElementAutomationPeer.CreatePeerForElement(password.ContinueButton)!.GetPattern(PatternInterface.Invoke)).Invoke();
                    });
                },selectArchiveEditSources:action=>action==ArchiveEditAction.AddFolder?[folder]:action==ArchiveEditAction.Replace?[replacement]:[added],
                    confirmArchiveEdit:(action,remove,add)=>approved,prepareContents:dialog=>
                {
                    dialog.WindowStartupLocation=WindowStartupLocation.Manual;dialog.Left=-32000;dialog.Top=-32000;dialog.Opacity=0;dialog.ShowActivated=false;dialog.ShowInTaskbar=false;
                    dialog.Loaded+=(_,_)=>dialog.Dispatcher.BeginInvoke((Action)(async()=>
                    {
                        shown++;var view=(ArchiveContentsViewModel)dialog.DataContext;
                        // Bind the actual menu commands without opening a native popup on the user's desktop.
                        dialog.EditContentsMenu.DataContext=view;
                        async Task Execute(string name) {await Layout(dialog);Invoke((MenuItem)dialog.FindName(name));await Layout(dialog);await view.WaitForIdleAsync();await Layout(dialog);}
                        try
                        {
                            Check(version+" editable browser and initial command state",view.EditingAvailable && view.AddFilesCommand.CanExecute(null) && !view.DeleteSelectedCommand.CanExecute(null) && !view.ReplaceSelectedCommand.CanExecute(null));
                            approved=false;await Execute("AddEntryFilesAction");Check(version+" cancelled confirmation preserves exact archive",bytes.SequenceEqual(File.ReadAllBytes(archive)) && view.Rows.Count==1);approved=true;
                            await Execute("AddEntryFilesAction");Check(version+" real add refreshes content and clears selection",view.Rows.Count==2 && view.Rows.Any(row=>row.Path=="added.txt") && !view.DeleteSelectedCommand.CanExecute(null));
                            Check(version+" actual original backup retained",Directory.GetFiles(data,version+".7z.before-edit-*").Any(path=>bytes.SequenceEqual(File.ReadAllBytes(path))));
                            view.Rows.Single(row=>row.Path=="keep.txt").Selected=true;await Execute("ReplaceEntryAction");Check(version+" actual replacement keeps path",view.Rows.Count==2 && view.Rows.Single(row=>row.Path=="keep.txt").Entry.Size==(ulong)new FileInfo(replacement).Length);
                            model!.AutoTry=false;await Execute("AddEntryFolderAction");Check(version+" actual folder add contains child and empty folder",view.Rows.Any(row=>row.Path.Replace('\\','/')=="folder/child.txt") && view.Rows.Any(row=>row.Path.Replace('\\','/').TrimEnd('/')=="folder/empty"));
                            view.Rows.Single(row=>row.Path.Replace('\\','/').TrimEnd('/')=="folder").Selected=true;view.Search="keep";Check(version+" hidden folder selections include descendants",view.SelectedEntries.Count==3 && view.Rows.Count==1);
                            await Execute("DeleteEntriesAction");Check(version+" folder delete refreshes filtered view",view.Rows.Count==1 && view.Summary.Contains("2 项") && !view.DeleteSelectedCommand.CanExecute(null));view.Search="";
                            var output=Path.Combine(data,"output-"+version);await client.ExtractAsync(archive,output,"fixture-password");Check(version+" actual edited bytes and kept password",File.ReadAllText(Path.Combine(output,"keep.txt"))=="replacement" && File.ReadAllText(Path.Combine(output,"added.txt"))=="added 中文" && (await client.ListAsync(archive)).NeedsPassword);
                            Check(version+" modified task resets previous result",!model!.Tasks.Single().Completed && model.Tasks.Single().Status.Contains("待解压"));
                            await Execute("AddEntryFilesAction");Check(version+" name collision visible without changing archive",view.OperationMessage.Contains("路径冲突") && view.Rows.Count==2);
                            Check(version+" all manual verification flows completed",manualPrompts==3);
                            foreach(var contrast in new[]{false,true}) foreach(var size in new[]{(900d,600d),(640d,400d)})
                            {
                                theme.Apply(contrast);dialog.Width=size.Item1;dialog.Height=size.Item2;await Layout(dialog);
                                var first=(DataGridRow)dialog.EntryTable.ItemContainerGenerator.ContainerFromIndex(0);
                                Check($"{version} complete row {size.Item1} {contrast}",first is not null && Fits(dialog.EntryTable,first));
                                Check($"{version} editing and primary buttons fit {size.Item1} {contrast}",Fits((FrameworkElement)dialog.Content,dialog.EditContentsButton) && Fits((FrameworkElement)dialog.Content,dialog.ExtractSelectedButton));
                                Check($"{version} edit button keyboard and accessible label {size.Item1} {contrast}",UIElementAutomationPeer.CreatePeerForElement(dialog.EditContentsButton)!.IsKeyboardFocusable() && UIElementAutomationPeer.CreatePeerForElement(dialog.EditContentsButton)!.GetName().Contains("修改"));
                                await Capture(dialog,Path.Combine(screenshots,$"{version}-{size.Item1}-{contrast}.png"));
                            }
                        }
                        catch(Exception error) {checks[version+" dialog exception "+error]=false;}
                        finally {dialog.Close();}
                    }));
                });
                model=new MainViewModel(baseDirectory,Path.Combine(data,"state-"+version),vault,dialogs);window.DataContext=model;await model.InitializeAsync();
                model.SelectedEngineVersion=version;model.ApplyEngineCommand.Execute(null);await model.WaitForIdleAsync();
                Check(version+" selected engine actually activated",model.EngineSummary.Contains(version));
                model.AddFiles([archive]);model.SelectedTask=model.Tasks.Single();model.BrowseArchiveCommand.Execute(null);await model.WaitForIdleAsync();await Layout(window);
                Check(version+" actual browser completed and returned idle",model.IsIdle);
            }
            Check("both engine browser flows executed",shown==2);
            Check("editing never writes vault records",vaultBefore.SequenceEqual(File.ReadAllBytes(vaultPath)));
            var readOnly=new ArchiveContentsViewModel("sample.rar",[new(0,"file",10,false,false)],format:"Rar",edit:(_,_,_,_)=>Task.FromResult<ArchiveEditOutcome?>(null));readOnly.Rows[0].Selected=true;
            Check("RAR edit commands disabled",!readOnly.EditingAvailable && !readOnly.AddFilesCommand.CanExecute(null) && !readOnly.DeleteSelectedCommand.CanExecute(null) && !readOnly.ReplaceSelectedCommand.CanExecute(null));
        }
        catch(Exception error) {checks["exception "+error]=false;}
        finally {theme.Apply(SystemParameters.HighContrast);}
        var passed=checks.Count>0 && checks.Values.All(value=>value);await File.WriteAllTextAsync(Path.Combine(root,"artifacts/archive-editing-ui-validation.json"),JsonSerializer.Serialize(new{passed,total=checks.Count,checks,syntheticOnly=true},new JsonSerializerOptions{WriteIndented=true}));return passed;
    }
    private static void Invoke(MenuItem item)=>((IInvokeProvider)UIElementAutomationPeer.CreatePeerForElement(item)!.GetPattern(PatternInterface.Invoke)).Invoke();
    private static async Task Layout(Window window)=>await window.Dispatcher.InvokeAsync(window.UpdateLayout,DispatcherPriority.ApplicationIdle);
    private static bool Fits(FrameworkElement parent,FrameworkElement item) {var bounds=item.TransformToAncestor(parent).TransformBounds(new Rect(item.RenderSize));return bounds.Top>=0 && bounds.Bottom<=parent.ActualHeight+1 && bounds.Left>=0 && bounds.Right<=parent.ActualWidth+1;}
    private static async Task Capture(Window window,string path)
    {
        var content=(Panel)window.Content;content.SetResourceReference(Panel.BackgroundProperty,"SurfaceBrush");await Layout(window);
        var width=content.ActualWidth+content.Margin.Left+content.Margin.Right;var height=content.ActualHeight+content.Margin.Top+content.Margin.Bottom;
        var raw=new RenderTargetBitmap((int)Math.Ceiling(width),(int)Math.Ceiling(height),96,96,PixelFormats.Pbgra32);raw.Render(content);
        var visual=new DrawingVisual();using(var drawing=visual.RenderOpen()) {drawing.DrawRectangle(window.Background,null,new Rect(0,0,width,height));drawing.DrawImage(raw,new Rect(0,0,width,height));}
        var bitmap=new RenderTargetBitmap(raw.PixelWidth,raw.PixelHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(visual);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var output=File.Create(path);encoder.Save(output);
    }
}
