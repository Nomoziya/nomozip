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
    private string expectedHash = expected, digest = "", status = UiText.Get("T_2890ec1b2f04");
    public string Path { get; } = path;
    public string Name => System.IO.Path.GetFileName(Path);
    public string ExpectedHash
    {
        get => expectedHash;
        set { if (Set(ref expectedHash, value)) { Changed(nameof(ExpectedError)); Compare(); } }
    }
    public string ExpectedError => string.IsNullOrWhiteSpace(ExpectedHash) || Sha256Manifest.IsDigest(ExpectedHash.Trim()) ? "" : UiText.Get("T_f585137c61b2");
    public string Digest { get => digest; private set => Set(ref digest, value); }
    public long BytesRead { get; private set; }
    public string Status { get => status; internal set => Set(ref status, value); }
    public FileHashResult Result => new(Path, BytesRead, Digest);
    internal void Reset() { Digest = ""; BytesRead = 0; Status = UiText.Get("T_2890ec1b2f04"); }
    internal void Complete(FileHashResult value) { BytesRead = value.BytesRead; Digest = value.Digest; Compare(); }
    private void Compare()
    {
        if (ExpectedError.Length > 0) Status = UiText.Get("T_30d420c8637f");
        else if (Digest.Length == 0) Status = UiText.Get("T_2890ec1b2f04");
        else Status = string.IsNullOrWhiteSpace(ExpectedHash) ? UiText.Get("T_cb82f06947d1")
            : string.Equals(Digest, ExpectedHash.Trim(), StringComparison.OrdinalIgnoreCase) ? UiText.Get("T_0680a4a36ca8") : UiText.Get("T_a50309ed2709");
    }
}

public sealed class FileHashDialogs(Window owner, Func<string[]>? selectFiles = null, Func<bool, string?>? selectManifest = null, Action<string>? copy = null)
{
    public string[] SelectFiles() => selectFiles?.Invoke() ?? (new OpenFileDialog { Multiselect = true, Title = UiText.Get("T_2aea093e5709"), Filter = UiText.Get("T_b0e866423d58") } is var dialog && dialog.ShowDialog(owner) == true ? dialog.FileNames : []);
    public string? SelectManifest(bool exporting)
    {
        if (selectManifest is not null) return selectManifest(exporting);
        if (exporting)
        {
            var dialog = new SaveFileDialog { Title = UiText.Get("T_e29c21446e54"), Filter = UiText.Get("T_fbefab1520fc"), FileName = "SHA256SUMS.txt", DefaultExt = ".txt", AddExtension = true, OverwritePrompt = true };
            return dialog.ShowDialog(owner) == true ? dialog.FileName : null;
        }
        var input = new OpenFileDialog { Title = UiText.Get("T_4b1aa2b0c024"), Filter = UiText.Get("T_fbefab1520fc") };
        return input.ShowDialog(owner) == true ? input.FileName : null;
    }
    public void Copy(string value) { if (copy is not null) copy(value); else Clipboard.SetText(value); }
}

public sealed class FileHashViewModel(IFileHashCalculator calculator, FileHashDialogs dialogs) : ObservableObject
{
    private bool busy;
    private string message = UiText.Get("T_49012ae3beb3"), progressText = "";
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
        try { dialogs.Copy(Selected!.Digest); Message = UiText.Get("T_57f471c35128"); }
        catch (System.Runtime.InteropServices.ExternalException) { Message = UiText.Get("T_1c6e594d100e"); }
    }, () => IsIdle && Selected?.Digest.Length > 0);
    private ICommand? add, remove, calculate, cancel, import, export, copy;
    public Task WaitForIdleAsync() => idle.Task;
    public void Cancel() => cancellation?.Cancel();
    public void AddFiles(IEnumerable<string> paths)
    {
        if (IsBusy) return;
        var values = paths.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).Where(path => !Rows.Any(row => string.Equals(row.Path, path, StringComparison.OrdinalIgnoreCase))).ToArray();
        if (Rows.Count + values.Length > Sha256Manifest.MaxEntries) { Message = UiText.Get("T_554ae194934c"); return; }
        foreach (var path in values) Add(new(path));
        Selected ??= Rows.FirstOrDefault(); ProgressText = ""; Message = UiText.Format("T_cebf5ff2ca42", Rows.Count);
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
        Begin(); Progress = 0; ProgressText = UiText.Get("T_d446a6f3b2e9"); Message = UiText.Get("T_d1a3e356e435");
        foreach (var row in Rows) row.Reset();
        FileHashRow? current = null; var interrupted = false; var completed = 0;
        try
        {
            foreach (var row in Rows)
            {
                cancellation!.Token.ThrowIfCancellationRequested(); current = row; row.Status = UiText.Get("T_28a8e216754d");
                var ordinal = ++completed; var token = cancellation.Token;
                var feedback = new Progress<FileHashProgress>(value =>
                {
                    if (token.IsCancellationRequested || !IsBusy) return;
                    Progress = value.TotalBytes == 0 ? 100 : Math.Min(100, value.BytesRead * 100.0 / value.TotalBytes);
                    ProgressText = UiText.Format("T_1277dd53f347", ordinal, Rows.Count, row.Name, value.BytesRead, value.TotalBytes);
                });
                try { row.Complete(await calculator.ComputeAsync(row.Path, feedback, token)); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException) { row.Status = UiText.Get("T_c27041585556"); }
                current = null;
            }
            Progress = 100;
        }
        catch (OperationCanceledException) { interrupted = true; if (current is not null) current.Status = UiText.Get("T_a37778f17c5f"); }
        finally
        {
            Message = UiText.Format("T_98b68837365d", (interrupted ? UiText.Get("T_a37778f17c5f") : UiText.Get("T_f28461bb49c8")), Rows.Count(value => value.Digest.Length > 0), Rows.Count(value => value.Status == UiText.Get("T_0680a4a36ca8")), Rows.Count(value => value.Status == UiText.Get("T_a50309ed2709")), Rows.Count(value => value.Status == UiText.Get("T_c27041585556")));
            ProgressText = interrupted ? UiText.Get("T_ee8c9493f18c") : UiText.Get("T_0dce8699aebd");
            End();
        }
    }
    private async Task ImportAsync()
    {
        var path = dialogs.SelectManifest(false); if (path is null) return;
        Begin(); Message = UiText.Get("T_692c042a909e");
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
            Selected ??= Rows.FirstOrDefault(); Message = UiText.Format("T_96e6f5b1e07a", entries.Count);
        }
        catch (OperationCanceledException) { Message = UiText.Get("T_35e82ca44119"); }
        catch (HashManifestException error) { Message = ManifestError(error); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException) { Message = UiText.Get("T_a42a77297e7c"); }
        finally { End(); }
    }
    private async Task ExportAsync()
    {
        var path = dialogs.SelectManifest(true); if (path is null) return;
        Begin(); Message = UiText.Get("T_583afe53968f");
        try
        {
            if (Rows.Any(value => string.Equals(value.Path, Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase))) throw new HashManifestException("manifest_is_source_file");
            var results = Rows.Where(value => value.Digest.Length > 0).Select(value => value.Result).ToArray();
            await FileHashManifest.ExportAsync(path, results, cancellation!.Token); Message = UiText.Format("T_e6e9a47ae51b", results.Length);
        }
        catch (OperationCanceledException) { Message = UiText.Get("T_3eda1af52f71"); }
        catch (HashManifestException error) { Message = ManifestError(error); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException) { Message = UiText.Get("T_4828c2a4bf97"); }
        finally { End(); }
    }
    private static string ManifestError(HashManifestException error) => error.Code switch
    {
        "manifest_too_large" => UiText.Get("T_0492995d7555"),
        "manifest_too_many_files" => UiText.Get("T_2b4c6e7a2c92"),
        "empty_manifest" => UiText.Get("T_a71b8c644be1"),
        "manifest_is_source_file" => UiText.Get("T_a6c2deee185b"),
        "conflicting_manifest_hashes" => UiText.Get("T_a0bdfdcc647c"),
        _ => UiText.Format("T_7a4c72a941be", (error.Line > 0 ? UiText.Format("T_43cf63c5c302", error.Line) : ""))
    };
}
