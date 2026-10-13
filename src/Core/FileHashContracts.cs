namespace ArchivePassword.Core;

public sealed record FileHashProgress(long BytesRead, long TotalBytes);
public sealed record FileHashResult(string Path, long BytesRead, string Digest);
public interface IFileHashCalculator
{
    Task<FileHashResult> ComputeAsync(string path, IProgress<FileHashProgress>? progress = null, CancellationToken token = default);
}

public sealed record Sha256ManifestEntry(string FileName, string Digest);
public sealed class HashManifestException(string code, int line = 0) : Exception(code)
{
    public string Code { get; } = code;
    public int Line { get; } = line;
}

public static class Sha256Manifest
{
    public const int MaxEntries = 10_000, MaxBytes = 4 * 1024 * 1024;
    public static bool IsDigest(string value) => value.Length == 64 && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F');
    public static IReadOnlyList<Sha256ManifestEntry> Parse(string text)
    {
        if (text.Length > MaxBytes) throw new HashManifestException("manifest_too_large");
        var rows = new List<Sha256ManifestEntry>();
        using var reader = new StringReader(text.TrimStart('\uFEFF'));
        string? line; var number = 0;
        while ((line = reader.ReadLine()) is not null)
        {
            number++;
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#')) continue;
            var escaped = line.StartsWith('\\');
            if (escaped) line = line[1..];
            if (line.Length < 67 || !IsDigest(line[..64]) || line[64] != ' ' || line[65] is not (' ' or '*'))
                throw new HashManifestException("invalid_manifest_line", number);
            var name = escaped ? Unescape(line[66..], number) : line[66..];
            if (string.IsNullOrWhiteSpace(name) || name.Contains('\0')) throw new HashManifestException("invalid_manifest_line", number);
            rows.Add(new(name, line[..64].ToLowerInvariant()));
            if (rows.Count > MaxEntries) throw new HashManifestException("manifest_too_many_files", number);
        }
        if (rows.Count == 0) throw new HashManifestException("empty_manifest");
        return rows;
    }
    public static string Format(IReadOnlyList<Sha256ManifestEntry> rows)
    {
        if (rows.Count == 0) throw new HashManifestException("empty_manifest");
        if (rows.Count > MaxEntries) throw new HashManifestException("manifest_too_many_files");
        var result = new System.Text.StringBuilder();
        foreach (var row in rows)
        {
            if (!IsDigest(row.Digest) || string.IsNullOrWhiteSpace(row.FileName) || row.FileName.Contains('\0')) throw new HashManifestException("invalid_manifest_line");
            var escaped = row.FileName.IndexOfAny(['\\', '\n', '\r']) >= 0;
            if (escaped) result.Append('\\');
            result.Append(row.Digest.ToLowerInvariant()).Append(" *").Append(escaped
                ? row.FileName.Replace("\\", "\\\\").Replace("\n", "\\n").Replace("\r", "\\r") : row.FileName).Append('\n');
            if (result.Length > MaxBytes) throw new HashManifestException("manifest_too_large");
        }
        return result.ToString();
    }
    private static string Unescape(string name, int line)
    {
        var result = new System.Text.StringBuilder();
        for (var index = 0; index < name.Length; index++)
        {
            var character = name[index];
            if (character == '\\')
            {
                if (++index == name.Length) throw new HashManifestException("invalid_manifest_escape", line);
                character = name[index] switch { '\\' => '\\', 'n' => '\n', 'r' => '\r', _ => throw new HashManifestException("invalid_manifest_escape", line) };
            }
            result.Append(character);
        }
        return result.ToString();
    }
}
