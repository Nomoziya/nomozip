using System.Security.Cryptography;
using System.Text.Json;
using ArchivePassword.Core;

namespace ArchivePassword.Infrastructure;

public sealed class EngineCatalog(string directory, string? installedDirectory = null, string? publicKey = null)
{
    private readonly Dictionary<string, string> paths = new(StringComparer.Ordinal);
    public string? OfficialDirectory => installedDirectory is null ? null : installedDirectory + "-official";
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
                if (root.GetProperty("protocol").GetInt32() != 1 || root.GetProperty("architecture").GetString() != EngineArchitecture.Current || version != Path.GetFileName(folder) || !Version.TryParse(version, out _)) continue;
                if (!EngineArchitecture.IsNativeImage(await File.ReadAllBytesAsync(dll))) continue;
                await using var input = File.OpenRead(dll);
                var hash = Convert.ToHexString(await SHA256.HashDataAsync(input));
                if (string.Equals(hash, root.GetProperty("dllSha256").GetString(), StringComparison.OrdinalIgnoreCase)) paths[version] = dll;
            }
            catch (Exception exception) when (exception is JsonException or IOException or KeyNotFoundException or UnauthorizedAccessException or InvalidOperationException) { }
        }
        if (OfficialDirectory is { } official && Directory.Exists(official))
            foreach (var folder in Directory.EnumerateDirectories(official))
            {
                try
                {
                    var receipt = await OfficialEngineReceipt.VerifyInstalledAsync(folder);
                    if (receipt.Update.Version == Path.GetFileName(folder)) paths[receipt.Update.Version] = Path.Combine(folder, "7z.dll");
                }
                catch (Exception exception) when (exception is UpdateException or IOException or UnauthorizedAccessException) { }
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
    public async Task<SfxModule> ReadSfxModuleAsync(string version,CancellationToken token=default)
    {
        var selectedDll=Path.GetFullPath(DllPath(version));var selectedFolder=Path.GetDirectoryName(selectedDll)!;
        if (OfficialDirectory is { } official && selectedDll.StartsWith(Path.GetFullPath(official).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))
        {
            var receipt=await OfficialEngineReceipt.VerifyInstalledAsync(selectedFolder,token);
            var module=new SfxModule(version,receipt.Files["7z.sfx"],await File.ReadAllBytesAsync(Path.Combine(selectedFolder,"7z.sfx"),token));
            if(receipt.Update.Version!=version || !module.IsValid())throw new UpdateException("sfx_module_invalid");return module;
        }
        if(installedDirectory is not null && publicKey is not null && selectedDll.StartsWith(Path.GetFullPath(installedDirectory).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))
        {
            var manifest=await EngineUpdatePackage.VerifyInstalledAsync(selectedFolder,publicKey,token);
            var bytes=await File.ReadAllBytesAsync(Path.Combine(selectedFolder,"compatibility-report.json"),token);
            if(!Convert.ToHexString(SHA256.HashData(bytes)).Equals(manifest.Files["compatibility-report.json"],StringComparison.OrdinalIgnoreCase)) throw new UpdateException("hash_mismatch");
            using var report=JsonDocument.Parse(bytes);
            if(report.RootElement.TryGetProperty("sfx",out var sfx))
            {
                try
                {
                    var module=new SfxModule(sfx.GetProperty("version").GetString()!,sfx.GetProperty("sha256").GetString()!,Convert.FromBase64String(sfx.GetProperty("moduleBase64").GetString()!));
                    if(module.Version!=version || !module.IsValid()) throw new UpdateException("sfx_module_invalid");return module;
                }
                catch(Exception error) when(error is JsonException or KeyNotFoundException or FormatException or InvalidOperationException or ArgumentException) {throw new UpdateException("sfx_module_invalid",error);}
            }
        }
        // Legacy signed packages can reuse an identical bundled engine's module, never another engine version.
        var bundled=Path.Combine(directory,version);var manifestPath=Path.Combine(bundled,"manifest.json");var path=Path.Combine(bundled,"7z.sfx");
        if(!File.Exists(manifestPath) || !File.Exists(path)) throw new UpdateException("sfx_module_unavailable");
        using var local=JsonDocument.Parse(await File.ReadAllBytesAsync(manifestPath,token));var root=local.RootElement;
        var selectedHash=Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(selectedDll,token)));
        if(root.GetProperty("version").GetString()!=version || !selectedHash.Equals(root.GetProperty("dllSha256").GetString(),StringComparison.OrdinalIgnoreCase)
            || !root.TryGetProperty("sfxSha256",out var expected)) throw new UpdateException("sfx_module_unavailable");
        var result=new SfxModule(version,expected.GetString()!,await File.ReadAllBytesAsync(path,token));
        if(!result.IsValid() || !Version.TryParse(System.Diagnostics.FileVersionInfo.GetVersionInfo(path).FileVersion,out var actual) || actual!=Version.Parse(version)) throw new UpdateException("sfx_module_invalid");return result;
    }
}
