using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using ArchivePassword.Core;
using ArchivePassword.Infrastructure;

namespace ArchivePassword.Desktop;
public sealed partial class MainViewModel
{
    private ExtractionDirectoryRules directoryRules=new();
    private List<string> recentOutputPaths=[];
    private Action<IReadOnlyList<string>>? openResultOverride;
    public ExtractionDirectoryRules DirectoryRules=>directoryRules;
    public IReadOnlyList<string> RecentOutputPaths=>recentOutputPaths;
    public bool IsOutputPathReadOnly=>SelectedTask?.Checkpoint is not null || HasShellDestinations || directoryRules.Location!=ExtractionLocation.Custom;
    public string DisplayOutputPath
    {
        get => SelectedTask?.Checkpoint is {} checkpoint ? checkpoint.Destination : HasShellDestinations || directoryRules.Location==ExtractionLocation.Custom ? OutputPath
            : directoryRules.Location==ExtractionLocation.ArchiveParent && Tasks.Count==0 ? UiText.Get("T_7567f17ec844")
            : ExtractionDirectoryPlanner.BaseDirectory(SelectedTask?.Path ?? Tasks.FirstOrDefault()?.Path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"archive.7z"),OutputPath,directoryRules.Location);
        set { if(!IsOutputPathReadOnly) OutputPath=value; }
    }
    public string ExtractionSubtitle=>UiText.Format("Extraction.Subtitle", (directoryRules.Location switch { ExtractionLocation.ArchiveParent=>UiText.Get("T_6dd88ad49bac"),ExtractionLocation.Desktop=>UiText.Get("T_55373b6437b7"),ExtractionLocation.Documents=>UiText.Get("T_3a3e31cae862"),_=>UiText.Get("T_327149af926b") }), (directoryRules.Layout switch { ExtractionLayout.ArchiveFolder=>UiText.Get("T_131f0bc8b9ac"),ExtractionLayout.Smart=>UiText.Get("T_a24b356d60ba"),_=>UiText.Get("T_74953ee22008") }));
    public ICommand EditExtractionRulesCommand { get;private set; }=null!;
    public ICommand TestArchiveCommand { get;private set; }=null!;
    public ICommand ShowTestResultCommand { get;private set; }=null!;
    public ICommand BrowseDefaultOutputCommand {get;private set;}=null!;
    public ICommand SaveFileDefaultsCommand {get;private set;}=null!;
    private string fileDefaultsMessage="";
    private bool defaultOutputInvalid;
    public bool DefaultOutputInvalid {get=>defaultOutputInvalid;private set=>Set(ref defaultOutputInvalid,value);}
    public string FileDefaultsMessage {get=>fileDefaultsMessage;private set=>Set(ref fileDefaultsMessage,value);}
    public string DefaultOutputDirectory
    {
        get=>OutputPath;
        set
        {
            if(!IsIdle || value==OutputPath)return;
            // Defaults must not clear destinations attached to queued shell tasks.
            directoryRules=directoryRules with {Location=ExtractionLocation.Custom};OutputPath=value;
            Changed(nameof(DirectoryRules));Changed(nameof(IsOutputPathReadOnly));Changed(nameof(DisplayOutputPath));Changed(nameof(ExtractionSubtitle));FileDefaultsMessage="";DefaultOutputInvalid=false;
        }
    }
    private void InitializeExtractionToolCommands()
    {
        EditExtractionRulesCommand=new AsyncCommand(EditExtractionRulesAsync,()=>CanConfigureExtraction && SelectedTask?.Checkpoint is null);
        TestArchiveCommand=new AsyncCommand(TestSelectedArchiveAsync,()=>Page==0 && IsIdle && SelectedTask is not null && activeEngine.Length>0);
        ShowTestResultCommand=new RelayCommand(()=>dialogs.ShowExtractionFailure(SelectedTask!.Name,SelectedTask.VerificationResult!,testing:true),()=>IsIdle && SelectedTask?.VerificationResult is not null);
        BrowseDefaultOutputCommand=new RelayCommand(()=>{if(dialogs.SelectOutput(OutputPath) is {} path)DefaultOutputDirectory=path;},()=>IsIdle);
        SaveFileDefaultsCommand=new AsyncCommand(async()=>
        {
            if(directoryRules.Location==ExtractionLocation.Custom && !Path.IsPathFullyQualified(OutputPath)){DefaultOutputInvalid=true;FileDefaultsMessage=UiText.Get("Settings.InvalidOutput");return;}
            DefaultOutputInvalid=false;
            IsBusy=true;idle=new TaskCompletionSource();
            try{await SaveSettingsAsync();FileDefaultsMessage=UiText.Get(settingsWritable?"Settings.DefaultsSaved":"T_a66f3be2b81c");}
            catch(Exception error) when(error is IOException or UnauthorizedAccessException){FileDefaultsMessage=UiText.Get("T_23a051575c7a");}
            finally{IsBusy=false;idle.TrySetResult();}
        },()=>IsIdle);
    }
    private async Task EditExtractionRulesAsync()
    {
        var priorBusy=IsBusy;var priorIdle=idle;IsBusy=true;idle=new TaskCompletionSource();
        try
        {
            var editor=dialogs.EditExtractionRules(directoryRules,OutputPath,recentOutputPaths);
            if(editor is null) return;
            ApplyDirectoryRules(editor.Rules,editor.CustomPath);
            await SaveSettingsAsync();Footer=settingsWritable?UiText.Get("T_d94c48315238"):UiText.Get("T_a66f3be2b81c");
        }
        catch(Exception error) when(error is IOException or UnauthorizedAccessException) { Banner=UiText.Get("T_23a051575c7a"); }
        finally {IsBusy=priorBusy;idle.TrySetResult();idle=priorIdle;}
    }
    internal void ApplyDirectoryRules(ExtractionDirectoryRules rules,string custom)
    {
        DefaultOutputInvalid=false;FileDefaultsMessage="";
        ClearShellDestinations(); directoryRules=rules; OutputPath=custom;
        Changed(nameof(DirectoryRules)); Changed(nameof(IsOutputPathReadOnly)); Changed(nameof(DisplayOutputPath)); Changed(nameof(ExtractionSubtitle));Changed(nameof(CreateArchiveFolder));
    }
    private void SelectCustomOutput(string path)
    { ApplyDirectoryRules(directoryRules with { Location=ExtractionLocation.Custom },path); }
    private void RememberCustomDirectory()
    {
        if(directoryRules.Location==ExtractionLocation.Custom && Path.IsPathFullyQualified(OutputPath))
            recentOutputPaths=new[] {Path.GetFullPath(OutputPath)}.Concat(recentOutputPaths).Distinct(StringComparer.OrdinalIgnoreCase).Take(5).ToList();
    }
    private void OpenSelectedOutput()
    {
        var targets=SelectedTask?.LatestOutputPath is { } selected && Directory.Exists(selected) ? new[] {selected}
            : WorkspaceTasks.Where(task=>task.LatestOutputPath is not null && Directory.Exists(task.LatestOutputPath)).Select(task=>task.LatestOutputPath!).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if(targets.Length==0 && Directory.Exists(OutputPath)) targets=[Path.GetFullPath(OutputPath)];
        OpenResultDirectories(targets);
    }
    private void OpenResultDirectories(IReadOnlyList<string> paths)
    {
        if(paths.Count==0) return;
        if(openResultOverride is not null) { openResultOverride(paths); return; }
        void Open(string path)
        {
            try { OpenFolder(path); }
            catch(Exception error) when(error is Win32Exception or InvalidOperationException) { Banner=UiText.Get("T_f4a81440138a"); }
        }
        if(paths.Count==1) Open(paths[0]);
        else new ResultDirectoriesDialog(paths,Open) {Owner=Application.Current.MainWindow}.ShowDialog();
    }
    internal void ConfigureResultDirectoryValidation(Action<IReadOnlyList<string>> open) =>openResultOverride=open;
    private async Task TestSelectedArchiveAsync()
    {
        var task=SelectedTask!;
        IsBusy=true;idle=new TaskCompletionSource();cancellation=new CancellationTokenSource();skipPasswords=new CancellationTokenSource();Banner="";
        var wasCompleted=task.Completed;
        try
        {
            task.Running=true; task.IsIndeterminate=true; task.VerificationResult=null;
            var client=CreateReadClient(task);
            var service=new ArchiveVerificationService(client,new PasswordCandidateProvider(vault)); var prompts=0;
            var outcome=await service.VerifyAsync(task.Path,AutoTry,
                failure=>dialogs.AskPasswordAsync(task.Name,prompts++>0,allPasswords,false,cancellation.Token,previousFailure:failure,testing:true),
                new Progress<TaskUpdate>(value=>
                {
                    task.Status=value.Message; Footer=UiText.Format("Common.TaskMessage", task.Name, value.Message);CanSkipPasswords=value.State=="password";
                    if(value.Progress is {} progress) {task.IsIndeterminate=progress.Total==0;task.Percent=progress.Total==0?0:Math.Min(100,progress.Completed*100.0/progress.Total);}
                }),cancellation.Token,skipPasswords.Token);
            task.VerificationResult=outcome.Engine;
            task.Status=outcome.Engine.IsSuccess ? (wasCompleted?UiText.Get("T_3bca99f1c692"):UiText.Get("T_3051a776eaa5")) : outcome.Engine.Status=="cancelled"?UiText.Get("T_90b5a4f6315a"):UiText.Get("T_06e5d870ebce")+ErrorText(outcome.Engine.Code);
            Footer=UiText.Format("T_d1dbd2948956", task.Name, task.Status);
            if(outcome.Warning is not null) Banner=outcome.Warning;
        }
        catch(OperationCanceledException) {task.Status=UiText.Get("T_90b5a4f6315a");task.VerificationResult=new EngineResult {Status="cancelled",Code="cancelled"};Footer=UiText.Get("T_71b6e7bd57a8");}
        catch(Exception error) when(error is IOException or UnauthorizedAccessException or ArgumentException) {Banner=UiText.Get("T_8b6f82263946");}
        finally {task.Completed=wasCompleted;task.Running=false;CanSkipPasswords=false;cancellation.Dispose();skipPasswords.Dispose();cancellation=null;skipPasswords=null;IsBusy=false;idle.TrySetResult();}
    }
}
