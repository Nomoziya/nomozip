using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using ArchivePassword.Core;
using ArchivePassword.Infrastructure;

var root=Path.GetFullPath(args[0]);var data=Path.Combine(root,"artifacts/drag-data-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(data);
var checks=new Dictionary<string,bool>();void Check(string name,bool value) {checks.Add(name,value);Console.WriteLine((value?"PASS ":"FAIL ")+name);}
var versions=args.Length>1?args.Skip(1).SelectMany(v=>v.Split(',')).Distinct().ToArray():["26.04","26.03"];
try
{
    foreach(var version in versions)
    {
        var label=version+" ";var profile=Path.Combine(data,version);Directory.CreateDirectory(profile);var temp=Path.Combine(profile,"drag-temp");
        bool Clean()=>!Directory.Exists(temp) || !Directory.EnumerateFileSystemEntries(temp).Any();
        var archive=Path.Combine(profile,"目录.zip");
        using(var zip=ZipFile.Open(archive,ZipArchiveMode.Create)) foreach(var path in new[]{"folder/","folder/中文.txt","folder/sub/b.txt","folder2/hidden.txt","empty/","outside.txt"})
        {var entry=zip.CreateEntry(path);if(!path.EndsWith('/')) {using var writer=new StreamWriter(entry.Open());writer.Write(path+" 😀");}}
        var beforeArchive=File.ReadAllBytes(archive);var client=new ArchiveEngineClient(Path.Combine(root,"artifacts/worker/ArchiveEngineWorker.exe"),Path.Combine(root,"engines",version,"7z.dll"));
        var vaultPath=Path.Combine(profile,"passwords.apv");var vault=new DpapiPasswordVault(vaultPath);
        foreach(var secret in new[]{"password","password2","drag-secret"}) await vault.SaveAsync(new(Guid.NewGuid(),"synthetic "+secret,secret,"",true,DateTimeOffset.UtcNow));
        if(version=="26.03") await vault.EnableMasterPasswordAsync("synthetic-master");
        var beforeVault=File.ReadAllBytes(vaultPath);var service=new ArchiveDragService(client,vault,new PasswordCandidateProvider(vault),temp,new WindowsExtractionOrigin());
        Task<PasswordPromptResponse?> NoPassword(EngineResult _)=>Task.FromResult<PasswordPromptResponse?>(null);
        var list=await client.ListAsync(archive);Check(label+"real listing",list.IsSuccess);
        foreach(var path in new[]{"folder","folder/sub/b.txt","empty","outside.txt"})
        {
            var entry=list.Items.Single(e=>e.Path.Replace('\\','/').TrimEnd('/')==path);var selection=ArchiveSelection.Expand(list.Items,[entry.Index]);
            var result=await service.PrepareAsync(archive,selection,true,NoPassword);
            Check(label+path+" prepared selected range",result.Engine.IsSuccess && result.Content is not null);
            if(result.Content is not { } lease) continue;
            var expectedRoot=path.Split('/')[0];
            Check(label+path+" top-level file-drop preserves hierarchy",lease.Paths.Count==1 && Path.GetFileName(lease.Paths.Single())==expectedRoot && !Directory.Exists(Path.Combine(lease.DirectoryPath,"folder2")));
            var files=Directory.GetFiles(lease.DirectoryPath,"*",SearchOption.AllDirectories);
            Check(label+path+" exact selected file bytes",files.Length==selection.Count(e=>!e.Directory) && files.All(f=>File.ReadAllText(f)==Path.GetRelativePath(lease.DirectoryPath,f).Replace('\\','/')+" 😀"));
            Check(label+path+" empty directories preserved",!entry.Directory || Directory.Exists(Path.Combine(lease.DirectoryPath,path)));
            Check(label+path+" readonly source and vault",beforeArchive.SequenceEqual(File.ReadAllBytes(archive)) && beforeVault.SequenceEqual(File.ReadAllBytes(vaultPath)));
            Check(label+path+" scoped idempotent cleanup",lease.Cleanup() is null && lease.Cleanup() is null && Clean() && File.Exists(archive));
        }
        var single=list.Items.Single(e=>e.Path=="outside.txt");
        foreach(var invalid in new IReadOnlyList<ArchiveEntry>[]{[],[single,single],[single with {Path="stale.txt"}],[single with {Index=uint.MaxValue}]})
        {
            var result=await service.PrepareAsync(archive,invalid,false,NoPassword);
            Check(label+"invalid selection "+checks.Count,!result.Engine.IsSuccess && result.Content is null && Clean());
        }
        using(var cancel=new CancellationTokenSource())
        {cancel.Cancel();var stopped=await service.PrepareAsync(archive,[single],true,NoPassword,token:cancel.Token);Check(label+"pre-cancel cleans temporary files",stopped.Engine.Status=="cancelled" && stopped.Content is null && Clean());}
        var source=Path.Combine(profile,"source.txt");await File.WriteAllTextAsync(source,"encrypted bytes 中文 😀");var encrypted=Path.Combine(profile,"header.7z");
        var created=await client.CreateAsync(new([new(source,"nested/secret.txt",false),new(source,"ignore.txt",false)],"7z",5,true),encrypted,"drag-secret");
        Check(label+"real encrypted solid creation",created.IsSuccess);var encryptedList=await client.ListAsync(encrypted,"drag-secret");var chosen=encryptedList.Items.Single(e=>e.Path.Replace('\\','/')=="nested/secret.txt");
        foreach(var auto in new[]{true,false})
        {
            int prompts=0;var ready=await service.PrepareAsync(encrypted,[chosen],auto,_=>{prompts++;return Task.FromResult<PasswordPromptResponse?>(new("drag-secret",true,"must not save"));});
            Check(label+"encrypted selected bytes "+auto,ready.Engine.IsSuccess && ready.Content is not null && File.ReadAllBytes(Path.Combine(ready.Content.DirectoryPath,"nested/secret.txt")).SequenceEqual(File.ReadAllBytes(source)));
            Check(label+"password auto or manual "+auto,prompts==(auto?0:1));
            Check(label+"no new password or success association "+auto,beforeVault.SequenceEqual(File.ReadAllBytes(vaultPath)));
            Check(label+"encrypted cleanup "+auto,ready.Content?.Cleanup() is null && Clean());
        }
        var refused=await service.PrepareAsync(encrypted,[chosen],false,NoPassword);
        Check(label+"password cancel leaves no files",refused.Engine.Status=="cancelled" && refused.Content is null && Clean());
        using(var stopping=new CancellationTokenSource())
        {
            var stopped=await service.PrepareAsync(encrypted,[chosen],true,NoPassword,new ForwardProgress<TaskUpdate>(update=>{if(update.State=="extracting") stopping.Cancel();}),stopping.Token);
            Check(label+"cancel after verification cleans prepared range",stopping.IsCancellationRequested && stopped.Engine.Status=="cancelled" && stopped.Content is null && Clean());
        }
        foreach(var fixture in new[]{"mixed-traditional.zip","mixed-aes.zip","test_read_format_rar5_encrypted.rar"})
        {
            var input=Path.Combine(root,"tests/fixtures/generated",fixture);var entries=await client.ListAsync(input,"password");var selection=entries.Items.Where(e=>e.Encrypted && !e.Directory).ToArray();
            var prepared=await service.PrepareAsync(input,selection,true,NoPassword);
            Check(label+fixture+" per-entry passwords",prepared.Engine.IsSuccess && prepared.Content is not null && Directory.GetFiles(prepared.Content.DirectoryPath,"*",SearchOption.AllDirectories).Length==selection.Length);
            Check(label+fixture+" readonly vault",beforeVault.SequenceEqual(File.ReadAllBytes(vaultPath)));
            Check(label+fixture+" cleanup",prepared.Content?.Cleanup() is null && Clean());
        }
        var origin="[ZoneTransfer]\r\nZoneId=3\r\n";await File.WriteAllTextAsync(archive+":Zone.Identifier",origin);
        var marked=await service.PrepareAsync(archive,[single],false,NoPassword);
        Check(label+"origin retained on prepared bytes",marked.Content is not null && File.ReadAllText(marked.Content.Paths.Single()+":Zone.Identifier")==origin);
        Check(label+"origin cleanup",marked.Content?.Cleanup() is null && Clean());
        var held=await service.PrepareAsync(archive,[single],false,NoPassword);
        using(var handle=new FileStream(held.Content!.Paths.Single(),FileMode.Open,FileAccess.Read,FileShare.Read))
        {var warning=held.Content.Cleanup();Check(label+"locked cleanup reports actual path",warning?.Contains(held.Content.DirectoryPath)==true && Directory.Exists(held.Content.DirectoryPath));}
        Check(label+"cleanup can retry after target read",held.Content.Cleanup() is null && Clean());
        Check(label+"all operations preserve source and password library",beforeArchive.SequenceEqual(File.ReadAllBytes(archive)) && beforeVault.SequenceEqual(File.ReadAllBytes(vaultPath)));
    }
}
catch(Exception error) {Console.WriteLine(error);Check("exception "+error,false);}
finally {if(Directory.Exists(data)) Directory.Delete(data,true);}
var hashes=versions.ToDictionary(v=>v,v=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(root,"engines",v,"7z.dll")))).ToLowerInvariant());
await File.WriteAllTextAsync(Path.Combine(root,"artifacts/drag-validation.json"),JsonSerializer.Serialize(new{passed=checks.Values.All(v=>v),total=checks.Count,checks,engineHashes=hashes},new JsonSerializerOptions{WriteIndented=true}));
return checks.Values.All(v=>v)?0:1;
