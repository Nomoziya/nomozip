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
internal static class DesktopFormatValidation
{
    internal static async Task<bool> RunAsync(MainWindow window,ThemeResources theme,string root,string baseDirectory)
    {
        var data=Path.Combine(root,"artifacts/fu-"+Guid.NewGuid().ToString("N")[..8]);Directory.CreateDirectory(data);
        var screenshots=Path.Combine(root,"artifacts/formats-preview");Directory.CreateDirectory(screenshots);
        var checks=new Dictionary<string,bool>();void Check(string name,bool value)=>checks[name]=value;
        var source=Path.Combine(data,"内容.txt");await File.WriteAllTextAsync(source,"Formats 中文 exact bytes");
        var second=Path.Combine(data,"extra.txt");await File.WriteAllTextAsync(second,"extra");
        var sourceFolder=Path.Combine(data,"文件夹");Directory.CreateDirectory(sourceFolder);await File.WriteAllTextAsync(Path.Combine(sourceFolder,"child.txt"),"child");
        try
        {
            foreach(var version in new[]{"26.04","26.03"})
            {
                var prompts=0;var vaultPath=Path.Combine(data,version+".apv");var vault=new DpapiPasswordVault(vaultPath);
                var dialogs=new WindowDialogs(window,confirmEngineUpdate:(_,_)=>false,prepareDialog:dialog=>
                {
                    dialog.WindowStartupLocation=WindowStartupLocation.Manual;dialog.Left=-32000;dialog.Top=-32000;dialog.Opacity=0;dialog.ShowActivated=false;dialog.ShowInTaskbar=false;
                    dialog.Loaded+=(_,_)=>dialog.Dispatcher.BeginInvoke(()=>{prompts++;dialog.SecretBox.Password="Synthetic-password";dialog.ConfirmationBox.Password=dialog.SecretBox.Password;dialog.SavePassword.IsChecked=true;Invoke(dialog.ContinueButton);});
                });
                var model=new MainViewModel(baseDirectory,Path.Combine(data,"state-"+version),vault,dialogs);window.DataContext=model;
                await model.InitializeAsync();model.SelectedEngineVersion=version;model.ApplyEngineCommand.Execute(null);await model.WaitForIdleAsync();
                model.Page=3;model.AddCompressionSources([source]);model.CompressionOutput=Path.Combine(data,version+".7z");await Layout(window);
                var view=(CompressionView)window.FindName("CompressionPage");var format=(ComboBox)view.FindName("FormatBox");
                var create=(Button)view.FindName("CreateButton");var level=(ComboBox)view.FindName("LevelBox");var encryption=(CheckBox)view.FindName("EncryptBox");var volume=(ComboBox)view.FindName("VolumeOptionBox");var sfx=(CheckBox)view.FindName("SfxBox");var error=(TextBlock)view.FindName("FormatSourceError");
                Check(version+" accessible format chooser exposes all five formats",format.Items.Count==5 && format.Items.Cast<ComboBoxItem>().Select(item=>item.Content.ToString()).SequenceEqual(CompressionFormats.All.Select(item=>item.Label)) && UIElementAutomationPeer.CreatePeerForElement(format)!.GetName()=="压缩格式");
                model.CompressionEncrypt=true;model.SetCompressionPasswordCommand.Execute(null);await Layout(window);await model.WaitForIdleAsync();
                Check(version+" synthetic password staged before switching format",prompts==1 && model.CompressionPasswordStatus.Contains("已设置"));
                model.CompressionVolumeOption=1;await Select(format,2);await Layout(window);
                Check(version+" TAR clears password and volumes changes extension",model.CompressionFormat==2 && !model.CompressionEncrypt && model.CompressionPasswordStatus.Contains("尚未设置") && model.CompressionVolumeOption==0 && model.CompressionOutput.EndsWith(".tar"));
                Check(version+" TAR disables unsupported controls with visible explanation",!level.IsEnabled && !encryption.IsEnabled && !volume.IsEnabled && !sfx.IsEnabled && model.CompressionHint.Contains("不压缩"));
                foreach(var index in new[]{2,3,4})
                {
                    model.CompressionSources.Clear();model.AddCompressionSources([source]);await Select(format,index);await Layout(window);
                    var options=CompressionFormats.All[index];model.CompressionOutput=Path.Combine(data,version+"-"+options.Name+"-内容.txt"+options.Extension);await Layout(window);
                    Check(version+" "+options.Name+" control availability follows format",level.IsEnabled==options.CompressionLevel && !encryption.IsEnabled && !volume.IsEnabled && !sfx.IsEnabled);
                    Check(version+" "+options.Name+" valid single source enables actual command",create.IsEnabled && error.Visibility==Visibility.Collapsed);
                    model.AddCompressionSources([second]);await Layout(window);
                    Check(version+" "+options.Name+" multiple source constraint shown",options.SingleFile?!create.IsEnabled && error.IsVisible && error.Text.Contains("一个文件"):create.IsEnabled && error.Visibility==Visibility.Collapsed);
                    model.CompressionSources.Clear();model.AddCompressionSources([sourceFolder]);await Layout(window);
                    Check(version+" "+options.Name+" folder constraint shown",options.SingleFile?!create.IsEnabled && error.IsVisible:create.IsEnabled);
                    model.CompressionSources.Clear();model.AddCompressionSources([source]);await Layout(window);model.Page=3;await Layout(window);Invoke(create);await Layout(window);await model.WaitForIdleAsync();await Layout(window);
                    var client=new ArchiveEngineClient(Path.Combine(baseDirectory,"ArchiveEngineWorker.exe"),Path.Combine(baseDirectory,"engines",version,"7z.dll"));
                    var archive=model.CompressionOutput;var listed=await client.ListAsync(archive);
                    Check(version+" "+options.Name+" actual button creates tested readable archive",model.IsIdle && model.CompressionMessage.Contains("已创建并校验") && listed.IsSuccess && listed.Items.Count==1 && !listed.Items[0].Encrypted);
                    // TAR stores the source filename; stream formats use the output basename.
                    Check(version+" "+options.Name+" exact displayed extraction path",listed.IsSuccess && listed.Items.Single().Path==(options.Name=="tar"?"内容.txt":Path.GetFileNameWithoutExtension(archive)));
                    model.Page=0;model.Tasks.Clear();model.OutputPath=Path.Combine(data,version+"-"+options.Name+"-extract");model.AddFiles([archive]);model.ExtractCommand.Execute(null);await model.WaitForIdleAsync();await Layout(window);
                    Check(version+" "+options.Name+" existing extraction page exact bytes",model.Tasks.Single().Completed && File.ReadAllText(Directory.GetFiles(model.OutputPath,"*",SearchOption.AllDirectories).Single())=="Formats 中文 exact bytes");
                    model.Page=3;await Layout(window);var before=File.ReadAllBytes(archive);model.Page=3;await Layout(window);Invoke(create);await Layout(window);await model.WaitForIdleAsync();
                    Check(version+" "+options.Name+" declined replacement preserves archive",before.SequenceEqual(File.ReadAllBytes(archive)) && model.CompressionMessage.Contains("已保留"));
                }
                await Select(format,3);model.AddCompressionSources([second]);await Layout(window);
                foreach(var contrast in new[]{false,true}) foreach(var dimensions in new[]{(1120d,740d),(760d,540d)})
                {
                    theme.Apply(contrast);window.Width=dimensions.Item1;window.Height=dimensions.Item2;await Layout(window);
                    var outer=(ScrollViewer)view.FindName("CompressionViewport");var settings=(ScrollViewer)view.FindName("SettingsScroll");outer.ScrollToBottom();settings.ScrollToBottom();await Layout(window);
                    Check($"{version} visible constraint and fixed action {dimensions.Item1} {contrast}",Fits(view,create) && Fits(view,error) && error.ActualHeight>=error.FontSize);
                    await Capture(view,Path.Combine(screenshots,$"{version}-{dimensions.Item1}-{contrast}.png"));
                }
                theme.Apply(false);window.Width=1120;window.Height=740;model.CompressionSources.Clear();model.AddCompressionSources([source]);
                foreach(var index in new[]{0,1})
                {
                    await Select(format,index);model.CompressionEncrypt=true;model.CompressionOutput=Path.Combine(data,version+"-return"+CompressionFormats.All[index].Extension);await Layout(window);model.Page=3;await Layout(window);Invoke(create);await Layout(window);await model.WaitForIdleAsync();await Layout(window);
                    var client=new ArchiveEngineClient(Path.Combine(baseDirectory,"ArchiveEngineWorker.exe"),Path.Combine(baseDirectory,"engines",version,"7z.dll"));
                    Check(version+" return to "+CompressionFormats.All[index].Label+" encryption still works",encryption.IsEnabled && volume.IsEnabled && level.IsEnabled && (await client.TestAsync(model.CompressionOutput,"Synthetic-password")).IsSuccess && (await client.TestAsync(model.CompressionOutput)).NeedsPassword);
                }
                Check(version+" stream formats never prompted or saved password",prompts==3 && (await vault.ReadAsync()).Count==2);
            }
        }
        catch(Exception error) {checks["exception "+error]=false;}
        finally {theme.Apply(SystemParameters.HighContrast);}
        var passed=checks.Count>0 && checks.Values.All(value=>value);await File.WriteAllTextAsync(Path.Combine(root,"artifacts/format-ui-validation.json"),JsonSerializer.Serialize(new{passed,total=checks.Count,checks,syntheticOnly=true},new JsonSerializerOptions{WriteIndented=true}));return passed;
    }
    private static Task Select(ComboBox box,int index)
    {
        // This is the same data-item peer created by ComboBoxAutomationPeer.
        // The FrameworkElement wrapper peer alone has no selection pattern.
        var owner=(ComboBoxAutomationPeer)UIElementAutomationPeer.CreatePeerForElement(box)!;
        var peer=new ListBoxItemAutomationPeer(box.Items[index],owner);
        ((ISelectionItemProvider)peer.GetPattern(PatternInterface.SelectionItem)!).Select();
        return Task.CompletedTask;
    }
    private static void Invoke(Button button)=>((IInvokeProvider)UIElementAutomationPeer.CreatePeerForElement(button)!.GetPattern(PatternInterface.Invoke)).Invoke();
    private static async Task Layout(Window window)=>await window.Dispatcher.InvokeAsync(window.UpdateLayout,DispatcherPriority.ApplicationIdle);
    private static bool Fits(FrameworkElement parent,FrameworkElement item) {var bounds=item.TransformToAncestor(parent).TransformBounds(new Rect(item.RenderSize));return bounds.Top>=0 && bounds.Bottom<=parent.ActualHeight+1 && bounds.Left>=0 && bounds.Right<=parent.ActualWidth+1;}
    private static async Task Capture(FrameworkElement content,string path)
    {
        var raw=new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth),(int)Math.Ceiling(content.ActualHeight),96,96,PixelFormats.Pbgra32);raw.Render(content);
        var visual=new DrawingVisual();using(var drawing=visual.RenderOpen()) {drawing.DrawRectangle((Brush)content.FindResource("SurfaceBrush"),null,new Rect(content.RenderSize));drawing.DrawImage(raw,new Rect(content.RenderSize));}
        var bitmap=new RenderTargetBitmap(raw.PixelWidth,raw.PixelHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(visual);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var output=File.Create(path);encoder.Save(output);
    }
}
