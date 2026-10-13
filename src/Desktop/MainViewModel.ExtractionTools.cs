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
    public bool IsOutputPathReadOnly=>HasShellDestinations || directoryRules.Location!=ExtractionLocation.Custom;
    public string DisplayOutputPath
    {
        get => HasShellDestinations || directoryRules.Location==ExtractionLocation.Custom ? OutputPath
            : directoryRules.Location==ExtractionLocation.ArchiveParent && Tasks.Count==0 ? "各压缩包所在目录"
            : ExtractionDirectoryPlanner.BaseDirectory(SelectedTask?.Path ?? Tasks.FirstOrDefault()?.Path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"archive.7z"),OutputPath,directoryRules.Location);
        set { if(!IsOutputPathReadOnly) OutputPath=value; }
    }
    public string ExtractionSubtitle=>"添加压缩包，"+(directoryRules.Location switch { ExtractionLocation.ArchiveParent=>"解压到各包所在目录",ExtractionLocation.Desktop=>"解压到桌面",ExtractionLocation.Documents=>"解压到文档",_=>"解压到所选目录" })+" · "+(directoryRules.Layout switch { ExtractionLayout.ArchiveFolder=>"每包新建目录",ExtractionLayout.Smart=>"智能目录",_=>"直接放入" })+"。";
    public ICommand EditExtractionRulesCommand { get;private set; }=null!;
    public ICommand TestArchiveCommand { get;private set; }=null!;
    public ICommand ShowTestResultCommand { get;private set; }=null!;
    private void InitializeExtractionToolCommands()
    {
        EditExtractionRulesCommand=new AsyncCommand(EditExtractionRulesAsync,()=>IsIdle);
        TestArchiveCommand=new AsyncCommand(TestSelectedArchiveAsync,()=>Page==0 && IsIdle && SelectedTask is not null && activeEngine.Length>0);
        ShowTestResultCommand=new RelayCommand(()=>dialogs.ShowExtractionFailure(SelectedTask!.Name,SelectedTask.VerificationResult!,testing:true),()=>IsIdle && SelectedTask?.VerificationResult is not null);
    }
    private async Task EditExtractionRulesAsync()
    {
        IsBusy=true;idle=new TaskCompletionSource();
        try
        {
            var editor=dialogs.EditExtractionRules(directoryRules,OutputPath,recentOutputPaths);
            if(editor is null) return;
            ApplyDirectoryRules(editor.Rules,editor.CustomPath);
            await SaveSettingsAsync();Footer=settingsWritable?"已应用解压规则。":"规则已在本次使用，原设置文件保持不变。";
        }
        catch(Exception error) when(error is IOException or UnauthorizedAccessException) { Banner="规则已在本次使用，但设置未能保存。"; }
        finally {IsBusy=false;idle.TrySetResult();}
    }
    internal void ApplyDirectoryRules(ExtractionDirectoryRules rules,string custom)
    {
        ClearShellDestinations(); directoryRules=rules; OutputPath=custom;
        Changed(nameof(DirectoryRules)); Changed(nameof(IsOutputPathReadOnly)); Changed(nameof(DisplayOutputPath)); Changed(nameof(ExtractionSubtitle));
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
        var targets=SelectedTask?.ResultOutputPath is { } selected && Directory.Exists(selected) ? new[] {selected}
            : Tasks.Where(task=>task.Completed && task.ResultOutputPath is not null && Directory.Exists(task.ResultOutputPath)).Select(task=>task.ResultOutputPath!).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
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
            catch(Exception error) when(error is Win32Exception or InvalidOperationException) { Banner="无法打开结果目录，请在资源管理器中访问目标路径。"; }
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
            var client=new ArchiveEngineClient(Path.Combine(baseDirectory,"ArchiveEngineWorker.exe"),engines.DllPath(activeEngine));
            var service=new ArchiveVerificationService(client,new PasswordCandidateProvider(vault)); var prompts=0;
            var outcome=await service.VerifyAsync(task.Path,AutoTry,
                failure=>dialogs.AskPasswordAsync(task.Name,prompts++>0,allPasswords,false,cancellation.Token,previousFailure:failure,testing:true),
                new Progress<TaskUpdate>(value=>
                {
                    task.Status=value.Message; Footer=$"{task.Name}：{value.Message}";CanSkipPasswords=value.State=="password";
                    if(value.Progress is {} progress) {task.IsIndeterminate=progress.Total==0;task.Percent=progress.Total==0?0:Math.Min(100,progress.Completed*100.0/progress.Total);}
                }),cancellation.Token,skipPasswords.Token);
            task.VerificationResult=outcome.Engine;
            task.Status=outcome.Engine.IsSuccess ? (wasCompleted?"已解压 · 校验通过":"校验通过") : outcome.Engine.Status=="cancelled"?"已取消校验":"校验未通过 · "+ErrorText(outcome.Engine.Code);
            Footer=$"{task.Name}：{task.Status}。未输出文件；格式没有校验值时，仅检查数据可读取性。";
            if(outcome.Warning is not null) Banner=outcome.Warning;
        }
        catch(OperationCanceledException) {task.Status="已取消校验";task.VerificationResult=new EngineResult {Status="cancelled",Code="cancelled"};Footer="已取消校验。";}
        catch(Exception error) when(error is IOException or UnauthorizedAccessException or ArgumentException) {Banner="无法校验压缩包，请检查文件位置与访问权限。";}
        finally {task.Completed=wasCompleted;task.Running=false;CanSkipPasswords=false;cancellation.Dispose();skipPasswords.Dispose();cancellation=null;skipPasswords=null;IsBusy=false;idle.TrySetResult();}
    }
}
