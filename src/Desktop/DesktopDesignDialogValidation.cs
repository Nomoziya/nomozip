using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using ArchivePassword.Core;

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
        var compression=new PasswordDialog("资料备份.7z",false,[],true,compressing:true);
        Show(compression,"compression-password",()=>
        {
            compression.SecretBox.Password="synthetic-password";compression.ConfirmationBox.Password="different";compression.ContinueButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
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
            master.ConfirmBox.Password=master.NewBox.Password;check("editing master confirmation clears stale error",!FieldState.GetHasError(master.ConfirmBox) && master.ErrorLabel.Text.Length==0);master.ContinueButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        });
        check("master password inputs clear after close",master.Response is not null && master.NewBox.Password.Length==0 && master.ConfirmBox.Password.Length==0);
        foreach(var exporting in new[]{false,true})
        {
            var backup=new VaultBackupDialog(exporting,Path.Combine(folder,"synthetic-vault-backup"));
            Show(backup,exporting?"vault-backup-export":"vault-backup-import",()=>
            {
                backup.SecretBox.Password="synthetic-backup-passphrase";
                if(exporting)
                {
                    backup.ConfirmationBox.Password="different";backup.ContinueButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));check("backup export rejects mismatched passphrases",backup.IsVisible && backup.Passphrase is null);
                    backup.UpdateLayout();var backupError=(Border)backup.ConfirmationBox.Template.FindName("FieldFocus",backup.ConfirmationBox);
                    check("backup mismatch marks the confirmation field",backupError.Visibility==Visibility.Visible && ReferenceEquals(backupError.BorderBrush,Application.Current.Resources["ErrorBrush"]));Capture(backup,"vault-backup-mismatch");
                    backup.ConfirmationBox.Password=backup.SecretBox.Password;check("editing backup confirmation clears stale error",!FieldState.GetHasError(backup.ConfirmationBox) && backup.ErrorLabel.Text.Length==0);
                }
                backup.ContinueButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            });
            check("backup fields clear after close "+exporting,backup.Passphrase is not null && backup.SecretBox.Password.Length==0 && backup.ConfirmationBox.Password.Length==0);
        }
        var failureDialog=new ExtractionFailureDialog("资料合集.zip",new EngineResult {Code="corrupt_archive",SuccessfulItems=18,Failures=[new(18,"corrupt_archive","损坏文件.dat"),new(19,"unsupported_method","文档.txt")]});
        Show(failureDialog,"extraction-failures");
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
        theme.SetMode("light");
    }
}
