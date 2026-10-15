using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
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

internal static class DesktopLocalizationValidation
{
    internal static async Task<bool> RunAsync(MainWindow window,ThemeResources theme,string root,string baseDirectory,string profile)
    {
        var language=UiText.LanguageCode;var english=language=="en";
        var checks=new Dictionary<string,bool>();void Check(string name,bool value)=>checks.Add(name,value);
        var clipped=new List<string>();var samples=0;
        string Hash(string path)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
        void Hide(Window target){target.WindowStartupLocation=WindowStartupLocation.Manual;target.Left=-32000;target.Top=-32000;target.Opacity=0;target.ShowActivated=false;target.ShowInTaskbar=false;}
        IEnumerable<DependencyObject> Children(DependencyObject target)
        {
            yield return target;
            for(var i=0;i<VisualTreeHelper.GetChildrenCount(target);i++)foreach(var child in Children(VisualTreeHelper.GetChild(target,i)))yield return child;
        }
        async Task Inspect(Window target,string name)
        {
            await Layout(target);samples++;
            foreach(var block in Children(target).OfType<TextBlock>().Where(b=>b.IsVisible && b.ActualWidth>0 && b.TextWrapping==TextWrapping.NoWrap && b.TextTrimming==TextTrimming.None && b.GetBindingExpression(TextBlock.TextProperty) is null && !string.IsNullOrWhiteSpace(b.Text)))
            {
                var text=new FormattedText(block.Text,CultureInfo.CurrentCulture,block.FlowDirection,new Typeface(block.FontFamily,block.FontStyle,block.FontWeight,block.FontStretch),block.FontSize,Brushes.Black,VisualTreeHelper.GetDpi(block).PixelsPerDip);
                if(text.WidthIncludingTrailingWhitespace>block.ActualWidth+3)clipped.Add(name+": "+block.Text+" ("+text.WidthIncludingTrailingWhitespace.ToString("F1")+" > "+block.ActualWidth.ToString("F1")+")");
            }
            var preview=Path.Combine(root,"artifacts/localization-preview");Directory.CreateDirectory(preview);
            target.Opacity=1;DesktopAccessibilityValidation.Render(target,Path.Combine(preview,language+"-"+name+".png"),1,clientOnly:true);target.Opacity=0;
        }
        try
        {
            Directory.CreateDirectory(profile);
            Check("startup reads saved culture before XAML",LanguagePreferenceStore.Read(profile).Language==language && window.Language.IetfLanguageTag.Equals(CultureInfo.CurrentCulture.IetfLanguageTag,StringComparison.OrdinalIgnoreCase));
            Check("navigation is translated",window.Navigation.Items.Cast<ListBoxItem>().Select(v=>v.Content?.ToString()).SequenceEqual(english?new[]{"Extract","Compress","Vault","Settings"}:new[]{"解压","压缩","密码库","设置"}));
            var vaultPath=Path.Combine(profile,"passwords.apv");var vault=new DpapiPasswordVault(vaultPath);
            var record=new SavedPassword(Guid.NewGuid(),"解压","用户 secret ' $ & 密码","原始备注",true,DateTimeOffset.UtcNow);
            await vault.SaveAsync(record);
            var prompts=0;
            var dialogs=new WindowDialogs(window,prepareDialog:dialog=>
            {
                Hide(dialog);dialog.Loaded+=(_,_)=>dialog.Dispatcher.BeginInvoke(()=>
                {
                    prompts++;Check("password dialog translated "+prompts,dialog.Title==(english?"Set archive password":"设置压缩密码"));
                    dialog.SecretBox.Password="本次 Unicode ' $ & 密码";dialog.SavePassword.IsChecked=true;dialog.NameBox.Text="压缩密码";Invoke(dialog.ContinueButton);
                });
            });
            var model=new MainViewModel(baseDirectory,profile,vault,dialogs);window.DataContext=model;await model.InitializeAsync();
            Check("stored language appears in settings selection",model.SelectedLanguage.Code==language);
            Check("user password name and notes are unchanged",model.PasswordRows.Any(v=>v.Id==record.Id && v.Name=="解压" && v.Notes=="原始备注"));
            var archive=Path.Combine(profile,"压缩.zip");File.Copy(Path.Combine(root,"tests/fixtures/generated/plain.zip"),archive);
            foreach(var version in new[]{"26.03","26.04"})
            {
                model.SelectedEngineVersion=version;model.ApplyEngineCommand.Execute(null);await model.WaitForIdleAsync();
                model.Tasks.Clear();model.AddFiles([archive]);model.Page=0;model.OutputPath=Path.Combine(profile,"plain-"+version);model.ConflictPolicy=1;
                Check(version+" archive path and localized pending state",model.Tasks.Single().Name=="压缩.zip" && model.Tasks.Single().Status==(english?"Ready":"准备就绪"));
                await Layout(window);Invoke(window.ExtractAction);await Layout(window);await model.WaitForIdleAsync();await Layout(window);
                Check(version+" actual extraction has translated success",model.Tasks.Single().Completed && model.Tasks.Single().Status.StartsWith(english?"Extracted":"已解压",StringComparison.Ordinal));
                Check(version+" extracted Unicode filename and exact bytes",Hash(Path.Combine(model.Tasks.Single().ResultOutputPath!,"中文示例.txt"))==Hash(Path.Combine(root,"tests/fixtures/generated/中文示例.txt")));
                model.Page=3;var source=Path.Combine(profile,"原始资料-"+version+".txt");await File.WriteAllTextAsync(source,"user content 用户内容 ' $ &",Encoding.UTF8);model.AddCompressionSources([source]);
                model.CompressionOutput=Path.Combine(profile,"encrypted-"+version+".7z");model.CompressionEncrypt=true;
                model.SetCompressionPasswordCommand.Execute(null);await Layout(window);await model.WaitForIdleAsync();
                var view=(CompressionView)window.FindName("CompressionPage");var create=(Button)view.FindName("CreateButton");
                Invoke(create);await Layout(window);await model.WaitForIdleAsync();await Layout(window);
                Check(version+" encrypted creation reports localized success",File.Exists(model.CompressionOutput) && model.CompressionMessage==(english?"Archive created and validated.":"已创建并校验压缩包。"));
                model.Page=0;model.Tasks.Clear();model.AddFiles([model.CompressionOutput]);model.OutputPath=Path.Combine(profile,"encrypted-out-"+version);
                await Layout(window);
                Invoke(window.ExtractAction);await Layout(window);await model.WaitForIdleAsync();await Layout(window);
                Check(version+" saved Unicode password automatically decrypts",model.Tasks.Single().Completed && Hash(Path.Combine(model.Tasks.Single().ResultOutputPath!,Path.GetFileName(source)))==Hash(source));
                model.CompressionSources.Clear();
            }
            Check("language switch has not modified original user record",(await vault.ReadAsync()).Single(v=>v.Id==record.Id)==record);
            model.Page=0;await Layout(window);window.ArchiveTasks.UpdateLayout();
            var row=(DataGridRow)window.ArchiveTasks.ItemContainerGenerator.ContainerFromIndex(0);
            var rowName=new DataGridRowAutomationPeer(row).GetName();
            Check("archive row accessible name includes original filename and translated status",rowName.Contains(model.Tasks.Single().Name,StringComparison.Ordinal) && rowName.Contains(english?"Extracted":"已解压",StringComparison.Ordinal));
            foreach(var contrast in new[]{false,true})foreach(var width in new[]{1120.0,800.0})foreach(var page in new[]{0,3,1,2})
            {
                theme.Apply(contrast);window.Width=width;window.Height=760;model.Page=page;await Layout(window);
                await Inspect(window,"page-"+page+"-"+width+"-"+contrast);
                var client=(FrameworkElement)window.Content;
                Check("page heading fully inside client area "+page+" "+width+" "+contrast,Children(window).OfType<TextBlock>().Where(v=>v.IsVisible && v.FontSize==24).All(v=>{var bounds=v.TransformToAncestor(client).TransformBounds(new Rect(v.RenderSize));return bounds.Top>=0 && bounds.Left>=0 && bounds.Bottom<=client.ActualHeight && bounds.Right<=client.ActualWidth;}));
                Check("page has no horizontal scrolling "+page+" "+width+" "+contrast,Children(window).OfType<ScrollViewer>().Where(v=>v.IsVisible).All(v=>v.ComputedHorizontalScrollBarVisibility!=Visibility.Visible));
                Check("scroll thumb names use selected language "+page+" "+width+" "+contrast,Children(window).OfType<System.Windows.Controls.Primitives.Thumb>().Where(v=>v.IsVisible && System.Windows.Automation.AutomationProperties.GetName(v).Length>0).All(v=>System.Windows.Automation.AutomationProperties.GetName(v)==(english?"Vertical scroll thumb":"垂直滚动滑块")));
            }
            theme.Apply(false);
            var entries=new[]{new ArchiveEntry(0,"用户目录/资料.txt",24,false,true)};
            var contents=new ArchiveContentsViewModel(archive,entries);
            var failure=new EngineResult{Code="corrupt_archive",Failures=[new(0,"crc_error","用户目录/资料.txt")]};
            var windows=new List<(string,Window)>
            {
                ("password",new PasswordDialog("用户.7z",false,[record],true,record,editing:true)),
                ("backup-export",new VaultBackupDialog(true,Path.Combine(profile,"独立备份.nvault"))),
                ("backup-import",new VaultBackupDialog(false,Path.Combine(profile,"独立备份.nvault"))),
                ("import-preview",new VaultImportDialog([record],[record with{Id=Guid.NewGuid()}])),
                ("rules",new ExtractionRulesDialog(new(),profile,[profile])),
                ("hashes",new FileHashDialog()),
                ("encoding",new FilenameEncodingDialog(new(archive,0,(_,_)=>Task.FromResult(new EngineResult{Items=entries})))),
                ("contents",new ArchiveContentsDialog(contents)),
                ("failure",new ExtractionFailureDialog("用户.zip",failure)),
                ("results",new ResultDirectoriesDialog([profile],_=>throw new InvalidOperationException("Opening folders is not part of this validation.")))
            };
            windows.AddRange(Enum.GetValues<MasterPasswordAction>().Select(action=>("master-"+action,(Window)new MasterPasswordDialog(action))));
            foreach(var (name,dialog) in windows)
            {
                Hide(dialog);dialog.Owner=window;dialog.Show();
                foreach(var contrast in new[]{false,true})
                {
                    theme.Apply(contrast);dialog.Width=Math.Max(dialog.MinWidth,640);dialog.Height=Math.Max(dialog.MinHeight,600);
                    await Inspect(dialog,name+"-"+contrast);
                }
                dialog.Close();
            }
            Check("all inspected static labels fit their allocated width",clipped.Count==0);
            theme.Apply(false);model.Page=2;window.Width=1120;await Layout(window);window.LanguageChoice.BringIntoView();await Layout(window);
            Check("language selector has translated accessible name",UIElementAutomationPeer.CreatePeerForElement(window.LanguageChoice)!.GetName()==(english?"Language":"语言"));
            Check("save language is keyboard focusable",new ButtonAutomationPeer(window.SaveLanguageAction).IsKeyboardFocusable());
            var settings=Path.Combine(profile,"settings.json");var settingsHash=Hash(settings);var vaultHash=Hash(vaultPath);
            window.LanguageChoice.SelectedIndex=english?0:1;await Layout(window);Invoke(window.SaveLanguageAction);await Layout(window);
            var deadline=DateTime.UtcNow.AddSeconds(5);while(model.LanguageMessage!=UiText.Get("Language.Saved") && DateTime.UtcNow<deadline)await Task.Delay(10);
            Check("actual WPF selection and UIA save persist next language",LanguagePreferenceStore.Read(profile).Language==(english?"zh-CN":"en") && model.LanguageMessage==UiText.Get("Language.Saved"));
            Check("saving language waits for restart",UiText.LanguageCode==language && ((ListBoxItem)window.Navigation.Items[0]).Content?.ToString()==(english?"Extract":"解压"));
            Check("saving language preserves settings and vault bytes",Hash(settings)==settingsHash && Hash(vaultPath)==vaultHash);
            var preferencePath=Path.Combine(profile,LanguagePreferenceStore.FileName);var savedPreference=await File.ReadAllBytesAsync(preferencePath);
            try
            {
                await File.WriteAllTextAsync(preferencePath,"{\"Version\":99,\"Language\":\"future\"}");var unknownHash=Hash(preferencePath);
                Invoke(window.SaveLanguageAction);await Layout(window);
                deadline=DateTime.UtcNow.AddSeconds(5);while(model.LanguageMessage!=UiText.Get("Language.SaveFailed") && DateTime.UtcNow<deadline)await Task.Delay(10);
                Check("unknown preference save reports a localized error without crashing",model.LanguageMessage==UiText.Get("Language.SaveFailed"));
                Check("unknown preference UI save preserves file and existing user data",Hash(preferencePath)==unknownHash && Hash(settings)==settingsHash && Hash(vaultPath)==vaultHash);
            }
            finally {await File.WriteAllBytesAsync(preferencePath,savedPreference);}
        }
        catch(Exception error){checks["exception "+error]=false;}
        finally{theme.Apply(SystemParameters.HighContrast);window.Opacity=0;}
        var result=new{passed=checks.Count>0 && checks.Values.All(v=>v),total=checks.Count,checks,language,windowLanguage=window.Language.IetfLanguageTag,threadCulture=CultureInfo.CurrentCulture.Name,samples,clipped,syntheticData=true,actualVisibleNarratorTested=false};
        await File.WriteAllTextAsync(Path.Combine(root,"artifacts/localization-"+language+"-ui-validation.json"),JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true}));
        return result.passed;
    }
    private static async Task Layout(Window window){await window.Dispatcher.InvokeAsync(window.UpdateLayout,DispatcherPriority.ApplicationIdle);System.Windows.Input.CommandManager.InvalidateRequerySuggested();await window.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);}
    private static void Invoke(Button button)=>((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)!).Invoke();
}
