using System.IO;
using ArchivePassword.Core;
using ArchivePassword.Infrastructure;

namespace ArchivePassword.Desktop;
public sealed partial class MainViewModel
{
    private async Task<ArchiveEditOutcome?> EditArchiveAsync(ArchiveTaskRow task,ArchiveEngineClient client,ArchiveEditAction action,
        IReadOnlyList<ArchiveEntry> snapshot,IReadOnlyList<ArchiveEntry> selection,IProgress<string> status)
    {
        cancellation?.Dispose();cancellation=new CancellationTokenSource();var token=cancellation.Token;
        try
        {
            ArchiveEditOutcome? outcome;
            if(action==ArchiveEditAction.EditText)
            {
                if(selection.Count!=1) return null;
                var prompts=0;
                Task<PasswordPromptResponse?> Ask(EngineResult failure)=>dialogs.AskPasswordAsync(task.Name,prompts++>0,allPasswords,false,token,previousFailure:failure,testing:true);
                var progress=new Progress<TaskUpdate>(value=>status.Report(value.Message));
                var start=await new ArchiveTextEditingService(client,client,new PasswordCandidateProvider(vault),Path.Combine(dataDirectory,"edit-temp")).BeginAsync(task.Path,selection[0],AutoTry,Ask,progress,token);
                if(start.Warning is not null) Banner=start.Warning;
                var session=start.Session;
                if(session is null) outcome=new(start.Engine,snapshot);
                else
                {
                    try
                    {
                    if(!dialogs.ConfirmTextEdit(session.Entry,false,session.EncodingName)) return null;
                    var text=dialogs.EditTextCopy(session);
                    if(text is null || text==session.OriginalText) {status.Report(UiText.Get("TextEdit.Discarded"));return null;}
                    if(!dialogs.ConfirmTextEdit(session.Entry,true,session.EncodingName,session.Encode(text).LongLength)) return null;
                    outcome=await session.WriteBackAsync(text,snapshot,AutoTry,Ask,progress,token);
                    }
                    finally {session.Dispose();if(session.CleanupWarning is not null)Banner=UiText.Format("TextEdit.CleanupWarning",session.CleanupWarning);}
                }
            }
            else
            {
            IReadOnlyList<CompressionEntry> additions=[];IReadOnlyList<uint> removed=[];
            if(action==ArchiveEditAction.Delete) removed=selection.Select(item=>item.Index).ToArray();
            else
            {
                var sources=dialogs.SelectArchiveEditSources(action);if(sources.Length==0) return null;
                if(action==ArchiveEditAction.Replace)
                {
                    if(selection.Count!=1 || selection[0].Directory || sources.Length!=1 || Directory.Exists(sources[0])) {status.Report(UiText.Get("T_3b1b95cffc07"));return null;}
                    additions=[new(sources[0],selection[0].Path,false)];removed=[selection[0].Index];
                }
                else additions=await Task.Run(()=>ArchiveEditingService.CollectSources(sources,token),token);
            }
            if(!dialogs.ConfirmArchiveEdit(action,removed.Count,additions.Count)) return null;
            var prompts=0;
            outcome=await new ArchiveEditingService(client,client,new PasswordCandidateProvider(vault)).EditAsync(task.Path,snapshot,removed,additions,AutoTry,
                failure=>dialogs.AskPasswordAsync(task.Name,prompts++>0,allPasswords,false,token,previousFailure:failure,testing:true),
                new Progress<TaskUpdate>(value=>{status.Report(value.Message);Footer=UiText.Format("Common.TaskMessage", task.Name, value.Message);}),token);
            }
            var message=outcome.Engine.IsSuccess?UiText.Get("T_a5b37bdf8cf4"):outcome.Engine.Status=="cancelled"?UiText.Get("T_18cd155e6e88"):outcome.Engine.Code switch
            {
                "archive_edit_read_only"=>UiText.Get("T_e1fb8aa2fcac"),
                "archive_edit_container_read_only"=>UiText.Get("T_70e9460bb754"),
                "archive_edit_mixed_passwords"=>UiText.Get("T_6a2d72e11b34"),
                "archive_edit_empty_encrypted"=>UiText.Get("T_fcc381b75f0b"),
                "selection_changed"=>UiText.Get("T_f41a0d58cf70"),
                "duplicate_archive_path"=>UiText.Get("T_8680cb4ca45a"),
                "unsafe_archive_path" or "unsupported_link"=>UiText.Get("T_2575b9f84805"),
                _=>ErrorText(outcome.Engine.Code)+UiText.Get("T_b94136fcd4cd")
            };
            status.Report(message);Footer=UiText.Format("Common.TaskMessage", task.Name, message);if(outcome.Warning is not null) Banner=outcome.Warning;
            if(outcome.Engine.IsSuccess) {task.VerificationResult=null;if(!task.IsCompression) {task.Completed=false;task.SelectedOutputPath=null;task.ResultOutputPath=null;task.Failure=null;task.Percent=0;task.Status=UiText.Get("T_34a6bbd80e45");}}
            return outcome;
        }
        catch(OperationCanceledException) {status.Report(UiText.Get("T_18cd155e6e88"));return null;}
        catch(Exception error) when(error is IOException or UnauthorizedAccessException or ArgumentException) {status.Report(UiText.Get("T_47c6a0bd6bc3"));return null;}
    }
}
