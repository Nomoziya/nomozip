using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ArchivePassword.Core;
using ArchivePassword.Infrastructure;

var root = Path.GetFullPath(args[0]);
var data = Path.Combine(root, "artifacts/task-improvements-core", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(data);
var checks = new Dictionary<string, bool>();
void Check(string name, bool value) { checks.Add(name, value); if (!value) throw new Exception(name); }
TaskCompletionSource Gate() => new(TaskCreationOptions.RunContinuationsAsynchronously);
string? error = null;
try
{
    using var queuePause = new ExtractionPause();
    using var firstPause = new ExtractionPause(queuePause);
    using var secondPause = new ExtractionPause(queuePause);
    var signals = 0; firstPause.Changed += () => signals++;
    firstPause.Pause();
    Check("local pause leaves sibling and queue running", firstPause.IsRequested && !secondPause.IsRequested && !queuePause.IsRequested);
    var waiting = firstPause.WaitAsync();
    queuePause.Pause(); firstPause.Resume();
    Check("resuming local cannot bypass queue pause", firstPause.IsRequested && secondPause.IsRequested && !waiting.IsCompleted);
    queuePause.Resume(); await waiting.WaitAsync(TimeSpan.FromSeconds(5));
    Check("both pause sources release after their resumes", !firstPause.IsRequested && !secondPause.IsRequested && signals >= 4);
    firstPause.Pause(); queuePause.Pause(); queuePause.Resume();
    Check("resuming queue retains local pause", firstPause.IsRequested && !secondPause.IsRequested);
    using (var cancel = new CancellationTokenSource())
    {
        var paused = firstPause.WaitAsync(cancel.Token); cancel.Cancel();
        try { await paused; Check("cancel wakes paused task", false); } catch (OperationCanceledException) { Check("cancel wakes paused task", true); }
    }
    firstPause.Resume();
    using (var local = new CancellationTokenSource())
    {
        var start = Gate(); var keepRunning = Gate(); var continuing = false; var queued = false;
        var run = ExtractionQueue.RunAsync(new[] {0,1,2}, 2,
            index => new(Path.Combine(data,$"{index}.7z"),Path.Combine(data,$"out-{index}")), async (index, token) =>
        {
            if (index == 0)
            {
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, local.Token);
                start.TrySetResult();
                try { await Task.Delay(Timeout.Infinite, linked.Token); } catch (OperationCanceledException) when (local.IsCancellationRequested && !token.IsCancellationRequested) { }
            }
            else if (index == 1) { await keepRunning.Task.WaitAsync(token); continuing = !token.IsCancellationRequested; }
            else { queued = true; keepRunning.TrySetResult(); }
        });
        await start.Task.WaitAsync(TimeSpan.FromSeconds(5)); local.Cancel(); await run.WaitAsync(TimeSpan.FromSeconds(5));
        Check("single cancel allows sibling and queued task to finish", continuing && queued);
    }
    var password = new SavedPassword(Guid.NewGuid(), "Synthetic record", "synthetic-local-fixture", "note", true, DateTimeOffset.UtcNow);
    var records = new[] { password };
    var snapshot = VaultBackupSnapshot.Capture(records, DateTimeOffset.UtcNow);
    Check("new snapshot covers exact records", !snapshot.HasChanges(records) && snapshot.AddedSince(records) == 0);
    Check("successful use does not create edit reminder", !snapshot.HasChanges([password with { LastSuccessAt = DateTimeOffset.UtcNow }]));
    Check("secret edit requires new backup", snapshot.HasChanges([password with { Secret = "new-synthetic-fixture" }]));
    Check("metadata edit requires new backup", snapshot.HasChanges([password with { Notes = "edited" }]));
    Check("deletion requires new backup", snapshot.HasChanges([]));
    Check("added count reflects new records only", snapshot.AddedSince([password,password with {Id=Guid.NewGuid()}]) == 1);
    var history = Path.Combine(data, "history.apb");
    await VaultBackupHistory.SaveAsync(history, snapshot);
    var raw = await File.ReadAllBytesAsync(history);
    Check("history does not expose secret or password digest", !Encoding.UTF8.GetString(raw).Contains(password.Secret) && !Encoding.UTF8.GetString(raw).Contains(snapshot.ContentDigest));
    var restored = await VaultBackupHistory.ReadAsync(history);
    Check("encrypted history survives restart", restored?.ExportedAt == snapshot.ExportedAt && !restored.HasChanges(records));
    raw[^1] ^= 1; await File.WriteAllBytesAsync(history, raw);
    try { await VaultBackupHistory.ReadAsync(history); Check("damaged history rejected", false); } catch (CryptographicException) { Check("damaged history rejected", true); }
    var cache = Path.Combine(data, "cache"); Directory.CreateDirectory(cache);
    var owned = Path.Combine(cache, new string('a',32)+".exe");
    var prepared = Path.Combine(cache, new string('b',32)+".exe");
    var locked = Path.Combine(cache, new string('c',32)+".exe");
    var partial = Path.Combine(cache, new string('d',32)+".exe.partial");
    foreach (var file in new[]{owned,prepared,locked,partial,Path.Combine(cache,"user-data.exe")}) await File.WriteAllBytesAsync(file,new byte[100]);
    var nested = Path.Combine(cache,"nested"); Directory.CreateDirectory(nested); await File.WriteAllTextAsync(Path.Combine(nested,new string('e',32)+".exe"),"keep");
    var size = ApplicationInstallerCache.Inspect(cache);
    Check("cache counts only generated direct files", size.Files == 4 && size.Bytes == 400);
    using (File.Open(locked, FileMode.Open, FileAccess.Read, FileShare.None))
    {
        var clean = ApplicationInstallerCache.Clean(cache, prepared);
        Check("cleanup reports actual files and bytes", clean.Deleted == 2 && clean.Bytes == 200 && clean.Skipped == 2);
        Check("prepared and busy installers retained", File.Exists(prepared) && File.Exists(locked));
    }
    Check("unrelated files and nested files preserved", File.Exists(Path.Combine(cache,"user-data.exe")) && File.Exists(Path.Combine(nested,new string('e',32)+".exe")));
    var again = ApplicationInstallerCache.Clean(cache, prepared);
    Check("unlocked stale installer becomes removable", again.Deleted == 1 && again.Skipped == 1 && !File.Exists(locked));
    Check("missing cache is harmless", ApplicationInstallerCache.Clean(Path.Combine(data,"missing")).Deleted == 0);
}
catch (Exception exception) { error = exception.ToString(); }
var report = new {passed=error is null && checks.Values.All(value=>value),checks,error};
Directory.CreateDirectory(Path.Combine(root,"artifacts"));
await File.WriteAllTextAsync(Path.Combine(root,"artifacts/task-improvements-core-validation.json"),JsonSerializer.Serialize(report,new JsonSerializerOptions {WriteIndented=true}));
Console.WriteLine(JsonSerializer.Serialize(report)); return report.passed ? 0 : 1;
