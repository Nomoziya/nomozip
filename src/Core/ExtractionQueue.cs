namespace ArchivePassword.Core;

public sealed record ExtractionLease(string InputPath,string OutputRoot)
{
    private static string Normalize(string path)=>Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    private static bool Contains(string root,string path)
    {root=Normalize(root);path=Normalize(path);return path.Equals(root,StringComparison.OrdinalIgnoreCase) || path.StartsWith(Path.EndsInDirectorySeparator(root)?root:root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase);}
    public bool ConflictsWith(ExtractionLease other)=>Contains(OutputRoot,other.OutputRoot) || Contains(other.OutputRoot,OutputRoot)
        || Contains(OutputRoot,other.InputPath) || Contains(other.OutputRoot,InputPath);
}

// Keeps launch order, bounds active work, and drains active tasks before returning on cancellation/error.
public static class ExtractionQueue
{
    public static async Task RunAsync<T>(IReadOnlyList<T> items,int concurrency,Func<T,ExtractionLease> reserve,
        Func<T,CancellationToken,Task> run,CancellationToken cancellationToken=default,ExtractionPause? pause=null)
    {
        if(concurrency is not (1 or 2)) throw new ArgumentOutOfRangeException(nameof(concurrency));
        using var cancellation=CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var active=new List<(ExtractionLease Lease,Task Work)>();var next=0;
        try
        {
            while(next<items.Count || active.Count>0)
            {
                if(pause?.IsRequested==true && !cancellation.IsCancellationRequested) await pause.WaitAsync(cancellation.Token);
                while(next<items.Count && active.Count<concurrency && !cancellation.IsCancellationRequested)
                {
                    var item=items[next];var lease=reserve(item);
                    if(active.Any(value=>value.Lease.ConflictsWith(lease))) break;
                    next++;active.Add((lease,run(item,cancellation.Token)));
                }
                if(active.Count==0) break;
                var finished=await Task.WhenAny(active.Select(value=>value.Work));
                active.RemoveAll(value=>value.Work==finished);await finished;
            }
        }
        catch
        {
            cancellation.Cancel();
            try {await Task.WhenAll(active.Select(value=>value.Work));} catch { /* Preserve the initiating failure after every active operation ends. */ }
            throw;
        }
    }
}
