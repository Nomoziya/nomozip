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
    private string status="请选择编码，再预览名称。";
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
    public ZipFilenameEncoding Selected {get=>selected;set {if(Set(ref selected,value)) {Names.Clear();PreviewReady=false;Status="编码已更改，请重新预览后应用。";}}}
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
        cancellation=CancellationTokenSource.CreateLinkedTokenSource(parentToken);Status="正在读取文件名…";
        try
        {
            var result=await read(cp,cancellation.Token);
            if(cancellation.IsCancellationRequested || result.Status=="cancelled") {Status="已取消预览，原编码保持不变。";return;}
            if(!result.IsSuccess || !result.Format.Equals("zip",StringComparison.OrdinalIgnoreCase))
            {
                Status=result.Code switch {"unsupported_operation" or "filename_encoding_unsupported"=>"当前工作程序或引擎不支持此编码，请更新软件。","filename_encoding_not_zip"=>"此文件不是 ZIP 压缩包。",_=>"无法预览此 ZIP，请检查格式、文件位置与访问权限。"};return;
            }
            foreach(var item in result.Items) Names.Add(item);
            PreviewReady=true;Status=$"已预览 {Names.Count} 项。确认名称正确后应用。";
        }
        catch(OperationCanceledException) {Status="已取消预览，原编码保持不变。";}
        finally {cancellation.Dispose();cancellation=null;IsBusy=false;idle.TrySetResult();}
    }
}
