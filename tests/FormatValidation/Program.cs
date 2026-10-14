using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using ArchivePassword.Core;
using ArchivePassword.Infrastructure;

var root = Path.GetFullPath(args[0]);
var folder = Path.Combine(root, "artifacts/fm-" + Guid.NewGuid().ToString("N")[..8]);
var source = Path.Combine(folder, "中文目录"); Directory.CreateDirectory(Path.Combine(source, "空目录"));
var data = Enumerable.Range(0, 100_003).Select(value => (byte)(value * 17)).ToArray();
var file = Path.Combine(source, "内容.txt"); await File.WriteAllBytesAsync(file, data);
var externalSource=Path.Combine(folder,"external-source");Directory.CreateDirectory(externalSource);await File.WriteAllBytesAsync(Path.Combine(externalSource,"external.txt"),data);
await File.WriteAllBytesAsync(Path.Combine(source, "空文件.bin"), []);
var longName = new string('文', 45) + ".txt"; await File.WriteAllTextAsync(Path.Combine(source, longName), "long UTF-8 name");
var versions = args.Length > 1 ? args.Skip(1).SelectMany(value => value.Split(',')).Distinct().ToArray() : ["26.04", "26.03"];
if (versions.Any(value => !System.Text.RegularExpressions.Regex.IsMatch(value, @"^\d{2}\.\d{2}$"))) throw new ArgumentException("Invalid engine version.");
var cases = new List<object>(); int passed = 0, total = 0;
void Check(string name, bool value) { total++; if(value) passed++; cases.Add(new {name,passed=value}); Console.WriteLine((value?"PASS ":"FAIL ")+name); }
async Task<bool> Cli(string version, string working, params string[] arguments)
{
    var start = new ProcessStartInfo(Path.Combine(root,"engines",version,"7z.exe")) {WorkingDirectory=working,UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
    foreach(var argument in arguments) start.ArgumentList.Add(argument);
    using var process = Process.Start(start)!;var output=process.StandardOutput.ReadToEndAsync();var error=process.StandardError.ReadToEndAsync();
    await process.WaitForExitAsync();await output;await error;return process.ExitCode==0;
}
bool ExactTree(string output) => File.ReadAllBytes(Path.Combine(output,"中文目录/内容.txt")).SequenceEqual(data)
    && File.ReadAllText(Path.Combine(output,"中文目录",longName))=="long UTF-8 name"
    && File.Exists(Path.Combine(output,"中文目录/空文件.bin")) && new FileInfo(Path.Combine(output,"中文目录/空文件.bin")).Length==0
    && Directory.Exists(Path.Combine(output,"中文目录/空目录"));
foreach(var version in versions)
{
    var client = new ArchiveEngineClient(Path.Combine(root,"artifacts/worker/ArchiveEngineWorker.exe"),Path.Combine(root,"engines",version,"7z.dll"));
    var service = new CompressionService(client,client);
    foreach(var format in new[]{"tar","gzip","xz"})
    {
        var extension=CompressionFormats.Find(format)!.Extension;
        foreach(var level in new[]{0,1,3,5,7,9})
        {
            var target=Path.Combine(folder,$"{version}-{format}-{level}-内容.txt"+extension);
            var created=await service.CreateAsync([format=="tar"?source:file],target,format,level);
            Check($"{version} {format} level {level} real create and test",created.Engine.IsSuccess && (await client.TestAsync(target)).IsSuccess);
            if(!created.Engine.IsSuccess) continue;
            var listed=await client.ListAsync(target);
            Check($"{version} {format} level {level} nonempty readable inventory",listed.IsSuccess && listed.Items.All(value=>value.Path.Length>0 && !value.Encrypted) && (format=="tar"?listed.Items.Count==5:listed.Items.Count==1));
            var output=target+"-out";var extracted=await client.ExtractAsync(target,output);
            Check($"{version} {format} level {level} worker exact bytes and names",extracted.IsSuccess && (format=="tar"?ExactTree(output):File.ReadAllBytes(Path.Combine(output,Path.GetFileNameWithoutExtension(target))).SequenceEqual(data)));
            var official=target+"-cli";var cli=await Cli(version,folder,"x","-y","-o"+official,target);
            Check($"{version} {format} level {level} official CLI exact bytes",cli && (format=="tar"?ExactTree(official):File.ReadAllBytes(Directory.GetFiles(official).Single()).SequenceEqual(data)));
            if(format=="gzip")
            {
                using var compressed=File.OpenRead(target);using var gzip=new GZipStream(compressed,CompressionMode.Decompress);using var decoded=new MemoryStream();await gzip.CopyToAsync(decoded);
                Check($"{version} gzip level {level} independent .NET decoder",decoded.ToArray().SequenceEqual(data) && (File.ReadAllBytes(target)[3]&8)==0);
            }
            if(format=="tar")
            {
                using var input=File.OpenRead(target);using var reader=new TarReader(input);var contents=new Dictionary<string,byte[]>();TarEntry? entry;
                while((entry=reader.GetNextEntry()) is not null) {if(entry.DataStream is null) continue;using var memory=new MemoryStream();entry.DataStream.CopyTo(memory);contents[entry.Name]=memory.ToArray();}
                Check($"{version} tar level {level} independent .NET decoder Unicode long name",contents["中文目录/内容.txt"].SequenceEqual(data) && System.Text.Encoding.UTF8.GetString(contents["中文目录/"+longName])=="long UTF-8 name");
            }
            var selection=listed.Items.Where(value=>!value.Directory && value.Size==(ulong)data.Length).ToArray();var selectedOutput=target+"-selection";
            var selected=await client.ExtractSelectionAsync(target,selectedOutput,selection,null,new Dictionary<uint,string>());
            Check($"{version} {format} level {level} selection exact bytes matches displayed path",selected.IsSuccess && Directory.GetFiles(selectedOutput,"*",SearchOption.AllDirectories).Length==1 && File.ReadAllBytes(Directory.GetFiles(selectedOutput,"*",SearchOption.AllDirectories).Single()).SequenceEqual(data));
        }
        var existing=Path.Combine(folder,$"{version}-{format}-existing"+extension);await File.WriteAllTextAsync(existing,"preserve original");
        var refused=await service.CreateAsync([file],existing,format);
        Check($"{version} {format} existing output preserved without consent",refused.Engine.Code=="output_exists" && File.ReadAllText(existing)=="preserve original");
        var encrypted=await service.CreateAsync([file],existing,format,password:"synthetic only",replace:true);
        Check($"{version} {format} password explicitly rejected before replacement",encrypted.Engine.Code=="format_encryption_unsupported" && File.ReadAllText(existing)=="preserve original");
        var nativeEncrypted=await client.CreateAsync(new([new(file,"内容.txt",false)],format,5,false),existing+"-native", "synthetic only");
        Check($"{version} {format} worker independently rejects encryption",nativeEncrypted.Code=="format_encryption_unsupported" && !File.Exists(existing+"-native"));
        var nativeSplit=await client.CreateAsync(new([new(file,"内容.txt",false)],format,5,false,1024),existing+"-split",null);
        Check($"{version} {format} worker rejects split",nativeSplit.Code=="format_volumes_unsupported" && !Directory.GetFiles(folder,Path.GetFileName(existing)+"-split*").Any());
        var cancel=new CancellationTokenSource();cancel.Cancel();var cancelled=await service.CreateAsync([file],existing,format,replace:true,cancellationToken:cancel.Token);
        Check($"{version} {format} cancellation preserves destination",cancelled.Engine.Status=="cancelled" && File.ReadAllText(existing)=="preserve original");
        using(var runningCancel=new CancellationTokenSource())
        {
            var running=await service.CreateAsync([file],existing,format,replace:true,progress:new ForwardProgress<TaskUpdate>(value=>{if(value.Progress is not null) runningCancel.Cancel();}),cancellationToken:runningCancel.Token);
            Check($"{version} {format} cancellation during worker output preserves original",runningCancel.IsCancellationRequested && running.Engine.Status=="cancelled" && File.ReadAllText(existing)=="preserve original" && !Directory.GetFiles(folder,".nomozip-create-*").Any());
        }
        using(var commitCancel=new CancellationTokenSource())
        {
            var pending=await new CompressionService(client,new CancelAfterTest(client,commitCancel)).CreateAsync([file],existing,format,replace:true,cancellationToken:commitCancel.Token);
            Check($"{version} {format} cancel after validation before commit preserves original",pending.Engine.Status=="cancelled" && File.ReadAllText(existing)=="preserve original" && !Directory.GetFiles(folder,".nomozip-create-*").Any());
        }
        var bad=new CompressionService(new CorruptCreator(client),client);var corrupt=await bad.CreateAsync([file],existing,format,replace:true);
        Check($"{version} {format} corrupted candidate not committed",!corrupt.Engine.IsSuccess && File.ReadAllText(existing)=="preserve original" && !Directory.GetFiles(folder,".nomozip-create-*").Any());
        var wrong = await new CompressionService(new WrongSourceCreator(client,secondPath:Path.Combine(source,"空文件.bin")),client).CreateAsync([file],existing,format,replace:true);
        Check($"{version} {format} valid archive with wrong inventory not committed",wrong.Engine.Code=="format_validation_failed" && File.ReadAllText(existing)=="preserve original" && !Directory.GetFiles(folder,".nomozip-create-*").Any());
        using(var held=File.Open(existing,FileMode.Open,FileAccess.Read,FileShare.Read))
        {
            var locked=await service.CreateAsync([file],existing,format,replace:true);
            Check($"{version} {format} real commit lock preserves original",!locked.Engine.IsSuccess && File.ReadAllText(existing)=="preserve original" && !Directory.GetFiles(folder,".nomozip-create-*").Any());
        }
        var replaced=await service.CreateAsync([file],existing,format,replace:true);
        Check($"{version} {format} explicit replace validated",replaced.Engine.IsSuccess && (await client.TestAsync(existing)).IsSuccess);
        var zero=Path.Combine(folder,$"{version}-{format}-zero"+extension);var empty=await service.CreateAsync([Path.Combine(source,"空文件.bin")],zero,format);
        var zeroOutput=zero+"-out";var zeroResult=await client.ExtractAsync(zero,zeroOutput);
        Check($"{version} {format} empty file exact round trip",empty.Engine.IsSuccess && zeroResult.IsSuccess && Directory.GetFiles(zeroOutput).Length==1 && new FileInfo(Directory.GetFiles(zeroOutput).Single()).Length==0);
        var reverse=Path.Combine(folder,$"{version}-{format}-official"+extension);
        // Official GZIP stores its optional name in CP_ACP. Use an ASCII header
        // fixture so this reverse-reader check is independent of the runner locale.
        var reverseCreated=await Cli(version,format=="tar"?folder:externalSource,"a","-t"+format,"-y",reverse,format=="tar"?"中文目录":"external.txt");
        var reverseOutput=reverse+"-out";var reverseExtract=await client.ExtractAsync(reverse,reverseOutput);
        Check($"{version} {format} official-created archive worker exact bytes",reverseCreated && reverseExtract.IsSuccess && (format=="tar"?ExactTree(reverseOutput):File.ReadAllBytes(Directory.GetFiles(reverseOutput).Single()).SequenceEqual(data)));
        if(format!="tar")
        {
            foreach(var sources in new[]{new[]{file,Path.Combine(source,"空文件.bin")},new[]{source}})
            {
                var rejected=await service.CreateAsync(sources,existing,format,replace:true);
                Check($"{version} {format} {(sources.Length==1?"folder":"multiple files")} rejected preserves destination",rejected.Engine.Code=="format_single_file_required" && (await client.TestAsync(existing)).IsSuccess);
            }
            var direct=await client.CreateAsync(new([new(source,"中文目录",true)],format,5,false),existing+"-folder",null);
            Check($"{version} {format} native directory guard before writing",direct.Code=="format_single_file_required" && !File.Exists(existing+"-folder"));
        }
    }
}
var hashes=versions.ToDictionary(value=>value,value=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(root,"engines",value,"7z.dll")))).ToLowerInvariant());
await File.WriteAllTextAsync(Path.Combine(root,"artifacts/format-validation.json"),JsonSerializer.Serialize(new{passed,total,cases,engineHashes=hashes},new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine($"{passed}/{total} passed");return passed==total?0:1;
sealed class CorruptCreator(IArchiveCreator inner):IArchiveCreator
{
    public async Task<EngineResult> CreateAsync(CompressionRequest request,string output,string? password,IProgress<EngineProgress>? progress=null,CancellationToken cancellationToken=default)
    {var result=await inner.CreateAsync(request,output,password,progress,cancellationToken);if(result.IsSuccess) await File.WriteAllTextAsync(output,"corrupt synthetic archive",cancellationToken);return result;}
}
sealed class WrongSourceCreator(IArchiveCreator inner,string secondPath):IArchiveCreator
{
    public Task<EngineResult> CreateAsync(CompressionRequest request,string output,string? password,IProgress<EngineProgress>? progress=null,CancellationToken cancellationToken=default)
        =>inner.CreateAsync(request with {Entries=[new(secondPath,request.Entries[0].Path,false)]},output,password,progress,cancellationToken);
}
sealed class CancelAfterTest(IArchiveEngine inner,CancellationTokenSource cancellation):IArchiveEngine
{
    public Task<EngineResult> GetCapabilitiesAsync(CancellationToken token=default)=>inner.GetCapabilitiesAsync(token);
    public Task<EngineResult> ListAsync(string archive,string? password=null,CancellationToken cancellationToken=default)=>inner.ListAsync(archive,password,cancellationToken);
    public async Task<EngineResult> TestAsync(string archive,string? password=null,IProgress<EngineProgress>? progress=null,CancellationToken cancellationToken=default)
    {var result=await inner.TestAsync(archive,password,progress,cancellationToken);cancellation.Cancel();return result;}
    public Task<EngineResult> ExtractAsync(string archive,string output,string? password=null,IProgress<EngineProgress>? progress=null,CancellationToken cancellationToken=default)=>inner.ExtractAsync(archive,output,password,progress,cancellationToken);
}
