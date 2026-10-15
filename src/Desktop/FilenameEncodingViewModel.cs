using System.Collections.ObjectModel;
using System.Windows.Input;
using ArchivePassword.Core;

namespace ArchivePassword.Desktop;
public sealed class FilenameEncodingViewModel : ObservableObject
{
    private readonly Func<int,CancellationToken,Task<EngineResult>> read;
    private readonly CancellationToken parentToken;
    private ZipFilenameEncoding selected;
    private bool busy, previewReady;
    private string status=UiText.Get("T_61a734266cbd");
    private CancellationTokenSource? cancellation;
    private TaskCompletionSource idle=new();
    public FilenameEncodingViewModel(string archive,int codePage,Func<int,CancellationToken,Task<EngineResult>> read,CancellationToken token=default)
    {
        ArchiveName=System.IO.Path.GetFileName(archive);this.read=read;parentToken=token;
        selected=Choices.Single(value=>value.CodePage==codePage);idle.SetResult();
        PreviewCommand=new AsyncCommand(PreviewAsync,()=>!IsBusy);
    }
    public string ArchiveName {get;}
    public IReadOnlyList<ZipFilenameEncoding> Choices=>ZipFilenameEncoding.Choices;
    public ObservableCollection<ArchiveEntry> Names {get;}=[];
    public ZipFilenameEncoding Selected {get=>selected;set {if(Set(ref selected,value)) {Names.Clear();PreviewReady=false;Status=UiText.Get("T_e764d4668e4a");}}}
    public bool IsBusy {get=>busy;private set {if(Set(ref busy,value)) {Changed(nameof(IsIdle));Changed(nameof(CanApply));}}}
    public bool IsIdle=>!IsBusy;
    public bool PreviewReady {get=>previewReady;private set {if(Set(ref previewReady,value)) Changed(nameof(CanApply));}}
    public bool CanApply=>PreviewReady && IsIdle;
    public string Status {get=>status;private set=>Set(ref status,value);}
    public ICommand PreviewCommand {get;}
    public Task WaitForIdleAsync()=>idle.Task;
    public void Cancel()=>cancellation?.Cancel();
    private async Task PreviewAsync()
    {
        var cp=Selected.CodePage;Names.Clear();PreviewReady=false;IsBusy=true;idle=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        cancellation=CancellationTokenSource.CreateLinkedTokenSource(parentToken);Status=UiText.Get("T_18e6101f922f");
        try
        {
            var result=await read(cp,cancellation.Token);
            if(cancellation.IsCancellationRequested || result.Status=="cancelled") {Status=UiText.Get("T_3cb0bad98799");return;}
            if(!result.IsSuccess || !result.Format.Equals("zip",StringComparison.OrdinalIgnoreCase))
            {
                Status=result.Code switch {"unsupported_operation" or "filename_encoding_unsupported"=>UiText.Get("T_e6dea7de8415"),"filename_encoding_not_zip"=>UiText.Get("T_5f5069faffe1"),_=>UiText.Get("T_24d6194b0a28")};return;
            }
            foreach(var item in result.Items) Names.Add(item);
            PreviewReady=true;Status=UiText.Format("T_daed8faf2fc1", Names.Count);
        }
        catch(OperationCanceledException) {Status=UiText.Get("T_3cb0bad98799");}
        finally {cancellation.Dispose();cancellation=null;IsBusy=false;idle.TrySetResult();}
    }
}
