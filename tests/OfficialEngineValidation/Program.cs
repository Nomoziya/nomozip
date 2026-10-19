using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ArchivePassword.Core;
using ArchivePassword.Infrastructure;

var root=Path.GetFullPath(args[0]);var integration=args.Contains("--integration");
if(integration && Environment.GetEnvironmentVariable("GITHUB_ACTIONS")!="true")throw new InvalidOperationException("Real engine integration requires isolated CI.");
var folder=Path.Combine(root,"artifacts/official-engine-validation",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
var checks=new Dictionary<string,bool>();
void Check(string name,bool passed){checks.Add(name,passed);Console.WriteLine((passed?"PASS ":"FAIL ")+name);if(!passed)throw new Exception(name);}
async Task Reject(string name,Func<Task> action,string? code=null)
{try{await action();Check(name,false);}catch(UpdateException e){Check(name,code is null || code==e.Code);}}
var payload=Encoding.UTF8.GetBytes(new string('a',200000));var digest=Convert.ToHexString(SHA256.HashData(payload));
object Asset(string arch,string? hash=null,string? url=null)=>new {name=$"7z2604-{arch}.exe",size=payload.LongLength,digest=hash ?? "sha256:"+digest,browser_download_url=url ?? OfficialEngineChannel.DownloadUrl("26.04",arch).AbsoluteUri};
string Release(object[]? assets=null,bool prerelease=false,bool draft=false,string version="26.04")=>JsonSerializer.Serialize(new{tag_name=version,prerelease,draft,assets=assets ?? [Asset("x64"),Asset("arm64")]});
HttpClient Client(string json,byte[]? bytes=null)=>new(new FixtureHandler(json,bytes ?? payload));
using(var client=Client(Release()))
{
    var channel=new OfficialEngineChannel(client);var found=await channel.CheckAsync();
    Check("stable official release selects native architecture",found is {Version:"26.04"} && found.Architecture==EngineArchitecture.Current);
    var path=Path.Combine(folder,"download.exe");await channel.DownloadAsync(found!,path);
    Check("official SHA256 download exact bytes",File.ReadAllBytes(path).SequenceEqual(payload));
    await File.WriteAllTextAsync(path,"keep existing download");
    try{await channel.DownloadAsync(found!,path);Check("existing download protected",false);}catch(IOException){Check("existing download protected",await File.ReadAllTextAsync(path)=="keep existing download");}
    await Reject("cross architecture rejected",()=>Task.Run(()=>OfficialEngineChannel.Validate(found! with {Architecture=EngineArchitecture.Current=="x64"?"arm64":"x64"})),"invalid_official_update");
    await Reject("asset size limit",()=>Task.Run(()=>OfficialEngineChannel.Validate(found! with {Size=long.MaxValue})),"invalid_official_update");
}
foreach(var test in new[]{("draft",Release(draft:true)),("prerelease",Release(prerelease:true)),("missing architecture",Release([]))})
{using var client=Client(test.Item2);Check(test.Item1+" is not offered",await new OfficialEngineChannel(client).CheckAsync() is null);}
foreach(var test in new[]{
    ("foreign URL",Release([Asset(EngineArchitecture.Current,url:"https://example.org/engine.exe")]),"invalid_official_update"),
    ("HTTP URL",Release([Asset(EngineArchitecture.Current,url:OfficialEngineChannel.DownloadUrl("26.04",EngineArchitecture.Current).AbsoluteUri.Replace("https:","http:"))]),"invalid_official_update"),
    ("malformed URL",Release([Asset(EngineArchitecture.Current,url:"not a URL")]),"invalid_official_update"),
    ("malformed metadata","{}","invalid_official_update"),
    ("missing digest",Release([Asset(EngineArchitecture.Current,hash:"")]),"official_digest_unavailable"),
    ("invalid digest",Release([Asset(EngineArchitecture.Current,hash:"sha256:123")]),"invalid_official_update"),
    ("duplicate assets",Release([Asset(EngineArchitecture.Current),Asset(EngineArchitecture.Current)]),"invalid_official_update"),
    ("invalid version",Release(version:"../../26.04"),"invalid_official_update")})
{using var client=Client(test.Item2);await Reject(test.Item1,async()=>{await new OfficialEngineChannel(client).CheckAsync();},test.Item3);}
foreach(var test in new[]{("tampered download",payload.Select(value=>(byte)(value^1)).ToArray(),"hash_mismatch"),("truncated download",payload[..100],"invalid_download"),("oversized download",new byte[payload.Length+1],"invalid_download")})
{
    using var client=Client(Release(),test.Item2);var channel=new OfficialEngineChannel(client);var found=(await channel.CheckAsync())!;var path=Path.Combine(folder,Guid.NewGuid()+".exe");
    await Reject(test.Item1,()=>channel.DownloadAsync(found,path),test.Item3);Check(test.Item1+" has no temporary file",!File.Exists(path));
}
using(var client=Client(Release()))
using(var cancelled=new CancellationTokenSource())
{
    var channel=new OfficialEngineChannel(client);var found=(await channel.CheckAsync())!;var path=Path.Combine(folder,"cancel.exe");
    var progress=new CancelProgress(cancelled);
    try{await channel.DownloadAsync(found,path,progress,cancelled.Token);Check("cancel download",false);}catch(OperationCanceledException){Check("cancel download removes partial file",!File.Exists(path));}
}

if(integration)
{
    using var client=new HttpClient {Timeout=TimeSpan.FromMinutes(2)};
    var channel=new OfficialEngineChannel(client);var found=await channel.CheckAsync();Check("real official release has native SHA256",found is not null);
    var installer=Path.Combine(folder,"official.exe");await channel.DownloadAsync(found!,installer);
    Check("real upstream installer downloaded",File.Exists(installer));
    var installed=Path.Combine(folder,"engines");var state=Path.Combine(folder,"selection.json");
    var publicKey=await File.ReadAllTextAsync(Path.Combine(root,"artifacts/update-test-assets/public.pem"));
    var catalog=new EngineCatalog(Path.Combine(root,"engines"),installed,publicKey);
    var updater=new EngineUpdateService(installed,state,publicKey,Path.Combine(root,"artifacts/worker/ArchiveEngineWorker.exe"),Path.Combine(root,"tests/fixtures/generated/encrypted-header.7z"),catalog);
    await catalog.ReadVerifiedVersionsAsync();await updater.ActivateAsync("26.03");var before=await File.ReadAllBytesAsync(state);
    using(var cancelled=new CancellationTokenSource())
    {
        cancelled.Cancel();try{await updater.InstallOfficialAsync(found!,installer,"26.03",token:cancelled.Token);Check("cancel install",false);}catch(OperationCanceledException){Check("cancel install preserves selection",File.ReadAllBytes(state).SequenceEqual(before));}
    }
    var damaged=Path.Combine(folder,"damaged.exe");await File.WriteAllBytesAsync(damaged,"invalid"u8.ToArray());
    await Reject("tampered installer rejected before extraction",()=>updater.InstallOfficialAsync(found!,damaged,"26.03"),"hash_mismatch");
    Check("failed preparation preserves selection",File.ReadAllBytes(state).SequenceEqual(before));
    var version=await updater.InstallOfficialAsync(found!,installer,"26.03");
    Check("official install does not autoactivate",File.ReadAllBytes(state).SequenceEqual(before));
    await updater.ActivateAsync(version);Check("official version activated",(await updater.ReadSelectionAsync()) is {Active:var active,Previous:"26.03"} && active==version);
    var official=Path.Combine(catalog.OfficialDirectory!,version);var receipt=await OfficialEngineReceipt.VerifyInstalledAsync(official);
    Check("upstream provenance retained",receipt.Update.Sha256==found!.Sha256 && receipt.Files.Count==4);
    Check("official SFX module retained",(await catalog.ReadSfxModuleAsync(version)).IsValid());
    var reopened=new EngineCatalog(Path.Combine(root,"engines"),installed,publicKey);await reopened.ReadVerifiedVersionsAsync();
    Check("official engine verified after restart",reopened.DllPath(version).StartsWith(catalog.OfficialDirectory!,StringComparison.OrdinalIgnoreCase));
    var receiptPath=Path.Combine(official,OfficialEngineReceipt.FileName);var receiptBytes=await File.ReadAllBytesAsync(receiptPath);receiptBytes[^1]^=1;await File.WriteAllBytesAsync(receiptPath,receiptBytes);
    await Reject("protected receipt tampering rejected",async()=>{await OfficialEngineReceipt.VerifyInstalledAsync(official);},"invalid_official_update");
    receiptBytes[^1]^=1;await File.WriteAllBytesAsync(receiptPath,receiptBytes);
    var dll=Path.Combine(official,"7z.dll");var saved=await File.ReadAllBytesAsync(dll);await File.WriteAllBytesAsync(dll,"tampered"u8.ToArray());
    await Reject("installed DLL tampering rejected",async()=>{await OfficialEngineReceipt.VerifyInstalledAsync(official);},"hash_mismatch");
    var remaining=await reopened.ReadVerifiedVersionsAsync();Check("tampered official engine excluded",!remaining.Contains(version) || !reopened.DllPath(version).StartsWith(catalog.OfficialDirectory!,StringComparison.OrdinalIgnoreCase));
    await File.WriteAllBytesAsync(dll,saved);await updater.RollbackAsync();Check("rollback to previous engine",(await updater.ReadSelectionAsync())?.Active=="26.03");
    await catalog.ReadVerifiedVersionsAsync();
    Check("official update never creates a nomozip signature",!File.Exists(Path.Combine(official,"update-manifest.sig")));
    Check("staging cleaned",!Directory.EnumerateDirectories(catalog.OfficialDirectory!,".prepare-*").Any());
    File.Delete(installer);
}
await File.WriteAllTextAsync(Path.Combine(root,"artifacts/official-engine-validation.json"),JsonSerializer.Serialize(new{passed=checks.Values.All(value=>value),architecture=EngineArchitecture.Current,realPublicNetwork=integration,total=checks.Count,checks},new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine($"{checks.Count}/{checks.Count} passed");

sealed class CancelProgress(CancellationTokenSource source):IProgress<string>{public void Report(string value)=>source.Cancel();}
sealed class FixtureHandler(string json,byte[] payload):HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){RequestMessage=request,Content=request.RequestUri!.Host=="api.github.com"?new StringContent(json,Encoding.UTF8,"application/json"):new ByteArrayContent(payload)});
    }
}
