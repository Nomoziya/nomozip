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
    public bool IsCompression {get;init;}
    public string OperationLabel=>UiText.Get(IsCompression?"Redesign.CompressionTask":"Contents.CompactExtract");
    private CheckpointRecord? checkpoint;
    public CheckpointRecord? Checkpoint {get=>checkpoint;set=>Set(ref checkpoint,value);}
    private string? resultOutputPath;
    private string? selectedOutputPath;
    public string? SelectedOutputPath {get=>selectedOutputPath;set {if(Set(ref selectedOutputPath,value)) Changed(nameof(DisplayOutputPath));}}
    public string? LatestOutputPath=>SelectedOutputPath ?? ResultOutputPath;
    public string DisplayOutputPath=>LatestOutputPath ?? ShellOutputPath ?? UiText.Get("Redesign.OutputPending");
    public string PrimaryActionCaption=>UiText.Get(Running?IsCompression?"Conflict.Cancel":"Redesign.PauseAll":Completed?"Redesign.OpenFolder":IsCompression?"Redesign.ShowCompression":"Contents.CompactExtract");
    private EngineResult? verificationResult;
    public string? ResultOutputPath { get=>resultOutputPath;set {if(Set(ref resultOutputPath,value)) Changed(nameof(DisplayOutputPath));} }
    public EngineResult? VerificationResult {get=>verificationResult;set=>Set(ref verificationResult,value);}
    private string? shellOutputPath;
    public string? ShellOutputPath { get => shellOutputPath; set {if(Set(ref shellOutputPath,value)) Changed(nameof(DisplayOutputPath));} }
    public string Path { get; private set; } = path;
    internal void SetArchiveResult(string value) {Path=value;Changed(nameof(Path));Changed(nameof(Name));Changed(nameof(SizeText));}
    public string Name => System.IO.Path.GetFileName(Path);
    private int filenameCodePage;
    public int FilenameCodePage { get=>filenameCodePage; set { if(!ZipFilenameEncoding.IsSupported(value)) throw new ArgumentOutOfRangeException(nameof(value)); if(Set(ref filenameCodePage,value)) Changed(nameof(SizeText)); } }
    public string SizeText => (previewSize ?? FileSize()) + (FilenameCodePage==0 ? "" : $" · CP{FilenameCodePage}");
    private string status = UiText.Get("T_1bd1893c0900");
    private double percent;
    private bool running, indeterminate;
    private bool completed;
    private EngineResult? failure;
    public bool Completed { get=>completed; set {if(Set(ref completed,value)) {Changed(nameof(PrimaryActionCaption));Changed(nameof(ProgressVisibility));Changed(nameof(DisplayPercent));Changed(nameof(PercentText));}} }
    public EngineResult? Failure { get=>failure; set=>Set(ref failure,value); }
    public string Status { get => status; set => Set(ref status, value); }
    public double Percent { get => percent; set {if(Set(ref percent, value)) {Changed(nameof(DisplayPercent));Changed(nameof(PercentText));}} }
    public double DisplayPercent => Completed ? 100 : Percent;
    public string PercentText=>IsIndeterminate || !Running && !Completed?"":$"{DisplayPercent:0}%";
    public bool IsIndeterminate { get => indeterminate; set {if(Set(ref indeterminate, value))Changed(nameof(PercentText));} }
    public bool Running { get => running; set { if (Set(ref running, value)) {Changed(nameof(ProgressVisibility));Changed(nameof(PrimaryActionCaption));} } }
    public Visibility ProgressVisibility => Running || Completed ? Visibility.Visible : Visibility.Collapsed;
    private string FileSize()
    {
        try { var bytes = new System.IO.FileInfo(Path).Length; return bytes >= 1048576 ? $"{bytes / 1048576.0:N1} MB" : $"{bytes / 1024.0:N1} KB"; }
        catch (System.IO.IOException) { return UiText.Get("T_d9851374e355"); }
    }
}
