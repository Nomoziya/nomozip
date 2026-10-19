using System.Security.Cryptography;
using System.Text.Json;
using ArchivePassword.Core;

namespace ArchivePassword.Infrastructure;

// Local verification receipt, not an upstream or nomozip digital signature.
public sealed record OfficialEngineReceipt(OfficialEngineUpdate Update, Dictionary<string, string> Files)
{
    public const string FileName = "official-receipt.dat";
    public static readonly string[] PayloadNames = ["7z.dll", "7z.exe", "7z.sfx", "License.txt"];
    public async Task SaveAsync(string directory, CancellationToken token)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(this, EngineUpdatePackage.JsonOptions);
        await File.WriteAllBytesAsync(Path.Combine(directory, FileName), ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser), token);
    }
    public static async Task<OfficialEngineReceipt> VerifyInstalledAsync(string directory, CancellationToken token = default)
    {
        try
        {
            var bytes = ProtectedData.Unprotect(await File.ReadAllBytesAsync(Path.Combine(directory, FileName), token), null, DataProtectionScope.CurrentUser);
            var receipt = JsonSerializer.Deserialize<OfficialEngineReceipt>(bytes, EngineUpdatePackage.JsonOptions) ?? throw new UpdateException("invalid_official_update");
            OfficialEngineChannel.Validate(receipt.Update);
            if (receipt.Files.Count != PayloadNames.Length || PayloadNames.Any(name => !receipt.Files.ContainsKey(name))) throw new UpdateException("invalid_official_update");
            foreach (var name in PayloadNames)
            {
                await using var file = File.OpenRead(Path.Combine(directory, name));
                if (file.Length > EngineUpdatePackage.MaximumPackageBytes || !Convert.ToHexString(await SHA256.HashDataAsync(file, token)).Equals(receipt.Files[name], StringComparison.OrdinalIgnoreCase))
                    throw new UpdateException("hash_mismatch");
            }
            foreach (var name in new[] { "7z.dll", "7z.exe", "7z.sfx" })
                if (!EngineArchitecture.IsNativeImage(await File.ReadAllBytesAsync(Path.Combine(directory, name), token))) throw new UpdateException("incompatible_update");
            return receipt;
        }
        catch (Exception error) when (error is CryptographicException or JsonException or ArgumentException or NullReferenceException)
        { throw new UpdateException("invalid_official_update", error); }
    }
}
