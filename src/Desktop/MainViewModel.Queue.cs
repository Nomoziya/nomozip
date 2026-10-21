using System.Windows.Input;

namespace ArchivePassword.Desktop;
public sealed partial class MainViewModel
{
    private ArchivePassword.Core.ExtractionPause? queuePause;
    public ICommand ToggleQueuePauseCommand {get;private set;}=null!;
    public bool IsQueuePaused => queuePause?.IsRequested == true;
    public bool CanPauseQueue=>IsBusy && queuePause is not null;
    public bool CanToggleTaskPause=>CanPauseQueue && !IsQueuePaused;
    public string QueuePauseCaption=>queuePause?.IsRequested==true?UiText.Get("T_c4bf814baf3d"):UiText.Get("T_e90e4d4b3a66");
    public string CancelActionCaption=>CanPauseQueue?UiText.Get("T_5d83503a78bc"):UiText.Get("T_d0356fd00208");
    private void ToggleQueuePause()
    {
        if(queuePause is null) return;
        if(queuePause.IsRequested) {queuePause.Resume();Footer=UiText.Get("T_fb1f94c09208");}
        else {queuePause.Pause();Footer=UiText.Get("T_642a46d4d83a");}
        Changed(nameof(QueuePauseCaption));Changed(nameof(IsQueuePaused));Changed(nameof(CanToggleTaskPause));RefreshQueuePasswordAction();RefreshWorkspaceTasks();
    }
    private int queueConcurrency=1;
    private readonly Dictionary<ArchiveTaskRow,(CancellationTokenSource Skip,bool Trying)> queueAttempts=[];
    private readonly Dictionary<ArchiveTaskRow,(CancellationTokenSource Cancel,ArchivePassword.Core.ExtractionPause Pause)> taskControls=[];
    public ICommand ToggleSelectedTaskPauseCommand {get;private set;}=null!;
    public ICommand CancelSelectedTaskCommand {get;private set;}=null!;
    public ICommand RetrySelectedTaskCommand {get;private set;}=null!;
    public ICommand RetryWithPasswordCommand {get;private set;}=null!;
    public ICommand RetryFailedTasksCommand {get;private set;}=null!;
    public ICommand OpenTaskSourceCommand {get;private set;}=null!;
    public string SelectedTaskPauseCaption=>UiText.Get(SelectedTask?.PauseRequested==true?"TaskControl.Resume":"TaskControl.Pause");
    private bool CanControlSelectedTask=>SelectedTask is {} row && taskControls.TryGetValue(row,out var control) && !control.Cancel.IsCancellationRequested;
    private bool CanRetrySelectedTask=>IsIdle && SelectedTask is {IsCompression:false,Completed:false,Failure:not null,Checkpoint:null} && !string.IsNullOrWhiteSpace(selectedEngine);
    private void ToggleSelectedTaskPause()
    {
        if(!CanControlSelectedTask || IsQueuePaused)return;
        var row=SelectedTask!;var control=taskControls[row];
        if(row.PauseRequested)control.Pause.Resume();else control.Pause.Pause();
        row.PauseRequested=!row.PauseRequested;
        if(row.PauseRequested)row.Status=UiText.Get("TaskControl.PauseRequested");
        Changed(nameof(SelectedTaskPauseCaption));RefreshQueuePasswordAction();
    }
    private void CancelSelectedTask()
    {
        if(!CanControlSelectedTask)return;
        var row=SelectedTask!;taskControls[row].Cancel.Cancel();row.Status=UiText.Get("TaskControl.Cancelling");
        CommandManager.InvalidateRequerySuggested();
    }
    public int QueueConcurrency {get=>queueConcurrency;set {if(IsBusy) return;if(Set(ref queueConcurrency,value==2?2:1)) Changed(nameof(QueueConcurrencyIndex));}}
    public int QueueConcurrencyIndex {get=>QueueConcurrency-1;set=>QueueConcurrency=value+1;}
    private void RefreshQueuePasswordAction()
    {CanSkipPasswords=queuePause?.IsRequested!=true && SelectedTask is {PauseRequested:false} selected && queueAttempts.TryGetValue(selected,out var attempt) && attempt.Trying;CommandManager.InvalidateRequerySuggested();}
    private void SkipSelectedPasswordAttempts()
    {if(queueAttempts.Count>0){if(SelectedTask is {} selected && queueAttempts.TryGetValue(selected,out var attempt) && attempt.Trying) attempt.Skip.Cancel();}else skipPasswords?.Cancel();}
    public ICommand MoveTaskUpCommand {get;private set;}=null!;
    public ICommand MoveTaskDownCommand {get;private set;}=null!;
    public ICommand ExtractSelectedTaskCommand {get;private set;}=null!;
    public ICommand PrepareSelectedTaskExtractionCommand {get;private set;}=null!;
    public ICommand RemoveCompletedTasksCommand {get;private set;}=null!;
    private void InitializeQueueCommands()
    {
        ToggleSelectedTaskPauseCommand=new RelayCommand(ToggleSelectedTaskPause,()=>CanControlSelectedTask && !IsQueuePaused);
        CancelSelectedTaskCommand=new RelayCommand(CancelSelectedTask,()=>CanControlSelectedTask);
        RetrySelectedTaskCommand=new AsyncCommand(()=>ExtractTasksAsync([SelectedTask!]),()=>CanRetrySelectedTask);
        RetryWithPasswordCommand=new AsyncCommand(()=>ExtractTasksAsync([SelectedTask!],forceManualPassword:true),()=>CanRetrySelectedTask);
        RetryFailedTasksCommand=new AsyncCommand(()=>ExtractTasksAsync(Tasks.Where(row=>!row.Completed && row.Failure is {Status:not "cancelled"} && row.Checkpoint is null).ToArray()),
            ()=>IsIdle && !string.IsNullOrWhiteSpace(selectedEngine) && Tasks.Any(row=>!row.Completed && row.Failure is {Status:not "cancelled"} && row.Checkpoint is null));
        OpenTaskSourceCommand=new RelayCommand(()=>OpenFolder(System.IO.Path.GetDirectoryName(SelectedTask!.Path)!),
            ()=>SelectedTask is {} row && System.IO.Directory.Exists(System.IO.Path.GetDirectoryName(row.Path)));
        ToggleQueuePauseCommand=new RelayCommand(ToggleQueuePause,()=>CanPauseQueue);
        MoveTaskUpCommand=new RelayCommand(()=>MoveTask(-1),()=>CanMoveTask(-1));
        MoveTaskDownCommand=new RelayCommand(()=>MoveTask(1),()=>CanMoveTask(1));
        ExtractSelectedTaskCommand=new AsyncCommand(()=>ExtractTasksAsync([SelectedTask!]),()=>Page==0 && IsIdle && SelectedTask is {Completed:false} selected && Tasks.Contains(selected) && !string.IsNullOrWhiteSpace(selectedEngine));
        PrepareSelectedTaskExtractionCommand=new AsyncCommand(async()=>{if(dialogs.ConfirmWorkspaceExtraction(1))await ExtractTasksAsync([SelectedTask!]);},()=>ExtractSelectedTaskCommand.CanExecute(null));
        RemoveCompletedTasksCommand=new RelayCommand(RemoveCompletedTasks,()=>IsIdle && WorkspaceTasks.Any(task=>task.Completed && task.Checkpoint is null));
    }
    private bool CanMoveTask(int direction)
    {var index=SelectedTask is {} selected?Tasks.IndexOf(selected):-1;return IsIdle && index>=0 && index+direction>=0 && index+direction<Tasks.Count;}
    private void MoveTask(int direction)
    {
        if(!CanMoveTask(direction)) return;
        var selected=SelectedTask!;var index=Tasks.IndexOf(selected);Tasks.Move(index,index+direction);
        SelectedTask=selected;Footer=UiText.Format("T_ffda925743a3", selected.Name, (direction<0?UiText.Get("T_f853a70b1204"):UiText.Get("T_e75e8b4e5c97")));
    }
    private void RemoveCompletedTasks()
    {
        if(!IsIdle) return;
        var completed=Tasks.Where(task=>task.Completed && task.Checkpoint is null).ToArray();foreach(var task in completed) Tasks.Remove(task);
        var compressed=WorkspaceTasks.Where(task=>task.IsCompression && task.Completed).ToArray();
        foreach(var row in compressed){row.PropertyChanged-=WorkspaceTaskChanged;WorkspaceTasks.Remove(row);}
        RefreshWorkspaceTasks(false);
        if(SelectedTask is {} selected && !WorkspaceTasks.Contains(selected)) SelectedTask=WorkspaceTasks.FirstOrDefault();
        Footer=UiText.Format("T_1e50592d591a", completed.Length+compressed.Length);
    }
}
