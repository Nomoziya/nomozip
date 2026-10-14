namespace ArchivePassword.Core;

public enum VaultProtectionState { Disabled, Locked, Unlocked }
public interface IPasswordVaultProtection
{
    Task<VaultProtectionState> GetProtectionStateAsync(CancellationToken token = default);
    Task UnlockAsync(string password, CancellationToken token = default);
    Task LockAsync(CancellationToken token = default);
    Task EnableMasterPasswordAsync(string password, CancellationToken token = default);
    Task ChangeMasterPasswordAsync(string current, string replacement, CancellationToken token = default);
    Task DisableMasterPasswordAsync(string current, CancellationToken token = default);
    Task RestoreMasterBackupAsync(string password, CancellationToken token = default);
    Task<string> RecoverFromBackupAsync(IReadOnlyList<SavedPassword> records, string newMasterPassword, CancellationToken token = default);
}
