using System.Windows;
using ArchivePassword.Core;
using Microsoft.Win32;

namespace ArchivePassword.Desktop;

public sealed class WindowDialogs(Window owner, Action<PasswordDialog>? prepareDialog = null,
    Func<string?>? selectEngineUpdate = null, Func<string, string, bool>? confirmEngineUpdate = null,
    Action<ArchiveContentsDialog>? prepareContents = null, Action<ExtractionFailureDialog>? prepareFailure = null,
    Func<bool, string?>? selectVaultBackup = null, Action<VaultBackupDialog>? prepareVaultBackup = null, Action<VaultImportDialog>? prepareVaultImport = null,Action<ExtractionRulesDialog>? prepareRules=null,
    Func<string, int, bool>? confirmPasswordOrganization = null, Action<FileHashDialog>? prepareHashes = null, Action<FilenameEncodingDialog>? prepareEncoding = null,
    Action<MasterPasswordDialog>? prepareMaster = null, Func<int,bool>? confirmVaultRecovery = null,
    Func<ArchiveEditAction,string[]>? selectArchiveEditSources=null,Func<ArchiveEditAction,int,int,bool>? confirmArchiveEdit=null)
{
    public string[] SelectArchiveEditSources(ArchiveEditAction action)
    {
        if(selectArchiveEditSources is not null) return selectArchiveEditSources(action);
        if(action==ArchiveEditAction.AddFolder)
        {
            var folder=new OpenFolderDialog {Title="添加文件夹到压缩包根目录"};return folder.ShowDialog(owner)==true?[folder.FolderName]:[];
        }
        var files=new OpenFileDialog {Title=action==ArchiveEditAction.Replace?"选择替换文件（保留包内路径）":"添加文件到压缩包根目录",Multiselect=action!=ArchiveEditAction.Replace,Filter="所有文件|*.*"};
        return files.ShowDialog(owner)==true?files.FileNames:[];
    }
    public bool ConfirmArchiveEdit(ArchiveEditAction action,int removed,int added)=>confirmArchiveEdit?.Invoke(action,removed,added) ?? MessageBox.Show(owner,
        $"删除或替换 {removed} 项，写入 {added} 项（文件夹包含子项）。\n\n新包完整校验成功后才替换，并在原包旁保留修改前的 .bak 副本。副本包含被删除和替换的旧内容。\n\n新增或替换的文件沿用本次验证的密码；混合多个密码的包暂不支持修改。是否继续？",
        "确认修改压缩包",MessageBoxButton.YesNo,MessageBoxImage.Question)==MessageBoxResult.Yes;
    public MasterPasswordResponse? AskMasterPassword(MasterPasswordAction action)
    {
        var dialog=new MasterPasswordDialog(action){Owner=owner};prepareMaster?.Invoke(dialog);return dialog.ShowDialog()==true?dialog.Response:null;
    }
    public bool ConfirmVaultRecovery(int count)=>confirmVaultRecovery?.Invoke(count) ?? MessageBox.Show(owner,$"用已验证备份中的 {count} 条密码创建新库？\n\n当前锁定库将另存原始加密副本。备份之后新增的记录不会出现在恢复结果中。下一步设置新主密码。","通过独立备份恢复",MessageBoxButton.YesNo,MessageBoxImage.Question)==MessageBoxResult.Yes;
    public bool NotifyExtractionComplete(int count) => owner is MainWindow window && window.NotifyExtractionComplete(count);
    public int? EditFilenameEncoding(string archive,int codePage,Func<int,CancellationToken,Task<EngineResult>> read,CancellationToken token)
    {
        var dialog=new FilenameEncodingDialog(new(archive,codePage,read,token)) {Owner=owner};prepareEncoding?.Invoke(dialog);
        using var registration=token.Register(()=>dialog.Dispatcher.BeginInvoke(()=>{if(dialog.IsVisible) dialog.Close();}));
        return dialog.ShowDialog()==true?dialog.Model.Selected.CodePage:null;
    }
    public void ShowFileHashes(string? path, CancellationToken token = default)
    {
        var dialog = new FileHashDialog(path) { Owner = owner }; prepareHashes?.Invoke(dialog);
        using var registration = token.Register(() => dialog.Dispatcher.BeginInvoke(() => { if (dialog.IsVisible) dialog.Close(); }));
        dialog.ShowDialog();
    }
    public ExtractionRulesEditor? EditExtractionRules(ExtractionDirectoryRules rules,string custom,IReadOnlyList<string> recent)
    {
        var dialog=new ExtractionRulesDialog(rules,custom,recent) {Owner=owner};prepareRules?.Invoke(dialog);
        return dialog.ShowDialog()==true?dialog.Editor:null;
    }
    public string? SelectVaultBackup(bool exporting)
    {
        if (selectVaultBackup is not null) return selectVaultBackup(exporting);
        var filter = "nomozip 加密密码备份|*.nvault";
        if (exporting)
        {
            var dialog = new SaveFileDialog { Title = "导出加密密码备份", Filter = filter, DefaultExt = ".nvault", AddExtension = true,
                FileName = "nomozip-passwords-" + DateTime.Now.ToString("yyyyMMdd"), OverwritePrompt = true };
            return dialog.ShowDialog(owner) == true ? dialog.FileName : null;
        }
        var input = new OpenFileDialog { Title = "导入加密密码备份", Filter = filter };
        return input.ShowDialog(owner) == true ? input.FileName : null;
    }
    public string? AskBackupPassword(bool exporting, string path)
    {
        var dialog = new VaultBackupDialog(exporting, path) { Owner = owner };
        prepareVaultBackup?.Invoke(dialog);
        return dialog.ShowDialog() == true ? dialog.Passphrase : null;
    }
    public PasswordImportMode? PreviewVaultImport(IReadOnlyList<SavedPassword> existing, IReadOnlyList<SavedPassword> incoming)
    {
        var dialog = new VaultImportDialog(existing, incoming) { Owner = owner };
        prepareVaultImport?.Invoke(dialog);
        return dialog.ShowDialog() == true ? dialog.Mode : null;
    }
    public string[] SelectCompressionFiles()
    {
        var dialog = new OpenFileDialog { Multiselect = true, Title = "添加要压缩的文件", Filter = "所有文件|*.*" };
        return dialog.ShowDialog(owner) == true ? dialog.FileNames : [];
    }
    public string? SelectCompressionFolder()
    {
        var dialog = new OpenFolderDialog { Title = "添加要压缩的文件夹" };
        return dialog.ShowDialog(owner) == true ? dialog.FolderName : null;
    }
    public string? SelectArchiveOutput(string format)
    {
        var dialog = new SaveFileDialog { Title = "保存新压缩包", Filter = format == "7z" ? "7z 压缩包|*.7z" : "ZIP 压缩包|*.zip", DefaultExt = "." + format, AddExtension = true, OverwritePrompt = false };
        return dialog.ShowDialog(owner) == true ? dialog.FileName : null;
    }
    public bool ConfirmArchiveReplacement(string path) => confirmEngineUpdate?.Invoke(path, "archive") ?? (MessageBox.Show(owner,
        "目标已有压缩包：\n" + path + "\n\n新压缩包校验成功后才替换。是否继续？", "替换压缩包", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes);
    public Task<PasswordPromptResponse?> AskCompressionPasswordAsync(string name, IReadOnlyList<SavedPassword> saved, bool canSave)
    {
        var dialog = new PasswordDialog(name, false, saved, canSave, compressing: true) { Owner = owner };
        prepareDialog?.Invoke(dialog);
        return Task.FromResult(dialog.ShowDialog() == true ? dialog.Response : null);
    }
    public string[] SelectArchives()
    {
        var dialog = new OpenFileDialog { Multiselect = true, Title = "添加压缩包", Filter = "常用压缩包|*.7z;*.zip;*.rar;*.001;*.tar;*.gz;*.bz2;*.xz|所有文件|*.*" };
        return dialog.ShowDialog(owner) == true ? dialog.FileNames : [];
    }
    public string? SelectOutput(string current)
    {
        var dialog = new OpenFolderDialog { Title = "选择输出目录", InitialDirectory = System.IO.Directory.Exists(current) ? current : "" };
        return dialog.ShowDialog(owner) == true ? dialog.FolderName : null;
    }
    public void ShowArchiveContents(ArchiveContentsViewModel model)
    {
        var dialog = new ArchiveContentsDialog(model) { Owner = owner };
        prepareContents?.Invoke(dialog);
        dialog.ShowDialog();
    }
    public void ShowExtractionFailure(string name, EngineResult failure,bool testing=false)
    {
        var dialog = new ExtractionFailureDialog(name, failure,testing) { Owner = owner };
        prepareFailure?.Invoke(dialog);
        dialog.ShowDialog();
    }
    public Task<PasswordPromptResponse?> AskPasswordAsync(string name, bool failed, IReadOnlyList<SavedPassword> saved, bool canSave, CancellationToken token, bool browsing = false, EngineResult? previousFailure = null,bool testing=false)
    {
        if (token.IsCancellationRequested) return Task.FromResult<PasswordPromptResponse?>(null);
        var dialog = new PasswordDialog(name, failed, saved, canSave, browsing: browsing, previousFailure: previousFailure, prepareFailure: prepareFailure,testing:testing) { Owner = owner };
        prepareDialog?.Invoke(dialog);
        using var registration = token.Register(() => dialog.Dispatcher.BeginInvoke(() => dialog.Close()));
        var accepted = dialog.ShowDialog();
        return Task.FromResult(accepted == true ? dialog.Response : null);
    }
    public SavedPassword? EditPassword(SavedPassword? existing)
    {
        var dialog = new PasswordDialog("", false, [], true, existing, editing: true) { Owner = owner };
        prepareDialog?.Invoke(dialog);
        return dialog.ShowDialog() == true ? dialog.Edited : null;
    }
    public bool ConfirmDuplicatePassword(int count) => confirmPasswordOrganization?.Invoke("duplicate", count) ??
        MessageBox.Show(owner, $"已有 {count} 条记录保存了相同密码。仍然保存此记录？\n\n可取消后编辑已有记录，或保存后在密码库合并重复记录。", "密码已存在", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
    public bool ConfirmMergePasswords(string name, int count) => confirmPasswordOrganization?.Invoke("merge", count) ??
        MessageBox.Show(owner, $"保留“{name}”，合并其它 {count} 条相同密码记录？\n\n保留所选记录的名称、备注、分组、来源、规则和自动尝试设置，其它记录的这些信息将被移除。保留最早创建时间、最近成功时间，所有压缩包关联转到所选记录。", "合并重复密码", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
    public ExistingFilePolicy ResolveConflict(string path) => MessageBox.Show(owner, "目标目录已有同名文件：\n" + path + "\n\n选择“是”覆盖，“否”跳过，“取消”停止当前任务。", "同名文件", MessageBoxButton.YesNoCancel, MessageBoxImage.Question) switch
    { MessageBoxResult.Yes => ExistingFilePolicy.Replace, MessageBoxResult.No => ExistingFilePolicy.Skip, _ => ExistingFilePolicy.Ask };
    public bool ConfirmDelete(string name) => MessageBox.Show(owner, $"删除本机保存的密码“{name}”？", "删除密码", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
    public bool ConfirmRestore() => MessageBox.Show(owner, "使用最近一次有效备份替换当前密码库？最近的新增或修改可能需要重新保存。", "恢复备份", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
    public string? SelectEngineUpdate()
    {
        if (selectEngineUpdate is not null) return selectEngineUpdate();
        var dialog = new OpenFileDialog { Title = "导入引擎更新包", Filter = "引擎更新包|ArchivePassword-engine-*-win-x64.zip|ZIP 文件|*.zip" };
        return dialog.ShowDialog(owner) == true ? dialog.FileName : null;
    }
    public bool ConfirmEngineUpdate(string version, string current) => confirmEngineUpdate?.Invoke(version, current) ?? (MessageBox.Show(owner,
        $"安装并使用 7-Zip {version}？\n\n当前引擎：{current}。密码库保持不变；完成后可以恢复上一版本。" +
        (Version.TryParse(current, out var currentVersion) && Version.Parse(version) < currentVersion ? "\n此更新包的版本低于当前版本。" : ""),
        "更新解压引擎", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes);
}
