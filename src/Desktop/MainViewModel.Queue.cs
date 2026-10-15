using System.Windows.Input;

namespace ArchivePassword.Desktop;
public sealed partial class MainViewModel
{
    private ArchivePassword.Core.ExtractionPause? queuePause;
    public ICommand ToggleQueuePauseCommand {get;private set;}=null!;
    public bool CanPauseQueue=>IsBusy && queuePause is not null;
    public string QueuePauseCaption=>queuePause?.IsRequested==true?UiText.Get("T_c4bf814baf3d"):UiText.Get("T_e90e4d4b3a66");
    public string CancelActionCaption=>CanPauseQueue?UiText.Get("T_5d83503a78bc"):UiText.Get("T_d0356fd00208");
    private void ToggleQueuePause()
    {
        if(queuePause is null) return;
        if(queuePause.IsRequested) {queuePause.Resume();Footer=UiText.Get("T_fb1f94c09208");}
        else {queuePause.Pause();Footer=UiText.Get("T_642a46d4d83a");}
        Changed(nameof(QueuePauseCaption));RefreshQueuePasswordAction();
    }
    private int queueConcurrency=1;
    private readonly Dictionary<ArchiveTaskRow,(CancellationTokenSource Skip,bool Trying)> queueAttempts=[];
    public int QueueConcurrency {get=>queueConcurrency;set {if(IsBusy) return;if(Set(ref queueConcurrency,value==2?2:1)) Changed(nameof(QueueConcurrencyIndex));}}
    public int QueueConcurrencyIndex {get=>QueueConcurrency-1;set=>QueueConcurrency=value+1;}
    private void RefreshQueuePasswordAction()
    {CanSkipPasswords=queuePause?.IsRequested!=true && SelectedTask is {} selected && queueAttempts.TryGetValue(selected,out var attempt) && attempt.Trying;CommandManager.InvalidateRequerySuggested();}
    private void SkipSelectedPasswordAttempts()
    {if(queueAttempts.Count>0){if(SelectedTask is {} selected && queueAttempts.TryGetValue(selected,out var attempt) && attempt.Trying) attempt.Skip.Cancel();}else skipPasswords?.Cancel();}
    public ICommand MoveTaskUpCommand {get;private set;}=null!;
    public ICommand MoveTaskDownCommand {get;private set;}=null!;
    public ICommand ExtractSelectedTaskCommand {get;private set;}=null!;
    public ICommand RemoveCompletedTasksCommand {get;private set;}=null!;
    private void InitializeQueueCommands()
    {
        ToggleQueuePauseCommand=new RelayCommand(ToggleQueuePause,()=>CanPauseQueue);
        MoveTaskUpCommand=new RelayCommand(()=>MoveTask(-1),()=>CanMoveTask(-1));
        MoveTaskDownCommand=new RelayCommand(()=>MoveTask(1),()=>CanMoveTask(1));
        ExtractSelectedTaskCommand=new AsyncCommand(()=>ExtractTasksAsync([SelectedTask!]),()=>Page==0 && IsIdle && SelectedTask is {Completed:false} selected && Tasks.Contains(selected) && !string.IsNullOrWhiteSpace(selectedEngine));
        RemoveCompletedTasksCommand=new RelayCommand(RemoveCompletedTasks,()=>IsIdle && Tasks.Any(task=>task.Completed));
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
        var completed=Tasks.Where(task=>task.Completed).ToArray();foreach(var task in completed) Tasks.Remove(task);
        if(SelectedTask is {} selected && !Tasks.Contains(selected)) SelectedTask=Tasks.FirstOrDefault();
        Footer=UiText.Format("T_1e50592d591a", completed.Length);
    }
}
