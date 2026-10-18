using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ArchivePassword.Core;
using ArchivePassword.Infrastructure;

namespace ArchivePassword.Desktop;
internal static class DesktopMasterVaultValidation
{
    internal static async Task<bool> RunAsync(MainWindow window,ThemeResources theme,string root,string baseDirectory)
    {
        var folder=Path.Combine(root,"artifacts/mvu-"+Guid.NewGuid().ToString("N")[..8]);Directory.CreateDirectory(folder);
        var checks=new Dictionary<string,bool>();void Check(string name,bool value)=>checks[name]=value;
        var screenshots=Path.Combine(root,"artifacts/master-vault-preview");Directory.CreateDirectory(screenshots);
        var master="合成主密码 2026 $ one";var next="另一个合成主密码 2026 two";var entered=master;var replacement=master;var cancel=false;var wrongBackup=false;var recoveryApproved=false;
        var vaultPath=Path.Combine(folder,"passwords.apv");var vault=new DpapiPasswordVault(vaultPath);var portable=Path.Combine(folder,"backup.nvault");
        var record=new SavedPassword(Guid.NewGuid(),"合成验收密码","测试密碼 ' $ & 空格","仅自动验收",true,DateTimeOffset.UtcNow);
        MainViewModel? model=null;
        try
        {
            await vault.SaveAsync(record);await EncryptedVaultBackup.ExportAsync(portable,[record],"synthetic portable password 2026");
            var dialogs=new WindowDialogs(window,prepareDialog:dialog=>Prepare(dialog,async()=>{dialog.SecretBox.Password=record.Secret;dialog.ConfirmationBox.Password=record.Secret;await Layout(dialog);Invoke(dialog.ContinueButton);}),
                selectVaultBackup:_=>portable,prepareVaultBackup:dialog=>Prepare(dialog,async()=>{dialog.SecretBox.Password=wrongBackup?"wrong":"synthetic portable password 2026";await Layout(dialog);Invoke(dialog.ContinueButton);}),
                prepareVaultImport:dialog=>Prepare(dialog,async()=>{await Layout(dialog);Check("recovery preview excludes secret",dialog.PreviewTable.Columns.All(column=>!column.Header.ToString()!.Contains("密码")));Invoke(dialog.ImportButton);}),
                prepareMaster:dialog=>Prepare(dialog,async()=>
                {
                    if(cancel){dialog.Close();return;}
                    dialog.CurrentBox.Password=entered;
                    var passwordField=dialog.CurrentPanel.IsVisible?dialog.CurrentBox:dialog.NewBox;var peer=UIElementAutomationPeer.CreatePeerForElement(passwordField)!;
                    Check("master password controls masked accessible and keyboard reachable",peer.IsPassword() && peer.GetName().Contains("主密码") && peer.IsKeyboardFocusable());
                    if(dialog.NewPanel.IsVisible)
                    {
                        dialog.NewBox.Password="short";Invoke(dialog.ContinueButton);await Layout(dialog);
                        Check("short new master stops confirmation",dialog.ErrorLabel.Text.Contains("12") && dialog.IsVisible);
                        dialog.NewBox.Password=replacement;dialog.ConfirmBox.Password="different";Invoke(dialog.ContinueButton);await Layout(dialog);
                        Check("master confirmation mismatch stops save",dialog.ErrorLabel.Text.Contains("不一致") && dialog.IsVisible);
                        dialog.ConfirmBox.Password=replacement;
                    }
                    dialog.CurrentBox.Password=entered;await Layout(dialog);await Capture(dialog,Path.Combine(screenshots,"master-dialog.png"),1);
                    Invoke(dialog.ContinueButton);
                }),confirmVaultRecovery:_=>recoveryApproved);
            model=new MainViewModel(baseDirectory,folder,vault,dialogs);window.DataContext=model;await model.InitializeAsync();
            async Task Execute(string button,int page=2)
            {model.Page=page;if(page==2)model.SettingsSection=2;await Layout(window);var element=(Button)window.FindName(button);element.BringIntoView();await Layout(window);Invoke(element);await Layout(window);await model.WaitForIdleAsync();await Layout(window);}
            Check("legacy startup does not require master",!model.VaultMasterEnabled && model.PasswordRows.Count==1 && model.EnableMasterCommand.CanExecute(null));
            cancel=true;await Execute("EnableMasterAction");Check("cancel enable preserves APV1",!model.VaultMasterEnabled && File.ReadAllBytes(vaultPath).AsSpan(0,4).SequenceEqual("APV1"u8));cancel=false;
            await Execute("EnableMasterAction");Check("actual settings enable protects vault",model.VaultMasterEnabled && !model.VaultLocked && model.PasswordRows.Count==1 && File.ReadAllBytes(vaultPath).AsSpan(0,4).SequenceEqual("APV2"u8));
            await Execute("VaultLockAction",1);Check("actual lock clears rows selection and compression password",model.VaultLocked && model.PasswordRows.Count==0 && model.SelectedPassword is null && model.CompressionPasswordStatus.Contains("尚未设置"));
            Check("locked edit import export merge actions disabled",!model.AddPasswordCommand.CanExecute(null) && !model.EditPasswordCommand.CanExecute(null) && !model.ImportVaultCommand.CanExecute(null) && !model.ExportVaultCommand.CanExecute(null) && !model.MergePasswordsCommand.CanExecute(null));
            Check("locked vault uses clear empty state",model.PasswordEmptyMessage.Contains("已锁定") && model.VaultSummary.Contains("手动"));
            entered="wrong";var original=File.ReadAllBytes(vaultPath);await Execute("VaultUnlockAction",1);Check("wrong UI unlock retains locked file",model.VaultLocked && model.Banner.Contains("不正确") && File.ReadAllBytes(vaultPath).SequenceEqual(original));
            cancel=true;await Execute("VaultUnlockAction",1);Check("cancel UI unlock remains locked",model.VaultLocked);cancel=false;
            model.Page=0;model.AddFiles([Path.Combine(root,"tests/fixtures/generated/encrypted-header.7z")]);model.OutputPath=Path.Combine(folder,"manual-output");model.ConflictPolicy=1;model.ExtractCommand.Execute(null);await model.WaitForIdleAsync();await Layout(window);
            Check("locked real extraction allows manual password without saving",model.Tasks.Single().Completed && model.VaultLocked && (await new DpapiPasswordVault(vaultPath).GetProtectionStateAsync())==VaultProtectionState.Locked);
            entered=master;await Execute("VaultUnlockAction",1);Check("correct UI unlock restores saved rows",!model.VaultLocked && model.PasswordRows.Single().Secret==record.Secret);
            model.VaultIdleIndex=1;await Execute("SaveVaultIdleAction");
            var reopened=new MainViewModel(baseDirectory,folder,new DpapiPasswordVault(vaultPath),dialogs);await reopened.InitializeAsync();Check("reopen needs master and persists idle setting",reopened.VaultLocked && reopened.VaultIdleIndex==1 && reopened.PasswordRows.Count==0);
            model.VaultIdleElapsedOverride=()=>TimeSpan.FromSeconds(59);await model.CheckVaultIdleAsync();Check("idle before threshold stays unlocked",!model.VaultLocked);
            model.VaultIdleElapsedOverride=()=>TimeSpan.FromMinutes(2);await model.CheckVaultIdleAsync();Check("idle expiry really locks storage and clears cache",model.VaultLocked && model.PasswordRows.Count==0);
            model.VaultIdleElapsedOverride=null;entered=master;await Execute("VaultUnlockAction",1);
            model.CompressionEncrypt=true;model.SetCompressionPasswordCommand.Execute(null);await Layout(window);await model.WaitForIdleAsync();Check("preselected compression password set for lock clearing",model.CompressionPasswordStatus.Contains("已设置"));
            await Execute("VaultLockAction",1);Check("lock clears pending compression password",model.CompressionPasswordStatus.Contains("尚未设置"));
            await Execute("VaultUnlockAction",1);replacement=next;await Execute("ChangeMasterAction");await Execute("VaultLockAction",1);
            entered=master;await Execute("VaultUnlockAction",1);Check("changed master refuses old password in UI",model.VaultLocked);entered=next;await Execute("VaultUnlockAction",1);Check("changed master accepts new password in UI",!model.VaultLocked);
            // Expiry while a real operation is busy defers the storage lock until its completion.
            model.Page=0;model.Tasks.Clear();model.AddFiles([Path.Combine(root,"tests/fixtures/generated/encrypted-header.7z")]);model.OutputPath=Path.Combine(folder,"auto-output");model.ExtractCommand.Execute(null);
            model.VaultIdleElapsedOverride=()=>TimeSpan.FromMinutes(2);await model.CheckVaultIdleAsync();Check("busy real extraction defers idle lock",model.IsBusy && !model.VaultLocked);await model.WaitForIdleAsync();await model.CheckVaultIdleAsync();
            Check("deferred lock after task preserves successful output",model.Tasks.Single().Completed && model.VaultLocked && File.Exists(Path.Combine(folder,"auto-output/中文示例.txt")));model.VaultIdleElapsedOverride=null;
            wrongBackup=true;await Execute("RecoverLockedVaultAction");Check("wrong independent backup cannot reset locked vault",model.VaultLocked && model.Banner.Contains("备份密码"));wrongBackup=false;
            recoveryApproved=false;original=File.ReadAllBytes(vaultPath);await Execute("RecoverLockedVaultAction");Check("recovery confirmation cancel preserves file",model.VaultLocked && File.ReadAllBytes(vaultPath).SequenceEqual(original));
            recoveryApproved=true;replacement=master;await Execute("RecoverLockedVaultAction");Check("actual recovery creates protected vault and retains encrypted original",!model.VaultLocked && model.PasswordRows.Count==1 && Directory.GetFiles(folder,"passwords.apv.before-recovery-*").Length==2);
            entered=master;await Execute("DisableMasterAction");Check("actual disable returns to Windows encryption",!model.VaultMasterEnabled && (await new DpapiPasswordVault(vaultPath).ReadAsync()).Count==1);
            replacement=master;await Execute("EnableMasterAction");model.VaultIdleIndex=0;model.VaultIdleElapsedOverride=()=>TimeSpan.FromHours(1);await model.CheckVaultIdleAsync();Check("disabled automatic timer does not lock",!model.VaultLocked);model.VaultIdleElapsedOverride=null;
            foreach(var contrast in new[]{false,true})
            {
                theme.Apply(contrast);
                foreach(var size in new[]{(1120d,760d),(800d,600d)})
                {
                    window.Width=size.Item1;window.Height=size.Item2;model.Page=1;await Layout(window);
                    Check($"vault first complete row {size.Item1} contrast {contrast}",window.PasswordTable.ActualHeight>=96);
                    Check($"vault lock focus reachable {size.Item1} contrast {contrast}",window.VaultLockAction.IsEnabled && UIElementAutomationPeer.CreatePeerForElement(window.VaultLockAction)!.IsKeyboardFocusable());
                    await Capture(window,Path.Combine(screenshots,$"vault-{size.Item1}-{contrast}.png"),1);
                    model.Page=2;model.SettingsSection=2;window.VaultProtectionCard.BringIntoView();await Layout(window);
                    Check($"settings protection controls reachable {size.Item1} contrast {contrast}",window.VaultIdleChoice.IsVisible && window.ChangeMasterAction.IsEnabled);
                    await Capture(window,Path.Combine(screenshots,$"settings-{size.Item1}-{contrast}.png"),1);
                }
                var dialog=new MasterPasswordDialog(MasterPasswordAction.Change){Owner=window,Width=360,Height=340,ShowActivated=false,ShowInTaskbar=false,Opacity=0};dialog.WindowStartupLocation=WindowStartupLocation.Manual;dialog.Left=-32000;dialog.Top=-32000;dialog.Show();await Layout(dialog);
                Check($"small master dialog keeps actions visible contrast {contrast}",Fits(dialog,dialog.ContinueButton));dialog.ConfirmBox.BringIntoView();await Layout(dialog);Check($"small dialog confirmation scroll reachable contrast {contrast}",Fits(dialog,dialog.ConfirmBox));await Capture(dialog,Path.Combine(screenshots,$"small-master-{contrast}.png"),1);dialog.Close();
            }
            theme.Apply(SystemParameters.HighContrast);await model.ReleaseVaultSessionAsync();Check("session release actually drops vault access",await vault.GetProtectionStateAsync()==VaultProtectionState.Locked && model.PasswordRows.Count==0);
        }
        catch(Exception error){checks["exception "+error.GetType().Name+": "+error.Message]=false;}
        var passed=checks.Count>0 && checks.Values.All(v=>v);await File.WriteAllTextAsync(Path.Combine(root,"artifacts/master-vault-ui-validation.json"),JsonSerializer.Serialize(new{passed,total=checks.Count,checks,syntheticOnly=true},new JsonSerializerOptions{WriteIndented=true}));return passed;
    }
    private static async Task Capture(Window window,string path,double scale)
    {
        var content=(FrameworkElement)window.Content;var panel=content as Panel;var hadBackground=panel?.Background is not null;if(panel is not null && !hadBackground)panel.SetResourceReference(Panel.BackgroundProperty,"SurfaceBrush");
        await Layout(window);var width=content.ActualWidth+content.Margin.Left+content.Margin.Right;var height=content.ActualHeight+content.Margin.Top+content.Margin.Bottom;
        var raw=new RenderTargetBitmap((int)Math.Ceiling(width*scale),(int)Math.Ceiling(height*scale),96*scale,96*scale,PixelFormats.Pbgra32);raw.Render(content);
        var visual=new DrawingVisual();using(var drawing=visual.RenderOpen())
        {drawing.DrawRectangle(window.Background ?? (Brush)window.FindResource("SurfaceBrush"),null,new Rect(0,0,width,height));drawing.DrawImage(raw,new Rect(0,0,width,height));}
        var bitmap=new RenderTargetBitmap(raw.PixelWidth,raw.PixelHeight,96*scale,96*scale,PixelFormats.Pbgra32);bitmap.Render(visual);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var output=File.Create(path);encoder.Save(output);if(panel is not null && !hadBackground)panel.ClearValue(Panel.BackgroundProperty);
    }
    private static void Prepare(Window dialog,Func<Task> action){dialog.WindowStartupLocation=WindowStartupLocation.Manual;dialog.Left=-32000;dialog.Top=-32000;dialog.ShowActivated=false;dialog.ShowInTaskbar=false;dialog.Opacity=0;dialog.Loaded+=(_,_)=>dialog.Dispatcher.BeginInvoke(async()=>await action());}
    private static void Invoke(Button button)=>((IInvokeProvider)UIElementAutomationPeer.CreatePeerForElement(button)!.GetPattern(PatternInterface.Invoke)).Invoke();
    private static async Task Layout(Window window)=>await window.Dispatcher.InvokeAsync(window.UpdateLayout,DispatcherPriority.ApplicationIdle);
    private static bool Fits(Window window,FrameworkElement element){var content=(FrameworkElement)window.Content;var bounds=element.TransformToAncestor(content).TransformBounds(new Rect(element.RenderSize));return element.IsVisible && bounds.Top>=0 && bounds.Bottom<=content.ActualHeight+1 && bounds.Left>=0 && bounds.Right<=content.ActualWidth+1;}
}
