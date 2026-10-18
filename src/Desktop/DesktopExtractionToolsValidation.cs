using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Threading;
using ArchivePassword.Core;
using ArchivePassword.Infrastructure;

namespace ArchivePassword.Desktop;
internal static class DesktopExtractionToolsValidation
{
    internal static async Task<bool> RunAsync(MainWindow window,ThemeResources theme,string root,string baseDirectory)
    {
        var data=Path.GetFullPath(Path.Combine(root,"artifacts/extraction-tools-ui",Guid.NewGuid().ToString("N")));Directory.CreateDirectory(data);
        var checks=new Dictionary<string,bool>();void Check(string name,bool passed)=>checks[name]=passed;
        async Task Layout()=>await window.Dispatcher.InvokeAsync(window.UpdateLayout,DispatcherPriority.ApplicationIdle);
        void Invoke(Button button)=>((IInvokeProvider)UIElementAutomationPeer.CreatePeerForElement(button)!.GetPattern(PatternInterface.Invoke)).Invoke();
        var ruleClose=false;var promptSecret="测试密碼 ' $ & 空格";var passwordCount=0;var opens=new List<string[]>();
        try
        {
            var vault=new DpapiPasswordVault(Path.Combine(data,"passwords.apv"));await vault.SaveAsync(new(Guid.NewGuid(),"synthetic","测试密碼 ' $ & 空格","",true,DateTimeOffset.UtcNow));
            var originalVault=File.ReadAllBytes(Path.Combine(data,"passwords.apv"));
            var dialogs=new WindowDialogs(window,prepareDialog:dialog=>dialog.Loaded+=(_,_)=>dialog.Dispatcher.BeginInvoke(()=>
            {
                passwordCount++;
                if(dialog.Title=="输入校验密码") Check("test password dialog purpose and no save",((CheckBox)dialog.FindName("SavePassword")).Visibility==Visibility.Collapsed);
                if(promptSecret=="cancel") {dialog.Close();return;}
                ((PasswordBox)dialog.FindName("SecretBox")).Password=promptSecret;Invoke((Button)dialog.FindName("ContinueButton"));
            }),prepareFailure:dialog=>dialog.Loaded+=(_,_)=>dialog.Dispatcher.BeginInvoke(()=>
            {
                Check("result dialog labels testing without extraction claim",dialog.Title=="压缩包校验结果" && ((TextBlock)dialog.FindName("ContextDescription")).Text.Contains("没有输出"));
                dialog.Close();
            }),prepareRules:dialog=>dialog.Loaded+=(_,_)=>dialog.Dispatcher.BeginInvoke(()=>
            {
                dialog.Editor.Layout=2;
                ((ComboBox)dialog.FindName("LocationBox")).SelectedIndex=2;dialog.UpdateLayout();
                Check($"desktop preset binding {window.Width} {ruleClose}",dialog.Editor.Rules.Location==ExtractionLocation.Desktop && !((ComboBox)dialog.FindName("CustomPathBox")).IsEnabled);
                ((ComboBox)dialog.FindName("LocationBox")).SelectedIndex=0;dialog.UpdateLayout();
                var key=$"rules {window.Width} {ruleClose}";
                foreach(var name in new[] {"LocationBox","CustomPathBox","LayoutBox","NamingBox","CollisionBox","OpenAfterBox","ApplyRulesButton"})
                {
                    var element=(FrameworkElement)dialog.FindName(name);element.BringIntoView();dialog.UpdateLayout();
                    var peer=UIElementAutomationPeer.CreatePeerForElement(element)!;Check(key+" reachable "+name,element.IsVisible && peer.IsKeyboardFocusable() && !string.IsNullOrEmpty(peer.GetName()));
                }
                ((ScrollViewer)dialog.FindName("RulesScroll")).ScrollToTop();dialog.UpdateLayout();
                DesktopAccessibilityValidation.Render(dialog,Path.Combine(root,$"artifacts/extraction-rules-{window.Width}-{ruleClose}.png"),1.0);
                if(ruleClose) {dialog.Close();return;}
                dialog.Editor.Layout=2;dialog.Editor.CustomPath=Path.Combine(data,"smart-output");dialog.Editor.OpenAfterSuccess=true;
                Invoke((Button)dialog.FindName("ApplyRulesButton"));
            }));
            var model=new MainViewModel(baseDirectory,data,vault,dialogs);window.DataContext=model;await model.InitializeAsync();model.ConfigureResultDirectoryValidation(paths=>opens.Add(paths.ToArray()));
            foreach(var contrast in new[] {false,true})
            foreach(var width in new[] {1120,800})
            {
                theme.Apply(contrast);window.MinWidth=width;window.Width=width;window.Height=width==800?600:760;await Layout();
                ruleClose=contrast;var before=model.DirectoryRules;
                window.PrepareExtractionOptionsForValidation=confirmation=>
                {
                    confirmation.WindowStartupLocation=WindowStartupLocation.Manual;confirmation.Left=confirmation.Top=-32000;confirmation.ShowActivated=false;confirmation.Opacity=0;
                    confirmation.Loaded+=(_,_)=>confirmation.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,new Action(async()=>
                    {
                        var button=window.ExtractionRulesAction;
                        for(DependencyObject? parent=button;parent is not null;parent=LogicalTreeHelper.GetParent(parent))if(parent is Expander expander)expander.IsExpanded=true;
                        button.BringIntoView();confirmation.UpdateLayout();
                        Check($"rules command reachable {width} {contrast}",button.IsVisible && button.IsEnabled && UIElementAutomationPeer.CreatePeerForElement(button)!.IsKeyboardFocusable());
                        Invoke(button);await Layout();await model.WaitForIdleAsync();confirmation.Close();
                    }));
                };
                window.ConfirmExtractionOptions();window.PrepareExtractionOptionsForValidation=null;await Layout();await model.WaitForIdleAsync();await Layout();
                Check($"rules command binding {width} {contrast}",contrast?before==model.DirectoryRules:model.DirectoryRules.Layout==ExtractionLayout.Smart && model.DirectoryRules.OpenAfterSuccess);
                DesktopAccessibilityValidation.Render(window,Path.Combine(root,$"artifacts/extraction-tools-{width}-{contrast}.png"),1.0);
            }
            var preservedRules=model.DirectoryRules;
            var reopened=new MainViewModel(baseDirectory,data,vault,dialogs);await reopened.InitializeAsync();
            Check("rules and recent custom paths survive restart",reopened.DirectoryRules==preservedRules && reopened.RecentOutputPaths.Contains(model.OutputPath));
            var encrypted=Path.Combine(root,"tests/fixtures/generated/encrypted-header.7z");model.AddFiles([encrypted]);model.SelectedTask=model.Tasks.Single();
            await DesktopConversionValidation.ChooseTaskMenuItem(window,(MenuItem)window.FindName("TestArchiveAction"));await Layout();await model.WaitForIdleAsync();await Layout();
            Check("actual test command no extraction completion",model.SelectedTask.VerificationResult?.IsSuccess==true && !model.SelectedTask.Completed && model.SelectedTask.Status=="校验通过");
            Check("actual test command no output save association or automatic open",!Directory.Exists(model.OutputPath) && File.ReadAllBytes(Path.Combine(data,"passwords.apv")).SequenceEqual(originalVault) && opens.Count==0);
            Invoke((Button)window.FindName("TestResultAction"));await Layout();
            model.AutoTry=false;model.TestArchiveCommand.Execute(null);await model.WaitForIdleAsync();await Layout();
            Check("manual test password never saved",passwordCount==1 && File.ReadAllBytes(Path.Combine(data,"passwords.apv")).SequenceEqual(originalVault));
            promptSecret="cancel";model.TestArchiveCommand.Execute(null);await model.WaitForIdleAsync();await Layout();
            Check("test cancellation state is not extraction",model.SelectedTask.VerificationResult?.Status=="cancelled" && !model.SelectedTask.Completed && opens.Count==0);
            model.Tasks.Clear();
            foreach(var name in new[] {"one","two"})
            {
                var path=Path.Combine(data,name+".zip");using(var zip=ZipFile.Open(path,ZipArchiveMode.Create)) {foreach(var entryName in new[] {"a.txt","b.txt"}) {using var writer=new StreamWriter(zip.CreateEntry(entryName).Open());writer.Write(name);}}
                model.AddFiles([path]);
            }
            model.ConflictPolicy=2;model.ExtractCommand.Execute(null);await model.WaitForIdleAsync();await Layout();
            Check("batch real smart destinations recorded",model.Tasks.All(task=>task.Completed && task.ResultOutputPath==Path.Combine(model.OutputPath,Path.GetFileNameWithoutExtension(task.Path))));
            Check("batch automatically opens once with distinct results",opens.Count==1 && opens[0].Length==2 && opens[0].Distinct().Count()==2);
            var individual=new List<string>();var picker=new ResultDirectoriesDialog(opens[0],path=>individual.Add(path)) {Owner=window};
            picker.Loaded+=(_,_)=>picker.Dispatcher.BeginInvoke((Action)(async ()=>
            {
                ((ListBox)picker.FindName("DirectoriesBox")).SelectedIndex=1;Invoke((Button)picker.FindName("OpenDirectoryButton"));
                await picker.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);picker.Close();
            }));picker.ShowDialog();
            Check("multiple results picker opens only selected folder",individual.Count==1 && individual[0]==opens[0][1]);
            model.SelectedTask=model.Tasks.First();model.OutputPath=Path.Combine(data,"changed-later");model.OpenOutputCommand.Execute(null);
            Check("manual open uses completed target after base change",opens.Count==2 && opens[1].Single()==model.SelectedTask.ResultOutputPath);
            model.TestArchiveCommand.Execute(null);await model.WaitForIdleAsync();await Layout();
            Check("testing extracted row retains completion and destination",model.SelectedTask.Completed && model.SelectedTask.ResultOutputPath is not null && model.SelectedTask.Status=="已解压 · 校验通过");
            var other=Path.Combine(data,"other-source");Directory.CreateDirectory(other);File.Copy(Path.Combine(data,"two.zip"),Path.Combine(other,"two.zip"));
            model.ApplyDirectoryRules(new(Location:ExtractionLocation.ArchiveParent,Layout:ExtractionLayout.ArchiveFolder),model.OutputPath);
            model.Tasks.Clear();model.AddFiles([Path.Combine(data,"one.zip"),Path.Combine(other,"two.zip")]);var opensBeforeParent=opens.Count;
            model.ExtractCommand.Execute(null);await model.WaitForIdleAsync();await Layout();
            Check("archive parent preset processes separate real parents",model.Tasks.All(task=>task.Completed && task.ResultOutputPath==Path.Combine(Path.GetDirectoryName(task.Path)!,Path.GetFileNameWithoutExtension(task.Path))) && opens.Count==opensBeforeParent);
            model.SelectedTask=model.Tasks.First();Check("selected archive parent display and readonly",model.DisplayOutputPath==data && model.IsOutputPathReadOnly);
            model.ApplyDirectoryRules(preservedRules,Path.Combine(data,"changed-later"));
            model.Tasks.Clear();model.AddFiles([Path.Combine(root,"tests/fixtures/generated/plain.zip"),Path.Combine(root,"tests/fixtures/generated/corrupt.7z")]);var openBefore=opens.Count;
            model.ExtractCommand.Execute(null);await model.WaitForIdleAsync();await Layout();
            Check("failed batch never auto opens",opens.Count==openBefore && model.Tasks.Any(task=>!task.Completed));
            model.Tasks.Clear();model.AddFiles([encrypted]);model.AutoTry=false;model.ExtractCommand.Execute(null);await model.WaitForIdleAsync();await Layout();
            Check("cancelled batch never auto opens",opens.Count==openBefore && !model.Tasks.Single().Completed);
            var editor=new ExtractionRulesEditor {CustomPath="relative"};Check("invalid custom directory validation",!editor.Validate() && editor.Error.Contains("完整"));
            var oldData=Path.Combine(data,"legacy");Directory.CreateDirectory(oldData);var legacyOutput=Path.Combine(data,"legacy-output");
            await File.WriteAllTextAsync(Path.Combine(oldData,"settings.json"),JsonSerializer.Serialize(new {Version=1,OutputPath=legacyOutput,AutoTry=false,ConflictPolicy=1,EngineVersion="26.04"}));
            var legacy=new MainViewModel(baseDirectory,oldData,new DpapiPasswordVault(Path.Combine(oldData,"passwords.apv")),dialogs);await legacy.InitializeAsync();
            Check("legacy settings retain defaults and original output",legacy.DirectoryRules==new ExtractionDirectoryRules() && legacy.OutputPath==legacyOutput && !legacy.AutoTry && legacy.ConflictPolicy==1);
        }
        catch(Exception error) {checks["unexpected "+error]=false;}
        finally {theme.Apply(SystemParameters.HighContrast);}
        await File.WriteAllTextAsync(Path.Combine(root,"artifacts/extraction-tools-ui-validation.json"),JsonSerializer.Serialize(new {passed=checks.Values.All(value=>value),total=checks.Count,checks,syntheticData=true,browserAndExplorerNotStarted=true}));
        return checks.Values.All(value=>value);
    }
}
