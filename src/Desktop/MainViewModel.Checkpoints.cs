using System.IO;
using System.Security.Cryptography;
using System.Windows.Input;
using ArchivePassword.Core;
using ArchivePassword.Infrastructure;

namespace ArchivePassword.Desktop;

public sealed partial class MainViewModel
{
    private bool retainExtractionProgress;
    public bool RetainExtractionProgress {get=>retainExtractionProgress;set=>Set(ref retainExtractionProgress,value);}
    public ICommand ResumeCheckpointCommand {get;private set;}=null!;
    public ICommand DiscardCheckpointCommand {get;private set;}=null!;
    private ExtractionCheckpointStore CheckpointStore=>new(Path.Combine(dataDirectory,"extraction-progress"));
    private void InitializeCheckpointCommands()
    {
        ResumeCheckpointCommand=new AsyncCommand(()=>ExtractTasksAsync([SelectedTask!]),()=>Page==0 && IsIdle && SelectedTask is {Completed:false,Checkpoint:{}});
        DiscardCheckpointCommand=new AsyncCommand(DiscardCheckpointAsync,()=>IsIdle && SelectedTask?.Checkpoint is not null);
    }
    private void RefreshCheckpointRows()
    {
        var records=CheckpointStore.ReadPending(path=>Banner=UiText.Format("Checkpoint.ReadFailed",path));
        foreach(var task in Tasks.Where(task=>task.Checkpoint is not null).ToArray())
            if(!records.Any(record=>record.Id==task.Checkpoint!.Id)) task.Checkpoint=null;
        foreach(var record in records)
        {
            var task=Tasks.FirstOrDefault(task=>task.Checkpoint?.Id==record.Id)
                ??Tasks.FirstOrDefault(task=>task.Checkpoint is null && !task.Completed && string.Equals(Path.GetFullPath(task.Path),record.ArchivePath,StringComparison.OrdinalIgnoreCase));
            if(task is null) {task=new(record.ArchivePath);Tasks.Add(task);}
            task.Checkpoint=record;task.FilenameCodePage=record.Binding.FilenameCodePage;
            if(!task.Running)
            {
                task.Completed=record.Phase=="complete";
                if(task.Completed) task.ResultOutputPath=record.Destination;
                task.Status=task.Completed?UiText.Get("Checkpoint.CleanupPending"):UiText.Format("Checkpoint.Available",record.Receipts.Count,record.Entries.Count);
            }
        }
        if(records.Count>0) Footer=UiText.Format("Checkpoint.Found",records.Count);
        Changed(nameof(TaskSummary));Changed(nameof(ExtractButtonText));Changed(nameof(DisplayOutputPath));Changed(nameof(IsOutputPathReadOnly));
        CommandManager.InvalidateRequerySuggested();
    }
    private async Task DiscardCheckpointAsync()
    {
        if(SelectedTask?.Checkpoint is not {} record || !dialogs.ConfirmDiscardCheckpoint(Path.GetFileName(record.ArchivePath))) return;
        IsBusy=true;idle=new TaskCompletionSource();cancellation=new CancellationTokenSource();
        try
        {
            await CheckpointStore.DiscardAsync(record.Id,cancellation.Token);
            var task=SelectedTask!;task.Checkpoint=null;
            if(!task.Completed) task.Status=UiText.Get("T_1bd1893c0900");
            Footer=UiText.Get("Checkpoint.Discarded");Banner="";
        }
        catch(CheckpointException error) {Banner=UiText.Get("Checkpoint.Error."+error.Code);}
        catch(OperationCanceledException) {Banner=UiText.Get("Checkpoint.DiscardFailed");}
        catch(Exception error) when(error is IOException or UnauthorizedAccessException)
        {Banner=UiText.Get("Checkpoint.DiscardFailed");}
        finally {cancellation.Dispose();cancellation=null;IsBusy=false;idle.TrySetResult();CommandManager.InvalidateRequerySuggested();}
    }
    private (ArchiveEngineClient Client,ExtractionCheckpointRequest? Request) ExtractionClient(ArchiveTaskRow task,ExtractionPause pause)
    {
        if(task.Checkpoint is null && !RetainExtractionProgress) return (CreateReadClient(task,pause),null);
        var version=task.Checkpoint?.Binding.EngineVersion ?? activeEngine;
        if(!EngineVersions.Contains(version)) throw new CheckpointException("checkpoint_binding_changed");
        var dll=engines.DllPath(version);
        var binding=new CheckpointBinding(version,Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(dll))).ToLowerInvariant(),EngineArchitecture.Current,task.FilenameCodePage);
        return(new(Path.Combine(baseDirectory,"ArchiveEngineWorker.exe"),dll,task.FilenameCodePage,pause),new(CheckpointStore,binding,task.Checkpoint?.Id));
    }
}
