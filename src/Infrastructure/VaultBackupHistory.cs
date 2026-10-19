using System.Security.Cryptography;
using System.Text.Json;
using ArchivePassword.Core;

namespace ArchivePassword.Infrastructure;

public sealed record VaultBackupSnapshot(DateTimeOffset ExportedAt, Guid[] RecordIds, string ContentDigest)
{
    public static VaultBackupSnapshot Capture(IReadOnlyList<SavedPassword> records, DateTimeOffset now) =>
        new(now, records.Select(value => value.Id).ToArray(), Digest(records));
    public int AddedSince(IReadOnlyList<SavedPassword> records)
    { var included = RecordIds.ToHashSet(); return records.Count(value => !included.Contains(value.Id)); }
    public bool HasChanges(IReadOnlyList<SavedPassword> records) => ContentDigest != Digest(records);
    private static string Digest(IReadOnlyList<SavedPassword> records)
    {
        // A successful use is not an edit. Never persist this password-derived digest without DPAPI.
        var bytes = JsonSerializer.SerializeToUtf8Bytes(records.OrderBy(value => value.Id)
            .Select(value => value with { LastSuccessAt = null }));
        try { return Convert.ToHexString(SHA256.HashData(bytes)); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
}

public static class VaultBackupHistory
{
    public static async Task<VaultBackupSnapshot?> ReadAsync(string path)
    {
        if (!File.Exists(path)) return null;
        var decrypted = ProtectedData.Unprotect(await File.ReadAllBytesAsync(path), null, DataProtectionScope.CurrentUser);
        try
        {
            var value = JsonSerializer.Deserialize<VaultBackupSnapshot>(decrypted);
            if (value is null || value.RecordIds is null || value.ContentDigest is null || value.ContentDigest.Length != 64)
                throw new InvalidDataException("Invalid backup history.");
            return value;
        }
        finally { CryptographicOperations.ZeroMemory(decrypted); }
    }

    public static async Task SaveAsync(string path, VaultBackupSnapshot snapshot)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(snapshot);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var encrypted = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
            await File.WriteAllBytesAsync(temporary, encrypted);
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
