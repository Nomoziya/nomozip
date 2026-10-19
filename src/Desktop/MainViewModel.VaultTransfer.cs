using System.IO;
using ArchivePassword.Core;
using ArchivePassword.Infrastructure;

namespace ArchivePassword.Desktop;

public sealed partial class MainViewModel
{
    private async Task TransferVaultAsync(bool exporting)
    {
        IsBusy = true; idle = new TaskCompletionSource(); cancellation = new CancellationTokenSource(); Banner = "";
        var imported = false;
        try
        {
            var path = dialogs.SelectVaultBackup(exporting);
            if (path is null) return;
            if (exporting && new[] { Path.Combine(dataDirectory, "passwords.apv"), Path.Combine(dataDirectory, "passwords.apv.bak") }
                .Any(protectedPath => string.Equals(Path.GetFullPath(path), Path.GetFullPath(protectedPath), StringComparison.OrdinalIgnoreCase)))
            { Banner = UiText.Get("T_f8fcd8c32414"); return; }
            var passphrase = dialogs.AskBackupPassword(exporting, path);
            if (passphrase is null) return;
            if (exporting)
            {
                Footer = UiText.Get("T_77176f743984");
                var records = await vault.ReadAsync(cancellation.Token);
                await Task.Run(() => EncryptedVaultBackup.ExportAsync(path, records, passphrase, cancellation.Token));
                Banner = UiText.Format("T_229445d7c9f8", records.Count);
                await RememberVaultBackupAsync(records);
            }
            else
            {
                Footer = UiText.Get("T_20b5563e0a94");
                var incoming = await Task.Run(() => EncryptedVaultBackup.ReadAsync(path, passphrase, cancellation.Token));
                cancellation.Token.ThrowIfCancellationRequested();
                var existing = await vault.ReadAsync(cancellation.Token);
                var mode = dialogs.PreviewVaultImport(existing, incoming);
                if (mode is null) { Banner = UiText.Get("T_f9e7c5fdc973"); return; }
                var result = await vault.ImportAsync(incoming, mode.Value, cancellation.Token);
                imported = true;
                await RefreshVaultAsync();
                Banner = UiText.Format("T_ed470de7dfeb", result.Added, result.Skipped);
            }
        }
        catch (OperationCanceledException) { Banner = UiText.Get("T_83f46410c4f4"); }
        catch (VaultTransferException exception)
        {
            Banner = exception.Code switch
            {
                "backup_password_or_damage" => UiText.Get("T_f5d4d2b687bd"),
                "unsupported_backup_version" => UiText.Get("T_027d5093d92e"),
                "short_backup_password" => UiText.Get("T_652879ddcefd"),
                "backup_too_large" => UiText.Get("T_1b44be05dd6e"),
                _ => UiText.Get("T_9f07f55881a9")
            };
        }
        catch (Exception exception) when (exception is VaultException or IOException or UnauthorizedAccessException)
        { Banner = imported ? UiText.Get("T_ecb9f0adb4e4") : exporting ? UiText.Get("T_8feb2108c770") : UiText.Get("T_4922bfc42b52"); }
        finally
        {
            Footer = UiText.Get("T_619d8d493bd0");
            cancellation.Dispose(); cancellation = null; IsBusy = false; idle.TrySetResult();
        }
    }
}
