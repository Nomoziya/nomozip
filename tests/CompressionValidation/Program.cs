using System.Diagnostics;
using System.Text.Json;
using ArchivePassword.Core;
using ArchivePassword.Infrastructure;

var root = Path.GetFullPath(args[0]);
var folder = Path.Combine(root, "artifacts/compression-validation", Guid.NewGuid().ToString("N"));
var source = Path.Combine(folder, "中文文件夹"); Directory.CreateDirectory(Path.Combine(source, "empty"));
var text = "Synthetic compression content 中文。\n";
await File.WriteAllTextAsync(Path.Combine(source, "内容.txt"), text);
await File.WriteAllBytesAsync(Path.Combine(source, "zero.bin"), []);
var results = new List<object>(); int passed = 0, total = 0;
void Check(string name, bool value) { total++; if (value) passed++; results.Add(new { name, passed = value }); Console.WriteLine((value ? "PASS " : "FAIL ") + name); }
async Task<bool> OfficialExtract(string version, string archive, string? password, string output)
{
    var start = new ProcessStartInfo(Path.Combine(root, "engines", version, "7z.exe")) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
    foreach (var argument in new[] { "x", "-y", "-o" + output, archive }) start.ArgumentList.Add(argument);
    // Only synthetic fixture passwords enter the official CLI's test-only command arguments.
    if (password is not null) start.ArgumentList.Add("-p" + password);
    using var process = Process.Start(start)!;
    var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
    await process.WaitForExitAsync(); await stdout; await stderr;
    return process.ExitCode == 0 && File.ReadAllText(Path.Combine(output, "中文文件夹/内容.txt")) == text
        && File.Exists(Path.Combine(output, "中文文件夹/zero.bin")) && Directory.Exists(Path.Combine(output, "中文文件夹/empty"));
}
var versions = args.Length > 1 ? args.Skip(1).SelectMany(value => value.Split(',')).Distinct().ToArray() : ["26.04", "26.03"];
if (versions.Any(value => !System.Text.RegularExpressions.Regex.IsMatch(value, @"^\d{2}\.\d{2}$"))) throw new ArgumentException("Invalid engine version.");
foreach (var version in versions)
{
    var client = new ArchiveEngineClient(Path.Combine(root, "artifacts/worker/ArchiveEngineWorker.exe"), Path.Combine(root, "engines", version, "7z.dll"));
    var service = new CompressionService(client, client);
    await CompressionExclusionsValidation.RunAsync(folder,version,client,Check);
    foreach (var level in new[] { 0, 9 })
    {
        var archive = Path.Combine(folder, $"{version}-level-{level}.7z");
        var created = await service.CreateAsync([source], archive, "7z", level: level);
        Check($"{version} level {level} official CLI exact contents", created.Engine.IsSuccess && await OfficialExtract(version, archive, null, archive + ".out"));
    }
    foreach (var format in new[] { "7z", "zip" })
    foreach (var encrypted in new[] { false, true })
    {
        var password = !encrypted ? null : format == "7z" ? "测试密碼 ' $ & 空格" : "AES ' $ & spaces";
        var archive = Path.Combine(folder, $"{version}-{format}-{encrypted}.{format}");
        var created = await service.CreateAsync([source], archive, format, password: password);
        Check($"{version} {format} {encrypted} created and validated", created.Engine.IsSuccess);
        Check($"{version} {format} {encrypted} official CLI exact contents", created.Engine.IsSuccess && await OfficialExtract(version, archive, password, archive + ".out"));
        if (encrypted)
        {
            Check($"{version} {format} wrong password rejected", !(await client.TestAsync(archive, "wrong synthetic password")).IsSuccess);
            if (format == "7z") Check($"{version} hidden 7z filenames require password", !(await client.ListAsync(archive)).IsSuccess);
        }
    }
    var existing = Path.Combine(folder, version + "-existing.7z"); await File.WriteAllTextAsync(existing, "preserve me");
    var refused = await service.CreateAsync([source], existing, "7z");
    Check(version + " existing output requires confirmation", refused.Engine.Code == "output_exists" && File.ReadAllText(existing) == "preserve me");
    var replaced = await service.CreateAsync([source], existing, "7z", replace: true);
    Check(version + " confirmed replacement produces valid archive", replaced.Engine.IsSuccess && (await client.TestAsync(existing)).IsSuccess);
    var duplicate = Path.Combine(folder, "other/中文文件夹"); Directory.CreateDirectory(duplicate);
    var rejected = await service.CreateAsync([source, duplicate], Path.Combine(folder, version + "-duplicate.7z"), "7z");
    Check(version + " different roots with same archive path rejected", rejected.Engine.Code == "duplicate_archive_path");
    var invalidZip = await service.CreateAsync([source], Path.Combine(folder, version + "-unicode.zip"), "zip", password: "中文");
    Check(version + " unsupported ZIP Unicode password rejected before writing", invalidZip.Engine.Code == "zip_password_ascii_only" && !File.Exists(invalidZip.OutputPath));
    var allCancelled = true;
    for (var attempt = 0; attempt < 4; attempt++)
    {
        using var cancel = new CancellationTokenSource();
        var cancelOutput = Path.Combine(folder, version + "-cancel-" + attempt + ".7z");
        var cancelled = await service.CreateAsync([source], cancelOutput, "7z", progress: new ForwardProgress<TaskUpdate>(value => { if (value.Progress is not null) cancel.Cancel(); }), cancellationToken: cancel.Token);
        var cancelledCleanly = cancel.IsCancellationRequested && cancelled.Engine.Status == "cancelled" && !File.Exists(cancelOutput) && !Directory.EnumerateFiles(folder, ".nomozip-create-*").Any();
        if (!cancelledCleanly) Console.WriteLine(JsonSerializer.Serialize(new { version, attempt, cancelled.Engine.Status, cancelled.Engine.Code, requested = cancel.IsCancellationRequested, outputExists = File.Exists(cancelOutput) }));
        allCancelled &= cancelledCleanly;
    }
    Check(version + " cancel removes partial output", allCancelled);
    var failedReplace = await service.CreateAsync([Path.Combine(folder, "missing")], existing, "7z", replace: true);
    Check(version + " failed replacement keeps existing valid archive", !failedReplace.Engine.IsSuccess && (await client.TestAsync(existing)).IsSuccess);
}
var hashes = versions.ToDictionary(value => value, value => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(Path.Combine(root, "engines", value, "7z.dll")))).ToLowerInvariant());
await File.WriteAllTextAsync(Path.Combine(root, "artifacts/compression-validation.json"), JsonSerializer.Serialize(new { passed, total, cases = results, engineHashes = hashes }, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"{passed}/{total} passed."); return passed == total ? 0 : 1;
