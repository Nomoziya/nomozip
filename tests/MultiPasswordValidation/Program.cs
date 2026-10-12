using System.Text.Json;
using System.Diagnostics;
using ArchivePassword.Core;
using ArchivePassword.Infrastructure;

var root = Path.GetFullPath(args[0]);
var testRoot = Path.Combine(root, "artifacts/multi-password-validation", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(testRoot);
var cases = new Dictionary<string, bool>();
void Check(string name, bool passed) { cases[name] = passed; Console.WriteLine((passed ? "PASS " : "FAIL ") + name); if (!passed) throw new Exception(name); }
var fixtures = Path.Combine(root, "tests/fixtures/generated");
var rar = Path.Combine(fixtures, "test_read_format_rar5_encrypted.rar");
var expectedRar = Path.Combine(testRoot, "official-rar-reference"); Directory.CreateDirectory(expectedRar);
foreach (var selection in new[] { ("password", new[] { "a.txt", "b.txt", "c.txt" }), ("password2", new[] { "d.txt" }) })
{
    var start = new ProcessStartInfo(Path.Combine(root, "engines/26.04/7z.exe")) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
    foreach (var arg in new[] { "x", "-y", "-p" + selection.Item1, rar, "-o" + expectedRar }.Concat(selection.Item2)) start.ArgumentList.Add(arg);
    using var process = Process.Start(start)!;
    var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
    await process.WaitForExitAsync(); await output; await error;
    if (process.ExitCode != 0) throw new Exception("Official reference extraction failed.");
}
bool Exact(string output, string archive) => archive.EndsWith(".rar")
    ? new[] { "a.txt", "b.txt", "c.txt", "d.txt" }.All(name => File.ReadAllBytes(Path.Combine(output, name)).SequenceEqual(File.ReadAllBytes(Path.Combine(expectedRar, name))))
    : new[] { "第一文件.txt", "第二文件.txt" }.All(name => File.ReadAllBytes(Path.Combine(output, name)).SequenceEqual(File.ReadAllBytes(Path.Combine(fixtures, name))));
try
{
    foreach (var version in new[] { "26.04", "26.03" })
    foreach (var archive in new[] { rar, Path.Combine(fixtures, "mixed-traditional.zip"), Path.Combine(fixtures, "mixed-aes.zip") })
    {
        var name = version + " " + Path.GetFileName(archive);
        var data = Path.Combine(testRoot, version + "-" + Path.GetFileName(archive)); Directory.CreateDirectory(data);
        var vault = new DpapiPasswordVault(Path.Combine(data, "passwords.apv"));
        var first = new SavedPassword(Guid.NewGuid(), "first", "password", "", true, DateTimeOffset.UtcNow);
        var second = new SavedPassword(Guid.NewGuid(), "second", "password2", "", true, DateTimeOffset.UtcNow);
        var wrong = new SavedPassword(Guid.NewGuid(), "wrong", "never matches", "", true, DateTimeOffset.UtcNow);
        await vault.SaveAsync(wrong); await vault.SaveAsync(first); await vault.SaveAsync(second);
        var engine = new ArchiveEngineClient(Path.Combine(root, "artifacts/worker/ArchiveEngineWorker.exe"), Path.Combine(root, $"engines/{version}/7z.dll"));
        var service = new ExtractionService(engine, vault, new PasswordCandidateProvider(vault));
        var prompts = 0;
        var automatic = await service.ExtractAsync(archive, Path.Combine(data, "automatic"), true, _ => { prompts++; return Task.FromResult<PasswordPromptResponse?>(null); }, _ => Task.FromResult(ExistingFilePolicy.Replace));
        Check(name + " automatic combined extraction exact", automatic.Engine.IsSuccess && prompts == 0 && Exact(automatic.OutputPath, archive));
        var saved = await vault.ReadAsync();
        Check(name + " only used saved passwords record success", saved.Single(value => value.Id == first.Id).LastSuccessAt is not null && saved.Single(value => value.Id == second.Id).LastSuccessAt is not null && saved.Single(value => value.Id == wrong.Id).LastSuccessAt is null);
        var before = await File.ReadAllBytesAsync(Path.Combine(data, "passwords.apv"));
        using var skip = new CancellationTokenSource();
        var progress = new ForwardProgress<TaskUpdate>(value => { if (value.State == "password") skip.Cancel(); });
        var cancelled = await service.ExtractAsync(archive, Path.Combine(data, "cancelled"), false, _ => Task.FromResult<PasswordPromptResponse?>(prompts++ == 0 ? new("password", true, "first new") : null), _ => Task.FromResult(ExistingFilePolicy.Replace));
        Check(name + " cancelling after partial does not commit or save", cancelled.Engine.Status == "cancelled" && !Directory.EnumerateFileSystemEntries(cancelled.OutputPath).Any() && (await File.ReadAllBytesAsync(Path.Combine(data, "passwords.apv"))).SequenceEqual(before));
        prompts = 0;
        var manual = await service.ExtractAsync(archive, Path.Combine(data, "manual"), false, _ => Task.FromResult<PasswordPromptResponse?>(new(new[] { "password", "wrong manual secret", "password2" }[prompts++], true, "manual " + prompts)), _ => Task.FromResult(ExistingFilePolicy.Replace));
        Check(name + " manual supplementary password completes exact", manual.Engine.IsSuccess && prompts == 3 && Exact(manual.OutputPath, archive));
        saved = await vault.ReadAsync();
        Check(name + " both verified manual passwords saved only after success", saved.Count(value => value.Name.StartsWith("manual ") && value.LastSuccessAt is not null) == 2 && !saved.Any(value => value.Secret == "wrong manual secret"));
        Check(name + " no staging remains after success", !Directory.EnumerateDirectories(manual.OutputPath, ".archive-password-*").Any());
        prompts = 0;
        var skipped = await service.ExtractAsync(archive, Path.Combine(data, "skipped"), true, _ => Task.FromResult<PasswordPromptResponse?>(new(prompts++ == 0 ? "password" : "password2", false, "")), _ => Task.FromResult(ExistingFilePolicy.Replace), progress, skipSavedPasswordToken: skip.Token);
        Check(name + " skip candidates keeps manual supplementary flow", skipped.Engine.IsSuccess && prompts == 2 && Exact(skipped.OutputPath, archive));
        before = await File.ReadAllBytesAsync(Path.Combine(data, "passwords.apv"));
        using var cancellation = new CancellationTokenSource();
        var cancelledProgress = new ForwardProgress<TaskUpdate>(value => { if (value.State == "extracting") cancellation.Cancel(); });
        var cancelledExtraction = await service.ExtractAsync(archive, Path.Combine(data, "cancelled-extraction"), true, _ => Task.FromResult<PasswordPromptResponse?>(null), _ => Task.FromResult(ExistingFilePolicy.Replace), cancelledProgress, cancellation.Token);
        Check(name + " cancel before combined extraction cleans staging and preserves vault", cancelledExtraction.Engine.Status == "cancelled" && !Directory.EnumerateFileSystemEntries(cancelledExtraction.OutputPath).Any() && (await File.ReadAllBytesAsync(Path.Combine(data, "passwords.apv"))).SequenceEqual(before));
    }
}
finally
{
    await File.WriteAllTextAsync(Path.Combine(root, "artifacts/multi-password-validation.json"), JsonSerializer.Serialize(new { passed = cases.Count == 48 && cases.Values.All(value => value), total = cases.Count, cases, syntheticData = true }));
}
