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
    private bool compressionSfx,sfxAvailable;
    public bool CompressionSfx
    {
        get=>compressionSfx;
        set {if(value && (!Is7zCompression || volumeOption!=0 || !sfxAvailable)) return;var previous=CompressionExtension;
            if(Set(ref compressionSfx,value)) {AlignCompressionExtension(previous);Changed(nameof(CompressionSfxHint));}}
    }
    private CompressionFormatInfo CompressionOptions => CompressionFormats.All[CompressionFormat];
    private string CompressionExtension=>CompressionSfx?".exe":CompressionOptions.Extension;
    public bool CanEncryptCompression => IsIdle && CompressionOptions.Encryption;
    public bool CanSetCompressionLevel => IsIdle && CompressionOptions.CompressionLevel;
    public bool CanSplitCompression => IsIdle && CompressionOptions.Encryption;
    public string CompressionSourceError => CompressionOptions.SingleFile && CompressionSources.Count > 0
        && (CompressionSources.Count != 1 || !File.Exists(CompressionSources[0].Path)) ? "仅支持一个文件。文件夹或多个文件请先创建 TAR。" : "";
    private void CompressionCapabilitiesChanged()
    {
        Changed(nameof(CanEncryptCompression));Changed(nameof(CanSetCompressionLevel));Changed(nameof(CanSplitCompression));
        Changed(nameof(CompressionSourceError));CommandManager.InvalidateRequerySuggested();
    }
    public bool CanUseSfx=>IsIdle && Is7zCompression && volumeOption==0 && sfxAvailable;
    public string CompressionSfxHint=>!Is7zCompression?(CompressionFormat==1?"自解压仅支持 7z。":""):volumeOption!=0?"自解压不能与分卷同时使用。":!sfxAvailable?"当前引擎没有匹配的自解压模块；可选择随包引擎。":CompressionSfx?"生成 Windows EXE，接收者无需安装 7-Zip 或 .NET；只解压，不自动运行包内文件。":"";
    private void AlignCompressionExtension(string previous) {if(Path.GetExtension(CompressionOutput).Equals(previous,StringComparison.OrdinalIgnoreCase)) CompressionOutput=Path.ChangeExtension(CompressionOutput,CompressionExtension);CommandManager.InvalidateRequerySuggested();}
    private async Task RefreshSfxAvailabilityAsync()
    {
        try {sfxAvailable=activeEngine.Length>0 && (await engines.ReadSfxModuleAsync(activeEngine)).IsValid();}
        catch(Exception error) when(error is UpdateException or IOException or UnauthorizedAccessException or System.Text.Json.JsonException) {sfxAvailable=false;}
        if(!sfxAvailable) CompressionSfx=false;Changed(nameof(CanUseSfx));Changed(nameof(CompressionSfxHint));
    }
    private int volumeOption,volumeUnit;private string volumeCustom="100",createdCompressionPath="";
    public int CompressionVolumeOption {get=>volumeOption;set {if(value!=0 && !CompressionOptions.Encryption) return;if(Set(ref volumeOption,value)) VolumeOptionsChanged();}}
    public int CompressionVolumeUnit {get=>volumeUnit;set {if(Set(ref volumeUnit,value)) VolumeOptionsChanged();}}
    public string CompressionVolumeCustom {get=>volumeCustom;set {if(Set(ref volumeCustom,value)) VolumeOptionsChanged();}}
    private bool ValidVolumeSize=>VolumeSizeOptions.TryGetBytes(volumeOption,volumeCustom,volumeUnit,out _);
    private void VolumeOptionsChanged() {if(volumeOption!=0) CompressionSfx=false;Changed(nameof(CompressionVolumeError));Changed(nameof(CompressionVolumeHint));Changed(nameof(CanUseSfx));Changed(nameof(CompressionSfxHint));CommandManager.InvalidateRequerySuggested();}
    public string CompressionVolumeError=>ValidVolumeSize?"":"请输入大于 0 的整数，分卷上限为 1 TiB。";
    public string CompressionVolumeHint=>volumeOption==0?"":ValidVolumeSize?"生成 .001 起的连续分卷；全部放在同一目录，选择首卷解压。已有分卷不覆盖。":"";
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
            if (value < 0 || value >= CompressionFormats.All.Count) return;
            var previousExtension = CompressionExtension;
            if (Set(ref compressionFormat, value))
            {
                if(!Is7zCompression && compressionSfx) {compressionSfx=false;Changed(nameof(CompressionSfx));}
                if (!CompressionOptions.Encryption) {CompressionEncrypt=false;CompressionVolumeOption=0;}
                AlignCompressionExtension(previousExtension);Changed(nameof(CanUseSfx));Changed(nameof(CompressionSfxHint));
                Changed(nameof(Is7zCompression)); Changed(nameof(CompressionHint));
                CompressionCapabilitiesChanged();
            }
        }
    }
    public bool Is7zCompression => CompressionFormat == 0;
    public string CompressionHint => CompressionFormatName switch
    {
        "7z" => "7z 支持中文密码及文件名加密。",
        "zip" => "ZIP 使用 AES-256 加密；当前官方引擎仅支持 ASCII 密码，中文密码请选择 7z。",
        "tar" => "TAR 仅打包，不压缩；不支持加密、分卷或 EXE。",
        _ => $"单文件压缩，不支持加密、分卷或 EXE。建议保存为“原文件名{CompressionOptions.Extension}”；解压名称来自包名。"
    };
    public int CompressionLevel { get => compressionLevel; set => Set(ref compressionLevel, value); }
    public bool CompressionEncrypt { get => compressionEncrypt; set { if (value && !CompressionOptions.Encryption) return; if (Set(ref compressionEncrypt, value)) { if (!value) compressionPassword = null; Changed(nameof(CompressionPasswordStatus)); } } }
    public bool CompressionHeaders { get => compressionHeaders; set => Set(ref compressionHeaders, value); }
    public string CompressionPasswordStatus => compressionPassword is null ? "尚未设置，创建时可输入或选择已保存的密码。" : "已设置本次压缩密码，内容保持隐藏。";
    public string CompressionOutput { get => compressionOutput; set {if(Set(ref compressionOutput,value)) createdCompressionPath="";} }
    public string CompressionMessage { get => compressionMessage; set => Set(ref compressionMessage, value); }
    public double CompressionPercent { get => compressionPercent; private set => Set(ref compressionPercent, value); }
    public bool CompressionIndeterminate { get => compressionIndeterminate; private set => Set(ref compressionIndeterminate, value); }
    public bool CompressionRunning { get => compressionRunning; private set => Set(ref compressionRunning, value); }
    public CompressionSourceRow? SelectedCompressionSource { get => selectedCompressionSource; set => Set(ref selectedCompressionSource, value); }
    private string CompressionFormatName => CompressionOptions.Name;
    private void InitializeCompressionCommands()
    {
        AddCompressionFilesCommand = new RelayCommand(() => AddCompressionSources(dialogs.SelectCompressionFiles()), () => IsIdle);
        AddCompressionFolderCommand = new RelayCommand(() => { if (dialogs.SelectCompressionFolder() is { } path) AddCompressionSources([path]); }, () => IsIdle);
        RemoveCompressionSourceCommand = new RelayCommand(() => { if (SelectedCompressionSource is { } item) CompressionSources.Remove(item); }, () => IsIdle && SelectedCompressionSource is not null);
        BrowseCompressionOutputCommand = new RelayCommand(() => { if (dialogs.SelectArchiveOutput(CompressionSfx?"sfx":CompressionFormatName) is { } path) CompressionOutput = path; }, () => IsIdle);
        SetCompressionPasswordCommand = new AsyncCommand(SetCompressionPasswordAsync, () => IsIdle && CompressionEncrypt);
        CreateArchiveCommand = new AsyncCommand(CreateArchiveAsync, () => Page == 3 && IsIdle && CompressionSources.Count > 0 && !string.IsNullOrWhiteSpace(CompressionOutput) && EngineVersions.Count > 0 && ValidVolumeSize && CompressionSourceError.Length == 0);
        OpenCreatedArchiveFolderCommand = new RelayCommand(() => OpenFolder(Path.GetDirectoryName(Path.GetFullPath(createdCompressionPath.Length>0?createdCompressionPath:CompressionOutput))!), () => File.Exists(createdCompressionPath.Length>0?createdCompressionPath:CompressionOutput));
        CompressionSources.CollectionChanged += (_, _) => CompressionCapabilitiesChanged();
    }
    public void AddCompressionSources(IEnumerable<string> paths)
    {
        if (!IsIdle) return;
        foreach (var path in paths.Where(value => File.Exists(value) || Directory.Exists(value)).Select(Path.GetFullPath))
            if (!CompressionSources.Any(value => value.Path.Equals(path, StringComparison.OrdinalIgnoreCase))) CompressionSources.Add(new(path));
    }
    private async Task SetCompressionPasswordAsync()
    {
        var ownsBusy=IsIdle;if(ownsBusy){IsBusy=true;idle=new TaskCompletionSource();}
        try { compressionPassword = await dialogs.AskCompressionPasswordAsync(Path.GetFileName(CompressionOutput), allPasswords, vaultAvailable); Changed(nameof(CompressionPasswordStatus)); }
        finally {if(ownsBusy){IsBusy=false;idle.TrySetResult();}}
    }
    private async Task CreateArchiveAsync()
    {
        if (CompressionSourceError.Length > 0) {CompressionMessage=CompressionSourceError;return;}
        if(!VolumeSizeOptions.TryGetBytes(volumeOption,volumeCustom,volumeUnit,out var volumeSize)) {CompressionMessage=CompressionVolumeError;return;}
        if (!Path.GetExtension(CompressionOutput).Equals(CompressionExtension, StringComparison.OrdinalIgnoreCase)) { CompressionMessage = "目标扩展名与格式不一致，请重新选择保存位置。"; return; }
        if(volumeSize>0 && VolumeCompressionService.HasExistingTarget(CompressionOutput)) {CompressionMessage="已有同名文件或分卷，已保留。请使用新名称或新目录。";return;}
        IsBusy=true;idle=new TaskCompletionSource();cancellation=new CancellationTokenSource();
        try
        {
        SfxModule? module=null;
        if(CompressionSfx)
        {
            if(volumeSize>0 || !Is7zCompression) {CompressionMessage="自解压仅支持不分卷的 7z。";return;}
            try {module=await engines.ReadSfxModuleAsync(activeEngine,cancellation.Token);}
            catch(Exception error) when(error is UpdateException or IOException or UnauthorizedAccessException or System.Text.Json.JsonException) {CompressionMessage="自解压模块无法通过校验；请重新取得产品包或切换引擎。";return;}
        }
        var replace = volumeSize==0 && File.Exists(CompressionOutput);
        if (replace && !dialogs.ConfirmArchiveReplacement(CompressionOutput)) { CompressionMessage = "已保留原压缩包。"; return; }
        if (CompressionEncrypt && compressionPassword is null) await SetCompressionPasswordAsync();
        if (CompressionEncrypt && compressionPassword is null) { CompressionMessage = "已取消设置密码，未创建压缩包。"; return; }
        createdCompressionPath="";CompressionRunning = true;CompressionPercent = 0; CompressionIndeterminate = true;
            var client = new ArchiveEngineClient(Path.Combine(baseDirectory, "ArchiveEngineWorker.exe"), engines.DllPath(activeEngine));
            var progress = new Progress<TaskUpdate>(value =>
            {
                CompressionMessage = value.Message;
                if (value.Progress is { } state) { CompressionIndeterminate = state.Total == 0; CompressionPercent = state.Total == 0 ? 0 : Math.Min(100, state.Completed * 100.0 / state.Total); }
                else CompressionIndeterminate = true;
            });
            var outcome = module is not null ? await new SfxCompressionService(client,client).CreateAsync(CompressionSources.Select(value=>value.Path).ToArray(),CompressionOutput,module,
                new[]{0,1,5,7,9}[CompressionLevel],CompressionEncrypt?compressionPassword?.Secret:null,CompressionHeaders,replace,progress,cancellation.Token)
                : await new CompressionService(client, client).CreateAsync(CompressionSources.Select(value => value.Path).ToArray(), CompressionOutput,
                CompressionFormatName, new[] { 0, 1, 5, 7, 9 }[CompressionLevel], CompressionEncrypt ? compressionPassword?.Secret : null, CompressionHeaders, replace, progress, cancellation.Token,volumeSize);
            if(outcome.Engine.IsSuccess) createdCompressionPath=outcome.OutputPath;
            CompressionMessage = outcome.Engine.IsSuccess ? module is not null?"已创建并校验自解压包。":outcome.Volumes is { } volumes ? $"已创建并校验 {volumes.Count} 个分卷。首卷：{outcome.OutputPath}" : "已创建并校验压缩包。" : outcome.Engine.Status == "cancelled" ? "已取消，本次半成品已清理。" : CompressionError(outcome.Engine.Code);
            if (outcome.Engine.IsSuccess && CompressionEncrypt && compressionPassword is { } supplied && (supplied.Save || supplied.SavedId is not null))
            {
                try
                {
                    var savedId = supplied.SavedId ?? Guid.NewGuid();
                    if (supplied.SavedId is null) await vault.SaveAsync(new SavedPassword(savedId, string.IsNullOrWhiteSpace(supplied.Name) ? Path.GetFileName(CompressionOutput) : supplied.Name, supplied.Secret, "", true, DateTimeOffset.UtcNow));
                    await vault.RecordSuccessAsync(savedId, outcome.OutputPath); await RefreshVaultAsync();
                }
                catch (Exception exception) when (exception is VaultException or IOException or UnauthorizedAccessException) { Banner = "压缩包已创建，但密码未能保存，请保留你使用的密码。"; }
            }
            Footer = CompressionMessage;
        }
        catch(OperationCanceledException) {CompressionMessage="已取消，目标文件保持不变。";}
        finally
        {
            compressionPassword = null; Changed(nameof(CompressionPasswordStatus)); CompressionIndeterminate = false;
            Footer=CompressionMessage;cancellation.Dispose(); cancellation = null; CompressionRunning = false; IsBusy = false; idle.TrySetResult();
        }
    }
    private static string CompressionError(string code) => code switch
    {
        "zip_password_ascii_only" => "ZIP 加密仅支持 ASCII 密码；中文密码请改用 7z。",
        "format_single_file_required" => "此格式只允许一个文件；文件夹或多个文件请先打包为 TAR。",
        "format_encryption_unsupported" => "此格式不支持加密，请选择 7z 或 ZIP。",
        "format_validation_failed" => "新包内容清单未通过校验，目标文件保持不变。",
        "duplicate_archive_path" => "选中的不同文件或文件夹具有相同名称，请分开压缩。",
        "unsupported_link" => "当前压缩不支持链接或联接点，请选择原始文件。",
        "output_exists" => "目标文件已存在，未覆盖。",
        "empty_sources" => "没有可压缩的文件，请重新选择。",
        "invalid_volume_size"=>"分卷大小无效，请检查数值与单位。",
        "invalid_volume_set"=>"分卷未能通过完整校验，未交付目标文件。",
        "sfx_module_invalid"=>"自解压模块未通过校验，未创建目标文件。",
        "sfx_validation_failed"=>"自解压内容未通过完整校验，未交付目标文件。",
        _ => "未能创建完整压缩包。请检查源文件、保存位置和可用空间。"
    };
}
