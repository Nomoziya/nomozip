using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ArchivePassword.Infrastructure;

public sealed record AvailableApplicationRelease(string Version, Uri ReleaseUrl, Uri InstallerUrl, long InstallerSize = 0, string? InstallerSha256 = null)
{
    public bool CanDownload => InstallerSize is > 0 and <= ApplicationInstallerDownload.MaximumBytes &&
        InstallerSha256 is { Length: 64 } hash && hash.All(Uri.IsHexDigit);
    public bool IsNewerThan(string current)
    {
        var core = current.Split('+')[0].Split('-')[0];
        if (!System.Version.TryParse(core, out var installed)) throw new ArgumentException("Invalid application version.", nameof(current));
        var released = System.Version.Parse(Version);
        return released > installed || released == installed && current.Split('+')[0].Contains('-');
    }
}

// Software releases are independent of the signed engine channel's /releases/latest.
public sealed partial class GitHubApplicationChannel(HttpClient client)
{
    public const string Repository = "Nomoziya/nomozip";
    [GeneratedRegex(@"^v(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$")]
    private static partial Regex StableTag();

    public async Task<AvailableApplicationRelease?> CheckAsync(string architecture, CancellationToken token = default)
    {
        if (architecture is not ("x64" or "arm64" or "x86")) throw new ArgumentException("Unsupported architecture.", nameof(architecture));
        AvailableApplicationRelease? best = null;
        // Follow every page, including pages containing only engine or preview releases.
        for (var page = 1; page <= 20; page++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{Repository}/releases?per_page=100&page={page}");
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            request.Headers.UserAgent.ParseAdd("nomozip-update-check");
            using var response = await client.SendAsync(request, token);
            if (response.StatusCode == HttpStatusCode.NotFound) return null;
            response.EnsureSuccessStatusCode();
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
            var releases = document.RootElement;
            foreach (var release in releases.EnumerateArray())
            {
                if (release.GetProperty("draft").GetBoolean() || release.GetProperty("prerelease").GetBoolean()) continue;
                var tag = release.GetProperty("tag_name").GetString() ?? "";
                if (!StableTag().IsMatch(tag) || !System.Version.TryParse(tag[1..], out var version)) continue;
                var name = $"nomozip-{tag[1..]}-light-setup-win-{architecture}.exe";
                var expectedUrl = new Uri($"https://github.com/{Repository}/releases/download/{tag}/{name}");
                foreach (var asset in release.GetProperty("assets").EnumerateArray())
                {
                    if (asset.GetProperty("name").GetString() != name || asset.GetProperty("size").GetInt64() <= 0) continue;
                    if (!Uri.TryCreate(asset.GetProperty("browser_download_url").GetString(), UriKind.Absolute, out var url) || url != expectedUrl) continue;
                    if (best is null || version > System.Version.Parse(best.Version))
                    {
                        var digest = asset.TryGetProperty("digest", out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
                        best = new(tag[1..], new Uri($"https://github.com/{Repository}/releases/tag/{tag}"), expectedUrl,
                            asset.GetProperty("size").GetInt64(), digest?.StartsWith("sha256:", StringComparison.Ordinal) == true ? digest[7..] : null);
                    }
                }
            }
            if (releases.GetArrayLength() < 100) return best;
        }
        throw new HttpRequestException("Release history exceeds the supported page limit; check the release page manually.");
    }
}
