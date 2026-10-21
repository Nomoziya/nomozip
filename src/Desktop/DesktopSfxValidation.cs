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
internal static class DesktopSfxValidation
{
    internal static async Task<bool> RunAsync(MainWindow window,ThemeResources theme,string root,string baseDirectory)
    {
        var data=Path.Combine(root,"artifacts/su-"+Guid.NewGuid().ToString("N")[..8]);Directory.CreateDirectory(data);
        var screenshots=Path.Combine(root,"artifacts/sfx-preview");Directory.CreateDirectory(screenshots);var checks=new Dictionary<string,bool>();void Check(string name,bool value)=>checks[name]=value;
        var source=Path.Combine(data,"样本.txt");await File.WriteAllTextAsync(source,"SFX 中文内容");
        try
        {
            foreach(var version in new[]{"26.04","26.03"})
            {
                int prompts=0;bool replace=false;var vaultPath=Path.Combine(data,version+".apv");var vault=new DpapiPasswordVault(vaultPath);
                var dialogs=new WindowDialogs(window,confirmEngineUpdate:(_,_)=>replace,prepareDialog:dialog=>
                {
                    dialog.WindowStartupLocation=WindowStartupLocation.Manual;dialog.Left=-32000;dialog.Top=-32000;dialog.Opacity=0;dialog.ShowActivated=false;dialog.ShowInTaskbar=false;
                    dialog.Loaded+=(_,_)=>dialog.Dispatcher.BeginInvoke(()=>
                    {
                        prompts++;if(prompts>1) dialog.SavedBox.SelectedIndex=0;else {dialog.SecretBox.Password="SFX 密码";dialog.ConfirmationBox.Password=dialog.SecretBox.Password;dialog.SavePassword.IsChecked=true;dialog.NameBox.Text="SFX fixture";}
                        Invoke(dialog.ContinueButton);
                    });
                });
                var model=new MainViewModel(baseDirectory,Path.Combine(data,"state-"+version),vault,dialogs);window.DataContext=model;await model.InitializeAsync();model.SelectedEngineVersion=version;model.ApplyEngineCommand.Execute(null);await model.WaitForIdleAsync();
                model.Page=3;model.AddCompressionSources([source]);model.CompressionOutput=Path.Combine(data,version+".7z");await Layout(window);
                var view=(CompressionView)window.FindName("CompressionPage");view.AdvancedOptions.IsExpanded=true;var sfx=(CheckBox)view.FindName("SfxBox");var create=(Button)view.FindName("CreateButton");
                Check(version+" default ordinary compression and SFX available",!model.CompressionSfx && sfx.IsEnabled && model.CanUseSfx);
                Check(version+" accessible SFX checkbox",UIElementAutomationPeer.CreatePeerForElement(sfx)!.IsKeyboardFocusable() && UIElementAutomationPeer.CreatePeerForElement(sfx)!.GetName().Contains("自解压"));
                Toggle(sfx);await Layout(window);Check(version+" actual toggle changes extension and explanation",model.CompressionSfx && model.CompressionOutput.EndsWith(".exe") && model.CompressionSfxHint.Contains("不自动运行"));
                model.CompressionFormat=1;await Layout(window);Check(version+" ZIP disables SFX and corrects path",!model.CompressionSfx && !sfx.IsEnabled && model.CompressionOutput.EndsWith(".zip") && model.CompressionSfxHint.Contains("仅支持"));
                model.CompressionFormat=0;model.CompressionVolumeOption=1;await Layout(window);Check(version+" split mode disables SFX with explanation",!sfx.IsEnabled && model.CompressionSfxHint.Contains("分卷"));
                model.CompressionVolumeOption=0;await Layout(window);Toggle(sfx);model.CompressionVolumeOption=1;await Layout(window);Check(version+" selecting split clears SFX and restores 7z path",!model.CompressionSfx && model.CompressionOutput.EndsWith(".7z"));
                model.CompressionVolumeOption=0;await Layout(window);Toggle(sfx);model.CompressionEncrypt=true;await Layout(window);
                foreach(var contrast in new[]{false,true}) foreach(var dimensions in new[]{(1120d,740d),(760d,540d)})
                {
                    theme.Apply(contrast);window.Width=dimensions.Item1;window.Height=dimensions.Item2;await Layout(window);
                    var outer=(ScrollViewer)view.FindName("CompressionViewport");var settings=(ScrollViewer)view.FindName("SettingsScroll");outer.ScrollToBottom();settings.ScrollToBottom();await Layout(window);
                    Check($"{version} SFX and primary action fit {dimensions.Item1} {contrast}",Fits(view,create) && Fits(view,sfx) && sfx.ActualWidth>=180);
                    await Capture(view,Path.Combine(screenshots,$"{version}-{dimensions.Item1}-{contrast}.png"));
                }
                theme.Apply(false);window.Width=1120;window.Height=740;await Layout(window);model.Page=3;await Layout(window);Invoke(create);await Layout(window);await model.WaitForIdleAsync();await Layout(window);
                var exe=model.CompressionOutput;var before=File.ReadAllBytes(exe);var records=await vault.ReadAsync();
                Check(version+" actual create button produces verified encrypted EXE",model.CompressionMessage.Contains("已创建并校验自解压") && records.Count==1 && await vault.GetAssociatedPasswordAsync(exe)==records.Single().Id && model.OpenCreatedArchiveFolderCommand.CanExecute(null));
                Check(version+" created password cleared from model",model.CompressionPasswordStatus.Contains("尚未设置"));
                var modulePath=Path.Combine(baseDirectory,"engines",version,"7z.sfx");var officialModule=File.ReadAllBytes(modulePath);var vaultSnapshot=File.ReadAllBytes(vaultPath);
                try
                {
                    var corrupt=officialModule.ToArray();corrupt[100]^=1;File.WriteAllBytes(modulePath,corrupt);model.Page=3;await Layout(window);Invoke(create);await Layout(window);await model.WaitForIdleAsync();await Layout(window);
                    Check(version+" actual damaged module blocks creation without vault change",model.IsIdle && model.CompressionMessage.Contains("无法通过校验") && before.SequenceEqual(File.ReadAllBytes(exe)) && vaultSnapshot.SequenceEqual(File.ReadAllBytes(vaultPath)));
                }
                finally {File.WriteAllBytes(modulePath,officialModule);}
                var vaultBefore=File.ReadAllBytes(vaultPath);model.Page=3;await Layout(window);Invoke(create);await Layout(window);await model.WaitForIdleAsync();
                Check(version+" replacement declined preserves original EXE and vault",before.SequenceEqual(File.ReadAllBytes(exe)) && vaultBefore.SequenceEqual(File.ReadAllBytes(vaultPath)) && model.CompressionMessage.Contains("已保留"));
                await Layout(window);replace=true;model.Page=3;await Layout(window);Invoke(create);await Layout(window);await model.WaitForIdleAsync();await Layout(window);Check(version+" explicit replacement succeeds",model.CompressionMessage.Contains("已创建并校验自解压"));
                var client=new ArchiveEngineClient(Path.Combine(baseDirectory,"ArchiveEngineWorker.exe"),Path.Combine(baseDirectory,"engines",version,"7z.dll"));var listed=await client.ListAsync(exe,"SFX 密码");
                var browser=new ArchiveContentsViewModel(exe,listed.Items,format:listed.Format,edit:(_,_,_,_)=>Task.FromResult<ArchiveEditOutcome?>(null));
                Check(version+" SFX content listing remains read only",listed.IsSuccess && listed.Items.Single().Path=="样本.txt" && !browser.EditingAvailable);
                var edit=await client.UpdateAsync(exe,exe+"-edit",new(listed.Items,[],[],false),"SFX 密码");Check(version+" actual SFX prefix refuses editing",edit.Code=="archive_edit_container_read_only" && !File.Exists(exe+"-edit"));
                model.Page=0;model.Tasks.Clear();model.OutputPath=Path.Combine(data,"ex-"+version);model.AddFiles([exe]);model.ExtractCommand.Execute(null);await model.WaitForIdleAsync();
                Check(version+" EXE automatically extracts via saved password without running it",model.Tasks.Single().Completed && File.ReadAllText(Path.Combine(model.OutputPath,"样本.txt"))=="SFX 中文内容" && prompts==2);
                model.Page=3;await Layout(window);Toggle(sfx);model.CompressionEncrypt=false;await Layout(window);model.Page=3;await Layout(window);Invoke(create);await Layout(window);await model.WaitForIdleAsync();Check(version+" returning to ordinary compression works",model.CompressionOutput.EndsWith(".7z") && File.Exists(model.CompressionOutput));
            }
        }
        catch(Exception error) {checks["exception "+error]=false;}
        finally {theme.Apply(SystemParameters.HighContrast);}
        var passed=checks.Count>0 && checks.Values.All(value=>value);await File.WriteAllTextAsync(Path.Combine(root,"artifacts/sfx-ui-validation.json"),JsonSerializer.Serialize(new{passed,total=checks.Count,checks,syntheticOnly=true},new JsonSerializerOptions{WriteIndented=true}));return passed;
    }
    private static void Invoke(Button button)=>((IInvokeProvider)UIElementAutomationPeer.CreatePeerForElement(button)!.GetPattern(PatternInterface.Invoke)).Invoke();
    private static void Toggle(CheckBox box)=>((IToggleProvider)UIElementAutomationPeer.CreatePeerForElement(box)!.GetPattern(PatternInterface.Toggle)).Toggle();
    private static async Task Layout(Window window)=>await window.Dispatcher.InvokeAsync(window.UpdateLayout,DispatcherPriority.ApplicationIdle);
    private static bool Fits(FrameworkElement parent,FrameworkElement item) {var bounds=item.TransformToAncestor(parent).TransformBounds(new Rect(item.RenderSize));return bounds.Top>=0 && bounds.Bottom<=parent.ActualHeight+1 && bounds.Left>=0 && bounds.Right<=parent.ActualWidth+1;}
    private static async Task Capture(FrameworkElement content,string path)
    {
        await content.Dispatcher.InvokeAsync(content.UpdateLayout,DispatcherPriority.ApplicationIdle);var raw=new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth),(int)Math.Ceiling(content.ActualHeight),96,96,PixelFormats.Pbgra32);raw.Render(content);
        var visual=new DrawingVisual();using(var drawing=visual.RenderOpen()) {drawing.DrawRectangle((Brush)content.FindResource("SurfaceBrush"),null,new Rect(content.RenderSize));drawing.DrawImage(raw,new Rect(content.RenderSize));}
        var bitmap=new RenderTargetBitmap(raw.PixelWidth,raw.PixelHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(visual);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var output=File.Create(path);encoder.Save(output);
    }
}
