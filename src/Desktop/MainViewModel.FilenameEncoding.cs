using System.IO;
using System.Windows.Input;
using ArchivePassword.Infrastructure;

namespace ArchivePassword.Desktop;
public sealed partial class MainViewModel
{
    public ICommand FilenameEncodingCommand { get; private set; } = null!;
    private ArchiveEngineClient CreateReadClient(ArchiveTaskRow task,ArchivePassword.Core.ExtractionPause? pause=null) => new(Path.Combine(baseDirectory,"ArchiveEngineWorker.exe"),engines.DllPath(activeEngine),task.FilenameCodePage,pause);
    private void InitializeFilenameEncodingCommands() => FilenameEncodingCommand = new AsyncCommand(EditFilenameEncodingAsync,
        () => Page==0 && IsIdle && activeEngine.Length>0 && SelectedTask is {} task && Path.GetExtension(task.Path).Equals(".zip",StringComparison.OrdinalIgnoreCase));
    private Task EditFilenameEncodingAsync()
    {
        var task=SelectedTask!; IsBusy=true; idle=new TaskCompletionSource(); cancellation=new CancellationTokenSource();
        try
        {
            var encoding=dialogs.EditFilenameEncoding(task.Path,task.FilenameCodePage,
                (cp,token)=>new ArchiveEngineClient(Path.Combine(baseDirectory,"ArchiveEngineWorker.exe"),engines.DllPath(activeEngine),cp).ListAsync(task.Path,cancellationToken:token),cancellation.Token);
            if(encoding is {} cp && cp!=task.FilenameCodePage)
            {
                task.FilenameCodePage=cp; task.Completed=false; task.Failure=null; task.VerificationResult=null;
                task.Status="编码已更改 · 准备就绪";Changed(nameof(TaskSummary));Changed(nameof(ExtractButtonText));
                Footer=$"{task.Name}：已应用{(cp==0 ? "引擎默认编码" : "CP"+cp)}，仅影响此任务的文件名。";
            }
        }
        finally { cancellation.Dispose();cancellation=null;IsBusy=false;idle.TrySetResult(); }
        return Task.CompletedTask;
    }
}
