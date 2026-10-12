using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using ArchivePassword.Core;

namespace ArchivePassword.Infrastructure;

public sealed partial class GitHubEngineChannel(HttpClient client)
{
    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9-]*/[A-Za-z0-9_.-]+$")]
    private static partial Regex RepositoryPattern();
    [GeneratedRegex(@"^ArchivePassword-engine-(\d{2}\.\d{2})-win-x64\.zip$")]
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
        using var response = await client.GetAsync($"https://api.github.com/repos/{repository}/releases/latest", token);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
        var release = document.RootElement;
        if (release.GetProperty("draft").GetBoolean() || release.GetProperty("prerelease").GetBoolean()) return null;
        var candidates = new List<AvailableEngineUpdate>();
        foreach (var asset in release.GetProperty("assets").EnumerateArray())
        {
            var match = AssetPattern().Match(asset.GetProperty("name").GetString() ?? "");
            if (!match.Success) continue;
            var uri = new Uri(asset.GetProperty("browser_download_url").GetString()!);
            if (uri.Scheme != "https" || uri.Host != "github.com" || !uri.AbsolutePath.StartsWith($"/{repository}/releases/download/", StringComparison.OrdinalIgnoreCase) || asset.GetProperty("size").GetInt64() > EngineUpdatePackage.MaximumPackageBytes) throw new UpdateException("invalid_download");
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
        var buffer = new byte[81920]; long total = 0; int count;
        while ((count = await input.ReadAsync(buffer, token)) != 0)
        {
            total += count; if (total > EngineUpdatePackage.MaximumPackageBytes) throw new UpdateException("invalid_download");
            await output.WriteAsync(buffer.AsMemory(0, count), token);
            progress?.Report($"正在下载更新包：{total / 1024:N0} KB…");
        }
        if (total != update.Size) throw new UpdateException("invalid_download");
    }
}
