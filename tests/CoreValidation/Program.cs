using ArchivePassword.Core;
using ArchivePassword.Infrastructure;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

var root = Path.GetFullPath(args.Length == 1 ? args[0] : ".");
var testRoot = Path.Combine(root, "artifacts", "core-validation", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(testRoot);
var checks = new List<object>();
void Check(string name, bool condition)
{
    Console.WriteLine($"{(condition ? "PASS" : "FAIL")} {name}");
    checks.Add(new { name, passed = condition });
    if (!condition) throw new Exception("Failed: " + name);
}
var vaultPath = Path.Combine(testRoot, "passwords.apv");
var vault = new DpapiPasswordVault(vaultPath);
Check("empty vault", (await vault.ReadAsync()).Count == 0);
var first = new SavedPassword(Guid.NewGuid(), "synthetic private label", "incorrect sample password", "private sample notes", true, DateTimeOffset.UtcNow);
await vault.SaveAsync(first);
var blob = await File.ReadAllBytesAsync(vaultPath);
Check("all sensitive fields encrypted at rest", !Encoding.UTF8.GetString(blob).Contains(first.Secret) && !Encoding.UTF8.GetString(blob).Contains(first.Name) && !Encoding.UTF8.GetString(blob).Contains(first.Notes));
var reopened = new DpapiPasswordVault(vaultPath);
Check("new process-style instance can reopen DPAPI vault", (await reopened.ReadAsync()).Single().Secret == first.Secret);
var password = "测试密碼 ' $ & 空格";
var correct = new SavedPassword(Guid.NewGuid(), "archive password", password, "", true, DateTimeOffset.UtcNow);
await vault.SaveAsync(correct);
var archive = Path.Combine(root, "tests/fixtures/generated/encrypted-header.7z");
await vault.RecordSuccessAsync(correct.Id, archive);
var provider = new PasswordCandidateProvider(vault);
Check("association ordered first", (await provider.GetAsync(archive)).First().SavedId == correct.Id);
await vault.SaveAsync(correct with { AutoTry = false });
Check("excluded password not tried", (await provider.GetAsync(archive)).All(value => value.SavedId != correct.Id));
await vault.SaveAsync(correct);
var duplicate = correct with { Id = Guid.NewGuid(), Name = "same secret, other label" };
await vault.SaveAsync(duplicate);
Check("duplicate candidate deduplicated", (await provider.GetAsync(archive)).Count(value => value.Secret == password) == 1);
await vault.DeleteAsync(duplicate.Id);
Check("password deletion persists", (await new DpapiPasswordVault(vaultPath).ReadAsync()).Count == 2);

var engine = new ArchiveEngineClient(Path.Combine(root, "artifacts/worker/ArchiveEngineWorker.exe"), Path.Combine(root, "engines/26.04/7z.dll"));
var service = new ExtractionService(engine, vault, provider);
var promptCount = 0;
var output = Path.Combine(testRoot, "automatic-output");
var outcome = await service.ExtractAsync(archive, output, true,
    () => { promptCount++; return Task.FromResult<PasswordPromptResponse?>(null); },
    _ => Task.FromResult(ExistingFilePolicy.Replace));
var source = Path.Combine(root, "tests/fixtures/generated/中文示例.txt");
Check("automatic password reaches real extraction", outcome.Engine.IsSuccess && promptCount == 0 && File.ReadAllBytes(Path.Combine(output, "中文示例.txt")).SequenceEqual(File.ReadAllBytes(source)));
Check("staging cleaned after success", !Directory.EnumerateDirectories(output, ".archive-password-*").Any());

await vault.DeleteAsync(correct.Id);
promptCount = 0;
var manualOutput = Path.Combine(testRoot, "manual-output");
var manual = await service.ExtractAsync(archive, manualOutput, true,
    () => { promptCount++; return Task.FromResult<PasswordPromptResponse?>(new(password, true, "newly saved")); },
    _ => Task.FromResult(ExistingFilePolicy.Replace));
Check("automatic failure falls back to manual", manual.Engine.IsSuccess && promptCount == 1);
Check("successful manual password saved and survives reopen", (await new DpapiPasswordVault(vaultPath).ReadAsync()).Any(value => value.Secret == password && value.LastSuccessAt is not null));

var existing = Path.Combine(manualOutput, "中文示例.txt");
await File.WriteAllTextAsync(existing, "keep this existing file");
var skipped = await service.ExtractAsync(archive, manualOutput, true,
    () => Task.FromResult<PasswordPromptResponse?>(null), _ => Task.FromResult(ExistingFilePolicy.Skip));
Check("skip conflict preserves existing data", skipped.Engine.IsSuccess && await File.ReadAllTextAsync(existing) == "keep this existing file" && skipped.Warning is not null);
var declined = await service.ExtractAsync(archive, manualOutput, true,
    () => Task.FromResult<PasswordPromptResponse?>(null), _ => Task.FromResult(ExistingFilePolicy.Ask));
Check("cancel conflict writes no final files", declined.Engine.Status == "cancelled" && await File.ReadAllTextAsync(existing) == "keep this existing file");

using (var skip = new CancellationTokenSource())
{
    promptCount = 0;
    var progress = new ForwardProgress<TaskUpdate>(value => { if (value.State == "password") skip.Cancel(); });
    var skippedCandidates = await service.ExtractAsync(archive, Path.Combine(testRoot, "skip-candidates"), true,
        () => { promptCount++; return Task.FromResult<PasswordPromptResponse?>(new(password, false, "")); },
        _ => Task.FromResult(ExistingFilePolicy.Replace), progress, skipSavedPasswordToken: skip.Token);
    Check("skip saved candidates enters manual flow", skippedCandidates.Engine.IsSuccess && promptCount == 1);
}

var prior = await File.ReadAllBytesAsync(vaultPath);
await File.WriteAllBytesAsync(vaultPath, "corrupted vault fixture"u8.ToArray());
try { await vault.SaveAsync(correct); Check("unreadable vault blocks overwrite", false); }
catch (VaultException exception) { Check("unreadable vault blocks overwrite", exception.BackupAvailable && (await File.ReadAllBytesAsync(vaultPath)).SequenceEqual("corrupted vault fixture"u8.ToArray())); }
await vault.RestoreBackupAsync();
Check("explicit backup recovery", (await vault.ReadAsync()).Count > 0);
var futurePath = Path.Combine(testRoot, "future.apv");
var futurePayload = ProtectedData.Protect("{\"version\":99,\"passwords\":[],\"associations\":{}}"u8.ToArray(), null, DataProtectionScope.CurrentUser);
await File.WriteAllBytesAsync(futurePath, "APV1"u8.ToArray().Concat(futurePayload).ToArray());
var futureBefore = await File.ReadAllBytesAsync(futurePath);
try { await new DpapiPasswordVault(futurePath).SaveAsync(correct); Check("future schema preserved", false); }
catch (VaultException exception) { Check("future schema preserved", exception.Code == "unsupported_vault_version" && (await File.ReadAllBytesAsync(futurePath)).SequenceEqual(futureBefore)); }
await File.WriteAllTextAsync(Path.Combine(root, "artifacts/core-validation.json"), JsonSerializer.Serialize(new { passed = checks.Count, total = checks.Count, checks }, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"{checks.Count}/{checks.Count} passed.");
return 0;
