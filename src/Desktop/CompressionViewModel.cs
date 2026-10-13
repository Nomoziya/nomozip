using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using ArchivePassword.Core;
using ArchivePassword.Infrastructure;

namespace ArchivePassword.Desktop;

public sealed record CompressionSourceRow(string Path, string? PreviewKind = null)
{
    public string Name => System.IO.Path.GetFileName(System.IO.Path.TrimEndingDirectorySeparator(Path));
    public string Kind => PreviewKind ?? (Directory.Exists(Path) ? "文件夹" : "文件");
}
public sealed partial class MainViewModel
{
    private int compressionFormat, compressionLevel = 2;
    private bool compressionEncrypt, compressionHeaders = true, compressionIndeterminate, compressionRunning;
    private double compressionPercent;
    private string compressionOutput = "", compressionMessage = "添加文件或文件夹，选择新压缩包的位置。";
    private CompressionSourceRow? selectedCompressionSource;
    private PasswordPromptResponse? compressionPassword;
    public ObservableCollection<CompressionSourceRow> CompressionSources { get; } = [];
    public ICommand AddCompressionFilesCommand { get; private set; } = null!;
    public ICommand AddCompressionFolderCommand { get; private set; } = null!;
    public ICommand RemoveCompressionSourceCommand { get; private set; } = null!;
    public ICommand BrowseCompressionOutputCommand { get; private set; } = null!;
    public ICommand SetCompressionPasswordCommand { get; private set; } = null!;
    public ICommand CreateArchiveCommand { get; private set; } = null!;
    public ICommand OpenCreatedArchiveFolderCommand { get; private set; } = null!;
    public int CompressionFormat
    {
        get => compressionFormat;
        set
        {
            var previousExtension = Is7zCompression ? ".7z" : ".zip";
            if (Set(ref compressionFormat, value))
            {
                if (Path.GetExtension(CompressionOutput).Equals(previousExtension, StringComparison.OrdinalIgnoreCase))
                    CompressionOutput = Path.ChangeExtension(CompressionOutput, Is7zCompression ? ".7z" : ".zip");
                Changed(nameof(Is7zCompression)); Changed(nameof(CompressionHint));
            }
        }
    }
    public bool Is7zCompression => CompressionFormat == 0;
    public string CompressionHint => Is7zCompression ? "7z 支持中文密码及文件名加密。" : "ZIP 使用 AES-256 加密；当前官方引擎仅支持 ASCII 密码，中文密码请选择 7z。";
    public int CompressionLevel { get => compressionLevel; set => Set(ref compressionLevel, value); }
    public bool CompressionEncrypt { get => compressionEncrypt; set { if (Set(ref compressionEncrypt, value)) { if (!value) compressionPassword = null; Changed(nameof(CompressionPasswordStatus)); } } }
    public bool CompressionHeaders { get => compressionHeaders; set => Set(ref compressionHeaders, value); }
    public string CompressionPasswordStatus => compressionPassword is null ? "尚未设置，创建时可输入或选择已保存的密码。" : "已设置本次压缩密码，内容保持隐藏。";
    public string CompressionOutput { get => compressionOutput; set => Set(ref compressionOutput, value); }
    public string CompressionMessage { get => compressionMessage; set => Set(ref compressionMessage, value); }
    public double CompressionPercent { get => compressionPercent; private set => Set(ref compressionPercent, value); }
    public bool CompressionIndeterminate { get => compressionIndeterminate; private set => Set(ref compressionIndeterminate, value); }
    public bool CompressionRunning { get => compressionRunning; private set => Set(ref compressionRunning, value); }
    public CompressionSourceRow? SelectedCompressionSource { get => selectedCompressionSource; set => Set(ref selectedCompressionSource, value); }
    private string CompressionFormatName => Is7zCompression ? "7z" : "zip";
    private void InitializeCompressionCommands()
    {
        AddCompressionFilesCommand = new RelayCommand(() => AddCompressionSources(dialogs.SelectCompressionFiles()), () => IsIdle);
        AddCompressionFolderCommand = new RelayCommand(() => { if (dialogs.SelectCompressionFolder() is { } path) AddCompressionSources([path]); }, () => IsIdle);
        RemoveCompressionSourceCommand = new RelayCommand(() => { if (SelectedCompressionSource is { } item) CompressionSources.Remove(item); }, () => IsIdle && SelectedCompressionSource is not null);
        BrowseCompressionOutputCommand = new RelayCommand(() => { if (dialogs.SelectArchiveOutput(CompressionFormatName) is { } path) CompressionOutput = path; }, () => IsIdle);
        SetCompressionPasswordCommand = new AsyncCommand(SetCompressionPasswordAsync, () => IsIdle && CompressionEncrypt);
        CreateArchiveCommand = new AsyncCommand(CreateArchiveAsync, () => Page == 3 && IsIdle && CompressionSources.Count > 0 && !string.IsNullOrWhiteSpace(CompressionOutput) && EngineVersions.Count > 0);
        OpenCreatedArchiveFolderCommand = new RelayCommand(() => OpenFolder(Path.GetDirectoryName(Path.GetFullPath(CompressionOutput))!), () => !string.IsNullOrWhiteSpace(CompressionOutput) && File.Exists(CompressionOutput));
        CompressionSources.CollectionChanged += (_, _) => CommandManager.InvalidateRequerySuggested();
    }
    public void AddCompressionSources(IEnumerable<string> paths)
    {
        if (!IsIdle) return;
        foreach (var path in paths.Where(value => File.Exists(value) || Directory.Exists(value)).Select(Path.GetFullPath))
            if (!CompressionSources.Any(value => value.Path.Equals(path, StringComparison.OrdinalIgnoreCase))) CompressionSources.Add(new(path));
    }
    private async Task SetCompressionPasswordAsync()
    {
        compressionPassword = await dialogs.AskCompressionPasswordAsync(Path.GetFileName(CompressionOutput), allPasswords, vaultAvailable);
        Changed(nameof(CompressionPasswordStatus));
    }
    private async Task CreateArchiveAsync()
    {
        if (!Path.GetExtension(CompressionOutput).Equals("." + CompressionFormatName, StringComparison.OrdinalIgnoreCase)) { CompressionMessage = "目标扩展名与格式不一致，请重新选择保存位置。"; return; }
        if (CompressionEncrypt && compressionPassword is null) await SetCompressionPasswordAsync();
        if (CompressionEncrypt && compressionPassword is null) { CompressionMessage = "已取消设置密码，未创建压缩包。"; return; }
        var replace = File.Exists(CompressionOutput);
        if (replace && !dialogs.ConfirmArchiveReplacement(CompressionOutput)) { CompressionMessage = "已保留原压缩包。"; return; }
        IsBusy = true; CompressionRunning = true; idle = new TaskCompletionSource(); cancellation = new CancellationTokenSource(); CompressionPercent = 0; CompressionIndeterminate = true;
        try
        {
            var client = new ArchiveEngineClient(Path.Combine(baseDirectory, "ArchiveEngineWorker.exe"), engines.DllPath(activeEngine));
            var progress = new Progress<TaskUpdate>(value =>
            {
                CompressionMessage = value.Message;
                if (value.Progress is { } state) { CompressionIndeterminate = state.Total == 0; CompressionPercent = state.Total == 0 ? 0 : Math.Min(100, state.Completed * 100.0 / state.Total); }
                else CompressionIndeterminate = true;
            });
            var outcome = await new CompressionService(client, client).CreateAsync(CompressionSources.Select(value => value.Path).ToArray(), CompressionOutput,
                CompressionFormatName, new[] { 0, 1, 5, 7, 9 }[CompressionLevel], CompressionEncrypt ? compressionPassword?.Secret : null, CompressionHeaders, replace, progress, cancellation.Token);
            CompressionMessage = outcome.Engine.IsSuccess ? "已创建并校验压缩包。" : outcome.Engine.Status == "cancelled" ? "已取消，本次半成品已清理。" : CompressionError(outcome.Engine.Code);
            if (outcome.Engine.IsSuccess && CompressionEncrypt && compressionPassword is { } supplied && (supplied.Save || supplied.SavedId is not null))
            {
                try
                {
                    var savedId = supplied.SavedId ?? Guid.NewGuid();
                    if (supplied.SavedId is null) await vault.SaveAsync(new SavedPassword(savedId, string.IsNullOrWhiteSpace(supplied.Name) ? Path.GetFileName(CompressionOutput) : supplied.Name, supplied.Secret, "", true, DateTimeOffset.UtcNow));
                    await vault.RecordSuccessAsync(savedId, CompressionOutput); await RefreshVaultAsync();
                }
                catch (Exception exception) when (exception is VaultException or IOException or UnauthorizedAccessException) { Banner = "压缩包已创建，但密码未能保存，请保留你使用的密码。"; }
            }
            Footer = CompressionMessage;
        }
        finally
        {
            compressionPassword = null; Changed(nameof(CompressionPasswordStatus)); CompressionIndeterminate = false;
            cancellation.Dispose(); cancellation = null; CompressionRunning = false; IsBusy = false; idle.TrySetResult();
        }
    }
    private static string CompressionError(string code) => code switch
    {
        "zip_password_ascii_only" => "ZIP 加密仅支持 ASCII 密码；中文密码请改用 7z。",
        "duplicate_archive_path" => "选中的不同文件或文件夹具有相同名称，请分开压缩。",
        "unsupported_link" => "当前压缩不支持链接或联接点，请选择原始文件。",
        "output_exists" => "目标文件已存在，未覆盖。",
        "empty_sources" => "没有可压缩的文件，请重新选择。",
        _ => "未能创建完整压缩包。请检查源文件、保存位置和可用空间。"
    };
}
