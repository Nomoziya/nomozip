using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using ArchivePassword.Core;
using ArchivePassword.Infrastructure;
using Microsoft.Win32;

namespace ArchivePassword.Desktop;

public sealed class FileHashRow(string path, string expected = "") : ObservableObject
{
    private string expectedHash = expected, digest = "", status = "待计算";
    public string Path { get; } = path;
    public string Name => System.IO.Path.GetFileName(Path);
    public string ExpectedHash
    {
        get => expectedHash;
        set { if (Set(ref expectedHash, value)) { Changed(nameof(ExpectedError)); Compare(); } }
    }
    public string ExpectedError => string.IsNullOrWhiteSpace(ExpectedHash) || Sha256Manifest.IsDigest(ExpectedHash.Trim()) ? "" : "请输入 64 位十六进制 SHA-256。";
    public string Digest { get => digest; private set => Set(ref digest, value); }
    public long BytesRead { get; private set; }
    public string Status { get => status; internal set => Set(ref status, value); }
    public FileHashResult Result => new(Path, BytesRead, Digest);
    internal void Reset() { Digest = ""; BytesRead = 0; Status = "待计算"; }
    internal void Complete(FileHashResult value) { BytesRead = value.BytesRead; Digest = value.Digest; Compare(); }
    private void Compare()
    {
        if (ExpectedError.Length > 0) Status = "期望值无效";
        else if (Digest.Length == 0) Status = "待计算";
        else Status = string.IsNullOrWhiteSpace(ExpectedHash) ? "已计算（未比较）"
            : string.Equals(Digest, ExpectedHash.Trim(), StringComparison.OrdinalIgnoreCase) ? "一致" : "不一致";
    }
}

public sealed class FileHashDialogs(Window owner, Func<string[]>? selectFiles = null, Func<bool, string?>? selectManifest = null, Action<string>? copy = null)
{
    public string[] SelectFiles() => selectFiles?.Invoke() ?? (new OpenFileDialog { Multiselect = true, Title = "选择需要计算 SHA-256 的文件", Filter = "所有文件|*.*" } is var dialog && dialog.ShowDialog(owner) == true ? dialog.FileNames : []);
    public string? SelectManifest(bool exporting)
    {
        if (selectManifest is not null) return selectManifest(exporting);
        if (exporting)
        {
            var dialog = new SaveFileDialog { Title = "导出计算值清单", Filter = "SHA-256 清单|*.txt;*.sha256|所有文件|*.*", FileName = "SHA256SUMS.txt", DefaultExt = ".txt", AddExtension = true, OverwritePrompt = true };
            return dialog.ShowDialog(owner) == true ? dialog.FileName : null;
        }
        var input = new OpenFileDialog { Title = "导入 SHA-256 校验清单", Filter = "SHA-256 清单|*.txt;*.sha256|所有文件|*.*" };
        return input.ShowDialog(owner) == true ? input.FileName : null;
    }
    public void Copy(string value) { if (copy is not null) copy(value); else Clipboard.SetText(value); }
}

public sealed class FileHashViewModel(IFileHashCalculator calculator, FileHashDialogs dialogs) : ObservableObject
{
    private bool busy;
    private string message = "添加文件或导入清单，然后点击计算。", progressText = "";
    private double progress;
    private FileHashRow? selected;
    private CancellationTokenSource? cancellation;
    private TaskCompletionSource idle = Ready();
    private static TaskCompletionSource Ready() { var value = new TaskCompletionSource(); value.SetResult(); return value; }
    public ObservableCollection<FileHashRow> Rows { get; } = [];
    public FileHashRow? Selected
    {
        get => selected;
        set { if (Set(ref selected, value)) { Changed(nameof(HasSelection)); CommandManager.InvalidateRequerySuggested(); } }
    }
    public bool HasSelection => Selected is not null;
    public bool IsBusy { get => busy; private set { if (Set(ref busy, value)) Changed(nameof(IsIdle)); } }
    public bool IsIdle => !IsBusy;
    public string Message { get => message; private set => Set(ref message, value); }
    public string ProgressText { get => progressText; private set => Set(ref progressText, value); }
    public double Progress { get => progress; private set => Set(ref progress, value); }
    public ICommand AddFilesCommand => add ??= new RelayCommand(() => AddFiles(dialogs.SelectFiles()), () => IsIdle);
    public ICommand RemoveCommand => remove ??= new RelayCommand(() => { if (Selected is { } row) { Rows.Remove(row); Selected = Rows.FirstOrDefault(); } }, () => IsIdle && HasSelection);
    public ICommand CalculateCommand => calculate ??= new AsyncCommand(CalculateAsync, () => IsIdle && Rows.Count > 0 && Rows.All(value => value.ExpectedError.Length == 0));
    public ICommand CancelCommand => cancel ??= new RelayCommand(Cancel, () => IsBusy);
    public ICommand ImportCommand => import ??= new AsyncCommand(ImportAsync, () => IsIdle);
    public ICommand ExportCommand => export ??= new AsyncCommand(ExportAsync, () => IsIdle && Rows.Any(value => value.Digest.Length > 0));
    public ICommand CopyCommand => copy ??= new RelayCommand(() =>
    {
        try { dialogs.Copy(Selected!.Digest); Message = "已复制所选文件的 SHA-256。"; }
        catch (System.Runtime.InteropServices.ExternalException) { Message = "剪贴板暂时不可用，请稍后重试。"; }
    }, () => IsIdle && Selected?.Digest.Length > 0);
    private ICommand? add, remove, calculate, cancel, import, export, copy;
    public Task WaitForIdleAsync() => idle.Task;
    public void Cancel() => cancellation?.Cancel();
    public void AddFiles(IEnumerable<string> paths)
    {
        if (IsBusy) return;
        var values = paths.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).Where(path => !Rows.Any(row => string.Equals(row.Path, path, StringComparison.OrdinalIgnoreCase))).ToArray();
        if (Rows.Count + values.Length > Sha256Manifest.MaxEntries) { Message = "最多添加 10000 个文件。"; return; }
        foreach (var path in values) Add(new(path));
        Selected ??= Rows.FirstOrDefault(); ProgressText = ""; Message = $"共 {Rows.Count} 个文件，点击计算开始读取。";
    }
    private void Add(FileHashRow row)
    {
        row.PropertyChanged += (_, args) => { if (args.PropertyName is nameof(FileHashRow.ExpectedHash) or nameof(FileHashRow.Digest)) CommandManager.InvalidateRequerySuggested(); };
        Rows.Add(row);
    }
    private void Begin() { IsBusy = true; idle = new TaskCompletionSource(); cancellation = new CancellationTokenSource(); }
    private void End() { cancellation?.Dispose(); cancellation = null; IsBusy = false; idle.TrySetResult(); CommandManager.InvalidateRequerySuggested(); }
    private async Task CalculateAsync()
    {
        Begin(); Progress = 0; ProgressText = "准备读取文件…"; Message = "正在计算文件的 SHA-256…";
        foreach (var row in Rows) row.Reset();
        FileHashRow? current = null; var interrupted = false; var completed = 0;
        try
        {
            foreach (var row in Rows)
            {
                cancellation!.Token.ThrowIfCancellationRequested(); current = row; row.Status = "计算中";
                var ordinal = ++completed; var token = cancellation.Token;
                var feedback = new Progress<FileHashProgress>(value =>
                {
                    if (token.IsCancellationRequested || !IsBusy) return;
                    Progress = value.TotalBytes == 0 ? 100 : Math.Min(100, value.BytesRead * 100.0 / value.TotalBytes);
                    ProgressText = $"{ordinal}/{Rows.Count} · {row.Name} · {value.BytesRead:N0} / {value.TotalBytes:N0} 字节";
                });
                try { row.Complete(await calculator.ComputeAsync(row.Path, feedback, token)); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException) { row.Status = "无法读取"; }
                current = null;
            }
            Progress = 100;
        }
        catch (OperationCanceledException) { interrupted = true; if (current is not null) current.Status = "已取消"; }
        finally
        {
            Message = $"{(interrupted ? "已取消" : "已完成")} · {Rows.Count(value => value.Digest.Length > 0)} 项已计算 · {Rows.Count(value => value.Status == "一致")} 项一致 · {Rows.Count(value => value.Status == "不一致")} 项不一致 · {Rows.Count(value => value.Status == "无法读取")} 项无法读取";
            ProgressText = interrupted ? "未完成的文件不会生成哈希；可重新计算。" : "未填期望值的文件仅计算，不作一致判断。";
            End();
        }
    }
    private async Task ImportAsync()
    {
        var path = dialogs.SelectManifest(false); if (path is null) return;
        Begin(); Message = "正在读取校验清单…";
        try
        {
            var entries = FileHashManifest.Resolve(await FileHashManifest.ReadAsync(path, cancellation!.Token), path);
            cancellation.Token.ThrowIfCancellationRequested();
            var additions = entries.Where(value => !Rows.Any(row => string.Equals(row.Path, value.FileName, StringComparison.OrdinalIgnoreCase))).ToArray();
            if (Rows.Count + additions.Length > Sha256Manifest.MaxEntries) throw new HashManifestException("manifest_too_many_files");
            foreach (var entry in entries)
            {
                var row = Rows.FirstOrDefault(value => string.Equals(value.Path, entry.FileName, StringComparison.OrdinalIgnoreCase));
                if (row is not null) row.ExpectedHash = entry.Digest; else Add(new(entry.FileName, entry.Digest));
            }
            Selected ??= Rows.FirstOrDefault(); Message = $"已导入 {entries.Count} 项期望值；点击计算进行校验。";
        }
        catch (OperationCanceledException) { Message = "已取消导入，原列表保留。"; }
        catch (HashManifestException error) { Message = ManifestError(error); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException) { Message = "清单无法读取，或文件路径/UTF-8 编码无效；原列表保留。"; }
        finally { End(); }
    }
    private async Task ExportAsync()
    {
        var path = dialogs.SelectManifest(true); if (path is null) return;
        Begin(); Message = "正在导出实际计算值…";
        try
        {
            if (Rows.Any(value => string.Equals(value.Path, Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase))) throw new HashManifestException("manifest_is_source_file");
            var results = Rows.Where(value => value.Digest.Length > 0).Select(value => value.Result).ToArray();
            await FileHashManifest.ExportAsync(path, results, cancellation!.Token); Message = $"已导出 {results.Length} 项实际计算值；未完成的文件不包含在清单中。";
        }
        catch (OperationCanceledException) { Message = "已取消导出，原清单保留。"; }
        catch (HashManifestException error) { Message = ManifestError(error); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException) { Message = "清单未能写入，原文件保留。"; }
        finally { End(); }
    }
    private static string ManifestError(HashManifestException error) => error.Code switch
    {
        "manifest_too_large" => "清单超过 4 MB，未导入或写入。",
        "manifest_too_many_files" => "清单超过 10000 项，原列表保留。",
        "empty_manifest" => "清单中没有 SHA-256 文件记录。",
        "manifest_is_source_file" => "清单不能覆盖待校验的源文件。请选择其它保存位置。",
        "conflicting_manifest_hashes" => "清单对同一文件给出了不同哈希，原列表保留。",
        _ => $"清单{(error.Line > 0 ? $"第 {error.Line} 行" : "")}格式或路径无效。请使用 UTF-8 的 SHA256SUMS 格式。"
    };
}
