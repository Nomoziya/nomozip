using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using ArchivePassword.Core;
using ArchivePassword.Infrastructure;

var root=Path.GetFullPath(args[0]);var data=Path.Combine(root,"artifacts/pause-data",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(data);
var worker=Path.GetFullPath(Path.Combine(root,"artifacts/worker/ArchiveEngineWorker.exe"));var archive=Path.Combine(root,"tests/fixtures/generated/large.7z");
var checks=new Dictionary<string,bool>();void Check(string name,bool value)=>checks.Add(name,value);
await File.WriteAllTextAsync(Path.Combine(root,"artifacts/pause-validation.json"),"{\"passed\":false,\"state\":\"running\"}");
TaskCompletionSource Gate()=>new(TaskCreationOptions.RunContinuationsAsynchronously);
string Hash(string file) {using var stream=File.OpenRead(file);return Convert.ToHexString(SHA256.HashData(stream));}
int[] WorkerIds() {var values=new List<int>();foreach(var process in Process.GetProcessesByName("ArchiveEngineWorker")){using(process) try {if(process.MainModule?.FileName.Equals(worker,StringComparison.OrdinalIgnoreCase)==true) values.Add(process.Id);}catch(Exception error) when(error is System.ComponentModel.Win32Exception or InvalidOperationException){}}return values.ToArray();}
async Task<bool> NoRemainingWorkersAsync()
{
    var deadline=DateTime.UtcNow.AddSeconds(2);
    while(WorkerIds().Length!=0 && DateTime.UtcNow<deadline) await Task.Delay(20);
    return WorkerIds().Length==0;
}
{
    var pause=new ExtractionPause();pause.Pause();var first=pause.WaitAsync();var second=pause.WaitAsync();pause.Pause();Check("pause holds all managed waiters",!first.IsCompleted && !second.IsCompleted);pause.Resume();await Task.WhenAll(first,second);Check("resume releases all managed waiters",first.IsCompletedSuccessfully && second.IsCompletedSuccessfully && !pause.IsRequested);
    pause.Resume();Check("resume without pause is harmless",pause.WaitAsync().IsCompletedSuccessfully);
    pause.Pause();using var cancel=new CancellationTokenSource();var pending=pause.WaitAsync(cancel.Token);cancel.Cancel();try {await pending;}catch(OperationCanceledException){}Check("cancelled waiter exits without resuming others",pending.IsCanceled && pause.IsRequested && !pause.WaitAsync().IsCompleted);pause.Resume();
}
{
    var pause=new ExtractionPause();pause.Pause();var count=0;var run=ExtractionQueue.RunAsync(new[] {0,1},2,index=>new(Path.Combine(data,$"input{index}"),Path.Combine(data,$"out{index}")),(_,_)=>{count++;return Task.CompletedTask;},pause:pause);
    Check("paused queue starts no waiting tasks",count==0 && !run.IsCompleted);pause.Resume();await run;Check("resumed queue starts all pending tasks",count==2);
    pause.Pause();using var cancel=new CancellationTokenSource();count=0;run=ExtractionQueue.RunAsync(new[] {0,1},2,index=>new(Path.Combine(data,$"input{index}"),Path.Combine(data,$"out{index}")),(_,_)=>{count++;return Task.CompletedTask;},cancel.Token,pause);cancel.Cancel();try {await run;}catch(OperationCanceledException){}Check("paused waiting queue cancels without launching work",count==0 && run.IsCanceled);pause.Resume();
}
var versions=args.Length>1?args[1].Split(',').Distinct().ToArray():new[] {"26.03","26.04"};
foreach(var version in versions)
{
    var dll=Path.Combine(root,"engines",version,"7z.dll");var pause=new ExtractionPause();pause.Pause();var ack=Gate();var normal=0;
    var client=new ArchiveEngineClient(worker,dll,pause:pause);var work=client.TestAsync(archive,progress:new ForwardProgress<EngineProgress>(value=>{if(value.PausedState==true) ack.TrySetResult();else if(value.PausedState is null) Interlocked.Increment(ref normal);}));
    await ack.Task.WaitAsync(TimeSpan.FromSeconds(15));var ids=WorkerIds();Check(version+" actual engine acknowledges initial pause",ids.Length==1 && !work.IsCompleted);var before=normal;await Task.Delay(180);Check(version+" acknowledged pause keeps same worker and no progress",WorkerIds().SequenceEqual(ids) && normal==before && !work.IsCompleted);
    for(var index=0;index<12;index++) {pause.Resume();pause.Pause();}pause.Resume();var result=await work.WaitAsync(TimeSpan.FromSeconds(20));Check(version+" rapid toggles resume actual validation correctly",result.IsSuccess && result.SuccessfulItems==1);Check(version+" resumed test releases native process",WorkerIds().Length==0);
    var longer=Path.Combine(data,"longer-"+version+".7z");var entries=Enumerable.Range(0,4).Select(index=>new CompressionEntry(Path.Combine(root,"tests/fixtures/generated/large.bin"),index==0?"large.bin":"extra-"+index+".bin",false)).ToArray();
    var created=await new ArchiveEngineClient(worker,dll).CreateAsync(new(entries,"7z",1,false),longer,null);if(!created.IsSuccess) throw new IOException("long-running synthetic archive creation failed");
    var output=Path.Combine(data,version,"resume");pause=new();ack=Gate();var requested=false;var resumed=0;var progressEvents=0;ulong maxCompleted=0;
    client=new(worker,dll,pause:pause);work=client.ExtractAsync(longer,output,progress:new ForwardProgress<EngineProgress>(value=>{if(value.PausedState==true) ack.TrySetResult();else if(value.PausedState==false) Interlocked.Increment(ref resumed);else {progressEvents++;maxCompleted=Math.Max(maxCompleted,value.Completed);if(value.Completed>0 && !requested){requested=true;pause.Pause();}}}));
    try {await ack.Task.WaitAsync(TimeSpan.FromSeconds(15));}
    catch(TimeoutException)
    {
        var diagnostic=new{passed=false,phase="running pause acknowledgement",version,architecture=EngineArchitecture.Current,requested,progressEvents,maxCompleted,workStatus=work.Status.ToString(),workers=WorkerIds(),outputBytes=Directory.Exists(output)?Directory.GetFiles(output).Sum(path=>new FileInfo(path).Length):0,engineResult=work.IsCompletedSuccessfully?work.Result:null};
        await File.WriteAllTextAsync(Path.Combine(root,"artifacts/pause-validation.json"),JsonSerializer.Serialize(diagnostic));
        Console.WriteLine(JsonSerializer.Serialize(diagnostic));pause.Resume();throw;
    }
    ids=WorkerIds();var file=Path.Combine(output,"large.bin");await Task.Delay(80);var length=File.Exists(file)?new FileInfo(file).Length:0;await Task.Delay(180);
    Check(version+" running extraction actually pauses",requested && ids.Length==1 && !work.IsCompleted);
    Check(version+" paused output is stable without replacing worker",(File.Exists(file)?new FileInfo(file).Length:0)==length && WorkerIds().SequenceEqual(ids));pause.Resume();result=await work.WaitAsync(TimeSpan.FromSeconds(20));
    Check(version+" resume continues current extraction with correct bytes",result.IsSuccess && resumed>0 && Hash(file)==Hash(Path.Combine(root,"tests/fixtures/generated/large.bin")));Check(version+" resumed extraction releases worker",WorkerIds().Length==0);
    pause=new();pause.Pause();ack=Gate();using var cancellation=new CancellationTokenSource();client=new(worker,dll,pause:pause);work=client.TestAsync(archive,progress:new ForwardProgress<EngineProgress>(value=>{if(value.PausedState==true) ack.TrySetResult();}),cancellationToken:cancellation.Token);
    await ack.Task.WaitAsync(TimeSpan.FromSeconds(15));cancellation.Cancel();result=await work.WaitAsync(TimeSpan.FromSeconds(5));
    var cancellationReleased=await NoRemainingWorkersAsync();
    if(result.Status!="cancelled" || result.Code!="cancelled" || !cancellationReleased) Console.WriteLine(JsonSerializer.Serialize(new{phase="cancel paused engine",version,result.Status,result.Code,workers=WorkerIds()}));
    Check(version+" cancel wakes paused native engine",result.Status=="cancelled" && result.Code=="cancelled" && cancellationReleased);
    var legacy=Path.Combine(root,"artifacts/concurrency-candidate/ArchiveEngineWorker.exe");if(File.Exists(legacy)) {pause=new();pause.Pause();result=await new ArchiveEngineClient(legacy,dll,pause:pause).TestAsync(archive).WaitAsync(TimeSpan.FromSeconds(5));Check(version+" previous worker rejects pausable operation",!result.IsSuccess);}
}
foreach(var cancelAtCommit in new[] {false,true})
{
    var pause=new ExtractionPause();var stopped=Gate();var output=Path.Combine(data,"commit-"+cancelAtCommit);Directory.CreateDirectory(output);var existing=Path.Combine(output,"file.txt");await File.WriteAllTextAsync(existing,"old bytes");using var cancel=new CancellationTokenSource();
    var engine=new CommitEngine(()=>{pause.Pause();stopped.TrySetResult();});var service=new ExtractionService(engine,new EmptyVault(),new EmptyCandidates(),pause:pause);
    var work=service.ExtractAsync(Path.Combine(data,"synthetic.zip"),output,false,_=>Task.FromResult<PasswordPromptResponse?>(null),_=>Task.FromResult(ExistingFilePolicy.Replace),cancellationToken:cancel.Token);
    await stopped.Task;Check("commit waits before overwriting "+cancelAtCommit,File.ReadAllText(existing)=="old bytes" && !work.IsCompleted);
    if(cancelAtCommit) cancel.Cancel();else pause.Resume();try {var result=await work;Check("resumed commit succeeds",result.Engine.IsSuccess && File.ReadAllText(existing)=="new bytes");}catch(OperationCanceledException){Check("cancelled commit preserves existing bytes",File.ReadAllText(existing)=="old bytes");}
    Check("private staging cleaned "+cancelAtCommit,!Directory.EnumerateDirectories(output,".archive-password-*").Any());
}
var report=new {passed=checks.Values.All(value=>value),total=checks.Count,checks,engineHashes=versions.ToDictionary(version=>version,version=>Hash(Path.Combine(root,"engines",version,"7z.dll")))};await File.WriteAllTextAsync(Path.Combine(root,"artifacts/pause-validation.json"),JsonSerializer.Serialize(report,new JsonSerializerOptions {WriteIndented=true}));Console.WriteLine(JsonSerializer.Serialize(report));return report.passed?0:1;

sealed class CommitEngine(Action pause):IArchiveEngine
{
    public Task<EngineResult> GetCapabilitiesAsync(CancellationToken token=default)=>Task.FromResult(new EngineResult {Status="success"});
    public Task<EngineResult> ListAsync(string archive,string? password=null,CancellationToken token=default)=>Task.FromResult(new EngineResult {Status="success"});
    public Task<EngineResult> TestAsync(string archive,string? password=null,IProgress<EngineProgress>? progress=null,CancellationToken cancellationToken=default)=>Task.FromResult(new EngineResult {Status="success"});
    public async Task<EngineResult> ExtractAsync(string archive,string output,string? password=null,IProgress<EngineProgress>? progress=null,CancellationToken cancellationToken=default) {Directory.CreateDirectory(output);await File.WriteAllTextAsync(Path.Combine(output,"file.txt"),"new bytes");pause();return new EngineResult {Status="success"};}
}
sealed class EmptyCandidates:IPasswordCandidateProvider {public Task<IReadOnlyList<PasswordCandidate>> GetAsync(string archive,CancellationToken token=default)=>Task.FromResult<IReadOnlyList<PasswordCandidate>>([]);}
sealed class EmptyVault:IPasswordVault
{
    public Task<PasswordImportPlan> ImportAsync(IReadOnlyList<SavedPassword> values,PasswordImportMode mode,CancellationToken token=default)=>throw new NotSupportedException();
    public Task<IReadOnlyList<SavedPassword>> ReadAsync(CancellationToken token=default)=>Task.FromResult<IReadOnlyList<SavedPassword>>([]);
    public Task<Guid?> GetAssociatedPasswordAsync(string archive,CancellationToken token=default)=>Task.FromResult<Guid?>(null);
    public Task SaveAsync(SavedPassword value,CancellationToken token=default)=>Task.CompletedTask;
    public Task RecordSuccessAsync(Guid id,string archive,CancellationToken token=default)=>Task.CompletedTask;
    public Task DeleteAsync(Guid id,CancellationToken token=default)=>Task.CompletedTask;
    public Task RestoreBackupAsync(CancellationToken token=default)=>Task.CompletedTask;
}
