using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using ArchivePassword.Core;

namespace ArchivePassword.Infrastructure;

public static partial class EngineUpdatePackage
{
    public const long MaximumPackageBytes = 32 * 1024 * 1024;
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    public static readonly string[] PayloadNames = ["7z.dll", "License.txt", "compatibility-report.json"];
    [GeneratedRegex(@"^\d{2}\.\d{2}$")]
    public static partial Regex VersionPattern();
    public static EngineUpdateManifest Verify(byte[] manifest, byte[] signature, string publicKey, string? architecture = null)
    {
        try
        {
            using var rsa = RSA.Create(); rsa.ImportFromPem(publicKey);
            if (!rsa.VerifyData(manifest, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pss)) throw new UpdateException("invalid_signature");
            var value = JsonSerializer.Deserialize<EngineUpdateManifest>(manifest, JsonOptions) ?? throw new UpdateException("invalid_manifest");
            var target = architecture ?? EngineArchitecture.Current;
            if (target is not ("x64" or "arm64") || value.Schema != 1 || value.Protocol != 1 || value.Architecture != target || value.Version is null || !VersionPattern().IsMatch(value.Version) ||
                !Version.TryParse(value.MinimumAppVersion, out var minimum) || minimum > new Version(0, 1)) throw new UpdateException("incompatible_update");
            if (!Uri.TryCreate(value.Source, UriKind.Absolute, out var source) || source.Scheme != "https" || source.Host != "github.com" ||
                source.AbsolutePath != $"/ip7z/7zip/releases/download/{value.Version}/7z{value.Version.Replace(".", "")}-{value.Architecture}.exe") throw new UpdateException("invalid_manifest");
            if (value.Files is null || value.Files.Count != PayloadNames.Length || PayloadNames.Any(name => !value.Files.TryGetValue(name, out var hash) || hash is null || hash.Length != 64 || !hash.All(Uri.IsHexDigit))) throw new UpdateException("invalid_manifest");
            return value;
        }
        catch (Exception exception) when (exception is CryptographicException or ArgumentException or JsonException or NullReferenceException)
        { throw new UpdateException("invalid_manifest", exception); }
    }
    public static async Task<(EngineUpdateManifest Manifest, byte[] ManifestBytes, byte[] Signature, Dictionary<string, byte[]> Payload)> ReadAsync(string path, string publicKey, CancellationToken token, string? architecture = null)
    {
        if (new FileInfo(path).Length > MaximumPackageBytes) throw new UpdateException("invalid_package");
        await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var zip = new ZipArchive(file, ZipArchiveMode.Read);
        var names = new[] { "manifest.json", "manifest.sig" }.Concat(PayloadNames.Select(name => "engine/" + name)).ToArray();
        if (zip.Entries.Count != names.Length || zip.Entries.Select(entry => entry.FullName).Distinct(StringComparer.Ordinal).Count() != names.Length || zip.Entries.Any(entry => !names.Contains(entry.FullName, StringComparer.Ordinal))) throw new UpdateException("invalid_package");
        async Task<byte[]> Read(string name, int maximum)
        {
            var entry = zip.GetEntry(name) ?? throw new UpdateException("invalid_package");
            if (entry.Length > maximum || entry.Length < 1) throw new UpdateException("invalid_package");
            using var result = new MemoryStream(); await using var input = entry.Open();
            var buffer = new byte[81920]; int count;
            while ((count = await input.ReadAsync(buffer, token)) != 0)
            { if (result.Length + count > maximum) throw new UpdateException("invalid_package"); await result.WriteAsync(buffer.AsMemory(0, count), token); }
            return result.ToArray();
        }
        var manifestBytes = await Read("manifest.json", 64 * 1024);
        var signature = await Read("manifest.sig", 1024);
        var manifest = Verify(manifestBytes, signature, publicKey, architecture);
        var payload = new Dictionary<string, byte[]>();
        foreach (var name in PayloadNames)
        {
            var bytes = await Read("engine/" + name, name == "7z.dll" ? 16 * 1024 * 1024 : 1024 * 1024);
            if (!Convert.ToHexString(SHA256.HashData(bytes)).Equals(manifest.Files[name], StringComparison.OrdinalIgnoreCase)) throw new UpdateException("hash_mismatch");
            payload.Add(name, bytes);
        }
        if (!EngineArchitecture.IsNativeImage(payload["7z.dll"],architecture)) throw new UpdateException("incompatible_update");
        return (manifest, manifestBytes, signature, payload);
    }
    public static async Task<EngineUpdateManifest> VerifyInstalledAsync(string directory, string publicKey, CancellationToken token = default)
    {
        var manifest = Verify(await File.ReadAllBytesAsync(Path.Combine(directory, "update-manifest.json"), token), await File.ReadAllBytesAsync(Path.Combine(directory, "update-manifest.sig"), token), publicKey);
        foreach (var name in PayloadNames)
        {
            await using var file = File.OpenRead(Path.Combine(directory, name));
            if (!Convert.ToHexString(await SHA256.HashDataAsync(file, token)).Equals(manifest.Files[name], StringComparison.OrdinalIgnoreCase)) throw new UpdateException("hash_mismatch");
        }
        if (!EngineArchitecture.IsNativeImage(await File.ReadAllBytesAsync(Path.Combine(directory,"7z.dll"),token))) throw new UpdateException("incompatible_update");
        return manifest;
    }
}
