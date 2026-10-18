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
internal static class DesktopVolumeValidation
{
    internal static async Task<bool> RunAsync(MainWindow window,ThemeResources theme,string root,string baseDirectory)
    {
        var data=Path.Combine(root,"artifacts/vu-"+Guid.NewGuid().ToString("N")[..8]);Directory.CreateDirectory(data);
        var screenshots=Path.Combine(root,"artifacts/volume-preview");Directory.CreateDirectory(screenshots);
        var checks=new Dictionary<string,bool>();void Check(string name,bool value)=>checks[name]=value;
        var source=Path.Combine(data,"样本.bin");var bytes=new byte[1572864];new Random(21).NextBytes(bytes);await File.WriteAllBytesAsync(source,bytes);
        try
        {
            foreach(var version in new[]{"26.04","26.03"})
            {
                int prompts=0;var vault=new DpapiPasswordVault(Path.Combine(data,version+".apv"));
                var dialogs=new WindowDialogs(window,prepareDialog:dialog=>
                {
                    dialog.WindowStartupLocation=WindowStartupLocation.Manual;dialog.Left=-32000;dialog.Top=-32000;dialog.Opacity=0;dialog.ShowActivated=false;dialog.ShowInTaskbar=false;
                    dialog.Loaded+=(_,_)=>dialog.Dispatcher.BeginInvoke(()=>
                    {
                        prompts++;if(prompts>1) dialog.SavedBox.SelectedIndex=0;
                        else {dialog.SecretBox.Password="volume-password";dialog.ConfirmationBox.Password=dialog.SecretBox.Password;dialog.SavePassword.IsChecked=true;dialog.NameBox.Text="volume fixture";}
                        Invoke(dialog.ContinueButton);
                    });
                });
                var model=new MainViewModel(baseDirectory,Path.Combine(data,"state-"+version),vault,dialogs);window.DataContext=model;await model.InitializeAsync();
                model.SelectedEngineVersion=version;model.ApplyEngineCommand.Execute(null);await model.WaitForIdleAsync();model.Page=3;model.AddCompressionSources([source]);
                var view=(CompressionView)window.FindName("CompressionPage");var option=(ComboBox)view.FindName("VolumeOptionBox");var size=(TextBox)view.FindName("VolumeSizeBox");var unit=(ComboBox)view.FindName("VolumeUnitBox");var create=(Button)view.FindName("CreateButton");
                model.CompressionOutput=Path.Combine(data,version+"-default.7z");await Layout(window);
                Check(version+" default retains ordinary archive",option.SelectedIndex==0 && !size.IsVisible && model.CompressionVolumeHint=="" && model.CreateArchiveCommand.CanExecute(null));
                Check(version+" volume controls accessible",UIElementAutomationPeer.CreatePeerForElement(option)!.IsKeyboardFocusable() && UIElementAutomationPeer.CreatePeerForElement(option)!.GetName().Contains("分卷"));
                option.SelectedIndex=4;await Layout(window);
                foreach(var invalid in new[]{"","0","-1","1.5","text","1048577"})
                {
                    size.Text=invalid;await Layout(window);Check(version+" invalid size blocked "+invalid,!model.CreateArchiveCommand.CanExecute(null) && model.CompressionVolumeError.Length>0);
                }
                size.Text="1";unit.SelectedIndex=0;await Layout(window);
                Check(version+" custom recovers command and exposes units",model.CreateArchiveCommand.CanExecute(null) && model.CompressionVolumeError=="" && size.IsVisible && unit.IsVisible && UIElementAutomationPeer.CreatePeerForElement(size)!.IsKeyboardFocusable());
                unit.SelectedIndex=1;await Layout(window);Check(version+" GiB value bound",model.CompressionVolumeUnit==1 && VolumeSizeOptions.TryGetBytes(model.CompressionVolumeOption,model.CompressionVolumeCustom,model.CompressionVolumeUnit,out var gib) && gib==1073741824);
                unit.SelectedIndex=0;
                foreach(var contrast in new[]{false,true}) foreach(var dimensions in new[]{(1120d,740d),(760d,540d)})
                {
                    theme.Apply(contrast);window.Width=dimensions.Item1;window.Height=dimensions.Item2;await Layout(window);
                    var scroll=(ScrollViewer)view.FindName("CompressionViewport");scroll.ScrollToBottom();await Layout(window);
                    Check($"{version} volume labels and button fit {dimensions.Item1} {contrast}",Fits(view,create) && Fits(view,size) && Fits(view,option) && size.ActualWidth>=40);
                    await Capture(view,Path.Combine(screenshots,$"{version}-{dimensions.Item1}-{contrast}.png"));
                }
                theme.Apply(false);window.Width=1120;window.Height=740;await Layout(window);
                foreach(var format in new[]{0,1})
                {
                    model.CompressionFormat=format;model.CompressionOutput=Path.Combine(data,$"{version}-{format}."+(format==0?"7z":"zip"));model.CompressionEncrypt=true;option.SelectedIndex=4;size.Text="1";unit.SelectedIndex=0;await Layout(window);
                    model.Page=3;await Layout(window);Invoke(create);await Layout(window);await model.WaitForIdleAsync();await Layout(window);
                    var first=model.CompressionOutput+".001";var records=await vault.ReadAsync();
                    Check($"{version} {format} actual compression button completes two parts",File.Exists(first) && File.Exists(model.CompressionOutput+".002") && !File.Exists(model.CompressionOutput) && model.CompressionMessage.Contains("2 个分卷") && model.IsIdle);
                    Check($"{version} {format} successful password associated with first part",records.Count==1 && await vault.GetAssociatedPasswordAsync(first)==records.Single().Id && await vault.GetAssociatedPasswordAsync(model.CompressionOutput)==null);
                    Check($"{version} {format} result folder command enabled",model.OpenCreatedArchiveFolderCommand.CanExecute(null));
                    var client=new ArchiveEngineClient(Path.Combine(baseDirectory,"ArchiveEngineWorker.exe"),Path.Combine(baseDirectory,"engines",version,"7z.dll"));var listed=await client.ListAsync(first,"volume-password");
                    var browser=new ArchiveContentsViewModel(first,listed.Items,format:listed.Format,edit:(_,_,_,_)=>Task.FromResult<ArchiveEditOutcome?>(null));
                    Check($"{version} {format} actual volume contents disable editing",listed.Items.Count==1 && !browser.EditingAvailable && !browser.AddFilesCommand.CanExecute(null));
                    var before=Directory.GetFiles(data,Path.GetFileName(model.CompressionOutput)+".*").ToDictionary(path=>path,path=>File.ReadAllBytes(path));var vaultBefore=File.ReadAllBytes(Path.Combine(data,version+".apv"));
                    model.Page=3;await Layout(window);Invoke(create);await Layout(window);await model.WaitForIdleAsync();
                    Check($"{version} {format} existing parts retained without password prompt",prompts==format+1 && before.All(pair=>pair.Value.SequenceEqual(File.ReadAllBytes(pair.Key))) && model.CompressionMessage.Contains("已保留") && vaultBefore.SequenceEqual(File.ReadAllBytes(Path.Combine(data,version+".apv"))));
                    model.Page=0;model.Tasks.Clear();model.OutputPath=Path.Combine(data,$"ex-{version}-{format}");model.AddFiles([first]);model.ExtractCommand.Execute(null);await model.WaitForIdleAsync();
                    Check($"{version} {format} saved password automatically extracts exact original",model.Tasks.Single().Completed && bytes.SequenceEqual(File.ReadAllBytes(Path.Combine(model.OutputPath,"样本.bin"))) && prompts==format+1);
                    model.Page=3;
                }
                model.CompressionEncrypt=false;option.SelectedIndex=0;model.CompressionOutput=Path.Combine(data,version+"-ordinary.7z");model.CompressionFormat=0;await Layout(window);model.Page=3;await Layout(window);Invoke(create);await Layout(window);await model.WaitForIdleAsync();
                Check(version+" returning to no split creates ordinary archive",File.Exists(model.CompressionOutput) && !File.Exists(model.CompressionOutput+".001") && !size.IsVisible);
            }
            foreach(var preset in new[]{(0,0UL),(1,104857600UL),(2,1073741824UL),(3,4294967296UL)}) Check("preset bytes "+preset.Item1,VolumeSizeOptions.TryGetBytes(preset.Item1,"ignored",0,out var result) && result==preset.Item2);
        }
        catch(Exception error) {checks["exception "+error]=false;}
        finally {theme.Apply(SystemParameters.HighContrast);}
        var passed=checks.Count>0 && checks.Values.All(value=>value);await File.WriteAllTextAsync(Path.Combine(root,"artifacts/volume-ui-validation.json"),JsonSerializer.Serialize(new{passed,total=checks.Count,checks,syntheticOnly=true},new JsonSerializerOptions{WriteIndented=true}));return passed;
    }
    private static void Invoke(Button button)=>((IInvokeProvider)UIElementAutomationPeer.CreatePeerForElement(button)!.GetPattern(PatternInterface.Invoke)).Invoke();
    private static async Task Layout(Window window)=>await window.Dispatcher.InvokeAsync(window.UpdateLayout,DispatcherPriority.ApplicationIdle);
    private static bool Fits(FrameworkElement parent,FrameworkElement item) {var bounds=item.TransformToAncestor(parent).TransformBounds(new Rect(item.RenderSize));return bounds.Top>=0 && bounds.Bottom<=parent.ActualHeight+1 && bounds.Left>=0 && bounds.Right<=parent.ActualWidth+1;}
    private static async Task Capture(FrameworkElement content,string path)
    {
        await content.Dispatcher.InvokeAsync(content.UpdateLayout,DispatcherPriority.ApplicationIdle);
        var raw=new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth),(int)Math.Ceiling(content.ActualHeight),96,96,PixelFormats.Pbgra32);raw.Render(content);
        var visual=new DrawingVisual();using(var drawing=visual.RenderOpen()) {drawing.DrawRectangle((Brush)content.FindResource("SurfaceBrush"),null,new Rect(content.RenderSize));drawing.DrawImage(raw,new Rect(content.RenderSize));}
        var bitmap=new RenderTargetBitmap(raw.PixelWidth,raw.PixelHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(visual);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var output=File.Create(path);encoder.Save(output);
    }
}
