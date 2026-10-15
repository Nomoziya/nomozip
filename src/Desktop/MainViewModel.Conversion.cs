using System.Windows.Input;
using ArchivePassword.Core;
using ArchivePassword.Infrastructure;

namespace ArchivePassword.Desktop;
public sealed partial class MainViewModel
{
    public ICommand ConvertArchiveCommand {get;private set;}=null!;
    private void InitializeConversionCommands()=>ConvertArchiveCommand=new AsyncCommand(ConvertArchiveAsync,
        ()=>Page==0 && IsIdle && SelectedTask is not null && activeEngine.Length>0);
    private Task ConvertArchiveAsync()
    {
        var task=SelectedTask!;IsBusy=true;idle=new TaskCompletionSource();cancellation=new CancellationTokenSource();
        try
        {
            var client=CreateReadClient(task);var prompts=0;
            var model=new ArchiveConversionViewModel(task.Path,new ArchiveConversionService(client,client,new PasswordCandidateProvider(vault),new WindowsExtractionOrigin()),AutoTry,
                failure=>dialogs.AskPasswordAsync(task.Name,prompts++>0,allPasswords,false,cancellation.Token,previousFailure:failure,testing:true),dialogs.ConfirmConversionReplace,cancellation.Token);
            dialogs.ShowArchiveConversion(model);
            if(model.Outcome is not null) Footer=model.Message;
        }
        finally {cancellation.Dispose();cancellation=null;IsBusy=false;idle.TrySetResult();}
        return Task.CompletedTask;
    }
}
