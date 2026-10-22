using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using ArchivePassword.Core;

namespace ArchivePassword.Desktop;

public sealed partial class ArchiveContentsViewModel : ObservableObject
{
    private readonly string archivePath;
    private IReadOnlyList<ArchiveEntry> items;
    private IReadOnlyList<ArchiveContentRow> allRows;
    private IReadOnlyList<ArchiveContentRow> orderedRows;
    private IReadOnlyList<ArchiveContentRow> selectedRows=[];
    private IReadOnlyList<ArchiveEntry> selectedEntries=[];
    private int visibleSelectedRows,hiddenSelectedEntries,fileCount,folderCount;
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
        Func<IReadOnlyList<ArchiveEntry>,IProgress<string>,Task<ArchiveDragOutcome>>? prepareDrag=null,Action<string>? reportCleanupWarning=null,
        BrowserPreferences? browserPreferences=null)
    {
        this.archivePath=archivePath;this.items=items;this.cancel=cancel;this.prepareDrag=prepareDrag;this.reportCleanupWarning=reportCleanupWarning;
        this.browserPreferences=browserPreferences is {IsValid:true}?browserPreferences:new();gridView=this.browserPreferences.GridView;
        allRows=BuildRows(items);orderedRows=OrderRows(allRows);Rows=new(allRows);VisibleRows=[];
        UpdateInventoryCounts();
        AttachRows();
        SelectVisibleCommand=new RelayCommand(()=>Select(true),()=>CanSelect && VisibleRows.Count>0);
        ClearSelectionCommand=new RelayCommand(()=>Select(false),()=>CanSelect && selectedRows.Count>0);
        ExtractSelectedCommand=new AsyncCommand(async()=>
        {
            idle=new TaskCompletionSource();IsWorking=true;OutputPath="";
            try {OutputPath=await extract!(SelectedEntries,new Progress<string>(value=>OperationMessage=value)) ?? "";}
            finally {IsWorking=false;idle.TrySetResult();}
        },()=>extract is not null && CanSelect && selectedRows.Count>0);
        CancelCommand=new RelayCommand(()=>{if(IsWorking) cancel?.Invoke();else {OperationMessage=UiText.Get("Drag.Cancelled");ClearDrag();}},()=>IsWorking || dragContent is not null);
        PrepareDragCommand=new AsyncCommand(()=>DragAsync(_=>null),()=>CanDrag);
        PreviewCommand=new AsyncCommand(async()=>
        {
            var entry=CurrentRow!.Entry;idle=new TaskCompletionSource();IsWorking=true;
            try {await preview!(entry,new Progress<string>(value=>OperationMessage=value));}
            finally {IsWorking=false;idle.TrySetResult();}
        },()=>preview is not null && CanSelect && CurrentRow is not null && ArchivePreviewPolicy.Rejection(CurrentRow.Entry)=="");
        FolderUpCommand=new RelayCommand(()=>Navigate(Parent(currentFolder)),()=>CanSelect && currentFolder.Length>0);
        OpenCurrentEntryCommand=new RelayCommand(()=>{if(CurrentRow?.Entry.Directory==true)Navigate(Normalize(CurrentRow.Path));else if(PreviewCommand.CanExecute(null))PreviewCommand.Execute(null);},()=>CanSelect && CurrentRow is not null && (CurrentRow.Entry.Directory || PreviewCommand.CanExecute(null)));
        OpenResultCommand=new RelayCommand(()=>open?.Invoke(OutputPath),()=>!IsWorking && OutputPath.Length>0 && open is not null);
        EditingAvailable=edit is not null && !VolumeCompressionService.IsVolumePath(archivePath) && !Path.GetExtension(archivePath).Equals(".exe",StringComparison.OrdinalIgnoreCase) && format?.ToLowerInvariant() is "7z" or "zip";
        AsyncCommand EditingCommand(ArchiveEditAction action,Func<bool> allowed)=>new(async()=>
        {
            idle=new TaskCompletionSource();IsWorking=true;OutputPath="";
            try
            {
                if(!ClearDrag()) return;
                var outcome=await edit!(action,this.items,action==ArchiveEditAction.EditText?[CurrentRow!.Entry]:selectedRows.Count>0?SelectedEntries:[],new Progress<string>(value=>OperationMessage=value));
                if(outcome?.Engine.IsSuccess==true)
                {
                    var currentPath=CurrentRow?.Path;
                    this.items=outcome.Items;
                    if(UsesBackgroundView)
                    {
                        var inventory=await Task.Run(()=>{var rows=BuildRows(outcome.Items);return(rows,ordered:OrderRows(rows));});
                        allRows=inventory.rows;orderedRows=inventory.ordered;
                    }
                    else {allRows=BuildRows(this.items);orderedRows=OrderRows(allRows);}
                    selectedRows=[];selectedEntries=[];UpdateInventoryCounts();
                    AttachRows();
                    while(currentFolder.Length>0 && !allRows.Any(row=>row.Entry.Directory && Normalize(row.Path)==currentFolder))currentFolder=Parent(currentFolder);
                    FilterRows();RefreshBreadcrumbs();
                    CurrentRow=allRows.FirstOrDefault(row=>row.Path==currentPath);
                    Changed(nameof(Summary));Changed(nameof(CompactSummary));SelectionChanged();
                    OutputPath=Path.GetDirectoryName(archivePath)!;
                    OperationMessage=UiText.Get("T_449c0abb4f19")+outcome.BackupPath;
                }
            }
            finally {IsWorking=false;idle.TrySetResult();}
        },()=>EditingAvailable && CanSelect && allowed());
        AddFilesCommand=EditingCommand(ArchiveEditAction.AddFiles,()=>true);
        AddFolderCommand=EditingCommand(ArchiveEditAction.AddFolder,()=>true);
        DeleteSelectedCommand=EditingCommand(ArchiveEditAction.Delete,()=>selectedRows.Count>0);
        ReplaceSelectedCommand=EditingCommand(ArchiveEditAction.Replace,()=>selectedRows.Count==1 && selectedRows[0].Entry.Directory==false);
        EditTextCommand=EditingCommand(ArchiveEditAction.EditText,()=>CurrentRow is not null && ArchivePreviewPolicy.Kind(CurrentRow.Path)=="text" && ArchivePreviewPolicy.Rejection(CurrentRow.Entry)=="");
        FilterRows();RefreshBreadcrumbs();
    }
    private static string Normalize(string path)=>path.Replace('\\','/').TrimEnd('/');
    private static string Parent(string path)=>path.LastIndexOf('/') is var slash && slash>=0?path[..slash]:"";
    private static IReadOnlyList<ArchiveContentRow> BuildRows(IReadOnlyList<ArchiveEntry> entries)
    {
        var rows=entries.Select(entry=>new ArchiveContentRow(entry)).ToList();
        var folders=rows.Where(row=>row.Entry.Directory).Select(row=>Normalize(row.Path)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach(var entry in entries)
        {
            var parent=Parent(Normalize(entry.Path));
            while(parent.Length>0)
            {
                if(folders.Add(parent))rows.Add(new(new ArchiveEntry(uint.MaxValue,parent,0,true,false),true));
                parent=Parent(parent);
            }
        }
        return rows;
    }
    private void AttachRows()
    {
        foreach(var row in allRows)row.PropertyChanged+=(_,e)=>
        {
            if(e.PropertyName!=nameof(ArchiveContentRow.Selected) || selecting)return;
            selecting=true;
            try {if(row.Entry.Directory)foreach(var child in allRows.Where(child=>Normalize(child.Path).StartsWith(Normalize(row.Path)+"/",StringComparison.OrdinalIgnoreCase)))child.Selected=row.Selected;}
            finally {selecting=false;}
            SelectionChanged();
        };
    }
    private static IReadOnlyList<ArchiveContentRow> OrderRows(IEnumerable<ArchiveContentRow> rows) =>
        rows.OrderByDescending(row=>row.Entry.Directory).ThenBy(row=>row.Path,StringComparer.CurrentCultureIgnoreCase).ToArray();
    private void FilterRows()
    {
        if(UsesBackgroundView){QueueViewUpdate();return;}
        var valid=ArchiveEntryFilter.TryCreate(filterExtensions,filterMinimum,filterMaximum,filterKindIndex,out var filter);
        FilterError=valid?"":UiText.Get("Browser.FilterError");
        var acrossFolders=showSelectedOnly || search.Length>0 || filterExtensions.Length>0 || filterMinimum.Length>0 || filterMaximum.Length>0 || filterKindIndex!=0;
        var scope=acrossFolders && searchScopeIndex==1?currentFolder:null;
        bool Matches(ArchiveContentRow row)=>valid && InSearchScope(row,scope) && (!showSelectedOnly || row.Selected) && row.Path.Contains(search,StringComparison.CurrentCultureIgnoreCase) && filter.Matches(row.Entry);
        var matches=orderedRows.Where(Matches).ToArray();
        var visible=acrossFolders?matches:matches.Where(row=>string.Equals(row.ParentPath,currentFolder,StringComparison.OrdinalIgnoreCase)).ToArray();
        ApplyView(matches,visible,acrossFolders);
    }
    private void ApplyView(IReadOnlyList<ArchiveContentRow> matches,IReadOnlyList<ArchiveContentRow> visible,bool acrossFolders)
    {
        var previous=CurrentRow;
        foreach(var row in visible) row.ShowPath=acrossFolders;
        UpdateVisibleSelection(visible);
        Rows.ReplaceAll(matches);VisibleRows.ReplaceAll(visible);
        CurrentRow=previous is not null && visible.Contains(previous)?previous:null;
        Changed(nameof(Summary));Changed(nameof(CompactSummary));Changed(nameof(VisibleSelection));Changed(nameof(CompactSelectionSummary));
        CommandManager.InvalidateRequerySuggested();
    }
    private string filterExtensions="",filterMinimum="",filterMaximum="",filterError="";
    private int filterKindIndex;
    public string FilterExtensions {get=>filterExtensions;set {if(Set(ref filterExtensions,value))FilterRows();}}
    public string FilterMinimum {get=>filterMinimum;set {if(Set(ref filterMinimum,value))FilterRows();}}
    public string FilterMaximum {get=>filterMaximum;set {if(Set(ref filterMaximum,value))FilterRows();}}
    public int FilterKindIndex {get=>filterKindIndex;set {if(Set(ref filterKindIndex,value))FilterRows();}}
    public string FilterError {get=>filterError;private set=>Set(ref filterError,value);}
    public IReadOnlyList<string> FilterKindChoices {get;}=[UiText.Get("Browser.AllKinds"),UiText.Get("Browser.FilesOnly"),UiText.Get("Browser.FoldersOnly")];
    public ICommand ClearFiltersCommand => new RelayCommand(()=>
    {
        search=filterExtensions=filterMinimum=filterMaximum="";filterKindIndex=0;showSelectedOnly=false;
        foreach(var name in new[]{nameof(Search),nameof(FilterExtensions),nameof(FilterMinimum),nameof(FilterMaximum),nameof(FilterKindIndex),nameof(ShowSelectedOnly)})Changed(name);
        FilterRows();
    },()=>CanSelect);
    private bool gridView;
    private bool showSelectedOnly;
    public bool ShowSelectedOnly
    {
        get=>showSelectedOnly;
        set
        {
            if (!Set(ref showSelectedOnly,value)) return;
            if(value)
            {
                searchScopeIndex=0;Changed(nameof(SearchScopeIndex));
                search=filterExtensions=filterMinimum=filterMaximum="";filterKindIndex=0;
                foreach(var name in new[]{nameof(Search),nameof(FilterExtensions),nameof(FilterMinimum),nameof(FilterMaximum),nameof(FilterKindIndex)})Changed(name);
            }
            FilterRows();
        }
    }
    public bool GridView {get=>gridView;set {if(Set(ref gridView,value)){browserPreferences=browserPreferences with {GridView=value};Changed(nameof(ListView));}}}
    public bool ListView {get=>!GridView;set=>GridView=!value;}
    internal void ClearPreviewThumbnails() {foreach(var row in allRows)row.PreviewThumbnail=null;}
    private string currentFolder="";
    public string CurrentFolder=>currentFolder;
    public string ArchiveLocation=>System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(ArchivePath))) is {Length:>0} name?name:System.IO.Path.GetPathRoot(System.IO.Path.GetFullPath(ArchivePath)) ?? "";
    public ObservableCollection<ArchiveBreadcrumb> Breadcrumbs {get;}=[];
    public ResettableCollection<ArchiveContentRow> VisibleRows {get;}
    public ICommand FolderUpCommand {get;}
    public ICommand OpenCurrentEntryCommand {get;}
    public void Navigate(string path)
    {
        path=Normalize(path);
        if(!CanSelect || path.Length>0 && !allRows.Any(row=>row.Entry.Directory && string.Equals(Normalize(row.Path),path,StringComparison.OrdinalIgnoreCase)))return;
        currentFolder=path;search=filterExtensions=filterMinimum=filterMaximum="";filterKindIndex=0;showSelectedOnly=false;
        foreach(var name in new[]{nameof(Search),nameof(FilterExtensions),nameof(FilterMinimum),nameof(FilterMaximum),nameof(FilterKindIndex),nameof(CurrentFolder),nameof(ShowSelectedOnly)})Changed(name);
        FilterRows();RefreshBreadcrumbs();CurrentRow=null;
    }
    private void RefreshBreadcrumbs()
    {
        Breadcrumbs.Clear();Breadcrumbs.Add(new(ArchiveName,new RelayCommand(()=>Navigate(""),()=>CanSelect)));
        var path="";
        foreach(var part in currentFolder.Split('/',StringSplitOptions.RemoveEmptyEntries))
        {path=path.Length==0?part:path+"/"+part;var destination=path;Breadcrumbs.Add(new(part,new RelayCommand(()=>Navigate(destination),()=>CanSelect)));}
        Changed(nameof(CurrentFolder));CommandManager.InvalidateRequerySuggested();
    }
    public bool EditingAvailable {get;}
    public string EditingHint=>EditingAvailable?UiText.Get("T_d12b676ae379"):UiText.Get("T_e1fb8aa2fcac");
    public ICommand AddFilesCommand {get;}
    public ICommand AddFolderCommand {get;}
    public ICommand DeleteSelectedCommand {get;}
    public ICommand ReplaceSelectedCommand {get;}
    public ICommand EditTextCommand {get;}
    public ICommand PrepareDragCommand {get;}
    public bool CanDrag=>prepareDrag is not null && CanSelect && (selectedRows.Count>0 || CurrentRow is not null);
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
        var selection=selectedRows.Count>0?SelectedEntries:EntriesFor([CurrentRow!]);
        idle=new TaskCompletionSource();IsWorking=true;
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
    public async Task WaitForIdleAsync()
    {
        while(true){var update=viewUpdate;await idle.Task;await update;if(ReferenceEquals(update,viewUpdate) && !IsWorking)return;}
    }
    public void Cancel()=>cancel?.Invoke();
    public bool IsWorking {get=>working;private set {if(Set(ref working,value)) Changed(nameof(CanSelect));}}
    public bool CanSelect=>!IsWorking && !IsRefreshing;
    public string OperationMessage {get=>operationMessage;set {if(Set(ref operationMessage,value))Changed(nameof(ViewStatus));}}
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
    private IReadOnlyList<ArchiveEntry> EntriesFor(IEnumerable<ArchiveContentRow> rows)=>ArchiveSelection.Expand(items,rows.SelectMany(row=>row.Virtual?items.Where(entry=>Normalize(entry.Path).StartsWith(Normalize(row.Path)+"/",StringComparison.OrdinalIgnoreCase)).Select(entry=>entry.Index):[row.Entry.Index]));
    public IReadOnlyList<ArchiveEntry> SelectedEntries=>selectedEntries;
    public string SelectionSummary=>selectedRows.Count>0?UiText.Format("T_09dc19185a68",selectedRows.Count,selectedEntries.Count):UiText.Get("T_1b210b6e4fb0");
    public string CompactSelectionSummary
    {
        get
        {
            return hiddenSelectedEntries==0?UiText.Format("Browser.CompactSelection",selectedEntries.Count):UiText.Format("Browser.HiddenSelection",selectedEntries.Count,hiddenSelectedEntries);
        }
    }
    public string CompactSummary=>UiText.Format("Browser.CompactCount",VisibleRows.Count);
    public bool? VisibleSelection
    {
        get => visibleSelectedRows==0 ? false : visibleSelectedRows==VisibleRows.Count ? true : null;
        set=>SetSelection(VisibleRows,value==true);
    }
    private void SelectionChanged()
    {
        selecting=true;
        try
        {
            var childrenByParent=allRows.GroupBy(row=>Parent(Normalize(row.Path)),StringComparer.OrdinalIgnoreCase).ToDictionary(group=>group.Key,group=>group.ToArray(),StringComparer.OrdinalIgnoreCase);
            foreach(var folder in allRows.Where(row=>row.Entry.Directory).OrderByDescending(row=>Normalize(row.Path).Count(c=>c=='/')))
            {
                if(childrenByParent.TryGetValue(Normalize(folder.Path),out var children))folder.Selected=children.All(row=>row.Selected);
            }
        }
        finally {selecting=false;}
        selectedRows=allRows.Where(row=>row.Selected).ToArray();
        var indices=selectedRows.Where(row=>!row.Virtual).Select(row=>row.Entry.Index).ToArray();
        selectedEntries=indices.Length==0?[]:ArchiveSelection.Expand(items,indices);
        UpdateVisibleSelection(VisibleRows);
        if(showSelectedOnly)FilterRows();
        Changed(nameof(SelectionSummary));Changed(nameof(CompactSelectionSummary));Changed(nameof(VisibleSelection));CommandManager.InvalidateRequerySuggested();
    }
    private void UpdateInventoryCounts()
    {
        fileCount=items.Count(entry=>!entry.Directory);folderCount=allRows.Count(row=>row.Entry.Directory);
    }
    private void UpdateVisibleSelection(IReadOnlyList<ArchiveContentRow> rows)
    {
        visibleSelectedRows=0;hiddenSelectedEntries=selectedEntries.Count;
        if(selectedRows.Count==0)return;
        var remaining=selectedEntries.Select(entry=>entry.Index).ToHashSet();
        foreach(var row in rows)
        {
            if(row.Selected)visibleSelectedRows++;
            if(!row.Virtual && remaining.Remove(row.Entry.Index))hiddenSelectedEntries--;
        }
    }
    private void SetSelection(IEnumerable<ArchiveContentRow> rows,bool value)
    {
        selecting=true;
        try
        {
            var chosen=rows.ToArray();foreach(var row in chosen)row.Selected=value;
            if(chosen.Length!=allRows.Count)
            {
                var folders=chosen.Where(row=>row.Entry.Directory).Select(row=>Normalize(row.Path)).ToHashSet(StringComparer.OrdinalIgnoreCase);
                foreach(var child in allRows)
                {
                    var parent=Parent(Normalize(child.Path));
                    while(parent.Length>0){if(folders.Contains(parent)){child.Selected=value;break;}parent=Parent(parent);}
                }
            }
        }
        finally {selecting=false;SelectionChanged();}
    }
    private void Select(bool visible)
    {
        SetSelection(visible?VisibleRows:allRows,visible);
    }
    private string search = "";
    public string ArchivePath => archivePath;
    public string ArchiveName => Path.GetFileName(archivePath);
    public ResettableCollection<ArchiveContentRow> Rows { get; }
    public string Search { get => search; set { if (Set(ref search, value)) { FilterRows(); Changed(nameof(Summary));Changed(nameof(CompactSummary)); Changed(nameof(VisibleSelection)); } } }
    public string Summary => UiText.Format("T_55f9d9c65be9",VisibleRows.Count,allRows.Count,fileCount,folderCount);
}
public sealed record ArchiveBreadcrumb(string Label,ICommand NavigateCommand);
public enum ArchiveEditAction {AddFiles,AddFolder,Delete,Replace,EditText}

public sealed class ArchiveContentRow(ArchiveEntry entry,bool virtualFolder=false) : ObservableObject
{
    internal string ParentPath {get;}=ParentOf(entry.Path);
    private static string ParentOf(string path) {var normalized=path.Replace('\\','/').TrimEnd('/');var slash=normalized.LastIndexOf('/');return slash<0?"":normalized[..slash];}
    public bool Virtual=>virtualFolder;
    private System.Windows.Media.ImageSource? previewThumbnail;
    public System.Windows.Media.ImageSource? PreviewThumbnail {get=>previewThumbnail;internal set=>Set(ref previewThumbnail,value);}
    private bool showPath;
    public bool ShowPath
    {
        get=>showPath;
        set
        {
            if(showPath==value)return;
            // Applying a large result already refreshes command state once after the batch.
            showPath=value;Changed(nameof(ShowPath));Changed(nameof(DisplayName));Changed(nameof(DirectoryLabel));
        }
    }
    private readonly string displayName=System.IO.Path.GetFileName(entry.Path.Replace('\\','/').TrimEnd('/'));
    internal string Name=>displayName;
    public string FileName=>displayName;
    private readonly string extension=entry.Directory?"":System.IO.Path.GetExtension(entry.Path);
    public string Extension=>extension;
    public string FileStem=>extension.Length==0?displayName:displayName[..^extension.Length];
    public string DirectoryLabel=>ShowPath?(ParentPath.Length>0?ParentPath:UiText.Get("Browser.ArchiveRoot")):"";
    public string DisplayName=>ShowPath?Path:displayName;
    private bool selected;
    public ArchiveEntry Entry=>entry;
    public bool Selected {get=>selected;set=>Set(ref selected,value);}
    public string Path => entry.Path;
    public string Kind => FileKindIcon.TypeDescription(Path,entry.Directory);
    public string SizeText => entry.Directory ? "—" : entry.Size < 1024 ? $"{entry.Size} B" : entry.Size < 1024 * 1024 ? $"{entry.Size / 1024.0:0.#} KB" : entry.Size < 1024ul * 1024 * 1024 ? $"{entry.Size / (1024.0 * 1024):0.#} MB" : $"{entry.Size / (1024.0 * 1024 * 1024):0.#} GB";
    public string ModifiedText=>entry.ModifiedLocal?.ToString("yyyy/MM/dd HH:mm") ?? "—";
    public string EncryptionText => entry.Directory?"—":entry.Encrypted ? UiText.Get("T_b66975fbd35f") : UiText.Get("T_7c568abbd672");
}
