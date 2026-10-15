using System.Net.Http;
using System.Security.Cryptography;

namespace ArchivePassword.Infrastructure;

// A release digest from GitHub's HTTPS API checks download integrity, not publisher signing.
public sealed class ApplicationInstallerDownload(HttpClient client)
{
    public const long MaximumBytes = 64 * 1024 * 1024;
    public async Task<string> DownloadAsync(AvailableApplicationRelease release, string architecture, string cache,
        IProgress<int>? progress = null, CancellationToken token = default)
    {
        var expected = new Uri($"https://github.com/{GitHubApplicationChannel.Repository}/releases/download/v{release.Version}/nomozip-{release.Version}-light-setup-win-{architecture}.exe");
        if (architecture is not ("x64" or "arm64") || !release.CanDownload || release.InstallerUrl != expected)
            throw new InvalidDataException("The release has no supported verified installer.");
        Directory.CreateDirectory(cache);
        var destination = Path.Combine(cache, Guid.NewGuid().ToString("N") + ".exe");
        var partial = destination + ".partial";
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, expected);
            request.Headers.UserAgent.ParseAdd("nomozip-update-download");
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is { } length && length != release.InstallerSize)
                throw new InvalidDataException("Installer length differs from release metadata.");
            await using (var input = await response.Content.ReadAsStreamAsync(token))
            await using (var output = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true))
            using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                var buffer = new byte[65536]; long total = 0; int previous = -1;
                for (int count; (count = await input.ReadAsync(buffer, token)) != 0;)
                {
                    total += count;
                    if (total > release.InstallerSize || total > MaximumBytes) throw new InvalidDataException("Installer exceeds declared size.");
                    hash.AppendData(buffer, 0, count);
                    await output.WriteAsync(buffer.AsMemory(0, count), token);
                    var percent = (int)(total * 100 / release.InstallerSize);
                    if (percent != previous) { progress?.Report(percent); previous = percent; }
                }
                if (total != release.InstallerSize || !Convert.ToHexString(hash.GetHashAndReset()).Equals(release.InstallerSha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Installer checksum differs from release metadata.");
            }
            token.ThrowIfCancellationRequested();
            File.Move(partial, destination);
            return destination;
        }
        finally { if (File.Exists(partial)) File.Delete(partial); }
    }

    // Hold this read-only handle until the installer process is started, preventing replacement.
    public static FileStream OpenVerified(string path, AvailableApplicationRelease release)
    {
        var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            if (!release.CanDownload || file.Length != release.InstallerSize ||
                !Convert.ToHexString(SHA256.HashData(file)).Equals(release.InstallerSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The prepared installer has changed.");
            file.Position = 0; return file;
        }
        catch { file.Dispose(); throw; }
    }
}
