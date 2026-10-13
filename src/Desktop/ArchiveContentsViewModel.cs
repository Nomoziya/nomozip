using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using ArchivePassword.Core;

namespace ArchivePassword.Desktop;

public sealed class ArchiveContentsViewModel : ObservableObject
{
    private readonly string archivePath;
    private readonly IReadOnlyList<ArchiveEntry> items;
    private readonly IReadOnlyList<ArchiveContentRow> allRows;
    private bool selecting,working;
    private string operationMessage="勾选文件或文件夹；文件夹包含其全部子项。",outputPath="";
    private TaskCompletionSource idle=CompletedSource();
    private readonly Action? cancel;
    public ArchiveContentsViewModel(string archivePath,IReadOnlyList<ArchiveEntry> items,Func<IReadOnlyList<ArchiveEntry>,IProgress<string>,Task<string?>>? extract=null,Action? cancel=null,Action<string>? open=null)
    {
        this.archivePath=archivePath;this.items=items;this.cancel=cancel;
        allRows=items.Select(entry=>new ArchiveContentRow(entry)).ToArray();Rows=new(allRows);
        foreach(var row in allRows) row.PropertyChanged+=(_,e)=>{if(e.PropertyName==nameof(ArchiveContentRow.Selected) && !selecting) SelectionChanged();};
        SelectVisibleCommand=new RelayCommand(()=>Select(true),()=>!IsWorking && Rows.Count>0);
        ClearSelectionCommand=new RelayCommand(()=>Select(false),()=>!IsWorking && allRows.Any(row=>row.Selected));
        ExtractSelectedCommand=new AsyncCommand(async()=>
        {
            IsWorking=true;idle=new TaskCompletionSource();OutputPath="";
            try {OutputPath=await extract!(SelectedEntries,new Progress<string>(value=>OperationMessage=value)) ?? "";}
            finally {IsWorking=false;idle.TrySetResult();}
        },()=>extract is not null && !IsWorking && allRows.Any(row=>row.Selected));
        CancelCommand=new RelayCommand(()=>cancel?.Invoke(),()=>IsWorking);
        OpenResultCommand=new RelayCommand(()=>open?.Invoke(OutputPath),()=>!IsWorking && OutputPath.Length>0 && open is not null);
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
    public ICommand CancelCommand {get;}
    public ICommand OpenResultCommand {get;}
    public IReadOnlyList<ArchiveEntry> SelectedEntries=>ArchiveSelection.Expand(items,allRows.Where(row=>row.Selected).Select(row=>row.Entry.Index));
    public string SelectionSummary=>allRows.Any(row=>row.Selected)?$"已勾选 {allRows.Count(row=>row.Selected)} 项 · 包含 {SelectedEntries.Count} 项（含子项和隐藏项）":"未选择内容";
    private void SelectionChanged() {Changed(nameof(SelectionSummary));CommandManager.InvalidateRequerySuggested();}
    private void Select(bool visible)
    {
        selecting=true;try {foreach(var row in visible?Rows:allRows) row.Selected=visible;} finally {selecting=false;SelectionChanged();}
    }
    private string search = "";
    public string ArchivePath => archivePath;
    public string ArchiveName => Path.GetFileName(archivePath);
    public ObservableCollection<ArchiveContentRow> Rows { get; }
    public string Search { get => search; set { if (Set(ref search, value)) { Rows.Clear(); foreach (var row in allRows.Where(row => row.Path.Contains(search, StringComparison.CurrentCultureIgnoreCase))) Rows.Add(row); Changed(nameof(Summary)); } } }
    public string Summary => $"显示 {Rows.Count} / {allRows.Count} 项 · {items.Count(value => !value.Directory)} 个文件 · {items.Count(value => value.Directory)} 个文件夹";
}

public sealed class ArchiveContentRow(ArchiveEntry entry) : ObservableObject
{
    private bool selected;
    public ArchiveEntry Entry=>entry;
    public bool Selected {get=>selected;set=>Set(ref selected,value);}
    public string Path => entry.Path;
    public string Kind => entry.Directory ? "文件夹" : "文件";
    public string SizeText => entry.Directory ? "—" : entry.Size < 1024 ? $"{entry.Size} B" : entry.Size < 1024 * 1024 ? $"{entry.Size / 1024.0:0.#} KB" : entry.Size < 1024ul * 1024 * 1024 ? $"{entry.Size / (1024.0 * 1024):0.#} MB" : $"{entry.Size / (1024.0 * 1024 * 1024):0.#} GB";
    public string EncryptionText => entry.Encrypted ? "已加密" : "未加密";
}
