using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using ArchivePassword.Core;

namespace ArchivePassword.Infrastructure;

public sealed record OfficialEngineUpdate(string Version, string Architecture, Uri DownloadUrl, long Size, string Sha256);

// Upstream assets have their own trust path; they never bypass the nomozip package signature check.
public sealed class OfficialEngineChannel(HttpClient client)
{
    public const string LatestReleaseUrl = "https://api.github.com/repos/ip7z/7zip/releases/latest";
    public static Uri DownloadUrl(string version, string architecture) =>
        new($"https://github.com/ip7z/7zip/releases/download/{version}/7z{version.Replace(".", "")}-{architecture}.exe");

    public static void Validate(OfficialEngineUpdate update, string? architecture = null)
    {
        if (!EngineUpdatePackage.VersionPattern().IsMatch(update.Version) || update.Architecture != (architecture ?? EngineArchitecture.Current)
            || update.Architecture is not ("x64" or "arm64") || update.DownloadUrl != DownloadUrl(update.Version, update.Architecture)
            || update.Size is <= 0 or > EngineUpdatePackage.MaximumPackageBytes || update.Sha256.Length != 64 || !update.Sha256.All(Uri.IsHexDigit))
            throw new UpdateException("invalid_official_update");
    }

    public async Task<OfficialEngineUpdate?> CheckAsync(CancellationToken token = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseUrl);
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        request.Headers.UserAgent.ParseAdd("nomozip-official-engine-update");
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();
        var bytes = await ReadBoundedAsync(response, 1024 * 1024, token);
        try { return ReadRelease(bytes); }
        catch (Exception error) when (error is JsonException or KeyNotFoundException or InvalidOperationException or FormatException or ArgumentException or OverflowException)
        { throw new UpdateException("invalid_official_update", error); }
    }

    private static OfficialEngineUpdate? ReadRelease(byte[] bytes)
    {
        using var document = JsonDocument.Parse(bytes);
        var release = document.RootElement;
        if (release.GetProperty("draft").GetBoolean() || release.GetProperty("prerelease").GetBoolean()) return null;
        var version = release.GetProperty("tag_name").GetString() ?? "";
        if (!EngineUpdatePackage.VersionPattern().IsMatch(version)) throw new UpdateException("invalid_official_update");
        var name = $"7z{version.Replace(".", "")}-{EngineArchitecture.Current}.exe";
        var assets = release.GetProperty("assets").EnumerateArray().Where(asset => asset.GetProperty("name").GetString() == name).ToArray();
        if (assets.Length == 0) return null;
        if (assets.Length != 1) throw new UpdateException("invalid_official_update");
        var asset = assets[0];
        var digest = asset.TryGetProperty("digest", out var value) ? value.GetString() : null;
        if (digest is null || !digest.StartsWith("sha256:", StringComparison.Ordinal)) throw new UpdateException("official_digest_unavailable");
        var update = new OfficialEngineUpdate(version, EngineArchitecture.Current,
            new Uri(asset.GetProperty("browser_download_url").GetString()!), asset.GetProperty("size").GetInt64(), digest[7..]);
        Validate(update);
        return update;
    }

    public async Task DownloadAsync(OfficialEngineUpdate update, string path, IProgress<string>? progress = null, CancellationToken token = default)
    {
        Validate(update);
        var created=false;
        try
        {
            using var response = await client.GetAsync(update.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, token);
            response.EnsureSuccessStatusCode();
            var final = response.RequestMessage?.RequestUri ?? update.DownloadUrl;
            if (final.Scheme != "https" || final.Host is not ("github.com" or "release-assets.githubusercontent.com" or "objects.githubusercontent.com")
                || response.Content.Headers.ContentLength is { } length && length != update.Size) throw new UpdateException("invalid_download");
            await using (var input = await response.Content.ReadAsStreamAsync(token))
            await using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
            using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                created=true;
                var buffer = new byte[81920]; long total = 0; int count;
                while ((count = await input.ReadAsync(buffer, token)) != 0)
                {
                    total += count;
                    if (total > update.Size) throw new UpdateException("invalid_download");
                    hash.AppendData(buffer, 0, count);
                    await output.WriteAsync(buffer.AsMemory(0, count), token);
                    progress?.Report(UiText.Format("EngineSource.Downloading", total / 1024, update.Size / 1024));
                }
                if (total != update.Size || !Convert.ToHexString(hash.GetHashAndReset()).Equals(update.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new UpdateException("hash_mismatch");
            }
        }
        catch { if (created && File.Exists(path)) File.Delete(path); throw; }
    }

    private static async Task<byte[]> ReadBoundedAsync(HttpResponseMessage response, int limit, CancellationToken token)
    {
        if (response.Content.Headers.ContentLength > limit) throw new UpdateException("invalid_official_update");
        await using var input = await response.Content.ReadAsStreamAsync(token);
        using var output = new MemoryStream(); var buffer = new byte[81920]; int count;
        while ((count = await input.ReadAsync(buffer, token)) != 0)
        { if (output.Length + count > limit) throw new UpdateException("invalid_official_update"); await output.WriteAsync(buffer.AsMemory(0, count), token); }
        return output.ToArray();
    }
}
