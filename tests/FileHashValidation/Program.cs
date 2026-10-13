using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ArchivePassword.Core;
using ArchivePassword.Infrastructure;

var root = Path.GetFullPath(args[0]);
var data = Path.GetFullPath(Path.Combine(root, "artifacts/file-hash", Guid.NewGuid().ToString("N"))); Directory.CreateDirectory(data);
var checks = new Dictionary<string, bool>();
void Check(string name, bool passed) { checks[name] = passed; Console.WriteLine($"{(passed ? "PASS" : "FAIL")} {name}"); if (!passed) throw new Exception(name); }
var calculator = new FileHashCalculator();
var emptyDigest = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";
var abcDigest = "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad";
var empty = Path.Combine(data, "empty.txt"); var abc = Path.Combine(data, "中文 abc.txt");
try
{
    await File.WriteAllBytesAsync(empty, []); await File.WriteAllTextAsync(abc, "abc", new UTF8Encoding(false));
    var sourceBytes = await File.ReadAllBytesAsync(abc);
    Check("known empty SHA256 vector", (await calculator.ComputeAsync(empty)).Digest == emptyDigest);
    var abcResult = await calculator.ComputeAsync(abc);
    Check("known abc SHA256 vector and byte count", abcResult.Digest == abcDigest && abcResult.BytesRead == 3);
    Check("source file never modified by calculation", Enumerable.SequenceEqual(sourceBytes, await File.ReadAllBytesAsync(abc)));
    var million = Path.Combine(data, "million-a.bin"); await File.WriteAllBytesAsync(million, Enumerable.Repeat((byte)'a', 1_000_000).ToArray());
    Check("NIST million-a SHA256 vector", (await calculator.ComputeAsync(million)).Digest == "cdc76e5c9914fb9281a1c7e284d73e67f1809a48a497200e046d39ccc7112cd0");
    var binary = Path.Combine(data, "binary.dat"); var content = Enumerable.Range(0, 4099).Select(value => (byte)value).ToArray(); await File.WriteAllBytesAsync(binary, content);
    Check("binary data digest equals platform reference", (await calculator.ComputeAsync(binary)).Digest == Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant());
    Check("valid digest accepts both cases", Sha256Manifest.IsDigest(abcDigest.ToUpperInvariant()) && Sha256Manifest.IsDigest(abcDigest));
    Check("wrong length nonhex and padded digest rejected", !Sha256Manifest.IsDigest(abcDigest[..63]) && !Sha256Manifest.IsDigest(new('g', 64)) && !Sha256Manifest.IsDigest(" " + abcDigest));
    try { await calculator.ComputeAsync(Path.Combine(data, "missing.bin")); Check("missing file reports read failure", false); }
    catch (FileNotFoundException) { Check("missing file reports read failure", true); }
    using (var locked = new FileStream(abc, FileMode.Open, FileAccess.Write, FileShare.Read))
    {
        try { await calculator.ComputeAsync(abc); Check("active writer is not read as a stable digest", false); }
        catch (IOException) { Check("active writer is not read as a stable digest", true); }
    }
    var large = Path.Combine(data, "streaming-512mb.bin"); await using (var file = File.Create(large)) file.SetLength(512L * 1024 * 1024);
    using (var cancel = new CancellationTokenSource())
    {
        var last = new FileHashProgress(0, 0);
        try { await calculator.ComputeAsync(large, new InlineProgress(value => { last = value; if (value.BytesRead > 0) cancel.Cancel(); }), cancel.Token); Check("large file cancels before reading complete source", false); }
        catch (OperationCanceledException) { Check("large file cancels before reading complete source", last.BytesRead > 0 && last.BytesRead < last.TotalBytes); }
        using var reopened = new FileStream(large, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        Check("cancelled computation releases source handle", reopened.Length == 512L * 1024 * 1024);
    }
    using (var cancel = new CancellationTokenSource())
    {
        cancel.Cancel();
        try { await calculator.ComputeAsync(abc, token: cancel.Token); Check("already cancelled request stops before opening file", false); }
        catch (OperationCanceledException) { Check("already cancelled request stops before opening file", true); }
    }
    var progress = new List<FileHashProgress>(); var allocationBefore = GC.GetTotalAllocatedBytes(precise: true);
    var largeResult = await calculator.ComputeAsync(large, new InlineProgress(value => progress.Add(value)));
    var allocated = GC.GetTotalAllocatedBytes(precise: true) - allocationBefore;
    Check("512MB file uses bounded allocations rather than full-file buffer", allocated < 8 * 1024 * 1024 && largeResult.BytesRead == 512L * 1024 * 1024);
    await using (var reference = File.OpenRead(large)) Check("streamed large digest equals independent platform stream API", largeResult.Digest == Convert.ToHexString(await SHA256.HashDataAsync(reference)).ToLowerInvariant());
    Check("byte progress is monotonic and finishes at actual size", progress.Count >= 3 && progress[0].BytesRead == 0 && progress[^1].BytesRead == largeResult.BytesRead && progress.Zip(progress.Skip(1)).All(pair => pair.First.BytesRead <= pair.Second.BytesRead));
    Check("large progress does not flood UI per buffer", progress.Count < 512L * 1024 * 1024 / FileHashCalculator.BufferBytes);
    File.Delete(large);
    var simple = Sha256Manifest.Parse("\uFEFF# SHA-256\r\n\r\n" + abcDigest.ToUpperInvariant() + "  中文 abc.txt\r\n" + emptyDigest + " *empty.txt\n");
    Check("GNU text binary BOM CRLF comments and Unicode names supported", simple.SequenceEqual(new[] { new Sha256ManifestEntry("中文 abc.txt", abcDigest), new Sha256ManifestEntry("empty.txt", emptyDigest) }));
    var escaped = new[] { new Sha256ManifestEntry("folder\\file with spaces.txt", abcDigest), new Sha256ManifestEntry("newline\nand\rreturn", emptyDigest) };
    Check("GNU backslash newline and return escaping roundtrip", Sha256Manifest.Parse(Sha256Manifest.Format(escaped)).SequenceEqual(escaped));
    void Reject(string name, string input, string code)
    {
        try { Sha256Manifest.Parse(input); Check(name, false); }
        catch (HashManifestException error) { Check(name, error.Code == code); }
    }
    Reject("bad digest cannot be silently skipped", "bad *file.txt", "invalid_manifest_line");
    Reject("unsupported checksum mode rejected", abcDigest + " !file.txt", "invalid_manifest_line");
    Reject("unknown GNU escape rejected", "\\" + abcDigest + " *file\\q.txt", "invalid_manifest_escape");
    Reject("empty manifest not treated as successful verification", "# only comments\n", "empty_manifest");
    Reject("excess entries rejected", string.Concat(Enumerable.Repeat(abcDigest + " *a.txt\n", Sha256Manifest.MaxEntries + 1)), "manifest_too_many_files");
    Reject("oversized text bounded", new('a', Sha256Manifest.MaxBytes + 1), "manifest_too_large");
    var exportDirectory = Path.Combine(data, "manifests"); Directory.CreateDirectory(exportDirectory);
    var exported = Path.Combine(exportDirectory, "SHA256SUMS.txt");
    await FileHashManifest.ExportAsync(exported, [abcResult, await calculator.ComputeAsync(empty)]);
    var resolved = FileHashManifest.Resolve(await FileHashManifest.ReadAsync(exported), exported);
    Check("export import resolves relative parent paths to original files", resolved.Select(value => value.FileName).SequenceEqual([abc, empty]) && resolved[0].Digest == abcDigest);
    Check("exported manifest specifies binary byte mode and UTF8", (await File.ReadAllTextAsync(exported)).Contains(" *../中文 abc.txt") && !(await File.ReadAllBytesAsync(exported)).Take(3).SequenceEqual(new byte[] { 0xef, 0xbb, 0xbf }));
    Check("identical manifest duplicate references deduplicated", FileHashManifest.Resolve([new(abc, abcDigest), new(abc, abcDigest)], exported).Count == 1);
    try { FileHashManifest.Resolve([new(abc, abcDigest), new(abc, emptyDigest)], exported); Check("conflicting manifest digests rejected before reading files", false); }
    catch (HashManifestException error) { Check("conflicting manifest digests rejected before reading files", error.Code == "conflicting_manifest_hashes"); }
    var oldExport = await File.ReadAllBytesAsync(exported);
    try { await FileHashManifest.ExportAsync(abc, [abcResult]); Check("export cannot overwrite original source", false); }
    catch (HashManifestException error) { Check("export cannot overwrite original source", error.Code == "manifest_is_source_file" && Enumerable.SequenceEqual(sourceBytes, await File.ReadAllBytesAsync(abc))); }
    using (var cancel = new CancellationTokenSource())
    {
        cancel.Cancel();
        try { await FileHashManifest.ExportAsync(exported, [abcResult], cancel.Token); Check("cancelled export preserves previous manifest", false); }
        catch (OperationCanceledException) { Check("cancelled export preserves previous manifest", Enumerable.SequenceEqual(oldExport, await File.ReadAllBytesAsync(exported))); }
    }
    using (var locked = new FileStream(exported, FileMode.Open, FileAccess.Read, FileShare.Read))
    {
        try { await FileHashManifest.ExportAsync(exported, [abcResult]); Check("failed replacement preserves original manifest", false); }
        catch (IOException) { Check("failed replacement preserves original manifest", Enumerable.SequenceEqual(oldExport, await File.ReadAllBytesAsync(exported))); }
    }
    Check("failed writes leave no temporary files", !Directory.EnumerateFiles(exportDirectory, "*.tmp-*").Any());
    var invalidUtf8 = Path.Combine(data, "invalid-utf8.txt"); await File.WriteAllBytesAsync(invalidUtf8, [0xff, 0xfe, 0xff]);
    try { await FileHashManifest.ReadAsync(invalidUtf8); Check("invalid UTF8 rejected rather than altering filenames", false); }
    catch (DecoderFallbackException) { Check("invalid UTF8 rejected rather than altering filenames", true); }
    var oversized = Path.Combine(data, "oversized.txt"); await using (var file = File.Create(oversized)) file.SetLength(Sha256Manifest.MaxBytes + 1L);
    try { await FileHashManifest.ReadAsync(oversized); Check("oversized manifest rejected before full allocation", false); }
    catch (HashManifestException error) { Check("oversized manifest rejected before full allocation", error.Code == "manifest_too_large"); }
}
finally
{
    await File.WriteAllTextAsync(Path.Combine(root, "artifacts/file-hash-validation.json"), JsonSerializer.Serialize(new { passed = checks.Count > 0 && checks.Values.All(value => value), total = checks.Count, checks, syntheticFiles = true }, new JsonSerializerOptions { WriteIndented = true }));
}

sealed class InlineProgress(Action<FileHashProgress> report) : IProgress<FileHashProgress>
{
    public void Report(FileHashProgress value) => report(value);
}
