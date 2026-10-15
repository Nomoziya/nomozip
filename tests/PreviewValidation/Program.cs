using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ArchivePassword.Core;
using ArchivePassword.Infrastructure;

var root=Path.GetFullPath(args[0]);var folder=Path.Combine(root,"artifacts/pv-"+Guid.NewGuid().ToString("N")[..8]);Directory.CreateDirectory(folder);
var checks=new Dictionary<string,bool>();void Check(string name,bool value) {checks.Add(name,value);Console.WriteLine((value?"PASS ":"FAIL ")+name);}
var versions=args.Length>1?args.Skip(1).SelectMany(v=>v.Split(',')).Distinct().ToArray():["26.04","26.03"];
if(versions.Any(v=>!System.Text.RegularExpressions.Regex.IsMatch(v,@"^\d{2}\.\d{2}$"))) throw new ArgumentException();
Task<PasswordPromptResponse?> NoPassword(EngineResult _)=>Task.FromResult<PasswordPromptResponse?>(null);
try
{
foreach(var version in versions)
{
    var label=version+" ";var data=Path.Combine(folder,version);Directory.CreateDirectory(data);var temp=Path.Combine(data,"temporary");
    bool Clean()=>!Directory.Exists(temp) || !Directory.EnumerateFileSystemEntries(temp).Any();
    var client=new ArchiveEngineClient(Path.Combine(root,"artifacts/worker/ArchiveEngineWorker.exe"),Path.Combine(root,"engines",version,"7z.dll"));
    var text=Path.Combine(data,"text.txt");await File.WriteAllTextAsync(text,"第一行 中文 😀\r\nsecond\tline",new UTF8Encoding(false));
    var empty=Path.Combine(data,"empty.txt");await File.WriteAllBytesAsync(empty,[]);
    var other=Path.Combine(data,"other.exe");await File.WriteAllBytesAsync(other,[1,2,3,4]);
    var candidates=new Candidates();var service=new ArchivePreviewService(client,candidates,temp);
    foreach(var format in new[]{"7z","zip"}) foreach(var encrypted in new[]{false,true})
    {
        var archive=Path.Combine(data,format+encrypted+"."+format);var secret=encrypted?"preview-secret":null;
        var created=await client.CreateAsync(new([new(text,"目录/内容.txt",false),new(empty,"empty.txt",false),new(other,"do-not-extract.exe",false)],format,5,encrypted),archive,secret);
        var listed=await client.ListAsync(archive,secret);var entry=listed.Items.Single(e=>e.Path.Replace('\\','/')=="目录/内容.txt");var before=File.ReadAllBytes(archive);
        var tag=label+format+encrypted+" ";Check(tag+"real fixture created",created.IsSuccess);
        var result=await service.ReadAsync(archive,entry,false,_=>Task.FromResult<PasswordPromptResponse?>(encrypted?new(secret!,true,"must-not-save"):null));
        Check(tag+"exact selected Unicode bytes only",result.Engine.IsSuccess && result.Content!.SequenceEqual(File.ReadAllBytes(text)));
        Check(tag+"source byte exact and all temporary files removed",before.SequenceEqual(File.ReadAllBytes(archive)) && Clean());
        var emptyEntry=listed.Items.Single(e=>e.Path=="empty.txt");var emptyResult=await service.ReadAsync(archive,emptyEntry,false,_=>Task.FromResult<PasswordPromptResponse?>(new(secret??"",false,"")));
        Check(tag+"empty text works",emptyResult.Engine.IsSuccess && emptyResult.Content!.Length==0 && Clean());
        var stale=await service.ReadAsync(archive,entry with {Size=entry.Size+1},false,_=>Task.FromResult<PasswordPromptResponse?>(new(secret??"",false,"")));
        Check(tag+"stale browser metadata refuses output",stale.Engine.Code=="selection_changed" && Clean());
        if(encrypted)
        {
            candidates.Secret=secret;var calls=candidates.Calls;
            var auto=await service.ReadAsync(archive,entry,true,_=>throw new Exception("Unexpected prompt"));
            Check(tag+"saved candidate readonly auto preview",auto.Engine.IsSuccess && candidates.Calls==calls+1 && Clean());
            int prompts=0;var retry=await service.ReadAsync(archive,entry,false,_=>Task.FromResult<PasswordPromptResponse?>(++prompts==1?new("wrong",false,""):new(secret!,false,"")));
            Check(tag+"wrong password retries before exact content",prompts==2 && retry.Engine.IsSuccess && retry.Content!.SequenceEqual(File.ReadAllBytes(text)) && Clean());
            var cancel=await service.ReadAsync(archive,entry,false,NoPassword);
            Check(tag+"password cancellation cleans temporary output",cancel.Engine.Status=="cancelled" && Clean());
        }
    }
    var plain=Path.Combine(data,"7zFalse.7z");var items=(await client.ListAsync(plain)).Items;var selected=items.Single(e=>e.Path.EndsWith("内容.txt"));
    var encodedClient=new ArchiveEngineClient(Path.Combine(root,"artifacts/worker/ArchiveEngineWorker.exe"),Path.Combine(root,"engines",version,"7z.dll"),936);
    var encodedArchive=Path.Combine(data,"zipFalse.zip");var encodedEntry=(await encodedClient.ListAsync(encodedArchive)).Items.Single(e=>e.Path.EndsWith("内容.txt"));
    var encodedResult=await new ArchivePreviewService(encodedClient,candidates,temp).ReadAsync(encodedArchive,encodedEntry,false,NoPassword);
    Check(label+"encoded preview operation preserves UTF8 filename priority",encodedResult.Engine.IsSuccess && encodedResult.Content!.SequenceEqual(File.ReadAllBytes(text)) && Clean());
    var rar=Path.Combine(root,"tests/fixtures/generated/test_read_format_rar.rar");var rarEntry=(await client.ListAsync(rar)).Items.Single(e=>e.Path=="test.txt");var rarResult=await service.ReadAsync(rar,rarEntry,false,NoPassword);
    var rarCli=new System.Diagnostics.ProcessStartInfo(Path.Combine(root,"engines",version,"7z.exe")){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};foreach(var argument in new[]{"e","-so",rar,"test.txt"}) rarCli.ArgumentList.Add(argument);
    using(var process=System.Diagnostics.Process.Start(rarCli)!) {using var bytes=new MemoryStream();var read=process.StandardOutput.BaseStream.CopyToAsync(bytes);var stderr=process.StandardError.ReadToEndAsync();await process.WaitForExitAsync();await read;await stderr;Check(label+"RAR preview exact bytes match independent official CLI",rarResult.Engine.IsSuccess && process.ExitCode==0 && rarResult.Content!.SequenceEqual(bytes.ToArray()) && Clean());}
    foreach(var entry in new[]{selected with {Size=ArchivePreviewPolicy.TextLimit+1},selected with {Path="image.png",Size=ArchivePreviewPolicy.ImageLimit+1},selected with {Directory=true},selected with {Path="run.exe"},selected with {Path="vector.svg"}})
    {
        var result=await service.ReadAsync(plain,entry,false,NoPassword);Check(label+"policy rejects "+entry.Path+entry.Size+entry.Directory,!result.Engine.IsSuccess && result.Content is null && Clean());
    }
    var traversal=await service.ReadAsync(plain,selected with {Path="../outside.txt"},false,NoPassword);
    Check(label+"unsafe preview path refused",traversal.Engine.Code=="unsafe_path" && Clean());
    using(var cancelled=new CancellationTokenSource()) {cancelled.Cancel();var result=await service.ReadAsync(plain,selected,false,NoPassword,token:cancelled.Token);Check(label+"pre-cancelled request no output",result.Engine.Status=="cancelled" && Clean());}
    using(var cancelled=new CancellationTokenSource())
    {
        var result=await service.ReadAsync(plain,selected,false,NoPassword,new ForwardProgress<TaskUpdate>(p=>{if(p.State=="preview-reading") cancelled.Cancel();}),cancelled.Token);
        Check(label+"cancel between validation and materialization cleans workspace",result.Engine.Status=="cancelled" && Clean());
    }
    foreach(var limit in new ulong[]{0,ArchivePreviewPolicy.ImageLimit+1})
    {var result=await client.ExtractPreviewAsync(plain,Path.Combine(data,"invalid"),selected,limit,null,new Dictionary<uint,string>());Check(label+"native invalid limit "+limit,result.Code=="invalid_preview" && !Directory.Exists(Path.Combine(data,"invalid")));}
    var small=await client.ExtractPreviewAsync(plain,Path.Combine(data,"too-small"),selected,1,null,new Dictionary<uint,string>());
    Check(label+"native refuses selected size before creating output",small.Code=="preview_too_large" && !Directory.Exists(Path.Combine(data,"too-small")));
    // Same source metadata, but actual extraction changed: service must not display unexpected bytes or files.
    foreach(var extra in new[]{false,true})
    {var altered=new ArchivePreviewService(new Substitute(client,extra),candidates,temp);var result=await altered.ReadAsync(plain,selected,false,NoPassword);Check(label+"materialized inventory mismatch "+extra,result.Engine.Code=="preview_content_mismatch" && Clean());}
    var locked=new ArchivePreviewService(new CleanupLock(client),candidates,temp);var lockResult=await locked.ReadAsync(plain,selected,false,NoPassword);
    Check(label+"cleanup lock gives actual path warning",lockResult.Engine.IsSuccess && lockResult.Warning?.Contains(Path.GetFullPath(temp))==true && !Clean());
    CleanupLock.Handle?.Dispose();CleanupLock.Handle=null;
    foreach(var owned in Directory.GetDirectories(temp)) Directory.Delete(owned,true);
    var retained=Path.Combine(temp,"unrelated.txt");await File.WriteAllTextAsync(retained,"retain");
    var retainedResult=await service.ReadAsync(plain,selected,false,NoPassword);
    Check(label+"only owned GUID workspace deleted",retainedResult.Engine.IsSuccess && File.ReadAllText(retained)=="retain" && Directory.GetFiles(temp).Length==1);File.Delete(retained);
    var large=Path.Combine(data,"large.txt");await File.WriteAllBytesAsync(large,new byte[1024*1024]);var archiveLarge=large+".zip";
    using(var zip=ZipFile.Open(archiveLarge,ZipArchiveMode.Create)) zip.CreateEntryFromFile(large,"large.txt");
    var maxEntry=(await client.ListAsync(archiveLarge)).Items.Single();var max=await service.ReadAsync(archiveLarge,maxEntry,false,NoPassword);
    Check(label+"exact one MiB text allowed",max.Engine.IsSuccess && max.Content!.Length==1024*1024 && Clean());
    var deceptive=File.ReadAllBytes(archiveLarge);
    for(var offset=0;offset<deceptive.Length-28;offset++)
    {
        if(deceptive.AsSpan(offset,4).SequenceEqual(new byte[]{0x50,0x4b,3,4})) BitConverter.GetBytes(16u).CopyTo(deceptive,offset+22);
        if(deceptive.AsSpan(offset,4).SequenceEqual(new byte[]{0x50,0x4b,1,2})) BitConverter.GetBytes(16u).CopyTo(deceptive,offset+24);
    }
    var misleading=Path.Combine(data,"misleading.zip");await File.WriteAllBytesAsync(misleading,deceptive);var deceptiveEntry=(await client.ListAsync(misleading)).Items.Single();var limitedFolder=Path.Combine(data,"limited");
    var limited=await client.ExtractPreviewAsync(misleading,limitedFolder,deceptiveEntry,64,null,new Dictionary<uint,string>());
    Check(label+"actual stream cannot exceed cap despite forged small ZIP size",!limited.IsSuccess && deceptiveEntry.Size==16 && (!Directory.Exists(limitedFolder) || Directory.GetFiles(limitedFolder,"*",SearchOption.AllDirectories).All(f=>new FileInfo(f).Length<=64)));
    var foreign=Path.Combine(data,"new-source.zip");using(var zip=ZipFile.Open(foreign,ZipArchiveMode.Create)) {using var writer=new StreamWriter(zip.CreateEntry("different.txt").Open());writer.Write("different");}
    var changed=await service.ReadAsync(foreign,selected,false,NoPassword);Check(label+"replaced archive selection rejected",changed.Engine.Code=="selection_changed" && Clean());
}
foreach(var encoding in new Encoding[]{new UTF8Encoding(true,true),new UnicodeEncoding(false,true,true),new UnicodeEncoding(true,true,true),new UTF32Encoding(false,true,true),new UTF32Encoding(true,true,true)})
{var value="<script>not executed</script> 中文 😀";var decoded=ArchivePreviewPolicy.DecodeText(encoding.GetPreamble().Concat(encoding.GetBytes(value)).ToArray());Check("strict BOM decode "+encoding.WebName,decoded.Text==value);}
foreach(var bytes in new[]{new byte[]{0xff,0xfe,0x20},new byte[]{0xc0,0x80},new byte[]{0,1,2}})
{bool rejected=false;try {ArchivePreviewPolicy.DecodeText(bytes);}catch(DecoderFallbackException) {rejected=true;}Check("binary or malformed Unicode "+Convert.ToHexString(bytes),rejected);}
}
catch(Exception error) {Console.WriteLine(error);Check("exception",false);}
finally {CleanupLock.Handle?.Dispose();if(Directory.Exists(folder)) Directory.Delete(folder,true);}
var hashes=versions.ToDictionary(v=>v,v=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(root,"engines",v,"7z.dll")))).ToLowerInvariant());
await File.WriteAllTextAsync(Path.Combine(root,"artifacts/preview-validation.json"),JsonSerializer.Serialize(new{passed=checks.Values.All(v=>v),total=checks.Count,checks,engineHashes=hashes},new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine($"{checks.Count(p=>p.Value)}/{checks.Count} passed");return checks.Values.All(v=>v)?0:1;

sealed class Candidates:IPasswordCandidateProvider
{public string? Secret;public int Calls;public Task<IReadOnlyList<PasswordCandidate>> GetAsync(string archive,CancellationToken token=default) {Calls++;return Task.FromResult<IReadOnlyList<PasswordCandidate>>(Secret is null?[]:[new(Guid.NewGuid(),Secret)]);}}
class Substitute(ArchiveEngineClient inner,bool extra):IArchiveEngine,IArchiveSelectionEngine,IArchivePreviewEngine
{
    protected ArchiveEngineClient Client=>inner;
    public Task<EngineResult> GetCapabilitiesAsync(CancellationToken cancellationToken=default)=>inner.GetCapabilitiesAsync(cancellationToken);
    public Task<EngineResult> ListAsync(string archivePath,string? password=null,CancellationToken cancellationToken=default)=>inner.ListAsync(archivePath,password,cancellationToken);
    public Task<EngineResult> TestAsync(string archivePath,string? password=null,IProgress<EngineProgress>? progress=null,CancellationToken cancellationToken=default)=>inner.TestAsync(archivePath,password,progress,cancellationToken);
    public Task<EngineResult> ExtractAsync(string archivePath,string outputPath,string? password=null,IProgress<EngineProgress>? progress=null,CancellationToken cancellationToken=default)=>inner.ExtractAsync(archivePath,outputPath,password,progress,cancellationToken);
    public Task<EngineResult> TestSelectionAsync(string archive,IReadOnlyList<ArchiveEntry> selection,string? password,IReadOnlyDictionary<uint,string> itemPasswords,IProgress<EngineProgress>? progress=null,CancellationToken token=default)=>inner.TestSelectionAsync(archive,selection,password,itemPasswords,progress,token);
    public Task<EngineResult> ExtractSelectionAsync(string archive,string output,IReadOnlyList<ArchiveEntry> selection,string? password,IReadOnlyDictionary<uint,string> itemPasswords,IProgress<EngineProgress>? progress=null,CancellationToken token=default)=>inner.ExtractSelectionAsync(archive,output,selection,password,itemPasswords,progress,token);
    public virtual async Task<EngineResult> ExtractPreviewAsync(string archive,string output,ArchiveEntry entry,ulong limit,string? password,IReadOnlyDictionary<uint,string> itemPasswords,IProgress<EngineProgress>? progress=null,CancellationToken token=default)
    {var result=await inner.ExtractPreviewAsync(archive,output,entry,limit,password,itemPasswords,progress,token);if(result.IsSuccess) await File.WriteAllTextAsync(Path.Combine(output,extra?"unexpected.txt":entry.Path.Replace('/',Path.DirectorySeparatorChar)),"wrong");return result;}
}
sealed class CleanupLock(ArchiveEngineClient inner):Substitute(inner,false)
{
    public static FileStream? Handle;
    public override async Task<EngineResult> ExtractPreviewAsync(string archive,string output,ArchiveEntry entry,ulong limit,string? password,IReadOnlyDictionary<uint,string> itemPasswords,IProgress<EngineProgress>? progress=null,CancellationToken token=default)
    {var result=await Client.ExtractPreviewAsync(archive,output,entry,limit,password,itemPasswords,progress,token);Handle=new FileStream(Path.Combine(output,entry.Path.Replace('/',Path.DirectorySeparatorChar)),FileMode.Open,FileAccess.Read,FileShare.Read);return result;}
}
