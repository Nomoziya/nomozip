using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using ArchivePassword.Core;
using ArchivePassword.Infrastructure;

if (args.Length == 3 && args[0] == "inspect")
{
    var package = await EngineUpdatePackage.ReadAsync(args[1], await File.ReadAllTextAsync(args[2]), CancellationToken.None);
    Console.WriteLine("Verified signed engine package: " + package.Manifest.Version); return 0;
}
if (args.Length == 3 && args[0] == "keygen")
{
    if (File.Exists(args[1]) || File.Exists(args[2])) throw new IOException("Refusing to replace an existing signing key.");
    using var rsa = RSA.Create(3072);
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[1]))!);
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[2]))!);
    await File.WriteAllTextAsync(args[1], rsa.ExportPkcs8PrivateKeyPem());
    await File.WriteAllTextAsync(args[2], rsa.ExportSubjectPublicKeyInfoPem());
    Console.WriteLine("Signing key created. Keep the private key out of Git and release packages."); return 0;
}
if (args.Length != 8 || args[0] != "pack")
{
    Console.WriteLine("keygen PRIVATE.pem PUBLIC.pem | inspect PACKAGE.zip PUBLIC.pem | pack ENGINE_DIR VERSION REPORT.json PRIVATE.pem OUTPUT.zip TRUSTED_PUBLIC.pem COMPRESSION_REPORT.json"); return 2;
}
var directory = Path.GetFullPath(args[1]); var version = args[2];
if (!EngineUpdatePackage.VersionPattern().IsMatch(version)) throw new ArgumentException("Invalid version.");
using var localManifest = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(directory, "manifest.json")));
if (localManifest.RootElement.GetProperty("protocol").GetInt32() != 1 || localManifest.RootElement.GetProperty("version").GetString() != version) throw new InvalidOperationException("Engine has not been enabled after validation.");
var report = await File.ReadAllBytesAsync(args[3]);
using var evidence = JsonDocument.Parse(report);
var cases = evidence.RootElement.GetProperty("cases").EnumerateArray().ToArray();
if (evidence.RootElement.GetProperty("passed").GetInt32() != evidence.RootElement.GetProperty("total").GetInt32() || cases.Any(value => !value.GetProperty("passed").GetBoolean()) || cases.Count(value => value.GetProperty("name").GetString()!.StartsWith(version + " ", StringComparison.Ordinal)) < 20) throw new InvalidOperationException("A passing compatibility report for this version is required.");
var payload = new Dictionary<string, byte[]> { ["7z.dll"] = await File.ReadAllBytesAsync(Path.Combine(directory, "7z.dll")), ["License.txt"] = await File.ReadAllBytesAsync(Path.Combine(directory, "License.txt")), ["compatibility-report.json"] = report };
var hashes = payload.ToDictionary(value => value.Key, value => Convert.ToHexString(SHA256.HashData(value.Value)).ToLowerInvariant());
using var compressionEvidence = JsonDocument.Parse(await File.ReadAllBytesAsync(args[7]));
var compressionCases = compressionEvidence.RootElement.GetProperty("cases").EnumerateArray().ToArray();
if (compressionEvidence.RootElement.GetProperty("passed").GetInt32() != compressionEvidence.RootElement.GetProperty("total").GetInt32()
    || compressionCases.Any(value => !value.GetProperty("passed").GetBoolean())
    || compressionCases.Count(value => value.GetProperty("name").GetString()!.StartsWith(version + " ", StringComparison.Ordinal)) < 17
    || !hashes["7z.dll"].Equals(compressionEvidence.RootElement.GetProperty("engineHashes").GetProperty(version).GetString(), StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Passing compression evidence for this exact engine binary is required.");
var combinedReport = System.Text.Json.Nodes.JsonNode.Parse(report)!;
combinedReport["compression"] = System.Text.Json.Nodes.JsonNode.Parse(compressionEvidence.RootElement.GetRawText());
payload["compatibility-report.json"] = JsonSerializer.SerializeToUtf8Bytes(combinedReport);
hashes["compatibility-report.json"] = Convert.ToHexString(SHA256.HashData(payload["compatibility-report.json"])).ToLowerInvariant();
if (!hashes["7z.dll"].Equals(localManifest.RootElement.GetProperty("dllSha256").GetString(), StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Engine hash does not match the validated local manifest.");
if (!hashes["7z.dll"].Equals(evidence.RootElement.GetProperty("engineHashes").GetProperty(version).GetString(), StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Report belongs to a different engine binary.");
var manifest = new EngineUpdateManifest(1, version, "x64", 1, "0.1", localManifest.RootElement.GetProperty("source").GetString()!, hashes);
var bytes = JsonSerializer.SerializeToUtf8Bytes(manifest, EngineUpdatePackage.JsonOptions);
using var key = RSA.Create(); key.ImportFromPem(await File.ReadAllTextAsync(args[4]));
var signature = key.SignData(bytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pss);
EngineUpdatePackage.Verify(bytes, signature, await File.ReadAllTextAsync(args[6]));
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[5]))!);
using (var output = new FileStream(args[5], FileMode.CreateNew, FileAccess.Write))
using (var zip = new ZipArchive(output, ZipArchiveMode.Create))
{
    async Task Add(string name, byte[] value) { await using var stream = zip.CreateEntry(name, CompressionLevel.Optimal).Open(); await stream.WriteAsync(value); }
    await Add("manifest.json", bytes); await Add("manifest.sig", signature);
    foreach (var (name, value) in payload) await Add("engine/" + name, value);
}
Console.WriteLine("Signed update package created: " + Path.GetFullPath(args[5])); return 0;
