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
            IReadOnlyList<CompressionEntry> additions=[];IReadOnlyList<uint> removed=[];
            if(action==ArchiveEditAction.Delete) removed=selection.Select(item=>item.Index).ToArray();
            else
            {
                var sources=dialogs.SelectArchiveEditSources(action);if(sources.Length==0) return null;
                if(action==ArchiveEditAction.Replace)
                {
                    if(selection.Count!=1 || selection[0].Directory || sources.Length!=1 || Directory.Exists(sources[0])) {status.Report("替换时请只选择一个包内文件和一个本地文件。");return null;}
                    additions=[new(sources[0],selection[0].Path,false)];removed=[selection[0].Index];
                }
                else additions=await Task.Run(()=>ArchiveEditingService.CollectSources(sources),token);
            }
            if(!dialogs.ConfirmArchiveEdit(action,removed.Count,additions.Count)) return null;
            var prompts=0;
            var outcome=await new ArchiveEditingService(client,client,new PasswordCandidateProvider(vault)).EditAsync(task.Path,snapshot,removed,additions,AutoTry,
                failure=>dialogs.AskPasswordAsync(task.Name,prompts++>0,allPasswords,false,token,previousFailure:failure,browsing:true),
                new Progress<TaskUpdate>(value=>{status.Report(value.Message);Footer=$"{task.Name}：{value.Message}";}),token);
            var message=outcome.Engine.IsSuccess?"修改完成，原包副本已保留。":outcome.Engine.Status=="cancelled"?"已取消修改，原包保持不变。":outcome.Engine.Code switch
            {
                "archive_edit_read_only"=>"此格式仅支持查看与解压。",
                "archive_edit_mixed_passwords"=>"此包包含多个密码，暂不支持修改；可继续查看与解压。",
                "archive_edit_empty_encrypted"=>"不能删空已加密文件名的包，否则会失去文件名加密。原包未修改。",
                "selection_changed"=>"压缩包已变化，请关闭并重新查看内容。",
                "duplicate_archive_path"=>"包内路径冲突。替换已有文件请使用“替换所选文件”。",
                "unsafe_archive_path" or "unsupported_link"=>"包含不支持的路径或链接，原包未修改。",
                _=>ErrorText(outcome.Engine.Code)+" 原包未修改。"
            };
            status.Report(message);Footer=$"{task.Name}：{message}";if(outcome.Warning is not null) Banner=outcome.Warning;
            if(outcome.Engine.IsSuccess) {task.Completed=false;task.SelectedOutputPath=null;task.ResultOutputPath=null;task.VerificationResult=null;task.Failure=null;task.Percent=0;task.Status="内容已修改 · 待解压";}
            return outcome;
        }
        catch(OperationCanceledException) {status.Report("已取消修改，原包保持不变。");return null;}
        catch(Exception error) when(error is IOException or UnauthorizedAccessException or ArgumentException) {status.Report("无法准备修改，请检查文件、目录和权限。原包未修改。");return null;}
    }
}
