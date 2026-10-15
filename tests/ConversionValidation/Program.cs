using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using ArchivePassword.Core;
using ArchivePassword.Infrastructure;

var root=Path.GetFullPath(args[0]);var folder=Path.Combine(root,"artifacts/cv-"+Guid.NewGuid().ToString("N")[..8]);Directory.CreateDirectory(folder);
var cases=new List<object>();int passed=0,total=0;
void Check(string name,bool value) {total++;if(value) passed++;cases.Add(new{name,passed=value});Console.WriteLine((value?"PASS ":"FAIL ")+name);}
var versions=args.Length>1?args.Skip(1).SelectMany(value=>value.Split(',')).Distinct().ToArray():["26.04","26.03"];
if(versions.Any(value=>!System.Text.RegularExpressions.Regex.IsMatch(value,@"^\d{2}\.\d{2}$"))) throw new ArgumentException("Invalid engine version.");
try
{
foreach(var version in versions)
{
    var client=new ArchiveEngineClient(Path.Combine(root,"artifacts/worker/ArchiveEngineWorker.exe"),Path.Combine(root,"engines",version,"7z.dll"));
    var data=Path.Combine(folder,version);Directory.CreateDirectory(data);
    var content=Path.Combine(data,"content");await File.WriteAllTextAsync(content,"exact 中文 bytes");
    var empty=Path.Combine(data,"empty");await File.WriteAllBytesAsync(empty,[]);
    var emptyDirectory=Path.Combine(data,"directory");Directory.CreateDirectory(emptyDirectory);
    var service=new ArchiveConversionService(client,client,new EmptyCandidates(),new WindowsExtractionOrigin());
    foreach(var sourceFormat in new[]{"7z","zip"}) foreach(var sourceEncrypted in new[]{false,true})
    {
        var source=Path.Combine(data,"source-"+sourceEncrypted+"."+sourceFormat);string? sourceSecret=sourceEncrypted?"source-secret":null;
        var entries=new[]{new CompressionEntry(content,"目录/file.txt",false),new CompressionEntry(empty,"empty.txt",false),new CompressionEntry(emptyDirectory,"empty-folder",true)};
        Check(version+sourceFormat+sourceEncrypted+" source fixture",(await client.CreateAsync(new(entries,sourceFormat,5,sourceEncrypted),source,sourceSecret)).IsSuccess);
        var before=File.ReadAllBytes(source);
        foreach(var format in new[]{"7z","zip"}) foreach(var encrypted in new[]{false,true})
        {
            var label=$"{version} {sourceFormat}/{sourceEncrypted} -> {format}/{encrypted}";
            var output=Path.Combine(data,Guid.NewGuid().ToString("N")+"."+format);var secret=encrypted?(format=="7z"?"新密碼 ' $ 空格":"new-secret"):null;
            int prompts=0;
            var result=await service.ConvertAsync(source,output,format,encrypted?9:0,encrypted,secret,true,false,true,
                _=>Task.FromResult<PasswordPromptResponse?>(++prompts==1?new(sourceSecret!,true,"must-not-save"):null));
            if(!result.Engine.IsSuccess) Console.WriteLine(JsonSerializer.Serialize(result));
            Check(label+" success, source preserved and staging cleaned",result.Engine.IsSuccess && File.ReadAllBytes(source).SequenceEqual(before) && !Directory.GetDirectories(data,".nomozip-convert-*").Any());
            Check(label+" only prompts for source encryption",prompts==(sourceEncrypted?1:0));
            var listed=await client.ListAsync(output,secret);
            Check(label+" names, implied parent, empty entries and encryption",listed.IsSuccess && listed.Format==format && listed.Items.Count==4 && listed.Items.Count(item=>!item.Directory)==2 && listed.Items.Where(item=>!item.Directory && (item.Size>0 || !encrypted)).All(item=>item.Encrypted==encrypted));
            if(encrypted) Check(label+" output rejects source password",(await client.TestAsync(output,sourceSecret)).NeedsPassword && (await client.TestAsync(output,secret)).IsSuccess);
            if(encrypted && format=="7z") Check(label+" filename encryption",(await client.ListAsync(output)).NeedsPassword);
            var extraction=output+"-out";var start=new ProcessStartInfo(Path.Combine(root,"engines",version,"7z.exe")){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
            foreach(var argument in new[]{"x","-y","-o"+extraction,output}) start.ArgumentList.Add(argument);if(secret is not null) start.ArgumentList.Add("-p"+secret);
            using var process=Process.Start(start)!;var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();await process.WaitForExitAsync();await stdout;await stderr;
            Check(label+" official CLI exact content and empty directory",process.ExitCode==0 && File.ReadAllBytes(Path.Combine(extraction,"目录/file.txt")).SequenceEqual(File.ReadAllBytes(content)) && new FileInfo(Path.Combine(extraction,"empty.txt")).Length==0 && Directory.Exists(Path.Combine(extraction,"empty-folder")));
        }
    }
    var archive=Path.Combine(data,"source-False.7z");var original=File.ReadAllBytes(archive);var target=Path.Combine(data,"existing.zip");await File.WriteAllTextAsync(target,"existing target");var existing=File.ReadAllBytes(target);
    Task<PasswordPromptResponse?> NoPassword(EngineResult _)=>Task.FromResult<PasswordPromptResponse?>(null);
    async Task Failure(string label,string expected,ArchiveConversionService? selected=null,IProgress<TaskUpdate>? progress=null,CancellationToken token=default,bool replace=true,string? output=null,string format="zip",bool encrypt=false,string? password=null)
    {
        var result=await (selected??service).ConvertAsync(archive,output??target,format,5,encrypt,password,true,replace,false,NoPassword,progress,token);
        Check(version+" "+label,result.Engine.Code==expected && File.ReadAllBytes(archive).SequenceEqual(original) && File.ReadAllBytes(target).SequenceEqual(existing) && !Directory.GetDirectories(data,".nomozip-convert-*").Any());
    }
    await Failure("existing target requires approval","output_exists",replace:false);
    await Failure("same path rejected","conversion_same_path",output:archive);
    await Failure("RAR writing rejected","invalid_compression_options",format:"rar");
    await Failure("encryption requires password","conversion_password_required",encrypt:true);
    await Failure("ZIP Unicode password rejected","zip_password_ascii_only",encrypt:true,password:"中文");
    foreach(var stage in new[]{"converting-extract","converting-create","converting-verify","converting-commit"})
    {
        using var cancellation=new CancellationTokenSource();
        await Failure("cancellation at "+stage,"cancelled",progress:new ForwardProgress<TaskUpdate>(update=>{if(update.State==stage) cancellation.Cancel();}),token:cancellation.Token);
    }
    await Failure("failed creator preserves existing target","output_write_failed",new(client,new RejectCreator(),new EmptyCandidates()));
    var substitute=Path.Combine(data,"substitute");await File.WriteAllTextAsync(substitute,"wrong 中文 bytes");
    await Failure("valid archive with changed bytes is rejected","conversion_content_mismatch",new(client,new SubstituteCreator(client,substitute),new EmptyCandidates()));
    FileStream? locked=null;
    try {await Failure("actual target sharing lock preserves target","source_or_output_unavailable",progress:new ForwardProgress<TaskUpdate>(update=>{if(update.State=="converting-commit") locked=new FileStream(target,FileMode.Open,FileAccess.Read,FileShare.Read);}));}
    finally {locked?.Dispose();}
    var success=await service.ConvertAsync(archive,target,"zip",5,false,null,false,true,false,NoPassword);
    Check(version+" approved replacement succeeds and retains original",success.Engine.IsSuccess && (await client.TestAsync(target)).IsSuccess && File.ReadAllBytes(archive).SequenceEqual(original));
    var encryptedSource=Path.Combine(data,"source-True.7z");int wrongPrompts=0;
    var cancelled=await service.ConvertAsync(encryptedSource,Path.Combine(data,"wrong.zip"),"zip",5,false,null,false,false,false,
        _=>Task.FromResult<PasswordPromptResponse?>(wrongPrompts++==0?new("wrong",false,""):null));
    Check(version+" wrong source password and cancel creates no output",cancelled.Engine.Status=="cancelled" && !File.Exists(Path.Combine(data,"wrong.zip")));
    var cached=new ArchiveConversionService(client,client,new SavedCandidates("source-secret"));
    var cachedResult=await cached.ConvertAsync(encryptedSource,Path.Combine(data,"cached.7z"),"7z",5,false,null,false,false,true,_=>throw new Exception("Unexpected password prompt"));
    Check(version+" saved password auto-read without inheritance",cachedResult.Engine.IsSuccess && (await client.TestAsync(cachedResult.OutputPath)).IsSuccess);
    var mixed=Path.Combine(root,"tests/fixtures/generated/mixed-aes.zip");int mixedPrompts=0;
    var mixedResult=await service.ConvertAsync(mixed,Path.Combine(data,"mixed.7z"),"7z",5,true,"new-secret",true,false,false,
        _=>Task.FromResult<PasswordPromptResponse?>(++mixedPrompts<=2?new(mixedPrompts==1?"password":"password2",false,""):null));
    Check(version+" multiple source passwords become one explicit new password",mixedResult.Engine.IsSuccess && mixedPrompts==2 && (await client.TestAsync(mixedResult.OutputPath,"new-secret")).IsSuccess);
    var rar=Directory.GetFiles(Path.Combine(root,"tests/fixtures"),"*.rar",SearchOption.AllDirectories).First(path=>!path.Contains("encrypted") && !path.Contains("mixed"));
    var rarBefore=File.ReadAllBytes(rar);var rarResult=await service.ConvertAsync(rar,Path.Combine(data,"from-rar.zip"),"zip",5,false,null,false,false,false,NoPassword);
    Check(version+" RAR read conversion retains original",rarResult.Engine.IsSuccess && rarBefore.SequenceEqual(File.ReadAllBytes(rar)));
    var mark="[ZoneTransfer]\r\nZoneId=3\r\nHostUrl=https://example.test/source.7z\r\n";await File.WriteAllTextAsync(archive+":Zone.Identifier",mark);
    var originResult=await service.ConvertAsync(archive,Path.Combine(data,"origin.zip"),"zip",5,false,null,false,false,false,NoPassword);
    Check(version+" NTFS origin propagated to new archive",originResult.Engine.IsSuccess && File.ReadAllText(originResult.OutputPath+":Zone.Identifier")==mark && File.ReadAllText(archive+":Zone.Identifier")==mark);
}
}
catch(Exception error) {Check("exception "+error,false);}
var hashes=versions.ToDictionary(value=>value,value=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(root,"engines",value,"7z.dll")))).ToLowerInvariant());
await File.WriteAllTextAsync(Path.Combine(root,"artifacts/conversion-validation.json"),JsonSerializer.Serialize(new{passed,total,cases,engineHashes=hashes},new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine($"{passed}/{total} passed");return passed==total?0:1;
sealed class EmptyCandidates:IPasswordCandidateProvider {public Task<IReadOnlyList<PasswordCandidate>> GetAsync(string path,CancellationToken token=default)=>Task.FromResult<IReadOnlyList<PasswordCandidate>>([]);}
sealed class SavedCandidates(string secret):IPasswordCandidateProvider {public Task<IReadOnlyList<PasswordCandidate>> GetAsync(string path,CancellationToken token=default)=>Task.FromResult<IReadOnlyList<PasswordCandidate>>([new(Guid.NewGuid(),secret)]);}
sealed class RejectCreator:IArchiveCreator {public Task<EngineResult> CreateAsync(CompressionRequest request,string outputPath,string? password,IProgress<EngineProgress>? progress=null,CancellationToken cancellationToken=default)=>Task.FromResult(new EngineResult {Code="output_write_failed"});}
sealed class SubstituteCreator(IArchiveCreator creator,string substitute):IArchiveCreator
{
    public Task<EngineResult> CreateAsync(CompressionRequest request,string outputPath,string? password,IProgress<EngineProgress>? progress=null,CancellationToken cancellationToken=default)
    =>creator.CreateAsync(request with {Entries=request.Entries.Select(entry=>entry.Path=="目录/file.txt"?entry with {SourcePath=substitute}:entry).ToArray()},outputPath,password,progress,cancellationToken);
}
