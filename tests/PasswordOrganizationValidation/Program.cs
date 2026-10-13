using System.Security.Cryptography;
using System.Buffers.Binary;
using System.Text.Json;
using ArchivePassword.Core;
using ArchivePassword.Infrastructure;

var root = Path.GetFullPath(args[0]);
var fixture = Path.Combine(root, "artifacts/password-organization", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(fixture);
var checks = new Dictionary<string, bool>();
void Check(string name, bool passed) { checks[name] = passed; Console.WriteLine($"{(passed ? "PASS" : "FAIL")} {name}"); if (!passed) throw new Exception(name); }
var path = Path.Combine(fixture, "synthetic.apv");
var vault = new DpapiPasswordVault(path);
var created = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
var keep = new SavedPassword(Guid.NewGuid(), "保留记录", "synthetic shared secret", "keep notes", true, created.AddDays(3), null, "照片", "example.test", "Trip");
var duplicate = keep with { Id = Guid.NewGuid(), Name = "删除记录", Group = "其它组", Source = "other.example", MatchText = "Other", AutoTry = false, CreatedAt = created };
var another = keep with { Id = Guid.NewGuid(), Secret = "another synthetic secret", MatchText = "", Group = "", Source = "" };
try
{
    // Construct the pre-B2 APV1 format independently, with no new metadata fields.
    var oldId = Guid.NewGuid();
    var oldJson = JsonSerializer.SerializeToUtf8Bytes(new { version = 1, passwords = new[] { new { id = oldId, name = "old name", secret = "old synthetic secret", notes = "old notes", autoTry = true, createdAt = created, lastSuccessAt = (DateTimeOffset?)null } }, associations = new Dictionary<string, Guid> { [Path.GetFullPath(Path.Combine(fixture, "old.zip")).ToUpperInvariant()] = oldId } });
    await File.WriteAllBytesAsync(path, "APV1"u8.ToArray().Concat(ProtectedData.Protect(oldJson, null, DataProtectionScope.CurrentUser)).ToArray());
    CryptographicOperations.ZeroMemory(oldJson);
    var before = await File.ReadAllBytesAsync(path);
    var legacy = (await vault.ReadAsync()).Single();
    Check("old vault defaults metadata and preserves original bytes", legacy.Group == "" && legacy.Source == "" && legacy.MatchText == "" && Enumerable.SequenceEqual(before, await File.ReadAllBytesAsync(path)));
    Check("old archive association survives read", await vault.GetAssociatedPasswordAsync(Path.Combine(fixture, "old.zip")) == oldId);
    var legacyBackup = Path.Combine(fixture, "legacy.nvault");
    await WriteLegacyBackup(legacyBackup, legacy);
    Check("old portable backup without metadata imports with empty defaults", (await EncryptedVaultBackup.ReadAsync(legacyBackup, "synthetic old backup password")).Single() == legacy);
    await vault.SaveAsync(legacy with { Group = "旧记录", Source = "archive.example", MatchText = "old" });
    Check("old vault upgrade preserves secret identity and association", (await vault.ReadAsync()).Single().Id == oldId && await vault.GetAssociatedPasswordAsync(Path.Combine(fixture, "old.zip")) == oldId);
    await vault.SaveAsync(keep); await vault.SaveAsync(duplicate); await vault.SaveAsync(another);
    await vault.RecordSuccessAsync(keep.Id, Path.Combine(fixture, "keep.7z"));
    await vault.RecordSuccessAsync(duplicate.Id, Path.Combine(fixture, "duplicate.zip"));
    var records = await vault.ReadAsync(); keep = records.Single(value => value.Id == keep.Id); duplicate = records.Single(value => value.Id == duplicate.Id);
    Check("reopen retains group source and rule", (await new DpapiPasswordVault(path).ReadAsync()).SequenceEqual(records));
    var provider = new PasswordCandidateProvider(vault);
    var candidates = await provider.GetAsync(Path.Combine(fixture, "TRIP.7z"));
    Check("filename rule prioritizes case-insensitively", candidates[0].SavedId == keep.Id);
    Check("nonmatching candidates retained and identical secrets deduplicated", candidates.Count == 3 && candidates.Any(value => value.SavedId == another.Id));
    await vault.RecordSuccessAsync(another.Id, Path.Combine(fixture, "trip-associated.zip"));
    Check("real association outranks filename rule", (await provider.GetAsync(Path.Combine(fixture, "trip-associated.zip")))[0].SavedId == another.Id);
    Check("matching uses filename not parent directories", !PasswordOrganization.MatchesArchive(keep, Path.Combine(fixture, "trip", "data.zip")));
    Check("empty rule does not force priority", !PasswordOrganization.MatchesArchive(another, "trip.zip"));
    Check("search finds metadata without searching secret", new[] { "照片", "EXAMPLE.TEST", "trip", "keep notes" }.All(query => PasswordOrganization.MatchesSearch(keep, query)) && !PasswordOrganization.MatchesSearch(keep, keep.Secret));
    Check("metadata boundaries accepted", PasswordOrganization.ValidMetadata(keep with { Group = new('a', 128), Source = new('b', 512), MatchText = new('c', 256) }));
    before = await File.ReadAllBytesAsync(path);
    try { await vault.SaveAsync(keep with { Group = new('a', 129) }); Check("oversized metadata rejected before write", false); }
    catch (ArgumentException) { Check("oversized metadata rejected before write", Enumerable.SequenceEqual(before, await File.ReadAllBytesAsync(path))); }
    try { await vault.MergeDuplicatesAsync(keep.Id, [another.Id]); Check("different passwords cannot merge", false); }
    catch (VaultException) { Check("different passwords cannot merge", Enumerable.SequenceEqual(before, await File.ReadAllBytesAsync(path))); }
    try { await vault.MergeDuplicatesAsync(keep.Id, [Guid.NewGuid()]); Check("stale merge cannot remove other records", false); }
    catch (VaultException) { Check("stale merge cannot remove other records", Enumerable.SequenceEqual(before, await File.ReadAllBytesAsync(path))); }
    try { await vault.MergeDuplicatesAsync(keep.Id, [keep.Id]); Check("keeper cannot be removed", false); }
    catch (VaultException) { Check("keeper cannot be removed", Enumerable.SequenceEqual(before, await File.ReadAllBytesAsync(path))); }
    Check("empty merge does not rewrite vault", await vault.MergeDuplicatesAsync(keep.Id, []) == 0 && Enumerable.SequenceEqual(before, await File.ReadAllBytesAsync(path)));
    using (var cancel = new CancellationTokenSource())
    {
        cancel.Cancel();
        try { await vault.MergeDuplicatesAsync(keep.Id, [duplicate.Id], cancel.Token); Check("cancelled merge leaves vault unchanged", false); }
        catch (OperationCanceledException) { Check("cancelled merge leaves vault unchanged", Enumerable.SequenceEqual(before, await File.ReadAllBytesAsync(path))); }
    }
    using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
    {
        try { await vault.MergeDuplicatesAsync(keep.Id, [duplicate.Id]); Check("failed atomic replacement preserves vault and associations", false); }
        catch (IOException) { Check("failed atomic replacement preserves vault and associations", Enumerable.SequenceEqual(before, await File.ReadAllBytesAsync(path)) && await vault.GetAssociatedPasswordAsync(Path.Combine(fixture, "duplicate.zip")) == duplicate.Id); }
    }
    Check("failed merge removes temporary files", !Directory.EnumerateFiles(fixture, "*.tmp-*").Any());
    Check("explicit duplicate merge removes exactly requested records", await vault.MergeDuplicatesAsync(keep.Id, [duplicate.Id]) == 1 && (await vault.ReadAsync()).Count == 3);
    var merged = (await vault.ReadAsync()).Single(value => value.Id == keep.Id);
    Check("merge retains chosen metadata and earliest creation latest success", merged == keep with { CreatedAt = duplicate.CreatedAt, LastSuccessAt = duplicate.LastSuccessAt > keep.LastSuccessAt ? duplicate.LastSuccessAt : keep.LastSuccessAt });
    Check("merge migrates removed record association and preserves keeper association", await vault.GetAssociatedPasswordAsync(Path.Combine(fixture, "duplicate.zip")) == keep.Id && await vault.GetAssociatedPasswordAsync(Path.Combine(fixture, "keep.7z")) == keep.Id);
    Check("unrelated association remains unchanged", await vault.GetAssociatedPasswordAsync(Path.Combine(fixture, "old.zip")) == oldId && await vault.GetAssociatedPasswordAsync(Path.Combine(fixture, "trip-associated.zip")) == another.Id);
    Check("atomic merge backup retains premerge bytes", Enumerable.SequenceEqual(before, await File.ReadAllBytesAsync(path + ".bak")));
    await vault.RestoreBackupAsync();
    Check("restore undo recovers duplicates and their associations", (await vault.ReadAsync()).Any(value => value.Id == duplicate.Id) && await vault.GetAssociatedPasswordAsync(Path.Combine(fixture, "duplicate.zip")) == duplicate.Id);
    var backup = Path.Combine(fixture, "synthetic.nvault");
    await EncryptedVaultBackup.ExportAsync(backup, await vault.ReadAsync(), "synthetic transfer password 2026");
    var imported = await EncryptedVaultBackup.ReadAsync(backup, "synthetic transfer password 2026");
    Check("portable encrypted backup retains new metadata", imported.SequenceEqual(await vault.ReadAsync()));
    var target = new DpapiPasswordVault(Path.Combine(fixture, "target.apv"));
    await target.ImportAsync(imported, PasswordImportMode.KeepBoth);
    Check("import preserves new metadata", (await target.ReadAsync()).SequenceEqual(imported));
    var invalid = keep with { Source = null! };
    try { await target.ImportAsync([invalid], PasswordImportMode.KeepBoth); Check("invalid backup metadata rejected", false); }
    catch (VaultTransferException) { Check("invalid backup metadata rejected", (await target.ReadAsync()).Count == imported.Count); }
    var cipher = System.Text.Encoding.UTF8.GetString(await File.ReadAllBytesAsync(backup));
    Check("export does not expose source group or rule", !cipher.Contains(keep.Group) && !cipher.Contains(keep.Source) && !cipher.Contains(keep.Secret));
}


finally
{
    await File.WriteAllTextAsync(Path.Combine(root, "artifacts/password-organization-validation.json"), JsonSerializer.Serialize(new { passed = checks.Count > 0 && checks.Values.All(value => value), total = checks.Count, checks, syntheticData = true }, new JsonSerializerOptions { WriteIndented = true }));
}

static async Task WriteLegacyBackup(string path, SavedPassword value)
{
    // Independent v1 fixture, intentionally omitting all organization metadata.
    var plain = JsonSerializer.SerializeToUtf8Bytes(new { version = 1, passwords = new[] { new { id = value.Id, name = value.Name, secret = value.Secret, notes = value.Notes, autoTry = value.AutoTry, createdAt = value.CreatedAt, lastSuccessAt = value.LastSuccessAt } } });
    var bytes = new byte[60 + plain.Length]; "NOMOVLT\0"u8.CopyTo(bytes);
    BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8, 4), 1); BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(12, 4), 600_000);
    RandomNumberGenerator.Fill(bytes.AsSpan(16, 28));
    var key = Rfc2898DeriveBytes.Pbkdf2("synthetic old backup password", bytes.AsSpan(16, 16), 600_000, HashAlgorithmName.SHA256, 32);
    try
    {
        using var aes = new AesGcm(key, 16); aes.Encrypt(bytes.AsSpan(32, 12), plain, bytes.AsSpan(60), bytes.AsSpan(44, 16), bytes.AsSpan(0, 44));
        await File.WriteAllBytesAsync(path, bytes);
    }
    finally { CryptographicOperations.ZeroMemory(plain); CryptographicOperations.ZeroMemory(key); }
}