using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
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
internal static class DesktopCheckpointValidation
{
    internal static async Task<bool> RunAsync(MainWindow startup,ThemeResources theme,string root,string baseDirectory)
    {
        var data=Path.GetFullPath(Path.Combine(root,"artifacts/checkpoint-ui-"+Guid.NewGuid().ToString("N")));Directory.CreateDirectory(data);
        var screenshots=Path.Combine(root,"artifacts/checkpoint-preview");Directory.CreateDirectory(screenshots);
        var checks=new Dictionary<string,bool>();void Check(string name,bool passed)=>checks.Add(name,passed);
        var originalLanguage=UiText.LanguageCode;var originalMode=theme.Mode;
        try
        {
            foreach(var language in new[]{"zh-CN","en"})
            {
                UiText.Configure(language);
                var window=new MainWindow {WindowStartupLocation=WindowStartupLocation.Manual,Left=-32000,Top=-32000,Opacity=0,ShowActivated=false,ShowInTaskbar=false,SuppressExitConfirmationForValidation=true};window.Show();
                try
                {
                    var profile=Path.Combine(data,language);Directory.CreateDirectory(profile);var archive=Path.Combine(profile,"input.zip");
                    var liveSizeProbe=Path.Combine(profile,"live-size.bin");
                    using(var writer=new FileStream(liveSizeProbe,FileMode.Create,FileAccess.Write,FileShare.None))
                    {
                        writer.Write(new byte[64*1024]);writer.Flush();
                        Check(language+" metadata-only size query observes exclusive active writer",DesktopPauseValidation.LiveLength(liveSizeProbe)==64*1024);
                    }
                    File.Delete(liveSizeProbe);
                    using(var zip=ZipFile.Open(archive,ZipArchiveMode.Create)) foreach(var name in new[]{"a.txt","nested/b.txt","c.txt"}) {using var writer=new StreamWriter(zip.CreateEntry(name).Open());writer.Write(name+" original");}
                    var output=Path.Combine(profile,"output");var store=new ExtractionCheckpointStore(Path.Combine(profile,"extraction-progress"));
                    var vault=new DpapiPasswordVault(Path.Combine(profile,"passwords.apv"));
                    var confirm=false;var confirms=0;
                    MainViewModel Model()=>new(baseDirectory,profile,vault,new WindowDialogs(window,confirmDiscardCheckpoint:_=>{confirms++;return confirm;}));
                    var model=Model();window.DataContext=model;await model.InitializeAsync();model.Page=0;await Layout(window);
                    var choice=(CheckBox)window.FindName("RetainProgressChoice");
                    Check(language+" retention defaults off for old profile",!model.RetainExtractionProgress && choice.IsChecked==false);
                    var peer=UIElementAutomationPeer.CreatePeerForElement(choice)!;
                    Check(language+" option has localized accessible name and focus",peer.GetName()==UiText.Get("Checkpoint.Enable") && peer.IsKeyboardFocusable());
                    ((IToggleProvider)peer.GetPattern(PatternInterface.Toggle)).Toggle();await Layout(window);
                    Check(language+" UIA option changes bound setting",model.RetainExtractionProgress);
                    model.OutputPath=output;
                    var version=model.SelectedEngineVersion;
                    var dll=Path.Combine(baseDirectory,"engines",version,"7z.dll");
                    var binding=new CheckpointBinding(version,Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(dll))).ToLowerInvariant(),EngineArchitecture.Current,0);
                    async Task Stop()
                    {
                        using var cancel=new CancellationTokenSource();
                        var stopped=await new ExtractionService(new ArchiveEngineClient(Path.Combine(baseDirectory,"ArchiveEngineWorker.exe"),dll),vault,new PasswordCandidateProvider(vault)).ExtractAsync(archive,output,false,
                            _=>Task.FromResult<PasswordPromptResponse?>(null),_=>Task.FromResult(ExistingFilePolicy.Replace),new ForwardProgress<TaskUpdate>(update=>{if(update.State=="checkpoint") cancel.Cancel();}),cancel.Token,checkpoint:new(store,binding));
                        Check(language+" controlled real stop "+checks.Count,stopped.Engine.Status=="cancelled" && store.ReadPending().Single().Receipts.Count==1);
                    }
                    await Stop();
                    // A new view model uses only persisted files, not the old task row.
                    model=Model();window.DataContext=model;await model.InitializeAsync();model.Page=0;await Layout(window);
                    var task=model.Tasks.Single();model.SelectedTask=task;await Layout(window);
                    Check(language+" startup reconstructs saved task",task.Checkpoint is not null && task.Status==UiText.Format("Checkpoint.Available",1,3) && !task.Completed && !model.RetainExtractionProgress);
                    Check(language+" destination is bound and encoding editing disabled",model.DisplayOutputPath==output && model.IsOutputPathReadOnly && !model.FilenameEncodingCommand.CanExecute(null));
                    Check(language+" bound destination pickers disabled",!model.BrowseOutputCommand.CanExecute(null) && !model.EditExtractionRulesCommand.CanExecute(null));
                    Check(language+" retained row cannot silently be removed",!model.RemoveFileCommand.CanExecute(null) && model.ResumeCheckpointCommand.CanExecute(null) && model.DiscardCheckpointCommand.CanExecute(null));
                    foreach(var mode in new[]{"light","dark","high-contrast"}) foreach(var width in new[]{1120d,800d})
                    {
                        theme.SetMode(mode=="dark"?"dark":"light");theme.Apply(mode=="high-contrast");window.Width=width;window.Height=width==800?600:760;await Layout(window);
                        var checkbox=(CheckBox)window.FindName("RetainProgressChoice");checkbox.BringIntoView();await Layout(window);
                        var primary=(Button)window.FindName("ExtractAction");var table=(DataGrid)window.FindName("ArchiveTasks");
                        var row=table.ItemContainerGenerator.ContainerFromItem(task) as DataGridRow;
                        Check(language+mode+width+" layout retains reachable option and fixed primary",Fits(window,checkbox) && Fits(window,primary));
                        window.Opacity=1;Render(window,Path.Combine(screenshots,language+"-"+mode+width+".png"));window.Opacity=0;
                        if(row is not null) {row.BringIntoView();await Layout(window);}
                        Check(language+mode+width+" saved row reachable inside scrolling viewport",row is not null && Fits(window,row) && Fits(window,primary));
                        if(width==800) {window.Opacity=1;Render(window,Path.Combine(screenshots,language+"-"+mode+width+"-list.png"));window.Opacity=0;}

                    }
                    var menu=(ContextMenu)window.FindName("QueueActionsMenu");menu.Opacity=0;
                    ((Button)window.FindName("QueueActionsAction")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Layout(window);
                    var resume=(MenuItem)window.FindName("QueueResumeAction");var discard=(MenuItem)window.FindName("QueueDiscardAction");
                    Check(language+" menu binds localized resume and discard",menu.IsOpen && resume.IsEnabled && discard.IsEnabled && resume.Header.ToString()==UiText.Get("Checkpoint.Resume") && discard.Header.ToString()==UiText.Get("Checkpoint.Discard"));
                    menu.IsOpen=false;
                    model.DiscardCheckpointCommand.Execute(null);await model.WaitForIdleAsync();
                    Check(language+" decline discard keeps progress",confirms==1 && store.ReadPending().Count==1 && task.Checkpoint is not null);
                    model.RetainExtractionProgress=true;model.ResumeCheckpointCommand.Execute(null);await model.WaitForIdleAsync();await Layout(window);
                    Check(language+" actual resume command delivers exact remaining tree",task.Completed && task.Checkpoint is null && store.ReadPending().Count==0 && Directory.GetFiles(output,"*",SearchOption.AllDirectories).Length==3 && File.ReadAllText(Path.Combine(output,"nested/b.txt"))=="nested/b.txt original");
                    var restarted=Model();await restarted.InitializeAsync();
                    Check(language+" new settings retain opt-in without restoring completed task",restarted.RetainExtractionProgress && restarted.Tasks.Count==0);
                    foreach(var file in Directory.GetFiles(output,"*",SearchOption.AllDirectories)) File.Delete(file);
                    await Stop();
                    model=Model();window.DataContext=model;await model.InitializeAsync();model.SelectedTask=model.Tasks.Single();confirm=true;
                    var unrelated=Path.Combine(output,"unrelated.txt");File.WriteAllText(unrelated,"keep");
                    model.DiscardCheckpointCommand.Execute(null);await model.WaitForIdleAsync();await Layout(window);
                    Check(language+" actual discard removes private progress and keeps archive",store.ReadPending().Count==0 && model.SelectedTask.Checkpoint is null && !model.SelectedTask.Completed && File.ReadAllText(unrelated)=="keep" && File.Exists(archive));
                    var resetOutput=(TextBox)window.FindName("OutputBox");
                    Check(language+" discard refreshes displayed destination and editability",resetOutput.Text==model.OutputPath && !resetOutput.IsReadOnly && model.BrowseOutputCommand.CanExecute(null) && model.EditExtractionRulesCommand.CanExecute(null));
                    Check(language+" final actions disable after discard",!model.ResumeCheckpointCommand.CanExecute(null) && !model.DiscardCheckpointCommand.CanExecute(null) && model.RemoveFileCommand.CanExecute(null));
                }
                finally {window.Close();}
            }
        }
        catch(Exception error){checks["exception "+error]=false;}
        finally {theme.SetMode(originalMode);theme.Apply(false);UiText.Configure(originalLanguage);if(Directory.Exists(data)) Directory.Delete(data,true);}
        await File.WriteAllTextAsync(Path.Combine(root,"artifacts/checkpoint-ui-validation.json"),JsonSerializer.Serialize(new{passed=checks.Values.All(value=>value),total=checks.Count,checks},new JsonSerializerOptions{WriteIndented=true}));
        return checks.Values.All(value=>value);
    }
    private static async Task Layout(Window window)=>await window.Dispatcher.InvokeAsync(window.UpdateLayout,DispatcherPriority.ApplicationIdle);
    private static bool Fits(Window window,FrameworkElement element)
    {if(!element.IsVisible || element.ActualHeight<=0)return false;var content=(FrameworkElement)window.Content;var rect=element.TransformToAncestor(content).TransformBounds(new Rect(element.RenderSize));if(rect.Top< -1 || rect.Left< -1 || rect.Bottom>content.ActualHeight+1 || rect.Right>content.ActualWidth+1)return false;
        for(DependencyObject? parent=VisualTreeHelper.GetParent(element);parent is not null;parent=VisualTreeHelper.GetParent(parent))
            if(parent is ScrollContentPresenter viewport){var bounds=element.TransformToAncestor(viewport).TransformBounds(new Rect(element.RenderSize));if(bounds.Top< -1 || bounds.Left< -1 || bounds.Bottom>viewport.ActualHeight+1 || bounds.Right>viewport.ActualWidth+1)return false;}
        return true;}
    private static void Render(Window window,string path)
    {var bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap((int)window.ActualWidth,(int)window.ActualHeight,96,96,System.Windows.Media.PixelFormats.Pbgra32);bitmap.Render(window);var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));using var output=File.Create(path);encoder.Save(output);}
}
