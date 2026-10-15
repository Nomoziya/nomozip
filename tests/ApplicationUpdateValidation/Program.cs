using System.Net;
using System.Text.Json;
using ArchivePassword.Infrastructure;
using System.Security.Cryptography;

var root = Path.GetFullPath(args[0]);
var checks = new Dictionary<string,bool>();
void Check(string name, bool passed) { checks[name] = passed; Console.WriteLine($"{(passed ? "PASS" : "FAIL")} {name}"); }
object Release(string tag, string arch = "x64", bool preview = false, bool draft = false, string? url = null, long size = 1024, bool bundled = false, string? digest = null)
{
    var name = $"nomozip-{tag[1..]}-{(bundled ? "" : "light-")}setup-win-{arch}.exe";
    return new { tag_name=tag, prerelease=preview, draft, assets=new[] { new { name, size, digest, browser_download_url=url ?? $"https://github.com/Nomoziya/nomozip/releases/download/{tag}/{name}" } } };
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
var cache=Path.Combine(root,"artifacts/application-update-data",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(cache);
try
{
    var bytes=Enumerable.Range(0,130000).Select(v=>(byte)(v%251)).ToArray();
    var sha=Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    var good=(await Find([Release("v99.0.0",size:bytes.Length,digest:"sha256:"+sha)]))!;
    Check("release digest and exact size retained",good.CanDownload && good.InstallerSha256==sha && good.InstallerSize==bytes.Length);
    foreach(var digest in new string?[]{null,"sha1:"+sha,"sha256:bad","sha256:"+new string('z',64)})
    {
        var metadata=(await Find([Release("v99.0.0",digest:digest)]))!;
        Check("legacy or malformed digest still offers page only "+(digest??"null"),!metadata.CanDownload && metadata.ReleaseUrl==good.ReleaseUrl);
    }
    Check("unreasonably large installer not automatically downloadable",!(await Find([Release("v99.0.0",size:ApplicationInstallerDownload.MaximumBytes+1,digest:"sha256:"+sha)]))!.CanDownload);
    using var client=new HttpClient(new Handler((request,_)=>
    {
        Check("download exact fixed product asset anonymous",request.RequestUri==good.InstallerUrl && request.Headers.Authorization is null && request.Headers.UserAgent.Any());
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(bytes)});
    }));
    var progress=new List<int>();
    var path=await new ApplicationInstallerDownload(client).DownloadAsync(good,"x64",cache,new InlineProgress(v=>progress.Add(v)));
    Check("actual downloaded file exact bytes",File.ReadAllBytes(path).SequenceEqual(bytes));
    Check("progress advances to 100 monotonically",progress.Count>1 && progress[^1]==100 && progress.SequenceEqual(progress.Order()));
    using(var locked=ApplicationInstallerDownload.OpenVerified(path,good))
    {
        var refused=false;try{using var overwrite=new FileStream(path,FileMode.Create,FileAccess.Write);}catch(IOException){refused=true;}
        Check("reverified installer held against replacement",refused && locked.Position==0);
    }
    File.WriteAllBytes(path,[1,2,3]);
    var tampered=false;try{using var locked=ApplicationInstallerDownload.OpenVerified(path,good);}catch(InvalidDataException){tampered=true;}
    Check("changed prepared file refused at install time",tampered);File.Delete(path);
    var wrongSize=false;try{await new ApplicationInstallerDownload(client).DownloadAsync(good with {InstallerSize=bytes.Length+1},"x64",cache);}catch(InvalidDataException){wrongSize=true;}
    Check("header length mismatch rejected and partial removed",wrongSize && !Directory.EnumerateFiles(cache).Any());
    var wrongHash=false;try{await new ApplicationInstallerDownload(client).DownloadAsync(good with {InstallerSha256=new string('0',64)},"x64",cache);}catch(InvalidDataException){wrongHash=true;}
    Check("SHA mismatch rejected and partial removed",wrongHash && !Directory.EnumerateFiles(cache).Any());
    using var unbounded=new HttpClient(new Handler((_,_)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StreamContent(new NonSeekable(bytes))})));
    foreach(var declared in new[]{bytes.Length-1,bytes.Length+1})
    {
        var refused=false;try{await new ApplicationInstallerDownload(unbounded).DownloadAsync(good with {InstallerSize=declared},"x64",cache);}catch(InvalidDataException){refused=true;}
        Check("stream actual size enforced without Content-Length "+declared,refused && !Directory.EnumerateFiles(cache).Any());
    }
    using var cancelling=new HttpClient(new Handler(async(_,token)=>{await Task.Delay(Timeout.Infinite,token);return new HttpResponseMessage();}));
    using(var stop=new CancellationTokenSource(30))
    {
        var refused=false;try{await new ApplicationInstallerDownload(cancelling).DownloadAsync(good,"x64",cache,token:stop.Token);}catch(OperationCanceledException){refused=true;}
        Check("download cancellation cleans own partial",refused && !Directory.EnumerateFiles(cache).Any());
    }
    using(var stop=new CancellationTokenSource())
    {
        var wrote=false;var refused=false;
        try{await new ApplicationInstallerDownload(unbounded).DownloadAsync(good,"x64",cache,new InlineProgress(_=>{wrote=Directory.EnumerateFiles(cache,"*.partial").Any();stop.Cancel();}),stop.Token);}catch(OperationCanceledException){refused=true;}
        Check("mid-stream cancellation removes actual partial file",wrote && refused && !Directory.EnumerateFiles(cache).Any());
    }
    using(var failed=new HttpClient(new Handler((_,_)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)))))
    {
        var refused=false;try{await new ApplicationInstallerDownload(failed).DownloadAsync(good,"x64",cache);}catch(HttpRequestException){refused=true;}
        Check("HTTP download failure keeps cache empty",refused && !Directory.EnumerateFiles(cache).Any());
    }
    using(var stalled=new HttpClient(new Handler((_,_)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StreamContent(new StalledContent(bytes))}))))
    {
        var refused=false;try{await new ApplicationInstallerDownload(stalled,TimeSpan.FromMilliseconds(200)).DownloadAsync(good,"x64",cache);}catch(OperationCanceledException){refused=true;}
        Check("whole download deadline covers stalled response body after headers",refused && !Directory.EnumerateFiles(cache).Any());
    }
    var blocked=Path.Combine(cache,"blocked-cache");File.WriteAllText(blocked,"unrelated");
    var diskFailure=false;try{await new ApplicationInstallerDownload(client).DownloadAsync(good,"x64",blocked);}catch(IOException){diskFailure=true;}
    Check("unavailable cache preserves existing unrelated file",diskFailure && File.ReadAllText(blocked)=="unrelated");File.Delete(blocked);
    foreach(var bad in new[]{good with {InstallerUrl=new Uri("https://example.com/setup.exe")},good with {InstallerSha256=null},good with {InstallerSize=0}})
    {
        var refused=false;try{await new ApplicationInstallerDownload(client).DownloadAsync(bad,"x64",cache);}catch(InvalidDataException){refused=true;}
        Check("unsupported download metadata refused "+bad,refused && !Directory.EnumerateFiles(cache).Any());
    }
    var install=Path.Combine(cache,"a directory with 空格");var start=ApplicationInstallation.CreateStart(Path.Combine(cache,"setup.exe"),install);
    Check("installer launch explicit executable no shell correct NSIS path semantics",!start.UseShellExecute && start.Arguments=="/S /UPDATE /D="+Path.GetFullPath(install) && start.FileName==Path.GetFullPath(Path.Combine(cache,"setup.exe")));
    Check("registered installation exact own executable",ApplicationInstallation.Matches(Path.Combine(install,"ArchivePassword.exe"),install));
    Check("portable different copy cannot upgrade registered installation",!ApplicationInstallation.Matches(Path.Combine(cache,"ArchivePassword.exe"),install));
    var unrelated=Path.Combine(cache,"unrelated.txt");File.WriteAllText(unrelated,"keep");
    path=await new ApplicationInstallerDownload(client).DownloadAsync(good,"x64",cache);
    Check("successful retry keeps unrelated cache content",File.ReadAllText(unrelated)=="keep" && File.Exists(path));
}
finally {Directory.Delete(cache,true);}
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
sealed class InlineProgress(Action<int> report):IProgress<int>{public void Report(int value)=>report(value);}
sealed class NonSeekable(byte[] bytes):MemoryStream(bytes){public override bool CanSeek=>false;}
sealed class StalledContent(byte[] bytes):MemoryStream(bytes)
{
    public override bool CanSeek=>false;
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer,CancellationToken token=default)
    {if(Position>0){await Task.Delay(Timeout.Infinite,token);return 0;}return await base.ReadAsync(buffer,token);}
}
