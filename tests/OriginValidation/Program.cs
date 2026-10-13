using System.Text;
using System.Text.Json;
using ArchivePassword.Core;
using ArchivePassword.Infrastructure;

var root=Path.GetFullPath(args[0]);var data=Path.Combine(root,"artifacts/origin-validation",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(data);
var checks=new Dictionary<string,bool>();void Check(string name,bool value) {checks.Add(name,value);Console.WriteLine((value?"PASS ":"FAIL ")+name);}
var marker=new WindowsExtractionOrigin();var zone3=Encoding.UTF8.GetBytes("[ZoneTransfer]\r\nZoneId=3\r\nReferrerUrl=https://example.invalid/下载\r\nHostUrl=https://example.invalid/file.zip?test=1\r\n");var zone4="[ZoneTransfer]\r\nZoneId=4\r\n"u8.ToArray();
var source=Path.Combine(data,"源 文件.txt");File.WriteAllText(source,"actual content");
var emptyDirectory=Path.Combine(data,"empty-source");Directory.CreateDirectory(emptyDirectory);
var entries=new[] {new CompressionEntry(source,"folder/a.txt",false),new CompressionEntry(source,"b.txt",false),new CompressionEntry(emptyDirectory,"empty",true)};
var versions=args.Length>1?args[1].Split(',').Distinct():new[] {"26.04","26.03"};
var externalOutput=args.Length>2?Path.GetFullPath(args[2]):null;
foreach(var version in versions) foreach(var format in new[] {"zip","7z"})
{
    var tag=version+"-"+format;var engine=new ArchiveEngineClient(Path.Combine(root,"artifacts/worker/ArchiveEngineWorker.exe"),Path.Combine(root,$"engines/{version}/7z.dll"));
    var archive=Path.Combine(data,tag+"."+format);var created=await engine.CreateAsync(new(entries,format,5,false),archive,"ascii-password");Check(tag+" actual encrypted archive",created.IsSuccess);
    File.WriteAllBytes(archive+":private",new byte[] {7,8,9});var archiveBytes=File.ReadAllBytes(archive);await File.WriteAllBytesAsync(archive+":Zone.Identifier",zone3);
    var vaultPath=Path.Combine(data,tag+".apv");var vault=new DpapiPasswordVault(vaultPath);await vault.SaveAsync(new(Guid.NewGuid(),"test","ascii-password","",true,DateTimeOffset.UtcNow));
    var service=new ExtractionService(engine,vault,new PasswordCandidateProvider(vault),marker);
    Task<ExtractionOutcome> Extract(string output,ExistingFilePolicy policy=ExistingFilePolicy.Replace,IReadOnlyList<ArchiveEntry>? selection=null)=>service.ExtractAsync(archive,output,true,_=>Task.FromResult<PasswordPromptResponse?>(null),_=>Task.FromResult(policy),selection:selection);
    var output=externalOutput is null?Path.Combine(data,tag+"-all"):Path.Combine(externalOutput,tag+"-all");var result=await Extract(output);
    Check(tag+" actual output bytes unchanged",result.Engine.IsSuccess && File.ReadAllText(Path.Combine(output,"folder/a.txt"))=="actual content" && File.ReadAllText(Path.Combine(output,"b.txt"))=="actual content");
    if(externalOutput is not null)
    {
        Check(tag+" unsupported filesystem warning",result.Warning?.Contains("不支持来源标记")==true);
        Check(tag+" no fabricated regular marker file",!Directory.GetFiles(output,"*",SearchOption.AllDirectories).Any(path=>Path.GetFileName(path).Contains("Zone.Identifier")));
        Check(tag+" unsupported output still completes and cleans staging",Directory.Exists(Path.Combine(output,"empty")) && Directory.GetDirectories(output,".archive-password-*").Length==0);
        var listingFat=await engine.ListAsync(archive);var selectedFat=listingFat.Items.Single(item=>item.Path=="b.txt");var selectedFatOut=Path.Combine(externalOutput,tag+"-selected");var selectedFatResult=await Extract(selectedFatOut,selection:[selectedFat]);
        Check(tag+" actual selected exFAT output succeeds",selectedFatResult.Engine.IsSuccess && File.ReadAllText(Path.Combine(selectedFatOut,"b.txt"))=="actual content");
        Check(tag+" selected exFAT warning remains visible",selectedFatResult.Warning?.Contains("不支持来源标记")==true);
        Check(tag+" selected exFAT contains only requested file",Directory.GetFiles(selectedFatOut,"*",SearchOption.AllDirectories).Length==1);
        File.Delete(archive+":Zone.Identifier");var localFat=await Extract(Path.Combine(externalOutput,tag+"-local"));
        Check(tag+" local unmarked archive on exFAT has no spurious warning",localFat.Engine.IsSuccess && localFat.Warning is null);
        continue;
    }
    Check(tag+" actual NTFS streams preserved byte for byte",File.ReadAllBytes(Path.Combine(output,"folder/a.txt")+":Zone.Identifier").SequenceEqual(zone3) && File.ReadAllBytes(Path.Combine(output,"b.txt")+":Zone.Identifier").SequenceEqual(zone3));
    Check(tag+" directory is not marked and no unrelated ADS copied",!File.Exists(Path.Combine(output,"empty")+":Zone.Identifier") && !File.Exists(Path.Combine(output,"b.txt")+":private"));
    Check(tag+" source archive and origin not modified",File.ReadAllBytes(archive).SequenceEqual(archiveBytes) && File.ReadAllBytes(archive+":Zone.Identifier").SequenceEqual(zone3));
    var listing=await engine.ListAsync(archive);var chosen=listing.Items.Single(item=>item.Path=="b.txt");var selectedOut=Path.Combine(data,tag+"-selected");var selected=await Extract(selectedOut,selection:[chosen]);
    Check(tag+" selected extraction marks only selected output",selected.Engine.IsSuccess && Directory.GetFiles(selectedOut,"*",SearchOption.AllDirectories).Length==1 && File.ReadAllBytes(Path.Combine(selectedOut,"b.txt")+":Zone.Identifier").SequenceEqual(zone3));
    var skipOut=Path.Combine(data,tag+"-skip");Directory.CreateDirectory(Path.Combine(skipOut,"folder"));File.WriteAllText(Path.Combine(skipOut,"folder/a.txt"),"keep");File.WriteAllBytes(Path.Combine(skipOut,"folder/a.txt")+":Zone.Identifier",zone4);
    var skipped=await Extract(skipOut,ExistingFilePolicy.Skip);
    Check(tag+" skip preserves target content and its prior origin",skipped.Engine.IsSuccess && File.ReadAllText(Path.Combine(skipOut,"folder/a.txt"))=="keep" && File.ReadAllBytes(Path.Combine(skipOut,"folder/a.txt")+":Zone.Identifier").SequenceEqual(zone4));
    Check(tag+" non skipped output gets source origin",File.ReadAllBytes(Path.Combine(skipOut,"b.txt")+":Zone.Identifier").SequenceEqual(zone3));
    File.Delete(archive+":Zone.Identifier");var localOut=Path.Combine(data,tag+"-local");var local=await Extract(localOut);
    Check(tag+" local unmarked archive stays unmarked",local.Engine.IsSuccess && !File.Exists(Path.Combine(localOut,"b.txt")+":Zone.Identifier"));
    var replacedLocal=await Extract(output);Check(tag+" local replacement removes obsolete target origin",replacedLocal.Engine.IsSuccess && !File.Exists(Path.Combine(output,"b.txt")+":Zone.Identifier"));
    foreach(var id in new[] {0,1,2,3,4})
    {
        var mark=Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes($"[ZoneTransfer]\r\nZoneId={id}\r\nHostUrl=https://example.invalid/utf16\r\n")).ToArray();File.WriteAllBytes(archive+":Zone.Identifier",mark);
        var zoneOut=Path.Combine(data,tag+"-zone"+id);var zoned=await Extract(zoneOut,selection:[chosen]);Check(tag+$" preserves original zone {id} and UTF16 bytes without inventing Internet",zoned.Engine.IsSuccess && File.ReadAllBytes(Path.Combine(zoneOut,"b.txt")+":Zone.Identifier").SequenceEqual(mark));
    }
    File.WriteAllBytes(archive+":Zone.Identifier",zone3);
    var snapshot=await marker.ReadAsync(archive,CancellationToken.None);File.WriteAllBytes(archive+":Zone.Identifier",zone4);var snapshotFile=Path.Combine(data,tag+"-snapshot.txt");File.WriteAllText(snapshotFile,"snapshot");await snapshot!.ApplyAsync([snapshotFile],CancellationToken.None);
    Check(tag+" origin captured before work and not reread during commit",File.ReadAllBytes(snapshotFile+":Zone.Identifier").SequenceEqual(zone3));File.WriteAllBytes(archive+":Zone.Identifier",zone3);
    var unknownCapabilities=new WindowsExtractionOrigin(_=>null);var fallbackMark=await unknownCapabilities.ReadAsync(archive,CancellationToken.None);var fallbackFile=Path.Combine(data,tag+"-fallback.txt");File.WriteAllText(fallbackFile,"fallback");await fallbackMark!.ApplyAsync([fallbackFile],CancellationToken.None);
    Check(tag+" simulated unknown volume capability falls back to actual stream IO",File.ReadAllBytes(fallbackFile+":Zone.Identifier").SequenceEqual(zone3));
    var beforeVault=File.ReadAllBytes(vaultPath);var deniedOut=Path.Combine(data,tag+"-denied");Directory.CreateDirectory(deniedOut);File.WriteAllText(Path.Combine(deniedOut,"b.txt"),"old");File.WriteAllBytes(Path.Combine(deniedOut,"b.txt")+":Zone.Identifier",zone4);
    var denied=new ExtractionService(engine,vault,new PasswordCandidateProvider(vault),new StubReader(true));var failed=await denied.ExtractAsync(archive,deniedOut,true,_=>Task.FromResult<PasswordPromptResponse?>(null),_=>Task.FromResult(ExistingFilePolicy.Replace));
    Check(tag+" metadata failure before any content commit",failed.Engine.Code=="origin_write_failed" && File.ReadAllText(Path.Combine(deniedOut,"b.txt"))=="old" && !Directory.Exists(Path.Combine(deniedOut,"folder")) && File.ReadAllBytes(Path.Combine(deniedOut,"b.txt")+":Zone.Identifier").SequenceEqual(zone4));
    Check(tag+" failed marking leaves vault unchanged and cleans staging",File.ReadAllBytes(vaultPath).SequenceEqual(beforeVault) && Directory.GetDirectories(deniedOut,".archive-password-*").Length==0);
    using(var locked=new FileStream(archive+":Zone.Identifier",FileMode.Open,FileAccess.ReadWrite,FileShare.None))
    {
        var lockedOut=Path.Combine(data,tag+"-locked");var lockedResult=await Extract(lockedOut);Check(tag+" unreadable source origin not treated as missing",lockedResult.Engine.Code=="origin_read_failed" && !Directory.Exists(lockedOut));
    }
    var warningService=new ExtractionService(engine,vault,new PasswordCandidateProvider(vault),new StubReader(false));var warned=await warningService.ExtractAsync(archive,skipOut,true,_=>Task.FromResult<PasswordPromptResponse?>(null),_=>Task.FromResult(ExistingFilePolicy.Skip));
    // All files skipped: still keep all pre-existing files and both warnings produced by the stub.
    Check(tag+" skipped and origin warnings remain visible",warned.Engine.IsSuccess && warned.Warning!.Contains("来源标记测试") && warned.Warning.Contains("已跳过"));
    using(var lockedVault=new FileStream(vaultPath,FileMode.Open,FileAccess.ReadWrite,FileShare.None))
    {
        var saveFailure=await warningService.ExtractAsync(archive,Path.Combine(data,tag+"-save-failure"),false,_=>Task.FromResult<PasswordPromptResponse?>(new("ascii-password",true,"test save")),_=>Task.FromResult(ExistingFilePolicy.Replace));
        Check(tag+" origin warning retained when password save also fails",saveFailure.Engine.IsSuccess && saveFailure.Warning!.Contains("来源标记测试") && saveFailure.Warning.Contains("密码未能保存"));
    }
    using var stop=new CancellationTokenSource();var cancelOut=Path.Combine(data,tag+"-cancel");var cancelled=false;
    try {await service.ExtractAsync(archive,cancelOut,true,_=>Task.FromResult<PasswordPromptResponse?>(null),_=>Task.FromResult(ExistingFilePolicy.Replace),new ForwardProgress<TaskUpdate>(update=>{if(update.State=="origin") stop.Cancel();}),stop.Token);}catch(OperationCanceledException) {cancelled=true;}
    Check(tag+" cancellation at origin phase commits no files",cancelled && Directory.GetFiles(cancelOut,"*",SearchOption.AllDirectories).Length==0 && Directory.GetDirectories(cancelOut,".archive-password-*").Length==0);
    File.WriteAllBytes(archive+":Zone.Identifier",new byte[65537]);var largeOut=Path.Combine(data,tag+"-oversize");var tooLarge=await Extract(largeOut);Check(tag+" source metadata bounded without dropping mark silently",tooLarge.Engine.Code=="origin_read_failed" && !Directory.Exists(largeOut));
}
var report=externalOutput is null?"origin-validation.json":"origin-fat-validation.json";
await File.WriteAllTextAsync(Path.Combine(root,"artifacts",report),JsonSerializer.Serialize(new {total=checks.Count,passed=checks.Count(value=>value.Value),checks,actualStreams=true,externalOutput},new JsonSerializerOptions {WriteIndented=true}));return checks.Values.All(value=>value)?0:1;

sealed class StubReader(bool fail) : IExtractionOriginReader,IExtractionOriginMark
{
    public Task<IExtractionOriginMark?> ReadAsync(string archive,CancellationToken token)=>Task.FromResult<IExtractionOriginMark?>(this);
    public async Task<string?> ApplyAsync(IReadOnlyList<string> files,CancellationToken token)
    {
        if(fail) {if(files.Count>0) await File.WriteAllTextAsync(files[0]+":Zone.Identifier","[ZoneTransfer]\r\nZoneId=3",token);throw new ExtractionOriginException("origin_write_failed","origin validation failure");}
        return "来源标记测试提示";
    }
}
