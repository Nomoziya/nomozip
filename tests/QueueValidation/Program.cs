using System.Collections.Concurrent;
using System.Text.Json;
using ArchivePassword.Core;

var root=Path.GetFullPath(args[0]);var data=Path.Combine(root,"artifacts/queue-core",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(data);
var checks=new Dictionary<string,bool>();void Check(string name,bool value)=>checks.Add(name,value);
TaskCompletionSource Gate()=>new(TaskCreationOptions.RunContinuationsAsynchronously);
ExtractionLease Lease(int index)=>new(Path.Combine(data,$"input-{index}.7z"),Path.Combine(data,$"output-{index}"));
foreach(var limit in new[] {1,2})
{
    var started=Enumerable.Range(0,3).Select(_=>Gate()).ToArray();var release=Enumerable.Range(0,3).Select(_=>Gate()).ToArray();var order=new ConcurrentQueue<int>();
    var work=ExtractionQueue.RunAsync(new[] {0,1,2},limit,Lease,async (index,token)=>{order.Enqueue(index);started[index].TrySetResult();await release[index].Task.WaitAsync(token);});
    await started[0].Task.WaitAsync(TimeSpan.FromSeconds(5));if(limit==2) await started[1].Task.WaitAsync(TimeSpan.FromSeconds(5));
    Check($"limit {limit} caps active work",order.Count==limit && !started[2].Task.IsCompleted);
    release[0].TrySetResult();await started[limit].Task.WaitAsync(TimeSpan.FromSeconds(5));
    Check($"limit {limit} frees one slot in display order",order.SequenceEqual(limit==1?new[] {0,1}:new[] {0,1,2}));
    release[1].TrySetResult();release[2].TrySetResult();await work.WaitAsync(TimeSpan.FromSeconds(5));Check($"limit {limit} drains all work",order.SequenceEqual(new[] {0,1,2}));
}
var first=Lease(0);
var conflicts=new Dictionary<string,ExtractionLease> {
    ["same output"]=first with {InputPath=Lease(1).InputPath},
    ["case insensitive output"]=new(Lease(1).InputPath,first.OutputRoot.ToUpperInvariant()),
    ["nested output"]=new(Lease(1).InputPath,Path.Combine(first.OutputRoot,"child")),
    ["parent output"]=new(Lease(1).InputPath,data),
    ["output contains another input"]=new(Path.Combine(first.OutputRoot,"source.zip"),Lease(1).OutputRoot),
    ["other output contains input"]=new(Lease(1).InputPath,Path.GetDirectoryName(first.InputPath)!),
    ["normalized output"]=new(Lease(1).InputPath,Path.Combine(first.OutputRoot,"child",".."))};
foreach(var pair in conflicts)
{
    Check(pair.Key+" detects mutual conflict",first.ConflictsWith(pair.Value) && pair.Value.ConflictsWith(first));
    var release=Gate();var second=Gate();var work=ExtractionQueue.RunAsync(new[] {first,pair.Value},2,value=>value,async (value,_)=>{if(value==first) await release.Task;else second.TrySetResult();});
    Check(pair.Key+" does not launch overlapping work",!second.Task.IsCompleted);release.TrySetResult();await work.WaitAsync(TimeSpan.FromSeconds(5));Check(pair.Key+" runs waiting task afterwards",second.Task.IsCompleted);
}
Check("same prefix sibling paths are independent",!first.ConflictsWith(new(Lease(1).InputPath,first.OutputRoot+"-other")));
using(var cancel=new CancellationTokenSource())
{
    var started=Gate();var draining=Gate();var release=Gate();var count=0;
    var work=ExtractionQueue.RunAsync(new[] {0,1,2},2,Lease,async (_,token)=>{if(Interlocked.Increment(ref count)==2) started.TrySetResult();try {await Task.Delay(Timeout.Infinite,token);}finally {draining.TrySetResult();await release.Task;}},cancel.Token);
    await started.Task.WaitAsync(TimeSpan.FromSeconds(5));cancel.Cancel();await draining.Task.WaitAsync(TimeSpan.FromSeconds(5));Check("cancel waits for active cleanup",!work.IsCompleted);release.TrySetResult();
    try {await work.WaitAsync(TimeSpan.FromSeconds(5));}catch(OperationCanceledException){}
    Check("cancel does not launch waiting task",count==2);Check("cancel returns after active cleanup",work.IsCompleted);
}
{
    var fail=Gate();var second=Gate();var sawCancel=Gate();var release=Gate();var count=0;
    var work=ExtractionQueue.RunAsync(new[] {0,1,2},2,Lease,async (index,token)=>{Interlocked.Increment(ref count);if(index==0){await fail.Task;throw new IOException("synthetic failure");}second.TrySetResult();try {await Task.Delay(Timeout.Infinite,token);}catch(OperationCanceledException){sawCancel.TrySetResult();await release.Task;throw;}});
    await second.Task.WaitAsync(TimeSpan.FromSeconds(5));fail.TrySetResult();await sawCancel.Task.WaitAsync(TimeSpan.FromSeconds(5));Check("unexpected failure cancels and drains other active task",!work.IsCompleted && count==2);release.TrySetResult();
    try {await work.WaitAsync(TimeSpan.FromSeconds(5));Check("original failure preserved",false);}catch(IOException error){Check("original failure preserved",error.Message=="synthetic failure");}
}
using(var cancel=new CancellationTokenSource()) {cancel.Cancel();var count=0;await ExtractionQueue.RunAsync(new[] {0,1},2,Lease,(_,_)=>{count++;return Task.CompletedTask;},cancel.Token);Check("pre-cancelled batch starts nothing",count==0);}
var empty=0;await ExtractionQueue.RunAsync(Array.Empty<int>(),2,Lease,(_,_)=>{empty++;return Task.CompletedTask;});Check("empty batch completes without work",empty==0);
foreach(var invalid in new[] {0,3}) {try {await ExtractionQueue.RunAsync(Array.Empty<int>(),invalid,Lease,(_,_)=>Task.CompletedTask);Check("reject concurrency "+invalid,false);}catch(ArgumentOutOfRangeException){Check("reject concurrency "+invalid,true);}}
var report=new {passed=checks.Values.All(value=>value),total=checks.Count,checks};await File.WriteAllTextAsync(Path.Combine(root,"artifacts/queue-core-validation.json"),JsonSerializer.Serialize(report,new JsonSerializerOptions {WriteIndented=true}));Console.WriteLine(JsonSerializer.Serialize(report));return report.passed?0:1;
