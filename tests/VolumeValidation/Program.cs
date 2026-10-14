using System.Text.Json;
using System.Diagnostics;
using System.Security.Cryptography;
using ArchivePassword.Core;
using ArchivePassword.Infrastructure;

var root=Path.GetFullPath(args[0]);var data=Path.Combine(root,"artifacts/vl-"+Guid.NewGuid().ToString("N")[..8]);Directory.CreateDirectory(data);
var source=Path.Combine(data,"中文目录");Directory.CreateDirectory(Path.Combine(source,"empty"));
var content=RandomNumberGenerator.GetBytes(32000);await File.WriteAllBytesAsync(Path.Combine(source,"内容.bin"),content);await File.WriteAllBytesAsync(Path.Combine(source,"zero.bin"),[]);
var checks=new Dictionary<string,bool>();void Check(string name,bool passed) {checks.Add(name,passed);Console.WriteLine((passed?"PASS ":"FAIL ")+name);}
var versions=args.Length>1 ? args.Skip(1).SelectMany(s=>s.Split(',')).Distinct().ToArray() : ["26.04","26.03"];
if(versions.Any(v=>!System.Text.RegularExpressions.Regex.IsMatch(v,@"^\d{2}\.\d{2}$"))) throw new ArgumentException("Invalid engine version.");
foreach(var version in versions)
{
    var client=new ArchiveEngineClient(Path.Combine(root,"artifacts/worker/ArchiveEngineWorker.exe"),Path.Combine(root,"engines",version,"7z.dll"));var service=new CompressionService(client,client);
    foreach(var format in new[]{"7z","zip"}) foreach(var encrypted in new[]{false,true})
    {
        var label=$"{version} {format} {encrypted}";var folder=Path.Combine(data,version+format+encrypted);Directory.CreateDirectory(folder);
        var output=Path.Combine(folder,"sample."+format);var password=encrypted ? "fixture-password" : null;
        var made=await service.CreateAsync([source],output,format,password:password,volumeSize:4096);
        if(!made.Engine.IsSuccess) Console.WriteLine(JsonSerializer.Serialize(made.Engine));
        Check(label+" creates validated standard volumes",made.Engine.IsSuccess && made.Volumes?.Count>2 && Path.GetFullPath(made.OutputPath)==Path.GetFullPath(output)+".001" && !File.Exists(output));
        if(!made.Engine.IsSuccess) continue;
        var parts=made.Volumes!;
        Check(label+" sizes obey requested limit",parts.All(p=>new FileInfo(p).Length<=4096 && new FileInfo(p).Length>0) && parts.Take(parts.Count-1).All(p=>new FileInfo(p).Length==4096));
        var listed=await client.ListAsync(parts[0],password);
        Check(label+" first volume lists actual archive entries",listed.IsSuccess && listed.Format?.Equals(format,StringComparison.OrdinalIgnoreCase)==true && listed.Items.Count==4 && listed.Items.Any(i=>i.Path.Replace('\\','/')=="中文目录/内容.bin"));
        var attemptedEdit=await client.UpdateAsync(parts[0],output+"-edit",new(listed.Items,[],[],false),password);
        Check(label+" split set remains read only for editing",attemptedEdit.Code=="archive_edit_container_read_only" && !File.Exists(output+"-edit"));
        var target=Path.Combine(folder,"direct");var extracted=await client.ExtractAsync(parts[0],target,password);
        Check(label+" first volume directly extracts exact bytes and empty folder",extracted.IsSuccess && File.ReadAllBytes(Path.Combine(target,"中文目录/内容.bin")).SequenceEqual(content) && Directory.Exists(Path.Combine(target,"中文目录/empty")));
        var official=Path.Combine(folder,"official");var start=new ProcessStartInfo(Path.Combine(root,"engines",version,"7z.exe")){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
        foreach(var value in new[]{"x","-y","-o"+official,parts[0]}) start.ArgumentList.Add(value);if(password is not null) start.ArgumentList.Add("-p"+password);
        using(var process=Process.Start(start)!) {var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();await process.WaitForExitAsync();await stdout;await stderr;Check(label+" official CLI reads exact original bytes",process.ExitCode==0 && File.ReadAllBytes(Path.Combine(official,"中文目录/内容.bin")).SequenceEqual(content));}
        var foreign=Path.Combine(folder,"foreign."+format);var foreignStart=new ProcessStartInfo(Path.Combine(root,"engines",version,"7z.exe")){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
        foreach(var value in new[]{"a","-y","-t"+format,"-v4096b",foreign,source}) foreignStart.ArgumentList.Add(value);
        if(password is not null) {foreignStart.ArgumentList.Add("-p"+password);foreignStart.ArgumentList.Add(format=="7z"?"-mhe=on":"-mem=AES256");}
        using(var process=Process.Start(foreignStart)!) {var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();await process.WaitForExitAsync();await stdout;await stderr;
            var foreignTarget=Path.Combine(folder,"foreign-extracted");var foreignRead=await client.ExtractAsync(foreign+".001",foreignTarget,password);
            Check(label+" reads official CLI produced volumes",process.ExitCode==0 && foreignRead.IsSuccess && File.ReadAllBytes(Path.Combine(foreignTarget,"中文目录/内容.bin")).SequenceEqual(content));}
        var joined=Path.Combine(folder,"joined."+format);await using(var file=File.Create(joined)) foreach(var part in parts) {await using var input=File.OpenRead(part);await input.CopyToAsync(file);}
        Check(label+" concatenated volumes form valid original-format archive",(await client.TestAsync(joined,password)).IsSuccess);
        var snapshots=parts.ToDictionary(p=>p,p=>File.ReadAllBytes(p));var refused=await service.CreateAsync([source],output,format,password:password,replace:true,volumeSize:4096);
        Check(label+" existing volume set cannot be overwritten",refused.Engine.Code=="output_exists" && snapshots.All(p=>File.ReadAllBytes(p.Key).SequenceEqual(p.Value)));
        Check(label+" non-first volume clearly refused",(await client.TestAsync(parts[1],password)).Code=="first_volume_required");
        foreach(var index in new[]{1,parts.Count-1})
        {
            var missing=Path.Combine(folder,"missing-"+index);Directory.CreateDirectory(missing);
            foreach(var (part,i) in parts.Select((p,i)=>(p,i))) if(i!=index) File.Copy(part,Path.Combine(missing,Path.GetFileName(part)));
            var failed=await client.TestAsync(Path.Combine(missing,Path.GetFileName(parts[0])),password);
            Check(label+" missing volume "+index+" never reports success",!failed.IsSuccess && failed.Code=="missing_volume");
        }
        if(encrypted) {Check(label+" wrong password cannot validate",!(await client.TestAsync(parts[0],"wrong-password")).IsSuccess);Check(label+" encryption remains required",(await client.TestAsync(parts[0])).NeedsPassword);}
        foreach(var phase in new[]{"compressing","testing","committing"})
        {
            var cancelledOutput=Path.Combine(folder,"cancel-"+phase+"."+format);using var cancel=new CancellationTokenSource();
            var outcome=await service.CreateAsync([source],cancelledOutput,format,password:password,volumeSize:4096,cancellationToken:cancel.Token,
                progress:new ForwardProgress<TaskUpdate>(value=>{if(value.State==phase) cancel.Cancel();}));
            Check(label+" cancellation "+phase+" cleans owned volumes",outcome.Engine.Status=="cancelled" && !Directory.GetFiles(folder,"cancel-"+phase+".*").Any() && !Directory.GetDirectories(folder,".nomozip-volumes-*").Any());
        }
        var collision=Path.Combine(folder,"collision."+format);bool injected=false;
        var failedCommit=await service.CreateAsync([source],collision,format,password:password,volumeSize:4096,
            progress:new ForwardProgress<TaskUpdate>(value=>{if(value.State=="committing" && !injected) {injected=true;File.WriteAllText(collision+".002","existing independent file");}}));
        Check(label+" actual partial commit collision preserves external file",!failedCommit.Engine.IsSuccess && !File.Exists(collision+".001") && File.ReadAllText(collision+".002")=="existing independent file" && !File.Exists(collision+".003"));
        var corruptOutput=Path.Combine(folder,"bad."+format);
        var corrupted=await new CompressionService(new CorruptCreator(client),client).CreateAsync([source],corruptOutput,format,password:password,volumeSize:4096);
        Check(label+" damaged temporary volume blocks delivery",!corrupted.Engine.IsSuccess && !Directory.GetFiles(folder,"bad.*").Any() && !Directory.GetDirectories(folder,".nomozip-volumes-*").Any());
    }
    var small=Path.Combine(data,version+"-small.7z");var only=await service.CreateAsync([Path.Combine(source,"zero.bin")],small,"7z",volumeSize:1024*1024);
    Check(version+" one-volume archive follows same first-part convention",only.Engine.IsSuccess && only.Volumes?.Count==1 && (await client.ListAsync(only.OutputPath)).Items.Count==1);
    var many=Path.Combine(data,version+"-many.7z");var manySource=Path.Combine(data,version+"-many.bin");await File.WriteAllBytesAsync(manySource,RandomNumberGenerator.GetBytes(129000));
    var thousand=await service.CreateAsync([manySource],many,"7z",volumeSize:128,level:0);
    Check(version+" numbering after 999 volumes and seeks remain valid",thousand.Engine.IsSuccess && thousand.Volumes?.Count>1000 && File.Exists(many+".1000") && (await client.TestAsync(thousand.OutputPath)).IsSuccess);
    Check(version+" non-first volume after 999 clearly refused",(await client.TestAsync(many+".1000")).Code=="first_volume_required");
    var occupied=Path.Combine(data,version+"-orphan.7z");await File.WriteAllTextAsync(occupied+".9999","keep orphan");
    var orphan=await service.CreateAsync([source],occupied,"7z",volumeSize:4096);Check(version+" orphan high-number volume prevents mixed sets",orphan.Engine.Code=="output_exists" && File.ReadAllText(occupied+".9999")=="keep orphan");
    Check(version+" invalid volume size refused",(await service.CreateAsync([source],occupied+"invalid","7z",volumeSize:1)).Engine.Code=="invalid_volume_size");
}
var hashes=versions.ToDictionary(v=>v,v=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(root,"engines",v,"7z.dll")))).ToLowerInvariant());
var passed=checks.Count(v=>v.Value);await File.WriteAllTextAsync(Path.Combine(root,"artifacts/volume-validation.json"),JsonSerializer.Serialize(new{passed,total=checks.Count,checks,engineHashes=hashes},new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine($"{passed}/{checks.Count} passed");return passed==checks.Count?0:1;
sealed class CorruptCreator(IArchiveCreator creator):IArchiveCreator
{
    public async Task<EngineResult> CreateAsync(CompressionRequest request,string output,string? password,IProgress<EngineProgress>? progress=null,CancellationToken cancellationToken=default)
    {var result=await creator.CreateAsync(request,output,password,progress,cancellationToken);if(result.IsSuccess) await File.WriteAllTextAsync(output+".001","corrupt",cancellationToken);return result;}
}
