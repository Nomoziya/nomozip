using System.Security.Cryptography;
using System.Text.Json;
using ArchivePassword.Core;

namespace ArchivePassword.Infrastructure;

public sealed partial class DpapiPasswordVault(string path) : IPasswordVault, IPasswordOrganizationVault, IPasswordVaultProtection, IPasswordVaultBackupState
{
    private static readonly byte[] Magic = "APV1"u8.ToArray();
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
    private readonly SemaphoreSlim gate = new(1, 1);
    private string BackupPath => path + ".bak";

    public Task<IReadOnlyList<SavedPassword>> ReadAsync(CancellationToken cancellationToken = default) => WithDocumentAsync<IReadOnlyList<SavedPassword>>(document => document.Passwords.ToArray(), false, cancellationToken);
    public Task<VaultBackupState> ReadBackupStateAsync(CancellationToken cancellationToken = default) =>
        WithDocumentAsync(document => new VaultBackupState(document.Passwords.ToArray(), document.ContentStamp), false, cancellationToken);
    public Task<Guid?> GetAssociatedPasswordAsync(string archivePath, CancellationToken cancellationToken = default) => WithDocumentAsync<Guid?>(document => document.Associations.TryGetValue(Path.GetFullPath(archivePath).ToUpperInvariant(), out var id) ? id : null, false, cancellationToken);

    public async Task<PasswordImportPlan> ImportAsync(IReadOnlyList<SavedPassword> passwords, PasswordImportMode mode, CancellationToken cancellationToken = default)
    {
        EncryptedVaultBackup.ValidateRecords(passwords);
        await gate.WaitAsync(cancellationToken);
        try
        {
            var document = File.Exists(path) ? await LoadAsync(path, cancellationToken) : new VaultDocument { Version = 1 };
            var plan = PasswordImportPlanner.Build(document.Passwords, passwords, mode);
            if (plan.Added > 0)
            {
                document.Passwords.AddRange(plan.Additions);
                document.ContentStamp = Guid.NewGuid().ToString("N");
                await SaveDocumentAsync(document, cancellationToken);
            }
            return plan;
        }
        finally { gate.Release(); }
    }

    public Task SaveAsync(SavedPassword password, CancellationToken cancellationToken = default) => WithDocumentAsync<object?>(document =>
    {
        if (string.IsNullOrEmpty(password.Secret)) throw new ArgumentException("Password cannot be empty.", nameof(password));
        if (!PasswordOrganization.ValidMetadata(password)) throw new ArgumentException("Invalid password metadata.", nameof(password));
        var existing = document.Passwords.FindIndex(value => value.Id == password.Id);
        if (existing < 0 || (document.Passwords[existing] with { LastSuccessAt = null }) != (password with { LastSuccessAt = null }))
            document.ContentStamp = Guid.NewGuid().ToString("N");
        if (existing < 0) document.Passwords.Add(password);
        else document.Passwords[existing] = password;
        return null;
    }, true, cancellationToken);

    public async Task<int> MergeDuplicatesAsync(Guid keepId, IReadOnlyList<Guid> removeIds, CancellationToken token = default)
    {
        await gate.WaitAsync(token);
        try
        {
            var document = File.Exists(path) ? await LoadAsync(path, token) : new VaultDocument { Version = 1 };
            var ids = removeIds.ToHashSet();
            if (ids.Count == 0) return 0;
            var keepIndex = document.Passwords.FindIndex(value => value.Id == keepId);
            var others = document.Passwords.Where(value => ids.Contains(value.Id)).ToArray();
            if (keepIndex < 0 || ids.Contains(keepId) || others.Length != ids.Count)
                throw new VaultException("password_records_changed");
            var keep = document.Passwords[keepIndex];
            if (others.Any(value => !string.Equals(value.Secret, keep.Secret, StringComparison.Ordinal)))
                throw new VaultException("password_records_changed");
            document.Passwords[keepIndex] = PasswordOrganization.Merge(keep, others);
            document.Passwords.RemoveAll(value => ids.Contains(value.Id));
            document.ContentStamp = Guid.NewGuid().ToString("N");
            foreach (var key in document.Associations.Where(pair => ids.Contains(pair.Value)).Select(pair => pair.Key).ToArray())
                document.Associations[key] = keepId;
            await SaveDocumentAsync(document, token);
            return ids.Count;
        }
        finally { gate.Release(); }
    }

    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) => WithDocumentAsync<object?>(document =>
    {
        if (document.Passwords.RemoveAll(value => value.Id == id) > 0) document.ContentStamp = Guid.NewGuid().ToString("N");
        foreach (var key in document.Associations.Where(pair => pair.Value == id).Select(pair => pair.Key).ToArray()) document.Associations.Remove(key);
        return null;
    }, true, cancellationToken);

    public Task RecordSuccessAsync(Guid id, string archivePath, CancellationToken cancellationToken = default) => WithDocumentAsync<object?>(document =>
    {
        var index = document.Passwords.FindIndex(value => value.Id == id);
        if (index >= 0)
        {
            document.Passwords[index] = document.Passwords[index] with { LastSuccessAt = DateTimeOffset.UtcNow };
            document.Associations[Path.GetFullPath(archivePath).ToUpperInvariant()] = id;
        }
        return null;
    }, true, cancellationToken);

    public async Task RestoreBackupAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(BackupPath)) throw new VaultException("backup_missing");
            if (await IsMasterFileAsync(path, cancellationToken) && !await IsMasterFileAsync(BackupPath, cancellationToken))
                throw new VaultException("invalid_protected_vault");
            await LoadAsync(BackupPath, cancellationToken); // Validate before replacing anything.
            var temporary = path + ".restore-" + Guid.NewGuid().ToString("N");
            try
            {
                File.Copy(BackupPath, temporary);
                File.Move(temporary, path, overwrite: true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        finally { gate.Release(); }
    }

    private async Task<T> WithDocumentAsync<T>(Func<VaultDocument, T> change, bool save, CancellationToken token)
    {
        await gate.WaitAsync(token);
        try
        {
            var document = File.Exists(path) ? await LoadAsync(path, token) : new VaultDocument { Version = 1 };
            var result = change(document);
            if (save) await SaveDocumentAsync(document, token);
            return result;
        }
        finally { gate.Release(); }
    }

    private async Task<VaultDocument> LoadAsync(string source, CancellationToken token)
    {
        byte[]? plaintext = null;
        try
        {
            var bytes = await File.ReadAllBytesAsync(source, token);
            if (MasterVaultCipher.IsProtected(bytes))
            {
                if (masterKey is null || masterSalt is null) throw new VaultException("vault_locked");
                plaintext = MasterVaultCipher.Decrypt(bytes, masterKey, masterSalt);
            }
            else
            {
                if (masterKey is not null) throw new VaultException("vault_protection_changed");
                if (bytes.Length < Magic.Length || !bytes.AsSpan(0, Magic.Length).SequenceEqual(Magic)) throw new VaultException("unknown_vault_format", File.Exists(BackupPath));
                plaintext = ProtectedData.Unprotect(bytes.AsSpan(Magic.Length).ToArray(), null, DataProtectionScope.CurrentUser);
            }
            var document = JsonSerializer.Deserialize<VaultDocument>(plaintext, Options) ?? throw new JsonException();
            if (document.Version != 1) throw new VaultException("unsupported_vault_version", File.Exists(BackupPath));
            return document;
        }
        catch (Exception exception) when (exception is CryptographicException or JsonException)
        { throw new VaultException("vault_unreadable", File.Exists(BackupPath)); }
        finally { if (plaintext is not null) CryptographicOperations.ZeroMemory(plaintext); }
    }

    private async Task SaveDocumentAsync(VaultDocument document, CancellationToken token)
    {
        var plaintext = JsonSerializer.SerializeToUtf8Bytes(document, Options);
        byte[] encrypted;
        try { encrypted = masterKey is not null && masterSalt is not null ? MasterVaultCipher.Encrypt(plaintext, masterKey, masterSalt)
            : [.. Magic, .. ProtectedData.Protect(plaintext, null, DataProtectionScope.CurrentUser)]; }
        finally { CryptographicOperations.ZeroMemory(plaintext); }
        var destination = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temporary = destination + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            await using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                await file.WriteAsync(encrypted, token);
                await file.FlushAsync(token);
            }
            if (File.Exists(destination)) File.Replace(temporary, destination, BackupPath);
            else File.Move(temporary, destination);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private sealed class VaultDocument
    {
        // Random edit identity inside existing vault encryption; contains no password verifier.
        // Old documents omit it and remain read-only until an actual content change.
        public string ContentStamp { get; set; } = "legacy";
        public int Version { get; init; }
        public List<SavedPassword> Passwords { get; init; } = [];
        public Dictionary<string, Guid> Associations { get; init; } = [];
    }
}
