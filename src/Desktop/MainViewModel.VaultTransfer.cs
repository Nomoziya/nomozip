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
            { Banner = "请选择另一个文件名，不能覆盖本机密码库或恢复备份。"; return; }
            var passphrase = dialogs.AskBackupPassword(exporting, path);
            if (passphrase is null) return;
            if (exporting)
            {
                Footer = "正在生成加密密码备份…";
                var records = await vault.ReadAsync(cancellation.Token);
                await Task.Run(() => EncryptedVaultBackup.ExportAsync(path, records, passphrase, cancellation.Token));
                Banner = $"已导出 {records.Count} 条密码的加密备份。导入时需要备份密码。";
            }
            else
            {
                Footer = "正在打开加密备份…";
                var incoming = await Task.Run(() => EncryptedVaultBackup.ReadAsync(path, passphrase, cancellation.Token));
                cancellation.Token.ThrowIfCancellationRequested();
                var existing = await vault.ReadAsync(cancellation.Token);
                var mode = dialogs.PreviewVaultImport(existing, incoming);
                if (mode is null) { Banner = "已取消导入，密码库保持不变。"; return; }
                var result = await vault.ImportAsync(incoming, mode.Value, cancellation.Token);
                imported = true;
                await RefreshVaultAsync();
                Banner = $"导入完成：新增 {result.Added} 条，跳过 {result.Skipped} 条。原有密码已保留。";
            }
        }
        catch (OperationCanceledException) { Banner = "已取消，密码库保持不变。"; }
        catch (VaultTransferException exception)
        {
            Banner = exception.Code switch
            {
                "backup_password_or_damage" => "备份密码不正确，或备份文件已损坏。密码库保持不变。",
                "unsupported_backup_version" => "此备份版本暂不支持，请使用支持该版本的 nomozip。密码库保持不变。",
                "short_backup_password" => "备份密码至少需要 12 个字符。",
                "backup_too_large" => "备份数据超过支持范围，未覆盖原备份。",
                _ => "不是有效的 nomozip 加密备份，或记录无法读取。密码库保持不变。"
            };
        }
        catch (Exception exception) when (exception is VaultException or IOException or UnauthorizedAccessException)
        { Banner = imported ? "导入已经保存，但列表未能刷新。请关闭后重新打开密码库。" : exporting ? "备份未能导出，请检查保存位置。密码库保持不变。" : "备份未能导入，请检查文件与权限。密码库保持不变。"; }
        finally
        {
            Footer = "密码备份与导入操作已结束。";
            cancellation.Dispose(); cancellation = null; IsBusy = false; idle.TrySetResult();
        }
    }
}
