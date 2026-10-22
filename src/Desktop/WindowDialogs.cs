using System.Windows;
using ArchivePassword.Core;
using Microsoft.Win32;

namespace ArchivePassword.Desktop;

public sealed record CompressionPasswordSelection(PasswordPromptResponse Password,bool? EncryptHeaders);

public sealed class WindowDialogs(Window owner, Action<PasswordDialog>? prepareDialog = null,
    Func<string?>? selectEngineUpdate = null, Func<string, string, bool>? confirmEngineUpdate = null,
    Action<ArchiveContentsDialog>? prepareContents = null, Action<ExtractionFailureDialog>? prepareFailure = null,
    Func<bool, string?>? selectVaultBackup = null, Action<VaultBackupDialog>? prepareVaultBackup = null, Action<VaultImportDialog>? prepareVaultImport = null,Action<ExtractionRulesDialog>? prepareRules=null,
    Func<string, int, bool>? confirmPasswordOrganization = null, Action<FileHashDialog>? prepareHashes = null, Action<FilenameEncodingDialog>? prepareEncoding = null,
    Action<MasterPasswordDialog>? prepareMaster = null, Func<int,bool>? confirmVaultRecovery = null,
    Func<ArchiveEditAction,string[]>? selectArchiveEditSources=null,Func<ArchiveEditAction,int,int,bool>? confirmArchiveEdit=null,Action<ArchiveConversionDialog>? prepareConversion=null,Func<string,bool>? confirmConversionReplace=null,
    Action<ArchivePreviewDialog>? preparePreview=null,Func<string,bool>? confirmApplicationInstall=null,Func<string,bool>? confirmDiscardCheckpoint=null,
    Action<ExistingFileDialog>? prepareConflict=null,Action<ArchiveTextEditDialog>? prepareTextEdit=null,Action<ArchiveEditConfirmDialog>? prepareEditConfirm=null,
    Action<CompressionContentsDialog>? prepareCompressionContents=null)
{
    public void ShowCompressionContents(CompressionContentsViewModel model)
    {var dialog=new CompressionContentsDialog(model){Owner=owner};prepareCompressionContents?.Invoke(dialog);dialog.ShowDialog();}
    public bool ConfirmWorkspaceExtraction(int? count=null,int? selectedItemCount=null) => prepareContents is not null || owner is not MainWindow main || main.ConfirmExtractionOptions(count,selectedItemCount);
    public bool ConfirmDiscardCheckpoint(string name)=>confirmDiscardCheckpoint?.Invoke(name) ?? MessageBox.Show(owner,
        UiText.Format("Checkpoint.DiscardPrompt",name),UiText.Get("Checkpoint.Discard"),MessageBoxButton.YesNo,MessageBoxImage.Question,MessageBoxResult.No)==MessageBoxResult.Yes;
    public bool ConfirmApplicationInstall(string version) => confirmApplicationInstall?.Invoke(version) ??
        MessageBox.Show(owner,UiText.Format("SoftwareUpdate.Confirm",version),UiText.Get("T_651f27447015"),
            MessageBoxButton.YesNo,MessageBoxImage.Question,MessageBoxResult.No)==MessageBoxResult.Yes;
    public void ShowArchivePreview(ArchivePreviewViewModel model)
    {
        var dialog=new ArchivePreviewDialog(model) {Owner=owner.OwnedWindows.OfType<ArchiveContentsDialog>().FirstOrDefault() ?? owner};
        if(preparePreview is null && owner is MainWindow main && main.HostPreview(dialog,model)) return;
        preparePreview?.Invoke(dialog);dialog.ShowDialog();
    }
    public void ShowArchiveConversion(ArchiveConversionViewModel model)
    {
        var dialog=new ArchiveConversionDialog(model) {Owner=owner};prepareConversion?.Invoke(dialog);dialog.ShowDialog();
    }
    public bool ConfirmConversionReplace(string path)=>confirmConversionReplace?.Invoke(path) ?? MessageBox.Show(owner,UiText.Get("Conversion.ReplacePrompt")+"\n"+path,UiText.Get("Conversion.Title"),MessageBoxButton.YesNo,MessageBoxImage.Question)==MessageBoxResult.Yes;
    public string[] SelectArchiveEditSources(ArchiveEditAction action)
    {
        if(selectArchiveEditSources is not null) return selectArchiveEditSources(action);
        if(action==ArchiveEditAction.AddFolder)
        {
            var folder=new OpenFolderDialog {Title=UiText.Get("T_357e9c21398d")};return folder.ShowDialog(owner)==true?[folder.FolderName]:[];
        }
        var files=new OpenFileDialog {Title=action==ArchiveEditAction.Replace?UiText.Get("T_6c93c9b1cf76"):UiText.Get("T_776af38fd768"),Multiselect=action!=ArchiveEditAction.Replace,Filter=UiText.Get("T_b0e866423d58")};
        return files.ShowDialog(owner)==true?files.FileNames:[];
    }
    public bool ConfirmArchiveEdit(ArchiveEditAction action,int removed,int added)=>confirmArchiveEdit?.Invoke(action,removed,added) ?? MessageBox.Show(owner,
        UiText.Format("T_18a17ee1b961", removed, added),
        UiText.Get("T_79bedfdfce1a"),MessageBoxButton.YesNo,MessageBoxImage.Question)==MessageBoxResult.Yes;
    public MasterPasswordResponse? AskMasterPassword(MasterPasswordAction action)
    {
        var dialog=new MasterPasswordDialog(action){Owner=owner};prepareMaster?.Invoke(dialog);return dialog.ShowDialog()==true?dialog.Response:null;
    }
    public bool ConfirmVaultRecovery(int count)=>confirmVaultRecovery?.Invoke(count) ?? MessageBox.Show(owner,UiText.Format("T_4ac1c3d04751", count),UiText.Get("T_31f7ee87ce42"),MessageBoxButton.YesNo,MessageBoxImage.Question)==MessageBoxResult.Yes;
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
        var filter = UiText.Get("T_2e537d66280c");
        if (exporting)
        {
            var dialog = new SaveFileDialog { Title = UiText.Get("T_e22598f79cf3"), Filter = filter, DefaultExt = ".nvault", AddExtension = true,
                FileName = "nomozip-passwords-" + DateTime.Now.ToString("yyyyMMdd"), OverwritePrompt = true };
            return dialog.ShowDialog(owner) == true ? dialog.FileName : null;
        }
        var input = new OpenFileDialog { Title = UiText.Get("T_18653fc51f17"), Filter = filter };
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
        var dialog = new OpenFileDialog { Multiselect = true, Title = UiText.Get("T_e8166a5ae76c"), Filter = UiText.Get("T_b0e866423d58") };
        return dialog.ShowDialog(owner) == true ? dialog.FileNames : [];
    }
    public string? SelectCompressionFolder()
    {
        var dialog = new OpenFolderDialog { Title = UiText.Get("T_89cd547d9c4a") };
        return dialog.ShowDialog(owner) == true ? dialog.FolderName : null;
    }
    public string? SelectArchiveOutput(string format)
    {
        var options = CompressionFormats.Find(format);
        var extension = format == "sfx" ? ".exe" : options?.Extension ?? throw new ArgumentException("Unsupported compression format.", nameof(format));
        var dialog = new SaveFileDialog { Title = UiText.Get("T_408fc90e4db5"), Filter = (format=="sfx"?UiText.Get("T_0a0eaf72b488"):options!.Label+UiText.Get("T_1c5e6e08253a"))+"|*"+extension, DefaultExt = extension, AddExtension = true, OverwritePrompt = false };
        return dialog.ShowDialog(owner) == true ? dialog.FileName : null;
    }
    public bool ConfirmArchiveReplacement(string path) => confirmEngineUpdate?.Invoke(path, "archive") ?? (MessageBox.Show(owner,
        UiText.Get("T_19686d32f96f") + path + UiText.Get("T_5c6b348e7c5f"), UiText.Get("T_bcd9ac8817f3"), MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes);
    public Task<CompressionPasswordSelection?> AskCompressionPasswordAsync(string name, IReadOnlyList<SavedPassword> saved, bool canSave,bool? encryptHeaders=null)
    {
        var dialog = new PasswordDialog(name, false, saved, canSave, compressing: true,encryptHeaders:encryptHeaders) { Owner = owner };
        prepareDialog?.Invoke(dialog);
        var response=dialog.ShowDialog()==true?dialog.Response:null;
        return Task.FromResult(response is null?null:new CompressionPasswordSelection(response,dialog.CompressionHeaderEncryption));
    }
    public string[] SelectArchives()
    {
        var dialog = new OpenFileDialog { Multiselect = true, Title = UiText.Get("T_309cb972a002"), Filter = UiText.Get("T_dfaa16616d31") };
        return dialog.ShowDialog(owner) == true ? dialog.FileNames : [];
    }
    public string? SelectOutput(string current)
    {
        var dialog = new OpenFolderDialog { Title = UiText.Get("T_537bdbd1e894"), InitialDirectory = System.IO.Directory.Exists(current) ? current : "" };
        return dialog.ShowDialog(owner) == true ? dialog.FolderName : null;
    }
    public async Task ShowArchiveContentsAsync(ArchiveContentsViewModel model)
    {
        if(prepareContents is null && owner is MainWindow main) {await main.HostArchiveAsync(new ArchiveContentsDialog(model),model);return;}
        ShowArchiveContents(model);
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
    public void ShowCompressionResult(string name,string status,EngineResult failure)
    {
        var dialog=new ExtractionFailureDialog(name,failure) {Owner=owner};
        dialog.Title=dialog.ResultHeading.Text=UiText.Get("Compression.ResultTitle");dialog.ResultSummary.Text=status;dialog.ContextDescription.Text="";
        if(failure.Failures.Count==0){dialog.FailureTable.Visibility=Visibility.Collapsed;dialog.MinHeight=300;dialog.Height=360;}
        prepareFailure?.Invoke(dialog);dialog.ShowDialog();
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
        MessageBox.Show(owner, UiText.Format("T_f321085e8ea2", count), UiText.Get("T_b7cddbf2761a"), MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
    public bool ConfirmMergePasswords(string name, int count) => confirmPasswordOrganization?.Invoke("merge", count) ??
        MessageBox.Show(owner, UiText.Format("T_e98b7cc07066", name, count), UiText.Get("T_acc3854e8b71"), MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
    public ExistingFilePolicy ResolveConflict(string path) => ResolveFileConflict(new(path,path)).Policy;
    public ExistingFileDecision ResolveFileConflict(ExistingFileConflict conflict)
    {
        var dialog=new ExistingFileDialog(conflict) {Owner=owner};prepareConflict?.Invoke(dialog);
        return dialog.ShowDialog()==true?dialog.Decision:new(ExistingFilePolicy.Ask);
    }
    public bool ConfirmTextEdit(ArchiveEntry entry,bool writeBack,string encoding,long? editedSize=null)
    {
        var dialog=new ArchiveEditConfirmDialog(entry,writeBack,encoding,editedSize) {Owner=owner};prepareEditConfirm?.Invoke(dialog);
        return dialog.ShowDialog()==true;
    }
    public string? EditTextCopy(ArchiveTextEditSession session)
    {
        var dialog=new ArchiveTextEditDialog(session) {Owner=owner};prepareTextEdit?.Invoke(dialog);
        return dialog.ShowDialog()==true?dialog.AcceptedText:null;
    }
    public bool ConfirmDelete(string name) => MessageBox.Show(owner, UiText.Format("T_2c2b32da9ad3", name), UiText.Get("T_9e08e64ab2ee"), MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
    public bool ConfirmRestore() => MessageBox.Show(owner, UiText.Get("T_0a2b7a9840b4"), UiText.Get("T_1ae486278078"), MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
    public string? SelectEngineUpdate()
    {
        if (selectEngineUpdate is not null) return selectEngineUpdate();
        var dialog = new OpenFileDialog { Title = UiText.Get("T_7e7357ef4c49"), Filter = UiText.Format("T_9f2931076bbe", EngineArchitecture.Current) };
        return dialog.ShowDialog(owner) == true ? dialog.FileName : null;
    }
    public bool ConfirmEngineUpdate(string version, string current) => confirmEngineUpdate?.Invoke(version, current) ?? (MessageBox.Show(owner,
        UiText.Format("T_27b9b12b34a0", version, current) +
        (Version.TryParse(current, out var currentVersion) && Version.Parse(version) < currentVersion ? UiText.Get("T_e72f93a83aa9") : ""),
        UiText.Get("T_984c2e351e4f"), MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes);
}
