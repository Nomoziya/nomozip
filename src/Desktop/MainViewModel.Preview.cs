using ArchivePassword.Core;
using ArchivePassword.Infrastructure;

namespace ArchivePassword.Desktop;
public sealed partial class MainViewModel
{
    private async Task PreviewEntryAsync(ArchiveTaskRow task,ArchiveEngineClient client,ArchiveEntry entry,IProgress<string> status)
    {
        cancellation?.Dispose();skipPasswords?.Dispose();cancellation=new CancellationTokenSource();skipPasswords=new CancellationTokenSource();
        var prompts=0;var finished=false;
        try
        {
            var service=new ArchivePreviewService(client,new PasswordCandidateProvider(vault),System.IO.Path.Combine(dataDirectory,"preview-temp"));
            var result=await service.ReadAsync(task.Path,entry,AutoTry,
                failure=>dialogs.AskPasswordAsync(task.Name,prompts++>0,allPasswords,false,cancellation.Token,previousFailure:failure,browsing:true),
                new Progress<TaskUpdate>(value=>{if(!finished) {status.Report(value.Message);CanSkipPasswords=value.State=="password";}}),cancellation.Token,skipPasswords.Token);
            finished=true;
            CanSkipPasswords=false;
            if(result.Warning is not null) Banner=result.Warning;
            if(result.Engine.IsSuccess && result.Content is not null)
            {
                var preview=await ArchivePreviewViewModel.CreateAsync(entry,result.Content,cancellation.Token);
                cancellation.Token.ThrowIfCancellationRequested();
                status.Report(UiText.Get("Preview.ReadOnly"));dialogs.ShowArchivePreview(preview);
            }
            else status.Report(result.Engine.Status=="cancelled"?UiText.Get("Preview.Cancelled"):result.Engine.Code switch
            {
                "preview_too_large"=>UiText.Get("Preview.TooLarge"),
                "preview_unsupported"=>UiText.Get("Preview.Unsupported"),
                "selection_changed" or "invalid_selection"=>UiText.Get("T_67c260d2140d"),
                _=>ErrorText(result.Engine.Code)
            });
        }
        catch(OperationCanceledException) {status.Report(UiText.Get("Preview.Cancelled"));}
        finally {finished=true;CanSkipPasswords=false;}
    }
}
