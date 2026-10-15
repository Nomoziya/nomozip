using ArchivePassword.Core;
using ArchivePassword.Infrastructure;

namespace ArchivePassword.Desktop;
public sealed partial class MainViewModel
{
    private async Task<ArchiveDragOutcome> PrepareDragAsync(ArchiveTaskRow task,ArchiveEngineClient client,IReadOnlyList<ArchiveEntry> selection,IProgress<string> status)
    {
        cancellation?.Dispose();skipPasswords?.Dispose();cancellation=new CancellationTokenSource();skipPasswords=new CancellationTokenSource();
        var prompts=0;var finished=false;
        try
        {
            return await new ArchiveDragService(client,vault,new PasswordCandidateProvider(vault),System.IO.Path.Combine(dataDirectory,"drag-temp"),new WindowsExtractionOrigin())
                .PrepareAsync(task.Path,selection,AutoTry,
                    failure=>dialogs.AskPasswordAsync(task.Name,prompts++>0,allPasswords,false,cancellation.Token,previousFailure:failure,browsing:true),
                    new Progress<TaskUpdate>(value=>{if(!finished) {status.Report(value.Message);CanSkipPasswords=value.State=="password";}}),cancellation.Token,skipPasswords.Token);
        }
        finally {finished=true;CanSkipPasswords=false;}
    }
}
