using System.Text.Json;
using System.Security.Cryptography;
using System.Diagnostics;
using System.Text;
using ArchivePassword.Core;
using ArchivePassword.Infrastructure;

var root=Path.GetFullPath(args[0]);var folder=Path.Combine(root,"artifacts/ed-"+Guid.NewGuid().ToString("N")[..8]);Directory.CreateDirectory(folder);
var cases=new List<object>();int passed=0,total=0;
void Check(string name,bool value) {total++;if(value) passed++;cases.Add(new{name,passed=value});Console.WriteLine((value?"PASS ":"FAIL ")+name);}
var versions=args.Length>1 ? args.Skip(1).SelectMany(value=>value.Split(',')).Distinct().ToArray() : ["26.04","26.03"];
if(versions.Any(value=>!System.Text.RegularExpressions.Regex.IsMatch(value,@"^\d{2}\.\d{2}$"))) throw new ArgumentException("Invalid engine version.");
foreach(var version in versions)
{
    var client=new ArchiveEngineClient(Path.Combine(root,"artifacts/worker/ArchiveEngineWorker.exe"),Path.Combine(root,"engines",version,"7z.dll"));
    var service=new ArchiveEditingService(client,client,new EmptyCandidates());
    foreach(var format in new[]{"7z","zip"}) foreach(var encrypted in new[]{false,true})
    {
        var name=$"{version} {format} {(encrypted?"encrypted":"plain")}";
        var data=Path.Combine(folder,version+format+encrypted);Directory.CreateDirectory(data);
        var file=Path.Combine(data,"new.txt");await File.WriteAllTextAsync(file,"replacement 中文");
        var original=Path.Combine(data,"original.txt");await File.WriteAllTextAsync(original,"original content");
        var secret=encrypted?"fixture-password":null;var archive=Path.Combine(data,"sample."+format);
        var sources=new[]{new CompressionEntry(original,"目录/a.txt",false),new CompressionEntry(original,"目录/b.txt",false),new CompressionEntry(original,"keep.txt",false)};
        var created=await client.CreateAsync(new(sources,format,5,encrypted),archive,secret);
        Check(name+" source fixture",created.IsSuccess);
        var bytes=File.ReadAllBytes(archive);
        var snapshot=(await client.ListAsync(archive,secret)).Items;
        Task<PasswordPromptResponse?> Ask(EngineResult _)=>Task.FromResult<PasswordPromptResponse?>(secret is null ? null :new(secret,false,"",null));
        var outcome=await service.EditAsync(archive,snapshot,[],[new(file,"new.txt",false)],false,Ask);
        if(!outcome.Engine.IsSuccess) Console.WriteLine(JsonSerializer.Serialize(outcome.Engine));
        Check(name+" adds with byte-identical original backup",outcome.Engine.IsSuccess && outcome.Items.Count==4 && outcome.BackupPath is not null && bytes.SequenceEqual(File.ReadAllBytes(outcome.BackupPath)));
        snapshot=(await client.ListAsync(archive,secret)).Items;bytes=File.ReadAllBytes(archive);
        var replace=snapshot.Single(item=>item.Path.Replace('\\','/')=="目录/a.txt");
        outcome=await service.EditAsync(archive,snapshot,[replace.Index],[new(file,replace.Path,false)],false,Ask);
        Check(name+" replaces exact path",outcome.Engine.IsSuccess && outcome.Items.Count==4 && bytes.SequenceEqual(File.ReadAllBytes(outcome.BackupPath!)));
        snapshot=(await client.ListAsync(archive,secret)).Items;bytes=File.ReadAllBytes(archive);
        var remove=snapshot.Single(item=>item.Path.Replace('\\','/')=="目录/b.txt");
        outcome=await service.EditAsync(archive,snapshot,[remove.Index],[],false,Ask);
        Check(name+" deletes selected and retains others",outcome.Engine.IsSuccess && outcome.Items.Count==3 && bytes.SequenceEqual(File.ReadAllBytes(outcome.BackupPath!)));
        var output=Path.Combine(data,"out");
        var start=new ProcessStartInfo(Path.Combine(root,"engines",version,"7z.exe")){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
        foreach(var argument in new[]{"x","-y","-o"+output,archive}) start.ArgumentList.Add(argument);if(secret is not null) start.ArgumentList.Add("-p"+secret);
        using(var process=Process.Start(start)!) {var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();await process.WaitForExitAsync();await stdout;await stderr;
            Check(name+" official CLI exact files and bytes",process.ExitCode==0 && Directory.GetFiles(output,"*",SearchOption.AllDirectories).Length==3 && File.ReadAllText(Path.Combine(output,"目录/a.txt"))=="replacement 中文" && File.ReadAllText(Path.Combine(output,"new.txt"))=="replacement 中文" && File.ReadAllText(Path.Combine(output,"keep.txt"))=="original content");}
        snapshot=(await client.ListAsync(archive,secret)).Items;bytes=File.ReadAllBytes(archive);
        async Task Failed(string label,IReadOnlyList<uint> remove,IReadOnlyList<CompressionEntry> add,string code,CancellationToken token=default,IProgress<TaskUpdate>? progress=null)
        {
            var result=await service.EditAsync(archive,snapshot,remove,add,false,Ask,progress,token);
            Check(name+" "+label,result.Engine.Code==code && bytes.SequenceEqual(File.ReadAllBytes(archive)) && !Directory.GetFiles(data,".nomozip-edit-*").Any());
        }
        await Failed("collision keeps original",[],[new(file,"keep.txt",false)],"duplicate_archive_path");
        await Failed("traversal keeps original",[],[new(file,"../escape",false)],"unsafe_archive_path");
        await Failed("file/folder collision keeps original",[],[new(file,"keep.txt/child",false)],"duplicate_archive_path");
        await Failed("self-source keeps original",[],[new(archive,"self",false)],"invalid_source");
        await Failed("missing source keeps original",[],[new(file+"missing","missing",false)],"source_or_output_unavailable");
        using(var cancellation=new CancellationTokenSource())
            await Failed("cancel before commit keeps original",[],[new(file,"newer.txt",false)],"cancelled",cancellation.Token,new ForwardProgress<TaskUpdate>(value=>{if(value.State=="editing") cancellation.Cancel();}));
        var stale=snapshot.Select(item=>item.Index==snapshot[0].Index?item with {Size=item.Size+1}:item).ToArray();
        var staleResult=await service.EditAsync(archive,stale,[snapshot[0].Index],[],false,Ask);
        Check(name+" stale snapshot keeps original",staleResult.Engine.Code=="selection_changed" && bytes.SequenceEqual(File.ReadAllBytes(archive)));
        if(encrypted)
        {
            int prompts=0;
            var wrong=await service.EditAsync(archive,snapshot,[snapshot[0].Index],[],false,_=>Task.FromResult<PasswordPromptResponse?>(prompts++==0?new("wrong-fixture",false,"",null):null));
            Check(name+" wrong password then cancel keeps original",wrong.Engine.Status=="cancelled" && bytes.SequenceEqual(File.ReadAllBytes(archive)));
            Check(name+" encryption retained",(await client.TestAsync(archive)).NeedsPassword && (await client.TestAsync(archive,secret)).IsSuccess);
            if(format=="7z") Check(name+" encrypted filenames retained",(await client.ListAsync(archive)).NeedsPassword);
        }
        var reject=new ArchiveEditingService(client,new RejectEditor(),new EmptyCandidates());
        var rejected=await reject.EditAsync(archive,snapshot,[],[new(file,"failed.txt",false)],false,Ask);
        Check(name+" worker failure keeps original and cleans temp",rejected.Engine.Code=="output_write_failed" && bytes.SequenceEqual(File.ReadAllBytes(archive)) && !Directory.GetFiles(data,".nomozip-edit-*").Any());
        var tampered=await new ArchiveEditingService(client,new TamperEditor(client),new EmptyCandidates()).EditAsync(archive,snapshot,[],[new(file,"tampered.txt",false)],false,Ask);
        Check(name+" corrupted candidate cannot replace original",!tampered.Engine.IsSuccess && bytes.SequenceEqual(File.ReadAllBytes(archive)) && !Directory.GetFiles(data,".nomozip-edit-*").Any());
        FileStream? blocking=null;
        try
        {
            var blocked=await service.EditAsync(archive,snapshot,[],[new(file,"blocked.txt",false)],false,Ask,
                new ForwardProgress<TaskUpdate>(value=>{if(value.State=="testing") blocking??=new FileStream(archive,FileMode.Open,FileAccess.Read,FileShare.Read);}));
            Check(name+" actual commit sharing failure keeps original",blocked.Engine.Code=="source_or_output_unavailable" && bytes.SequenceEqual(File.ReadAllBytes(archive)) && !Directory.GetFiles(data,".nomozip-edit-*").Any());
        }
        finally {blocking?.Dispose();}
        using(var cancellation=new CancellationTokenSource())
            await Failed("cancel during final verification keeps original",[],[new(file,"cancel-final.txt",false)],"cancelled",cancellation.Token,new ForwardProgress<TaskUpdate>(value=>{if(value.State=="testing") cancellation.Cancel();}));
        if(format=="zip" && !encrypted)
        {
            const string origin="[ZoneTransfer]\r\nZoneId=3\r\nHostUrl=https://example.test/synthetic.zip\r\n";
            await File.WriteAllTextAsync(archive+":Zone.Identifier",origin);
            var originResult=await service.EditAsync(archive,snapshot,[],[new(file,"origin.txt",false)],false,Ask);
            Check(name+" replacement retains NTFS origin mark",originResult.Engine.IsSuccess && File.ReadAllText(archive+":Zone.Identifier")==origin);
            snapshot=(await client.ListAsync(archive,secret)).Items;
        }
        var newFolder=Path.Combine(data,"folder");Directory.CreateDirectory(Path.Combine(newFolder,"empty"));await File.WriteAllTextAsync(Path.Combine(newFolder,"child.txt"),"child bytes");
        var folderResult=await service.EditAsync(archive,snapshot,[],ArchiveEditingService.CollectSources([newFolder]),false,Ask);
        Check(name+" folder addition preserves child and empty directory",folderResult.Engine.IsSuccess && folderResult.Items.Count==snapshot.Count+3);
        var folderOutput=Path.Combine(data,"folder-output");var folderExtract=await client.ExtractAsync(archive,folderOutput,secret);
        Check(name+" actual folder bytes and empty directory",folderExtract.IsSuccess && File.ReadAllText(Path.Combine(folderOutput,"folder/child.txt"))=="child bytes" && Directory.Exists(Path.Combine(folderOutput,"folder/empty")));
        snapshot=(await client.ListAsync(archive,secret)).Items;bytes=File.ReadAllBytes(archive);
        var empty=await service.EditAsync(archive,snapshot,snapshot.Select(item=>item.Index).ToArray(),[],false,Ask);
        Check(name+" deleting all preserves filename encryption or yields valid empty archive",encrypted && format=="7z" ? empty.Engine.Code=="archive_edit_empty_encrypted" && bytes.SequenceEqual(File.ReadAllBytes(archive)) : empty.Engine.IsSuccess && (await client.ListAsync(archive,secret)).Items.Count==0 && (await client.TestAsync(archive,secret)).IsSuccess);
    }
    var mixed=Path.Combine(folder,version+"-mixed.zip");File.Copy(Path.Combine(root,"tests/fixtures/generated/mixed-aes.zip"),mixed);
    var mixedBytes=File.ReadAllBytes(mixed);var mixedSnapshot=(await client.ListAsync(mixed)).Items;int mixedPrompts=0;
    var mixedResult=await service.EditAsync(mixed,mixedSnapshot,[mixedSnapshot[0].Index],[],false,_=>Task.FromResult<PasswordPromptResponse?>(++mixedPrompts<=2 ? new(mixedPrompts==1?"password":"password2",false,"",null):null));
    Check(version+" mixed-password writes refused with original preserved",mixedResult.Engine.Code=="archive_edit_mixed_passwords" && mixedBytes.SequenceEqual(File.ReadAllBytes(mixed)));
    var rar=Path.Combine(root,"tests/fixtures/generated/plain.rar");
    if(!File.Exists(rar)) rar=Directory.GetFiles(Path.Combine(root,"tests/fixtures"),"*.rar",SearchOption.AllDirectories).First(path=>!path.Contains("encrypted") && !path.Contains("mixed"));
    var rarItems=(await client.ListAsync(rar)).Items;
    var readOnly=await service.EditAsync(rar,rarItems,rarItems.Take(1).Select(item=>item.Index).ToArray(),[],false,_=>Task.FromResult<PasswordPromptResponse?>(null));
    Check(version+" RAR refuses write",readOnly.Engine.Code=="archive_edit_read_only");
    var fixtureSource=Path.Combine(folder,version+"-extra.txt");await File.WriteAllTextAsync(fixtureSource,"extra");
    foreach(var fixture in new[]{("encrypted.7z","测试密碼 ' $ & 空格"),("encrypted-legacy.zip","Fixture-Password-123")})
    {
        var target=Path.Combine(folder,version+"-"+fixture.Item1);File.Copy(Path.Combine(root,"tests/fixtures/generated",fixture.Item1),target);
        var listed=await client.ListAsync(target,fixture.Item2);var previous=File.ReadAllBytes(target);
        var edited=await service.EditAsync(target,listed.Items,[],[new(fixtureSource,"extra.txt",false)],false,_=>Task.FromResult<PasswordPromptResponse?>(new(fixture.Item2,false,"",null)));
        Check(version+" "+fixture.Item1+" updates without dropping data encryption",edited.Engine.IsSuccess && edited.BackupPath is not null && previous.SequenceEqual(File.ReadAllBytes(edited.BackupPath)) && (await client.TestAsync(target)).NeedsPassword);
        Check(version+" "+fixture.Item1+" clear filenames and exact added bytes",(await client.ListAsync(target)).IsSuccess && (await client.ExtractAsync(target,Path.Combine(folder,version+"-"+fixture.Item1+"-out"),fixture.Item2)).IsSuccess && File.ReadAllText(Path.Combine(folder,version+"-"+fixture.Item1+"-out/extra.txt"))=="extra");
    }
    Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    var cpPath=Path.Combine(folder,version+"-gbk.zip");WriteLegacyZip(cpPath,Encoding.GetEncoding(936).GetBytes("中文/资料.txt"));
    var cpClient=new ArchiveEngineClient(Path.Combine(root,"artifacts/worker/ArchiveEngineWorker.exe"),Path.Combine(root,"engines",version,"7z.dll"),936);
    var cpSnapshot=(await cpClient.ListAsync(cpPath)).Items;
    var cpEdited=await new ArchiveEditingService(cpClient,cpClient,new EmptyCandidates()).EditAsync(cpPath,cpSnapshot,[],[new(fixtureSource,"extra.txt",false)],false,_=>Task.FromResult<PasswordPromptResponse?>(null));
    Check(version+" explicit GBK editing preserves old decoded path",cpEdited.Engine.IsSuccess && cpEdited.Items.Any(item=>item.Path.Replace('\\','/')=="中文/资料.txt"));
    var cpOutput=Path.Combine(folder,version+"-cp-out");var cpExtract=await cpClient.ExtractAsync(cpPath,cpOutput);
    Check(version+" explicit GBK edited output exact original and added bytes",cpExtract.IsSuccess && File.ReadAllText(Path.Combine(cpOutput,"中文/资料.txt"))=="original" && File.ReadAllText(Path.Combine(cpOutput,"extra.txt"))=="extra");
}
var hashes=versions.ToDictionary(value=>value,value=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(root,"engines",value,"7z.dll")))).ToLowerInvariant());
await File.WriteAllTextAsync(Path.Combine(root,"artifacts/archive-editing-validation.json"),JsonSerializer.Serialize(new{passed,total,cases,engineHashes=hashes},new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine($"{passed}/{total} passed");return passed==total?0:1;
static void WriteLegacyZip(string path,byte[] name)
{
    var content="original"u8.ToArray();uint crc=0xffffffff;foreach(var b in content) {crc^=b;for(var bit=0;bit<8;bit++) crc=(crc>>1)^((crc&1)==0?0:0xedb88320);}crc=~crc;
    using var stream=File.Create(path);using var writer=new BinaryWriter(stream);
    writer.Write(0x04034b50u);writer.Write((ushort)20);writer.Write((ushort)0);writer.Write((ushort)0);writer.Write(0u);writer.Write(crc);writer.Write((uint)content.Length);writer.Write((uint)content.Length);writer.Write((ushort)name.Length);writer.Write((ushort)0);writer.Write(name);writer.Write(content);
    var central=(uint)stream.Position;writer.Write(0x02014b50u);writer.Write((ushort)20);writer.Write((ushort)20);writer.Write((ushort)0);writer.Write((ushort)0);writer.Write(0u);writer.Write(crc);writer.Write((uint)content.Length);writer.Write((uint)content.Length);writer.Write((ushort)name.Length);writer.Write((ushort)0);writer.Write((ushort)0);writer.Write((ushort)0);writer.Write((ushort)0);writer.Write(0u);writer.Write(0u);writer.Write(name);
    var size=(uint)stream.Position-central;writer.Write(0x06054b50u);writer.Write((ushort)0);writer.Write((ushort)0);writer.Write((ushort)1);writer.Write((ushort)1);writer.Write(size);writer.Write(central);writer.Write((ushort)0);
}
sealed class EmptyCandidates:IPasswordCandidateProvider {public Task<IReadOnlyList<PasswordCandidate>> GetAsync(string path,CancellationToken token=default)=>Task.FromResult<IReadOnlyList<PasswordCandidate>>([]);}
sealed class RejectEditor:IArchiveEditor
{public async Task<EngineResult> UpdateAsync(string archive,string output,ArchiveEditRequest request,string? password,IProgress<EngineProgress>? progress=null,CancellationToken token=default) {await File.WriteAllTextAsync(output,"partial",token);return new(){Code="output_write_failed"};}}
sealed class TamperEditor(IArchiveEditor inner):IArchiveEditor
{public async Task<EngineResult> UpdateAsync(string archive,string output,ArchiveEditRequest request,string? password,IProgress<EngineProgress>? progress=null,CancellationToken token=default) {var result=await inner.UpdateAsync(archive,output,request,password,progress,token);if(result.IsSuccess) await File.WriteAllTextAsync(output,"not an archive",token);return result;}}
