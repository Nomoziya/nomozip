using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ArchivePassword.Core;
using ArchivePassword.Infrastructure;

const string Secret="synthetic-F07-unique-secret-59a40";
var options=new JsonSerializerOptions(JsonSerializerDefaults.Web){WriteIndented=true};
string Hash(string path)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
CheckpointBinding Binding(string root,string version,int codePage=0)=>new(version,Hash(Path.Combine(root,"engines",version,"7z.dll")),System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture==System.Runtime.InteropServices.Architecture.Arm64?"arm64":"x64",codePage);
Task<ExistingFilePolicy> Replace(string _)=>Task.FromResult(ExistingFilePolicy.Replace);
if(args[0]=="--child")
{
    var mode=args[1];var root=args[2];var folder=args[3];var archive=args[4];var version=args[5];
    var store=new ExtractionCheckpointStore(Path.Combine(folder,"records"));
    var prior=store.ReadPending().SingleOrDefault();
    var vault=new DpapiPasswordVault(Path.Combine(folder,"vault.apv"));
    var client=new ObservedEngine(new ArchiveEngineClient(Path.Combine(folder,"ArchiveEngineWorker.exe"),Path.Combine(root,"engines",version,"7z.dll")));
    using var cancel=new CancellationTokenSource();

    var progress=new ForwardProgress<TaskUpdate>(update=>
    {
        if(update.State=="checkpoint")
        {
            var record=store.ReadPending().Single();
            if(mode=="cancel") cancel.Cancel();
            if(mode=="stop" && record.Receipts.Any(receipt=>!record.Entries.Single(entry=>entry.Index==receipt.Index).Directory))
            {File.WriteAllText(Path.Combine(folder,"stopped"),Environment.ProcessId.ToString());Thread.Sleep(Timeout.Infinite);}
        }
        if(mode=="commit-stop" && update.State=="checkpoint-commit")
        {File.WriteAllText(Path.Combine(folder,"stopped"),Environment.ProcessId.ToString());Thread.Sleep(Timeout.Infinite);}
    });
    var result=await new ExtractionService(client,vault,new PasswordCandidateProvider(vault),new WindowsExtractionOrigin()).ExtractAsync(archive,Path.Combine(folder,"output"),true,
        _=>Task.FromResult<PasswordPromptResponse?>(new(Secret,false,"")),Replace,progress,cancel.Token,
        checkpoint:new(store,Binding(root,version),prior?.Id));
    await File.WriteAllTextAsync(Path.Combine(folder,"result.json"),JsonSerializer.Serialize(new{result.Engine.Status,result.Engine.Code,result.Warning,completed=client.Completed,process=Environment.ProcessId},options));
    return result.Engine.IsSuccess || result.Engine.Status=="cancelled"?0:1;
}

if(args[0]=="--verify-normal-cancellation")
{
    var cancelProject=Path.GetFullPath(args[1]);var cancelData=Path.Combine(cancelProject,"artifacts/checkpoint-normal-cancel-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(cancelData);
    var contractChecks=new Dictionary<string,bool>();
    try
    {
        foreach(var cancelAtCommit in new[]{false,true})
        {
            var output=Path.Combine(cancelData,cancelAtCommit.ToString());Directory.CreateDirectory(output);var target=Path.Combine(output,"file.txt");File.WriteAllText(target,"old bytes");
            var pause=new ExtractionPause();var stopped=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);using var cancel=new CancellationTokenSource();
            var vault=new DpapiPasswordVault(Path.Combine(cancelData,"unused.apv"));
            var service=new ExtractionService(new NormalCommitEngine(()=>{pause.Pause();stopped.TrySetResult();}),vault,new PasswordCandidateProvider(vault),pause:pause);
            var work=service.ExtractAsync(Path.Combine(cancelData,"synthetic.zip"),output,false,_=>Task.FromResult<PasswordPromptResponse?>(null),Replace,cancellationToken:cancel.Token);
            await stopped.Task.WaitAsync(TimeSpan.FromSeconds(5));
            contractChecks["commit waits before overwrite "+cancelAtCommit]=!work.IsCompleted && File.ReadAllText(target)=="old bytes";
            if(cancelAtCommit)cancel.Cancel();else pause.Resume();
            try {var result=await work;contractChecks["original completion contract "+cancelAtCommit]=!cancelAtCommit && result.Engine.IsSuccess && File.ReadAllText(target)=="new bytes";}
            catch(OperationCanceledException){contractChecks["original cancellation contract "+cancelAtCommit]=cancelAtCommit && File.ReadAllText(target)=="old bytes";}
            contractChecks["private staging cleaned "+cancelAtCommit]=!Directory.EnumerateDirectories(output,".archive-password-*").Any();
        }
    }
    finally {Directory.Delete(cancelData,true);}
    var passed=contractChecks.Values.All(value=>value);
    var report=new{passed,total=contractChecks.Count,checks=contractChecks,largeFixtureUsed=false};
    File.WriteAllText(Path.Combine(cancelProject,"artifacts/checkpoint-normal-cancellation-validation.json"),JsonSerializer.Serialize(report,options));
    Console.WriteLine(JsonSerializer.Serialize(report));return passed?0:1;
}

if(args[0]=="--seed")
{
    var profile=Path.GetFullPath(args[1]);var output=Path.GetFullPath(args[2]);var app=Path.GetFullPath(args[3]);
    Directory.CreateDirectory(profile);Directory.CreateDirectory(Path.GetDirectoryName(output)!);
    var archive=output+"-source.zip";
    using(var zip=ZipFile.Open(archive,ZipArchiveMode.Create)) foreach(var name in new[]{"a.txt","b.txt","c.txt"}) {using var writer=new StreamWriter(zip.CreateEntry(name).Open());writer.Write(name+" preserved extraction progress");}
    var store=new ExtractionCheckpointStore(Path.Combine(profile,"extraction-progress"));
    var vault=new DpapiPasswordVault(Path.Combine(profile,"passwords.apv"));
    var binding=new CheckpointBinding("26.04",Hash(Path.Combine(app,"engines/26.04/7z.dll")),EngineArchitecture.Current,0);
    using var stop=new CancellationTokenSource();
    var result=await new ExtractionService(new ArchiveEngineClient(Path.Combine(app,"ArchiveEngineWorker.exe"),Path.Combine(app,"engines/26.04/7z.dll")),vault,new PasswordCandidateProvider(vault)).ExtractAsync(archive,output,false,
        _=>Task.FromResult<PasswordPromptResponse?>(null),Replace,new ForwardProgress<TaskUpdate>(update=>{if(update.State=="checkpoint")stop.Cancel();}),stop.Token,checkpoint:new(store,binding));
    if(result.Engine.Status!="cancelled") throw new IOException("Could not seed actual interrupted progress.");
    var record=store.ReadPending().Single();var receipt=record.Receipts.Single();var entry=record.Entries.Single(entry=>entry.Index==receipt.Index);
    var manifest=Path.Combine(profile,"extraction-progress",record.Id.ToString("N"),"session.json");
    var staged=Path.Combine(output,".nomozip-checkpoint-"+record.Id.ToString("N"),"files",entry.Path);
    var journal=Path.Combine(Path.GetDirectoryName(manifest)!,"progress-"+record.Journal.ToString("N")+".jsonl");
    Console.WriteLine(JsonSerializer.Serialize(new{files=new[]{manifest,journal,staged,archive}.Select(path=>new{path,sha256=Hash(path)})}));
    return 0;
}

var project=Path.GetFullPath(args[0]);
var data=Path.GetFullPath(Path.Combine(project,"artifacts/checkpoint-data-"+Guid.NewGuid().ToString("N")));Directory.CreateDirectory(data);
var workerOption=Array.IndexOf(args,"--worker");
var worker=Path.GetFullPath(workerOption>=0?args[workerOption+1]:Path.Combine(project,"artifacts/worker/ArchiveEngineWorker.exe"));
var versions=args.Skip(1).TakeWhile(value=>!value.StartsWith("--")).SelectMany(value=>value.Split(',')).ToArray();
if(versions.Length==0) versions=["26.04","26.03"];
var checks=new Dictionary<string,bool>();void Check(string name,bool value){checks.Add(name,value);Console.WriteLine((value?"PASS ":"FAIL ")+name);}
Process Child(string mode,string folder,string archive,string version)
{
    var start=new ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
    foreach(var value in new[]{Assembly.GetExecutingAssembly().Location,"--child",mode,project,folder,archive,version}) start.ArgumentList.Add(value);
    return Process.Start(start)!;
}
async Task<JsonDocument> FinishChild(string folder,string archive,string version)
{
    using var child=Child("resume",folder,archive,version);
    var stdout=child.StandardOutput.ReadToEndAsync();var stderr=child.StandardError.ReadToEndAsync();
    await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
    if(child.ExitCode!=0) throw new IOException("Child failed: "+await stdout+await stderr+File.ReadAllText(Path.Combine(folder,"result.json")));
    return JsonDocument.Parse(File.ReadAllText(Path.Combine(folder,"result.json")));
}
async Task<int[]> StopChild(string mode,string folder,string archive,string version)
{
    File.Delete(Path.Combine(folder,"stopped"));
    using var child=Child(mode,folder,archive,version);
    var deadline=DateTime.UtcNow.AddSeconds(25);
    while(!File.Exists(Path.Combine(folder,"stopped")) && !child.HasExited && DateTime.UtcNow<deadline) await Task.Delay(20);
    if(!File.Exists(Path.Combine(folder,"stopped")))
    {if(!child.HasExited) child.Kill(true);throw new IOException("Stop boundary not reached: "+await child.StandardError.ReadToEndAsync()+await child.StandardOutput.ReadToEndAsync());}
    var natives=Process.GetProcessesByName("ArchiveEngineWorker").Where(process=>
    {try{return string.Equals(process.MainModule!.FileName,Path.Combine(folder,"ArchiveEngineWorker.exe"),StringComparison.OrdinalIgnoreCase);}catch{return false;}}).ToArray();
    var ids=natives.Select(process=>process.Id).ToArray();
    var activeRecord=new ExtractionCheckpointStore(Path.Combine(folder,"records")).ReadPending().Single();
    var lease=Path.Combine(activeRecord.Destination,".nomozip-checkpoint-"+activeRecord.Id.ToString("N"),".nomozip-checkpoint-writer");
    Check(Path.GetFileName(folder)+" "+mode+" active writer lease blocks recovery I/O",ThrowsIo(()=>{using var held=new FileStream(lease,FileMode.Open,FileAccess.ReadWrite,FileShare.None);}));
    // Kill only the actual managed owner, deliberately leave the native child alive.
    child.Kill();await child.WaitForExitAsync();
    foreach(var native in natives)
    {try{await native.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(8));}finally{if(!native.HasExited) native.Kill();native.Dispose();}}
    return ids;
}
async Task<string> NewCase(string label)
{
    var folder=Path.Combine(data,label);Directory.CreateDirectory(folder);
    File.Copy(worker,Path.Combine(folder,"ArchiveEngineWorker.exe"));
    await new DpapiPasswordVault(Path.Combine(folder,"vault.apv")).SaveAsync(new(Guid.NewGuid(),"synthetic",Secret,"",true,DateTimeOffset.UtcNow));
    return folder;
}
async Task<bool> Reject(Func<Task> action,string code)
{try{await action();return false;}catch(CheckpointException error){return error.Code==code;}}
string Stage(CheckpointRecord record)=>Path.Combine(record.Destination,".nomozip-checkpoint-"+record.Id.ToString("N"),"files");
try
{
    foreach(var version in versions)
    {
        var client=new ArchiveEngineClient(worker,Path.Combine(project,"engines",version,"7z.dll"));
        foreach(var kind in new[]{"zip","solid","encrypted","volumes"})
        {
            var label=version+" "+kind;var folder=await NewCase(version+"-"+kind);
            var source=Path.Combine(folder,"source.txt");await File.WriteAllTextAsync(source,string.Concat(Enumerable.Repeat("small contents 中文 😀 ",160)));
            var archive=Path.Combine(folder,kind=="zip"?"input.zip":"input.7z");
            var request=new CompressionRequest([new(source,"a.txt",false),new(source,"folder/b.txt",false),new(source,"c.txt",false),new(folder,"empty",true)],kind=="zip"?"zip":"7z",5,kind=="encrypted",kind=="volumes"?256UL:0UL);
            var created=await client.CreateAsync(request,archive,kind=="encrypted"?Secret:null);
            Check(label+" real small fixture",created.IsSuccess);
            if(kind=="volumes") archive+=".001";
            var beforeVault=File.ReadAllBytes(Path.Combine(folder,"vault.apv"));
            await File.WriteAllTextAsync(archive+":Zone.Identifier","[ZoneTransfer]\r\nZoneId=3\r\n");
            var natives=await StopChild("stop",folder,archive,version);
            var store=new ExtractionCheckpointStore(Path.Combine(folder,"records"));var record=store.ReadPending().Single();
            var retained=record.Receipts.Where(receipt=>!record.Entries.Single(entry=>entry.Index==receipt.Index).Directory).ToArray();
            Check(label+" real managed kill exits native writer",natives.Length==1 && natives.All(id=>!Process.GetProcesses().Any(process=>process.Id==id)));
            Check(label+" durable successful partial progress",retained.Length==1 && record.Receipts.Count<record.Entries.Count);
            Check(label+" source set includes every actual volume",kind!="volumes"?record.Sources.Count==1:record.Sources.Count==Directory.GetFiles(folder,"input.7z.*").Length && record.Sources.Count>1);
            Check(label+" no password persisted",!Directory.GetFiles(Path.Combine(folder,"records"),"*",SearchOption.AllDirectories).Any(path=>File.ReadAllText(path).Contains(Secret,StringComparison.Ordinal)));
            Check(label+" stopped extraction did not write vault",beforeVault.SequenceEqual(File.ReadAllBytes(Path.Combine(folder,"vault.apv"))));
            var first=record.Entries.Single(entry=>entry.Index==retained[0].Index);var staged=Path.Combine(Stage(record),first.Path);
            var bytes=File.ReadAllBytes(staged);var stamp=File.GetLastWriteTimeUtc(staged);
            await using(var own=await store.ResumeAsync(record.Id,Binding(project,version)))
            {
                Check(label+" independent owner sees exact remaining range",own.Remaining.Count==record.Entries.Count-record.Receipts.Count && !own.Remaining.Any(entry=>entry.Index==first.Index));
                Check(label+" second owner rejected",await Reject(async()=>{await using var second=await store.ResumeAsync(record.Id,Binding(project,version));},"checkpoint_busy"));
                Check(label+" discard rejects live owner",await Reject(()=>store.DiscardAsync(record.Id),"checkpoint_busy"));
                Check(label+" source write held until operation ends",ThrowsIo(()=>{using var changed=new FileStream(archive,FileMode.Open,FileAccess.Write,FileShare.ReadWrite);}));
            }
            Check(label+" engine version binding enforced",await Reject(async()=>{await using var wrong=await store.ResumeAsync(record.Id,Binding(project,version) with {EngineVersion="future"});},"checkpoint_binding_changed"));
            Check(label+" engine hash binding enforced",await Reject(async()=>{await using var wrong=await store.ResumeAsync(record.Id,Binding(project,version) with {EngineSha256=new string('a',64)});},"checkpoint_binding_changed"));
            Check(label+" filename encoding binding enforced",await Reject(async()=>{await using var wrong=await store.ResumeAsync(record.Id,Binding(project,version) with {FilenameCodePage=936});},"checkpoint_binding_changed"));
            if(kind=="zip")
            {
                var manifest=Path.Combine(folder,"records",record.Id.ToString("N"),"session.json");var valid=File.ReadAllBytes(manifest);
                File.WriteAllText(manifest,"{broken");
                Check(label+" malformed record refused",await Reject(()=>Task.Run(()=>store.ReadPending()),"checkpoint_invalid"));
                Check(label+" malformed discard does not leak ownership",await Reject(()=>store.DiscardAsync(record.Id),"checkpoint_invalid"));
                File.WriteAllBytes(manifest,valid);
                var traversal=JsonSerializer.Deserialize<CheckpointRecord>(valid,options)!;
                traversal=traversal with {Entries=traversal.Entries.Select((entry,i)=>i==0?entry with {Path="../outside.txt"}:entry).ToArray()};
                File.WriteAllText(manifest,JsonSerializer.Serialize(traversal,options));
                Check(label+" traversal metadata refused",await Reject(()=>Task.Run(()=>store.ReadPending()),"checkpoint_invalid"));
                File.WriteAllBytes(manifest,valid);
                File.WriteAllText(Path.Combine(Path.GetDirectoryName(manifest)!,"session.next"),"{incomplete snapshot");
                var stale=Path.Combine(Path.GetDirectoryName(manifest)!,"progress-"+Guid.NewGuid().ToString("N")+".jsonl");File.WriteAllText(stale,"invalid old generation\n");
                Check(label+" interrupted snapshot and stale generation ignored",store.ReadPending().Single().Receipts.Count==record.Receipts.Count);
            }
            // A valid final log record without a terminating newline is not committed.
            record=store.ReadPending().Single();var log=Path.Combine(folder,"records",record.Id.ToString("N"),"progress-"+record.Journal.ToString("N")+".jsonl");
            await File.AppendAllTextAsync(log,"{\"index\":4294967295");
            Check(label+" interrupted append retains previous receipts",store.ReadPending().Single().Receipts.Count==record.Receipts.Count);
            if(kind=="volumes")
            {
                var last=record.Sources.Last(source=>source.Path!=record.ArchivePath).Path;
                File.Move(last,last+".held");
                Check(label+" missing bound volume rejected",await Reject(async()=>{await using var broken=await store.ResumeAsync(record.Id,Binding(project,version));},"checkpoint_source_changed"));
                File.Move(last+".held",last);
                var extra=archive[..^3]+(record.Sources.Count+1).ToString("D3");File.WriteAllBytes(extra,[1]);
                Check(label+" additional volume changes bound source set",await Reject(async()=>{await using var broken=await store.ResumeAsync(record.Id,Binding(project,version));},"checkpoint_source_changed"));
                File.Delete(extra);
            }
            using var result=await FinishChild(folder,archive,version);
            var extracted=result.RootElement.GetProperty("completed").EnumerateArray().Select(value=>value.GetUInt32()).ToArray();
            Check(label+" new process extracts only remaining entries",extracted.Length==record.Entries.Count-record.Receipts.Count && !extracted.Contains(first.Index));
            Check(label+" retained file was moved without re-extracting",bytes.SequenceEqual(File.ReadAllBytes(Path.Combine(record.Destination,first.Path))) && stamp==File.GetLastWriteTimeUtc(Path.Combine(record.Destination,first.Path)));
            Check(label+" final exact tree and bytes",Directory.GetFiles(record.Destination,"*",SearchOption.AllDirectories).Length==3 && Directory.GetFiles(record.Destination,"*",SearchOption.AllDirectories).All(path=>File.ReadAllBytes(path).SequenceEqual(File.ReadAllBytes(source))) && Directory.Exists(Path.Combine(record.Destination,"empty")));
            Check(label+" origin retained after independent resume",Directory.GetFiles(record.Destination,"*",SearchOption.AllDirectories).All(path=>File.ReadAllText(path+":Zone.Identifier").Contains("ZoneId=3")));
            Check(label+" final cleanup removes owned session only",store.ReadPending().Count==0 && !Directory.GetDirectories(record.Destination,".nomozip-checkpoint-*").Any() && File.Exists(archive) && File.Exists(Path.Combine(folder,"vault.apv")));
        }
        {
            var label=version+" persistence drain";var folder=await NewCase(version+"-drain");var archive=Path.Combine(folder,"input.zip");
            using(var zip=ZipFile.Open(archive,ZipArchiveMode.Create)) foreach(var name in new[]{"a.txt","b.txt"}){using var writer=new StreamWriter(zip.CreateEntry(name).Open());writer.Write(name);}
            var listed=await client.ListAsync(archive);var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);using var cancel=new CancellationTokenSource();
            var callbackFinished=false;
            var work=client.ExtractCheckpointAsync(archive,Path.Combine(folder,"files"),listed.Items,null,new Dictionary<uint,string>(),async(_,_)=>{entered.SetResult();await release.Task;callbackFinished=true;},token:cancel.Token);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(8));cancel.Cancel();await Task.Delay(2400);
            Check(label+" cancellation cannot release a live persistence callback",!work.IsCompleted && !callbackFinished);
            release.SetResult();var result=await work.WaitAsync(TimeSpan.FromSeconds(8));
            Check(label+" cancellation returns only after callback drains",result.Status=="cancelled" && callbackFinished);
        }
        foreach(var fixture in new[]{"mixed-traditional.zip","mixed-aes.zip","test_read_format_rar5_encrypted.rar"})
        {
            var label=version+" "+fixture;var folder=await NewCase(version+"-"+fixture);var archive=Path.Combine(project,"tests/fixtures/generated",fixture);
            var vault=new DpapiPasswordVault(Path.Combine(folder,"vault.apv"));
            foreach(var password in new[]{"password","password2"}) await vault.SaveAsync(new(Guid.NewGuid(),password,password,"",true,DateTimeOffset.UtcNow));
            var before=File.ReadAllBytes(Path.Combine(folder,"vault.apv"));
            var store=new ExtractionCheckpointStore(Path.Combine(folder,"records"));var output=Path.Combine(folder,"output");
            using(var stop=new CancellationTokenSource())
            {
                var stopped=await new ExtractionService(client,vault,new PasswordCandidateProvider(vault)).ExtractAsync(archive,output,true,_=>Task.FromResult<PasswordPromptResponse?>(null),Replace,new ForwardProgress<TaskUpdate>(update=>{if(update.State=="checkpoint") stop.Cancel();}),stop.Token,checkpoint:new(store,Binding(project,version)));
                Check(label+" multiple-password progress retained",stopped.Engine.Status=="cancelled" && store.ReadPending().Single().Receipts.Count==1);
            }
            Check(label+" failed run leaves vault byte-exact",before.SequenceEqual(File.ReadAllBytes(Path.Combine(folder,"vault.apv"))));
            var record=store.ReadPending().Single();var observed=new ObservedEngine(client);
            var resumed=await new ExtractionService(observed,vault,new PasswordCandidateProvider(vault)).ExtractAsync(archive,output,true,_=>Task.FromResult<PasswordPromptResponse?>(null),Replace,checkpoint:new(store,Binding(project,version),record.Id));
            Check(label+" multiple passwords resume only remaining entries",resumed.Engine.IsSuccess && observed.Completed.Count==record.Entries.Count-1 && !observed.Completed.Contains(record.Receipts.Single().Index));
            // Compare each output against an independent official CLI with its matching secret.
            var exact=true;
            foreach(var entry in record.Entries.Where(entry=>!entry.Directory))
            {
                var actual=File.ReadAllBytes(Path.Combine(output,entry.Path));var match=false;
                foreach(var password in new[]{"password","password2"})
                {
                    var one=Path.Combine(folder,"reference-"+entry.Index+"-"+password);Directory.CreateDirectory(one);
                    var start=new ProcessStartInfo(Path.Combine(project,"engines",version,"7z.exe")){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
                    foreach(var arg in new[]{"x","-y","-p"+password,archive,"-o"+one,entry.Path}) start.ArgumentList.Add(arg);
                    using var official=Process.Start(start)!;var drain=official.StandardOutput.ReadToEndAsync();var error=official.StandardError.ReadToEndAsync();await official.WaitForExitAsync();await drain;await error;
                    if(official.ExitCode==0 && File.Exists(Path.Combine(one,entry.Path)) && actual.SequenceEqual(File.ReadAllBytes(Path.Combine(one,entry.Path)))) match=true;
                }
                exact &= match;
            }
            Check(label+" exact bytes agree with independent official CLI",exact && store.ReadPending().Count==0);
        }
        foreach(var scenario in new[]{"corrupt","missing-source","changed-source","commit","intent-gap","changed-delivery","save-failure","discard","cancel"})
        {
            var label=version+" "+scenario;var folder=await NewCase(version+"-"+scenario);var archive=Path.Combine(folder,"input.zip");
            using(var zip=ZipFile.Open(archive,ZipArchiveMode.Create)) foreach(var name in new[]{"a.txt","b.txt","c.txt"}) {using var writer=new StreamWriter(zip.CreateEntry(name).Open());writer.Write(name+" original");}
            if(scenario=="cancel")
            {
                using var child=Child("cancel",folder,archive,version);await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));
                Check(label+" cooperative stop retains progress",child.ExitCode==0 && new ExtractionCheckpointStore(Path.Combine(folder,"records")).ReadPending().Single().Receipts.Count==1);
            }
            else await StopChild(scenario is "commit" or "changed-delivery"?"commit-stop":"stop",folder,archive,version);
            var store=new ExtractionCheckpointStore(Path.Combine(folder,"records"));var record=store.ReadPending().Single();
            if(scenario is "missing-source" or "changed-source")
            {
                var original=File.ReadAllBytes(archive);
                if(scenario=="missing-source") File.Move(archive,archive+".held");else{var changed=original.ToArray();changed[^1]^=1;File.WriteAllBytes(archive,changed);}
                Check(label+" source change refused before output",await Reject(async()=>{await using var wrong=await store.ResumeAsync(record.Id,Binding(project,version));},"checkpoint_source_changed"));
                if(scenario=="missing-source") File.Move(archive+".held",archive);else File.WriteAllBytes(archive,original);
            }
            if(scenario=="save-failure")
            {
                string? blocker=null;
                var vault=new DpapiPasswordVault(Path.Combine(folder,"vault.apv"));
                var failed=await new ExtractionService(client,vault,new PasswordCandidateProvider(vault)).ExtractAsync(archive,record.Destination,false,_=>Task.FromResult<PasswordPromptResponse?>(null),Replace,new ForwardProgress<TaskUpdate>(update=>
                {
                    if(update.State!="extracting" || blocker is not null)return;
                    var current=store.ReadPending().Single();
                    blocker=Path.Combine(folder,"records",current.Id.ToString("N"),"progress-"+current.Journal.ToString("N")+".jsonl");Directory.CreateDirectory(blocker);
                }),checkpoint:new(store,Binding(project,version),record.Id));
                Check(label+" actual journal failure stops worker without false ACK",failed.Engine.Code=="checkpoint_save_failed" && store.ReadPending().Single().Receipts.Count==record.Receipts.Count);
                Directory.Delete(blocker!);
                using var retried=await FinishChild(folder,archive,version);
                Check(label+" failed checkpoint can resume exact remaining range",retried.RootElement.GetProperty("completed").GetArrayLength()==2 && store.ReadPending().Count==0);
            }
            else if(scenario=="changed-delivery")
            {
                var delivered=record.Receipts.Single(receipt=>receipt.State=="delivered");var entry=record.Entries.Single(entry=>entry.Index==delivered.Index);
                await ReplaceAfterExitAsync(Path.Combine(record.Destination,entry.Path),"user modified");
                var conflicts=0;
                var engine=new ObservedEngine(new ArchiveEngineClient(Path.Combine(folder,"ArchiveEngineWorker.exe"),Path.Combine(project,"engines",version,"7z.dll")));
                var vault=new DpapiPasswordVault(Path.Combine(folder,"vault.apv"));
                var repaired=await new ExtractionService(engine,vault,new PasswordCandidateProvider(vault)).ExtractAsync(archive,record.Destination,false,_=>Task.FromResult<PasswordPromptResponse?>(null),_=>{conflicts++;return Task.FromResult(ExistingFilePolicy.Skip);},checkpoint:new(store,Binding(project,version),record.Id));
                Check(label+" modified delivered file rechecked and conflict respected",repaired.Engine.IsSuccess && engine.Completed.SequenceEqual(new[]{entry.Index}) && conflicts==1 && File.ReadAllText(Path.Combine(record.Destination,entry.Path))=="user modified");
            }
            else if(scenario=="corrupt")
            {
                var receipt=record.Receipts.Single();var entry=record.Entries.Single(entry=>entry.Index==receipt.Index);
                File.WriteAllText(Path.Combine(Stage(record),entry.Path),"damaged");
                var unfinished=record.Entries.First(other=>other.Index!=entry.Index);
                File.WriteAllText(Path.Combine(Stage(record),unfinished.Path),"uncommitted partial bytes");
                using var result=await FinishChild(folder,archive,version);
                Check(label+" corrupt checkpoint re-extracted",result.RootElement.GetProperty("completed").GetArrayLength()==3 && File.ReadAllText(Path.Combine(record.Destination,entry.Path))==entry.Path+" original");
            }
            else if(scenario=="intent-gap")
            {
                await using(var session=await store.ResumeAsync(record.Id,Binding(project,version)))
                {
                    var entry=session.Record.Entries.Single(entry=>entry.Index==session.Record.Receipts.Single().Index);
                    await session.SetIntentAsync(session.Source(entry),ExistingFilePolicy.Replace,CancellationToken.None);
                    File.Move(session.Source(entry),session.Target(entry));
                }
                using var result=await FinishChild(folder,archive,version);
                Check(label+" move-before-journal interruption recovered",result.RootElement.GetProperty("completed").GetArrayLength()==2 && File.ReadAllText(Path.Combine(record.Destination,"a.txt"))=="a.txt original");
            }
            else if(scenario=="discard")
            {
                var unrelated=Path.Combine(record.Destination,"unrelated.txt");File.WriteAllText(unrelated,"keep");
                var entry=record.Entries.Single(entry=>entry.Index==record.Receipts.Single().Index);
                using(var held=new FileStream(Path.Combine(Stage(record),entry.Path),FileMode.Open,FileAccess.Read,FileShare.Read))
                    Check(label+" occupied plaintext prevents silent discard",ThrowsIo(()=>store.DiscardAsync(record.Id).GetAwaiter().GetResult()) && store.ReadPending().Count==1);
                await store.DiscardAsync(record.Id);
                Check(label+" scoped discard keeps unrelated files",store.ReadPending().Count==0 && File.ReadAllText(unrelated)=="keep" && File.Exists(archive));
            }
            else
            {
                var delivered=record.Receipts.Where(receipt=>receipt.State=="delivered").ToArray();
                using var result=await FinishChild(folder,archive,version);
                Check(label+" independent recovery succeeds",result.RootElement.GetProperty("status").GetString()=="success" && store.ReadPending().Count==0);
                if(scenario=="commit") Check(label+" completed delivery not re-extracted",delivered.Length==1 && result.RootElement.GetProperty("completed").GetArrayLength()==0);
            }
        }
    }
}
catch(Exception error){Console.WriteLine(error);Check("exception "+error,false);}
finally
{
    foreach(var native in Process.GetProcessesByName("ArchiveEngineWorker"))
    {try{if(native.MainModule!.FileName.StartsWith(data+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)){native.Kill();await native.WaitForExitAsync();}}catch(InvalidOperationException){}finally{native.Dispose();}}
    if(Directory.Exists(data)) Directory.Delete(data,true);
}
var hashes=versions.ToDictionary(version=>version,version=>Hash(Path.Combine(project,"engines",version,"7z.dll")));
await File.WriteAllTextAsync(Path.Combine(project,"artifacts/checkpoint-validation.json"),JsonSerializer.Serialize(new{passed=checks.Values.All(value=>value),total=checks.Count,checks,engineHashes=hashes},options));
return checks.Values.All(value=>value)?0:1;

bool ThrowsIo(Action action){try{action();return false;}catch(IOException){return true;}}
async Task ReplaceAfterExitAsync(string path,string text)
{
    var deadline=DateTime.UtcNow.AddSeconds(5);
    while(true)
    {
        try
        {
            await using var stream=new FileStream(path,FileMode.Open,FileAccess.Write,FileShare.None);
            stream.SetLength(0);await using var writer=new StreamWriter(stream,new UTF8Encoding(false));await writer.WriteAsync(text);return;
        }
        catch(IOException error) when((error.HResult & 0xffff) is 32 or 33 && DateTime.UtcNow<deadline)
        {await Task.Delay(20);}
    }
}


sealed class ObservedEngine(ArchiveEngineClient client):IArchiveEngine,IArchiveSelectionEngine,IArchiveMultiPasswordEngine,IArchiveCheckpointEngine
{
    public List<uint> Completed {get;}=[];
    public Task<EngineResult> GetCapabilitiesAsync(CancellationToken token=default)=>client.GetCapabilitiesAsync(token);
    public Task<EngineResult> ListAsync(string archive,string? password=null,CancellationToken cancellationToken=default)=>client.ListAsync(archive,password,cancellationToken);
    public Task<EngineResult> TestAsync(string archive,string? password=null,IProgress<EngineProgress>? progress=null,CancellationToken cancellationToken=default)=>client.TestAsync(archive,password,progress,cancellationToken);
    public Task<EngineResult> ExtractAsync(string archive,string output,string? password=null,IProgress<EngineProgress>? progress=null,CancellationToken cancellationToken=default)=>client.ExtractAsync(archive,output,password,progress,cancellationToken);
    public Task<EngineResult> TestSelectionAsync(string archive,IReadOnlyList<ArchiveEntry> entries,string? password,IReadOnlyDictionary<uint,string> passwords,IProgress<EngineProgress>? progress=null,CancellationToken token=default)=>client.TestSelectionAsync(archive,entries,password,passwords,progress,token);
    public Task<EngineResult> ExtractSelectionAsync(string archive,string output,IReadOnlyList<ArchiveEntry> entries,string? password,IReadOnlyDictionary<uint,string> passwords,IProgress<EngineProgress>? progress=null,CancellationToken token=default)=>client.ExtractSelectionAsync(archive,output,entries,password,passwords,progress,token);
    public Task<EngineResult> TestWithPasswordsAsync(string archive,string? password,IReadOnlyDictionary<uint,string> passwords,IProgress<EngineProgress>? progress=null,CancellationToken cancellationToken=default)=>client.TestWithPasswordsAsync(archive,password,passwords,progress,cancellationToken);
    public Task<EngineResult> ExtractWithPasswordsAsync(string archive,string output,string? password,IReadOnlyDictionary<uint,string> passwords,IProgress<EngineProgress>? progress=null,CancellationToken cancellationToken=default)=>client.ExtractWithPasswordsAsync(archive,output,password,passwords,progress,cancellationToken);
    public Task<EngineResult> ExtractCheckpointAsync(string archive,string output,IReadOnlyList<ArchiveEntry> entries,string? password,IReadOnlyDictionary<uint,string> passwords,Func<uint,CancellationToken,Task> saved,IProgress<EngineProgress>? progress=null,CancellationToken token=default)
        =>client.ExtractCheckpointAsync(archive,output,entries,password,passwords,async(index,ct)=>{Completed.Add(index);await saved(index,ct);},progress,token);
}

sealed class NormalCommitEngine(Action paused):IArchiveEngine
{
    public Task<EngineResult> GetCapabilitiesAsync(CancellationToken token=default)=>Task.FromResult(new EngineResult{Status="success"});
    public Task<EngineResult> ListAsync(string archive,string? password=null,CancellationToken cancellationToken=default)=>Task.FromResult(new EngineResult{Status="success"});
    public Task<EngineResult> TestAsync(string archive,string? password=null,IProgress<EngineProgress>? progress=null,CancellationToken cancellationToken=default)=>Task.FromResult(new EngineResult{Status="success"});
    public async Task<EngineResult> ExtractAsync(string archive,string output,string? password=null,IProgress<EngineProgress>? progress=null,CancellationToken cancellationToken=default)
    {Directory.CreateDirectory(output);await File.WriteAllTextAsync(Path.Combine(output,"file.txt"),"new bytes");paused();return new EngineResult{Status="success"};}
}
