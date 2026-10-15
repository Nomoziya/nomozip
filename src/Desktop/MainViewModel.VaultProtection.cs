using System.Diagnostics;
using System.IO;
using System.Windows.Input;
using ArchivePassword.Core;
using ArchivePassword.Infrastructure;

namespace ArchivePassword.Desktop;
public sealed partial class MainViewModel
{
    private VaultProtectionState vaultProtection;
    private int vaultIdleIndex=2;
    private long vaultActivity=Stopwatch.GetTimestamp();
    private bool vaultLockRequested;
    internal Func<TimeSpan>? VaultIdleElapsedOverride {get;set;}
    public string PasswordEmptyMessage=>VaultLocked?UiText.Get("T_a047e3a4eafa"):UiText.Get("T_8b8de629cdd2");
    public bool VaultLocked=>vaultProtection==VaultProtectionState.Locked;
    public bool VaultMasterEnabled=>vaultProtection!=VaultProtectionState.Disabled;
    public string VaultProtectionMessage=>VaultLocked?UiText.Get("T_3751e3763d93"):VaultMasterEnabled?UiText.Get("T_04c3e1fec3f5"):UiText.Get("T_c273eb49ef49");
    public int VaultIdleIndex {get=>vaultIdleIndex;set{if(!IsBusy && Set(ref vaultIdleIndex,Math.Clamp(value,0,4)))RecordVaultActivity();}}
    private int VaultIdleMinutes=>new[]{0,1,5,15,30}[VaultIdleIndex];
    public ICommand UnlockVaultCommand {get;private set;}=null!;
    public ICommand LockVaultCommand {get;private set;}=null!;
    public ICommand EnableMasterCommand {get;private set;}=null!;
    public ICommand ChangeMasterCommand {get;private set;}=null!;
    public ICommand DisableMasterCommand {get;private set;}=null!;
    public ICommand RecoverLockedVaultCommand {get;private set;}=null!;
    public ICommand SaveVaultIdleCommand {get;private set;}=null!;
    private void InitializeVaultProtectionCommands()
    {
        UnlockVaultCommand=new AsyncCommand(()=>ChangeVaultProtectionAsync(MasterPasswordAction.Unlock),()=>IsIdle && VaultLocked);
        EnableMasterCommand=new AsyncCommand(()=>ChangeVaultProtectionAsync(MasterPasswordAction.Enable),()=>IsIdle && !VaultMasterEnabled && vaultAvailable && vault is IPasswordVaultProtection);
        ChangeMasterCommand=new AsyncCommand(()=>ChangeVaultProtectionAsync(MasterPasswordAction.Change),()=>IsIdle && VaultMasterEnabled);
        DisableMasterCommand=new AsyncCommand(()=>ChangeVaultProtectionAsync(MasterPasswordAction.Disable),()=>IsIdle && VaultMasterEnabled);
        LockVaultCommand=new AsyncCommand(LockVaultAsync,()=>IsIdle && VaultMasterEnabled && !VaultLocked);
        RecoverLockedVaultCommand=new AsyncCommand(RecoverLockedVaultAsync,()=>IsIdle && VaultLocked);
        SaveVaultIdleCommand=new AsyncCommand(async()=>{await VaultOperationAsync(async()=>{await SaveSettingsAsync();Banner=settingsWritable?UiText.Get("T_58a15ee482f7"):UiText.Get("T_38af3ab9fef1");});},()=>IsIdle);
    }
    public void RecordVaultActivity(){vaultActivity=Stopwatch.GetTimestamp();vaultLockRequested=false;}
    private async Task RefreshVaultProtectionAsync()
    {
        vaultProtection=vault is IPasswordVaultProtection protection?await protection.GetProtectionStateAsync():VaultProtectionState.Disabled;
        foreach(var name in new[]{nameof(VaultLocked),nameof(VaultMasterEnabled),nameof(VaultProtectionMessage),nameof(VaultSummary),nameof(PasswordEmptyMessage)})Changed(name);
    }
    private void ClearVaultReferences()
    {
        allPasswords=[];PasswordRows.Clear();SelectedPassword=null;compressionPassword=null;Changed(nameof(CompressionPasswordStatus));vaultAvailable=false;
        RefreshPasswordGroups();Changed(nameof(PasswordListSummary));Changed(nameof(DuplicateSummary));Changed(nameof(VaultSummary));
    }
    private async Task VaultOperationAsync(Func<Task> operation)
    {
        IsBusy=true;idle=new TaskCompletionSource();
        try{await operation();}
        catch(VaultException error){Banner=error.Code switch{"master_password_or_damage"=>UiText.Get("T_95b8d5f6906e"),"master_password_length"=>UiText.Get("T_7a325297a9db"),"vault_locked"=>UiText.Get("T_2d678603a0b6"),_=>UiText.Get("T_70ad689f1006")};}
        catch(VaultTransferException){Banner=UiText.Get("T_827f88b30e62");}
        catch(Exception error) when(error is IOException or UnauthorizedAccessException){Banner=UiText.Get("T_1213fe7f3fbd");}
        finally{IsBusy=false;idle.TrySetResult();CommandManager.InvalidateRequerySuggested();}
    }
    private async Task ChangeVaultProtectionAsync(MasterPasswordAction action)=>await VaultOperationAsync(async()=>
    {
        if(vault is not IPasswordVaultProtection protection)return;
        var response=dialogs.AskMasterPassword(action);if(response is null){Banner=UiText.Get("T_d0f1011a6592");return;}
        await Task.Run(async()=>{switch(action){case MasterPasswordAction.Unlock:await protection.UnlockAsync(response.Current);break;case MasterPasswordAction.Enable:await protection.EnableMasterPasswordAsync(response.Replacement);break;case MasterPasswordAction.Change:await protection.ChangeMasterPasswordAsync(response.Current,response.Replacement);break;case MasterPasswordAction.Disable:await protection.DisableMasterPasswordAsync(response.Current);break;}});
        await RefreshVaultAsync();RecordVaultActivity();Banner=action==MasterPasswordAction.Disable?UiText.Get("T_01a43920a4e9"):UiText.Get("T_9d35f5dfdf51");
    });
    private async Task LockVaultAsync()=>await VaultOperationAsync(async()=>
    {
        if(vault is IPasswordVaultProtection protection){await protection.LockAsync();ClearVaultReferences();await RefreshVaultProtectionAsync();vaultLockRequested=false;Banner=UiText.Get("T_924799380463");}
    });
    public async Task CheckVaultIdleAsync()
    {
        if(!VaultMasterEnabled || VaultLocked || VaultIdleMinutes==0)return;
        var elapsed=VaultIdleElapsedOverride?.Invoke() ?? Stopwatch.GetElapsedTime(vaultActivity);
        if(elapsed>=TimeSpan.FromMinutes(VaultIdleMinutes))vaultLockRequested=true;
        if(vaultLockRequested && IsIdle)await LockVaultAsync();
    }
    public async Task ReleaseVaultSessionAsync()
    {
        if(vault is IPasswordVaultProtection protection)await protection.LockAsync();ClearVaultReferences();
    }
    private async Task RecoverLockedVaultAsync()=>await VaultOperationAsync(async()=>
    {
        if(vault is not IPasswordVaultProtection protection)return;
        var path=dialogs.SelectVaultBackup(false);if(path is null)return;
        var password=dialogs.AskBackupPassword(false,path);if(password is null)return;
        var records=await Task.Run(()=>EncryptedVaultBackup.ReadAsync(path,password));
        if(dialogs.PreviewVaultImport([],records) is null || !dialogs.ConfirmVaultRecovery(records.Count))return;
        var response=dialogs.AskMasterPassword(MasterPasswordAction.Recover);if(response is null)return;
        var preserved=await Task.Run(()=>protection.RecoverFromBackupAsync(records,response.Replacement));
        await RefreshVaultAsync();RecordVaultActivity();Banner=UiText.Format("T_21bc9141d482", records.Count, preserved);
    });
}
