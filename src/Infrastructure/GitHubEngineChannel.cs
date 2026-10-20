using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using ArchivePassword.Core;

namespace ArchivePassword.Infrastructure;

public sealed partial class GitHubEngineChannel(HttpClient client)
{
    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9-]*/[A-Za-z0-9_.-]+$")]
    private static partial Regex RepositoryPattern();
    [GeneratedRegex(@"^ArchivePassword-engine-(\d{2}\.\d{2})-win-(x64|arm64)\.zip$")]
    private static partial Regex AssetPattern();
    public static string NormalizeRepository(string value)
    {
        var name = value.Trim().TrimEnd('/');
        const string prefix = "https://github.com/";
        if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) name = name[prefix.Length..];
        if (name.EndsWith(".git", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
        if (!RepositoryPattern().IsMatch(name) || name.Split('/')[1] is "." or "..") throw new UpdateException("invalid_repository");
        return name;
    }
    public async Task<AvailableEngineUpdate?> CheckAsync(string repository, CancellationToken token = default)
    {
        repository = NormalizeRepository(repository);
        if (EngineArchitecture.Current == "arm64")
        {
            AvailableEngineUpdate? best = null;
            // ARM64 releases do not replace the established x64 latest channel.
            for (var page = 1; page <= 20; page++)
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{repository}/releases?per_page=100&page={page}");
                request.Headers.Accept.ParseAdd("application/vnd.github+json");
                request.Headers.UserAgent.ParseAdd("nomozip-update-check");
                using var listed = await client.SendAsync(request, token);
                if (listed.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
                listed.EnsureSuccessStatusCode();
                using var list = JsonDocument.Parse(await listed.Content.ReadAsStringAsync(token));
                foreach (var item in list.RootElement.EnumerateArray())
                {
                    var candidate = ReadRelease(item, repository);
                    if (candidate is not null && (best is null || Version.Parse(candidate.Version) > Version.Parse(best.Version))) best = candidate;
                }
                if (list.RootElement.GetArrayLength() < 100) return best;
            }
            throw new HttpRequestException("Release history exceeds the supported page limit; check the release page manually.");
        }
        using var response = await client.GetAsync($"https://api.github.com/repos/{repository}/releases/latest", token);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
        return ReadRelease(document.RootElement, repository);
    }
    private static AvailableEngineUpdate? ReadRelease(JsonElement release, string repository)
    {
        if (release.GetProperty("draft").GetBoolean() || release.GetProperty("prerelease").GetBoolean()) return null;
        var candidates = new List<AvailableEngineUpdate>();
        foreach (var asset in release.GetProperty("assets").EnumerateArray())
        {
            var match = AssetPattern().Match(asset.GetProperty("name").GetString() ?? "");
            if (!match.Success || match.Groups[2].Value != EngineArchitecture.Current) continue;
            var uri = new Uri(asset.GetProperty("browser_download_url").GetString()!);
            if (uri.Scheme != "https" || uri.Host != "github.com" || !uri.AbsolutePath.StartsWith($"/{repository}/releases/download/", StringComparison.OrdinalIgnoreCase) || asset.GetProperty("size").GetInt64() <= 0 || asset.GetProperty("size").GetInt64() > EngineUpdatePackage.MaximumPackageBytes) throw new UpdateException("invalid_download");
            candidates.Add(new(match.Groups[1].Value, uri, asset.GetProperty("size").GetInt64()));
        }
        return candidates.OrderByDescending(value => Version.Parse(value.Version)).FirstOrDefault();
    }
    public async Task DownloadAsync(AvailableEngineUpdate update, string path, IProgress<string>? progress, CancellationToken token = default)
    {
        using var response = await client.GetAsync(update.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();
        if (response.RequestMessage?.RequestUri?.Scheme != "https" || response.Content.Headers.ContentLength > EngineUpdatePackage.MaximumPackageBytes) throw new UpdateException("invalid_download");
        await using var input = await response.Content.ReadAsStreamAsync(token);
        await using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromMinutes(10));
        try
        {
            await DownloadTransfer.CopyAsync(input, output, update.Size, EngineUpdatePackage.MaximumPackageBytes,
                new ArchivePassword.Core.ForwardProgress<DownloadProgress>(value => progress?.Report(value.Description)), deadline.Token);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { throw new TimeoutException("Engine download timed out."); }
        catch (InvalidDataException) { throw new UpdateException("invalid_download"); }
    }
}
