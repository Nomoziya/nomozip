using System.IO;
using ArchivePassword.Core;
using ArchivePassword.Infrastructure;

namespace ArchivePassword.Desktop;
public sealed partial class MainViewModel
{
    private async Task<string?> ExtractSelectionAsync(ArchiveTaskRow task,ArchiveEngineClient client,IReadOnlyList<ArchiveEntry> selection,IProgress<string> status)
    {
        // A cancelled selection can be retried in the same browser with fresh cancellation sources.
        cancellation?.Dispose();skipPasswords?.Dispose();cancellation=new CancellationTokenSource();skipPasswords=new CancellationTokenSource();
        var prompts=0;Banner="";
        try
        {
            await SaveSettingsAsync();
            var destination=task.ShellOutputPath ?? ExtractionDirectoryPlanner.BaseDirectory(task.Path,OutputPath,directoryRules.Location);
            Func<IReadOnlyList<ArchiveEntry>,string>? resolve=task.ShellOutputPath is null && directoryRules.Layout!=ExtractionLayout.Direct
                ? entries=>ExtractionDirectoryPlanner.Resolve(task.Path,destination,directoryRules,entries,DateOnly.FromDateTime(DateTime.Today)):null;
            var result=await new ExtractionService(client,vault,new PasswordCandidateProvider(vault),new WindowsExtractionOrigin()).ExtractAsync(task.Path,destination,AutoTry,
                failure=>dialogs.AskPasswordAsync(task.Name,prompts++>0,allPasswords,vaultAvailable,cancellation.Token,previousFailure:failure),
                path=>Task.FromResult(ConflictPolicy==1?ExistingFilePolicy.Skip:ConflictPolicy==2?ExistingFilePolicy.Replace:dialogs.ResolveConflict(path)),
                new Progress<TaskUpdate>(value=>{status.Report(value.Message);Footer=$"{task.Name}：{value.Message}";CanSkipPasswords=value.State=="password";}),
                cancellation.Token,skipPasswords.Token,resolve,selection);
            var message=result.Engine.IsSuccess?$"所选 {selection.Count} 项已解压，保留包内目录结构。":result.Engine.Status=="cancelled"?"已取消所选解压。":result.Engine.Code is "selection_changed" or "invalid_selection"?"压缩包内容已变化或选择无效，请重新查看内容后选择。":ErrorText(result.Engine.Code);
            status.Report(message);Footer=$"{task.Name}：{message}";
            if(result.Warning is not null) Banner=result.Warning;
            if(!result.Engine.IsSuccess) return null;
            task.SelectedOutputPath=result.OutputPath;task.Status=(task.Completed?"已解压 · ":"")+"所选内容已解压";
            await RefreshVaultAsync();
            if(directoryRules.OpenAfterSuccess && !cancellation.IsCancellationRequested) OpenResultDirectories([result.OutputPath]);
            return result.OutputPath;
        }
        catch(OperationCanceledException) {status.Report("已取消所选解压。");return null;}
        catch(Exception error) when(error is IOException or UnauthorizedAccessException or ArgumentException) {status.Report("所选内容未能解压，请检查目标目录与访问权限。");return null;}
        finally {CanSkipPasswords=false;}
    }
}
