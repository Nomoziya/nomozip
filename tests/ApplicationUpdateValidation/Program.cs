using System.Net;
using System.Text.Json;
using ArchivePassword.Infrastructure;

var root = Path.GetFullPath(args[0]);
var checks = new Dictionary<string,bool>();
void Check(string name, bool passed) { checks[name] = passed; Console.WriteLine($"{(passed ? "PASS" : "FAIL")} {name}"); }
object Release(string tag, string arch = "x64", bool preview = false, bool draft = false, string? url = null, long size = 1024, bool bundled = false)
{
    var name = $"nomozip-{tag[1..]}-{(bundled ? "" : "light-")}setup-win-{arch}.exe";
    return new { tag_name=tag, prerelease=preview, draft, assets=new[] { new { name, size, browser_download_url=url ?? $"https://github.com/Nomoziya/nomozip/releases/download/{tag}/{name}" } } };
}
async Task<AvailableApplicationRelease?> Find(object[] releases, string arch = "x64")
{
    using var client = new HttpClient(new Handler((request,_) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(releases)) })));
    return await new GitHubApplicationChannel(client).CheckAsync(arch);
}
var found = await Find([Release("engine-26.04"),Release("v0.2.0-preview.9"),Release("v99.0.0",preview:true),Release("v98.0.0",draft:true),Release("v0.1.0"),Release("v0.3.0"),Release("v0.2.0")]);
Check("stable highest software release excludes engine preview draft", found?.Version == "0.3.0");
Check("canonical release page", found?.ReleaseUrl.AbsoluteUri == "https://github.com/Nomoziya/nomozip/releases/tag/v0.3.0");
Check("canonical lightweight installer", found?.InstallerUrl.AbsoluteUri.EndsWith("nomozip-0.3.0-light-setup-win-x64.exe") == true);
foreach (var current in new[] { "0.1.0", "0.2.0-preview.3", "0.3.0-preview.3", "0.3.0-preview.3+abc" }) Check("newer than " + current, found!.IsNewerThan(current));
foreach (var current in new[] { "0.3.0", "0.3.0+abc", "0.4.0", "0.4.0-preview.1" }) Check("no downgrade from " + current, !found!.IsNewerThan(current));
Check("numeric version sorting", (await Find([Release("v0.9.0"),Release("v0.10.0")]))?.Version == "0.10.0");
Check("architecture missing does not offer x64", await Find([Release("v0.3.0","arm64")]) is null);
Check("ARM64 match", (await Find([Release("v0.3.0","arm64")],"arm64"))?.Version == "0.3.0");
Check("x86 match", (await Find([Release("v0.3.0","x86")],"x86"))?.Version == "0.3.0");
Check("bundled runtime excluded", await Find([Release("v0.3.0",bundled:true)]) is null);
Check("empty asset excluded", await Find([Release("v0.3.0",size:0)]) is null);
Check("invalid stable tags excluded", await Find([Release("v01.0.0"),Release("v0.3"),Release("v2147483648.0.0")]) is null);
foreach (var url in new[] { "https://example.com/setup.exe", "http://github.com/Nomoziya/nomozip/releases/download/v0.3.0/nomozip-0.3.0-light-setup-win-x64.exe", "https://github.com/other/nomozip/releases/download/v0.3.0/nomozip-0.3.0-light-setup-win-x64.exe" })
    Check("foreign or insecure asset " + url, await Find([Release("v0.3.0",url:url)]) is null);
var pages = new List<string>();
using (var client = new HttpClient(new Handler((request,_) =>
{
    pages.Add(request.RequestUri!.AbsoluteUri);
    Check("anonymous API with accept and user agent", request.Headers.Authorization is null && request.Headers.Accept.Any() && request.Headers.UserAgent.Any());
    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(pages.Count == 1 ? Enumerable.Repeat(Release("engine-26.04"),100).ToArray() : new[] { Release("v0.5.0") })) });
})))
{
    Check("application found after full engine-only page", (await new GitHubApplicationChannel(client).CheckAsync("x64"))?.Version == "0.5.0");
    Check("separate list endpoint pagination", pages.Count == 2 && pages[1].EndsWith("per_page=100&page=2") && pages.All(page => !page.Contains("latest")));
}
foreach (var status in new[] { HttpStatusCode.Forbidden,HttpStatusCode.TooManyRequests,HttpStatusCode.ServiceUnavailable })
{
    using var client = new HttpClient(new Handler((_,_) => Task.FromResult(new HttpResponseMessage(status))));
    try { await new GitHubApplicationChannel(client).CheckAsync("x64"); Check("failure preserved " + status,false); }
    catch (HttpRequestException error) { Check("failure preserved " + status,error.StatusCode == status); }
}
using (var client = new HttpClient(new Handler(async (_,token) => { await Task.Delay(Timeout.Infinite,token); return new HttpResponseMessage(); })))
{
    using var cancel = new CancellationTokenSource(30);
    try { await new GitHubApplicationChannel(client).CheckAsync("x64",cancel.Token); Check("cancellation",false); }
    catch (OperationCanceledException) { Check("cancellation",true); }
}
if (args.Contains("--online"))
{
    using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
    var actual = await new GitHubApplicationChannel(client).CheckAsync("x64");
    Check("public application stable exists", actual is not null);
    await File.WriteAllTextAsync(Path.Combine(root,"artifacts/application-update-public-validation.json"),JsonSerializer.Serialize(new { passed=actual is not null,release=actual,anonymous=true }));
}
await File.WriteAllTextAsync(Path.Combine(root,"artifacts/application-update-validation.json"),JsonSerializer.Serialize(new { passed=checks.Values.All(value=>value),total=checks.Count,checks }));
return checks.Values.All(value=>value) ? 0 : 1;

sealed class Handler(Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> action) : HttpMessageHandler
{ protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token) => action(request,token); }
