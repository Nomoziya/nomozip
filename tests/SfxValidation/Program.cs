using System.Security.Cryptography;
using System.Text.Json;
using ArchivePassword.Core;
using ArchivePassword.Infrastructure;

var root=Path.GetFullPath(args[0]);var versions=args.Length>1?args.Skip(1).SelectMany(value=>value.Split(',')).Distinct().ToArray():["26.04","26.03"];
var data=Path.Combine(root,"artifacts/sf-"+Guid.NewGuid().ToString("N")[..8]);Directory.CreateDirectory(data);var source=Path.Combine(data,"中文目录");Directory.CreateDirectory(Path.Combine(source,"empty"));
var content=RandomNumberGenerator.GetBytes(16384);await File.WriteAllBytesAsync(Path.Combine(source,"data.bin"),content);await File.WriteAllBytesAsync(Path.Combine(source,"zero.bin"),[]);
await File.WriteAllTextAsync(Path.Combine(source,"autorun.cmd"),"@echo executed>\"%TEMP%\\must-not-run.txt\"");
var checks=new Dictionary<string,bool>();void Check(string name,bool value) {checks[name]=value;Console.WriteLine((value?"PASS ":"FAIL ")+name);}
try
{
    foreach(var version in versions)
    {
        var engineFolder=Path.Combine(root,"engines",version);using var manifest=JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(engineFolder,"manifest.json")));
        var module=new SfxModule(version,manifest.RootElement.GetProperty("sfxSha256").GetString()!,await File.ReadAllBytesAsync(Path.Combine(engineFolder,"7z.sfx")));
        var client=new ArchiveEngineClient(Path.Combine(root,"artifacts/worker/ArchiveEngineWorker.exe"),Path.Combine(engineFolder,"7z.dll"));var service=new SfxCompressionService(client,client);
        Check(version+" exact official module and version",module.IsValid() && Version.Parse(System.Diagnostics.FileVersionInfo.GetVersionInfo(Path.Combine(engineFolder,"7z.sfx")).FileVersion!)==Version.Parse(version));
        foreach(var level in new[]{0,5,9}) foreach(var kind in new[]{0,1,2})
        {
            var label=$"{version} {level} {kind}";var folder=Path.Combine(data,version+level+kind);Directory.CreateDirectory(folder);var output=Path.Combine(folder,"package.exe");var password=kind==0?null:"synthetic 密码 123";
            var made=await service.CreateAsync([source],output,module,level,password,kind==2);Check(label+" complete validated EXE",made.Engine.IsSuccess && File.Exists(output));if(!made.Engine.IsSuccess) continue;
            Check(label+" unchanged official executable prefix",File.ReadAllBytes(output).AsSpan(0,module.Bytes.Length).SequenceEqual(module.Bytes));
            var listed=await client.ListAsync(output,password);Check(label+" actual 7z entries and encryption",listed.IsSuccess && listed.Format.Equals("7z",StringComparison.OrdinalIgnoreCase) && listed.Items.Count==5 && listed.Items.Where(item=>!item.Directory && item.Size>0).All(item=>item.Encrypted==(kind!=0)));
            Check(label+" hidden filenames only when selected",(await client.ListAsync(output)).IsSuccess==(kind!=2));
            var target=Path.Combine(folder,"worker");var extracted=await client.ExtractAsync(output,target,password);Check(label+" worker extracts exact data and empty directory",extracted.IsSuccess && content.SequenceEqual(File.ReadAllBytes(Path.Combine(target,"中文目录/data.bin"))) && Directory.Exists(Path.Combine(target,"中文目录/empty")));
            var nativeTarget=Path.Combine(folder,"standalone");var exit=await Task.Run(()=>NativeSfxRunner.Run(output,nativeTarget,password));Check(label+" actual standalone EXE exact bytes without external tools",exit==0 && content.SequenceEqual(File.ReadAllBytes(Path.Combine(nativeTarget,"中文目录/data.bin"))) && Directory.Exists(Path.Combine(nativeTarget,"中文目录/empty")));
            Check(label+" extraction does not execute included script",!File.Exists(Path.Combine(folder,"must-not-run.txt")));
            if(kind!=0) Check(label+" wrong password never validates",!(await client.TestAsync(output,"wrong")).IsSuccess);
            var before=File.ReadAllBytes(output);var blocked=await service.CreateAsync([source],output,module,level,password,kind==2);Check(label+" existing EXE retained without replacement",blocked.Engine.Code=="output_exists" && before.SequenceEqual(File.ReadAllBytes(output)));
            var replaced=await service.CreateAsync([source],output,module,level,password,kind==2,replace:true);Check(label+" explicit replacement validated",replaced.Engine.IsSuccess && (await client.TestAsync(output,password)).IsSuccess);
            var replacementBytes=File.ReadAllBytes(output);
            foreach(var phase in new[]{"compressing","assembling","testing","committing"})
            {
                using var cancel=new CancellationTokenSource();var cancelled=await service.CreateAsync([source],output,module,level,password,kind==2,replace:true,token:cancel.Token,
                    progress:new ForwardProgress<TaskUpdate>(value=>{if(value.State==phase) cancel.Cancel();}));
                Check(label+" cancel "+phase+" keeps exact prior EXE",cancelled.Engine.Status=="cancelled" && replacementBytes.SequenceEqual(File.ReadAllBytes(output)) && !Directory.GetFiles(folder,".nomozip-sfx-*").Any());
            }
        }
        var invalid=module with {Bytes=module.Bytes.Select((value,index)=>index==100?(byte)(value^1):value).ToArray()};var bad=Path.Combine(data,version+"-bad.exe");
        Check(version+" altered module refused before output",(await service.CreateAsync([source],bad,invalid)).Engine.Code=="sfx_module_invalid" && !File.Exists(bad));
        Check(version+" non EXE destination refused",(await service.CreateAsync([source],bad+".7z",module)).Engine.Code=="sfx_output_extension");
        Check(version+" sources unchanged",File.ReadAllBytes(Path.Combine(source,"data.bin")).SequenceEqual(content));
        var kept=Path.Combine(data,version+"-kept.exe");await File.WriteAllTextAsync(kept,"keep original EXE");var prior=File.ReadAllBytes(kept);
        var damaged=await new SfxCompressionService(new CorruptCreator(client),client).CreateAsync([source],kept,module,replace:true);
        Check(version+" corrupted candidate preserves exact original EXE",!damaged.Engine.IsSuccess && prior.SequenceEqual(File.ReadAllBytes(kept)) && !Directory.GetFiles(data,".nomozip-sfx-*").Any());
        using(var locked=new FileStream(kept,FileMode.Open,FileAccess.Read,FileShare.Read))
        {
            var refused=await service.CreateAsync([source],kept,module,replace:true);Check(version+" actual commit lock preserves exact original EXE",!refused.Engine.IsSuccess && prior.SequenceEqual(File.ReadAllBytes(kept)) && !Directory.GetFiles(data,".nomozip-sfx-*").Any());
        }
    }
}
catch(Exception error) {Check("exception "+error,false);}
var hashes=versions.ToDictionary(version=>version,version=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(root,"engines",version,"7z.dll")))).ToLowerInvariant());
var moduleHashes=versions.ToDictionary(version=>version,version=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(root,"engines",version,"7z.sfx")))).ToLowerInvariant());
var passed=checks.Count(value=>value.Value);await File.WriteAllTextAsync(Path.Combine(root,"artifacts/sfx-validation.json"),JsonSerializer.Serialize(new{passed,total=checks.Count,checks,engineHashes=hashes,moduleHashes,privateDesktopExecution=true},new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine($"{passed}/{checks.Count} passed");return passed==checks.Count?0:1;
sealed class CorruptCreator(IArchiveCreator creator):IArchiveCreator
{
    public async Task<EngineResult> CreateAsync(CompressionRequest request,string output,string? password,IProgress<EngineProgress>? progress=null,CancellationToken cancellationToken=default)
    {var result=await creator.CreateAsync(request,output,password,progress,cancellationToken);if(result.IsSuccess) await File.WriteAllTextAsync(output,"corrupt",cancellationToken);return result;}
}
