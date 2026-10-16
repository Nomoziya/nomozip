using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Threading;
using ArchivePassword.Core;
using ArchivePassword.Infrastructure;

namespace ArchivePassword.Desktop;
internal static class DesktopConversionValidation
{
    internal static async Task<bool> RunAsync(MainWindow startup,ThemeResources theme,string root,string baseDirectory)
    {
        var data=Path.Combine(root,"artifacts/cu-"+Guid.NewGuid().ToString("N")[..8]);Directory.CreateDirectory(data);
        var previews=Path.Combine(root,"artifacts/conversion-preview");Directory.CreateDirectory(previews);
        var checks=new Dictionary<string,bool>();void Check(string name,bool value)=>checks.Add(name,value);
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
                        var label=language+" "+version+" ";
                        var source=Path.Combine(data,language+version+".7z");var content=source+".txt";await File.WriteAllTextAsync(content,"exact 中文 conversion bytes");
                        var client=new ArchiveEngineClient(Path.Combine(baseDirectory,"ArchiveEngineWorker.exe"),Path.Combine(baseDirectory,"engines",version,"7z.dll"));
                        await client.CreateAsync(new([new(content,"目录/原文件.txt",false)],"7z",5,true),source,"source-secret");var before=File.ReadAllBytes(source);
                        var vault=new DpapiPasswordVault(source+".apv");await vault.SaveAsync(new(Guid.NewGuid(),"source fixture","source-secret","",true,DateTimeOffset.UtcNow));var vaultBefore=File.ReadAllBytes(source+".apv");
                        bool approved=false;int confirmations=0,shown=0;MainViewModel? main=null;
                        var dialogs=new WindowDialogs(window,confirmConversionReplace:_=>{confirmations++;return approved;},prepareConversion:dialog=>
                        {
                            dialog.WindowStartupLocation=WindowStartupLocation.Manual;dialog.Left=-32000;dialog.Top=-32000;dialog.Opacity=0;dialog.ShowActivated=false;dialog.ShowInTaskbar=false;
                            dialog.Loaded+=(_,_)=>dialog.Dispatcher.BeginInvoke((Action)(async()=>
                            {
                                shown++;var view=(ArchiveConversionViewModel)dialog.DataContext;
                                async Task Start() {Invoke(dialog.StartButton);await Layout(dialog);await view.WaitForIdleAsync();await Layout(dialog);}
                                try
                                {
                                    Check(label+" real modal holds main busy",main!.IsBusy && !main.ConvertArchiveCommand.CanExecute(null));
                                    Check(label+" translated title and accessible controls",dialog.Title==UiText.Get("Conversion.Title") && new ButtonAutomationPeer(dialog.StartButton).GetName()==UiText.Get("Conversion.Start") && new PasswordBoxAutomationPeer(dialog.SecretBox).GetName()==UiText.Get("Conversion.Password"));
                                    Check(label+" explicit clear output default and safe suggested path",!view.Encrypt && view.Output!=source && view.Output.EndsWith("-converted.7z") && dialog.SecretBox.IsEnabled==false);
                                    var output=Path.Combine(data,language+version+"-ui.zip");view.Output=output;view.Format="zip";await Layout(dialog);
                                    await Start();
                                    Check(label+" actual plain ZIP conversion with saved source password",view.Outcome?.Engine.IsSuccess==true && view.Message==UiText.Get("Conversion.Success") && (await client.TestAsync(output)).IsSuccess && (await client.ListAsync(output)).Items.Any(item=>item.Path.Replace('\\','/')=="目录/原文件.txt"));
                                    Check(label+" original task and original bytes preserved",before.SequenceEqual(File.ReadAllBytes(source)) && main.Tasks.Count==1 && !main.Tasks[0].Completed);
                                    view.Encrypt=true;await Layout(dialog);dialog.SecretBox.Password="new-secret";dialog.ConfirmBox.Password="different";await Start();
                                    Check(label+" mismatched new password visible and cleared",view.Outcome is null && view.Message==UiText.Get("Conversion.PasswordMismatch") && dialog.SecretBox.Password.Length==0 && dialog.ConfirmBox.Password.Length==0);
                                    view.Format="7z";view.Output=Path.Combine(data,language+version+"-new.7z");await Layout(dialog);dialog.SecretBox.Password=dialog.ConfirmBox.Password="新密碼 $ '";await Start();
                                    Check(label+" actual encrypted 7z output uses new password",view.Outcome?.Engine.IsSuccess==true && (await client.ListAsync(view.Output)).NeedsPassword && (await client.TestAsync(view.Output,"新密碼 $ '")).IsSuccess && (await client.TestAsync(view.Output,"source-secret")).NeedsPassword);
                                    Check(label+" new password controls masked and cleared",new PasswordBoxAutomationPeer(dialog.SecretBox).IsPassword() && dialog.SecretBox.Password.Length==0 && dialog.ConfirmBox.Password.Length==0);
                                    view.Encrypt=false;await Layout(dialog);var previous=File.ReadAllBytes(view.Output);await Start();
                                    Check(label+" declined replacement keeps existing bytes",confirmations==1 && view.Message==UiText.Get("Conversion.Cancelled") && previous.SequenceEqual(File.ReadAllBytes(view.Output)));
                                    approved=true;await Start();Check(label+" approved replacement creates verified clear archive",confirmations==2 && view.Outcome?.Engine.IsSuccess==true && (await client.TestAsync(view.Output)).IsSuccess);
                                    view.Output=source;await Start();Check(label+" same source destination error preserves archive",view.Outcome?.Engine.Code=="conversion_same_path" && before.SequenceEqual(File.ReadAllBytes(source)));
                                    view.Encrypt=true;await Layout(dialog);
                                    foreach(var mode in new[]{"light","dark","contrast"}) foreach(var size in new[]{(700d,660d),(540d,420d)})
                                    {
                                        theme.SetMode(mode=="dark"?"dark":"light");theme.Apply(mode=="contrast");dialog.Width=size.Item1;dialog.Height=size.Item2;await Layout(dialog);
                                        Check(label+mode+size.Item1+" fixed action area and focus accessible",Fits(dialog,dialog.StartButton) && Fits(dialog,dialog.CancelButton) && Fits(dialog,dialog.CloseButton) && new ButtonAutomationPeer(dialog.StartButton).IsKeyboardFocusable());
                                        // Render only a window that remains offscreen; never activate it.
                                        dialog.Opacity=1;DesktopAccessibilityValidation.Render(dialog,Path.Combine(previews,language+"-"+version+"-"+mode+"-"+size.Item1+".png"),1,clientOnly:true);dialog.Opacity=0;
                                    }
                                    Check(label+" vault encrypted bytes never changed by conversion",vaultBefore.SequenceEqual(File.ReadAllBytes(source+".apv")));
                                }
                                catch(Exception error) {Check(label+" dialog exception "+error,false);}
                                finally {dialog.Close();}
                            }));
                        });
                        main=new MainViewModel(baseDirectory,source+"-profile",vault,dialogs);window.DataContext=main;await main.InitializeAsync();
                        main.SelectedEngineVersion=version;main.ApplyEngineCommand.Execute(null);await main.WaitForIdleAsync();
                        Check(label+" no selection disables real entry",!main.ConvertArchiveCommand.CanExecute(null));
                        main.AddFiles([source]);main.SelectedTask=main.Tasks.Single();await Layout(window);
                        await ChooseTaskMenuItem(window,window.ConvertArchiveAction);await Layout(window);await main.WaitForIdleAsync();
                        Check(label+" actual right-click menu entered conversion and returned idle",shown==1 && main.IsIdle);
                    }
                }
                finally {window.Close();}
            }
        }
        catch(Exception error) {Check("exception "+error,false);}
        finally {UiText.Configure("zh-CN");theme.SetMode("light");theme.Apply(SystemParameters.HighContrast);}
        var passed=checks.Count>0 && checks.Values.All(value=>value);
        await File.WriteAllTextAsync(Path.Combine(root,"artifacts/conversion-ui-validation.json"),JsonSerializer.Serialize(new{passed,total=checks.Count,checks,syntheticOnly=true},new JsonSerializerOptions{WriteIndented=true}));return passed;
    }
    private static void Invoke(Button button)=>((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
    /// <summary>Opens the task right-click menu on the task list and invokes one entry, as a pointer user would.</summary>
    internal static async Task ChooseTaskMenuItem(MainWindow window,MenuItem item)
    {
        var menu=window.TaskContextMenu;menu.Opacity=0;menu.PlacementTarget=window.ArchiveTasks;menu.IsOpen=true;
        await window.Dispatcher.InvokeAsync(()=>menu.UpdateLayout(),DispatcherPriority.Background);
        if(item.Command is null || !item.Command.CanExecute(null)) throw new InvalidOperationException("Task context menu entry is unavailable: "+item.Name);
        ((IInvokeProvider)new MenuItemAutomationPeer(item).GetPattern(PatternInterface.Invoke)).Invoke();
        await window.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.Background);menu.IsOpen=false;
    }
    private static async Task Layout(Window window)=>await window.Dispatcher.InvokeAsync(window.UpdateLayout,DispatcherPriority.ApplicationIdle);
    private static bool Fits(FrameworkElement parent,FrameworkElement item) {var bounds=item.TransformToAncestor(parent).TransformBounds(new Rect(item.RenderSize));return bounds.Top>=0 && bounds.Bottom<=parent.ActualHeight+1 && bounds.Left>=0 && bounds.Right<=parent.ActualWidth+1;}
}
