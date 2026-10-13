using System.Text;
using System.Text.Json;
using ArchivePassword.Core;
using ArchivePassword.Infrastructure;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
var root=Path.GetFullPath(args[0]);var data=Path.Combine(root,"artifacts/encoding-validation",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(data);
var checks=new Dictionary<string,bool>();void Check(string name,bool passed) {checks.Add(name,passed);Console.WriteLine((passed?"PASS ":"FAIL ")+name);}
var fixtures=new[] {(936,"中文目录/资料.txt"),(932,"日本語/資料.txt"),(437,"Café/naïve.txt"),(65001,"中文/日本語.txt")};
var fixturePaths=new Dictionary<int,string>();
foreach(var (cp,name) in fixtures) {var path=Path.Combine(data,$"legacy-{cp}.zip");WriteZip(path,Encoding.GetEncoding(cp).GetBytes(name),0,[],"actual content"u8.ToArray());fixturePaths[cp]=path;}
await File.WriteAllTextAsync(Path.Combine(root,"artifacts/encoding-fixtures.json"),JsonSerializer.Serialize(new {zip=fixturePaths[936],name=fixtures[0].Item2}));
var versions=args.Length>1?args[1].Split(',').Distinct():new[] {"26.04","26.03"};
var worker=Path.Combine(root,"artifacts/worker/ArchiveEngineWorker.exe");
foreach(var version in versions)
{
    ArchiveEngineClient Client(int cp=0)=>new(worker,Path.Combine(root,$"engines/{version}/7z.dll"),cp);
    foreach(var (cp,name) in fixtures)
    {
        var client=Client(cp);var archive=fixturePaths[cp];var listed=await client.ListAsync(archive);
        Check($"{version} CP{cp} official listing",listed.IsSuccess && listed.Items.Single().Path.Replace('\\','/')==name);
        Check($"{version} CP{cp} readonly test",(await client.TestAsync(archive)).IsSuccess);
        var output=Path.Combine(data,$"{version}-{cp}-all");var extracted=await client.ExtractAsync(archive,output);
        Check($"{version} CP{cp} extraction path and bytes",extracted.IsSuccess && File.Exists(Path.Combine(output,name)) && File.ReadAllBytes(Path.Combine(output,name)).SequenceEqual("actual content"u8.ToArray()));
        var selection=listed.Items;var passwords=new Dictionary<uint,string>();
        Check($"{version} CP{cp} selected test",(await client.TestSelectionAsync(archive,selection,null,passwords)).IsSuccess);
        var selectedOut=Path.Combine(data,$"{version}-{cp}-selected");var selected=await client.ExtractSelectionAsync(archive,selectedOut,selection,null,passwords);
        Check($"{version} CP{cp} selected extraction path and bytes",selected.IsSuccess && File.ReadAllBytes(Path.Combine(selectedOut,name)).SequenceEqual("actual content"u8.ToArray()));
    }
    var raw=Encoding.GetEncoding(936).GetBytes("中文目录/资料.txt");var unicode="Unicode/日本語.txt";
    using var extra=new MemoryStream();using(var extraWriter=new BinaryWriter(extra,Encoding.UTF8,true)) {extraWriter.Write((ushort)0x7075);extraWriter.Write((ushort)(5+Encoding.UTF8.GetByteCount(unicode)));extraWriter.Write((byte)1);extraWriter.Write(Crc(raw));extraWriter.Write(Encoding.UTF8.GetBytes(unicode));}
    foreach(var (kind,flag,bytes,fields,expected) in new[] {("flag",(ushort)0x800,Encoding.UTF8.GetBytes(unicode),Array.Empty<byte>(),unicode),("unicode-extra",(ushort)0,raw,extra.ToArray(),unicode)})
    {
        var zip=Path.Combine(data,$"{version}-{kind}.zip");WriteZip(zip,bytes,flag,fields,[1,2,3]);
        var client=Client(932);var listing=await client.ListAsync(zip);Check($"{version} {kind} overrides manual CP",listing.IsSuccess && listing.Items.Single().Path.Replace('\\','/')==expected);
        var output=Path.Combine(data,$"{version}-{kind}-output");Check($"{version} {kind} actual output remains Unicode",(await client.ExtractAsync(zip,output)).IsSuccess && File.ReadAllBytes(Path.Combine(output,expected)).SequenceEqual(new byte[] {1,2,3}));
    }
    var gbk=Client(936);var correct=await gbk.ListAsync(fixturePaths[936]);var wrong=Client(437);
    Check(version+" changed encoding rejects stale selection",(await wrong.TestSelectionAsync(fixturePaths[936],correct.Items,null,new Dictionary<uint,string>())).Code=="selection_changed");
    var denied=Path.Combine(data,version+"-stale-output");Check(version+" stale selected extraction emits no file",(await wrong.ExtractSelectionAsync(fixturePaths[936],denied,correct.Items,null,new Dictionary<uint,string>())).Code=="selection_changed" && !Directory.Exists(denied));
    Check(version+" automatic default unchanged",(await Client().ListAsync(fixturePaths[936])).IsSuccess);
    var source=Path.Combine(data,version+"-source");Directory.CreateDirectory(source);File.WriteAllText(Path.Combine(source,"中文.txt"),"encrypted bytes");
    var encrypted=Path.Combine(data,version+"-encrypted.zip");
    var created=await Client().CreateAsync(new CompressionRequest([new CompressionEntry(Path.Combine(source,"中文.txt"),"source/中文.txt",false)],"zip",5,false),encrypted,"ascii-password");
    Check(version+" encrypted ZIP creation retained",created.IsSuccess);
    var encryptedNames=await gbk.ListAsync(encrypted);Check(version+" encrypted Unicode ZIP retains names",encryptedNames.IsSuccess && encryptedNames.Items.Any(item=>item.Path.Replace('\\','/').EndsWith("中文.txt")));
    Check(version+" encoding does not bypass password",(await gbk.TestAsync(encrypted)).NeedsPassword);
    var encryptedFile=encryptedNames.Items.Single(item=>!item.Directory);var passwordsByIndex=new Dictionary<uint,string> {[encryptedFile.Index]="ascii-password"};
    Check(version+" per-item password test retained",(await gbk.TestWithPasswordsAsync(encrypted,null,passwordsByIndex)).IsSuccess);
    var encryptedOut=Path.Combine(data,version+"-encrypted-output");var decoded=await gbk.ExtractWithPasswordsAsync(encrypted,encryptedOut,null,passwordsByIndex);
    Check(version+" encrypted actual bytes preserved",decoded.IsSuccess && File.ReadAllText(Path.Combine(encryptedOut,encryptedFile.Path))=="encrypted bytes");
    var nonzip=Path.Combine(data,version+".7z");Check(version+" 7z creation unchanged",(await Client().CreateAsync(new CompressionRequest([new CompressionEntry(Path.Combine(source,"中文.txt"),"source/中文.txt",false)],"7z",5,false),nonzip,null)).IsSuccess);
    Check(version+" explicit encoding rejects non ZIP",(await gbk.ListAsync(nonzip)).Code=="filename_encoding_not_zip");
    Check(version+" default 7z still works",(await Client().TestAsync(nonzip)).IsSuccess);
    using var cancellation=new CancellationTokenSource();cancellation.Cancel();Check(version+" cancelled request does not produce output",(await gbk.ExtractAsync(fixturePaths[936],Path.Combine(data,version+"-cancel"),cancellationToken:cancellation.Token)).Status=="cancelled");
    var unsafeZip=Path.Combine(data,version+"-unsafe.zip");WriteZip(unsafeZip,"../escape.txt"u8.ToArray(),0,[],[4]);
    var vault=new DpapiPasswordVault(Path.Combine(data,version+".apv"));var unsafeResult=await new ExtractionService(gbk,vault,new PasswordCandidateProvider(vault)).ExtractAsync(unsafeZip,Path.Combine(data,version+"-safe"),false,_=>Task.FromResult<PasswordPromptResponse?>(null),_=>Task.FromResult(ExistingFilePolicy.Replace));
    Check(version+" encoding preserves traversal refusal",unsafeResult.Engine.Code=="unsafe_path" && !File.Exists(Path.Combine(data,"escape.txt")));
}
try {await new ArchiveEngineClient(worker,Path.Combine(root,"engines/26.04/7z.dll"),999999).ListAsync(fixturePaths[936]);Check("unknown codepage rejected",false);}catch(ArgumentOutOfRangeException) {Check("unknown codepage rejected",true);}
var oldWorker=Path.Combine(root,"artifacts/file-hash-candidate/ArchiveEngineWorker.exe");
if(File.Exists(oldWorker)) {var old=new ArchiveEngineClient(oldWorker,Path.Combine(root,"engines/26.04/7z.dll"),936);Check("old worker refuses encoded listing",(await old.ListAsync(fixturePaths[936])).Code=="invalid_request_or_worker_error");var target=Path.Combine(data,"old-output");Check("old worker refuses encoded extraction",(await old.ExtractAsync(fixturePaths[936],target)).Code=="invalid_request_or_worker_error" && !Directory.Exists(target));}
await File.WriteAllTextAsync(Path.Combine(root,"artifacts/encoding-validation.json"),JsonSerializer.Serialize(new {total=checks.Count,passed=checks.Count(value=>value.Value),checks},new JsonSerializerOptions {WriteIndented=true}));
return checks.All(value=>value.Value)?0:1;

static uint Crc(byte[] bytes) {uint crc=0xffffffff;foreach(var value in bytes) {crc^=value;for(var bit=0;bit<8;bit++) crc=(crc>>1)^((crc&1)==0?0:0xedb88320);}return ~crc;}
static void WriteZip(string path,byte[] name,ushort flags,byte[] extra,byte[] content)
{
    using var stream=File.Create(path);using var writer=new BinaryWriter(stream);var crc=Crc(content);
    writer.Write(0x04034b50u);writer.Write((ushort)20);writer.Write(flags);writer.Write((ushort)0);writer.Write(0u);writer.Write(crc);writer.Write((uint)content.Length);writer.Write((uint)content.Length);writer.Write((ushort)name.Length);writer.Write((ushort)extra.Length);writer.Write(name);writer.Write(extra);writer.Write(content);
    var central=(uint)stream.Position;writer.Write(0x02014b50u);writer.Write((ushort)20);writer.Write((ushort)20);writer.Write(flags);writer.Write((ushort)0);writer.Write(0u);writer.Write(crc);writer.Write((uint)content.Length);writer.Write((uint)content.Length);writer.Write((ushort)name.Length);writer.Write((ushort)extra.Length);writer.Write((ushort)0);writer.Write((ushort)0);writer.Write((ushort)0);writer.Write(0u);writer.Write(0u);writer.Write(name);writer.Write(extra);
    var size=(uint)stream.Position-central;writer.Write(0x06054b50u);writer.Write((ushort)0);writer.Write((ushort)0);writer.Write((ushort)1);writer.Write((ushort)1);writer.Write(size);writer.Write(central);writer.Write((ushort)0);
}
