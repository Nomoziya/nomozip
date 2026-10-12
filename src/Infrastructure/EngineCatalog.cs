using System.Security.Cryptography;
using System.Text.Json;
using ArchivePassword.Core;

namespace ArchivePassword.Infrastructure;

public sealed class EngineCatalog(string directory, string? installedDirectory = null, string? publicKey = null)
{
    private readonly Dictionary<string, string> paths = new(StringComparer.Ordinal);
    public async Task<IReadOnlyList<string>> ReadVerifiedVersionsAsync()
    {
        paths.Clear();
        if (Directory.Exists(directory)) foreach (var folder in Directory.EnumerateDirectories(directory))
        {
            var manifestPath = Path.Combine(folder, "manifest.json");
            var dll = Path.Combine(folder, "7z.dll");
            if (!File.Exists(manifestPath) || !File.Exists(dll)) continue;
            try
            {
                using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(manifestPath));
                var root = manifest.RootElement;
                var version = root.GetProperty("version").GetString();
                if (root.GetProperty("protocol").GetInt32() != 1 || root.GetProperty("architecture").GetString() != "x64" || version != Path.GetFileName(folder) || !Version.TryParse(version, out _)) continue;
                await using var input = File.OpenRead(dll);
                var hash = Convert.ToHexString(await SHA256.HashDataAsync(input));
                if (string.Equals(hash, root.GetProperty("dllSha256").GetString(), StringComparison.OrdinalIgnoreCase)) paths[version] = dll;
            }
            catch (Exception exception) when (exception is JsonException or IOException or KeyNotFoundException or UnauthorizedAccessException or InvalidOperationException) { }
        }
        if (installedDirectory is not null && publicKey is not null && Directory.Exists(installedDirectory))
            foreach (var folder in Directory.EnumerateDirectories(installedDirectory))
            {
                try
                {
                    var manifest = await EngineUpdatePackage.VerifyInstalledAsync(folder, publicKey);
                    if (manifest.Version == Path.GetFileName(folder)) paths[manifest.Version] = Path.Combine(folder, "7z.dll");
                }
                catch (Exception exception) when (exception is UpdateException or IOException or UnauthorizedAccessException) { }
            }
        return paths.Keys.OrderByDescending(value => Version.Parse(value)).ToArray();
    }
    public string DllPath(string version) => paths.TryGetValue(version, out var path) ? path : throw new UpdateException("engine_not_available");
}
