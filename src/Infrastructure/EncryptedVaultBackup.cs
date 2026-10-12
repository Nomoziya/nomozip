using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using ArchivePassword.Core;

namespace ArchivePassword.Infrastructure;

// NomoZip vault transfer v1: authenticated header + AES-256-GCM JSON payload.
// Uses only .NET cryptography; exported files never contain DPAPI-wrapped data.
public static class EncryptedVaultBackup
{
    private static readonly byte[] Magic = "NOMOVLT\0"u8.ToArray();
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
    private const int HeaderSize = 44, TagSize = 16, Iterations = 600_000;
    public const int MaxFileBytes = 16 * 1024 * 1024;

    public static void ValidateRecords(IReadOnlyList<SavedPassword> passwords)
    {
        if (passwords.Count > 10_000 || passwords.Any(value => value is null || value.Id == Guid.Empty
            || string.IsNullOrEmpty(value.Secret) || value.Secret.Length > 16_384
            || value.Name is null || value.Name.Length > 16_384 || value.Notes is null || value.Notes.Length > 65_536))
            throw new VaultTransferException("invalid_backup_records");
    }

    public static async Task ExportAsync(string destination, IReadOnlyList<SavedPassword> passwords, string passphrase, CancellationToken token = default)
    {
        if (passphrase.Length < 12) throw new VaultTransferException("short_backup_password");
        ValidateRecords(passwords);
        token.ThrowIfCancellationRequested();
        var plaintext = JsonSerializer.SerializeToUtf8Bytes(new BackupDocument(1, passwords), Options);
        byte[]? key = null;
        var path = Path.GetFullPath(destination);
        var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            if (plaintext.Length > MaxFileBytes - HeaderSize - TagSize) throw new VaultTransferException("backup_too_large");
            var bytes = new byte[HeaderSize + TagSize + plaintext.Length];
            Magic.CopyTo(bytes, 0);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8, 4), 1);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(12, 4), Iterations);
            RandomNumberGenerator.Fill(bytes.AsSpan(16, 28)); // 16-byte salt and 12-byte nonce.
            key = Rfc2898DeriveBytes.Pbkdf2(passphrase, bytes.AsSpan(16, 16), Iterations, HashAlgorithmName.SHA256, 32);
            token.ThrowIfCancellationRequested();
            using (var aes = new AesGcm(key, TagSize))
                aes.Encrypt(bytes.AsSpan(32, 12), plaintext, bytes.AsSpan(HeaderSize + TagSize), bytes.AsSpan(HeaderSize, TagSize), bytes.AsSpan(0, HeaderSize));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                await file.WriteAsync(bytes, token);
                await file.FlushAsync(token);
            }
            token.ThrowIfCancellationRequested();
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
            if (key is not null) CryptographicOperations.ZeroMemory(key);
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    public static async Task<IReadOnlyList<SavedPassword>> ReadAsync(string source, string passphrase, CancellationToken token = default)
    {
        await using var file = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);
        if (file.Length < HeaderSize + TagSize || file.Length > MaxFileBytes) throw new VaultTransferException("invalid_backup_format");
        var bytes = new byte[(int)file.Length];
        await file.ReadExactlyAsync(bytes, token);
        if (!bytes.AsSpan(0, Magic.Length).SequenceEqual(Magic)) throw new VaultTransferException("invalid_backup_format");
        if (BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(8, 4)) != 1) throw new VaultTransferException("unsupported_backup_version");
        // Version 1 fixes the KDF cost, bounding work even for an untrusted file.
        if (BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(12, 4)) != Iterations) throw new VaultTransferException("invalid_backup_format");
        byte[]? key = null;
        var plaintext = new byte[bytes.Length - HeaderSize - TagSize];
        try
        {
            key = Rfc2898DeriveBytes.Pbkdf2(passphrase, bytes.AsSpan(16, 16), Iterations, HashAlgorithmName.SHA256, 32);
            token.ThrowIfCancellationRequested();
            using (var aes = new AesGcm(key, TagSize))
                aes.Decrypt(bytes.AsSpan(32, 12), bytes.AsSpan(HeaderSize + TagSize), bytes.AsSpan(HeaderSize, TagSize), plaintext, bytes.AsSpan(0, HeaderSize));
            var document = JsonSerializer.Deserialize<BackupDocument>(plaintext, Options) ?? throw new JsonException();
            if (document.Version != 1) throw new VaultTransferException("unsupported_backup_version");
            if (document.Passwords is null) throw new JsonException();
            ValidateRecords(document.Passwords);
            return document.Passwords.ToArray();
        }
        catch (CryptographicException) { throw new VaultTransferException("backup_password_or_damage"); }
        catch (JsonException) { throw new VaultTransferException("invalid_backup_records"); }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
            if (key is not null) CryptographicOperations.ZeroMemory(key);
        }
    }

    private sealed record BackupDocument(int Version, IReadOnlyList<SavedPassword> Passwords);
}
