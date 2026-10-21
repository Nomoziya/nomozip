using System.ComponentModel;
using System.Collections.ObjectModel;
using System.Windows.Data;
using ArchivePassword.Core;

namespace ArchivePassword.Desktop;

public sealed partial class MainViewModel
{
    private int settingsSection;
    private bool preparingExtraction;
    public int SettingsSection { get => settingsSection; set => Set(ref settingsSection, Math.Clamp(value, 0, 5)); }
    public IReadOnlyList<string> SettingsCategories { get; } = [UiText.Get("Redesign.General"), UiText.Get("Redesign.FileOperations"), UiText.Get("Redesign.Security"), UiText.Get("Redesign.Integration"), UiText.Get("Redesign.Updates"), UiText.Get("Redesign.About")];
    public bool PreparingExtraction { get => preparingExtraction; internal set { if (Set(ref preparingExtraction, value)) { Changed(nameof(CanConfigureExtraction));Changed(nameof(CanChangeExtractionLayout));System.Windows.Input.CommandManager.InvalidateRequerySuggested(); } } }
    public bool CanConfigureExtraction => IsIdle || PreparingExtraction;
    public bool CanChangeExtractionLayout=>CanConfigureExtraction && SelectedTask?.Checkpoint is null;
    public bool? CreateArchiveFolder
    {
        get=>directoryRules.Layout switch {ExtractionLayout.ArchiveFolder=>true,ExtractionLayout.Direct=>false,_=>null};
        set {if(!CanConfigureExtraction || SelectedTask?.Checkpoint is not null)return;directoryRules=directoryRules with {Layout=value==true?ExtractionLayout.ArchiveFolder:value==false?ExtractionLayout.Direct:ExtractionLayout.Smart};Changed();Changed(nameof(DirectoryRules));Changed(nameof(ExtractionSubtitle));}
    }
    public bool HasCompressionActivity => CompressionRunning || CompressionOutput.Length > 0 && compressionActivityStarted;
    private bool compressionActivityStarted;
    private int taskFilterIndex;
    private int taskDensityIndex;
    private bool taskDetailsExpanded;
    public int TaskDensityIndex {get=>taskDensityIndex;set {if(Set(ref taskDensityIndex,Math.Clamp(value,0,1)))Changed(nameof(TaskRowHeight));}}
    public double TaskRowHeight=>TaskDensityIndex==0?64:88;
    public IReadOnlyList<string> TaskDensityChoices {get;}=[UiText.Get("Tasks.Compact"),UiText.Get("Tasks.Comfortable")];
    public bool TaskDetailsExpanded {get=>taskDetailsExpanded;set=>Set(ref taskDetailsExpanded,value);}
    private static bool ShouldExpandTaskDetails(ArchiveTaskRow? row)=>row?.Failure is { } failure && (failure.Status!="cancelled" || failure.Failures.Count>0);
    private ListCollectionView? taskView;
    public ObservableCollection<ArchiveTaskRow> WorkspaceTasks {get;}=[];
    public string WorkspaceTaskSummary=>UiText.Format("Redesign.WorkspaceSummary",WorkspaceTasks.Count(row=>row.Running),WorkspaceTasks.Count(row=>!row.Running && !row.Completed && row.Failure is null),WorkspaceTasks.Count(row=>row.Completed || row.Failure is not null));
    public string FileWorkspaceTaskSummary
    {
        get
        {
            var running=WorkspaceTasks.Count(row=>row.Running);
            var pending=WorkspaceTasks.Count(row=>!row.Running && !row.Completed && row.Failure is null);
            return running>0?UiText.Format("Browser.RunningTasks",running):pending>0?UiText.Format("Browser.PendingTasks",pending):"";
        }
    }
    public int ActiveTaskCount=>WorkspaceTasks.Count(row=>row.Running || !row.Completed && row.Failure is null);
    public ICollectionView TaskView => taskView ??= new ListCollectionView(WorkspaceTasks) { Filter = value => MatchesTaskFilter((ArchiveTaskRow)value) };
    private void SyncExtractionRows()
    {
        foreach(var row in WorkspaceTasks.Where(row=>!row.IsCompression && !Tasks.Contains(row)).ToArray())WorkspaceTasks.Remove(row);
        for(var index=0;index<Tasks.Count;index++)
        {
            var row=Tasks[index];var current=WorkspaceTasks.IndexOf(row);
            if(current<0)WorkspaceTasks.Insert(index,row);else if(current!=index)WorkspaceTasks.Move(current,index);
        }
    }
    private void RemoveWorkspaceTask()
    {
        if(SelectedTask is not {} row)return;
        if(row.IsCompression){row.PropertyChanged-=WorkspaceTaskChanged;WorkspaceTasks.Remove(row);RefreshWorkspaceTasks(false);}else Tasks.Remove(row);
        SelectedTask=WorkspaceTasks.FirstOrDefault();
    }
    internal void OpenTaskOutput(ArchiveTaskRow row)
    {
        if(row.LatestOutputPath is {} path && System.IO.Directory.Exists(path))OpenResultDirectories([path]);
        else Footer=UiText.Get("Redesign.OutputUnavailable");
    }
    internal void ShowCompressionTaskResult(ArchiveTaskRow row)=>dialogs.ShowCompressionResult(row.Name,row.Status,row.Failure ?? new EngineResult {Status="cancelled",Code="cancelled"});
    private ArchiveTaskRow AddCompressionTask()
    {
        var row=new ArchiveTaskRow(CompressionOutput) {IsCompression=true,Running=true,IsIndeterminate=true,Status=UiText.Get("Redesign.CompressionTask")};
        row.PropertyChanged+=WorkspaceTaskChanged;WorkspaceTasks.Add(row);RefreshWorkspaceTasks(false);SelectedTask=row;return row;
    }
    private static readonly string[] taskFilterKeys = ["Redesign.AllTasks","Redesign.RunningTasks","Redesign.PendingTasks","Redesign.EndedTasks","Tasks.Failed","Tasks.Paused"];
    public int TaskFilterIndex { get => taskFilterIndex; set { if (Set(ref taskFilterIndex,Math.Clamp(value,0,taskFilterKeys.Length-1))) { taskView?.Refresh(); Changed(nameof(ShowCompressionActivity)); } } }
    public IReadOnlyList<string> TaskFilters {get;} = taskFilterKeys.Select(UiText.Get).ToArray();
    public IReadOnlyList<TaskFilterBadge> TaskFilterBadges {get;} = taskFilterKeys.Select(key=>new TaskFilterBadge(UiText.Get(key))).ToArray();
    public bool ShowCompressionActivity => HasCompressionActivity && (TaskFilterIndex==0 || TaskFilterIndex==1 && CompressionRunning || TaskFilterIndex==3 && !CompressionRunning);
    private bool MatchesTaskFilter(ArchiveTaskRow row) => MatchesTaskFilter(row,TaskFilterIndex);
    private bool MatchesTaskFilter(ArchiveTaskRow row,int filter) => filter switch
    {
        1=>row.Running,
        2=>!row.Running && !row.Completed && row.Failure is null,
        3=>row.Completed || row.Failure is not null,
        4=>!row.Running && !row.Completed && row.Failure is {Status:not "cancelled"},
        5=>row.Running && (row.Paused || row.PauseRequested || IsQueuePaused && !row.IsCompression),
        _=>true
    };
    private void RefreshWorkspaceTasks(bool refreshView = true)
    {
        // Collection changes already reach ListCollectionView. Refreshing during
        // its source notification would make it process the new row twice.
        if (refreshView && taskView is not null)
        {
            var selected=SelectedTask;taskView.Refresh();
            if(selected is not null && taskView.Contains(selected)){taskView.MoveCurrentTo(selected);SelectedTask=selected;}
        }
        for(var index=0;index<taskFilterKeys.Length;index++) TaskFilterBadges[index].Count=WorkspaceTasks.Count(row=>MatchesTaskFilter(row,index));
        Changed(nameof(TaskSummary));Changed(nameof(WorkspaceTaskSummary));Changed(nameof(ActiveTaskCount));Changed(nameof(FileWorkspaceTaskSummary));Changed(nameof(ShowCompressionActivity));
    }
    private void WorkspaceTaskChanged(object? sender,PropertyChangedEventArgs e)
    {
        if(e.PropertyName==nameof(ArchiveTaskRow.Failure) && ReferenceEquals(sender,SelectedTask) && ShouldExpandTaskDetails(SelectedTask))TaskDetailsExpanded=true;
        if(e.PropertyName is nameof(ArchiveTaskRow.Running) or nameof(ArchiveTaskRow.Completed) or nameof(ArchiveTaskRow.Failure) or nameof(ArchiveTaskRow.Paused) or nameof(ArchiveTaskRow.PauseRequested)) RefreshWorkspaceTasks();
    }

    internal async Task SaveVaultDetailsAsync(SavedPassword edited)
    {
        if (!IsIdle || !vaultAvailable || VaultLocked) return;
        var duplicates = allPasswords.Count(value => value.Id != edited.Id && value.Secret == edited.Secret);
        if (duplicates > 0 && !dialogs.ConfirmDuplicatePassword(duplicates)) return;
        IsBusy = true; idle = new TaskCompletionSource();
        try { await vault.SaveAsync(edited); await RefreshVaultAsync(); SelectedPassword = allPasswords.FirstOrDefault(value => value.Id == edited.Id); Banner = ""; }
        catch (Exception error) when (error is VaultException or System.IO.IOException or UnauthorizedAccessException) { Banner = UiText.Get("T_91d7521c9927"); }
        finally { IsBusy = false; idle.TrySetResult(); }
    }

    internal async Task OpenWorkspaceArchiveAsync(IEnumerable<string> paths)
    {
        if (!IsIdle) return;
        var added = paths.Where(System.IO.File.Exists).Select(System.IO.Path.GetFullPath).ToArray();
        AddFiles(added);
        SelectedTask = Tasks.FirstOrDefault(value => added.Contains(value.Path, StringComparer.OrdinalIgnoreCase));
        Page = 4;
        if(added.Length>1) {Page=0;return;}
        if (SelectedTask is not null) await BrowseArchiveAsync();
    }
}

public sealed class TaskFilterBadge(string label) : ObservableObject
{
    public string Label {get;}=label;
    private int count;
    public int Count {get=>count;set=>Set(ref count,value);}
}
