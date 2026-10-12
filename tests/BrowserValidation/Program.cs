using ArchivePassword.Core;
using ArchivePassword.Infrastructure;
using System.Security.Cryptography;
using System.Text.Json;

var root = Path.GetFullPath(args[0]);
var testRoot = Path.Combine(root, "artifacts/browser-validation", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(testRoot);
var vaultPath = Path.Combine(testRoot, "passwords.apv");
var vault = new DpapiPasswordVault(vaultPath);
await vault.SaveAsync(new(Guid.NewGuid(), "synthetic wrong", "incorrect", "", true, DateTimeOffset.UtcNow.AddMinutes(-1)));
const string password = "测试密碼 ' $ & 空格";
await vault.SaveAsync(new(Guid.NewGuid(), "synthetic correct", password, "", true, DateTimeOffset.UtcNow));
var before = SHA256.HashData(await File.ReadAllBytesAsync(vaultPath));
var fileCount = Directory.EnumerateFiles(testRoot, "*", SearchOption.AllDirectories).Count();
var checks = new Dictionary<string, bool>();
void Check(string name, bool passed) { checks[name] = passed; Console.WriteLine((passed ? "PASS " : "FAIL ") + name); }
string Fixture(string name) => Path.Combine(root, "tests/fixtures/generated", name);
try
{
    foreach (var version in new[] { "26.04", "26.03" })
    {
        var client = new ArchiveEngineClient(Path.Combine(root, "artifacts/worker/ArchiveEngineWorker.exe"), Path.Combine(root, "engines", version, "7z.dll"));
        var service = new ArchiveBrowsingService(client, new PasswordCandidateProvider(vault));
        foreach (var name in new[] { "plain.7z", "plain.zip", "encrypted-legacy.zip", "encrypted-aes-ascii.zip" })
        {
            var prompts = 0;
            var outcome = await service.BrowseAsync(Fixture(name), true, () => { prompts++; return Task.FromResult<string?>(null); });
            Check(version + " readable filenames " + name, outcome.Engine.IsSuccess && outcome.Engine.Items.Any(item => item.Path == "中文示例.txt") && prompts == 0);
        }
        var rar = await service.BrowseAsync(Fixture("test_read_format_rar.rar"), true, () => Task.FromResult<string?>(null));
        Check(version + " RAR content", rar.Engine.IsSuccess && rar.Engine.Items.Count > 0);
        var rar5 = await service.BrowseAsync(Fixture("test_read_format_rar5_encrypted_filenames.rar"), false, () => Task.FromResult<string?>("password"));
        Check(version + " RAR5 encrypted filenames", rar5.Engine.IsSuccess && rar5.Engine.Items.Count > 0);
        var automatic = await service.BrowseAsync(Fixture("encrypted-header.7z"), true, () => throw new Exception("Unexpected prompt"));
        Check(version + " header uses saved password", automatic.Engine.IsSuccess && automatic.Engine.Items.Any(item => item.Path == "中文示例.txt" && item.Encrypted));
        var manualCalls = 0;
        var manual = await service.BrowseAsync(Fixture("encrypted-header.7z"), false, () => { manualCalls++; return Task.FromResult<string?>(password); });
        Check(version + " disabled automatic prompts", manual.Engine.IsSuccess && manualCalls == 1);
        manualCalls = 0;
        var retry = await service.BrowseAsync(Fixture("encrypted-header.7z"), false, () => Task.FromResult<string?>(++manualCalls == 1 ? "wrong" : password));
        Check(version + " wrong manual password then correct", retry.Engine.IsSuccess && manualCalls == 2);
        var cancelled = await service.BrowseAsync(Fixture("encrypted-header.7z"), false, () => Task.FromResult<string?>(null));
        Check(version + " password dialog cancellation", cancelled.Engine.Status == "cancelled" && cancelled.Engine.Items.Count == 0);
        var brokenCalls = 0;
        var corrupt = await service.BrowseAsync(Fixture("corrupt.7z"), true, () => { brokenCalls++; return Task.FromResult<string?>(null); });
        Check(version + " corrupt does not invent listing", !corrupt.Engine.IsSuccess && corrupt.Engine.Items.Count == 0 && brokenCalls == 0);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        var cancelledCall = false;
        try { await service.BrowseAsync(Fixture("plain.7z"), true, () => Task.FromResult<string?>(null), cancellationToken: cancellation.Token); }
        catch (OperationCanceledException) { cancelledCall = true; }
        Check(version + " cancellation before reading", cancelledCall);
        using var skip = new CancellationTokenSource(); var skippedPrompts = 0;
        var skipped = await service.BrowseAsync(Fixture("encrypted-header.7z"), true,
            () => { skippedPrompts++; return Task.FromResult<string?>(password); },
            new CallbackProgress(update => { if (update.State == "password") skip.Cancel(); }), skipSavedPasswordToken: skip.Token);
        Check(version + " skip saved passwords prompts manually", skipped.Engine.IsSuccess && skippedPrompts == 1);
        Check(version + " vault and workspace unchanged", SHA256.HashData(await File.ReadAllBytesAsync(vaultPath)).SequenceEqual(before)
            && Directory.EnumerateFiles(testRoot, "*", SearchOption.AllDirectories).Count() == fileCount);
    }
}
finally
{
    var passed = checks.Count == 28 && checks.Values.All(value => value);
    await File.WriteAllTextAsync(Path.Combine(root, "artifacts/browser-validation.json"), JsonSerializer.Serialize(new { passed, total = checks.Count, checks, syntheticData = true, scope = "readonly content listing with two official engines and no password vault writes" }));
}
return checks.Count == 28 && checks.Values.All(value => value) ? 0 : 1;

sealed class CallbackProgress(Action<TaskUpdate> report) : IProgress<TaskUpdate> { public void Report(TaskUpdate value) => report(value); }
