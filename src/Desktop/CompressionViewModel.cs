using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using ArchivePassword.Core;
using ArchivePassword.Infrastructure;

namespace ArchivePassword.Desktop;

public sealed record CompressionSourceRow(string Path, string? PreviewKind = null)
{
    public string Name => System.IO.Path.GetFileName(System.IO.Path.TrimEndingDirectorySeparator(Path));
    public bool IsDirectory=>PreviewKind==UiText.Get("T_7c7802d8adae") || Directory.Exists(Path);
    public string SizeText {get {try {var size=IsDirectory?-1:new FileInfo(Path).Length;return size<0?"—":size<1024?$"{size} B":size<1048576?$"{size/1024.0:0.#} KB":$"{size/1048576.0:0.#} MB";}catch(Exception error) when(error is IOException or UnauthorizedAccessException) {return "—";}}}
    public string Kind => FileKindIcon.TypeDescription(Path,IsDirectory);
}
public sealed partial class MainViewModel
{
    private int compressionFormat, compressionLevel = 2;
    private bool compressionEncrypt, compressionHeaders = true, compressionIndeterminate, compressionRunning;
    private double compressionPercent;
    private string compressionOutput = "", compressionMessage = UiText.Get("T_d576b99c6465");
    private CompressionSourceRow? selectedCompressionSource;
    private PasswordPromptResponse? compressionPassword;
    private bool compressionSfx,sfxAvailable;
    public bool CompressionSfx
    {
        get=>compressionSfx;
        set {if(value && (!Is7zCompression || volumeOption!=0 || !sfxAvailable)) return;var previous=CompressionExtension;
            if(Set(ref compressionSfx,value)) {AlignCompressionExtension(previous);Changed(nameof(CompressionSfxHint));Changed(nameof(CompressionAdvancedSummary));}}
    }
    private CompressionFormatInfo CompressionOptions => CompressionFormats.All[CompressionFormat];
    private string CompressionExtension=>CompressionSfx?".exe":CompressionOptions.Extension;
    public bool CanEncryptCompression => IsIdle && CompressionOptions.Encryption;
    public bool CanSetCompressionLevel => IsIdle && CompressionOptions.CompressionLevel;
    public bool CanSplitCompression => IsIdle && CompressionOptions.Encryption;
    public string CompressionSourceError => CompressionOptions.SingleFile && CompressionSources.Count > 0
        && (CompressionSources.Count != 1 || !File.Exists(CompressionSources[0].Path)) ? UiText.Get("T_238288d05599") : "";
    private void CompressionCapabilitiesChanged()
    {
        Changed(nameof(CanEncryptCompression));Changed(nameof(CanSetCompressionLevel));Changed(nameof(CanSplitCompression));
        Changed(nameof(CompressionSourceError));CommandManager.InvalidateRequerySuggested();
    }
    public bool CanUseSfx=>IsIdle && Is7zCompression && volumeOption==0 && sfxAvailable;
    public string CompressionSfxHint=>!Is7zCompression?(CompressionFormat==1?UiText.Get("T_aebafd23c2d9"):""):volumeOption!=0?UiText.Get("T_f12fba592449"):!sfxAvailable?UiText.Get("T_5b79de668b3b"):CompressionSfx?UiText.Get("T_ba22f8e0541a"):"";
    private void AlignCompressionExtension(string previous) {if(Path.GetExtension(CompressionOutput).Equals(previous,StringComparison.OrdinalIgnoreCase)) CompressionOutput=Path.ChangeExtension(CompressionOutput,CompressionExtension);CommandManager.InvalidateRequerySuggested();}
    private async Task RefreshSfxAvailabilityAsync()
    {
        try {sfxAvailable=activeEngine.Length>0 && (await engines.ReadSfxModuleAsync(activeEngine)).IsValid();}
        catch(Exception error) when(error is UpdateException or IOException or UnauthorizedAccessException or System.Text.Json.JsonException) {sfxAvailable=false;}
        if(!sfxAvailable) CompressionSfx=false;Changed(nameof(CanUseSfx));Changed(nameof(CompressionSfxHint));Changed(nameof(CompressionAdvancedSummary));
    }
    private int volumeOption,volumeUnit;private string volumeCustom="100",createdCompressionPath="";
    public int CompressionVolumeOption {get=>volumeOption;set {if(value!=0 && !CompressionOptions.Encryption) return;if(Set(ref volumeOption,value)) VolumeOptionsChanged();}}
    public int CompressionVolumeUnit {get=>volumeUnit;set {if(Set(ref volumeUnit,value)) VolumeOptionsChanged();}}
    public string CompressionVolumeCustom {get=>volumeCustom;set {if(Set(ref volumeCustom,value)) VolumeOptionsChanged();}}
    private bool ValidVolumeSize=>VolumeSizeOptions.TryGetBytes(volumeOption,volumeCustom,volumeUnit,out _);
    private void VolumeOptionsChanged() {if(volumeOption!=0) CompressionSfx=false;Changed(nameof(CompressionVolumeError));Changed(nameof(CompressionVolumeHint));Changed(nameof(CanUseSfx));Changed(nameof(CompressionSfxHint));Changed(nameof(CompressionAdvancedSummary));CommandManager.InvalidateRequerySuggested();}
    public string CompressionVolumeError=>ValidVolumeSize?"":UiText.Get("T_f1a4a993b180");
    public string CompressionVolumeHint=>volumeOption==0?"":ValidVolumeSize?UiText.Get("T_e836053f4b78"):"";
    public ObservableCollection<CompressionSourceRow> CompressionSources { get; } = [];
    private CompressionSourceRow[] selectedCompressionSources=[];
    public string CompressionSourceSummary=>UiText.Format("Compression.SourceCount",CompressionSources.Count,selectedCompressionSources.Count(CompressionSources.Contains));
    internal void SelectCompressionSources(IEnumerable<CompressionSourceRow> rows)
    {selectedCompressionSources=rows.ToArray();Changed(nameof(CompressionSourceSummary));CommandManager.InvalidateRequerySuggested();}
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
                AlignCompressionExtension(previousExtension);Changed(nameof(CanUseSfx));Changed(nameof(CompressionSfxHint));Changed(nameof(CompressionAdvancedSummary));
                Changed(nameof(Is7zCompression)); Changed(nameof(CompressionHint));
                CompressionCapabilitiesChanged();
            }
        }
    }
    public bool Is7zCompression => CompressionFormat == 0;
    public string CompressionHint => CompressionFormatName switch
    {
        "7z" => UiText.Get("T_22c2a744aea2"),
        "zip" => UiText.Get("T_abb6a356f9a2"),
        "tar" => UiText.Get("T_7f8c82c8d8b8"),
        _ => UiText.Format("T_b0f1ecfd6cb8", CompressionOptions.Extension)
    };
    public int CompressionLevel { get => compressionLevel; set => Set(ref compressionLevel, value); }
    public bool CompressionEncrypt { get => compressionEncrypt; set { if (value && !CompressionOptions.Encryption) return; if (Set(ref compressionEncrypt, value)) { if (!value) compressionPassword = null; Changed(nameof(CompressionPasswordStatus));Changed(nameof(CompressionPasswordSummary)); } } }
    public bool CompressionHeaders { get => compressionHeaders; set => Set(ref compressionHeaders, value); }
    public string CompressionPasswordStatus => compressionPassword is null ? UiText.Get("T_5d8111695438") : UiText.Get("T_bb1057c00cf3");
    public string CompressionAdvancedSummary=>UiText.Format("Compression.AdvancedSummary",CompressionVolumeOption switch {0=>UiText.Get("T_13fa566b385e"),1=>"100 MiB",2=>"1 GiB",3=>"4 GiB",_=>CompressionVolumeCustom+" "+(CompressionVolumeUnit==0?"MiB":"GiB")},UiText.Get(CompressionSfx?"Compression.Yes":"Compression.No"));
    public string CompressionPasswordSummary => UiText.Get(compressionPassword is null ? "Redesign.PasswordNotSet" : "T_bb1057c00cf3");
    public string CompressionOutput { get => compressionOutput; set {if(Set(ref compressionOutput,value)) {createdCompressionPath="";Changed(nameof(CompressionOutputDirectory));Changed(nameof(CompressionOutputName));}} }
    public string CompressionOutputDirectory
    {
        get {var slash=CompressionOutput.LastIndexOfAny(['\\','/']);return slash<0?"":CompressionOutput[..(slash==2 && CompressionOutput[1]==':'?slash+1:slash)];}
        set {var file=CompressionOutput[(CompressionOutput.LastIndexOfAny(['\\','/'])+1)..];CompressionOutput=JoinOutput(value,file.Length==0?"archive"+CompressionExtension:file);}
    }
    public string CompressionOutputName
    {
        get {var file=CompressionOutput[(CompressionOutput.LastIndexOfAny(['\\','/'])+1)..];return file.EndsWith(CompressionExtension,StringComparison.OrdinalIgnoreCase)?file[..^CompressionExtension.Length]:file;}
        set => CompressionOutput=JoinOutput(CompressionOutputDirectory,value.EndsWith(CompressionExtension,StringComparison.OrdinalIgnoreCase)?value:value+CompressionExtension);
    }
    private static string JoinOutput(string directory,string file)=>directory.Length==0?file:directory.TrimEnd('\\','/')+"\\"+file;
    public string CompressionMessage { get => compressionMessage; set => Set(ref compressionMessage, value); }
    public double CompressionPercent { get => compressionPercent; private set => Set(ref compressionPercent, value); }
    public bool CompressionIndeterminate { get => compressionIndeterminate; private set => Set(ref compressionIndeterminate, value); }
    public bool CompressionRunning { get => compressionRunning; private set {if(Set(ref compressionRunning,value)) {Changed(nameof(HasCompressionActivity));Changed(nameof(ShowCompressionActivity));}} }
    public CompressionSourceRow? SelectedCompressionSource { get => selectedCompressionSource; set => Set(ref selectedCompressionSource, value); }
    private string CompressionFormatName => CompressionOptions.Name;
    private void InitializeCompressionCommands()
    {
        InitializeCompressionProfiles();
        AddCompressionFilesCommand = new RelayCommand(() => AddCompressionSources(dialogs.SelectCompressionFiles()), () => IsIdle);
        AddCompressionFolderCommand = new RelayCommand(() => { if (dialogs.SelectCompressionFolder() is { } path) AddCompressionSources([path]); }, () => IsIdle);
        RemoveCompressionSourceCommand = new RelayCommand(() =>
        {
            var selected=selectedCompressionSources.Where(CompressionSources.Contains).ToArray();
            if(selected.Length==0 && SelectedCompressionSource is {} focused)selected=[focused];
            foreach(var item in selected)CompressionSources.Remove(item);
        }, () => IsIdle && (SelectedCompressionSource is not null || selectedCompressionSources.Any(CompressionSources.Contains)));
        BrowseCompressionOutputCommand = new RelayCommand(() => { if (dialogs.SelectArchiveOutput(CompressionSfx?"sfx":CompressionFormatName) is { } path) CompressionOutput = path; }, () => IsIdle);
        SetCompressionPasswordCommand = new AsyncCommand(SetCompressionPasswordAsync, () => IsIdle && CompressionOptions.Encryption);
        CreateArchiveCommand = new AsyncCommand(CreateArchiveAsync, () => Page == 3 && IsIdle && CompressionSources.Count > 0 && !string.IsNullOrWhiteSpace(CompressionOutput) && !string.IsNullOrWhiteSpace(CompressionOutputName) && EngineVersions.Count > 0 && ValidVolumeSize && CompressionSourceError.Length == 0);
        OpenCreatedArchiveFolderCommand = new RelayCommand(() => OpenFolder(Path.GetDirectoryName(Path.GetFullPath(createdCompressionPath.Length>0?createdCompressionPath:CompressionOutput))!), () => File.Exists(createdCompressionPath.Length>0?createdCompressionPath:CompressionOutput));
        CompressionSources.CollectionChanged += (_, _) => {CompressionCapabilitiesChanged();Changed(nameof(CompressionSourceSummary));};
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
        try
        {
            var selection=await dialogs.AskCompressionPasswordAsync(Path.GetFileName(CompressionOutput),allPasswords,vaultAvailable,Is7zCompression?CompressionHeaders:null);
            if(selection is null)return;
            CompressionEncrypt=true;compressionPassword=selection.Password;
            if(selection.EncryptHeaders is {} encryptHeaders)CompressionHeaders=encryptHeaders;
            Changed(nameof(CompressionPasswordStatus));Changed(nameof(CompressionPasswordSummary));
        }
        finally {if(ownsBusy){IsBusy=false;idle.TrySetResult();}}
    }
    private async Task CreateArchiveAsync()
    {
        if (CompressionSourceError.Length > 0) {CompressionMessage=CompressionSourceError;return;}
        if(!VolumeSizeOptions.TryGetBytes(volumeOption,volumeCustom,volumeUnit,out var volumeSize)) {CompressionMessage=CompressionVolumeError;return;}
        if (!Path.GetExtension(CompressionOutput).Equals(CompressionExtension, StringComparison.OrdinalIgnoreCase)) { CompressionMessage = UiText.Get("T_b64114dffb9b"); return; }
        if(volumeSize>0 && VolumeCompressionService.HasExistingTarget(CompressionOutput)) {CompressionMessage=UiText.Get("T_c4d54b1608b5");return;}
        lastCompressionProfile = CaptureCompressionProfile("");
        IsBusy=true;idle=new TaskCompletionSource();cancellation=new CancellationTokenSource();
        ArchiveTaskRow? workspaceTask=null;
        try
        {
        if (!await PersistCompressionProfilesAsync()) Banner = UiText.Get("Compression.ProfileSaveFailed");
        SfxModule? module=null;
        if(CompressionSfx)
        {
            if(volumeSize>0 || !Is7zCompression) {CompressionMessage=UiText.Get("T_0072b72a230a");return;}
            try {module=await engines.ReadSfxModuleAsync(activeEngine,cancellation.Token);}
            catch(Exception error) when(error is UpdateException or IOException or UnauthorizedAccessException or System.Text.Json.JsonException) {CompressionMessage=UiText.Get("T_0c3a93b2ceab");return;}
        }
        var replace = volumeSize==0 && File.Exists(CompressionOutput);
        if (replace && !dialogs.ConfirmArchiveReplacement(CompressionOutput)) { CompressionMessage = UiText.Get("T_b27d094f513e"); return; }
        if (CompressionEncrypt && compressionPassword is null) await SetCompressionPasswordAsync();
        if (CompressionEncrypt && compressionPassword is null) { CompressionMessage = UiText.Get("T_d2011de64a34"); return; }
        compressionActivityStarted=true;Changed(nameof(HasCompressionActivity));Page=0;
        createdCompressionPath="";CompressionRunning = true;CompressionPercent = 0; CompressionIndeterminate = true;
        workspaceTask=AddCompressionTask();
            var client = new ArchiveEngineClient(Path.Combine(baseDirectory, "ArchiveEngineWorker.exe"), engines.DllPath(activeEngine));
            var progress = new Progress<TaskUpdate>(value =>
            {
                CompressionMessage = value.Message;
                if (value.Progress is { } state) { CompressionIndeterminate = state.Total == 0; CompressionPercent = state.Total == 0 ? 0 : Math.Min(100, state.Completed * 100.0 / state.Total); }
                else CompressionIndeterminate = true;
                workspaceTask.Status=CompressionMessage;workspaceTask.Percent=CompressionPercent;workspaceTask.IsIndeterminate=CompressionIndeterminate;
            });
            var outcome = module is not null ? await new SfxCompressionService(client,client).CreateAsync(CompressionSources.Select(value=>value.Path).ToArray(),CompressionOutput,module,
                new[]{0,1,5,7,9}[CompressionLevel],CompressionEncrypt?compressionPassword?.Secret:null,CompressionHeaders,replace,progress,cancellation.Token)
                : await new CompressionService(client, client).CreateAsync(CompressionSources.Select(value => value.Path).ToArray(), CompressionOutput,
                CompressionFormatName, new[] { 0, 1, 5, 7, 9 }[CompressionLevel], CompressionEncrypt ? compressionPassword?.Secret : null, CompressionHeaders, replace, progress, cancellation.Token,volumeSize);
            if(outcome.Engine.IsSuccess) {createdCompressionPath=outcome.OutputPath;workspaceTask.SetArchiveResult(outcome.OutputPath);}
            CompressionMessage = outcome.Engine.IsSuccess ? module is not null?UiText.Get("T_ada71e8959d8"):outcome.Volumes is { } volumes ? UiText.Format("T_d18624b2347a", volumes.Count, outcome.OutputPath) : UiText.Get("T_1bb010c7351a") : outcome.Engine.Status == "cancelled" ? UiText.Get("T_8e8fba138ab2") : CompressionError(outcome.Engine.Code);
            workspaceTask.Completed=outcome.Engine.IsSuccess;workspaceTask.Failure=outcome.Engine.IsSuccess?null:outcome.Engine;
            if(workspaceTask.Completed)workspaceTask.ResultOutputPath=Path.GetDirectoryName(Path.GetFullPath(outcome.OutputPath));
            if (outcome.Engine.IsSuccess && CompressionEncrypt && compressionPassword is { } supplied && (supplied.Save || supplied.SavedId is not null))
            {
                try
                {
                    var savedId = supplied.SavedId ?? Guid.NewGuid();
                    if (supplied.SavedId is null) await vault.SaveAsync(new SavedPassword(savedId, string.IsNullOrWhiteSpace(supplied.Name) ? Path.GetFileName(CompressionOutput) : supplied.Name, supplied.Secret, "", true, DateTimeOffset.UtcNow));
                    await vault.RecordSuccessAsync(savedId, outcome.OutputPath); await RefreshVaultAsync();
                }
                catch (Exception exception) when (exception is VaultException or IOException or UnauthorizedAccessException) { Banner = UiText.Get("T_0346e5dcf690"); }
            }
            Footer = CompressionMessage;
        }
        catch(OperationCanceledException) {CompressionMessage=UiText.Get("T_76756534e51e");if(workspaceTask is not null)workspaceTask.Failure=new EngineResult {Status="cancelled",Code="cancelled"};}
        finally
        {
            compressionPassword = null; Changed(nameof(CompressionPasswordStatus));Changed(nameof(CompressionPasswordSummary)); CompressionIndeterminate = false;
            if(workspaceTask is not null){workspaceTask.Status=CompressionMessage;workspaceTask.Running=false;workspaceTask.IsIndeterminate=false;}
            Footer=CompressionMessage;cancellation.Dispose(); cancellation = null; CompressionRunning = false; IsBusy = false; idle.TrySetResult();
        }
    }
    private static string CompressionError(string code) => code switch
    {
        "zip_password_ascii_only" => UiText.Get("T_0f39ab20ad82"),
        "format_single_file_required" => UiText.Get("T_034a1bc397c6"),
        "format_encryption_unsupported" => UiText.Get("T_d2e37e15b46a"),
        "format_validation_failed" => UiText.Get("T_2bf33abd7e1e"),
        "duplicate_archive_path" => UiText.Get("T_ab621a6eecb5"),
        "unsupported_link" => UiText.Get("T_c78f695d1e5e"),
        "output_exists" => UiText.Get("T_c5d6fdc4bff5"),
        "empty_sources" => UiText.Get("T_1e332f6db100"),
        "invalid_volume_size"=>UiText.Get("T_65e10632e6f4"),
        "invalid_volume_set"=>UiText.Get("T_eb0e28eeb37b"),
        "sfx_module_invalid"=>UiText.Get("T_2207abf5d6fe"),
        "sfx_validation_failed"=>UiText.Get("T_e8c12deee3da"),
        _ => UiText.Get("T_bf7a3232e891")
    };
}
