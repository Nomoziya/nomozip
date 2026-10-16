using System.IO;
using ArchivePassword.Core;
using ArchivePassword.Infrastructure;

namespace ArchivePassword.Desktop;
public sealed partial class MainViewModel
{
    private async Task<string?> ExtractSelectionAsync(ArchiveTaskRow task,ArchiveEngineClient client,IReadOnlyList<ArchiveEntry> selection,IProgress<string> status)
    {
        if(!dialogs.ConfirmWorkspaceExtraction()) return null;
        // A cancelled selection can be retried in the same browser with fresh cancellation sources.
        cancellation?.Dispose();skipPasswords?.Dispose();cancellation=new CancellationTokenSource();skipPasswords=new CancellationTokenSource();
        var prompts=0;Banner="";task.Running=true;task.IsIndeterminate=true;
        try
        {
            await SaveSettingsAsync();
            var destination=task.ShellOutputPath ?? ExtractionDirectoryPlanner.BaseDirectory(task.Path,OutputPath,directoryRules.Location);
            Func<IReadOnlyList<ArchiveEntry>,string>? resolve=task.ShellOutputPath is null && directoryRules.Layout!=ExtractionLayout.Direct
                ? entries=>ExtractionDirectoryPlanner.Resolve(task.Path,destination,directoryRules,entries,DateOnly.FromDateTime(DateTime.Today)):null;
            var result=await new ExtractionService(client,vault,new PasswordCandidateProvider(vault),new WindowsExtractionOrigin()).ExtractAsync(task.Path,destination,AutoTry,
                failure=>dialogs.AskPasswordAsync(task.Name,prompts++>0,allPasswords,vaultAvailable,cancellation.Token,previousFailure:failure),
                path=>Task.FromResult(ConflictPolicy==1?ExistingFilePolicy.Skip:ConflictPolicy==2?ExistingFilePolicy.Replace:dialogs.ResolveConflict(path)),
                new Progress<TaskUpdate>(value=>{status.Report(value.Message);task.Status=value.Message;Footer=UiText.Format("Common.TaskMessage", task.Name, value.Message);CanSkipPasswords=value.State=="password";if(value.Progress is {} progress){task.IsIndeterminate=progress.Total==0;task.Percent=progress.Total==0?0:Math.Min(100,progress.Completed*100.0/progress.Total);}}),
                cancellation.Token,skipPasswords.Token,resolve,selection);
            var message=result.Engine.IsSuccess?UiText.Format("T_0e5e1daffc91", selection.Count):result.Engine.Status=="cancelled"?UiText.Get("T_cf8aefc70fc9"):result.Engine.Code is "selection_changed" or "invalid_selection"?UiText.Get("T_67c260d2140d"):ErrorText(result.Engine.Code);
            status.Report(message);Footer=UiText.Format("Common.TaskMessage", task.Name, message);
            if(result.Warning is not null) Banner=result.Warning;
            if(!result.Engine.IsSuccess) return null;
            task.SelectedOutputPath=result.OutputPath;task.Status=(task.Completed?UiText.Get("T_76aca1ff80df"):"")+UiText.Get("T_2cbc0219e82d");
            await RefreshVaultAsync();
            if(directoryRules.OpenAfterSuccess && !cancellation.IsCancellationRequested) OpenResultDirectories([result.OutputPath]);
            return result.OutputPath;
        }
        catch(OperationCanceledException) {status.Report(UiText.Get("T_cf8aefc70fc9"));return null;}
        catch(Exception error) when(error is IOException or UnauthorizedAccessException or ArgumentException) {status.Report(UiText.Get("T_afb84681f1c0"));return null;}
        finally {CanSkipPasswords=false;task.Running=false;}
    }
}
