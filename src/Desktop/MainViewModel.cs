using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using ArchivePassword.Core;
using ArchivePassword.Infrastructure;

namespace ArchivePassword.Desktop;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly string baseDirectory, dataDirectory, shellClassesPath;
    private readonly IPasswordVault vault;
    private readonly WindowDialogs dialogs;
    private readonly EngineCatalog engines;
    private readonly EngineUpdateService? updater;
    private AvailableEngineUpdate? availableUpdate;
    private OfficialEngineUpdate? availableOfficialUpdate;
    private int engineSourceIndex;
    internal Func<System.Net.Http.HttpClient>? EngineUpdateClientFactory { get; set; }
    private string activeEngine = "", previousEngine = "", updateRepository = "Nomoziya/nomozip";
    private CancellationTokenSource? cancellation, skipPasswords;
    private TaskCompletionSource idle = CompletedSource();
    private List<SavedPassword> allPasswords = [];
    private bool busy, initializing = true, vaultAvailable = true, autoTry = true, canSkipPasswords, settingsWritable = true, narrow;
    private string outputPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Extracted");
    private string banner = "", footer = UiText.Get("T_c35c7ca6d32f"), search = "", selectedEngine = "";
    private string updateMessage = UiText.Get("T_3ec6f6baa17d");
    private int page, conflictPolicy;
    private ArchiveTaskRow? selectedTask;
    private SavedPassword? selectedPassword;
    public ObservableCollection<ArchiveTaskRow> Tasks { get; } = [];
    public ObservableCollection<SavedPassword> PasswordRows { get; } = [];
    public ObservableCollection<string> EngineVersions { get; } = [];
    public string UpdateChannelDescription { get; private set; } = UiText.Get("T_df02d958a65f");

    public MainViewModel(string baseDirectory, string dataDirectory, IPasswordVault vault, WindowDialogs dialogs, string shellClassesPath = @"Software\Classes")
    {
        this.baseDirectory = baseDirectory; this.dataDirectory = dataDirectory; this.vault = vault; this.dialogs = dialogs;
        this.shellClassesPath = shellClassesPath;
        var keyPath = Path.Combine(baseDirectory, "update-public-key.pem");
        var publicKey = File.Exists(keyPath) ? File.ReadAllText(keyPath) : null;
        engines = new EngineCatalog(Path.Combine(baseDirectory, "engines"), Path.Combine(dataDirectory, EngineArchitecture.InstalledDirectoryName), publicKey);
        if (publicKey is not null) updater = new EngineUpdateService(Path.Combine(dataDirectory, EngineArchitecture.InstalledDirectoryName), Path.Combine(dataDirectory, EngineArchitecture.SelectionFileName), publicKey,
            Path.Combine(baseDirectory, "ArchiveEngineWorker.exe"), Path.Combine(baseDirectory, "update-probe/encrypted-header.7z"), engines);
        var channelPath = Path.Combine(baseDirectory, "update-channel.json");
        if (File.Exists(channelPath))
        {
            try
            {
                using var channel = JsonDocument.Parse(File.ReadAllText(channelPath)); updateRepository = channel.RootElement.GetProperty("repository").GetString() ?? "";
                if (channel.RootElement.TryGetProperty("developmentSigningKey", out var development) && development.ValueKind == JsonValueKind.False)
                    UpdateChannelDescription = UiText.Get("T_6629a0133704");
            }
            catch (Exception exception) when (exception is JsonException or KeyNotFoundException) { }
        }
        Tasks.CollectionChanged += (_, e) => { if(e.OldItems is not null) foreach(ArchiveTaskRow row in e.OldItems) row.PropertyChanged-=WorkspaceTaskChanged; if(e.NewItems is not null) foreach(ArchiveTaskRow row in e.NewItems) row.PropertyChanged+=WorkspaceTaskChanged; SyncExtractionRows();RefreshWorkspaceTasks(refreshView: false); Changed(nameof(ExtractButtonText)); CommandManager.InvalidateRequerySuggested(); };
        AddFilesCommand = new RelayCommand(() => AddFiles(dialogs.SelectArchives()), () => IsIdle);
        OpenInputCommand = new AsyncCommand(async () => { if (Page == 3) AddCompressionFilesCommand.Execute(null); else if (Page == 4) await OpenWorkspaceArchiveAsync(dialogs.SelectArchives()); else AddFilesCommand.Execute(null); }, () => IsIdle);
        RemoveFileCommand = new RelayCommand(RemoveWorkspaceTask, () => IsIdle && SelectedTask is {Checkpoint:null});
        BrowseOutputCommand = new RelayCommand(() => { if (dialogs.SelectOutput(OutputPath) is { } path) SelectCustomOutput(path); }, () => CanConfigureExtraction && SelectedTask?.Checkpoint is null);
        ExtractCommand = new AsyncCommand(ExtractAsync, () => Page == 0 && IsIdle && Tasks.Any(value => !value.Completed) && !string.IsNullOrWhiteSpace(selectedEngine));
        BrowseArchiveCommand = new AsyncCommand(BrowseArchiveAsync, () => Page is 0 or 4 && IsIdle && SelectedTask is {} selected && (!selected.IsCompression || selected.Completed) && !string.IsNullOrWhiteSpace(activeEngine));
        ShowFailureCommand = new RelayCommand(() => {if(SelectedTask!.IsCompression)ShowCompressionTaskResult(SelectedTask);else dialogs.ShowExtractionFailure(SelectedTask.Name, SelectedTask.Failure!);}, () => IsIdle && SelectedTask?.Failure is {} failure && (SelectedTask.IsCompression || failure.Failures.Count>0));
        CancelCommand = new RelayCommand(() => cancellation?.Cancel(), () => IsBusy);
        SkipPasswordsCommand = new RelayCommand(SkipSelectedPasswordAttempts, () => IsBusy && CanSkipPasswords);
        OpenOutputCommand = new RelayCommand(OpenSelectedOutput, () => IsIdle && (Directory.Exists(OutputPath) || WorkspaceTasks.Any(task=>task.LatestOutputPath is {} path && Directory.Exists(path))));
        OpenVaultCommand = new RelayCommand(() => Page = 1);
        AddPasswordCommand = new AsyncCommand(() => EditPasswordAsync(null), () => IsIdle && vaultAvailable);
        EditPasswordCommand = new AsyncCommand(() => EditPasswordAsync(SelectedPassword), () => IsIdle && vaultAvailable && SelectedPassword is not null);
        DeletePasswordCommand = new AsyncCommand(DeletePasswordAsync, () => IsIdle && vaultAvailable && SelectedPassword is not null);
        RestoreVaultCommand = new AsyncCommand(RestoreVaultAsync, () => IsIdle);
        ExportVaultCommand = new AsyncCommand(() => TransferVaultAsync(true), () => IsIdle && vaultAvailable && allPasswords.Count > 0);
        ImportVaultCommand = new AsyncCommand(() => TransferVaultAsync(false), () => IsIdle && vaultAvailable);
        CheckUpdatesCommand = new AsyncCommand(CheckUpdatesAsync, () => IsIdle);
        ImportUpdateCommand = new AsyncCommand(ImportUpdateAsync, () => IsIdle && updater is not null);
        InstallUpdateCommand = new AsyncCommand(InstallOnlineUpdateAsync, () => IsIdle && updater is not null && (EngineSourceIndex == 1 ? availableOfficialUpdate is not null : availableUpdate is not null));
        ApplyEngineCommand = new AsyncCommand(() => RunUpdateOperationAsync(async token => { await updater!.ActivateAsync(SelectedEngineVersion, token); await RefreshEngineSelectionAsync(); UpdateMessage = UiText.Get("T_918e0ce2177e"); }), () => IsIdle && updater is not null && SelectedEngineVersion != activeEngine && EngineVersions.Contains(SelectedEngineVersion));
        RollbackEngineCommand = new AsyncCommand(() => RunUpdateOperationAsync(async token => { await updater!.RollbackAsync(token); await RefreshEngineSelectionAsync(); UpdateMessage = UiText.Get("T_17c4979ce5bf"); }), () => IsIdle && updater is not null && EngineVersions.Contains(previousEngine));
        OpenLicensesCommand = new RelayCommand(() => OpenFolder(Path.Combine(baseDirectory, "licenses")));
        InitializeConversionCommands();
        InitializeCompressionCommands();
        InitializeAssociationCommands();
        InitializeApplicationUpdateCommands();
        InitializeExtractionToolCommands();
        InitializeQueueCommands();
        InitializeCheckpointCommands();
        InitializePasswordOrganizationCommands();
        InitializeFileHashCommands();
        InitializeFilenameEncodingCommands();
        InitializeNotificationCommands();
        InitializeVaultProtectionCommands();
        InitializeLanguageCommands();
        InitializeAppearanceCommands();
    }
    private static TaskCompletionSource CompletedSource() { var source = new TaskCompletionSource(); source.SetResult(); return source; }
    public Task WaitForIdleAsync() => idle.Task;
    public ICommand AddFilesCommand { get; }
    public ICommand OpenInputCommand { get; }
    public ICommand RemoveFileCommand { get; }
    public ICommand BrowseOutputCommand { get; }
    public ICommand ExtractCommand { get; }
    public ICommand BrowseArchiveCommand { get; }
    public ICommand ShowFailureCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand SkipPasswordsCommand { get; }
    public ICommand OpenOutputCommand { get; }
    public ICommand OpenVaultCommand { get; }
    public ICommand AddPasswordCommand { get; }
    public ICommand EditPasswordCommand { get; }
    public ICommand DeletePasswordCommand { get; }
    public ICommand RestoreVaultCommand { get; }
    public ICommand ExportVaultCommand { get; }
    public ICommand ImportVaultCommand { get; }
    public ICommand CheckUpdatesCommand { get; }
    public ICommand ImportUpdateCommand { get; }
    public ICommand InstallUpdateCommand { get; }
    public ICommand ApplyEngineCommand { get; }
    public ICommand RollbackEngineCommand { get; }
    public ICommand OpenLicensesCommand { get; }
    public int Page { get => page; set { if (Set(ref page, value)) { Changed(nameof(NavigationIndex)); CommandManager.InvalidateRequerySuggested(); } } }
    public int NavigationIndex { get => Page switch { 0 => 1, 1 => 2, 5 => 3, 2 => -1, _ => 0 }; set { if (value >= 0) Page = value switch { 1 => 0, 2 => 1, 3 => 5, _ => 4 }; } }
    public bool IsBusy { get => busy; private set { if (Set(ref busy, value)) {Changed(nameof(IsIdle));Changed(nameof(CanEditEngineRepository));Changed(nameof(CanConfigureExtraction));Changed(nameof(CanChangeExtractionLayout));Changed(nameof(CanUseSfx));CompressionCapabilitiesChanged();} } }
    public bool IsIdle => !IsBusy && !initializing;
    public bool IsNarrow { get => narrow; set => Set(ref narrow, value); }
    public bool CanSkipPasswords { get => canSkipPasswords; private set => Set(ref canSkipPasswords, value); }
    public string OutputPath { get => outputPath; set { if(Set(ref outputPath,value)) {Changed(nameof(DisplayOutputPath));Changed(nameof(DefaultOutputDirectory));} } }
    public bool AutoTry { get => autoTry; set => Set(ref autoTry, value); }
    public int ConflictPolicy { get => conflictPolicy; set => Set(ref conflictPolicy, value); }
    public string Banner { get => banner; set => Set(ref banner, value); }
    public string Footer { get => footer; set => Set(ref footer, value); }
    public string UpdateMessage { get => updateMessage; set => Set(ref updateMessage, value); }
    public string UpdateRepository { get => updateRepository; set { if (Set(ref updateRepository, value)) { availableUpdate = null; CommandManager.InvalidateRequerySuggested(); } } }
    public IReadOnlyList<string> EngineSourceChoices { get; } = [UiText.Get("EngineSource.Verified"), UiText.Get("EngineSource.Official")];
    public int EngineSourceIndex
    {
        get => engineSourceIndex;
        set
        {
            if (!Set(ref engineSourceIndex, Math.Clamp(value,0,1))) return;
            availableUpdate=null;availableOfficialUpdate=null;
            UpdateMessage=UiText.Get("EngineSource.Recheck");
            Changed(nameof(EngineSourceDescription));Changed(nameof(CanEditEngineRepository));
            CommandManager.InvalidateRequerySuggested();
        }
    }
    public bool CanEditEngineRepository => IsIdle && EngineSourceIndex==0;
    public string EngineSourceDescription => UiText.Get(EngineSourceIndex==1?"EngineSource.OfficialHint":"EngineSource.VerifiedHint");
    public string Search { get => search; set { if (Set(ref search, value)) FilterPasswords(); } }
    public string SelectedEngineVersion { get => selectedEngine; set { if (value is not null && Set(ref selectedEngine, value)) Changed(nameof(EngineSummary)); } }
    public string EngineSummary => string.IsNullOrWhiteSpace(activeEngine) ? UiText.Get("T_b2a1d05496f2") : UiText.Format("T_9a4813174fe6", activeEngine);
    public string PreviousEngineSummary => string.IsNullOrWhiteSpace(previousEngine) ? UiText.Get("T_dca602fdf931") : UiText.Format("T_f0b9c3e0a17b", previousEngine);
    public string VaultSummary => VaultLocked ? UiText.Get("T_b4caab65183a") : vaultAvailable ? UiText.Format("T_357176e2a16d", allPasswords.Count(value => value.AutoTry)) : UiText.Get("T_98b23dcfbd8c");
    public string TaskSummary => UiText.Format("T_3292893c1e67", Tasks.Count(value => !value.Completed), Tasks.Count(value => value.Completed));
    public string ExtractButtonText => UiText.Format("T_ab3ecbe376c2", Tasks.Count(value => !value.Completed));
    public ArchiveTaskRow? SelectedTask { get => selectedTask; set { if(Set(ref selectedTask,value)) {Changed(nameof(DisplayOutputPath));Changed(nameof(IsOutputPathReadOnly));Changed(nameof(CanChangeExtractionLayout));Changed(nameof(SelectedTaskPauseCaption));if(queueAttempts.Count>0) RefreshQueuePasswordAction();} } }
    public SavedPassword? SelectedPassword { get => selectedPassword; set { if (Set(ref selectedPassword, value)) { Changed(nameof(DuplicateSummary)); CommandManager.InvalidateRequerySuggested(); } } }

    public async Task InitializeAsync()
    {
        try
        {
            foreach (var version in await engines.ReadVerifiedVersionsAsync()) EngineVersions.Add(version);
            SelectedEngineVersion = EngineVersions.FirstOrDefault() ?? "";
            await LoadSettingsAsync();
            activeEngine = SelectedEngineVersion;
            if (updater is not null)
            {
                try
                {
                    var selection = await updater.ReadSelectionAsync();
                    if (selection is null && activeEngine.Length > 0) await updater.ActivateAsync(activeEngine);
                    await RefreshEngineSelectionAsync();
                }
                catch (UpdateException) { Banner = UiText.Get("T_1174cb4266e5"); }
            }
            Changed(nameof(EngineSummary));
            await RefreshSfxAvailabilityAsync();
            await RefreshVaultAsync();
            RefreshCheckpointRows();
            if (EngineVersions.Count == 0) Banner = UiText.Get("T_e274b7537023");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        { Banner = UiText.Get("T_b83678cb1bcf"); }
        finally { initializing = false; Changed(nameof(IsIdle));Changed(nameof(CanEditEngineRepository));Changed(nameof(CanConfigureExtraction));Changed(nameof(CanUseSfx)); CompressionCapabilitiesChanged(); CommandManager.InvalidateRequerySuggested(); }
    }
    public void AddFiles(IEnumerable<string> paths)
    {
        if (IsBusy) return;
        foreach (var path in paths.Where(File.Exists))
            if (!Tasks.Any(value => string.Equals(value.Path, Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase)))
                Tasks.Add(new ArchiveTaskRow(Path.GetFullPath(path)) { ShellOutputPath = shellAction is { } action ? new ShellSelection(action, []).OutputFor(Path.GetFullPath(path)) : null });
        Footer = UiText.Get("T_d6b35eaaa1d9"); Changed(nameof(DisplayOutputPath));
    }
    private async Task RefreshVaultAsync()
    {
        await RefreshVaultProtectionAsync();
        if (VaultLocked) { ClearVaultReferences(); NotifyBackupReminder(); CommandManager.InvalidateRequerySuggested(); return; }
        try
        {
            var state = vault is IPasswordVaultBackupState source ? await source.ReadBackupStateAsync() : new VaultBackupState(await vault.ReadAsync(), "untracked");
            allPasswords = state.Passwords.ToList(); vaultContentStamp = state.ContentStamp; vaultAvailable = true;
        }
        catch (VaultException exception) { allPasswords = []; vaultAvailable = false; Banner = exception.BackupAvailable ? UiText.Get("T_33838fbc8ea5") : UiText.Get("T_33e9a9f2b109"); }
        await RefreshBackupReminderAsync();
        RefreshPasswordGroups(); FilterPasswords(); Changed(nameof(VaultSummary)); CommandManager.InvalidateRequerySuggested();
    }
    private void FilterPasswords()
    {
        PasswordRows.Clear();
        foreach (var item in allPasswords.Where(value => (SelectedPasswordGroup?.Value is null || value.Group == SelectedPasswordGroup.Value)
            && PasswordOrganization.MatchesSearch(value, Search))) PasswordRows.Add(item);
        Changed(nameof(PasswordListSummary)); Changed(nameof(DuplicateSummary));
    }
    private Task ExtractAsync()=>ExtractTasksAsync(Tasks.Where(value=>!value.Completed).ToArray());
    private async Task ExtractTasksAsync(IReadOnlyList<ArchiveTaskRow> batch,bool forceManualPassword=false)
    {
        if (batch.Any(task=>task.Checkpoint is null) && directoryRules.Location==ExtractionLocation.Custom && string.IsNullOrWhiteSpace(OutputPath)) { Banner = UiText.Get("T_36711b07451f"); return; }
        IsBusy=true;idle=new TaskCompletionSource();Banner="";cancellation=new CancellationTokenSource();queuePause=new();Changed(nameof(CanPauseQueue));Changed(nameof(CanToggleTaskPause));Changed(nameof(IsQueuePaused));Changed(nameof(QueuePauseCaption));Changed(nameof(CancelActionCaption));
        using var prompts=new SemaphoreSlim(1,1);
        var openResults=new List<string>();
        string Destination(ArchiveTaskRow task)=>task.Checkpoint?.Destination ?? task.ShellOutputPath ?? ExtractionDirectoryPlanner.BaseDirectory(task.Path,OutputPath,directoryRules.Location);
        async Task<T> PromptAsync<T>(Func<Task<T>> show,CancellationToken token,ExtractionPause taskPause)
        {
            while(true)
            {
                await taskPause.WaitAsync(token);
                await prompts.WaitAsync(token);
                if(!taskPause.IsRequested)break;
                prompts.Release();
            }
            try {token.ThrowIfCancellationRequested();return await show();}
            finally {prompts.Release();}
        }
        async Task RunTaskAsync(ArchiveTaskRow task,CancellationToken token)
        {
            using var taskCancel=CancellationTokenSource.CreateLinkedTokenSource(token);
            using var taskPause=new ExtractionPause(queuePause);
            taskControls.Add(task,(taskCancel,taskPause));token=taskCancel.Token;
            task.PauseRequested=false;task.Paused=false;task.Percent=0;
            using var skip=new CancellationTokenSource();queueAttempts.Add(task,(skip,false));
            task.Running=true;task.IsIndeterminate=true;task.Failure=null;
            if(queueAttempts.Count==1) SelectedTask=task;
            var attempts=0;
            var progress=new Progress<TaskUpdate>(value=>
            {
                if(!task.Running || !taskControls.TryGetValue(task,out var currentControl) || currentControl.Cancel!=taskCancel) return;
                if(value.Progress?.PausedState is {} pauseState)task.Paused=pauseState;
                task.Status=value.Progress?.PausedState==true?UiText.Get("T_eb0c326b60ae"):value.Message;Footer=UiText.Format("Common.TaskMessage", task.Name, task.Status);
                queueAttempts[task]=(skip,value.State=="password");RefreshQueuePasswordAction();
                if(value.Progress is {PausedState:null} details) {task.IsIndeterminate=details.Total==0;task.Percent=details.Total==0?0:Math.Min(100,details.Completed*100.0/details.Total);}
                else if(value.Progress is {PausedState:{} paused} state) task.IsIndeterminate=!paused && state.Total==0;
            });
            try
            {
                var destination=Destination(task);
                Func<IReadOnlyList<ArchiveEntry>,string>? resolveDestination=task.Checkpoint is null && task.ShellOutputPath is null && directoryRules.Layout!=ExtractionLayout.Direct
                    ? items=>ExtractionDirectoryPlanner.Resolve(task.Path,destination,directoryRules,items,DateOnly.FromDateTime(DateTime.Today)) : null;
                var (client,checkpoint)=ExtractionClient(task,taskPause);
                var service=new ExtractionService(client,vault,new PasswordCandidateProvider(vault),new WindowsExtractionOrigin(),taskPause);
                var outcome=await service.ExtractAsync(task.Path,destination,AutoTry && !forceManualPassword,
                    failure=>PromptAsync(()=>dialogs.AskPasswordAsync(task.Name,attempts++>0,allPasswords,vaultAvailable,token,previousFailure:failure),token,taskPause),
                    path=>ConflictPolicy==1?Task.FromResult(ExistingFilePolicy.Skip):ConflictPolicy==2?Task.FromResult(ExistingFilePolicy.Replace)
                        :PromptAsync(()=>Task.FromResult(dialogs.ResolveConflict(path)),token,taskPause),
                    progress,token,skip.Token,resolveDestination,checkpoint:checkpoint,
                    resolveFileConflict:conflict=>ConflictPolicy==1?Task.FromResult(new ExistingFileDecision(ExistingFilePolicy.Skip)):ConflictPolicy==2?Task.FromResult(new ExistingFileDecision(ExistingFilePolicy.Replace))
                        :PromptAsync(()=>Task.FromResult(dialogs.ResolveFileConflict(conflict)),token,taskPause));
                task.Completed=outcome.Engine.IsSuccess;
                task.ResultOutputPath=task.Completed?outcome.OutputPath:null;task.SelectedOutputPath=null;
                if(task.Completed) openResults.Add(outcome.OutputPath);
                task.Status=task.Completed?UiText.Get("T_362133bf383f"):outcome.Engine.Status=="cancelled"?UiText.Get("T_a37778f17c5f"):ErrorText(outcome.Engine.Code);
                task.Failure=task.Completed?null:outcome.Engine;
                if(task.Failure?.Failures.Count>0) task.Status+=UiText.Format("T_e3848c27e720", task.Failure.Failures.Count);
                if(outcome.Warning is not null) Banner+=(Banner.Length>0?"\n":"")+(batch.Count>1?task.Name+UiText.Get("Common.Colon"):"")+outcome.Warning;
                Footer=UiText.Format("Common.TaskMessage", task.Name, task.Status);
                if(outcome.Engine.Status=="cancelled" && !taskCancel.IsCancellationRequested) cancellation.Cancel();
            }
            catch(CheckpointException error) {task.Failure=new EngineResult {Code=error.Code};task.Status=UiText.Get("Checkpoint.Error."+error.Code);Banner=task.Status;}
            catch(OperationCanceledException) {task.Failure=new EngineResult {Status="cancelled",Code="cancelled"};task.Status=UiText.Get("T_a37778f17c5f");Footer=UiText.Format("T_39f2e118230d", task.Name);}
            catch(Exception error) when(error is IOException or UnauthorizedAccessException or ArgumentException)
            {task.Failure=new EngineResult {Code="output_write_failed"};task.Status=UiText.Get("T_9719b364c066");Banner+=(Banner.Length>0?"\n":"")+task.Name+UiText.Get("T_51bf39e5552c");}
            finally {task.Running=false;task.PauseRequested=false;task.Paused=false;taskControls.Remove(task);queueAttempts.Remove(task);Changed(nameof(SelectedTaskPauseCaption));RefreshQueuePasswordAction();Changed(nameof(TaskSummary));Changed(nameof(ExtractButtonText));}
        }
        try
        {
            await SaveSettingsAsync();
            await ExtractionQueue.RunAsync(batch,QueueConcurrency,task=>new(task.Path,Destination(task)),RunTaskAsync,cancellation.Token,queuePause);
            await RefreshVaultAsync();
        }
        catch(OperationCanceledException) {Footer=UiText.Get("T_e29c3af1bd15");}
        catch(Exception error) when(error is IOException or UnauthorizedAccessException or ArgumentException)
        {Banner=UiText.Get("T_abcbf20aca41");}
        finally
        {
            var batchSucceeded=!cancellation.IsCancellationRequested && batch.Count>0 && batch.All(task=>task.Completed);
            var shouldOpen=directoryRules.OpenAfterSuccess && batchSucceeded;
            try {RefreshCheckpointRows();}
            catch(Exception error) when(error is IOException or UnauthorizedAccessException) {Banner=UiText.Get("Checkpoint.ScanFailed");}
            CanSkipPasswords=false;queuePause.Resume();queuePause=null;Changed(nameof(CanPauseQueue));Changed(nameof(CanToggleTaskPause));Changed(nameof(IsQueuePaused));Changed(nameof(QueuePauseCaption));Changed(nameof(CancelActionCaption));cancellation.Dispose();cancellation=null;IsBusy=false;
            try {if(batchSucceeded) NotifyCompletedBatch(batch.Count);if(shouldOpen) OpenResultDirectories(openResults.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());}
            finally {idle.TrySetResult();}
        }
    }
    private async Task EditPasswordAsync(SavedPassword? existing)
    {
        var edited = dialogs.EditPassword(existing);
        if (edited is null) return;
        var duplicateCount = allPasswords.Count(value => value.Id != edited.Id && value.Secret == edited.Secret);
        if (duplicateCount > 0 && !dialogs.ConfirmDuplicatePassword(duplicateCount)) return;
        IsBusy = true; idle = new TaskCompletionSource();
        try { await vault.SaveAsync(edited); await RefreshVaultAsync(); Banner = ""; }
        catch (Exception exception) when (exception is VaultException or IOException or UnauthorizedAccessException) { Banner = UiText.Get("T_91d7521c9927"); }
        finally { IsBusy = false; idle.TrySetResult(); CommandManager.InvalidateRequerySuggested(); }
    }
    private async Task DeletePasswordAsync()
    {
        if (SelectedPassword is not { } item || !dialogs.ConfirmDelete(item.Name)) return;
        try { await vault.DeleteAsync(item.Id); await RefreshVaultAsync(); }
        catch (Exception exception) when (exception is VaultException or IOException or UnauthorizedAccessException) { Banner = UiText.Get("T_e12de546f6c3"); }
    }
    private async Task RestoreVaultAsync()=>await VaultOperationAsync(async()=>
    {
        if (!dialogs.ConfirmRestore()) return;
        if (VaultMasterEnabled && vault is IPasswordVaultProtection protection)
        {
            var response=dialogs.AskMasterPassword(MasterPasswordAction.Unlock);if(response is null)return;
            await Task.Run(()=>protection.RestoreMasterBackupAsync(response.Current));
        }
        else await vault.RestoreBackupAsync();
        await RefreshVaultAsync();RecordVaultActivity();Banner=UiText.Get("T_bc8c3fd9b315");
    });
    private async Task CheckUpdatesAsync() => await RunUpdateOperationAsync(async token =>
    {
        UpdateMessage = UiText.Get("T_91834aebecab"); availableUpdate = null; availableOfficialUpdate=null;
        if (EngineSourceIndex==0) UpdateRepository = GitHubEngineChannel.NormalizeRepository(UpdateRepository);
        await SaveSettingsAsync();
        using var client = EngineUpdateClientFactory?.Invoke() ?? CreateUpdateClient();
        if (EngineSourceIndex==1)
        {
            var official=await new OfficialEngineChannel(client).CheckAsync(token);
            if(official is not null && (!Version.TryParse(activeEngine,out var active) || Version.Parse(official.Version)>active)) availableOfficialUpdate=official;
            UpdateMessage=official is null?UiText.Get("EngineSource.NoAsset"):
                UiText.Format(availableOfficialUpdate is null?"EngineSource.Current":"EngineSource.Available",official.Version);
            CommandManager.InvalidateRequerySuggested();return;
        }
        var officialMessage = UiText.Get("T_56d81aa40608");
        try
        {
            using var release = JsonDocument.Parse(await client.GetStringAsync("https://api.github.com/repos/ip7z/7zip/releases/latest", token));
            var latest = release.RootElement.GetProperty("tag_name").GetString();
            officialMessage = UiText.Format("T_89a718b42e08", latest);
        }
        catch (Exception exception) when (exception is System.Net.Http.HttpRequestException or JsonException or KeyNotFoundException) { }
        catch (TaskCanceledException) when (!token.IsCancellationRequested) { }
        var found = await new GitHubEngineChannel(client).CheckAsync(UpdateRepository, token);
        if (found is not null && (!Version.TryParse(activeEngine, out var currentVersion) || Version.Parse(found.Version) > currentVersion)) availableUpdate = found;
        UpdateMessage = officialMessage + (availableUpdate is null ? UiText.Get("T_39d754d63791") : UiText.Format("T_8961163c6eb0", availableUpdate.Version));
        CommandManager.InvalidateRequerySuggested();
    });
    private static System.Net.Http.HttpClient CreateUpdateClient()
    {
        var client = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("ArchivePassword/0.1"); return client;
    }
    private async Task ImportUpdateAsync()
    {
        if (dialogs.SelectEngineUpdate() is not { } path) return;
        await RunUpdateOperationAsync(token => InstallPackageAsync(path, null, token));
    }
    private async Task InstallOnlineUpdateAsync()
    {
        if (EngineSourceIndex==1)
        {
            if(availableOfficialUpdate is not {} official)return;
            await RunUpdateOperationAsync(async token =>
            {
                if(!dialogs.ConfirmEngineUpdate(official.Version,activeEngine)){UpdateMessage=UiText.Get("T_ff2a17217e7c");return;}
                var directory=Path.Combine(dataDirectory,"downloads");Directory.CreateDirectory(directory);
                var path=Path.Combine(directory,Guid.NewGuid().ToString("N")+".exe");
                try
                {
                    using var client=EngineUpdateClientFactory?.Invoke() ?? CreateUpdateClient();
                    await new OfficialEngineChannel(client).DownloadAsync(official,path,new Progress<string>(value=>UpdateMessage=value),token);
                    var installed=await updater!.InstallOfficialAsync(official,path,activeEngine,new Progress<string>(value=>UpdateMessage=value),token);
                    await updater.ActivateAsync(installed,token);await RefreshEngineSelectionAsync();
                    availableOfficialUpdate=null;UpdateMessage=UiText.Get("EngineSource.Installed");
                }
                finally {if(File.Exists(path))File.Delete(path);}
            });
            return;
        }
        if (availableUpdate is not { } candidate) return;
        await RunUpdateOperationAsync(async token =>
        {
            var downloadDirectory = Path.Combine(dataDirectory, "downloads"); Directory.CreateDirectory(downloadDirectory);
            var path = Path.Combine(downloadDirectory, Guid.NewGuid().ToString("N") + ".zip");
            try
            {
                using var client = EngineUpdateClientFactory?.Invoke() ?? CreateUpdateClient();
                await new GitHubEngineChannel(client).DownloadAsync(candidate, path, new Progress<string>(value => UpdateMessage = value), token);
                await InstallPackageAsync(path, candidate.Version, token);
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        });
    }
    private async Task InstallPackageAsync(string path, string? expectedVersion, CancellationToken token)
    {
        UpdateMessage = UiText.Get("T_d0b2c6fecb84");
        var manifest = await updater!.InspectAsync(path, token);
        if (expectedVersion is not null && manifest.Version != expectedVersion) throw new UpdateException("invalid_package");
        if (!dialogs.ConfirmEngineUpdate(manifest.Version, activeEngine)) { UpdateMessage = UiText.Get("T_ff2a17217e7c"); return; }
        var installed = await updater.InstallAsync(path, new Progress<string>(value => UpdateMessage = value), token);
        await updater.ActivateAsync(installed, token); await RefreshEngineSelectionAsync();
        availableUpdate = null; UpdateMessage = UiText.Get("T_633e63054e43");
    }
    private async Task RefreshEngineSelectionAsync()
    {
        var versions = await engines.ReadVerifiedVersionsAsync(); EngineVersions.Clear(); foreach (var version in versions) EngineVersions.Add(version);
        var state = await updater!.ReadSelectionAsync();
        if (state is not null)
        {
            if (versions.Contains(state.Active)) activeEngine = state.Active;
            else if (state.Previous is not null && versions.Contains(state.Previous)) { activeEngine = state.Previous; Banner = UiText.Get("T_64b8cce1d2f1"); }
            else { activeEngine = versions.FirstOrDefault() ?? ""; Banner = UiText.Get("T_aa7456af5f94"); }
            previousEngine = state.Previous ?? "";
        }
        SelectedEngineVersion = activeEngine; Changed(nameof(EngineSummary)); Changed(nameof(PreviousEngineSummary));
        await RefreshSfxAvailabilityAsync();
    }
    private async Task RunUpdateOperationAsync(Func<CancellationToken, Task> operation)
    {
        IsBusy = true; idle = new TaskCompletionSource(); cancellation = new CancellationTokenSource();
        try { await operation(cancellation.Token); }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { UpdateMessage = UiText.Get("T_596d7d8dbed2"); }
        catch (OperationCanceledException) { UpdateMessage = UiText.Get("T_c85f7e6675d5"); }
        catch (UpdateException exception) { UpdateMessage = UpdateErrorText(exception.Code); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Net.Http.HttpRequestException or JsonException or KeyNotFoundException or InvalidOperationException or ArgumentException)
        { UpdateMessage = UiText.Get("T_fc4b460196dc"); }
        finally { cancellation.Dispose(); cancellation = null; IsBusy = false; Changed(nameof(CanEditEngineRepository)); idle.TrySetResult(); }
    }
    private static string UpdateErrorText(string code) => code switch
    {
        "official_digest_unavailable" => UiText.Get("EngineSource.NoDigest"),
        "invalid_official_update" => UiText.Get("EngineSource.Invalid"),
        "version_conflict" => UiText.Get("EngineSource.VersionConflict"),
        "invalid_signature" or "invalid_manifest" or "hash_mismatch" => UiText.Get("T_cf3032ad5651"),
        "incompatible_update" => UiText.Get("T_42893dea38ad"),
        "engine_validation_failed" => UiText.Get("T_7ab82fcea02a"),
        "invalid_repository" => UiText.Get("T_f08ca2748091"),
        "no_previous_engine" => UiText.Get("T_af906e3c6e4c"),
        "invalid_selection" => UiText.Get("T_c1956c02fc37"),
        _ => UiText.Get("T_ee3467b3533f"),
    };
    private async Task LoadSettingsAsync()
    {
        var path = Path.Combine(dataDirectory, "settings.json");
        if (!File.Exists(path)) return;
        var settings = JsonSerializer.Deserialize<Settings>(await File.ReadAllTextAsync(path));
        if (settings is null || settings.Version != 1) { settingsWritable = false; Banner = UiText.Get("T_14703b3eea3e"); return; }
        OutputPath = settings.OutputPath; AutoTry = settings.AutoTry; ConflictPolicy = Math.Clamp(settings.ConflictPolicy, 0, 2);QueueConcurrency=settings.QueueConcurrency;NotifyExtractionSuccess=settings.NotifyExtractionSuccess;VaultIdleIndex=settings.VaultIdleIndex;RetainExtractionProgress=settings.RetainExtractionProgress;
        if (EngineVersions.Contains(settings.EngineVersion)) SelectedEngineVersion = settings.EngineVersion;
        if (!string.IsNullOrWhiteSpace(settings.UpdateRepository)) UpdateRepository = settings.UpdateRepository;
        EngineSourceIndex=settings.EngineSourceIndex;
        directoryRules=settings.DirectoryRules ?? new();
        if(!Enum.IsDefined(directoryRules.Location) || !Enum.IsDefined(directoryRules.Layout) || !Enum.IsDefined(directoryRules.Collision) || !Enum.IsDefined(directoryRules.Naming))
        { directoryRules=new();settingsWritable=false;Banner=UiText.Get("T_9874c8d6f32d"); }
        recentOutputPaths=settings.RecentOutputPaths?.Where(path=>!string.IsNullOrWhiteSpace(path) && Path.IsPathFullyQualified(path)).Distinct(StringComparer.OrdinalIgnoreCase).Take(5).ToList() ?? [];
        Changed(nameof(DirectoryRules));Changed(nameof(DisplayOutputPath));Changed(nameof(IsOutputPathReadOnly));Changed(nameof(ExtractionSubtitle));
    }
    private async Task SaveSettingsAsync()
    {
        if (!settingsWritable) return;
        Directory.CreateDirectory(dataDirectory);
        RememberCustomDirectory();
        await File.WriteAllTextAsync(Path.Combine(dataDirectory, "settings.json"), JsonSerializer.Serialize(new Settings(1, OutputPath, AutoTry, ConflictPolicy, activeEngine, UpdateRepository,directoryRules,recentOutputPaths,QueueConcurrency,NotifyExtractionSuccess,VaultIdleIndex,RetainExtractionProgress,EngineSourceIndex)));
    }
    private static void OpenFolder(string path)
    {
        if (Directory.Exists(path)) Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { Path.GetFullPath(path) }, UseShellExecute = true });
    }
    private static string ErrorText(string code) => code switch
    {
        "checkpoint_busy" or "checkpoint_binding_changed" or "checkpoint_source_changed" or "checkpoint_invalid" or "checkpoint_missing" or "checkpoint_sources_unavailable" or "checkpoint_unsafe_path" or "checkpoint_output_changed" or "checkpoint_incomplete" or "checkpoint_save_failed"=>UiText.Get("Checkpoint.Error."+code),
        "unsupported_checkpoint"=>UiText.Get("Checkpoint.Error.checkpoint_sources_unavailable"),
        "origin_read_failed" => UiText.Get("T_893970c571a4"), "origin_write_failed" => UiText.Get("T_0a521f1e9069"),
        "filename_encoding_not_zip" => UiText.Get("T_85d4df741e35"), "filename_encoding_unsupported" => UiText.Get("T_7ffb2c276490"),
        "missing_volume" => UiText.Get("T_a2e6438fa7e0"), "first_volume_required"=>UiText.Get("T_6d06665b464f"), "unsafe_path" => UiText.Get("T_7aeb3a3773e5"), "archive_read_failed" => UiText.Get("T_0cc98408032b"),
        "wrong_password" => UiText.Get("T_c62af9393001"), "password_or_corrupt" => UiText.Get("T_64acf7e7c429"), "corrupt_archive" => UiText.Get("T_449136abb0a6"),
        "unsupported_or_corrupt" => UiText.Get("T_21777c1adb09"), "output_write_failed" or "write_failed" => UiText.Get("T_736922310835"),
        "worker_connection_failed" or "engine_load_failed" => UiText.Get("T_ffcd45757108"), _ => UiText.Get("T_54d74bc17776"),
    };
    private sealed record Settings(int Version, string OutputPath, bool AutoTry, int ConflictPolicy, string EngineVersion, string? UpdateRepository = null,ExtractionDirectoryRules? DirectoryRules=null,IReadOnlyList<string>? RecentOutputPaths=null,int QueueConcurrency=1,bool NotifyExtractionSuccess=false,int VaultIdleIndex=2,bool RetainExtractionProgress=false,int EngineSourceIndex=0);
    public void LoadPreview()
    {
        CompressionSources.Add(new("D:\\Documents\\资料", UiText.Get("T_7c7802d8adae")));
        CompressionSources.Add(new("D:\\Documents\\说明.txt", UiText.Get("T_39932f24fe11")));
        CompressionOutput = "D:\\Documents\\资料备份.7z";
        CompressionEncrypt = true;
        initializing = false; Changed(nameof(IsIdle));CompressionCapabilitiesChanged();
        Tasks.Add(new ArchiveTaskRow("D:\\Downloads\\资料合集.7z", "128 MB"));
        Tasks.Add(new ArchiveTaskRow("D:\\Downloads\\图片备份.zip", "42 MB"));
        Tasks.Add(new ArchiveTaskRow("D:\\Downloads\\设计素材.rar", "86 MB") { Status = UiText.Get("Redesign.SampleCompleted"), Completed = true });
        OutputPath = "D:\\Extracted"; EngineVersions.Add("26.04"); EngineVersions.Add("26.03"); SelectedEngineVersion = "26.04";
        activeEngine = "26.04"; previousEngine = "26.03"; Changed(nameof(EngineSummary)); Changed(nameof(PreviousEngineSummary));
        allPasswords = [new SavedPassword(Guid.NewGuid(), "常用资源密码", "preview-only", "示例备注", true, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)];
        allPasswords.Add(new SavedPassword(Guid.NewGuid(),"项目归档","preview-project","",true,DateTimeOffset.UtcNow,DateTimeOffset.UtcNow.AddDays(-1),Group:"工作"));
        allPasswords.Add(new SavedPassword(Guid.NewGuid(),"家庭备份","preview-family","",true,DateTimeOffset.UtcNow,DateTimeOffset.UtcNow.AddDays(-3),Group:"个人"));
        allPasswords.Add(new SavedPassword(Guid.NewGuid(),"旧资料","preview-history","",false,DateTimeOffset.UtcNow));
        allPasswords.Add(new SavedPassword(Guid.NewGuid(),"素材下载","preview-assets","",true,DateTimeOffset.UtcNow,DateTimeOffset.UtcNow.AddDays(-5),Group:"下载"));
        RefreshPasswordGroups();FilterPasswords(); Changed(nameof(VaultSummary));
        Footer = UiText.Get("Preview.SyntheticNotice");
    }
}
