using System.Buffers.Binary;
using System.IO.Compression;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using ArchivePassword.Core;
using ArchivePassword.Infrastructure;

if(args.Length>1 && args[1].Contains("vault",StringComparison.Ordinal)) return await CrossArchitectureVault.RunAsync(args);
var root=Path.GetFullPath(args[0]);var expected=args.Length>1 && args[1] is "x64" or "arm64"?args[1]:EngineArchitecture.Current;
var versions=args.Skip(1).Where(value=>value is not ("x64" or "arm64")).SelectMany(value=>value.Split(',')).Distinct().ToArray();
if(versions.Length==0) versions=["26.04","26.03"];
if(versions.Any(value=>!EngineUpdatePackage.VersionPattern().IsMatch(value))) throw new ArgumentException("Invalid engine version.");
var folder=Path.Combine(root,"artifacts/architecture-"+Guid.NewGuid().ToString("N")[..8]);Directory.CreateDirectory(folder);
var cases=new List<object>();int passed=0,total=0;
void Check(string name,bool value) {total++;if(value)passed++;cases.Add(new{name,passed=value});Console.WriteLine((value?"PASS ":"FAIL ")+name);}
Check("actual native OS and managed process",EngineArchitecture.Current==expected && RuntimeInformation.OSArchitecture.ToString().Equals(expected,StringComparison.OrdinalIgnoreCase));
Check("worker native PE",EngineArchitecture.IsNativeImage(await File.ReadAllBytesAsync(Path.Combine(root,"artifacts/worker/ArchiveEngineWorker.exe"))));
Check("profile directories preserve x64 layout and isolate ARM64",EngineArchitecture.InstalledDirectoryName==(expected=="x64"?"engines":"engines-arm64") && EngineArchitecture.SelectionFileName==(expected=="x64"?"active-engine.json":"active-engine-arm64.json"));
using var key=RSA.Create(3072);var publicKey=key.ExportSubjectPublicKeyInfoPem();var foreign=expected=="x64"?"arm64":"x64";
async Task<string> Package(string version,string architecture,byte[]? replacement=null,string? source=null)
{
    var payload=new Dictionary<string,byte[]>{["7z.dll"]=replacement??await File.ReadAllBytesAsync(Path.Combine(root,"engines",version,"7z.dll")),["License.txt"]="synthetic architecture test"u8.ToArray(),["compatibility-report.json"]="{}"u8.ToArray()};
    var hashes=payload.ToDictionary(pair=>pair.Key,pair=>Convert.ToHexString(SHA256.HashData(pair.Value)).ToLowerInvariant());
    var manifest=new EngineUpdateManifest(1,version,architecture,1,"0.1",source??$"https://github.com/ip7z/7zip/releases/download/{version}/7z{version.Replace(".","")}-{architecture}.exe",hashes);
    var bytes=JsonSerializer.SerializeToUtf8Bytes(manifest,EngineUpdatePackage.JsonOptions);var signature=key.SignData(bytes,HashAlgorithmName.SHA256,RSASignaturePadding.Pss);
    var file=Path.Combine(folder,Guid.NewGuid().ToString("N")+".zip");using var output=File.Create(file);using var zip=new ZipArchive(output,ZipArchiveMode.Create);
    void Add(string name,byte[] value){using var stream=zip.CreateEntry(name).Open();stream.Write(value);}
    Add("manifest.json",bytes);Add("manifest.sig",signature);foreach(var pair in payload)Add("engine/"+pair.Key,pair.Value);return file;
}
async Task Reject(string name,Func<Task> action,string code)
{
    try {await action();Check(name,false);}
    catch(UpdateException error){Check(name,error.Code==code);}
    catch(Exception error)
    {
        Console.WriteLine(JsonSerializer.Serialize(new{name,exception=error.ToString(),win32Code=error.HResult&0xffff,
            retainedDlls=Directory.GetFiles(folder,"7z.dll",SearchOption.AllDirectories).Select(path=>new{path,attributes=File.GetAttributes(path).ToString()})}));
        Check(name,false);
    }
}
var nativePackages=new Dictionary<string,string>();var brokenPackages=new Dictionary<string,string>();
foreach(var version in versions)
{
    var dll=await File.ReadAllBytesAsync(Path.Combine(root,"engines",version,"7z.dll"));
    Check(version+" official DLL and CLI native PE",EngineArchitecture.IsNativeImage(dll) && EngineArchitecture.IsNativeImage(await File.ReadAllBytesAsync(Path.Combine(root,"engines",version,"7z.exe"))));
    Check(version+" foreign image rejected by explicit target",!EngineArchitecture.IsNativeImage(dll,foreign));
    var archive=await Package(version,expected);nativePackages[version]=archive;
    var accepted=await EngineUpdatePackage.ReadAsync(archive,publicKey,CancellationToken.None);
    Check(version+" actual signed native package accepted",accepted.Manifest.Architecture==expected && accepted.Payload["7z.dll"].SequenceEqual(dll));
    var wrong=await Package(version,foreign);await Reject(version+" signed foreign manifest rejected",()=>EngineUpdatePackage.ReadAsync(wrong,publicKey,CancellationToken.None),"incompatible_update");
    var wrongImage=dll.ToArray();var offset=BinaryPrimitives.ReadInt32LittleEndian(wrongImage.AsSpan(60));BinaryPrimitives.WriteUInt16LittleEndian(wrongImage.AsSpan(offset+4),foreign=="x64"?(ushort)0x8664:(ushort)0xaa64);
    wrong=await Package(version,expected,wrongImage);await Reject(version+" native signed manifest with foreign PE rejected",()=>EngineUpdatePackage.ReadAsync(wrong,publicKey,CancellationToken.None),"incompatible_update");
    wrong=await Package(version,expected,source:$"https://github.com/ip7z/7zip/releases/download/{version}/7z{version.Replace(".","")}-{foreign}.exe");
    await Reject(version+" foreign official URL rejected",()=>EngineUpdatePackage.ReadAsync(wrong,publicKey,CancellationToken.None),"invalid_manifest");
    var builtin=Path.Combine(folder,"catalog-"+version);var entry=Path.Combine(builtin,version);Directory.CreateDirectory(entry);await File.WriteAllBytesAsync(Path.Combine(entry,"7z.dll"),dll);
    async Task SetManifest(string architecture,byte[] image){await File.WriteAllBytesAsync(Path.Combine(entry,"7z.dll"),image);await File.WriteAllTextAsync(Path.Combine(entry,"manifest.json"),JsonSerializer.Serialize(new{version,architecture,protocol=1,dllSha256=Convert.ToHexString(SHA256.HashData(image))}));}
    await SetManifest(expected,dll);Check(version+" correct builtin catalog recognized",(await new EngineCatalog(builtin).ReadVerifiedVersionsAsync()).SequenceEqual(new[]{version}));
    await SetManifest(foreign,dll);Check(version+" foreign catalog architecture ignored",(await new EngineCatalog(builtin).ReadVerifiedVersionsAsync()).Count==0);
    await SetManifest(expected,wrongImage);Check(version+" incorrect PE despite matching manifest hash ignored",(await new EngineCatalog(builtin).ReadVerifiedVersionsAsync()).Count==0);
    var noExports=dll.ToArray();Array.Clear(noExports,offset+24+112,8);
    var broken=await Package(version,expected,noExports);
    brokenPackages[version]=broken;
    var brokenUpdater=new EngineUpdateService(Path.Combine(folder,"broken-"+version),Path.Combine(folder,"broken-state-"+version),publicKey,
        Path.Combine(root,"artifacts/worker/ArchiveEngineWorker.exe"),Path.Combine(root,"tests/fixtures/generated/encrypted-header.7z"),new EngineCatalog(Path.Combine(root,"engines")));
    await Reject(version+" native signed PE with missing exports fails real worker probe",()=>brokenUpdater.InstallAsync(broken),"engine_validation_failed");
}
{
    var cleanupRoot=Path.Combine(folder,"cleanup-lock");FileStream? held=null;Task release=Task.CompletedTask;
    var cleanupUpdater=new EngineUpdateService(cleanupRoot,Path.Combine(folder,"cleanup-state"),publicKey,
        Path.Combine(root,"artifacts/worker/ArchiveEngineWorker.exe"),Path.Combine(root,"tests/fixtures/generated/encrypted-header.7z"),new EngineCatalog(Path.Combine(root,"engines"),cleanupRoot,publicKey));
    var progress=new InlineProgress<string>(message=>
    {
        if(message!=UiText.Get("T_5679ccc535d2"))return;
        var dll=Directory.GetFiles(cleanupRoot,"7z.dll",SearchOption.AllDirectories).Single();
        held=new FileStream(dll,FileMode.Open,FileAccess.Read,FileShare.Read);
        release=Task.Run(async()=>{await Task.Delay(300);held.Dispose();});
    });
    try {await Reject("temporary preparation DLL lock preserves validation failure",()=>cleanupUpdater.InstallAsync(brokenPackages["26.04"],progress),"engine_validation_failed");}
    finally {await release;held?.Dispose();}
    var installedAfterFailure=await cleanupUpdater.InstallAsync(nativePackages["26.04"]).WaitAsync(TimeSpan.FromSeconds(15));
    Check("failed preparation releases gate and cleans directory before next install",Directory.Exists(installedAfterFailure) && !Directory.GetDirectories(cleanupRoot,".prepare-*").Any());
}
var installed=Path.Combine(folder,EngineArchitecture.InstalledDirectoryName);var state=Path.Combine(folder,EngineArchitecture.SelectionFileName);
var catalog=new EngineCatalog(Path.Combine(root,"engines"),installed,publicKey);
var updater=new EngineUpdateService(installed,state,publicKey,Path.Combine(root,"artifacts/worker/ArchiveEngineWorker.exe"),Path.Combine(root,"tests/fixtures/generated/encrypted-header.7z"),catalog);
await updater.InstallAsync(nativePackages["26.03"]);await updater.ActivateAsync("26.03");await updater.InstallAsync(nativePackages["26.04"]);await updater.ActivateAsync("26.04");
var selection=await updater.ReadSelectionAsync();Check("real native installation activation and previous version",selection?.Active=="26.04" && selection.Previous=="26.03");
await updater.RollbackAsync();Check("native engine rollback preserves previous version",(await updater.ReadSelectionAsync())?.Active=="26.03");
var before=File.ReadAllBytes(state);var otherPackage=await Package("26.04",foreign);await Reject("foreign install refused before state change",()=>updater.InstallAsync(otherPackage),"incompatible_update");Check("foreign install preserves state and clears preparation",before.SequenceEqual(File.ReadAllBytes(state)) && !Directory.GetDirectories(installed,".prepare-*").Any());
var assets=new[]{foreign,expected}.Select(architecture=>new{name=$"ArchivePassword-engine-26.04-win-{architecture}.zip",size=10,browser_download_url=$"https://github.com/Nomoziya/nomozip/releases/download/engine-26.04/ArchivePassword-engine-26.04-win-{architecture}.zip"}).ToArray();
object ChannelBody(object release)=>expected=="arm64"?new[]{release}:release;
using(var client=new HttpClient(new FixtureHandler(ChannelBody(new{draft=false,prerelease=false,assets}))))
{var found=await new GitHubEngineChannel(client).CheckAsync("Nomoziya/nomozip");Check("channel selects only native asset in mixed release",found?.DownloadUrl.AbsoluteUri.EndsWith("win-"+expected+".zip")==true);}
using(var client=new HttpClient(new FixtureHandler(ChannelBody(new{draft=false,prerelease=false,assets=assets.Take(1).ToArray()}))))
{Check("foreign-only engine release is not offered",await new GitHubEngineChannel(client).CheckAsync("Nomoziya/nomozip") is null);}
var softwareAsset=new{name=$"nomozip-0.3.0-light-setup-win-{expected}.exe",size=10,browser_download_url=$"https://github.com/Nomoziya/nomozip/releases/download/v0.3.0/nomozip-0.3.0-light-setup-win-{expected}.exe"};
if(expected=="arm64")
{
    var page=new PageHandler(Enumerable.Repeat<object>(new{draft=false,prerelease=false,assets=assets.Take(1).ToArray()},100).ToArray(),new object[]{new{draft=false,prerelease=false,assets}});
    using var client=new HttpClient(page);
    Check("ARM64 release behind full x64 history page is discovered",(await new GitHubEngineChannel(client).CheckAsync("Nomoziya/nomozip"))?.Version=="26.04" && page.Requests==2);
}
var softwareRelease=new{draft=false,prerelease=false,tag_name="v0.3.0",assets=new[]{softwareAsset}};
using(var client=new HttpClient(new FixtureHandler(new[]{softwareRelease})))
{Check("software channel offers matching lightweight architecture",(await new GitHubApplicationChannel(client).CheckAsync(expected))?.InstallerUrl.AbsoluteUri.EndsWith("win-"+expected+".exe")==true);}
var engineHashes=versions.ToDictionary(version=>version,version=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(root,"engines",version,"7z.dll")))).ToLowerInvariant());
await File.WriteAllTextAsync(Path.Combine(root,"artifacts/architecture-validation.json"),JsonSerializer.Serialize(new{passed,total,cases,architecture=EngineArchitecture.Current,osArchitecture=RuntimeInformation.OSArchitecture.ToString(),nativeProcess=true,syntheticOnly=true,engineHashes},new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine($"{passed}/{total} passed");return passed==total?0:1;
sealed class FixtureHandler(object body):HttpMessageHandler
{protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(JsonSerializer.Serialize(body))});}
sealed class PageHandler(object first,object second):HttpMessageHandler
{
    public int Requests{get;private set;}
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
    {Requests++;return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(JsonSerializer.Serialize(Requests==1?first:second))});}
}
sealed class InlineProgress<T>(Action<T> report):IProgress<T>{public void Report(T value)=>report(value);}
