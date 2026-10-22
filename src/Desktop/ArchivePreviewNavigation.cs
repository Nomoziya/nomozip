using System.Windows.Input;
using ArchivePassword.Core;

namespace ArchivePassword.Desktop;

public sealed partial class ArchiveContentsViewModel
{
    private Action? previewNavigationCancel;
    internal void SetPreviewLoading(bool value,Action? cancelLoad=null)
    {
        if(value){idle=new TaskCompletionSource();previewNavigationCancel=cancelLoad;IsWorking=true;}
        else {previewNavigationCancel=null;IsWorking=false;idle.TrySetResult();}
    }
}

public sealed partial class ArchivePreviewViewModel
{
    private readonly CancellationTokenSource previewLifetime=new();
    private CancellationTokenSource? loadCancellation;
    private IReadOnlyList<ArchiveEntry> sequence=[];
    private int sequenceIndex;
    private Func<ArchiveEntry,CancellationToken,Task<ArchivePreviewViewModel>>? loadEntry;
    private Action<bool,Action?>? navigationActivity;
    private Action<ArchiveEntry>? selectEntry;
    private bool loading,findExpanded;
    private string findText="",findStatus="";
    private int match=-1;
    private Task navigationTask=Task.CompletedTask;
    private AsyncCommand? nextPreview,previousPreview;
    private RelayCommand? nextMatch,previousMatch;
    public bool HasNavigation=>sequence.Count>1;
    public bool ShowNavigationBar=>HasNavigation || IsText;
    public bool CanInteract=>!cleared && !IsLoading;
    public bool IsLoading {get=>loading;private set {if(Set(ref loading,value)){Changed(nameof(CanInteract));Changed(nameof(StatusText));Changed(nameof(IsText));Changed(nameof(ShowFind));CommandManager.InvalidateRequerySuggested();}}}
    public string NavigationPosition=>$"{sequenceIndex+1} / {sequence.Count}";
    public bool FindExpanded {get=>findExpanded;set {if(Set(ref findExpanded,value))Changed(nameof(ShowFind));}}
    public bool ShowFind=>FindExpanded && IsText;
    public string FindText {get=>findText;set {if(Set(ref findText,value)){match=-1;FindMatch(false);CommandManager.InvalidateRequerySuggested();}}}
    public string FindStatus {get=>findStatus;private set=>Set(ref findStatus,value);}
    public ICommand NextPreviewCommand=>nextPreview ??= new AsyncCommand(()=>navigationTask=MoveAsync(1),()=>CanInteract && sequenceIndex+1<sequence.Count);
    public ICommand PreviousPreviewCommand=>previousPreview ??= new AsyncCommand(()=>navigationTask=MoveAsync(-1),()=>CanInteract && sequenceIndex>0);
    public ICommand FindNextCommand=>nextMatch ??= new RelayCommand(()=>FindMatch(false),()=>CanInteract && IsText && FindText.Length>0 && Text.Length>0);
    public ICommand FindPreviousCommand=>previousMatch ??= new RelayCommand(()=>FindMatch(true),()=>CanInteract && IsText && FindText.Length>0 && Text.Length>0);
    public event Action<int,int>? FindMatchRequested;
    internal void ConfigureNavigation(IReadOnlyList<ArchiveEntry> entries,ArchiveEntry current,
        Func<ArchiveEntry,CancellationToken,Task<ArchivePreviewViewModel>> loader,Action<bool,Action?>? activity=null,Action<ArchiveEntry>? selected=null)
    {
        sequence=entries.ToArray();sequenceIndex=Array.FindIndex(sequence.ToArray(),entry=>entry.Index==current.Index && entry.Path==current.Path);
        if(sequenceIndex<0){sequence=[current];sequenceIndex=0;}
        loadEntry=loader;navigationActivity=activity;selectEntry=selected;RaiseContent();
    }
    public Task WaitForNavigationAsync()=>navigationTask;
    public void CancelNavigation()=>loadCancellation?.Cancel();
    private async Task MoveAsync(int offset)
    {
        var index=sequenceIndex+offset;
        if(!CanInteract || index<0 || index>=sequence.Count || loadEntry is null)return;
        var entry=sequence[index];var loader=loadEntry;var activity=navigationActivity;
        using var cancellation=CancellationTokenSource.CreateLinkedTokenSource(previewLifetime.Token);loadCancellation=cancellation;
        ReleaseContent();EntryPath=entry.Path;sequenceIndex=index;match=-1;IsLoading=true;RaiseContent();activity?.Invoke(true,CancelNavigation);
        try
        {
            var loaded=await loader(entry,cancellation.Token);
            if(cleared || cancellation.IsCancellationRequested){loaded.Clear();return;}
            Text=loaded.Text;Picture=loaded.Picture;Details=loaded.Details;Bytes=loaded.Bytes;Message=loaded.Message;ModifiedText=loaded.ModifiedText;
            imageBytes=loaded.imageBytes;loaded.imageBytes=null;loaded.Clear();selectEntry?.Invoke(entry);RaiseContent();
        }
        catch(OperationCanceledException) when(cancellation.IsCancellationRequested)
        {if(!cleared){Message=UiText.Get("Preview.Cancelled");RaiseContent();}}
        catch(Exception error) when(error is System.IO.IOException or UnauthorizedAccessException)
        {if(!cleared){Message=UiText.Get("Preview.NavigationFailed");RaiseContent();}}
        finally
        {
            loadCancellation=null;activity?.Invoke(false,null);
            if(!cleared){IsLoading=false;FindMatch(false);}
        }
    }
    private void FindMatch(bool backwards)
    {
        if(cleared || !IsText || FindText.Length==0){FindStatus="";return;}
        var position=-1;
        if(Text.Length>0)
        {
            if(backwards)
            {
                var start=match<0?Text.Length-1:match-1;
                if(start>=0)position=Text.LastIndexOf(FindText,start,StringComparison.OrdinalIgnoreCase);
                if(position<0)position=Text.LastIndexOf(FindText,StringComparison.OrdinalIgnoreCase);
            }
            else
            {
                var start=match<0?0:Math.Min(Text.Length,match+FindText.Length);
                position=Text.IndexOf(FindText,start,StringComparison.OrdinalIgnoreCase);
                if(position<0)position=Text.IndexOf(FindText,StringComparison.OrdinalIgnoreCase);
            }
        }
        match=position;FindStatus=UiText.Get(position<0?"Preview.FindNoMatch":"Preview.FindLocated");
        if(position>=0)FindMatchRequested?.Invoke(position,FindText.Length);
    }
    private void ReleaseContent()
    {
        if(imageBytes is not null)System.Security.Cryptography.CryptographicOperations.ZeroMemory(imageBytes);
        imageBytes=null;Text="";Picture=null;Details="";Bytes=0;Message="";ModifiedText="—";openMessage="";
    }
    private void RaiseContent()
    {
        foreach(var name in new[]{nameof(EntryPath),nameof(EntryName),nameof(Text),nameof(Picture),nameof(Details),nameof(PixelDimensions),nameof(Bytes),nameof(SizeText),nameof(Message),nameof(ModifiedText),nameof(IsText),nameof(IsImage),nameof(HasError),nameof(StatusText),nameof(HasNavigation),nameof(ShowNavigationBar),nameof(NavigationPosition),nameof(ShowFind)})Changed(name);
        CommandManager.InvalidateRequerySuggested();
    }
    private void ClearNavigation()
    {
        previewLifetime.Cancel();previewLifetime.Dispose();sequence=[];loadEntry=null;navigationActivity=null;selectEntry=null;findText="";FindStatus="";
    }
    internal static ArchivePreviewViewModel Error(ArchiveEntry entry,string message)=>new(entry.Path){Message=message};
}
