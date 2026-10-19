using System.Text.RegularExpressions;

namespace ArchivePassword.Infrastructure;

public sealed record InstallerCacheSummary(int Files, long Bytes);
public sealed record InstallerCacheCleanup(int Deleted, long Bytes, int Skipped);

// Only files created by ApplicationInstallerDownload, directly in its dedicated cache.
public static partial class ApplicationInstallerCache
{
    [GeneratedRegex(@"\A[0-9a-f]{32}\.exe(?:\.partial)?\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex OwnedName();

    private static IEnumerable<FileInfo> Files(string cache)
    {
        var directory = new DirectoryInfo(cache);
        if (!directory.Exists) return [];
        if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Update cache must be a regular directory.");
        return directory.EnumerateFiles().Where(file => OwnedName().IsMatch(file.Name) &&
            (file.Attributes & FileAttributes.ReparsePoint) == 0).ToArray();
    }

    public static InstallerCacheSummary Inspect(string cache)
    {
        var files = Files(cache).ToArray();
        return new(files.Length, files.Sum(file => file.Length));
    }

    public static InstallerCacheCleanup Clean(string cache, string? preparedInstaller = null)
    {
        if (preparedInstaller is not null) preparedInstaller = Path.GetFullPath(preparedInstaller);
        var deleted = 0; long bytes = 0; var skipped = 0;
        foreach (var file in Files(cache))
        {
            if (string.Equals(file.FullName, preparedInstaller, StringComparison.OrdinalIgnoreCase)) { skipped++; continue; }
            try
            {
                // Deletion and exclusive ownership share one handle; locked/running installers are skipped.
                long length;
                using (var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.ReadWrite,
                    FileShare.None, 4096, FileOptions.DeleteOnClose)) length = stream.Length;
                if (File.Exists(file.FullName)) { skipped++; continue; }
                bytes += length; deleted++;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { skipped++; }
        }
        return new(deleted, bytes, skipped);
    }
}
