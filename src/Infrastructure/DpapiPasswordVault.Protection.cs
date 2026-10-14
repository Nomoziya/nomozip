using System.Security.Cryptography;
using System.Text.Json;
using ArchivePassword.Core;

namespace ArchivePassword.Infrastructure;

public sealed partial class DpapiPasswordVault
{
    private byte[]? masterKey, masterSalt;
    private static async Task<bool> IsMasterFileAsync(string source, CancellationToken token)
    {
        if (!File.Exists(source)) return false;
        await using var file = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read);
        var prefix = new byte[4]; var count = await file.ReadAsync(prefix, token);
        return count == 4 && MasterVaultCipher.IsProtected(prefix);
    }
    public async Task<VaultProtectionState> GetProtectionStateAsync(CancellationToken token = default)
    {
        await gate.WaitAsync(token);
        try { return !await IsMasterFileAsync(path, token) ? VaultProtectionState.Disabled : masterKey is null ? VaultProtectionState.Locked : VaultProtectionState.Unlocked; }
        finally { gate.Release(); }
    }
    private void ClearKey()
    {
        if (masterKey is not null) CryptographicOperations.ZeroMemory(masterKey);
        masterKey = null; masterSalt = null;
    }
    public async Task LockAsync(CancellationToken token = default)
    {
        await gate.WaitAsync(token); try { ClearKey(); } finally { gate.Release(); }
    }
    private static VaultDocument DecodeDocument(byte[] plaintext)
    {
        try
        {
            var document = JsonSerializer.Deserialize<VaultDocument>(plaintext, Options) ?? throw new JsonException();
            if (document.Version != 1) throw new VaultException("unsupported_vault_version");
            EncryptedVaultBackup.ValidateRecords(document.Passwords);
            if (document.Associations is null) throw new JsonException();
            return document;
        }
        catch (Exception error) when (error is JsonException or VaultTransferException) { throw new VaultException("invalid_protected_vault"); }
    }
    private async Task<(VaultDocument Document, byte[] Key, byte[] Salt)> VerifyMasterAsync(string password, CancellationToken token, string? source = null)
    {
        var bytes = await File.ReadAllBytesAsync(source ?? path, token); var salt = MasterVaultCipher.Salt(bytes);
        byte[]? key = MasterVaultCipher.Derive(password, salt), plaintext = null;
        try
        {
            token.ThrowIfCancellationRequested(); plaintext = MasterVaultCipher.Decrypt(bytes, key, salt);
            var result = (DecodeDocument(plaintext), key, salt); key = null; return result;
        }
        finally { if (key is not null) CryptographicOperations.ZeroMemory(key); if (plaintext is not null) CryptographicOperations.ZeroMemory(plaintext); }
    }
    public async Task UnlockAsync(string password, CancellationToken token = default)
    {
        await gate.WaitAsync(token);
        try { var verified = await VerifyMasterAsync(password, token); ClearKey(); masterKey = verified.Key; masterSalt = verified.Salt; }
        finally { gate.Release(); }
    }
    public Task EnableMasterPasswordAsync(string password, CancellationToken token = default) => ReplaceProtectionAsync(null, password, false, token);
    public Task ChangeMasterPasswordAsync(string current, string replacement, CancellationToken token = default) => ReplaceProtectionAsync(current, replacement, false, token);
    public Task DisableMasterPasswordAsync(string current, CancellationToken token = default) => ReplaceProtectionAsync(current, null, false, token);
    public async Task RestoreMasterBackupAsync(string password, CancellationToken token = default)
    {
        await gate.WaitAsync(token); byte[]? key = null; var temporary = path + ".restore-" + Guid.NewGuid().ToString("N");
        try
        {
            if (!await IsMasterFileAsync(path, token) || !await IsMasterFileAsync(BackupPath, token)) throw new VaultException("invalid_protected_vault");
            var verified = await VerifyMasterAsync(password, token, BackupPath); key = verified.Key;
            File.Copy(BackupPath, temporary); token.ThrowIfCancellationRequested(); File.Move(temporary, path, overwrite: true);
            ClearKey(); masterKey = key; masterSalt = verified.Salt; key = null;
        }
        finally { if (key is not null) CryptographicOperations.ZeroMemory(key); if (File.Exists(temporary)) File.Delete(temporary); gate.Release(); }
    }
    private async Task ReplaceProtectionAsync(string? current, string? replacement, bool recovery, CancellationToken token, VaultDocument? restored = null, bool gateHeld = false)
    {
        if (replacement is not null) MasterVaultCipher.ValidatePassword(replacement);
        if (!gateHeld) await gate.WaitAsync(token); byte[]? newKey = null, plaintext = null;
        try
        {
            VaultDocument document;
            if (recovery) document = restored!;
            else if (current is not null)
            {
                var verified = await VerifyMasterAsync(current, token); document = verified.Document;
                CryptographicOperations.ZeroMemory(verified.Key);
            }
            else
            {
                if (await IsMasterFileAsync(path, token)) throw new VaultException("master_already_enabled");
                document = File.Exists(path) ? await LoadAsync(path, token) : new VaultDocument { Version = 1 };
            }
            plaintext = JsonSerializer.SerializeToUtf8Bytes(document, Options);
            var salt = replacement is null ? null : RandomNumberGenerator.GetBytes(16);
            if (replacement is not null) newKey = MasterVaultCipher.Derive(replacement, salt!);
            byte[] Encode() => newKey is null ? [.. Magic, .. ProtectedData.Protect(plaintext, null, DataProtectionScope.CurrentUser)] : MasterVaultCipher.Encrypt(plaintext, newKey, salt!);
            var next = Encode(); var backup = Encode();
            if (newKey is not null)
            {
                var validation = MasterVaultCipher.Decrypt(next, newKey, salt!);
                try { DecodeDocument(validation); } finally { CryptographicOperations.ZeroMemory(validation); }
            }
            await ReplacePairAsync(next, backup, token);
            ClearKey(); masterKey = newKey; masterSalt = salt; newKey = null;
        }
        finally { if (newKey is not null) CryptographicOperations.ZeroMemory(newKey); if (plaintext is not null) CryptographicOperations.ZeroMemory(plaintext); if (!gateHeld) gate.Release(); }
    }
    // Stage both ciphertexts before committing. Backup commits first so a successful
    // APV2 activation never leaves an APV1 recovery file. No cancellation mid-commit.
    private async Task ReplacePairAsync(byte[] next, byte[] backup, CancellationToken token)
    {
        var destination = Path.GetFullPath(path); Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temporary = destination + ".protect-" + Guid.NewGuid().ToString("N"); var temporaryBackup = temporary + ".bak";
        var previousBackup = File.Exists(BackupPath) ? await File.ReadAllBytesAsync(BackupPath, token) : null;
        var backupCommitted = false;
        try
        {
            await File.WriteAllBytesAsync(temporary, next, token); await File.WriteAllBytesAsync(temporaryBackup, backup, token);
            token.ThrowIfCancellationRequested();
            File.Move(temporaryBackup, BackupPath, overwrite: true); backupCommitted = true;
            if (File.Exists(destination)) File.Replace(temporary, destination, null); else File.Move(temporary, destination);
        }
        catch
        {
            if (backupCommitted)
            {
                if (previousBackup is null) File.Delete(BackupPath);
                else { await File.WriteAllBytesAsync(temporaryBackup, previousBackup, CancellationToken.None); File.Move(temporaryBackup, BackupPath, overwrite: true); }
            }
            throw;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); if (File.Exists(temporaryBackup)) File.Delete(temporaryBackup); }
    }
    public async Task<string> RecoverFromBackupAsync(IReadOnlyList<SavedPassword> records, string newMasterPassword, CancellationToken token = default)
    {
        EncryptedVaultBackup.ValidateRecords(records); MasterVaultCipher.ValidatePassword(newMasterPassword);
        await gate.WaitAsync(token);
        try
        {
            if (!await IsMasterFileAsync(path, token)) throw new VaultException("protected_recovery_only");
            token.ThrowIfCancellationRequested();
            // Preserve encrypted originals; this operation never decrypts the forgotten-password file.
            var preserved = Path.GetFullPath(path) + ".before-recovery-" + Guid.NewGuid().ToString("N");
            File.Copy(path, preserved); if (File.Exists(BackupPath)) File.Copy(BackupPath, preserved + ".bak");
            await ReplaceProtectionAsync(null, newMasterPassword, true, token, new VaultDocument { Version = 1, Passwords = records.ToList() }, gateHeld: true);
            return preserved;
        }
        finally { gate.Release(); }
    }
}
