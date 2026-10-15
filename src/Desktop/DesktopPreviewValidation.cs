using System.IO;
using System.Security.Cryptography;
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
internal static class DesktopPreviewValidation
{
    internal static async Task<bool> RunAsync(MainWindow startup,ThemeResources theme,string root,string baseDirectory)
    {
        var data=Path.GetFullPath(Path.Combine(root,"artifacts/pu-"+Guid.NewGuid().ToString("N")[..8]));Directory.CreateDirectory(data);
        var screenshots=Path.Combine(root,"artifacts/preview-content-preview");Directory.CreateDirectory(screenshots);
        var checks=new Dictionary<string,bool>();void Check(string name,bool value) {checks.Add(name,value);}
        try
        {
            foreach(var language in new[]{"zh-CN","en"})
            {
                UiText.Configure(language);
                var window=new MainWindow {WindowStartupLocation=WindowStartupLocation.Manual,Left=-32000,Top=-32000,Opacity=0,ShowActivated=false,ShowInTaskbar=false,SuppressExitConfirmationForValidation=true};window.Show();
                try
                {
                    foreach(var version in new[]{"26.04","26.03"})
                    {
                        var label=language+" "+version+" ";var profile=Path.Combine(data,language+version);Directory.CreateDirectory(profile);
                        var client=new ArchiveEngineClient(Path.Combine(baseDirectory,"ArchiveEngineWorker.exe"),Path.Combine(baseDirectory,"engines",version,"7z.dll"));
                        var text=Path.Combine(profile,"source.txt");await File.WriteAllTextAsync(text,"<script>plain text only</script>\n中文 😀\n"+string.Join('\n',Enumerable.Repeat("scrollable text",70)));
                        var png=Path.Combine(profile,"image.png");var colors=new byte[320*180*4];for(var y=0;y<180;y++) for(var x=0;x<320;x++) {var offset=(y*320+x)*4;colors[offset]=(byte)(x*255/319);colors[offset+1]=(byte)(y*255/179);colors[offset+2]=100;colors[offset+3]=255;}
                        var pixel=BitmapSource.Create(320,180,96,96,PixelFormats.Bgra32,null,colors,320*4);
                        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(pixel));using(var output=File.Create(png)) encoder.Save(output);
                        var archive=Path.Combine(profile,"encrypted.7z");await client.CreateAsync(new([new(text,"folder/中文.txt",false),new(png,"image.png",false),new(text,"do-not-run.exe",false)],"7z",5,true),archive,"preview-secret");
                        var archiveBefore=File.ReadAllBytes(archive);var vaultPath=Path.Combine(profile,"passwords.apv");var vault=new DpapiPasswordVault(vaultPath);
                        await vault.SaveAsync(new(Guid.NewGuid(),"synthetic preview","preview-secret","",true,DateTimeOffset.UtcNow));
                        if(version=="26.03") await vault.EnableMasterPasswordAsync("preview-master-secret");
                        var before=File.ReadAllBytes(vaultPath);int contentsShown=0,previewShown=0;ArchivePreviewViewModel? last=null;
                        var dialogs=new WindowDialogs(window,prepareDialog:_=>throw new InvalidOperationException("Saved preview password must work without a prompt"),prepareContents:dialog=>
                        {
                            Hide(dialog);dialog.Loaded+=(_,_)=>dialog.Dispatcher.BeginInvoke((Action)(async()=>
                            {
                                try
                                {
                                    contentsShown++;var view=(ArchiveContentsViewModel)dialog.DataContext;var table=dialog.EntryTable;
                                    Check(label+"no row disables preview",!view.PreviewCommand.CanExecute(null));
                                    var marked=view.Rows.Single(r=>r.Path.EndsWith(".exe"));marked.Selected=true;table.SelectedItem=marked;await Layout(dialog);
                                    Check(label+"unsupported highlighted row explains disabled preview",!view.PreviewCommand.CanExecute(null) && view.PreviewHint==UiText.Get("Preview.Unsupported"));
                                    foreach(var suffix in new[]{".txt",".png"})
                                    {
                                        var row=view.Rows.Single(r=>r.Path.EndsWith(suffix));table.SelectedItem=row;await Layout(dialog);
                                        Check(label+suffix+" real row binding and keyboard shortcut",view.CurrentRow==row && view.PreviewCommand.CanExecute(null) && dialog.InputBindings.OfType<System.Windows.Input.KeyBinding>().Any(b=>b.Key==System.Windows.Input.Key.P && b.Modifiers==System.Windows.Input.ModifierKeys.Control));
                                        Invoke(dialog.PreviewEntryButton);await Layout(dialog);await view.WaitForIdleAsync();await Layout(dialog);
                                        Check(label+suffix+" return restores browser and extraction checkboxes",!view.IsWorking && marked.Selected && !row.Selected && last?.Text=="" && last.Picture is null);
                                    }
                                    foreach(var mode in new[]{"light","dark","high-contrast"}) foreach(var size in new[]{(900d,600d),(640d,400d)})
                                    {
                                        theme.SetMode(mode=="dark"?"dark":"light");theme.Apply(mode=="high-contrast");dialog.Width=size.Item1;dialog.Height=size.Item2;await Layout(dialog);
                                        Check(label+mode+size.Item1+" browser actions and row stay visible",Fits(dialog,dialog.PreviewEntryButton) && Fits(dialog,dialog.ExtractSelectedButton) && table.ActualHeight>=48);
                                    }
                                    Check(label+" preview leaves extraction and library untouched",view.OutputPath=="" && before.SequenceEqual(File.ReadAllBytes(vaultPath)) && archiveBefore.SequenceEqual(File.ReadAllBytes(archive)));
                                }
                                catch(Exception error) {Check(label+"browser exception "+error,false);}
                                finally {dialog.Close();}
                            }));
                        },preparePreview:dialog=>
                        {
                            Hide(dialog);dialog.Loaded+=(_,_)=>dialog.Dispatcher.BeginInvoke((Action)(async()=>
                            {
                                try
                                {
                                    previewShown++;last=(ArchivePreviewViewModel)dialog.DataContext;await Layout(dialog);
                                    Check(label+previewShown+" modal owner and temporary cleanup before display",dialog.Owner is ArchiveContentsDialog && !Directory.GetDirectories(Path.Combine(profile,"preview-temp")).Any());
                                    if(last.IsText) Check(label+" exact text readonly and accessible",last.Text==File.ReadAllText(text) && dialog.PreviewText.IsReadOnly && UIElementAutomationPeer.CreatePeerForElement(dialog.PreviewText)!.GetName()==UiText.Get("Preview.TextName"));
                                    else Check(label+" PNG exact dimensions and frozen memory only",last.Picture?.PixelWidth==320 && last.Picture.PixelHeight==180 && last.Picture.IsFrozen);
                                    foreach(var mode in new[]{"light","dark","high-contrast"}) foreach(var size in new[]{(900d,600d),(480d,320d)})
                                    {
                                        theme.SetMode(mode=="dark"?"dark":"light");theme.Apply(mode=="high-contrast");dialog.Width=size.Item1;dialog.Height=size.Item2;await Layout(dialog);
                                        Check(label+previewShown+mode+size.Item1+" fixed actions and bounded content",Fits(dialog,dialog.ClosePreviewButton) && Fits(dialog,dialog.PreviewNotice) && (last.IsText?Fits(dialog,dialog.PreviewText):Fits(dialog,dialog.PreviewImage)));
                                        if(version=="26.04") {dialog.Opacity=1;Render(dialog,Path.Combine(screenshots,language+previewShown+mode+size.Item1+".png"));dialog.Opacity=0;}
                                    }
                                    Check(label+previewShown+" keyboard focus reaches close",dialog.ClosePreviewButton.Focus());
                                }
                                catch(Exception error) {Check(label+"preview exception "+error,false);}
                                finally {dialog.Close();}
                            }));
                        });
                        var main=new MainViewModel(baseDirectory,profile,vault,dialogs);window.DataContext=main;await main.InitializeAsync();
                        main.SelectedEngineVersion=version;main.ApplyEngineCommand.Execute(null);await main.WaitForIdleAsync();main.AddFiles([archive]);main.SelectedTask=main.Tasks.Single();
                        main.BrowseArchiveCommand.Execute(null);await main.WaitForIdleAsync();
                        Check(label+" actual browser entry completes both previews and remains unextracted",contentsShown==1 && previewShown==2 && main.IsIdle && !main.Tasks.Single().Completed && main.Tasks.Single().SelectedOutputPath is null);
                        Check(label+" APV file byte preservation",before.SequenceEqual(File.ReadAllBytes(vaultPath)));
                    }
                    {
                        var profile=Path.Combine(data,language+"-manual");Directory.CreateDirectory(profile);var path=Path.Combine(profile,"content.txt");await File.WriteAllTextAsync(path,"manual preview");
                        var archive=path+".zip";var client=new ArchiveEngineClient(Path.Combine(baseDirectory,"ArchiveEngineWorker.exe"),Path.Combine(baseDirectory,"engines/26.04/7z.dll"));await client.CreateAsync(new([new(path,"content.txt",false)],"zip",5,false),archive,"manual-secret");
                        var prompts=0;var shown=0;var vaultPath=Path.Combine(profile,"passwords.apv");var vault=new DpapiPasswordVault(vaultPath);
                        var dialogs=new WindowDialogs(window,prepareDialog:dialog=>
                        {
                            Hide(dialog);dialog.Loaded+=(_,_)=>dialog.Dispatcher.BeginInvoke((Action)(()=>
                            {
                                prompts++;Check(language+" manual preview cannot save password",dialog.SavePassword.Visibility!=Visibility.Visible && !dialog.SavePassword.IsChecked.GetValueOrDefault());
                                dialog.SecretBox.Password="manual-secret";Invoke(dialog.ContinueButton);
                            }));
                        },prepareContents:dialog=>
                        {
                            Hide(dialog);dialog.Loaded+=(_,_)=>dialog.Dispatcher.BeginInvoke((Action)(async()=>
                            {
                                try {var view=(ArchiveContentsViewModel)dialog.DataContext;dialog.EntryTable.SelectedItem=view.Rows.Single();await Layout(dialog);Invoke(dialog.PreviewEntryButton);await Layout(dialog);await view.WaitForIdleAsync();}
                                catch(Exception error) {Check(language+"manual browser exception "+error,false);}
                                finally {dialog.Close();}
                            }));
                        },preparePreview:dialog=>{Hide(dialog);dialog.Loaded+=(_,_)=>dialog.Dispatcher.BeginInvoke((Action)(()=>{shown++;Check(language+" manual preview displays exact text",((ArchivePreviewViewModel)dialog.DataContext).Text=="manual preview");dialog.Close();}));});
                        var main=new MainViewModel(baseDirectory,profile,vault,dialogs);window.DataContext=main;await main.InitializeAsync();var existed=File.Exists(vaultPath);var original=existed?File.ReadAllBytes(vaultPath):[];
                        main.AddFiles([archive]);main.SelectedTask=main.Tasks.Single();main.BrowseArchiveCommand.Execute(null);await main.WaitForIdleAsync();
                        Check(language+" manual preview prompt once and returns idle",prompts==1 && shown==1 && main.IsIdle);
                        Check(language+" manual secret never persisted",File.Exists(vaultPath)==existed && (!existed || original.SequenceEqual(File.ReadAllBytes(vaultPath))));
                    }
                    var sources=new BitmapEncoder[]{new PngBitmapEncoder(),new JpegBitmapEncoder(),new BmpBitmapEncoder(),new GifBitmapEncoder()};
                    foreach(var source in sources)
                    {
                        var pixel=BitmapSource.Create(3,2,96,96,PixelFormats.Bgra32,null,Enumerable.Repeat((byte)255,24).ToArray(),12);source.Frames.Add(BitmapFrame.Create(pixel));
                        using var bytes=new MemoryStream();source.Save(bytes);var ext=source is PngBitmapEncoder?".png":source is JpegBitmapEncoder?".jpg":source is BmpBitmapEncoder?".bmp":".gif";
                        var entry=new ArchiveEntry(0,"sample"+ext,(ulong)bytes.Length,false,false);var result=await ArchivePreviewViewModel.CreateAsync(entry,bytes.ToArray());
                        Check(language+ext+" decoder first frame and independent image",result.Picture?.PixelWidth==3 && result.Picture.PixelHeight==2 && result.Picture.IsFrozen && (!ext.Equals(".gif") || result.Details.Contains(UiText.Get("Preview.FirstFrame"))));
                        var invalid=await ArchivePreviewViewModel.CreateAsync(entry,new byte[]{1,2,3,4});Check(language+ext+" corrupt image gives local error",invalid.HasError && invalid.Picture is null);
                    }
                    var invalidText=await ArchivePreviewViewModel.CreateAsync(new(0,"binary.txt",3,false,false),[0,1,2]);Check(language+" binary text gives local error",invalidText.HasError && invalidText.Text=="");
                    var wide=BitmapSource.Create(20000,1,96,96,PixelFormats.Bgra32,null,new byte[80000],80000);var wideEncoder=new BmpBitmapEncoder();wideEncoder.Frames.Add(BitmapFrame.Create(wide));using var wideBytes=new MemoryStream();wideEncoder.Save(wideBytes);
                    var tooLarge=await ArchivePreviewViewModel.CreateAsync(new(0,"wide.bmp",(ulong)wideBytes.Length,false,false),wideBytes.ToArray());Check(language+" valid image dimensions rejected before full pixel copy",tooLarge.Picture is null && tooLarge.Message==UiText.Get("Preview.ImageTooLarge"));
                    using var cancellation=new CancellationTokenSource();var cancelled=false;
                    var closingModel=new ArchiveContentsViewModel("synthetic.zip",[new(0,"file.txt",1,false,false)],cancel:()=>cancellation.Cancel(),preview:async(_,_)=>{try {await Task.Delay(Timeout.Infinite,cancellation.Token);}catch(OperationCanceledException) {cancelled=true;}});
                    closingModel.CurrentRow=closingModel.Rows.Single();var closingDialog=new ArchiveContentsDialog(closingModel);Hide(closingDialog);closingDialog.Show();closingModel.PreviewCommand.Execute(null);closingDialog.Close();await closingModel.WaitForIdleAsync();await Layout(closingDialog);
                    Check(language+" closing browser cancels and waits for active preview",cancelled && !closingModel.IsWorking && !closingDialog.IsVisible);
                }
                finally {window.Close();}
            }
        }
        catch(Exception error) {Check("exception "+error,false);}
        finally {UiText.Configure("zh-CN");theme.SetMode("light");theme.Apply(SystemParameters.HighContrast);if(Directory.Exists(data)) Directory.Delete(data,true);}
        var passed=checks.Count>0 && checks.Values.All(v=>v);
        await File.WriteAllTextAsync(Path.Combine(root,"artifacts/preview-ui-validation.json"),JsonSerializer.Serialize(new{passed,total=checks.Count,checks,syntheticOnly=true,actualVisibleNarratorTested=false},new JsonSerializerOptions{WriteIndented=true}));return passed;
    }
    private static void Hide(Window window) {window.WindowStartupLocation=WindowStartupLocation.Manual;window.Left=-32000;window.Top=-32000;window.Opacity=0;window.ShowActivated=false;window.ShowInTaskbar=false;}
    private static void Render(Window window,string path)
    {
        var content=(FrameworkElement)window.Content;var width=(int)Math.Ceiling(content.ActualWidth+content.Margin.Left+content.Margin.Right);var height=(int)Math.Ceiling(content.ActualHeight+content.Margin.Top+content.Margin.Bottom);
        var bitmap=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);var background=new DrawingVisual();
        using(var drawing=background.RenderOpen()) drawing.DrawRectangle(window.Background,null,new Rect(0,0,width,height));
        bitmap.Render(background);bitmap.Render(content);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var stream=File.Create(path);encoder.Save(stream);
    }
    private static void Invoke(Button button)=>((IInvokeProvider)UIElementAutomationPeer.CreatePeerForElement(button)!.GetPattern(PatternInterface.Invoke)).Invoke();
    private static async Task Layout(Window window)=>await window.Dispatcher.InvokeAsync(window.UpdateLayout,DispatcherPriority.ApplicationIdle);
    private static bool Fits(Window window,FrameworkElement element) {if(!element.IsVisible || element.ActualHeight<=0) return false;var client=(FrameworkElement)window.Content;var bounds=element.TransformToAncestor(client).TransformBounds(new Rect(element.RenderSize));return bounds.Top>=-1 && bounds.Left>=-1 && bounds.Right<=client.ActualWidth+1 && bounds.Bottom<=client.ActualHeight+1;}
}
