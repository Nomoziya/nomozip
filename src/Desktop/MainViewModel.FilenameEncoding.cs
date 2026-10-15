using System.IO;
using System.Windows.Input;
using ArchivePassword.Infrastructure;

namespace ArchivePassword.Desktop;
public sealed partial class MainViewModel
{
    public ICommand FilenameEncodingCommand { get; private set; } = null!;
    private ArchiveEngineClient CreateReadClient(ArchiveTaskRow task,ArchivePassword.Core.ExtractionPause? pause=null) => new(Path.Combine(baseDirectory,"ArchiveEngineWorker.exe"),engines.DllPath(activeEngine),task.FilenameCodePage,pause);
    private void InitializeFilenameEncodingCommands() => FilenameEncodingCommand = new AsyncCommand(EditFilenameEncodingAsync,
        () => Page==0 && IsIdle && activeEngine.Length>0 && SelectedTask is {Checkpoint:null} task && Path.GetExtension(task.Path).Equals(".zip",StringComparison.OrdinalIgnoreCase));
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
                task.Status=UiText.Get("T_b786485ba1c7");Changed(nameof(TaskSummary));Changed(nameof(ExtractButtonText));
                Footer=UiText.Format("T_5a21ce30671c", task.Name, (cp==0 ? UiText.Get("T_b438e0890c2a") : "CP"+cp));
            }
        }
        finally { cancellation.Dispose();cancellation=null;IsBusy=false;idle.TrySetResult(); }
        return Task.CompletedTask;
    }
}
