using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using ArchivePassword.Core;
using ArchivePassword.Infrastructure;

var root = Path.GetFullPath(args[0]);
var data = Path.Combine(root, "artifacts/post032-core", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(data);
var checks = new Dictionary<string, bool>();
void Check(string name, bool value) { checks.Add(name, value); if (!value) throw new Exception(name); }
string? error = null;
try
{
    UiText.Configure("en");
    Check("one password has singular grammar", UiText.Format("T_357176e2a16d",1) == "1 available password in the vault");
    Check("zero and multiple keep plural grammar", UiText.Format("Browser.CompactCount",0)=="0 items" && UiText.Format("Browser.CompactCount",2)=="2 items");
    Check("mixed counts inflect independently", UiText.Format("SoftwareUpdate.CacheCleaned",1,2.5,2).Contains("1 file,") && UiText.Format("SoftwareUpdate.CacheCleaned",2,2.5,1).EndsWith("1 file."));
    Check("count formatter preserves ordinary numeric formatting",UiText.Format("Download.Progress",1.25,2.5,64.3).Contains("1.25 / 2.5 MB"));
    UiText.Configure("zh-CN"); Check("Chinese count remains unchanged",UiText.Format("Browser.CompactCount",1)=="1 项");

    var batchRoot=Path.Combine(data,"separate");var sources=Path.Combine(batchRoot,"sources");var target=Path.Combine(batchRoot,"output");
    Directory.CreateDirectory(sources);Directory.CreateDirectory(target);
    var firstDirectory=Path.Combine(sources,"backup.v1");Directory.CreateDirectory(firstDirectory);
    var firstFile=Path.Combine(sources,"notes.txt");await File.WriteAllTextAsync(firstFile,"first");
    var otherDirectory=Path.Combine(sources,"other");Directory.CreateDirectory(otherDirectory);
    var secondFile=Path.Combine(otherDirectory,"notes.txt");await File.WriteAllTextAsync(secondFile,"second");
    var existing=Path.Combine(target,"notes.zip");await File.WriteAllTextAsync(existing,"keep existing");
    var batch=SeparateCompressionPlan.Create([firstFile,secondFile,firstDirectory,firstFile],target,".zip");
    Check("separate plan deduplicates sources and assigns independent output paths",batch.Count==3 && batch.Select(item=>item.SourcePath).Distinct().Count()==3);
    Check("same source names and existing output get deterministic suffixes",Path.GetFileName(batch[0].OutputPath)=="notes (2).zip" && Path.GetFileName(batch[1].OutputPath)=="notes (3).zip");
    Check("directory naming preserves dots",Path.GetFileName(batch[2].OutputPath)=="backup.v1.zip");
    Check("planning preserves existing files and creates no output archives",await File.ReadAllTextAsync(existing)=="keep existing" && Directory.GetFiles(target).Length==1);
    await File.WriteAllTextAsync(Path.Combine(target,"backup.v1.7z.001"),"keep volume");
    Check("separate naming protects an existing split volume set",Path.GetFileName(SeparateCompressionPlan.Create([firstDirectory],target,".7z")[0].OutputPath)=="backup.v1 (2).7z");
    var archiveSource=Path.Combine(sources,"original.zip");await File.WriteAllTextAsync(archiveSource,"keep source");
    Check("separate output never reuses a source archive path",SeparateCompressionPlan.Create([archiveSource],sources,".zip")[0].OutputPath!=archiveSource);
    foreach(var nested in new[]{firstDirectory,Path.Combine(firstDirectory,"output")})
    {
        var rejected=false;try{SeparateCompressionPlan.Create([firstDirectory],nested,".zip");}catch(InvalidDataException failure){rejected=failure.Message=="batch_output_inside_source";}
        Check("separate output inside source is rejected "+Path.GetFileName(nested),rejected);
    }
    var sibling=firstDirectory+"-output";
    Check("separate plan allows a sibling with shared path prefix",SeparateCompressionPlan.Create([firstDirectory],sibling,".zip").Count==1);
    var hidden=Path.Combine(sources,".notes");await File.WriteAllTextAsync(hidden,"hidden");
    Check("dot filename still produces a named output",Path.GetFileName(SeparateCompressionPlan.Create([hidden],target,".zip")[0].OutputPath)==".notes.zip");

    var cache = Path.Combine(data,"application-updates"); Directory.CreateDirectory(cache);
    var installer=Path.Combine(cache,Guid.NewGuid().ToString("N")+".exe"); await File.WriteAllBytesAsync(installer,new byte[27]);
    var outside=Path.Combine(data,"user-download.exe");await File.WriteAllTextAsync(outside,"keep");
    var directory=Path.Combine(data,"installation");
    await new ApplicationUpdateReceipt("0.3.1","0.3.2",directory).SaveAsync(cache);
    Check("failed or pending upgrade cannot clean",await ApplicationUpdateReceipt.CompleteAsync(cache,"0.3.1",directory,true) is null && File.Exists(installer));
    Check("other install directory cannot clean",await ApplicationUpdateReceipt.CompleteAsync(cache,"0.3.2",directory+"-other",true) is null && File.Exists(installer));
    using(var locked=new FileStream(installer,FileMode.Open,FileAccess.Read,FileShare.None))
    {
        var result=await ApplicationUpdateReceipt.CompleteAsync(cache,"0.3.2",directory,true);
        Check("successful upgrade skips occupied installer",result is {Deleted:0,Skipped:1} && File.Exists(installer));
    }
    await new ApplicationUpdateReceipt("0.3.1","0.3.2",directory).SaveAsync(cache);
    Check("disabled cleanup retains cache",await ApplicationUpdateReceipt.CompleteAsync(cache,"0.3.2",directory,false) is null && File.Exists(installer));
    await new ApplicationUpdateReceipt("0.3.1","0.3.2",directory).SaveAsync(cache);
    Check("successful upgraded startup cleans owned files",await ApplicationUpdateReceipt.CompleteAsync(cache,"0.3.2",directory,true) is {Deleted:1,Bytes:27} && !File.Exists(installer) && File.Exists(outside));
    Check("receipt consumed once",await ApplicationUpdateReceipt.CompleteAsync(cache,"0.3.2",directory,true) is null);
    Directory.CreateDirectory(directory);await File.WriteAllTextAsync(installer,"old cache");
    await File.WriteAllLinesAsync(Path.Combine(directory,"completed-update.txt"),["0.3.1.0","0.3.2.0"]);
    Check("installer receipt handles old app with no pending receipt",await ApplicationUpdateReceipt.CompleteAsync(cache,"0.3.2",directory,true) is {Deleted:1} && !File.Exists(installer));

    var entry=new ArchiveEntry(1,"folder/Picture.JPG",1048576,false,false);
    Check("combined extension size and kind",ArchiveEntryFilter.TryCreate("txt; jpg","1","1",1,out var filter) && filter.Matches(entry) && !filter.Matches(entry with {Size=1048575}));
    Check("types match full compound extension",ArchiveEntryFilter.TryCreate("tar.gz","","",0,out filter) && filter.Matches(entry with {Path="folder/file.TAR.GZ"}));
    Check("type filter excludes folders",!filter.Matches(entry with {Directory=true}));
    Check("folders only excludes files",ArchiveEntryFilter.TryCreate("","","",2,out filter) && filter.Matches(entry with {Directory=true}) && !filter.Matches(entry));
    Check("empty filters restore all kinds",ArchiveEntryFilter.TryCreate("","","",0,out filter) && filter.Matches(entry) && filter.Matches(entry with {Directory=true}));
    foreach(var value in new[]{("-1",""),("2","1"),("NaN",""),("999999999999999999999",""),("1,000","")})
        Check("invalid size rejected "+value.Item1+value.Item2,!ArchiveEntryFilter.TryCreate("",value.Item1,value.Item2,0,out _));
    Check("unsupported wildcard rejected",!ArchiveEntryFilter.TryCreate("*.txt","","",0,out _));
    Check("inclusive fractional bounds",ArchiveEntryFilter.TryCreate("","0.000001","0.000002",1,out filter) && filter.Matches(entry with {Size=2}) && !filter.Matches(entry with {Size=1}));

    var password=new SavedPassword(Guid.NewGuid(),"synthetic folder rule","synthetic-test-secret","",true,DateTimeOffset.UtcNow,MatchDirectory:Path.Combine(data,"source"));
    Check("directory rule includes root and descendants",PasswordOrganization.MatchesArchive(password,Path.Combine(data,"source","file.zip")) && PasswordOrganization.MatchesArchive(password,Path.Combine(data,"source","nested","file.zip")));
    Check("directory rule respects path component boundary",!PasswordOrganization.MatchesArchive(password,Path.Combine(data,"source-other","file.zip")));
    Check("directory and filename rules are alternatives",PasswordOrganization.MatchesArchive(password with {MatchText="name"},Path.Combine(data,"elsewhere","name.zip")));
    Check("relative source directory rejected",!PasswordOrganization.ValidMetadata(password with {MatchDirectory="relative"}));
    var vault=new DpapiPasswordVault(Path.Combine(data,"synthetic.apv"));await vault.SaveAsync(password);
    var fallback=password with {Id=Guid.NewGuid(),Secret="synthetic-fallback",MatchDirectory="",LastSuccessAt=DateTimeOffset.UtcNow};await vault.SaveAsync(fallback);
    var provider=new PasswordCandidateProvider(vault);
    var candidates=await provider.GetAsync(Path.Combine(data,"source","file.zip"));
    Check("directory priority retains fallback",candidates.Count==2 && candidates[0].SavedId==password.Id && candidates[1].SavedId==fallback.Id);
    await vault.RecordSuccessAsync(fallback.Id,Path.Combine(data,"source","file.zip"));
    Check("actual association outranks directory",(await provider.GetAsync(Path.Combine(data,"source","file.zip")))[0].SavedId==fallback.Id);
    var backup=Path.Combine(data,"synthetic.nvault");await EncryptedVaultBackup.ExportAsync(backup,await vault.ReadAsync(),"synthetic backup passphrase 123");
    Check("encrypted backup preserves directory rule",(await EncryptedVaultBackup.ReadAsync(backup,"synthetic backup passphrase 123")).Single(value=>value.Id==password.Id).MatchDirectory==password.MatchDirectory);
    var old=JsonSerializer.Deserialize<SavedPassword>("{\"Id\":\"11111111-1111-1111-1111-111111111111\",\"Name\":\"old\",\"Secret\":\"synthetic-old\",\"Notes\":\"\",\"AutoTry\":true,\"CreatedAt\":\"2026-01-01T00:00:00Z\"}")!;
    Check("old record defaults directory without migration",old.MatchDirectory=="" && PasswordOrganization.ValidMetadata(old));

    var bytes=Enumerable.Range(0,130000).Select(i=>(byte)(i%251)).ToArray();
    var digest=Convert.ToHexString(SHA256.HashData(bytes));
    var release=new AvailableApplicationRelease("99.0.0",new Uri("https://github.com/Nomoziya/nomozip/releases/tag/v99.0.0"),
        new Uri("https://github.com/Nomoziya/nomozip/releases/download/v99.0.0/nomozip-99.0.0-light-setup-win-x64.exe"),bytes.Length,digest);
    var samples=new List<DownloadProgress>();
    using(var input=new DelayedStream(bytes,TimeSpan.FromMilliseconds(650)))
    using(var output=new MemoryStream())
    {
        var found=await DownloadTransfer.CopyAsync(input,output,bytes.Length,bytes.Length,new ForwardProgress<DownloadProgress>(samples.Add),default,TimeSpan.FromMilliseconds(200));
        Check("slow transfer shows stalled and recovers with complete digest",samples.Any(p=>p.Stalled) && samples[^1].Bytes==bytes.Length && !samples[^1].Stalled && found==digest && output.ToArray().SequenceEqual(bytes));
        Check("transfer speed is finite and nonnegative",samples.All(p=>p.BytesPerSecond>=0 && double.IsFinite(p.BytesPerSecond)));
    }
    foreach(var mode in new[]{"ok","tampered","disconnect","timeout","cancel"})
    {
        using var cancel=new CancellationTokenSource();
        using var client=new HttpClient(new Handler(mode,bytes));
        if(mode=="cancel")cancel.CancelAfter(40);
        try
        {
            var path=await new ApplicationInstallerDownload(client,mode=="timeout"?TimeSpan.FromMilliseconds(40):null).DownloadAsync(release,"x64",cache,token:cancel.Token);
            Check(mode+" result",mode=="ok" && (await File.ReadAllBytesAsync(path)).SequenceEqual(bytes));File.Delete(path);
        }
        catch(InvalidDataException) { Check(mode+" integrity rejection",mode is "tampered" or "disconnect"); }
        catch(OperationCanceledException) { Check(mode+" cancellation distinction",mode=="timeout"?!cancel.IsCancellationRequested:mode=="cancel" && cancel.IsCancellationRequested); }
        Check(mode+" partial removed",!Directory.EnumerateFiles(cache,"*.partial").Any());
    }
    using(var client=new HttpClient(new Handler("ok",bytes)))
    {
        var path=await new ApplicationInstallerDownload(client).DownloadAsync(release,"x64",cache);
        Check("retry after failed downloads succeeds",File.Exists(path));File.Delete(path);
    }
}
catch(Exception failure) { error=failure.ToString(); }
var passed=error is null && checks.Count>0 && checks.Values.All(value=>value);
await File.WriteAllTextAsync(Path.Combine(root,"artifacts/post032-core-validation.json"),JsonSerializer.Serialize(new {passed,error,checks},new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine(JsonSerializer.Serialize(new {passed,error,count=checks.Count}));
return passed?0:1;

sealed class Handler(string mode,byte[] bytes):HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
    {
        var data=mode=="tampered"?bytes.Select(value=>(byte)(value^1)).ToArray():mode=="disconnect"?bytes[..100]:bytes;
        var response=new HttpResponseMessage(HttpStatusCode.OK){RequestMessage=request,Content=new StreamContent(new DelayedStream(data,mode is "timeout" or "cancel"?TimeSpan.FromSeconds(30):TimeSpan.Zero))};
        return Task.FromResult(response);
    }
}
sealed class DelayedStream(byte[] bytes,TimeSpan delay):MemoryStream(bytes)
{
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer,CancellationToken token=default)
    { if(delay>TimeSpan.Zero)await Task.Delay(delay,token);return await base.ReadAsync(buffer,token); }
}
