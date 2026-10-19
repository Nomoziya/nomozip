using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using ArchivePassword.Core;
using ArchivePassword.Infrastructure;

namespace ArchivePassword.Desktop;

public sealed partial class MainViewModel
{
    private VaultBackupSnapshot? vaultBackupSnapshot;
    private bool vaultBackupHistoryUnavailable;
    private string VaultBackupHistoryPath => Path.Combine(dataDirectory, "vault-backup-history.apb");
    public string VaultBackupSummary
    {
        get
        {
            if (VaultLocked || !vaultAvailable) return UiText.Get("BackupReminder.Unlock");
            if (allPasswords.Count == 0) return UiText.Get("BackupReminder.Empty");
            if (vaultBackupHistoryUnavailable) return UiText.Get("BackupReminder.Unavailable");
            if (vaultBackupSnapshot is null) return UiText.Get("BackupReminder.Never");
            var time = vaultBackupSnapshot.ExportedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
            var added = vaultBackupSnapshot.AddedSince(allPasswords);
            return added > 0 ? UiText.Format("BackupReminder.Added", time, added) :
                vaultBackupSnapshot.HasChanges(allPasswords) ? UiText.Format("BackupReminder.Changed", time) :
                UiText.Format("BackupReminder.Current", time);
        }
    }
    public bool VaultBackupReminderNeeded => !VaultLocked && vaultAvailable && allPasswords.Count > 0 &&
        (vaultBackupSnapshot is null || vaultBackupHistoryUnavailable || vaultBackupSnapshot.HasChanges(allPasswords) ||
        DateTimeOffset.UtcNow - vaultBackupSnapshot.ExportedAt >= TimeSpan.FromDays(30));
    private void NotifyBackupReminder()
    { Changed(nameof(VaultBackupSummary)); Changed(nameof(VaultBackupReminderNeeded)); }
    private async Task RefreshBackupReminderAsync()
    {
        try { vaultBackupSnapshot = await VaultBackupHistory.ReadAsync(VaultBackupHistoryPath); vaultBackupHistoryUnavailable = false; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or CryptographicException or JsonException)
        { vaultBackupSnapshot = null; vaultBackupHistoryUnavailable = true; }
        NotifyBackupReminder();
    }
    private async Task RememberVaultBackupAsync(IReadOnlyList<SavedPassword> records)
    {
        var snapshot = VaultBackupSnapshot.Capture(records, DateTimeOffset.UtcNow);
        try
        {
            await VaultBackupHistory.SaveAsync(VaultBackupHistoryPath, snapshot);
            vaultBackupSnapshot = snapshot; vaultBackupHistoryUnavailable = false;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or CryptographicException)
        { vaultBackupHistoryUnavailable = true; Banner += "\n" + UiText.Get("BackupReminder.SaveFailed"); }
        NotifyBackupReminder();
    }
}
