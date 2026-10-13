using System.Windows.Input;

namespace ArchivePassword.Desktop;
public sealed partial class MainViewModel
{
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
