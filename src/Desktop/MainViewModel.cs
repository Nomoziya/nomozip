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
    private string activeEngine = "", previousEngine = "", updateRepository = "Nomoziya/nomozip";
    private CancellationTokenSource? cancellation, skipPasswords;
    private TaskCompletionSource idle = CompletedSource();
    private List<SavedPassword> allPasswords = [];
    private bool busy, initializing = true, vaultAvailable = true, autoTry = true, canSkipPasswords, settingsWritable = true, narrow;
    private string outputPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Extracted");
    private string banner = "", footer = "添加压缩包以开始。", search = "", selectedEngine = "";
    private string updateMessage = "尚未检查更新。";
    private int page, conflictPolicy;
    private ArchiveTaskRow? selectedTask;
    private SavedPassword? selectedPassword;
    public ObservableCollection<ArchiveTaskRow> Tasks { get; } = [];
    public ObservableCollection<SavedPassword> PasswordRows { get; } = [];
    public ObservableCollection<string> EngineVersions { get; } = [];
    public string UpdateChannelDescription { get; private set; } = "仅安装本产品验证过的官方引擎更新包。此版本使用开发预览更新渠道。";

    public MainViewModel(string baseDirectory, string dataDirectory, IPasswordVault vault, WindowDialogs dialogs, string shellClassesPath = @"Software\Classes")
    {
        this.baseDirectory = baseDirectory; this.dataDirectory = dataDirectory; this.vault = vault; this.dialogs = dialogs;
        this.shellClassesPath = shellClassesPath;
        var keyPath = Path.Combine(baseDirectory, "update-public-key.pem");
        var publicKey = File.Exists(keyPath) ? File.ReadAllText(keyPath) : null;
        engines = new EngineCatalog(Path.Combine(baseDirectory, "engines"), Path.Combine(dataDirectory, "engines"), publicKey);
        if (publicKey is not null) updater = new EngineUpdateService(Path.Combine(dataDirectory, "engines"), Path.Combine(dataDirectory, "active-engine.json"), publicKey,
            Path.Combine(baseDirectory, "ArchiveEngineWorker.exe"), Path.Combine(baseDirectory, "update-probe/encrypted-header.7z"), engines);
        var channelPath = Path.Combine(baseDirectory, "update-channel.json");
        if (File.Exists(channelPath))
        {
            try
            {
                using var channel = JsonDocument.Parse(File.ReadAllText(channelPath)); updateRepository = channel.RootElement.GetProperty("repository").GetString() ?? "";
                if (channel.RootElement.TryGetProperty("developmentSigningKey", out var development) && development.ValueKind == JsonValueKind.False)
                    UpdateChannelDescription = "仅安装本产品验证过的官方引擎更新包。";
            }
            catch (Exception exception) when (exception is JsonException or KeyNotFoundException) { }
        }
        Tasks.CollectionChanged += (_, _) => { Changed(nameof(TaskSummary)); Changed(nameof(ExtractButtonText)); CommandManager.InvalidateRequerySuggested(); };
        AddFilesCommand = new RelayCommand(() => AddFiles(dialogs.SelectArchives()), () => IsIdle);
        OpenInputCommand = new RelayCommand(() => { if (Page == 3) AddCompressionFilesCommand.Execute(null); else AddFilesCommand.Execute(null); }, () => IsIdle);
        RemoveFileCommand = new RelayCommand(() => { if (SelectedTask is { } task) Tasks.Remove(task); }, () => IsIdle && SelectedTask is not null);
        BrowseOutputCommand = new RelayCommand(() => { if (dialogs.SelectOutput(OutputPath) is { } path) SelectCustomOutput(path); }, () => IsIdle);
        ExtractCommand = new AsyncCommand(ExtractAsync, () => Page == 0 && IsIdle && Tasks.Any(value => !value.Completed) && !string.IsNullOrWhiteSpace(selectedEngine));
        BrowseArchiveCommand = new AsyncCommand(BrowseArchiveAsync, () => Page == 0 && IsIdle && SelectedTask is not null && !string.IsNullOrWhiteSpace(activeEngine));
        ShowFailureCommand = new RelayCommand(() => dialogs.ShowExtractionFailure(SelectedTask!.Name, SelectedTask.Failure!), () => IsIdle && SelectedTask?.Failure?.Failures.Count > 0);
        CancelCommand = new RelayCommand(() => cancellation?.Cancel(), () => IsBusy);
        SkipPasswordsCommand = new RelayCommand(SkipSelectedPasswordAttempts, () => IsBusy && CanSkipPasswords);
        OpenOutputCommand = new RelayCommand(OpenSelectedOutput, () => IsIdle && (Directory.Exists(OutputPath) || Tasks.Any(task=>task.LatestOutputPath is {} path && Directory.Exists(path))));
        OpenVaultCommand = new RelayCommand(() => Page = 1);
        AddPasswordCommand = new AsyncCommand(() => EditPasswordAsync(null), () => IsIdle && vaultAvailable);
        EditPasswordCommand = new AsyncCommand(() => EditPasswordAsync(SelectedPassword), () => IsIdle && vaultAvailable && SelectedPassword is not null);
        DeletePasswordCommand = new AsyncCommand(DeletePasswordAsync, () => IsIdle && vaultAvailable && SelectedPassword is not null);
        RestoreVaultCommand = new AsyncCommand(RestoreVaultAsync, () => IsIdle);
        ExportVaultCommand = new AsyncCommand(() => TransferVaultAsync(true), () => IsIdle && vaultAvailable && allPasswords.Count > 0);
        ImportVaultCommand = new AsyncCommand(() => TransferVaultAsync(false), () => IsIdle && vaultAvailable);
        CheckUpdatesCommand = new AsyncCommand(CheckUpdatesAsync, () => IsIdle);
        ImportUpdateCommand = new AsyncCommand(ImportUpdateAsync, () => IsIdle && updater is not null);
        InstallUpdateCommand = new AsyncCommand(InstallOnlineUpdateAsync, () => IsIdle && updater is not null && availableUpdate is not null);
        ApplyEngineCommand = new AsyncCommand(() => RunUpdateOperationAsync(async token => { await updater!.ActivateAsync(SelectedEngineVersion, token); await RefreshEngineSelectionAsync(); UpdateMessage = "已切换解压引擎。"; }), () => IsIdle && updater is not null && SelectedEngineVersion != activeEngine && EngineVersions.Contains(SelectedEngineVersion));
        RollbackEngineCommand = new AsyncCommand(() => RunUpdateOperationAsync(async token => { await updater!.RollbackAsync(token); await RefreshEngineSelectionAsync(); UpdateMessage = "已恢复上一版本。"; }), () => IsIdle && updater is not null && EngineVersions.Contains(previousEngine));
        OpenLicensesCommand = new RelayCommand(() => OpenFolder(Path.Combine(baseDirectory, "licenses")));
        InitializeCompressionCommands();
        InitializeAssociationCommands();
        InitializeApplicationUpdateCommands();
        InitializeExtractionToolCommands();
        InitializeQueueCommands();
        InitializePasswordOrganizationCommands();
        InitializeFileHashCommands();
        InitializeFilenameEncodingCommands();
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
    public int Page { get => page; set { if (Set(ref page, value)) Changed(nameof(NavigationIndex)); } }
    public int NavigationIndex { get => Page switch { 3 => 1, 1 => 2, 2 => 3, _ => 0 }; set => Page = value switch { 1 => 3, 2 => 1, 3 => 2, _ => 0 }; }
    public bool IsBusy { get => busy; private set { if (Set(ref busy, value)) Changed(nameof(IsIdle)); } }
    public bool IsIdle => !IsBusy && !initializing;
    public bool IsNarrow { get => narrow; set => Set(ref narrow, value); }
    public bool CanSkipPasswords { get => canSkipPasswords; private set => Set(ref canSkipPasswords, value); }
    public string OutputPath { get => outputPath; set { if(Set(ref outputPath,value)) Changed(nameof(DisplayOutputPath)); } }
    public bool AutoTry { get => autoTry; set => Set(ref autoTry, value); }
    public int ConflictPolicy { get => conflictPolicy; set => Set(ref conflictPolicy, value); }
    public string Banner { get => banner; set => Set(ref banner, value); }
    public string Footer { get => footer; set => Set(ref footer, value); }
    public string UpdateMessage { get => updateMessage; set => Set(ref updateMessage, value); }
    public string UpdateRepository { get => updateRepository; set { if (Set(ref updateRepository, value)) { availableUpdate = null; CommandManager.InvalidateRequerySuggested(); } } }
    public string Search { get => search; set { if (Set(ref search, value)) FilterPasswords(); } }
    public string SelectedEngineVersion { get => selectedEngine; set { if (value is not null && Set(ref selectedEngine, value)) Changed(nameof(EngineSummary)); } }
    public string EngineSummary => string.IsNullOrWhiteSpace(activeEngine) ? "没有可用的已验证引擎。" : $"现用引擎：7-Zip {activeEngine}";
    public string PreviousEngineSummary => string.IsNullOrWhiteSpace(previousEngine) ? "尚无可恢复的上一版本。" : $"上一版本：7-Zip {previousEngine}";
    public string VaultSummary => vaultAvailable ? $"密码库中有 {allPasswords.Count(value => value.AutoTry)} 个可用密码" : "密码库无法读取，可手动输入密码";
    public string TaskSummary => $"{Tasks.Count(value => !value.Completed)} 个待处理 · {Tasks.Count(value => value.Completed)} 个已完成";
    public string ExtractButtonText => $"开始解压（{Tasks.Count(value => !value.Completed)}）";
    public ArchiveTaskRow? SelectedTask { get => selectedTask; set { if(Set(ref selectedTask,value)) {Changed(nameof(DisplayOutputPath));if(queueAttempts.Count>0) RefreshQueuePasswordAction();} } }
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
                catch (UpdateException) { Banner = "引擎选择记录无法使用，已保留原记录。可先使用随包引擎。"; }
            }
            Changed(nameof(EngineSummary));
            await RefreshVaultAsync();
            if (EngineVersions.Count == 0) Banner = "未找到通过校验的解压引擎，请重新取得完整产品包。";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        { Banner = "设置或引擎文件无法读取，请检查产品包和文件权限。"; }
        finally { initializing = false; Changed(nameof(IsIdle)); CommandManager.InvalidateRequerySuggested(); }
    }
    public void AddFiles(IEnumerable<string> paths)
    {
        if (IsBusy) return;
        foreach (var path in paths.Where(File.Exists))
            if (!Tasks.Any(value => string.Equals(value.Path, Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase)))
                Tasks.Add(new ArchiveTaskRow(Path.GetFullPath(path)) { ShellOutputPath = shellAction is { } action ? new ShellSelection(action, []).OutputFor(Path.GetFullPath(path)) : null });
        Footer = "选择输出目录后开始解压。"; Changed(nameof(DisplayOutputPath));
    }
    private async Task RefreshVaultAsync()
    {
        try { allPasswords = (await vault.ReadAsync()).ToList(); vaultAvailable = true; }
        catch (VaultException exception) { allPasswords = []; vaultAvailable = false; Banner = exception.BackupAvailable ? "密码库无法读取。你可以在设置中恢复备份，或先手动输入解压密码。" : "密码库无法读取，请保留原文件。你仍可手动输入解压密码。"; }
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
    private async Task ExtractTasksAsync(IReadOnlyList<ArchiveTaskRow> batch)
    {
        if (directoryRules.Location==ExtractionLocation.Custom && string.IsNullOrWhiteSpace(OutputPath)) { Banner = "请选择输出目录。"; return; }
        IsBusy=true;idle=new TaskCompletionSource();Banner="";cancellation=new CancellationTokenSource();
        using var prompts=new SemaphoreSlim(1,1);
        var openResults=new List<string>();
        string Destination(ArchiveTaskRow task)=>task.ShellOutputPath ?? ExtractionDirectoryPlanner.BaseDirectory(task.Path,OutputPath,directoryRules.Location);
        async Task<T> PromptAsync<T>(Func<Task<T>> show,CancellationToken token)
        {
            await prompts.WaitAsync(token);
            try {token.ThrowIfCancellationRequested();return await show();}
            finally {prompts.Release();}
        }
        async Task RunTaskAsync(ArchiveTaskRow task,CancellationToken token)
        {
            using var skip=new CancellationTokenSource();queueAttempts.Add(task,(skip,false));
            task.Running=true;task.IsIndeterminate=true;task.Failure=null;
            if(queueAttempts.Count==1) SelectedTask=task;
            var attempts=0;
            var progress=new Progress<TaskUpdate>(value=>
            {
                if(!task.Running) return;
                task.Status=value.Message;Footer=$"{task.Name}：{value.Message}";
                queueAttempts[task]=(skip,value.State=="password");RefreshQueuePasswordAction();
                if(value.Progress is {} details) {task.IsIndeterminate=details.Total==0;task.Percent=details.Total==0?0:Math.Min(100,details.Completed*100.0/details.Total);}
            });
            try
            {
                var destination=Destination(task);
                Func<IReadOnlyList<ArchiveEntry>,string>? resolveDestination=task.ShellOutputPath is null && directoryRules.Layout!=ExtractionLayout.Direct
                    ? items=>ExtractionDirectoryPlanner.Resolve(task.Path,destination,directoryRules,items,DateOnly.FromDateTime(DateTime.Today)) : null;
                var service=new ExtractionService(CreateReadClient(task),vault,new PasswordCandidateProvider(vault),new WindowsExtractionOrigin());
                var outcome=await service.ExtractAsync(task.Path,destination,AutoTry,
                    failure=>PromptAsync(()=>dialogs.AskPasswordAsync(task.Name,attempts++>0,allPasswords,vaultAvailable,token,previousFailure:failure),token),
                    path=>ConflictPolicy==1?Task.FromResult(ExistingFilePolicy.Skip):ConflictPolicy==2?Task.FromResult(ExistingFilePolicy.Replace)
                        :PromptAsync(()=>Task.FromResult(dialogs.ResolveConflict(path)),token),
                    progress,token,skip.Token,resolveDestination);
                task.Completed=outcome.Engine.IsSuccess;
                task.ResultOutputPath=task.Completed?outcome.OutputPath:null;task.SelectedOutputPath=null;
                if(task.Completed) openResults.Add(outcome.OutputPath);
                task.Status=task.Completed?"已解压":outcome.Engine.Status=="cancelled"?"已取消":ErrorText(outcome.Engine.Code);
                task.Failure=task.Completed?null:outcome.Engine;
                if(task.Failure?.Failures.Count>0) task.Status+=$" · {task.Failure.Failures.Count} 项失败";
                if(outcome.Warning is not null) Banner+=(Banner.Length>0?"\n":"")+(batch.Count>1?task.Name+"：":"")+outcome.Warning;
                Footer=$"{task.Name}：{task.Status}";
                if(outcome.Engine.Status=="cancelled") cancellation.Cancel();
            }
            catch(OperationCanceledException) {task.Status="已取消";Footer=$"{task.Name}：已取消";}
            catch(Exception error) when(error is IOException or UnauthorizedAccessException or ArgumentException)
            {task.Failure=new EngineResult {Code="output_write_failed"};task.Status="处理失败，请检查文件与输出目录";Banner+=(Banner.Length>0?"\n":"")+task.Name+"：无法处理文件或写入输出，请检查位置与访问权限。";}
            finally {task.Running=false;queueAttempts.Remove(task);RefreshQueuePasswordAction();Changed(nameof(TaskSummary));Changed(nameof(ExtractButtonText));}
        }
        try
        {
            await SaveSettingsAsync();
            await ExtractionQueue.RunAsync(batch,QueueConcurrency,task=>new(task.Path,Destination(task)),RunTaskAsync,cancellation.Token);
            await RefreshVaultAsync();
        }
        catch(Exception error) when(error is IOException or UnauthorizedAccessException or ArgumentException)
        {Banner="无法处理文件或写入设置，请检查输出目录与文件权限。";}
        finally
        {
            var shouldOpen=directoryRules.OpenAfterSuccess && !cancellation.IsCancellationRequested && batch.Count>0 && batch.All(task=>task.Completed);
            CanSkipPasswords=false;cancellation.Dispose();cancellation=null;IsBusy=false;
            try {if(shouldOpen) OpenResultDirectories(openResults.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());}
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
        catch (Exception exception) when (exception is VaultException or IOException or UnauthorizedAccessException) { Banner = "密码未能保存，原密码库已保留。"; }
        finally { IsBusy = false; idle.TrySetResult(); CommandManager.InvalidateRequerySuggested(); }
    }
    private async Task DeletePasswordAsync()
    {
        if (SelectedPassword is not { } item || !dialogs.ConfirmDelete(item.Name)) return;
        try { await vault.DeleteAsync(item.Id); await RefreshVaultAsync(); }
        catch (Exception exception) when (exception is VaultException or IOException or UnauthorizedAccessException) { Banner = "密码未能删除，请检查密码库。"; }
    }
    private async Task RestoreVaultAsync()
    {
        if (!dialogs.ConfirmRestore()) return;
        try { await vault.RestoreBackupAsync(); await RefreshVaultAsync(); Banner = "已恢复密码库备份。"; }
        catch (Exception exception) when (exception is VaultException or IOException or UnauthorizedAccessException) { Banner = "备份不存在或无法读取，原密码库已保留。"; }
    }
    private async Task CheckUpdatesAsync() => await RunUpdateOperationAsync(async token =>
    {
        UpdateMessage = "正在检查更新…"; availableUpdate = null;
        UpdateRepository = GitHubEngineChannel.NormalizeRepository(UpdateRepository); await SaveSettingsAsync();
        using var client = CreateUpdateClient();
        var officialMessage = "官方版本暂时无法查询。";
        try
        {
            using var release = JsonDocument.Parse(await client.GetStringAsync("https://api.github.com/repos/ip7z/7zip/releases/latest", token));
            var latest = release.RootElement.GetProperty("tag_name").GetString();
            officialMessage = $"官方最新稳定版本：{latest}。";
        }
        catch (Exception exception) when (exception is System.Net.Http.HttpRequestException or JsonException or KeyNotFoundException) { }
        catch (TaskCanceledException) when (!token.IsCancellationRequested) { }
        var found = await new GitHubEngineChannel(client).CheckAsync(UpdateRepository, token);
        if (found is not null && (!Version.TryParse(activeEngine, out var currentVersion) || Version.Parse(found.Version) > currentVersion)) availableUpdate = found;
        UpdateMessage = officialMessage + (availableUpdate is null ? "产品渠道尚无较新的引擎更新包。" : $"产品渠道提供 {availableUpdate.Version}，可下载更新。");
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
        if (availableUpdate is not { } candidate) return;
        await RunUpdateOperationAsync(async token =>
        {
            var downloadDirectory = Path.Combine(dataDirectory, "downloads"); Directory.CreateDirectory(downloadDirectory);
            var path = Path.Combine(downloadDirectory, Guid.NewGuid().ToString("N") + ".zip");
            try
            {
                using var client = CreateUpdateClient();
                await new GitHubEngineChannel(client).DownloadAsync(candidate, path, new Progress<string>(value => UpdateMessage = value), token);
                await InstallPackageAsync(path, candidate.Version, token);
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        });
    }
    private async Task InstallPackageAsync(string path, string? expectedVersion, CancellationToken token)
    {
        UpdateMessage = "正在校验更新包…";
        var manifest = await updater!.InspectAsync(path, token);
        if (expectedVersion is not null && manifest.Version != expectedVersion) throw new UpdateException("invalid_package");
        if (!dialogs.ConfirmEngineUpdate(manifest.Version, activeEngine)) { UpdateMessage = "已取消更新，继续使用当前引擎。"; return; }
        var installed = await updater.InstallAsync(path, new Progress<string>(value => UpdateMessage = value), token);
        await updater.ActivateAsync(installed, token); await RefreshEngineSelectionAsync();
        availableUpdate = null; UpdateMessage = "更新完成，可继续解压。需要时可恢复上一版本。";
    }
    private async Task RefreshEngineSelectionAsync()
    {
        var versions = await engines.ReadVerifiedVersionsAsync(); EngineVersions.Clear(); foreach (var version in versions) EngineVersions.Add(version);
        var state = await updater!.ReadSelectionAsync();
        if (state is not null)
        {
            if (versions.Contains(state.Active)) activeEngine = state.Active;
            else if (state.Previous is not null && versions.Contains(state.Previous)) { activeEngine = state.Previous; Banner = "当前引擎未通过校验，已使用上一可用版本。请在设置中确认引擎。"; }
            else { activeEngine = versions.FirstOrDefault() ?? ""; Banner = "所选引擎无法使用，已使用随包可用引擎。"; }
            previousEngine = state.Previous ?? "";
        }
        SelectedEngineVersion = activeEngine; Changed(nameof(EngineSummary)); Changed(nameof(PreviousEngineSummary));
    }
    private async Task RunUpdateOperationAsync(Func<CancellationToken, Task> operation)
    {
        IsBusy = true; idle = new TaskCompletionSource(); cancellation = new CancellationTokenSource();
        try { await operation(cancellation.Token); }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { UpdateMessage = "已取消更新，当前引擎保持可用。"; }
        catch (OperationCanceledException) { UpdateMessage = "更新请求超时，请稍后重试。当前引擎保持可用。"; }
        catch (UpdateException exception) { UpdateMessage = UpdateErrorText(exception.Code); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Net.Http.HttpRequestException or JsonException or KeyNotFoundException or InvalidOperationException or ArgumentException)
        { UpdateMessage = "更新未完成，请检查网络、更新包与文件权限。当前引擎保持可用。"; }
        finally { cancellation.Dispose(); cancellation = null; IsBusy = false; idle.TrySetResult(); }
    }
    private static string UpdateErrorText(string code) => code switch
    {
        "invalid_signature" or "invalid_manifest" or "hash_mismatch" => "更新包无法通过真实性校验，未切换引擎。请重新取得产品发布的更新包。",
        "incompatible_update" => "此更新包与当前软件不兼容，请先更新软件。",
        "engine_validation_failed" => "新引擎无法通过解压验证，仍使用当前引擎。",
        "invalid_repository" => "请输入 GitHub 仓库地址，或用户名/仓库名。",
        "no_previous_engine" => "没有可恢复的上一版本。",
        "invalid_selection" => "引擎选择记录无法读取，原文件已保留。",
        _ => "更新包无法使用，未切换引擎。",
    };
    private async Task LoadSettingsAsync()
    {
        var path = Path.Combine(dataDirectory, "settings.json");
        if (!File.Exists(path)) return;
        var settings = JsonSerializer.Deserialize<Settings>(await File.ReadAllTextAsync(path));
        if (settings is null || settings.Version != 1) { settingsWritable = false; Banner = "设置文件版本无法识别，已使用默认设置并保留原文件。"; return; }
        OutputPath = settings.OutputPath; AutoTry = settings.AutoTry; ConflictPolicy = Math.Clamp(settings.ConflictPolicy, 0, 2);QueueConcurrency=settings.QueueConcurrency;
        if (EngineVersions.Contains(settings.EngineVersion)) SelectedEngineVersion = settings.EngineVersion;
        if (!string.IsNullOrWhiteSpace(settings.UpdateRepository)) UpdateRepository = settings.UpdateRepository;
        directoryRules=settings.DirectoryRules ?? new();
        if(!Enum.IsDefined(directoryRules.Location) || !Enum.IsDefined(directoryRules.Layout) || !Enum.IsDefined(directoryRules.Collision) || !Enum.IsDefined(directoryRules.Naming))
        { directoryRules=new();settingsWritable=false;Banner="解压规则暂不可识别，已使用默认规则并保留设置文件。"; }
        recentOutputPaths=settings.RecentOutputPaths?.Where(path=>!string.IsNullOrWhiteSpace(path) && Path.IsPathFullyQualified(path)).Distinct(StringComparer.OrdinalIgnoreCase).Take(5).ToList() ?? [];
        Changed(nameof(DirectoryRules));Changed(nameof(DisplayOutputPath));Changed(nameof(IsOutputPathReadOnly));Changed(nameof(ExtractionSubtitle));
    }
    private async Task SaveSettingsAsync()
    {
        if (!settingsWritable) return;
        Directory.CreateDirectory(dataDirectory);
        RememberCustomDirectory();
        await File.WriteAllTextAsync(Path.Combine(dataDirectory, "settings.json"), JsonSerializer.Serialize(new Settings(1, OutputPath, AutoTry, ConflictPolicy, activeEngine, UpdateRepository,directoryRules,recentOutputPaths,QueueConcurrency)));
    }
    private static void OpenFolder(string path)
    {
        if (Directory.Exists(path)) Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { Path.GetFullPath(path) }, UseShellExecute = true });
    }
    private static string ErrorText(string code) => code switch
    {
        "origin_read_failed" => "无法读取来源信息", "origin_write_failed" => "来源信息无法保留，未交付文件",
        "filename_encoding_not_zip" => "所选文件不是 ZIP", "filename_encoding_unsupported" => "引擎不支持文件名编码",
        "missing_volume" => "缺少分卷文件", "unsafe_path" => "压缩包包含不安全路径", "archive_read_failed" => "无法读取压缩包",
        "wrong_password" => "密码不匹配", "password_or_corrupt" => "密码不匹配或文件损坏", "corrupt_archive" => "压缩包已损坏",
        "unsupported_or_corrupt" => "格式不支持或文件损坏", "output_write_failed" or "write_failed" => "输出目录无法写入",
        "worker_connection_failed" or "engine_load_failed" => "引擎无法启动", _ => "处理失败，请检查压缩包",
    };
    private sealed record Settings(int Version, string OutputPath, bool AutoTry, int ConflictPolicy, string EngineVersion, string? UpdateRepository = null,ExtractionDirectoryRules? DirectoryRules=null,IReadOnlyList<string>? RecentOutputPaths=null,int QueueConcurrency=1);
    public void LoadPreview()
    {
        CompressionSources.Add(new("D:\\Documents\\资料", "文件夹"));
        CompressionSources.Add(new("D:\\Documents\\说明.txt", "文件"));
        CompressionOutput = "D:\\Documents\\资料备份.7z";
        CompressionEncrypt = true;
        initializing = false; Changed(nameof(IsIdle));
        Tasks.Add(new ArchiveTaskRow("D:\\Downloads\\资料合集.7z", "128 MB"));
        Tasks.Add(new ArchiveTaskRow("D:\\Downloads\\图片备份.zip", "42 MB"));
        Tasks.Add(new ArchiveTaskRow("D:\\Downloads\\设计素材.rar", "86 MB") { Status = "已解压", Completed = true });
        OutputPath = "D:\\Extracted"; EngineVersions.Add("26.04"); EngineVersions.Add("26.03"); SelectedEngineVersion = "26.04";
        activeEngine = "26.04"; previousEngine = "26.03"; Changed(nameof(EngineSummary)); Changed(nameof(PreviousEngineSummary));
        allPasswords = [new SavedPassword(Guid.NewGuid(), "常用资源密码", "preview-only", "示例备注", true, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)];
        FilterPasswords(); Changed(nameof(VaultSummary));
        Footer = "界面渲染预览，使用虚构数据。";
    }
}
