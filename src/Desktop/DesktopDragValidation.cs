using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using ArchivePassword.Core;
using ArchivePassword.Infrastructure;
using ComDataObject=System.Runtime.InteropServices.ComTypes.IDataObject;

namespace ArchivePassword.Desktop;
internal static class DesktopDragValidation
{
    internal static async Task<bool> RunAsync(MainWindow startup,ThemeResources theme,string root,string baseDirectory)
    {
        var data=Path.Combine(root,"artifacts/drag-ui-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(data);
        var screenshots=Path.Combine(root,"artifacts/drag-preview");Directory.CreateDirectory(screenshots);
        var checks=new Dictionary<string,bool>();void Check(string name,bool value)
        {checks.Add(name,value);File.WriteAllText(Path.Combine(root,"artifacts/drag-ui-progress.json"),JsonSerializer.Serialize(new{last=name,value,total=checks.Count}));}
        var diagnostics=new List<object>();
        try
        {
            foreach(var language in new[]{"zh-CN","en"})
            {
                UiText.Configure(language);
                var window=new MainWindow {WindowStartupLocation=WindowStartupLocation.Manual,Left=-32000,Top=-32000,Opacity=0,ShowActivated=false,ShowInTaskbar=false,SuppressExitConfirmationForValidation=true};window.Show();
                try
                {
                    var profile=Path.Combine(data,language);Directory.CreateDirectory(profile);
                    var source=Path.Combine(profile,"source.txt");await File.WriteAllTextAsync(source,"exact drag bytes 中文 😀");var archive=Path.Combine(profile,"encrypted.7z");
                    var client=new ArchiveEngineClient(Path.Combine(baseDirectory,"ArchiveEngineWorker.exe"),Path.Combine(baseDirectory,"engines/26.04/7z.dll"));
                    await client.CreateAsync(new([new(source,"folder/中文.txt",false),new(source,"other.txt",false)],"7z",5,true),archive,"synthetic-secret");
                    var original=File.ReadAllBytes(archive);var vaultPath=Path.Combine(profile,"passwords.apv");var vault=new DpapiPasswordVault(vaultPath);
                    await vault.SaveAsync(new(Guid.NewGuid(),"synthetic","synthetic-secret","",true,DateTimeOffset.UtcNow));var before=File.ReadAllBytes(vaultPath);
                    MainViewModel? main=null;int shown=0;
                    var dialogs=new WindowDialogs(window,prepareDialog:_=>throw new InvalidOperationException("Saved password should work"),prepareContents:dialog=>
                    {
                        Hide(dialog);dialog.Loaded+=(_,_)=>dialog.Dispatcher.BeginInvoke((Action)(async()=>
                        {
                            try
                            {
                                shown++;var view=(ArchiveContentsViewModel)dialog.DataContext;
                                Check(language+" no selection disables drag",!view.CanDrag && !view.PrepareDragCommand.CanExecute(null));
                                var chosen=view.Rows.Single(r=>r.Path.Replace('\\','/')=="folder/中文.txt");var other=view.Rows.Single(r=>r.Path=="other.txt");
                                chosen.Selected=true;dialog.EntryTable.SelectedItem=other;view.Search="other";await Layout(dialog);
                                dialog.EditContentsMenu.Opacity=0;dialog.EditContentsButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Layout(dialog);
                                Check(language+" keyboard menu binds preparation",dialog.EditContentsMenu.IsOpen && dialog.PrepareDragAction.Command==view.PrepareDragCommand && view.PrepareDragCommand.CanExecute(null));dialog.EditContentsMenu.IsOpen=false;
                                view.PrepareDragCommand.Execute(null);await view.WaitForIdleAsync();await Layout(dialog);
                                var temporary=Path.Combine(profile,"drag-temp");
                                Check(language+" explicit preparation keeps checkbox scope across search",view.OperationMessage.Contains(UiText.Get("Drag.Ready")) && Directory.GetFiles(temporary,"*",SearchOption.AllDirectories).Length==1 && Directory.GetFiles(temporary,"*",SearchOption.AllDirectories).Single().EndsWith("中文.txt"));
                                Check(language+" preparation and browse do not mark archive delivered",main!.SelectedTask!.Completed==false && main.SelectedTask.SelectedOutputPath is null && view.OutputPath=="" && before.SequenceEqual(File.ReadAllBytes(vaultPath)));
                                foreach(var mode in new[]{"light","dark","high-contrast"}) foreach(var size in new[]{(900d,600d),(640d,400d),(512d,360d)})
                                {
                                    theme.SetMode(mode=="dark"?"dark":"light");theme.Apply(mode=="high-contrast");dialog.MinWidth=0;dialog.MinHeight=0;dialog.Width=size.Item1;dialog.Height=size.Item2;await Layout(dialog);
                                    var row=dialog.EntryTable.ItemContainerGenerator.ContainerFromItem(other) as DataGridRow;
                                    Check(language+mode+size.Item1+" prepared layout retains actions and row",Fits(dialog,dialog.PreviewEntryButton) && Fits(dialog,dialog.ExtractSelectedButton) && Fits(dialog,dialog.EditContentsButton) && Fits(dialog,dialog.EntrySearch) && dialog.EntrySearch.ActualWidth>=80 && row is not null && Fits(dialog,row));
                                    dialog.Opacity=1;Render(dialog,Path.Combine(screenshots,language+"-"+mode+size.Item1+".png"));dialog.Opacity=0;
                                }
                                var target=Path.Combine(profile,"drop-target");Directory.CreateDirectory(target);
                                await view.DragAsync(paths=>
                                {
                                    Check(language+" prepared paths still exist during drop",paths.Count==1 && Directory.Exists(paths.Single()));
                                    var dataObject=new ArchiveFileDrag.SynchronousFiles((ComDataObject)new DataObject(DataFormats.FileDrop,paths.ToArray()));
                                    var pointer=Marshal.GetIUnknownForObject(dataObject);var asyncId=new Guid("3D8B0590-F691-11d2-8EA9-006097DF5BD4");
                                    var hr=Marshal.QueryInterface(pointer,in asyncId,out var unsupported);if(unsupported!=IntPtr.Zero) Marshal.Release(unsupported);Marshal.Release(pointer);
                                    Check(language+" COM data object declines asynchronous consumption",hr==unchecked((int)0x80004002));
                                    var format=new System.Runtime.InteropServices.ComTypes.FORMATETC {cfFormat=15,dwAspect=System.Runtime.InteropServices.ComTypes.DVASPECT.DVASPECT_CONTENT,lindex=-1,tymed=System.Runtime.InteropServices.ComTypes.TYMED.TYMED_HGLOBAL};
                                    dataObject.GetData(ref format,out var medium);
                                    try
                                    {
                                        var count=DragQueryFile(medium.unionmember,uint.MaxValue,null,0);var names=new List<string>();
                                        for(uint i=0;i<count;i++) {var text=new System.Text.StringBuilder(32768);DragQueryFile(medium.unionmember,i,text,(uint)text.Capacity);names.Add(text.ToString());}
                                        Check(language+" native HDROP contains exact absolute prepared paths",names.SequenceEqual(paths));
                                        foreach(var path in names) foreach(var file in Directory.GetFiles(path,"*",SearchOption.AllDirectories))
                                        {var destination=Path.Combine(target,Path.GetFileName(path),Path.GetRelativePath(path,file));Directory.CreateDirectory(Path.GetDirectoryName(destination)!);File.Copy(file,destination);}
                                    }
                                    finally {ReleaseStgMedium(ref medium);}
                                    return true;
                                });
                                Check(language+" target gets selected bytes and staging is cleared",File.ReadAllBytes(Path.Combine(target,"folder/中文.txt")).SequenceEqual(File.ReadAllBytes(source)) && !File.Exists(Path.Combine(target,"other.txt")) && !Directory.EnumerateFileSystemEntries(temporary).Any());
                                await view.DragAsync(_=>null);view.CancelCommand.Execute(null);
                                Check(language+" ready cancel cleans temporary files",!Directory.EnumerateFileSystemEntries(temporary).Any() && view.OperationMessage==UiText.Get("Drag.Cancelled"));
                                view.ClearSelectionCommand.Execute(null);view.Search="";dialog.EntryTable.SelectedItem=other;await Layout(dialog);
                                await view.DragAsync(paths=>{Check(language+" unmarked highlighted row supported",paths.Count==1 && Path.GetFileName(paths.Single())=="other.txt");return false;});
                                Check(language+" rejected target cleans staging",!Directory.EnumerateFileSystemEntries(temporary).Any() && view.OperationMessage==UiText.Get("Drag.Cancelled"));
                                diagnostics.Add(new{language,stage="after rejection",view.OperationMessage,view.IsWorking,current=view.CurrentRow?.Path,paths=Directory.GetFileSystemEntries(temporary)});
                                await view.DragAsync(_=>null);dialog.Close();await Layout(dialog);
                                Check(language+" close ready browser cleans staging",!Directory.EnumerateFileSystemEntries(temporary).Any());
                                diagnostics.Add(new{language,stage="after closing",view.OperationMessage,view.IsWorking,current=view.CurrentRow?.Path,paths=Directory.GetFileSystemEntries(temporary),dialog.IsVisible});
                                Check(language+" all transfers keep source and library unchanged",before.SequenceEqual(File.ReadAllBytes(vaultPath)) && original.SequenceEqual(File.ReadAllBytes(archive)) && !main.SelectedTask.Completed && main.SelectedTask.SelectedOutputPath is null);
                            }
                            catch(Exception error) {Check(language+" exception "+error,false);dialog.Close();}
                        }));
                    });
                    main=new MainViewModel(baseDirectory,profile,vault,dialogs);window.DataContext=main;await main.InitializeAsync();main.AddFiles([archive]);main.SelectedTask=main.Tasks.Single();main.BrowseArchiveCommand.Execute(null);await main.WaitForIdleAsync();
                    Check(language+" production browser returns idle",shown==1 && main.IsIdle);
                    using var cancel=new CancellationTokenSource();var cancelled=false;
                    var model=new ArchiveContentsViewModel("synthetic.zip",[new(0,"file.txt",1,false,false)],cancel:()=>cancel.Cancel(),prepareDrag:async(_,_)=>
                    {try {await Task.Delay(Timeout.Infinite,cancel.Token);}catch(OperationCanceledException) {cancelled=true;}return new(new EngineResult {Status="cancelled",Code="cancelled"});});
                    model.CurrentRow=model.Rows.Single();var closing=new ArchiveContentsDialog(model);Hide(closing);closing.Show();model.PrepareDragCommand.Execute(null);closing.Close();await model.WaitForIdleAsync();await Layout(closing);
                    Check(language+" closing during preparation waits for cancellation",cancelled && !model.IsWorking && !closing.IsVisible);
                    var dropSource=new ArchiveFileDrag.CopySource();Check(language+" OLE copy source escape release and default cursor",dropSource.QueryContinueDrag(true,1)==0x40101 && dropSource.QueryContinueDrag(false,0)==0x40100 && dropSource.QueryContinueDrag(false,1)==0 && dropSource.GiveFeedback(1)==0x40102);
                    // OLE only queries cancellation after input messages. Post to this private
                    // test thread; never inject input or move the actual desktop pointer.
                    var thread=GetCurrentThreadId();
                    var cancelSource=new CancelSource();var ticks=0;
                    using(var wake=new System.Threading.Timer(_=>PostThreadMessage(thread,Interlocked.Increment(ref ticks)>100?0x0012u:0x0200u,UIntPtr.Zero,IntPtr.Zero),null,100,100))
                        Check(language+" actual native OLE cancellation returns without reading or moving files",!ArchiveFileDrag.Copy([source],cancelSource) && cancelSource.Calls>0 && File.Exists(source));
                }
                finally {window.Close();}
            }
        }
        catch(Exception error) {Check("exception "+error,false);}
        finally {UiText.Configure("zh-CN");theme.SetMode("light");theme.Apply(SystemParameters.HighContrast);if(Directory.Exists(data)) Directory.Delete(data,true);}
        var passed=checks.Count>0 && checks.Values.All(v=>v);
        await File.WriteAllTextAsync(Path.Combine(root,"artifacts/drag-ui-validation.json"),JsonSerializer.Serialize(new{passed,total=checks.Count,checks,diagnostics,syntheticOnly=true,actualExplorerDragTested=false},new JsonSerializerOptions{WriteIndented=true}));return passed;
    }
    [DllImport("shell32.dll",CharSet=CharSet.Unicode)] private static extern uint DragQueryFile(IntPtr drop,uint index,System.Text.StringBuilder? file,uint size);
    [ComVisible(true),ClassInterface(ClassInterfaceType.None)] public sealed class CancelSource:ArchiveFileDrag.IDropSource
    {public int Calls;public int QueryContinueDrag(bool escape,uint keys) {Calls++;return 0x40101;}public int GiveFeedback(uint effect)=>0x40102;}
    [DllImport("ole32.dll")] private static extern void ReleaseStgMedium(ref System.Runtime.InteropServices.ComTypes.STGMEDIUM medium);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] [return:MarshalAs(UnmanagedType.Bool)] private static extern bool PostThreadMessage(uint thread,uint message,UIntPtr wParam,IntPtr lParam);
    private static void Hide(Window window) {window.WindowStartupLocation=WindowStartupLocation.Manual;window.Left=-32000;window.Top=-32000;window.Opacity=0;window.ShowActivated=false;window.ShowInTaskbar=false;}
    private static async Task Layout(Window window)=>await window.Dispatcher.InvokeAsync(window.UpdateLayout,DispatcherPriority.ApplicationIdle);
    private static void Render(Window window,string path)
    {
        var bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap((int)window.ActualWidth,(int)window.ActualHeight,96,96,System.Windows.Media.PixelFormats.Pbgra32);bitmap.Render(window);
        var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));using var output=File.Create(path);encoder.Save(output);
    }
    private static bool Fits(Window window,FrameworkElement element) {if(!element.IsVisible || element.ActualHeight<=0) return false;var client=(FrameworkElement)window.Content;var bounds=element.TransformToAncestor(client).TransformBounds(new Rect(element.RenderSize));return bounds.Top>=-1 && bounds.Left>=-1 && bounds.Right<=client.ActualWidth+1 && bounds.Bottom<=client.ActualHeight+1;}
}
