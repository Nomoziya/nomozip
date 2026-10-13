using System.Windows.Input;

namespace ArchivePassword.Desktop;
public sealed partial class MainViewModel
{
    private int queueConcurrency=1;
    private readonly Dictionary<ArchiveTaskRow,(CancellationTokenSource Skip,bool Trying)> queueAttempts=[];
    public int QueueConcurrency {get=>queueConcurrency;set {if(IsBusy) return;if(Set(ref queueConcurrency,value==2?2:1)) Changed(nameof(QueueConcurrencyIndex));}}
    public int QueueConcurrencyIndex {get=>QueueConcurrency-1;set=>QueueConcurrency=value+1;}
    private void RefreshQueuePasswordAction()
    {CanSkipPasswords=SelectedTask is {} selected && queueAttempts.TryGetValue(selected,out var attempt) && attempt.Trying;CommandManager.InvalidateRequerySuggested();}
    private void SkipSelectedPasswordAttempts()
    {if(queueAttempts.Count>0){if(SelectedTask is {} selected && queueAttempts.TryGetValue(selected,out var attempt) && attempt.Trying) attempt.Skip.Cancel();}else skipPasswords?.Cancel();}
    public ICommand MoveTaskUpCommand {get;private set;}=null!;
    public ICommand MoveTaskDownCommand {get;private set;}=null!;
    public ICommand ExtractSelectedTaskCommand {get;private set;}=null!;
    public ICommand RemoveCompletedTasksCommand {get;private set;}=null!;
    private void InitializeQueueCommands()
    {
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
        SelectedTask=selected;Footer=$"已将 {selected.Name} {(direction<0?"上移":"下移")}。";
    }
    private void RemoveCompletedTasks()
    {
        if(!IsIdle) return;
        var completed=Tasks.Where(task=>task.Completed).ToArray();foreach(var task in completed) Tasks.Remove(task);
        if(SelectedTask is {} selected && !Tasks.Contains(selected)) SelectedTask=Tasks.FirstOrDefault();
        Footer=$"已移除 {completed.Length} 个完成任务；输出文件保留。";
    }
}
