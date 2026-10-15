using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows;
using ArchivePassword.Core;

namespace ArchivePassword.Desktop;

public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value; Changed(name); CommandManager.InvalidateRequerySuggested(); return true;
    }
    protected void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
public sealed class RelayCommand(Action execute, Func<bool>? enabled = null) : ICommand
{
    public event EventHandler? CanExecuteChanged { add => CommandManager.RequerySuggested += value; remove => CommandManager.RequerySuggested -= value; }
    public bool CanExecute(object? parameter) => enabled?.Invoke() ?? true;
    public void Execute(object? parameter) => execute();
}
public sealed class AsyncCommand(Func<Task> execute, Func<bool>? enabled = null) : ICommand
{
    private bool executing;
    public event EventHandler? CanExecuteChanged { add => CommandManager.RequerySuggested += value; remove => CommandManager.RequerySuggested -= value; }
    public bool CanExecute(object? parameter) => !executing && (enabled?.Invoke() ?? true);
    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter)) return;
        executing = true; CommandManager.InvalidateRequerySuggested();
        try { await execute(); }
        finally { executing = false; CommandManager.InvalidateRequerySuggested(); }
    }
}
public sealed class ArchiveTaskRow(string path, string? previewSize = null) : ObservableObject
{
    private string? resultOutputPath;
    public string? SelectedOutputPath {get;set;}
    public string? LatestOutputPath=>SelectedOutputPath ?? ResultOutputPath;
    private EngineResult? verificationResult;
    public string? ResultOutputPath { get=>resultOutputPath;set=>Set(ref resultOutputPath,value); }
    public EngineResult? VerificationResult {get=>verificationResult;set=>Set(ref verificationResult,value);}
    private string? shellOutputPath;
    public string? ShellOutputPath { get => shellOutputPath; set => Set(ref shellOutputPath, value); }
    public string Path { get; } = path;
    public string Name => System.IO.Path.GetFileName(Path);
    private int filenameCodePage;
    public int FilenameCodePage { get=>filenameCodePage; set { if(!ZipFilenameEncoding.IsSupported(value)) throw new ArgumentOutOfRangeException(nameof(value)); if(Set(ref filenameCodePage,value)) Changed(nameof(SizeText)); } }
    public string SizeText => (previewSize ?? FileSize()) + (FilenameCodePage==0 ? "" : $" · CP{FilenameCodePage}");
    private string status = UiText.Get("T_1bd1893c0900");
    private double percent;
    private bool running, indeterminate;
    public bool Completed { get; set; }
    public EngineResult? Failure { get; set; }
    public string Status { get => status; set => Set(ref status, value); }
    public double Percent { get => percent; set => Set(ref percent, value); }
    public bool IsIndeterminate { get => indeterminate; set => Set(ref indeterminate, value); }
    public bool Running { get => running; set { if (Set(ref running, value)) Changed(nameof(ProgressVisibility)); } }
    public Visibility ProgressVisibility => Running ? Visibility.Visible : Visibility.Collapsed;
    private string FileSize()
    {
        try { var bytes = new System.IO.FileInfo(Path).Length; return bytes >= 1048576 ? $"{bytes / 1048576.0:N1} MB" : $"{bytes / 1024.0:N1} KB"; }
        catch (System.IO.IOException) { return UiText.Get("T_d9851374e355"); }
    }
}
