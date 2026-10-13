using System.Security.Cryptography;
using System.Text;
using ArchivePassword.Core;

namespace ArchivePassword.Infrastructure;

public sealed class FileHashCalculator : IFileHashCalculator
{
    public const int BufferBytes = 128 * 1024;
    public Task<FileHashResult> ComputeAsync(string path, IProgress<FileHashProgress>? progress = null, CancellationToken token = default) => Task.Run(async () =>
    {
        token.ThrowIfCancellationRequested();
        var fullPath = Path.GetFullPath(path);
        await using var file = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, BufferBytes, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var length = file.Length; var buffer = new byte[BufferBytes]; long count = 0;
        var clock = System.Diagnostics.Stopwatch.StartNew(); long lastReport = 0;
        progress?.Report(new(0, length));
        int read;
        while ((read = await file.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
        {
            token.ThrowIfCancellationRequested();
            hash.AppendData(buffer, 0, read); count += read;
            if (count == read || count == length || clock.ElapsedMilliseconds - lastReport >= 100)
            { progress?.Report(new(count, length)); lastReport = clock.ElapsedMilliseconds; }
        }
        token.ThrowIfCancellationRequested();
        if (count != length || file.Length != length) throw new IOException("File length changed during hashing.");
        return new FileHashResult(fullPath, count, Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant());
    }, token);
}

public static class FileHashManifest
{
    public static async Task<IReadOnlyList<Sha256ManifestEntry>> ReadAsync(string path, CancellationToken token = default)
    {
        await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);
        if (file.Length > Sha256Manifest.MaxBytes) throw new HashManifestException("manifest_too_large");
        using var reader = new StreamReader(file, new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: false);
        return Sha256Manifest.Parse(await reader.ReadToEndAsync(token));
    }
    public static IReadOnlyList<Sha256ManifestEntry> Resolve(IReadOnlyList<Sha256ManifestEntry> entries, string manifestPath)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(manifestPath))!;
        var result = new Dictionary<string, Sha256ManifestEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            var fullPath = Path.GetFullPath(entry.FileName, directory);
            if (entry.FileName.IndexOfAny(['\n', '\r', '\0']) >= 0) throw new HashManifestException("invalid_manifest_path");
            if (result.TryGetValue(fullPath, out var existing) && !string.Equals(existing.Digest, entry.Digest, StringComparison.OrdinalIgnoreCase))
                throw new HashManifestException("conflicting_manifest_hashes");
            result[fullPath] = new(fullPath, entry.Digest);
        }
        return result.Values.ToArray();
    }
    public static async Task ExportAsync(string path, IReadOnlyList<FileHashResult> results, CancellationToken token = default)
    {
        var destination = Path.GetFullPath(path);
        if (results.Any(value => string.Equals(Path.GetFullPath(value.Path), destination, StringComparison.OrdinalIgnoreCase)))
            throw new HashManifestException("manifest_is_source_file");
        var directory = Path.GetDirectoryName(destination)!;
        var rows = results.Select(value => new Sha256ManifestEntry(Path.GetRelativePath(directory, value.Path).Replace('\\', '/'), value.Digest)).ToArray();
        var bytes = Encoding.UTF8.GetBytes(Sha256Manifest.Format(rows));
        if (bytes.Length > Sha256Manifest.MaxBytes) throw new HashManifestException("manifest_too_large");
        var temporary = destination + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            token.ThrowIfCancellationRequested();
            await File.WriteAllBytesAsync(temporary, bytes, token);
            token.ThrowIfCancellationRequested();
            if (File.Exists(destination)) File.Replace(temporary, destination, null); else File.Move(temporary, destination);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
