using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using ArchivePassword.Core;
using ArchivePassword.Infrastructure;

namespace ArchivePassword.Desktop;

internal static class DesktopDesignDialogValidation
{
    internal static void Run(MainWindow owner,ThemeResources theme,string folder,Action<string,bool> check)
    {
        if(Environment.GetEnvironmentVariable("GITHUB_ACTIONS")!="true")throw new InvalidOperationException("Isolated CI only.");
        void Capture(Window dialog,string name)
        {
            dialog.UpdateLayout();dialog.Opacity=1;DesktopAccessibilityValidation.Render(dialog,Path.Combine(folder,name+".png"),1,clientOnly:true);dialog.Opacity=0;
        }
        void Show(Window dialog,string name,Action? exercise=null)
        {
            dialog.Owner=owner;dialog.WindowStartupLocation=WindowStartupLocation.Manual;dialog.Left=dialog.Top=-32000;dialog.Opacity=0;dialog.ShowActivated=false;
            Exception? failure=null;
            dialog.Loaded+=(_,_)=>dialog.Dispatcher.BeginInvoke(() =>
            {
                try
                {
                    foreach(var mode in new[]{"light","dark"}){theme.SetMode(mode);Capture(dialog,name+"-"+mode);}
                    theme.SetMode("light");exercise?.Invoke();
                }
                catch(Exception error){failure=error;}
                finally {if(dialog.IsVisible)dialog.Close();}
            });
            dialog.ShowDialog();if(failure is not null)throw failure;
        }
        var password=new PasswordDialog("资料合集.7z",false,[],true);
        Show(password,"password-prompt",()=>
        {
            check("password prompt defaults to masked and unsaved",password.ShowPassword.IsChecked!=true && password.SavePassword.IsChecked!=true && password.VisibleSecret.Visibility==Visibility.Collapsed);
            theme.SetMode("soft");password.Activate();Keyboard.Focus(password.SecretBox);password.UpdateLayout();
            var ring=(Border)password.SecretBox.Template.FindName("FieldFocus",password.SecretBox);
            check("soft password field shows semantic keyboard focus",password.SecretBox.IsKeyboardFocusWithin && ring.Visibility==Visibility.Visible && ReferenceEquals(ring.BorderBrush,Application.Current.Resources["FocusBrush"]));Capture(password,"soft-password-keyboard-focus");theme.SetMode("light");
            password.SecretBox.Password="synthetic-secret";password.ContinueButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        });
        check("password prompt clears secret on close",password.Response?.Secret=="synthetic-secret" && password.SecretBox.Password.Length==0 && password.VisibleSecret.Text.Length==0);
        var retryPassword=new PasswordDialog("资料合集.7z",true,[],true);
        Show(retryPassword,"password-retry",()=>check("retry reason is visible separately from archive identity",retryPassword.RetryBanner.IsVisible && retryPassword.RetryMessage.Text.Length>0 && retryPassword.Description.Text=="资料合集.7z"));
        var unlock=new MasterPasswordDialog(MasterPasswordAction.Unlock);
        Show(unlock,"master-unlock",()=>
        {
            unlock.ContinueButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check("empty master unlock stays open and marks the real field",unlock.IsVisible && unlock.Response is null && FieldState.GetHasError(unlock.CurrentBox) && unlock.ErrorLabel.Text.Length>0);
            Capture(unlock,"master-unlock-required");
        });
        var compression=new PasswordDialog("资料备份.7z",false,[],true,compressing:true,encryptHeaders:true);
        Show(compression,"compression-password",()=>
        {
            compression.SecretBox.Password="synthetic-password";compression.ConfirmationBox.Password="different";compression.ContinueButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check("7z password dialog exposes filename encryption",compression.CompressionHeadersPanel.IsVisible && compression.EncryptHeadersBox.IsChecked==true);
            check("new compression password requires matching confirmation",compression.IsVisible && compression.Response is null && compression.ErrorLabel.Text.Length>0);
            compression.UpdateLayout();var compressionError=(Border)compression.ConfirmationBox.Template.FindName("FieldFocus",compression.ConfirmationBox);
            check("compression mismatch marks the confirmation field",compressionError.Visibility==Visibility.Visible && ReferenceEquals(compressionError.BorderBrush,Application.Current.Resources["ErrorBrush"]));
            Capture(compression,"compression-password-mismatch");compression.ConfirmationBox.Password="synthetic-password";
            check("editing compression confirmation clears stale error",!FieldState.GetHasError(compression.ConfirmationBox) && compression.ErrorLabel.Text.Length==0);
            compression.ShowPassword.IsChecked=true;check("show password reveals both confirmation fields consistently",compression.VisibleSecret.Text==compression.VisibleConfirmation.Text && compression.VisibleConfirmation.Text=="synthetic-password");
            compression.ShowPassword.IsChecked=false;compression.ContinueButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        });
        check("compression password confirmation and visible copies clear",compression.Response?.Secret=="synthetic-password" && compression.SecretBox.Password.Length==0 && compression.ConfirmationBox.Password.Length==0 && compression.VisibleConfirmation.Text.Length==0);
        var master=new MasterPasswordDialog(MasterPasswordAction.Enable);
        Show(master,"master-password",()=>
        {
            master.NewBox.Password="synthetic-master-secret";master.ConfirmBox.Password="different";master.ContinueButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check("master password mismatch stays inline",master.IsVisible && master.Response is null && master.ErrorLabel.Text.Length>0);
            master.UpdateLayout();var masterError=(Border)master.ConfirmBox.Template.FindName("FieldFocus",master.ConfirmBox);
            check("master mismatch marks the confirmation field",masterError.Visibility==Visibility.Visible && ReferenceEquals(masterError.BorderBrush,Application.Current.Resources["ErrorBrush"]));Capture(master,"master-password-mismatch");
            master.ConfirmBox.Password=master.NewBox.Password;check("editing master confirmation clears stale error",!FieldState.GetHasError(master.ConfirmBox) && master.ErrorLabel.Text.Length==0);
            master.RevealToggle.IsChecked=true;check("master eye reveals both new fields",master.VisibleNew.Text==master.NewBox.Password && master.VisibleConfirm.Text==master.ConfirmBox.Password && master.VisibleNew.IsVisible);
            master.VisibleNew.Text="edited-synthetic-master";master.VisibleConfirm.Text="edited-synthetic-master";master.RevealToggle.IsChecked=false;
            check("master visible edits retain the secret while hiding copies",master.NewBox.Password=="edited-synthetic-master" && master.ConfirmBox.Password==master.NewBox.Password && master.VisibleNew.Text.Length==0 && master.VisibleConfirm.Text.Length==0);master.ContinueButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        });
        check("master password inputs clear after close",master.Response is not null && master.NewBox.Password.Length==0 && master.ConfirmBox.Password.Length==0 && master.VisibleCurrent.Text.Length==0 && master.VisibleNew.Text.Length==0 && master.VisibleConfirm.Text.Length==0);
        foreach(var exporting in new[]{false,true})
        {
            var backup=new VaultBackupDialog(exporting,Path.Combine(folder,"synthetic-vault-backup"));
            Show(backup,exporting?"vault-backup-export":"vault-backup-import",()=>
            {
                backup.SecretBox.Password="synthetic-backup-passphrase";
                backup.RevealToggle.IsChecked=true;check("backup eye reveals only on explicit request "+exporting,backup.VisibleSecret.IsVisible && backup.VisibleSecret.Text==backup.SecretBox.Password);backup.RevealToggle.IsChecked=false;
                check("backup eye removes visible copy when hidden "+exporting,backup.VisibleSecret.Text.Length==0 && backup.SecretBox.IsVisible);
                if(exporting)
                {
                    backup.ConfirmationBox.Password="different";backup.ContinueButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));check("backup export rejects mismatched passphrases",backup.IsVisible && backup.Passphrase is null);
                    backup.UpdateLayout();var backupError=(Border)backup.ConfirmationBox.Template.FindName("FieldFocus",backup.ConfirmationBox);
                    check("backup mismatch marks the confirmation field",backupError.Visibility==Visibility.Visible && ReferenceEquals(backupError.BorderBrush,Application.Current.Resources["ErrorBrush"]));Capture(backup,"vault-backup-mismatch");
                    backup.ConfirmationBox.Password=backup.SecretBox.Password;check("editing backup confirmation clears stale error",!FieldState.GetHasError(backup.ConfirmationBox) && backup.ErrorLabel.Text.Length==0);
                }
                backup.ContinueButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            });
            check("backup fields clear after close "+exporting,backup.Passphrase is not null && backup.SecretBox.Password.Length==0 && backup.ConfirmationBox.Password.Length==0 && backup.VisibleSecret.Text.Length==0 && backup.VisibleConfirmation.Text.Length==0);
        }
        var thumbnailDirectory=Path.Combine(folder,"synthetic","conflict-thumbnails");Directory.CreateDirectory(thumbnailDirectory);
        var thumbnailPaths=new[]{Path.Combine(thumbnailDirectory,"existing.png"),Path.Combine(thumbnailDirectory,"incoming.png")};
        foreach(var path in thumbnailPaths)
        {
            var pixels=Enumerable.Repeat((byte)180,24*24*4).ToArray();for(var index=3;index<pixels.Length;index+=4)pixels[index]=255;
            var bitmap=System.Windows.Media.Imaging.BitmapSource.Create(24,24,96,96,System.Windows.Media.PixelFormats.Bgra32,null,pixels,24*4);
            var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));using var output=File.Create(path);encoder.Save(output);
        }
        var thumbnailConflict=new ExistingFileDialog(new ExistingFileConflict(thumbnailPaths[0],thumbnailPaths[1]));
        Show(thumbnailConflict,"file-conflict-image",()=>check("conflict previews decode the two actual disk images",thumbnailConflict.ExistingThumbnail.Source is System.Windows.Media.Imaging.BitmapSource {PixelWidth:24} && thumbnailConflict.IncomingThumbnail.Source is System.Windows.Media.Imaging.BitmapSource {PixelWidth:24}));
        check("conflict thumbnails clear on close",thumbnailConflict.ExistingThumbnail.Source is null && thumbnailConflict.IncomingThumbnail.Source is null);
        var failureDialog=new ExtractionFailureDialog("资料合集.zip",new EngineResult {Code="corrupt_archive",SuccessfulItems=18,Failures=[new(18,"corrupt_archive","损坏文件.dat"),new(19,"unsupported_method","文档.txt")]});
        Show(failureDialog,"extraction-failures",()=>check("failure file and reason columns use the available width",failureDialog.FailureTable.Columns[0].ActualWidth>=230 && Math.Abs(failureDialog.FailureTable.Columns.Sum(column=>column.ActualWidth)-failureDialog.FailureTable.ActualWidth)<=22));
        var compressionResultShown=false;
        new WindowDialogs(owner,prepareFailure:dialog=>
        {
            dialog.WindowStartupLocation=WindowStartupLocation.Manual;dialog.Left=dialog.Top=-32000;dialog.Opacity=0;dialog.ShowActivated=false;
            dialog.Loaded+=(_,_)=>dialog.Dispatcher.BeginInvoke(()=>
            {
                compressionResultShown=dialog.ResultHeading.Text==UiText.Get("Compression.ResultTitle") && dialog.ResultSummary.Text=="Synthetic compression failure";
                Capture(dialog,"compression-failure-result");dialog.Close();
            });
        }).ShowCompressionResult("synthetic.7z","Synthetic compression failure",new EngineResult {Code="source_or_output_unavailable"});
        check("compression failure details keep the selected job status",compressionResultShown);
        var supplemental=Path.Combine(folder,"synthetic","supplemental-dialogs");Directory.CreateDirectory(supplemental);
        Show(new ExtractionRulesDialog(new(),supplemental,[supplemental]),"extraction-rules");
        Show(new ResultDirectoriesDialog([supplemental],_=>throw new InvalidOperationException("This capture does not open folders.")),"result-directories");
        var incoming=new SavedPassword(Guid.NewGuid(),"Synthetic import","synthetic-import-secret","",true,DateTimeOffset.UtcNow);
        Show(new VaultImportDialog([], [incoming]),"vault-import-preview");
        var names=new FilenameEncodingViewModel(Path.Combine(supplemental,"synthetic.zip"),0,(_,_)=>Task.FromResult(new EngineResult {Status="success",Format="zip",Items=[new ArchiveEntry(0,"说明.txt",32,false,false)]}));
        Show(new FilenameEncodingDialog(names),"filename-encoding",()=>check("supplemental encoding capture uses a ready preview",names.CanApply && names.Names.Count==1));
        var conversionClient=new ArchiveEngineClient(Path.Combine(AppContext.BaseDirectory,"ArchiveEngineWorker.exe"),Path.Combine(AppContext.BaseDirectory,"engines","26.04","7z.dll"));
        var conversionService=new ArchiveConversionService(conversionClient,conversionClient,new PasswordCandidateProvider(new DpapiPasswordVault(Path.Combine(supplemental,"vault.apv"))));
        var conversion=new ArchiveConversionViewModel(Path.Combine(supplemental,"synthetic.zip"),conversionService,false,_=>Task.FromResult<PasswordPromptResponse?>(null),_=>false);
        Show(new ArchiveConversionDialog(conversion),"archive-conversion");
        theme.SetMode("light");
    }
}
