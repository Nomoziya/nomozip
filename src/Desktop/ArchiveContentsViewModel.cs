using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using ArchivePassword.Core;

namespace ArchivePassword.Desktop;

public sealed class ArchiveContentsViewModel : ObservableObject
{
    private readonly string archivePath;
    private IReadOnlyList<ArchiveEntry> items;
    private IReadOnlyList<ArchiveContentRow> allRows;
    private bool selecting,working;
    private string operationMessage=UiText.Get("T_59a916550385"),outputPath="";
    private TaskCompletionSource idle=CompletedSource();
    private readonly Action? cancel;
    private readonly Func<IReadOnlyList<ArchiveEntry>,IProgress<string>,Task<ArchiveDragOutcome>>? prepareDrag;
    private ArchiveDragLease? dragContent;
    private IReadOnlyList<ArchiveEntry> dragSelection=[];
    private bool dragCleanupNeeded;
    private readonly Action<string>? reportCleanupWarning;
    public ArchiveContentsViewModel(string archivePath,IReadOnlyList<ArchiveEntry> items,Func<IReadOnlyList<ArchiveEntry>,IProgress<string>,Task<string?>>? extract=null,Action? cancel=null,Action<string>? open=null,
        string? format=null,Func<ArchiveEditAction,IReadOnlyList<ArchiveEntry>,IReadOnlyList<ArchiveEntry>,IProgress<string>,Task<ArchiveEditOutcome?>>? edit=null,
        Func<ArchiveEntry,IProgress<string>,Task>? preview=null,
        Func<IReadOnlyList<ArchiveEntry>,IProgress<string>,Task<ArchiveDragOutcome>>? prepareDrag=null,Action<string>? reportCleanupWarning=null)
    {
        this.archivePath=archivePath;this.items=items;this.cancel=cancel;this.prepareDrag=prepareDrag;this.reportCleanupWarning=reportCleanupWarning;
        allRows=items.Select(entry=>new ArchiveContentRow(entry)).ToArray();Rows=new(allRows);
        AttachRows();
        SelectVisibleCommand=new RelayCommand(()=>Select(true),()=>!IsWorking && Rows.Count>0);
        ClearSelectionCommand=new RelayCommand(()=>Select(false),()=>!IsWorking && allRows.Any(row=>row.Selected));
        ExtractSelectedCommand=new AsyncCommand(async()=>
        {
            IsWorking=true;idle=new TaskCompletionSource();OutputPath="";
            try {OutputPath=await extract!(SelectedEntries,new Progress<string>(value=>OperationMessage=value)) ?? "";}
            finally {IsWorking=false;idle.TrySetResult();}
        },()=>extract is not null && !IsWorking && allRows.Any(row=>row.Selected));
        CancelCommand=new RelayCommand(()=>{if(IsWorking) cancel?.Invoke();else {OperationMessage=UiText.Get("Drag.Cancelled");ClearDrag();}},()=>IsWorking || dragContent is not null);
        PrepareDragCommand=new AsyncCommand(()=>DragAsync(_=>null),()=>CanDrag);
        PreviewCommand=new AsyncCommand(async()=>
        {
            var entry=CurrentRow!.Entry;IsWorking=true;idle=new TaskCompletionSource();
            try {await preview!(entry,new Progress<string>(value=>OperationMessage=value));}
            finally {IsWorking=false;idle.TrySetResult();}
        },()=>preview is not null && !IsWorking && CurrentRow is not null && ArchivePreviewPolicy.Rejection(CurrentRow.Entry)=="");
        OpenResultCommand=new RelayCommand(()=>open?.Invoke(OutputPath),()=>!IsWorking && OutputPath.Length>0 && open is not null);
        EditingAvailable=edit is not null && !VolumeCompressionService.IsVolumePath(archivePath) && !Path.GetExtension(archivePath).Equals(".exe",StringComparison.OrdinalIgnoreCase) && format?.ToLowerInvariant() is "7z" or "zip";
        AsyncCommand EditingCommand(ArchiveEditAction action,Func<bool> allowed)=>new(async()=>
        {
            IsWorking=true;idle=new TaskCompletionSource();OutputPath="";
            try
            {
                if(!ClearDrag()) return;
                var outcome=await edit!(action,this.items,allRows.Any(row=>row.Selected)?SelectedEntries:[],new Progress<string>(value=>OperationMessage=value));
                if(outcome?.Engine.IsSuccess==true)
                {
                    this.items=outcome.Items;allRows=this.items.Select(entry=>new ArchiveContentRow(entry)).ToArray();AttachRows();FilterRows();
                    Changed(nameof(Summary));SelectionChanged();
                    OutputPath=Path.GetDirectoryName(archivePath)!;
                    OperationMessage=UiText.Get("T_449c0abb4f19")+outcome.BackupPath;
                }
            }
            finally {IsWorking=false;idle.TrySetResult();}
        },()=>EditingAvailable && !IsWorking && allowed());
        AddFilesCommand=EditingCommand(ArchiveEditAction.AddFiles,()=>true);
        AddFolderCommand=EditingCommand(ArchiveEditAction.AddFolder,()=>true);
        DeleteSelectedCommand=EditingCommand(ArchiveEditAction.Delete,()=>allRows.Any(row=>row.Selected));
        ReplaceSelectedCommand=EditingCommand(ArchiveEditAction.Replace,()=>allRows.Count(row=>row.Selected)==1 && allRows.Single(row=>row.Selected).Entry.Directory==false);
    }
    private void AttachRows() {foreach(var row in allRows) row.PropertyChanged+=(_,e)=>{if(e.PropertyName==nameof(ArchiveContentRow.Selected) && !selecting) SelectionChanged();};}
    private void FilterRows() {Rows.Clear();foreach(var row in allRows.Where(row=>row.Path.Contains(search,StringComparison.CurrentCultureIgnoreCase))) Rows.Add(row);}
    public bool EditingAvailable {get;}
    public string EditingHint=>EditingAvailable?UiText.Get("T_d12b676ae379"):UiText.Get("T_e1fb8aa2fcac");
    public ICommand AddFilesCommand {get;}
    public ICommand AddFolderCommand {get;}
    public ICommand DeleteSelectedCommand {get;}
    public ICommand ReplaceSelectedCommand {get;}
    public ICommand PrepareDragCommand {get;}
    public bool CanDrag=>prepareDrag is not null && !IsWorking && (allRows.Any(row=>row.Selected) || CurrentRow is not null);
    public bool ClearDrag()
    {
        var warning=dragContent?.Cleanup();dragCleanupNeeded=warning is not null;
        if(warning is not null) {OperationMessage=warning;reportCleanupWarning?.Invoke(warning);}
        else {dragContent=null;dragSelection=[];}
        CommandManager.InvalidateRequerySuggested();
        return warning is null;
    }
    public async Task DragAsync(Func<IReadOnlyList<string>,bool?> copy)
    {
        if(!CanDrag) return;
        var selection=allRows.Any(row=>row.Selected)?SelectedEntries:ArchiveSelection.Expand(items,[CurrentRow!.Entry.Index]);
        IsWorking=true;idle=new TaskCompletionSource();
        try
        {
            if(dragCleanupNeeded && !ClearDrag()) return;
            if(dragContent is null || !dragSelection.SequenceEqual(selection))
            {
                if(!ClearDrag()) return;
                OperationMessage=UiText.Get("Drag.Preparing");
                var result=await prepareDrag!(selection,new Progress<string>(value=>OperationMessage=value));
                if(result.Warning is not null) reportCleanupWarning?.Invoke(result.Warning);
                if(result.Content is null)
                {OperationMessage=result.Warning ?? UiText.Get(result.Engine.Status=="cancelled"?"Drag.Cancelled":"Drag.Failed");return;}
                dragContent=result.Content;dragSelection=selection.ToArray();
                // Keep origin/cleanup warnings visible instead of replacing them with readiness text.
                OperationMessage=string.Join("\n",new[]{result.Warning,UiText.Get("Drag.Ready")}.Where(s=>!string.IsNullOrEmpty(s)));
            }
            var copied=copy(dragContent.Paths);
            if(copied is not null)
            {
                OperationMessage=UiText.Get(copied.Value?"Drag.Copied":"Drag.Cancelled");ClearDrag();
            }
        }
        catch(Exception error) when(error is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {OperationMessage=UiText.Get("Drag.Failed");ClearDrag();}
        finally {IsWorking=false;idle.TrySetResult();CommandManager.InvalidateRequerySuggested();}
    }
    private static TaskCompletionSource CompletedSource() {var source=new TaskCompletionSource();source.SetResult();return source;}
    public Task WaitForIdleAsync()=>idle.Task;
    public void Cancel()=>cancel?.Invoke();
    public bool IsWorking {get=>working;private set {if(Set(ref working,value)) Changed(nameof(CanSelect));}}
    public bool CanSelect=>!IsWorking;
    public string OperationMessage {get=>operationMessage;set=>Set(ref operationMessage,value);}
    public string OutputPath {get=>outputPath;private set=>Set(ref outputPath,value);}
    public ICommand SelectVisibleCommand {get;}
    public ICommand ClearSelectionCommand {get;}
    public ICommand ExtractSelectedCommand {get;}
    public ICommand ExtractAllCommand => new RelayCommand(() =>
    {
        selecting=true;
        try {foreach(var row in allRows) row.Selected=true;}
        finally {selecting=false;SelectionChanged();}
        ExtractSelectedCommand.Execute(null);
    },()=>CanSelect && allRows.Count>0);
    public ICommand CancelCommand {get;}
    public ICommand PreviewCommand {get;}
    private ArchiveContentRow? currentRow;
    public ArchiveContentRow? CurrentRow {get=>currentRow;set {if(Set(ref currentRow,value)) {Changed(nameof(PreviewHint));CommandManager.InvalidateRequerySuggested();}}}
    public string PreviewHint=>CurrentRow is null?UiText.Get("Preview.SelectOne"):ArchivePreviewPolicy.Rejection(CurrentRow.Entry) switch
    {"preview_too_large"=>UiText.Get("Preview.TooLarge"),"preview_unsupported"=>UiText.Get("Preview.Unsupported"),_=>UiText.Get("Preview.Shortcut")};
    public ICommand OpenResultCommand {get;}
    public IReadOnlyList<ArchiveEntry> SelectedEntries=>ArchiveSelection.Expand(items,allRows.Where(row=>row.Selected).Select(row=>row.Entry.Index));
    public string SelectionSummary=>allRows.Any(row=>row.Selected)?UiText.Format("T_09dc19185a68", allRows.Count(row=>row.Selected), SelectedEntries.Count):UiText.Get("T_1b210b6e4fb0");
    private void SelectionChanged() {Changed(nameof(SelectionSummary));CommandManager.InvalidateRequerySuggested();}
    private void Select(bool visible)
    {
        selecting=true;try {foreach(var row in visible?Rows:allRows) row.Selected=visible;} finally {selecting=false;SelectionChanged();}
    }
    private string search = "";
    public string ArchivePath => archivePath;
    public string ArchiveName => Path.GetFileName(archivePath);
    public ObservableCollection<ArchiveContentRow> Rows { get; }
    public string Search { get => search; set { if (Set(ref search, value)) { FilterRows(); Changed(nameof(Summary)); } } }
    public string Summary => UiText.Format("T_55f9d9c65be9", Rows.Count, allRows.Count, items.Count(value => !value.Directory), items.Count(value => value.Directory));
}
public enum ArchiveEditAction {AddFiles,AddFolder,Delete,Replace}

public sealed class ArchiveContentRow(ArchiveEntry entry) : ObservableObject
{
    private bool selected;
    public ArchiveEntry Entry=>entry;
    public bool Selected {get=>selected;set=>Set(ref selected,value);}
    public string Path => entry.Path;
    public string Kind => entry.Directory ? UiText.Get("T_7c7802d8adae") : UiText.Get("T_39932f24fe11");
    public string SizeText => entry.Directory ? "—" : entry.Size < 1024 ? $"{entry.Size} B" : entry.Size < 1024 * 1024 ? $"{entry.Size / 1024.0:0.#} KB" : entry.Size < 1024ul * 1024 * 1024 ? $"{entry.Size / (1024.0 * 1024):0.#} MB" : $"{entry.Size / (1024.0 * 1024 * 1024):0.#} GB";
    public string EncryptionText => entry.Encrypted ? UiText.Get("T_b66975fbd35f") : UiText.Get("T_7c568abbd672");
}
