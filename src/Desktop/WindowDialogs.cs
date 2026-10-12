using System.Windows;
using ArchivePassword.Core;
using Microsoft.Win32;

namespace ArchivePassword.Desktop;

public sealed class WindowDialogs(Window owner, Action<PasswordDialog>? prepareDialog = null,
    Func<string?>? selectEngineUpdate = null, Func<string, string, bool>? confirmEngineUpdate = null,
    Action<ArchiveContentsDialog>? prepareContents = null, Action<ExtractionFailureDialog>? prepareFailure = null)
{
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
    public void ShowExtractionFailure(string name, EngineResult failure)
    {
        var dialog = new ExtractionFailureDialog(name, failure) { Owner = owner };
        prepareFailure?.Invoke(dialog);
        dialog.ShowDialog();
    }
    public Task<PasswordPromptResponse?> AskPasswordAsync(string name, bool failed, IReadOnlyList<SavedPassword> saved, bool canSave, CancellationToken token, bool browsing = false, EngineResult? previousFailure = null)
    {
        if (token.IsCancellationRequested) return Task.FromResult<PasswordPromptResponse?>(null);
        var dialog = new PasswordDialog(name, failed, saved, canSave, browsing: browsing, previousFailure: previousFailure, prepareFailure: prepareFailure) { Owner = owner };
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
