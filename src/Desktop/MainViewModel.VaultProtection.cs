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
    public string PasswordEmptyMessage=>VaultLocked?"密码库已锁定。解锁后查看保存的密码。":"没有匹配的密码。可调整搜索或添加密码。";
    public bool VaultLocked=>vaultProtection==VaultProtectionState.Locked;
    public bool VaultMasterEnabled=>vaultProtection!=VaultProtectionState.Disabled;
    public string VaultProtectionMessage=>VaultLocked?"密码库已锁定；自动尝试不可用，可手动输入解压密码。":VaultMasterEnabled?"主密码保护已开启，本次已解锁。请定期导出独立加密备份。":"主密码未开启，本机密码由当前 Windows 用户加密保护。";
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
        SaveVaultIdleCommand=new AsyncCommand(async()=>{await VaultOperationAsync(async()=>{await SaveSettingsAsync();Banner=settingsWritable?"已保存空闲锁定设置。":"本次设置已生效，原设置文件保持不变。";});},()=>IsIdle);
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
        catch(VaultException error){Banner=error.Code switch{"master_password_or_damage"=>"主密码不正确，或密码库已损坏。原文件保持不变。","master_password_length"=>"主密码需要 12 至 16384 个字符。","vault_locked"=>"密码库已锁定，请先解锁。",_=>"密码库操作未完成，请保留原文件或使用独立加密备份。"};}
        catch(VaultTransferException){Banner="备份密码不正确，或备份文件无法读取。原密码库保持不变。";}
        catch(Exception error) when(error is IOException or UnauthorizedAccessException){Banner="密码库操作未完成，请检查文件权限。原数据未被清空。";}
        finally{IsBusy=false;idle.TrySetResult();CommandManager.InvalidateRequerySuggested();}
    }
    private async Task ChangeVaultProtectionAsync(MasterPasswordAction action)=>await VaultOperationAsync(async()=>
    {
        if(vault is not IPasswordVaultProtection protection)return;
        var response=dialogs.AskMasterPassword(action);if(response is null){Banner="已取消，密码库保持原状态。";return;}
        await Task.Run(async()=>{switch(action){case MasterPasswordAction.Unlock:await protection.UnlockAsync(response.Current);break;case MasterPasswordAction.Enable:await protection.EnableMasterPasswordAsync(response.Replacement);break;case MasterPasswordAction.Change:await protection.ChangeMasterPasswordAsync(response.Current,response.Replacement);break;case MasterPasswordAction.Disable:await protection.DisableMasterPasswordAsync(response.Current);break;}});
        await RefreshVaultAsync();RecordVaultActivity();Banner=action==MasterPasswordAction.Disable?"已关闭主密码，恢复 Windows 用户加密保护。":"密码库已解锁，主密码保护已生效。";
    });
    private async Task LockVaultAsync()=>await VaultOperationAsync(async()=>
    {
        if(vault is IPasswordVaultProtection protection){await protection.LockAsync();ClearVaultReferences();await RefreshVaultProtectionAsync();vaultLockRequested=false;Banner="密码库已锁定。";}
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
        await RefreshVaultAsync();RecordVaultActivity();Banner=$"已通过独立备份恢复 {records.Count} 条密码。原锁定库保存在：{preserved}";
    });
}
