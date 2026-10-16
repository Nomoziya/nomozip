using System.IO;
using System.Windows;
using System.Windows.Controls;
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
            password.SecretBox.Password="synthetic-secret";password.ContinueButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        });
        check("password prompt clears secret on close",password.Response?.Secret=="synthetic-secret" && password.SecretBox.Password.Length==0 && password.VisibleSecret.Text.Length==0);
        var compression=new PasswordDialog("资料备份.7z",false,[],true,compressing:true);
        Show(compression,"compression-password",()=>
        {
            compression.SecretBox.Password="synthetic-password";compression.ConfirmationBox.Password="different";compression.ContinueButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check("new compression password requires matching confirmation",compression.IsVisible && compression.Response is null && compression.ErrorLabel.Text.Length>0);
            Capture(compression,"compression-password-mismatch");compression.ConfirmationBox.Password="synthetic-password";
            compression.ShowPassword.IsChecked=true;check("show password reveals both confirmation fields consistently",compression.VisibleSecret.Text==compression.VisibleConfirmation.Text && compression.VisibleConfirmation.Text=="synthetic-password");
            compression.ShowPassword.IsChecked=false;compression.ContinueButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        });
        check("compression password confirmation and visible copies clear",compression.Response?.Secret=="synthetic-password" && compression.SecretBox.Password.Length==0 && compression.ConfirmationBox.Password.Length==0 && compression.VisibleConfirmation.Text.Length==0);
        var master=new MasterPasswordDialog(MasterPasswordAction.Enable);
        Show(master,"master-password",()=>
        {
            master.NewBox.Password="synthetic-master-secret";master.ConfirmBox.Password="different";master.ContinueButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check("master password mismatch stays inline",master.IsVisible && master.Response is null && master.ErrorLabel.Text.Length>0);Capture(master,"master-password-mismatch");
            master.ConfirmBox.Password=master.NewBox.Password;master.ContinueButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        });
        check("master password inputs clear after close",master.Response is not null && master.NewBox.Password.Length==0 && master.ConfirmBox.Password.Length==0);
        foreach(var exporting in new[]{false,true})
        {
            var backup=new VaultBackupDialog(exporting,Path.Combine(folder,"synthetic-vault-backup"));
            Show(backup,exporting?"vault-backup-export":"vault-backup-import",()=>
            {
                backup.SecretBox.Password="synthetic-backup-passphrase";
                if(exporting){backup.ConfirmationBox.Password="different";backup.ContinueButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));check("backup export rejects mismatched passphrases",backup.IsVisible && backup.Passphrase is null);backup.ConfirmationBox.Password=backup.SecretBox.Password;}
                backup.ContinueButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            });
            check("backup fields clear after close "+exporting,backup.Passphrase is not null && backup.SecretBox.Password.Length==0 && backup.ConfirmationBox.Password.Length==0);
        }
        var failureDialog=new ExtractionFailureDialog("资料合集.zip",new EngineResult {Code="corrupt_archive",SuccessfulItems=18,Failures=[new(18,"corrupt_archive","损坏文件.dat"),new(19,"unsupported_method","文档.txt")]});
        Show(failureDialog,"extraction-failures");
        theme.SetMode("light");
    }
}
