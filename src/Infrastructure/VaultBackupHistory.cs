using System.Security.Cryptography;
using System.Text.Json;
using ArchivePassword.Core;

namespace ArchivePassword.Infrastructure;

public sealed record VaultBackupSnapshot(DateTimeOffset ExportedAt, Guid[] RecordIds, string ContentStamp)
{
    public static VaultBackupSnapshot Capture(IReadOnlyList<SavedPassword> records, DateTimeOffset now, string contentStamp) =>
        new(now, records.Select(value => value.Id).ToArray(), contentStamp);
    public int AddedSince(IReadOnlyList<SavedPassword> records)
    { var included = RecordIds.ToHashSet(); return records.Count(value => !included.Contains(value.Id)); }
    public bool HasChanges(IReadOnlyList<SavedPassword> records, string contentStamp) =>
        ContentStamp != contentStamp || !RecordIds.ToHashSet().SetEquals(records.Select(value => value.Id));
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
            if (value is null || value.RecordIds is null || string.IsNullOrEmpty(value.ContentStamp))
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
