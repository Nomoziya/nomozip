using System.Text.Json;

namespace ArchivePassword.Infrastructure;

// Written before starting the installer; consumed only after the target version initializes.
public sealed record ApplicationUpdateReceipt(string FromVersion, string ToVersion, string InstallationDirectory)
{
    private static string ReceiptPath(string cache) => Path.Combine(cache, "pending-update.json");
    public async Task SaveAsync(string cache)
    {
        Directory.CreateDirectory(cache);
        var path = ReceiptPath(cache); var temporary = path + ".tmp";
        try { await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(this)); File.Move(temporary, path, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public static void Discard(string cache) { var path = ReceiptPath(cache); if (File.Exists(path)) File.Delete(path); }
    public static async Task<InstallerCacheCleanup?> CompleteAsync(string cache, string currentVersion, string installationDirectory, bool clean)
    {
        var path = ReceiptPath(cache);
        var installedReceipt = Path.Combine(installationDirectory, "completed-update.txt");
        ApplicationUpdateReceipt? receipt = null;
        if (File.Exists(installedReceipt))
        {
            // The installer also records upgrades from older releases which cannot write a pending receipt.
            var lines = await File.ReadAllLinesAsync(installedReceipt);
            if (lines.Length == 2) receipt = new(lines[0], lines[1], installationDirectory);
        }
        if (receipt is null && File.Exists(path)) receipt = JsonSerializer.Deserialize<ApplicationUpdateReceipt>(await File.ReadAllTextAsync(path));
        if (receipt is null || receipt.FromVersion is null || !Version.TryParse(receipt.FromVersion.Split('-')[0], out var from) || !Version.TryParse(receipt.ToVersion, out var target)
            || !Version.TryParse(currentVersion, out var current) || Normalize(current) < Normalize(target)
            || (Normalize(current) <= Normalize(from) && !(receipt.FromVersion.Contains('-') && Normalize(current) == Normalize(target)))
            || !string.Equals(Path.GetFullPath(receipt.InstallationDirectory), Path.GetFullPath(installationDirectory), StringComparison.OrdinalIgnoreCase)) return null;
        var result = clean ? ApplicationInstallerCache.Clean(cache) : null;
        if (File.Exists(path)) File.Delete(path);
        if (File.Exists(installedReceipt)) File.Delete(installedReceipt);
        return result;
    }

    private static Version Normalize(Version value) => new(value.Major, value.Minor, Math.Max(value.Build,0), Math.Max(value.Revision,0));
}
