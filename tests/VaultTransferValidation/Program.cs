using System.Diagnostics;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using ArchivePassword.Core;
using ArchivePassword.Infrastructure;

// Every credential in this executable is an artificial acceptance fixture.
var root = Path.GetFullPath(args[0]);
var mode = args.Length > 1 ? args[1] : "local";
var fixture = args.Length > 2 ? Path.GetFullPath(args[2]) : Path.Combine(root, "artifacts/vault-transfer", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(fixture);
var report = new Dictionary<string, bool>();
void Check(string name, bool passed)
{
    report[name] = passed;
    Console.WriteLine($"{(passed ? "PASS" : "FAIL")} {name}");
    if (!passed) throw new Exception(name);
}
var passphrase = "虚构的迁移口令 2026 $ '&";
var record = new SavedPassword(Guid.Parse("d9101152-45d1-4d48-a327-332188ee0fba"), "synthetic transfer label", "测试密碼 ' $ & 空格", "synthetic notes", true, DateTimeOffset.Parse("2026-01-02T03:04:05Z"), null, "合成迁移分组", "synthetic.example", "header");
var sourcePath = Path.Combine(fixture, "source.apv");
var backupPath = Path.Combine(fixture, "synthetic.nvault");
var original = new DpapiPasswordVault(sourcePath);
try
{
    if (mode == "export")
    {
        await original.SaveAsync(record);
        await original.RecordSuccessAsync(record.Id, Path.Combine(fixture, "source-only-archive.7z"));
        await EncryptedVaultBackup.ExportAsync(backupPath, await original.ReadAsync(), passphrase);
        await File.WriteAllTextAsync(Path.Combine(fixture, "source-identity.json"), JsonSerializer.Serialize(new
        { machine = Environment.MachineName, sid = WindowsIdentity.GetCurrent().User!.Value }));
        Check("source Windows identity exported synthetic encrypted fixtures", File.Exists(backupPath));
    }
    else if (mode == "cross-import")
    {
        using var identity = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(fixture, "source-identity.json")));
        Check("independent Windows identity or machine", identity.RootElement.GetProperty("machine").GetString() != Environment.MachineName
            || identity.RootElement.GetProperty("sid").GetString() != WindowsIdentity.GetCurrent().User!.Value);
        try { await original.ReadAsync(); Check("source DPAPI vault rejected by independent user", false); }
        catch (VaultException error) { Check("source DPAPI vault rejected by independent user", error.Code == "vault_unreadable"); }
        var portable = await EncryptedVaultBackup.ReadAsync(backupPath, passphrase);
        var destination = new DpapiPasswordVault(Path.Combine(fixture, "destination.apv"));
        var result = await destination.ImportAsync(portable, PasswordImportMode.SkipDuplicates);
        Check("encrypted backup imported on independent Windows machine", result.Added == 1 && (await destination.ReadAsync()).Single().Secret == record.Secret);
        var restored = await new DpapiPasswordVault(Path.Combine(fixture, "destination.apv")).ReadAsync();
        Check("destination DPAPI vault reopens with local protection", restored.Single() == portable.Single());
        var worker = Path.Combine(root, "artifacts/worker/ArchiveEngineWorker.exe");
        if (!File.Exists(worker)) throw new Exception("Cross-machine acceptance requires the official extraction engine.");
        var engine = new ArchiveEngineClient(worker, Path.Combine(root, "engines/26.04/7z.dll"));
        var extraction = new ExtractionService(engine, destination, new PasswordCandidateProvider(destination));
        var output = Path.Combine(fixture, "extracted");
        var extracted = await extraction.ExtractAsync(Path.Combine(root, "tests/fixtures/generated/encrypted-header.7z"), output, true,
            _ => throw new Exception("Imported password was not reused"), _ => Task.FromResult(ExistingFilePolicy.Replace));
        Check("migrated password automatically decrypts actual archive", extracted.Engine.IsSuccess
            && File.ReadAllBytes(Path.Combine(output, "中文示例.txt")).SequenceEqual(File.ReadAllBytes(Path.Combine(root, "tests/fixtures/generated/中文示例.txt"))));
    }
    else
    {
        await original.SaveAsync(record);
        await original.RecordSuccessAsync(record.Id, "C:/synthetic-source/archive.7z");
        var disabled = record with { Id = Guid.NewGuid(), Secret = "synthetic disabled secret", Name = "disabled", AutoTry = false };
        await original.SaveAsync(disabled);
        var before = await File.ReadAllBytesAsync(sourcePath);
        var records = await original.ReadAsync();
        var watch = Stopwatch.StartNew();
        await EncryptedVaultBackup.ExportAsync(backupPath, records, passphrase);
        Console.WriteLine($"Export KDF and file duration: {watch.ElapsedMilliseconds} ms");
        Check("export preserves source vault bytes and associations", Enumerable.SequenceEqual(before, await File.ReadAllBytesAsync(sourcePath))
            && await original.GetAssociatedPasswordAsync("C:/synthetic-source/archive.7z") == record.Id);
        var bytes = await File.ReadAllBytesAsync(backupPath);
        var text = Encoding.UTF8.GetString(bytes);
        Check("export encrypts names notes passwords and archive metadata", !text.Contains(record.Secret) && !text.Contains(record.Name) && !text.Contains(record.Notes));
        var recovered = await EncryptedVaultBackup.ReadAsync(backupPath, passphrase);
        Check("Unicode passphrase roundtrip retains all record fields", recovered.SequenceEqual(records));
        var another = Path.Combine(fixture, "different.nvault");
        await EncryptedVaultBackup.ExportAsync(another, records, passphrase);
        Check("fresh exports use different salt nonce and ciphertext", !Enumerable.SequenceEqual(bytes, await File.ReadAllBytesAsync(another)));
        var targetPath = Path.Combine(fixture, "target.apv");
        var target = new DpapiPasswordVault(targetPath);
        var local = record with { Name = "keep local metadata", Notes = "keep local notes", AutoTry = false };
        await target.SaveAsync(local); await target.RecordSuccessAsync(local.Id, "C:/synthetic-target/archive.7z");
        var localBefore = (await target.ReadAsync()).Single();
        var preview = PasswordImportPlanner.Build(await target.ReadAsync(), recovered, PasswordImportMode.SkipDuplicates);
        Check("preview identifies duplicate secret without writing", preview.Added == 1 && preview.Skipped == 1 && (await target.ReadAsync()).Count == 1);
        var imported = await target.ImportAsync(recovered, PasswordImportMode.SkipDuplicates);
        Check("default merge keeps existing metadata and associations", imported.Added == 1 && (await target.ReadAsync()).Contains(localBefore)
            && await target.GetAssociatedPasswordAsync("C:/synthetic-target/archive.7z") == local.Id);
        Check("source-only path association not migrated", await target.GetAssociatedPasswordAsync("C:/synthetic-source/archive.7z") is null);
        var priorBytes = await File.ReadAllBytesAsync(targetPath);
        Check("repeat import adds nothing", (await target.ImportAsync(recovered, PasswordImportMode.SkipDuplicates)).Added == 0);
        Check("all-duplicate import does not rewrite vault", Enumerable.SequenceEqual(priorBytes, await File.ReadAllBytesAsync(targetPath)));
        var keep = await target.ImportAsync(recovered, PasswordImportMode.KeepBoth);
        var all = await target.ReadAsync();
        Check("keep both adds collision-free IDs", keep.Added == 2 && all.Count == 4 && all.Select(value => value.Id).Distinct().Count() == 4 && all.Contains(localBefore));
        Check("disabled record preserved on import", all.Count(value => value.Secret == disabled.Secret && !value.AutoTry) == 2);
        var collision = disabled with { Id = record.Id, Secret = "different secret with same imported id" };
        Check("distinct secret with conflicting ID never replaces original", (await target.ImportAsync([collision], PasswordImportMode.SkipDuplicates)).Added == 1
            && (await target.ReadAsync()).Contains(localBefore));
        var internalDuplicates = PasswordImportPlanner.Build([], [record, record with { Id = Guid.NewGuid() }], PasswordImportMode.SkipDuplicates);
        Check("duplicates within backup also skipped", internalDuplicates.Added == 1 && internalDuplicates.Skipped == 1);
        before = await File.ReadAllBytesAsync(targetPath);
        async Task Reject(string name, string path, string password, string expected)
        {
            try { await EncryptedVaultBackup.ReadAsync(path, password); Check(name, false); }
            catch (VaultTransferException error) { Check(name, error.Code == expected && Enumerable.SequenceEqual(before, await File.ReadAllBytesAsync(targetPath))); }
        }
        await Reject("wrong passphrase leaves existing vault intact", backupPath, "wrong synthetic passphrase", "backup_password_or_damage");
        foreach (var offset in new[] { 16, 32, 44, 60, bytes.Length - 1 })
        {
            var damaged = bytes.ToArray(); damaged[offset] ^= 1;
            var corruptedPath = Path.Combine(fixture, "damaged-" + offset + ".nvault");
            await File.WriteAllBytesAsync(corruptedPath, damaged);
            await Reject("authenticated corruption rejected at " + offset, corruptedPath, passphrase, "backup_password_or_damage");
        }
        var unsupported = bytes.ToArray(); unsupported[8] = 2;
        await File.WriteAllBytesAsync(another, unsupported);
        await Reject("future backup version rejected without mutation", another, passphrase, "unsupported_backup_version");
        var invalidCost = bytes.ToArray(); invalidCost[12] ^= 1;
        await File.WriteAllBytesAsync(another, invalidCost);
        await Reject("altered KDF parameters rejected before expensive derivation", another, passphrase, "invalid_backup_format");
        await File.WriteAllBytesAsync(another, bytes[..30]);
        await Reject("truncated export rejected", another, passphrase, "invalid_backup_format");
        await Reject("local DPAPI vault is not a portable backup", sourcePath, passphrase, "invalid_backup_format");
        using (var cancelled = new CancellationTokenSource())
        {
            cancelled.Cancel();
            try { await target.ImportAsync(recovered, PasswordImportMode.KeepBoth, cancelled.Token); Check("cancelled import retains old vault", false); }
            catch (OperationCanceledException) { Check("cancelled import retains old vault", Enumerable.SequenceEqual(before, await File.ReadAllBytesAsync(targetPath))); }
            try { await EncryptedVaultBackup.ExportAsync(backupPath, recovered, passphrase, cancelled.Token); Check("cancelled export retains old backup", false); }
            catch (OperationCanceledException) { Check("cancelled export retains old backup", Enumerable.SequenceEqual(bytes, await File.ReadAllBytesAsync(backupPath))); }
        }
        try { await target.ImportAsync([record with { Secret = "" }], PasswordImportMode.KeepBoth); Check("invalid record never mutates target", false); }
        catch (VaultTransferException) { Check("invalid record never mutates target", Enumerable.SequenceEqual(before, await File.ReadAllBytesAsync(targetPath))); }
        try { await EncryptedVaultBackup.ExportAsync(backupPath, recovered, "short"); Check("short export password keeps prior file", false); }
        catch (VaultTransferException) { Check("short export password keeps prior file", Enumerable.SequenceEqual(bytes, await File.ReadAllBytesAsync(backupPath))); }
        var protectedDirectory = Path.Combine(fixture, "unwritable-target"); Directory.CreateDirectory(protectedDirectory);
        var failureTarget = new DpapiPasswordVault(protectedDirectory);
        try { await failureTarget.ImportAsync(recovered, PasswordImportMode.KeepBoth); Check("failed commit leaves existing data intact", false); }
        catch (IOException) { Check("failed commit leaves existing data intact", Directory.Exists(protectedDirectory) && Enumerable.SequenceEqual(before, await File.ReadAllBytesAsync(targetPath))); }
        Check("failed writes clean temporary files", !Directory.EnumerateFiles(fixture, "*.tmp-*").Any());
        using (var locked = new FileStream(targetPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            try { await target.ImportAsync(recovered, PasswordImportMode.KeepBoth); Check("locked existing vault survives failed atomic commit", false); }
            catch (IOException) { Check("locked existing vault survives failed atomic commit", Enumerable.SequenceEqual(before, await File.ReadAllBytesAsync(targetPath))); }
        }
        using (var locked = new FileStream(backupPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            try { await EncryptedVaultBackup.ExportAsync(backupPath, [], passphrase); Check("locked old export survives failed replacement", false); }
            catch (IOException) { Check("locked old export survives failed replacement", Enumerable.SequenceEqual(bytes, await File.ReadAllBytesAsync(backupPath))); }
        }
        Check("failed replacement cleans encrypted temporary files", !Directory.EnumerateFiles(fixture, "*.tmp-*").Any());
        var malformed = Path.Combine(fixture, "oversized.nvault");
        await using (var oversized = File.Create(malformed)) oversized.SetLength(EncryptedVaultBackup.MaxFileBytes + 1L);
        await Reject("oversized backup rejected before allocation", malformed, passphrase, "invalid_backup_format");
        var empty = Path.Combine(fixture, "empty.nvault");
        await EncryptedVaultBackup.ExportAsync(empty, [], passphrase);
        Check("empty backup handled without changing local vault", (await EncryptedVaultBackup.ReadAsync(empty, passphrase)).Count == 0);
    }
}
finally
{
    await File.WriteAllTextAsync(Path.Combine(root, "artifacts/vault-transfer-" + mode + "-validation.json"), JsonSerializer.Serialize(new
    { passed = report.Count > 0 && report.Values.All(value => value), total = report.Count, checks = report }, new JsonSerializerOptions { WriteIndented = true }));
}
